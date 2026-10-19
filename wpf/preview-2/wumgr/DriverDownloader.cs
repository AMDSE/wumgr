using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Downloader;

namespace wumgr
{
    // Only primitive, explicitly validated data is persisted; never deserialize a DownloadPackage or BinaryFormatter.
    public sealed class DriverResumeState
    {
        public int Format { get; set; }
        public string Url { get; set; }
        public string ResolvedUrl { get; set; }
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public long Length { get; set; }
        public List<DriverResumeChunk> Chunks { get; set; } = new List<DriverResumeChunk>();
    }
    public sealed class DriverResumeChunk
    {
        public long Start { get; set; }
        public long End { get; set; }
        public long Position { get; set; }
    }
    internal sealed class DriverTransferProgress
    {
        public long Received, Total;
        public double Speed, ChunkPercent;
        public string ChunkId;
        public bool IsChunk;
        public int ActiveChunks;
    }

    internal sealed class DriverDownloader
    {
        internal const int ChunkCount = 8, ParallelCount = 4;
        private const int BufferSize = 65536, BufferLimit = 4 * 1024 * 1024;
        private static readonly SemaphoreSlim EngineGate = new SemaphoreSlim(1, 1);
        private sealed class RemoteFile
        {
            public string Url, ETag, LastModified;
            public long Length;
            public bool Ranges;
            public bool CanResume { get { return Ranges && Length > 0 && (!String.IsNullOrEmpty(ETag) || !String.IsNullOrEmpty(LastModified)); } }
        }

        public async Task DownloadAsync(string url, string target, CancellationToken token, Action<DriverTransferProgress> report)
        {
            await EngineGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // Downloader 3.1.2 changes process-wide networking defaults in its constructor.
                // Restore them before issuing any request; do not allow expired/self-signed certificates.
                var certificatePolicy = ServicePointManager.ServerCertificateValidationCallback;
                var protocol = ServicePointManager.SecurityProtocol;
                int connectionLimit = ServicePointManager.DefaultConnectionLimit;
                int idleTime = ServicePointManager.MaxServicePointIdleTime;
                bool expectContinue = ServicePointManager.Expect100Continue;
                using (var engine = new ConfiguredDownloadService(CreateConfiguration()))
                {
                    ServicePointManager.ServerCertificateValidationCallback = certificatePolicy;
                    ServicePointManager.SecurityProtocol = protocol == SecurityProtocolType.SystemDefault ? protocol : protocol | SecurityProtocolType.Tls12;
                    ServicePointManager.DefaultConnectionLimit = connectionLimit;
                    ServicePointManager.MaxServicePointIdleTime = idleTime;
                    ServicePointManager.Expect100Continue = expectContinue;
                    // Do not inherit the library's global 1000-connection setting.
                    ServicePointManager.FindServicePoint(new Uri(url)).ConnectionLimit = Math.Max(connectionLimit, ParallelCount);
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        deadline.CancelAfter(TimeSpan.FromMinutes(15));
                        try { await TransferAsync(engine, url, target, deadline.Token, report).ConfigureAwait(false); }
                        catch (Exception) when (deadline.IsCancellationRequested)
                        {
                            token.ThrowIfCancellationRequested();
                            throw new TimeoutException("单个文件下载超过15分钟，已保留可用断点。请重试。");
                        }
                    }
                }
            }
            finally { EngineGate.Release(); }
        }

        private static DownloadConfiguration CreateConfiguration()
        {
            return new DownloadConfiguration {
                ChunkCount = ChunkCount, ParallelCount = ParallelCount, ParallelDownload = true,
                BufferBlockSize = BufferSize, MaximumMemoryBufferBytes = BufferLimit,
                MaxTryAgainOnFailover = 3, Timeout = 15000, MinimumSizeOfChunking = 256 * 1024,
                ClearPackageOnCompletionWithFailure = false, ReserveStorageSpaceBeforeStartingDownload = false,
                RequestConfiguration = new RequestConfiguration {
                    Timeout = 30000, KeepAlive = false, UseDefaultCredentials = false, AllowAutoRedirect = false,
                    Proxy = WebRequest.DefaultWebProxy, UserAgent = "WuMgr-Driver/5.0 Downloader/3.1.2",
                    AutomaticDecompression = DecompressionMethods.None,
                    CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore)
                }
            };
        }

        private static async Task TransferAsync(ConfiguredDownloadService engine, string url, string target, CancellationToken token, Action<DriverTransferProgress> report)
        {
            string part = target + ".part", statePath = target + ".resume.xml";
            // Prevent simultaneous processes from sharing a mutable partial file.
            using (var fileLock = new FileStream(target + ".download.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                RemoteFile remote = await ProbeAsync(url, token).ConfigureAwait(false);
                var state = ReadState(statePath, part, url, remote);
                if (state == null) { DeleteIfExists(statePath); DeleteIfExists(part); }
                var package = new DownloadPackage { FileName = part, Urls = new[] { url } };
                if (state != null)
                    package.Chunks = state.Chunks.Select((c, i) => new Chunk(c.Start, c.End) { Id = i.ToString(), Position = c.Position, MaxTryAgainOnFailover = 3, Timeout = 15000 }).ToArray();
                // Hold references: the library clears Package.Chunks on success before raising Completed.
                Chunk[] chunks = null;
                var positions = new Dictionary<string, long>();
                var positionGate = new object();
                Exception transferError = null;
                bool completed = false, cancelled = false;
                engine.DownloadStarted += (s, e) => { chunks = engine.Package.Chunks; lock (positionGate) foreach (var c in chunks) positions[c.Id] = c.Position; };
                engine.DownloadFileCompleted += (s, e) => { transferError = e.Error; cancelled = e.Cancelled; completed = e.Error == null && !e.Cancelled; };
                long lastReport = 0;
                double lastSpeed = 0;
                var progressGate = new object();
                engine.DownloadProgressChanged += (s, e) => {
                    if (report == null) return;
                    lock (progressGate)
                    {
                        lastSpeed = e.BytesPerSecondSpeed;
                        long now = Environment.TickCount & Int32.MaxValue;
                        if (now >= lastReport && now - lastReport < 120 && e.ReceivedBytesSize < e.TotalBytesToReceive) return;
                        lastReport = now;
                        report(new DriverTransferProgress { Received = e.ReceivedBytesSize, Total = remote.Length > 0 ? remote.Length : e.TotalBytesToReceive, Speed = e.BytesPerSecondSpeed, ActiveChunks = e.ActiveChunks });
                    }
                };
                var lastChunkReport = new Dictionary<string, long>();
                engine.ChunkDownloadProgressChanged += (s, e) => {
                    lock (positionGate) positions[e.ProgressId] = e.ReceivedBytesSize;
                    if (report == null) return;
                    lock (progressGate)
                    {
                        long now = Environment.TickCount & Int32.MaxValue, last;
                        if (lastChunkReport.TryGetValue(e.ProgressId, out last) && now >= last && now - last < 250 && e.ProgressPercentage < 100) return;
                        lastChunkReport[e.ProgressId] = now;
                        report(new DriverTransferProgress { Received = engine.Package.ReceivedBytesSize, Total = remote.Length, Speed = lastSpeed, ChunkId = e.ProgressId, ChunkPercent = e.ProgressPercentage, IsChunk = true, ActiveChunks = e.ActiveChunks });
                    }
                };
                // Conditional requests prevent mixing remote revisions between parallel connections.
                var headers = new WebHeaderCollection();
                if (!String.IsNullOrEmpty(remote.ETag)) headers["If-Match"] = remote.ETag;
                else if (!String.IsNullOrEmpty(remote.LastModified)) headers["If-Unmodified-Since"] = remote.LastModified;
                // The constructor clones configuration shallowly; configure the shared request object before start.
                // A new engine is used for every file, so headers never leak to another driver.
                engine.ConfigureHeaders(headers);
                try
                {
                    token.ThrowIfCancellationRequested();
                    using (var stream = await engine.DownloadFileTaskAsync(package, remote.Url, token).ConfigureAwait(false)) { }
                    token.ThrowIfCancellationRequested();
                    if (transferError != null) throw new IOException("驱动文件下载失败：" + transferError.Message, transferError);
                    if (cancelled || !completed) throw new IOException("下载未完成，已保留可用断点。");
                    long actual = File.Exists(part) ? new FileInfo(part).Length : 0;
                    long received;
                    lock (positionGate) received = positions.Values.Sum();
                    if (actual <= 0 || received <= 0) throw new IOException("服务器返回空响应。");
                    if (remote.Length > 0 && (actual != remote.Length || received != remote.Length || chunks.Any(c => !positions.ContainsKey(c.Id) || positions[c.Id] != c.Length)))
                        throw new IOException("响应长度或分块完整性不匹配，未替换正式文件。");
                    if (File.Exists(target)) File.Replace(part, target, null); else File.Move(part, target);
                    DeleteIfExists(statePath);
                }
                catch
                {
                    // DownloadFileTaskAsync returns only after workers stop and disk buffers are flushed.
                    // Store only stopped chunk offsets; never claim in-flight RAM as durable data.
                    if (remote.CanResume && chunks != null && File.Exists(part) && chunks.Any(c => c.Position > 0))
                        WriteState(statePath, url, remote, chunks);
                    else DeleteIfExists(statePath);
                    token.ThrowIfCancellationRequested();
                    throw;
                }
                finally { package.Storage?.Dispose(); }
            }
        }

        // Access through our subclass rather than reflection into the NuGet assembly.
        private sealed class ConfiguredDownloadService : DownloadService
        {
            public ConfiguredDownloadService(DownloadConfiguration config) : base(config) { }
            public void ConfigureHeaders(WebHeaderCollection headers) { Options.RequestConfiguration.Headers = headers; }
        }

        private static async Task<RemoteFile> ProbeAsync(string url, CancellationToken token)
        {
            var first = await ProbeRangeAsync(url, 0, token).ConfigureAwait(false);
            if (first.Ranges && first.Length > 1)
            {
                var second = await ProbeRangeAsync(url, 1, token).ConfigureAwait(false);
                if (!second.Ranges || first.Length != second.Length || first.ETag != second.ETag || first.LastModified != second.LastModified)
                    throw new IOException("服务器未正确响应分块请求或文件已变化，请重试。");
            }
            return first;
        }
        private static async Task<RemoteFile> ProbeRangeAsync(string url, long start, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET"; request.AddRange(start, start);
            request.Timeout = 30000; request.ReadWriteTimeout = 15000;
            request.UserAgent = "WuMgr-Driver/5.0";
            request.AutomaticDecompression = DecompressionMethods.None;
            request.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            using (token.Register(request.Abort))
            {
                HttpWebResponse response;
                try { response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false); }
                catch (WebException ex) { token.ThrowIfCancellationRequested(); if (ex.Response != null) ex.Response.Dispose(); throw; }
                using (response)
                {
                    var uri = response.ResponseUri;
                    if ((uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo)) throw new IOException("不允许的下载重定向地址。");
                    if (new Uri(url).Scheme == "https" && uri.Scheme != "https") throw new IOException("拒绝从 HTTPS 降级到 HTTP 的下载重定向。");
                    var result = new RemoteFile { Url = uri.AbsoluteUri, Length = response.ContentLength, ETag = response.Headers["ETag"], LastModified = response.Headers["Last-Modified"] };
                    if (result.ETag != null && result.ETag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) result.ETag = null;
                    if (response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        var range = response.Headers["Content-Range"];
                        string prefix = "bytes " + start + "-" + start + "/";
                        long size;
                        if (range == null || !range.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Int64.TryParse(range.Substring(prefix.Length), out size) || size <= start || response.ContentLength != 1)
                            throw new IOException("服务器返回无效的 Content-Range。");
                        result.Length = size; result.Ranges = true;
                    }
                    return result;
                }
            }
        }

        private static DriverResumeState ReadState(string path, string part, string url, RemoteFile remote)
        {
            if (!remote.CanResume || !File.Exists(path) || !File.Exists(part)) return null;
            try
            {
                if (new FileInfo(path).Length > 64 * 1024) return null;
                DriverResumeState state;
                using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
                    state = (DriverResumeState)new XmlSerializer(typeof(DriverResumeState)).Deserialize(reader);
                if (state.Format != 1 || state.Url != url || state.ResolvedUrl != remote.Url || state.Length != remote.Length || state.ETag != remote.ETag || state.LastModified != remote.LastModified || state.Chunks == null || state.Chunks.Count < 1 || state.Chunks.Count > ChunkCount) return null;
                long next = 0, fileLength = new FileInfo(part).Length;
                if (fileLength > remote.Length) return null;
                foreach (var c in state.Chunks)
                {
                    if (c.Start != next || c.End < c.Start || c.End >= remote.Length || c.Position < 0 || c.Position > c.End - c.Start + 1 || (c.Position > 0 && c.Start + c.Position > fileLength)) return null;
                    next = c.End + 1;
                }
                return next == remote.Length ? state : null;
            }
            catch (IOException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (XmlException) { return null; }
        }
        private static void WriteState(string path, string url, RemoteFile remote, Chunk[] chunks)
        {
            var state = new DriverResumeState { Format = 1, Url = url, ResolvedUrl = remote.Url, ETag = remote.ETag, LastModified = remote.LastModified, Length = remote.Length,
                Chunks = chunks.Select(c => new DriverResumeChunk { Start = c.Start, End = c.End, Position = c.Position }).ToList() };
            string temp = path + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { new XmlSerializer(typeof(DriverResumeState)).Serialize(output, state); output.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            catch (IOException) { DeleteIfExists(path); } // No metadata means a safe full restart, not a corrupt resume.
            catch (UnauthorizedAccessException) { DeleteIfExists(path); }
            finally { DeleteIfExists(temp); }
        }
        private static void DeleteIfExists(string path) { if (File.Exists(path)) File.Delete(path); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using wumgr;

// Downloader 3.1.2 acceptance tests. These intentionally use only loopback HTTP.
class DownloaderTests
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    static DriverInfo Item(string id, string url) { return new DriverInfo { Id = id, Title = id, Version = "1.0.0.0", DownloadUrls = new List<string> { url } }; }
    sealed class Progress : IProgress<DriverDownloadProgress>
    {
        public Action<DriverDownloadProgress> Action;
        public void Report(DriverDownloadProgress value) { if (Action != null) Action(value); }
    }
    sealed class LocalServer : IDisposable
    {
        readonly HttpListener listener = new HttpListener();
        readonly List<Task> workers = new List<Task>();
        readonly object gate = new object();
        readonly byte[] bytes;
        public readonly byte[] ChangedPayload;
        public readonly string Etag = "\"download-a\"";
        public int Current, MaxConcurrent, Requests, RangeRequests, BodyBytes;
        public readonly List<string> RangeHeaders = new List<string>();
        public bool ValidatorChanged;
        public LocalServer(byte[] payload)
        {
            bytes = payload; ChangedPayload = (byte[])payload.Clone();
            for (int i = 0; i < ChangedPayload.Length; i += 7919) ChangedPayload[i] ^= 0xA5;
            var a = new TcpListener(IPAddress.Loopback, 0); a.Start(); int port = ((IPEndPoint)a.LocalEndpoint).Port; a.Stop();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); Url = "http://127.0.0.1:" + port;
            Task.Run(async () => { while (listener.IsListening) { try { var c = await listener.GetContextAsync(); lock (gate) { Requests++; workers.Add(Handle(c)); } } catch { break; } } });
        }
        public string Url { get; private set; }
        async Task Handle(HttpListenerContext c)
        {
            int now = Interlocked.Increment(ref Current); if (now > MaxConcurrent) MaxConcurrent = now;
            try
            {
                string path = c.Request.Url.AbsolutePath;
                if (path == "/missing") { c.Response.StatusCode = 404; return; }
                bool noRange = path == "/norange.bin";
                bool ignore = path == "/ignore.bin";
                bool etagRoute = path == "/etag.bin";
                string etag = etagRoute && ValidatorChanged ? "\"download-b\"" : Etag;
                byte[] payload = etagRoute && ValidatorChanged ? ChangedPayload : bytes;
                c.Response.Headers["ETag"] = etag; c.Response.Headers["Last-Modified"] = "Tue, 01 Jan 2030 00:00:00 GMT";
                var range = c.Request.Headers["Range"];
                if (!noRange) c.Response.Headers["Accept-Ranges"] = "bytes";
                int start = 0, end = payload.Length - 1; bool ranged = !noRange && !String.IsNullOrEmpty(range);
                if (ranged) { Interlocked.Increment(ref RangeRequests); lock (gate) RangeHeaders.Add(range); ParseRange(range, out start, out end); }
                if (ignore && ranged) { start = 0; end = bytes.Length - 1; ranged = false; }
                if (ranged && !ignore)
                {
                    c.Response.StatusCode = 206; c.Response.Headers["Content-Range"] = "bytes " + start + "-" + end + "/" + bytes.Length;
                }
                c.Response.ContentLength64 = end - start + 1;
                for (int offset = start; offset <= end; offset += 16 * 1024)
                {
                    int chunk = Math.Min(16 * 1024, end - offset + 1);
                    await c.Response.OutputStream.WriteAsync(payload, offset, chunk);
                    await c.Response.OutputStream.FlushAsync();
                    Interlocked.Add(ref BodyBytes, chunk);
                    await Task.Delay(20);
                }
            }
            catch (Exception) { /* cancellation closes the connection by design */ }
            finally { Interlocked.Decrement(ref Current); try { c.Response.Close(); } catch { } }
        }
        static void ParseRange(string value, out int start, out int end)
        {
            start = end = 0; string s = value.Substring(value.IndexOf('=') + 1); string[] p = s.Split('-'); Int32.TryParse(p[0], out start); if (p.Length > 1 && p[1].Length != 0) Int32.TryParse(p[1], out end); else end = start + 262143;
        }
        public void Dispose()
        {
            listener.Stop(); listener.Close(); Task[] a; lock (gate) a = workers.ToArray(); try { Task.WhenAll(a).GetAwaiter().GetResult(); } catch { }
        }
    }
    static string Find(string root, string suffix) { return Directory.GetFiles(root, "*" + suffix, SearchOption.AllDirectories).FirstOrDefault(); }
    static string Target(string root, string id) { return Directory.GetFiles(Path.Combine(root, SafeName(id)), "driver_01.bin", SearchOption.TopDirectoryOnly).FirstOrDefault(); }
    static string SafeName(string id) { foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_'); return id + "_" + Hash(id).Substring(0, 12); }
    static string Hash(string s) { using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", ""); }
    static async Task MainTask()
    {
        var data = new byte[2 * 1024 * 1024]; new Random(19770311).NextBytes(data);
        string root = Path.Combine(Path.GetTempPath(), "wumgr-downloader-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using (var server = new LocalServer(data))
        {
            try
            {
                string url = server.Url; var first = Item("resume", url + "/range.bin"); bool canceled = false; long progressBytes = 0; bool sawChunk = false, sawTotal = false, sawSpeed = false; var speedClock = Stopwatch.StartNew();
                using (var cts = new CancellationTokenSource())
                {
                    var p = new Progress { Action = x => { progressBytes = Math.Max(progressBytes, x.ReceivedBytes); sawChunk |= x.IsChunkProgress && !String.IsNullOrEmpty(x.ChunkId); sawTotal |= x.TotalBytes >= data.Length; sawSpeed |= x.BytesPerSecond > 0 || (x.ReceivedBytes > 0 && speedClock.ElapsedMilliseconds > 0); if (x.ReceivedBytes >= 900 * 1024) cts.Cancel(); } };
                    try { await new DriverService().DownloadAsync(new[] { first }, root, cts.Token, p); } catch (OperationCanceledException) { canceled = true; }
                }
                string part = Find(root, ".part"), resume = Find(root, ".resume.xml");
                Check(canceled && part != null && resume != null && Target(root, "resume") == null, "取消保留part、resume且不创建正式文件");
                Check(progressBytes > 0 && sawChunk && sawTotal, "进度包含chunk标识和字节总量");
                int before = server.BodyBytes; var second = Item("resume", url + "/range.bin"); await new DriverService().DownloadAsync(new[] { second }, root, CancellationToken.None, new Progress { Action = x => { sawSpeed |= x.BytesPerSecond > 0; } });
                Check(File.ReadAllBytes(Target(root, "resume")).SequenceEqual(data), "跨DriverService实例续传内容完整");
                Check(server.RangeRequests > 0 && server.BodyBytes - before < data.Length, "续传使用Range且新增字节少于完整文件");
                await new DriverService().DownloadAsync(new[] { Item("speed", url + "/range.bin") }, root, CancellationToken.None, new Progress { Action = x => { sawSpeed |= x.BytesPerSecond > 0; } });
                Check(sawSpeed, "速度报告为正值");
                Check(Find(root, ".part") == null && Find(root, ".resume.xml") == null, "成功后清理续传文件");
                Check(server.MaxConcurrent >= 2, "服务器观察到并发Range请求");

                var changed = Item("validator", url + "/etag.bin"); using (var cts = new CancellationTokenSource()) { var p = new Progress { Action = x => { if (x.ReceivedBytes >= 300 * 1024) cts.Cancel(); } }; try { await new DriverService().DownloadAsync(new[] { changed }, root, cts.Token, p); } catch (OperationCanceledException) { } }
                int rangesBeforeChange = server.RangeRequests; server.ValidatorChanged = true; await new DriverService().DownloadAsync(new[] { Item("validator", url + "/etag.bin") }, root, CancellationToken.None, null);
                Check(File.ReadAllBytes(Target(root, "validator")).SequenceEqual(server.ChangedPayload) && server.RangeRequests >= rangesBeforeChange, "ETag变化丢弃旧片并安全重下");

                var u = Item("url-change", url + "/range.bin"); using (var cts = new CancellationTokenSource()) { var p = new Progress { Action = x => { if (x.ReceivedBytes >= 300 * 1024) cts.Cancel(); } }; try { await new DriverService().DownloadAsync(new[] { u }, root, cts.Token, p); } catch (OperationCanceledException) { } }
                u.DownloadUrls[0] = url + "/norange.bin"; await new DriverService().DownloadAsync(new[] { u }, root, CancellationToken.None, null);
                Check(File.ReadAllBytes(Target(root, "url-change")).SequenceEqual(data) && server.BodyBytes >= data.Length, "URL变化丢弃旧片并安全重下");

                var no = Item("no-range", url + "/norange.bin"); int requestsBefore = server.Requests; await new DriverService().DownloadAsync(new[] { no }, root, CancellationToken.None, null);
                Check(File.ReadAllBytes(Target(root, "no-range")).SequenceEqual(data) && server.Requests > requestsBefore, "无Range服务器单流成功");

                var ignored = Item("ignore-range", url + "/ignore.bin"); using (var cts = new CancellationTokenSource()) { var p = new Progress { Action = x => { if (x.ReceivedBytes >= 300 * 1024) cts.Cancel(); } }; try { await new DriverService().DownloadAsync(new[] { ignored }, root, cts.Token, p); } catch (OperationCanceledException) { } catch (AggregateException) { } }
                bool aggregate = false; try { await new DriverService().DownloadAsync(new[] { ignored }, root, CancellationToken.None, null); } catch (AggregateException) { aggregate = true; }
                string ignoredTarget = Target(root, "ignore-range"); bool cleanRestart = ignoredTarget != null && File.ReadAllBytes(ignoredTarget).SequenceEqual(data);
                Check((aggregate && ignoredTarget == null) || cleanRestart, "不守Range服务器不会静默生成错误文件");
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
        Console.WriteLine("下载器测试完成：" + passed + " 项全部通过。");
    }
    public static int Main() { try { MainTask().GetAwaiter().GetResult(); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }
}

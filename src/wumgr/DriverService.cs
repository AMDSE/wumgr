using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace wumgr
{
    public class DriverService
    {
        public IReadOnlyList<string> LastScanDiagnostics { get; private set; } = new List<string>();
        public bool Partial { get; private set; }
        private readonly DriverDownloader downloader = new DriverDownloader();
        private const string NotProvided = "未提供";
        public const string DriverSearchCriteria = "(Type='Driver' and IsInstalled=0 and IsHidden=0 and DeploymentAction='Installation') or (Type='Driver' and IsInstalled=0 and IsHidden=0 and DeploymentAction='OptionalInstallation')";

        public Task<List<DriverInfo>> ScanAsync(CancellationToken token, IProgress<string> progress)
        {
            return ScanAsync(false, token, progress);
        }

        public Task<List<DriverInfo>> ScanAsync(bool includeHistory, CancellationToken token, IProgress<string> progress)
        {
            var tcs = new TaskCompletionSource<List<DriverInfo>>();
            var thread = new Thread(() => { try { var v = ScanOnSta(includeHistory, token, progress); if (token.IsCancellationRequested) tcs.TrySetCanceled(); else tcs.TrySetResult(v); } catch (OperationCanceledException) { tcs.TrySetCanceled(); } catch (Exception ex) { tcs.TrySetException(new InvalidOperationException("驱动扫描失败：" + ex.Message, ex)); } });
            thread.IsBackground = true; thread.Name = "wumgr Driver WUA STA"; thread.SetApartmentState(ApartmentState.STA); thread.Start(); return tcs.Task;
        }

        public Task<List<DriverInfo>> ScanWithCatalogAsync(IEnumerable<LocalDeviceInfo> devices, bool includeHistory, CancellationToken token, IProgress<string> progress)
        {
            var coordinator = new DriverSearchCoordinator((history, ct, p) => ScanAsync(history, ct, p), () => new MicrosoftUpdateCatalogClient());
            return CopyDiagnosticsAsync(coordinator, devices, includeHistory, token, progress);
        }

        async Task<List<DriverInfo>> CopyDiagnosticsAsync(DriverSearchCoordinator coordinator, IEnumerable<LocalDeviceInfo> devices, bool history, CancellationToken token, IProgress<string> progress)
        {
            var result = await coordinator.ScanWithCatalogAsync(devices, history, token, progress).ConfigureAwait(false);
            LastScanDiagnostics = coordinator.LastDiagnostics; Partial = coordinator.Partial;
            if (Partial) progress?.Report("扫描诊断：" + String.Join("；", LastScanDiagnostics));
            return result;
        }

        public async Task DownloadAsync(IList<DriverInfo> selected, string folder, CancellationToken token, IProgress<DriverDownloadProgress> progress)
        {
            if (selected == null) throw new ArgumentNullException("selected");
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentException("导出目录不能为空。", "folder");
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(folder); var errors = new List<Exception>();
            for (int i = 0; i < selected.Count; i++) { try { token.ThrowIfCancellationRequested(); await DownloadOneAsync(selected[i], folder, i, selected.Count, token, progress).ConfigureAwait(false); } catch (OperationCanceledException) { for (int n = i; n < selected.Count; n++) { if (selected[n].Status != "已下载" && !selected[n].Status.StartsWith("失败：")) selected[n].Status = "已取消"; } throw; } catch (Exception ex) { selected[i].Status = "失败：" + ex.Message; errors.Add(new InvalidOperationException(selected[i].Title + "：" + ex.Message, ex)); progress?.Report(new DriverDownloadProgress { Percent = 0, Message = "下载失败：" + selected[i].Title + "；" + ex.Message }); } }
            if (errors.Count != 0) throw new AggregateException("一个或多个驱动下载失败。", errors);
        }

        private List<DriverInfo> ScanOnSta(bool includeHistory, CancellationToken token, IProgress<string> progress)
        {
            dynamic session = null, searcher = null, result = null, updates = null;
            try
            {
                token.ThrowIfCancellationRequested(); progress?.Report("正在创建 Windows Update Agent 扫描线程（专用 STA）…");
                token.ThrowIfCancellationRequested(); session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true));
                token.ThrowIfCancellationRequested(); session.ClientApplicationID = "wumgr-driver-scan"; searcher = session.CreateUpdateSearcher();
                try { searcher.IncludePotentiallySupersededUpdates = includeHistory; } catch (Exception ex) { if (includeHistory) throw new InvalidOperationException("当前更新服务未接受历史候选查询。请取消勾选“扫描时包含历史版本”后重试。", ex); }
                token.ThrowIfCancellationRequested();
                // WUA 搜索语言只允许在顶层使用 OR；两支均显式限制为未安装且未隐藏的驱动。
                result = searcher.Search(DriverSearchCriteria);
                int code = Convert.ToInt32(result.ResultCode); if (code != 2 && code != 3) throw new InvalidOperationException("Windows Update 搜索失败，结果代码：" + code);
                updates = result.Updates; var output = new List<DriverInfo>(); int count = Convert.ToInt32(updates.Count);
                for (int i = 0; i < count; i++) { token.ThrowIfCancellationRequested(); dynamic update = null; try { update = updates.Item(i); var info = ReadDriver(update); if (info != null) output.Add(info); } finally { ReleaseCom(update); } progress?.Report("已扫描 " + (i + 1) + "/" + count + " 个驱动更新。"); }
                output = DriverCatalog.NormalizeCandidates(output);
                progress?.Report("驱动扫描完成，共发现 " + output.Count + " 个未安装驱动。"); return output;
            }
            finally { ReleaseCom(updates); ReleaseCom(result); ReleaseCom(searcher); ReleaseCom(session); }
        }

        private DriverInfo ReadDriver(dynamic update)
        {
            try
            {
                if (Convert.ToInt32(Get(update, "Type")) != 2) return null;
                var id = Text(Get(update, "Identity.UpdateID")); if (id == NotProvided) id = Text(Get(update, "UpdateID")); if (id == NotProvided) return null;
                var info = new DriverInfo { Id = id, RevisionNumber = Get(update, "Identity.RevisionNumber") == null ? -1 : Convert.ToInt32(Get(update, "Identity.RevisionNumber")), Title = Text(Get(update, "Title")), Manufacturer = Text(Get(update, "DriverManufacturer")), Provider = Text(Get(update, "DriverProvider")), DriverClass = Text(Get(update, "DriverClass")), DeviceModel = Text(Get(update, "DriverModel")), HardwareId = HardwareText(Get(update, "DriverHardwareID")), Version = ExtractVersion(Text(Get(update, "Title"))), Superseded = Convert.ToBoolean(Get(update, "IsSuperseded") ?? false), Source = "Windows Update Agent" };
                var date = Get(update, "DriverVerDate"); DateTime d; if (date is DateTime) info.DriverDate = (DateTime)date; else if (DateTime.TryParse(Convert.ToString(date), out d)) info.DriverDate = d;
                info.DownloadUrls = GetDownloadUrls(update); decimal size; if (Decimal.TryParse(Convert.ToString(Get(update, "MaxDownloadSize")), out size) && size >= 0) info.Size = size;
                info.Status = info.DownloadUrls.Count == 0 ? "无可用下载地址" : "待下载"; return info;
            } catch { return null; }
        }

        private List<string> GetDownloadUrls(dynamic update)
        {
            var urls = new List<string>(); var seen = new HashSet<object>();
            CollectDownloadUrls(update, urls, seen); return urls;
        }
        private void CollectDownloadUrls(dynamic update, List<string> urls, HashSet<object> seen)
        {
            if (update == null || !seen.Add(update)) return; dynamic contents = null;
            try { contents = update.DownloadContents; int n = Convert.ToInt32(contents.Count); for (int i = 0; i < n; i++) { dynamic c = null; try { c = contents.Item(i); if (Convert.ToBoolean(Get(c, "IsDeltaCompressedContent"))) continue; var u = Text(Get(c, "DownloadUrl")); Uri uri; if (Uri.TryCreate(u, UriKind.Absolute, out uri) && IsHttpUrl(uri) && !urls.Contains(uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)) urls.Add(uri.AbsoluteUri); } finally { ReleaseCom(c); } } } catch { } finally { ReleaseCom(contents); }
            dynamic bundles = null; try { bundles = update.BundledUpdates; int n = Convert.ToInt32(bundles.Count); for (int i = 0; i < n; i++) { dynamic b = null; try { b = bundles.Item(i); CollectDownloadUrls(b, urls, seen); } finally { ReleaseCom(b); } } } catch { } finally { ReleaseCom(bundles); }
        }

        private async Task DownloadOneAsync(DriverInfo driver, string root, int index, int total, CancellationToken token, IProgress<DriverDownloadProgress> progress)
        {
            if (driver == null) throw new ArgumentNullException("driver");
            token.ThrowIfCancellationRequested();
            if (driver.Source == "Microsoft Update Catalog" && !driver.CanDownload) throw new InvalidOperationException("目录候选缺少已核验硬件、有效更新ID或包含不受信任URL。");
            var urls = (driver.DownloadUrls ?? new List<string>()).Where(IsHttpUrl).Where(u => driver.Source != "Microsoft Update Catalog" || MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (driver.Source == "Microsoft Update Catalog" && !driver.CatalogHardwareVerified) throw new InvalidOperationException("目录硬件匹配未验证，禁止下载。");
            if (urls.Count == 0 && (driver.Source == "Microsoft Update Catalog" || (driver.Source == "Windows Update Agent" && Regex.IsMatch(driver.Id ?? "", @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))))
            {
                if (driver.Source == "Microsoft Update Catalog" && !driver.CatalogHardwareVerified) throw new InvalidOperationException("目录硬件匹配未验证，禁止下载。");
                token.ThrowIfCancellationRequested();
                using (var catalog = new MicrosoftUpdateCatalogClient())
                {
                    var lazy = await catalog.GetDownloadUrlsAsync(driver.Id, token).ConfigureAwait(false) ?? new List<string>();
                    urls = lazy.Where(u => IsHttpUrl(u) && MicrosoftUpdateCatalogClient.IsTrustedDownloadUrl(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    driver.DownloadUrls = urls;
                    driver.DownloadUrlSource = "Microsoft Update Catalog (revision unverified)";
                    driver.Status = "下载地址来源：Microsoft Update Catalog（当前目录包，修订未验证；WUA 将独立验证）";
                }
            }
            if (urls.Count == 0) throw new InvalidOperationException(driver.Source == "Microsoft Update Catalog" ? "目录未提供受信任的下载 URL，可重试。" : "WUA 未提供可下载的 http/https URL。");
            var dir = Path.Combine(root, SafeDirectoryName(driver.Id)); Directory.CreateDirectory(dir); var written = new List<string>(); driver.LocalPath = String.Empty; driver.Status = "下载中";
            try
            {
                for (int i = 0; i < urls.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    int fileIndex = i, highest = OverallPercent(index, total, i, urls.Count, 0);
                    var gate = new object();
                    var target = Path.Combine(dir, "driver_" + (i + 1).ToString("D2") + ExtensionFromUrl(urls[i]));
                    progress?.Report(new DriverDownloadProgress { Percent = highest, Message = "正在分块下载驱动 " + driver.Title + "（" + (i + 1) + "/" + urls.Count + "）…" });
                    await downloader.DownloadAsync(urls[i], target, token, p => {
                        int local = p.Total > 0 ? (int)Math.Min(99, p.Received * 100.0 / p.Total) : 0;
                        lock (gate)
                        {
                            highest = Math.Max(highest, OverallPercent(index, total, fileIndex, urls.Count, local));
                            progress?.Report(new DriverDownloadProgress {
                                Percent = highest, ReceivedBytes = p.Received, TotalBytes = p.Total,
                                BytesPerSecond = p.Speed, ChunkId = p.ChunkId ?? "", ChunkPercent = p.ChunkPercent, IsChunkProgress = p.IsChunk,
                                Message = "正在下载 " + driver.Title + " · " + FormatBytes(p.Received) + (p.Total > 0 ? "/" + FormatBytes(p.Total) : "") +
                                    (p.IsChunk ? " · 分块 " + p.ChunkId + "：" + Math.Min(100, p.ChunkPercent).ToString("0") + "%" : " · " + FormatBytes(p.Speed) + "/s · " + p.ActiveChunks + " 路连接")
                            });
                        }
                    }, driver.Source == "Microsoft Update Catalog" || (driver.DownloadUrlSource ?? "").StartsWith("Microsoft Update Catalog", StringComparison.Ordinal)).ConfigureAwait(false);
                    written.Add(target);
                }
                File.WriteAllText(Path.Combine(dir, "说明.txt"), BuildMetadata(driver, written), new UTF8Encoding(false)); driver.LocalPath = dir; driver.Status = "已下载"; progress?.Report(new DriverDownloadProgress { Percent = OverallPercent(index, total, urls.Count - 1, urls.Count, 100), Message = "已下载驱动：" + driver.Title });
            }
            catch (OperationCanceledException) { driver.Status = "已取消"; throw; }
        }

        private static string FormatBytes(double value) { return value >= 1048576 ? (value / 1048576).ToString("0.00") + " MB" : value >= 1024 ? (value / 1024).ToString("0.0") + " KB" : Math.Max(0, value).ToString("0") + " B"; }

        private static string BuildMetadata(DriverInfo d, IList<string> files) { var b = new StringBuilder(); b.AppendLine("wumgr 驱动原始下载说明"); b.AppendLine("说明：本说明记录原始包下载，下载不等于安装适用性或签名验证。目录补齐的包属于目录当前修订，不保证等于WUA特定修订；安装始终由WUA重新验证并使用系统缓存。"); b.AppendLine("候选来源：" + d.Source); b.AppendLine("目录详情：" + d.CatalogUrl); b.AppendLine("适用产品（请人工核对）：" + d.Products); b.AppendLine("架构（请人工核对）：" + d.Architecture); b.AppendLine("原始更新 ID：" + d.Id); b.AppendLine("更新修订号：" + (d.RevisionNumber < 0 ? NotProvided : d.RevisionNumber.ToString())); b.AppendLine("原始标题：" + d.Title); b.AppendLine("版本：" + d.Version); b.AppendLine("型号：" + d.DeviceModel); b.AppendLine("硬件 ID：" + d.HardwareId); b.AppendLine("制造商：" + d.Manufacturer); b.AppendLine("提供商：" + d.Provider); b.AppendLine("驱动类别：" + d.DriverClass); b.AppendLine("WUA 已被取代：" + (d.Superseded ? "是" : "否")); b.AppendLine("驱动日期：" + (d.DriverDate == DateTime.MinValue ? NotProvided : d.DriverDate.ToString("yyyy-MM-dd"))); b.AppendLine("下载文件："); foreach (var f in files) b.AppendLine("- " + Path.GetFileName(f)); b.AppendLine("下载地址来源：" + (!String.IsNullOrWhiteSpace(d.DownloadUrlSource) ? d.DownloadUrlSource : (d.Source == "Microsoft Update Catalog" ? "Microsoft Update Catalog（官方白名单）" : "Windows Update Agent"))); b.AppendLine("原始 URL："); foreach (var u in d.DownloadUrls ?? new List<string>()) b.AppendLine("- " + u); return b.ToString(); }

        public static string ExtractVersion(string title)
        {
            if (String.IsNullOrWhiteSpace(title)) return NotProvided; Match m = Regex.Match(title, @"[-–—]\s*((?:\d+\.){1,3}\d+)\s*$"); if (!m.Success) m = Regex.Match(title, @"(?:版本|version|ver\.?)[^0-9]{0,8}((?:\d+\.){1,3}\d+)", RegexOptions.IgnoreCase); return m.Success && !IsDateVersion(m.Groups[1].Value) ? m.Groups[1].Value : NotProvided;
        }
        private static bool IsDateVersion(string v) { var p = v.Split('.'); return p.Length >= 3 && p[0].Length == 4 && p[1].Length == 2 && p[2].Length == 2; }
        private static object Get(dynamic o, string p) { try { if (o == null) return null; switch (p) { case "Identity.UpdateID": return o.Identity.UpdateID; case "Identity.RevisionNumber": return o.Identity.RevisionNumber; case "UpdateID": return o.UpdateID; case "Type": return o.Type; case "Title": return o.Title; case "DriverManufacturer": return o.DriverManufacturer; case "DriverProvider": return o.DriverProvider; case "DriverClass": return o.DriverClass; case "DriverModel": return o.DriverModel; case "DriverHardwareID": return o.DriverHardwareID; case "DriverVerDate": return o.DriverVerDate; case "MaxDownloadSize": return o.MaxDownloadSize; case "IsSuperseded": return o.IsSuperseded; case "DownloadUrl": return o.DownloadUrl; case "IsDeltaCompressedContent": return o.IsDeltaCompressedContent; default: return null; } } catch { return null; } }
        private static string HardwareText(object value) { if (value == null) return NotProvided; var e = value as IEnumerable; if (e != null && !(value is string)) { var a = new List<string>(); foreach (var x in e) if (x != null && !String.IsNullOrWhiteSpace(Convert.ToString(x))) a.Add(Convert.ToString(x).Trim()); return a.Count == 0 ? NotProvided : String.Join("; ", a); } return Text(value); }
        private static string Text(object v) { var s = v == null ? "" : Convert.ToString(v); return String.IsNullOrWhiteSpace(s) ? NotProvided : s.Trim(); }
        private static void ReleaseCom(object v) { try { if (v != null && Marshal.IsComObject(v)) Marshal.FinalReleaseComObject(v); } catch { } }
        private static bool IsHttpUrl(string s) { Uri u; return Uri.TryCreate(s, UriKind.Absolute, out u) && IsHttpUrl(u); }
        private static bool IsHttpUrl(Uri u) { return (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && String.IsNullOrEmpty(u.UserInfo); }
        private static int OverallPercent(int item, int total, int file, int files, int local) { if (total <= 0) return Math.Min(100, local); double units = Math.Min(files, file + local / 100.0); return Math.Max(0, Math.Min(100, (int)Math.Floor((item + units / Math.Max(1, files)) * 100.0 / total))); }
        private static string ExtensionFromUrl(string url) { try { var e = Path.GetExtension(new Uri(url).AbsolutePath); return String.IsNullOrWhiteSpace(e) || e.Length > 8 || e.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? ".bin" : e; } catch { return ".bin"; } }
        private static string SafeDirectoryName(string id) { var value = String.IsNullOrWhiteSpace(id) || id == NotProvided ? "driver" : id; foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); value = value.Trim().Trim('.'); if (value.Length == 0) value = "driver"; if (value.Length > 80) value = value.Substring(0, 80).Trim().Trim('.'); using (var sha = SHA256.Create()) value += "_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id ?? ""))).Replace("-", "").Substring(0, 12); return value; }

    }
}

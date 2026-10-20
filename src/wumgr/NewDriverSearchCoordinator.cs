using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace wumgr
{
    /// <summary>可注入的目录编排依赖，供离线测试替换 COM WUA 和目录 HTTP 客户端。</summary>
    public sealed class DriverSearchCoordinator
    {
        public const int MaxCatalogQueries = 12;
        public const int CatalogPagesPerQuery = 2;
        public IReadOnlyList<string> LastDiagnostics { get; private set; } = new List<string>();
        public bool Partial { get; private set; }
        public delegate Task<List<DriverInfo>> WuaScanDelegate(bool includeHistory, CancellationToken token, IProgress<string> progress);
        public delegate MicrosoftUpdateCatalogClient CatalogClientFactory();
        readonly WuaScanDelegate wua;
        readonly CatalogClientFactory factory;

        public DriverSearchCoordinator(WuaScanDelegate wuaScan, CatalogClientFactory catalogFactory)
        {
            if (wuaScan == null) throw new ArgumentNullException("wuaScan");
            if (catalogFactory == null) throw new ArgumentNullException("catalogFactory");
            wua = wuaScan; factory = catalogFactory;
        }

        public async Task<List<DriverInfo>> ScanWithCatalogAsync(IEnumerable<LocalDeviceInfo> localDevices, bool includeHistory, CancellationToken token, IProgress<string> progress)
        {
            token.ThrowIfCancellationRequested();
            LastDiagnostics = new List<string>(); Partial = false;
            var devices = DeviceDiscovery.MergeDevices(localDevices, null);
            var all = new List<DriverInfo>(); var diagnostics = new List<string>();
            bool wuaGood = false;
            try
            {
                var wuaItems = await wua(includeHistory, token, progress).ConfigureAwait(false) ?? new List<DriverInfo>();
                all.AddRange(wuaItems); wuaGood = wuaItems.Count > 0;
                if (!wuaGood) diagnostics.Add("Windows Update 未返回驱动候选");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { diagnostics.Add("Windows Update 失败：" + ex.Message); }

            // WUA 优先；历史勾选时额外查询目录，即使 WUA 已有结果。
            if (!wuaGood || includeHistory || all.Count == 0)
                await AppendCatalogAsync(devices, all, diagnostics, token, progress).ConfigureAwait(false);
            Partial = diagnostics.Count > 0;
            LastDiagnostics = diagnostics.AsReadOnly();
            var normalized = DriverCatalog.NormalizeCandidates(all);
            if (diagnostics.Count > 0)
                progress?.Report("扫描诊断：" + String.Join("；", diagnostics) + "。已保留成功结果，共 " + normalized.Count + " 个候选；0 不代表没有更新。");
            else progress?.Report("扫描完成，共 " + normalized.Count + " 个候选（WUA 与微软目录）。");
            return normalized;
        }

        async Task AppendCatalogAsync(List<LocalDeviceInfo> devices, List<DriverInfo> all, List<string> diagnostics, CancellationToken token, IProgress<string> progress)
        {
            var queries = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in CommonDeviceFilter.Select(devices))
            {
                // 每台最多两个真实 specific Hardware IDs，不把实例 ID/设备名伪装为硬件查询。
                foreach (var id in HardwareQueries(device))
                    if (seen.Add(id.Trim())) queries.Add(id.Trim());
            }
            if (queries.Count == 0) { diagnostics.Add("没有可用于微软目录的本机具体 Hardware ID"); return; }
            if (queries.Count > MaxCatalogQueries) diagnostics.Add("微软目录查询超过限制，已截断为最多 " + MaxCatalogQueries + " 个查询");
            var detailsCache = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
            using (var client = factory())
            {
                for (int i = 0; i < queries.Count && i < MaxCatalogQueries; i++)
                {
                    token.ThrowIfCancellationRequested(); string q = queries[i];
                    try
                    {
                        progress?.Report("正在搜索 Microsoft Update Catalog（" + (i + 1) + "/" + Math.Min(queries.Count, MaxCatalogQueries) + "）：" + q);
                        var search = await client.SearchAsync(q, CatalogPagesPerQuery, token).ConfigureAwait(false);
                        if (search == null) { diagnostics.Add("目录查询无结果：" + q); continue; }
                        foreach (var diagnostic in search.Diagnostics) diagnostics.Add(diagnostic);
                        if (search.Truncated) diagnostics.Add("目录查询达到两页限制：" + q);
                        var deviceMatches = devices.Where(d => DeviceDiscovery.MatchHardwareId(d, q)).ToList();
                        foreach (var raw in search.Entries ?? new List<CatalogEntry>())
                        {
                            token.ThrowIfCancellationRequested();
                            CatalogEntry detail;
                            try { if (raw == null || String.IsNullOrWhiteSpace(raw.Id)) continue; if (!detailsCache.TryGetValue(raw.Id, out detail)) { detail = await client.GetDetailsAsync(raw, token).ConfigureAwait(false); detailsCache[raw.Id] = detail; } }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { diagnostics.Add("目录详情失败 " + (raw == null ? "" : raw.Id) + "：" + ex.Message); continue; }
                            if (detail == null || !HasCompleteMatch(deviceMatches, detail.HardwareIds)) continue;
                            var item = ReadCatalog(detail, deviceMatches);
                            if (item == null) continue;
                            if (!all.Any(x => String.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase) && x.Source == item.Source)) all.Add(item);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { diagnostics.Add("目录查询失败 " + q + "：" + ex.Message); }
                }
            }
        }

        public static List<string> HardwareQueries(LocalDeviceInfo device)
        {
            // Prefer a broad *real* hardware ID, then one specific ID. The detail page
            // must still match any complete local Hardware/Compatible ID; queries are not evidence.
            var ids = (device == null ? new List<string>() : device.HardwareIds ?? new List<string>())
                .Where(x => !String.IsNullOrWhiteSpace(x) && x.Trim().Length <= 100)
                .Select(x => x.Trim()).Where(x => x.IndexOf("\\", StringComparison.Ordinal) > 0)
                .Where(x => x.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) >= 0 || x.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Length).ToList();
            var result = new List<string>();
            if (ids.Count > 0) result.Add(ids[0]);
            if (ids.Count > 1 && !String.Equals(ids[0], ids[ids.Count-1], StringComparison.OrdinalIgnoreCase)) result.Add(ids[ids.Count-1]);
            return result;
        }
        static bool HasCompleteMatch(IEnumerable<LocalDeviceInfo> devices, IEnumerable<string> ids)
        {
            var catalog = new HashSet<string>((ids ?? Enumerable.Empty<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(Normalize), StringComparer.OrdinalIgnoreCase);
            return catalog.Count > 0 && devices.Any(d => (d.HardwareIds ?? new List<string>()).Concat(d.CompatibleIds ?? new List<string>()).Any(x => catalog.Contains(Normalize(x))));
        }
        static string Normalize(string id) { return (id ?? "").Trim().ToUpperInvariant(); }

        /// <summary>只使用详情真实 HardwareIds；搜索词永远不会写入 HardwareId。</summary>
        public static DriverInfo ReadCatalog(CatalogEntry e)
        {
            if (e == null || String.IsNullOrWhiteSpace(e.Id)) return null;
            var ids = (e.HardwareIds ?? new List<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (ids.Count == 0) return null;
            return new DriverInfo { Id = e.Id, Title = e.Title ?? "未提供", DeviceModel = e.Model ?? e.Title ?? "未提供", Version = String.IsNullOrWhiteSpace(e.Version) ? "未提供" : e.Version, HardwareId = String.Join("; ", ids), Manufacturer = e.Manufacturer ?? "未提供", Provider = e.Provider ?? "未提供", DriverClass = e.DriverClass ?? "未提供", DriverDate = e.DriverDate, Size = e.Size, Source = "Microsoft Update Catalog", CatalogUrl = e.DetailUrl ?? "", Products = e.Products ?? "", Architecture = e.Architecture ?? "", CatalogHardwareVerified = false, DownloadUrls = new List<string>(), Status = "目录候选 · 下载地址待解析" };
        }

        /// <summary>仅在本地设备与目录详情完整匹配后标记硬件已验证。</summary>
        public static DriverInfo ReadCatalog(CatalogEntry e, IEnumerable<LocalDeviceInfo> devices)
        {
            var item = ReadCatalog(e);
            if (item == null) return null;
            item.CatalogHardwareVerified = HasCompleteMatch(devices ?? Enumerable.Empty<LocalDeviceInfo>(), e.HardwareIds);
            return item;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace wumgr
{
    /// <summary>本机即插即用设备及其驱动匹配所需的公开信息。</summary>
    public sealed class LocalDeviceInfo
    {
        public string InstanceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public List<string> HardwareIds { get; set; } = new List<string>();
        public List<string> CompatibleIds { get; set; } = new List<string>();
        public int ProblemCode { get; set; }
        public string InstalledVersion { get; set; } = string.Empty;

        public string DisplayStatus
        {
            get { return ProblemCode == 0 ? "正常" : (ProblemCode < 0 ? "未知" : "问题代码: " + ProblemCode); }
        }
    }

    /// <summary>从 WMI 发现设备，并将 WUA 返回的候选按完整硬件 ID 精确匹配。</summary>
    public sealed class DeviceDiscovery
    {
        public Task<List<LocalDeviceInfo>> ScanAsync(CancellationToken cancellationToken, IProgress<string> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("设备发现需要 Windows WMI (Win32_PnPEntity)。");

            return Task.Run(() => ScanCore(cancellationToken, progress), cancellationToken);
        }

        public static List<DriverInfo> Match(LocalDeviceInfo device, IEnumerable<DriverInfo> candidates)
        {
            var result = new List<DriverInfo>();
            if (device == null || candidates == null) return result;
            var hardware = IdSet(device.HardwareIds);
            var compatible = IdSet(device.CompatibleIds);
            if (hardware.Count == 0 && compatible.Count == 0) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fallback = new List<DriverInfo>();
            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                bool direct = Intersects(hardware, IdSet(SplitIds(candidate.HardwareId)));
                bool compat = !direct && Intersects(compatible, IdSet(SplitIds(candidate.HardwareId)));
                if (!direct && !compat) continue;
                string id = (candidate.Id ?? string.Empty).Trim();
                if (id.Length != 0 && !seen.Add(id)) continue;
                if (direct) result.Add(candidate); else fallback.Add(candidate);
            }
            result.AddRange(fallback);
            return result;
        }

        public static List<DriverInfo> SelectAutomatic(IEnumerable<LocalDeviceInfo> devices, IEnumerable<DriverInfo> candidates)
        {
            var result = new List<DriverInfo>();
            if (devices == null || candidates == null) return result;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in devices)
            {
                if (device == null) continue;
                var matches = Match(device, candidates);
                var eligible = matches.Where(d => d != null && !d.Superseded
                    && !String.IsNullOrWhiteSpace(d.Id)
                    && d.DownloadUrls != null && d.DownloadUrls.Any(u => !String.IsNullOrWhiteSpace(u))
                    && TryVersion(d.Version, out _)
                    && IsNewerThanInstalled(d, device.InstalledVersion)
                    )
                    .ToList();
                if (eligible.Count == 0) continue;
                eligible.Sort((a, b) => CompareAutomatic(a, b, device));
                var selected = eligible[0];
                if (used.Add(selected.Id.Trim())) result.Add(selected);
            }
            return result;
        }

        private static List<LocalDeviceInfo> ScanCore(CancellationToken token, IProgress<string> progress)
        {
            var result = new List<LocalDeviceInfo>();
            progress?.Report("正在读取即插即用设备…");
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var signed = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion FROM Win32_PnPSignedDriver"))
            using (var objects = signed.Get())
            {
                foreach (ManagementObject item in objects)
                {
                    token.ThrowIfCancellationRequested();
                    var id = Text(item["DeviceID"]);
                    if (id.Length != 0 && !versions.ContainsKey(id)) versions[id] = Text(item["DriverVersion"]);
                }
            }
            using (var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, Name, Manufacturer, PNPClass, HardwareID, CompatibleID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Present = TRUE"))
            using (var objects = searcher.Get())
            {
                foreach (ManagementObject item in objects)
                {
                    token.ThrowIfCancellationRequested();
                    var device = new LocalDeviceInfo {
                        InstanceId = Text(item["PNPDeviceID"]), Name = Text(item["Name"]),
                        Manufacturer = Text(item["Manufacturer"]), Class = Text(item["PNPClass"]),
                        HardwareIds = Values(item["HardwareID"]), CompatibleIds = Values(item["CompatibleID"]),
                        ProblemCode = Number(item["ConfigManagerErrorCode"]), InstalledVersion = string.Empty
                    };
                    string version;
                    if (versions.TryGetValue(device.InstanceId, out version)) device.InstalledVersion = version;
                    result.Add(device);
                }
            }
            progress?.Report("设备发现完成：" + result.Count);
            return result;
        }

        private static int CompareAutomatic(DriverInfo a, DriverInfo b, LocalDeviceInfo device)
        {
            int aRank = MatchRank(device, a), bRank = MatchRank(device, b);
            if (aRank != bRank) return bRank.CompareTo(aRank);
            Version av, bv; TryVersion(a.Version, out av); TryVersion(b.Version, out bv);
            int c = bv.CompareTo(av); if (c != 0) return c;
            c = b.DriverDate.CompareTo(a.DriverDate); if (c != 0) return c;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id);
        }

        private static int MatchRank(LocalDeviceInfo d, DriverInfo c)
        {
            return Intersects(IdSet(d.HardwareIds), IdSet(SplitIds(c.HardwareId))) ? 2 : 1;
        }

        private static bool IsNewerThanInstalled(DriverInfo candidate, string installed)
        {
            Version iv;
            if (String.IsNullOrWhiteSpace(installed) || String.Equals(installed.Trim(), "未提供", StringComparison.OrdinalIgnoreCase)) return true;
            if (!TryVersion(installed, out iv)) return false;
            Version cv; return TryVersion(candidate.Version, out cv) && cv > iv;
        }

        private static bool TryVersion(string text, out Version version)
        {
            version = null;
            return !String.IsNullOrWhiteSpace(text) && !String.Equals(text.Trim(), "未提供", StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(text.Trim(), out version);
        }

        private static HashSet<string> IdSet(IEnumerable<string> values)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values != null) foreach (var value in values) { var id = Normalize(value); if (id.Length != 0) result.Add(id); }
            return result;
        }
        private static bool Intersects(HashSet<string> a, HashSet<string> b) { return a.Any(b.Contains); }
        private static IEnumerable<string> SplitIds(string value) { return (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries); }
        private static string Normalize(string value) { return String.IsNullOrWhiteSpace(value) ? string.Empty : (value.Trim() == "未提供" ? string.Empty : value.Trim().ToUpperInvariant()); }
        private static string Text(object value) { return value == null ? string.Empty : Convert.ToString(value).Trim(); }
        private static List<string> Values(object value)
        {
            var list = new List<string>();
            var array = value as Array;
            if (array != null) foreach (var item in array) { var text = Text(item); if (text.Length != 0) list.Add(text); }
            else { var text = Text(value); if (text.Length != 0) list.Add(text); }
            return list;
        }
        private static int Number(object value) { int n; return Int32.TryParse(Text(value), out n) ? n : 0; }
    }
}

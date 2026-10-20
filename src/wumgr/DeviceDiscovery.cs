using System;
using System.Collections.Generic;
using System.Globalization;
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
        public string ClassGuid { get; set; } = string.Empty;
        public string Category { get { return DeviceClassification.Category(Class, ClassGuid); } }
        public List<string> HardwareIds { get; set; } = new List<string>();
        public List<string> CompatibleIds { get; set; } = new List<string>();
        public int ProblemCode { get; set; } = -1;
        public string InstalledVersion { get; set; } = string.Empty;
        public string InstalledProvider { get; set; } = string.Empty;
        public string InstalledDate { get; set; } = string.Empty;

        public string DisplayStatus
        {
            get { return ProblemCode == 0 ? "正常" : (ProblemCode < 0 ? "未知" : "问题代码: " + ProblemCode); }
        }
    }

    /// <summary>通过 SetupAPI 发现设备，WMI 仅作补充，并将 WUA 返回的候选按完整硬件 ID 精确匹配。</summary>
    public sealed class DeviceDiscovery
    {
        public Task<List<LocalDeviceInfo>> ScanAsync(CancellationToken cancellationToken, IProgress<string> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("设备发现需要 Windows SetupAPI。");

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
            foreach (var candidate in DriverCatalog.NormalizeCandidates(candidates))
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

        public static bool MatchHardwareId(LocalDeviceInfo device, string id)
        {
            if (device == null || String.IsNullOrWhiteSpace(id)) return false;
            string wanted = Normalize(id);
            return (device.HardwareIds ?? new List<string>()).Concat(device.CompatibleIds ?? new List<string>()).Any(x => Normalize(x) == wanted);
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
                    && d.CanDownload && d.Source != "Microsoft Update Catalog"
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
            var native = new List<LocalDeviceInfo>();
            var wmi = new List<LocalDeviceInfo>();
            var errors = new List<string>();
            try { native = SetupApiDeviceSource.Scan(token, progress); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add("SetupAPI: " + ex.Message); }
            // WMI provider 不可用或单个属性缺失，不应使已发现的全部设备丢失。
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity"))
                using (var objects = searcher.Get())
                {
                    foreach (ManagementObject item in objects)
                    using (item)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var present = Property(item, "Present");
                            if (present is bool && !(bool)present) continue;
                            var device = new LocalDeviceInfo {
                                InstanceId = Text(Property(item, "PNPDeviceID")), Name = Text(Property(item, "Name")),
                                Manufacturer = Text(Property(item, "Manufacturer")), Class = Text(Property(item, "PNPClass")),
                                ClassGuid = Text(Property(item, "ClassGuid")),
                                HardwareIds = Values(Property(item, "HardwareID")), CompatibleIds = Values(Property(item, "CompatibleID")),
                                ProblemCode = Number(Property(item, "ConfigManagerErrorCode"))
                            };
                            if (device.InstanceId.Length == 0) device.InstanceId = Text(Property(item, "DeviceID"));
                            if (device.Name.Length == 0) device.Name = Text(Property(item, "Description"));
                            wmi.Add(device);
                        }
                        catch (Exception ex) { errors.Add("WMI device: " + ex.Message); }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add("WMI inventory: " + ex.Message); }
            var result = MergeDevices(native, wmi);
            // 已安装版本读取失败只影响版本显示，不能阻断显卡/网卡等枚举。
            try
            {
                var versions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                using (var signed = new ManagementObjectSearcher("SELECT DeviceID, DriverProviderName, DriverDate, DriverVersion FROM Win32_PnPSignedDriver"))
                using (var objects = signed.Get())
                    foreach (ManagementObject item in objects)
                    using (item)
                    {
                        token.ThrowIfCancellationRequested();
                        var id = Text(Property(item, "DeviceID"));
                        if (id.Length != 0) versions[id] = new string[] {
                            Text(Property(item, "DriverVersion")), Text(Property(item, "DriverProviderName")),
                            NormalizeDate(Text(Property(item, "DriverDate"))) };
                    }
                foreach (var device in result)
                {
                    string[] metadata;
                    if (!versions.TryGetValue(device.InstanceId ?? "", out metadata)) continue;
                    if (String.IsNullOrWhiteSpace(device.InstalledVersion)) device.InstalledVersion = metadata[0];
                    if (String.IsNullOrWhiteSpace(device.InstalledProvider)) device.InstalledProvider = metadata[1];
                    if (String.IsNullOrWhiteSpace(device.InstalledDate)) device.InstalledDate = metadata[2];
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add("WMI versions: " + ex.Message); }
            if (result.Count == 0 && errors.Count > 0)
                throw new InvalidOperationException("无法读取本机设备：" + String.Join("; ", errors.Distinct()));
            if (errors.Count > 0) progress?.Report("设备扫描已降级：" + String.Join("; ", errors.Distinct()));
            progress?.Report("设备发现完成：" + result.Count);
            return result;
        }

        /// <summary>按实例而不是显示名称归并，保留无驱动/未知设备和同型号多实例。</summary>
        public static List<LocalDeviceInfo> MergeDevices(IEnumerable<LocalDeviceInfo> primary, IEnumerable<LocalDeviceInfo> secondary)
        {
            var result = new List<LocalDeviceInfo>();
            var byId = new Dictionary<string, LocalDeviceInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in (primary ?? Enumerable.Empty<LocalDeviceInfo>()).Concat(secondary ?? Enumerable.Empty<LocalDeviceInfo>()))
            {
                if (source == null) continue;
                string id = (source.InstanceId ?? "").Trim();
                LocalDeviceInfo target;
                if (id.Length == 0 || !byId.TryGetValue(id, out target))
                {
                    target = new LocalDeviceInfo { InstanceId = id, Name = source.Name, Manufacturer = source.Manufacturer,
                        Class = source.Class, ClassGuid = source.ClassGuid, ProblemCode = source.ProblemCode,
                        InstalledVersion = source.InstalledVersion, InstalledProvider = source.InstalledProvider,
                        InstalledDate = source.InstalledDate, HardwareIds = ValuesFromIds(source.HardwareIds), CompatibleIds = ValuesFromIds(source.CompatibleIds) };
                    result.Add(target);
                    if (id.Length > 0) byId.Add(id, target);
                    continue;
                }
                if (String.IsNullOrWhiteSpace(target.Name)) target.Name = source.Name;
                if (String.IsNullOrWhiteSpace(target.Manufacturer)) target.Manufacturer = source.Manufacturer;
                if (String.IsNullOrWhiteSpace(target.Class)) target.Class = source.Class;
                if (String.IsNullOrWhiteSpace(target.ClassGuid)) target.ClassGuid = source.ClassGuid;
                if (String.IsNullOrWhiteSpace(target.InstalledVersion)) target.InstalledVersion = source.InstalledVersion;
                if (String.IsNullOrWhiteSpace(target.InstalledProvider)) target.InstalledProvider = source.InstalledProvider;
                if (String.IsNullOrWhiteSpace(target.InstalledDate)) target.InstalledDate = source.InstalledDate;
                if (target.ProblemCode < 0) target.ProblemCode = source.ProblemCode;
                target.HardwareIds = ValuesFromIds(target.HardwareIds.Concat(source.HardwareIds ?? new List<string>()));
                target.CompatibleIds = ValuesFromIds(target.CompatibleIds.Concat(source.CompatibleIds ?? new List<string>()));
            }
            return result;
        }
        static List<string> ValuesFromIds(IEnumerable<string> ids)
        {
            return (ids ?? Enumerable.Empty<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        static object Property(ManagementObject item, string name) { try { return item[name]; } catch { return null; } }

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
        private static string NormalizeDate(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            DateTime date;
            string text = value.Trim();
            if (text.Length >= 8 && text[0] >= '0' && text[0] <= '9' && text[1] >= '0' && text[1] <= '9'
                && text[2] >= '0' && text[2] <= '9' && text[3] >= '0' && text[3] <= '9' && text[4] == '-'
                && text[7] == '-')
            {
                if (DateTime.TryParseExact(text.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date)) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            if (text.Length >= 14 && DateTime.TryParseExact(text.Substring(0, 14), "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (DateTime.TryParseExact(text, "yyyyMMddHHmmss.ffffffzzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParseExact(text, "yyyyMMddHHmmss.ffffff+000", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return String.Empty;
        }
        private static List<string> Values(object value)
        {
            var list = new List<string>();
            var array = value as Array;
            if (array != null) foreach (var item in array) { var text = Text(item); if (text.Length != 0) list.Add(text); }
            else { var text = Text(value); if (text.Length != 0) list.Add(text); }
            return list;
        }
        private static int Number(object value) { int n; return Int32.TryParse(Text(value), out n) ? n : -1; }
    }
}

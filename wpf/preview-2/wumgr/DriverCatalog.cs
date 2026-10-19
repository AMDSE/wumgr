using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace wumgr
{
    /// <summary>同一设备的驱动候选版本集合。历史仅限 WUA 本次查询返回的候选。</summary>
    public sealed class DriverDevice
    {
        public string Key { get; internal set; }
        public string Model { get; internal set; }
        public string Manufacturer { get; internal set; }
        public string Provider { get; internal set; }
        public string Class { get; internal set; }
        public LocalDeviceInfo LocalDevice { get; internal set; }
        public List<DriverInfo> Versions { get; private set; }
        public DriverInfo Latest { get { return Versions.Count == 0 ? null : Versions[0]; } }

        internal DriverDevice(string key, DriverInfo first)
        {
            Key = key;
            Model = first.DeviceModel;
            Manufacturer = first.Manufacturer;
            Provider = first.Provider;
            Class = first.DriverClass;
            Versions = new List<DriverInfo>();
        }
    }

    /// <summary>将 WUA 驱动候选安全地归并为设备，并提供版本顺序。</summary>
    public static class DriverCatalog
    {
        private const string NotProvided = "未提供";

        public static List<DriverDevice> Group(IEnumerable<DriverInfo> drivers)
        {
            var result = new List<DriverDevice>();
            if (drivers == null) return result;

            var byKey = new Dictionary<string, DriverDevice>(StringComparer.Ordinal);
            int missingIdOrdinal = 0;
            foreach (var driver in NormalizeCandidates(drivers))
            {
                if (driver == null) continue;
                string id = Canonical(driver.Id);

                // 无硬件 ID 的每个候选都保留为独立设备（即使其余字段完全相同）。
                string key = GroupKey(driver, Missing(id) ? missingIdOrdinal++ : -1);
                DriverDevice device;
                if (!byKey.TryGetValue(key, out device))
                {
                    device = new DriverDevice(key, driver);
                    byKey.Add(key, device);
                    result.Add(device);
                }
                device.Versions.Add(driver);
            }
            foreach (var device in result)
                device.Versions.Sort(CompareVersions);
            return result;
        }

        /// <summary>
        /// 以保守语义规范化 WUA 候选。返回的对象始终是输入中的原实例；同一更新 ID
        /// 选择最高修订号；不同修订的下载包绝不能混合。
        /// 不同 ID 只有在所有这些字段及规范化的有效下载 URL 集完全相同且字段完整时才合并。
        /// </summary>
        public static List<DriverInfo> NormalizeCandidates(IEnumerable<DriverInfo> drivers)
        {
            var input = (drivers ?? Enumerable.Empty<DriverInfo>()).Where(x => x != null).ToList();
            var byId = new Dictionary<string, List<DriverInfo>>(StringComparer.Ordinal);
            var noId = new List<DriverInfo>();
            foreach (var d in input)
            {
                string id = Canonical(d.Id);
                if (Missing(id)) noId.Add(d);
                else
                {
                    List<DriverInfo> list;
                    if (!byId.TryGetValue(id, out list)) byId.Add(id, list = new List<DriverInfo>());
                    list.Add(d);
                }
            }

            var afterIds = new List<DriverInfo>();
            var validUrls = new Dictionary<DriverInfo, List<string>>();
            foreach (var pair in byId)
            {
                // WUA identity is UpdateID + RevisionNumber. A new revision replaces
                // the old metadata; never mix payload URLs from different revisions.
                var chosen = pair.Value.OrderByDescending(d => d.RevisionNumber)
                    .ThenByDescending(d => HasValidUrl(d, validUrls)).First();
                if (chosen.DownloadUrls == null) chosen.DownloadUrls = new List<string>();
                afterIds.Add(chosen);
            }

            // Missing IDs are intentionally never deduplicated. They are not reliable keys.
            afterIds.AddRange(noId);
            var result = new List<DriverInfo>();
            var byPayload = new Dictionary<string, DriverInfo>(StringComparer.Ordinal);
            foreach (var d in afterIds)
            {
                if (d.DownloadUrls == null) d.DownloadUrls = new List<string>();
                string payload = SemanticKey(d, validUrls);
                if (payload == null) { result.Add(d); continue; }
                DriverInfo existing;
                if (!byPayload.TryGetValue(payload, out existing))
                {
                    byPayload.Add(payload, d);
                    result.Add(d);
                }

            }
            return result;
        }

        private static string SemanticKey(DriverInfo d, Dictionary<DriverInfo, List<string>> validUrls)
        {
            // Missing metadata or no valid payload makes a semantic merge unsafe.
            if (Missing(d.Id) || Missing(d.HardwareId)) return null;
            List<string> urls;
            if (!validUrls.TryGetValue(d, out urls))
            {
                urls = ValidUrls(d).ToList();
                validUrls.Add(d, urls);
            }
            if (urls.Count == 0) return null;
            return TupleKey(d, true) + LengthPrefix(d.Version) + LengthPrefix(d.DriverDate.Ticks.ToString(CultureInfo.InvariantCulture))
                + LengthPrefix(String.Join(";", urls.OrderBy(x => x, StringComparer.Ordinal)));
        }

        private static string TupleKey(DriverInfo d, bool includeHardware)
        {
            return LengthPrefix(includeHardware ? HardwareKey(d.HardwareId) : String.Empty)
                + LengthPrefix(Canonical(d.DeviceModel)) + LengthPrefix(Canonical(d.Provider))
                + LengthPrefix(Canonical(d.DriverClass)) + LengthPrefix(Canonical(d.Manufacturer));
        }

        private static IEnumerable<string> ValidUrls(DriverInfo d)
        {
            return (d.DownloadUrls ?? new List<string>()).Select(CanonicalUrl).Where(x => x != null).Distinct(StringComparer.Ordinal);
        }

        private static bool HasValidUrl(DriverInfo d, Dictionary<DriverInfo, List<string>> validUrls)
        {
            List<string> urls;
            if (!validUrls.TryGetValue(d, out urls))
            {
                urls = ValidUrls(d).ToList();
                validUrls.Add(d, urls);
            }
            return urls.Count > 0;
        }

        private static string CanonicalUrl(string value)
        {
            Uri uri;
            if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !String.IsNullOrEmpty(uri.UserInfo)) return null;
            return uri.AbsoluteUri;
        }

        private static string GroupKey(DriverInfo driver, int missingIdOrdinal)
        {
            string id = Canonical(driver.Id);
            if (Missing(id)) return "item|" + LengthPrefix(missingIdOrdinal.ToString(CultureInfo.InvariantCulture));

            string hardware = HardwareKey(driver.HardwareId);
            if (hardware.Length == 0) return "id|" + LengthPrefix(id);

            // 组键是 (hardware set, model, provider, class, manufacturer) 的长度前缀编码，避免分隔符冲突。
            return "tuple|" + LengthPrefix(hardware)
                + LengthPrefix(Canonical(driver.DeviceModel))
                + LengthPrefix(Canonical(driver.Provider))
                + LengthPrefix(Canonical(driver.DriverClass))
                + LengthPrefix(Canonical(driver.Manufacturer));
        }

        private static string LengthPrefix(string value)
        {
            value = value ?? String.Empty;
            return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        }

        private static int CompareVersions(DriverInfo a, DriverInfo b)
        {
            bool av, bv; Version va, vb;
            av = TryVersion(a == null ? null : a.Version, out va);
            bv = TryVersion(b == null ? null : b.Version, out vb);
            if (av != bv) return av ? -1 : 1;
            if (av)
            {
                int c = vb.CompareTo(va);
                if (c != 0) return c;
            }
            int date = DateTime.Compare(b.DriverDate, a.DriverDate);
            if (date != 0) return date;
            return StringComparer.OrdinalIgnoreCase.Compare(a.Id ?? String.Empty, b.Id ?? String.Empty);
        }

        private static bool TryVersion(string text, out Version version)
        {
            version = null;
            if (Missing(text)) return false;
            return Version.TryParse(text.Trim(), out version);
        }

        private static bool Missing(string value) { return String.IsNullOrWhiteSpace(value) || String.Equals(value.Trim(), NotProvided, StringComparison.OrdinalIgnoreCase); }
        private static string Canonical(string value)
        {
            return Missing(value) ? NotProvided : value.Trim().ToUpperInvariant();
        }

        private static string HardwareKey(string value)
        {
            if (Missing(value)) return String.Empty;
            return String.Join(";", value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !Missing(x))
                .Select(x => x.ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal));
        }
    }
}

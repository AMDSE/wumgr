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

            // WUA 更新 ID 是候选的安全去重键；没有 ID 的候选不能用空字符串互相去重。
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byKey = new Dictionary<string, DriverDevice>(StringComparer.Ordinal);
            int missingIdOrdinal = 0;
            foreach (var driver in drivers)
            {
                if (driver == null) continue;
                string id = Canonical(driver.Id);
                if (!Missing(id) && !seenIds.Add(id)) continue;

                // 无 ID 的每个候选都保留为独立设备（即使其余字段完全相同）。
                string key = GroupKey(driver, Missing(id) ? missingIdOrdinal++ : -1);
                DriverDevice device;
                if (!byKey.TryGetValue(key, out device))
                {
                    device = new DriverDevice(key, driver);
                    byKey.Add(key, device);
                    result.Add(device);
                }
                if (!device.Versions.Any(x => !Missing(x.Id) && String.Equals(x.Id.Trim(), id, StringComparison.OrdinalIgnoreCase)))
                    device.Versions.Add(driver);
            }
            foreach (var device in result)
                device.Versions.Sort(CompareVersions);
            return result;
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

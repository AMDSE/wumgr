using System;
using System.Collections.Generic;
using System.Linq;

namespace wumgr
{
    /// <summary>设备类别来自 PnP 类或安装类 GUID，而不是驱动标题、厂商或 CPU 名称。</summary>
    public static class DeviceClassification
    {
        public static string Category(string deviceClass, string classGuid = null)
        {
            string c = (deviceClass ?? "").Trim().ToLowerInvariant();
            string g = (classGuid ?? "").Trim().Trim('{', '}').ToLowerInvariant();
            if (c == "bluetooth" || c == "蓝牙" || g == "e0cbf06c-cd8b-4647-bb8a-263b43f0f974") return "蓝牙";
            if (c == "display" || c == "display adapters" || c == "显示适配器" || c == "显卡" || g == "4d36e968-e325-11ce-bfc1-08002be10318") return "显卡";
            if (c == "net" || c == "network adapters" || c == "网络适配器" || g == "4d36e972-e325-11ce-bfc1-08002be10318") return "网络适配器";
            if (c == "media" || c == "audioendpoint" || c == "audio" || c == "sound" || c == "声音、视频和游戏控制器" || c == "音频输入和输出" || c == "声卡" || g == "4d36e96c-e325-11ce-bfc1-08002be10318" || g == "c166523c-fe0c-4a94-a586-f1a80cfbbf3e") return "声卡";
            if (c == "system" || c == "processor" || c == "系统设备" || c == "处理器" || g == "4d36e97d-e325-11ce-bfc1-08002be10318" || g == "50127dc3-0f36-415e-a6cc-4cb3be910b65") return "系统设备";
            return "其他设备";
        }
    }

    public static class DeviceInventory
    {
        /// <summary>每个真实实例一张卡；没有更新也不能从设备清单消失。</summary>
        public static List<DriverDevice> BuildEntries(IEnumerable<LocalDeviceInfo> local, IEnumerable<DriverInfo> candidates)
        {
            var available = DriverCatalog.NormalizeCandidates(candidates);
            var inventory = DeviceDiscovery.MergeDevices(local, null);
            var entries = new List<DriverDevice>();
            int ordinal = 0;
            foreach (var item in inventory)
            {
                var matches = DeviceDiscovery.Match(item, available);
                // Match 保留硬件精确匹配优先；同级候选按数值版本、日期排序。
                var ordered = matches.OrderByDescending(d => IsDirect(item, d))
                    .ThenByDescending(d => ParseVersion(d.Version)).ThenByDescending(d => d.DriverDate)
                    .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
                var first = ordered.FirstOrDefault() ?? new DriverInfo();
                var entry = new DriverDevice("local|" + (String.IsNullOrWhiteSpace(item.InstanceId) ? "ordinal:" + ordinal : item.InstanceId.Trim().ToUpperInvariant()), first) {
                    LocalDevice = item,
                    Model = String.IsNullOrWhiteSpace(item.Name) ? item.InstanceId : item.Name,
                    Manufacturer = item.Manufacturer,
                    Class = item.Class
                };
                entry.Versions.AddRange(ordered);
                entries.Add(entry);
                ordinal++;
            }
            return entries;
        }
        static bool IsDirect(LocalDeviceInfo device, DriverInfo candidate)
        {
            var ids = new HashSet<string>(device.HardwareIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return (candidate.HardwareId ?? "").Split(';').Any(x => ids.Contains(x.Trim()));
        }
        static Version ParseVersion(string text) { Version v; return Version.TryParse(text, out v) ? v : new Version(0, 0); }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

    /// <summary>只过滤主界面；完整枚举仍保留给设备搜索和精确匹配，不改写系统设备。</summary>
    public static class CommonDeviceFilter
    {
        static bool Has(string text, string pattern) { return Regex.IsMatch(text ?? "", pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        public static bool IsCommon(LocalDeviceInfo device)
        {
            if (device == null || String.IsNullOrWhiteSpace(device.Name)) return false;
            string id = (device.InstanceId ?? "").Trim();
            string name = device.Name;
            string c = (device.Class ?? "").Trim();
            if (Has(id, @"^(SWD\\|SW\\|HTREE\\|VMBUS\\|BTHENUM\\|BTHLEDEVICE\\|ROOT\\(VMS|VIRTUAL|RDP|NET|VPN))")
                || Has(name, @"virtual|虚拟|WAN Miniport|WAN 微型端口|Hyper-V|VMware|VirtualBox|TAP-Windows|TUN Adapter|WireGuard|Wintun|OpenVPN|ZeroTier|Tailscale|Loopback|环回|Remote Desktop|远程桌面|Microsoft Wi-Fi Direct|Microsoft Kernel Debug|Microsoft Bluetooth (Enumerator|LE Enumerator)|蓝牙.*枚举|Bluetooth Device \((Personal Area Network|RFCOMM)|蓝牙设备.*(个人区域|RFCOMM)")) return false;
            if (device.Category == "网络适配器" && Has(id, @"^ROOT\\")) return false;
            if (device.Category == "显卡" || device.Category == "网络适配器" || device.Category == "蓝牙") return true;
            // 音频端点是声卡的播放/录音子节点，不是另一张声卡。
            if (device.Category == "声卡") return !Has(c, "^AudioEndpoint$") && !Has(device.ClassGuid, "c166523c-fe0c-4a94-a586-f1a80cfbbf3e");
            if (device.Category == "系统设备")
            {
                if (Has(c, "^(Processor|处理器)$") || Has(device.ClassGuid, "50127dc3-0f36-415e-a6cc-4cb3be910b65")) return true;
                // 保留可辨识的芯片组功能控制器，隐藏 PCI 桥、ACPI、计时器、中断和根枚举器。
                return Has(name, @"chipset|芯片组|SMBus|SM Bus|Management Engine|管理引擎|Platform Security Processor|Serial IO|Serial I/O|GPIO|I2C|I²C|SPI Controller|Thunderbolt|雷电|USB4|Dynamic Tuning|Dynamic Platform|Innovation Platform|AMD PSP")
                    && !Has(name, @"Root Port|根端口|PCI.*Bridge|PCI.*桥|Enumerator|枚举|Resource|资源");
            }
            if (Has(c, @"^(DiskDrive|HDC|SCSIAdapter|Keyboard|Mouse|Monitor|Camera|Image|Printer|Biometric|SmartCardReader|SDHost)$")) return true;
            // 安装类 GUID 回退：磁盘、存储控制器、键盘、鼠标、显示器、摄像头、图像、打印机、生物识别。
            if (Has(device.ClassGuid, @"4d36e96(7|a|b|f)-e325|4d36e97(9|b)-e325|4d36e96e-e325|ca3e7ab9-b4c3|6bdd1fc6-810f|4d36e979-e325|53d29ef7-377c")) return true;
            if (Has(c, "^USB$") || Has(device.ClassGuid, "36fc9e60-c465"))
                return Has(name, @"Host Controller|主机控制器|xHCI|eHCI") && !Has(name, @"Hub|集线器");
            // HID 只保留有意义的触摸/手写/游戏设备，不罗列每个 HID collection。
            return Has(c, "^HIDClass$") && Has(name, @"Touchpad|触摸板|Touch Screen|触摸屏|Game Controller|游戏控制器|Gamepad|手柄|Pen Device|手写笔");
        }
        public static List<LocalDeviceInfo> Select(IEnumerable<LocalDeviceInfo> source)
        {
            var processors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return DeviceDiscovery.MergeDevices(source, null).Where(IsCommon).Where(d => {
                bool cpu = Has(d.Class, "^(Processor|处理器)$") || Has(d.ClassGuid, "50127dc3-0f36-415e-a6cc-4cb3be910b65");
                // 同一处理器的逻辑核心在 PnP 中有多个实例，仅主界面折叠；真实同型号外设仍分开。
                return !cpu || processors.Add(d.Name + "|" + d.InstalledVersion);
            }).ToList();
        }
    }

    public static class DeviceInventory
    {
        /// <summary>每个常见硬件实例一张卡；没有更新也保留当前已安装驱动。</summary>
        public static List<DriverDevice> BuildEntries(IEnumerable<LocalDeviceInfo> local, IEnumerable<DriverInfo> candidates)
        {
            var available = DriverCatalog.NormalizeCandidates(candidates);
            var inventory = CommonDeviceFilter.Select(local);
            var entries = new List<DriverDevice>();
            int ordinal = 0;
            foreach (var item in inventory)
            {
                var ordered = DeviceDiscovery.Match(item, available).OrderByDescending(d => IsDirect(item, d))
                    .ThenByDescending(d => ParseVersion(d.Version)).ThenByDescending(d => d.DriverDate)
                    .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
                var entry = new DriverDevice("local|" + (String.IsNullOrWhiteSpace(item.InstanceId) ? "ordinal:" + ordinal : item.InstanceId.Trim().ToUpperInvariant()), ordered.FirstOrDefault() ?? new DriverInfo()) {
                    LocalDevice = item, Model = item.Name, Manufacturer = item.Manufacturer, Class = item.Class
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

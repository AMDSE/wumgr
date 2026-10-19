using System;
using System.Collections.Generic;
using wumgr;

namespace WuMgr.Wpf.Services
{
    /// <summary>Synthetic fixtures only. example.invalid is reserved and never downloaded.</summary>
    public static class DemoData
    {
        public static IList<LocalDeviceInfo> Devices()
        {
            return new List<LocalDeviceInfo>
            {
                Device("GRAPHICS", "示例图形适配器 / Sample graphics", "Display", "PCI\\VEN_DEMO&DEV_0001", "31.0.10.1000"),
                Device("NETWORK", "示例以太网控制器 / Sample Ethernet", "Net", "PCI\\VEN_DEMO&DEV_0002", "12.1.0.5"),
                Device("AUDIO", "示例音频设备 / Sample audio", "MEDIA", "HDAUDIO\\DEMO_0003", "6.0.1.8000"),
                Device("BLUETOOTH", "示例蓝牙适配器 / Sample Bluetooth", "Bluetooth", "USB\\VID_DEMO&PID_0004", "1.0.0.0"),
                Device("CHIPSET", "示例芯片组设备 / Sample chipset", "System", "PCI\\VEN_DEMO&DEV_0005", "10.1.1.1"),
                new LocalDeviceInfo { InstanceId = "DEMO\\UNKNOWN", Name = "未知设备示例 / Unknown device", Class = "Unknown", Manufacturer = "Sample vendor", HardwareIds = new List<string> { "ACPI\\DEMO0006" }, ProblemCode = 28 }
            };
        }
        static LocalDeviceInfo Device(string id, string name, string cls, string hardware, string installed)
        {
            return new LocalDeviceInfo
            {
                InstanceId = "DEMO\\" + id, Name = name, Class = cls, Manufacturer = "Sample vendor",
                HardwareIds = new List<string> { hardware }, InstalledVersion = installed,
                InstalledProvider = "Sample vendor", InstalledDate = "2025-04-18", ProblemCode = 0
            };
        }
        public static IList<DriverInfo> Candidates()
        {
            return new List<DriverInfo>
            {
                Candidate(1, "Sample graphics driver", "Display", "PCI\\VEN_DEMO&DEV_0001", "32.0.20.2000", false),
                Candidate(2, "Sample graphics driver - previous", "Display", "PCI\\VEN_DEMO&DEV_0001", "31.0.15.1500", true),
                Candidate(3, "Sample graphics driver - installed generation", "Display", "PCI\\VEN_DEMO&DEV_0001", "31.0.10.1000", true),
                Candidate(4, "Sample Ethernet driver", "Net", "PCI\\VEN_DEMO&DEV_0002", "12.3.0.10", false),
                Candidate(5, "Sample Ethernet driver - previous", "Net", "PCI\\VEN_DEMO&DEV_0002", "12.2.0.8", true),
                Candidate(6, "Sample audio driver", "MEDIA", "HDAUDIO\\DEMO_0003", "6.0.1.9000", false),
                Candidate(7, "Sample chipset driver", "System", "PCI\\VEN_DEMO&DEV_0005", "10.1.2.1", false)
            };
        }
        static DriverInfo Candidate(int number, string title, string cls, string hardware, string version, bool superseded)
        {
            return new DriverInfo
            {
                Id = "00000000-0000-0000-0000-" + number.ToString("D12"), RevisionNumber = 1,
                Title = title, DeviceModel = title, Manufacturer = "Sample vendor", Provider = "Sample vendor",
                DriverClass = cls, HardwareId = hardware, Version = version, Superseded = superseded,
                DriverDate = new DateTime(2025, 6, 1).AddDays(-number * 10), Status = "候选",
                DownloadUrls = new List<string> { "https://example.invalid/wumgr-demo/" + number + ".cab" }
            };
        }
    }
}

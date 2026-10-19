using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using wumgr;

class DeviceTests
{
    static int passed;
    static void Check(bool value, string name) { if (!value) throw new Exception("失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    static DriverInfo D(string id, string hw, string version, DateTime date, bool superseded = false, bool url = true)
    {
        return new DriverInfo { Id = id, HardwareId = hw, Version = version, DriverDate = date,
            Superseded = superseded, DownloadUrls = url ? new List<string> { "https://example.invalid/" + id } : new List<string>() };
    }
    static LocalDeviceInfo Device(string instance, string installed, params string[] hardware)
    {
        return new LocalDeviceInfo { InstanceId = instance, InstalledVersion = installed, HardwareIds = hardware.ToList(), ProblemCode = 0 };
    }
    public static int Main()
    {
        try
        {
            var device = Device("D1", "1.0.0.0", "PCI\\VEN_ABCD&DEV_0001");
            var exact = D("exact", "pci\\ven_abcd&dev_0001", "2.0.0.0", new DateTime(2024, 1, 1));
            var substring = D("substring", "PCI\\VEN_ABCD", "9.0.0.0", new DateTime(2025, 1, 1));
            var compatible = D("compatible", "USB\\Class_01", "3.0.0.0", new DateTime(2023, 1, 1));
            device.CompatibleIds.Add("USB\\Class_01");
            var matches = DeviceDiscovery.Match(device, new[] { substring, compatible, exact });
            Check(matches.Count == 2 && matches[0] == exact && matches[1] == compatible, "大小写不敏感完整ID精确匹配并拒绝substring");
            Check(DeviceDiscovery.Match(Device("D2", "", ""), new[] { exact }).Count == 0, "空或缺失硬件ID不匹配");

            var old = D("old", exact.HardwareId, "0.9.0.0", new DateTime(2025, 1, 1));
            var unknown = D("unknown", exact.HardwareId, "未提供", new DateTime(2030, 1, 1));
            var superseded = D("superseded", exact.HardwareId, "8.0.0.0", new DateTime(2030, 1, 1), true);
            var noUrl = D("no-url", exact.HardwareId, "7.0.0.0", new DateTime(2030, 1, 1), false, false);
            var automatic = DeviceDiscovery.SelectAutomatic(new[] { device }, new[] { old, unknown, superseded, noUrl, exact });
            Check(automatic.Count == 1 && automatic[0] == exact, "自动选择排除旧版、未知版本、Superseded和无下载地址");

            var d2 = Device("D2", "1.0.0.0", "PCI\\VEN_ABCD&DEV_0001");
            var newer = D("newer", exact.HardwareId, "3.0.0.0", new DateTime(2025, 1, 1));
            var cross = DeviceDiscovery.SelectAutomatic(new[] { device, d2 }, new[] { newer, exact });
            Check(cross.Count == 1 && cross[0] == newer, "每设备至多一项且跨设备按Id去重");
            Check(device.DisplayStatus == "正常" && new LocalDeviceInfo { ProblemCode = 28 }.DisplayStatus.Contains("28"), "显示状态反映问题代码");

            Check(new LocalDeviceInfo().DisplayStatus == "未知", "状态缺失不冒充设备正常");
            var native = new LocalDeviceInfo { InstanceId = "PCI\\GPU", Name = "GPU", ClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}", HardwareIds = new List<string> { "PCI\\VEN_10DE&DEV_1234" } };
            var supplement = new LocalDeviceInfo { InstanceId = " pci\\gpu ", Class = "Display", InstalledVersion = "1.0", HardwareIds = new List<string> { "pci\\ven_10de&dev_1234", "PCI\\CC_030000" }, ProblemCode = 0 };
            var another = new LocalDeviceInfo { InstanceId = "PCI\\GPU2", Name = "GPU", Class = "Display" };
            var inventory = DeviceDiscovery.MergeDevices(new[] { native, another }, new[] { supplement });
            Check(inventory.Count == 2 && inventory[0].HardwareIds.Count == 2 && inventory[0].InstalledVersion == "1.0" && inventory[0].Name == "GPU", "按实例ID合并原生和WMI元数据，保留同名多设备");
            Check(native.HardwareIds.Count == 1 && native.InstalledVersion == "", "合并不污染原始设备对象");
            Check(DeviceClassification.Category("", native.ClassGuid) == "显卡", "PNPClass缺失时通过安装类GUID识别显卡");
            Check(DeviceClassification.Category("MEDIA") == "声卡" && DeviceClassification.Category("AudioEndpoint") == "声卡", "声卡和音频端点分类");
            Check(DeviceClassification.Category("Net") == "网络适配器" && DeviceClassification.Category("Bluetooth") == "蓝牙", "网卡与蓝牙分开分类");
            Check(DeviceClassification.Category("System") == "系统设备" && DeviceClassification.Category("USB") == "其他设备", "系统设备不吞并其他设备");
            Check(DeviceClassification.Category("Unknown") == "其他设备", "未知类别保持其他设备，不按厂商猜芯片组");
            var all = new[] { native, new LocalDeviceInfo { InstanceId = "HDAUDIO\\A", Name = "Audio", Class = "MEDIA" }, new LocalDeviceInfo { InstanceId = "PCI\\NET", Name = "Wi-Fi", Class = "Net" }, new LocalDeviceInfo { InstanceId = "USB\\BT", Name = "Bluetooth", Class = "Bluetooth" }, new LocalDeviceInfo { InstanceId = "ACPI\\UNKNOWN", ProblemCode = 28 } };
            var entries = DeviceInventory.BuildEntries(all, new DriverInfo[0]);
            Check(entries.Count == 4 && entries.All(e => e.Versions.Count == 0 && e.LocalDevice != null), "没有任何WUA候选时常见显卡声卡网卡蓝牙保留，未知杂项隐藏");
            var gpuUpdate = D("gpu", native.HardwareIds[0], "2.0", new DateTime(2025,1,1));
            entries = DeviceInventory.BuildEntries(all, new[] { gpuUpdate });
            Check(entries.Count == 4 && entries[0].Latest == gpuUpdate && entries.Skip(1).All(e => e.Latest == null), "更新候选只关联匹配设备，不影响其他设备可见性");
            Check(DeviceInventory.BuildEntries(new[] { native, supplement }, new[] { gpuUpdate }).Count == 1, "重复实例只生成一张本机设备卡");
            Check(DeviceDiscovery.MergeDevices(new[] { new LocalDeviceInfo(), new LocalDeviceInfo() }, null).Count == 2, "缺失实例ID不能相互误去重");

            var canceled = false;
            try { new DeviceDiscovery().ScanAsync(new CancellationToken(true), null).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "预取消明确抛出OperationCanceledException");
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                var platform = false;
                try { new DeviceDiscovery().ScanAsync(CancellationToken.None, null).GetAwaiter().GetResult(); }
                catch (PlatformNotSupportedException) { platform = true; }
                Check(platform, "非Windows明确报告PlatformNotSupportedException");
            }
            Console.WriteLine("设备测试完成：" + passed + " 项全部通过。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

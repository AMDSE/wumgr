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
        return new LocalDeviceInfo { InstanceId = instance, InstalledVersion = installed, HardwareIds = hardware.ToList() };
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

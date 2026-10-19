using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using wumgr;

class PerformanceTests
{
    static LocalDeviceInfo Device(int n)
    {
        return new LocalDeviceInfo {
            InstanceId = "PCI\\DEV_" + n,
            Name = "Performance GPU " + n,
            Class = "Display",
            HardwareIds = new List<string> { "PCI\\VEN_PERF&DEV_" + (n % 16).ToString("X4") },
            InstalledVersion = "1.0.0.0"
        };
    }

    static DriverInfo Driver(int n)
    {
        return new DriverInfo {
            Id = "perf-" + n,
            HardwareId = "PCI\\VEN_PERF&DEV_" + (n % 16).ToString("X4"),
            Version = (n % 9 + 1) + ".0.0.0",
            DeviceModel = "Performance GPU",
            Provider = "PerfVendor",
            Manufacturer = "PerfVendor",
            DriverClass = "Display",
            DriverDate = new DateTime(2024, 1, 1).AddDays(n),
            DownloadUrls = new List<string> { "https://example.invalid/perf/" + n + ".cab" }
        };
    }

    static long Time(Action action, int repetitions)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < repetitions; i++) action();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    public static int Main()
    {
        try
        {
            var devices = Enumerable.Range(0, 96).Select(Device).ToList();
            var candidates = Enumerable.Range(0, 192).Select(Driver).ToList();
            var entries = DeviceInventory.BuildEntries(devices, candidates);
            if (entries.Count != devices.Count || entries.Any(e => e.Latest == null))
                throw new Exception("库存性能样本的匹配结果不完整");

            // 结果断言用于防止基准代码掩盖匹配语义变化；计时用于发布前比较同一机器的回归趋势。
            var catalogMs = Time(() => DriverCatalog.NormalizeCandidates(candidates), 12);
            var inventoryMs = Time(() => DeviceInventory.BuildEntries(devices, candidates), 3);
            Console.WriteLine("性能样本：设备={0}，候选={1}", devices.Count, candidates.Count);
            Console.WriteLine("NormalizeCandidates 12次：{0} ms", catalogMs);
            Console.WriteLine("BuildEntries 3次：{0} ms", inventoryMs);
            Console.WriteLine("性能测试完成：匹配结果与数量断言通过。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("性能测试失败：" + ex);
            return 1;
        }
    }
}

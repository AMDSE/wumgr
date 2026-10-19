using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using wumgr;

class EnumerationTests
{
    static int passed;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("失败：" + name);
        passed++;
        Console.WriteLine("通过：" + name);
    }

    public static int Main()
    {
        try
        {
            List<string> values = SetupApiDeviceSource.ParseMultiSz("PCI\\VEN_1234\0PCI\\CC_0300\0\0");
            Check(values.Count == 2 && values[0] == "PCI\\VEN_1234" && values[1] == "PCI\\CC_0300", "MULTI_SZ解析多个UTF-16项目");
            values = SetupApiDeviceSource.ParseMultiSz("one\0two\0");
            Check(values.Count == 2 && values[1] == "two", "MULTI_SZ允许单终止符");
            Check(SetupApiDeviceSource.ParseMultiSz(null).Count == 0, "空MULTI_SZ解析为空列表");
            int expected = IntPtr.Size == 8 ? 32 : 28;
            Check(SetupApiDeviceSource.NativeDeviceInfoDataSize == expected, "SP_DEVINFO_DATA跨32/64布局尺寸");
            var source = typeof(SetupApiDeviceSource);
            Check((uint)source.GetField("SPDRP_CLASS", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue() == 7 && (uint)source.GetField("SPDRP_MFG", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue() == 11, "设备类别和制造商属性常量符合Windows SDK");
            var openKey = source.GetMethod("SetupDiOpenDevRegKey", BindingFlags.NonPublic | BindingFlags.Static);
            Check(openKey.GetParameters().Length == 6 && typeof(SafeHandle).IsAssignableFrom(openKey.ReturnType), "SetupDiOpenDevRegKey返回HKEY而非BOOL加out参数");
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                bool platform = false;
                try { SetupApiDeviceSource.Scan(CancellationToken.None, null); }
                catch (PlatformNotSupportedException) { platform = true; }
                Check(platform, "非Windows明确报告PlatformNotSupportedException");
            }
            Console.WriteLine("枚举测试完成：" + passed + " 项全部通过。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using wumgr;
class InventoryTests
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("通过：" + name); }
    static LocalDeviceInfo D(string id, string name, string cls) { return new LocalDeviceInfo { InstanceId = id, Name = name, Class = cls, InstalledVersion = "1.2.3.4", InstalledProvider = "Vendor", InstalledDate = "2024-01-02" }; }
    static int Main() {
        try {
            var common = new[] {
                D("PCI\\GPU", "NVIDIA GeForce", "Display"), D("PCI\\NET", "Intel Wi-Fi", "Net"),
                D("HDAUDIO\\1", "Realtek Audio", "MEDIA"), D("USB\\BT", "Intel Wireless Bluetooth", "Bluetooth"),
                D("SCSI\\SSD", "Samsung SSD", "DiskDrive"), D("PCI\\NVME", "Standard NVM Express Controller", "SCSIAdapter"),
                D("HID\\KEY", "HID Keyboard Device", "Keyboard"), D("HID\\MOUSE", "HID-compliant mouse", "Mouse"),
                D("DISPLAY\\1", "Generic PnP Monitor", "Monitor"), D("USB\\CAM", "Integrated Camera", "Camera"),
                D("USBPRINT\\1", "HP Printer", "Printer"), D("PCI\\SMBUS", "Intel SMBus Controller", "System"),
                D("ACPI\\CPU0", "Intel Core CPU", "Processor"), D("PCI\\USB", "USB xHCI Host Controller", "USB"),
                D("HID\\TP", "HID-compliant touchpad", "HIDClass") };
            Check(CommonDeviceFilter.Select(common).Count == common.Length, "显卡网卡声卡蓝牙存储键鼠显示器摄像头打印机芯片组处理器USB控制器触摸板无更新均保留");
            var noise = new[] {
                D("SWD\\MMDEVAPI", "Speakers", "AudioEndpoint"), D("ROOT\\NET\\1", "Adapter", "Net"),
                D("PCI\\1", "WAN Miniport (IP)", "Net"), D("ROOT\\1", "Hyper-V Virtual Ethernet", "Net"),
                D("USB\\HUB", "USB Root Hub", "USB"), D("USB\\COMPOSITE", "USB Composite Device", "USB"),
                D("PCI\\BRIDGE", "PCI Express Root Port", "System"), D("ACPI\\1", "ACPI Timer", "System"),
                D("BTHENUM\\1", "Bluetooth Peripheral", "Bluetooth"), D("USB\\ENUM", "Microsoft Bluetooth Enumerator", "Bluetooth"),
                D("HID\\1", "HID-compliant vendor-defined device", "HIDClass"), D("ACPI\\UNK", "Unknown device", "Unknown"),
                D("VMBUS\\NET", "Ethernet", "Net"), D("ROOT\\VPN", "WireGuard Tunnel", "Net") };
            Check(CommonDeviceFilter.Select(noise).Count == 0, "虚拟设备端点协议节点集线器PCI桥ACPI杂项和未知设备不挤占主界面");
            var all = common.Concat(noise).Concat(new[] { common[0], D("ACPI\\CPU1", "Intel Core CPU", "Processor"), D("HID\\MOUSE2", "HID-compliant mouse", "Mouse") }).ToList();
            var entries = DeviceInventory.BuildEntries(all, new DriverInfo[0]);
            Check(entries.Count == common.Length + 1, "重复实例和逻辑CPU折叠，同名真实鼠标保留");
            Check(entries.All(x => x.Latest == null && x.Versions.Count == 0 && x.LocalDevice.InstalledVersion == "1.2.3.4"), "零更新不虚构候选且保留当前驱动");
            Check(entries.All(x => x.LocalDevice.InstalledProvider == "Vendor" && x.LocalDevice.InstalledDate == "2024-01-02"), "归并保留已安装驱动提供商和日期");
            var update = new DriverInfo { Id = "update", HardwareId = "PCI\\GPU", Version = "2.0", DriverClass = "Display" };
            common[0].HardwareIds.Add("PCI\\GPU");
            entries = DeviceInventory.BuildEntries(all, new[] { update });
            Check(entries.Count == common.Length + 1 && entries.Count(x => x.Latest != null) == 1, "单项候选不隐藏无候选常见硬件");
            Check(DeviceDiscovery.MergeDevices(all, null).Count > entries.Count, "完整枚举仍保留隐藏设备供设备搜索使用");
            var guidOnly = D("SCSI\\G", "SSD", ""); guidOnly.ClassGuid = "{4d36e967-e325-11ce-bfc1-08002be10318}";
            Check(CommonDeviceFilter.IsCommon(guidOnly), "常见存储设备支持安装类GUID回退");
            Console.WriteLine("常见清单测试完成：" + count + " 项全部通过。"); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

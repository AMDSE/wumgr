using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Windows.Forms;
using wumgr;

class UiTests
{
    static int count;
    static void Check(bool ok, string message) { if (!ok) throw new Exception("失败：" + message); count++; Console.WriteLine("通过：" + message); }
    static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
    static void Refresh(object form) { form.GetType().GetMethod("RefreshRows", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null); Application.DoEvents(); }
    [STAThread]
    public static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            using (var f = new DriverForm(true))
            {
                f.Show(); Application.DoEvents();
                var list = Field<FlowLayoutPanel>(f, "list"); var search = Field<TextBox>(f, "search");
                Field<Button>(f, "languageEn").PerformClick(); Application.DoEvents(); Check(UiLanguageService.Current == UiLanguage.EnUS && f.Text.StartsWith("Driver Center"), "English switch applies immediately");
                Field<Button>(f, "languageZh").PerformClick(); Application.DoEvents(); Check(UiLanguageService.Current == UiLanguage.ZhCN && f.Text.StartsWith("驱动下载中心"), "Chinese switch restores immediately");
                Func<List<Control>> cards = () => list.Controls.Cast<Control>().ToList();
                Check(cards().Count == 6, "演示数据包含三张候选卡和三张无更新设备卡");
                Check(cards().Any(c => c.Height > 250), "初始音频设备展开历史版本");
                Check(Field<Label>(f, "selectedLabel").Text.StartsWith("已选 1 项"), "演示初始只选择一个最新版本");
                Check(!Field<CheckBox>(f, "silentInstall").Checked && !Field<CheckBox>(f, "silentInstall").Enabled, "演示模式禁止安装且默认不勾选");
                Check(!Field<Button>(f, "deviceSearch").Enabled && !Field<Button>(f, "autoDownload").Enabled && !Field<Button>(f, "policyButton").Enabled, "演示模式禁止自动搜索和策略修改");
                Check(!Field<Button>(f, "scan").Enabled && !Field<Button>(f, "download").Enabled, "演示模式禁止真实扫描与下载");

                search.Text = "23.60"; Application.DoEvents(); Check(cards().Count == 1 && cards()[0].Text == "", "按驱动版本筛选设备组");
                search.Text = "NVIDIA"; Application.DoEvents(); Check(cards().Count == 1, "按厂商筛选设备组");
                search.Text = "VEN_10EC"; Application.DoEvents(); Check(cards().Count == 1, "按硬件标识筛选设备组");
                search.Text = "6.0.9605.1"; Application.DoEvents(); Check(cards().Count == 1, "历史版本匹配仍显示整个设备组");
                search.Text = "不存在的设备"; Application.DoEvents(); Check(cards().Count == 0 && Field<Label>(f, "empty").Visible, "无匹配项显示中文空状态");

                search.Text = ""; Application.DoEvents();
                var tabs = ((IList)f.GetType().GetField("tabs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f)).Cast<Button>().ToList();
                tabs.First(t => t.Text == "显卡").PerformClick(); Check(cards().Count == 1, "类别页签筛选显卡");
                tabs.First(t => t.Text == "声卡").PerformClick(); Check(cards().Count == 1, "类别页签筛选声卡");
                tabs.First(t => t.Text == "全部设备").PerformClick();

                Field<Button>(f, "selectAll").PerformClick();
                Check(Field<Label>(f, "selectedLabel").Text.StartsWith("已选 3 项"), "全选每个设备当前可下载版本且不重复");
                Field<Button>(f, "clear").PerformClick(); Check(Field<Label>(f, "selectedLabel").Text.StartsWith("已选 0 项"), "清空勾选");

                // Expand/collapse rebuilds cards; always reacquire the new control reference.
                var audio = cards().First(c => c.Height > 250); var historyButton = (Button)audio.Controls.Cast<Control>().First(c => c.Text.StartsWith("收起版本")); historyButton.PerformClick(); Application.DoEvents();
                Check(cards().First(c => c.Controls.Cast<Control>().Any(x => x.Text.StartsWith("历史版本"))).Height < 200, "折叠历史版本后卡片重建");
                var collapsed = cards().First(c => c.Controls.Cast<Control>().Any(x => x.Text.StartsWith("历史版本"))); ((Button)collapsed.Controls.Cast<Control>().First(c => c.Text.StartsWith("历史版本"))).PerformClick(); Application.DoEvents();
                var expanded = cards().First(c => c.Height > 250); var radios = expanded.Controls.Cast<Control>().OfType<RadioButton>().ToList();
                Check(radios.Count == 3 && radios.Count(r => r.Checked) == 1, "展开历史版本仅有一个真实版本单选");
                Field<Button>(f, "selectAll").PerformClick();
                var selected = Field<HashSet<string>>(f, "selected");
                Check(selected.Count == 3 && selected.Contains("demo-audio-1"), "全选后音频当前版本在选中集合中");
                radios.First(r => r.AccessibleName.Contains("6.0.9657.1")).PerformClick(); Application.DoEvents();
                Check(Field<Label>(f, "selectedLabel").Text.StartsWith("已选 3 项") && selected.Count == 3 && selected.Contains("demo-audio-2") && !selected.Contains("demo-audio-1"), "切换历史版本替换原选中ID且设备总数仍为三");
                var collapsedAfterChoice = cards().First(c => c.Controls.Cast<Control>().Any(x => x.Text.StartsWith("收起版本"))); ((Button)collapsedAfterChoice.Controls.Cast<Control>().First(c => c.Text.StartsWith("收起版本"))).PerformClick(); Application.DoEvents();
                search.Text = "Realtek"; Application.DoEvents(); search.Text = ""; Application.DoEvents();
                var restored = cards().First(c => c.Controls.Cast<Control>().Any(x => x.Text.StartsWith("历史版本")));
                var restoredChoice = (DriverInfo)restored.GetType().GetField("choice", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(restored);
                Check(restoredChoice.Version == "6.0.9657.1", "切换历史版本后折叠、搜索重建仍保留choice");

                Field<Button>(f, "clear").PerformClick();
                // Supplementary demo preview: installed-only hardware alongside real-shaped candidate cards.
                Field<HashSet<string>>(f, "expanded").Clear();
                var previewLocal = Field<List<LocalDeviceInfo>>(f, "localDevices");
                var btPreview = previewLocal.First(x => x.Class == "Bluetooth"); previewLocal.Remove(btPreview); previewLocal.Insert(1, btPreview);
                Refresh(f); Application.DoEvents();
                System.IO.Directory.CreateDirectory("docs/images");
                f.SavePreview(System.IO.Path.GetFullPath("docs/images/device-inventory-zh-CN.png"));
                Field<Button>(f, "languageEn").PerformClick(); Application.DoEvents();
                f.SavePreview(System.IO.Path.GetFullPath("docs/images/device-inventory-en-US.png"));
                Field<Button>(f, "languageZh").PerformClick(); Application.DoEvents();
                var drivers = Field<List<DriverInfo>>(f, "drivers"); drivers.Add(new DriverInfo { Id = "no-url", DeviceModel = "无地址的测试设备", Manufacturer = "测试厂商", Provider = "测试厂商", DriverClass = "MEDIA", HardwareId = "PCI\\VEN_TEST&DEV_0001", Version = "1.0.0.0", Title = "测试驱动", DownloadUrls = new List<string>() });
                Field<List<LocalDeviceInfo>>(f, "localDevices").Add(new LocalDeviceInfo { InstanceId = "TEST_NO_URL", Name = "无地址的测试设备", Class = "MEDIA", HardwareIds = new List<string> { "PCI\\VEN_TEST&DEV_0001" } });
                Refresh(f); search.Text = "无地址"; Application.DoEvents();
                var noUrl = cards().Single(); var noUrlCheck = noUrl.Controls.Cast<Control>().OfType<CheckBox>().Single(); Check(!noUrlCheck.Enabled, "无下载地址的设备复选框禁用");
                ((Button)noUrl.Controls.Cast<Control>().First(c => c.Text.StartsWith("历史版本") || c.Text.StartsWith("查看版本"))).PerformClick(); Application.DoEvents();
                var noUrlRadio = cards().Single().Controls.Cast<Control>().OfType<RadioButton>().Single(); Check(!noUrlRadio.Enabled, "无下载地址的历史版本单选禁用");
                Field<Button>(f, "selectAll").PerformClick(); Check(Field<Label>(f, "selectedLabel").Text.StartsWith("已选 0 项"), "全选不勾选无下载地址设备");
                search.Text = ""; Application.DoEvents(); f.ClientSize = new Size(1050, 760); Application.DoEvents();
                Check(list.Width >= 800 && cards().All(c => c.Width <= list.ClientSize.Width), "最小尺寸下卡片保留可视布局");
                Check(Field<Button>(f, "download").Bottom <= Field<Button>(f, "download").Parent.ClientSize.Height, "底部下载按钮未溢出布局");
                f.Close();
            }
            using (var f = new DriverForm(false))
            {
                Check(Field<FlowLayoutPanel>(f, "list").Controls.Count == 0, "正式模式不注入演示驱动");
                Check(Field<Button>(f, "scan").Enabled && !Field<Button>(f, "download").Enabled, "正式模式初始允许扫描且禁止空下载");
                Check(!Field<CheckBox>(f, "silentInstall").Checked && Field<Button>(f, "deviceSearch").Enabled && Field<Button>(f, "autoDownload").Enabled && Field<Button>(f, "policyButton").Enabled, "默认仅下载且新功能入口可见");
                Field<CheckBox>(f, "silentInstall").Checked = true;
                Check(Field<Button>(f, "download").Text == "下载并静默安装", "安装勾选同步主操作文案");
                f.Show(); Application.DoEvents();
                var local = Field<List<LocalDeviceInfo>>(f, "localDevices");
                local.AddRange(new[] {
                    new LocalDeviceInfo { InstanceId = "GPU", Name = "测试显卡", Class = "Display", HardwareIds = new List<string> { "PCI\\VEN_TEST&DEV_GPU" } },
                    new LocalDeviceInfo { InstanceId = "AUDIO", Name = "测试声卡", Class = "MEDIA" },
                    new LocalDeviceInfo { InstanceId = "NET", Name = "测试网卡", Class = "Net" },
                    new LocalDeviceInfo { InstanceId = "BT", Name = "测试蓝牙", Class = "Bluetooth" },
                    new LocalDeviceInfo { InstanceId = "CHIP", Name = "Intel SMBus Controller", Class = "System" },
                    new LocalDeviceInfo { InstanceId = "UNKNOWN", Name = "未知设备", ProblemCode = 28 }
                });
                Refresh(f);
                var inventoryList = Field<FlowLayoutPanel>(f, "list");
                Check(inventoryList.Controls.Count == 5, "无更新候选时主界面仍列出五类常见设备，不列未知杂项");
                Check(inventoryList.Controls.Cast<Control>().All(c => c.Controls.Cast<Control>().Any(x => x.Text.StartsWith("已安装驱动："))), "无候选设备均有独立已安装驱动信息行");
                Check(Field<Label>(f, "total").Text.StartsWith("5 个常见设备"), "设备计数基于真实清单而不是更新数");
                Field<Button>(f, "selectAll").PerformClick();
                Check(Field<HashSet<string>>(f, "selected").Count == 0, "无候选设备不能全选成假驱动");
                var inventoryTabs = ((IList)f.GetType().GetField("tabs", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f)).Cast<Button>().ToList();
                foreach (string label in new[] { "显卡", "声卡", "网络适配器", "蓝牙", "系统设备" }) {
                    inventoryTabs.First(t => t.Text == label).PerformClick();
                    Check(inventoryList.Controls.Count == 1, label + "类别即使无更新也能查看");
                }
                inventoryTabs.First(t => t.Text == "全部设备").PerformClick();
                Field<List<DriverInfo>>(f, "drivers").Add(new DriverInfo { Id = "gpu-update", DeviceModel = "显卡候选", HardwareId = "PCI\\VEN_TEST&DEV_GPU", DriverClass = "Display", Version = "2.0", DownloadUrls = new List<string> { "https://example.invalid/gpu.cab" } });
                Refresh(f);
                Check(inventoryList.Controls.Count == 5 && inventoryList.Controls.Cast<Control>().Count(c => c.GetType().Name == "DriverCard") == 1, "只有一项更新也不隐藏其余四个常见设备");
                Field<TextBox>(f, "search").Text = "测试蓝牙"; Application.DoEvents();
                Check(inventoryList.Controls.Count == 1, "无候选设备也支持名称搜索");
                Field<TextBox>(f, "search").Text = "";
                Field<Button>(f, "languageEn").PerformClick(); Application.DoEvents();
                Check(inventoryList.Controls.Count == 5 && inventoryList.Controls.Cast<Control>().Where(c => c.GetType().Name == "LocalDeviceCard").All(c => c.Controls.Cast<Control>().Any(x => x.Text.StartsWith("Installed driver:"))), "常见设备已安装信息支持英文切换");
                Field<Button>(f, "languageZh").PerformClick();
                f.Close();

            }
            using (var picker = new DeviceSearchForm(new List<LocalDeviceInfo> { new LocalDeviceInfo { Name = "测试网卡", HardwareIds = new List<string> { "PCI\\VEN_TEST" }, InstalledVersion = "1.0" } }, new List<DriverInfo> { new DriverInfo { Id = "test", DeviceModel = "测试网卡", HardwareId = "pci\\ven_test", Version = "2.0", DownloadUrls = new List<string> { "https://example.invalid/test.cab" } } }))
            {
                picker.Show(); Application.DoEvents();
                Check(Field<DataGridView>(picker, "deviceGrid").Rows.Count == 1 && Field<DataGridView>(picker, "driverGrid").Rows.Count == 1, "设备搜索窗口显示本机设备与精确匹配驱动");
                Field<TextBox>(picker, "query").Text = "不存在"; Application.DoEvents();
                Check(Field<DataGridView>(picker, "deviceGrid").Rows.Count == 0 && !Field<Button>(picker, "download").Enabled, "设备搜索无匹配时禁止下载"); picker.Close();
            }
            var asm = typeof(DriverForm).Assembly;
            Check(asm.GetType("wumgr.UpdateInstaller") == null && asm.GetType("wumgr.WuAgent") == null && asm.GetType("wumgr.Program") == null, "旧安装器及旧入口未编译进入程序");
            Console.WriteLine("界面测试完成：" + count + " 项全部通过。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using wumgr;

class LayoutTests
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; Console.WriteLine("通过：" + message); }
    static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
    static IEnumerable<Control> All(Control c) { foreach (Control child in c.Controls) { yield return child; foreach (var x in All(child)) yield return x; } }
    static Rectangle ScreenBounds(Control c) { return new Rectangle(c.PointToScreen(Point.Empty), c.Size); }
    static void Key(DriverForm f, Keys keys)
    {
        var args = new object[] { new Message(), keys };
        typeof(DriverForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, args);
        Application.DoEvents();
    }
    [STAThread]
    static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            using (var f = new DriverForm(true))
            {
                f.Show(); Application.DoEvents();
                var list = Field<FlowLayoutPanel>(f, "list"); var search = Field<TextBox>(f, "search");
                var snapshot = Field<List<DriverDevice>>(f, "devices");
                search.Text = "Intel"; Application.DoEvents(); search.Clear(); Application.DoEvents();
                Check(Object.ReferenceEquals(snapshot, Field<List<DriverDevice>>(f, "devices")), "搜索复用已匹配设备快照，不重新匹配硬件");
                var tabs = Field<IList>(f, "tabs").Cast<Button>().ToList();
                tabs[1].PerformClick(); tabs[0].PerformClick();
                Check(Object.ReferenceEquals(snapshot, Field<List<DriverDevice>>(f, "devices")), "分类切换不重建硬件匹配结果");
                Key(f, Keys.Control | Keys.F); Check(search.Focused, "Ctrl+F 聚焦搜索");
                search.Text = "test"; Key(f, Keys.Escape); Check(search.Text == "", "搜索框 Escape 清空筛选");
                Key(f, Keys.F5); Check(Field<Label>(f, "status").Text.Contains("演示"), "演示模式 F5 不触发系统扫描");
                foreach (UiLanguage lang in new[] { UiLanguage.ZhCN, UiLanguage.EnUS })
                {
                    Field<Button>(f, lang == UiLanguage.ZhCN ? "languageZh" : "languageEn").PerformClick(); Application.DoEvents();
                    foreach (Size size in new[] { new Size(1024, 680), new Size(1280, 860), new Size(1380, 1000) })
                    {
                        f.ClientSize = size; Application.DoEvents();
                        string context = lang + " " + size;
                        var bounds = new Rectangle(f.PointToScreen(Point.Empty), f.ClientSize);
                        var controls = new[] { "scan", "download", "cancel", "browse", "open", "history", "silentInstall", "status", "progress", "search", "languageZh", "languageEn", "deviceSearch", "policyButton" }.Select(name => Field<Control>(f, name)).ToList();
                        Check(controls.All(c => c.Width > 0 && c.Height > 0 && bounds.Contains(ScreenBounds(c))), context + "：主要操作全部在客户区内");
                        Check(!ScreenBounds(Field<Button>(f, "scan")).IntersectsWith(ScreenBounds(Field<Label>(f, "summary"))), context + "：扫描按钮与摘要不重叠");
                        Check(!ScreenBounds(Field<Label>(f, "selectedLabel")).IntersectsWith(ScreenBounds(Field<Button>(f, "cancel"))), context + "：选择摘要与操作按钮不重叠");
                        Check(list.Width > 700 && list.Height >= 200 && list.Controls.Cast<Control>().All(c => c.Width <= list.ClientSize.Width), context + "：设备列表有可用空间且卡片不横向溢出");
                        var local = list.Controls.Cast<Control>().First(c => c.GetType().Name == "LocalDeviceCard");
                        Check(local.Controls.Cast<Control>().All(c => c.Left >= 0 && c.Top >= 0 && c.Right <= local.ClientSize.Width && c.Bottom <= local.ClientSize.Height), context + "：无候选设备信息与详情按钮不溢出");
                        var brand = All(f).First(c => c.Text == "WuMgr");
                        Check(brand.Height >= 30 && brand.Width > 80 && bounds.Contains(ScreenBounds(brand)), context + "：侧栏品牌标题完整显示");
                    }
                    f.ClientSize = new Size(1280, 860); Field<HashSet<string>>(f, "expanded").Clear();
                    typeof(DriverForm).GetMethod("RenderRows", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null); Application.DoEvents();
                    f.SavePreview(System.IO.Path.GetFullPath("artifacts/v10-workspace-" + (lang == UiLanguage.ZhCN ? "zh-CN" : "en-US") + ".png"));
                }
                f.Close();
            }
            UiLanguageService.Set(UiLanguage.ZhCN, false);
            using (var f = new DriverForm(true))
            {
                f.Show(); Application.DoEvents();
                var local = Field<List<LocalDeviceInfo>>(f, "localDevices"); local.Clear();
                var candidates = Field<List<DriverInfo>>(f, "drivers"); candidates.Clear();
                Field<HashSet<string>>(f, "selected").Clear(); Field<HashSet<string>>(f, "expanded").Clear();
                local.Add(new LocalDeviceInfo { Name = "Anonymous Audio", InstanceId = "ANON_AUDIO", Class = "MEDIA", HardwareIds = new List<string> { "PCI\\VEN_ANON&DEV_AUDIO" } });
                local.Add(new LocalDeviceInfo { Name = "Anonymous GPU", InstanceId = "ANON_GPU", Class = "Display", HardwareIds = new List<string> { "PCI\\VEN_ANON&DEV_GPU" } });
                candidates.Add(new DriverInfo { Id = "", DeviceModel = "Anonymous Audio", HardwareId = "PCI\\VEN_ANON&DEV_AUDIO", DriverClass = "MEDIA", Version = "2.0", DownloadUrls = new List<string> { "https://example.invalid/audio2.cab" } });
                candidates.Add(new DriverInfo { Id = "", DeviceModel = "Anonymous Audio", HardwareId = "PCI\\VEN_ANON&DEV_AUDIO", DriverClass = "MEDIA", Version = "1.0", DownloadUrls = new List<string> { "https://example.invalid/audio1.cab" } });
                candidates.Add(new DriverInfo { Id = null, DeviceModel = "Anonymous GPU", HardwareId = "PCI\\VEN_ANON&DEV_GPU", DriverClass = "Display", Version = "3.0", DownloadUrls = new List<string> { "https://example.invalid/gpu.cab" } });
                typeof(DriverForm).GetMethod("RefreshRows", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null); Application.DoEvents();
                var list = Field<FlowLayoutPanel>(f, "list");
                Func<Control> audio = () => list.Controls.Cast<Control>().First(c => c.AccessibleName.StartsWith("Anonymous Audio"));
                audio().Controls.OfType<CheckBox>().Single().Checked = true;
                Func<List<DriverInfo>> picked = () => (List<DriverInfo>)typeof(DriverForm).GetMethod("SelectedDrivers", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
                Check(picked().Count == 1 && picked()[0].Version == "2.0", "空更新ID候选勾选不串选其他设备或历史版本");
                Check(Field<Label>(f, "total").Text.Contains("3 个候选版本"), "空更新ID候选分别计数，不合成一个候选");
                audio().Controls.OfType<Button>().First(b => b.Text.StartsWith("历史版本")).PerformClick(); Application.DoEvents();
                var radios = audio().Controls.OfType<RadioButton>().ToList();
                Check(radios.Count == 2 && radios.Count(r => r.Checked) == 1, "空更新ID历史单选使用对象身份，仅选中一行");
                radios.First(r => r.AccessibleName.EndsWith("1.0")).PerformClick(); Application.DoEvents();
                Check(picked().Count == 1 && picked()[0].Version == "1.0", "空更新ID切换历史版本只替换本设备选择");
                Field<TextBox>(f, "search").Text = "GPU"; Application.DoEvents(); Field<TextBox>(f, "search").Clear();
                Field<Button>(f, "languageEn").PerformClick(); Application.DoEvents();
                Check(picked().Count == 1 && picked()[0].Version == "1.0", "匿名选择在搜索和语言切换后保留");
                Field<Label>(f, "status").Text = "正在发现本机设备并扫描适用驱动，请稍候…";
                Check(Field<Label>(f, "status").Text.StartsWith("Discovering local devices"), "英文扫描开始状态完整翻译");
                Check(candidates.Take(2).All(d => d.Id == "") && candidates[2].Id == null, "界面临时选择键不篡改原始WUA更新ID");
                f.Close();
            }
            Console.WriteLine("布局测试完成：" + count + " 项全部通过。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { UiLanguageService.Set(UiLanguage.ZhCN, false); }
    }
}

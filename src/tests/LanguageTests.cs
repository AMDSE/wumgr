using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using wumgr;

class LanguageTests
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; Console.WriteLine("通过：" + message); }
    static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o); }
    static IEnumerable<Control> Descendants(Control c) { foreach (Control child in c.Controls) { yield return child; foreach (var x in Descendants(child)) yield return x; } }
    static bool Chinese(string s) { return Regex.IsMatch(s ?? "", "[\\u4e00-\\u9fff]"); }
    [STAThread]
    static int Main()
    {
        string old = Environment.GetEnvironmentVariable("WUMGR_CONFIG_DIR");
        string temp = Path.Combine(Path.GetTempPath(), "wumgr-language-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WUMGR_CONFIG_DIR", temp);
        try
        {
            Application.EnableVisualStyles();
            Check(UiLanguageService.Load() == UiLanguage.ZhCN, "首次无偏好默认中文");
            UiLanguageService.Set(UiLanguage.EnUS, false);
            Check(!File.Exists(UiLanguageService.ConfigPath), "本次语言覆盖不写入偏好");
            UiLanguageService.Set(UiLanguage.EnUS, true);
            Check(UiLanguageService.Load() == UiLanguage.EnUS, "英文偏好落盘后可重新加载");
            UiLanguageService.Set(UiLanguage.ZhCN, false);
            Check(UiLanguageService.Load() == UiLanguage.EnUS, "临时覆盖不替换已保存偏好");
            UiLanguageService.Set(UiLanguage.ZhCN, true);
            Check(UiLanguageService.Load(UiLanguage.EnUS) == UiLanguage.ZhCN, "中文偏好显式恢复不受英文fallback影响");
            using (var f = new DriverForm(true))
            {
                f.Show(); Application.DoEvents();
                var list = Field<FlowLayoutPanel>(f, "list");
                var audio = list.Controls.Cast<Control>().First(c => c.Height > 250);
                var radios = audio.Controls.OfType<RadioButton>().ToList();
                radios[1].PerformClick(); Application.DoEvents();
                var tabs = Field<IList>(f, "tabs").Cast<Button>().ToList();
                tabs.First(t => (string)t.Tag == "声卡").PerformClick();
                Field<TextBox>(f, "search").Text = "Realtek";
                Field<TextBox>(f, "folder").Text = "Network";
                Field<CheckBox>(f, "silentInstall").Checked = true;
                Field<CheckBox>(f, "history").Checked = false;
                var selected = new HashSet<string>(Field<HashSet<string>>(f, "selected"));
                var choices = new Dictionary<string,string>(Field<Dictionary<string,string>>(f, "choices"));
                var expanded = new HashSet<string>(Field<HashSet<string>>(f, "expanded"));
                Field<Button>(f, "languageEn").PerformClick(); Application.DoEvents();
                Check(f.Text == "Driver Center · WuMgr", "标题即时变英文");
                Check(Field<Label>(f, "summary").Text == "6 common devices, 6 candidate versions", "动态设备摘要为英文");
                Check(Field<Label>(f, "subtitle").Text.StartsWith("Demo data"), "演示安全提示为英文");
                Check(Field<Label>(f, "total").Text.Contains("Showing 1"), "筛选统计为英文");
                Check(Field<Label>(f, "selectedLabel").Text.StartsWith("Selected 1"), "选择统计为英文");
                Check(Field<Button>(f, "download").Text == "Download & silently install", "安装选项同步英文主按钮");
                Check(Field<string>(f, "category") == "声卡" && list.Controls.Count == 1, "语言切换保留业务分类键与可见设备");
                Check(Field<TextBox>(f, "search").Text == "Realtek" && Field<TextBox>(f, "folder").Text == "Network", "语言切换绝不翻译搜索词或目录文本");
                Check(!Field<CheckBox>(f, "history").Checked && Field<CheckBox>(f, "silentInstall").Checked, "保留历史和静默安装选项");
                Check(selected.SetEquals(Field<HashSet<string>>(f, "selected")) && choices.All(p => Field<Dictionary<string,string>>(f, "choices")[p.Key] == p.Value) && expanded.SetEquals(Field<HashSet<string>>(f, "expanded")), "保留勾选、历史版本选择与展开状态");
                Check(list.Controls[0].Controls.OfType<Button>().Any(b => b.Text.StartsWith("Collapse versions")) && list.Controls[0].Controls.OfType<Button>().Any(b => b.Text == "Details"), "动态重建卡片的按钮为英文");
                Check(!Field<Button>(f, "scan").Enabled && !Field<Button>(f, "download").Enabled && !Field<Button>(f, "policyButton").Enabled, "英文演示模式仍禁止真实系统操作");
                var status = Field<Label>(f, "status");
                status.Text = "扫描失败，可重试";
                Check(status.Text == "Scan failed; try again", "后续运行状态在显示层翻译");
                Field<Button>(f, "languageZh").PerformClick(); Application.DoEvents();
                Check(status.Text == "扫描失败，可重试", "切回中文还原运行状态源文本而非伪报就绪");
                Check(Field<Button>(f, "download").Text == "下载并静默安装" && Field<TextBox>(f, "folder").Text == "Network", "往返切换恢复中文且不改变用户路径");
                Field<TextBox>(f, "search").Text = "网络适配器";
                Field<Button>(f, "languageEn").PerformClick();
                Check(Field<TextBox>(f, "search").Text == "网络适配器", "中文搜索词也不会被自动翻译");
                Field<TextBox>(f, "search").Text = "";
                tabs.First(t => (string)t.Tag == "全部设备").PerformClick(); Application.DoEvents();
                var residual = Descendants(f).Where(c => !(c is TextBoxBase) && c.Text != "简体中文" && Chinese(c.Text)).Select(c => c.GetType().Name + ": " + c.Text).ToArray();
                Check(residual.Length == 0, "英文可见控件无应用自有中文残留：" + String.Join("; ", residual));
                f.Close();
            }
            UiLanguageService.Set(UiLanguage.EnUS, false);
            var local = new LocalDeviceInfo { Name = "OEM test", HardwareIds = new List<string> { "PCI\\VEN_TEST" }, InstalledVersion = "1.0", ProblemCode = 0 };
            var driver = new DriverInfo { Id = "test", DeviceModel = "OEM test", HardwareId = "PCI\\VEN_TEST", Version = "2.0", Status = "待下载", DownloadUrls = new List<string> { "https://example.invalid/test.cab" } };
            using (var picker = new DeviceSearchForm(new List<LocalDeviceInfo> { local }, new List<DriverInfo> { driver }))
            {
                picker.Show(); Application.DoEvents();
                var dg = Field<DataGridView>(picker, "deviceGrid"); var cg = Field<DataGridView>(picker, "driverGrid");
                Check(picker.Text.StartsWith("Local device search") && dg.Columns[0].HeaderText == "Device" && cg.Columns[4].HeaderText == "Status", "英文设备搜索窗口标题和表头");
                Check(Field<Label>(picker, "details").Text.Contains("candidates · Installed version"), "英文设备搜索动态详情");
                Check(Convert.ToString(dg.Rows[0].Cells[3].FormattedValue) == "Normal" && Convert.ToString(cg.Rows[0].Cells[4].FormattedValue) == "Pending download", "设备和驱动状态只在表格显示边界翻译");
                Check(driver.Status == "待下载" && local.DisplayStatus == "正常", "翻译不修改业务原始状态");
                Field<TextBox>(picker, "query").Text = "missing"; Application.DoEvents();
                Check(Field<Label>(picker, "details").Text == "No matching local device" && !Field<Button>(picker, "download").Enabled, "英文设备搜索空状态与禁用下载");
                picker.Close();
            }
            Check(!Chinese(UiLanguageService.Get("任务仍在进行。取消任务后关闭窗口？系统扫描或安装需要等待当前调用返回；已完成安装不会回滚。")), "退出风险确认有完整英文");
            Check(!Chinese(UiLanguageService.Get("静默安装需要管理员权限。请关闭程序，右键选择“以管理员身份运行”，再重试；仅下载无需管理员权限。")), "管理员权限提示有完整英文");
            Check(UiLanguageService.Display("已安装 · 需重启").ToLowerInvariant().Contains("restart"), "安装重启状态有英文显示");
            Console.WriteLine("语言测试完成：" + count + " 项全部通过。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            UiLanguageService.Set(UiLanguage.ZhCN, false);
            Environment.SetEnvironmentVariable("WUMGR_CONFIG_DIR", old);
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
}

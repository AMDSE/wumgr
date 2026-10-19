using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace wumgr
{
    internal static class DriverTheme
    {
        public static readonly Color Ink = Color.FromArgb(28, 44, 49), Muted = Color.FromArgb(115, 130, 134), Accent = Color.FromArgb(17, 151, 116), Line = Color.FromArgb(230, 237, 235), Pale = Color.FromArgb(235, 248, 242), Canvas = Color.FromArgb(245, 248, 247);
        public static readonly string FontName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "Microsoft YaHei UI" : "Noto Sans CJK SC";
        public static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font(FontName, size, style); }
        public static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular) { return new Label { Text = text, Font = Font(size, style), ForeColor = color, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, BackColor = Color.Transparent }; }
        public static string Size(decimal bytes) { return bytes <= 0 ? UiLanguageService.Get("大小未提供") : bytes >= 1073741824M ? (bytes / 1073741824M).ToString("0.00") + " GB" : (bytes / 1048576M).ToString("0.1") + " MB"; }
        public static string Date(DriverInfo d) { return d.DriverDate == DateTime.MinValue ? UiLanguageService.Get("日期未提供") : d.DriverDate.ToString("yyyy-MM-dd"); }
        public static string Category(DriverInfo d)
        {
            return DeviceClassification.Category(d == null ? null : d.DriverClass);
        }
        public static string Category(DriverDevice device)
        {
            return device.LocalDevice == null ? Category(device.Latest) : device.LocalDevice.Category;
        }
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        public static void Text(Graphics g, string text, Font f, Color color, Rectangle r, TextFormatFlags flags = TextFormatFlags.Left) { TextRenderer.DrawText(g, text, f, r, color, flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix); }
    }

    internal sealed class SoftButton : Button
    {
        public bool Primary, Active;
        bool hover;
        public SoftButton(string text, bool primary = false) { Text = text; Primary = primary; Font = DriverTheme.Font(9); Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); Height = 38; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent == null ? DriverTheme.Canvas : Parent.BackColor);
            Color bg = !Enabled ? Color.FromArgb(236, 240, 238) : Primary ? (hover ? Color.FromArgb(12, 131, 100) : DriverTheme.Accent) : Active || hover ? DriverTheme.Pale : Color.White;
            using (var path = DriverTheme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 7)) { using (var b = new SolidBrush(bg)) g.FillPath(b, path); if (!Primary && !Active) using (var pen = new Pen(DriverTheme.Line)) g.DrawPath(pen, path); }
            DriverTheme.Text(g, Text, Font, !Enabled ? Color.FromArgb(153, 165, 160) : Primary ? Color.White : Active ? DriverTheme.Accent : DriverTheme.Ink, ClientRectangle, TextFormatFlags.HorizontalCenter);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 9, Height - 9));
        }
    }
    internal class SoftPanel : Panel
    {
        public SoftPanel() { BackColor = Color.White; DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using (var p = DriverTheme.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12)) using (var pen = new Pen(DriverTheme.Line)) e.Graphics.DrawPath(pen, p); }
    }

    public sealed class DriverForm : Form
    {
        readonly DriverService service = new DriverService();
        readonly List<DriverInfo> drivers = new List<DriverInfo>();
        readonly HashSet<string> selected = new HashSet<string>();
        readonly Dictionary<string, string> choices = new Dictionary<string, string>();
        readonly HashSet<string> expanded = new HashSet<string>();
        readonly FlowLayoutPanel list = new FlowLayoutPanel();
        readonly TextBox search = new TextBox(), folder = new TextBox();
        readonly Label total = new Label(), selectedLabel = new Label(), empty = new Label(), summary = new Label(), subtitle = new Label();
        readonly LocalizedStatusLabel status = new LocalizedStatusLabel();
        readonly ProgressBar progress = new ProgressBar();
        readonly SoftButton scan, download, cancel, browse, open, selectAll, clear;
        readonly CheckBox history = new CheckBox(), silentInstall = new CheckBox();
        readonly SoftButton deviceSearch = new SoftButton(UiLanguageService.Get("设备搜索")), autoDownload = new SoftButton(UiLanguageService.Get("自动搜索下载"));
        readonly SoftButton policyButton = new SoftButton(UiLanguageService.Get("驱动更新保护"));
        readonly SoftButton languageZh = new SoftButton(UiLanguageService.Get("简体中文")), languageEn = new SoftButton("English");
        readonly DriverUpdatePolicy updatePolicy = new DriverUpdatePolicy();
        readonly DeviceDiscovery discovery = new DeviceDiscovery();
        readonly DriverInstaller installer = new DriverInstaller();
        readonly List<LocalDeviceInfo> localDevices = new List<LocalDeviceInfo>();
        bool installing, rebootRequired;
        readonly List<SoftButton> tabs = new List<SoftButton>();
        readonly bool demo;
        CancellationTokenSource operation;
        bool closeAfterCancel;
        string configPath, category = "全部设备";
        List<DriverDevice> devices = new List<DriverDevice>();
        public DriverForm(bool demoMode)
        {
            demo = demoMode; Text = UiLanguageService.Get("驱动下载中心 · WuMgr"); Font = DriverTheme.Font(9); ForeColor = DriverTheme.Ink; BackColor = DriverTheme.Canvas;
            ClientSize = new Size(1200, 940); MinimumSize = new Size(1050, 760); StartPosition = FormStartPosition.CenterScreen; AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(30, 0, 30, 18), Margin = Padding.Empty };
            foreach (int h in new[] { 76, 116, 132, 0, 154 }) root.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, h)); Controls.Add(root);
            var header = new Panel { Size = new Size(1140, 86), Dock = DockStyle.Fill, Margin = Padding.Empty };
            var logo = new Label { Text = "↓", Font = DriverTheme.Font(20, FontStyle.Bold), BackColor = DriverTheme.Accent, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(0, 22), Size = new Size(44, 44) }; header.Controls.Add(logo);
            var name = DriverTheme.Label(UiLanguageService.Get("驱动下载中心"), 19, DriverTheme.Ink, FontStyle.Bold); name.SetBounds(58, 10, 500, 44); header.Controls.Add(name);
            var desc = DriverTheme.Label(UiLanguageService.Get("WuMgr  /  让驱动下载，更简单"), 8.5F, DriverTheme.Muted); desc.SetBounds(59, 49, 500, 21); header.Controls.Add(desc);
            var about = new SoftButton(UiLanguageService.Get("关于与许可")); about.Anchor = AnchorStyles.Top | AnchorStyles.Right; about.SetBounds(1018, 27, 122, 34); header.Controls.Add(about); about.Click += (s, e) => ShowLicense();
            languageEn.Anchor = AnchorStyles.Top | AnchorStyles.Right; languageEn.SetBounds(905, 27, 103, 34); header.Controls.Add(languageEn); languageZh.Anchor = AnchorStyles.Top | AnchorStyles.Right; languageZh.SetBounds(790, 27, 110, 34); header.Controls.Add(languageZh);
            languageZh.Click += (s, e) => SetLanguage(UiLanguage.ZhCN); languageEn.Click += (s, e) => SetLanguage(UiLanguage.EnUS);
            root.Controls.Add(header, 0, 0);
            var hero = new SoftPanel { Size = new Size(1140, 116), Dock = DockStyle.Fill, BackColor = DriverTheme.Pale, Margin = new Padding(0, 0, 0, 10) };
            var safe = DriverTheme.Label(UiLanguageService.Get("设备自动发现 · 可选静默安装"), 9, DriverTheme.Accent, FontStyle.Bold); safe.SetBounds(24, 13, 780, 25); hero.Controls.Add(safe);
            summary.Font = DriverTheme.Font(19, FontStyle.Bold); summary.ForeColor = DriverTheme.Ink; summary.SetBounds(23, 33, 780, 44); summary.Text = UiLanguageService.Get("为你的设备，找到适用驱动"); summary.AutoEllipsis = true; summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; hero.Controls.Add(summary);
            subtitle.Font = DriverTheme.Font(9); subtitle.ForeColor = DriverTheme.Muted; subtitle.SetBounds(25, 76, 790, 26); subtitle.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; subtitle.Text = UiLanguageService.Get("自动发现本机设备、匹配适用驱动；可选择下载完成后后台静默安装。"); hero.Controls.Add(subtitle);
            scan = new SoftButton(UiLanguageService.Get("扫描设备驱动"), true) { Anchor = AnchorStyles.Top | AnchorStyles.Right }; scan.SetBounds(926, 35, 184, 44); hero.Controls.Add(scan); root.Controls.Add(hero, 0, 1);
            var tools = new Panel { Size = new Size(1140, 102), Dock = DockStyle.Fill, Margin = Padding.Empty };
            string[] names = { "全部设备", "显卡", "网络适配器", "声卡", "蓝牙", "系统设备", "其他设备" }; int left = 0;
            foreach (string n in names) { var tab = new SoftButton(n) { Active = n == category, Tag = n }; int w = n == "全部设备" || n == "网络适配器" || n == "其他设备" ? 108 : 72; tab.SetBounds(left, 6, w, 35); left += w + 7; tabs.Add(tab); tools.Controls.Add(tab); tab.Click += (s, e) => { category = (string)tab.Tag; foreach (var b in tabs) { b.Active = (string)b.Tag == category; b.Invalidate(); } RefreshRows(); }; }
            var searchHost = new SoftPanel { Anchor = AnchorStyles.Top | AnchorStyles.Right }; searchHost.SetBounds(806, 6, 334, 36); tools.Controls.Add(searchHost);
            var searchIcon = DriverTheme.Label(UiLanguageService.Get("查找"), 9, DriverTheme.Muted); searchIcon.SetBounds(10, 3, 43, 29); searchHost.Controls.Add(searchIcon);
            search.BorderStyle = BorderStyle.None; search.SetBounds(59, 8, 259, 24); search.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; search.AccessibleName = UiLanguageService.Get("搜索设备型号、驱动号、厂商或硬件标识"); searchHost.Controls.Add(search);
            total.Font = DriverTheme.Font(9); total.ForeColor = DriverTheme.Muted; total.SetBounds(0, 57, 360, 28); tools.Controls.Add(total);
            history.Text = UiLanguageService.Get("扫描时包含历史版本"); history.Checked = true; history.AutoSize = true; history.ForeColor = DriverTheme.Muted; history.Anchor = AnchorStyles.Top | AnchorStyles.Right; history.Location = new Point(653, 61); tools.Controls.Add(history);
            selectAll = new SoftButton(UiLanguageService.Get("全选可下载")) { Anchor = AnchorStyles.Top | AnchorStyles.Right }; selectAll.SetBounds(891, 55, 133, 32); clear = new SoftButton(UiLanguageService.Get("清空选择")) { Anchor = AnchorStyles.Top | AnchorStyles.Right }; clear.SetBounds(1035, 55, 105, 32); tools.Controls.Add(selectAll); tools.Controls.Add(clear); deviceSearch.SetBounds(0, 94, 120, 32); autoDownload.SetBounds(132, 94, 150, 32); tools.Controls.Add(deviceSearch); tools.Controls.Add(autoDownload);
            var autoHint = DriverTheme.Label(UiLanguageService.Get("按本机硬件 ID 匹配，自动跳过旧版与被取代候选"), 8, DriverTheme.Muted); autoHint.SetBounds(298, 94, 630, 32); tools.Controls.Add(autoHint); policyButton.SetBounds(986, 94, 154, 32); policyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; tools.Controls.Add(policyButton); root.Controls.Add(tools, 0, 2);
            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = DriverTheme.Canvas };
            list.Dock = DockStyle.Fill; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.AutoScroll = true; list.Padding = Padding.Empty; list.Margin = Padding.Empty; host.Controls.Add(list);
            empty.Dock = DockStyle.Fill; empty.Font = DriverTheme.Font(12); empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = DriverTheme.Muted; empty.Text = UiLanguageService.Get("还没有扫描设备\r\n\r\n点击“扫描设备驱动”，开始查找可下载的版本。"); host.Controls.Add(empty); root.Controls.Add(host, 0, 3);
            var footer = new Panel { Size = new Size(1140, 132), Dock = DockStyle.Fill, Margin = Padding.Empty };
            footer.Paint += (s, e) => { using (var pen = new Pen(DriverTheme.Line)) e.Graphics.DrawLine(pen, 0, 9, footer.Width, 9); };
            var loc = DriverTheme.Label(UiLanguageService.Get("保存至"), 9, DriverTheme.Muted); loc.SetBounds(0, 19, 60, 30); footer.Controls.Add(loc);
            folder.ReadOnly = true; folder.BorderStyle = BorderStyle.None; folder.BackColor = DriverTheme.Canvas; folder.ForeColor = DriverTheme.Ink; folder.SetBounds(66, 25, 796, 23); folder.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; footer.Controls.Add(folder);
            browse = new SoftButton(UiLanguageService.Get("更改目录")) { Anchor = AnchorStyles.Top | AnchorStyles.Right }; browse.SetBounds(907, 19, 108, 32); open = new SoftButton(UiLanguageService.Get("打开目录")) { Anchor = AnchorStyles.Top | AnchorStyles.Right }; open.SetBounds(1027, 19, 113, 32); footer.Controls.Add(browse); footer.Controls.Add(open);
            silentInstall.Text = UiLanguageService.Get("下载后后台静默安装（需管理员权限，不自动重启）"); silentInstall.AutoSize = true; silentInstall.Location = new Point(0, 59); silentInstall.ForeColor = DriverTheme.Ink; footer.Controls.Add(silentInstall);
            silentInstall.CheckedChanged += (s, e) => UpdateSelection();
            selectedLabel.SetBounds(0, 93, 500, 28); selectedLabel.Font = DriverTheme.Font(11, FontStyle.Bold); selectedLabel.ForeColor = DriverTheme.Ink; footer.Controls.Add(selectedLabel);
            status.SetBounds(0, 126, 820, 22); status.Font = DriverTheme.Font(8); status.ForeColor = DriverTheme.Muted; status.AutoEllipsis = true; status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; status.Text = "就绪 · 多线程分块下载 · 默认仅下载；静默安装需主动勾选"; footer.Controls.Add(status);
            cancel = new SoftButton(UiLanguageService.Get("取消任务")) { Enabled = false, Anchor = AnchorStyles.Top | AnchorStyles.Right }; cancel.SetBounds(825, 97, 113, 44); download = new SoftButton(UiLanguageService.Get("下载所选驱动"), true) { Anchor = AnchorStyles.Top | AnchorStyles.Right }; download.SetBounds(950, 97, 190, 44); footer.Controls.Add(cancel); footer.Controls.Add(download);
            progress.SetBounds(0, 149, 1140, 4); progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; progress.Maximum = 100; footer.Controls.Add(progress); root.Controls.Add(footer, 0, 4);
            list.SizeChanged += (s, e) => ResizeCards(); search.TextChanged += (s, e) => RefreshRows();
            scan.Click += async (s, e) => await Scan();
            policyButton.Click += (s, e) => ShowUpdatePolicy();
            deviceSearch.Click += async (s, e) => await SearchDevices(); autoDownload.Click += async (s, e) => await AutoDownload(); download.Click += async (s, e) => await Download();
            cancel.Click += (s, e) => { if (operation != null) { operation.Cancel(); cancel.Enabled = false; status.Text = "正在取消；当前系统查询或安装须等待返回，不会强制中断…"; } };
            browse.Click += (s, e) => { using (var dialog = new FolderBrowserDialog { Description = UiLanguageService.Get("选择驱动包保存文件夹"), SelectedPath = folder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; SaveFolder(); } };
            open.Click += (s, e) => { try { Directory.CreateDirectory(folder.Text); Process.Start(new ProcessStartInfo(folder.Text) { UseShellExecute = true }); } catch (Exception ex) { Error(UiLanguageService.Get("无法打开下载目录"), ex); } };
            selectAll.Click += (s, e) => { foreach (var d in VisibleDevices()) { var v = Choice(d); if (v != null && v.DownloadUrls.Count > 0) selected.Add(v.Id); } RefreshRows(); };
            clear.Click += (s, e) => { selected.Clear(); RefreshRows(); };
            FormClosing += OnClosing; LoadFolder(); RefreshRows(); ApplyLanguage(); if (demo) Shown += (s, e) => LoadDemo(); UpdateSelection();
        }
        void SetLanguage(UiLanguage language)
        {
            UiLanguageService.Set(language, true);
            ApplyLanguage();
            RefreshRows();
        }
        void ApplyLanguage()
        {
            UiLanguageService.Apply(this);
            languageZh.Active = UiLanguageService.Current == UiLanguage.ZhCN;
            languageEn.Active = UiLanguageService.Current == UiLanguage.EnUS;
            languageZh.Invalidate(); languageEn.Invalidate();
            Text = UiLanguageService.Get("驱动下载中心") + " · WuMgr";
            download.Text = silentInstall.Checked ? UiLanguageService.Get("下载并静默安装") : UiLanguageService.Get("下载所选驱动");
            status.Relocalize();
            search.AccessibleName = UiLanguageService.Get("搜索设备型号、驱动号、厂商或硬件标识");
            RefreshRows();
        }

        internal DriverInfo Choice(DriverDevice d) { string id; return choices.TryGetValue(d.Key, out id) ? d.Versions.FirstOrDefault(x => x.Id == id) ?? d.Latest : d.Latest; }
        IEnumerable<DriverDevice> VisibleDevices()
        {
            string term = search.Text.Trim();
            return devices.Where(d => (category == "全部设备" || DriverTheme.Category(d) == category)
                && (term.Length == 0 || String.Join(" ", new[] { d.Model, d.Manufacturer, d.Class,
                    d.LocalDevice == null ? "" : d.LocalDevice.InstanceId,
                    d.LocalDevice == null ? "" : String.Join(";", d.LocalDevice.HardwareIds) }).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                    || d.Versions.Any(v => String.Join(" ", new[] { v.DeviceModel, v.Version, v.Title, v.HardwareId, v.Manufacturer, v.Provider }).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)));

        }
        void RefreshRows()
        {
            devices = localDevices.Count == 0 ? DriverCatalog.Group(drivers) : DeviceInventory.BuildEntries(localDevices, drivers); var visible = VisibleDevices().ToList(); var oldScroll = list.AutoScrollPosition; list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToArray()) c.Dispose(); list.Controls.Clear();
            foreach (var d in visible)
            {
                if (d.Latest == null)
                {
                    list.Controls.Add(new LocalDeviceCard(d.LocalDevice));
                    continue;
                }
                var card = new DriverCard(d, Choice(d), selected.Contains(Choice(d).Id), expanded.Contains(d.Key), demo);
                card.CheckChanged = value => { if (value && Choice(d).DownloadUrls.Count > 0) selected.Add(Choice(d).Id); else selected.Remove(Choice(d).Id); UpdateSelection(); };
                card.ToggleHistory = () => { if (!expanded.Add(d.Key)) expanded.Remove(d.Key); RefreshRows(); };
                card.Choose = v => { bool wasSelected = selected.Remove(Choice(d).Id); choices[d.Key] = v.Id; if (wasSelected && v.DownloadUrls.Count > 0) selected.Add(v.Id); RefreshRows(); };
                card.ShowInfo = () => ShowDetails(Choice(d));
                card.Download = async () => await DownloadItems(new List<DriverInfo> { Choice(d) });
                list.Controls.Add(card);
            }
            ResizeCards(); list.ResumeLayout(); list.AutoScrollPosition = new Point(-oldScroll.X, -oldScroll.Y);
            empty.Visible = visible.Count == 0; empty.Text = UiLanguageService.Get(drivers.Count == 0 ? UiLanguageService.Get("还没有可显示的驱动\r\n\r\n点击“扫描设备驱动”，查找当前更新源提供的版本。") : UiLanguageService.Get("没有匹配的设备\r\n\r\n试试其他型号、驱动号，或切换“全部设备”。"));
            total.Text = UiLanguageService.F(devices.Count + " 个设备  /  " + drivers.Count + " 个版本" + (visible.Count != devices.Count ? "  ·  显示 " + visible.Count + " 个设备" : ""), devices.Count + " devices  /  " + drivers.Count + " versions" + (visible.Count != devices.Count ? "  ·  Showing " + visible.Count + " devices" : ""));
            summary.Text = devices.Count == 0 ? UiLanguageService.Get("为你的设备，找到适用驱动") : localDevices.Count == 0 ? UiLanguageService.F(devices.Count + " 个设备，有可查看的驱动版本", devices.Count + " devices with available driver versions") : UiLanguageService.F(devices.Count + " 个本机设备，" + drivers.Count + " 个更新候选", devices.Count + " local devices, " + drivers.Count + " update candidates");
            subtitle.Text = demo ? UiLanguageService.Get("演示数据 · 非真实扫描结果 · 扫描与下载已禁用") : UiLanguageService.F("已发现本机 " + localDevices.Count + " 个设备；仅展示匹配候选，无候选不代表未识别或已是最新。", localDevices.Count + " local devices found; only matched candidates are shown. No candidate does not mean undetected or up to date.");
            UpdateSelection();
        }
        void ResizeCards() { foreach (Control c in list.Controls) c.Width = Math.Max(100, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2); }
        void UpdateSelection()
        {
            var picked = drivers.Where(d => selected.Contains(d.Id)).ToList(); selectedLabel.Text = UiLanguageService.Current == UiLanguage.EnUS ? "Selected " + picked.Count + " item(s)" + (picked.Count == 0 ? "  ·  one version per device" : "  ·  " + DriverTheme.Size(picked.Sum(d => d.Size))) : "已选 " + picked.Count + " 项" + (picked.Count == 0 ? "  ·  每个设备可选择一个版本" : "  ·  " + DriverTheme.Size(picked.Sum(d => d.Size)));
            download.Text = silentInstall.Checked ? UiLanguageService.Get("下载并静默安装") : UiLanguageService.Get("下载所选驱动");
            bool idle = operation == null; deviceSearch.Enabled = autoDownload.Enabled = silentInstall.Enabled = policyButton.Enabled = idle && !demo; download.Enabled = idle && picked.Count > 0 && !demo; scan.Enabled = idle && !demo; selectAll.Enabled = clear.Enabled = search.Enabled = history.Enabled = browse.Enabled = list.Enabled = idle;
        }
        void LoadFolder() { configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WuMgrDrivers", "download-folder.txt"); folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WuMgr驱动"); try { if (File.Exists(configPath)) { string value = File.ReadAllText(configPath).Trim(); if (Path.IsPathRooted(value)) folder.Text = value; } } catch { } }
        void SaveFolder() { try { Directory.CreateDirectory(Path.GetDirectoryName(configPath)); File.WriteAllText(configPath, folder.Text); } catch (Exception ex) { Error(UiLanguageService.Get("目录已更改，但无法保存偏好设置"), ex); } }
        void BeginOperation() { operation = new CancellationTokenSource(); cancel.Enabled = true; progress.Style = ProgressBarStyle.Marquee; UpdateSelection(); }
        void EndOperation() { operation.Dispose(); operation = null; cancel.Enabled = false; progress.Style = ProgressBarStyle.Continuous; UpdateSelection(); if (closeAfterCancel) Close(); }
        async Task<bool> Scan()
        {
            if (demo || operation != null) return false;
            BeginOperation(); status.Text = "正在发现本机设备并扫描适用驱动，请稍候…";
            try
            {
                var found = await discovery.ScanAsync(operation.Token, new Progress<string>(message => status.Text = message));
                // 先显示真实设备清单：即使更新源失败，也不能把设备当成未识别。
                localDevices.Clear(); localDevices.AddRange(found); drivers.Clear(); selected.Clear(); choices.Clear(); expanded.Clear(); RefreshRows();
                var result = await service.ScanAsync(history.Checked, operation.Token, new Progress<string>(message => status.Text = message));
                drivers.AddRange(DriverCatalog.NormalizeCandidates(result)); RefreshRows(); progress.Value = 100;
                status.Text = "扫描完成 · 本机 " + found.Count + " 个设备，" + drivers.Count + " 个候选；无候选不代表设备没有驱动"; return true;
            }
            catch (OperationCanceledException) { status.Text = "扫描已取消，保留已读取的设备清单"; return false; }
            catch (Exception ex) { status.Text = localDevices.Count > 0 ? "本机设备已读取，更新候选扫描失败；可查看设备清单并重试" : "扫描失败，可重试"; Error("无法扫描设备驱动", ex); return false; }
            finally { EndOperation(); }
        }
        async Task SearchDevices()
        {
            if (demo || operation != null) return;
            if (localDevices.Count == 0) { await Scan(); if (localDevices.Count == 0) return; }
            using (var dialog = new DeviceSearchForm(localDevices, drivers))
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedDriver != null)
                    await DownloadItems(new List<DriverInfo> { dialog.SelectedDriver });
        }
        async Task AutoDownload()
        {
            if (demo || operation != null || !await Scan()) return;
            var items = DeviceDiscovery.SelectAutomatic(localDevices, drivers);
            if (items.Count == 0) { status.Text = "没有可自动下载的明确匹配驱动，可进入设备搜索查看候选"; MessageBox.Show(this, "没有找到满足自动匹配条件的驱动。已跳过旧版、版本未知、无地址及被取代候选；可在设备搜索中人工核对。", "自动搜索下载", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            selected.Clear(); foreach (var d in items) selected.Add(d.Id);
            foreach (var group in DriverCatalog.Group(drivers)) { var choice = group.Versions.FirstOrDefault(v => selected.Contains(v.Id)); if (choice != null) choices[group.Key] = choice.Id; } RefreshRows();
            var names = String.Join("\r\n", items.Take(15).Select(d => "• " + d.DeviceModel + " / " + d.Version));
            if (items.Count > 15) names += UiLanguageService.Get("\r\n另有 ") + (items.Count - 15) + UiLanguageService.Get(" 项，可在主列表查看。");
            if (MessageBox.Show(this, UiLanguageService.Get("已按本机硬件精确匹配 ") + items.Count + UiLanguageService.Get(" 个驱动：\r\n\r\n") + names + UiLanguageService.Get("\r\n\r\n开始下载？") + (silentInstall.Checked ? UiLanguageService.Get("下载后还会进入安装确认。") : UiLanguageService.Get("本次不会安装。")), UiLanguageService.Get("自动搜索下载"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) await DownloadItems(items);
        }
        async Task Download() { await DownloadItems(drivers.Where(d => selected.Contains(d.Id)).ToList()); }
        async Task DownloadItems(List<DriverInfo> items)
        {
            if (demo || operation != null || items.Count == 0) return;
            bool install = silentInstall.Checked;
            if (install)
            {
                if (!DriverInstaller.IsAdministrator()) { MessageBox.Show(this, UiLanguageService.Get("静默安装需要管理员权限。请关闭程序，右键选择“以管理员身份运行”，再重试；仅下载无需管理员权限。"), UiLanguageService.Get("需要管理员权限"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                if (MessageBox.Show(this, UiLanguageService.Get("将下载并后台静默安装以下 ") + items.Count + UiLanguageService.Get(" 个驱动：\r\n\r\n") + String.Join("\r\n", items.Select(d => d.DeviceModel + " / " + d.Version)) + UiLanguageService.Get("\r\n\r\n安装会修改系统驱动，可能短暂断网、闪屏或需要重启。不会自动重启，不绕过系统签名检查。是否继续？"), UiLanguageService.Get("确认下载并安装"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            BeginOperation(); progress.Style = ProgressBarStyle.Continuous; progress.Value = 0;
            try
            {
                var report = new Progress<DriverDownloadProgress>(p => { progress.Value = Math.Max(0, Math.Min(100, p.Percent)); status.Text = p.Message; });
                Exception downloadError = null;
                try { await service.DownloadAsync(items, folder.Text, operation.Token, report); }
                catch (AggregateException ex) { downloadError = ex; }
                operation.Token.ThrowIfCancellationRequested();
                if (install)
                {
                    var ready = items.Where(d => d.Status == "已下载").ToList();
                    if (ready.Count == 0) throw downloadError ?? new InvalidOperationException(UiLanguageService.Get("没有成功下载的驱动，未执行安装。"));
                    installing = true; status.Text = "正在后台静默安装；Windows 更新缓存可能需再次获取文件…";
                    var result = await installer.InstallAsync(ready, operation.Token, report); rebootRequired |= result.RebootRequired;
                    progress.Value = 100;
                    status.Text = (result.Canceled ? "已停止后续安装：成功 " : "安装完成：成功 ") + result.InstalledCount + "，失败 " + result.FailedCount + (downloadError != null ? "；部分下载失败" : "") + (rebootRequired ? " · 需要手动重启" : " · 未自动重启");
                    if (result.Errors.Count > 0 || downloadError != null || result.RebootRequired || result.Canceled)
                        ShowText(UiLanguageService.Get("安装结果"), status.Text + "\r\n\r\n" + String.Join("\r\n", result.Errors.Select(UiLanguageService.Display)) + (downloadError == null ? "" : "\r\n" + UiLanguageService.Display(downloadError.Message)) + UiLanguageService.Get("\r\n\r\n系统日志：Windows\\Logs\\WindowsUpdate；本地日志：各驱动目录中的安装日志.txt。重启前请保存工作。"));
                    if (result.InstalledCount > 0 && !operation.IsCancellationRequested && MessageBox.Show(this, UiLanguageService.Get("驱动已成功安装。是否查看并选择开启“驱动更新保护”？\r\n该策略全局排除质量更新中的驱动，不会关闭系统安全更新，也不能保证阻止所有驱动变更。默认不会自动开启。"), UiLanguageService.Get("安装后保护"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) ShowUpdatePolicy();
                }
                else { if (downloadError != null) throw downloadError; progress.Value = 100; status.Text = "所选驱动下载完成，未执行任何安装"; }
            }
            catch (OperationCanceledException) { status.Text = installing ? "已停止后续安装；当前系统调用已返回，已完成安装不会回滚。请检查各项状态及重启提示" : "下载已取消，已保留可用断点；在同一目录重试可续传"; }
            catch (Exception ex) { status.Text = "任务未全部完成，请查看各版本状态后重试"; Error("驱动任务未全部完成", ex); }
            finally { installing = false; RefreshRows(); EndOperation(); }
        }
        void ShowUpdatePolicy()
        {
            if (demo) return;
            try
            {
                var state = updatePolicy.Read();
                using (var dialog = new Form { Text = UiLanguageService.Get("驱动更新保护 · 可恢复策略"), Size = new Size(760, 490), MinimumSize = new Size(650, 450), Font = Font, StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, Padding = new Padding(20) })
                {
                    var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
                    var close = new Button { Text = UiLanguageService.Get("关闭"), AutoSize = true, DialogResult = DialogResult.Cancel };
                    var restore = new Button { Text = UiLanguageService.Get("恢复原设置"), AutoSize = true, Enabled = updatePolicy.HasBackup };
                    var enable = new Button { Text = UiLanguageService.Get("开启保护"), AutoSize = true, Enabled = !state.Enabled };
                    actions.Controls.Add(close); actions.Controls.Add(restore); actions.Controls.Add(enable); dialog.Controls.Add(actions); dialog.CancelButton = close;
                    var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Text = UiLanguageService.Get("当前状态：") + UiLanguageService.Display(state.Description) + UiLanguageService.Get("\r\n\r\n开启后：设置微软官方“Windows 更新不包括驱动程序”策略，仅排除 Windows 质量更新中的驱动。安全补丁、系统更新仍保持启用。作用于全机，不是单个设备。\r\n\r\n边界：不能阻断随操作系统安全／关键更新提供的驱动，也不拦截功能更新期间的动态驱动安装；设备初次安装、手动安装或厂商工具也可能更改驱动，也不保证消除冲突。组织组策略 / MDM 可覆盖本设置。该策略可能影响本工具后续查询；需更新驱动时请先恢复原设置。\r\n\r\n兼容性：Windows 10 1607+ / Windows 11：微软文档列出专业版、企业版、教育版、IoT 企业版；家庭版不承诺生效。\r\n\r\n安全：需要管理员权限；开启前备份本工具涉及的原始注册表值，恢复时还原，不停止 Windows Update 服务、不改其他更新策略。建议重启后核验；不自动重启。\r\n\r\n官方说明：https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update#excludewudriversinqualityupdate") };
                    dialog.Controls.Add(text); text.BringToFront();
                    enable.Click += (s, e) => { if (MessageBox.Show(dialog, "确认开启全机驱动更新保护？不会关闭安全更新；可能影响本工具后续驱动查询，且无法保证阻断所有驱动变更。", "确认策略变更", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return; try { updatePolicy.Enable(); status.Text = "驱动更新保护策略已设置 · 建议重启并核验，不保证阻断所有驱动来源"; dialog.Close(); } catch (Exception ex) { Error("无法开启驱动更新保护", ex); } };
                    restore.Click += (s, e) => { if (MessageBox.Show(dialog, "恢复本工具开启保护前保存的原始设置？恢复后系统可能重新通过 Windows 更新提供驱动。", "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return; try { updatePolicy.Restore(); status.Text = "已恢复驱动更新原始设置 · 请在 Windows 中核验"; dialog.Close(); } catch (Exception ex) { Error("无法恢复原设置", ex); } };
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception ex) { Error(UiLanguageService.Get("无法读取驱动更新保护状态"), ex); }
        }
        void Error(string title, Exception ex) { MessageBox.Show(this, title + "。\r\n" + UiLanguageService.Display(ex.Message), UiLanguageService.Get("驱动下载中心"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void OnClosing(object sender, FormClosingEventArgs e) { if (operation == null) return; e.Cancel = true; if (!closeAfterCancel && MessageBox.Show(this, "任务仍在进行。取消任务后关闭窗口？系统扫描或安装需要等待当前调用返回；已完成安装不会回滚。", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { closeAfterCancel = true; operation.Cancel(); cancel.Enabled = false; status.Text = "正在取消，完成清理后将自动关闭…"; } }
        void ShowDetails(DriverInfo d)
        {
            string text = UiLanguageService.Get("设备型号：") + d.DeviceModel + UiLanguageService.Get("\r\n驱动号 / 版本：") + d.Version + UiLanguageService.Get("\r\n硬件标识：") + d.HardwareId + UiLanguageService.Get("\r\n提供商：") + d.Provider + UiLanguageService.Get("\r\n原始类别：") + d.DriverClass + UiLanguageService.Get("\r\n原始名称：") + d.Title + UiLanguageService.Get("\r\n更新标识：") + d.Id + UiLanguageService.Get("\r\n下载状态：") + UiLanguageService.Display(d.Status) + UiLanguageService.Get("\r\n保存位置：") + (String.IsNullOrEmpty(d.LocalPath) ? UiLanguageService.Get("尚未下载") : d.LocalPath) + UiLanguageService.Get("\r\n\r\n版本仅为当前更新源提供的候选，不代表已安装版本。旧版本不一定更适用，安装前请自行核验硬件匹配及供应商签名。");
            ShowText(UiLanguageService.Get("驱动详情"), text);
        }
        void ShowText(string title, string text) { using (var dialog = new Form { Text = title, Size = new Size(760, 510), StartPosition = FormStartPosition.CenterParent, Font = Font, Padding = new Padding(22), BackColor = Color.White }) { dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, Text = text }); dialog.ShowDialog(this); } }
        void ShowLicense()
        {
            string legal = UiLanguageService.Get("WuMgr 驱动下载中心 7.0 · 中英文修改版\r\n原项目：Copyright © DavidXanatos 2018-2020\r\n上游：https://github.com/DavidXanatos/wumgr\r\n本修改版依据 GNU GPL 第 3 版发布，不提供任何担保。完整对应源码与程序一同提供。\r\n下载组件：Downloader 3.1.2 · Behzad Khosravifar · MIT\r\nhttps://github.com/bezzad/Downloader\r\n许可见 Downloader-LICENSE.txt。\r\n图标来源：Icons8，https://icons8.com，CC BY-ND 3.0。\r\n\r\n默认仅下载；可主动启用下载后后台静默安装，需管理员权限，不自动重启。可选开启驱动更新保护，仅排除质量更新中的驱动，不关闭系统安全更新。\r\n历史版本仅限 Windows 更新源返回的适用候选，无法保证完整。\r\n\r\n");
            try { legal += File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE")); } catch { legal += UiLanguageService.Get("完整许可证参见源码根目录 LICENSE：https://www.gnu.org/licenses/gpl-3.0.html"); } ShowText(UiLanguageService.Get("关于与许可证"), legal);
        }
        void LoadDemo()
        {
            AddDemo("audio-1", "Realtek(R) Audio", "6.0.9700.1", "Realtek", "MEDIA", "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0256", new DateTime(2024, 5, 15), 50.8M);
            AddDemo("audio-2", "Realtek(R) Audio", "6.0.9657.1", "Realtek", "MEDIA", "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0256", new DateTime(2024, 2, 20), 49.6M);
            AddDemo("audio-3", "Realtek(R) Audio", "6.0.9605.1", "Realtek", "MEDIA", "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0256", new DateTime(2023, 11, 7), 48.2M);
            AddDemo("net-1", "Intel(R) Wi-Fi 6E AX211 160MHz", "23.60.1.2", "Intel", "Net", "PCI\\VEN_8086&DEV_51F0", new DateTime(2024, 6, 2), 37M);
            AddDemo("net-2", "Intel(R) Wi-Fi 6E AX211 160MHz", "23.40.0.4", "Intel", "Net", "PCI\\VEN_8086&DEV_51F0", new DateTime(2024, 3, 18), 35.4M);
            AddDemo("display-1", "NVIDIA GeForce RTX 4060", "32.0.15.6094", "NVIDIA", "Display", "PCI\\VEN_10DE&DEV_2882", new DateTime(2024, 8, 14), 675M);
            devices = DriverCatalog.Group(drivers); var audio = devices.First(d => d.Latest.Manufacturer == "Realtek"); expanded.Add(audio.Key); selected.Add(audio.Latest.Id); RefreshRows(); status.Text = "演示模式 · 不是真实扫描结果 · 不会扫描、下载或安装";
        }
        void AddDemo(string id, string model, string version, string maker, string type, string hardware, DateTime date, decimal mb) { drivers.Add(new DriverInfo { Id = "demo-" + id, DeviceModel = model, Version = version, Title = maker + " - " + type + " - " + version, Manufacturer = maker, Provider = maker, DriverClass = type, HardwareId = hardware, DriverDate = date, Size = mb * 1048576M, DownloadUrls = new List<string> { "https://example.invalid/demo.cab" }, Status = "示例 · 待下载" }); }
        public void SavePreview(string path) { using (var image = new Bitmap(ClientSize.Width, ClientSize.Height)) { using (var g = Graphics.FromImage(image)) g.CopyFromScreen(PointToScreen(Point.Empty), Point.Empty, ClientSize); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); } }
    }

    internal sealed class DriverCard : SoftPanel
    {
        public Action<bool> CheckChanged;
        public Action ToggleHistory, ShowInfo;
        public Action<DriverInfo> Choose;
        public Func<Task> Download;
        readonly float scale;
        readonly DriverDevice device;
        readonly DriverInfo choice;
        readonly bool isExpanded;
        readonly CheckBox check = new CheckBox();
        readonly SoftButton versions, get, info;
        readonly List<RadioButton> radios = new List<RadioButton>();
        readonly ToolTip tips = new ToolTip { InitialDelay = 400, AutoPopDelay = 12000 };
        public DriverCard(DriverDevice d, DriverInfo current, bool isChecked, bool showHistory, bool demo)
        {
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = graphics.DpiX / 96F;
            device = d; choice = current; isExpanded = showHistory; AccessibleName = d.Model + "，" + UiLanguageService.Get("驱动号  ") + current.Version; AccessibleDescription = UiLanguageService.Get("厂商") + " " + d.Manufacturer + "，" + UiLanguageService.Get("日期") + " " + DriverTheme.Date(current) + "，" + DriverTheme.Size(current.Size); Margin = new Padding(0, 0, 0, S(12)); Height = S(showHistory ? 151 + d.Versions.Count * 40 : 108);
            check.Checked = isChecked; check.Enabled = current.DownloadUrls.Count > 0; check.AccessibleName = UiLanguageService.Get("选择 ") + d.Model; check.Bounds = R(18, 39, 22, 25); Controls.Add(check); check.CheckedChanged += (s, e) => { if (CheckChanged != null) CheckChanged(check.Checked); };
            versions = new SoftButton((showHistory ? UiLanguageService.Get("收起版本") + "  ∧" : d.Versions.Count > 1 ? UiLanguageService.Get("历史版本") + "  ∨" : UiLanguageService.Get("查看版本") + "  ∨") + "  " + d.Versions.Count); Controls.Add(versions); versions.Click += (s, e) => { if (ToggleHistory != null) ToggleHistory(); };
            get = new SoftButton(UiLanguageService.Get("下载此版本"), true) { Enabled = !demo && current.DownloadUrls.Count > 0 }; Controls.Add(get); get.Click += async (s, e) => { if (Download != null) await Download(); };
            info = new SoftButton(UiLanguageService.Get("详情")); Controls.Add(info); info.Click += (s, e) => { if (ShowInfo != null) ShowInfo(); };
            if (showHistory) foreach (var v in d.Versions)
            {
                var radio = new RadioButton { Checked = v.Id == current.Id, AutoSize = false, Text = "", Enabled = v.DownloadUrls.Count > 0, AccessibleName = UiLanguageService.Get("选择驱动版本 ") + v.Version, Tag = v };
                radio.BackColor = v.Id == current.Id ? DriverTheme.Pale : Color.FromArgb(248, 251, 249); radios.Add(radio); Controls.Add(radio); radio.CheckedChanged += (s, e) => { if (radio.Checked && Choose != null) Choose(v); };
            }
            tips.SetToolTip(this, (d.LocalDevice == null ? "" : d.LocalDevice.InstanceId + "\n" + UiLanguageService.T("已安装版本：", "Installed version: ") + d.LocalDevice.InstalledVersion + "\n") + d.Model + UiLanguageService.Get("\n当前驱动号：") + current.Version + UiLanguageService.Get("\n硬件标识：") + current.HardwareId);
            tips.SetToolTip(versions, d.Versions.Count > 1 ? UiLanguageService.Get("查看当前更新源提供的历史候选；不保证完整历史。") : UiLanguageService.Get("当前更新源仅返回一个候选，并无其他历史版本。"));
            Resize += (s, e) => Arrange(); Arrange();
        }
        void DrawDeviceIcon(Graphics g, string category)
        {
            using (var pen = new Pen(DriverTheme.Accent, 1.8F * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (category == "网络适配器")
                {
                    g.DrawArc(pen, R(60, 39, 34, 31), 220, 100); g.DrawArc(pen, R(66, 46, 22, 22), 220, 100); g.DrawArc(pen, R(72, 53, 10, 12), 220, 100);
                    using (var b = new SolidBrush(DriverTheme.Accent)) g.FillEllipse(b, R(75, 63, 4, 4));
                }
                else if (category == "声卡")
                {
                    Point[] speaker = { new Point(S(62), S(46)), new Point(S(68), S(46)), new Point(S(75), S(40)), new Point(S(75), S(63)), new Point(S(68), S(57)), new Point(S(62), S(57)) }; g.DrawPolygon(pen, speaker);
                    g.DrawArc(pen, R(74, 44, 12, 15), -65, 130); g.DrawArc(pen, R(74, 38, 23, 27), -63, 126);
                }
                else
                {
                    g.DrawRectangle(pen, R(62, 39, 30, 22)); g.DrawLine(pen, S(77), S(62), S(77), S(67)); g.DrawLine(pen, S(69), S(67), S(85), S(67));
                }
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (!isExpanded || e.Button != MouseButtons.Left) return;
            int y = (int)(e.Y / scale); int row = (y - 143) / 40;
            if (y >= 143 && row >= 0 && row < radios.Count && radios[row].Enabled) radios[row].Checked = true;
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
        int S(int value) { return (int)Math.Round(value * scale); }
        Rectangle R(int x, int y, int width, int height) { return new Rectangle(S(x), S(y), S(width), S(height)); }
        void Arrange()
        {
            int w = (int)Math.Round(Width / scale);
            versions.Bounds = R(w - 318, 20, 148, 33); get.Bounds = R(w - 154, 20, 134, 36); info.Bounds = R(w - 78, 65, 58, 26);
            for (int i = 0; i < radios.Count; i++) radios[i].Bounds = R(37, 145 + 40 * i, 24, 28); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); int w = (int)Math.Round(Width / scale), h = (int)Math.Round(Height / scale); var g = e.Graphics; var ink = DriverTheme.Ink; var muted = DriverTheme.Muted; var accent = DriverTheme.Accent; g.SmoothingMode = SmoothingMode.AntiAlias;
            string cat = DriverTheme.Category(device);
            using (var b = new SolidBrush(DriverTheme.Pale)) g.FillEllipse(b, R(53, 27, 48, 48));
            DrawDeviceIcon(g, cat);
            using (var f = DriverTheme.Font(8.5F)) DriverTheme.Text(g, UiLanguageService.Get(cat) + "  /  " + device.Manufacturer, f, muted, R(117, 13, w - 460, 24));
            using (var f = DriverTheme.Font(11, FontStyle.Bold)) DriverTheme.Text(g, device.Model, f, ink, R(116, 36, w - 465, 29));
            using (var f = DriverTheme.Font(8.5F)) DriverTheme.Text(g, UiLanguageService.Get("驱动号  ") + choice.Version + "     ·     " + DriverTheme.Date(choice) + "     ·     " + DriverTheme.Size(choice.Size), f, muted, R(117, 68, w - 435, 25));
            using (var f = DriverTheme.Font(8)) DriverTheme.Text(g, UiLanguageService.Display(choice.Status), f, choice.Status.StartsWith("失败") ? Color.Firebrick : muted, R(w - 310, 67, 220, 22), TextFormatFlags.Right);
            if (!isExpanded) return;
            using (var b = new SolidBrush(Color.FromArgb(248, 251, 249))) g.FillRectangle(b, R(1, 107, w - 2, h - 119));
            using (var pen = new Pen(DriverTheme.Line)) g.DrawLine(pen, S(20), S(107), S(w - 20), S(107));
            using (var f = DriverTheme.Font(8))
            {
                DriverTheme.Text(g, UiLanguageService.Get("选择驱动号 / 版本"), f, muted, R(71, 112, w - 720, 28));
                DriverTheme.Text(g, UiLanguageService.Get("驱动日期"), f, muted, R(w - 620, 112, 140, 28));
                DriverTheme.Text(g, UiLanguageService.Get("大小"), f, muted, R(w - 425, 112, 120, 28));
                DriverTheme.Text(g, UiLanguageService.Get("版本说明"), f, muted, R(w - 262, 112, 225, 28));
                for (int i = 0; i < device.Versions.Count; i++)
                {
                    var v = device.Versions[i]; int y = 143 + i * 40;
                    if (v.Id == choice.Id) using (var b = new SolidBrush(DriverTheme.Pale)) g.FillRectangle(b, R(22, y, w - 44, 36));
                    using (var vf = DriverTheme.Font(9, v.Id == choice.Id ? FontStyle.Bold : FontStyle.Regular)) DriverTheme.Text(g, v.Version, vf, v.Id == choice.Id ? accent : ink, R(71, y, w - 720, 36));
                    DriverTheme.Text(g, DriverTheme.Date(v), f, muted, R(w - 620, y, 155, 36));
                    DriverTheme.Text(g, DriverTheme.Size(v.Size), f, muted, R(w - 425, y, 125, 36));
                    Version parsed; string note = v.DownloadUrls.Count == 0 ? UiLanguageService.Get("无可用下载地址") : i == 0 && Version.TryParse(v.Version, out parsed) ? UiLanguageService.Get("列表最高版本 · 非安装建议") : v.Superseded ? UiLanguageService.Get("已被取代候选 · 请核验") : i > 0 ? UiLanguageService.Get("历史候选 · 安装前请核验") : UiLanguageService.Get("版本未明确 · 请核验详情");
                    DriverTheme.Text(g, note, f, i == 0 ? accent : muted, R(w - 262, y, 235, 36));
                }
            }
        }
    }
}

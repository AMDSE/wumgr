using System;
using System.Drawing;
using System.Windows.Forms;

namespace wumgr
{
    public sealed partial class DriverForm
    {
        static TableLayoutPanel Grid(int columns, int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, Padding = Padding.Empty };
        }
        static void Cell(Control control, TableLayoutPanel grid, int column, int row, Padding margin)
        {
            control.Dock = DockStyle.Fill; control.Margin = margin; grid.Controls.Add(control, column, row);
        }
        static Label Caption(string zh, string en, float size, Color color, FontStyle style = FontStyle.Regular)
        {
            // Tag keeps a stable translation key even after multiple language switches.
            var label = DriverTheme.Label(UiLanguageService.T(zh, en), size, color, style);
            label.Tag = new LocalizedCaption(zh, en); return label;
        }
        void BuildLayout()
        {
            SuspendLayout();
            var shell = Grid(2, 1);
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(shell);

            var nav = Grid(1, 13); nav.BackColor = Color.White; nav.Padding = new Padding(16, 22, 16, 16);
            nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            for (int i = 0; i < 7; i++) nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            nav.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Cell(nav, shell, 0, 0, Padding.Empty);
            var brand = Grid(1, 2); brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Cell(DriverTheme.Label("WuMgr", 19, DriverTheme.Ink, FontStyle.Bold), brand, 0, 0, Padding.Empty);
            Cell(Caption("驱动工作台", "DRIVER WORKSPACE", 8, DriverTheme.Muted), brand, 0, 1, Padding.Empty); Cell(brand, nav, 0, 0, new Padding(8, 0, 0, 12));
            string[] names = { "全部设备", "显卡", "网络适配器", "声卡", "蓝牙", "系统设备", "其他设备" };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                var tab = new SoftButton(UiLanguageService.Get(name)) { Active = name == category, Tag = name, AccessibleRole = AccessibleRole.PageTab };
                tabs.Add(tab); Cell(tab, nav, 0, i + 1, new Padding(0, 2, 0, 2));
                tab.Click += (s, e) => { category = name; foreach (var b in tabs) { b.Active = (string)b.Tag == category; b.Invalidate(); } RenderRows(); };
            }
            Cell(deviceSearch, nav, 0, 9, new Padding(0, 2, 0, 2));
            Cell(policyButton, nav, 0, 10, new Padding(0, 2, 0, 2));
            var about = new SoftButton(UiLanguageService.Get("关于与许可")); about.Click += (s, e) => ShowLicense(); Cell(about, nav, 0, 11, new Padding(0, 2, 0, 2));
            Cell(DriverTheme.Label("10.0 PREVIEW", 8, DriverTheme.Muted), nav, 0, 12, new Padding(8, 0, 0, 0));

            var work = Grid(1, 5); work.Padding = new Padding(24, 12, 24, 14); work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 60, 104, 92, 0, 168 }) work.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, h));
            Cell(work, shell, 1, 0, Padding.Empty);
            var header = Grid(3, 1); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            Cell(Caption("设备与驱动", "Devices & drivers", 19, DriverTheme.Ink, FontStyle.Bold), header, 0, 0, Padding.Empty);
            Cell(languageZh, header, 1, 0, new Padding(0, 12, 6, 12)); Cell(languageEn, header, 2, 0, new Padding(0, 12, 0, 12)); Cell(header, work, 0, 0, Padding.Empty);

            var hero = new SoftPanel { BackColor = DriverTheme.Pale, Padding = new Padding(18, 10, 16, 10) };
            var heroGrid = Grid(2, 2); heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); hero.Controls.Add(heroGrid);
            summary.Font = DriverTheme.Font(14, FontStyle.Bold); summary.ForeColor = DriverTheme.Ink; summary.AutoEllipsis = true; summary.TextAlign = ContentAlignment.MiddleLeft;
            subtitle.Font = DriverTheme.Font(8.5F); subtitle.ForeColor = DriverTheme.Muted; subtitle.AutoEllipsis = true; subtitle.TextAlign = ContentAlignment.MiddleLeft;
            Cell(summary, heroGrid, 0, 0, new Padding(0, 0, 12, 0)); Cell(subtitle, heroGrid, 0, 1, new Padding(0, 0, 12, 0));
            Cell(scan, heroGrid, 1, 0, new Padding(0, 1, 0, 3)); Cell(autoDownload, heroGrid, 1, 1, new Padding(0, 2, 0, 0)); Cell(hero, work, 0, 1, new Padding(0, 0, 0, 8));

            var tools = Grid(4, 2); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 252)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var searchHost = new SoftPanel { Padding = new Padding(10, 8, 10, 5) };
            var searchGrid = Grid(2, 1); searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65)); searchGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Cell(Caption("搜索", "Search", 9, DriverTheme.Muted), searchGrid, 0, 0, Padding.Empty);
            search.BorderStyle = BorderStyle.None; search.AccessibleName = UiLanguageService.Get("搜索设备型号、驱动号、厂商或硬件标识"); Cell(search, searchGrid, 1, 0, new Padding(0, 2, 0, 0)); searchHost.Controls.Add(searchGrid);
            Cell(searchHost, tools, 0, 0, new Padding(0, 2, 0, 4)); tools.SetColumnSpan(searchHost, 4);
            total.Font = DriverTheme.Font(8.5F); total.ForeColor = DriverTheme.Muted; total.AutoEllipsis = true; total.TextAlign = ContentAlignment.MiddleLeft; Cell(total, tools, 0, 1, Padding.Empty);
            history.Text = UiLanguageService.Get("扫描时包含历史版本"); history.Checked = true; history.ForeColor = DriverTheme.Muted; history.AutoSize = false; Cell(history, tools, 1, 1, new Padding(4, 0, 0, 0));
            Cell(selectAll, tools, 2, 1, new Padding(6, 4, 0, 4)); Cell(clear, tools, 3, 1, new Padding(6, 4, 0, 4)); Cell(tools, work, 0, 2, Padding.Empty);

            var host = new Panel { BackColor = DriverTheme.Canvas };
            list.Dock = DockStyle.Fill; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.AutoScroll = true; list.Padding = Padding.Empty; list.Margin = Padding.Empty; host.Controls.Add(list);
            empty.Dock = DockStyle.Fill; empty.Font = DriverTheme.Font(11); empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = DriverTheme.Muted; host.Controls.Add(empty); Cell(host, work, 0, 3, Padding.Empty);

            var footer = Grid(1, 5); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 38, 34, 52, 32, 5 }) footer.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            footer.Paint += (s, e) => { using (var pen = new Pen(DriverTheme.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var location = Grid(4, 1); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            Cell(Caption("保存至", "Save to", 9, DriverTheme.Muted), location, 0, 0, Padding.Empty);
            folder.ReadOnly = true; folder.BorderStyle = BorderStyle.None; folder.BackColor = DriverTheme.Canvas; folder.ForeColor = DriverTheme.Ink; folder.AccessibleName = UiLanguageService.T("下载保存目录", "Download folder");
            Cell(folder, location, 1, 0, new Padding(0, 11, 8, 0)); Cell(browse, location, 2, 0, new Padding(4, 4, 0, 0)); Cell(open, location, 3, 0, new Padding(6, 4, 0, 0)); Cell(location, footer, 0, 0, Padding.Empty);
            silentInstall.Text = UiLanguageService.Get("下载后后台静默安装（需管理员权限，不自动重启）"); silentInstall.AutoSize = false; silentInstall.ForeColor = DriverTheme.Ink; Cell(silentInstall, footer, 0, 1, Padding.Empty);
            var actions = Grid(3, 1); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
            selectedLabel.Font = DriverTheme.Font(10, FontStyle.Bold); selectedLabel.ForeColor = DriverTheme.Ink; selectedLabel.AutoEllipsis = true; selectedLabel.TextAlign = ContentAlignment.MiddleLeft;
            Cell(selectedLabel, actions, 0, 0, Padding.Empty); Cell(cancel, actions, 1, 0, new Padding(0, 4, 8, 4)); Cell(download, actions, 2, 0, new Padding(0, 4, 0, 4)); Cell(actions, footer, 0, 2, Padding.Empty);
            status.Font = DriverTheme.Font(8); status.ForeColor = DriverTheme.Muted; status.AutoEllipsis = true; status.TextAlign = ContentAlignment.MiddleLeft; status.Text = "就绪 · 多线程分块下载 · 默认仅下载；静默安装需主动勾选"; Cell(status, footer, 0, 3, Padding.Empty);
            progress.Maximum = 100; Cell(progress, footer, 0, 4, Padding.Empty); Cell(footer, work, 0, 4, Padding.Empty);
            ResumeLayout(true);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { if (search.Enabled) search.Focus(); return true; }
            if (keyData == Keys.Escape && search.Focused) { search.Clear(); return true; }
            if (keyData == Keys.F5 && scan.Enabled) { scan.PerformClick(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
    internal sealed class LocalizedCaption
    {
        internal readonly string Zh, En;
        internal LocalizedCaption(string zh, string en) { Zh = zh; En = en; }
    }
    internal sealed class BufferedDeviceList : FlowLayoutPanel
    {
        internal BufferedDeviceList() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace wumgr
{
    /// <summary>本机设备清单与 WUA 已返回候选的精确硬件匹配视图。</summary>
    public sealed class DeviceSearchForm : Form
    {
        readonly List<LocalDeviceInfo> devices;
        readonly List<DriverInfo> candidates;
        readonly TextBox query = new TextBox();
        readonly DataGridView deviceGrid = Grid(), driverGrid = Grid();
        readonly Label details = new Label();
        readonly Button download = new Button { Text = "下载所选版本", AutoSize = true, Enabled = false };
        public DriverInfo SelectedDriver { get; private set; }
        static DataGridView Grid()
        {
            return new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false, RowHeadersVisible = false, BackgroundColor = Color.White, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        }
        static void Column(DataGridView grid, string name, string property, int weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, DataPropertyName = property, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        }
        public DeviceSearchForm(List<LocalDeviceInfo> local, List<DriverInfo> available)
        {
            devices = local; candidates = available;
            Text = UiLanguageService.Get("本机设备搜索 · 精确匹配驱动"); Font = DriverTheme.Font(9); BackColor = DriverTheme.Canvas; Size = new Size(1000, 720); MinimumSize = new Size(800, 580); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
            foreach (int h in new[] { 30, 34, 0, 52, 0, 42, 40 }) layout.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent, 50) : new RowStyle(SizeType.Absolute, h));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = UiLanguageService.Get("搜索本机设备名称、硬件 ID、实例 ID 或制造商"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            query.Dock = DockStyle.Fill; query.AccessibleName = "本机设备搜索关键词"; layout.Controls.Add(query, 0, 1);
            Column(deviceGrid, UiLanguageService.Get("设备名称"), "Name", 180); Column(deviceGrid, UiLanguageService.Get("类别"), "Class", 70); Column(deviceGrid, UiLanguageService.Get("当前版本"), "InstalledVersion", 100); Column(deviceGrid, UiLanguageService.Get("设备状态"), "DisplayStatus", 100);
            layout.Controls.Add(deviceGrid, 0, 2);
            details.Dock = DockStyle.Fill; details.AutoEllipsis = true; details.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(details, 0, 3);
            Column(driverGrid, UiLanguageService.Get("适用驱动 / 型号"), "DeviceModel", 160); Column(driverGrid, UiLanguageService.Get("驱动版本"), "Version", 90); Column(driverGrid, UiLanguageService.Get("日期"), "DriverDate", 90); Column(driverGrid, UiLanguageService.Get("提供商"), "Provider", 100); Column(driverGrid, UiLanguageService.Get("状态"), "Status", 100);
            driverGrid.Columns[2].DefaultCellStyle.Format = "yyyy-MM-dd"; layout.Controls.Add(driverGrid, 0, 4);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = UiLanguageService.Get("仅显示更新源已返回且硬件 ID / 兼容 ID 完整匹配的候选。空列表不代表设备没有驱动；旧版本不一定更适用。"), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel }; actions.Controls.Add(close); actions.Controls.Add(download); layout.Controls.Add(actions, 0, 6); CancelButton = close;
            query.TextChanged += (s, e) => RefreshDevices(); deviceGrid.SelectionChanged += (s, e) => RefreshDrivers(); driverGrid.SelectionChanged += (s, e) => UpdateSelection();
            download.Click += (s, e) => { UpdateSelection(); if (SelectedDriver != null) { DialogResult = DialogResult.OK; Close(); } };
            query.AccessibleName = UiLanguageService.Get("本机设备搜索关键词");
            deviceGrid.CellFormatting += (s, e) => { if (e.ColumnIndex == 3 && e.Value != null) { e.Value = UiLanguageService.Display(Convert.ToString(e.Value)); e.FormattingApplied = true; } };
            driverGrid.CellFormatting += (s, e) => { if (e.ColumnIndex == 4 && e.Value != null) { e.Value = UiLanguageService.Display(Convert.ToString(e.Value)); e.FormattingApplied = true; } };
            RefreshDevices(); UiLanguageService.Apply(this);
        }
        void RefreshDevices()
        {
            string term = query.Text.Trim();
            deviceGrid.DataSource = devices.Where(d => term.Length == 0 || String.Join(" ", new[] { d.Name, d.InstanceId, d.Manufacturer, String.Join(";", d.HardwareIds), String.Join(";", d.CompatibleIds) }).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            RefreshDrivers();
        }
        void RefreshDrivers()
        {
            var device = deviceGrid.CurrentRow == null ? null : deviceGrid.CurrentRow.DataBoundItem as LocalDeviceInfo;
            var matches = device == null ? new List<DriverInfo>() : DeviceDiscovery.Match(device, candidates);
            driverGrid.DataSource = matches;
            details.Text = device == null ? UiLanguageService.Get("没有匹配的本机设备") : UiLanguageService.Get("硬件 ID：") + String.Join("; ", device.HardwareIds) + "\r\n" + matches.Count + UiLanguageService.Get(" 个候选版本 · 当前版本：") + device.InstalledVersion;
            UpdateSelection();
        }
        void UpdateSelection()
        {
            SelectedDriver = driverGrid.CurrentRow == null ? null : driverGrid.CurrentRow.DataBoundItem as DriverInfo;
            download.Enabled = SelectedDriver != null && SelectedDriver.DownloadUrls != null && SelectedDriver.DownloadUrls.Count > 0;
        }
    }
}

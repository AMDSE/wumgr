using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        readonly Dictionary<string, CatalogEntry> detailCache = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        bool catalogBusy;
        readonly Button catalog = new Button { Text = "搜索微软目录", AutoSize = true };
        CancellationTokenSource catalogCancellation; readonly Dictionary<string, int> catalogPagesByDevice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); int catalogPages = 2;
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
            devices = DeviceDiscovery.MergeDevices(local, null); candidates = DriverCatalog.NormalizeCandidates(available);
            Text = UiLanguageService.Get("本机设备搜索 · 精确匹配驱动"); Font = DriverTheme.Font(9); BackColor = DriverTheme.Canvas; Size = new Size(1000, 720); MinimumSize = new Size(800, 580); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
            foreach (int h in new[] { 30, 34, 0, 52, 0, 42, 40 }) layout.RowStyles.Add(h == 0 ? new RowStyle(SizeType.Percent, 50) : new RowStyle(SizeType.Absolute, h));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = UiLanguageService.Get("搜索本机设备名称、硬件 ID、实例 ID 或制造商"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            query.Dock = DockStyle.Fill; query.AccessibleName = "本机设备搜索关键词"; layout.Controls.Add(query, 0, 1);
            Column(deviceGrid, UiLanguageService.Get("设备名称"), "Name", 180); Column(deviceGrid, UiLanguageService.Get("类别"), "Category", 70); Column(deviceGrid, UiLanguageService.Get("当前版本"), "InstalledVersion", 100); Column(deviceGrid, UiLanguageService.Get("设备状态"), "DisplayStatus", 100);
            layout.Controls.Add(deviceGrid, 0, 2);
            details.Dock = DockStyle.Fill; details.AutoEllipsis = true; details.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(details, 0, 3);
            Column(driverGrid, UiLanguageService.Get("适用驱动 / 型号"), "DeviceModel", 140); Column(driverGrid, UiLanguageService.Get("驱动版本"), "Version", 80); Column(driverGrid, UiLanguageService.Get("日期"), "DriverDate", 80); Column(driverGrid, UiLanguageService.Get("提供商"), "Provider", 80); Column(driverGrid, UiLanguageService.Get("状态"), "Status", 120); Column(driverGrid, UiLanguageService.Get("来源"), "Source", 100); Column(driverGrid, UiLanguageService.Get("产品"), "Products", 120); Column(driverGrid, UiLanguageService.Get("架构"), "Architecture", 80);
            driverGrid.Columns[2].DefaultCellStyle.Format = "yyyy-MM-dd"; layout.Controls.Add(driverGrid, 0, 4);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = UiLanguageService.Get("仅显示更新源已返回且硬件 ID / 兼容 ID 完整匹配的候选。空列表不代表设备没有驱动；旧版本不一定更适用。"), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "关闭", AutoSize = true, DialogResult = DialogResult.Cancel }; actions.Controls.Add(close); actions.Controls.Add(catalog); actions.Controls.Add(download); layout.Controls.Add(actions, 0, 6); CancelButton = close;
            query.TextChanged += (s, e) => RefreshDevices(); deviceGrid.SelectionChanged += (s, e) => RefreshDrivers(); driverGrid.SelectionChanged += (s, e) => UpdateSelection();
            download.Click += (s, e) => { UpdateSelection(); if (SelectedDriver != null) { DialogResult = DialogResult.OK; Close(); } };
            catalog.Click += async (s, e) => await SearchCatalogAsync();
            FormClosed += (s, e) => { if (catalogCancellation != null) { catalogCancellation.Cancel(); catalogCancellation.Dispose(); catalogCancellation = null; } };
            query.AccessibleName = UiLanguageService.Get("本机设备搜索关键词");
            deviceGrid.CellFormatting += (s, e) => { if ((e.ColumnIndex == 1 || e.ColumnIndex == 3) && e.Value != null) { e.Value = UiLanguageService.Display(Convert.ToString(e.Value)); e.FormattingApplied = true; } };
            driverGrid.CellFormatting += (s, e) => { if (e.ColumnIndex == 4 && e.Value != null) { e.Value = UiLanguageService.Display(Convert.ToString(e.Value)); e.FormattingApplied = true; } };
            RefreshDevices(); UiLanguageService.Apply(this);
        }
        async Task SearchCatalogAsync()
        {
            if (IsDisposed || Disposing) return;
            var device = deviceGrid.CurrentRow == null ? null : deviceGrid.CurrentRow.DataBoundItem as LocalDeviceInfo;
            if (device == null) return;
            var ids = DriverSearchCoordinator.HardwareQueries(device);
            if (ids.Count == 0) { MessageBox.Show(this, UiLanguageService.Get("当前设备没有可用于目录搜索的具体 Hardware ID。")); return; }
            if (catalogCancellation != null) catalogCancellation.Cancel();
            if (!catalogPagesByDevice.TryGetValue(device.InstanceId ?? "", out catalogPages)) catalogPages = 2;
            catalogCancellation = new CancellationTokenSource(); var owner = catalogCancellation; var token = owner.Token;
            catalogBusy = true; download.Enabled = false; catalog.Enabled = false; catalog.Text = UiLanguageService.Get("正在搜索微软目录…"); var diagnostics = new List<string>();
            try
            {
                using (var client = new MicrosoftUpdateCatalogClient())
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var id in ids)
                    {
                        token.ThrowIfCancellationRequested(); CatalogSearchResult result = null;
                        try { result = await client.SearchAsync(id, catalogPages, token).ConfigureAwait(true); if (result != null) diagnostics.AddRange(result.Diagnostics); if (result != null && result.Truncated) diagnostics.Add("目录查询达到" + catalogPages + "页上限：" + id); }
                        catch (OperationCanceledException) { throw; } catch (Exception ex) { diagnostics.Add("目录查询失败 " + id + "：" + ex.Message); continue; }
                        foreach (var raw in result == null ? new List<CatalogEntry>() : (result.Entries ?? new List<CatalogEntry>()))
                        {
                            token.ThrowIfCancellationRequested(); if (raw == null || !seen.Add(raw.Id ?? "")) continue; CatalogEntry detail = null;
                            try { if (!detailCache.TryGetValue(raw.Id, out detail)) { detail = await client.GetDetailsAsync(raw, token).ConfigureAwait(true); if (detailCache.Count < 2000) detailCache[raw.Id] = detail; } }
                            catch (OperationCanceledException) { throw; } catch (Exception ex) { diagnostics.Add("目录详情失败 " + raw.Id + "：" + ex.Message); continue; }
                            if (detail == null || !(detail.HardwareIds ?? new List<string>()).Any(x => (device.HardwareIds ?? new List<string>()).Concat(device.CompatibleIds ?? new List<string>()).Any(y => String.Equals(x.Trim(), y.Trim(), StringComparison.OrdinalIgnoreCase)))) continue;
                            var item = DriverSearchCoordinator.ReadCatalog(detail, new[] { device });
                            if (item != null && !candidates.Any(x => String.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase))) candidates.Add(item);
                        }
                    }
                }
                token.ThrowIfCancellationRequested(); if (IsDisposed || Disposing) return;
                catalogPages = Math.Min(20, catalogPages * 2); catalogPagesByDevice[device.InstanceId ?? ""] = catalogPages; RefreshDrivers();
                catalog.Text = catalogPages >= 20 ? UiLanguageService.Get("刷新微软目录") : UiLanguageService.Get("更多目录版本");
                if (diagnostics.Count > 0 && !IsDisposed) MessageBox.Show(this, String.Join("\r\n", diagnostics), UiLanguageService.Get("目录搜索诊断"));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { diagnostics.Add("目录搜索失败：" + ex.Message); if (!IsDisposed) MessageBox.Show(this, String.Join("\r\n", diagnostics), UiLanguageService.Get("微软目录搜索失败")); }
            finally { catalogBusy = false; if (!IsDisposed && !Disposing) { RefreshDrivers(); catalog.Enabled = true; catalog.Text = UiLanguageService.Get("更多目录版本"); } if (Object.ReferenceEquals(catalogCancellation, owner)) catalogCancellation = null; owner.Dispose(); }
        }

        void RefreshDevices()
        {
            string term = query.Text.Trim();
            deviceGrid.DataSource = devices.Where(d => term.Length == 0 || String.Join(" ", new[] { d.Name, d.InstanceId, d.Manufacturer, d.Class, d.Category, String.Join(";", d.HardwareIds), String.Join(";", d.CompatibleIds) }).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
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
            download.Enabled = !catalogBusy && SelectedDriver != null && SelectedDriver.CanDownload;
        }
    }
}

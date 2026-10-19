using System;
using System.Drawing;
using System.Windows.Forms;

namespace wumgr
{
    /// <summary>没有 WUA 更新候选也显示已安装驱动，不能用假候选填充。</summary>
    internal sealed class LocalDeviceCard : SoftPanel
    {
        readonly LocalDeviceInfo device;
        readonly ToolTip tip = new ToolTip();
        static string Known(string value) { return String.IsNullOrWhiteSpace(value) ? UiLanguageService.Get("未提供") : value; }
        internal static string InstalledText(LocalDeviceInfo d) { return UiLanguageService.T("已安装驱动：", "Installed driver: ") + Known(d.InstalledVersion); }
        internal static string DriverMetadata(LocalDeviceInfo d) { return UiLanguageService.T("提供商：", "Provider: ") + Known(d.InstalledProvider) + "  ·  " + UiLanguageService.T("驱动日期：", "Driver date: ") + Known(d.InstalledDate); }
        public LocalDeviceCard(LocalDeviceInfo value)
        {
            device = value;
            Height = 156; Width = 1090; Margin = new Padding(0, 0, 0, 12);
            AccessibleName = device.Name;
            var title = DriverTheme.Label(device.Name, 11, DriverTheme.Ink, FontStyle.Bold);
            title.SetBounds(24, 10, 760, 29); title.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            var category = DriverTheme.Label(UiLanguageService.Get(device.Category) + "  /  " + Known(device.Manufacturer), 8.5F, DriverTheme.Muted);
            category.SetBounds(24, 39, 760, 22); category.Anchor = title.Anchor;
            var installed = DriverTheme.Label(InstalledText(device), 9, DriverTheme.Ink);
            installed.SetBounds(24, 65, 760, 23); installed.Anchor = title.Anchor;
            var metadata = DriverTheme.Label(DriverMetadata(device), 8.5F, DriverTheme.Muted);
            metadata.SetBounds(24, 89, 760, 23); metadata.Anchor = title.Anchor;
            var note = DriverTheme.Label(UiLanguageService.Display(device.DisplayStatus) + " · " + UiLanguageService.T("当前更新源无匹配候选（不代表已是最新版）", "No matching update from current source (not a latest-version guarantee)"), 8, DriverTheme.Muted);
            note.SetBounds(24, 116, 880, 25); note.Anchor = title.Anchor;
            var details = new SoftButton(UiLanguageService.Get("详情")); details.SetBounds(950, 49, 120, 34); details.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            details.Click += (s, e) => MessageBox.Show(FindForm(), device.Name + "\r\n" + device.InstanceId + "\r\n\r\n" + InstalledText(device) + "\r\n" + DriverMetadata(device) + "\r\n\r\n" + UiLanguageService.Get("硬件 ID：") + String.Join("; ", device.HardwareIds) + "\r\n" + UiLanguageService.T("兼容 ID：", "Compatible IDs: ") + String.Join("; ", device.CompatibleIds) + "\r\n" + device.Class + " / " + device.ClassGuid + "\r\n\r\n" + UiLanguageService.T("没有更新候选不代表没有驱动、设备未识别或已是最新版。", "No update candidate does not mean the device is undetected, has no driver, or is up to date."), UiLanguageService.Get("详情"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            Controls.Add(title); Controls.Add(category); Controls.Add(installed); Controls.Add(metadata); Controls.Add(note); Controls.Add(details);
            tip.SetToolTip(title, device.Name + "\n" + device.InstanceId);
            tip.SetToolTip(installed, installed.Text); tip.SetToolTip(metadata, metadata.Text); tip.SetToolTip(note, note.Text);
            // Cards are created after Form autoscaling (scan/filter); scale fresh controls explicitly.
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                if (Math.Abs(graphics.DpiX - 96F) > 0.5F) Scale(new SizeF(graphics.DpiX / 96F, graphics.DpiY / 96F));
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}

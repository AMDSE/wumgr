using System;
using System.Drawing;
using System.Windows.Forms;

namespace wumgr
{
    /// <summary>没有 WUA 更新候选也是设备，不能用假驱动条目填充。</summary>
    internal sealed class LocalDeviceCard : SoftPanel
    {
        readonly LocalDeviceInfo device;
        readonly ToolTip tip = new ToolTip();
        public LocalDeviceCard(LocalDeviceInfo value)
        {
            device = value;
            Height = 112; Margin = new Padding(0, 0, 0, 12);
            AccessibleName = device.Name;
            var title = DriverTheme.Label(device.Name, 11, DriverTheme.Ink, FontStyle.Bold);
            title.SetBounds(24, 12, 760, 29); title.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            var category = DriverTheme.Label(UiLanguageService.Get(device.Category) + "  /  " + device.Manufacturer, 8.5F, DriverTheme.Muted);
            category.SetBounds(24, 42, 760, 22); category.Anchor = title.Anchor;
            string installed = String.IsNullOrWhiteSpace(device.InstalledVersion) ? UiLanguageService.Get("未提供") : device.InstalledVersion;
            var note = DriverTheme.Label(UiLanguageService.T("已安装：", "Installed: ") + installed + " · " + UiLanguageService.Display(device.DisplayStatus) + " · " + UiLanguageService.T("当前更新源无匹配候选", "No matching update from current source"), 8.5F, DriverTheme.Muted);
            note.SetBounds(24, 72, 760, 25); note.Anchor = title.Anchor;
            var details = new SoftButton(UiLanguageService.Get("详情")); details.SetBounds(950, 36, 120, 34); details.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            details.Click += (s, e) => MessageBox.Show(FindForm(), device.Name + "\r\n" + device.InstanceId + "\r\n\r\n" + UiLanguageService.Get("硬件 ID：") + String.Join("; ", device.HardwareIds) + "\r\n" + UiLanguageService.T("兼容 ID：", "Compatible IDs: ") + String.Join("; ", device.CompatibleIds) + "\r\n" + device.Class + " / " + device.ClassGuid + "\r\n\r\n" + UiLanguageService.T("没有更新候选不代表没有驱动、设备未识别或已是最新版。", "No update candidate does not mean the device is undetected, has no driver, or is up to date."), UiLanguageService.Get("详情"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            // Set a stable baseline before anchored children are added.
            Width = 1090;
            Controls.Add(title); Controls.Add(category); Controls.Add(note); Controls.Add(details);
            tip.SetToolTip(title, device.Name + "\n" + device.InstanceId);
            tip.SetToolTip(note, note.Text);
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}

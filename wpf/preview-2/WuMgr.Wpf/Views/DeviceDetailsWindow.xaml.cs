using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using WuMgr.Wpf.ViewModels;

namespace WuMgr.Wpf.Views
{
    public partial class DeviceDetailsWindow : Window
    {
        readonly DeviceRowViewModel row;
        readonly bool english;
        public DeviceDetailsWindow(DeviceRowViewModel row, bool english = false)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            this.row = row;
            this.english = english;
            InitializeComponent();
            DataContext = row;
            Title = english ? "Device details" : "设备详情";
            InstalledLabel.Text = english ? "Installed version" : "当前安装版本";
            ProviderLabel.Text = english ? "Installed provider" : "已安装驱动提供商";
            ManufacturerLabel.Text = english ? "Manufacturer" : "设备制造商";
            InstanceIdLabel.Text = english ? "Instance ID" : "设备实例 ID";
            HardwareIdsLabel.Text = english ? "Hardware IDs" : "硬件 ID";
            AvailableVersionsGroup.Header = english ? "Applicable candidates · select one version" : "适用候选 · 每台设备选择一个版本";
            CopyHardwareIdsButton.Content = english ? "Copy hardware IDs" : "复制硬件 ID";
            CloseButton.Content = english ? "Close" : "关闭";
        }
        void CopyHardwareIds_Click(object sender, RoutedEventArgs e) { Copy(row.HardwareIds); }
        void CopyUrlButton_Loaded(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            button.Content = english ? "Copy URLs" : "复制链接";
            AutomationProperties.SetName(button, english ? "Copy candidate download URLs" : "复制候选下载链接");
            var item = button.Tag as VersionItem;
            button.IsEnabled = item != null && item.Driver != null && item.Driver.DownloadUrls != null && item.Driver.DownloadUrls.Any(DeviceRowViewModel.IsSafeUrl);
        }
        void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).Tag as VersionItem;
            if (item?.Driver?.DownloadUrls == null) return;
            Copy(string.Join(Environment.NewLine, item.Driver.DownloadUrls.Where(DeviceRowViewModel.IsSafeUrl).Distinct()));
        }
        void Copy(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            try { Clipboard.SetText(text); }
            catch (ExternalException) { MessageBox.Show(this, english ? "The clipboard is busy. Please try again." : "剪贴板被占用，请稍后重试。", "WuMgr", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
    }
}

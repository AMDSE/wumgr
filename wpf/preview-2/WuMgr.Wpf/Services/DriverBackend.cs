using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using wumgr;

namespace WuMgr.Wpf.Services
{
    public sealed class DriverBackend : IBackend
    {
        readonly DriverService drivers = new DriverService();
        readonly DeviceDiscovery discovery = new DeviceDiscovery();
        readonly DriverUpdatePolicy policy = new DriverUpdatePolicy();
        public bool IsAdministrator { get { return DriverInstaller.IsAdministrator(); } }
        public async Task<IList<LocalDeviceInfo>> GetDevicesAsync(CancellationToken token, IProgress<string> progress)
        {
            return await discovery.ScanAsync(token, progress).ConfigureAwait(false);
        }
        public async Task<IList<DriverInfo>> GetCandidatesAsync(bool includeHistory, CancellationToken token, IProgress<string> progress)
        {
            return await drivers.ScanAsync(includeHistory, token, progress).ConfigureAwait(false);
        }
        public async Task DownloadAsync(IList<DriverInfo> driversToDownload, string folder, CancellationToken token, IProgress<DriverDownloadProgress> progress)
        {
            await drivers.DownloadAsync(driversToDownload, folder, token, progress).ConfigureAwait(false);
        }
        public async Task<DriverInstallResult> InstallAsync(IList<DriverInfo> driversToInstall, CancellationToken token, IProgress<DriverDownloadProgress> progress)
        {
            DriverInstallSummary result = await new DriverInstaller().InstallAsync(driversToInstall, token, progress).ConfigureAwait(false);
            return new DriverInstallResult
            {
                Success = result.Errors.Count == 0 && !result.Canceled,
                Canceled = result.Canceled,
                RebootRequired = result.RebootRequired,
                InstalledCount = result.InstalledCount,
                FailedCount = result.FailedCount,
                Message = result.Errors.Count == 0 ? "" : "安装存在失败项",
                Errors = result.Errors
            };
        }
        public Task<PolicyState> ReadPolicyAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                wumgr.PolicyState value = policy.Read();
                return new PolicyState
                {
                    Enabled = value.Enabled,
                    HasBackup = policy.HasBackup,
                    Description = value.Description + " 策略只读刷新；启用前会备份原值，恢复时还原原值。"
                };
            }, token);
        }
        public Task EnablePolicyAsync(CancellationToken token)
        {
            return Task.Run(() => { token.ThrowIfCancellationRequested(); policy.Enable(); }, token);
        }
        public Task RestorePolicyAsync(CancellationToken token)
        {
            return Task.Run(() => { token.ThrowIfCancellationRequested(); policy.Restore(); }, token);
        }
    }
}

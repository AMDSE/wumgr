using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using wumgr;

namespace WuMgr.Wpf.Services
{
    public interface IBackend
    {
        Task<IList<LocalDeviceInfo>> GetDevicesAsync(CancellationToken token, IProgress<string> progress);
        Task<IList<DriverInfo>> GetCandidatesAsync(bool includeHistory, CancellationToken token, IProgress<string> progress);
        Task DownloadAsync(IList<DriverInfo> drivers, string folder, CancellationToken token, IProgress<DriverDownloadProgress> progress);
        Task<DriverInstallResult> InstallAsync(IList<DriverInfo> drivers, CancellationToken token, IProgress<DriverDownloadProgress> progress);
        bool IsAdministrator { get; }
        Task<PolicyState> ReadPolicyAsync(CancellationToken token);
        Task EnablePolicyAsync(CancellationToken token);
        Task RestorePolicyAsync(CancellationToken token);
    }

    public sealed class DriverInstallResult
    {
        public bool Success;
        public bool Canceled;
        public bool RebootRequired;
        public int InstalledCount;
        public int FailedCount;
        public string Message = "";
        public IList<string> Errors = new List<string>();
    }

    public sealed class PolicyState
    {
        public bool Enabled;
        public bool HasBackup;
        public bool CanRestore { get { return HasBackup; } set { HasBackup = value; } }
        public string Description = "";
    }

    public interface IInteraction
    {
        bool Confirm(string title, string message);
        void ShowMessage(string title, string message);
        string ChooseFolder(string current);
        void OpenFolder(string path);
        void ShowDetails(WuMgr.Wpf.ViewModels.DeviceRowViewModel row);
        void SaveLanguage(bool english);
    }
}

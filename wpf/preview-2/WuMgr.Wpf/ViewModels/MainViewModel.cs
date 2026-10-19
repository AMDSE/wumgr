using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using wumgr;
using WuMgr.Wpf.Services;

namespace WuMgr.Wpf.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        readonly Action action;
        readonly Func<bool> predicate;
        public RelayCommand(Action action, Func<bool> predicate = null)
        {
            this.action = action;
            this.predicate = predicate;
        }
        public bool CanExecute(object parameter) { return predicate == null || predicate(); }
        // The operation itself also checks its guards: callers may bypass CanExecute.
        public void Execute(object parameter) { action(); }
        public event EventHandler CanExecuteChanged;
        public void Raise() { CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
        public void RaiseCanExecuteChanged() { Raise(); }
    }

    public sealed class CategoryItem : INotifyPropertyChanged
    {
        string name;
        public string Key { get; set; }
        public string Name
        {
            get { return name; }
            set { if (name != value) { name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); } }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class VersionItem : INotifyPropertyChanged
    {
        string status;
        public string Display { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string Date { get; set; }
        public string Provider { get; set; }
        public string UpdateId { get; set; }
        public DriverInfo Driver { get; set; }
        public string Status
        {
            get { return status; }
            set { if (status != value) { status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed class DeviceRowViewModel : INotifyPropertyChanged
    {
        readonly LocalDeviceInfo raw;
        bool selected, expanded, english;
        VersionItem selectedVersion;
        public DeviceRowViewModel(LocalDeviceInfo device, IEnumerable<DriverInfo> candidates)
        {
            raw = device ?? throw new ArgumentNullException(nameof(device));
            var versions = (candidates ?? Enumerable.Empty<DriverInfo>()).Where(x => x != null)
                .OrderByDescending(DirectMatch)
                .ThenBy(x => x.Superseded)
                .ThenByDescending(x => ParseVersion(x.Version))
                .ThenByDescending(x => x.DriverDate)
                .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase);
            Versions = new ObservableCollection<VersionItem>(versions.Select(ToVersion));
            selectedVersion = Versions.FirstOrDefault(IsDownloadable) ?? Versions.FirstOrDefault();
        }
        bool DirectMatch(DriverInfo driver)
        {
            var ids = new HashSet<string>(raw.HardwareIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return (driver.HardwareId ?? "").Split(';').Any(x => ids.Contains(x.Trim()));
        }
        static Version ParseVersion(string text)
        {
            Version version;
            return System.Version.TryParse(text, out version) ? version : new Version(0, 0);
        }
        static VersionItem ToVersion(DriverInfo driver)
        {
            return new VersionItem
            {
                Driver = driver, Title = driver.Title, Version = driver.Version,
                Date = driver.DriverDate == DateTime.MinValue ? "" : driver.DriverDate.ToString("yyyy-MM-dd"),
                Provider = driver.Provider, UpdateId = driver.Id, Status = driver.Status,
                Display = (driver.Version ?? "") + "  " + (driver.Title ?? "")
            };
        }
        public string Name { get { return raw.Name; } }
        public string Category { get { return raw.Category; } }
        public string Manufacturer { get { return raw.Manufacturer; } }
        public string InstalledVersion { get { return raw.InstalledVersion; } }
        public string InstalledProvider { get { return raw.InstalledProvider; } }
        public string InstalledDate { get { return raw.InstalledDate; } }
        public string HardwareIds { get { return string.Join("; ", raw.HardwareIds ?? new List<string>()); } }
        public string InstanceId { get { return raw.InstanceId; } }
        public string Status
        {
            get
            {
                string candidateStatus = selectedVersion?.Driver?.Status;
                return Localization.Display(raw.DisplayStatus + (string.IsNullOrWhiteSpace(candidateStatus) ? "" : " · " + candidateStatus), english);
            }
        }
        public string VersionSummary { get { return selectedVersion?.Display ?? ""; } }
        public bool IsSelected
        {
            get { return selected; }
            set
            {
                bool next = value && CanSelect;
                if (selected == next) return;
                selected = next;
                Notify(nameof(IsSelected));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public bool CanSelect { get { return IsDownloadable(selectedVersion); } }
        public bool HasCandidates { get { return Versions.Count > 0; } }
        public bool IsExpanded
        {
            get { return expanded; }
            set { if (expanded != value) { expanded = value; Notify(nameof(IsExpanded)); } }
        }
        public ObservableCollection<VersionItem> Versions { get; private set; }
        public VersionItem SelectedVersion
        {
            get { return selectedVersion; }
            set
            {
                if (value == null || !Versions.Contains(value) || selectedVersion == value) return;
                selectedVersion = value;
                if (!CanSelect) selected = false;
                foreach (string property in new[] { nameof(SelectedVersion), nameof(VersionSummary), nameof(CanSelect), nameof(IsSelected), nameof(Status) }) Notify(property);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public ICommand DetailsCommand { get; internal set; }
        public LocalDeviceInfo Raw { get { return raw; } }
        internal static bool IsDownloadable(VersionItem item)
        {
            return item?.Driver?.DownloadUrls != null && item.Driver.DownloadUrls.Any(IsSafeUrl);
        }
        internal static bool IsSafeUrl(string value)
        {
            Uri url;
            return Uri.TryCreate(value, UriKind.Absolute, out url)
                && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
                && string.IsNullOrEmpty(url.UserInfo);
        }
        public void RefreshDisplay(bool useEnglish)
        {
            english = useEnglish;
            RefreshDriverStatus();
        }
        public void RefreshDriverStatus()
        {
            foreach (VersionItem item in Versions) item.Status = Localization.Display(item.Driver?.Status, english);
            Notify(nameof(Status));
            Notify(nameof(VersionSummary));
        }
        void Notify(string property) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property)); }
        public event EventHandler SelectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// UI-neutral coordinator. Every public operation begins on the UI context.
    /// Only IBackend owns background work; await resumes on the caller's UI context.
    /// A single task owns the cancellation source until the actual worker returns.
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        readonly IBackend backend;
        readonly IInteraction interaction;
        readonly List<DeviceRowViewModel> allRows = new List<DeviceRowViewModel>();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        CancellationTokenSource operation;
        Task currentTask = Task.CompletedTask;
        Services.PolicyState policyState;
        bool initialized, busy, disposed, lifetimeDisposed, cancelRequested;
        readonly bool demo;
        bool english, includeHistory, install;
        string search = "", folder = "", status = "就绪", policySource = "", progressText = "", log = "";
        double progress;
        long generation;
        DateTime lastProgress, lastStatus;
        CategoryItem selectedCategory;

        public MainViewModel(IBackend backend, IInteraction interaction, bool demo, bool english)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
            this.demo = demo;
            this.english = english;
            if (SynchronizationContext.Current == null) throw new InvalidOperationException("A UI synchronization context is required.");
            ScanCommand = new RelayCommand(() => Start(ScanAsync), () => CanStart && !demo);
            RefreshDevicesCommand = new RelayCommand(() => Start(RefreshDevicesAsync), () => CanStart);
            CancelCommand = new RelayCommand(Cancel, () => busy && !cancelRequested);
            DownloadCommand = new RelayCommand(() => Start(DownloadAsync), () => CanStart && !demo && SelectedCount > 0);
            AutoSelectCommand = new RelayCommand(AutoSelect, () => CanStart);
            ClearSelectionCommand = new RelayCommand(ClearSelection, () => CanStart);
            BrowseCommand = new RelayCommand(Browse, () => CanStart && !demo);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => CanStart && !demo && !string.IsNullOrWhiteSpace(folder));
            EnablePolicyCommand = new RelayCommand(() => Start(token => PolicyAsync(true, token)), () => CanStart && !demo);
            RestorePolicyCommand = new RelayCommand(() => Start(token => PolicyAsync(false, token)), () => CanStart && !demo && policyState?.CanRestore == true);
            ToggleLanguageCommand = new RelayCommand(() => IsEnglish = !IsEnglish, () => !disposed);
        }
        bool CanStart { get { return !busy && !disposed; } }
        public ObservableCollection<DeviceRowViewModel> Devices { get; } = new ObservableCollection<DeviceRowViewModel>();
        public ObservableCollection<CategoryItem> Categories { get; } = new ObservableCollection<CategoryItem>();
        public CategoryItem SelectedCategory
        {
            get { return selectedCategory; }
            set { if (selectedCategory != value) { selectedCategory = value; Notify(nameof(SelectedCategory)); ApplyFilter(); } }
        }
        public string SearchText
        {
            get { return search; }
            set { value = value ?? ""; if (search != value) { search = value; Notify(nameof(SearchText)); ApplyFilter(); } }
        }
        public bool IncludeHistory
        {
            get { return includeHistory; }
            set { if (!busy && includeHistory != value) { includeHistory = value; Notify(nameof(IncludeHistory)); } }
        }
        public bool InstallAfterDownload
        {
            get { return install; }
            set { if (!busy && install != value) { install = value; Notify(nameof(InstallAfterDownload)); } }
        }
        public string DownloadFolder
        {
            get { return folder; }
            set { if (!busy) SetFolder(value); }
        }
        void SetFolder(string value)
        {
            value = value ?? "";
            if (folder == value) return;
            folder = value;
            Notify(nameof(DownloadFolder));
            RaiseCommands();
        }
        public bool IsBusy { get { return busy; } }
        public bool IsIdle { get { return !busy; } }
        public bool IsDemo { get { return demo; } }
        public bool IsEnglish
        {
            get { return english; }
            set
            {
                if (disposed || english == value) return;
                english = value;
                Notify(nameof(IsEnglish));
                foreach (DeviceRowViewModel row in allRows) row.RefreshDisplay(english);
                UpdateCategoryNames();
                NotifyLabels();
                try { interaction.SaveLanguage(english); }
                catch (Exception ex) { AppendLog(ex.Message); }
            }
        }
        public bool CancelRequested { get { return cancelRequested; } }
        public string Title { get { return this["AppName"]; } }
        public string Subtitle { get { return this["Workspace"]; } }
        public string Summary { get { return this["SelectionLabel"] + ": " + SelectedCount + "  ·  " + this["DeviceCountLabel"] + ": " + DeviceCount + "  ·  " + this["CandidateCountLabel"] + ": " + CandidateCount; } }
        public string StatusText { get { return Localization.Display(status, english); } }
        public string PolicyText { get { return Localization.Display(policySource, english); } }
        public string ProgressText { get { return Localization.Display(progressText, english); } }
        public string LogText { get { return Localization.Display(log, english); } }
        public double ProgressValue { get { return progress; } }
        public int SelectedCount { get { return allRows.Count(row => row.IsSelected && row.CanSelect); } }
        public int DeviceCount { get { return Devices.Count; } }
        public int CandidateCount { get { return Devices.Sum(row => row.Versions.Count); } }
        public string this[string key] { get { return Localization.Get(key, english); } }
        public ICommand ScanCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand AutoSelectCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand BrowseCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand EnablePolicyCommand { get; }
        public ICommand RestorePolicyCommand { get; }
        public ICommand ToggleLanguageCommand { get; }

        public Task InitializeAsync()
        {
            if (initialized || disposed) return currentTask;
            initialized = true;
            return Start(RefreshDevicesAsync);
        }
        Task Start(Func<CancellationToken, Task> work)
        {
            if (!CanStart) return currentTask;
            busy = true;
            cancelRequested = false;
            generation++;
            ResetProgress();
            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            NotifyBusy();
            currentTask = RunAsync(work, operation.Token);
            return currentTask;
        }
        async Task RunAsync(Func<CancellationToken, Task> work, CancellationToken token)
        {
            // Assign currentTask before the operation can complete or invoke a dialog.
            await Task.Yield();
            try { await work(token); }
            catch (OperationCanceledException) { SetStatus("已请求取消；已完成的操作不会回滚"); }
            catch (Exception ex) { SetStatus("操作失败：" + ex.Message); AppendLog(ex.ToString()); }
            finally
            {
                foreach (DeviceRowViewModel row in allRows) row.RefreshDriverStatus();
                operation.Dispose();
                operation = null;
                busy = false;
                NotifyBusy();
                if (disposed) DisposeLifetime();
            }
        }
        public async Task WaitForIdleAsync()
        {
            while (busy) await currentTask;
        }
        public void Cancel()
        {
            if (!busy || cancelRequested) return;
            cancelRequested = true;
            Notify(nameof(CancelRequested));
            AppendLog("COM/WUA 操作不能立即中断，将等待后台操作真正结束。");
            operation.Cancel();
            RaiseCommands();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Cancel();
            lifetime.Cancel();
            RaiseCommands();
            if (!busy) DisposeLifetime();
        }
        void DisposeLifetime()
        {
            if (lifetimeDisposed) return;
            lifetimeDisposed = true;
            lifetime.Dispose();
        }

        async Task RefreshDevicesAsync(CancellationToken token)
        {
            IList<LocalDeviceInfo> devices = demo ? DemoData.Devices() : await backend.GetDevicesAsync(token, NewStatusProgress());
            token.ThrowIfCancellationRequested();
            SetRows(devices, demo ? DemoData.Candidates() : null);
            SetStatus("设备发现完成：" + allRows.Count);
            if (!demo)
            {
                try { await ReadPolicyAsync(token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { SetPolicyUnknown(ex.Message); }
            }
        }
        async Task ScanAsync(CancellationToken token)
        {
            if (demo) { SetStatus(this["DemoNotice"]); return; }
            IList<LocalDeviceInfo> devices = await backend.GetDevicesAsync(token, NewStatusProgress());
            token.ThrowIfCancellationRequested();
            SetRows(devices, null); // Device inventory becomes visible before the WUA query.
            try
            {
                IList<DriverInfo> candidates = await backend.GetCandidatesAsync(includeHistory, token, NewStatusProgress());
                token.ThrowIfCancellationRequested();
                SetRows(devices, candidates);
                SetStatus("更新扫描完成");
            }
            catch (OperationCanceledException) { SetStatus("WUA 扫描已取消；当前设备清单保留"); }
            catch (Exception ex) { SetStatus("WUA 扫描失败；当前设备清单保留：" + ex.Message); AppendLog(ex.ToString()); }
        }
        void SetRows(IList<LocalDeviceInfo> devices, IList<DriverInfo> candidates)
        {
            if (devices == null) return;
            foreach (DeviceRowViewModel row in allRows) row.SelectionChanged -= SelectionChanged;
            var normalized = DriverCatalog.NormalizeCandidates(candidates ?? Enumerable.Empty<DriverInfo>());
            allRows.Clear();
            foreach (LocalDeviceInfo device in devices.Where(x => x != null))
            {
                var row = new DeviceRowViewModel(device, DeviceDiscovery.Match(device, normalized));
                row.RefreshDisplay(english);
                row.SelectionChanged += SelectionChanged;
                row.DetailsCommand = new RelayCommand(() =>
                {
                    if (disposed) return;
                    try { interaction.ShowDetails(row); }
                    catch (Exception ex) { ShowError(ex.Message); }
                });
                allRows.Add(row);
            }
            RebuildCategories();
            ApplyFilter();
        }
        void RebuildCategories()
        {
            string oldKey = selectedCategory?.Key ?? "common";
            Categories.Clear();
            Categories.Add(new CategoryItem { Key = "common" });
            Categories.Add(new CategoryItem { Key = "all" });
            foreach (string category in allRows.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                Categories.Add(new CategoryItem { Key = category });
            UpdateCategoryNames();
            selectedCategory = Categories.FirstOrDefault(x => string.Equals(x.Key, oldKey, StringComparison.OrdinalIgnoreCase)) ?? Categories[0];
            Notify(nameof(SelectedCategory));
        }
        void UpdateCategoryNames()
        {
            foreach (CategoryItem item in Categories)
                item.Name = item.Key == "common" ? this["CommonDevices"] : item.Key == "all" ? this["AllDevices"] : Localization.Display(item.Key, english);
        }
        void ApplyFilter()
        {
            Devices.Clear();
            string query = search.Trim();
            foreach (DeviceRowViewModel row in allRows)
            {
                string key = selectedCategory?.Key ?? "common";
                bool category = key == "all" || (key == "common" && CommonDeviceFilter.IsCommon(row.Raw)) || string.Equals(key, row.Category, StringComparison.OrdinalIgnoreCase);
                bool matches = query.Length == 0 || SearchValues(row).Any(x => (x ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                if (category && matches) Devices.Add(row);
            }
            foreach (string name in new[] { nameof(DeviceCount), nameof(CandidateCount), nameof(SelectedCount), nameof(Summary) }) Notify(name);
            RaiseCommands();
        }
        IEnumerable<string> SearchValues(DeviceRowViewModel row)
        {
            return new[] { row.Name, row.Category, row.Raw.Class, Localization.Display(row.Category, true), row.Manufacturer,
                row.HardwareIds, string.Join(";", row.Raw.CompatibleIds ?? new List<string>()), row.InstanceId, row.InstalledVersion, row.InstalledProvider }
                .Concat(row.Versions.SelectMany(x => new[] { x.Title, x.Version, x.Provider, x.UpdateId }));
        }
        void SelectionChanged(object sender, EventArgs e)
        {
            Notify(nameof(SelectedCount));
            Notify(nameof(Summary));
            RaiseCommands();
        }
        void AutoSelect()
        {
            if (!CanStart) return;
            foreach (DeviceRowViewModel row in allRows)
            {
                row.IsSelected = false;
                var safe = row.Versions.Where(DeviceRowViewModel.IsDownloadable).Select(x => x.Driver);
                DriverInfo chosen = DeviceDiscovery.SelectAutomatic(new[] { row.Raw }, safe).FirstOrDefault();
                if (chosen == null) continue;
                row.SelectedVersion = row.Versions.First(x => ReferenceEquals(x.Driver, chosen));
                row.IsSelected = true;
            }
        }
        void ClearSelection()
        {
            if (!CanStart) return;
            foreach (DeviceRowViewModel row in allRows) row.IsSelected = false;
        }
        static string Identity(DriverInfo driver)
        {
            return string.IsNullOrWhiteSpace(driver.Id)
                ? "missing:" + RuntimeHelpers.GetHashCode(driver)
                : driver.Id.Trim() + "|" + driver.RevisionNumber;
        }
        string Describe(IEnumerable<DriverInfo> drivers)
        {
            return string.Join(Environment.NewLine, drivers.Select(x => (x.Title ?? "") + " | " + (x.Version ?? "") + " | " + (x.Id ?? "") + " rev " + x.RevisionNumber));
        }
        async Task DownloadAsync(CancellationToken token)
        {
            if (demo) { SetStatus(this["DemoNotice"]); return; }
            // All material choices are fixed before any await or confirmation.
            bool installSnapshot = install;
            var snapshot = allRows.Where(x => x.IsSelected && x.CanSelect).Select(x => x.SelectedVersion.Driver)
                .GroupBy(Identity, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            if (snapshot.Count == 0) { SetStatus(this["NoCandidates"]); return; }
            string folderSnapshot = folder;
            if (string.IsNullOrWhiteSpace(folderSnapshot)) folderSnapshot = ChooseFolder(folder);
            if (string.IsNullOrWhiteSpace(folderSnapshot)) { SetStatus("操作已取消（未开始）"); return; }
            folderSnapshot = Path.GetFullPath(folderSnapshot);
            token.ThrowIfCancellationRequested();
            if (!Confirm(this["Download"], this["DownloadConfirm"] + Environment.NewLine + folderSnapshot + Environment.NewLine + Describe(snapshot)))
            { SetStatus("操作已取消（未开始）"); return; }
            token.ThrowIfCancellationRequested();
            bool downloading = true;
            try
            {
                await backend.DownloadAsync(snapshot, folderSnapshot, token, NewDownloadProgress());
            }
            catch (OperationCanceledException) { SetStatus("下载已取消；不会安装"); return; }
            catch (Exception ex) { AppendLog("下载失败：" + ex.Message); }
            finally { foreach (DeviceRowViewModel row in allRows) row.RefreshDriverStatus(); }
            if (token.IsCancellationRequested) { SetStatus("下载已取消；不会安装"); return; }
            var successful = snapshot.Where(DriverInstaller.HasDownloadedPackage).ToList();
            string summary = "下载成功项：" + successful.Count + "；下载失败项：" + (snapshot.Count - successful.Count);
            SetStatus(summary);
            if (!installSnapshot || successful.Count == 0) return;
            if (!backend.IsAdministrator) { ShowError("安装需要管理员权限；已保留下载，不安装。"); return; }
            if (!Confirm(this["Install"], this["InstallConfirm"] + Environment.NewLine + Describe(successful))) return;
            token.ThrowIfCancellationRequested();
            downloading = false;
            try
            {
                DriverInstallResult result = await backend.InstallAsync(successful, token, NewDownloadProgress());
                string installed = "安装完成项：" + result.InstalledCount + "；失败项：" + result.FailedCount;
                if (result.RebootRequired) installed += "；需要重启";
                if (result.Canceled || token.IsCancellationRequested) installed += "；已取消后续项，已完成安装不会回滚";
                if (!string.IsNullOrWhiteSpace(result.Message)) installed += "；" + result.Message;
                if (result.Errors != null && result.Errors.Count > 0) installed += "；" + string.Join(" | ", result.Errors);
                SetStatus(installed);
            }
            catch (OperationCanceledException)
            {
                SetStatus(downloading ? "下载已取消；不会安装" : "已取消后续项，已完成安装不会回滚；请查看安装日志");
            }
        }
        async Task PolicyAsync(bool enable, CancellationToken token)
        {
            if (demo) { SetStatus(this["DemoNotice"]); return; }
            if (!backend.IsAdministrator) { ShowError("修改全机策略需要管理员权限。"); return; }
            try { await ReadPolicyAsync(token); }
            catch { SetPolicyUnknown("策略读取失败"); throw; }
            token.ThrowIfCancellationRequested();
            if (!enable && policyState?.CanRestore != true) { ShowError("没有可恢复的策略备份"); return; }
            if (!Confirm(this["Protection"], this[enable ? "PolicyEnableConfirm" : "PolicyRestoreConfirm"])) return;
            token.ThrowIfCancellationRequested();
            if (enable) await backend.EnablePolicyAsync(token); else await backend.RestorePolicyAsync(token);
            // A submitted registry change may have completed despite cancellation.
            await ReadPolicyAsync(CancellationToken.None);
            SetStatus(token.IsCancellationRequested ? "已请求取消；已完成的操作不会回滚" : "策略操作完成");
        }
        async Task ReadPolicyAsync(CancellationToken token)
        {
            Services.PolicyState value = await backend.ReadPolicyAsync(token);
            token.ThrowIfCancellationRequested();
            if (value == null) throw new InvalidOperationException("策略读取失败");
            policyState = value;
            policySource = value.Description + " 当前状态：" + (value.Enabled ? "已启用" : "未启用") + (value.HasBackup ? "；可恢复备份" : "");
            Notify(nameof(PolicyText));
            RaiseCommands();
        }
        void SetPolicyUnknown(string error)
        {
            policyState = null;
            policySource = "策略读取失败：" + error;
            Notify(nameof(PolicyText));
            AppendLog(policySource);
            RaiseCommands();
        }
        void Browse() { if (CanStart && !demo) ChooseFolder(folder); }
        string ChooseFolder(string current)
        {
            try
            {
                string selected = interaction.ChooseFolder(current);
                if (!string.IsNullOrWhiteSpace(selected)) SetFolder(selected);
                return selected;
            }
            catch (Exception ex) { ShowError(ex.Message); return null; }
        }
        void OpenFolder()
        {
            if (!CanStart || demo) return;
            try { interaction.OpenFolder(folder); }
            catch (Exception ex) { ShowError(ex.Message); }
        }
        bool Confirm(string title, string message)
        {
            try { return interaction.Confirm(title, Localization.Display(message, english)); }
            catch (Exception ex) { ShowError(ex.Message); return false; }
        }
        void ShowError(string message)
        {
            try { interaction.ShowMessage(this["AppName"], Localization.Display(message, english)); }
            catch (Exception ex) { AppendLog(ex.Message); }
        }
        IProgress<string> NewStatusProgress()
        {
            long expected = generation;
            return new Progress<string>(value =>
            {
                if (!busy || disposed || expected != generation) return;
                DateTime now = DateTime.UtcNow;
                if ((now - lastStatus).TotalMilliseconds < 100) return;
                lastStatus = now;
                SetStatus(value);
            });
        }
        IProgress<DriverDownloadProgress> NewDownloadProgress()
        {
            long expected = generation;
            return new Progress<DriverDownloadProgress>(value =>
            {
                if (value == null || !busy || disposed || expected != generation) return;
                DateTime now = DateTime.UtcNow;
                if ((now - lastProgress).TotalMilliseconds < 100 && value.Percent < 100) return;
                lastProgress = now;
                progress = Math.Max(0, Math.Min(100, value.Percent));
                progressText = value.Message ?? "";
                Notify(nameof(ProgressValue));
                Notify(nameof(ProgressText));
                foreach (DeviceRowViewModel row in allRows) row.RefreshDriverStatus();
            });
        }
        void ResetProgress()
        {
            progress = 0; progressText = ""; lastProgress = lastStatus = DateTime.MinValue;
            Notify(nameof(ProgressValue)); Notify(nameof(ProgressText)); Notify(nameof(CancelRequested));
        }
        void SetStatus(string value)
        {
            status = value ?? "";
            Notify(nameof(StatusText));
            AppendLog(status);
        }
        void AppendLog(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            log = (log.Length == 0 ? "" : log + Environment.NewLine) + value;
            if (log.Length > 20000) log = log.Substring(log.Length - 20000);
            Notify(nameof(LogText));
        }
        void NotifyBusy()
        {
            Notify(nameof(IsBusy)); Notify(nameof(IsIdle)); RaiseCommands();
        }
        void NotifyLabels()
        {
            foreach (string name in new[] { nameof(Title), nameof(Subtitle), nameof(Summary), nameof(StatusText), nameof(PolicyText), nameof(ProgressText), nameof(LogText), "Item[]" }) Notify(name);
            RaiseCommands();
        }
        void RaiseCommands()
        {
            foreach (ICommand command in new[] { ScanCommand, RefreshDevicesCommand, CancelCommand, DownloadCommand, AutoSelectCommand,
                ClearSelectionCommand, BrowseCommand, OpenFolderCommand, EnablePolicyCommand, RestorePolicyCommand, ToggleLanguageCommand })
                (command as RelayCommand)?.Raise();
        }
        void Notify(string property) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property)); }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using wumgr;
using WuMgr.Wpf.Services;
using WuMgr.Wpf.ViewModels;

sealed class PumpContext : SynchronizationContext
{
    readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
    public override void Post(SendOrPostCallback callback, object state) { queue.Add(() => callback(state)); }
    public void Finish() { queue.CompleteAdding(); }
    public void Run() { foreach (Action action in queue.GetConsumingEnumerable()) action(); }
}
sealed class FakeBackend : IBackend
{
    public int DeviceReads, CandidateReads, Downloads, Installs, PolicyReads, Enables, Restores;
    public bool IsAdministrator { get; set; } = true;
    public bool HistoryRequested, PolicyEnabled, HasBackup;
    public IList<LocalDeviceInfo> Devices = new List<LocalDeviceInfo>();
    public IList<DriverInfo> Candidates = new List<DriverInfo>();
    public Func<CancellationToken, Task<IList<DriverInfo>>> CandidateBody;
    public Func<CancellationToken, Task<WuMgr.Wpf.Services.PolicyState>> PolicyBody;
    public Func<IList<DriverInfo>, string, CancellationToken, Task> DownloadBody;
    public Func<IList<DriverInfo>, CancellationToken, Task<DriverInstallResult>> InstallBody;
    public string LastFolder;
    public IList<DriverInfo> LastDownloaded, LastInstalled;
    public IProgress<DriverDownloadProgress> LastProgress;
    public Task<IList<LocalDeviceInfo>> GetDevicesAsync(CancellationToken token, IProgress<string> progress)
    { DeviceReads++; return Task.FromResult(Devices); }
    public Task<IList<DriverInfo>> GetCandidatesAsync(bool history, CancellationToken token, IProgress<string> progress)
    { CandidateReads++; HistoryRequested = history; return CandidateBody != null ? CandidateBody(token) : Task.FromResult(Candidates); }
    public async Task DownloadAsync(IList<DriverInfo> drivers, string folder, CancellationToken token, IProgress<DriverDownloadProgress> progress)
    {
        Downloads++; LastDownloaded = drivers; LastFolder = folder; LastProgress = progress;
        if (DownloadBody != null) { await DownloadBody(drivers, folder, token); return; }
        foreach (DriverInfo driver in drivers) MarkDownloaded(driver, folder);
    }
    public static void MarkDownloaded(DriverInfo driver, string folder)
    {
        driver.LocalPath = Path.Combine(folder, driver.Id ?? "missing");
        Directory.CreateDirectory(driver.LocalPath);
        driver.Status = DriverInstaller.DownloadedStatus;
    }
    public Task<DriverInstallResult> InstallAsync(IList<DriverInfo> drivers, CancellationToken token, IProgress<DriverDownloadProgress> progress)
    {
        Installs++; LastInstalled = drivers;
        if (InstallBody != null) return InstallBody(drivers, token);
        foreach (DriverInfo driver in drivers) driver.Status = "已安装";
        return Task.FromResult(new DriverInstallResult { Success = true, InstalledCount = drivers.Count });
    }
    public Task<WuMgr.Wpf.Services.PolicyState> ReadPolicyAsync(CancellationToken token)
    {
        PolicyReads++;
        return PolicyBody != null ? PolicyBody(token) : Task.FromResult(new WuMgr.Wpf.Services.PolicyState { Enabled = PolicyEnabled, HasBackup = HasBackup, Description = "policy" });
    }
    public Task EnablePolicyAsync(CancellationToken token) { Enables++; HasBackup = true; PolicyEnabled = true; return Task.CompletedTask; }
    public Task RestorePolicyAsync(CancellationToken token) { Restores++; HasBackup = false; PolicyEnabled = false; return Task.CompletedTask; }
}
sealed class FakeInteraction : IInteraction
{
    readonly int uiThread = Thread.CurrentThread.ManagedThreadId;
    public int Saves, Confirms, Messages, Choosers, Opens, Details;
    public bool WrongThread, ThrowChoose, ThrowSave, ThrowOpen;
    public string Folder;
    public Queue<bool> Answers = new Queue<bool>();
    public List<string> Questions = new List<string>();
    public List<string> Notices = new List<string>();
    void CheckThread() { if (Thread.CurrentThread.ManagedThreadId != uiThread) WrongThread = true; }
    public bool Confirm(string title, string message) { CheckThread(); Confirms++; Questions.Add(message); return Answers.Count == 0 || Answers.Dequeue(); }
    public void ShowMessage(string title, string message) { CheckThread(); Messages++; Notices.Add(message); }
    public string ChooseFolder(string current) { CheckThread(); Choosers++; if (ThrowChoose) throw new IOException("chooser failed"); return Folder; }
    public void OpenFolder(string path) { CheckThread(); Opens++; if (ThrowOpen) throw new IOException("shell failed"); }
    public void ShowDetails(DeviceRowViewModel row) { CheckThread(); Details++; }
    public void SaveLanguage(bool english) { CheckThread(); Saves++; if (ThrowSave) throw new IOException("save failed"); }
}
static class VMTests
{
    static int count;
    static string root;
    static void Check(bool condition, string description)
    {
        count++;
        if (!condition) throw new Exception("Assertion " + count + ": " + description);
        Console.WriteLine("PASS " + description);
    }
    public static void Main()
    {
        var context = new PumpContext();
        SynchronizationContext.SetSynchronizationContext(context);
        root = Path.Combine(Path.GetTempPath(), "wumgr-wpf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Task task = Run();
        task.ContinueWith(_ => context.Finish(), TaskScheduler.Default);
        context.Run();
        try { task.GetAwaiter().GetResult(); Console.WriteLine("Assertions: " + count); }
        finally { Directory.Delete(root, true); }
    }
    static LocalDeviceInfo Device(string instance, string hardware, string cls = "Display", string installed = "1.0")
    {
        return new LocalDeviceInfo { InstanceId = instance, Name = instance, Class = cls, InstalledVersion = installed, Manufacturer = "Fixture", InstalledProvider = "Fixture", ProblemCode = 0, HardwareIds = new List<string> { hardware } };
    }
    static DriverInfo Driver(string hardware, string version = "2.0", string id = null, string url = "https://example.invalid/package.cab")
    {
        return new DriverInfo { Id = id ?? Guid.NewGuid().ToString(), RevisionNumber = 3, HardwareId = hardware, Version = version, Title = "Fixture " + version + " " + hardware, Provider = "Fixture", Manufacturer = "Fixture", DriverClass = "Display", DriverDate = new DateTime(2025, 1, 1), Status = "候选", DownloadUrls = new List<string> { url } };
    }
    static FakeBackend Backend()
    {
        return new FakeBackend { Devices = new List<LocalDeviceInfo> { Device("GPU", "PCI\\GPU"), Device("Audio", "HDAUDIO\\AUDIO", "MEDIA"), Device("Unknown", "ACPI\\UNKNOWN", "Unknown") }, Candidates = new List<DriverInfo> { Driver("PCI\\GPU"), Driver("HDAUDIO\\AUDIO") } };
    }
    static MainViewModel VM(FakeBackend backend, FakeInteraction interaction, bool demo = false)
    { return new MainViewModel(backend, interaction, demo, false) { DownloadFolder = root }; }
    static async Task Scan(MainViewModel vm) { vm.ScanCommand.Execute(null); await vm.WaitForIdleAsync(); }
    static async Task Ready(MainViewModel vm) { await vm.InitializeAsync(); await Scan(vm); }
    static async Task Until(Func<bool> predicate)
    {
        for (int i = 0; i < 2000 && !predicate(); i++) await Task.Delay(1);
        if (!predicate()) throw new TimeoutException("Controlled fake did not reach expected stage.");
    }
    static async Task Download(MainViewModel vm) { vm.DownloadCommand.Execute(null); await vm.WaitForIdleAsync(); }
    static async Task Run()
    {
        await InventoryAndSelection();
        await DemoAndErrors();
        await DownloadsAndInstallation();
        await CancellationAndConcurrency();
        await PolicyAndInitialization();
    }
    static async Task InventoryAndSelection()
    {
        var b = Backend(); var i = new FakeInteraction(); var vm = VM(b, i);
        await vm.InitializeAsync();
        Check(b.DeviceReads == 1 && b.CandidateReads == 0, "startup reads devices without a WUA search");
        Check(b.PolicyReads == 1 && b.Enables == 0 && b.Restores == 0, "startup reads policy without changing it");
        Check(vm.SelectedCategory.Key == "common", "common hardware is the default category");
        Check(vm.DeviceCount == 2, "common hardware handles localized categories");
        Check(vm.CandidateCount == 0, "offline inventory does not invent candidates");
        vm.SelectedCategory = vm.Categories.Single(x => x.Key == "all");
        Check(vm.DeviceCount == 3, "all devices retains unknown hardware");
        await vm.InitializeAsync(); Check(b.DeviceReads == 1, "initialization is idempotent");
        vm.IncludeHistory = true; await Scan(vm);
        Check(b.CandidateReads == 1 && b.HistoryRequested, "history choice reaches backend");
        Check(vm.CandidateCount == 2, "only exact hardware matches get candidates");
        var row = vm.Devices.Single(x => x.Name == "GPU");
        var unknown = vm.Devices.Single(x => x.Name == "Unknown");
        unknown.IsSelected = true; Check(!unknown.IsSelected, "device without URLs cannot be selected");
        int changes = 0; vm.DownloadCommand.CanExecuteChanged += (s, e) => changes++;
        row.IsSelected = true;
        Check(vm.SelectedCount == 1 && vm.DownloadCommand.CanExecute(null) && changes > 0, "row selection updates count and command notification");
        vm.SearchText = "no results"; Check(vm.DeviceCount == 0 && vm.SelectedCount == 1, "hidden selection remains explicit in summary");
        vm.SearchText = "GPU"; Check(ReferenceEquals(row, vm.Devices.Single()), "search retains row identity and selection");
        vm.SearchText = "Fixture"; Check(vm.DeviceCount == 3, "provider search includes installed metadata");
        vm.SearchText = "";
        vm.IsEnglish = true;
        Check(vm.Title.Contains("Preview") && vm["Scan"] == "Scan updates", "English labels change immediately");
        Check(vm.Categories.Single(x => x.Key == "common").Name == "Common hardware", "category names notify in English");
        Check(!row.Status.Contains("正常") && !row.Status.Contains("候选"), "device and candidate status translate without changing raw metadata");
        Check(row.Raw.DisplayStatus == "正常", "language changes do not mutate raw inventory");
        vm.IsEnglish = false; Check(i.Saves == 2, "language preference is saved on both switches");
        row.DetailsCommand.Execute(null); Check(i.Details == 1 && !i.WrongThread, "detail interaction runs on UI thread");
        vm.RefreshDevicesCommand.Execute(null); await vm.WaitForIdleAsync();
        Check(b.CandidateReads == 1 && vm.CandidateCount == 0, "refresh devices does not silently start WUA");
        vm.Dispose(); Check(!vm.ScanCommand.CanExecute(null) && !vm.RefreshDevicesCommand.CanExecute(null), "disposed VM disables commands");
        vm.DownloadCommand.Execute(null); await vm.WaitForIdleAsync(); Check(b.Downloads == 0, "disposed operation execution is guarded");

        var d = Device("D", "ID");
        var safe = Driver("ID", "2.0"); var noUrl = Driver("ID", "99.0", url: "file:///bad");
        var r = new DeviceRowViewModel(d, new[] { noUrl, safe });
        Check(r.SelectedVersion.Driver == safe && r.CanSelect, "default candidate skips unsafe URL");
        r.IsSelected = true; r.SelectedVersion = r.Versions.Single(x => x.Driver == noUrl);
        Check(!r.CanSelect && !r.IsSelected, "switching to unavailable version clears selection");
        r.SelectedVersion = r.Versions.Single(x => x.Driver == safe);
        Check(r.VersionSummary.Contains("2.0"), "summary tracks selected version, not all versions");
        var v10 = Driver("ID", "10.0"); var v9 = Driver("ID", "9.0");
        Check(new DeviceRowViewModel(d, new[] { v9, v10 }).Versions.First().Driver == v10, "version ordering is numeric, not lexicographic");
        Check(!new DeviceRowViewModel(d, new[] { Driver("ID", url: "https://name:password@example.invalid/a") }).CanSelect, "credential-bearing download URL is rejected");
        Check(!new DeviceRowViewModel(d, new[] { Driver("ID", url: "javascript:bad") }).CanSelect, "non-HTTP download URL is rejected");
        b = Backend(); i = new FakeInteraction(); vm = VM(b, i);
        b.Candidates.Add(Driver("OTHER", "90.0")); await Ready(vm);
        Check(vm.CandidateCount == 2, "same device class or title never overrides exact ID matching");
        vm.AutoSelectCommand.Execute(null); Check(vm.SelectedCount == 2, "automatic selection picks applicable newer versions");
        b.Candidates[0].Superseded = true; b.Candidates[1].Version = "0.5"; await Scan(vm);
        vm.AutoSelectCommand.Execute(null); Check(vm.SelectedCount == 0, "automatic selection rejects superseded and older versions");
        b.Candidates[0].Superseded = false; b.Candidates[0].Version = "not a version"; await Scan(vm);
        vm.AutoSelectCommand.Execute(null); Check(vm.SelectedCount == 0, "automatic selection rejects unparseable candidate versions");
        vm.Dispose();
    }
    static async Task DemoAndErrors()
    {
        var b = Backend(); var i = new FakeInteraction(); var vm = VM(b, i, true);
        await vm.InitializeAsync();
        Check(vm.DeviceCount > 0 && b.DeviceReads == 0 && b.PolicyReads == 0, "demo initialization never touches real backend");
        vm.AutoSelectCommand.Execute(null);
        Check(!vm.ScanCommand.CanExecute(null) && !vm.DownloadCommand.CanExecute(null), "demo scan and download buttons remain disabled");
        foreach (var command in new[] { vm.ScanCommand, vm.DownloadCommand, vm.EnablePolicyCommand, vm.RestorePolicyCommand })
        { command.Execute(null); await vm.WaitForIdleAsync(); }
        vm.BrowseCommand.Execute(null); vm.OpenFolderCommand.Execute(null);
        Check(b.DeviceReads + b.CandidateReads + b.Downloads + b.Installs + b.PolicyReads + b.Enables + b.Restores == 0, "direct command execution cannot bypass demo guards");
        Check(i.Choosers + i.Opens + i.Confirms == 0, "demo has no folder, shell or confirmation side effects"); vm.Dispose();
        b = Backend(); i = new FakeInteraction(); vm = VM(b, i);
        b.CandidateBody = token => Task.FromException<IList<DriverInfo>>(new IOException("WUA unavailable"));
        await Ready(vm);
        Check(vm.DeviceCount == 2 && vm.CandidateCount == 0, "failed WUA query retains visible local devices");
        Check(vm.StatusText.Contains("WUA unavailable") && vm.IsIdle, "WUA error is explicit and releases busy state");
        i.ThrowSave = true; vm.IsEnglish = true; Check(vm.LogText.Contains("save failed"), "language save error is logged without crashing");
        i.ThrowOpen = true; vm.OpenFolderCommand.Execute(null); Check(i.Messages == 1, "shell error is handled");
        i.ThrowChoose = true; vm.BrowseCommand.Execute(null); Check(i.Messages == 2, "folder chooser error is handled"); vm.Dispose();
    }
    static async Task DownloadsAndInstallation()
    {
        var b = Backend(); var i = new FakeInteraction(); var vm = VM(b, i); await Ready(vm);
        b.IsAdministrator = false; vm.AutoSelectCommand.Execute(null); await Download(vm);
        Check(b.Downloads == 1 && b.Installs == 0, "standard user can download without installation");
        Check(i.Questions.Single().Contains(root) && i.Questions.Single().Contains(b.Candidates[0].Id), "download confirmation identifies folder and exact updates");
        Check(vm.StatusText.Contains("下载成功项：2"), "download completion reports successful count");
        Check(vm.Devices.All(x => x.Status.Contains("已下载")), "download result refreshes row status");
        Check(!i.WrongThread, "download confirmation returns to UI context");
        vm.InstallAfterDownload = true; await Download(vm);
        Check(b.Downloads == 2 && b.Installs == 0 && i.Messages == 1, "installation alone is blocked without administrator rights");
        b.IsAdministrator = true; i.Answers.Enqueue(true); i.Answers.Enqueue(false); await Download(vm);
        Check(b.Installs == 0, "declining separate installation confirmation prevents install");
        i.Answers.Enqueue(false); int previous = b.Downloads; await Download(vm);
        Check(b.Downloads == previous, "declining download confirmation has no backend side effect");
        b.DownloadBody = (drivers, path, token) =>
        {
            FakeBackend.MarkDownloaded(drivers[0], path);
            drivers[1].Status = "下载失败";
            drivers[1].LocalPath = path; // Existing directory must NOT count as a successful download.
            throw new IOException("partial failure");
        };
        await Download(vm);
        Check(b.Installs == 1 && b.LastInstalled.Count == 1, "partial failure installs only explicitly successful downloaded package");
        Check(vm.LogText.Contains("partial failure"), "partial failure details remain in activity log");
        Check(i.Questions.Last().Contains(b.LastInstalled[0].Id) && i.Questions.Last().Contains("不回滚"), "installation confirmation includes identities and irreversible effects");
        b.DownloadBody = null;
        b.InstallBody = (drivers, token) => Task.FromResult(new DriverInstallResult { InstalledCount = 1, FailedCount = 1, RebootRequired = true, Errors = new List<string> { "HRESULT fixture" } });
        await Download(vm);
        Check(vm.StatusText.Contains("需要重启") && vm.StatusText.Contains("HRESULT fixture"), "reboot requirement and per-item errors are not discarded");
        Check(!i.WrongThread, "all installation interactions use UI thread"); vm.Dispose();

        b = Backend(); i = new FakeInteraction { Folder = null }; vm = VM(b, i); await Ready(vm);
        vm.DownloadFolder = ""; vm.AutoSelectCommand.Execute(null); await Download(vm);
        Check(b.Downloads == 0 && i.Choosers == 1, "canceling initial folder chooser prevents download");
        vm.DownloadFolder = root; vm.BrowseCommand.Execute(null); Check(vm.DownloadFolder == root, "canceling browse preserves prior directory"); vm.Dispose();
        b = Backend(); b.Devices.Add(Device("GPU second instance", "PCI\\GPU")); i = new FakeInteraction(); vm = VM(b, i); await Ready(vm);
        vm.AutoSelectCommand.Execute(null); Check(vm.SelectedCount == 3, "same-model device instances remain independently selected");
        await Download(vm); Check(b.LastDownloaded.Count == 2, "shared update identity is downloaded only once"); vm.Dispose();
    }
    static async Task CancellationAndConcurrency()
    {
        var b = Backend(); var i = new FakeInteraction(); var vm = VM(b, i); await Ready(vm);
        vm.AutoSelectCommand.Execute(null); vm.InstallAfterDownload = true;
        var gate = new TaskCompletionSource<bool>();
        b.DownloadBody = async (drivers, path, token) =>
        { await gate.Task; foreach (var driver in drivers) FakeBackend.MarkDownloaded(driver, path); };
        vm.DownloadCommand.Execute(null); await Until(() => b.Downloads == 1);
        Check(vm.IsBusy && !vm.ScanCommand.CanExecute(null), "busy operation disables conflicting commands");
        vm.DownloadCommand.Execute(null); vm.ScanCommand.Execute(null); vm.BrowseCommand.Execute(null); vm.OpenFolderCommand.Execute(null);
        Check(b.Downloads == 1 && b.CandidateReads == 1 && i.Choosers == 0 && i.Opens == 0, "direct execution cannot reenter busy backend or shell");
        vm.DownloadFolder = Path.Combine(root, "changed"); vm.InstallAfterDownload = false; vm.IncludeHistory = true;
        Check(vm.DownloadFolder == root && vm.InstallAfterDownload && !vm.IncludeHistory, "material choices are frozen during operation");
        vm.Cancel(); Task waiting = vm.WaitForIdleAsync();
        Check(vm.IsBusy && !waiting.IsCompleted && vm.CancelRequested, "cancel waits for actual uninterruptible worker completion");
        gate.SetResult(true); await waiting;
        Check(vm.IsIdle && b.Installs == 0 && vm.StatusText.Contains("不会安装"), "backend ignoring cancel still cannot trigger installation");
        b.LastProgress.Report(new DriverDownloadProgress { Percent = 99, Message = "stale progress" });
        await Task.Delay(10); Check(vm.ProgressText != "stale progress", "late progress cannot overwrite finished operation");
        b.DownloadBody = null; vm.InstallAfterDownload = false; await Download(vm);
        Check(!vm.CancelRequested && b.Downloads == 2, "next operation resets cancellation state"); vm.Dispose();

        b = Backend(); i = new FakeInteraction(); vm = VM(b, i); await Ready(vm);
        vm.AutoSelectCommand.Execute(null); vm.InstallAfterDownload = true;
        var installGate = new TaskCompletionSource<bool>();
        b.InstallBody = async (drivers, token) => { await installGate.Task; return new DriverInstallResult { InstalledCount = 1, Canceled = true, RebootRequired = true }; };
        vm.DownloadCommand.Execute(null); await Until(() => b.Installs == 1); vm.Cancel();
        Task closeWait = vm.WaitForIdleAsync(); Check(!closeWait.IsCompleted, "closing waits for actual installation result");
        installGate.SetResult(true); await closeWait;
        Check(vm.StatusText.Contains("安装完成项：1") && vm.StatusText.Contains("不会回滚") && vm.StatusText.Contains("需要重启"), "cancel after installation preserves completed count and reboot warning"); vm.Dispose();
    }
    static async Task PolicyAndInitialization()
    {
        var b = Backend(); var i = new FakeInteraction(); var vm = VM(b, i);
        var gate = new TaskCompletionSource<WuMgr.Wpf.Services.PolicyState>(); b.PolicyBody = token => gate.Task;
        Task initialization = vm.InitializeAsync(); await Until(() => b.PolicyReads == 1);
        Check(vm.DeviceCount == 2 && vm.IsBusy, "devices display while initial policy read is pending");
        Task waiting = vm.WaitForIdleAsync(); Check(!waiting.IsCompleted, "initial policy read participates in close wait");
        vm.Cancel(); gate.SetResult(new WuMgr.Wpf.Services.PolicyState()); await initialization; await waiting;
        Check(vm.IsIdle, "canceled initialization ends only after policy worker returns"); vm.Dispose(); vm.Dispose();
        b = Backend(); i = new FakeInteraction(); vm = VM(b, i); await vm.InitializeAsync();
        Check(!vm.RestorePolicyCommand.CanExecute(null), "restore disabled when no backup exists");
        vm.RestorePolicyCommand.Execute(null); await vm.WaitForIdleAsync(); Check(b.Restores == 0, "direct restore execution refuses absent backup");
        b.IsAdministrator = false; vm.EnablePolicyCommand.Execute(null); await vm.WaitForIdleAsync();
        Check(b.Enables == 0 && i.Messages >= 1, "policy write alone requires administrator rights");
        b.IsAdministrator = true; i.Answers.Enqueue(false); vm.EnablePolicyCommand.Execute(null); await vm.WaitForIdleAsync();
        Check(b.Enables == 0, "declined policy confirmation causes no registry write");
        vm.EnablePolicyCommand.Execute(null); await vm.WaitForIdleAsync();
        Check(b.Enables == 1 && vm.RestorePolicyCommand.CanExecute(null), "successful protection creates a restorable backup");
        Check(i.Questions.Last().Contains("全机") || i.Questions.Last().Contains("整台计算机"), "policy confirmation states computer-wide scope");
        Check(i.Questions.Last().Contains("MDM") && i.Questions.Last().Contains("安全更新"), "policy confirmation explains organizational limits and retained security updates");
        vm.RestorePolicyCommand.Execute(null); await vm.WaitForIdleAsync();
        Check(b.Restores == 1 && !vm.RestorePolicyCommand.CanExecute(null), "successful restore clears stale restore capability");
        b.PolicyBody = token => Task.FromException<WuMgr.Wpf.Services.PolicyState>(new IOException("registry read failed"));
        vm.RefreshDevicesCommand.Execute(null); await vm.WaitForIdleAsync();
        Check(vm.DeviceCount == 2 && vm.PolicyText.Contains("registry read failed"), "policy read failure does not discard device inventory");
        Check(!vm.RestorePolicyCommand.CanExecute(null), "failed policy read disables stale restore capability");
        Check(!i.WrongThread, "policy dialogs remain on UI context"); vm.Dispose();
    }
}

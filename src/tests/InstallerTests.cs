using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using wumgr;

public class InstallerTests
{
    static int passed;
    static void Check(bool value, string name) { if (!value) throw new Exception("失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    public static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wumgr-installer-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string id = "01234567-89ab-cdef-0123-456789abcdef";
            Check(DriverInstaller.IsValidUpdateId(id), "有效 GUID"); Check(!DriverInstaller.IsValidUpdateId(id + "' or 1=1"), "拒绝查询注入字符串");
            string criteria = DriverInstaller.BuildDriverSearchCriteria(id.ToUpperInvariant()); Check(criteria == "IsInstalled=0 and IsHidden=0 and Type='Driver' and UpdateID='01234567-89ab-cdef-0123-456789abcdef'", "精确 WUA Driver 查询条件");
            var driver = new DriverInfo { Id = id, Title = "测试驱动", Status = DriverInstaller.DownloadedStatus, LocalPath = dir };
            Check(DriverInstaller.HasDownloadedPackage(driver), "已下载且目录存在"); driver.Status = "正在安装…"; Check(!DriverInstaller.HasDownloadedPackage(driver), "状态门禁严格匹配"); driver.Status = DriverInstaller.DownloadedStatus;
            Check(DriverInstaller.ValidateDriver(driver) == null, "验证不依赖下载目录内 EXE/CAB"); driver.LocalPath = Path.Combine(dir, "missing"); Check(DriverInstaller.ValidateDriver(driver) != null, "拒绝不存在的本地目录"); driver.LocalPath = dir; driver.Id = "not-a-guid"; Check(DriverInstaller.ValidateDriver(driver) != null, "拒绝无效项"); driver.Id = id;
            Check(!DriverInstaller.IsSupportedPlatform(PlatformID.Unix), "拒绝非 Windows 平台");
            var good = new FakeUpdate(id, 2, false, false, true, true); var wrongType = new FakeUpdate(id, 1, false, false, true, true); var installed = new FakeUpdate(id, 2, false, true, true, true);
            Check(DriverInstaller.IsApplicableDriver(good, id), "Type 数字 2 识别 Driver 且未安装"); Check(!DriverInstaller.IsApplicableDriver(wrongType, id), "非 Driver 类型拒绝"); Check(!DriverInstaller.IsApplicableDriver(installed, id), "已安装更新拒绝");
            var factory = new FakeFactory(good); DriverInstallSummary summary = new DriverInstaller().InstallAsync(new List<DriverInfo> { driver }, CancellationToken.None, null, factory).GetAwaiter().GetResult();
            Check(summary.InstalledCount == 1 && summary.FailedCount == 0 && factory.Session.Installer.ForceQuiet, "fake WUA 完整下载安装流程"); Check(factory.Session.Installer.LastResultCode == 2 && factory.Session.Installer.LastHResult == 0, "逐项 GetUpdateResult 结果码和 HRESULT"); Check(factory.LastCriteria.StartsWith("IsInstalled=0"), "Search 使用未安装条件");
            var cancelSource = new CancellationTokenSource(); var cancelFactory = new FakeFactory(new FakeUpdate(id, 2, false, false, true, true)); cancelFactory.CancelAfterDownload = cancelSource; var second = new DriverInfo { Id = id, Title = "取消驱动", Status = DriverInstaller.DownloadedStatus, LocalPath = dir };
            DriverInstallSummary canceled = new DriverInstaller().InstallAsync(new List<DriverInfo> { second }, cancelSource.Token, null, cancelFactory).GetAwaiter().GetResult(); Check(canceled.Canceled && canceled.InstalledCount == 0 && (cancelFactory.Session == null || cancelFactory.Session.Installer == null || !cancelFactory.Session.Installer.Called), "下载后取消绝不进入 Install"); Check(second.Status == "安装已取消", "取消项状态明确");
            var pre = new CancellationTokenSource(); pre.Cancel(); var preDriver = new DriverInfo { Id = id, Title = "预取消", Status = DriverInstaller.DownloadedStatus, LocalPath = dir }; DriverInstallSummary preSummary = new DriverInstaller().InstallAsync(new List<DriverInfo> { preDriver }, pre.Token, null, new FakeFactory(good)).GetAwaiter().GetResult(); Check(preSummary.Canceled && preDriver.Status == "安装已取消", "预取消项标记安装已取消");
            Check(DriverInstaller.BuildDriverSearchCriteria(id, 5).EndsWith("RevisionNumber=5") && !DriverInstaller.IsApplicableDriver(good, id, 4) && DriverInstaller.IsApplicableDriver(good, id, 5), "精确修订号查询且拒绝不同修订驱动");
            var eulaDriver = new DriverInfo { Id = id, Title = "未接受许可", Status = DriverInstaller.DownloadedStatus, LocalPath = dir }; var eulaFactory = new FakeFactory(new FakeUpdate(id, 2, false, false, false, true));
            var eulaSummary = new DriverInstaller().InstallAsync(new[] { eulaDriver }, CancellationToken.None, null, eulaFactory).GetAwaiter().GetResult();
            Check(eulaSummary.FailedCount == 1 && eulaSummary.InstalledCount == 0 && eulaFactory.Session.Installer == null, "未接受EULA绝不安装");
            Console.WriteLine("安装器测试完成：" + passed + " 项全部通过。Windows WUA COM 安装需在 Windows 管理员环境实测。"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } finally { try { Directory.Delete(dir, true); } catch { } }
    }

    public class FakeFactory : IWuaFactory
    {
        public FakeSession Session { get; private set; } public FakeUpdate Update; public CancellationTokenSource CancelAfterDownload; public string LastCriteria;
        public FakeFactory(FakeUpdate update) { Update = update; }
        public object CreateSession() { Session = new FakeSession(this); return Session; }
        public object CreateUpdateCollection() { return new FakeCollection(); }
    }
    public class FakeSession
    {
        readonly FakeFactory factory; public FakeInstaller Installer;
        public FakeSession(FakeFactory f) { factory = f; }
        public FakeSearcher CreateUpdateSearcher() { return new FakeSearcher(factory); }
        public FakeDownloader CreateUpdateDownloader() { return new FakeDownloader(factory); }
        public FakeInstaller CreateUpdateInstaller() { Installer = new FakeInstaller(); return Installer; }
    }
    public class FakeSearcher { readonly FakeFactory factory; public FakeSearcher(FakeFactory f) { factory = f; } public FakeSearchResult Search(string criteria) { factory.LastCriteria = criteria; return new FakeSearchResult(factory.Update); } }
    public class FakeSearchResult { public int ResultCode = 2; public FakeUpdates Updates; public FakeSearchResult(FakeUpdate update) { Updates = new FakeUpdates(update); } }
    public class FakeUpdates { readonly FakeUpdate update; public FakeUpdates(FakeUpdate u) { update = u; } public int Count { get { return 1; } } public FakeUpdate Item(int i) { return update; } }
    public class FakeCollection { public FakeUpdate Item; public void Add(object update) { Item = (FakeUpdate)update; } }
    public class FakeDownloader { readonly FakeFactory factory; public object Updates { get; set; } public FakeDownloader(FakeFactory f) { factory = f; } public FakeResult Download() { if (factory.CancelAfterDownload != null) factory.CancelAfterDownload.Cancel(); factory.Update.IsDownloaded = true; return new FakeResult(2, 0); } }
    public class FakeInstaller { public object Updates { get; set; } public bool ForceQuiet { get; set; } public bool AllowSourcePrompts { get; set; } public bool IsForced { get; set; } public bool Called; public int LastResultCode; public int LastHResult; public FakeInstallation Install() { Called = true; LastResultCode = 2; LastHResult = 0; return new FakeInstallation(); } }
    public class FakeInstallation { public int ResultCode = 2; public bool RebootRequired { get { return false; } } public FakeResult GetUpdateResult(int i) { return new FakeResult(2, 0); } }
    public class FakeResult { public bool RebootRequired; public int ResultCode; public int HResult; public FakeResult(int code, int hr) { ResultCode = code; HResult = hr; } }
    public class FakeIdentity { public string UpdateID; public int RevisionNumber = 5; public FakeIdentity(string id) { UpdateID = id; } }
    public class FakeUpdate { public FakeIdentity Identity; public bool IsHidden; public bool IsInstalled; public int Type; public bool EulaAccepted; public bool IsDownloaded; public FakeUpdate(string id, int type, bool hidden, bool installed, bool eula, bool downloaded) { Identity = new FakeIdentity(id); Type = type; IsHidden = hidden; IsInstalled = installed; EulaAccepted = eula; IsDownloaded = downloaded; } }
}

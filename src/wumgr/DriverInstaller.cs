using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace wumgr
{
    public sealed class DriverInstallSummary
    {
        public int InstalledCount { get; internal set; }
        public int FailedCount { get; internal set; }
        public bool RebootRequired { get; internal set; }
        public bool Canceled { get; internal set; }
        public List<string> Errors { get; private set; } = new List<string>();
    }
    public interface IWuaFactory { object CreateSession(); object CreateUpdateCollection(); }
    public sealed class DriverInstaller
    {
        public const string DownloadedStatus = "已下载";
        public const string DriverType = "Driver";
        private const int DriverTypeValue = 2;
        private sealed class DefaultWuaFactory : IWuaFactory
        {
            public object CreateSession() { return Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)); }
            public object CreateUpdateCollection() { return Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true)); }
        }
        public static bool IsSupportedWindows() { return IsSupportedPlatform(Environment.OSVersion.Platform); }
        public static bool IsSupportedPlatform(PlatformID platform) { return platform == PlatformID.Win32NT; }
        public static bool IsAdministrator() { try { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity != null && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        public static bool IsValidUpdateId(string id) { Guid ignored; return Guid.TryParse(id, out ignored); }
        public static string BuildDriverSearchCriteria(string id, int revisionNumber = -1)
        {
            Guid value; if (!Guid.TryParse(id, out value)) throw new ArgumentException("UpdateID 不是有效 GUID。", nameof(id));
            string criteria = "IsInstalled=0 and IsHidden=0 and Type='Driver' and UpdateID='" + value.ToString("D") + "'";
            return revisionNumber >= 0 ? criteria + " and RevisionNumber=" + revisionNumber : criteria;
        }
        public static bool HasDownloadedPackage(DriverInfo driver) { return driver != null && string.Equals(driver.Status, DownloadedStatus, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(driver.LocalPath) && Directory.Exists(driver.LocalPath); }
        public static string ValidateDriver(DriverInfo driver) { if (driver == null) return "驱动项为空。"; if (driver.Source != "Windows Update Agent") return "目录候选仅允许下载，不能作为 WUA 适用更新安装；请通过系统或厂商确认适用性。"; if (!IsValidUpdateId(driver.Id)) return "UpdateID 不是有效 GUID。"; if (!HasDownloadedPackage(driver)) return "必须先成功下载驱动（状态为“已下载”且本地目录存在）。"; return null; }
        public Task<DriverInstallSummary> InstallAsync(IList<DriverInfo> drivers, CancellationToken token, IProgress<DriverDownloadProgress> progress) { return InstallAsync(drivers, token, progress, null); }
        public Task<DriverInstallSummary> InstallAsync(IList<DriverInfo> drivers, CancellationToken token, IProgress<DriverDownloadProgress> progress, IWuaFactory factory)
        {
            var completion = new TaskCompletionSource<DriverInstallSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => { try { completion.SetResult(InstallOnSta(drivers, token, progress, factory ?? new DefaultWuaFactory())); } catch (Exception ex) { completion.SetException(ex); } });
            thread.IsBackground = true; try { thread.SetApartmentState(ApartmentState.STA); } catch (Exception ex) { completion.SetException(ex); return completion.Task; } thread.Start(); return completion.Task;
        }
        private static DriverInstallSummary InstallOnSta(IList<DriverInfo> drivers, CancellationToken token, IProgress<DriverDownloadProgress> progress, IWuaFactory factory)
        {
            var result = new DriverInstallSummary(); bool fake = !(factory is DefaultWuaFactory);
            if (drivers == null) { result.Errors.Add("驱动列表为空。"); return result; }
            if ((!IsSupportedWindows() || !IsAdministrator()) && !fake)
            {
                string error = !IsSupportedWindows() ? "仅支持 Windows；当前系统不是 Windows。" : "请以管理员身份重新打开程序后再安装驱动。"; result.Errors.Add(error);
                for (int n = 0; n < drivers.Count; n++) if (drivers[n] != null) { drivers[n].Status = "安装失败"; result.FailedCount++; } return result;
            }
            for (int i = 0; i < drivers.Count; i++)
            {
                if (token.IsCancellationRequested) { MarkCanceled(result, drivers, i, progress); break; }
                DriverInfo driver = drivers[i]; string validation = ValidateDriver(driver);
                if (validation != null) { Fail(result, driver, (driver == null ? "驱动项" : driver.Title) + "：" + validation, progress, i, drivers.Count); continue; }
                object session = null, searcher = null, searchResult = null, found = null, update = null, collection = null, downloader = null, downloadResult = null, installer = null, installation = null, itemResult = null;
                try
                {
                    driver.Status = "正在验证驱动…"; Report(progress, i, drivers.Count, 5, driver.Title, "正在通过 Windows Update Agent 重新验证驱动身份和适用性（目录硬件匹配不等于系统适用）…"); CheckCanceled(token, result, drivers, i, progress);
                    session = factory.CreateSession(); searcher = ((dynamic)session).CreateUpdateSearcher(); searchResult = ((dynamic)searcher).Search(BuildDriverSearchCriteria(driver.Id, driver.RevisionNumber));
                    CheckCanceled(token, result, drivers, i, progress);
                    if (Convert.ToInt32(((dynamic)searchResult).ResultCode) != 2) throw new InvalidOperationException("WUA 身份查询未完全成功，拒绝安装。 ");
                    found = ((dynamic)searchResult).Updates;
                    for (int n = 0; n < (int)((dynamic)found).Count; n++) { object candidate = ((dynamic)found).Item(n); if (IsApplicableDriver(candidate, driver.Id, driver.RevisionNumber)) { update = candidate; break; } Release(candidate); }
                    if (update == null) throw new InvalidOperationException("WUA 未返回匹配的、适用且未隐藏的驱动更新；目录包仅保留下载，不强制安装或绕过系统检查。");
                    if (!IsEulaAccepted(update)) throw new InvalidOperationException("驱动 EULA 尚未接受或读取失败；程序不会代替用户接受法律条款。");
                    driver.Status = "正在使用系统缓存下载…"; Report(progress, i, drivers.Count, 20, driver.Title, "正在使用 Windows Update 系统缓存准备驱动（不执行本地下载文件）…"); CheckCanceled(token, result, drivers, i, progress);
                    collection = factory.CreateUpdateCollection(); ((dynamic)collection).Add(update); downloader = ((dynamic)session).CreateUpdateDownloader(); ((dynamic)downloader).Updates = collection; downloadResult = ((dynamic)downloader).Download();
                    if ((int)((dynamic)downloadResult).ResultCode != 2 || !Convert.ToBoolean(((dynamic)update).IsDownloaded)) throw new InvalidOperationException("WUA 下载未成功或更新未标记为已下载。"); CheckCanceled(token, result, drivers, i, progress);
                    driver.Status = "正在安装…"; Report(progress, i, drivers.Count, 60, driver.Title, "正在静默安装驱动…"); installer = ((dynamic)session).CreateUpdateInstaller(); ((dynamic)installer).Updates = collection; ((dynamic)installer).ForceQuiet = true; ((dynamic)installer).AllowSourcePrompts = false; ((dynamic)installer).IsForced = false;
                    CheckCanceled(token, result, drivers, i, progress);
                    installation = ((dynamic)installer).Install(); bool reboot = Convert.ToBoolean(((dynamic)installation).RebootRequired); if (reboot) result.RebootRequired = true; itemResult = ((dynamic)installation).GetUpdateResult(0);
                    result.RebootRequired |= Convert.ToBoolean(((dynamic)itemResult).RebootRequired);
                    int code = (int)((dynamic)itemResult).ResultCode; int hresult = (int)((dynamic)itemResult).HResult; if (Convert.ToInt32(((dynamic)installation).ResultCode) != 2 || code != 2 || hresult < 0) throw new InvalidOperationException("WUA 安装失败，逐项结果码：" + code + "，HRESULT：" + hresult + "。");
                    driver.Status = result.RebootRequired ? "已安装 · 需重启" : "已安装"; result.InstalledCount++; WriteLog(result, driver, "安装成功；逐项结果码 2；HRESULT：" + hresult + "；需重启：" + reboot); Report(progress, i, drivers.Count, 100, driver.Title, "安装成功。" + (reboot ? "需要重启。" : ""));
                    if (token.IsCancellationRequested) { result.Canceled = true; MarkCanceled(result, drivers, i + 1, progress); break; }
                }
                catch (OperationCanceledException) { result.Canceled = true; MarkCanceled(result, drivers, i, progress); break; }
                catch (Exception ex) { driver.Status = "安装失败"; Fail(result, driver, (driver.Title ?? "驱动") + "：" + ex.Message, progress, i, drivers.Count); }
                finally { Release(itemResult); Release(installation); Release(installer); Release(downloadResult); Release(downloader); Release(collection); Release(update); Release(found); Release(searchResult); Release(searcher); Release(session); }
            }
            return result;
        }
        public static bool IsApplicableDriver(object update, string id) { return IsApplicableDriver(update, id, -1); }
        public static bool IsApplicableDriver(object update, string id, int revisionNumber)
        {
            try { Guid expected, actual = Guid.Empty; dynamic u = update; if (!Guid.TryParse(id, out expected) || !Guid.TryParse(Convert.ToString(u.Identity.UpdateID), out actual) || expected != actual) return false; return !Convert.ToBoolean(u.IsHidden) && !Convert.ToBoolean(u.IsInstalled) && Convert.ToInt32(u.Type) == DriverTypeValue && (revisionNumber < 0 || Convert.ToInt32(u.Identity.RevisionNumber) == revisionNumber); } catch { return false; }
        }
        private static bool IsEulaAccepted(object update) { try { return Convert.ToBoolean(((dynamic)update).EulaAccepted); } catch { return false; } }
        private static void CheckCanceled(CancellationToken token, DriverInstallSummary result, IList<DriverInfo> drivers, int index, IProgress<DriverDownloadProgress> progress) { if (token.IsCancellationRequested) { result.Canceled = true; MarkCanceled(result, drivers, index, progress); throw new OperationCanceledException(); } }
        private static void MarkCanceled(DriverInstallSummary result, IList<DriverInfo> drivers, int start, IProgress<DriverDownloadProgress> progress) { result.Canceled = true; for (int n = Math.Max(0, start); n < drivers.Count; n++) if (drivers[n] != null && !drivers[n].Status.StartsWith("已安装", StringComparison.Ordinal)) { drivers[n].Status = "安装已取消"; Report(progress, n, drivers.Count, 0, drivers[n].Title, "安装已取消。"); } }
        private static void Fail(DriverInstallSummary summary, DriverInfo driver, string message, IProgress<DriverDownloadProgress> progress, int index, int total) { summary.FailedCount++; summary.Errors.Add(message); if (driver != null) { driver.Status = "安装失败"; WriteLog(summary, driver, message); } Report(progress, index, total, 0, driver == null ? "驱动" : driver.Title, message); }
        private static void WriteLog(DriverInstallSummary summary, DriverInfo driver, string message) { try { File.AppendAllText(Path.Combine(driver.LocalPath, "安装日志.txt"), DateTime.Now.ToString("s") + " 来源=" + driver.Source + " UpdateID=" + driver.Id + " 目录=" + driver.CatalogUrl + " " + message + Environment.NewLine); } catch (Exception ex) { summary.Errors.Add((driver.Title ?? "驱动") + "：安装日志写入失败（不影响安装结果）：「" + ex.Message + "」"); } }
        private static void Report(IProgress<DriverDownloadProgress> progress, int index, int total, int percent, string title, string message) { if (progress == null) return; try { progress.Report(new DriverDownloadProgress { Percent = total == 0 ? percent : (index * 100 + percent) / total, Message = (title ?? "驱动") + "：" + message }); } catch { } }
        private static void Release(object value) { try { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); } catch { } }
    }
}

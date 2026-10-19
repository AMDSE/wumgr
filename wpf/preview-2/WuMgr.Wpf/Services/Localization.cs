using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace WuMgr.Wpf.Services
{
    internal static class Localization
    {
        static readonly Dictionary<string, string[]> Labels = new Dictionary<string, string[]>
        {
            { "AppName", new[] { "WuMgr WPF 预览版", "WuMgr WPF Preview" } },
            { "Workspace", new[] { "驱动工作区", "Driver workspace" } },
            { "SearchHint", new[] { "搜索设备、硬件 ID、版本或提供商 · Ctrl+F", "Search devices, hardware IDs, versions or providers · Ctrl+F" } },
            { "Scan", new[] { "扫描更新", "Scan updates" } },
            { "RefreshDevices", new[] { "刷新设备", "Refresh devices" } },
            { "Cancel", new[] { "取消", "Cancel" } },
            { "Download", new[] { "下载所选", "Download selected" } },
            { "AutoSelect", new[] { "自动选择新版", "Select newer versions" } },
            { "ClearSelection", new[] { "清除选择", "Clear selection" } },
            { "Browse", new[] { "浏览", "Browse" } },
            { "OpenFolder", new[] { "打开目录", "Open folder" } },
            { "History", new[] { "扫描时包含历史候选", "Include historical candidates in scan" } },
            { "Install", new[] { "下载后静默安装（需管理员，另行确认）", "Install after download (admin; separate confirmation)" } },
            { "PolicyEnable", new[] { "启用驱动更新保护", "Enable driver protection" } },
            { "PolicyRestore", new[] { "恢复原策略", "Restore original policy" } },
            { "Details", new[] { "详情", "Details" } },
            { "Versions", new[] { "候选版本", "Versions" } },
            { "Installed", new[] { "当前已安装", "Installed version" } },
            { "Candidate", new[] { "所选候选", "Selected candidate" } },
            { "NoCandidates", new[] { "无可下载候选", "No downloadable candidate" } },
            { "EmptyTitle", new[] { "没有匹配的设备", "No matching devices" } },
            { "EmptyBody", new[] { "清除搜索或切换设备分类；无候选不代表设备没有驱动。", "Clear the search or change category. No candidate does not mean the device has no driver." } },
            { "DemoNotice", new[] { "安全演示数据：不会扫描真实设备、下载、安装或修改策略。", "Safe demo data: no real device scan, download, installation or policy changes." } },
            { "DownloadFolderLabel", new[] { "下载目录", "Download folder" } },
            { "SelectionLabel", new[] { "已选择", "Selected" } },
            { "DeviceCountLabel", new[] { "设备", "Devices" } },
            { "CandidateCountLabel", new[] { "候选", "Candidates" } },
            { "Log", new[] { "操作日志", "Activity log" } },
            { "Protection", new[] { "Windows 驱动更新保护", "Windows driver protection" } },
            { "Language", new[] { "简体中文 → English", "English → 简体中文" } },
            { "About", new[] { "关于", "About" } },
            { "DeviceSearch", new[] { "设备搜索与分类", "Device search and categories" } },
            { "AllDevices", new[] { "全部设备", "All devices" } },
            { "CommonDevices", new[] { "常见硬件", "Common hardware" } },
            { "DownloadConfirm", new[] { "仅下载所选驱动到以下目录，不执行下载目录中的文件。安装如已勾选，还会单独确认。是否继续？", "Download the selected drivers to the folder below; do not execute downloaded files. If installation is selected, it requires a separate confirmation. Continue?" } },
            { "InstallConfirm", new[] { "安装将修改系统驱动，可能短暂断网、闪屏或需要手动重启。安装通过 WUA 重新核验 UpdateID、修订号和适用性，使用系统缓存，可能再次下载；不会执行下载目录中的文件，也不会代为接受 EULA。取消只阻止后续项，不回滚已完成安装；不自动提权、不自动重启。仅安装本轮下载成功项。是否继续？", "Installation changes system drivers and may disconnect the network, flicker the display or require a manual restart. WUA rechecks update ID, revision and applicability using its own cache and may download again; downloaded files are never executed and EULAs are not accepted on your behalf. Canceling prevents later items but does not roll back completed installations. No automatic elevation or restart. Only successfully downloaded items from this operation will be installed. Continue?" } },
            { "PolicyEnableConfirm", new[] { "此操作作用于整台计算机：备份原注册表值，设置 ExcludeWUDriversInQualityUpdate，仅排除 Windows 质量更新中的驱动。不停止 Windows Update，也不关闭安全更新。不能阻断安全/关键更新附带的驱动、功能更新动态驱动、首次安装、手动安装或厂商工具；组织策略/MDM 可覆盖，家庭版不承诺生效，也不保证消除冲突。可能影响后续 WUA 查询；需要更新时请先恢复原策略。需要管理员权限，不自动重启。是否备份并启用？", "This affects the entire computer: back up the original registry value and set ExcludeWUDriversInQualityUpdate to exclude drivers only from Windows quality updates. Windows Update and security updates remain enabled. It does not block drivers bundled with security/critical updates, dynamic feature-update drivers, first installation, manual installs or vendor tools. Organization policy/MDM may override it; Home edition effectiveness and conflict prevention are not guaranteed. Later WUA searches may be affected: restore the original policy before updating drivers. Administrator required; no automatic restart. Back up and enable?" } },
            { "PolicyRestoreConfirm", new[] { "还原本工具备份的全机驱动更新策略原值（原来不存在则删除），不修改其他更新策略。若值已被外部程序改变、备份损坏或不存在，将拒绝覆盖。是否恢复？", "Restore the original system-wide driver policy value saved by this tool (remove it if it originally did not exist). Other policies remain unchanged. Restoration refuses to overwrite externally modified values or use missing/corrupt backups. Restore?" } }
        };
        static readonly Dictionary<string, string> Phrases = BuildPhrases();
        static readonly Regex Pattern = new Regex(string.Join("|", Phrases.Keys.Where(x => x.Length >= 2 && x.Any(c => c >= '\u3400' && c <= '\u9fff')).OrderByDescending(x => x.Length).Select(Regex.Escape)), RegexOptions.Compiled);
        static Dictionary<string, string> BuildPhrases()
        {
            var result = new Dictionary<string, string>(wumgr.UiStrings.English);
            foreach (var pair in Labels) result[pair.Value[0]] = pair.Value[1];
            var extra = new Dictionary<string, string>
            {
                { "就绪", "Ready" }, { "正常", "OK" }, { "未知", "Unknown" }, { "未提供", "Not provided" },
                { "显卡", "Graphics" }, { "网络适配器", "Network adapters" }, { "声卡", "Audio" }, { "蓝牙", "Bluetooth" }, { "系统设备", "System devices" }, { "其他设备", "Other devices" },
                { "已取消；设备清单保留", "Canceled; device inventory retained" },
                { "设备刷新失败：", "Device refresh failed: " },
                { "更新扫描完成", "Update scan completed" },
                { "WUA 扫描已取消；当前设备清单保留", "WUA scan canceled; device inventory retained" },
                { "WUA 扫描失败；当前设备清单保留：", "WUA scan failed; device inventory retained: " },
                { "设备扫描已取消；当前设备清单保留", "Device scan canceled; device inventory retained" },
                { "设备扫描失败：", "Device scan failed: " },
                { "下载已取消；不会安装", "Download canceled; installation will not start" },
                { "已取消；不会安装", "Canceled; installation will not start" },
                { "安装需要管理员权限；已保留下载，不安装。", "Installation needs administrator rights. Downloads were kept; nothing will be installed." },
                { "修改全机策略需要管理员权限。", "Changing system-wide policy requires administrator rights." },
                { "安装完成项：", "Installed: " }, { "；失败项：", "; failed: " }, { "；需要重启", "; manual restart required" },
                { "下载/安装失败：", "Download/installation failed: " },
                { "策略操作已取消", "Policy operation canceled" }, { "策略操作失败：", "Policy operation failed: " }, { "策略读取失败：", "Cannot read policy: " },
                { " 当前状态：", " Current state: " }, { "；可恢复备份", "; backup available" }, { "已启用", "Enabled" }, { "未启用", "Not enabled" },
                { "COM/WUA 操作不能立即中断，将等待后台操作真正结束。", "COM/WUA calls cannot be interrupted immediately; waiting for the worker to finish." },
                { "将下载所选驱动。下载缓存可能再次下载；是否继续？", "Download selected drivers? WUA may download again for installation." },
                { "将备份并修改全机驱动更新策略，可能影响所有用户。是否继续？", "Back up and change the computer-wide driver policy for all users?" },
                { "将恢复此前备份的全机策略值。是否继续？", "Restore the original saved computer-wide policy value?" },
                { "将安装 ", "Install " }, { " 个已成功下载项（可能需要WUA缓存再下载）。EULA不会代为接受，不会自动重启。未下载成功项不会安装，是否继续？", " successfully downloaded items? WUA may download again. EULAs are not accepted on your behalf and there is no automatic restart. Failed downloads will not be installed." },
                { "下载完成", "Download completed" }, { "下载成功项：", "Downloaded: " }, { "；下载失败项：", "; download failures: " },
                { "候选", "Candidate" }, { "问题代码: ", "Problem code: " },
                { "设备发现完成：", "Devices discovered: " }, { "操作失败：", "Operation failed: " },
                { "操作已取消（未开始）", "Canceled before starting" },
                { "已请求取消；已完成的操作不会回滚", "Cancellation requested; completed operations are not rolled back" },
                { "；已取消后续项，已完成安装不会回滚", "; later items canceled; completed installations are not rolled back" },
                { "已取消后续项，已完成安装不会回滚；请查看安装日志", "Later items canceled; completed installations are not rolled back. Review installation logs." },
                { "策略操作完成", "Policy operation completed" }, { "策略读取失败", "Cannot read policy" },
                { "安装存在失败项", "Some installations failed" },
                { "没有可恢复的策略备份", "No policy backup is available" }, { "策略只读刷新；启用前会备份原值，恢复时还原原值。", "Read-only policy status. Enabling backs up the original value; restoration restores it." }
            };
            foreach (var pair in extra) result[pair.Key] = pair.Value;
            return result;
        }
        public static string Get(string key, bool english)
        {
            string[] value;
            return Labels.TryGetValue(key, out value) ? value[english ? 1 : 0] : key;
        }
        public static string Display(string source, bool english)
        {
            if (!english || string.IsNullOrEmpty(source)) return source ?? "";
            string translated;
            if (Phrases.TryGetValue(source, out translated)) return translated;
            return Pattern.Replace(source, match => Phrases[match.Value]);
        }
    }
}

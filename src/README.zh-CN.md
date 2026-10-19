> v8：主界面列出常见硬件及已安装驱动版本、提供商、日期，没有更新也保留；过滤虚拟设备、音频端点、系统杂项和重复逻辑核心。完整原始清单仍在“设备搜索”。详见 [更新说明-v8](更新说明-v8.md)。Windows 实机验收待完成。

# WuMgr 驱动下载中心 · 中英文版 v8

基于 [DavidXanatos/wumgr](https://github.com/DavidXanatos/wumgr) 的独立修改版，不是上游官方发布。当前以驱动中心为入口，不包含上游旧版完整更新管理界面。支持中英文界面、驱动型号／版本、历史候选选择与下载、设备自动发现、匹配下载、可选后台静默安装和驱动更新保护。

[项目首页与双语运行截图](README.md) · [English](README.en.md) · [下载 v8](WuMgr-驱动下载中心-v8.zip)

## 中英文切换

顶栏选择「简体中文 / English」即时切换，无需重启；保留搜索、分类、勾选、历史版本选择、保存目录及静默安装选项。首次默认简体中文，界面手动切换后保存语言偏好。命令行 `--lang zh-CN` / `--lang en-US` 可指定本次启动语言，例如 `wumgr.exe --demo --lang en-US`。设备型号、厂商原文与系统原始错误不翻译。

运行截图来自真实程序的 Linux / Mono 演示模式，不是真实设备扫描或 Windows 实机验收截图；Windows 字体与缩放可能不同。`--preview preview.png --lang en-US` 可以自行生成安全演示截图，不更改语言偏好。

## 如何使用

在 Windows 10 / 11 解压后运行 `程序\wumgr.exe`，需要 **.NET Framework 4.8**、Windows 更新服务和网络。默认仅下载，不自动安装、不自动开启保护。只有安装和修改保护策略需要右键“以管理员身份运行”；程序不自动提权。

设备扫描、搜索、自动选择、历史候选及安装流程保持 v4 功能。自动下载使用内置 Downloader 3.1.2：每个文件最多 **8 个分块、4 个并发请求**，内存 buffer 上限 **4 MiB**，显示速度、总体进度和分块进度。小于 **256 KiB** 的文件或服务器不支持 Range 时自动降为单流。

取消或失败会落盘保留 `*.part` 与 `*.resume.xml`，之后可尝试恢复；这不是对进程崩溃续传的承诺。相同目录、相同 URL 再次下载，只有服务器支持 Range 且提供有效的强 ETag 或 Last-Modified 时才续传；没有校验标识或资源已变化则重新下载。成功时确认文件非空且长度符合预期后，原子覆盖正式文件。

## 静默安装如何工作

安装仍使用 [微软 WUA 官方接口](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/searching--downloading--and-installing-updates)及 WUA 系统缓存，可能另行消耗网络和磁盘空间。上述 Downloader 多线程 HTTP 下载不是 WUA 安装下载，不能把两者混为一谈。程序不会运行下载目录内的 EXE、脚本或 CAB，不绕过 Windows 签名与信任检查，也不会自动接受未确认的 EULA。

安装在后台线程执行，关闭来源提示、使用静默方式，不强制安装、不自动重启。可能短暂断网、闪屏或需要重启；取消不会强杀已开始的系统安装，也不会回滚已完成驱动。

## 驱动更新保护

采用微软“Windows 更新不包括驱动程序”策略，仅排除常规质量更新周期中的驱动包；安全／关键更新、功能更新动态驱动、手动安装及厂商工具不在保证范围内。设置作用于全机，组织策略／MDM仍可能覆盖。详见 v4 说明中的保护边界。

## 构建与验证

Windows：安装 Visual Studio 2022“.NET 桌面开发”及 .NET Framework 4.8 目标包，执行 `build-windows.cmd`。脚本在缺少本地包时使用 NuGet CLI 还原固定的 Downloader 3.1.2；不允许漂移到其他版本。Linux／Mono 可执行 `bash tests/run-tests.sh`，脚本会校验本地包哈希并编译运行 `tests/DownloaderTests.cs` 及现有测试。

**Linux 仅用于自动化测试；Windows 真实 WMI、WUA 扫描／安装、微软 CDN、管理员权限及策略实际生效仍需实机验收。** `--demo`／`--preview` 使用演示数据，扫描、下载、安装、策略修改均禁用。

## 源码、第三方与许可

入口 `DriverProgram.cs`；后端包括 `DriverService.cs`、`DriverCatalog.cs`、`DeviceDiscovery.cs`、`DriverInstaller.cs`、`DriverUpdatePolicy.cs`。Downloader 固定为 **3.1.2/net462**，包无外部依赖；3.2+ 不兼容旧框架，不能直接升级。3.1.2 默认修改 TLS 证书策略，本项目适配恢复系统 TLS 1.2 和证书校验并限制并发。其 MIT 许可证及归属见 `third-party/Downloader/`，发布包也会携带。项目以 GNU GPLv3 发布，无任何担保。


![常见设备及已安装驱动；有候选与无候选设备同时保留，演示数据](docs/images/device-inventory-zh-CN.png)

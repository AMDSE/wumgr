# WuMgr WPF Preview 2：打开即检查运行环境

## 从哪个文件打开

解压完整包，运行 **`程序\WuMgr.exe`**。它是独立的 Windows 原生启动器，不依赖 .NET，因此即使电脑缺少 WPF 运行环境，也能先完成检查。`WuMgr.Wpf.exe` 保留为实际 WPF 主程序；直接打开它会绕过环境补装流程。

已安装 .NET Framework 4.8、4.8.1 或兼容的更高版本时，直接启动 WPF，不下载运行环境，不申请额外管理员权限。`--demo`、`--lang en-US` 等参数仍可通过新入口传给主程序。

## 缺少环境时会发生什么

启动器显示安装准备界面，自动通过 HTTPS 下载微软官方 .NET Framework 4.8 离线安装包。安装包目前约 **116 MiB**，不随 ZIP 捆绑；保持主程序体积小，但缺少环境的电脑首次启动必须联网。

下载完后核对固定安装包的 SHA-256，再验证 Windows Authenticode 信任链及实际签名者 Microsoft Corporation；任一验证不通过就停止。安装包仅使用固定微软 HTTPS 地址，禁止重定向；如果微软将来更换文件或地址，需要更新启动器，不会退回不校验的下载方式。只有补装运行环境时才请求 UAC 管理员授权，使用 `/passive /norestart` 安装。普通账号需要管理员批准，不能绕过系统授权或组织策略。

成功后重新检测环境，再自动打开 WPF。如果微软安装器报告需要重启，只提示用户手动重启；不会主动重启，也不会在本轮强行打开主程序。重启后再运行新入口即可。

下载中可取消；安装开始后由微软安装器处理，启动器等待结果，不能把关闭窗口当作安装回滚。取消下载可能需要等待当前网络请求超时（最多约 15 秒，系统代理处理可能更久）。网络故障、拒绝 UAC、安装失败或检测异常均停止并显示信息，可在解决问题后重试。不会因为环境安装成功就自动扫描或安装驱动。

## 兼容范围

WPF 主程序仍是 .NET Framework 4.8，不需要 .NET SDK、.NET 8 Desktop Runtime 或 VC++ 再发行包。64 位 Windows 不会因为使用 32 位原生启动器，就把 AnyCPU WPF 限制为 32 位。

自动补装支持 Windows 10 1607 及以后版本、Windows 11 的 x86/x64 系统。更旧系统不在本版支持范围内；ARM64 缺环境时不使用不匹配的 x86/x64 安装包。已有兼容环境时可尝试打开，但不能视为已完成 ARM64 实机兼容验收。

检测依据是注册表 `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full` 的 DWORD `Release >= 528040`，覆盖 32/64 位注册表视图。注册表检测并不等于完整运行环境健康检查；环境损坏时应通过 Windows 或微软安装器修复，而不是反复盲装。

## 构建与验证

完整源码在 `源码`。Windows 上使用 .NET 8 SDK 或更新版本构建 WPF，并安装 x86 MinGW-w64（提供 i686-w64-mingw32-gcc） 来编译原生启动器。运行 `build-wpf.ps1` 会构建两个部分，输出到 `dist\WuMgr-WPF`。Linux 交叉编译原生启动器使用 `bash Bootstrapper/build.sh`，需 MinGW-w64 i686；运行 `python3 package-wpf.py` 打包完整源码与 Release 程序。

交付中的构建与逻辑测试不代表已经在真实 Windows 上完成首次安装。请在可恢复的虚拟机上验收：

- 已有 4.8 / 4.8.1：不下载、不弹 UAC，中文/空格路径及演示参数可正常打开。
- 缺少或只有旧版 .NET：自动下载，显示 UAC，安装成功后自动启动。
- 下载中断、离线、拒绝 UAC、微软安装器失败：清楚报错，不启动 WPF、不改驱动和保护策略。
- 返回需重启：不自动重启、不启动 WPF；手动重启后可再次打开。
- 重复双击、取消下载、不可信安装包和不支持的系统：不重复安装、不执行未验证文件。

## 微软资料

[版本检测规则](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed) · [4.8 官方下载页](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48) · [离线安装包入口](https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net48-offline-installer) · [安装参数与返回码](https://learn.microsoft.com/en-us/dotnet/framework/deployment/deployment-guide-for-developers)

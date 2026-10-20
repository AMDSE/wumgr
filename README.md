> ## V8 / V10 微软目录深度修复包
> 已接入真实 Microsoft Update Catalog 硬件检索、`p` 参数翻页、详情完整硬件 ID 核验与官方 HTTPS 包链接解析。**目录候选只下载、不冒充 WUA 更新安装；旧包和独立 WPF Preview 2 均不变。** 尚待 Windows 实机验收。
>
> [V8 8.0.1 修复完整包](WuMgr-8.0.1-catalog.zip) · [V10 10.0.1 Preview 修复完整包](WuMgr-10.0.1-preview-catalog.zip) · [修复说明](目录驱动修复说明.md) · [SHA-256](SHA256SUMS-catalog.txt) · [V8 对 v8.0.0 的源码补丁](v8.0.1-catalog-from-v8.0.0.patch)
>
> 两包均含程序、完整对应源码、许可证、离线回归测试及在线协议日志。[在线验证结果](docs/Catalog验证结果.md)：真实两页 50 条不同更新，详情四条硬件 ID，官方下载地址解析成功；这不是 Windows WUA/安装实机验收。

## WPF Preview 2（与 WinForms 并存）

[下载 WPF Preview 2 完整包](https://github.com/AMDSE/wumgr/releases/tag/wpf-preview-2) · [WPF 中文指南](wpf/preview-2/README-WPF.zh-CN.md) · [English guide](wpf/preview-2/README-WPF.en.md) · [独立源码](wpf/preview-2) · [SHA-256](SHA256SUMS-WPF-preview-2.txt)

这是使用 XAML、原生 WPF 控件和 MVVM 协调层的新界面，共享 V10 驱动业务核心，不是嵌入 WinForms 或网页。它作为**独立预览版**发布；现有 [WinForms V10 预览版](https://github.com/AMDSE/wumgr/releases/tag/v10.0.0-preview)、`src/` 源码、历史包和 Release 均保留，**v8.0.0 继续作为 Latest 稳定版**。WPF 对应源码、构建/测试脚本及许可证单独放在 `wpf/preview-2/`，完整 ZIP 还含程序和 Windows 验收工具。下方原有界面截图属于 WinForms，不是 WPF 运行截图。

### WPF 如何运行

在 Windows 10/11 上完整解压 `WuMgr-WPF-preview-2.zip`，运行 **`程序\WuMgr.exe`**，保留同目录 DLL 和配置。原生启动器检查 .NET Framework 4.8+；已有环境直接启动，不需要 .NET SDK。缺少时通过 HTTPS 下载微软官方离线安装包（约 116 MiB，不随 ZIP 捆绑），校验固定 SHA-256、Authenticode 信任及微软签名者后，才请求 UAC 授权安装。需要重启时仅提示手动重启，不自动重启。自动补装范围是 Windows 10 1607+ / Windows 11 的 x86/x64；ARM64 缺环境时不自动补装。详见[运行环境说明](wpf/preview-2/RUNTIME-BOOTSTRAP.zh-CN.md)。

在 `程序` 目录可先用演示数据查看界面：

```powershell
.\WuMgr.exe --demo --lang zh-CN
.\WuMgr.exe --demo --lang en-US
```

演示模式禁用真实设备扫描、WUA、下载、安装和策略操作。正常启动只读取设备与保护策略；点击“扫描更新”才查询 WUA，默认仅下载。驱动安装或策略写入需自行以管理员身份重新打开 WPF，并逐项确认；WPF 不自动提权、不自动重启（缺环境时启动器安装运行环境的 UAC 请求除外）。程序未签名，不建议关闭系统防护绕过警告。

### 预览与 Windows 实测限制

已完成 .NET Framework 4.8 交叉编译和 **82 项可控后台逻辑断言**；这些不等于 Windows 实机通过。当前 Linux 构建环境没有验证真实 WPF 渲染、首次运行环境补装/UAC/重启、SetupAPI/WMI 设备枚举、WUA 扫描与安装、保护策略生效、高 DPI、多屏及辅助功能。`验收工具\运行安全验收.cmd` 提供已编译的安全演示验收工具，无需 SDK，但本次**尚未在 Windows 执行**；没有伪造 PASS 或 WPF 运行截图。首次驱动安装与策略写入请在可恢复的 Windows 测试机/虚拟机完成。

源码构建需 .NET SDK 8+ 与提供 `i686-w64-mingw32-gcc` 的 x86 MinGW-w64；在 `wpf/preview-2/` 执行 `build-wpf.ps1`，再执行 `test-wpf-windows.ps1`，输出为 `dist/WuMgr-WPF`。应用运行时仍是 .NET Framework 4.8，而非 .NET 8 Desktop Runtime。

---

> **下载 V10**：[WinForms V10 预览完整包](https://github.com/AMDSE/wumgr/releases/tag/v10.0.0-preview)。V8 仍为稳定版；独立 WPF 预览版见上方。

> **v10 预览版：圆角去方框、现代复选框。** 修复控件圆角外的矩形底色与原生外框，统一绿色圆角复选框和历史版本单选框，保留原生键盘/选择语义。已发布为 GitHub 预览版，Windows 实机及高 DPI 验收待完成。详见 [v10 说明](更新说明-v10-preview.md)。

> **v9 预览版：界面重构与渐进优化。** 新增侧栏分类、自适应操作区、Ctrl+F / Escape / F5；筛选复用已匹配快照，减少 URL 和硬件 ID 重复处理。保留原生 WinForms + .NET Framework 4.8，尚待 Windows 实机及高 DPI 验收。预览版不替换稳定版 v8，详见[预览版说明](更新说明-v9-preview.md)。下方 v8 功能与安全边界继续适用。

> v8：主界面列出常见硬件及已安装驱动版本、提供商、日期，没有更新也保留；过滤虚拟设备、音频端点、系统杂项和重复逻辑核心。完整原始清单仍在“设备搜索”。详见 [更新说明-v8](更新说明-v8.md)。Windows 实机验收待完成。

# WuMgr 驱动下载中心 · v8

**设备驱动搜索、历史候选选择、多线程下载，以及可选的静默安装。现在支持简体中文 / English 即时切换。**

[English](README.en.md) · [中文详细说明](README.zh-CN.md) · [下载 v8 完整包](WuMgr-驱动下载中心-v8.zip) · [v8 更新说明](更新说明-v8.md)

这是基于 [DavidXanatos/wumgr](https://github.com/DavidXanatos/wumgr) 的独立修改版，**不是上游官方发布**。当前可执行程序以驱动下载中心为入口，不包含上游旧版完整 Windows 更新管理界面。

## 运行界面预览

以下图片由本项目实际 WinForms 程序在 Linux / Mono 下运行并截取，**使用演示数据，不是真实设备扫描结果，也不是 Windows 实机验收截图**。Windows 的字体、窗口边框及缩放效果可能不同。演示模式禁止真实扫描、下载、安装和策略修改。

### 简体中文

![WuMgr 简体中文驱动下载中心，包含设备分类、历史版本和下载选项](docs/images/driver-center-zh-CN.png)

### English

![WuMgr English driver center with device filters, version history and download options](docs/images/driver-center-en-US.png)

![常见设备及已安装驱动；有候选与无候选设备同时保留，演示数据](docs/images/device-inventory-zh-CN.png)

## 能做什么

- **查设备、找驱动**：读取本机设备，按设备名、厂商、硬件 ID 或驱动版本搜索；设备搜索窗口展示硬件 ID / 兼容 ID 精确匹配的候选。
- **查看历史候选**：同一设备按版本归组，可展开、选择并下载。仅展示当前 Windows 更新源返回的适用候选，不保证完整版本历史；列表最高版本不等于安装建议。
- **自动匹配下载**：跳过旧版、版本未知、无地址及被取代候选，开始前展示确认。
- **多线程下载**：Downloader 3.1.2，每个文件最多 8 分块、4 并发请求，显示进度与速度；小文件或不支持 Range 时改用单流。
- **按需安装和保护**：默认仅下载。用户主动勾选并确认后，可通过 WUA 后台静默安装；可选择启用可恢复的全机驱动更新保护。
- **双语界面**：顶栏选择「简体中文 / English」，无需重启，保留当前操作选择并记住语言偏好。

## 下载与运行

1. [下载 `WuMgr-驱动下载中心-v8.zip`](WuMgr-驱动下载中心-v8.zip)，完整解压，不要直接在压缩包内运行。
2. 在 **Windows 10 / 11** 打开 `程序\wumgr.exe`；需要 **.NET Framework 4.8**、Windows 更新服务与网络。
3. 点击「扫描设备驱动」，核对设备型号和版本，选择保存目录后下载。也可以进入「设备搜索」查看精确匹配。
4. 若确需安装，先右键「以管理员身份运行」，勾选「下载后后台静默安装」并阅读确认提示。程序不自动提权、不自动重启，默认也不开启更新保护。

[v5 旧版完整包](WuMgr-驱动下载中心-v5.zip) 仍保留，便于回退。v8 包含程序、完整对应源码、双语 README、界面预览、测试日志和 SHA-256 清单。

## 中英文切换

顶栏语言选择器即时切换主窗口、版本卡片、设备搜索、详情、保护说明与操作提示。设备型号、硬件 ID、厂商名称，以及 Windows 返回的原始错误信息保持原文。

首次默认简体中文，之后使用本地保存的语言偏好。也可通过命令行指定本次启动的语言：

```bat
wumgr.exe --lang zh-CN
wumgr.exe --lang en-US
wumgr.exe --demo --lang en-US
wumgr.exe --preview preview-en.png --lang en-US
```

`--demo` 与 `--preview` 使用安全演示数据；命令行语言用于本次启动。界面内手动选择会保存偏好。截图模式不修改用户偏好。

## 安全边界与已知限制

**下载不等于安装。** 多线程 HTTP 下载独立于 [Windows Update Agent（WUA）](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/portal-client) 的系统安装缓存；安装时 WUA 可能再次下载。程序不会直接执行下载目录中的 EXE、脚本或 CAB，不绕过系统签名检查；安装可能短暂断网、闪屏或需要手动重启。取消不会回滚已经完成的安装。

**续传有条件。** 取消或失败后保留 `.part` 与 `.resume.xml`；相同 URL、相同目录，且服务器支持 Range 并提供有效强 ETag 或 Last-Modified 时可尝试恢复。资源改变或缺少校验标识则重新下载，不承诺进程崩溃后续传。

**保护不是全面拦截。** 使用微软 [ExcludeWUDriversInQualityUpdate 策略](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update#excludewudriversinqualityupdate)，仅排除常规质量更新中的驱动，不关闭安全更新；不能保证拦截功能更新、手动安装或厂商工具。全机生效，组织策略可能覆盖，家庭版不承诺生效。开启前备份相关原值，可通过界面恢复。

**验证范围：Linux / Mono 自动化测试与演示截图；Windows 真实 WMI、WUA、微软 CDN、管理员权限和策略生效仍待实机验收。** 不应据此把程序视为已完成 Windows 生产环境验证。参见 [Windows 验收清单](tests/Windows验收清单.md) 和 [测试日志](最终测试日志-v8.txt)。

## 构建与测试

Windows：安装 Visual Studio 2022「.NET 桌面开发」及 .NET Framework 4.8 目标包，在源码根目录执行：

```bat
build-windows.cmd
```

脚本在缺包时通过 NuGet CLI 还原固定的 Downloader 3.1.2。Linux / Mono 仅用于测试与演示：

```bash
bash tests/run-tests.sh
python3 package-release.py
```

源码目录 `wumgr/`，入口 `DriverProgram.cs`；自动化用例在 `tests/`。打包脚本不会把 NuGet 缓存、`bin/obj` 或测试 EXE 混入源码。

## 许可与来源

本项目依据 [GNU GPLv3](LICENSE) 发布，无任何担保。上游版权归 DavidXanatos；下载组件 [Downloader 3.1.2](https://github.com/bezzad/Downloader/tree/v3.1.2) 使用 MIT 许可，见 [第三方说明](third-party/Downloader/README.md)。图标来源 Icons8，保留原有归属与许可。原仓库根目录 Apache 2.0 模板与包内 GPLv3 不一致，已原样备份并写明 [许可历史与适用范围](docs/licensing/README.md)。更多数据处理说明见 [隐私政策](PRIVACY_POLICY.md)。

# Preview 2 验证结果

## 已实际完成

- WPF Release 解决方案构建成功，0 警告、0 错误。包括 WuMgr.Core、WuMgr.Wpf、WuMgr.Wpf.Logic、WuMgr.Wpf.Smoke。见 `build-preview2.txt`。
- 原有 WPF 协调层 82 项逻辑断言通过，使用可控假后台，没有执行真实驱动、WUA、网络下载或保护策略写入。见 `logic-preview2.txt`。
- 原生启动器通过 MinGW-w64 x86 严格交叉编译；产物为 PE32 Windows GUI，无 CLR 头，仅依赖 Windows 系统 DLL（含系统自带 msvcrt），不需要 .NET 或单独 VC++ 再发行包。见 `bootstrapper-build.txt`、`bootstrapper-imports.txt`。
- 313 项启动策略断言通过，覆盖 .NET 版本阈值、成功/失败/需重启退出码、空/短 URL、精确微软主机及长主机名边界。其中 270 项为不同主机长度的边界检查，并非 313 种独立安装场景。相同测试在 AddressSanitizer 与 UndefinedBehaviorSanitizer 下通过。见 `bootstrapper-policy-tests.txt`、`bootstrapper-policy-sanitizers.txt`。
- 原 WPF 的 4 个 XAML 文件 XML 解析通过，并由 WPF 构建编译。原有设备、驱动下载/安装、更新保护和 ViewModel 业务源码与 Preview 1 未改动。
- 从微软官方最终 HTTPS 地址实际下载运行环境安装包，核实文件大小 121346568 字节并固定 SHA-256。提取的内嵌签名者为 Microsoft Corporation；Linux 信任库不能代替 Windows WinVerifyTrust，未声称已通过 Windows 签名验证。见 `microsoft-installer-source.txt`。
- 完整包通过 ZIP CRC、所需入口/源码/依赖、程序目录小于 5 MB、文件 SHA-256 清单逐项校验。运行命令：`python3 tests/test-preview2-package.py <ZIP路径>`。

## 仍需 Windows 实机或虚拟机验收

**当前环境是 Linux。未执行真实 Windows 缺环境首次启动、WinTrust 信任/吊销检查、UAC、微软运行环境安装与重启、注册表访问权限异常、下载取消及安装阶段关闭窗口的实际时序。**

同样未执行 WPF 真界面冒烟、真实硬件和 WUA、管理员保护策略启用/恢复、实际驱动安装、高 DPI/多屏和辅助功能验收。Windows 冒烟工具已提供和编译，但没有虚构 PASS 或程序截图。

`build-release.txt`、`logic-tests.txt`、`assembly-references.txt` 和 `v10-original-regression.txt` 为 Preview 1 随包保留的历史基线，不作为本轮原生安装器实机证据。Windows 验收场景详见 `RUNTIME-BOOTSTRAP.zh-CN.md`。

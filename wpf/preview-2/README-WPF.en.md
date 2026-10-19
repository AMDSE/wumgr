# WuMgr WPF Preview 2

**New entry point: `程序/WuMgr.exe`.** The native bootstrapper runs without .NET, checks for .NET Framework 4.8+, and downloads the official Microsoft offline installer only when required (~116 MiB). It verifies Microsoft Authenticode trust before requesting UAC approval, installs with `/passive /norestart`, rechecks the runtime and opens WPF. Reboot-required results stop and ask for a manual reboot. Existing runtimes do not trigger a runtime download. Automatic provisioning targets Windows 10 1607+ / Windows 11 x86/x64; missing runtimes on ARM64 are not auto-installed. The full ZIP stays small because the runtime is not bundled. Building the native launcher requires an x86 MinGW-w64 toolchain providing i686-w64-mingw32-gcc in addition to the .NET SDK. Windows first-run installation, UAC, reboot and UI remain unverified in this Linux build environment.


A genuine XAML/WPF desktop implementation with an MVVM coordinator and the existing V10 driver core. No WinForms hosting, browser surface, or simulated UI screenshot. It runs independently from WinForms V10.

**Preview, not Windows-accepted stable software.** The application and its Windows UI smoke harness compile successfully for .NET Framework 4.8. Eighty-two controlled-backend logic assertions pass. The current build environment is Linux: actual WPF rendering, Windows device enumeration, WUA installation and high-DPI behavior have not been validated here.

## Run

On Windows 10/11, extract the whole archive and open `程序/WuMgr.exe`. Keep its DLLs and configuration alongside it. The .NET SDK is not needed to run the prebuilt application.

Use `WuMgr.exe --demo --lang en-US` for synthetic data without device, WUA, download, installation, policy or shell side effects. `--lang zh-CN` selects Chinese. The demonstration URLs use the reserved `example.invalid` domain and are never downloaded.

Normal startup reads device inventory and policy status only. Click **Scan updates** to query WUA; **Refresh devices** does not query updates. Download-only is the default and does not require administrator rights. Optional installation and policy writes require manually reopening as administrator and a specific confirmation. No automatic elevation or restart.

## Important behavior

Devices are matched only by complete hardware/compatible IDs. Historical versions are limited to applicable candidates returned by WUA, not an exhaustive vendor archive. No candidate does not mean a device lacks a driver. Automatic selection is only a conservative selection aid, not a compatibility guarantee.

Search/category changes retain selected rows and selected versions. Hidden selected devices remain part of the global selection. Review the exact names, versions, IDs, revisions and destination in the confirmation before downloading. A shared update identity is downloaded once even if it applies to multiple device instances.

Each download operation freezes its material choices. Failed downloads and stale directories are excluded from installation. WUA revalidates update identity, revision and applicability and may download again into its system cache; arbitrary downloaded executables/INF files are not executed. EULAs are not accepted on the user's behalf. Installation can disconnect the network, flicker the display or require a manual restart.

Cancellation cannot immediately interrupt every COM/WUA call. Closing waits for actual completion, and completed installations are not rolled back. Counts, failures and reboot requirements are preserved even if cancellation happens after an installation.

Protection backs up and changes the computer-wide `ExcludeWUDriversInQualityUpdate` policy. It excludes drivers from quality updates, not all driver changes; security updates remain enabled. Organizational policy/MDM may override it, and Home edition effectiveness is not promised. Restore the original policy before later searches if protection interferes with WUA. Missing/corrupt backups or externally modified values are not blindly overwritten.

## Build and validate

The `源码` folder contains the solution and complete source. With .NET SDK 8 or newer on Windows, run `build-wpf.ps1`, then `test-wpf-windows.ps1`. Runtime output: `dist/WuMgr-WPF`.

The precompiled safe tools in `验收工具` do not need the SDK. The logic suite uses controlled fakes and a single-thread UI synchronization context. The WPF smoke harness uses demo data, checks actual bindings/search/selection/language/footer bounds and writes four language/size previews, one detail preview and `RESULT.txt`. It has been compiled but **not executed on Windows in this delivery**. No fabricated PASS or rendering images are included.

Still validate on Windows: device discovery, WUA/network failures, administrator policy enable/restore, signed-driver installation, keyboard/accessibility, 100/125/150/200% display scaling and multi-monitor changes. Use a recoverable test machine/VM for first installation and policy-write tests.

Shortcuts: F5 scans updates; Ctrl+F searches; Esc clears the focused search box. Language preference is saved to `%LOCALAPPDATA%/WuMgr/language.txt`. The executable is unsigned; do not disable protection to bypass a security warning.

Licensing: GNU GPL v3, no warranty. Complete source, build/test scripts, Downloader MIT license and inherited icon attribution accompany this archive. See `LICENSE`, `Downloader-LICENSE.txt`, `THIRD-PARTY-NOTICES.md` and `图标归属.txt`.

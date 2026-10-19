> v8: the main list shows common detected hardware and installed driver version/provider/date even without updates. Virtual devices, endpoint noise and duplicate CPU cores are hidden here; Device search retains the full inventory. Windows hardware acceptance is pending.

# WuMgr Driver Download Center · v8

**Find device drivers, choose historical candidates, download in parallel, and optionally install silently. Now with instant Simplified Chinese / English switching.**

[简体中文](README.md) · [Download v8](WuMgr-驱动下载中心-v8.zip) · [Detailed Chinese guide](README.zh-CN.md)

This is an independent modification of [DavidXanatos/wumgr](https://github.com/DavidXanatos/wumgr), **not an official upstream release**. The current executable opens the driver center; it does not include the upstream legacy full Windows update-management UI.

## Running interface

These are captures of the actual WinForms application running under Linux / Mono with **demonstration data**, not real-device scan results or Windows acceptance screenshots. Fonts, borders and scaling may differ on Windows. Real scanning, downloads, installation and policy changes are disabled in demo mode.

### English

![English driver center: device filters, version history and download options](docs/images/driver-center-en-US.png)

### 简体中文

![Simplified Chinese driver center](docs/images/driver-center-zh-CN.png)

![Common device inventory: installed drivers with and without update candidates](docs/images/device-inventory-en-US.png)

## Features

- Discover local devices and search by model, vendor, hardware ID or driver version. The device-search dialog shows exact hardware/compatible-ID matches.
- Group candidates by device and choose historical versions. Only candidates returned by the current Windows update source are shown; complete history is not guaranteed. The highest listed version is not an installation recommendation.
- Automatically select matching drivers, skipping older, unknown-version, superseded and missing-URL candidates, then ask before downloading.
- Download through pinned Downloader 3.1.2: up to 8 blocks and 4 concurrent requests per file, with speed and progress. Small files and servers without Range use one stream.
- Download only by default. Optional WUA-based silent installation and reversible system-wide driver-update protection require explicit user action.
- Switch Simplified Chinese / English from the header without restarting; keep current selections and remember the language preference.

## Download and run

1. [Download the complete v8 ZIP](WuMgr-驱动下载中心-v8.zip) and extract it fully.
2. On **Windows 10 / 11**, open `程序\wumgr.exe`. **.NET Framework 4.8**, Windows Update services and network access are required.
3. Scan for drivers, verify the model and version, choose a folder, and download. Use device search to inspect exact matches.
4. Only if installation is intended, run as administrator, explicitly enable silent installation, and review the confirmation. The app does not auto-elevate or restart the computer, and protection is off by default.

The package includes the executable, corresponding source, bilingual documentation, previews, test output and SHA-256 manifest. [Previous Releases](https://github.com/AMDSE/wumgr/releases) remain available for rollback.

## Language switching

Use the header selector: **简体中文 / English**. It updates the app-owned UI, driver cards, device search, details, policy explanations and operation prompts. Device names, hardware IDs, vendor text and original Windows error details are not translated.

The first launch defaults to Simplified Chinese. Subsequent launches use the saved local preference. A command-line option overrides the language for the current launch:

```bat
wumgr.exe --lang zh-CN
wumgr.exe --lang en-US
wumgr.exe --demo --lang en-US
wumgr.exe --preview preview-en.png --lang en-US
```

Changing language in the UI saves the preference. Preview mode uses safe demo data and does not change saved preferences.

## Safety and limitations

**HTTP downloads and system installation are separate.** Installation uses the [Windows Update Agent API](https://learn.microsoft.com/en-us/windows/win32/wua_sdk/portal-client) and its system cache, so WUA may download files again. The application does not execute EXE, script or CAB files from the download folder or bypass Windows signature checks. Driver installation may briefly disconnect networking, flicker the display or require a manual reboot. Canceling does not roll back completed installations.

**Resume is conditional.** Cancellation/failure preserves `.part` and `.resume.xml` files. Resuming the same URL into the same folder requires Range support and a valid strong ETag or Last-Modified validator. Changed resources or missing validators cause a fresh download. Crash recovery is not guaranteed.

**Protection is limited.** The Microsoft [ExcludeWUDriversInQualityUpdate policy](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-update#excludewudriversinqualityupdate) excludes drivers from routine quality updates without disabling security updates. It does not guarantee blocking feature-update drivers, manual changes or vendor tools. It applies system-wide, can be overridden by organizational policy, and is not promised to work on Home editions. Relevant original values are backed up for restoration.

**Validation covers Linux / Mono automated tests and demo rendering only.** Real Windows WMI/WUA, Microsoft CDN downloads, elevation and effective policy behavior still require on-device acceptance testing. See the [Windows checklist ](src/tests/Windows验收清单.md) and [v8 test log](最终测试日志-v8.txt).

## Build and test

On Windows, install Visual Studio 2022 with the .NET desktop workload and .NET Framework 4.8 targeting pack, then run:

```bat
cd src
build-windows.cmd
```

The script restores pinned Downloader 3.1.2 with NuGet CLI if needed. On Linux / Mono, for tests and demo rendering only:

```bash
cd src
bash tests/run-tests.sh
python3 package-release.py
```

The repository entry point is `src/wumgr/DriverProgram.cs`; tests are in `src/tests/`. Packaging excludes NuGet caches, generated `bin/obj` directories and test executables from the source tree.

## License and attribution

Released under [GNU GPLv3](LICENSE), without warranty. Original project copyright belongs to DavidXanatos. [Downloader 3.1.2](https://github.com/bezzad/Downloader/tree/v3.1.2) is MIT-licensed; attribution is included in [third-party/Downloader](third-party/Downloader/README.md) and the release. Existing Icons8 attribution and licensing are retained. The previous root Apache 2.0 template is preserved for historical reference; see [licensing notes](docs/licensing/README.md). See [privacy policy](PRIVACY_POLICY.md).

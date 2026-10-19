# Attribution and third-party notices

The WPF work is a modification of WuMgr and is distributed under GNU General Public License version 3, without warranty. Complete corresponding source and build scripts accompany this preview.

Original WuMgr: Copyright © DavidXanatos 2018–2020. Upstream: https://github.com/DavidXanatos/wumgr . The user's modification repository is https://github.com/AMDSE/wumgr . Preserve the copyright and license notices in the retained source files. Full GPL text: `LICENSE`.

Downloader 3.1.2: Copyright © 2021 Behzad Khosravifar; MIT. Upstream: https://github.com/bezzad/Downloader . The complete MIT license is in `Downloader-LICENSE.txt`. NuGet restores the pinned package during development; the runtime package includes its DLL.

The inherited application icon comes from the existing WuMgr tree. The retained Icons8 attribution states Creative Commons Attribution-NoDerivs 3.0 Unported and requires a link to https://icons8.com . The original attribution is included as `图标归属.txt`; this preview does not claim ownership of the icon. WPF theme geometry, cards and check marks are drawn in XAML rather than downloaded icon assets.

Microsoft .NET Framework reference assemblies are build-time-only packages (`PrivateAssets=all`). Microsoft Windows, WPF and .NET Framework are platform components; they are not reimplemented or redistributed as part of this preview. This software is not affiliated with or endorsed by Microsoft or hardware vendors.

This is an unsigned preview executable, not an authenticated vendor driver package. Do not disable Windows security to bypass a warning. Driver installation is performed only through Windows Update Agent with identity/applicability checks, never by executing arbitrary files from the download directory.

The Preview 2 native Win32 bootstrapper is also distributed under GPL v3 with its complete source and build scripts. It uses Windows system APIs and does not bundle .NET Framework or a VC++ redistributable. When needed, it downloads the Microsoft .NET Framework 4.8 installer from Microsoft; the runtime remains governed by Microsoft's license. The bootstrapper itself is unsigned; downloaded runtime installers must pass Microsoft Authenticode publisher verification before execution.

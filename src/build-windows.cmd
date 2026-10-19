@echo off
chcp 65001 >nul
setlocal EnableExtensions
cd /d "%~dp0"

where msbuild >nul 2>nul
if errorlevel 1 (
  echo 请从 Visual Studio 2022 的 Developer Command Prompt 运行此脚本。
  echo 安装 .NET 桌面开发工作负载与 .NET Framework 4.8 目标包。
  exit /b 1
)

rem Restore the pinned package from packages.config; never float to another version.
if not exist packages\Downloader.3.1.2\lib\net462\Downloader.dll (
  where nuget >nul 2>nul
  if not errorlevel 1 (
    nuget restore wumgr.sln -PackagesDirectory packages -NonInteractive -Verbosity quiet
    if errorlevel 1 exit /b 1
  ) else (
    echo 未找到 nuget.exe，且本地 packages\Downloader.3.1.2 不完整。
    echo 请安装 NuGet CLI，或先准备校验过的 Downloader 3.1.2 包。
    exit /b 1
  )
)
if not exist packages\Downloader.3.1.2\lib\net462\Downloader.dll (
  echo Downloader 3.1.2 net462 未还原，拒绝继续构建。
  exit /b 1
)

msbuild wumgr.sln /t:Rebuild /p:Configuration=Release /p:Platform="Any CPU" /nologo /verbosity:minimal
if errorlevel 1 exit /b 1
copy /y LICENSE wumgr\bin\Release\LICENSE >nul
copy /y README.zh-CN.md wumgr\bin\Release\使用说明.md >nul
copy /y third-party\Downloader\LICENSE wumgr\bin\Release\Downloader-LICENSE.txt >nul
copy /y third-party\Downloader\README.md wumgr\bin\Release\Downloader-归属说明.md >nul
if not exist wumgr\bin\Release\Downloader.dll (
  echo 构建输出缺少 Downloader.dll。
  exit /b 1
)
echo 构建完成：wumgr\bin\Release\wumgr.exe
endlocal

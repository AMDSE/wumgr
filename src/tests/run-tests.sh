#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
TEST_CONFIG_DIR="$(mktemp -d)"
export WUMGR_CONFIG_DIR="$TEST_CONFIG_DIR"
trap 'rm -rf -- "$TEST_CONFIG_DIR"' EXIT
mkdir -p artifacts
exec > >(tee "artifacts/最终测试日志.txt") 2>&1

# Use the checked-in package when available. If absent, fetch only the pinned
# official flat-container package and verify it before extracting.
PKG_DIR="packages/Downloader.3.1.2"
DLL="$PKG_DIR/lib/net462/Downloader.dll"
PKG="$PKG_DIR/Downloader.3.1.2.nupkg"
PKG_SHA=886bc8ef5556719af3cf7c37b7e0937d58d3c3b00153ad26d1ee0533069d0d8d
DLL_SHA=c5d961ecaade6078a4024ad6c1ce4d463f686d1807ab0d2d5214ca762d8386ca
if [[ ! -f "$DLL" ]]; then
  command -v curl >/dev/null || { echo "缺少 curl，无法获取固定 Downloader 包" >&2; exit 1; }
  mkdir -p "$PKG_DIR"
  curl --fail --location --retry 3 -o "$PKG" https://api.nuget.org/v3-flatcontainer/downloader/3.1.2/downloader.3.1.2.nupkg
  echo "$PKG_SHA  $PKG" | sha256sum -c -
  unzip -q -o "$PKG" -d "$PKG_DIR"
fi
[[ -f "$DLL" ]]
echo "$DLL_SHA  $DLL" | sha256sum -c -

xbuild wumgr.sln /t:Rebuild /p:Configuration=Release /verbosity:minimal
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Windows.Forms -r:System.Drawing -r:System.Core -out:wumgr/bin/Release/UiTests.exe tests/UiTests.cs
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Core -out:wumgr/bin/Release/CatalogTests.exe tests/CatalogTests.cs
xvfb-run -a -s '-screen 0 1440x1100x24' mono wumgr/bin/Release/UiTests.exe
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Windows.Forms -r:System.Drawing -r:System.Core -out:wumgr/bin/Release/LanguageTests.exe tests/LanguageTests.cs
xvfb-run -a -s '-screen 0 1440x1100x24' mono wumgr/bin/Release/LanguageTests.exe
mono wumgr/bin/Release/CatalogTests.exe
if [[ -f tests/ServiceTests.cs ]]; then
  mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Net.Http -r:System.Core -out:wumgr/bin/Release/ServiceTests.exe tests/ServiceTests.cs
  mono wumgr/bin/Release/ServiceTests.exe
fi
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Core -out:wumgr/bin/Release/DeviceTests.exe tests/DeviceTests.cs
mono wumgr/bin/Release/DeviceTests.exe
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Core -out:wumgr/bin/Release/InventoryTests.exe tests/InventoryTests.cs
mono wumgr/bin/Release/InventoryTests.exe
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Core -out:wumgr/bin/Release/EnumerationTests.exe tests/EnumerationTests.cs
mono wumgr/bin/Release/EnumerationTests.exe
mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:System.Core -out:wumgr/bin/Release/InstallerTests.exe tests/InstallerTests.cs
mono wumgr/bin/Release/InstallerTests.exe
mcs -langversion:7.2 -r:System.Core -out:wumgr/bin/Release/PolicyTests.exe wumgr/DriverUpdatePolicy.cs tests/PolicyTests.cs
mono wumgr/bin/Release/PolicyTests.exe
if [[ -f tests/DownloaderTests.cs ]]; then
  mcs -langversion:7.2 -r:wumgr/bin/Release/wumgr.exe -r:"$DLL" -r:System.Net.Http -r:System.Core -out:wumgr/bin/Release/DownloaderTests.exe tests/DownloaderTests.cs
  mono wumgr/bin/Release/DownloaderTests.exe
else
  echo "缺少 tests/DownloaderTests.cs" >&2
  exit 1
fi
mkdir -p docs/images
xvfb-run -a -s '-screen 0 1440x1100x24' mono wumgr/bin/Release/wumgr.exe --preview "$PWD/artifacts/中文驱动界面-演示.png" --lang zh-CN
xvfb-run -a -s '-screen 0 1440x1100x24' mono wumgr/bin/Release/wumgr.exe --preview "$PWD/docs/images/driver-center-zh-CN.png" --lang zh-CN
xvfb-run -a -s '-screen 0 1440x1100x24' mono wumgr/bin/Release/wumgr.exe --preview "$PWD/docs/images/driver-center-en-US.png" --lang en-US

#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
if [ -z "${DOTNET:-}" ]; then
    if command -v dotnet >/dev/null 2>&1; then DOTNET=$(command -v dotnet)
    elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
    else echo 'A .NET SDK is required (set DOTNET to its executable).' >&2; exit 2
    fi
fi
command -v mono >/dev/null 2>&1 || { echo 'Mono is required to execute net48 tests on Linux.' >&2; exit 2; }
command -v mcs >/dev/null 2>&1 || { echo 'The Mono C# compiler (mcs) is required.' >&2; exit 2; }
TEMP=$(mktemp -d)
trap 'rm -rf "$TEMP"' EXIT HUP INT TERM
"$DOTNET" build "$ROOT/WuMgr.Core/WuMgr.Core.csproj" -c Release --nologo >/dev/null
CORE="$ROOT/WuMgr.Core/bin/Release/net48/WuMgr.Core.dll"
DOWNLOADER="${DOWNLOADER_DLL:-$HOME/.nuget/packages/downloader/3.1.2/lib/net462/Downloader.dll}"
mcs -langversion:7.2 -target:exe -main:VMTests -out:"$TEMP/WuMgr.Wpf.Logic.exe" \
 -r:System.dll -r:System.Core.dll -r:System.Management.dll -r:"$CORE" -r:"$DOWNLOADER" \
 "$ROOT/wumgr/UiStrings.cs" \
 "$ROOT/WuMgr.Wpf/Services/Contracts.cs" "$ROOT/WuMgr.Wpf/Services/DriverBackend.cs" \
 "$ROOT/WuMgr.Wpf/Services/Localization.cs" "$ROOT/WuMgr.Wpf/Services/DemoData.cs" \
 "$ROOT/WuMgr.Wpf/ViewModels/MainViewModel.cs" "$ROOT/tests/VMTests.cs"
MONO_PATH="$(dirname "$CORE"):$(dirname "$DOWNLOADER")${MONO_PATH:+:$MONO_PATH}" mono "$TEMP/WuMgr.Wpf.Logic.exe"

[CmdletBinding()]
param([ValidateSet('Release','Debug')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$compiler = Get-Command i686-w64-mingw32-gcc -ErrorAction SilentlyContinue
if (-not $compiler) {
    throw 'Install an x86 MinGW-w64 toolchain providing i686-w64-mingw32-gcc and put its bin directory on PATH. MSVC is not supported by this script.'
}
Push-Location $PSScriptRoot
try {
    New-Item -Force -ItemType Directory bin | Out-Null
    & $compiler.Source -m32 -std=c11 -Wall -Wextra -Werror -Wno-unknown-pragmas -Wno-missing-field-initializers -Os -municode -mwindows bootstrapper.c -o bin/WuMgr.exe -lwinhttp -lwintrust -lcrypt32 -lshell32 -lshlwapi -ladvapi32 -lole32 -luser32 -lgdi32
    if ($LASTEXITCODE -ne 0) { throw "Bootstrapper build failed: $LASTEXITCODE" }
    Write-Host 'Built Bootstrapper/bin/WuMgr.exe (native x86 GUI)'
}
finally {
    Pop-Location
}

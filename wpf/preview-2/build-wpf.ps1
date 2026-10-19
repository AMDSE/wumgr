[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK (or newer) before building. The program itself needs only .NET Framework 4.8.'
}
Push-Location $root
try {
    & dotnet build (Join-Path $root 'WuMgr.Wpf.sln') -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    & (Join-Path $root 'Bootstrapper/build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Native launcher build failed: $LASTEXITCODE" }
    $output = Join-Path $root "WuMgr.Wpf/bin/$Configuration/net48"
    $delivery = Join-Path $root 'dist/WuMgr-WPF'
    New-Item -ItemType Directory -Path $delivery -Force | Out-Null
    Copy-Item (Join-Path $root 'Bootstrapper/bin/WuMgr.exe') $delivery -Force
    Copy-Item (Join-Path $root 'START-HERE.zh-CN.txt') (Join-Path $delivery '启动说明.txt') -Force
    # Do not remove unrelated files or user data from an existing output folder.
    Get-ChildItem $output -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config', '.pdb' } |
        Copy-Item -Destination $delivery -Force
    Copy-Item (Join-Path $root 'LICENSE') $delivery -Force
    Copy-Item (Join-Path $root 'Downloader-LICENSE.txt') $delivery -Force
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $delivery -Force
    Copy-Item (Join-Path $root 'README-WPF.zh-CN.md') $delivery -Force
    Write-Host "Built: $delivery/WuMgr.exe (runtime-checking entry point)"
}
finally { Pop-Location }

[CmdletBinding()]
param([string]$ResultDirectory = (Join-Path $PSScriptRoot 'artifacts/windows-smoke'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'The WPF runtime smoke test requires Windows.' }
& (Join-Path $PSScriptRoot 'build-wpf.ps1') -Configuration Release
$logic = Join-Path $PSScriptRoot 'tests/WuMgr.Wpf.Logic/bin/Release/net48/WuMgr.Wpf.Logic.exe'
& $logic
if ($LASTEXITCODE -ne 0) { throw "Logic tests failed: $LASTEXITCODE" }
$smoke = Join-Path $PSScriptRoot 'tests/WuMgr.Wpf.Smoke/bin/Release/net48/WuMgr.Wpf.Smoke.exe'
& $smoke ([IO.Path]::GetFullPath($ResultDirectory))
if ($LASTEXITCODE -ne 0) { throw "WPF runtime smoke failed. Review $ResultDirectory/RESULT.txt" }
Write-Host "Safe demo UI tests passed: $ResultDirectory"
Write-Host 'Real hardware discovery, WUA, administrator policy writes, actual installation, and 125/150/200% display scaling still need a separate Windows acceptance test.'

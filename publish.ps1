<#
.SYNOPSIS
  Builds a standalone LedBalloon you can copy to a machine with nothing installed on it.

.DESCRIPTION
  Produces a single self-contained executable. The .NET runtime is bundled inside it, so the
  target machine needs no SDK, no runtime and no Visual Studio — copy the file and run it.

.PARAMETER Runtime
  win-x64 (default), win-arm64, linux-x64, osx-x64 or osx-arm64.
  Publishing for a runtime is cross-platform: you can build the Linux one from Windows.

.EXAMPLE
  .\publish.ps1
  .\publish.ps1 -Runtime linux-x64
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$outDir = Join-Path $root "publish/$Runtime"

Write-Host "Publishing LedBalloon for $Runtime..." -ForegroundColor Cyan

dotnet publish (Join-Path $root 'src/LedBalloon.App/LedBalloon.App.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    --output $outDir

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Get-ChildItem $outDir -File |
    Where-Object { $_.Name -like 'LedBalloon.App*' -and $_.Extension -in @('.exe', '') } |
    Select-Object -First 1

Write-Host ''
Write-Host "Done: $($exe.FullName)" -ForegroundColor Green
Write-Host ("Size:  {0:N0} MB" -f ($exe.Length / 1MB))
Write-Host ''
Write-Host 'Copy that one file anywhere and run it. Nothing needs installing on the other machine.'

if ($Runtime -like 'win-*') {
    Write-Host ''
    Write-Host 'Note: Windows SmartScreen will warn the first time, because the file is not code-signed.' -ForegroundColor Yellow
    Write-Host 'More info -> Run anyway. Signing it properly needs a certificate.' -ForegroundColor Yellow
}

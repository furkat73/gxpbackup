# Build distribution: publish + two MSI packages.
# Requirements: .NET SDK, WiX v5 (`dotnet tool install --global wix --version 5.0.2`).
# Run from repo root: powershell -ExecutionPolicy Bypass -File installer/build.ps1
#
# Output:
#   dist\MySqlBackupUtility-0.1.0-win-x64.msi          - all users (Program Files, ADMIN REQUIRED)
#   dist\MySqlBackupUtility-0.1.0-win-x64-peruser.msi  - current user only, NO ADMIN REQUIRED
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root 'dist\stage'
$msi = Join-Path $root 'dist\MySqlBackupUtility-0.1.0-win-x64.msi'
$msiUser = Join-Path $root 'dist\MySqlBackupUtility-0.1.0-win-x64-peruser.msi'

# Resolve wix.exe (global dotnet tool) explicitly: plain "wix" may not be on PATH.
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
if (-not (Test-Path $wix)) {
    $cmd = Get-Command wix -ErrorAction SilentlyContinue
    if ($cmd) { $wix = $cmd.Source } else { throw "wix.exe not found. Install: dotnet tool install --global wix --version 5.0.2" }
}

# Clean stage so files from previous builds do not leak in.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

dotnet publish (Join-Path $root 'src\MySqlBackupUtility') -c Release -r win-x64 `
  --self-contained true /p:PublishSingleFile=true -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

# -arch x64 is required, otherwise the MSI is x86 and ProgramFiles64Folder
# resolves to "Program Files (x86)".
# -dcl low -ct 1: low cabinet compression and a single compressor thread.
# The default (high, multi-threaded) makes wixnative.exe crash with 0xC0000005
# on this 154 MB single-file exe.
& $wix build -arch x64 -dcl low -ct 1 (Join-Path $PSScriptRoot 'Package.wxs') -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build (perMachine) failed: $LASTEXITCODE" }

& $wix build -arch x64 -dcl low -ct 1 (Join-Path $PSScriptRoot 'PackagePerUser.wxs') -o $msiUser
if ($LASTEXITCODE -ne 0) { throw "wix build (perUser) failed: $LASTEXITCODE" }

Write-Host ""
Write-Host "Done:"
Write-Host "  All users (admin required): $msi"
Write-Host "  No admin required:          $msiUser"

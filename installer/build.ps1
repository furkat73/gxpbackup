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
# wixnative.exe (cabinet compressor) intermittently crashes with 0xC0000005 on
# this large single-file exe, so retry a few times with decreasing compression.
function Build-Msi($wxs, $out) {
    foreach ($lvl in @('low', 'mszip', 'none')) {
        for ($i = 1; $i -le 3; $i++) {
            & $wix build -arch x64 -dcl $lvl -ct 1 $wxs -o $out
            if ($LASTEXITCODE -eq 0) { return }
            Write-Host "wix build failed (level=$lvl, attempt=$i): $LASTEXITCODE"
            Start-Sleep -Seconds 2
        }
    }
    throw "wix build failed for $wxs"
}

Build-Msi (Join-Path $PSScriptRoot 'Package.wxs') $msi
Build-Msi (Join-Path $PSScriptRoot 'PackagePerUser.wxs') $msiUser

Write-Host ""
Write-Host "Done:"
Write-Host "  All users (admin required): $msi"
Write-Host "  No admin required:          $msiUser"

# Сборка дистрибутива: publish + MSI.
# Требования: .NET SDK, WiX v5 (`dotnet tool install --global wix --version 5.0.2`).
# Запуск из корня репо: powershell -ExecutionPolicy Bypass -File installer/build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $root 'dist\stage'
$msi = Join-Path $root 'dist\MySqlBackupUtility-0.1.0-win-x64.msi'

dotnet publish (Join-Path $root 'src\MySqlBackupUtility') -c Release -r win-x64 `
  --self-contained true /p:PublishSingleFile=true /p:DeleteExistingFiles=true -o $stage

# -arch x64: обязательно, иначе MSI собирается как x86 и ProgramFiles64Folder
# разрешается в "Program Files (x86)" (файлы ложатся не туда).
wix build -arch x64 (Join-Path $PSScriptRoot 'Package.wxs') -o $msi
Write-Host "MSI: $msi"

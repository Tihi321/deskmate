# Builds DeskMate and installs it to %LOCALAPPDATA%\DeskMate, then starts it.
# DeskMate registers itself to start with Windows on launch.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'DeskMate'

Get-Process DeskMate -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

dotnet publish "$root\src\DeskMate\DeskMate.csproj" `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -o $dest

Start-Process (Join-Path $dest 'DeskMate.exe')
Write-Host "DeskMate installed to $dest and started. Look for the 'D' icon in the tray." -ForegroundColor Green

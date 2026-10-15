# Stops DeskMate, removes autostart and the installed files.
# Pass -KeepSettings to keep %APPDATA%\DeskMate\settings.json.
param([switch]$KeepSettings)

Get-Process DeskMate -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'DeskMate' -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA 'DeskMate') -Recurse -Force -ErrorAction SilentlyContinue
if (-not $KeepSettings) {
    Remove-Item (Join-Path $env:APPDATA 'DeskMate') -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'DeskMate uninstalled.' -ForegroundColor Green

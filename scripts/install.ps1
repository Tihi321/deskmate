# Builds DeskMate and installs it to %LOCALAPPDATA%\DeskMate, then starts it.
# DeskMate registers itself to start with Windows on launch.
#
# If the .NET 8 Desktop Runtime is installed machine-wide, a small framework-dependent exe is built.
# Otherwise (e.g. the SDK was installed per-user with dotnet-install.ps1) the exe is self-contained,
# so it still starts at login without relying on DOTNET_ROOT.
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'DeskMate'

# The dotnet on PATH may be a runtime-only install (e.g. Program Files) while the SDK lives in a
# per-user install, so pick the first dotnet.exe that actually has a .NET 8+ SDK.
$candidates = @(
    $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' })
    (Get-Command dotnet -All -ErrorAction SilentlyContinue).Source
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
$dotnet = $candidates | Where-Object {
    & $_ --list-sdks 2>$null | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 8 }
} | Select-Object -First 1
if (-not $dotnet) { throw '.NET 8 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0' }
Write-Host "Using $dotnet"

$machineRuntime = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.WindowsDesktop.App'
if (-not $SelfContained -and -not (Get-ChildItem $machineRuntime -Directory -Filter '8.*' -ErrorAction SilentlyContinue)) {
    Write-Host 'No machine-wide .NET 8 Desktop Runtime found, building a self-contained exe.' -ForegroundColor Yellow
    $SelfContained = $true
}
$publishArgs = if ($SelfContained) { @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }
               else { @('--self-contained', 'false') }

Get-Process DeskMate -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

& $dotnet publish "$root\src\DeskMate\DeskMate.csproj" `
    -c Release -r win-x64 @publishArgs `
    -p:PublishSingleFile=true -o $dest
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Start-Process (Join-Path $dest 'DeskMate.exe')
Write-Host "DeskMate installed to $dest and started. Look for the 'D' icon in the tray." -ForegroundColor Green

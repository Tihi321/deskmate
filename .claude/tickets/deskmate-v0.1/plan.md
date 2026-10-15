# DeskMate – Plan

Windows tray app (C# / .NET 8 WinForms) with small everyday helpers. Starts with Windows.

## Why this stack
- Single native `.exe`, ~15 MB RAM, no Python/AHK runtime.
- Direct Win32 access: `SetThreadExecutionState` (keep awake), `RegisterHotKey`, `GetPixel`.
- Autostart via `HKCU\...\Run` – no admin rights.

## Architecture
- `Program.cs` – single-instance mutex, starts `TrayApp`.
- `TrayApp.cs` – tray icon + menu; holds the list of helpers.
- `Helpers/IHelper.cs` – every feature implements `Initialize(ctx)` + `CreateMenuItems()` + `Dispose()`.
- `Core/` – `HotkeyManager`, `Settings` (`%APPDATA%\DeskMate\settings.json`), `Autostart`, `ScreenInfo`/`Clip`, `Native` (P/Invoke).

## v0.1 – done (code in repo, needs first build/test on Windows)
- [x] Tray icon (grey = idle, green = keep-awake active), tooltip with status/time left
- [x] Keep awake: Off · Until I turn it off · 15m · 30m · 1h · 2h · 4h · 8h
  - [x] "Keep screen on too" switch
  - [x] Always starts Off after reboot (temporary by design)
  - [x] Hotkey `Ctrl+Alt+K`, double-click tray icon toggles
  - [x] Notification when timer ends
- [x] Color picker: `Ctrl+Alt+C` copies color under cursor (HEX/RGB), recent colors menu
- [x] Cursor coordinates: `Ctrl+Alt+X` copies `X, Y`
- [x] Live overlay `Ctrl+Alt+O`: X, Y + color swatch next to cursor (click-through)
- [x] Start with Windows toggle; Edit settings; Exit
- [x] `scripts/install.ps1` / `uninstall.ps1`, GitHub Actions build

## Verify v0.1
- [x] `dotnet build` – 0 warnings / 0 errors (.NET SDK 8.0.426, installed per-user to `%LOCALAPPDATA%\Microsoft\dotnet`)
- [x] Smoke test (2026-10-15): all 4 hotkeys registered while running and released on exit;
      `Ctrl+Alt+X` → `1776, 684`, `Ctrl+Alt+C` → `#181818` on clipboard; overlay window shows/hides
- [x] Keep awake: system execution state `0x0` → `0x3` (SYSTEM + DISPLAY required) → `0x0` on toggle
      (checked via `CallNtPowerInformation(SystemExecutionState)` — `powercfg /requests` needs admin)
- [x] Self-contained Release exe starts without any .NET on PATH / `DOTNET_ROOT`
- [ ] Test on a 4K / 150 % scaling monitor (overlay size, picked pixel matches cursor) – manual
- [ ] Test multi-monitor (negative coordinates, overlay flipping at screen edges) – manual
- [ ] Reboot test: autostarts, keep awake is Off – manual

### Fixes made during verification
- Hotkey parser: was case-sensitive (`ctrl+alt+k` failed) and ignored `Win`; now tolerant, supports `Win`.
  Failure message now says whether the combo is invalid or taken by another app (`HelperContext.RegisterHotkey`).
- `settings.json` wrote `Ctrl+Alt+K`; now readable `Ctrl+Alt+K`.
- `install.ps1`: finds a per-user `dotnet`, and builds self-contained when no machine-wide
  .NET 8 Desktop Runtime exists (a framework-dependent exe showed "You must install .NET" otherwise).

## v0.2 – ideas (pick)
- [ ] Custom keep-awake duration ("Until 17:30…" / enter minutes)
- [ ] Settings window instead of editing JSON (hotkey recorder)
- [ ] Reload settings without restart
- [ ] Clipboard history (last N texts)
- [ ] Always-on-top toggle for active window
- [ ] Mute microphone hotkey
- [ ] Screen ruler / pixel distance
- [ ] Real `.ico` file + app icon in exe
- [ ] Release workflow: tag `v*` → GitHub Release with `DeskMate.exe`

## Conventions
- One helper = one file in `Helpers/`, registered in `TrayApp.cs`.
- Helper-specific settings go in `Core/Settings.cs` with sensible defaults.
- Commit prefix: `NO-TASK:` (or ticket key).

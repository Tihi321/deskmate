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

## Next: verify v0.1
- [ ] `dotnet build` – fix any compile errors
- [ ] Test on a 4K / 150 % scaling monitor (overlay size, picked pixel matches cursor)
- [ ] Test multi-monitor (negative coordinates, overlay flipping at screen edges)
- [ ] Keep awake: confirm with `powercfg /requests` (should list DeskMate under SYSTEM / DISPLAY)
- [ ] Reboot test: autostarts, keep awake is Off

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

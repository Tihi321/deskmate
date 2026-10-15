# DeskMate

A small Windows tray app with everyday helpers. Starts with Windows, lives next to the clock.

| Helper | How | Default hotkey |
|---|---|---|
| **Keep awake** | No sleep (and optionally no screen-off). Off · Until I turn it off · 15m · 30m · 1h · 2h · 4h · 8h. Always starts **Off** after reboot. Icon turns green while active; tooltip shows time left. Double-click the tray icon to toggle. | `Ctrl+Alt+K` |
| **Color picker** | Copies the color under the mouse (HEX or RGB). Recent colors in the menu. | `Ctrl+Alt+C` |
| **Cursor coordinates** | Copies `X, Y` of the mouse. | `Ctrl+Alt+X` |
| **Live overlay** | Small box next to the cursor showing X, Y and color. | `Ctrl+Alt+O` |

Right-click the tray icon for everything else (Start with Windows, edit settings, exit).

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (only the .NET 8 Desktop Runtime is needed to run)

## Install

```powershell
git clone <your-repo-url> deskmate
cd deskmate
.\scripts\install.ps1
```

This publishes `DeskMate.exe` to `%LOCALAPPDATA%\DeskMate` and starts it. On launch DeskMate adds itself to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, so it starts with Windows (toggle in the menu).

Uninstall: `.\scripts\uninstall.ps1` (add `-KeepSettings` to keep your settings).

## Develop

```powershell
dotnet run --project src/DeskMate
```

(Debug builds don't touch the autostart registry entry.)

## Settings

`%APPDATA%\DeskMate\settings.json` — open via **Edit settings…** in the tray menu. Restart DeskMate after editing.

```json
{
  "StartWithWindows": true,
  "KeepScreenOn": true,
  "KeepAwakeHotkey": "Ctrl+Alt+K",
  "ColorFormat": "HEX",
  "ColorPickerHotkey": "Ctrl+Alt+C",
  "CoordsHotkey": "Ctrl+Alt+X",
  "OverlayHotkey": "Ctrl+Alt+O"
}
```

## Adding a helper

1. Create `src/DeskMate/Helpers/MyHelper.cs` implementing `IHelper`:

```csharp
internal sealed class MyHelper : IHelper
{
    private HelperContext _ctx = null!;

    public void Initialize(HelperContext ctx)
    {
        _ctx = ctx;
        ctx.Hotkeys.Register("Ctrl+Alt+M", () => ctx.Notify("Hello", "MyHelper ran"));
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        var item = new ToolStripMenuItem("My helper");
        item.Click += (_, _) => _ctx.Notify("Hello", "Clicked");
        yield return item;
    }

    public void Dispose() { }
}
```

2. Add `new MyHelper()` to the list in `TrayApp.cs`.
3. Optional: add its settings (e.g. hotkey) to `Core/Settings.cs`.

## Project layout

```
src/DeskMate/
  Program.cs            entry point, single-instance guard
  TrayApp.cs            tray icon, menu, helper registration
  Core/                 hotkeys, settings, autostart, screen/clipboard, Win32 interop
  Helpers/              one file per feature
scripts/                install / uninstall
.github/workflows/      CI build → DeskMate.exe artifact
```

## Ideas for next helpers

Clipboard history · always-on-top for the active window · quick timer/pomodoro · mute mic hotkey · screen ruler.

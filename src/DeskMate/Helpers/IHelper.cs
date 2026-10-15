using DeskMate.Core;

namespace DeskMate.Helpers;

/// <summary>Shared services handed to every helper.</summary>
/// <param name="Notify">Show a tray notification (title, text).</param>
/// <param name="StateChanged">Ask the tray to refresh its icon/tooltip.</param>
internal sealed record HelperContext(
    Settings Settings,
    HotkeyManager Hotkeys,
    Action<string, string> Notify,
    Action StateChanged)
{
    /// <summary>Registers a global hotkey; tells the user if the combo is invalid or taken.</summary>
    public bool RegisterHotkey(string combo, Action handler)
    {
        if (Hotkeys.Register(combo, handler)) return true;
        Notify("DeskMate", HotkeyManager.TryParse(combo, out _, out _)
            ? $"Hotkey {combo} is already used by another app."
            : $"Hotkey \"{combo}\" is not valid. Use e.g. Ctrl+Alt+K.");
        return false;
    }
}

/// <summary>
/// A DeskMate feature. To add one: implement this, then add it to the list in TrayApp.
/// </summary>
internal interface IHelper : IDisposable
{
    /// <summary>Register hotkeys, read settings, start timers.</summary>
    void Initialize(HelperContext context);

    /// <summary>Items this helper contributes to the tray menu.</summary>
    IEnumerable<ToolStripItem> CreateMenuItems();
}

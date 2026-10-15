using DeskMate.Core;

namespace DeskMate.Helpers;

/// <summary>Shared services handed to every helper.</summary>
/// <param name="Notify">Show a tray notification (title, text).</param>
/// <param name="StateChanged">Ask the tray to refresh its icon/tooltip.</param>
internal sealed record HelperContext(
    Settings Settings,
    HotkeyManager Hotkeys,
    Action<string, string> Notify,
    Action StateChanged);

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

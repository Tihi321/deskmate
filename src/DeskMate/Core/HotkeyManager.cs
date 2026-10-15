namespace DeskMate.Core;

/// <summary>Registers system-wide hotkeys like "Ctrl+Alt+C" and calls a handler when pressed.</summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams());

    /// <returns>false if the combo is invalid or already taken by another app.</returns>
    public bool Register(string combo, Action handler)
    {
        if (!TryParse(combo, out uint mods, out uint vk)) return false;

        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, mods | Native.MOD_NOREPEAT, vk)) return false;
        _handlers[id] = handler;
        return true;
    }

    /// <summary>Parses "Ctrl+Alt+K", "win+shift+F1", "Alt+1" (case-insensitive) into RegisterHotKey args.</summary>
    public static bool TryParse(string? combo, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        foreach (string raw in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Native.MOD_CONTROL; continue;
                case "alt": modifiers |= Native.MOD_ALT; continue;
                case "shift": modifiers |= Native.MOD_SHIFT; continue;
                case "win" or "windows": modifiers |= Native.MOD_WIN; continue;
            }

            if (vk != 0) return false; // only one non-modifier key allowed
            string name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw; // "1" -> Keys.D1
            if (!Enum.TryParse(name, ignoreCase: true, out Keys key) || int.TryParse(name, out _)) return false;
            key &= Keys.KeyCode;
            if (key == Keys.None) return false;
            vk = (uint)key;
        }

        return vk != 0 && modifiers != 0;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _handlers.TryGetValue((int)m.WParam, out var handler))
            handler();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (int id in _handlers.Keys) Native.UnregisterHotKey(Handle, id);
        _handlers.Clear();
        DestroyHandle();
    }
}

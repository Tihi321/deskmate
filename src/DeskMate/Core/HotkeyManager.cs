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
        Keys keys;
        try { keys = (Keys)new KeysConverter().ConvertFromInvariantString(combo)!; }
        catch { return false; }

        uint mods = Native.MOD_NOREPEAT;
        if ((keys & Keys.Control) == Keys.Control) mods |= Native.MOD_CONTROL;
        if ((keys & Keys.Alt) == Keys.Alt) mods |= Native.MOD_ALT;
        if ((keys & Keys.Shift) == Keys.Shift) mods |= Native.MOD_SHIFT;
        uint vk = (uint)(keys & Keys.KeyCode);

        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, mods, vk)) return false;
        _handlers[id] = handler;
        return true;
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

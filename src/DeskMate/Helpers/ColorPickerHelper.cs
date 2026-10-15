using DeskMate.Core;

namespace DeskMate.Helpers;

/// <summary>
/// Hotkey copies the color under the mouse (HEX or RGB). Keeps a short history.
/// Can be disabled from the menu, which releases the hotkey for other apps.
/// </summary>
internal sealed class ColorPickerHelper : IHelper
{
    private const int MaxRecent = 8;

    private HelperContext _ctx = null!;
    private readonly List<Color> _recent = new();
    private ToolStripMenuItem _root = null!;
    private ToolStripMenuItem _enabledItem = null!;
    private ToolStripMenuItem _pickItem = null!;
    private ToolStripMenuItem _recentMenu = null!;
    private ToolStripMenuItem _formatMenu = null!;

    public void Initialize(HelperContext context)
    {
        _ctx = context;
        if (context.Settings.ColorPickerEnabled) context.RegisterHotkey(context.Settings.ColorPickerHotkey, Pick);
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        _root = new ToolStripMenuItem("Color picker");

        _enabledItem = new ToolStripMenuItem("Enabled");
        _enabledItem.Click += (_, _) => SetEnabled(!_ctx.Settings.ColorPickerEnabled);
        _root.DropDownItems.Add(_enabledItem);
        _root.DropDownItems.Add(new ToolStripSeparator());

        _pickItem = new ToolStripMenuItem("Pick color under cursor")
        {
            Enabled = false,
            ShortcutKeyDisplayString = _ctx.Settings.ColorPickerHotkey,
        };
        _root.DropDownItems.Add(_pickItem);

        _formatMenu = new ToolStripMenuItem("Copy format");
        foreach (var format in new[] { "HEX", "RGB" })
        {
            var item = new ToolStripMenuItem(format) { Checked = _ctx.Settings.ColorFormat == format };
            item.Click += (_, _) =>
            {
                _ctx.Settings.ColorFormat = format;
                _ctx.Settings.Save();
                foreach (ToolStripMenuItem i in _formatMenu.DropDownItems) i.Checked = i.Text == format;
                RebuildRecent();
            };
            _formatMenu.DropDownItems.Add(item);
        }
        _root.DropDownItems.Add(_formatMenu);

        _recentMenu = new ToolStripMenuItem("Recent colors");
        RebuildRecent();
        _root.DropDownItems.Add(_recentMenu);

        UpdateMenu();
        yield return _root;
    }

    private void SetEnabled(bool enabled)
    {
        _ctx.Settings.ColorPickerEnabled = enabled;
        _ctx.Settings.Save();

        if (enabled) _ctx.RegisterHotkey(_ctx.Settings.ColorPickerHotkey, Pick);
        else _ctx.Hotkeys.Unregister(Pick);

        UpdateMenu();
    }

    private void UpdateMenu()
    {
        bool enabled = _ctx.Settings.ColorPickerEnabled;
        _root.Text = enabled ? "Color picker" : "Color picker (off)";
        _enabledItem.Checked = enabled;
        _pickItem.ShortcutKeyDisplayString = enabled ? _ctx.Settings.ColorPickerHotkey : null;
        _formatMenu.Enabled = enabled;
        _recentMenu.Enabled = enabled;
    }

    private void Pick()
    {
        var (_, color) = ScreenInfo.UnderCursor();
        string text = ScreenInfo.FormatColor(color, _ctx.Settings.ColorFormat);
        Clip.Set(text);

        _recent.Remove(color);
        _recent.Insert(0, color);
        if (_recent.Count > MaxRecent) _recent.RemoveAt(MaxRecent);
        RebuildRecent();

        _ctx.Notify("Color copied", text);
    }

    private void RebuildRecent()
    {
        foreach (ToolStripItem old in _recentMenu.DropDownItems) old.Image?.Dispose();
        _recentMenu.DropDownItems.Clear();

        if (_recent.Count == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("(none yet)") { Enabled = false });
            return;
        }

        foreach (var color in _recent)
        {
            string text = ScreenInfo.FormatColor(color, _ctx.Settings.ColorFormat);
            var item = new ToolStripMenuItem(text, Swatch(color));
            item.Click += (_, _) => Clip.Set(text);
            _recentMenu.DropDownItems.Add(item);
        }
    }

    private static Bitmap Swatch(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, 16, 16);
        g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
        return bmp;
    }

    public void Dispose() { }
}

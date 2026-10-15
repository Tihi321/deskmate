using DeskMate.Core;

namespace DeskMate.Helpers;

/// <summary>Hotkey copies the color under the mouse (HEX or RGB). Keeps a short history.</summary>
internal sealed class ColorPickerHelper : IHelper
{
    private const int MaxRecent = 8;

    private HelperContext _ctx = null!;
    private readonly List<Color> _recent = new();
    private ToolStripMenuItem _recentMenu = null!;
    private ToolStripMenuItem _formatMenu = null!;

    public void Initialize(HelperContext context)
    {
        _ctx = context;
        if (!context.Hotkeys.Register(context.Settings.ColorPickerHotkey, Pick))
            context.Notify("DeskMate", $"Hotkey {context.Settings.ColorPickerHotkey} is unavailable.");
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        var root = new ToolStripMenuItem("Color picker");

        root.DropDownItems.Add(new ToolStripMenuItem("Pick color under cursor")
        {
            Enabled = false,
            ShortcutKeyDisplayString = _ctx.Settings.ColorPickerHotkey,
        });

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
        root.DropDownItems.Add(_formatMenu);

        _recentMenu = new ToolStripMenuItem("Recent colors");
        RebuildRecent();
        root.DropDownItems.Add(_recentMenu);

        yield return root;
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

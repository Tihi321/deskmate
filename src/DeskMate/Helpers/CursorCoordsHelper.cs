using DeskMate.Core;

namespace DeskMate.Helpers;

/// <summary>Hotkey copies cursor X,Y. Optional live overlay shows X,Y + color next to the cursor.</summary>
internal sealed class CursorCoordsHelper : IHelper
{
    private HelperContext _ctx = null!;
    private CursorOverlay? _overlay;
    private ToolStripMenuItem _overlayItem = null!;

    public void Initialize(HelperContext context)
    {
        _ctx = context;
        if (!context.Hotkeys.Register(context.Settings.CoordsHotkey, CopyCoords))
            context.Notify("DeskMate", $"Hotkey {context.Settings.CoordsHotkey} is unavailable.");
        if (!context.Hotkeys.Register(context.Settings.OverlayHotkey, ToggleOverlay))
            context.Notify("DeskMate", $"Hotkey {context.Settings.OverlayHotkey} is unavailable.");
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        var root = new ToolStripMenuItem("Cursor coordinates");

        root.DropDownItems.Add(new ToolStripMenuItem("Copy X, Y under cursor")
        {
            Enabled = false,
            ShortcutKeyDisplayString = _ctx.Settings.CoordsHotkey,
        });

        _overlayItem = new ToolStripMenuItem("Show live overlay (X, Y + color)")
        {
            ShortcutKeyDisplayString = _ctx.Settings.OverlayHotkey,
        };
        _overlayItem.Click += (_, _) => ToggleOverlay();
        root.DropDownItems.Add(_overlayItem);

        yield return root;
    }

    private void CopyCoords()
    {
        var (p, _) = ScreenInfo.UnderCursor();
        string text = $"{p.X}, {p.Y}";
        Clip.Set(text);
        _ctx.Notify("Coordinates copied", text);
    }

    private void ToggleOverlay()
    {
        _overlay ??= new CursorOverlay(_ctx.Settings);
        if (_overlay.Visible) _overlay.Hide();
        else _overlay.Show();
        _overlayItem.Checked = _overlay.Visible;
    }

    public void Dispose() => _overlay?.Dispose();
}

/// <summary>Small click-through, non-focusable box that follows the mouse.</summary>
internal sealed class CursorOverlay : Form
{
    private readonly Settings _settings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private Point _pos;
    private Color _color;
    private readonly float _scale;

    public CursorOverlay(Settings settings)
    {
        _settings = settings;
        _scale = DeviceDpi / 96f;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Opacity = 0.92;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 12f * _scale, GraphicsUnit.Pixel);
        Size = new Size((int)(220 * _scale), (int)(28 * _scale));
        _timer.Tick += (_, _) => Follow();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible;
        if (Visible) Follow();
    }

    private void Follow()
    {
        var (p, c) = ScreenInfo.UnderCursor();
        if (p == _pos && c == _color) return;
        _pos = p;
        _color = c;

        var area = Screen.FromPoint(p).WorkingArea;
        int offset = (int)(18 * _scale);
        int x = p.X + offset, y = p.Y + offset;
        if (x + Width > area.Right) x = p.X - Width - offset / 2;
        if (y + Height > area.Bottom) y = p.Y - Height - offset / 2;
        Location = new Point(x, y);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int pad = (int)(6 * _scale), sw = Height - 2 * pad;
        using (var brush = new SolidBrush(_color)) e.Graphics.FillRectangle(brush, pad, pad, sw, sw);
        e.Graphics.DrawRectangle(Pens.White, pad, pad, sw, sw);

        string text = $"{_pos.X}, {_pos.Y}    {ScreenInfo.FormatColor(_color, _settings.ColorFormat)}";
        var textRect = new Rectangle(pad * 2 + sw, 0, Width - sw - pad * 2, Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textRect, ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

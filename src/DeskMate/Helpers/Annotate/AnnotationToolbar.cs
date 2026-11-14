using System.Drawing;
using System.Windows.Forms;

namespace DeskMate.Helpers.Annotate;

/// <summary>Compact floating toolbar for the annotation overlay. Buttons are never focusable.</summary>
internal sealed class AnnotationToolbar : Panel
{
    public static readonly Color[] Palette =
    {
        ColorTranslator.FromHtml("#EF4444"), ColorTranslator.FromHtml("#FACC15"), ColorTranslator.FromHtml("#22C55E"),
        ColorTranslator.FromHtml("#3B82F6"), Color.Black, Color.White
    };
    public static readonly float[] BaseWidths = { 3f, 6f, 12f };
    public static readonly float[] BaseFontPx = { 20f, 28f, 40f };

    private static readonly Color BackgroundColor = ColorTranslator.FromHtml("#1C1C20");
    private static readonly Color BorderColor = ColorTranslator.FromHtml("#3C3C44");
    private static readonly Color ButtonColor = ColorTranslator.FromHtml("#2A2A30");
    private static readonly Color SelectedColor = ColorTranslator.FromHtml("#4A4A66");
    private static readonly Color AccentColor = ColorTranslator.FromHtml("#60A5FA");
    private static readonly Color SaveColor = ColorTranslator.FromHtml("#16A34A");

    private readonly float _scale;
    private readonly Font _font;
    private readonly Font _glyphFont;
    private readonly ToolTip _tip = new();
    private Font? _boldFont;

    private readonly ToolButton[] _toolButtons = new ToolButton[6];
    private readonly ToolButton[] _colorButtons = new ToolButton[6];
    private readonly ToolButton[] _widthButtons = new ToolButton[3];
    private ToolButton _regionButton = null!, _undoButton = null!, _redoButton = null!;

    private bool _dragging;
    private Point _dragStartCursor;
    private Point _dragStartLocation;

    public AnnotationTool Tool { get; private set; } = AnnotationTool.Pen;
    public Color CurrentColor { get; private set; } = Palette[0];
    public int WidthIndex { get; private set; } = 1;

    public event EventHandler? RegionClicked, UndoClicked, RedoClicked, ClearClicked, SaveClicked, CancelClicked;

    public AnnotationToolbar(float scale)
    {
        _scale = scale;
        _font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _glyphFont = new Font("Segoe UI", 14f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        BackColor = BackgroundColor;
        DoubleBuffered = true;
        TabStop = false;
        Cursor = Cursors.Default;

        int pad = S(6), gap = S(4), group = S(10), h = S(32), sq = S(32);
        int x = pad;

        // Grip
        var grip = new Label
        {
            Text = "≡", Font = _glyphFont, ForeColor = Color.Silver, BackColor = BackgroundColor,
            TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.SizeAll,
            Bounds = new Rectangle(x, pad, S(20), h)
        };
        grip.MouseDown += GripMouseDown;
        grip.MouseMove += GripMouseMove;
        grip.MouseUp += (_, _) => _dragging = false;
        _tip.SetToolTip(grip, "Drag to move");
        Controls.Add(grip);
        x += grip.Width + group;

        // Tools
        string[] glyphs = { "✎", "▌", "↗", "▭", "◯", "T" };
        string[] names = { "Pen", "Highlighter", "Arrow", "Rectangle", "Ellipse", "Text" };
        for (int i = 0; i < _toolButtons.Length; i++)
        {
            int idx = i;
            var b = MakeButton(glyphs[i], names[i], _glyphFont);
            b.Bounds = new Rectangle(x, pad, sq, h);
            b.Click += (_, _) => { Tool = (AnnotationTool)idx; UpdateSelection(); };
            _toolButtons[i] = b;
            x += sq + gap;
        }
        x += group - gap;

        // Colors
        string[] colorNames = { "Red", "Yellow", "Green", "Blue", "Black", "White" };
        for (int i = 0; i < Palette.Length; i++)
        {
            int idx = i;
            var b = MakeButton("", colorNames[i], _font);
            b.Bounds = new Rectangle(x, pad + S(4), S(24), S(24));
            b.BackColor = Palette[i];
            b.FlatAppearance.MouseOverBackColor = Palette[i];
            b.FlatAppearance.MouseDownBackColor = Palette[i];
            b.Click += (_, _) => { CurrentColor = Palette[idx]; UpdateSelection(); };
            _colorButtons[i] = b;
            x += S(24) + gap;
        }
        x += group - gap;

        // Widths
        string[] widthLabels = { "S", "M", "L" };
        string[] widthNames = { "Thin", "Medium", "Thick" };
        for (int i = 0; i < _widthButtons.Length; i++)
        {
            int idx = i;
            var b = MakeButton(widthLabels[i], $"Width: {widthNames[i]}", _font);
            b.Bounds = new Rectangle(x, pad, S(28), h);
            b.Click += (_, _) => { WidthIndex = idx; UpdateSelection(); };
            _widthButtons[i] = b;
            x += S(28) + gap;
        }
        x += group - gap;

        // Region
        _regionButton = AddTextButton("Region", "Select region to save", ref x, pad, h, gap, (_, _) => RegionClicked?.Invoke(this, EventArgs.Empty));
        x += group - gap;

        // Undo / redo / clear
        _undoButton = MakeButton("↶", "Undo (Ctrl+Z)", _glyphFont);
        _undoButton.Bounds = new Rectangle(x, pad, sq, h);
        _undoButton.Click += (_, _) => UndoClicked?.Invoke(this, EventArgs.Empty);
        x += sq + gap;
        _redoButton = MakeButton("↷", "Redo (Ctrl+Y)", _glyphFont);
        _redoButton.Bounds = new Rectangle(x, pad, sq, h);
        _redoButton.Click += (_, _) => RedoClicked?.Invoke(this, EventArgs.Empty);
        x += sq + gap;
        AddTextButton("Clear", "Clear all annotations (undoable)", ref x, pad, h, gap, (_, _) => ClearClicked?.Invoke(this, EventArgs.Empty));
        x += group - gap;

        // Save / Cancel
        var save = AddTextButton("Save", "Save (Enter)", ref x, pad, h, gap, (_, _) => SaveClicked?.Invoke(this, EventArgs.Empty));
        save.BackColor = SaveColor;
        save.FlatAppearance.MouseOverBackColor = ControlPaint.Light(SaveColor, 0.2f);
        _boldFont = new Font(_font, FontStyle.Bold);
        save.Font = _boldFont;
        AddTextButton("Cancel", "Cancel (Esc)", ref x, pad, h, gap, (_, _) => CancelClicked?.Invoke(this, EventArgs.Empty));

        Size = new Size(x - gap + pad, h + 2 * pad);
        SetUndoRedoState(false, false);
        UpdateSelection();
    }

    public void SetUndoRedoState(bool canUndo, bool canRedo)
    {
        _undoButton.Enabled = canUndo;
        _redoButton.Enabled = canRedo;
    }

    public void SetRegionActive(bool active)
    {
        _regionButton.BackColor = active ? SelectedColor : ButtonColor;
        _regionButton.FlatAppearance.BorderColor = active ? AccentColor : BorderColor;
        _regionButton.FlatAppearance.BorderSize = active ? 2 : 1;
    }

    private int S(float v) => (int)Math.Round(v * _scale);

    private ToolButton MakeButton(string text, string tip, Font font)
    {
        var b = new ToolButton
        {
            Text = text,
            Font = font,
            ForeColor = Color.White,
            BackColor = ButtonColor,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            TabStop = false,
            Cursor = Cursors.Hand,
            Padding = Padding.Empty
        };
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = BorderColor;
        b.FlatAppearance.MouseOverBackColor = SelectedColor;
        _tip.SetToolTip(b, tip);
        Controls.Add(b);
        return b;
    }

    private ToolButton AddTextButton(string text, string tip, ref int x, int pad, int h, int gap, EventHandler onClick)
    {
        int w = TextRenderer.MeasureText(text, _font).Width + S(20);
        var b = MakeButton(text, tip, _font);
        b.Bounds = new Rectangle(x, pad, w, h);
        b.Click += onClick;
        x += w + gap;
        return b;
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < _toolButtons.Length; i++)
            Highlight(_toolButtons[i], (int)Tool == i);
        for (int i = 0; i < _colorButtons.Length; i++)
        {
            bool sel = Palette[i] == CurrentColor;
            _colorButtons[i].FlatAppearance.BorderColor = sel ? AccentColor : BorderColor;
            _colorButtons[i].FlatAppearance.BorderSize = sel ? 2 : 1;
        }
        for (int i = 0; i < _widthButtons.Length; i++)
            Highlight(_widthButtons[i], WidthIndex == i);
    }

    private static void Highlight(ToolButton b, bool selected)
    {
        b.BackColor = selected ? SelectedColor : ButtonColor;
        b.FlatAppearance.BorderColor = selected ? AccentColor : BorderColor;
        b.FlatAppearance.BorderSize = selected ? 2 : 1;
    }

    private void GripMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragStartCursor = Cursor.Position;
        _dragStartLocation = Location;
    }

    private void GripMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging || Parent == null) return;
        var cur = Cursor.Position;
        int nx = _dragStartLocation.X + cur.X - _dragStartCursor.X;
        int ny = _dragStartLocation.Y + cur.Y - _dragStartCursor.Y;
        var area = Parent.ClientRectangle;
        nx = Math.Max(area.Left, Math.Min(nx, area.Right - Width));
        ny = Math.Max(area.Top, Math.Min(ny, area.Bottom - Height));
        Location = new Point(nx, ny);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            _font.Dispose();
            _glyphFont.Dispose();
            _boldFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class ToolButton : Button
    {
        public ToolButton()
        {
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowFocusCues => false;
    }
}

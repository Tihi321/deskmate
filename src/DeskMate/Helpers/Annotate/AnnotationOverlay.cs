using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DeskMate.Core;

namespace DeskMate.Helpers.Annotate;

/// <summary>
/// Borderless topmost window that covers one monitor, shows a frozen screenshot of it and lets the user
/// draw on it. All coordinates are monitor-local physical pixels (0,0 = top-left of the monitor).
/// </summary>
internal sealed class AnnotationOverlay : Form
{
    private const int DimAlpha = 140;
    private static readonly Color TextBoxBack = ColorTranslator.FromHtml("#1C1C20");

    private readonly Rectangle _screenBounds;
    private readonly Rectangle _monitorRect;   // (0,0,w,h) – the drawable area in client coordinates
    private readonly float _scale;
    private readonly Bitmap _screenshot;
    private readonly Bitmap _cache;            // screenshot + committed items (clipped to the region)
    private readonly AnnotationToolbar _toolbar;
    private readonly Font _labelFont;
    private readonly AnnotationDocument _document = new();

    // Region
    private Rectangle? _region;
    private bool _regionSelect;
    private bool _regionDragging;
    private Point _regionStart;
    private Rectangle _selection;

    // In-progress shape
    private bool _drawing;
    private AnnotationTool _drawTool;
    private Color _drawColor;
    private float _drawWidth;
    private Point _start;
    private Point _current;
    private readonly List<Point> _points = new();

    /// <summary>Area currently covered by transient drawing (in-progress shape or selection), for dirty-rect invalidation.</summary>
    private Rectangle _lastDirty = Rectangle.Empty;

    // Text tool
    private TextBox? _textBox;
    private Font? _textFont;
    private Color _textColor;
    private float _textFontPx;
    private float _textWidth;

    private bool _finished;
    private bool _resourcesDisposed;

    /// <summary>Raised on Enter/Save with the final image (region crop or full). The handler owns and disposes it.</summary>
    public event EventHandler<Bitmap>? Completed;

    /// <summary>Raised on Esc/Cancel.</summary>
    public event EventHandler? Cancelled;

    /// <param name="screenBounds">Physical bounds of the monitor.</param>
    /// <param name="screenshot">Owned by the overlay (disposed on close).</param>
    public AnnotationOverlay(Rectangle screenBounds, Bitmap screenshot, float scale, bool startInRegionSelect)
    {
        _screenBounds = screenBounds;
        _screenshot = screenshot;
        _scale = scale <= 0 ? 1f : scale;
        _monitorRect = new Rectangle(Point.Empty, screenBounds.Size);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        BackColor = Color.Black;
        Bounds = screenBounds;
        Cursor = Cursors.Cross;
        Text = "DeskMate annotate";

        _cache = new Bitmap(_monitorRect.Width, _monitorRect.Height, PixelFormat.Format32bppPArgb);
        RebuildCache();

        _labelFont = new Font("Segoe UI", 12f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);

        _toolbar = new AnnotationToolbar(_scale);
        _toolbar.Location = new Point(
            Math.Max(0, (_monitorRect.Width - _toolbar.Width) / 2),
            (int)Math.Round(12f * _scale));
        _toolbar.RegionClicked += (_, _) => { CommitText(); EnterRegionSelect(); };
        _toolbar.UndoClicked += (_, _) => { CommitText(); Undo(); };
        _toolbar.RedoClicked += (_, _) => { CommitText(); Redo(); };
        _toolbar.ClearClicked += (_, _) => { CommitText(); ClearAll(); };
        _toolbar.SaveClicked += (_, _) => Save();
        _toolbar.CancelClicked += (_, _) => CancelSession();
        Controls.Add(_toolbar);
        _toolbar.SetUndoRedoState(false, false);

        if (startInRegionSelect) EnterRegionSelect();
    }

    /// <summary>Brings the running session to the front (used when the hotkey is pressed again).</summary>
    public void ActivateSession()
    {
        if (IsDisposed || !Visible) return;
        Activate();
        Native.SetForegroundWindow(Handle);
    }

    // ── Window / DPI ──────────────────────────────────────────

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Never let WinForms rescale or move the window: it must cover the monitor 1:1 in physical pixels.
        e.Cancel = true;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _screenBounds;
        Activate();
        Native.SetForegroundWindow(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeResources();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeResources();
        base.Dispose(disposing);
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;
        _screenshot.Dispose();
        _cache.Dispose();
        _labelFont.Dispose();
        _textBox?.Dispose();
        _textBox = null;
        _textFont?.Dispose();
        _textFont = null;
        // The toolbar (a child control) disposes its own fonts when the form disposes its controls.
    }

    // ── Painting ──────────────────────────────────────────────

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Everything is painted in OnPaint from the cache; skipping this avoids flicker.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_resourcesDisposed) return;
        var g = e.Graphics;
        var clip = e.ClipRectangle;

        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_cache, clip, clip, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.InterpolationMode = InterpolationMode.Default;

        // In-progress shape (annotation layer, clipped to the region)
        var current = CurrentShape();
        if (current != null)
        {
            if (_region is { } r) g.SetClip(r);
            AnnotationRenderer.Draw(g, current);
            g.ResetClip();
        }

        if (_regionSelect)
        {
            if (_regionDragging)
            {
                DimOutside(g, _selection);
                DrawBorder(g, _selection);
                DrawSizeLabel(g, _selection);
            }
            else
            {
                using var dim = new SolidBrush(Color.FromArgb(DimAlpha, Color.Black));
                g.FillRectangle(dim, _monitorRect);
            }
        }
        else if (_region is { } region)
        {
            DimOutside(g, region);
            DrawBorder(g, region);
        }
    }

    private void DimOutside(Graphics g, Rectangle keep)
    {
        using var outside = new Region(_monitorRect);
        outside.Exclude(keep);
        using var dim = new SolidBrush(Color.FromArgb(DimAlpha, Color.Black));
        g.FillRegion(dim, outside);
    }

    private static void DrawBorder(Graphics g, Rectangle r)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        // 1 px white line just outside the kept area so the saved pixels stay untouched on screen.
        g.DrawRectangle(Pens.White, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
    }

    private void DrawSizeLabel(Graphics g, Rectangle sel)
    {
        var box = LabelRect(sel, out string text);
        using var back = new SolidBrush(Color.FromArgb(220, TextBoxBack));
        g.FillRectangle(back, box);
        TextRenderer.DrawText(g, text, _labelFont, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>Box for the "W × H" label: below the selection, or inside it when there is no room below.</summary>
    private Rectangle LabelRect(Rectangle sel, out string text)
    {
        text = $"{sel.Width} × {sel.Height}";
        var size = TextRenderer.MeasureText(text, _labelFont, Size.Empty, TextFormatFlags.NoPadding);
        int pad = (int)Math.Round(4 * _scale);
        int w = size.Width + 2 * pad, h = size.Height + pad;
        int x = sel.X;
        int y = sel.Bottom + pad;
        if (y + h > _monitorRect.Bottom) y = Math.Max(_monitorRect.Top, sel.Bottom - h - pad);
        x = Math.Max(_monitorRect.Left, Math.Min(x, _monitorRect.Right - w));
        return new Rectangle(x, y, w, h);
    }

    private void RebuildCache()
    {
        using var g = Graphics.FromImage(_cache);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_screenshot, _monitorRect, _monitorRect, GraphicsUnit.Pixel);
        g.CompositingMode = CompositingMode.SourceOver;
        if (_region is { } r) g.SetClip(r);
        AnnotationRenderer.DrawAll(g, _document.Items);
    }

    /// <summary>Draws one newly added item onto the cache (cheaper than a full rebuild).</summary>
    private void DrawOnCache(Annotation item)
    {
        using var g = Graphics.FromImage(_cache);
        if (_region is { } r) g.SetClip(r);
        AnnotationRenderer.Draw(g, item);
    }

    // ── Document ──────────────────────────────────────────────

    private void AddItem(Annotation item)
    {
        _document.Add(item);
        DrawOnCache(item);
        _toolbar.SetUndoRedoState(_document.CanUndo, _document.CanRedo);
        Invalidate();
    }

    private void DocumentChanged()
    {
        RebuildCache();
        _toolbar.SetUndoRedoState(_document.CanUndo, _document.CanRedo);
        Invalidate();
    }

    private void Undo()
    {
        if (_document.Undo()) DocumentChanged();
    }

    private void Redo()
    {
        if (_document.Redo()) DocumentChanged();
    }

    private void ClearAll()
    {
        if (_document.Items.Count == 0) return;
        _document.Clear();
        DocumentChanged();
    }

    // ── Region ────────────────────────────────────────────────

    private void EnterRegionSelect()
    {
        EndShape(commit: false);
        _regionSelect = true;
        _regionDragging = false;
        _toolbar.SetRegionActive(true);
        Invalidate();
    }

    private void FinishRegionSelect(Point end)
    {
        var r = RegionMath.FromDrag(_regionStart, end, _monitorRect);
        _region = RegionMath.IsUsable(r) ? r : null;
        _regionSelect = false;
        _regionDragging = false;
        _lastDirty = Rectangle.Empty;
        _toolbar.SetRegionActive(_region != null);
        RebuildCache(); // clip changed
        Invalidate();
    }

    private Rectangle SelectionDirty(Rectangle sel)
    {
        var dirty = Rectangle.Inflate(sel, 2, 2);
        dirty = Rectangle.Union(dirty, Rectangle.Inflate(LabelRect(sel, out _), 1, 1));
        return dirty;
    }

    // ── Mouse ─────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _finished) return;

        // Clicking elsewhere commits pending text.
        if (_textBox != null)
        {
            CommitText();
            return;
        }

        if (_drawing) EndShape(commit: false); // lost a mouse-up somewhere

        if (_regionSelect)
        {
            _regionDragging = true;
            _regionStart = e.Location;
            _selection = RegionMath.FromDrag(e.Location, e.Location, _monitorRect);
            _lastDirty = SelectionDirty(_selection);
            Invalidate(); // full-screen dim changes to "outside the selection"
            return;
        }

        if (_region is { } region && !region.Contains(e.Location)) return;

        var tool = _toolbar.Tool;
        if (tool == AnnotationTool.Text)
        {
            StartText(e.Location);
            return;
        }

        _drawing = true;
        _drawTool = tool;
        _drawColor = _toolbar.CurrentColor;
        _drawWidth = AnnotationToolbar.BaseWidths[_toolbar.WidthIndex] * _scale;
        _start = _current = e.Location;
        _points.Clear();
        _points.Add(e.Location);
        _lastDirty = Inflate(new Rectangle(e.Location, Size.Empty));
        Invalidate(_lastDirty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_regionSelect && _regionDragging)
        {
            _selection = RegionMath.FromDrag(_regionStart, e.Location, _monitorRect);
            var dirty = SelectionDirty(_selection);
            Invalidate(Rectangle.Union(_lastDirty, dirty));
            _lastDirty = dirty;
            return;
        }

        if (!_drawing || e.Location == _current) return;

        var prev = _current;
        _current = e.Location;

        if (_drawTool is AnnotationTool.Pen or AnnotationTool.Highlighter)
        {
            _points.Add(e.Location);
            // Only the newest segment changes on screen.
            var seg = Inflate(RegionMath.Normalize(prev, _current));
            _lastDirty = Rectangle.Union(_lastDirty, seg);
            Invalidate(seg);
        }
        else
        {
            var dirty = Inflate(RegionMath.Normalize(_start, _current));
            Invalidate(Rectangle.Union(_lastDirty, dirty));
            _lastDirty = dirty;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;

        if (_regionSelect && _regionDragging)
        {
            FinishRegionSelect(e.Location);
            return;
        }

        if (!_drawing) return;
        if (e.Location != _current)
        {
            _current = e.Location;
            if (_drawTool is AnnotationTool.Pen or AnnotationTool.Highlighter) _points.Add(e.Location);
        }
        EndShape(commit: true);
    }

    private Rectangle Inflate(Rectangle r)
    {
        int pad = (int)Math.Ceiling(_drawWidth * 4 + 2);
        return Rectangle.Inflate(r, pad, pad);
    }

    /// <summary>The shape being drawn right now, or null.</summary>
    private Annotation? CurrentShape()
    {
        if (!_drawing) return null;
        return BuildShape(preview: true);
    }

    private Annotation? BuildShape(bool preview)
    {
        switch (_drawTool)
        {
            case AnnotationTool.Pen:
            case AnnotationTool.Highlighter:
                bool hl = _drawTool == AnnotationTool.Highlighter;
                if (!preview && hl && _points.Count < 2) return null;
                IReadOnlyList<Point> pts = preview ? _points : _points.ToArray();
                return new StrokeAnnotation(_drawColor, _drawWidth, pts, hl);
            case AnnotationTool.Arrow:
                double dx = _current.X - _start.X, dy = _current.Y - _start.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < 3) return null;
                return new ArrowAnnotation(_drawColor, _drawWidth, _start, _current);
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
                var b = RegionMath.Normalize(_start, _current);
                if (b.Width < 1 || b.Height < 1) return null;
                return _drawTool == AnnotationTool.Rectangle
                    ? new RectangleAnnotation(_drawColor, _drawWidth, b)
                    : new EllipseAnnotation(_drawColor, _drawWidth, b);
            default:
                return null;
        }
    }

    private void EndShape(bool commit)
    {
        if (!_drawing) return;
        var item = commit ? BuildShape(preview: false) : null;
        _drawing = false;
        _points.Clear();
        var dirty = _lastDirty;
        _lastDirty = Rectangle.Empty;

        if (item != null) AddItem(item);
        else Invalidate(dirty);
    }

    // ── Text tool ─────────────────────────────────────────────

    private void StartText(Point location)
    {
        _textColor = _toolbar.CurrentColor;
        _textFontPx = AnnotationToolbar.BaseFontPx[_toolbar.WidthIndex] * _scale;
        _textWidth = AnnotationToolbar.BaseWidths[_toolbar.WidthIndex] * _scale;
        _textFont?.Dispose();
        _textFont = new Font("Segoe UI", _textFontPx, FontStyle.Bold, GraphicsUnit.Pixel);

        _textBox = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.None,
            BackColor = TextBoxBack,
            ForeColor = _textColor,
            Font = _textFont,
            WordWrap = false,
            ScrollBars = ScrollBars.None,
            Location = location,
            Cursor = Cursors.IBeam,
        };
        _textBox.TextChanged += (_, _) => SizeTextBox();
        Controls.Add(_textBox);
        _textBox.BringToFront();
        _toolbar.BringToFront();
        SizeTextBox();
        _textBox.Focus();
    }

    private void SizeTextBox()
    {
        if (_textBox == null || _textFont == null) return;
        // Trailing space so a trailing new line still counts as a line and the caret has room.
        var size = TextRenderer.MeasureText(_textBox.Text + " ", _textFont, Size.Empty, TextFormatFlags.TextBoxControl);
        int minW = (int)Math.Round(40 * _scale);
        _textBox.Size = new Size(Math.Max(minW, size.Width + (int)Math.Round(8 * _scale)), size.Height + 2);
    }

    /// <summary>Turns the active text box into a TextAnnotation (if not empty) and removes it.</summary>
    private void CommitText() => EndText(commit: true);

    private void EndText(bool commit)
    {
        var tb = _textBox;
        if (tb == null) return;
        _textBox = null;

        string text = tb.Text.TrimEnd();
        var bounds = tb.Bounds;
        Controls.Remove(tb);
        tb.Dispose();
        ActiveControl = null;
        Focus();

        if (commit && !string.IsNullOrWhiteSpace(text))
            AddItem(new TextAnnotation(_textColor, _textWidth, bounds.Location, text, _textFontPx));
        else
            Invalidate(bounds);
    }

    // ── Keys ──────────────────────────────────────────────────

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_textBox != null)
        {
            switch (keyData)
            {
                case Keys.Enter:
                    CommitText();
                    return true;
                case Keys.Escape:
                    EndText(commit: false);
                    return true;
                default:
                    return base.ProcessCmdKey(ref msg, keyData); // Shift+Enter = new line in the TextBox
            }
        }

        switch (keyData)
        {
            case Keys.Enter:
                Save();
                return true;
            case Keys.Escape:
                CancelSession();
                return true;
            case Keys.Control | Keys.Z:
                if (!_drawing) Undo();
                return true;
            case Keys.Control | Keys.Y:
                if (!_drawing) Redo();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── Finish ────────────────────────────────────────────────

    private void Save()
    {
        if (_finished) return;
        CommitText();
        EndShape(commit: true);
        _finished = true;

        var bmp = AnnotationRenderer.RenderFinal(_screenshot, _document.Items, _region ?? _monitorRect);
        Hide();
        if (Completed != null) Completed.Invoke(this, bmp);
        else bmp.Dispose();
        Close();
    }

    private void CancelSession()
    {
        if (_finished) return;
        _finished = true;
        Hide();
        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }
}

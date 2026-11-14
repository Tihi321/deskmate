namespace DeskMate.Helpers.Annotate;

internal enum AnnotationTool { Pen, Highlighter, Arrow, Rectangle, Ellipse, Text }

// All coordinates are monitor-local physical pixels (0,0 = top-left of the captured monitor).
internal abstract record Annotation(Color Color, float Width);
internal sealed record StrokeAnnotation(Color Color, float Width, IReadOnlyList<Point> Points, bool IsHighlighter) : Annotation(Color, Width);
internal sealed record ArrowAnnotation(Color Color, float Width, Point Start, Point End) : Annotation(Color, Width);
internal sealed record RectangleAnnotation(Color Color, float Width, Rectangle Bounds) : Annotation(Color, Width);
internal sealed record EllipseAnnotation(Color Color, float Width, Rectangle Bounds) : Annotation(Color, Width);
internal sealed record TextAnnotation(Color Color, float Width, Point Location, string Text, float FontPx) : Annotation(Color, Width);

/// <summary>Undo/redo list of annotations, implemented with snapshots of the item list.</summary>
internal sealed class AnnotationDocument
{
    private IReadOnlyList<Annotation> _items = Array.Empty<Annotation>();
    private readonly List<IReadOnlyList<Annotation>> _undo = new();
    private readonly List<IReadOnlyList<Annotation>> _redo = new();

    public IReadOnlyList<Annotation> Items => _items;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Add(Annotation item)
    {
        _undo.Add(_items);
        _items = new List<Annotation>(_items) { item };
        _redo.Clear();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _undo.Add(_items);
        _items = Array.Empty<Annotation>();
        _redo.Clear();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(_items);
        _items = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(_items);
        _items = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }
}

internal static class RegionMath
{
    public const int MinSize = 8;

    /// <summary>Normalised rectangle between two drag points (any direction), intersected with bounds.</summary>
    public static Rectangle FromDrag(Point a, Point b, Rectangle bounds) =>
        Rectangle.Intersect(Normalize(a, b), bounds);

    public static bool IsUsable(Rectangle r) => r.Width >= MinSize && r.Height >= MinSize;

    /// <summary>Normalised rectangle for shape tools from two points; no clamping.</summary>
    public static Rectangle Normalize(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}

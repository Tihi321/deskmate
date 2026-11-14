using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DeskMate.Helpers.Annotate;

internal static class AnnotationRenderer
{
    public const int HighlighterAlpha = 90;

    public static void DrawAll(Graphics g, IEnumerable<Annotation> items)
    {
        foreach (var item in items) Draw(g, item);
    }

    public static void Draw(Graphics g, Annotation item)
    {
        var oldSmoothing = g.SmoothingMode;
        var oldText = g.TextRenderingHint;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            switch (item)
            {
                case StrokeAnnotation s: DrawStroke(g, s); break;
                case ArrowAnnotation a: DrawArrow(g, a); break;
                case RectangleAnnotation r:
                    using (var pen = MakePen(r.Color, r.Width, LineCap.Round))
                        g.DrawRectangle(pen, r.Bounds);
                    break;
                case EllipseAnnotation e:
                    using (var pen = MakePen(e.Color, e.Width, LineCap.Round))
                        g.DrawEllipse(pen, e.Bounds);
                    break;
                case TextAnnotation t: DrawText(g, t); break;
            }
        }
        finally
        {
            g.SmoothingMode = oldSmoothing;
            g.TextRenderingHint = oldText;
        }
    }

    /// <summary>Three points: tip (End), then the two head corners. Head length = max(12, width*4), half-angle 25°.</summary>
    public static PointF[] ArrowHead(Point start, Point end, float width)
    {
        float len = Math.Max(12f, width * 4f);
        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        double half = 25.0 * Math.PI / 180.0;
        var left = new PointF(
            (float)(end.X - len * Math.Cos(angle - half)),
            (float)(end.Y - len * Math.Sin(angle - half)));
        var right = new PointF(
            (float)(end.X - len * Math.Cos(angle + half)),
            (float)(end.Y - len * Math.Sin(angle + half)));
        return new[] { new PointF(end.X, end.Y), left, right };
    }

    /// <summary>New bitmap of crop.Size: background cropped + all items translated by -crop.Location. Caller owns result.</summary>
    public static Bitmap RenderFinal(Bitmap background, IReadOnlyList<Annotation> items, Rectangle crop)
    {
        var bmp = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.DrawImage(background, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
        g.TranslateTransform(-crop.X, -crop.Y);
        DrawAll(g, items);
        return bmp;
    }

    private static Pen MakePen(Color color, float width, LineCap cap) => new(color, width)
    {
        LineJoin = LineJoin.Round,
        StartCap = cap,
        EndCap = cap,
    };

    private static void DrawStroke(Graphics g, StrokeAnnotation s)
    {
        if (s.Points.Count == 0) return;
        if (s.IsHighlighter)
        {
            if (s.Points.Count < 2) return;
            using var pen = MakePen(Color.FromArgb(HighlighterAlpha, s.Color), s.Width * 3f, LineCap.Flat);
            g.DrawLines(pen, s.Points.ToArray());
            return;
        }
        if (s.Points.Count == 1)
        {
            using var brush = new SolidBrush(s.Color);
            float r = s.Width / 2f;
            g.FillEllipse(brush, s.Points[0].X - r, s.Points[0].Y - r, s.Width, s.Width);
            return;
        }
        using var p = MakePen(s.Color, s.Width, LineCap.Round);
        g.DrawLines(p, s.Points.ToArray());
    }

    private static void DrawArrow(Graphics g, ArrowAnnotation a)
    {
        var head = ArrowHead(a.Start, a.End, a.Width);
        // Shaft ends at the middle of the head base so the round cap does not poke past the tip.
        var baseMid = new PointF((head[1].X + head[2].X) / 2f, (head[1].Y + head[2].Y) / 2f);
        using (var pen = MakePen(a.Color, a.Width, LineCap.Round))
            g.DrawLine(pen, a.Start.X, a.Start.Y, baseMid.X, baseMid.Y);
        using var brush = new SolidBrush(a.Color);
        g.FillPolygon(brush, head);
    }

    private static void DrawText(Graphics g, TextAnnotation t)
    {
        if (string.IsNullOrEmpty(t.Text)) return;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var font = new Font("Segoe UI", t.FontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(t.Color);
        g.DrawString(t.Text, font, brush, t.Location);
    }
}

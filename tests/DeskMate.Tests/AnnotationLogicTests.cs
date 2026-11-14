using System.Drawing;
using DeskMate.Helpers.Annotate;
using Xunit;

namespace DeskMate.Tests;

public class AnnotationLogicTests
{
    private static RectangleAnnotation Rect(int x) =>
        new(Color.Red, 3f, new Rectangle(x, 0, 10, 10));

    [Fact]
    public void Document_UndoRedo_and_NewAddClearsRedo()
    {
        var doc = new AnnotationDocument();
        var a = Rect(1); var b = Rect(2); var c = Rect(3);

        doc.Add(a); doc.Add(b);
        Assert.True(doc.Undo());
        Assert.Equal(new Annotation[] { a }, doc.Items);
        Assert.True(doc.Redo());
        Assert.Equal(new Annotation[] { a, b }, doc.Items);
        Assert.True(doc.Undo());
        doc.Add(c);

        Assert.Equal(new Annotation[] { a, c }, doc.Items);
        Assert.False(doc.CanRedo);
    }

    [Fact]
    public void Document_Clear_IsUndoable()
    {
        var doc = new AnnotationDocument();
        var a = Rect(1); var b = Rect(2);
        doc.Add(a); doc.Add(b);

        doc.Clear();
        Assert.Empty(doc.Items);
        Assert.True(doc.Undo());

        Assert.Equal(new Annotation[] { a, b }, doc.Items);
    }

    [Fact]
    public void RegionMath_FromDrag_NormalisesAndClamps()
    {
        var r = RegionMath.FromDrag(new Point(500, 400), new Point(-20, 50), new Rectangle(0, 0, 300, 300));

        Assert.Equal(new Rectangle(0, 50, 300, 250), r);
        Assert.False(RegionMath.IsUsable(new Rectangle(0, 0, 5, 40)));
    }
}

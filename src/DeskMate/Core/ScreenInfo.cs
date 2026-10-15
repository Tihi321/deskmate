namespace DeskMate.Core;

internal static class ScreenInfo
{
    /// <summary>Cursor position (physical pixels) and the color of the pixel under it.</summary>
    public static (Point Position, Color Color) UnderCursor()
    {
        Native.GetCursorPos(out var p);
        IntPtr dc = Native.GetDC(IntPtr.Zero);
        try
        {
            uint c = Native.GetPixel(dc, p.X, p.Y); // 0x00BBGGRR
            var color = Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            return (new Point(p.X, p.Y), color);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public static string FormatColor(Color c, string format) =>
        format == "RGB" ? $"rgb({c.R}, {c.G}, {c.B})" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

internal static class Clip
{
    /// <summary>Clipboard can be briefly locked by other apps — retry a few times.</summary>
    public static bool Set(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(40); }
        }
        return false;
    }
}

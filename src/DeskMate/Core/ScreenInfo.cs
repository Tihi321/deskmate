using System.Drawing.Imaging;

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

    /// <summary>Copies a screen area (physical pixels, PerMonitorV2) into a new bitmap. Caller owns it.</summary>
    public static Bitmap CaptureScreen(Rectangle physicalBounds)
    {
        var bmp = new Bitmap(physicalBounds.Width, physicalBounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(physicalBounds.Location, Point.Empty, physicalBounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>DPI scale (1.0 = 96 dpi) of the monitor containing the point; 1 if unavailable.</summary>
    public static float GetScale(Point physicalPoint)
    {
        try
        {
            IntPtr mon = Native.MonitorFromPoint(
                new Native.POINT { X = physicalPoint.X, Y = physicalPoint.Y }, Native.MONITOR_DEFAULTTONEAREST);
            if (mon != IntPtr.Zero && Native.GetDpiForMonitor(mon, 0, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96f;
        }
        catch { /* shcore missing -> 1 */ }
        return 1f;
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

    /// <summary>Puts an image on the clipboard (copied, so the caller may dispose it afterwards).</summary>
    public static bool SetImage(Image image)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetImage(image); return true; }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(40); }
        }
        return false;
    }
}

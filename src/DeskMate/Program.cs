namespace DeskMate;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one DeskMate per user session.
        using var mutex = new Mutex(true, @"Local\DeskMate.SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}

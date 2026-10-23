using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using DeskMate.Core;
using DeskMate.Helpers;

namespace DeskMate;

/// <summary>The tray icon + menu. Owns all helpers.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private static readonly Color IdleColor = Color.FromArgb(70, 80, 100);
    private static readonly Color AwakeColor = Color.FromArgb(22, 163, 74);
    private static readonly Color RecordingColor = Color.FromArgb(220, 38, 38);

    private readonly Settings _settings;
    private readonly HotkeyManager _hotkeys = new();
    private readonly NotifyIcon _tray;
    private readonly Icon _idleIcon = MakeIcon(IdleColor);
    private readonly Icon _awakeIcon = MakeIcon(AwakeColor);
    private readonly Icon _recordingIcon = MakeIcon(RecordingColor);

    private readonly KeepAwakeHelper _keepAwake = new();
    private readonly AudioRecorderHelper _audioRecorder = new();
    private readonly List<IHelper> _helpers;

    public TrayApp()
    {
        _settings = Settings.Load();
        SyncAutostart();

        _tray = new NotifyIcon { Icon = _idleIcon, Text = "DeskMate", Visible = true };

        // ── Register helpers here ──────────────────────────────
        _helpers = new List<IHelper>
        {
            _keepAwake,
            _audioRecorder,
            new ColorPickerHelper(),
            new CursorCoordsHelper(),
        };
        // ───────────────────────────────────────────────────────

        var context = new HelperContext(_settings, _hotkeys, Notify, RefreshTray);
        foreach (var helper in _helpers) helper.Initialize(context);

        _tray.ContextMenuStrip = BuildMenu();
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) _keepAwake.Toggle(); };

        RefreshTray();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        menu.Items.Add(new ToolStripMenuItem($"DeskMate {version?.ToString(3)}")
        {
            Enabled = false,
            Font = new Font(menu.Font, FontStyle.Bold),
        });
        menu.Items.Add(new ToolStripSeparator());

        foreach (var helper in _helpers)
            foreach (var item in helper.CreateMenuItems())
                menu.Items.Add(item);

        menu.Items.Add(new ToolStripSeparator());

        var autostart = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled };
        autostart.Click += (_, _) =>
        {
            _settings.StartWithWindows = !Autostart.IsEnabled;
            _settings.Save();
            Autostart.Set(_settings.StartWithWindows);
            autostart.Checked = Autostart.IsEnabled;
        };
        menu.Items.Add(autostart);

        var editSettings = new ToolStripMenuItem("Edit settings (hotkeys)…");
        editSettings.Click += (_, _) =>
        {
            _settings.Save();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Settings.FilePath}\""));
            Notify("DeskMate", "Restart DeskMate after saving for changes to apply.");
        };
        menu.Items.Add(editSettings);

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);

        return menu;
    }

    private void SyncAutostart()
    {
#if !DEBUG
        // Keeps the Run entry pointing at wherever this exe currently lives.
        try { Autostart.Set(_settings.StartWithWindows); } catch { /* ignore */ }
#endif
    }

    private void Notify(string title, string text) =>
        _tray.ShowBalloonTip(1500, title, text, ToolTipIcon.None);

    private void RefreshTray()
    {
        if (_audioRecorder.IsRecording)
        {
            _tray.Icon = _recordingIcon;
            var elapsed = _audioRecorder.Elapsed;
            string timeStr = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
            string tip = $"DeskMate · RECORDING ({timeStr}) · Keep awake: {_keepAwake.Status}";
            _tray.Text = tip.Length > 127 ? tip[..127] : tip;
        }
        else
        {
            _tray.Icon = _keepAwake.IsActive ? _awakeIcon : _idleIcon;
            string tip = $"DeskMate · Keep awake: {_keepAwake.Status}";
            _tray.Text = tip.Length > 127 ? tip[..127] : tip;
        }
    }

    private static Icon MakeIcon(Color background)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            using var path = new GraphicsPath();
            const int r = 8, s = 30;
            path.AddArc(1, 1, r, r, 180, 90);
            path.AddArc(1 + s - r, 1, r, r, 270, 90);
            path.AddArc(1 + s - r, 1 + s - r, r, r, 0, 90);
            path.AddArc(1, 1 + s - r, r, r, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(background);
            g.FillPath(brush, path);

            using var font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("D", font, Brushes.White, new RectangleF(0, 1, 32, 32), format);
        }

        IntPtr handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        Native.DestroyIcon(handle);
        return icon;
    }

    protected override void ExitThreadCore()
    {
        foreach (var helper in _helpers) helper.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _idleIcon.Dispose();
        _awakeIcon.Dispose();
        _recordingIcon.Dispose();
        base.ExitThreadCore();
    }
}

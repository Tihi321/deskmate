using System.Diagnostics;
using System.Drawing.Imaging;
using DeskMate.Core;
using DeskMate.Helpers.Annotate;

namespace DeskMate.Helpers;

/// <summary>
/// Hotkey freezes the monitor under the cursor so you can draw on it, then saves a PNG and copies it
/// to the clipboard. A second hotkey starts in region-select mode. Can be disabled from the menu.
/// </summary>
internal sealed class AnnotateHelper : IHelper
{
    private const int MenuStartDelayMs = 200;

    private readonly Action _startFull;
    private readonly Action _startRegion;
    private readonly System.Windows.Forms.Timer _delayTimer = new() { Interval = MenuStartDelayMs };
    private bool _delayedRegion;

    private HelperContext _ctx = null!;
    private AnnotationOverlay? _session;

    private ToolStripMenuItem _root = null!;
    private ToolStripMenuItem _enabledItem = null!;
    private ToolStripMenuItem _fullItem = null!;
    private ToolStripMenuItem _regionItem = null!;

    public AnnotateHelper()
    {
        // Stored once so Hotkeys.Unregister matches the same delegate instances.
        _startFull = () => Start(region: false);
        _startRegion = () => Start(region: true);
        _delayTimer.Tick += (_, _) =>
        {
            _delayTimer.Stop();
            Start(_delayedRegion);
        };
    }

    public void Initialize(HelperContext context)
    {
        _ctx = context;
        if (context.Settings.AnnotateEnabled) RegisterHotkeys();
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        _root = new ToolStripMenuItem("Annotate screen");

        _enabledItem = new ToolStripMenuItem("Enabled");
        _enabledItem.Click += (_, _) => SetEnabled(!_ctx.Settings.AnnotateEnabled);
        _root.DropDownItems.Add(_enabledItem);
        _root.DropDownItems.Add(new ToolStripSeparator());

        _fullItem = new ToolStripMenuItem("Annotate full screen");
        _fullItem.Click += (_, _) => StartDelayed(region: false);
        _root.DropDownItems.Add(_fullItem);

        _regionItem = new ToolStripMenuItem("Annotate region");
        _regionItem.Click += (_, _) => StartDelayed(region: true);
        _root.DropDownItems.Add(_regionItem);
        _root.DropDownItems.Add(new ToolStripSeparator());

        var openItem = new ToolStripMenuItem("Open annotations folder");
        openItem.Click += (_, _) => OpenFolder();
        _root.DropDownItems.Add(openItem);

        var chooseItem = new ToolStripMenuItem("Choose annotations folder…");
        chooseItem.Click += (_, _) => ChooseFolder();
        _root.DropDownItems.Add(chooseItem);

        UpdateMenu();
        yield return _root;
    }

    private void RegisterHotkeys()
    {
        _ctx.RegisterHotkey(_ctx.Settings.AnnotateHotkey, _startFull);
        _ctx.RegisterHotkey(_ctx.Settings.AnnotateRegionHotkey, _startRegion);
    }

    private void SetEnabled(bool enabled)
    {
        _ctx.Settings.AnnotateEnabled = enabled;
        _ctx.Settings.Save();

        if (enabled) RegisterHotkeys();
        else
        {
            _ctx.Hotkeys.Unregister(_startFull);
            _ctx.Hotkeys.Unregister(_startRegion);
        }

        UpdateMenu();
    }

    private void UpdateMenu()
    {
        bool enabled = _ctx.Settings.AnnotateEnabled;
        _root.Text = enabled ? "Annotate screen" : "Annotate screen (off)";
        _enabledItem.Checked = enabled;
        _fullItem.Enabled = enabled;
        _regionItem.Enabled = enabled;
        _fullItem.ShortcutKeyDisplayString = enabled ? _ctx.Settings.AnnotateHotkey : null;
        _regionItem.ShortcutKeyDisplayString = enabled ? _ctx.Settings.AnnotateRegionHotkey : null;
    }

    /// <summary>From the tray menu: wait until the context menu has closed so it is not in the screenshot.</summary>
    private void StartDelayed(bool region)
    {
        _delayedRegion = region;
        _delayTimer.Stop();
        _delayTimer.Start();
    }

    private void Start(bool region)
    {
        if (_session != null)
        {
            _session.ActivateSession();
            return;
        }

        Bitmap? screenshot = null;
        AnnotationOverlay? overlay = null;
        try
        {
            Native.GetCursorPos(out var pt);
            var p = new Point(pt.X, pt.Y);
            var bounds = Screen.FromPoint(p).Bounds;
            screenshot = ScreenInfo.CaptureScreen(bounds);
            float scale = ScreenInfo.GetScale(p);

            overlay = new AnnotationOverlay(bounds, screenshot, scale, region);
            screenshot = null; // owned by the overlay now
            overlay.Completed += OnCompleted;
            overlay.FormClosed += (_, _) => { if (ReferenceEquals(_session, overlay)) _session = null; };
            _session = overlay;
            overlay.Show();
        }
        catch (Exception ex)
        {
            screenshot?.Dispose();
            if (overlay != null)
            {
                if (ReferenceEquals(_session, overlay)) _session = null;
                try { overlay.Dispose(); } catch { /* ignore */ }
            }
            _ctx.Notify("DeskMate", $"Could not start annotation: {ex.Message}");
        }
    }

    private void OnCompleted(object? sender, Bitmap image)
    {
        try
        {
            string? folder = null, name = null, saveError = null;
            try
            {
                folder = _ctx.Settings.GetResolvedAnnotateFolder();
                Directory.CreateDirectory(folder);
                name = UniqueFileName(folder);
                image.Save(Path.Combine(folder, name), ImageFormat.Png);
            }
            catch (Exception ex)
            {
                saveError = ex.Message;
            }

            bool copied;
            try { copied = Clip.SetImage(image); }
            catch { copied = false; }

            if (saveError == null)
            {
                string clip = copied ? "Copied to clipboard." : "(clipboard busy, not copied)";
                _ctx.Notify("Annotation saved", $"{name}\n{clip} Saved in {folder}");
            }
            else
            {
                _ctx.Notify("DeskMate", copied
                    ? $"Could not save PNG: {saveError}. Image copied to clipboard."
                    : $"Could not save PNG: {saveError}. (clipboard busy, not copied)");
            }
        }
        finally
        {
            image.Dispose();
        }
    }

    /// <summary>Annotation_yyyy-MM-dd_HHmmss.png, or with _2, _3, … if that file already exists.</summary>
    private static string UniqueFileName(string folder)
    {
        string stem = $"Annotation_{DateTime.Now:yyyy-MM-dd_HHmmss}";
        string name = stem + ".png";
        for (int i = 2; File.Exists(Path.Combine(folder, name)); i++)
            name = $"{stem}_{i}.png";
        return name;
    }

    private void OpenFolder()
    {
        try
        {
            string folder = _ctx.Settings.GetResolvedAnnotateFolder();
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\""));
        }
        catch (Exception ex)
        {
            _ctx.Notify("DeskMate", $"Could not open folder: {ex.Message}");
        }
    }

    private void ChooseFolder()
    {
        string current;
        try { current = _ctx.Settings.GetResolvedAnnotateFolder(); }
        catch { current = ""; }

        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder to save annotations",
            SelectedPath = current,
            UseDescriptionForTitle = true,
        };

        if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _ctx.Settings.AnnotateFolder = dlg.SelectedPath;
            _ctx.Settings.Save();
            _ctx.Notify("DeskMate", $"Annotations folder set to: {dlg.SelectedPath}");
        }
    }

    public void Dispose()
    {
        _delayTimer.Stop();
        _delayTimer.Dispose();
        var session = _session;
        _session = null;
        if (session != null && !session.IsDisposed)
        {
            try { session.Close(); } catch { /* ignore */ }
        }
    }
}

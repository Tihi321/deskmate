using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Text;
using DeskMate.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DeskMate.Helpers;

/// <summary>
/// Hotkey toggles recording system audio (speakers / loopback) to a WAV file.
/// Default folder is Documents\Sounds, configurable via settings.
/// </summary>
internal sealed class AudioRecorderHelper : IHelper
{
    private readonly object _lock = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private HelperContext _ctx = null!;
    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _silenceOut;
    private FileStream? _fileStream;
    private WaveFileWriter? _writer;
    private string? _currentFilePath;
    private RecordingOverlay? _overlay;

    private ToolStripMenuItem _root = null!;
    private ToolStripMenuItem _toggleItem = null!;
    private ToolStripMenuItem _folderItem = null!;
    private ToolStripMenuItem _chooseFolderItem = null!;

    public bool IsRecording { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public TimeSpan Elapsed => StartedAt.HasValue ? DateTime.Now - StartedAt.Value : TimeSpan.Zero;

    public void Initialize(HelperContext context)
    {
        _ctx = context;
        _timer.Tick += (_, _) =>
        {
            if (IsRecording)
            {
                UpdateWavHeaderDirectly();
                _overlay?.UpdateStatus(Elapsed);
                _ctx.StateChanged();
            }
        };

        if (context.Settings.AudioRecorderEnabled)
        {
            context.RegisterHotkey(context.Settings.AudioRecordHotkey, Toggle);
        }
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        _root = new ToolStripMenuItem("Record system audio");

        _toggleItem = new ToolStripMenuItem("Start recording")
        {
            ShortcutKeyDisplayString = _ctx.Settings.AudioRecordHotkey,
        };
        _toggleItem.Click += (_, _) => Toggle();
        _root.DropDownItems.Add(_toggleItem);

        _root.DropDownItems.Add(new ToolStripSeparator());

        _folderItem = new ToolStripMenuItem("Open recordings folder");
        _folderItem.Click += (_, _) => OpenFolder();
        _root.DropDownItems.Add(_folderItem);

        _chooseFolderItem = new ToolStripMenuItem("Choose recordings folder…");
        _chooseFolderItem.Click += (_, _) => ChooseFolder();
        _root.DropDownItems.Add(_chooseFolderItem);

        yield return _root;
    }

    public void Toggle()
    {
        if (IsRecording) Stop();
        else Start();
    }

    public void Start()
    {
        if (IsRecording) return;

        try
        {
            string folder = _ctx.Settings.GetResolvedAudioFolder();
            Directory.CreateDirectory(folder);

            string fileName = $"Audio_{DateTime.Now:yyyy-MM-dd_HHmmss}.wav";
            string filePath = Path.Combine(folder, fileName);

            var deviceEnum = new MMDeviceEnumerator();
            var device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var capture = new WasapiLoopbackCapture(device);

            // Open with FileShare.ReadWrite so Explorer and other tools can inspect file size in real time
            var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
            var writer = new WaveFileWriter(fileStream, capture.WaveFormat);

            capture.DataAvailable += (_, e) =>
            {
                lock (_lock)
                {
                    if (_writer != null && e.BytesRecorded > 0)
                    {
                        _writer.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                }
            };

            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null)
                {
                    _ctx.Notify("DeskMate", $"Audio recording error: {e.Exception.Message}");
                }
            };

            // Play inaudible silence in shared mode so WASAPI loopback streams continuously
            // even when no other application is playing sound.
            WasapiOut? silenceOut = null;
            try
            {
                silenceOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
                silenceOut.Init(new SilenceProvider(capture.WaveFormat));
                silenceOut.Play();
            }
            catch
            {
                silenceOut?.Dispose();
                silenceOut = null;
            }

            capture.StartRecording();

            lock (_lock)
            {
                _capture = capture;
                _silenceOut = silenceOut;
                _fileStream = fileStream;
                _writer = writer;
                _currentFilePath = filePath;
                StartedAt = DateTime.Now;
                IsRecording = true;
            }

            _timer.Enabled = true;

            // Audible and visual cue
            try { SystemSounds.Asterisk.Play(); } catch { /* ignore */ }

            _overlay ??= new RecordingOverlay(Toggle, OpenFolder);
            _overlay.ShowRecording(fileName);

            UpdateMenu();
            _ctx.StateChanged();
            _ctx.Notify("Recording started", $"Recording system audio…\nPress {_ctx.Settings.AudioRecordHotkey} to stop.");
        }
        catch (Exception ex)
        {
            CleanUpCaptureAsync();
            _ctx.Notify("DeskMate", $"Failed to start recording: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!IsRecording) return;

        // Immediately update state and UI on the UI thread without blocking
        IsRecording = false;
        _timer.Enabled = false;

        TimeSpan duration = Elapsed;
        StartedAt = null;
        string? savedPath = _currentFilePath;

        UpdateMenu();
        _ctx.StateChanged();

        try { SystemSounds.Exclamation.Play(); } catch { /* ignore */ }

        if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
        {
            string name = Path.GetFileName(savedPath);
            string timeStr = duration.TotalSeconds < 60
                ? $"{duration.Seconds}s"
                : $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";

            _overlay?.ShowSaved(name, timeStr);
            _ctx.Notify("Audio saved", $"{name} ({timeStr})\nSaved in {_ctx.Settings.GetResolvedAudioFolder()}");
        }

        // Asynchronously stop audio devices and flush writer to avoid UI thread deadlocks
        CleanUpCaptureAsync();
    }

    private void UpdateWavHeaderDirectly()
    {
        lock (_lock)
        {
            if (_fileStream == null || _writer == null) return;
            try
            {
                _writer.Flush();
                long len = _fileStream.Length;
                if (len >= 58)
                {
                    int dataLen = (int)(len - 58);
                    int samples = dataLen / 8;
                    long savedPos = _fileStream.Position;
                    using var bw = new BinaryWriter(_fileStream, Encoding.ASCII, leaveOpen: true);
                    _fileStream.Position = 4;
                    bw.Write((int)(len - 8));
                    _fileStream.Position = 46;
                    bw.Write(samples);
                    _fileStream.Position = 54;
                    bw.Write(dataLen);
                    bw.Flush();
                    _fileStream.Position = savedPos;
                }
            }
            catch { /* non-fatal periodic flush */ }
        }
    }

    private void CleanUpCaptureAsync()
    {
        WasapiLoopbackCapture? capture;
        WasapiOut? silence;
        WaveFileWriter? writer;
        FileStream? fileStream;

        lock (_lock)
        {
            capture = _capture;
            silence = _silenceOut;
            writer = _writer;
            fileStream = _fileStream;

            _capture = null;
            _silenceOut = null;
            _writer = null;
            _fileStream = null;
        }

        // Offload blocking audio device stops and file disposal to background thread
        Task.Run(() =>
        {
            try { silence?.Stop(); } catch { /* ignore */ }
            try { silence?.Dispose(); } catch { /* ignore */ }

            try { capture?.StopRecording(); } catch { /* ignore */ }
            try { capture?.Dispose(); } catch { /* ignore */ }

            lock (_lock)
            {
                try
                {
                    if (fileStream != null && writer != null)
                    {
                        writer.Flush();
                        long len = fileStream.Length;
                        if (len >= 58)
                        {
                            int dataLen = (int)(len - 58);
                            int samples = dataLen / 8;
                            long savedPos = fileStream.Position;
                            using var bw = new BinaryWriter(fileStream, Encoding.ASCII, leaveOpen: true);
                            fileStream.Position = 4;
                            bw.Write((int)(len - 8));
                            fileStream.Position = 46;
                            bw.Write(samples);
                            fileStream.Position = 54;
                            bw.Write(dataLen);
                            bw.Flush();
                            fileStream.Position = savedPos;
                        }
                        writer.Dispose();
                    }
                }
                catch { /* ignore */ }
                finally
                {
                    try { fileStream?.Dispose(); } catch { /* ignore */ }
                }
            }
        });
    }

    private void UpdateMenu()
    {
        if (_toggleItem == null) return;
        _toggleItem.Text = IsRecording ? "Stop recording" : "Start recording";
        _toggleItem.Checked = IsRecording;
    }

    private void OpenFolder()
    {
        try
        {
            string folder = _ctx.Settings.GetResolvedAudioFolder();
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
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder to save audio recordings",
            SelectedPath = _ctx.Settings.GetResolvedAudioFolder(),
            UseDescriptionForTitle = true,
        };

        if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
        {
            _ctx.Settings.AudioRecordingFolder = dlg.SelectedPath;
            _ctx.Settings.Save();
            _ctx.Notify("DeskMate", $"Recordings folder set to: {dlg.SelectedPath}");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        CleanUpCaptureAsync();
        _overlay?.Dispose();
    }
}

/// <summary>
/// A non-intrusive floating HUD widget shown in the top-right corner while recording,
/// with a Stop button and saved notification.
/// </summary>
internal sealed class RecordingOverlay : Form
{
    private readonly Action _stopAction;
    private readonly Action _openFolderAction;
    private readonly System.Windows.Forms.Timer _dismissTimer = new() { Interval = 3500 };
    private readonly float _scale;

    private bool _isRecording;
    private string _mainText = "Recording";
    private string _timeText = "00:00";
    private bool _isSavedState;
    private Rectangle _buttonRect;
    private bool _buttonHovered;

    public RecordingOverlay(Action stopAction, Action openFolderAction)
    {
        _stopAction = stopAction;
        _openFolderAction = openFolderAction;
        _scale = DeviceDpi / 96f;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 24, 27);
        ForeColor = Color.White;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 12f * _scale, GraphicsUnit.Pixel);
        Size = new Size((int)(260 * _scale), (int)(38 * _scale));

        _dismissTimer.Tick += (_, _) => Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void ShowRecording(string fileName)
    {
        _dismissTimer.Stop();
        _isRecording = true;
        _isSavedState = false;
        _mainText = "Recording system audio";
        _timeText = "00:00";

        Reposition();
        if (!Visible) Show();
        Invalidate();
    }

    public void UpdateStatus(TimeSpan elapsed)
    {
        if (!_isRecording) return;
        _timeText = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        Invalidate();
    }

    public void ShowSaved(string fileName, string duration)
    {
        _isRecording = false;
        _isSavedState = true;
        _mainText = $"Saved ({duration})";
        _timeText = "";

        Invalidate();
        _dismissTimer.Start();
    }

    private void Reposition()
    {
        // Place in top-right corner of working area so it never collides with taskbar or tray overflow
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        int pad = (int)(20 * _scale);
        Location = new Point(area.Right - Width - pad, area.Top + pad);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hovered = _buttonRect.Contains(e.Location);
        if (hovered != _buttonHovered)
        {
            _buttonHovered = hovered;
            Cursor = hovered ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_buttonHovered)
        {
            _buttonHovered = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        if (_isRecording)
        {
            _stopAction();
        }
        else if (_isSavedState)
        {
            _openFolderAction();
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        // Draw pill background
        using var path = new GraphicsPath();
        const int r = 10;
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(Width - r - 1, 0, r, r, 270, 90);
        path.AddArc(Width - r - 1, Height - r - 1, r, r, 0, 90);
        path.AddArc(0, Height - r - 1, r, r, 90, 90);
        path.CloseFigure();

        using (var bgBrush = new SolidBrush(Color.FromArgb(28, 28, 32)))
        {
            e.Graphics.FillPath(bgBrush, path);
        }
        using (var borderPen = new Pen(Color.FromArgb(60, 60, 68), 1f))
        {
            e.Graphics.DrawPath(borderPen, path);
        }

        int padLeft = (int)(14 * _scale);

        if (_isRecording)
        {
            // Red recording dot
            int dotSize = (int)(10 * _scale);
            int dotY = (Height - dotSize) / 2;
            using var dotBrush = new SolidBrush(Color.FromArgb(239, 68, 68));
            e.Graphics.FillEllipse(dotBrush, padLeft, dotY, dotSize, dotSize);

            // Time & label
            string label = $"Rec  {_timeText}";
            using var boldFont = new Font(Font.FontFamily, 12f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            e.Graphics.DrawString(label, boldFont, textBrush, padLeft + dotSize + (int)(8 * _scale), (Height - 16 * _scale) / 2);

            // Stop button
            int btnWidth = (int)(68 * _scale);
            int btnHeight = (int)(24 * _scale);
            _buttonRect = new Rectangle(Width - btnWidth - (int)(10 * _scale), (Height - btnHeight) / 2, btnWidth, btnHeight);

            using var btnPath = new GraphicsPath();
            int br = 6;
            btnPath.AddArc(_buttonRect.X, _buttonRect.Y, br, br, 180, 90);
            btnPath.AddArc(_buttonRect.Right - br, _buttonRect.Y, br, br, 270, 90);
            btnPath.AddArc(_buttonRect.Right - br, _buttonRect.Bottom - br, br, br, 0, 90);
            btnPath.AddArc(_buttonRect.X, _buttonRect.Bottom - br, br, br, 90, 90);
            btnPath.CloseFigure();

            Color btnColor = _buttonHovered ? Color.FromArgb(75, 75, 85) : Color.FromArgb(48, 48, 54);
            using (var btnBrush = new SolidBrush(btnColor)) e.Graphics.FillPath(btnBrush, btnPath);
            using (var btnBorder = new Pen(Color.FromArgb(80, 80, 90), 1f)) e.Graphics.DrawPath(btnBorder, btnPath);

            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("■ Stop", Font, Brushes.White, _buttonRect, format);
        }
        else if (_isSavedState)
        {
            // Green checkmark dot
            int dotSize = (int)(10 * _scale);
            int dotY = (Height - dotSize) / 2;
            using var dotBrush = new SolidBrush(Color.FromArgb(34, 197, 94));
            e.Graphics.FillEllipse(dotBrush, padLeft, dotY, dotSize, dotSize);

            using var boldFont = new Font(Font.FontFamily, 12f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
            e.Graphics.DrawString(_mainText, boldFont, Brushes.White, padLeft + dotSize + (int)(8 * _scale), (Height - 16 * _scale) / 2);

            // Open folder link button
            int btnWidth = (int)(82 * _scale);
            int btnHeight = (int)(24 * _scale);
            _buttonRect = new Rectangle(Width - btnWidth - (int)(10 * _scale), (Height - btnHeight) / 2, btnWidth, btnHeight);

            Color btnColor = _buttonHovered ? Color.FromArgb(75, 75, 85) : Color.FromArgb(48, 48, 54);
            using var btnBrush = new SolidBrush(btnColor);
            e.Graphics.FillRectangle(btnBrush, _buttonRect);

            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("Open folder", Font, Brushes.White, _buttonRect, format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dismissTimer.Dispose();
        base.Dispose(disposing);
    }
}

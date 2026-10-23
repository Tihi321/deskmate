using System.Diagnostics;
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
    private WaveFileWriter? _writer;
    private string? _currentFilePath;

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
            if (IsRecording) _ctx.StateChanged();
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

            lock (_lock)
            {
                var deviceEnum = new MMDeviceEnumerator();
                var device = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                _capture = new WasapiLoopbackCapture(device);
                _writer = new WaveFileWriter(filePath, _capture.WaveFormat);

                _capture.DataAvailable += (_, e) =>
                {
                    lock (_lock)
                    {
                        if (_writer != null && e.BytesRecorded > 0)
                        {
                            _writer.Write(e.Buffer, 0, e.BytesRecorded);
                        }
                    }
                };

                _capture.RecordingStopped += (_, e) =>
                {
                    if (e.Exception != null)
                    {
                        _ctx.Notify("DeskMate", $"Audio recording stopped: {e.Exception.Message}");
                    }
                };

                // Play inaudible silence in shared mode so WASAPI loopback streams continuously
                // even when no other application is playing sound.
                try
                {
                    _silenceOut = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
                    _silenceOut.Init(new SilenceProvider(_capture.WaveFormat));
                    _silenceOut.Play();
                }
                catch
                {
                    _silenceOut?.Dispose();
                    _silenceOut = null;
                }

                _capture.StartRecording();
            }

            _currentFilePath = filePath;
            StartedAt = DateTime.Now;
            IsRecording = true;
            _timer.Enabled = true;

            UpdateMenu();
            _ctx.StateChanged();
            _ctx.Notify("Recording started", $"Recording system audio…\nPress {_ctx.Settings.AudioRecordHotkey} to stop.");
        }
        catch (Exception ex)
        {
            CleanUpCapture();
            _ctx.Notify("DeskMate", $"Failed to start recording: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!IsRecording) return;

        string? savedPath = _currentFilePath;
        TimeSpan duration = Elapsed;

        CleanUpCapture();

        UpdateMenu();
        _ctx.StateChanged();

        if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
        {
            string name = Path.GetFileName(savedPath);
            string timeStr = duration.TotalSeconds < 60
                ? $"{duration.Seconds}s"
                : $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s";
            _ctx.Notify("Audio saved", $"{name} ({timeStr})\nSaved in {_ctx.Settings.GetResolvedAudioFolder()}");
        }
    }

    private void CleanUpCapture()
    {
        lock (_lock)
        {
            IsRecording = false;
            StartedAt = null;
            _timer.Enabled = false;

            try { _silenceOut?.Stop(); } catch { /* ignore */ }
            try { _silenceOut?.Dispose(); } catch { /* ignore */ }
            _silenceOut = null;

            try { _capture?.StopRecording(); } catch { /* ignore */ }
            try { _capture?.Dispose(); } catch { /* ignore */ }
            _capture = null;

            try { _writer?.Flush(); } catch { /* ignore */ }
            try { _writer?.Dispose(); } catch { /* ignore */ }
            _writer = null;
        }
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
        CleanUpCapture();
    }
}

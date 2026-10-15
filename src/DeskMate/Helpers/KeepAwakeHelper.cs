using DeskMate.Core;
using static DeskMate.Core.Native;

namespace DeskMate.Helpers;

/// <summary>
/// Stops Windows from sleeping (and optionally turning off the screen) —
/// indefinitely or for a set time. Always starts Off after a restart.
/// </summary>
internal sealed class KeepAwakeHelper : IHelper
{
    private static readonly (string Label, TimeSpan? Duration)[] Options =
    {
        ("Until I turn it off", null),
        ("15 minutes", TimeSpan.FromMinutes(15)),
        ("30 minutes", TimeSpan.FromMinutes(30)),
        ("1 hour", TimeSpan.FromHours(1)),
        ("2 hours", TimeSpan.FromHours(2)),
        ("4 hours", TimeSpan.FromHours(4)),
        ("8 hours", TimeSpan.FromHours(8)),
    };

    private HelperContext _ctx = null!;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private ToolStripMenuItem _root = null!;
    private ToolStripMenuItem _offItem = null!;
    private ToolStripMenuItem _screenItem = null!;
    private readonly List<(ToolStripMenuItem Item, TimeSpan? Duration)> _optionItems = new();

    public bool IsActive { get; private set; }
    public DateTime? EndsAt { get; private set; }
    private TimeSpan? _activeOption;

    public void Initialize(HelperContext context)
    {
        _ctx = context;
        _timer.Tick += (_, _) => OnTick();
        context.RegisterHotkey(context.Settings.KeepAwakeHotkey, Toggle);
    }

    public IEnumerable<ToolStripItem> CreateMenuItems()
    {
        _root = new ToolStripMenuItem("Keep awake")
        {
            ShortcutKeyDisplayString = _ctx.Settings.KeepAwakeHotkey,
        };

        _offItem = new ToolStripMenuItem("Off");
        _offItem.Click += (_, _) => Stop(notify: true);
        _root.DropDownItems.Add(_offItem);

        foreach (var (label, duration) in Options)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => Start(duration);
            _optionItems.Add((item, duration));
            _root.DropDownItems.Add(item);
        }

        _root.DropDownItems.Add(new ToolStripSeparator());

        _screenItem = new ToolStripMenuItem("Keep screen on too") { Checked = _ctx.Settings.KeepScreenOn };
        _screenItem.Click += (_, _) =>
        {
            _ctx.Settings.KeepScreenOn = !_ctx.Settings.KeepScreenOn;
            _ctx.Settings.Save();
            Apply();
        };
        _root.DropDownItems.Add(_screenItem);

        RefreshMenu();
        yield return _root;
    }

    /// <summary>Hotkey / tray double-click: Off → indefinitely, On → Off.</summary>
    public void Toggle()
    {
        if (IsActive) Stop(notify: true);
        else Start(null);
    }

    public void Start(TimeSpan? duration)
    {
        IsActive = true;
        _activeOption = duration;
        EndsAt = duration is { } d ? DateTime.Now + d : null;
        _timer.Enabled = EndsAt.HasValue;
        Apply();
        _ctx.Notify("Keep awake ON", EndsAt is { } end ? $"Until {end:HH:mm}" : "Until you turn it off");
    }

    public void Stop(bool notify)
    {
        if (!IsActive) return;
        IsActive = false;
        EndsAt = null;
        _activeOption = null;
        _timer.Enabled = false;
        Apply();
        if (notify) _ctx.Notify("Keep awake OFF", "Normal sleep settings restored.");
    }

    /// <summary>Short status for the tray tooltip.</summary>
    public string Status => !IsActive ? "Off"
        : EndsAt is { } end ? $"On, {FormatRemaining(end - DateTime.Now)} left"
        : "On";

    private void OnTick()
    {
        if (EndsAt is { } end && DateTime.Now >= end)
        {
            Stop(notify: false);
            _ctx.Notify("Keep awake ended", "Timer finished. Normal sleep settings restored.");
            return;
        }
        RefreshMenu();
        _ctx.StateChanged();
    }

    private void Apply()
    {
        var state = ExecutionState.Continuous;
        if (IsActive)
        {
            state |= ExecutionState.SystemRequired;
            if (_ctx.Settings.KeepScreenOn) state |= ExecutionState.DisplayRequired;
        }
        SetThreadExecutionState(state);
        RefreshMenu();
        _ctx.StateChanged();
    }

    private void RefreshMenu()
    {
        if (_root is null) return;
        _root.Checked = IsActive;
        _root.Text = IsActive ? $"Keep awake ({Status})" : "Keep awake";
        _offItem.Checked = !IsActive;
        foreach (var (item, duration) in _optionItems)
            item.Checked = IsActive && _activeOption == duration;
        _screenItem.Checked = _ctx.Settings.KeepScreenOn;
    }

    private static string FormatRemaining(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:D2}m"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m"
        : $"{Math.Max(0, t.Seconds)}s";

    public void Dispose()
    {
        _timer.Dispose();
        SetThreadExecutionState(ExecutionState.Continuous); // release
    }
}

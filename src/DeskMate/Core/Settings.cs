using System.Text.Encodings.Web;
using System.Text.Json;

namespace DeskMate.Core;

/// <summary>User settings, stored in %APPDATA%\DeskMate\settings.json.</summary>
internal sealed class Settings
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskMate");
    public static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public bool StartWithWindows { get; set; } = true;

    // Keep awake (the on/off state itself is NOT saved — it always starts Off).
    public bool KeepScreenOn { get; set; } = true;
    public string KeepAwakeHotkey { get; set; } = "Ctrl+Alt+K";

    // Color picker / cursor coordinates
    public bool ColorPickerEnabled { get; set; } = true;
    public string ColorFormat { get; set; } = "HEX"; // HEX | RGB
    public string ColorPickerHotkey { get; set; } = "Ctrl+Alt+C";
    public string CoordsHotkey { get; set; } = "Ctrl+Alt+X";
    public string OverlayHotkey { get; set; } = "Ctrl+Alt+O";

    // System audio recorder
    public bool AudioRecorderEnabled { get; set; } = true;
    public string AudioRecordHotkey { get; set; } = "Ctrl+Alt+R";
    public string AudioRecordingFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sounds");

    public string GetResolvedAudioFolder()
    {
        string path = string.IsNullOrWhiteSpace(AudioRecordingFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sounds")
            : Environment.ExpandEnvironmentVariables(AudioRecordingFolder);
        return Path.GetFullPath(path);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Keep "Ctrl+Alt+K" readable instead of "Ctrl+Alt+K" — this file is hand-edited.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt file -> defaults */ }
        var fresh = new Settings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* non-fatal */ }
    }
}

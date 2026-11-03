using System.Text.Json;

namespace Guardian.Services;

public sealed class AppSettings
{
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Scan running programs' memory with YARA during the deep scan. Off by default (anti-cheat risk).</summary>
    public bool ScanProcessMemory { get; set; }

    /// <summary>Keys of deep-scan findings the user chose to ignore.</summary>
    public List<string> Ignored { get; set; } = new();

    /// <summary>Real-time protection options. Every feature has its own switch.</summary>
    public ProtectionSettings Protection { get; set; } = new();
}

public sealed class ProtectionSettings
{
    /// <summary>Master switch: when off, nothing below runs.</summary>
    public bool RealtimeEnabled { get; set; } = true;
    public bool WatchFiles { get; set; } = true;
    public bool WatchStartup { get; set; } = true;
    public bool ScanUsb { get; set; } = true;
    /// <summary>Check programs as they start, through Windows' kernel event feed (administrator only).</summary>
    public bool WatchPrograms { get; set; } = true;
    /// <summary>Pause a high-risk program the moment it starts, until the user decides. Off by default: a false alarm would freeze a good program.</summary>
    public bool PausePrograms { get; set; }
    /// <summary>Move serious threats into quarantine without asking. Off means Guardian only alerts.</summary>
    public bool AutoQuarantine { get; set; }
    /// <summary>Look up the hash of new unsigned programs on VirusTotal (needs the API key; only the hash is sent).</summary>
    public bool CloudCheck { get; set; }
    public bool Notify { get; set; } = true;
    public bool RunInBackground { get; set; }
    public bool StartWithWindows { get; set; }
    /// <summary>"Off", "Daily" or "Weekly".</summary>
    public string Schedule { get; set; } = "Off";
    public List<string> Exclusions { get; set; } = new();
}

/// <summary>Small per-user settings file in %LOCALAPPDATA%\Guardian.</summary>
public static class SettingsStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch { /* settings are a convenience; never crash over them */ }
    }
}

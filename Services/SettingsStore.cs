using System.Text.Json;

namespace Guardian.Services;

public sealed class AppSettings
{
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>Keys of deep-scan findings the user chose to ignore.</summary>
    public List<string> Ignored { get; set; } = new();
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

using System.Text.Json;

namespace Guardian.Services.Deep;

public sealed class QuarantineEntry
{
    public string Id { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime When { get; set; }
    public string DisplayName => Path.GetFileName(OriginalPath);
    public string Summary => $"{OriginalPath}\n{When:g}  ·  {Reason}";
}

/// <summary>
/// Moves a file into a locked-away folder (renamed so it can't run) and remembers where it came from,
/// so it can always be put back. Nothing is deleted.
/// </summary>
public static class Quarantine
{
    static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "Quarantine");

    static string IndexPath => Path.Combine(Folder, "index.json");

    public static List<QuarantineEntry> List()
    {
        try
        {
            return File.Exists(IndexPath)
                ? JsonSerializer.Deserialize<List<QuarantineEntry>>(File.ReadAllText(IndexPath)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    static void Save(List<QuarantineEntry> entries)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(entries));
    }

    /// <summary>Moves the file into quarantine. Throws if it is in use or protected.</summary>
    public static QuarantineEntry Add(string path, string sha256, string reason)
    {
        if (Heuristics.IsInWindowsDir(path) && Authenticode.Check(path).IsValid)
            throw new InvalidOperationException("This is a signed Windows file. Guardian won't move it.");

        Directory.CreateDirectory(Folder);
        var entry = new QuarantineEntry
        {
            Id = Guid.NewGuid().ToString("N"), OriginalPath = path, Sha256 = sha256, Reason = reason, When = DateTime.Now,
        };
        File.Move(path, Path.Combine(Folder, entry.Id + ".quarantined"));

        var entries = List();
        entries.Add(entry);
        Save(entries);
        return entry;
    }

    public static void Restore(QuarantineEntry entry)
    {
        var source = Path.Combine(Folder, entry.Id + ".quarantined");
        if (!File.Exists(source)) throw new FileNotFoundException("The quarantined copy is missing.");
        Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
        var target = File.Exists(entry.OriginalPath)
            ? Path.Combine(Path.GetDirectoryName(entry.OriginalPath)!, Path.GetFileNameWithoutExtension(entry.OriginalPath) + " (restored)" + Path.GetExtension(entry.OriginalPath))
            : entry.OriginalPath;
        File.Move(source, target);

        var entries = List();
        entries.RemoveAll(e => e.Id == entry.Id);
        Save(entries);
    }

    /// <summary>Permanently removes a quarantined file. Only call after the user explicitly asks.</summary>
    public static void DeleteForever(QuarantineEntry entry)
    {
        var source = Path.Combine(Folder, entry.Id + ".quarantined");
        if (File.Exists(source)) File.Delete(source);
        var entries = List();
        entries.RemoveAll(e => e.Id == entry.Id);
        Save(entries);
    }
}

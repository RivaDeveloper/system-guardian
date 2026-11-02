using System.Text.RegularExpressions;

namespace Guardian.Services;

public sealed class AppUpdate(string name, string id, string version, string available)
{
    public string Name { get; set; } = name;
    public string Id { get; set; } = id;
    public string Version { get; set; } = version;
    public string Available { get; set; } = available;
    public string VersionText => $"{Version}  →  {Available}";
}

/// <summary>Finds and applies application updates through winget.</summary>
public sealed class AppUpdateService
{
    public async Task<List<AppUpdate>> GetUpdatesAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("winget",
            "upgrade --include-unknown --accept-source-agreements --disable-interactivity", ct: ct);
        return Parse(r.Output);
    }

    public async Task<bool> UpgradeAsync(string id, Action<string>? onLine = null, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("winget",
            $"upgrade --id \"{id}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            onLine, ct: ct);
        return r.ExitCode == 0;
    }

    public async Task<bool> UpgradeAllAsync(Action<string>? onLine = null, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("winget",
            "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            onLine, ct: ct);
        return r.ExitCode == 0;
    }

    /// <summary>Parses winget's fixed-width table using the column offsets of the header row.</summary>
    internal static List<AppUpdate> Parse(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        var headerIdx = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^Name\s+Id\s+Version\s+Available"));
        var result = new List<AppUpdate>();
        if (headerIdx < 0) return result;

        var cols = Regex.Matches(lines[headerIdx], @"\S+").Select(m => m.Index).ToArray(); // Name, Id, Version, Available, Source
        if (cols.Length < 4) return result;

        for (var i = headerIdx + 2; i < lines.Length; i++) // skip the dashed separator
        {
            var line = lines[i];
            if (line.Trim().Length == 0 || Regex.IsMatch(line, @"^\d+ (upgrades?|package)")) break;
            if (line.Length <= cols[3]) continue;

            string Cell(int c)
            {
                var start = cols[c];
                var end = c + 1 < cols.Length ? Math.Min(cols[c + 1], line.Length) : line.Length;
                return start < line.Length ? line[start..end].Trim() : "";
            }
            var item = new AppUpdate(Cell(0), Cell(1), Cell(2), Cell(3));
            if (item.Id.Length > 0) result.Add(item);
        }
        return result;
    }
}

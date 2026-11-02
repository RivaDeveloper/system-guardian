using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Guardian.Services.Deep;

public sealed record YaraMatch(string Rule, string Description, string Author, string Reference, int Score, int Quality, string Target);

/// <summary>
/// Runs YARA (VirusTotal's pattern-matching engine) against files and process memory with a community rule set.
/// The engine binaries ship with the app; the rules are downloaded from YARA-Forge and compiled locally.
/// Rules are evidence, not verdicts: callers weigh a match by its score and quality and by the file's signature.
/// </summary>
public static class YaraEngine
{
    const string ForgeApi = "https://api.github.com/repos/YARAHQ/yara-forge/releases/latest";
    const string RulesAsset = "yara-forge-rules-core.zip";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Guardian-Intel");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // ---- locations ----

    static string EngineDir =>
        Environment.GetEnvironmentVariable("GUARDIAN_YARA_DIR") is { Length: > 0 } d ? d : Path.Combine(AppContext.BaseDirectory, "Assets", "yara");

    static string YaraExe => Path.Combine(EngineDir, "yara64.exe");
    static string YaracExe => Path.Combine(EngineDir, "yarac64.exe");
    static string RulesDir => Path.Combine(IntelStore.Dir, "rules");
    static string CoreRules => Path.Combine(RulesDir, "core.yar");
    public static string CompiledPath => Path.Combine(RulesDir, "compiled.yarc");

    /// <summary>Folder for the user's own .yar rule files. They are compiled together with the community rules.</summary>
    public static string CustomDir => Path.Combine(IntelStore.Dir, "custom");

    public static bool EngineAvailable => File.Exists(YaraExe) && File.Exists(YaracExe);
    public static bool RulesReady => File.Exists(CompiledPath);

    // ---- updating rules ----

    public static bool RulesStale(TimeSpan maxAge)
    {
        var meta = IntelMeta.Load();
        return !RulesReady || meta.RulesUpdatedUtc is null || DateTime.UtcNow - meta.RulesUpdatedUtc > maxAge;
    }

    /// <summary>Downloads the newest community rule set if it changed (verified against the checksum GitHub publishes), then compiles it.</summary>
    public static async Task<string> UpdateRulesAsync(IProgress<string>? progress, bool force, CancellationToken ct)
    {
        if (!EngineAvailable) return "The YARA engine isn't installed with this build.";
        Directory.CreateDirectory(RulesDir);
        var meta = IntelMeta.Load();

        progress?.Report("Checking for new YARA rules…");
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(ForgeApi, ct));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        JsonElement? asset = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == RulesAsset) asset = a;
        if (asset is null) return "The rule package wasn't found in the latest YARA-Forge release.";

        var digest = asset.Value.TryGetProperty("digest", out var dg) ? dg.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:")) return "The rule package has no published checksum, so it was not installed.";
        var expected = digest["sha256:".Length..].ToLowerInvariant();

        if (!force && RulesReady && meta.RulesDigest == expected)
        {
            meta.RulesUpdatedUtc = DateTime.UtcNow;
            meta.Save();
            return $"YARA rules are current ({meta.RuleCount:N0} rules, {meta.RulesTag})";
        }

        progress?.Report("Downloading YARA rules…");
        var zipPath = Path.Combine(RulesDir, "rules.zip");
        using (var resp = await Http.GetAsync(asset.Value.GetProperty("browser_download_url").GetString()!, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);
            await src.CopyToAsync(dst, ct);
        }

        string actual;
        await using (var check = File.OpenRead(zipPath)) actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(zipPath);
            return "The downloaded rules failed their checksum check, so they were discarded.";
        }

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".yar", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return "The rule package didn't contain any rules.";
            entry.ExtractToFile(CoreRules + ".new", overwrite: true);
        }
        File.Delete(zipPath);

        progress?.Report("Compiling YARA rules…");
        var error = await CompileAsync(CoreRules + ".new", ct);
        if (error != null) { File.Delete(CoreRules + ".new"); return "The new rules didn't compile: " + error; }

        File.Move(CoreRules + ".new", CoreRules, overwrite: true);
        // the compile above used the .new file; recompile so the compiled set matches the installed file name
        meta.RulesDigest = expected;
        meta.RulesTag = tag;
        meta.RulesUpdatedUtc = DateTime.UtcNow;
        meta.RuleCount = CountRules(CoreRules);
        meta.Save();
        return $"YARA rules updated ({meta.RuleCount:N0} rules, {tag})";
    }

    static int CountRules(string file)
    {
        try { return Regex.Matches(File.ReadAllText(file), @"(?m)^(private |global )*rule ").Count; }
        catch { return 0; }
    }

    /// <summary>Compiles the community rules plus any custom rules into one file. Returns an error message, or null on success.</summary>
    public static async Task<string?> CompileAsync(string coreFile, CancellationToken ct)
    {
        var inputs = new List<string> { coreFile };
        if (Directory.Exists(CustomDir)) inputs.AddRange(Directory.EnumerateFiles(CustomDir, "*.yar"));

        var temp = CompiledPath + ".new";
        var args = string.Join(" ", inputs.Select(i => $"\"{i}\"")) + $" \"{temp}\"";
        var result = await RunAsync(YaracExe, args, TimeSpan.FromMinutes(3), ct);

        // A broken custom rule shouldn't take the community rules down with it.
        if (result.ExitCode != 0 && inputs.Count > 1)
        {
            var retry = await RunAsync(YaracExe, $"\"{coreFile}\" \"{temp}\"", TimeSpan.FromMinutes(3), ct);
            if (retry.ExitCode == 0) { File.Move(temp, CompiledPath, overwrite: true); return "A custom rule didn't compile and was ignored: " + FirstLine(result.Error); }
        }
        if (result.ExitCode != 0) return FirstLine(result.Error + result.Output);
        File.Move(temp, CompiledPath, overwrite: true);
        return null;
    }

    /// <summary>True when a custom rule file is newer than the compiled rule set, so the next scan should recompile.</summary>
    public static bool CustomRulesChanged()
    {
        try
        {
            if (!RulesReady || !Directory.Exists(CustomDir)) return false;
            var compiled = File.GetLastWriteTimeUtc(CompiledPath);
            return Directory.EnumerateFiles(CustomDir, "*.yar").Any(f => File.GetLastWriteTimeUtc(f) > compiled);
        }
        catch { return false; }
    }

    /// <summary>Recompiles after the user adds or changes custom rules.</summary>
    public static async Task<string?> RecompileAsync(CancellationToken ct) =>
        File.Exists(CoreRules) ? await CompileAsync(CoreRules, ct) : "Download the community rules first.";

    static string FirstLine(string s) => s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "unknown error";

    // ---- scanning ----

    static readonly Regex MatchLine = new(
        @"^(?<rule>\S+) \[(?<tags>[^\]]*)\] \[(?<meta>.*)\] (?<target>[A-Za-z]:\\.*|\d+)$", RegexOptions.Compiled);

    static string Meta(string meta, string key)
    {
        var m = Regex.Match(meta, key + @"\s*=\s*""(?<v>(?:[^""\\]|\\.)*)""");
        return m.Success ? m.Groups["v"].Value : "";
    }

    static int MetaInt(string meta, string key)
    {
        var m = Regex.Match(meta, key + @"\s*=\s*(?<v>\d+)");
        return m.Success && int.TryParse(m.Groups["v"].Value, out var v) ? v : 0;
    }

    static List<YaraMatch> Parse(string output)
    {
        var list = new List<YaraMatch>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var m = MatchLine.Match(line);
            if (!m.Success) continue;
            var meta = m.Groups["meta"].Value;
            list.Add(new YaraMatch(m.Groups["rule"].Value, Meta(meta, "description"), Meta(meta, "author"), Meta(meta, "reference"),
                MetaInt(meta, "score"), MetaInt(meta, "quality"), m.Groups["target"].Value));
        }
        return list;
    }

    sealed record RunResult(int ExitCode, string Output, string Error);

    static async Task<RunResult> RunAsync(string exe, string args, TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try
        {
            var r = await ProcessRunner.RunAsync(exe, args, null, Encoding.UTF8, linked.Token);
            return new RunResult(r.ExitCode, r.Output, r.Error);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RunResult(-1, "", "timed out");
        }
    }

    /// <summary>Scans files in batches so progress can be reported. Paths longer than the classic Windows limit are skipped.</summary>
    public static async Task<List<YaraMatch>> ScanFilesAsync(IReadOnlyList<string> paths, Action<int, int>? progress, CancellationToken ct)
    {
        var all = new List<YaraMatch>();
        if (!EngineAvailable || !RulesReady) return all;

        var usable = paths.Where(p => p.Length < 250).ToList();
        var listFile = Path.Combine(IntelStore.Dir, "scanlist.txt");
        var utf16NoBom = new UnicodeEncoding(false, false);   // the Windows build of yara reads --scan-list as UTF-16
        const int batch = 250;

        for (var i = 0; i < usable.Count; i += batch)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = usable.Skip(i).Take(batch).ToList();
            await File.WriteAllTextAsync(listFile, string.Join("\n", chunk) + "\n", utf16NoBom, ct);
            var r = await RunAsync(YaraExe, $"-C -w -m -g -p 4 --scan-list \"{CompiledPath}\" \"{listFile}\"", TimeSpan.FromMinutes(4), ct);
            all.AddRange(Parse(r.Output));
            progress?.Invoke(Math.Min(i + batch, usable.Count), usable.Count);
        }
        try { File.Delete(listFile); } catch { }
        return all;
    }

    /// <summary>Scans one process's memory with the rules tagged MEMORY. The caller is responsible for the anti-cheat safety checks.</summary>
    public static async Task<List<YaraMatch>> ScanProcessAsync(int pid, CancellationToken ct)
    {
        if (!EngineAvailable || !RulesReady) return new();
        var r = await RunAsync(YaraExe, $"-C -w -m -g -t MEMORY \"{CompiledPath}\" {pid}", TimeSpan.FromSeconds(40), ct);
        return Parse(r.Output);
    }

    // ---- anti-cheat safety ----

    static readonly string[] AntiCheatProcesses =
    {
        "EasyAntiCheat", "EasyAntiCheat_EOS", "EasyAntiCheat_Launcher", "BEService", "BEService_x64", "vgc", "vgtray",
        "FACEIT", "faceitservice", "ESEADriver", "ESEAClient", "GameMon", "GameMon64", "npggNT", "xhunter1", "PnkBstrA", "PnkBstrB",
    };

    /// <summary>
    /// Name of a running anti-cheat service, if any. Reading another program's memory can look like cheating to these
    /// systems and has gotten accounts banned, so memory scanning refuses to run while one is active.
    /// </summary>
    public static string? RunningAntiCheat()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                    if (AntiCheatProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) return p.ProcessName;
            }
        }
        catch { }
        return null;
    }

    static readonly string[] GameFolders =
    {
        @"\steamapps\", @"\Epic Games\", @"\Riot Games\", @"\Battle.net\", @"\GOG Galaxy\Games\", @"\Ubisoft Game Launcher\games\",
        @"\EA Games\", @"\Origin Games\", @"\XboxGames\", @"\Rockstar Games\", @"\WindowsApps\",
    };

    public static bool IsGamePath(string path) => GameFolders.Any(g => path.Contains(g, StringComparison.OrdinalIgnoreCase));
}

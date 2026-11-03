namespace Guardian.Services.Deep;

/// <summary>Single-file and single-command checks for the real-time protection watchers.</summary>
public sealed partial class DeepScanner
{
    /// <summary>Checks one file with the full engine (signature, location, name, known-bad hashes, YARA). Returns the worst finding, or null when it looks fine.</summary>
    public async Task<Finding?> ScanFileAsync(string path, CancellationToken ct)
    {
        _ct = ct;
        if (!File.Exists(path)) return null;
        var ev = Assess(path, null, false);
        ReportAssessed(FindingKind.File, $"Suspicious file: {Path.GetFileName(path)}", path, ev, path,
            "Check where this file came from. Quarantine it if you don't recognise it; quarantine can always be undone.");

        if (_yaraTargets.ContainsKey(path) && YaraEngine.EngineAvailable && YaraEngine.RulesReady)
        {
            var hits = await YaraEngine.ScanFilesAsync(new[] { path }, null, ct);
            if (hits.Count > 0) ReportSignatureHits(path, hits, 0);
        }

        lock (_lock)
            return _findings.Where(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(f => f.Severity).ThenByDescending(f => f.Reasons.Count).FirstOrDefault();
    }

    /// <summary>Scores a startup entry's command line (the program and anything it is told to run).</summary>
    public (int Score, List<string> Reasons, string? File) ScoreCommand(string command)
    {
        var ev = AssessCommand(command);
        var exe = ResolveExecutable(Heuristics_NormalizeCommandPath(command));
        return (ev.Score, ev.Reasons.ToList(), exe);
    }

    /// <summary>The severity a score maps to, for callers outside the scanner.</summary>
    public static Severity SeverityFor(int score) => ToSeverity(score);
}

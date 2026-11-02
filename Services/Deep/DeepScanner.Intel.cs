using System.Collections.Concurrent;

namespace Guardian.Services.Deep;

public sealed partial class DeepScanner
{
    /// <summary>Also scan running programs' memory with YARA. Off by default: see <see cref="YaraEngine.RunningAntiCheat"/>.</summary>
    public bool ScanProcessMemory { get; set; }

    /// <summary>Download fresh hash lists and rules first when the local copies are over a week old.</summary>
    public bool UpdateIntel { get; set; } = true;

    readonly ConcurrentDictionary<string, byte> _yaraTargets = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, Evidence> _evidence = new(StringComparer.OrdinalIgnoreCase);
    List<(int Id, string Name, string Path)> _processList = new();

    sealed class DirectProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    // =====================================================================
    //  Stage: threat intelligence (hash lists and YARA rules)
    // =====================================================================

    void UpdateThreatIntel()
    {
        Report(2, "Checking threat data", force: true);
        var problems = new List<string>();
        var progress = new DirectProgress<string>(msg => Report(40, msg, force: true));

        if (UpdateIntel)
        {
            try
            {
                if (ThreatIntel.IsStale(TimeSpan.FromDays(7)))
                {
                    Report(10, "Updating the known-bad hash lists", force: true);
                    ThreatIntel.UpdateAsync(progress, false, _ct).GetAwaiter().GetResult();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { problems.Add("hash lists: " + ex.Message); }

            try
            {
                if (YaraEngine.EngineAvailable && YaraEngine.RulesStale(TimeSpan.FromDays(7)))
                {
                    Report(50, "Updating the YARA rules", force: true);
                    var result = YaraEngine.UpdateRulesAsync(progress, false, _ct).GetAwaiter().GetResult();
                    if (!result.StartsWith("YARA rules")) problems.Add(result);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { problems.Add("YARA rules: " + ex.Message); }
        }

        // Pick up custom rules the user added or edited since the last compile.
        try
        {
            if (YaraEngine.EngineAvailable && YaraEngine.CustomRulesChanged())
            {
                Report(90, "Applying your custom YARA rules", force: true);
                var error = YaraEngine.RecompileAsync(_ct).GetAwaiter().GetResult();
                if (error != null) problems.Add(error);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { problems.Add("custom rules: " + ex.Message); }

        var missing = new List<string>();
        if (ThreatIntel.Count == 0) missing.Add("the known-bad hash lists");
        if (!YaraEngine.EngineAvailable) missing.Add("the YARA engine");
        else if (!YaraEngine.RulesReady) missing.Add("the YARA signature rules");

        if (missing.Count > 0 || problems.Count > 0)
        {
            var finding = new Finding
            {
                Kind = FindingKind.Security, Severity = Severity.Info, Key = "intel-status",
                Title = missing.Count > 0 ? "Some signature data is missing" : "Threat data couldn't be refreshed",
                Detail = missing.Count > 0 ? "Missing: " + string.Join(", ", missing) : "The existing data was used.",
                Recommendation = "Connect to the internet and open Settings > Threat intelligence > Update now, then scan again.",
            };
            foreach (var p in problems) finding.Reasons.Add(p);
            if (missing.Count > 0) finding.Reasons.Add("Without it Guardian relies on behaviour and location checks alone, which catch fewer known threats.");
            Add(finding);
        }
        Report(100, "Threat data ready", force: true);
    }

    // =====================================================================
    //  Stage: signature scan (YARA)
    // =====================================================================

    void SignatureScan()
    {
        if (!YaraEngine.EngineAvailable || !YaraEngine.RulesReady)
        {
            Report(100, "Signature rules not available", force: true);
            return;
        }

        var targets = _yaraTargets.Keys.Where(File.Exists).ToList();
        var ruleCount = IntelMeta.Load().RuleCount;
        Report(1, $"Matching {targets.Count:N0} files against {ruleCount:N0} signatures", force: true);

        var matches = YaraEngine.ScanFilesAsync(targets,
            (done, total) =>
            {
                Interlocked.Exchange(ref _checked, Interlocked.Read(ref _checked) + 0);
                Report(2 + 78.0 * done / Math.Max(1, total), $"Signatures: {done:N0} of {total:N0} files", force: true);
            }, _ct).GetAwaiter().GetResult();

        foreach (var group in matches.GroupBy(m => m.Target, StringComparer.OrdinalIgnoreCase))
            ReportSignatureHits(group.Key, group.ToList(), pid: 0);

        if (ScanProcessMemory) ScanMemory();
        Report(100, "Done", force: true);
    }

    void ScanMemory()
    {
        // Reading a game's memory can look like cheating to anti-cheat software and has gotten accounts banned.
        var antiCheat = YaraEngine.RunningAntiCheat();
        if (antiCheat != null)
        {
            Add(new Finding
            {
                Kind = FindingKind.Process, Severity = Severity.Info, Key = "memory-skipped",
                Title = "Memory scan skipped: an anti-cheat is running",
                Detail = antiCheat,
                Reasons = { $"{antiCheat} is running. Scanning another program's memory can look like cheating to it and may get a game account banned." },
                Recommendation = "Close the game and its launcher, then scan memory again.",
            });
            return;
        }

        var candidates = _processList
            .Where(p => p.Id > 4 && p.Id != Environment.ProcessId && !YaraEngine.IsGamePath(p.Path))
            .Where(p =>
            {
                var sig = Authenticode.Check(p.Path);
                return !(sig.IsValid && Authenticode.IsWellKnownPublisher(sig.Signer));   // don't probe Windows and big vendors' programs
            })
            .Take(60).ToList();

        var done = 0;
        Report(82, $"Scanning the memory of {candidates.Count} running programs", force: true);
        var hits = new ConcurrentBag<(int Pid, string Path, List<YaraMatch> Matches)>();
        try
        {
            Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = _ct }, p =>
            {
                var m = YaraEngine.ScanProcessAsync(p.Id, _ct).GetAwaiter().GetResult();
                if (m.Count > 0) hits.Add((p.Id, p.Path, m));
                var n = Interlocked.Increment(ref done);
                Report(82 + 18.0 * n / Math.Max(1, candidates.Count), $"Memory: {p.Name} (PID {p.Id})", force: true);
            });
        }
        catch (OperationCanceledException) { throw; }

        foreach (var (pid, path, matches) in hits)
            ReportSignatureHits(path, matches, pid);
    }

    /// <summary>Turns YARA matches into evidence, adding it to whatever was already known about the file.</summary>
    void ReportSignatureHits(string target, List<YaraMatch> hits, int pid)
    {
        var sig = Authenticode.Check(target);
        var ordered = hits.GroupBy(h => h.Rule).Select(g => g.First()).OrderByDescending(h => h.Score).ToList();

        var ev = new Evidence();
        if (_evidence.TryGetValue(target, out var known)) ev.Merge(known);

        var yaraPoints = 0;
        for (var i = 0; i < Math.Min(3, ordered.Count); i++)
        {
            var (points, reason) = YaraPoints(ordered[i], sig.IsValid, pid != 0);
            yaraPoints += i == 0 ? points : points / 2;
            ev.Reasons.Add(reason);
        }
        if (ordered.Count >= 3) yaraPoints += 10;
        ev.Score += Math.Min(yaraPoints, 90);

        var top = ordered[0];
        var detail = string.Join("\n", ordered.Take(3).Select(h =>
            $"{h.Rule}: {(h.Description.Length > 0 ? h.Description : "no description")}{(h.Reference.Length > 0 ? "  " + h.Reference : "")}"));
        var location = pid != 0 ? $"in the memory of {Path.GetFileName(target)} (PID {pid})" : $"in {Path.GetFileName(target)}";
        ReportAssessed(pid != 0 ? FindingKind.Process : FindingKind.File,
            $"Signature match {location}: {top.Rule}", target, ev, detail,
            "A signature match is a lead, not proof. Check the file on VirusTotal before removing it, and check where it came from.", pid);
    }

    /// <summary>How much a rule hit counts, from the rule's own confidence (its score and quality metadata) and how reliable that kind of rule is.</summary>
    static (int Points, string Reason) YaraPoints(YaraMatch m, bool signedValid, bool inMemory)
    {
        var score = m.Score > 0 ? m.Score : 60;
        var quality = m.Quality > 0 ? m.Quality : 60;
        var points = score >= 90 && quality >= 80 ? 70 : score >= 80 ? 55 : score >= 70 ? 40 : 28;

        // Auto-generated, "suspicious" and tool-indicator rules describe behaviour, not a specific threat.
        var weak = m.Rule.Contains("_Auto", StringComparison.OrdinalIgnoreCase) || m.Rule.Contains("SUSP_", StringComparison.OrdinalIgnoreCase)
                   || m.Rule.Contains("INDICATOR_", StringComparison.OrdinalIgnoreCase) || m.Rule.Contains("_TOOL_", StringComparison.OrdinalIgnoreCase)
                   || m.Rule.Contains("HKTL_", StringComparison.OrdinalIgnoreCase);
        var note = "";
        if (weak) { points = Math.Min(points, 8); note = " (a general or low-confidence rule, so it counts for little)"; }
        if (signedValid) { points /= 2; note += " (the file is validly signed, so this counts for less)"; }
        if (inMemory) points = Math.Min(points, 60);

        var desc = m.Description.Length > 0 ? $" ({m.Description})" : "";
        return (points, $"Matches the signature “{m.Rule}”{desc}. Rule confidence {score}/100{note}");
    }
}

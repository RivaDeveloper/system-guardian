using System.Diagnostics;
using System.Security.Cryptography;

namespace Guardian.Services.Deep;

/// <summary>
/// Guardian's own scanner. It looks in the places malware uses to hide and to survive a reboot, and scores
/// what it finds from observable evidence (signatures, locations, names, command lines). It never opens
/// documents, photos or ID scans, never uploads anything, and never removes anything by itself.
/// </summary>
public sealed partial class DeepScanner
{
    readonly List<Finding> _findings = new();
    readonly object _lock = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly List<string> _done = new();

    IProgress<ScanProgress>? _progress;
    CancellationToken _ct;
    long _checked;
    long _lastReportMs;
    int _stageIndex, _stageCount;
    double _stageBase, _stageWeight;
    string _stageName = "";

    sealed record StageDef(string Name, double Weight, Action Run);

    /// <summary>Stage names in run order, so the progress window can list them before the scan starts.</summary>
    public static readonly string[] StageNames =
    {
        "Threat intelligence", "Security settings", "Startup and persistence", "Running processes",
        "Kernel drivers", "Hiding places", "Signature scan",
    };

    public bool Elevated { get; } = Elevation.IsElevated;

    /// <summary>How many files the hiding-place sweep chose to look at (for diagnostics).</summary>
    public int CandidateCount { get; private set; }

    public async Task<List<Finding>> RunAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        _progress = progress;
        _ct = ct;

        var stages = new[]
        {
            new StageDef(StageNames[0], 4, UpdateThreatIntel),
            new StageDef(StageNames[1], 3, AuditSecurity),
            new StageDef(StageNames[2], 12, AuditStartup),
            new StageDef(StageNames[3], 8, AuditProcesses),
            new StageDef(StageNames[4], 10, AuditDrivers),
            new StageDef(StageNames[5], 40, SweepFiles),
            new StageDef(StageNames[6], 23, SignatureScan),
        };
        _stageCount = stages.Length;
        var total = stages.Sum(s => s.Weight);
        double accumulated = 0;

        for (var i = 0; i < stages.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            _stageIndex = i;
            _stageName = stages[i].Name;
            _stageBase = accumulated / total * 100;
            _stageWeight = stages[i].Weight / total * 100;
            Report(0, "Starting…", force: true);

            try { await Task.Run(stages[i].Run, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Add(new Finding
                {
                    Kind = FindingKind.Security, Severity = Severity.Info, Key = "stage-failed|" + stages[i].Name,
                    Title = $"“{stages[i].Name}” could not finish", Detail = ex.Message,
                    Recommendation = "Run Guardian as administrator and scan again.",
                });
            }
            _done.Add(stages[i].Name);
            accumulated += stages[i].Weight;
        }

        Report(100, "Finished", force: true, overallOverride: 100);
        return Finalize();
    }

    // ---- progress ----

    void Report(double stagePercent, string item, bool force = false, double? overallOverride = null)
    {
        if (_progress is null) return;
        var now = _clock.ElapsedMilliseconds;
        if (!force && now - Interlocked.Read(ref _lastReportMs) < 90) return;
        Interlocked.Exchange(ref _lastReportMs, now);

        int count; lock (_lock) count = _findings.Count;
        _progress.Report(new ScanProgress
        {
            Stage = _stageName, StageIndex = _stageIndex, StageCount = _stageCount,
            Overall = overallOverride ?? Math.Min(100, _stageBase + _stageWeight * Math.Clamp(stagePercent, 0, 100) / 100),
            Item = item, ItemsChecked = Interlocked.Read(ref _checked), FindingCount = count,
            CompletedStages = _done.ToList(),
        });
    }

    void Tick(string item, double stagePercent)
    {
        _ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _checked);
        Report(stagePercent, item);
    }

    void Add(Finding f)
    {
        lock (_lock) _findings.Add(f);
    }

    // ---- evidence ----

    sealed class Evidence
    {
        public int Score;
        public readonly List<string> Reasons = new();
        public void Add(int points, string reason) { Score += points; Reasons.Add(reason); }
        public void Merge(Evidence other, string prefix = "")
        {
            Score += other.Score;
            Reasons.AddRange(other.Reasons.Select(r => prefix + r));
        }
    }

    static Severity ToSeverity(int score) =>
        score >= 95 ? Severity.Critical : score >= 65 ? Severity.High : score >= 45 ? Severity.Medium : score >= 30 ? Severity.Low : Severity.Info;

    static readonly HashSet<string> PeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".scr", ".com", ".pif", ".cpl", ".dll", ".ocx", ".sys", ".drv" };

    /// <summary>Scores one program file from what can be observed about it.</summary>
    Evidence Assess(string path, string? commandLine, bool running, bool skipTrustedLocations = false)
    {
        var ev = new Evidence();
        foreach (var (reason, points) in Heuristics.CommandLineIndicators(commandLine)) ev.Add(points, reason);

        if (!File.Exists(path))
        {
            if (running) ev.Add(60, "It is running, but the file it started from no longer exists on disk (malware sometimes deletes itself)");
            else ev.Add(15, "The file this entry points to does not exist (a leftover entry)");
            return ev;
        }

        var name = Path.GetFileName(path);
        var loc = Heuristics.Where(path);

        // Bulk sweeps skip files in ordinary install locations: they are checked if they run or start with Windows.
        if (skipTrustedLocations && loc.Risk == 0 && !Heuristics.IsInWindowsDir(path)) return ev;

        var sig = Authenticode.Check(path);
        var trusted = sig.IsValid;
        var isPe = PeExtensions.Contains(Path.GetExtension(path));

        if (sig.Status == SignatureStatus.Tampered)
            ev.Add(80, "Its digital signature no longer matches the file: it was changed after it was signed");
        else if (sig.Status == SignatureStatus.Untrusted)
            ev.Add(Math.Min(loc.Risk, 12) + 5, $"Signed by {(sig.Signer.Length > 0 ? sig.Signer : "an unknown publisher")}, but that certificate isn't trusted (expired or unrecognised)");
        else if ((sig.Status == SignatureStatus.Unsigned || (sig.Status == SignatureStatus.NotApplicable && isPe)) && loc.Risk > 0)
            ev.Add(loc.Risk, $"Unsigned program in {loc.Label}");   // a "program" Windows can't even parse as one counts as unsigned

        if (sig.Status == SignatureStatus.Unsigned && Heuristics.IsInWindowsDir(path) && isPe)
            ev.Add(40, "Unsigned program inside the Windows system folder");

        if (trusted && !Authenticode.IsWellKnownPublisher(sig.Signer) && loc.Risk >= 30)
            ev.Add(10, $"Signed by {sig.Signer}, but stored in {loc.Label}");

        var masquerade = Heuristics.Masquerade(path, includeNearMiss: loc.Risk >= 20);
        // A properly signed file inside the Windows folder is the real thing (32-bit copies legitimately live in SysWOW64).
        if (masquerade != null && !(trusted && Heuristics.IsInWindowsDir(path))) ev.Add(80, masquerade);

        if (Heuristics.HasDoubleExtension(name)) ev.Add(50, "A double extension makes a program look like a document");
        if (Heuristics.HasBidiTrick(name)) ev.Add(70, "Its name uses a hidden text-direction character to disguise the real extension");

        if (!trusted && loc.Risk >= 20)
        {
            // A program that is running right now out of a temp-like folder is a much stronger signal than one sitting there.
            if (running && loc.Risk >= 30) ev.Add(25, $"It is running right now from {loc.Label}");

            // Temp folders are full of random names, so that only means something elsewhere.
            if (!Heuristics.IsTightHotZone(path) && Heuristics.LooksRandom(Path.GetFileNameWithoutExtension(name))) ev.Add(15, "Random-looking file name");

            try
            {
                var attrs = File.GetAttributes(path);
                if (attrs.HasFlag(FileAttributes.Hidden) && attrs.HasFlag(FileAttributes.System))
                    ev.Add(20, "Marked hidden and system so it stays out of sight");
            }
            catch { }

            if (isPe)
            {
                try
                {
                    var buffer = new byte[512 * 1024];
                    int read;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        read = fs.Read(buffer, 0, buffer.Length);
                    // Weak signals on their own: installers and updaters are routinely compressed and unsigned.
                    if (read > 4096 && Heuristics.Entropy(buffer.AsSpan(0, read)) > 7.3)
                        ev.Add(5, "Its contents are packed or encrypted (very high entropy)");

                    var company = FileVersionInfo.GetVersionInfo(path).CompanyName;
                    if (string.IsNullOrWhiteSpace(company)) ev.Add(5, "No publisher or version information");
                }
                catch { }
            }
        }

        // Known-bad hashes, and registration for the signature (YARA) stage. Validly signed files from well-known publishers are skipped.
        if (!(trusted && Authenticode.IsWellKnownPublisher(sig.Signer)))
        {
            // Malware is rarely huge; capping the size keeps scans fast on machines full of big unsigned tools and games.
            var hashes = ThreatIntel.HashFile(path, maxBytes: 12_000_000);
            var known = hashes is null ? null : ThreatIntel.Lookup(hashes);
            if (known != null)
                ev.Add(known.IsVulnerableDriver ? 45 : 100, $"Its fingerprint matches a known {known.Kind}: {known.Name} (from {known.Source})");

            try
            {
                // Installer unpack folders (Inno Setup, NSIS, 7-Zip) hold plugin DLLs that signature rules often flag as "tools".
                if ((isPe || ScriptExtensions.Contains(Path.GetExtension(path))) && new FileInfo(path).Length <= 100_000_000 && !InstallerTemp.IsMatch(path))
                    _yaraTargets.TryAdd(path, 0);
            }
            catch { }
        }

        _evidence[path] = ev;
        return ev;
    }

    /// <summary>Scores a command line: the program it starts and any program or script it is told to run.</summary>
    Evidence AssessCommand(string command, bool running = false)
    {
        var ev = new Evidence();
        var exe = ResolveExecutable(Heuristics_NormalizeCommandPath(command));
        if (exe is null)
        {
            ev.Add(10, "The program it starts could not be found");
            foreach (var (reason, points) in Heuristics.CommandLineIndicators(command)) ev.Add(points, reason);
            return ev;
        }
        ev.Merge(Assess(exe, command, running));

        foreach (var target in ArgumentTargets(command))
        {
            if (target.Equals(exe, StringComparison.OrdinalIgnoreCase) || !File.Exists(target)) continue;
            var inner = Assess(target, null, false);
            ev.Merge(inner, $"It also runs {Path.GetFileName(target)}: ");
        }
        return ev;
    }

    static readonly System.Text.RegularExpressions.Regex ArgumentPath = new(
        @"(?<p>[A-Za-z]:\\[^""<>|*?\r\n]+?\.(exe|dll|vbs|vbe|js|jse|ps1|bat|cmd|hta|scr|wsf))",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static IEnumerable<string> ArgumentTargets(string command) =>
        ArgumentPath.Matches(Environment.ExpandEnvironmentVariables(command)).Select(m => m.Groups["p"].Value).Distinct(StringComparer.OrdinalIgnoreCase);

    static string Heuristics_NormalizeCommandPath(string raw)
    {
        raw = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (raw.StartsWith('"'))
        {
            var end = raw.IndexOf('"', 1);
            return end > 1 ? raw[1..end] : raw.Trim('"');
        }
        var m = System.Text.RegularExpressions.Regex.Match(raw,
            @"^(?<p>.+?\.(exe|com|scr|bat|cmd|ps1|vbs|js|msi|dll|sys|cpl|hta))(\s|$|,)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["p"].Value : raw.Split(' ')[0];
    }

    static string? ResolveExecutable(string path)
    {
        path = path.Trim().Trim('"');
        if (path.StartsWith(@"\??\")) path = path[4..];
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(Heuristics.WindowsDir, path[12..]);
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;

        foreach (var dir in new[] { Heuristics.System32, Heuristics.WindowsDir })
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate)) return candidate;
            if (File.Exists(candidate + ".exe")) return candidate + ".exe";
        }
        return null;
    }

    static string Sha256Of(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch { return ""; }
    }

    void ReportAssessed(FindingKind kind, string title, string path, Evidence ev, string detail, string recommendation, int pid = 0, int threshold = 30)
    {
        if (ev.Score < threshold) return;
        var exists = File.Exists(path);
        var f = new Finding
        {
            Key = $"{kind}|{path.ToLowerInvariant()}",
            Kind = kind, Severity = ToSeverity(ev.Score), Title = title, Detail = detail,
            FilePath = exists ? path : "", Reasons = ev.Reasons.ToList(), Recommendation = recommendation, Pid = pid,
        };
        if (exists)
        {
            f.Sha256 = Sha256Of(path);
            f.Signer = Authenticode.Check(path).Signer;
        }
        Add(f);
    }

    // ---- results ----

    List<Finding> Finalize()
    {
        if (!Elevated)
        {
            Add(new Finding
            {
                Kind = FindingKind.Security, Severity = Severity.Info, Key = "not-elevated",
                Title = "Some checks were limited", Detail = "Guardian is not running as administrator.",
                Reasons = { "Other users' folders, the Recycle Bin, scheduled-task details, WMI subscriptions, Defender exclusions and boot settings can't be read without administrator rights." },
                Recommendation = "Use “Restart as administrator” and scan again for a complete picture.",
            });
        }

        // One file reported by several stages becomes one finding that lists every reason.
        var merged = new List<Finding>();
        foreach (var group in _findings.GroupBy(f => f.FilePath.Length > 0 ? f.FilePath.ToLowerInvariant() : Guid.NewGuid().ToString()))
        {
            var items = group.OrderByDescending(f => f.Severity).ThenBy(f => f.Kind == FindingKind.File ? 1 : 0).ToList();
            var best = items[0];
            foreach (var other in items.Skip(1))
            {
                foreach (var r in other.Reasons)
                    if (!best.Reasons.Contains(r)) best.Reasons.Add($"{other.KindText}: {r}");
                if (other.Pid != 0 && best.Pid == 0) best.Pid = other.Pid;
                if (other.Kind is FindingKind.Startup or FindingKind.Process && best.Kind == FindingKind.File)
                {
                    best.Kind = other.Kind;
                    best.Title = other.Title;
                }
            }
            merged.Add(best);
        }

        return merged.OrderByDescending(f => f.Severity).ThenBy(f => f.Kind).ThenBy(f => f.Title).ToList();
    }
}

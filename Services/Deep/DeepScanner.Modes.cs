namespace Guardian.Services.Deep;

/// <summary>
/// The scans you can choose from besides the full deep scan. All of them use the same engine and the same evidence rules;
/// they only differ in how much ground they cover, so a quick check of the usual places doesn't need the time of a deep scan.
/// </summary>
public sealed partial class DeepScanner
{
    /// <summary>Stage names for a quick scan, in run order.</summary>
    public static readonly string[] QuickStageNames =
    {
        "Threat intelligence", "Startup and persistence", "Running processes", "Common hiding places", "Signature scan",
    };

    /// <summary>Stage names for a folder scan, in run order.</summary>
    public static readonly string[] FolderStageNames = { "Finding programs and scripts", "Checking files", "Signature scan" };

    List<Candidate> _pending = new();
    string _folder = "";

    /// <summary>
    /// A quick scan: startup entries, running programs and the places malware lands first (Downloads, temp folders, AppData,
    /// ProgramData, Public), a few folders deep. Skips the kernel driver audit, the security-settings audit and the deep file sweep.
    /// </summary>
    public Task<List<Finding>> RunQuickAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        _maxDepth = 3;
        return RunStagesAsync(new[]
        {
            new StageDef(QuickStageNames[0], 4, UpdateThreatIntel),
            new StageDef(QuickStageNames[1], 18, AuditStartup),
            new StageDef(QuickStageNames[2], 14, AuditProcesses),
            new StageDef(QuickStageNames[3], 44, SweepQuick),
            new StageDef(QuickStageNames[4], 20, SignatureScan),
        }, progress, ct);
    }

    /// <summary>Scans the programs and scripts inside one folder (and its subfolders) that the user picked, for example Downloads or a USB drive.</summary>
    public Task<List<Finding>> ScanFolderAsync(string folder, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        _folder = folder;
        _maxDepth = 12;
        return RunStagesAsync(new[]
        {
            new StageDef(FolderStageNames[0], 12, ListFolder),
            new StageDef(FolderStageNames[1], 63, () => CheckCandidates(_pending)),
            new StageDef(FolderStageNames[2], 25, SignatureScan),
        }, progress, ct);
    }

    void SweepQuick()
    {
        Report(1, "Choosing folders to look in", force: true);
        var candidates = new List<Candidate>();
        foreach (var dir in QuickPlaces())
        {
            Report(5, "Listing " + dir);
            Walk(dir, 0, true, candidates);
        }
        CheckCandidates(candidates);
    }

    IEnumerable<string> QuickPlaces()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, "Downloads");
        yield return Path.GetTempPath().TrimEnd('\\');
        yield return Path.Combine(Heuristics.WindowsDir, "Temp");
        yield return Path.Combine(Heuristics.UsersDir, "Public");
        yield return Path.Combine(profile, @"AppData\Roaming");
        yield return Path.Combine(profile, @"AppData\Local");
        yield return Heuristics.ProgramData;
    }

    void ListFolder()
    {
        Report(2, "Listing " + _folder, force: true);
        var list = new List<Candidate>();
        Walk(_folder, 0, true, list);
        _pending = list;
        CandidateCount = list.Count;
    }
}

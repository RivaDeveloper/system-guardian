namespace Guardian.Services.Deep;

public enum Severity { Info, Low, Medium, High, Critical }

public enum FindingKind { Security, Startup, Process, Driver, File }

/// <summary>
/// One thing the deep scan thinks you should look at, with the reasons it was flagged.
/// Plain properties (no records): the XAML type-info generator needs settable properties.
/// </summary>
public sealed partial class Finding
{
    /// <summary>Stable identity used by the ignore list (kind plus path or title).</summary>
    public string Key { get; set; } = "";
    public FindingKind Kind { get; set; }
    public Severity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Signer { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public int Pid { get; set; }
    public List<string> Reasons { get; set; } = new();

    public string SeverityText => Severity.ToString().ToUpperInvariant();

    public string KindText => Kind switch
    {
        FindingKind.Security => "Security setting",
        FindingKind.Startup => "Startup entry",
        FindingKind.Process => "Running process",
        FindingKind.Driver => "Kernel driver",
        _ => "File",
    };

    public string ReasonsText => string.Join("\n", Reasons.Select(r => "• " + r));

    public bool HasFile => FilePath.Length > 0 && File.Exists(FilePath);
}

/// <summary>What the progress window shows while a scan runs.</summary>
public sealed class ScanProgress
{
    public string Stage { get; set; } = "";
    public int StageIndex { get; set; }
    public int StageCount { get; set; }
    /// <summary>0 to 100 across the whole scan.</summary>
    public double Overall { get; set; }
    /// <summary>What is being looked at right now (a path, a registry key, a process).</summary>
    public string Item { get; set; } = "";
    public long ItemsChecked { get; set; }
    public int FindingCount { get; set; }
    public List<string> CompletedStages { get; set; } = new();
}

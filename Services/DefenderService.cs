namespace Guardian.Services;

public sealed record DefenderStatus(bool Enabled, bool RealTime, string SignatureVersion, string SignaturesUpdated);

// Classes with settable properties (not records): the XAML type-info generator can't assign init-only properties.
public sealed class ThreatInfo(string id, string name, int severity, bool active, string resources)
{
    public string Id { get; set; } = id;
    public string Name { get; set; } = name;
    public int Severity { get; set; } = severity;
    public bool Active { get; set; } = active;
    public string Resources { get; set; } = resources;
    public string SeverityText => Severity switch { 1 => "Low", 2 => "Moderate", 4 => "High", 5 => "Severe", _ => "Unknown" };
    public string StateText => Active ? "Active" : "Quarantined / resolved";
}

public enum DefenderScanType { Quick, Full, Custom }

/// <summary>
/// Drives Microsoft Defender through its PowerShell cmdlets. Nothing is ever removed
/// without the caller asking for it explicitly.
/// </summary>
public sealed class DefenderService
{
    public async Task<DefenderStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunPowerShellAsync(
            "$s=Get-MpComputerStatus; ConvertTo-Json -Compress -InputObject ([pscustomobject]@{" +
            "Enabled=[bool]$s.AntivirusEnabled;RealTime=[bool]$s.RealTimeProtectionEnabled;" +
            "Version=[string]$s.AntivirusSignatureVersion;Updated=$s.AntivirusSignatureLastUpdated.ToString('yyyy-MM-dd HH:mm')})",
            ct: ct);
        var item = JsonHelper.Items(r.Output).FirstOrDefault();
        if (item.ValueKind == System.Text.Json.JsonValueKind.Undefined) return null;
        return new DefenderStatus(item.Bool("Enabled"), item.Bool("RealTime"), item.Str("Version"), item.Str("Updated"));
    }

    public async Task<bool> UpdateSignaturesAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunPowerShellAsync("Update-MpSignature", ct: ct);
        return r.ExitCode == 0;
    }

    /// <summary>Runs a scan and waits for it to finish. Defender reports no incremental progress.</summary>
    public async Task<ProcessResult> ScanAsync(DefenderScanType type, string? path, CancellationToken ct = default)
    {
        var script = type switch
        {
            DefenderScanType.Quick => "Start-MpScan -ScanType QuickScan",
            DefenderScanType.Full => "Start-MpScan -ScanType FullScan",
            _ => $"Start-MpScan -ScanType CustomScan -ScanPath '{ProcessRunner.PsQuote(path ?? "")}'",
        };
        return await ProcessRunner.RunPowerShellAsync(script, ct: ct);
    }

    public async Task<List<ThreatInfo>> GetThreatsAsync(CancellationToken ct = default)
    {
        var script =
            "$d=@(Get-MpThreatDetection); " +
            "$items=@(Get-MpThreat | ForEach-Object { $id=$_.ThreatID; " +
            "$res=($d | Where-Object { $_.ThreatID -eq $id } | ForEach-Object { $_.Resources } | Select-Object -Unique) -join '; '; " +
            "[pscustomobject]@{Id=[string]$id;Name=$_.ThreatName;Severity=[int]$_.SeverityID;Active=[bool]$_.IsActive;Resources=$res} }); " +
            "ConvertTo-Json -Compress -InputObject $items";
        var r = await ProcessRunner.RunPowerShellAsync(script, ct: ct);
        return JsonHelper.Items(r.Output)
            .Select(e => new ThreatInfo(e.Str("Id"), e.Str("Name"), e.Int("Severity"), e.Bool("Active"), e.Str("Resources")))
            .ToList();
    }

    /// <summary>Removes active threats via Defender (quarantine/delete). Callers must confirm with the user first.</summary>
    public async Task<bool> RemoveActiveThreatsAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunPowerShellAsync("Remove-MpThreat", ct: ct);
        return r.ExitCode == 0;
    }
}

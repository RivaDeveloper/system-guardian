using System.Globalization;

namespace Guardian.Services;

public sealed class DriverInfo(string name, string manufacturer, string version, DateTime? date, string deviceClass)
{
    public string Name { get; set; } = name;
    public string Manufacturer { get; set; } = manufacturer;
    public string Version { get; set; } = version;
    public DateTime? Date { get; set; } = date;
    public string DeviceClass { get; set; } = deviceClass;

    // In-box Microsoft drivers are routinely dated 2006, so they are not treated as stale.
    public bool IsOld => Date is { } d && (DateTime.Today - d).TotalDays > 365 * 3
        && !Manufacturer.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
    public Microsoft.UI.Xaml.Media.Brush StatusBrush => IsOld ? Tones.Warn : Tones.Good;
    public string DateText => Date?.ToString("yyyy-MM-dd") ?? "";
    public string Status => Date is { } d && IsOld ? $"Old ({(DateTime.Today - d).TotalDays / 365:0.0} yrs)" : "OK";
}

public sealed class DriverUpdate(string title, string provider, string date)
{
    public string Title { get; set; } = title;
    public string Provider { get; set; } = provider;
    public string Date { get; set; } = date;
    public string Subtitle => $"{Provider} · {Date}";
}

public sealed record InstallOutcome(bool Success, bool RebootRequired, string Message);

public sealed class DriverService
{
    public async Task<List<DriverInfo>> GetInstalledAsync(CancellationToken ct = default)
    {
        var script =
            "$items=@(Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -and $_.DriverVersion } | " +
            "Sort-Object DeviceClass,DeviceName | ForEach-Object { [pscustomobject]@{Name=$_.DeviceName;Manufacturer=[string]$_.Manufacturer;" +
            "Version=[string]$_.DriverVersion;Date=$(if($_.DriverDate){$_.DriverDate.ToString('yyyy-MM-dd')}else{''});Class=[string]$_.DeviceClass} }); " +
            "ConvertTo-Json -Compress -InputObject $items";
        var r = await ProcessRunner.RunPowerShellAsync(script, ct: ct);
        return JsonHelper.Items(r.Output).Select(e =>
        {
            DateTime? date = DateTime.TryParseExact(e.Str("Date"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d) ? d : null;
            return new DriverInfo(e.Str("Name"), e.Str("Manufacturer"), e.Str("Version"), date, e.Str("Class"));
        }).ToList();
    }

    /// <summary>Asks Windows Update which driver updates are pending. This is the authoritative "outdated" check.</summary>
    public async Task<List<DriverUpdate>> GetAvailableUpdatesAsync(CancellationToken ct = default)
    {
        var script =
            "$s=New-Object -ComObject Microsoft.Update.Session; " +
            "$r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Driver'\"); " +
            "$items=@($r.Updates | ForEach-Object { [pscustomobject]@{Title=[string]$_.Title;Provider=[string]$_.DriverProvider;" +
            "Date=$(if($_.DriverVerDate){([datetime]$_.DriverVerDate).ToString('yyyy-MM-dd')}else{''})} }); " +
            "ConvertTo-Json -Compress -InputObject $items";
        var r = await ProcessRunner.RunPowerShellAsync(script, ct: ct);
        return JsonHelper.Items(r.Output)
            .Select(e => new DriverUpdate(e.Str("Title"), e.Str("Provider"), e.Str("Date")))
            .ToList();
    }

    /// <summary>Creates a restore point (best effort), then downloads and installs one driver update.</summary>
    public async Task<InstallOutcome> InstallAsync(string title, CancellationToken ct = default)
    {
        var script =
            "try{Checkpoint-Computer -Description 'Guardian driver update' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop}catch{}; " +
            "$s=New-Object -ComObject Microsoft.Update.Session; " +
            "$r=$s.CreateUpdateSearcher().Search(\"IsInstalled=0 and Type='Driver'\"); " +
            "$c=New-Object -ComObject Microsoft.Update.UpdateColl; " +
            $"foreach($u in $r.Updates){{ if($u.Title -eq '{ProcessRunner.PsQuote(title)}'){{ if(-not $u.EulaAccepted){{$u.AcceptEula()}}; [void]$c.Add($u) }} }}; " +
            "if($c.Count -eq 0){ 'NOTFOUND'; exit 2 }; " +
            "$d=$s.CreateUpdateDownloader(); $d.Updates=$c; [void]$d.Download(); " +
            "$i=$s.CreateUpdateInstaller(); $i.Updates=$c; $res=$i.Install(); " +
            "\"RESULT:$($res.ResultCode);REBOOT:$($res.RebootRequired)\"";
        var r = await ProcessRunner.RunPowerShellAsync(script, ct: ct);
        var text = r.Output;
        if (text.Contains("NOTFOUND")) return new InstallOutcome(false, false, "Update is no longer offered by Windows Update.");
        var ok = text.Contains("RESULT:2") || text.Contains("RESULT:3"); // 2 = succeeded, 3 = succeeded with errors
        var reboot = text.Contains("REBOOT:True");
        return new InstallOutcome(ok, reboot, ok ? (reboot ? "Installed. Restart required." : "Installed.") : "Install failed. Run as administrator and try again.");
    }
}

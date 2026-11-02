namespace Guardian.Services.Cleanup;

public sealed class DriverBackupInfo(string folder, DateTime created, int packages)
{
    public string Folder { get; set; } = folder;
    public DateTime Created { get; set; } = created;
    public int Packages { get; set; } = packages;
    public string Text => $"{Created:yyyy-MM-dd HH:mm} · {Packages} driver package(s)";
}

/// <summary>Backs up and restores third-party driver packages with the built-in pnputil tool.</summary>
public sealed class DriverBackup
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guardian driver backups");

    public async Task<DriverBackupInfo> BackupAsync(Action<string>? onLine = null, CancellationToken ct = default)
    {
        var folder = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var r = await ProcessRunner.RunAsync("pnputil.exe", $"/export-driver * \"{folder}\"", onLine, ct: ct);
        var count = Directory.GetDirectories(folder).Length;
        if (count == 0)
        {
            try { Directory.Delete(folder, true); } catch { }
            throw new InvalidOperationException("pnputil could not export the drivers. " + r.Output.Trim());
        }
        return new DriverBackupInfo(folder, DateTime.Now, count);
    }

    public List<DriverBackupInfo> List()
    {
        var list = new List<DriverBackupInfo>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.GetDirectories(Root).OrderByDescending(d => d))
            list.Add(new DriverBackupInfo(dir, Directory.GetCreationTime(dir), Directory.GetDirectories(dir).Length));
        return list;
    }

    public Task<ProcessResult> RestoreAsync(DriverBackupInfo backup, Action<string>? onLine = null, CancellationToken ct = default) =>
        ProcessRunner.RunAsync("pnputil.exe", $"/add-driver \"{Path.Combine(backup.Folder, "*.inf")}\" /subdirs /install", onLine, ct: ct);
}

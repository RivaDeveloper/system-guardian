using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Guardian.Services.Deep;
using Microsoft.Win32;

namespace Guardian.Services.Protection;

public sealed partial class ProtectionEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime When { get; set; } = DateTime.Now;
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    /// <summary>Info, Low, Medium, High or Critical.</summary>
    public string Severity { get; set; } = "Info";
    /// <summary>"Alert" (needs a decision), "Quarantined", "Allowed" or "Info".</summary>
    public string Action { get; set; } = "Info";
    public string WhenText => When.ToString("MMM d, HH:mm");
    public bool CanQuarantine => Action == "Alert" && Path.Length > 0 && File.Exists(Path);
    public string ActionText => Action switch { "Alert" => "Needs a look", "Quarantined" => "Quarantined", "Allowed" => "Allowed", _ => "" };
}

/// <summary>What Guardian's own protection has done since it was installed (kept in %LOCALAPPDATA%\Guardian).</summary>
public sealed class ProtectionState
{
    public long FilesChecked { get; set; }
    public int ThreatsFound { get; set; }
    public int Quarantined { get; set; }
    public List<string> StartupBaseline { get; set; } = new();
    public DateTime? LastScheduledScanUtc { get; set; }
}

/// <summary>
/// Guardian's real-time protection, running while the app is open (or in the tray). It watches the places malware lands
/// (downloads, desktop, temp and startup folders, plus USB drives), checks every new program or script with Guardian's own
/// engine, and watches for new startup entries. It works in user mode: it sees files after they are written, not before
/// they run, and it never reads documents. Each part has its own switch in <see cref="ProtectionSettings"/>.
/// </summary>
public static class ProtectionService
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian");
    static string EventsFile => Path.Combine(Dir, "protection-events.json");
    static string StateFile => Path.Combine(Dir, "protection-state.json");

    static readonly HashSet<string> Watched = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".scr", ".com", ".pif", ".cpl", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta", ".jar",
    };
    static readonly HashSet<string> Partial = new(StringComparer.OrdinalIgnoreCase) { ".tmp", ".crdownload", ".part", ".download", ".partial" };

    static readonly object Gate = new();
    static readonly List<FileSystemWatcher> Watchers = new();
    static readonly ConcurrentDictionary<string, DateTime> Recent = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> SeenDrives = new(StringComparer.OrdinalIgnoreCase);
    static Channel<string>? _queue;
    static CancellationTokenSource? _cts;
    static Timer? _poll;
    static ProtectionSettings _settings = new();
    static ProtectionState _state = new();
    static readonly List<ProtectionEvent> _events = new();
    static DateTime _lastSave = DateTime.MinValue;
    static bool _scanningScheduled;

    public static bool Running { get; private set; }
    public static ProtectionState State => _state;
    public static IReadOnlyList<ProtectionEvent> Events { get { lock (Gate) return _events.ToList(); } }

    /// <summary>Raised on a background thread whenever something is logged.</summary>
    public static event Action<ProtectionEvent>? EventLogged;
    /// <summary>Raised when counters or the running state change.</summary>
    public static event Action? StatsChanged;
    /// <summary>Shows a notification to the user (set by the tray icon).</summary>
    public static Action<string, string>? Notifier;

    // ---- lifecycle --------------------------------------------------------------------------------

    public static void Start()
    {
        lock (Gate)
        {
            LoadFiles();
            _settings = SettingsStore.Load().Protection;
            Apply();
        }
    }

    /// <summary>Reads the settings again and starts or stops the parts accordingly.</summary>
    public static void Reconfigure()
    {
        lock (Gate)
        {
            _settings = SettingsStore.Load().Protection;
            Apply();
        }
    }

    static void Apply()
    {
        StopWatchers();
        if (!_settings.RealtimeEnabled) { Running = false; StatsChanged?.Invoke(); return; }

        _cts = new CancellationTokenSource();
        _queue = Channel.CreateUnbounded<string>();
        Running = true;

        var thread = new Thread(() => Worker(_queue, _cts.Token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Guardian protection" };
        thread.Start();

        if (_settings.WatchFiles) StartFileWatchers();
        _poll = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
        StatsChanged?.Invoke();
    }

    public static void Stop()
    {
        lock (Gate)
        {
            StopWatchers();
            Running = false;
            SaveState(force: true);
        }
    }

    static void StopWatchers()
    {
        _poll?.Dispose(); _poll = null;
        foreach (var w in Watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
        Watchers.Clear();
        _cts?.Cancel();
        _queue?.Writer.TryComplete();
        _queue = null;
    }

    // ---- file watching ----------------------------------------------------------------------------

    static IEnumerable<string> WatchedFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Downloads");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Path.GetTempPath().TrimEnd('\\');
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
    }

    static void StartFileWatchers()
    {
        foreach (var folder in WatchedFolders().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var w = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                w.Created += (_, e) => Enqueue(e.FullPath);
                w.Changed += (_, e) => Enqueue(e.FullPath);
                w.Renamed += (_, e) => Enqueue(e.FullPath);
                w.Error += (_, _) => { };
                w.EnableRaisingEvents = true;
                Watchers.Add(w);
            }
            catch { }
        }
    }

    static bool ShouldScan(string path)
    {
        var ext = Path.GetExtension(path);
        if (!Watched.Contains(ext) || Partial.Contains(ext)) return false;
        if (path.StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) return false;      // Guardian's own files
        foreach (var ex in _settings.Exclusions)
            if (path.StartsWith(ex.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || path.Equals(ex, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    static void Enqueue(string path)
    {
        if (!ShouldScan(path)) return;
        // The same file raises several events while it is written: scan it once per minute at most.
        var now = DateTime.UtcNow.ToLocalTime();
        if (Recent.TryGetValue(path, out var last) && now - last < TimeSpan.FromMinutes(1)) return;
        Recent[path] = now;
        if (Recent.Count > 5000) foreach (var k in Recent.Where(kv => now - kv.Value > TimeSpan.FromMinutes(10)).Select(kv => kv.Key).ToList()) Recent.TryRemove(k, out _);
        _queue?.Writer.TryWrite(path);
    }

    static void Worker(Channel<string> queue, CancellationToken ct)
    {
        try
        {
            while (queue.Reader.WaitToReadAsync(ct).AsTask().GetAwaiter().GetResult())
            {
                while (queue.Reader.TryRead(out var path))
                {
                    if (ct.IsCancellationRequested) return;
                    try { CheckFile(path, ct, quiet: false); }
                    catch (OperationCanceledException) { return; }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Waits for a download or copy to finish, then checks the file.</summary>
    static void CheckFile(string path, CancellationToken ct, bool quiet)
    {
        if (!WaitUntilStable(path, ct)) return;
        var scanner = new DeepScanner();
        var finding = scanner.ScanFileAsync(path, ct).GetAwaiter().GetResult();
        Interlocked.Increment(ref _checkedCounter);
        _state.FilesChecked = Interlocked.Read(ref _checkedCounter);
        SaveState();

        if (finding == null || finding.Severity < Severity.Medium)
        {
            if (_settings.CloudCheck && !Authenticode.Check(path).IsValid) finding = CloudCheck(path, ct) ?? finding;
            if (finding == null || finding.Severity < Severity.Medium) return;
        }
        Raise(finding, path);
    }

    static long _checkedCounter;

    static bool WaitUntilStable(string path, CancellationToken ct)
    {
        long last = -1;
        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path)) return false;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length == last && fs.Length > 0) return true;
                last = fs.Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return false; }
            Thread.Sleep(1000);
        }
        return false;
    }

    static Finding? CloudCheck(string path, CancellationToken ct)
    {
        try
        {
            var r = VirusTotalService.CheckOneAsync(path, ct).GetAwaiter().GetResult();
            if (r.Verdict != VtVerdict.Malicious || r.Malicious < 3) return null;
            return new Finding
            {
                Kind = FindingKind.File, Severity = r.Malicious >= 10 ? Severity.Critical : Severity.High, FilePath = path, Sha256 = r.Sha256,
                Title = $"VirusTotal: {r.Malicious} of {r.Total} engines flag {Path.GetFileName(path)}",
                Reasons = { $"{r.Malicious} antivirus engines on VirusTotal identify this file's fingerprint as malicious" },
                Recommendation = "Quarantine it unless you are sure of where it came from.",
            };
        }
        catch { return null; }
    }

    // ---- reacting to a finding --------------------------------------------------------------------

    static void Raise(Finding f, string path)
    {
        var ev = new ProtectionEvent
        {
            Title = f.Title, Detail = string.Join("\n", f.Reasons.Take(4).Select(r => "• " + r)), Path = path,
            Sha256 = f.Sha256, Severity = f.Severity.ToString(), Action = "Alert",
        };
        _state.ThreatsFound++;

        if (_settings.AutoQuarantine && f.Severity >= Severity.High)
        {
            try
            {
                Quarantine.Add(path, f.Sha256, f.Reasons.FirstOrDefault() ?? f.Title);
                ev.Action = "Quarantined";
                _state.Quarantined++;
            }
            catch (Exception ex) { ev.Detail += "\n• Couldn't quarantine it automatically: " + ex.Message; }
        }

        Log(ev);
        Notify(ev.Action == "Quarantined" ? "Guardian quarantined a threat" : "Guardian found something suspicious",
            $"{Path.GetFileName(path)}: {f.Reasons.FirstOrDefault() ?? f.Title}");
    }

    static void Notify(string title, string text)
    {
        if (_settings.Notify) Notifier?.Invoke(title, text);
    }

    public static void Log(ProtectionEvent e)
    {
        lock (Gate)
        {
            _events.Insert(0, e);
            if (_events.Count > 200) _events.RemoveRange(200, _events.Count - 200);
            try { Directory.CreateDirectory(Dir); File.WriteAllText(EventsFile, JsonSerializer.Serialize(_events)); } catch { }
        }
        SaveState(force: true);
        EventLogged?.Invoke(e);
        StatsChanged?.Invoke();
    }

    /// <summary>Quarantines the file behind an alert, at the user's request.</summary>
    public static void QuarantineFromEvent(ProtectionEvent e)
    {
        Quarantine.Add(e.Path, e.Sha256, e.Title);
        e.Action = "Quarantined";
        _state.Quarantined++;
        lock (Gate) { try { File.WriteAllText(EventsFile, JsonSerializer.Serialize(_events)); } catch { } }
        SaveState(force: true);
        StatsChanged?.Invoke();
    }

    public static void ClearHistory()
    {
        lock (Gate) { _events.Clear(); try { File.WriteAllText(EventsFile, "[]"); } catch { } }
        StatsChanged?.Invoke();
    }

    // ---- polling: startup entries, USB drives, schedule ---------------------------------------------

    static void Poll()
    {
        try
        {
            if (_settings.WatchStartup) CheckStartup();
            if (_settings.ScanUsb) CheckUsb();
            CheckSchedule();
        }
        catch { }
    }

    static Dictionary<string, string> StartupEntries()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, path, label) in new[]
        {
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (32-bit)"),
        })
        {
            try
            {
                using var k = hive.OpenSubKey(path);
                if (k == null) continue;
                foreach (var n in k.GetValueNames())
                    if (k.GetValue(n) is string cmd && cmd.Length > 0) map[$"{label}|{n}"] = cmd;
            }
            catch { }
        }
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
        {
            try
            {
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                        map[$"Startup folder|{Path.GetFileName(f)}"] = f;
            }
            catch { }
        }
        return map;
    }

    static void CheckStartup()
    {
        var current = StartupEntries();
        var baseline = new HashSet<string>(_state.StartupBaseline, StringComparer.OrdinalIgnoreCase);
        var first = baseline.Count == 0 && _state.StartupBaseline.Count == 0 && !File.Exists(StateFile + ".seeded");

        foreach (var (key, cmd) in current)
        {
            var id = key + "=" + cmd;
            if (baseline.Contains(id)) continue;
            _state.StartupBaseline.Add(id);
            if (first) continue;           // the first run only records what is already there
            if (_cts?.IsCancellationRequested == true) return;

            var (score, reasons, file) = new DeepScanner().ScoreCommand(cmd);
            var sev = DeepScanner.SeverityFor(score);
            var name = key[(key.IndexOf('|') + 1)..];
            if (sev >= Severity.Medium)
            {
                _state.ThreatsFound++;
                Log(new ProtectionEvent
                {
                    Title = $"New startup entry looks suspicious: {name}", Path = file ?? "", Severity = sev.ToString(), Action = "Alert",
                    Detail = $"{key.Split('|')[0]}: {cmd}\n" + string.Join("\n", reasons.Take(4).Select(r => "• " + r)),
                });
                Notify("A suspicious program was set to start with Windows", $"{name}: {reasons.FirstOrDefault()}");
            }
            else
            {
                Log(new ProtectionEvent { Title = $"New startup entry: {name}", Detail = $"{key.Split('|')[0]}: {cmd}", Severity = sev.ToString(), Action = "Info", Path = file ?? "" });
            }
        }
        if (first) try { File.WriteAllText(StateFile + ".seeded", "1"); } catch { }
        SaveState(force: true);
    }

    static void CheckUsb()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Removable || !d.IsReady) { if (!d.IsReady) SeenDrives.Remove(d.Name); continue; }
                if (!SeenDrives.Add(d.Name)) continue;
                ScanUsb(d.RootDirectory.FullName);
            }
            catch { }
        }
    }

    static void ScanUsb(string root)
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 5, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = new List<string>();
        var autorun = false;
        foreach (var f in Directory.EnumerateFiles(root, "*", opts))
        {
            if (f.EndsWith("autorun.inf", StringComparison.OrdinalIgnoreCase)) autorun = true;
            if (Watched.Contains(Path.GetExtension(f)) && !Partial.Contains(Path.GetExtension(f)) && ShouldScan(f)) files.Add(f);
            if (files.Count >= 400) break;
        }

        var threats = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var finding = new DeepScanner().ScanFileAsync(f, ct).GetAwaiter().GetResult();
                Interlocked.Increment(ref _checkedCounter);
                if (finding != null && finding.Severity >= Severity.Medium) { threats++; Raise(finding, f); }
            }
            catch (OperationCanceledException) { return; }
            catch { }
        }
        _state.FilesChecked = Interlocked.Read(ref _checkedCounter);

        if (autorun)
        {
            Log(new ProtectionEvent
            {
                Title = $"USB drive {root} has an autorun.inf", Severity = "Low", Action = "Info",
                Detail = "Autorun files were a common way for malware to spread from USB sticks. Modern Windows ignores them, but the file is worth a look.",
            });
        }
        Log(new ProtectionEvent
        {
            Title = $"Checked USB drive {root}", Severity = threats > 0 ? "High" : "Info", Action = "Info",
            Detail = $"{files.Count} programs and scripts checked, {threats} suspicious.",
        });
        Notify("USB drive checked", threats == 0 ? $"{root}: nothing suspicious." : $"{root}: {threats} suspicious file(s). Open Guardian to review.");
    }

    static void CheckSchedule()
    {
        if (_scanningScheduled || _settings.Schedule == "Off") return;
        var due = _settings.Schedule == "Weekly" ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1);
        if (_state.LastScheduledScanUtc is { } last && DateTime.UtcNow - last < due) return;
        _scanningScheduled = true;
        _state.LastScheduledScanUtc = DateTime.UtcNow;
        SaveState(force: true);

        var ct = _cts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                var findings = await new DeepScanner().RunAsync(null, ct);
                var serious = findings.Where(f => f.Severity >= Severity.Medium && f.Kind != FindingKind.Security).ToList();
                _state.ThreatsFound += serious.Count;
                Log(new ProtectionEvent
                {
                    Title = $"Scheduled scan finished: {(serious.Count == 0 ? "nothing suspicious" : serious.Count + " suspicious item(s)")}",
                    Severity = serious.Count > 0 ? serious.Max(f => f.Severity).ToString() : "Info", Action = serious.Count > 0 ? "Alert" : "Info",
                    Detail = serious.Count > 0 ? string.Join("\n", serious.Take(5).Select(f => "• " + f.Title)) + "\nOpen Virus scan for details." : "",
                });
                Notify("Scheduled scan finished", serious.Count == 0 ? "Nothing suspicious found." : $"{serious.Count} suspicious item(s). Open Guardian to review.");
            }
            catch { }
            finally { _scanningScheduled = false; }
        });
    }

    // ---- storage ----------------------------------------------------------------------------------

    static void LoadFiles()
    {
        try { if (File.Exists(EventsFile)) { _events.Clear(); _events.AddRange(JsonSerializer.Deserialize<List<ProtectionEvent>>(File.ReadAllText(EventsFile)) ?? new()); } } catch { }
        try { if (File.Exists(StateFile)) _state = JsonSerializer.Deserialize<ProtectionState>(File.ReadAllText(StateFile)) ?? new(); } catch { }
        Interlocked.Exchange(ref _checkedCounter, _state.FilesChecked);
    }

    static void SaveState(bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastSave < TimeSpan.FromSeconds(30)) return;
        _lastSave = DateTime.UtcNow;
        try { Directory.CreateDirectory(Dir); File.WriteAllText(StateFile, JsonSerializer.Serialize(_state)); } catch { }
        StatsChanged?.Invoke();
    }
}

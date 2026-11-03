using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Guardian.Services.Deep;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace Guardian.Services.Protection;

/// <summary>
/// Watches programs as they start, using Windows' own kernel event feed (ETW, the "Microsoft-Windows-Kernel-Process" provider).
/// Windows reports every new process within moments, so Guardian can check it with its engine right away. It is not a kernel
/// driver: the program has already begun when the report arrives. Optionally Guardian then pauses a high-risk program until the
/// user decides. Needs administrator rights (Windows only lets administrators listen to this feed).
/// </summary>
internal static class ProgramWatcher
{
    const string SessionName = "GuardianProgramWatch";
    const uint ProcessQueryLimited = 0x1000, ProcessSuspendResume = 0x800, ProcessTerminate = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr h);
    [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr h);

    static readonly object Gate = new();
    static TraceEventSession? _session;
    static BlockingCollection<(int Pid, int Parent)>? _queue;
    static CancellationTokenSource? _cts;
    static readonly ConcurrentDictionary<string, (DateTime Written, Severity Sev, Finding? Finding)> Verdicts = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<int, string> Paused = new();
    static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static bool Running { get; private set; }

    /// <summary>Set GUARDIAN_DEBUG_WATCH=1 to write what the watcher sees to %LOCALAPPDATA%\Guardian\program-watch.log.</summary>
    static void Debug(string message)
    {
        if (Environment.GetEnvironmentVariable("GUARDIAN_DEBUG_WATCH") is not ("1" or "all")) return;
        try { File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "program-watch.log"), $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}"); } catch { }
    }

    /// <summary>Why the watcher isn't running, for the Protection page ("" when it is, or isn't wanted).</summary>
    public static string LastError { get; private set; } = "";

    public static void Start()
    {
        lock (Gate)
        {
            if (Running) return;
            Debug($"Start called, elevated={Elevation.IsElevated}");
            if (!Elevation.IsElevated) { LastError = "Needs administrator rights."; return; }
            try
            {
                _cts = new CancellationTokenSource();
                _queue = new BlockingCollection<(int, int)>(2000);
                var queue = _queue;
                var ct = _cts.Token;

                var session = new TraceEventSession(SessionName) { StopOnDispose = true };
                session.EnableProvider("Microsoft-Windows-Kernel-Process", TraceEventLevel.Informational, 0x10);   // 0x10: process start and stop
                session.Source.Dynamic.All += e =>
                {
                    // Event 1 is "ProcessStart"; the new process' id is in the payload (e.ProcessID is whoever created it).
                    Debug($"event {(int)e.ID} {e.ProviderName}");
                    if ((int)e.ID != 1 || e.ProviderName != "Microsoft-Windows-Kernel-Process") return;
                    try
                    {
                        var pid = Convert.ToInt32(e.PayloadByName("ProcessID"));
                        var parent = Convert.ToInt32(e.PayloadByName("ParentProcessID"));
                        queue.TryAdd((pid, parent));
                    }
                    catch { }
                };
                _session = session;

                new Thread(() => { try { Debug("feed started"); session.Source.Process(); Debug("feed ended"); } catch (Exception ex) { Debug("feed error: " + ex); } }) { IsBackground = true, Name = "Guardian program feed" }.Start();
                new Thread(() => Worker(queue, ct)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Guardian program checks" }.Start();
                Running = true;
                LastError = "";
            }
            catch (Exception ex)
            {
                Debug("start error: " + ex);
                LastError = ex.Message;
                Stop(false);
            }
        }
    }

    /// <summary>Stops listening. <paramref name="resumeAll"/> also lets every paused program continue (used when Guardian exits).</summary>
    public static void Stop(bool resumeAll)
    {
        lock (Gate)
        {
            Running = false;
            try { _cts?.Cancel(); } catch { }
            try { _queue?.CompleteAdding(); } catch { }
            try { _session?.Dispose(); } catch { }
            _session = null; _queue = null;
            if (resumeAll) foreach (var pid in Paused.Keys.ToList()) Resume(pid);
        }
    }

    static void Worker(BlockingCollection<(int Pid, int Parent)> queue, CancellationToken ct)
    {
        var self = Environment.ProcessId;
        try
        {
            foreach (var (pid, parent) in queue.GetConsumingEnumerable(ct))
            {
                try { Check(pid, parent, self, ct); }
                catch (OperationCanceledException) { return; }
                catch { }
            }
        }
        catch (OperationCanceledException) { }
    }

    static void Check(int pid, int parent, int self, CancellationToken ct)
    {
        if (pid == self || parent == self) return;                                   // Guardian's own helpers (YARA, PowerShell)
        var path = ImagePath(pid);
        Debug($"start pid {pid} parent {parent}: {path ?? "(no path)"}");
        if (path == null || !File.Exists(path)) return;
        if (path.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase)) return; // Windows' own programs
        if (path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)) return;
        if (ProtectionService.IsExcluded(path)) return;

        // The same program starts again and again: judge it once per version of the file.
        var written = File.GetLastWriteTimeUtc(path);
        Finding? finding;
        var fresh = false;
        if (Verdicts.TryGetValue(path, out var cached) && cached.Written == written) finding = cached.Finding;
        else
        {
            finding = new DeepScanner().ScanFileAsync(path, ct).GetAwaiter().GetResult();
            Verdicts[path] = (written, finding?.Severity ?? Severity.Info, finding);
            ProtectionService.CountChecked();
            if (Verdicts.Count > 4000) Verdicts.Clear();
            fresh = true;
        }
        Debug($"  verdict {(finding?.Severity.ToString() ?? "clean")} fresh={fresh}");
        // GUARDIAN_DEBUG_WATCH=all lowers both bars to Low so the alert and pause paths can be tried with a harmless test program.
        var all = Environment.GetEnvironmentVariable("GUARDIAN_DEBUG_WATCH") == "all";
        if (finding == null || finding.Severity < (all ? Severity.Low : Severity.Medium)) return;

        var pause = ProtectionService.PauseRiskyPrograms && finding.Severity >= (all ? Severity.Low : Severity.High) && Suspend(pid, path);
        // A program that was already reported only raises another alert if it was paused again.
        if (fresh || pause) ProtectionService.RaiseProgram(finding, path, pid, pause);
    }

    static string? ImagePath(int pid)
    {
        var h = OpenProcess(ProcessQueryLimited, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // ---- pausing ---------------------------------------------------------------------------------------

    static bool Suspend(int pid, string path)
    {
        var h = OpenProcess(ProcessSuspendResume, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            if (NtSuspendProcess(h) != 0) return false;
            Paused[pid] = path;
            return true;
        }
        finally { CloseHandle(h); }
    }

    public static bool IsPaused(int pid) => Paused.ContainsKey(pid);

    public static void Resume(int pid)
    {
        Paused.TryRemove(pid, out _);
        var h = OpenProcess(ProcessSuspendResume, false, pid);
        if (h == IntPtr.Zero) return;
        try { NtResumeProcess(h); } finally { CloseHandle(h); }
    }

    /// <summary>Ends a paused program (before its file is quarantined).</summary>
    public static void Terminate(int pid)
    {
        Paused.TryRemove(pid, out _);
        var h = OpenProcess(ProcessTerminate | ProcessSuspendResume, false, pid);
        if (h == IntPtr.Zero) return;
        try { TerminateProcess(h, 1); } finally { CloseHandle(h); }
    }
}

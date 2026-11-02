using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Guardian.Services;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    /// <summary>Runs a process, streaming each non-empty stdout line (including \r progress updates) to <paramref name="onLine"/>.</summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName, string arguments, Action<string>? onLine = null,
        Encoding? encoding = null, CancellationToken ct = default)
    {
        var enc = encoding ?? Encoding.UTF8;
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = enc,
            StandardErrorEncoding = enc,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close(); // tools that prompt get EOF instead of hanging

        using var reg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        var output = new StringBuilder();
        var error = new StringBuilder();

        async Task Pump(StreamReader reader, StringBuilder sink, bool report)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                line = line.Replace("\0", "");
                sink.AppendLine(line);
                if (report && line.Trim().Length > 0) onLine?.Invoke(line);
            }
        }

        await Task.WhenAll(Pump(process.StandardOutput, output, true), Pump(process.StandardError, error, false));
        await process.WaitForExitAsync();
        ct.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    public static Task<ProcessResult> RunPowerShellAsync(
        string script, Action<string>? onLine = null, CancellationToken ct = default)
    {
        var full = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", onLine, null, ct);
    }

    /// <summary>Escapes a value for use inside a single-quoted PowerShell string.</summary>
    public static string PsQuote(string value) => value.Replace("'", "''");
}

public static class JsonHelper
{
    public static List<JsonElement> Items(string json)
    {
        var list = new List<JsonElement>();
        json = json.Trim();
        if (json.Length == 0) return list;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
            foreach (var e in doc.RootElement.EnumerateArray()) list.Add(e.Clone());
        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            list.Add(doc.RootElement.Clone());
        return list;
    }

    public static string Str(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";

    public static bool Bool(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static int Int(this JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}

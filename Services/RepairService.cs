using System.Text;
using System.Text.RegularExpressions;

namespace Guardian.Services;

public enum RepairStep { DismScan, DismRestore, Sfc, Chkdsk }

public sealed record RepairOutcome(RepairStep Step, bool Success, string Summary);

/// <summary>Wraps the built-in Windows repair tools. Requires an elevated process.</summary>
public sealed class RepairService
{
    static readonly Regex Percent = new(@"(\d+(?:[.,]\d+)?)\s*%");

    public static string Title(RepairStep s) => s switch
    {
        RepairStep.DismScan => "DISM: scan component store health",
        RepairStep.DismRestore => "DISM: repair component store",
        RepairStep.Sfc => "SFC: verify and repair system files",
        _ => "CHKDSK: online scan of C:",
    };

    public async Task<RepairOutcome> RunAsync(
        RepairStep step, Action<string> onLine, Action<double> onProgress, CancellationToken ct = default)
    {
        var (file, args, enc) = step switch
        {
            RepairStep.DismScan => ("dism.exe", "/Online /Cleanup-Image /ScanHealth", Encoding.UTF8),
            RepairStep.DismRestore => ("dism.exe", "/Online /Cleanup-Image /RestoreHealth", Encoding.UTF8),
            RepairStep.Sfc => ("sfc.exe", "/scannow", Encoding.Unicode), // sfc writes UTF-16 when redirected
            _ => ("chkdsk.exe", "C: /scan", Encoding.UTF8),
        };

        void Handle(string line)
        {
            onLine(line);
            var m = Percent.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p))
                onProgress(Math.Clamp(p, 0, 100));
        }

        var r = await ProcessRunner.RunAsync(file, args, Handle, enc, ct);
        return Interpret(step, r);
    }

    static RepairOutcome Interpret(RepairStep step, ProcessResult r)
    {
        var text = r.Output;
        if (r.ExitCode == 740 || text.Contains("administrator", StringComparison.OrdinalIgnoreCase) && r.ExitCode != 0)
            return new RepairOutcome(step, false, "Needs administrator rights. Use \"Restart as administrator\".");

        switch (step)
        {
            case RepairStep.Sfc:
                if (text.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
                    return new RepairOutcome(step, true, "No integrity violations found.");
                if (text.Contains("successfully repaired", StringComparison.OrdinalIgnoreCase))
                    return new RepairOutcome(step, true, "Corrupt files were found and repaired.");
                if (text.Contains("unable to fix", StringComparison.OrdinalIgnoreCase))
                    return new RepairOutcome(step, false, "Corrupt files found that SFC could not fix. Run the DISM repair, then SFC again.");
                break;
            case RepairStep.DismScan:
                if (text.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
                    return new RepairOutcome(step, true, "No component store corruption detected.");
                if (text.Contains("repairable", StringComparison.OrdinalIgnoreCase))
                    return new RepairOutcome(step, false, "Component store is repairable. Run the DISM repair step.");
                break;
        }
        return r.ExitCode == 0
            ? new RepairOutcome(step, true, "Completed.")
            : new RepairOutcome(step, false, $"Finished with exit code {r.ExitCode}.");
    }
}

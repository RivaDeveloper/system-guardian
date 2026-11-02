namespace Guardian.Services.Tools;

public sealed class BloatApp
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string PackageFullName { get; set; } = "";
    public bool Recommended { get; set; }
    public string Subtitle => (Recommended ? "Recommended · " : "Optional · ") + Description;
}

/// <summary>
/// Removes pre-installed Store apps from a fixed, hand-picked list. Anything not on the list (the Store itself,
/// Calculator, Photos, Notepad, security and system components) is never offered, and every app removed here can be
/// reinstalled from the Microsoft Store.
/// </summary>
public static class DebloatService
{
    // Pattern matches the package name; recommended apps are the ones almost nobody uses on purpose.
    static readonly (string Pattern, string Title, string Description, bool Recommended)[] Known =
    {
        ("^Microsoft\\.BingNews", "News", "Ad-supported news feed", true),
        ("^Microsoft\\.BingSearch", "Bing search", "Web results in the Start menu search", true),
        ("^Microsoft\\.549981C3F5F10", "Cortana", "Retired voice assistant", true),
        ("^Microsoft\\.GetHelp", "Get Help", "Support app that opens a web page", true),
        ("^Microsoft\\.Getstarted", "Tips", "Windows tips and suggestions", true),
        ("^Microsoft\\.MicrosoftOfficeHub", "Microsoft 365 hub", "Mostly an advert for an Office subscription", true),
        ("^Microsoft\\.MixedReality\\.Portal", "Mixed Reality Portal", "VR headset setup, useless without a headset", true),
        ("^Microsoft\\.Microsoft3DViewer", "3D Viewer", "Rarely used 3D model viewer", true),
        ("^Microsoft\\.Print3D", "Print 3D", "3D printing app", true),
        ("^Microsoft\\.SkypeApp", "Skype", "Old consumer Skype", true),
        ("^Microsoft\\.People$", "People", "Contacts app that nothing uses", true),
        ("^Microsoft\\.WindowsFeedbackHub", "Feedback Hub", "Sends feedback to Microsoft", true),
        ("^(MicrosoftTeams|MSTeams)$", "Microsoft Teams (personal)", "Consumer chat app pinned to the taskbar", true),
        ("CandyCrush", "Candy Crush", "Sponsored game", true),
        ("Disney", "Disney+", "Sponsored streaming app", true),
        ("TikTok", "TikTok", "Sponsored app", true),
        ("Facebook", "Facebook", "Sponsored app", true),
        ("Instagram", "Instagram", "Sponsored app", true),
        ("Twitter", "Twitter", "Sponsored app", true),
        ("^Microsoft\\.BingWeather", "Weather", "Basic weather app", false),
        ("^Microsoft\\.Copilot", "Copilot", "AI assistant app", false),
        ("^Microsoft\\.MicrosoftSolitaireCollection", "Solitaire Collection", "Card games with ads", false),
        ("^Microsoft\\.YourPhone", "Phone Link", "Connects your phone; keep it if you use that", false),
        ("^Clipchamp\\.Clipchamp", "Clipchamp", "Microsoft's video editor", false),
        ("^Microsoft\\.WindowsMaps", "Maps", "Offline maps", false),
        ("^Microsoft\\.ZuneVideo", "Movies & TV", "Video player", false),
        ("^Microsoft\\.Todos", "Microsoft To Do", "Task list app", false),
        ("^Microsoft\\.PowerAutomateDesktop", "Power Automate", "Automation tool for work PCs", false),
        ("^Microsoft\\.OutlookForWindows", "Outlook (new)", "The new Outlook mail app", false),
        ("^Microsoft\\.Windows\\.DevHome", "Dev Home", "Developer dashboard", false),
        ("AmazonPrimeVideo|PrimeVideo", "Prime Video", "Sponsored streaming app", false),
        ("Netflix", "Netflix", "Streaming app; keep it if you use it", false),
    };

    public static async Task<List<BloatApp>> FindAsync()
    {
        var r = await ProcessRunner.RunPowerShellAsync(
            "Get-AppxPackage | Where-Object { -not $_.NonRemovable -and -not $_.IsFramework } | ForEach-Object { $_.Name + '|' + $_.PackageFullName }");
        var found = new List<BloatApp>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = line.IndexOf('|');
            if (i <= 0) continue;
            var name = line[..i];
            foreach (var (pattern, title, desc, rec) in Known)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                found.Add(new BloatApp { Title = title, Description = desc, Recommended = rec, PackageFullName = line[(i + 1)..] });
                break;
            }
        }
        return found.OrderByDescending(a => a.Recommended).ThenBy(a => a.Title).ToList();
    }

    public static async Task<(bool Ok, string Message)> RemoveAsync(BloatApp app)
    {
        var all = Elevation.IsElevated ? " -AllUsers" : "";
        var r = await ProcessRunner.RunPowerShellAsync(
            $"try {{ Remove-AppxPackage -Package '{app.PackageFullName.Replace("'", "''")}'{all} -ErrorAction Stop; 'OK' }} catch {{ $_.Exception.Message }}");
        var o = r.Output.Trim();
        return o.EndsWith("OK") ? (true, "") : (false, o.Length > 0 ? o : "Windows refused to remove it.");
    }
}

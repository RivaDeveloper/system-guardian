using System.Collections.ObjectModel;
using System.Diagnostics;
using Guardian.Services;
using Guardian.Services.Deep;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Guardian.Pages;

public sealed partial class ScanPage : Page
{
    readonly DefenderService _defender = new();
    readonly VirusTotalService _vt = new();
    CancellationTokenSource? _cts;
    CancellationTokenSource? _vtCts;

    public ObservableCollection<ThreatInfo> Threats { get; } = new();
    public ObservableCollection<VtResult> VtResults { get; } = new();
    public ObservableCollection<Finding> Findings { get; } = new();

    public ScanPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required; // keep a running scan alive while browsing other tabs
        Overlay.CancelRequested += () => _cts?.Cancel();
        ScanMemory.IsChecked = SettingsStore.Load().ScanProcessMemory;
        ShowDetails(null);
    }

    void ScanMemory_Changed(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStore.Load();
        settings.ScanProcessMemory = ScanMemory.IsChecked == true;
        SettingsStore.Save(settings);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadThreatsAsync();

    // =====================================================================
    //  Deep scan
    // =====================================================================

    enum GuardianScan { Quick, Folder, Deep }

    async void QuickStart_Click(object sender, RoutedEventArgs e) => await RunGuardianScanAsync(GuardianScan.Quick, null);

    async void DeepStart_Click(object sender, RoutedEventArgs e) => await RunGuardianScanAsync(GuardianScan.Deep, null);

    async void FolderScan_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Ui.PickFolderAsync();
        if (folder != null) await RunGuardianScanAsync(GuardianScan.Folder, folder);
    }

    void SetGuardianButtons(bool enabled) => DeepButton.IsEnabled = QuickScanButton.IsEnabled = FolderScanButton.IsEnabled = enabled;

    /// <summary>Runs Guardian's own engine in one of its three shapes and lists what it found.</summary>
    async Task RunGuardianScanAsync(GuardianScan kind, string? folder)
    {
        _cts = new CancellationTokenSource();
        SetGuardianButtons(false);
        DeepSummary.Text = "";
        Findings.Clear();
        ShowDetails(null);

        var started = DateTime.Now;
        var scanner = new DeepScanner { ScanProcessMemory = kind == GuardianScan.Deep && ScanMemory.IsChecked == true };
        long items = 0;
        List<Finding>? results = null;
        var defenderNote = "";
        var label = kind switch
        {
            GuardianScan.Quick => "Quick scan",
            GuardianScan.Folder => "Folder scan",
            _ => "Deep scan",
        };
        var stages = kind switch
        {
            GuardianScan.Quick => DeepScanner.QuickStageNames,
            GuardianScan.Folder => DeepScanner.FolderStageNames,
            _ => DeepScanner.StageNames,
        };

        Overlay.Begin(label, stages, percentKnown: true);
        try
        {
            var progress = new Progress<ScanProgress>(p => { items = p.ItemsChecked; Overlay.Update(p); });
            results = kind switch
            {
                GuardianScan.Quick => await scanner.RunQuickAsync(progress, _cts.Token),
                GuardianScan.Folder => await scanner.ScanFolderAsync(folder!, progress, _cts.Token),
                _ => await scanner.RunAsync(progress, _cts.Token),
            };

            if (kind == GuardianScan.Deep && IncludeDefender.IsChecked == true && _defenderUsable)
            {
                Overlay.Begin("Windows Defender quick scan", new[] { "Quick scan" }, percentKnown: false,
                    note: "Defender doesn't report a percentage while it scans, so Guardian shows how long it has been running. A quick scan usually takes a few minutes.");
                var r = await _defender.ScanAsync(DefenderScanType.Quick, null, _cts.Token);
                defenderNote = r.ExitCode == 0 ? " Defender quick scan finished." : " Defender quick scan reported an error.";
                await LoadThreatsAsync();
            }
        }
        catch (OperationCanceledException)
        {
            DeepSummary.Text = "Scan cancelled.";
            results = null;
        }
        finally
        {
            Overlay.End();
            SetGuardianButtons(true);
        }

        if (results is null) return;

        var ignored = SettingsStore.Load().Ignored;
        _all = results.Where(f => !ignored.Contains(f.Key)).ToList();
        _lastItems = items;
        _lastElapsed = DateTime.Now - started;
        _scanLabel = kind == GuardianScan.Folder ? $"Folder scan of {folder}" : label;
        _defenderNote = defenderNote;
        ApplyFilter();
    }

    string _scanLabel = "Scan";

    List<Finding> _all = new();
    long _lastItems;
    TimeSpan _lastElapsed;
    string _defenderNote = "";

    void ShowLow_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    /// <summary>Medium and above lead the list; low-risk and informational items are behind a toggle (except the "limited checks" note).</summary>
    void ApplyFilter()
    {
        var showLow = ShowLow.IsChecked == true;
        var visible = _all.Where(f => showLow || f.Severity >= Severity.Medium || f.Key == "not-elevated").ToList();

        Findings.Clear();
        foreach (var f in visible) Findings.Add(f);
        ShowDetails(null);

        if (_all.Count == 0 && _lastItems == 0) return;   // nothing scanned yet

        var attention = _all.Count(f => f.Severity >= Severity.Medium);
        var hidden = _all.Count - visible.Count;
        var worst = _all.Count(f => f.Severity >= Severity.High);
        var checkedText = $"{_scanLabel} checked {_lastItems:N0} items in {_lastElapsed:mm\\:ss}.";
        DeepSummary.Text = attention == 0
            ? $"Nothing suspicious found. {checkedText}{(hidden > 0 ? $" {hidden} low-risk notes are hidden." : "")}{_defenderNote}"
            : $"{checkedText} {attention} item{(attention == 1 ? "" : "s")} worth a look{(worst > 0 ? $", {worst} of them high priority" : "")}.{(hidden > 0 ? $" {hidden} low-risk notes are hidden." : "")}{_defenderNote}";
        DeepSummary.Foreground = attention == 0 ? Tones.Good : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["InkBrush"];
        if (Findings.Count > 0) FindingsList.SelectedIndex = 0;
    }

    Finding? Selected => FindingsList.SelectedItem as Finding;

    void Finding_Selected(object sender, SelectionChangedEventArgs e) => ShowDetails(Selected);

    void ShowDetails(Finding? f)
    {
        DetailsEmpty.Visibility = f is null ? Visibility.Visible : Visibility.Collapsed;
        DetailsScroll.Visibility = f is null ? Visibility.Collapsed : Visibility.Visible;
        if (f is null) return;

        DetailSeverity.Text = $"{f.SeverityText}  ·  {f.KindText.ToUpperInvariant()}";
        DetailSeverity.Foreground = f.SeverityBrush;
        DetailTitle.Text = f.Title;
        DetailReasons.Text = f.Reasons.Count > 0 ? f.ReasonsText : "No further detail.";
        DetailAdvice.Text = f.Recommendation.Length > 0 ? f.Recommendation : "Nothing to do unless you didn't expect this.";

        var info = new List<string>();
        if (f.Detail.Length > 0) info.Add(f.Detail);
        if (f.FilePath.Length > 0 && f.Detail != f.FilePath) info.Add(f.FilePath);
        if (f.Signer.Length > 0) info.Add("Signed by: " + f.Signer);
        if (f.Sha256.Length > 0) info.Add("SHA-256: " + f.Sha256);
        DetailInfo.Text = string.Join("\n", info);

        var canMove = f.HasFile && f.Kind != FindingKind.Security;
        QuarantineButton.Visibility = canMove ? Visibility.Visible : Visibility.Collapsed;
        ShowButton.Visibility = f.HasFile ? Visibility.Visible : Visibility.Collapsed;
        CheckVtButton.Visibility = f.HasFile ? Visibility.Visible : Visibility.Collapsed;
        EndButton.Visibility = f.Pid > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { HasFile: true } f)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{f.FilePath}\"") { UseShellExecute = true });
    }

    async void QuarantineFile_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { HasFile: true } f) return;
        if (!await Ui.ConfirmAsync(XamlRoot, "Quarantine this file?",
                $"{f.FilePath}\n\nGuardian moves it into a locked folder and renames it so it can't run. Nothing is deleted, and you can restore it from the Quarantine button.",
                "Quarantine"))
            return;
        try
        {
            Quarantine.Add(f.FilePath, f.Sha256, f.Title);
            Findings.Remove(f);
            DeepSummary.Text = $"Quarantined {Path.GetFileName(f.FilePath)}. It can be restored from the Quarantine button.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Ui.ShowAsync(XamlRoot, "Couldn't quarantine it",
                ex is IOException ? "The file is in use. End the process first (if it is running), then try again." : ex.Message);
        }
    }

    async void EndProcess_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { Pid: > 0 } f) return;
        if (!await Ui.ConfirmAsync(XamlRoot, "End this process?",
                $"{Path.GetFileName(f.FilePath)} (PID {f.Pid}) will be closed immediately. Unsaved work in it would be lost.", "End process"))
            return;
        try
        {
            using var p = Process.GetProcessById(f.Pid);
            // Process IDs get reused: make sure it's still the same program before ending anything.
            if (f.FilePath.Length > 0 && !string.Equals(p.MainModule?.FileName, f.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                await Ui.ShowAsync(XamlRoot, "Not the same program", "That process ID now belongs to a different program, so Guardian left it alone.");
                return;
            }
            p.Kill(entireProcessTree: true);
            DeepSummary.Text = $"Ended {p.ProcessName}.";
        }
        catch (Exception ex)
        {
            await Ui.ShowAsync(XamlRoot, "Couldn't end it", ex.Message + (Elevation.IsElevated ? "" : "\n\nTry restarting Guardian as administrator."));
        }
    }

    async void CheckVt_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { HasFile: true } f) return;
        CheckVtButton.IsEnabled = false;
        try
        {
            var r = await VirusTotalService.CheckOneAsync(f.FilePath);
            await Ui.ShowAsync(XamlRoot, "VirusTotal result",
                $"{Path.GetFileName(f.FilePath)}\n\n{r.VerdictText}\n\nOnly the file's SHA-256 hash was sent. The file itself was not uploaded.");
        }
        catch (Exception ex) { await Ui.ShowAsync(XamlRoot, "VirusTotal", ex.Message); }
        finally { CheckVtButton.IsEnabled = true; }
    }

    void Ignore_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } f) return;
        var settings = SettingsStore.Load();
        if (!settings.Ignored.Contains(f.Key)) settings.Ignored.Add(f.Key);
        SettingsStore.Save(settings);
        Findings.Remove(f);
        DeepSummary.Text = "Ignored. Guardian won't show this one again.";
    }

    async void ShowQuarantine_Click(object sender, RoutedEventArgs e)
    {
        var entries = Quarantine.List();
        if (entries.Count == 0)
        {
            await Ui.ShowAsync(XamlRoot, "Quarantine", "Nothing is in quarantine.");
            return;
        }

        var list = new ListView { ItemsSource = entries, SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Padding='0,6'><TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold'/><TextBlock Text='{Binding Summary}' Opacity='0.7' FontSize='12' TextWrapping='Wrap'/></StackPanel></DataTemplate>");
        list.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "Quarantine", Content = list,
            PrimaryButtonText = "Restore", SecondaryButtonText = "Delete forever", CloseButtonText = "Close",
        };
        var result = await dialog.ShowAsync();
        if (list.SelectedItem is not QuarantineEntry entry) return;

        try
        {
            if (result == ContentDialogResult.Primary)
            {
                Quarantine.Restore(entry);
                DeepSummary.Text = $"Restored {entry.DisplayName} to its original place.";
            }
            else if (result == ContentDialogResult.Secondary &&
                     await Ui.ConfirmAsync(XamlRoot, "Delete permanently?", $"{entry.DisplayName} will be deleted for good. This can't be undone.", "Delete"))
            {
                Quarantine.DeleteForever(entry);
                DeepSummary.Text = $"Deleted {entry.DisplayName}.";
            }
        }
        catch (Exception ex) { await Ui.ShowAsync(XamlRoot, "Quarantine", ex.Message); }
    }

    // =====================================================================
    //  Windows Defender
    // =====================================================================

    bool _defenderUsable = true;

    /// <summary>Guardian's own scan needs nothing from Defender. Without it the Defender options switch off instead of failing.</summary>
    async Task CheckDefenderAsync()
    {
        DefenderStatus? s = null;
        try { s = await _defender.GetStatusAsync(); } catch { /* cmdlets missing: no Defender */ }
        _defenderUsable = s is { Usable: true };
        IncludeDefender.IsEnabled = _defenderUsable;
        if (!_defenderUsable) IncludeDefender.IsChecked = false;
        SetScanning(false);
        if (!_defenderUsable)
            StatusText.Text = "Windows Defender isn't running on this PC. That's fine: Guardian's deep scan and real-time protection work without it.";
    }

    async Task LoadThreatsAsync()
    {
        await CheckDefenderAsync();
        if (!_defenderUsable) return;
        try
        {
            var threats = await _defender.GetThreatsAsync();
            Threats.Clear();
            foreach (var t in threats) Threats.Add(t);
            RemoveButton.IsEnabled = threats.Any(t => t.Active);
        }
        catch { /* Defender not available */ }
    }

    void SetScanning(bool scanning)
    {
        QuickButton.IsEnabled = FullButton.IsEnabled = FolderButton.IsEnabled = !scanning && _defenderUsable;
        SetGuardianButtons(!scanning);
    }

    async Task RunScanAsync(DefenderScanType type, string? path)
    {
        _cts = new CancellationTokenSource();
        SetScanning(true);
        var name = type switch { DefenderScanType.Quick => "quick scan", DefenderScanType.Full => "full scan", _ => "folder scan" };
        Overlay.Begin($"Windows Defender {name}", new[] { "Scanning" }, percentKnown: false,
            note: "Defender doesn't report a percentage while it scans, so Guardian shows how long it has been running. "
                + (type == DefenderScanType.Full ? "A full scan can take an hour or more." : "This usually takes a few minutes.")
                + " You can cancel at any time.");
        try
        {
            var r = await _defender.ScanAsync(type, path, _cts.Token);
            StatusText.Text = r.ExitCode == 0 ? "Scan finished." : $"Scan ended with an error: {r.Error.Trim()}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
        }
        finally
        {
            Overlay.End();
            SetScanning(false);
        }
        await LoadThreatsAsync();
        if (Threats.Any(t => t.Active))
            StatusText.Text += $" {Threats.Count(t => t.Active)} active threat(s) found.";
    }

    async void Quick_Click(object sender, RoutedEventArgs e) => await RunScanAsync(DefenderScanType.Quick, null);

    async void Full_Click(object sender, RoutedEventArgs e) => await RunScanAsync(DefenderScanType.Full, null);

    async void Folder_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Ui.PickFolderAsync();
        if (folder != null) await RunScanAsync(DefenderScanType.Custom, folder);
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var names = string.Join("\n", Threats.Where(t => t.Active).Select(t => "• " + t.Name));
        if (!await Ui.ConfirmAsync(XamlRoot, "Remove active threats?",
                $"Windows Defender will quarantine or delete:\n\n{names}\n\nReview them first. Anything Defender removes may not be recoverable.",
                "Remove"))
            return;

        StatusText.Text = "Removing…";
        var ok = await _defender.RemoveActiveThreatsAsync();
        StatusText.Text = ok ? "Removal finished." : "Removal failed. Try running as administrator.";
        await LoadThreatsAsync();
    }

    // =====================================================================
    //  VirusTotal folder check
    // =====================================================================

    async void VtFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ApiKeyStore.Load()))
        {
            await Ui.ShowAsync(XamlRoot, "API key needed", "Add your VirusTotal API key under Settings first.");
            return;
        }
        var folder = await Ui.PickFolderAsync();
        if (folder == null) return;

        VtResults.Clear();
        _vtCts = new CancellationTokenSource();
        VtFolderButton.IsEnabled = false;
        VtCancelButton.IsEnabled = true;
        VtBusy.IsActive = true;
        try
        {
            VtStatus.Text = "Looking for executables…";
            var files = await Task.Run(() => VirusTotalService.FindCandidates(folder, 40));
            if (files.Count == 0) { VtStatus.Text = "No executable files found."; return; }

            var done = 0;
            await foreach (var result in _vt.ScanAsync(files, _vtCts.Token))
            {
                VtResults.Add(result);
                VtStatus.Text = $"Checked {++done} of {files.Count}…";
            }
            var flagged = VtResults.Count(r => r.Verdict is VtVerdict.Malicious or VtVerdict.Suspicious);
            VtStatus.Text = $"Done. {flagged} flagged out of {files.Count} files.";
        }
        catch (OperationCanceledException) { VtStatus.Text = "Cancelled."; }
        catch (InvalidOperationException ex) { VtStatus.Text = ex.Message; }
        finally
        {
            VtBusy.IsActive = false;
            VtFolderButton.IsEnabled = true;
            VtCancelButton.IsEnabled = false;
        }
    }

    void VtCancel_Click(object sender, RoutedEventArgs e) => _vtCts?.Cancel();
}

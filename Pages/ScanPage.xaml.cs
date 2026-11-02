using System.Collections.ObjectModel;
using Guardian.Services;
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

    public ScanPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required; // keep a running scan alive while browsing other tabs
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await LoadThreatsAsync();

    async Task LoadThreatsAsync()
    {
        var threats = await _defender.GetThreatsAsync();
        Threats.Clear();
        foreach (var t in threats) Threats.Add(t);
        RemoveButton.IsEnabled = threats.Any(t => t.Active);
    }

    void SetScanning(bool scanning)
    {
        Busy.IsActive = scanning;
        QuickButton.IsEnabled = FullButton.IsEnabled = FolderButton.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
    }

    async Task RunScanAsync(DefenderScanType type, string? path)
    {
        _cts = new CancellationTokenSource();
        SetScanning(true);
        StatusText.Text = type == DefenderScanType.Full
            ? "Full scan running. This can take a long time…"
            : "Scanning…";
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

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

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

    // ---- VirusTotal ----

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

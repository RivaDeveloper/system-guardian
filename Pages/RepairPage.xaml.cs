using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class RepairPage : Page
{
    readonly RepairService _repair = new();
    CancellationTokenSource? _cts;

    public RepairPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    void SetBusy(bool busy)
    {
        FullButton.IsEnabled = DismScanButton.IsEnabled = DismRestoreButton.IsEnabled =
            SfcButton.IsEnabled = ChkdskButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
    }

    void Log(string line) => DispatcherQueue.TryEnqueue(() =>
    {
        LogBox.Text += line + "\n";
        LogBox.SelectionStart = LogBox.Text.Length;
    });

    async Task<bool> RequireAdminAsync()
    {
        if (Elevation.IsElevated) return true;
        await Ui.ShowAsync(XamlRoot, "Administrator needed",
            "System repair needs administrator rights. Use \"Restart as administrator\" at the top of the window.");
        return false;
    }

    async Task RunStepsAsync(params RepairStep[] steps)
    {
        if (!await RequireAdminAsync()) return;
        _cts = new CancellationTokenSource();
        SetBusy(true);
        LogBox.Text = "";
        try
        {
            foreach (var step in steps)
            {
                StatusText.Text = RepairService.Title(step) + "…";
                Progress.Value = 0;
                Log($"=== {RepairService.Title(step)} ===");
                var outcome = await _repair.RunAsync(step, Log,
                    p => DispatcherQueue.TryEnqueue(() => Progress.Value = p), _cts.Token);
                Log($"--> {outcome.Summary}");
                StatusText.Text = $"{RepairService.Title(step)}: {outcome.Summary}";

                // When the scan comes back healthy there is no point running the repair that follows it.
                if (!outcome.Success && step == RepairStep.DismScan) continue;
                if (!outcome.Success) break;
            }
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        finally { SetBusy(false); }
    }

    async void Full_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ui.ConfirmAsync(XamlRoot, "Run full repair?",
                "This runs DISM (scan, then repair) followed by SFC. It can take 15–30 minutes.", "Start"))
            return;
        await RunStepsAsync(RepairStep.DismScan, RepairStep.DismRestore, RepairStep.Sfc);
    }

    async void DismScan_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(RepairStep.DismScan);
    async void DismRestore_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(RepairStep.DismRestore);
    async void Sfc_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(RepairStep.Sfc);
    async void Chkdsk_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(RepairStep.Chkdsk);

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}

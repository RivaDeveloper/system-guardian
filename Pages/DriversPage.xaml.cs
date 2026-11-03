using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Cleanup;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class DriversPage : Page
{
    readonly DriverService _drivers = new();
    readonly DriverBackup _backup = new();

    public ObservableCollection<DriverBackupInfo> Backups { get; } = new();

    public ObservableCollection<DriverInfo> Installed { get; } = new();
    public ObservableCollection<DriverUpdate> Updates { get; } = new();

    public DriversPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        foreach (var b in _backup.List()) Backups.Add(b);
    }

    // ---- manufacturer drivers ---------------------------------------------------------------------

    public ObservableCollection<DriverCheck> Checks { get; } = new();
    List<DetectedDriver> _detected = new();
    BiosInfo? _board;

    void PasteBox_TextChanged(object sender, TextChangedEventArgs e) =>
        CompareButton.IsEnabled = PasteBox.Text.Trim().Length > 0;

    async void Detect_Click(object sender, RoutedEventArgs e)
    {
        DetectButton.IsEnabled = false;
        DetectBusy.IsActive = true;
        DetectStatus.Text = "Looking at installed drivers…";
        try
        {
            _detected = await ManufacturerDrivers.DetectAsync();
            Checks.Clear();
            foreach (var d in _detected)
                Checks.Add(new DriverCheck { Category = d.Category, Title = d.Name, Installed = d.Version + (d.Date.Length > 0 ? $" ({d.Date})" : ""), State = DriverState.Detected });
            GpuCpuButton.IsEnabled = _detected.Any(d => d.Category.StartsWith("Graphics") || d.Category == "Processor");
            DetectStatus.Text = $"{_detected.Count} driver groups detected. Now open the maker's page, copy its table and paste it above to compare.";
        }
        catch (Exception ex) { DetectStatus.Text = ex.Message; }
        finally { DetectButton.IsEnabled = true; DetectBusy.IsActive = false; }
    }

    void GpuCpuPages_Click(object sender, RoutedEventArgs e)
    {
        var pages = ManufacturerDrivers.VendorPages(_detected);
        foreach (var url in pages) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        DetectStatus.Text = pages.Count == 0 ? "Guardian doesn't recognise the GPU or CPU maker." :
            "Opened the official driver page(s). Copy the newest driver version from there and type it into the GPU box to compare.";
    }

    void GpuLatestBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) CompareGpu_Click(sender, e);
    }

    void CompareGpu_Click(object sender, RoutedEventArgs e)
    {
        var gpu = _detected.FirstOrDefault(d => d.Category == "Graphics" && d.Comparable) ?? _detected.FirstOrDefault(d => d.Category == "Graphics");
        if (gpu == null) { DetectStatus.Text = "Press \"Detect my drivers\" first."; return; }
        var m = System.Text.RegularExpressions.Regex.Match(GpuLatestBox.Text, @"\d+(\.\d+)+");
        if (!m.Success) { DetectStatus.Text = "Type a version number such as 566.36 or 25.10.42."; return; }
        if (!gpu.Comparable) { DetectStatus.Text = $"Windows shows {gpu.Name} as {gpu.Version}, which isn't the number on the vendor's page. Install the vendor's software (for example AMD Software) so Guardian can read its version, or compare by eye."; return; }
        var c = ManufacturerDrivers.CompareDotted(gpu.Version, m.Value);
        DetectStatus.Text = c > 0 ? $"A newer {gpu.Vendor} graphics driver is available: {m.Value} (you have {gpu.Version})." :
            c == 0 ? $"Your {gpu.Vendor} graphics driver {gpu.Version} is the version you entered." :
            $"You have {gpu.Version}, which is newer than {m.Value}.";
    }

    async void VendorPage_Click(object sender, RoutedEventArgs e)
    {
        _board ??= await BiosService.ReadAsync();
        var url = BiosService.SupportSearchUrl(_board, "driver");
        if (url == null)
        {
            DetectStatus.Text = "Guardian doesn't recognise this manufacturer. Open their support site and find your model's driver page yourself.";
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        DetectStatus.Text = $"Opened a search for {_board.SearchModel} on the manufacturer's site. Open your model's Support page, Driver tab, set OS to All, then Ctrl+A and Ctrl+C.";
    }

    async void CompareDrivers_Click(object sender, RoutedEventArgs e)
    {
        if (_detected.Count == 0) _detected = await ManufacturerDrivers.DetectAsync();
        var entries = ManufacturerDrivers.ParsePaste(PasteBox.Text);
        if (entries.Count == 0)
        {
            DetectStatus.Text = "Guardian couldn't find any driver rows in that text. Copy the whole page (Ctrl+A, Ctrl+C) from the Driver tab and paste again.";
            return;
        }
        Checks.Clear();
        var results = ManufacturerDrivers.Compare(_detected, entries);
        foreach (var c in results) Checks.Add(c);
        var newer = results.Count(c => c.State == DriverState.Newer);
        DetectStatus.Text = $"Read {entries.Count} rows from the page. " + (newer == 0 ? "Nothing newer found for the drivers Guardian can compare." : $"{newer} driver(s) have a newer version on the site.");
    }

    void BackupsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RestoreDriversButton.IsEnabled = BackupsList.SelectedItem != null;

    async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!Elevation.IsElevated)
        {
            await Ui.ShowAsync(XamlRoot, "Administrator needed", "Restart Guardian as administrator to back up drivers.");
            return;
        }
        BackupButton.IsEnabled = false;
        BackupBusy.IsActive = true;
        BackupStatus.Text = "Exporting drivers… this can take a few minutes.";
        try
        {
            var info = await _backup.BackupAsync();
            Backups.Insert(0, info);
            BackupStatus.Text = $"Saved {info.Packages} driver packages.";
        }
        catch (Exception ex) { BackupStatus.Text = ex.Message; }
        finally { BackupButton.IsEnabled = true; BackupBusy.IsActive = false; }
    }

    async void RestoreDrivers_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not DriverBackupInfo b) return;
        if (!Elevation.IsElevated)
        {
            await Ui.ShowAsync(XamlRoot, "Administrator needed", "Restart Guardian as administrator to restore drivers.");
            return;
        }
        if (!await Ui.ConfirmAsync(XamlRoot, "Restore drivers?",
                $"Reinstall the {b.Packages} driver packages saved on {b.Created:g}? Only matching devices are changed.", "Restore"))
            return;
        BackupBusy.IsActive = true;
        BackupStatus.Text = "Restoring drivers…";
        try
        {
            var r = await _backup.RestoreAsync(b);
            BackupStatus.Text = r.ExitCode == 0 ? "Drivers restored." :
                r.ExitCode == 3010 ? "Drivers restored. Restart Windows to finish." : "Finished with warnings; some packages may not apply to this PC.";
        }
        finally { BackupBusy.IsActive = false; }
    }

    void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(DriverBackup.Root);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DriverBackup.Root) { UseShellExecute = true });
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanBusy.IsActive = true;
        ScanStatus.Text = "Reading installed drivers…";
        try
        {
            var list = await _drivers.GetInstalledAsync();
            Installed.Clear();
            foreach (var d in list) Installed.Add(d);
            var old = list.Count(d => d.IsOld);
            ScanStatus.Text = $"{list.Count} drivers found, {old} older than 3 years (non-Microsoft). " +
                              "Age alone is only a hint; use the Available updates tab for the authoritative check.";
        }
        finally
        {
            ScanButton.IsEnabled = true;
            ScanBusy.IsActive = false;
        }
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateBusy.IsActive = true;
        UpdateStatus.Text = "Asking Windows Update… this can take a minute.";
        try
        {
            var list = await _drivers.GetAvailableUpdatesAsync();
            Updates.Clear();
            foreach (var u in list) Updates.Add(u);
            UpdateStatus.Text = list.Count == 0
                ? "Windows Update has no newer drivers for this PC."
                : $"{list.Count} driver update(s) available. Select the ones to install.";
        }
        finally
        {
            CheckButton.IsEnabled = true;
            UpdateBusy.IsActive = false;
        }
    }

    void UpdatesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        InstallButton.IsEnabled = UpdatesList.SelectedItems.Count > 0;

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        var selected = UpdatesList.SelectedItems.Cast<DriverUpdate>().ToList();
        if (!Elevation.IsElevated)
        {
            await Ui.ShowAsync(XamlRoot, "Administrator needed", "Restart Guardian as administrator to install drivers.");
            return;
        }
        if (!await Ui.ConfirmAsync(XamlRoot, "Install driver updates?",
                $"Install {selected.Count} driver update(s)? Guardian tries to create a restore point first.", "Install"))
            return;

        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        UpdateBusy.IsActive = true;
        var reboot = false;
        try
        {
            var n = 0;
            foreach (var u in selected)
            {
                UpdateStatus.Text = $"Installing {++n} of {selected.Count}: {u.Title}";
                var outcome = await _drivers.InstallAsync(u.Title);
                reboot |= outcome.RebootRequired;
                if (outcome.Success) Updates.Remove(u);
                else
                {
                    UpdateStatus.Text = $"{u.Title}: {outcome.Message}";
                    return;
                }
            }
            UpdateStatus.Text = reboot ? "Done. Restart Windows to finish installing." : "Done.";
        }
        finally
        {
            UpdateBusy.IsActive = false;
            CheckButton.IsEnabled = true;
        }
    }
}

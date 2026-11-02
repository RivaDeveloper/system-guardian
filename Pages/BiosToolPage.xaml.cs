using Guardian.Services;
using Guardian.Services.Cleanup;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class BiosToolPage : Page
{
    BiosInfo? _info;
    string? _bitlocker;

    public BiosToolPage()
    {
        InitializeComponent();
        SuspendButton.IsEnabled = RestoreButton.IsEnabled = Elevation.IsElevated;
        _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        InfoBusy.IsActive = true;
        try
        {
            _info = await BiosService.ReadAsync();
            _bitlocker = await BiosService.BitLockerAsync();
            var i = _info;
            InfoText.Text =
                $"BIOS: {i.BiosVendor} {i.BiosVersion}, dated {i.BiosDate}\n" +
                $"Board: {i.BoardVendor} {i.BoardModel}\n" +
                $"System: {i.SystemVendor} {i.SystemModel}\n" +
                $"Firmware: {(i.Uefi ? "UEFI" : "Legacy BIOS")}, Secure Boot {(i.SecureBoot ? "on" : "off")}";
            SaveInfoButton.IsEnabled = true;
            VendorButton.IsEnabled = BiosService.SupportSearchUrl(i) != null;
            if (!VendorButton.IsEnabled)
                FindStatus.Text = "Guardian doesn't recognise this manufacturer, so open their support site yourself and search for your model.";
            ShowChecks();
        }
        catch (Exception ex) { InfoText.Text = "Couldn't read the BIOS information: " + ex.Message; }
        finally { InfoBusy.IsActive = false; }
    }

    void ShowChecks()
    {
        if (_info == null) return;
        if (_info.HasBattery)
        {
            var ok = _info.OnAc && _info.BatteryPercent >= 50;
            PowerStatus.Text = ok ? $"✔ Laptop on mains power, battery {_info.BatteryPercent}%." :
                !_info.OnAc ? "✖ Laptop is running on battery. Plug in the charger before flashing." :
                $"✖ Battery is only at {_info.BatteryPercent}%. Charge it above 50% first.";
            PowerStatus.Foreground = ok ? Tones.Good : Tones.Bad;
        }
        else
        {
            PowerStatus.Text = "• Desktop PC: a power cut during the flash can ruin the BIOS. Avoid flashing in bad weather, and use a UPS if you have one.";
            PowerStatus.Foreground = Tones.Warn;
        }

        if (_bitlocker == null)
        {
            BitLockerStatus.Text = Elevation.IsElevated
                ? "• BitLocker status couldn't be read."
                : "• BitLocker status needs administrator rights. Restart Guardian as administrator to check it.";
            BitLockerStatus.Foreground = Tones.Warn;
        }
        else if (_bitlocker == "On")
        {
            BitLockerStatus.Text = "⚠ BitLocker is on. A firmware change can make Windows ask for your recovery key. Make sure you have the key, or pause BitLocker for one restart below.";
            BitLockerStatus.Foreground = Tones.Warn;
        }
        else
        {
            BitLockerStatus.Text = "✔ BitLocker is off on the system drive, so no recovery key will be needed.";
            BitLockerStatus.Foreground = Tones.Good;
        }
        SuspendButton.IsEnabled = Elevation.IsElevated && _bitlocker == "On";
    }

    async void Recheck_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    async void SaveInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Guardian BIOS information");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"bios-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(file, BiosService.BuildReport(_info, _bitlocker));
        SaveStatus.Text = $"Saved to {file}";
    }

    void Vendor_Click(object sender, RoutedEventArgs e)
    {
        var url = _info == null ? null : BiosService.SupportSearchUrl(_info);
        if (url != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    async void Wu_Click(object sender, RoutedEventArgs e)
    {
        WuButton.IsEnabled = false;
        WuBusy.IsActive = true;
        FindStatus.Text = "Asking Windows Update… this can take a minute.";
        try
        {
            var list = await new DriverService().GetAvailableUpdatesAsync();
            var fw = list.Where(u => u.Title.Contains("firmware", StringComparison.OrdinalIgnoreCase)
                                  || u.Title.Contains("BIOS", StringComparison.OrdinalIgnoreCase)
                                  || u.Title.Contains("UEFI", StringComparison.OrdinalIgnoreCase)).ToList();
            FindStatus.Text = fw.Count == 0
                ? "Windows Update has no firmware for this PC. That's normal for most desktop boards: their BIOS updates come only from the manufacturer."
                : "Windows Update offers: " + string.Join("; ", fw.Select(u => u.Title)) + ". Install it from Drivers, Available updates; Windows handles it safely and restarts to apply it.";
        }
        catch (Exception ex) { FindStatus.Text = ex.Message; }
        finally { WuButton.IsEnabled = true; WuBusy.IsActive = false; }
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        RestoreButton.IsEnabled = false;
        ActionStatus.Text = "Creating a restore point…";
        var (_, message) = await BiosService.CreateRestorePointAsync();
        ActionStatus.Text = message;
        RestoreButton.IsEnabled = Elevation.IsElevated;
    }

    async void Suspend_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ui.ConfirmAsync(XamlRoot, "Pause BitLocker for one restart?",
                "BitLocker stops checking the PC's startup state for the next restart only, then turns itself back on. Do this right before you flash, then restart once to flash and once more to confirm it's back on.", "Pause"))
            return;
        var (_, message) = await BiosService.SuspendBitLockerOnceAsync();
        ActionStatus.Text = message;
        _bitlocker = await BiosService.BitLockerAsync();
        ShowChecks();
    }

    async void CheckFile_Click(object sender, RoutedEventArgs e)
    {
        var path = await Ui.PickFileAsync("Choose the downloaded BIOS file");
        if (path == null) return;
        FileBusy.IsActive = true;
        FileResult.Text = "Checking…";
        try
        {
            var r = await BiosService.CheckFileAsync(path);
            FileResult.Text = $"{Path.GetFileName(path)} · {JunkCategory.Format(r.Size)}\nSHA-256: {r.Sha256}\n{r.SignatureNote}";
        }
        catch (Exception ex) { FileResult.Text = ex.Message; }
        finally { FileBusy.IsActive = false; }
    }
}

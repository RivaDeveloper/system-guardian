using System.Collections.ObjectModel;
using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class DriversPage : Page
{
    readonly DriverService _drivers = new();

    public ObservableCollection<DriverInfo> Installed { get; } = new();
    public ObservableCollection<DriverUpdate> Updates { get; } = new();

    public DriversPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
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

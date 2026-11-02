using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class OverviewPage : Page
{
    readonly DefenderService _defender = new();

    public OverviewPage() => InitializeComponent();

    async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    async Task RefreshAsync()
    {
        AdminText.Text = Elevation.IsElevated
            ? "Administrator: yes"
            : "Administrator: no (restart as administrator for drivers and repairs)";
        Busy.IsActive = true;
        try
        {
            var s = await _defender.GetStatusAsync();
            if (s is null)
            {
                DefenderText.Text = "Windows Defender: status unavailable (another antivirus may be managing protection).";
                SignatureText.Text = "";
                return;
            }
            DefenderText.Text = $"Windows Defender: {(s.Enabled ? "on" : "OFF")}, real-time protection {(s.RealTime ? "on" : "OFF")}";
            SignatureText.Text = $"Virus definitions: {s.SignatureVersion} (updated {s.SignaturesUpdated})";
        }
        finally { Busy.IsActive = false; }
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        Busy.IsActive = true;
        UpdateButton.IsEnabled = false;
        var ok = await _defender.UpdateSignaturesAsync();
        UpdateButton.IsEnabled = true;
        Busy.IsActive = false;
        if (!ok) await Ui.ShowAsync(XamlRoot, "Update failed", "Could not update virus definitions. Try running as administrator.");
        await RefreshAsync();
    }
}

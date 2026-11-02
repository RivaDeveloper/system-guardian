using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class OverviewPage : Page
{
    readonly DefenderService _defender = new();

    public OverviewPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    void Shortcut_Click(object sender, RoutedEventArgs e) =>
        App.MainWindow.NavigateTo((string)((Button)sender).Tag);

    async Task RefreshAsync()
    {
        var admin = Elevation.IsElevated;
        AdminValue.Text = admin ? "Administrator" : "Standard";
        AdminDot.Fill = admin ? Tones.Good : Tones.Warn;
        AdminDetail.Text = admin ? "Drivers and repairs are unlocked." : "Restart as administrator for drivers and repairs.";

        Busy.IsActive = true;
        try
        {
            var s = await _defender.GetStatusAsync();
            if (s is null)
            {
                ProtectionValue.Text = "Unknown";
                ProtectionDot.Fill = Tones.Neutral;
                ProtectionDetail.Text = "Another antivirus may be managing protection.";
                DefinitionsValue.Text = "—";
                DefinitionsDetail.Text = "";
                return;
            }
            var healthy = s.Enabled && s.RealTime;
            ProtectionValue.Text = healthy ? "Protected" : "At risk";
            ProtectionDot.Fill = healthy ? Tones.Good : Tones.Bad;
            ProtectionDetail.Text = $"Defender {(s.Enabled ? "on" : "OFF")}, real-time protection {(s.RealTime ? "on" : "OFF")}.";
            DefinitionsValue.Text = s.SignatureVersion;
            DefinitionsDetail.Text = $"Updated {s.SignaturesUpdated}";
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

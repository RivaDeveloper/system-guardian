using Guardian.Services;
using Guardian.Services.Deep;
using Guardian.Services.Protection;
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
            // Guardian protects on its own: Defender is a bonus when it is running, never a requirement.
            var s = await _defender.GetStatusAsync();
            var defenderOn = s is { Usable: true };
            var guardianOn = ProtectionService.Running;

            if (guardianOn || (defenderOn && s!.RealTime))
            {
                ProtectionValue.Text = "Protected";
                ProtectionDot.Fill = Tones.Good;
            }
            else
            {
                ProtectionValue.Text = "At risk";
                ProtectionDot.Fill = Tones.Bad;
            }
            var own = guardianOn ? "Guardian real-time protection on" : "Guardian real-time protection OFF";
            ProtectionDetail.Text = defenderOn
                ? $"{own}. Windows Defender {(s!.RealTime ? "on" : "real-time OFF")}."
                : $"{own}. Windows Defender isn't running, and Guardian doesn't need it.";

            var meta = IntelMeta.Load();
            if (defenderOn)
            {
                DefinitionsValue.Text = s!.SignatureVersion;
                DefinitionsDetail.Text = $"Defender updated {s.SignaturesUpdated}. Guardian: {ThreatIntel.Count:N0} fingerprints, {meta.RuleCount:N0} rules.";
            }
            else
            {
                DefinitionsValue.Text = ThreatIntel.Count > 0 ? $"{ThreatIntel.Count:N0}" : "None yet";
                DefinitionsDetail.Text = meta.HashesUpdatedUtc is { } h
                    ? $"Known-bad fingerprints, plus {meta.RuleCount:N0} rules. Updated {h.ToLocalTime():d MMM yyyy}."
                    : "Update Guardian's definitions to download the known-bad lists and signature rules.";
            }
        }
        finally { Busy.IsActive = false; }
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        Busy.IsActive = true;
        UpdateButton.IsEnabled = false;
        var failed = new List<string>();
        try
        {
            // Guardian's own definitions first: they are what protects the PC when Defender isn't there.
            var progress = new Progress<string>(_ => { });
            try { await ThreatIntel.UpdateAsync(progress, false, CancellationToken.None); } catch (Exception ex) { failed.Add("Guardian's lists: " + ex.Message); }
            try { await YaraEngine.UpdateRulesAsync(progress, false, CancellationToken.None); } catch (Exception ex) { failed.Add("signature rules: " + ex.Message); }
            var s = await _defender.GetStatusAsync();
            if (s is { Usable: true } && !await _defender.UpdateSignaturesAsync()) failed.Add("Windows Defender (try running as administrator)");
        }
        finally
        {
            UpdateButton.IsEnabled = true;
            Busy.IsActive = false;
        }
        if (failed.Count > 0) await Ui.ShowAsync(XamlRoot, "Some updates failed", string.Join("\n", failed));
        await RefreshAsync();
    }
}

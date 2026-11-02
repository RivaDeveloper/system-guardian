using Guardian.Services;
using Guardian.Services.Deep;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class SettingsPage : Page
{
    bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        StatusText.Text = string.IsNullOrEmpty(ApiKeyStore.Load()) ? "No key saved." : "A key is saved.";

        VersionText.Text = $"Guardian {UpdateService.CurrentVersion.ToString(3)}";
        AutoToggle.IsOn = SettingsStore.Load().AutoCheckUpdates;
        _loading = false;

        RefreshUpdateState();
        App.MainWindow.UpdateStateChanged += RefreshUpdateState;
        Unloaded += (_, _) => App.MainWindow.UpdateStateChanged -= RefreshUpdateState;
        RefreshIntelStatus();
    }

    // ---- threat intelligence ----

    void RefreshIntelStatus()
    {
        var meta = IntelMeta.Load();
        var hashes = meta.HashesUpdatedUtc is { } h
            ? $"{ThreatIntel.Count:N0} known-bad hashes (updated {h.ToLocalTime():d MMM yyyy})"
            : "no hash lists yet";
        var rules = !YaraEngine.EngineAvailable ? "YARA engine not installed with this build"
            : YaraEngine.RulesReady ? $"{meta.RuleCount:N0} YARA rules ({meta.RulesTag}, updated {meta.RulesUpdatedUtc?.ToLocalTime():d MMM yyyy})"
            : "no YARA rules yet";
        IntelStatus.Text = $"{hashes}\n{rules}";
    }

    async void IntelUpdate_Click(object sender, RoutedEventArgs e)
    {
        IntelButton.IsEnabled = false;
        IntelBusy.IsActive = true;
        var lines = new List<string>();
        var progress = new Progress<string>(m => IntelResult.Text = m);
        try
        {
            lines.Add(await ThreatIntel.UpdateAsync(progress, false, CancellationToken.None));
            lines.Add(await YaraEngine.UpdateRulesAsync(progress, false, CancellationToken.None));
            IntelResult.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            IntelResult.Text = "Couldn't update: " + ex.Message;
        }
        finally
        {
            IntelBusy.IsActive = false;
            IntelButton.IsEnabled = true;
            RefreshIntelStatus();
        }
    }

    void CustomRules_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(YaraEngine.CustomDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{YaraEngine.CustomDir}\"") { UseShellExecute = true });
    }

    async void ApplyRules_Click(object sender, RoutedEventArgs e)
    {
        ApplyRulesButton.IsEnabled = false;
        IntelBusy.IsActive = true;
        try
        {
            var error = await YaraEngine.RecompileAsync(CancellationToken.None);
            IntelResult.Text = error is null ? "Custom rules applied." : error;
        }
        finally
        {
            IntelBusy.IsActive = false;
            ApplyRulesButton.IsEnabled = true;
        }
    }

    void RefreshUpdateState()
    {
        var pending = App.MainWindow.PendingUpdate;
        InstallButton.Visibility = pending is null ? Visibility.Collapsed : Visibility.Visible;
        if (pending != null) UpdateStatus.Text = $"Version {pending.Version.ToString(3)} is available.";
    }

    void AutoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var settings = SettingsStore.Load();
        settings.AutoCheckUpdates = AutoToggle.IsOn;
        SettingsStore.Save(settings);
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        CheckBusy.IsActive = true;
        UpdateStatus.Text = "Checking…";
        UpdateStatus.Text = await App.MainWindow.CheckForUpdatesAsync();
        CheckBusy.IsActive = false;
        CheckButton.IsEnabled = true;
        RefreshUpdateState();
    }

    async void Install_Click(object sender, RoutedEventArgs e) => await App.MainWindow.InstallUpdateAsync();

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Password)) { StatusText.Text = "Paste a key first."; return; }
        ApiKeyStore.Save(KeyBox.Password);
        KeyBox.Password = "";
        StatusText.Text = "Key saved.";
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyStore.Clear();
        StatusText.Text = "Key removed.";
    }
}

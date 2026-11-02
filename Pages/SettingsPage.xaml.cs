using Guardian.Services;
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

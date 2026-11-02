using Guardian.Pages;
using Guardian.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Guardian;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(1240, 820));
        Title = Elevation.IsElevated ? "Guardian (administrator)" : "Guardian";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "guardian.ico"));

        // Draw our own title bar over the ambient background.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 238, 240, 247);

        AdminBar.IsOpen = !Elevation.IsElevated;
        // GUARDIAN_START_PAGE=scan|drivers|apps|repair|settings opens that page first (handy for screenshots).
        Nav.Loaded += (_, _) =>
        {
            var start = Environment.GetEnvironmentVariable("GUARDIAN_START_PAGE");
            if (start == "settings") Nav.SelectedItem = Nav.SettingsItem;
            else
            {
                Nav.SelectedItem = Nav.MenuItems[0];
                if (start != null) NavigateTo(start);
            }
            _ = AutoCheckForUpdatesAsync();
        };
    }

    /// <summary>Shows an unexpected error without closing the app (details go to errors.log).</summary>
    public void ShowError(Exception ex)
    {
        UpdateBar.Severity = InfoBarSeverity.Error;
        UpdateBar.Title = "Something went wrong";
        UpdateBar.Message = ex.Message + " Details were saved to %LOCALAPPDATA%\\Guardian\\errors.log.";
        UpdateProgress.Visibility = Visibility.Collapsed;
        NotesLink.Visibility = Visibility.Collapsed;
        UpdateNowButton.Visibility = Visibility.Collapsed;
        UpdateBar.IsClosable = true;
        UpdateBar.IsOpen = true;
    }

    // ---- Updates ----

    /// <summary>The release found by the last check, or null when this build is current.</summary>
    public UpdateInfo? PendingUpdate { get; private set; }

    /// <summary>Raised on the UI thread after a check finishes or an update starts or fails.</summary>
    public event Action? UpdateStateChanged;

    async Task AutoCheckForUpdatesAsync()
    {
        var settings = SettingsStore.Load();
        if (!settings.AutoCheckUpdates) return;
        if (settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(12)) return;
        await Task.Delay(TimeSpan.FromSeconds(3)); // let the first page settle before touching the network
        await CheckForUpdatesAsync();
    }

    /// <summary>Checks for a newer release and shows the banner if there is one. Returns a message for the UI.</summary>
    public async Task<string> CheckForUpdatesAsync()
    {
        var current = UpdateService.CurrentVersion.ToString(3);
        try
        {
            var info = await UpdateService.CheckAsync();
            var settings = SettingsStore.Load();
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SettingsStore.Save(settings);

            PendingUpdate = info;
            if (info is null)
            {
                UpdateBar.IsOpen = false;
                UpdateStateChanged?.Invoke();
                return $"You're up to date (version {current}).";
            }

            ShowUpdateBanner(info, current);
            UpdateStateChanged?.Invoke();
            return $"Version {info.Version.ToString(3)} is available.";
        }
        catch (Exception ex)
        {
            return $"Couldn't check for updates: {ex.Message}";
        }
    }

    void ShowUpdateBanner(UpdateInfo info, string current)
    {
        UpdateBar.Severity = InfoBarSeverity.Success;
        UpdateBar.Title = $"Guardian {info.Version.ToString(3)} is available";
        UpdateBar.Message = $"You have {current}. The update installs in a minute and Guardian reopens.";
        UpdateBar.IsClosable = true;
        UpdateNowButton.IsEnabled = true;
        UpdateProgress.Visibility = Visibility.Collapsed;
        NotesLink.Visibility = info.PageUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Uri.TryCreate(info.PageUrl, UriKind.Absolute, out var page)) NotesLink.NavigateUri = page;
        UpdateBar.IsOpen = true;
    }

    /// <summary>Downloads and verifies the pending update, then hands over to the installer and closes the app.</summary>
    public async Task InstallUpdateAsync()
    {
        if (PendingUpdate is not { } info) return;

        UpdateNowButton.IsEnabled = false;
        UpdateBar.IsClosable = false;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = $"Downloading Guardian {info.Version.ToString(3)}";
        UpdateBar.Message = "Keep the app open until the download finishes.";
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateBar.IsOpen = true;

        try
        {
            var progress = new Progress<double>(p => UpdateProgress.Value = p * 100);
            var installer = await UpdateService.DownloadAsync(info, progress);

            UpdateBar.Title = "Installing update";
            UpdateBar.Message = "Guardian will close and reopen by itself.";
            UpdateProgress.IsIndeterminate = true;
            UpdateService.LaunchInstaller(installer);
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            // Includes the user declining the Windows permission prompt.
            UpdateProgress.IsIndeterminate = false;
            ShowUpdateBanner(info, UpdateService.CurrentVersion.ToString(3));
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "Update didn't install";
            UpdateBar.Message = ex.Message;
            UpdateStateChanged?.Invoke();
        }
    }

    async void UpdateNow_Click(object sender, RoutedEventArgs e) => await InstallUpdateAsync();

    public void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if ((string)item.Tag == tag) { Nav.SelectedItem = item; return; }
    }

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItemContainer?.Tag as string) switch
            {
                "scan" => typeof(ScanPage),
                "drivers" => typeof(DriversPage),
                "apps" => typeof(AppsPage),
                "repair" => typeof(RepairPage),
                _ => typeof(OverviewPage),
            };
        ContentFrame.Navigate(page);
    }

    void Elevate_Click(object sender, RoutedEventArgs e) => Elevation.RestartElevated();
}

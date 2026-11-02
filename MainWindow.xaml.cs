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
        };
    }

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

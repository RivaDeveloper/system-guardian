using Guardian.Pages;
using Guardian.Services;
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
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1180, 780));
        Title = Elevation.IsElevated ? "Guardian (administrator)" : "Guardian";

        AdminBar.IsOpen = !Elevation.IsElevated;
        Nav.SelectedItem = Nav.MenuItems[0];
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

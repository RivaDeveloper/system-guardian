using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class StartupToolPage : Page
{
    public ObservableCollection<StartupItem> Items { get; } = new();

    public StartupToolPage()
    {
        InitializeComponent();
        Load();
    }

    void Load()
    {
        Items.Clear();
        foreach (var i in StartupService.List()) Items.Add(i);
        StatusText.Text = $"{Items.Count} startup entries.";
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    async void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        var sw = (ToggleSwitch)sender;
        if (sw.DataContext is not StartupItem item || sw.IsOn == item.IsEnabled) return;
        if (item.Machine && !Elevation.IsElevated)
        {
            await Ui.ShowAsync(XamlRoot, "Administrator needed", "This entry starts for every user. Restart Guardian as administrator to change it.");
            Load();
            return;
        }
        try
        {
            StartupService.SetEnabled(item, sw.IsOn);
            item.IsEnabled = sw.IsOn;
            StatusText.Text = $"{item.Name} will {(sw.IsOn ? "" : "no longer ")}start with Windows.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            Load();
        }
    }
}

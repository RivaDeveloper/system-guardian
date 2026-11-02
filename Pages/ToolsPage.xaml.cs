using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed class ToolTile(string key, string name, string glyph)
{
    public string Key { get; set; } = key;
    public string Name { get; set; } = name;
    public string Glyph { get; set; } = glyph;
}

public sealed partial class ToolsPage : Page
{
    public ObservableCollection<ToolTile> Tiles { get; } = new()
    {
        new("startup", "Startup manager", ""),
        new("uninstall", "Uninstall manager", ""),
        new("processes", "Process manager", ""),
        new("diskspace", "Disk space analyzer", ""),
        new("duplicates", "Duplicate files finder", ""),
        new("emptyfolders", "Empty folders finder", ""),
        new("sysinfo", "System information", ""),
        new("link:cleanup:1", "Disk cleanup", ""),
        new("link:cleanup:0", "Registry repair", ""),
        new("link:drivers", "Driver manager", ""),
        new("link:apps", "Software update", ""),
        new("link:repair", "System repair", ""),
        new("link:scan", "Virus scan", ""),
    };

    public ToolsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    void Tile_Click(object sender, ItemClickEventArgs e)
    {
        var tile = (ToolTile)e.ClickedItem;
        if (tile.Key.StartsWith("link:"))
        {
            var parts = tile.Key.Split(':');
            if (parts.Length > 2) CleanupPage.RequestedTab = int.Parse(parts[2]);
            App.MainWindow.NavigateTo(parts[1]);
            return;
        }

        Type? page = tile.Key switch
        {
            "startup" => typeof(StartupToolPage),
            "uninstall" => typeof(UninstallToolPage),
            "processes" => typeof(ProcessToolPage),
            "diskspace" => typeof(DiskSpaceToolPage),
            "duplicates" => typeof(DuplicatesToolPage),
            "emptyfolders" => typeof(EmptyFoldersToolPage),
            "sysinfo" => typeof(SysInfoToolPage),
            _ => null,
        };
        if (page == null) return;
        ToolFrame.Navigate(page);
        TitleText.Text = tile.Name;
        BackButton.Visibility = Visibility.Visible;
        TileScroll.Visibility = Visibility.Collapsed;
        ToolFrame.Visibility = Visibility.Visible;
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        ToolFrame.Content = null;
        ToolFrame.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        TileScroll.Visibility = Visibility.Visible;
        TitleText.Text = "System tools";
    }
}

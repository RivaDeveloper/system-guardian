using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed class ToolTile(string key, string name, string description, string glyph)
{
    public string Key { get; set; } = key;
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;
    public string Glyph { get; set; } = glyph;
}

public sealed partial class ToolsPage : Page
{
    public ObservableCollection<ToolTile> Cleaning { get; } = new()
    {
        new("link:cleanup:1", "Disk cleanup", "Clear temp files and caches", ""),
        new("link:cleanup:0", "Registry repair", "Fix leftover registry entries", ""),
        new("debloat", "Debloat", "Remove pre-installed apps you don't use", ""),
        new("tracks", "Tracks eraser", "Clear recent files, history and browser cache", ""),
        new("duplicates", "Duplicate files", "Find identical files taking up space", ""),
        new("emptyfolders", "Empty folders", "Find and remove folders with nothing in them", ""),
        new("shortcuts", "Shortcut fixer", "Repair or remove broken shortcuts", ""),
        new("uninstall", "Uninstall manager", "Remove programs you no longer use", ""),
    };

    public ObservableCollection<ToolTile> Speed { get; } = new()
    {
        new("startup", "Startup manager", "Choose what starts with Windows", ""),
        new("processes", "Process manager", "See what's running and end what's stuck", ""),
        new("diskspace", "Disk space analyzer", "See which folders take the most space", ""),
    };

    public ObservableCollection<ToolTile> Hardware { get; } = new()
    {
        new("link:drivers", "Driver manager", "Check, back up and restore drivers", ""),
        new("link:apps", "Software update", "Update your apps through winget", ""),
        new("bios", "BIOS update assistant", "Safe checks before a BIOS update", ""),
        new("sysinfo", "System information", "Your PC's hardware and Windows details", ""),
    };

    public ObservableCollection<ToolTile> Protect { get; } = new()
    {
        new("link:scan", "Virus scan", "Deep scan, Defender and VirusTotal", ""),
        new("link:repair", "System repair", "Repair Windows files with DISM and SFC", ""),
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
            "bios" => typeof(BiosToolPage),
            "debloat" => typeof(DebloatToolPage),
            "shortcuts" => typeof(ShortcutToolPage),
            "tracks" => typeof(TracksToolPage),
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

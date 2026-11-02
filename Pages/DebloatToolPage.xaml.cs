using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class DebloatToolPage : Page
{
    public ObservableCollection<BloatApp> Items { get; } = new();

    public DebloatToolPage() => InitializeComponent();

    void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveButton.IsEnabled = AppList.SelectedItems.Count > 0;

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = RemoveButton.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Text = "Looking at installed apps…";
        Items.Clear();
        try
        {
            foreach (var a in await DebloatService.FindAsync()) Items.Add(a);
            StatusText.Text = Items.Count == 0 ? "Nothing from the bloat list is installed." :
                $"{Items.Count} removable apps found. Recommended ones are ticked.";
            foreach (var a in Items.Where(a => a.Recommended)) AppList.SelectedItems.Add(a);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { ScanButton.IsEnabled = true; Busy.IsActive = false; }
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = AppList.SelectedItems.Cast<BloatApp>().ToList();
        if (!await Ui.ConfirmAsync(XamlRoot, "Remove these apps?",
                $"Remove {selected.Count} app(s): {string.Join(", ", selected.Select(a => a.Title))}.\n\nYou can reinstall any of them from the Microsoft Store.", "Remove"))
            return;
        ScanButton.IsEnabled = RemoveButton.IsEnabled = false;
        Busy.IsActive = true;
        var removed = 0;
        var failed = new List<string>();
        try
        {
            foreach (var app in selected)
            {
                StatusText.Text = $"Removing {app.Title}…";
                var (ok, message) = await DebloatService.RemoveAsync(app);
                if (ok) { Items.Remove(app); removed++; }
                else failed.Add($"{app.Title}");
            }
            StatusText.Text = $"Removed {removed}." + (failed.Count > 0 ? $" Couldn't remove: {string.Join(", ", failed)}." : "");
        }
        finally { ScanButton.IsEnabled = true; Busy.IsActive = false; }
    }
}

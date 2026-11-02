using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class TracksToolPage : Page
{
    public ObservableCollection<TrackCategory> Items { get; } = new();

    public TracksToolPage() => InitializeComponent();

    void TrackList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        CleanButton.IsEnabled = TrackList.SelectedItems.Count > 0;

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = CleanButton.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Text = "Looking for traces…";
        Items.Clear();
        try
        {
            foreach (var c in await TracksService.ScanAsync()) Items.Add(c);
            StatusText.Text = Items.Count == 0 ? "No traces found." : $"{Items.Count} kinds of traces found.";
            foreach (var c in Items.Where(c => !c.NeedsAdmin || Elevation.IsElevated)) TrackList.SelectedItems.Add(c);
        }
        finally { ScanButton.IsEnabled = true; Busy.IsActive = false; }
    }

    async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = TrackList.SelectedItems.Cast<TrackCategory>().ToList();
        if (!await Ui.ConfirmAsync(XamlRoot, "Erase these traces?", $"Erase: {string.Join(", ", selected.Select(c => c.Name))}. This can't be undone, but nothing important is lost.", "Erase"))
            return;
        ScanButton.IsEnabled = CleanButton.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Text = "Erasing…";
        try
        {
            var n = await TracksService.CleanAsync(selected, Elevation.IsElevated);
            var skipped = !Elevation.IsElevated && selected.Any(c => c.NeedsAdmin);
            StatusText.Text = $"Cleared {n:N0} item(s)." + (skipped ? " The DNS cache needs administrator." : "");
            Items.Clear();
        }
        finally { ScanButton.IsEnabled = true; Busy.IsActive = false; }
    }
}

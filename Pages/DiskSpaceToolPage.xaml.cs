using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Cleanup;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Guardian.Pages;

public sealed partial class DiskSpaceToolPage : Page
{
    string? _current;
    int _run;
    public ObservableCollection<FolderEntry> Items { get; } = new();

    public DiskSpaceToolPage() => InitializeComponent();

    async void Pick_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Ui.PickFolderAsync();
        if (folder != null) await AnalyzeAsync(folder);
    }

    async Task AnalyzeAsync(string folder)
    {
        var run = ++_run;
        _current = folder;
        UpButton.IsEnabled = Path.GetDirectoryName(folder) != null;
        Busy.IsActive = true;
        Items.Clear();
        StatusText.Text = $"Measuring {folder}…";
        try
        {
            var list = await FileTools.AnalyzeAsync(folder, CancellationToken.None);
            if (run != _run) return;
            foreach (var f in list) Items.Add(f);
            StatusText.Text = $"{folder} · {JunkCategory.Format(list.Sum(f => f.Bytes))}";
        }
        finally { if (run == _run) Busy.IsActive = false; }
    }

    async void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = _current == null ? null : Path.GetDirectoryName(_current);
        if (parent != null) await AnalyzeAsync(parent);
    }

    void SizeList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OpenButton.IsEnabled = SizeList.SelectedItem != null;

    async void SizeList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SizeList.SelectedItem is FolderEntry { IsFolder: true } f) await AnalyzeAsync(f.Path);
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (SizeList.SelectedItem is FolderEntry f)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{f.Path}\"") { UseShellExecute = true });
    }
}

using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class EmptyFoldersToolPage : Page
{
    public ObservableCollection<EmptyFolder> Items { get; } = new();

    public EmptyFoldersToolPage() => InitializeComponent();

    void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteButton.IsEnabled = FolderList.SelectedItems.Count > 0;

    async void Pick_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Ui.PickFolderAsync();
        if (folder == null) return;
        if (FileTools.IsProtected(folder))
        {
            await Ui.ShowAsync(XamlRoot, "Folder not allowed", "Windows and program folders are off limits here: some of their empty folders are needed.");
            return;
        }
        PickButton.IsEnabled = DeleteButton.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Text = "Scanning…";
        Items.Clear();
        try
        {
            foreach (var f in await FileTools.FindEmptyFoldersAsync(folder, CancellationToken.None)) Items.Add(f);
            StatusText.Text = Items.Count == 0 ? "No empty folders found." : $"{Items.Count} empty folders found.";
            FolderList.SelectAll();
        }
        finally { PickButton.IsEnabled = true; Busy.IsActive = false; }
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = FolderList.SelectedItems.Cast<EmptyFolder>().ToList();
        if (!await Ui.ConfirmAsync(XamlRoot, "Remove empty folders?", $"Remove {selected.Count} empty folder(s)? They contain no files.", "Remove")) return;
        var removed = 0;
        foreach (var f in selected)
        {
            try
            {
                // Recursive only so that nested empty folders go too; a folder that gained a file since the scan is left alone.
                if (Directory.EnumerateFiles(f.Path, "*", SearchOption.AllDirectories).Any()) continue;
                Directory.Delete(f.Path, true);
                Items.Remove(f);
                removed++;
            }
            catch { }
        }
        StatusText.Text = $"Removed {removed} folder(s).";
    }
}

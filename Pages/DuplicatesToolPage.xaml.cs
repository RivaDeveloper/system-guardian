using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Cleanup;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualBasic.FileIO;

namespace Guardian.Pages;

public sealed partial class DuplicatesToolPage : Page
{
    CancellationTokenSource? _cts;
    public ObservableCollection<DupFile> Items { get; } = new();

    public DuplicatesToolPage() => InitializeComponent();

    void DupList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DeleteButton.IsEnabled = DupList.SelectedItems.Count > 0;

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    async void Pick_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Ui.PickFolderAsync();
        if (folder == null) return;
        if (FileTools.IsProtected(folder))
        {
            await Ui.ShowAsync(XamlRoot, "Folder not allowed", "Windows and program folders are off limits here: removing files from them can break Windows or your programs.");
            return;
        }
        _cts = new CancellationTokenSource();
        PickButton.IsEnabled = DeleteButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        Busy.IsActive = true;
        Items.Clear();
        try
        {
            var list = await FileTools.FindDuplicatesAsync(folder, s => DispatcherQueue.TryEnqueue(() => StatusText.Text = s), _cts.Token);
            foreach (var f in list) Items.Add(f);
            // Pre-select every copy except the first of each set.
            foreach (var g in list.GroupBy(f => f.GroupId))
                foreach (var f in g.Skip(1)) DupList.SelectedItems.Add(f);
            var wasted = list.GroupBy(f => f.GroupId).Sum(g => g.First().Size * (g.Count() - 1));
            StatusText.Text = list.Count == 0 ? "No duplicates found." :
                $"{list.GroupBy(f => f.GroupId).Count()} sets of identical files; extra copies use {JunkCategory.Format(wasted)}.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        finally
        {
            PickButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            Busy.IsActive = false;
        }
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = DupList.SelectedItems.Cast<DupFile>().ToList();
        // Keep at least one copy per set, whatever the selection says.
        var protectedGroups = Items.GroupBy(f => f.GroupId)
            .Where(g => g.All(f => selected.Contains(f))).Select(g => g.Key).ToHashSet();
        var toDelete = selected.Where(f => !protectedGroups.Contains(f.GroupId)).ToList();
        if (toDelete.Count == 0)
        {
            await Ui.ShowAsync(XamlRoot, "Nothing to remove", "Every copy in the selected sets is ticked. Untick at least one copy per set to keep it.");
            return;
        }
        if (!await Ui.ConfirmAsync(XamlRoot, "Move to Recycle Bin?",
                $"Move {toDelete.Count} duplicate file(s) ({JunkCategory.Format(toDelete.Sum(f => f.Size))}) to the Recycle Bin? You can restore them from there.", "Move"))
            return;
        var moved = 0;
        foreach (var f in toDelete)
        {
            try
            {
                FileSystem.DeleteFile(f.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                Items.Remove(f);
                moved++;
            }
            catch { }
        }
        StatusText.Text = $"Moved {moved} file(s) to the Recycle Bin." + (protectedGroups.Count > 0 ? $" Kept one copy in {protectedGroups.Count} fully ticked set(s)." : "");
    }
}

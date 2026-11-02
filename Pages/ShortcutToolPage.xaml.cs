using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class ShortcutToolPage : Page
{
    public ObservableCollection<ShortcutItem> Items { get; } = new();

    public ShortcutToolPage() => InitializeComponent();

    void ShortcutList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var sel = ShortcutList.SelectedItems.Cast<ShortcutItem>().ToList();
        RemoveButton.IsEnabled = sel.Count > 0;
        RepairButton.IsEnabled = sel.Any(s => s.NewTarget != null);
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        Busy.IsActive = true;
        StatusText.Text = "Checking shortcuts…";
        Items.Clear();
        try
        {
            foreach (var s in await ShortcutService.ScanAsync()) Items.Add(s);
            var fixable = Items.Count(s => s.NewTarget != null);
            StatusText.Text = Items.Count == 0 ? "All shortcuts point at something that exists." :
                $"{Items.Count} broken shortcuts, {fixable} can be repaired.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { ScanButton.IsEnabled = true; Busy.IsActive = false; }
    }

    void Repair_Click(object sender, RoutedEventArgs e)
    {
        var done = 0;
        foreach (var s in ShortcutList.SelectedItems.Cast<ShortcutItem>().Where(s => s.NewTarget != null).ToList())
            if (ShortcutService.Repair(s)) { Items.Remove(s); done++; }
        StatusText.Text = $"Repaired {done} shortcut(s).";
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var sel = ShortcutList.SelectedItems.Cast<ShortcutItem>().ToList();
        if (!await Ui.ConfirmAsync(XamlRoot, "Remove shortcuts?", $"Move {sel.Count} broken shortcut(s) to the Recycle Bin? The programs themselves aren't touched.", "Remove"))
            return;
        var done = 0;
        foreach (var s in sel) if (ShortcutService.Remove(s)) { Items.Remove(s); done++; }
        StatusText.Text = $"Moved {done} shortcut(s) to the Recycle Bin.";
    }
}

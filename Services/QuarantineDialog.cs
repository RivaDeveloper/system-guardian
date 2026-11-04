using System.Collections.ObjectModel;
using Guardian.Services.Deep;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Services;

/// <summary>
/// The Quarantine window, shared by the Virus scan and Protection pages: every quarantined file with why it was moved,
/// and what can be done with the selected ones. Nothing here deletes unless the user asks twice.
/// </summary>
public static class QuarantineDialog
{
    public static async Task ShowAsync(XamlRoot root, Action? changed = null)
    {
        var items = new ObservableCollection<QuarantineEntry>(Quarantine.List().OrderByDescending(e => e.When));

        var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.Extended, MaxHeight = 320, MinWidth = 520 };
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Padding='0,6'><TextBlock Text='{Binding DisplayName}' FontWeight='SemiBold'/><TextBlock Text='{Binding Summary}' Opacity='0.7' FontSize='12' TextWrapping='Wrap'/></StackPanel></DataTemplate>");

        var empty = new TextBlock { Text = "Nothing is in quarantine.", Opacity = 0.7, Margin = new Thickness(0, 8, 0, 8) };
        var note = new TextBlock { Opacity = 0.75, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        var restore = new Button { Content = "Restore" };
        var allow = new Button { Content = "Restore and always allow" };
        var delete = new Button { Content = "Delete forever" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { restore, allow, delete } };

        void Refresh()
        {
            var any = items.Count > 0;
            empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            list.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            buttons.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            var some = list.SelectedItems.Count > 0;
            restore.IsEnabled = allow.IsEnabled = delete.IsEnabled = some;
            if (any && note.Text.Length == 0)
                note.Text = "Restore puts the file back where it was. Real-time protection may flag it again; \"Restore and always allow\" tells it to leave that file alone. Hold Ctrl or Shift to pick several.";
        }

        async Task Act(Func<QuarantineEntry, string> action)
        {
            var picked = list.SelectedItems.OfType<QuarantineEntry>().ToList();
            var done = 0;
            string? error = null;
            foreach (var entry in picked)
            {
                try { action(entry); items.Remove(entry); done++; }
                catch (Exception ex) { error = $"{entry.DisplayName}: {ex.Message}"; }
            }
            note.Text = error ?? (done == 1 ? "Done." : $"Done ({done} files).");
            if (done > 0) changed?.Invoke();
            Refresh();
            await Task.CompletedTask;
        }

        list.SelectionChanged += (_, _) => Refresh();
        restore.Click += async (_, _) => await Act(e => Quarantine.Restore(e));
        allow.Click += async (_, _) => await Act(e => Quarantine.Restore(e, alwaysAllow: true));

        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = "Quarantine", CloseButtonText = "Close",
            Content = new StackPanel { Children = { empty, list, buttons, note } },
        };
        delete.Click += async (_, _) =>
        {
            var picked = list.SelectedItems.OfType<QuarantineEntry>().ToList();
            if (picked.Count == 0) return;
            // one dialog at a time: hide this one, ask, then show it again
            dialog.Hide();
            var what = picked.Count == 1 ? picked[0].DisplayName : $"{picked.Count} files";
            if (await Ui.ConfirmAsync(root, "Delete permanently?", $"{what} will be deleted for good. This can't be undone.", "Delete"))
                foreach (var entry in picked)
                {
                    try { Quarantine.DeleteForever(entry); items.Remove(entry); }
                    catch (Exception ex) { note.Text = ex.Message; }
                }
            changed?.Invoke();
            Refresh();
            if (items.Count > 0) _ = dialog.ShowAsync();
        };

        Refresh();
        await dialog.ShowAsync();
    }
}

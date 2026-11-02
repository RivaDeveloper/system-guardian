using System.Collections.ObjectModel;
using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class AppsPage : Page
{
    readonly AppUpdateService _service = new();
    CancellationTokenSource? _cts;

    public ObservableCollection<AppUpdate> Apps { get; } = new();

    public AppsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
    }

    void SetBusy(bool busy)
    {
        Busy.IsActive = busy;
        CheckButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        UpdateAllButton.IsEnabled = !busy && Apps.Count > 0;
        UpdateSelectedButton.IsEnabled = !busy && AppsList.SelectedItems.Count > 0;
    }

    void Log(string line) => DispatcherQueue.TryEnqueue(() =>
    {
        // winget's "N package(s) have version numbers that cannot be determined" line becomes a small note instead of log text.
        var unknown = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+) package\(s\) have version numbers that cannot be determined");
        if (unknown.Success)
        {
            NoteText.Text = $"{unknown.Groups[1].Value} installed programs don't report a version number, so Guardian can't tell whether they're out of date.";
            NoteText.Visibility = Visibility.Visible;
            return;
        }
        LogBox.Text += line + "\n";
        LogBox.SelectionStart = LogBox.Text.Length;
    });

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = "Checking winget sources…";
        try
        {
            var list = await _service.GetUpdatesAsync(_cts.Token);
            Apps.Clear();
            foreach (var a in list) Apps.Add(a);
            StatusText.Text = list.Count == 0 ? "Everything winget knows about is up to date." : $"{list.Count} app(s) have updates.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        catch (System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "winget was not found. Install \"App Installer\" from the Microsoft Store.";
        }
        finally { SetBusy(false); }
    }

    void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedButton.IsEnabled = AppsList.SelectedItems.Count > 0 && CheckButton.IsEnabled;

    async void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = AppsList.SelectedItems.Cast<AppUpdate>().ToList();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            var n = 0;
            foreach (var app in selected)
            {
                StatusText.Text = $"Updating {++n} of {selected.Count}: {app.Name}";
                var ok = await _service.UpgradeAsync(app.Id, Log, _cts.Token);
                Log(ok ? $"== {app.Name}: done" : $"== {app.Name}: failed (it may be running, or need a manual update)");
                if (ok) Apps.Remove(app);
            }
            StatusText.Text = "Finished.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        finally { SetBusy(false); }
    }

    async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (!await Ui.ConfirmAsync(XamlRoot, "Update everything?",
                $"Update all {Apps.Count} apps silently with winget?", "Update all"))
            return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = "Updating all apps…";
        try
        {
            await _service.UpgradeAllAsync(Log, _cts.Token);
            StatusText.Text = "Finished. Run the check again to see what remains.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cancelled."; }
        finally { SetBusy(false); }
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
}

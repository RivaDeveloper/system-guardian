using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Cleanup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class CleanupPage : Page
{
    readonly RegistryCleaner _registry = new();
    readonly JunkCleaner _junk = new();

    /// <summary>Set by the Tools page to open a particular tab (0 registry, 1 junk, 2 restore).</summary>
    public static int RequestedTab = -1;

    public ObservableCollection<RegistryIssue> RegIssues { get; } = new();
    public ObservableCollection<JunkCategory> JunkItems { get; } = new();
    public ObservableCollection<RegistryBackup> Backups { get; } = new();

    public CleanupPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        RefreshBackups();
        RegHint.Text = Elevation.IsElevated ? "" :
            "Not running as administrator: machine-wide entries are listed but can't be fixed until you restart Guardian as administrator.";
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (RequestedTab >= 0) { CleanupPivot.SelectedIndex = RequestedTab; RequestedTab = -1; }
    }

    // ---- registry ---------------------------------------------------------------------------------

    async void RegScan_Click(object sender, RoutedEventArgs e)
    {
        RegScanButton.IsEnabled = RegFixButton.IsEnabled = false;
        RegBusy.IsActive = true;
        RegIssues.Clear();
        try
        {
            var list = await _registry.ScanAsync(s => DispatcherQueue.TryEnqueue(() => RegStatus.Text = $"Checking {s.ToLowerInvariant()}…"));
            foreach (var i in list) RegIssues.Add(i);
            RegStatus.Text = list.Count == 0 ? "Your registry looks clean." : $"{list.Count} leftover entries found.";
            RegList.SelectAll();
        }
        finally { RegScanButton.IsEnabled = true; RegBusy.IsActive = false; }
    }

    void RegList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RegFixButton.IsEnabled = RegList.SelectedItems.Count > 0;

    async void RegFix_Click(object sender, RoutedEventArgs e)
    {
        var selected = RegList.SelectedItems.Cast<RegistryIssue>().ToList();
        if (!await Ui.ConfirmAsync(XamlRoot, "Fix registry entries?",
                $"Remove {selected.Count} leftover entr{(selected.Count == 1 ? "y" : "ies")}? Guardian saves a backup first, and you can restore it from the Restore tab.", "Fix"))
            return;
        RegScanButton.IsEnabled = RegFixButton.IsEnabled = false;
        RegBusy.IsActive = true;
        try
        {
            var result = await _registry.FixAsync(selected, Elevation.IsElevated);
            foreach (var i in selected.ToList())
                if (!i.IsMachine || Elevation.IsElevated) RegIssues.Remove(i);
            RegStatus.Text = $"Fixed {result.Fixed}." + (result.Skipped > 0 ? $" {result.Skipped} skipped (administrator needed or not removable)." : "");
            RefreshBackups();
        }
        finally { RegScanButton.IsEnabled = true; RegBusy.IsActive = false; }
    }

    // ---- junk -------------------------------------------------------------------------------------

    async void JunkScan_Click(object sender, RoutedEventArgs e)
    {
        JunkScanButton.IsEnabled = JunkCleanButton.IsEnabled = false;
        JunkBusy.IsActive = true;
        JunkStatus.Text = "Looking for junk…";
        JunkItems.Clear();
        try
        {
            var list = await _junk.ScanAsync();
            foreach (var c in list) JunkItems.Add(c);
            var total = list.Sum(c => c.Bytes);
            JunkStatus.Text = list.Count == 0 ? "Nothing to clean." : $"{JunkCategory.Format(total)} can be cleaned.";
            JunkList.SelectAll();
        }
        finally { JunkScanButton.IsEnabled = true; JunkBusy.IsActive = false; }
    }

    void JunkList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        JunkCleanButton.IsEnabled = JunkList.SelectedItems.Count > 0;

    async void JunkClean_Click(object sender, RoutedEventArgs e)
    {
        var selected = JunkList.SelectedItems.Cast<JunkCategory>().ToList();
        var bytes = selected.Sum(c => c.Bytes);
        if (!await Ui.ConfirmAsync(XamlRoot, "Delete junk files?",
                $"About {JunkCategory.Format(bytes)} will be deleted permanently. These are temporary files and caches, not your documents.", "Clean"))
            return;
        JunkScanButton.IsEnabled = JunkCleanButton.IsEnabled = false;
        JunkBusy.IsActive = true;
        JunkStatus.Text = "Cleaning…";
        try
        {
            var r = await _junk.CleanAsync(selected, Elevation.IsElevated);
            var skippedAdmin = !Elevation.IsElevated && selected.Any(c => c.NeedsAdmin);
            JunkStatus.Text = $"Freed {JunkCategory.Format(r.Bytes)} ({r.Files:N0} files)." +
                              (r.Locked > 0 ? $" {r.Locked:N0} were in use and left alone." : "") +
                              (skippedAdmin ? " Windows-wide items need administrator." : "");
            JunkItems.Clear();
        }
        finally { JunkScanButton.IsEnabled = true; JunkBusy.IsActive = false; }
    }

    // ---- restore ----------------------------------------------------------------------------------

    void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _registry.ListBackups()) Backups.Add(b);
    }

    void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RestoreButton.IsEnabled = BackupList.SelectedItem != null;

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedItem is not RegistryBackup b) return;
        if (!await Ui.ConfirmAsync(XamlRoot, "Restore this backup?", $"Put back the {b.Items} entries saved on {b.Created:g}?", "Restore")) return;
        var ok = await _registry.RestoreAsync(b);
        RestoreStatus.Text = ok == b.Items ? $"Restored {ok} entries." :
            $"Restored {ok} of {b.Items}. Machine-wide entries need administrator rights.";
    }

    void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(RegistryCleaner.BackupRoot);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RegistryCleaner.BackupRoot) { UseShellExecute = true });
    }
}

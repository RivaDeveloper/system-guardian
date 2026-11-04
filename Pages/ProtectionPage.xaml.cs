using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Deep;
using Guardian.Services.Protection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class ProtectionPage : Page
{
    public ObservableCollection<ProtectionEvent> Events { get; } = new();
    public ObservableCollection<string> Exclusions { get; } = new();
    bool _loading;

    public ProtectionPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += (_, _) => { ProtectionService.EventLogged += OnEvent; ProtectionService.StatsChanged += OnStats; LoadAll(); };
        Unloaded += (_, _) => { ProtectionService.EventLogged -= OnEvent; ProtectionService.StatsChanged -= OnStats; };
    }

    void OnEvent(ProtectionEvent e) => DispatcherQueue.TryEnqueue(() => { Events.Insert(0, e); HistoryEmpty.Visibility = Visibility.Collapsed; });
    void OnStats() => DispatcherQueue.TryEnqueue(RefreshStatus);

    void LoadAll()
    {
        _loading = true;
        var p = SettingsStore.Load().Protection;
        MasterSwitch.IsOn = p.RealtimeEnabled;
        FilesSwitch.IsOn = p.WatchFiles;
        StartupSwitch.IsOn = p.WatchStartup;
        UsbSwitch.IsOn = p.ScanUsb;
        ProgramsSwitch.IsOn = p.WatchPrograms;
        PauseSwitch.IsOn = p.PausePrograms;
        AutoSwitch.IsOn = p.AutoQuarantine;
        CloudSwitch.IsOn = p.CloudCheck;
        NotifySwitch.IsOn = p.Notify;
        BackgroundSwitch.IsOn = p.RunInBackground;
        AutoStartSwitch.IsOn = AutoStart.IsEnabled;
        ScheduleBox.SelectedIndex = p.Schedule == "Daily" ? 1 : p.Schedule == "Weekly" ? 2 : 0;
        Exclusions.Clear();
        foreach (var x in p.Exclusions) Exclusions.Add(x);
        Events.Clear();
        foreach (var e in ProtectionService.Events) Events.Add(e);
        HistoryEmpty.Visibility = Events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _loading = false;
        RefreshStatus();
    }

    void RefreshStatus()
    {
        var on = ProtectionService.Running;
        var open = ProtectionService.Events.Count(e => e.Action == "Alert");
        StatusTitle.Text = on ? (open > 0 ? "NEEDS A LOOK" : "PROTECTED") : "PROTECTION OFF";
        StatusTitle.Foreground = on ? (open > 0 ? Tones.Warn : Tones.Good) : Tones.Bad;
        var tone = on ? (open > 0 ? Tones.Warn : Tones.Good) : Tones.Bad;
        StatusRing.Stroke = tone;
        StatusHalo.Fill = tone;
        StatusGlyph.Foreground = tone;
        StatusGlyph.Glyph = !on ? "" : open > 0 ? "" : "";
        StatusSub.Text = on
            ? (open > 0 ? $"{open} alert(s) in the history below are waiting for your decision." : "Guardian is watching for new files, startup changes and USB drives.")
            : "Real-time protection is switched off. Guardian still scans when you ask it to.";
        var problem = ProtectionService.ProgramWatchProblem;
        ProgramNote.Text = problem.Length > 0 ? "Not running: " + problem + (Elevation.IsElevated ? "" : " Restart Guardian as administrator to use it.") : "";
        ProgramNote.Foreground = Tones.Warn;
        ProgramNote.Visibility = problem.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var s = ProtectionService.State;
        CheckedText.Text = s.FilesChecked.ToString("N0");
        ThreatsText.Text = s.ThreatsFound.ToString("N0");
        QuarantinedText.Text = Quarantine.List().Count.ToString("N0");

        var meta = IntelMeta.Load();
        EngineText.Text = $"Detection: {ThreatIntel.Count:N0} known-bad fingerprints" +
                          (YaraEngine.EngineAvailable && YaraEngine.RulesReady ? $", {meta.RuleCount:N0} signature rules" : ", signature rules not downloaded yet (Settings, Threat intelligence)") +
                          ", plus signature, location and behaviour checks.";
        var changed = _loading;
        _loading = true;
        MasterSwitch.IsOn = on;
        _loading = changed;
    }

    static void Save(Action<ProtectionSettings> change)
    {
        var s = SettingsStore.Load();
        change(s.Protection);
        SettingsStore.Save(s);
        ProtectionService.Reconfigure();
    }

    void Master_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Save(p => p.RealtimeEnabled = MasterSwitch.IsOn);
    }

    void Option_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var sw = (ToggleSwitch)sender;
        var on = sw.IsOn;
        switch (sw.Tag as string)
        {
            case "files": Save(p => p.WatchFiles = on); break;
            case "startup": Save(p => p.WatchStartup = on); break;
            case "usb": Save(p => p.ScanUsb = on); break;
            case "programs": Save(p => p.WatchPrograms = on); break;
            case "pause": Save(p => p.PausePrograms = on); break;
            case "auto": Save(p => p.AutoQuarantine = on); break;
            case "cloud": Save(p => p.CloudCheck = on); break;
            case "notify": Save(p => p.Notify = on); break;
            case "background": Save(p => p.RunInBackground = on); break;
            case "autostart": AutoStart.Set(on); break;
        }
    }

    void Schedule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var value = ((ComboBoxItem)ScheduleBox.SelectedItem).Content as string ?? "Off";
        Save(p => p.Schedule = value);
    }

    // ---- exclusions ---------------------------------------------------------------------------------

    async void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        var folder = await Ui.PickFolderAsync();
        if (folder == null || Exclusions.Contains(folder)) return;
        Exclusions.Add(folder);
        Save(p => p.Exclusions = Exclusions.ToList());
    }

    void ExclusionList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveExclusionButton.IsEnabled = ExclusionList.SelectedItem != null;

    void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (ExclusionList.SelectedItem is not string s) return;
        Exclusions.Remove(s);
        Save(p => p.Exclusions = Exclusions.ToList());
    }

    // ---- history ------------------------------------------------------------------------------------

    async void EventQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ProtectionEvent ev) return;
        if (!await Ui.ConfirmAsync(XamlRoot, "Quarantine this file?", $"{ev.Path}\n\nIt is moved to Guardian's quarantine. You can restore it any time with \"View and restore\" at the top of this page.", "Quarantine")) return;
        try
        {
            ProtectionService.QuarantineFromEvent(ev);
            var i = Events.IndexOf(ev);
            if (i >= 0) { Events.RemoveAt(i); Events.Insert(i, ev); }
        }
        catch (Exception ex) { await Ui.ShowAsync(XamlRoot, "Couldn't quarantine it", ex.Message); }
    }

    async void OpenQuarantine_Click(object sender, RoutedEventArgs e) =>
        await QuarantineDialog.ShowAsync(XamlRoot, () => DispatcherQueue.TryEnqueue(RefreshStatus));

    void EventAllow_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ProtectionEvent ev) return;
        // "It's fine" excludes that one file's folder entry so it isn't raised again.
        var dir = Path.GetDirectoryName(ev.Path);
        if (!string.IsNullOrEmpty(dir) && !Exclusions.Contains(ev.Path)) { Exclusions.Add(ev.Path); Save(p => p.Exclusions = Exclusions.ToList()); }
        ProtectionService.ResumeFromEvent(ev);
        ev.Action = "Allowed";
        var i = Events.IndexOf(ev);
        if (i >= 0) { Events.RemoveAt(i); Events.Insert(i, ev); }
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        ProtectionService.ClearHistory();
        Events.Clear();
        HistoryEmpty.Visibility = Visibility.Visible;
    }
}

using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class PerformanceToolPage : Page
{
    public ObservableCollection<PerfTweak> Items { get; } = new();
    public ObservableCollection<PerfTweak> AdvancedItems { get; } = new();

    public PerformanceToolPage()
    {
        InitializeComponent();
        Load();
    }

    void Load()
    {
        Items.Clear();
        AdvancedItems.Clear();
        foreach (var t in PerformanceTweaks.List())
            (t.Advanced ? AdvancedItems : Items).Add(t);
        var on = Items.Count(t => t.IsApplied);
        StatusText.Text = $"{on} of {Items.Count} tweaks are on." + (AdvancedItems.Any(t => t.IsApplied) ? " Some security protections are switched off (see Advanced)." : "");
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    async void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        var sw = (ToggleSwitch)sender;
        if (sw.DataContext is not PerfTweak t || sw.IsOn == t.IsApplied) return;
        if (t.NeedsAdmin && !Elevation.IsElevated)
        {
            await Ui.ShowAsync(XamlRoot, "Administrator needed", "Restart Guardian as administrator to change this setting.");
            Load();
            return;
        }
        // Lowering protection needs an explicit, informed yes, and a restore point as a safety net.
        if (t.Advanced && sw.IsOn)
        {
            if (!await Ui.ConfirmAsync(XamlRoot, $"{t.Title}?", t.Risk + "\n\nGuardian will try to make a restore point first. Continue only if you understand the risk.", "I accept the risk"))
            {
                Load();
                return;
            }
            StatusText.Text = "Creating a restore point…";
            var (rp, rpMessage) = await BiosService.CreateRestorePointAsync();
            if (!rp && !await Ui.ConfirmAsync(XamlRoot, "No restore point", rpMessage + "\n\nContinue without one?", "Continue")) { Load(); return; }
        }
        var (ok, message) = PerformanceTweaks.Set(t.Key, sw.IsOn);
        if (!ok) { StatusText.Text = message; Load(); return; }
        t.IsApplied = sw.IsOn;
        StatusText.Text = t.Advanced
            ? $"{t.Title}: protection {(sw.IsOn ? "switched off" : "switched back on")}. Restart Windows to apply."
            : $"{t.Title}: {(sw.IsOn ? "on" : "off")}." + (t.NeedsRestart ? " Takes effect after a restart or sign-out." : "");
    }

    async void Recommended_Click(object sender, RoutedEventArgs e)
    {
        var todo = Items.Where(t => t.Recommended && !t.IsApplied).ToList();
        if (todo.Count == 0) { StatusText.Text = "All recommended tweaks are already on."; return; }
        if (!await Ui.ConfirmAsync(XamlRoot, "Apply recommended tweaks?",
                $"Turn on {todo.Count}: {string.Join(", ", todo.Select(t => t.Title))}.\n\nEach one can be switched off again here.", "Apply"))
            return;
        var done = 0; var skipped = 0;
        foreach (var t in todo)
        {
            if (t.NeedsAdmin && !Elevation.IsElevated) { skipped++; continue; }
            if (PerformanceTweaks.Set(t.Key, true).Ok) done++;
        }
        Load();
        StatusText.Text = $"Turned on {done}." + (skipped > 0 ? $" {skipped} need administrator." : "") +
                          (todo.Any(t => t.NeedsRestart) ? " Some changes take effect after a restart." : "");
    }

    async void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        var on = Items.Concat(AdvancedItems).Where(t => t.IsApplied).ToList();
        if (on.Count == 0) { StatusText.Text = "Nothing to undo."; return; }
        if (!await Ui.ConfirmAsync(XamlRoot, "Undo all tweaks?", $"Switch {on.Count} tweak(s) back to Windows' normal settings?", "Undo")) return;
        foreach (var t in on)
            if (!(t.NeedsAdmin && !Elevation.IsElevated)) PerformanceTweaks.Set(t.Key, false);
        Load();
    }
}

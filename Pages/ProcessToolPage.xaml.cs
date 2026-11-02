using System.Collections.ObjectModel;
using System.Diagnostics;
using Guardian.Services;
using Guardian.Services.Cleanup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed class ProcessRow
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public int Pid { get; set; }
    public long Memory { get; set; }
    public string MemoryText => JunkCategory.Format(Memory);
}

public sealed partial class ProcessToolPage : Page
{
    static readonly HashSet<string> Core = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "fontdrvhost", "explorer", "memory compression", "secure system", "guardian",
    };

    public ObservableCollection<ProcessRow> Items { get; } = new();

    public ProcessToolPage()
    {
        InitializeComponent();
        Load();
    }

    void Load()
    {
        Items.Clear();
        foreach (var p in Process.GetProcesses().OrderByDescending(p => SafeMemory(p)))
        {
            string title = "";
            try { title = p.MainWindowTitle; } catch { }
            Items.Add(new ProcessRow { Name = p.ProcessName, Pid = p.Id, Memory = SafeMemory(p), Title = title });
            p.Dispose();
        }
        StatusText.Text = $"{Items.Count} processes.";
    }

    static long SafeMemory(Process p)
    {
        try { return p.WorkingSet64; } catch { return 0; }
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    void ProcList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        EndButton.IsEnabled = ProcList.SelectedItem is ProcessRow r && !Core.Contains(r.Name);

    async void End_Click(object sender, RoutedEventArgs e)
    {
        if (ProcList.SelectedItem is not ProcessRow r || Core.Contains(r.Name)) return;
        if (!await Ui.ConfirmAsync(XamlRoot, $"End {r.Name}?", "The program closes immediately and unsaved work in it is lost.", "End process")) return;
        try
        {
            using var p = Process.GetProcessById(r.Pid);
            if (!string.Equals(p.ProcessName, r.Name, StringComparison.OrdinalIgnoreCase)) { StatusText.Text = "That process has already changed; refresh the list."; return; }
            p.Kill();
            StatusText.Text = $"Ended {r.Name}.";
            Load();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex is System.ComponentModel.Win32Exception
                ? "Windows refused: this program needs administrator rights to close."
                : ex.Message;
        }
    }
}

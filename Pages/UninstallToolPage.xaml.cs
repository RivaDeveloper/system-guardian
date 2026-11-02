using System.Collections.ObjectModel;
using Guardian.Services;
using Guardian.Services.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class UninstallToolPage : Page
{
    readonly List<InstalledProgram> _all = InstalledPrograms.List();
    public ObservableCollection<InstalledProgram> Items { get; } = new();

    public UninstallToolPage()
    {
        InitializeComponent();
        Filter();
    }

    void Filter()
    {
        var q = SearchBox.Text.Trim();
        Items.Clear();
        foreach (var p in _all.Where(p => q.Length == 0 || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                       || p.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)))
            Items.Add(p);
        StatusText.Text = $"{Items.Count} programs.";
    }

    void Search_TextChanged(object sender, TextChangedEventArgs e) => Filter();

    void ProgramList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UninstallButton.IsEnabled = ProgramList.SelectedItem != null;

    async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramList.SelectedItem is not InstalledProgram p) return;
        if (!await Ui.ConfirmAsync(XamlRoot, $"Uninstall {p.Name}?", "This starts the program's own uninstaller. You can still cancel there.", "Uninstall"))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/s /c \"{p.UninstallCommand}\"")
            {
                UseShellExecute = false, CreateNoWindow = true,
            });
            StatusText.Text = $"Started the uninstaller for {p.Name}. Refresh this list when it finishes.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}

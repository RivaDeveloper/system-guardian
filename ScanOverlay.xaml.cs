using System.Collections.ObjectModel;
using System.ComponentModel;
using Guardian.Services;
using Guardian.Services.Deep;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Guardian;

/// <summary>One line in the scan progress window's stage checklist.</summary>
public sealed class StageRow(string name) : INotifyPropertyChanged
{
    string _glyph = "";   // circle
    Brush _brush = (Brush)Application.Current.Resources["FaintBrush"];
    string _note = "";

    public string Name { get; } = name;
    public string Glyph { get => _glyph; set { _glyph = value; Raise(nameof(Glyph)); } }
    public Brush Brush { get => _brush; set { _brush = value; Raise(nameof(Brush)); } }
    public string Note { get => _note; set { _note = value; Raise(nameof(Note)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The scan progress window: stage checklist, percentage, current item, counters and elapsed time.</summary>
public sealed partial class ScanOverlay : UserControl
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    DateTime _started;
    int _findings;
    long _items;
    bool _showCounts;

    public ObservableCollection<StageRow> Stages { get; } = new();

    /// <summary>Raised when the user presses Cancel.</summary>
    public event Action? CancelRequested;

    public ScanOverlay()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => RefreshStats();
    }

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    /// <summary>Shows the window. Pass stage names for a checklist; with <paramref name="percentKnown"/> false it shows a moving bar.</summary>
    public void Begin(string title, IEnumerable<string> stageNames, bool percentKnown, string? note = null)
    {
        TitleText.Text = title;
        EyebrowText.Text = "SCANNING";
        Stages.Clear();
        foreach (var s in stageNames) Stages.Add(new StageRow(s));
        StageList.Visibility = Stages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        Bar.IsIndeterminate = !percentKnown;
        Bar.Value = 0;
        PercentText.Text = percentKnown ? "0%" : "…";
        PercentText.FontSize = percentKnown ? 44 : 32;
        StageText.Text = Stages.Count > 0 ? Stages[0].Name : title;
        ItemText.Text = "";
        _findings = 0; _items = 0;
        _showCounts = percentKnown;
        NoteText.Text = note ?? "";
        NoteText.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.IsEnabled = true;
        CancelButton.Content = "Cancel";

        _started = DateTime.Now;
        RefreshStats();
        _timer.Start();
        Visibility = Visibility.Visible;
    }

    public void Update(ScanProgress p)
    {
        _findings = p.FindingCount;
        _items = p.ItemsChecked;
        Bar.IsIndeterminate = false;
        Bar.Value = p.Overall;
        PercentText.Text = $"{Math.Floor(p.Overall):0}%";
        StageText.Text = $"{p.Stage}  ({Math.Min(p.StageIndex + 1, p.StageCount)} of {p.StageCount})";
        ItemText.Text = p.Item;

        for (var i = 0; i < Stages.Count; i++)
        {
            var row = Stages[i];
            if (p.CompletedStages.Contains(row.Name))
            {
                row.Glyph = ""; row.Brush = Tones.Good; row.Note = "done";
            }
            else if (i == p.StageIndex)
            {
                row.Glyph = ""; row.Brush = (Brush)Application.Current.Resources["AccentBrush"]; row.Note = "now";
            }
        }
        RefreshStats();
    }

    void RefreshStats()
    {
        var elapsed = Fmt(DateTime.Now - _started);
        StatsText.Text = _showCounts
            ? $"{_items:N0} items checked  ·  {_findings} to review  ·  {elapsed}"
            : $"Running for {elapsed}";
    }

    public void End()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        CancelButton.Content = "Cancelling…";
        CancelRequested?.Invoke();
    }
}

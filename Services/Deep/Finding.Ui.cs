using Microsoft.UI.Xaml.Media;

namespace Guardian.Services.Deep;

// WinUI-only half of Finding. Kept apart so the scan engine itself has no UI dependency and can be tested headless.
public sealed partial class Finding
{
    public Brush SeverityBrush => Severity switch
    {
        Severity.Critical or Severity.High => Tones.Bad,
        Severity.Medium => Tones.Warn,
        _ => Tones.Neutral,
    };

    public string Subtitle => FilePath.Length > 0 ? $"{KindText}  ·  {FilePath}" : KindText;
}

using Microsoft.UI.Xaml.Media;

namespace Guardian.Services.Protection;

public sealed partial class ProtectionEvent
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Brush SeverityBrush => Severity switch
    {
        "Critical" or "High" => Tones.Bad,
        "Medium" or "Low" => Tones.Warn,
        _ => Tones.Neutral,
    };
}

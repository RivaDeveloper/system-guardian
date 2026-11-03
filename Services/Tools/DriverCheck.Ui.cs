using Microsoft.UI.Xaml.Media;

namespace Guardian.Services.Tools;

public sealed partial class DriverCheck
{
    public Brush StatusBrush => State switch
    {
        DriverState.Newer => Tones.Warn,
        DriverState.UpToDate => Tones.Good,
        _ => Tones.Neutral,
    };
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Guardian.Services;

/// <summary>Status colours from the Lumen palette.</summary>
public static class Tones
{
    static Microsoft.UI.Xaml.Media.SolidColorBrush Make(byte r, byte g, byte b) =>
        new(Windows.UI.Color.FromArgb(255, r, g, b));

    public static readonly Microsoft.UI.Xaml.Media.SolidColorBrush Good = Make(0x46, 0xe0, 0x8a);
    public static readonly Microsoft.UI.Xaml.Media.SolidColorBrush Warn = Make(0xff, 0xb8, 0x4a);
    public static readonly Microsoft.UI.Xaml.Media.SolidColorBrush Bad = Make(0xff, 0x2e, 0x55);
    public static readonly Microsoft.UI.Xaml.Media.SolidColorBrush Neutral = Make(0xa2, 0xa8, 0xbc);
}

/// <summary>Small helpers shared by the pages (dialogs, pickers).</summary>
public static class Ui
{
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message, string primary)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowAsync(XamlRoot root, string title, string message)
    {
        await new ContentDialog { XamlRoot = root, Title = title, Content = message, CloseButtonText = "OK" }.ShowAsync();
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}

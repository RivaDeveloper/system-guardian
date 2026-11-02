using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    /// <summary>
    /// Classic Win32 folder dialog. The WinRT FolderPicker throws inside elevated processes,
    /// and Guardian is often run as administrator, so it can't be used here.
    /// </summary>
    public static Task<string?> PickFileAsync(string title) =>
        Task.FromResult(FolderDialog.PickFile(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow), title));

    public static Task<string?> PickFolderAsync() =>
        Task.FromResult(FolderDialog.Pick(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow), "Choose a folder"));
}

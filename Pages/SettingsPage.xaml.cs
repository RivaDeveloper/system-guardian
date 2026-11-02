using Guardian.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Guardian.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        StatusText.Text = string.IsNullOrEmpty(ApiKeyStore.Load()) ? "No key saved." : "A key is saved.";
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Password)) { StatusText.Text = "Paste a key first."; return; }
        ApiKeyStore.Save(KeyBox.Password);
        KeyBox.Password = "";
        StatusText.Text = "Key saved.";
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyStore.Clear();
        StatusText.Text = "Key removed.";
    }
}

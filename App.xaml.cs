using Microsoft.UI.Xaml;

namespace Guardian;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        // An unexpected error in a button handler should be logged and survived, not close the app.
        UnhandledException += (_, e) =>
        {
            e.Handled = true;
            LogCrash(e.Exception);
            MainWindow?.DispatcherQueue.TryEnqueue(() => MainWindow.ShowError(e.Exception));
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    static void LogCrash(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}

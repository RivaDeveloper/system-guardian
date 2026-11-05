using Guardian.Services;
using Guardian.Services.Protection;
using Microsoft.UI.Xaml;

namespace Guardian;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;
    public static bool Exiting { get; private set; }
    static TrayIcon? _tray;
    static Mutex? _instance;
    static EventWaitHandle? _showEvent;

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
        var background = Environment.GetCommandLineArgs().Contains("--background");

        // One Guardian per elevation level: a second launch just brings the first one to the front.
        var suffix = Elevation.IsElevated ? "-admin" : "";
        _instance = new Mutex(true, "Guardian.SingleInstance.v1" + suffix, out var first);
        if (!first)
        {
            try { using var ev = EventWaitHandle.OpenExisting("Guardian.ShowWindow.v1" + suffix); ev.Set(); } catch { }
            Environment.Exit(0);
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Guardian.ShowWindow.v1" + suffix);
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) MainWindow?.DispatcherQueue.TryEnqueue(ShowMainWindow);
        }) { IsBackground = true }.Start();

        Task.Run(Guardian.Services.Protection.AutoStart.Tidy);
        MainWindow = new MainWindow();

        _tray = new TrayIcon { IsProtectionOn = () => ProtectionService.Running };
        _tray.OpenRequested += ShowMainWindow;
        _tray.ToggleRequested += () =>
        {
            var s = SettingsStore.Load();
            s.Protection.RealtimeEnabled = !s.Protection.RealtimeEnabled;
            SettingsStore.Save(s);
            ProtectionService.Reconfigure();
        };
        _tray.ExitRequested += Shutdown;
        ProtectionService.Notifier = (title, text) => MainWindow.DispatcherQueue.TryEnqueue(() => _tray?.Balloon(title, text, warning: true));
        ProtectionService.Start();

        if (!background) MainWindow.Activate();
    }

    public static void ShowMainWindow()
    {
        MainWindow.AppWindow.Show();
        MainWindow.Activate();
    }

    /// <summary>Stops protection and removes the tray icon, ready for the process to end.</summary>
    public static void PrepareExit()
    {
        Exiting = true;
        ProtectionService.Stop();
        _tray?.Dispose();
        _tray = null;
    }

    /// <summary>Really quits (closing the window only hides it when background protection is on).</summary>
    public static void Shutdown()
    {
        PrepareExit();
        MainWindow.Close();
        Current.Exit();
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

namespace SpotifyToast;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\SpotifyToast.SingleInstance", out bool first);
        if (!first) return; // já está rodando no tray

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log(e.Exception); e.SetObserved(); };

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp(args));
    }

    /// <summary>Erros vão para %APPDATA%\SpotifyToast\erros.log (o app não fecha por causa deles).</summary>
    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpotifyToast");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "erros.log");
            if (File.Exists(file) && new FileInfo(file).Length > 256 * 1024) File.Delete(file);
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}

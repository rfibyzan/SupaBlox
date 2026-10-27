using Avalonia;
using SupaBlox.Core;

namespace SupaBlox;

internal static class Program
{
    public static bool StartInTray { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        StartInTray = args.Contains("--tray");

        // Cegah SupaBlox jalan dua kali.
        using var self = new Mutex(true, @"Local\SupaBlox_SelfMutex", out bool first);
        if (!first) return 0;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Storage.Log("unhandled", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) => { Storage.Log("task", e.Exception); e.SetObserved(); };

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Storage.Log("fatal", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

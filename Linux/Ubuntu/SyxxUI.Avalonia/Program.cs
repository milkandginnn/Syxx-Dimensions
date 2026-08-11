using System.Diagnostics;
using Avalonia;
using SyxxUI.Avalonia.Services;

namespace SyxxUI.Avalonia;

internal static class Program
{
    // Avalonia's desktop entry point. Nothing platform-specific here: the same binary shape runs on
    // GNOME, MATE, Cinnamon, and every other flavor because Avalonia draws its own pixels.
    [STAThread]
    public static void Main(string[] args)
    {
        // L2-002: global exception handlers — log to trace before the process tears down so
        // unobserved failures leave evidence in the crash reporter rather than vanishing silently.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Trace.TraceError("[Syxx] Unhandled exception: {0}", e.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Trace.TraceError("[Syxx] Unobserved task exception: {0}", e.Exception);
            e.SetObserved(); // prevent process teardown from a single unobserved task
        };

        // L2-001: teardown in try/finally so cleanup always runs even if the app throws during
        // shutdown or during the lifetime itself.
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            LinuxReadyServices.Stop();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            // L2-008: gate trace-level logging behind DEBUG so production builds don't emit
            // verbose diagnostic noise to stdout/stderr.
#if DEBUG
            .LogToTrace();
#else
            ;
#endif
}

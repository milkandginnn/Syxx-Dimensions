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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        LinuxReadyServices.Stop();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

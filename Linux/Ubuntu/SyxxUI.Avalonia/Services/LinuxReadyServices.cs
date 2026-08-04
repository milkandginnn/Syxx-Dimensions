using Syxx.Kit;

namespace SyxxUI.Avalonia.Services;

public static class LinuxReadyServices
{
    private static IDisposable? _tracerProvider;
    private static bool _started;

    public static void Start(SyxxSettings settings)
    {
        if (_started)
        {
            return;
        }
        _started = true;

        if (settings.CrashReportingEnabled)
        {
            global::Syxx.Telemetry.SentryDiagnosticSink.Install(
                surface: "gui",
                platform: "linux",
                ifAllowed: true);
        }

        if (settings.TelemetryEnabled)
        {
            _tracerProvider = global::Syxx.Telemetry.OTelBootstrap.Register(
                serviceName: "syxx-gui-linux",
                surface: "gui",
                ifAllowed: true);
        }
    }

    public static void Stop()
    {
        global::Syxx.Telemetry.SentryDiagnosticSink.Remove();
        _tracerProvider?.Dispose();
        _tracerProvider = null;
        _started = false;
    }
}

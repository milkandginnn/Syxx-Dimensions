using Syxx.Kit;

namespace SyxxUI.Avalonia.Services;

public static class LinuxReadyServices
{
    private static IDisposable? _tracerProvider;

    // L2-003: thread-safe started flag — volatile ensures visibility across threads without a lock.
    private static volatile bool _started;

    // L2-009: track whether Sentry is currently installed so it can be hot-toggled at runtime
    // (from Settings) without requiring an app restart.
    private static volatile bool _sentryInstalled;

    public static void Start(SyxxSettings settings)
    {
        if (_started)
        {
            return;
        }
        _started = true;

        ApplySentryState(settings.CrashReportingEnabled);

        if (settings.TelemetryEnabled)
        {
            _tracerProvider = global::Syxx.Telemetry.OTelBootstrap.Register(
                serviceName: "syxx-gui-linux",
                surface: "gui",
                ifAllowed: true);
        }
    }

    /// <summary>
    /// L2-009: toggle Sentry crash reporting at runtime without restarting the app.
    /// Called from SettingsViewModel when the crash reporting preference changes.
    /// </summary>
    public static void ApplySentryState(bool enabled)
    {
        if (enabled && !_sentryInstalled)
        {
            global::Syxx.Telemetry.SentryDiagnosticSink.Install(
                surface: "gui",
                platform: "linux",
                ifAllowed: true);
            _sentryInstalled = true;
        }
        else if (!enabled && _sentryInstalled)
        {
            global::Syxx.Telemetry.SentryDiagnosticSink.Remove();
            _sentryInstalled = false;
        }
    }

    public static void Stop()
    {
        if (_sentryInstalled)
        {
            global::Syxx.Telemetry.SentryDiagnosticSink.Remove();
            _sentryInstalled = false;
        }
        _tracerProvider?.Dispose();
        _tracerProvider = null;
        _started = false;
    }
}

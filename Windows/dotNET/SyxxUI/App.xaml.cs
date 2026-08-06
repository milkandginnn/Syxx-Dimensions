using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using Sentry;
using Syxx.Kit;

namespace SyxxUI;

public partial class App : Application
{
    private MainWindow? _window;
    private TracerProvider? _tracerProvider;
    private IDisposable? _sentry;
    private bool _readyServicesStarted;

    public App()
    {
        InitializeComponent();
        // No Suspending subscription: Application.Suspending is UWP-era and does not exist in
        // WinAppSDK/WinUI 3 (CS0246 on real Windows CI, first build ever). It also never fired
        // for this unpackaged app — which is why MainWindow.Closed and ProcessExit already call
        // FlushTelemetry (E160). Removing it loses nothing that ever ran.
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // FIX (IoFontGrotesk undefined): unpackaged WinUI 3 apps cannot resolve ms-appx:/// font
        // URIs. Register bundled Jost/Cinzel/etc. from Content-deployed .ttf so IoFont*
        // resolves at runtime. This is safe to call even if the file doesn't exist (no-op).
        RegisterAppFonts();

        _window = new MainWindow();
        _window.Activate();
        // First-run state owns startup. MainWindow creates the shell and starts allowed services
        // only after agreement and setup persistence have both been verified.
    }

    internal void StartReadyServices(SyxxSettings settings)
    {
        if (_readyServicesStarted)
        {
            return;
        }
        _readyServicesStarted = true;

        if (settings.CrashReportingEnabled)
        {
            try
            {
                var dsn = SyxxConfig.SentryDsn;
                if (!string.IsNullOrEmpty(dsn))
                {
                    _sentry = SentrySdk.Init(o =>
                    {
                        o.Dsn = dsn;
                        o.Environment = SyxxConfig.BuildEnvironment;
                        o.Release = $"syxx-windows@{SyxxConfig.Version}";
                        o.TracesSampleRate = 0.1;
                        o.AutoSessionTracking = true;
                        o.AttachStacktrace = true;
                        o.SendDefaultPii = false;
                        o.CaptureFailedRequests = true;
                        o.IsGlobalModeEnabled = true;
                    });
                    Syxx.Telemetry.SentryDiagnosticSink.Install(
                        surface: "gui",
                        platform: "windows");
                }
            }
            catch
            {
                // Crash reporting is best-effort after explicit setup consent.
            }
        }

        if (settings.TelemetryEnabled)
        {
            try
            {
                _tracerProvider = Sdk.CreateTracerProviderBuilder()
                    .AddSource("SyxxKit")
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri("https://bugsmasher.syxx.app/v1/traces");
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    })
                    .Build();
            }
            catch
            {
                // Telemetry is best-effort after explicit setup consent.
            }
        }
    }

    /// <summary>
    /// Registers application fonts from the Assets/Fonts directory so that FontFamily resources
    /// (like IoFontGrotesk) resolve even in unpackaged deployment where ms-appx:/// URIs do not
    /// work. Uses the DWrite API via Win32 to add the font file to the session font collection.
    /// Safe to call even when the file is missing or the OS doesn't support the API.
    /// </summary>
    private static void RegisterAppFonts()
    {
        try
        {
            var fontDir = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "Fonts");
            if (!System.IO.Directory.Exists(fontDir)) return;

            foreach (var ttf in System.IO.Directory.GetFiles(fontDir, "*.ttf"))
            {
                try { AddFontToSession(ttf); }
                catch { /* best effort — font registration failure should not crash */ }
            }
        }
        catch
        {
            // best effort
        }
    }

    [System.Runtime.InteropServices.DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(
        int factoryType, // DWRITE_FACTORY_TYPE_SHARED = 0
        [System.Runtime.InteropServices.In] ref Guid iid,
        out IntPtr factory);

    private static readonly Guid IID_IDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private static readonly Guid IID_IDWriteFontCollection = new("a84cee02-3ee5-4d02-9a11-5f4e0ddd1ddf");

    /// <summary>
    /// Registers a single .ttf font file via DWrite's private font collection API.
    /// Falls back to silently ignoring the font if DWrite fails.
    /// </summary>
    private static void AddFontToSession(string fontPath)
    {
        // Use the simpler Win32 AddFontResourceEx approach which works across all
        // unpackaged WinUI 3 scenarios without needing the full DWrite pipeline.
        const int FR_PRIVATE = 0x10;
        const int FR_NOT_ENUM = 0x20;

        [System.Runtime.InteropServices.DllImport("gdi32.dll", SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

        AddFontResourceEx(fontPath, FR_PRIVATE | FR_NOT_ENUM, IntPtr.Zero);
        // Return value == 0 means the font was already registered or the path is invalid;
        // neither is an error worth reporting.
    }

    /// E160: flush in-flight OTel spans and dispose Sentry before the process exits.
    /// OnSuspending does NOT fire for unpackaged Win32 desktop apps (WindowsPackageType=None),
    /// so MainWindow.Closed and ProcessExit also call this. Idempotent — safe to call multiple times.
    internal void FlushTelemetry()
    {
        try { _tracerProvider?.ForceFlush(); } catch { /* telemetry is best-effort */ }
        try { _sentry?.Dispose(); } catch { /* sentry is best-effort */ }
        _sentry = null;
    }
}

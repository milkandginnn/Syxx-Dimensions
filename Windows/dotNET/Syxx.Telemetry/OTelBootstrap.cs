using System;
using System.Collections.Generic;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Syxx.Kit;

namespace Syxx.Telemetry;

/// <summary>
/// Registers the OpenTelemetry tracer provider for every .NET surface.
///
/// <para>WHY THIS EXISTS. <c>SyxxTracer.Source</c> is an <c>ActivitySource</c>, and an
/// ActivitySource with no registered listener is a silent no-op: <c>StartActivity</c> returns null
/// and every span vanishes. Only the WinUI client ever registered a provider, so the CLI and the
/// Avalonia client produced spans that went nowhere — instrumented code, no telemetry, and nothing
/// anywhere that would reveal the difference. That is the same failure mode as the diagnostic sink
/// that nothing implemented: the wire was laid and never connected.</para>
///
/// <para>CONSENT. Registration is gated on the same <c>TelemetryGate</c> as Sentry and the
/// diagnostic sink. One gate, three consumers.</para>
///
/// <para>SERVICE NAMING names the ARTIFACT, not the OS. The CLI is one binary published for
/// win-x64, linux-x64 and linux-arm64, so calling it "syxx-cli-windows" would be false on the
/// Ubuntu ISO — the platform that ships first. The artifact is <c>syxx-cli</c>; where it ran is
/// <c>os.type</c>. Getting this backwards produces a dashboard that quietly attributes Linux
/// failures to Windows.</para>
/// </summary>
public static class OTelBootstrap
{
    private const string Endpoint = "https://bugsmasher.syxx.app/v1/traces";

    /// <summary>
    /// Register the provider if consent allows. Returns the provider so the caller can dispose it
    /// (disposal runs Shutdown, which drains the batch processor — a plain ForceFlush does not),
    /// or null when telemetry is off or registration failed.
    /// </summary>
    public static TracerProvider? Register(string serviceName, string surface, bool? ifAllowed = null)
    {
        if (!(ifAllowed ?? TelemetryGate.IsAllowed)) { return null; }

        try
        {
            return Sdk.CreateTracerProviderBuilder()
                // MUST match SyxxTracer.Source's name exactly. A mismatch here is invisible:
                // no error, no warning, just an ActivitySource nobody listens to.
                .AddSource("SyxxKit")
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(serviceName,
                                serviceNamespace: "syxx",
                                serviceVersion: SyxxConfig.Version)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment.name"] = SyxxConfig.BuildEnvironment,
                        ["os.type"] = CurrentOsType(),
                        ["syxx.surface"] = surface,
                    }))
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(Endpoint);
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                })
                .Build();
        }
        catch (Exception ex)
        {
            // Telemetry is best-effort and must never take the app down. But it must not fail
            // SILENTLY either: "no spans arrived" and "the exporter never started" look identical
            // from the collector, and that ambiguity is the same missing-data-reads-as-fine trap
            // this codebase exists to refuse. Report it through the diagnostic sink — which is a
            // different channel, so there is no circularity.
            SyxxDiagnostics.Capture(new DiagnosticEvent(
                ErrorClass: "telemetry.otel_init_failed",
                Component: nameof(OTelBootstrap),
                Section: null,
                Impact: MeasurementImpact.None,
                Severity: DiagnosticSeverity.Warning,
                Underlying: ex.GetType().Name,
                Fingerprint: new[] { "telemetry.otel_init_failed", ex.GetType().Name },
                Tags: new Dictionary<string, string>
                {
                    ["syxx.service_name"] = serviceName,
                    ["syxx.surface"] = surface,
                }));
            return null;
        }
    }

    public static string CurrentOsType() =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "darwin"
        : "unknown";
}

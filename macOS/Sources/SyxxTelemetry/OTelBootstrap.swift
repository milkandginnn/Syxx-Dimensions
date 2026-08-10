// Isolated OTel init — keeps OpenTelemetry module names out of the SwiftUI namespace.
import Foundation
import SyxxKit
import OpenTelemetryApi
import OpenTelemetrySdk
import OpenTelemetryProtocolExporterHttp
import ResourceExtension

public enum OTelBootstrap {
    public static func setup(serviceName: String, ifAllowed: Bool? = nil) {
        // CONSENT GATE. Previously this registered an exporter unconditionally, which made the
        // Settings toggle decorative and the privacy policy's "can be turned off" claim false.
        // `unknown` (never asked, or a settings file that will not decode) behaves as denied:
        // missing data is not consent.
        guard ifAllowed ?? TelemetryGate.isAllowed else { return }
        guard let endpoint = URL(string: "https://bugsmasher.syxx.app/v1/traces") else { return }
        let exporter = OtlpHttpTraceExporter(endpoint: endpoint)
        let processor = BatchSpanProcessor(spanExporter: exporter)
        OpenTelemetry.registerTracerProvider(tracerProvider:
            TracerProviderBuilder()
                .add(spanProcessor: processor)
                .with(resource: Resource(attributes: [
                    SemanticConventions.Service.name.rawValue: AttributeValue.string(serviceName)
                ]))
                .build()
        )
    }

    /// Flush any in-flight spans to the OTLP exporter. Best-effort — failures are silently
    /// ignored since telemetry loss is acceptable at shutdown.
    ///
    /// ONLY for normal shutdown. Do NOT call this when the user withdraws consent: flushing on
    /// opt-out transmits exactly the data they just refused. Use `discardOnConsentWithdrawal()`.
    public static func flush() {
        (OpenTelemetry.instance.tracerProvider as? TracerProviderSdk)?.forceFlush()
    }

    /// Consent withdrawn mid-session: replace the provider so nothing further is recorded, and
    /// deliberately do NOT flush what is buffered.
    public static func discardOnConsentWithdrawal() {
        // Order matters: stop RECORDING first, so a span racing in from a scan thread finds the
        // tracer disabled and creates nothing.
        SyxxTracer.disable()
        // Then swap the provider. On its own this is NOT sufficient and must not be relied on:
        // registerTracerProvider is a one-shot singleton (a second call is ignored), and the
        // SyxxKit tracer memo would keep the old provider's live BatchSpanProcessor exporting
        // regardless. SyxxTracer.disable() is what actually stops the flow; this is belt-and-braces
        // for anything that resolves a tracer without going through SyxxTracer.
        OpenTelemetry.registerTracerProvider(tracerProvider: DefaultTracerProvider.instance)
    }

    /// Consent granted again mid-session. Deliberately does not backfill.
    static func resumeOnConsentGranted() {
        SyxxTracer.enable()
    }
}

// D367: unified telemetry bootstrap that ensures Sentry and OTel always receive the SAME
// consent value. Previously, callers could pass different `ifAllowed` to each bootstrap,
// producing a state where Sentry reported crashes but OTel recorded no traces (or vice versa),
// making Sentry issues uncorrelatable with trace data.
//
// Usage: call TelemetryBootstrap.setup(allowed:) ONCE at app/CLI startup instead of calling
// SentryBootstrap.setup(), SentryDiagnosticSink.install(), and OTelBootstrap.setup() separately.
public enum TelemetryBootstrap {
    /// Install all telemetry (Sentry crash reporting, Sentry diagnostic sink, OTel tracing)
    /// with a single consent decision. `surface` is forwarded to SentryDiagnosticSink (D331).
    @discardableResult
    public static func setup(
        allowed: Bool = TelemetryGate.isAllowed,
        serviceName: String = "syxx-macos",
        surface: String = "gui"
    ) -> Bool {
        SentryBootstrap.setup(ifAllowed: allowed)
        let installed = SentryDiagnosticSink.install(ifAllowed: allowed, surface: surface)
        OTelBootstrap.setup(serviceName: serviceName, ifAllowed: allowed)
        return installed
    }

    /// Withdraw consent: disable tracing, close Sentry, discard buffered data.
    public static func withdrawConsent() {
        OTelBootstrap.discardOnConsentWithdrawal()
        SentryDiagnosticSink.remove()
    }

    /// Re-grant consent mid-session.
    public static func grantConsent(serviceName: String = "syxx-macos", surface: String = "gui") {
        OTelBootstrap.resumeOnConsentGranted()
        SentryDiagnosticSink.install(ifAllowed: true, surface: surface)
    }
}

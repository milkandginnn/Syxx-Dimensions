// Isolated Sentry init — keeps Sentry module names out of the SwiftUI namespace.
// Crash/error reporting complements OTel traces: Sentry captures crashes, unhandled
// exceptions, and app hangs; OTel captures structured operation spans.
import Foundation
import Sentry
import SyxxKit

public enum SentryBootstrap {
    /// Initialize Sentry crash reporting. No-op if SENTRY_DSN is not configured.
    /// Call BEFORE OTelBootstrap.setup() so Sentry captures any OTel init failures.
    public static func setup(ifAllowed: Bool? = nil) {
        // Crash reporting is a separate choice from usage telemetry. Callers that have already
        // resolved the typed settings pass that explicit choice; legacy callers retain the
        // fail-closed telemetry gate until they are migrated.
        let allowed = ifAllowed ?? TelemetryGate.isAllowed
        guard allowed else { return }
        guard let dsn = SyxxConfig.sentryDSN else { return }

        SentrySDK.start { options in
            options.dsn = dsn
            options.environment = SyxxConfig.buildEnvironment
            options.releaseName = "syxx-macos@\(SyxxConfig.version)"

            // Privacy-first: no PII, no IP collection, no session replay
            options.sendDefaultPii = false

            // Crash capture
            options.enableAutoPerformanceTracing = false  // OTel handles perf spans
            options.enableAppHangTracking = true
            options.appHangTimeoutInterval = 2.0  // >2s main thread block = app hang
            options.attachStacktrace = true

            // Breadcrumbs — what happened before the crash
            options.enableAutoBreadcrumbTracking = true
            options.maxBreadcrumbs = 50

            // Sampling — 10% of transactions for performance monitoring
            options.tracesSampleRate = 0.1

            // Pin the envelope cache so consent withdrawal can erase unsent events
            // deterministically (see SyxxConfig.telemetryCacheDirectory).
            if let dir = SyxxConfig.telemetryCacheDirectory { options.cacheDirectoryPath = dir.path }
        }

        // Connect the core's vendor-neutral sink to Sentry. Without this, every
        // SyxxDiagnostics.capture in SyxxKit finds a nil sink and silently drops — the core is
        // instrumented but the wire is not connected.
        SentryDiagnosticSink.install(ifAllowed: allowed)
    }
}

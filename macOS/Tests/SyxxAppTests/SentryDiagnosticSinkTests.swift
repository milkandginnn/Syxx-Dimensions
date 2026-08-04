import XCTest
import SyxxTelemetry
@testable import SyxxApp
@testable import SyxxKit

/// Guards the bridge that makes `SyxxDiagnostics` actually report, and the two properties of it
/// that fail silently and expensively:
///
///  1. REDACTION. This product's promise is that your DNS queries stay yours. A hostname or
///     resolver IP reaching the issue tracker is not a bug report, it is the thing the product
///     exists to prevent — and it would arrive looking like ordinary telemetry, so nothing would
///     flag it. `underlying` is documented as caller-redacted; this asserts the sink does not
///     rely on that promise.
///
///  2. CONSENT. `install(ifAllowed:)` is the single gate for the core's entire reporting path.
///     If it installs when consent is absent, every capture in `SyxxKit` starts transmitting and
///     no per-site check exists to stop it — that is the whole reason the design has one gate.
final class SentryDiagnosticSinkTests: XCTestCase {

    override func tearDown() {
        SyxxDiagnostics.uninstall()
        super.tearDown()
    }

    // MARK: - Redaction

    /// Failure case: delete the IPv4 pattern → the address survives → red.
    func testRedactsIPv4Addresses() {
        let out = SentryDiagnosticSink.redact("query to 192.168.1.77 failed")
        XCTAssertFalse(out.contains("192.168.1.77"), "a resolver IP must never reach the issue tracker")
        XCTAssertTrue(out.contains("<ip>"))
    }

    /// Failure case: delete the domain pattern → the hostname survives → red.
    /// A DNS query name is a browsing history; this is the highest-severity leak of the set.
    func testRedactsDomainNames() {
        let out = SentryDiagnosticSink.redact("resolution failed for private-intranet.example.com")
        XCTAssertFalse(out.lowercased().contains("private-intranet"),
                       "a queried domain must never reach the issue tracker")
        XCTAssertTrue(out.contains("<domain>"))
    }

    /// Home paths carry the account name.
    func testRedactsHomeDirectoryPaths() {
        let out = SentryDiagnosticSink.redact("could not write /Users/jane/.syxx/captures/x.pcap")
        XCTAssertFalse(out.contains("/Users/jane"))
        XCTAssertTrue(out.contains("/Users/<user>"))
    }

    /// Redaction must not destroy the diagnostic value — the machine-readable tokens that make an
    /// issue actionable have to survive, or engineers will route around the sink.
    func testKeepsNonIdentifyingDiagnosticText() {
        let out = SentryDiagnosticSink.redact("probe_deadline after 2500ms, attempt 2 of 2")
        XCTAssertTrue(out.contains("probe_deadline"))
        XCTAssertTrue(out.contains("attempt 2 of 2"))
    }

    // MARK: - Consent gate

    /// Failure case: drop the `guard ifAllowed` → a sink installs without consent → red.
    func testDoesNotInstallWhenConsentIsAbsent() {
        SyxxDiagnostics.uninstall()
        let installed = SentryDiagnosticSink.install(ifAllowed: false)

        XCTAssertFalse(installed)
        XCTAssertFalse(SyxxDiagnostics.isReporting,
                       "no sink may be installed without consent — this is the only gate")
    }

    /// The positive half, so the guard cannot be satisfied by disabling reporting entirely.
    func testInstallsWhenConsentIsPresent() {
        SyxxDiagnostics.uninstall()
        let installed = SentryDiagnosticSink.install(ifAllowed: true)

        XCTAssertTrue(installed)
        XCTAssertTrue(SyxxDiagnostics.isReporting)
    }

    /// Withdrawal must detach, so a capture racing in from a scan thread finds nothing.
    func testRemoveDetachesTheSink() {
        SentryDiagnosticSink.install(ifAllowed: true)
        XCTAssertTrue(SyxxDiagnostics.isReporting)

        SentryDiagnosticSink.remove()

        XCTAssertFalse(SyxxDiagnostics.isReporting,
                       "after withdrawal no event may reach any exporter")
    }

    /// `isReporting` must reflect OBSERVED state, not a stored preference — a settings screen that
    /// claims telemetry is on because a checkbox is ticked, while nothing is installed, is the
    /// same class of falsehood as a green result with no evidence.
    func testIsReportingReflectsInstalledSinkNotAToggle() {
        SyxxDiagnostics.uninstall()
        XCTAssertFalse(SyxxDiagnostics.isReporting)

        SyxxDiagnostics.install(RecordingSink())
        XCTAssertTrue(SyxxDiagnostics.isReporting)
    }

    /// A capture with no sink installed must be a no-op, not a crash — the core calls
    /// unconditionally by design, and the CLI and tests run with no sink at all.
    func testCaptureWithNoSinkIsSafe() {
        SyxxDiagnostics.uninstall()
        SyxxDiagnostics.capture(DiagnosticEvent(
            errorClass: "test.noop", component: "SentryDiagnosticSinkTests",
            impact: .none, severity: .warning, fingerprint: ["test.noop"]))
        XCTAssertFalse(SyxxDiagnostics.isReporting)
    }
}

/// Minimal sink for asserting install/uninstall state without touching Sentry.
private struct RecordingSink: DiagnosticSink {
    func capture(_ event: DiagnosticEvent) {}
    func breadcrumb(category: String, message: String,
                    level: DiagnosticSeverity, data: [String: String]) {}
    func discardBuffered() {}
}

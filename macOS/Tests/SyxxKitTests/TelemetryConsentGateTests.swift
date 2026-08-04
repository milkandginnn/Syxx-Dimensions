import XCTest
@testable import SyxxKit

/// The Settings telemetry toggle wrote a key that NO telemetry code read: Sentry and OTel both
/// initialized unconditionally. The published privacy policy states telemetry "can be turned off
/// in Settings", which was therefore false — a user-visible claim with no backing behavior, which
/// is the same defect class as a result with no evidence, applied to consent.
///
/// These tests pin the gate. `TelemetryGate` is the single question every exporter must ask.
final class TelemetryConsentGateTests: XCTestCase {

    private func settings(telemetry: Bool) -> SyxxSettings {
        var s = SyxxSettings.default
        s.telemetryEnabled = telemetry
        return s
    }

    /// Failure case: delete the `guard TelemetryGate.isAllowed` from either bootstrap → an opted-out
    /// user is still reported on → this contract no longer holds.
    func testExplicitOptOutDeniesTelemetry() {
        let consent = TelemetryGate.currentConsent(settings: settings(telemetry: false),
                                                   environment: [:])
        XCTAssertEqual(consent, .denied)
        XCTAssertFalse(consent.allowsTelemetry)
    }

    func testExplicitOptInGrantsTelemetry() {
        let consent = TelemetryGate.currentConsent(settings: settings(telemetry: true),
                                                   environment: [:])
        XCTAssertEqual(consent, .granted)
        XCTAssertTrue(consent.allowsTelemetry)
    }

    /// `SYXX_TELEMETRY=0` must win over stored settings: a CI run or a scripted invocation has to
    /// be able to guarantee silence without editing the user's settings file.
    /// Failure case: check the env var only when settings are absent → red.
    func testEnvironmentOverrideBeatsStoredOptIn() {
        let consent = TelemetryGate.currentConsent(settings: settings(telemetry: true),
                                                   environment: ["SYXX_TELEMETRY": "0"])
        XCTAssertEqual(consent, .denied,
                       "SYXX_TELEMETRY=0 must force silence regardless of stored settings")
    }

    /// `unknown` is NOT consent. This is the whole reason the type is tri-state rather than Bool:
    /// with a Bool defaulting to true, "never answered" and "yes" are the same value.
    func testUnknownIsNotConsent() {
        XCTAssertFalse(TelemetryConsent.unknown.allowsTelemetry,
                       "missing data must never be read as consent")
        XCTAssertFalse(TelemetryConsent.denied.allowsTelemetry)
        XCTAssertTrue(TelemetryConsent.granted.allowsTelemetry)
    }

    /// End-to-end with the real store: a corrupt settings file must not produce consent.
    /// This ties the gate to the fail-closed load path.
    func testCorruptSettingsFileYieldsDeniedThroughTheGate() throws {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-gate-\(UUID().uuidString).json")
        try Data("{ truncated".utf8).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        let consent = TelemetryGate.currentConsent(settings: SyxxSettingsStore.load(from: url),
                                                   environment: [:])

        XCTAssertEqual(consent, .denied,
                       "a corrupt settings file must not be read as permission to report")
    }
}

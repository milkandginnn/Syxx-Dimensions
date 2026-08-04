import XCTest
@testable import SyxxKit

/// G-4 settings store. The honesty-relevant guarantees: defaults are the SAFE resting state
/// (encryption off, telemetry on-with-disclosure), a corrupt file falls back to defaults rather than
/// crashing, and a round trip preserves every field.
final class SyxxSettingsTests: XCTestCase {

    private func tempURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-settings-\(UUID().uuidString)")
            .appendingPathComponent("settings.json")
    }

    func testDefaultsAreSafeRestingState() {
        let d = SyxxSettings.default
        XCTAssertFalse(d.encryptedDNSEnabled, "encryption must default OFF — never on without the user asking")
        XCTAssertEqual(d.encryptedDNSResolverID, EncryptedDNSConfig.defaultResolverID)
        XCTAssertNil(d.scanResolverID, "scans default to the system resolver")
    }

    func testRoundTripPreservesEveryField() {
        let url = tempURL(); defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        var s = SyxxSettings()
        s.encryptedDNSEnabled = true
        s.encryptedDNSResolverID = "quad9"
        s.encryptedDNSTransport = .dot
        s.scanResolverID = "google"
        s.notificationsEnabled = false
        s.telemetryEnabled = false
        XCTAssertTrue(SyxxSettingsStore.save(s, to: url), "save must report a real success")
        XCTAssertEqual(SyxxSettingsStore.load(from: url), s, "every field must survive the round trip")
    }

    /// A broken settings file must load usable values and never throw. Any preference that can
    /// trigger an external side effect remains disabled until the settings record is readable.
    ///
    /// TIGHTENED (not relaxed): consent is the one field where "the default" and "the safe value"
    /// point in OPPOSITE directions. `telemetryEnabled` defaults to `true`, so the old blanket
    /// `== .default` assertion was pinning a privacy defect: a user who opted OUT and then had
    /// this file truncated by a partial write was silently opted back IN. Every other field still
    /// defaults; consent now fails closed. See ConsentFailClosedTests for the dedicated guards.
    func testCorruptFileFallsBackToDefaultsButNeverResurrectsConsent() {
        let url = tempURL()
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        try? "{ this is not valid json".write(to: url, atomically: true, encoding: .utf8)

        let loaded = SyxxSettingsStore.load(from: url)   // must not throw or crash

        // Passive values still fall back to defaults.
        XCTAssertEqual(loaded.encryptedDNSEnabled, SyxxSettings.default.encryptedDNSEnabled)
        XCTAssertEqual(loaded.encryptedDNSResolverID, SyxxSettings.default.encryptedDNSResolverID)
        XCTAssertEqual(loaded.encryptedDNSTransport, SyxxSettings.default.encryptedDNSTransport)
        XCTAssertEqual(loaded.scanResolverID, SyxxSettings.default.scanResolverID)
        XCTAssertEqual(loaded.experienceMode, SyxxSettings.default.experienceMode)

        // Missing or corrupt data must never authorize a startup side effect.
        XCTAssertFalse(loaded.notificationsEnabled)
        XCTAssertFalse(loaded.telemetryEnabled,
                       "a corrupt settings file must not re-enable telemetry the user turned off")
        XCTAssertFalse(loaded.updateCheckEnabled)
        XCTAssertFalse(loaded.crashReportingEnabled)
    }

    func testMissingFileLoadsDefaults() {
        XCTAssertEqual(SyxxSettingsStore.load(from: tempURL()), .default)
    }
}

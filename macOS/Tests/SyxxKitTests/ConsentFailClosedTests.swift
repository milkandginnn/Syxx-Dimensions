import XCTest
@testable import SyxxKit

/// Consent must fail CLOSED. `telemetryEnabled` defaults to `true`, so before this guard a user
/// who opted OUT and then had ~/.syxx/settings.json truncated (power loss, partial write) was
/// silently opted back IN on the next launch, with no notification — missing data read as
/// consent, the inverse of the honesty spine's "missing data is not clean data".
final class ConsentFailClosedTests: XCTestCase {

    private func tempURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-consent-\(UUID().uuidString).json")
    }

    /// A file that EXISTS but will not decode is corruption, not a first run.
    /// Failure case: return `.default` for undecodable data → telemetryEnabled true → red.
    func testCorruptSettingsFileNeverResurrectsConsent() throws {
        let url = tempURL()
        try Data("{ this is not json".utf8).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        let loaded = SyxxSettingsStore.load(from: url)

        XCTAssertFalse(loaded.telemetryEnabled,
                       "a corrupt settings file must not re-enable telemetry the user turned off")
    }

    /// Truncated-to-empty is the realistic power-loss shape, and must behave the same.
    func testEmptyFileFailsClosedOnConsent() throws {
        let url = tempURL()
        try Data().write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        XCTAssertFalse(SyxxSettingsStore.load(from: url).telemetryEnabled)
    }

    /// The guard must be narrow: a genuinely ABSENT file is a first run, not a withdrawal,
    /// and must still get the documented defaults. Failure case: fail closed on absence too,
    /// which would silently change first-run behavior → red.
    func testAbsentFileStillGetsFirstRunDefaults() {
        let loaded = SyxxSettingsStore.load(from: tempURL())   // never created

        XCTAssertEqual(loaded.telemetryEnabled, SyxxSettings.default.telemetryEnabled)
        XCTAssertEqual(loaded.encryptedDNSResolverID, SyxxSettings.default.encryptedDNSResolverID)
    }

    /// An explicit opt-out round-trips and is not overwritten by the defaults path.
    func testExplicitOptOutSurvivesRoundTrip() throws {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url) }
        var s = SyxxSettings.default
        s.telemetryEnabled = false
        XCTAssertTrue(SyxxSettingsStore.save(s, to: url))

        XCTAssertFalse(SyxxSettingsStore.load(from: url).telemetryEnabled)
    }
}

import XCTest
@testable import SyxxKit

/// Tests for the dark/light mode theme system: SyxxThemeMode resolution,
/// SyxxSettings.themeMode persistence, and safe defaults.
final class ThemeModeTests: XCTestCase {

    private func tempURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-theme-\(UUID().uuidString)")
            .appendingPathComponent("settings.json")
    }

    // MARK: - SyxxThemeMode resolution

    func testSystemResolvesToSystem() {
        let mode = SyxxThemeMode(rawValueOrSystem: "system")
        XCTAssertEqual(mode, .system)
    }

    func testDarkResolvesToDark() {
        let mode = SyxxThemeMode(rawValueOrSystem: "dark")
        XCTAssertEqual(mode, .dark)
    }

    func testLightResolvesToLight() {
        let mode = SyxxThemeMode(rawValueOrSystem: "light")
        XCTAssertEqual(mode, .light)
    }

    func testUnknownValueFallsBackToSystem() {
        let mode = SyxxThemeMode(rawValueOrSystem: "neon")
        XCTAssertEqual(mode, .system, "Unrecognized theme strings must fall back to system, not crash")
    }

    func testEmptyStringFallsBackToSystem() {
        let mode = SyxxThemeMode(rawValueOrSystem: "")
        XCTAssertEqual(mode, .system)
    }

    func testDisplayNames() {
        XCTAssertEqual(SyxxThemeMode.system.displayName, "System")
        XCTAssertEqual(SyxxThemeMode.dark.displayName, "Dark")
        XCTAssertEqual(SyxxThemeMode.light.displayName, "Light")
    }

    func testAllCasesContainsThreeModes() {
        XCTAssertEqual(SyxxThemeMode.allCases.count, 3)
        XCTAssertTrue(SyxxThemeMode.allCases.contains(.system))
        XCTAssertTrue(SyxxThemeMode.allCases.contains(.dark))
        XCTAssertTrue(SyxxThemeMode.allCases.contains(.light))
    }

    // MARK: - SyxxSettings.themeMode defaults

    func testThemeModeDefaultsToSystem() {
        let settings = SyxxSettings.default
        XCTAssertEqual(settings.themeMode, "system",
                       "Theme must default to 'system' so the app follows macOS appearance on first launch")
    }

    // MARK: - SyxxSettings.themeMode round-trip persistence

    func testThemeModeRoundTripSystem() {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        var s = SyxxSettings()
        s.themeMode = "system"
        XCTAssertTrue(SyxxSettingsStore.save(s, to: url))
        XCTAssertEqual(SyxxSettingsStore.load(from: url).themeMode, "system")
    }

    func testThemeModeRoundTripDark() {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        var s = SyxxSettings()
        s.themeMode = "dark"
        XCTAssertTrue(SyxxSettingsStore.save(s, to: url))
        XCTAssertEqual(SyxxSettingsStore.load(from: url).themeMode, "dark")
    }

    func testThemeModeRoundTripLight() {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        var s = SyxxSettings()
        s.themeMode = "light"
        XCTAssertTrue(SyxxSettingsStore.save(s, to: url))
        XCTAssertEqual(SyxxSettingsStore.load(from: url).themeMode, "light")
    }

    func testThemeModeIncludedInFullRoundTrip() {
        let url = tempURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        var s = SyxxSettings()
        s.themeMode = "light"
        s.experienceMode = "general"
        s.encryptedDNSEnabled = true
        s.notificationsEnabled = false
        XCTAssertTrue(SyxxSettingsStore.save(s, to: url))
        let loaded = SyxxSettingsStore.load(from: url)
        XCTAssertEqual(loaded, s, "themeMode must survive alongside every other field")
        XCTAssertEqual(loaded.themeMode, "light")
    }

    // MARK: - Backward compatibility (missing themeMode in stored JSON)

    func testMissingThemeModeFallsBackToSystem() {
        // Simulates a settings file written by a pre-themeMode version of Syxx.
        // The decoder must default themeMode to "system" rather than failing.
        let url = tempURL()
        try? FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }

        // JSON with every field EXCEPT themeMode.
        let legacyJSON = """
        {
            "encryptedDNSEnabled": false,
            "encryptedDNSResolverID": "cloudflare",
            "encryptedDNSTransport": "doh",
            "notificationsEnabled": true,
            "telemetryEnabled": false,
            "updateCheckEnabled": true,
            "crashReportingEnabled": false,
            "experienceMode": "story",
            "settingsSchemaVersion": 2
        }
        """
        try? legacyJSON.write(to: url, atomically: true, encoding: .utf8)

        let loaded = SyxxSettingsStore.load(from: url)
        XCTAssertEqual(loaded.themeMode, "system",
                       "A pre-themeMode settings file must default to 'system', not fail to load")
    }

}

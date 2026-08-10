import Foundation
import SwiftUI
import XCTest
@testable import SyxxApp
@testable import SyxxKit

/// Tests for theme mode integration at the app level: GuidedSetupSelection wiring,
/// FirstRunCoordinator persistence, and ContentView scheme resolution.
@MainActor
final class ThemeModeAppTests: XCTestCase {

    private let document = AgreementDocument(
        version: "presentation-2026-07-30",
        sha256: String(repeating: "c", count: 64)
    )

    private func temporaryURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-theme-app-\(UUID().uuidString)")
            .appendingPathComponent("settings.json")
    }

    // MARK: - GuidedSetupSelection

    func testGuidedSetupSafeDefaultsIncludeThemeMode() {
        let defaults = GuidedSetupSelection.safeDefaults
        XCTAssertEqual(defaults.themeMode, "system",
                       "Guided setup must default to 'system' so first-run follows macOS appearance")
    }

    func testGuidedSetupSelectionAcceptsCustomThemeMode() {
        var selection = GuidedSetupSelection.safeDefaults
        selection.themeMode = "dark"
        XCTAssertEqual(selection.themeMode, "dark")
        XCTAssertNotEqual(selection, GuidedSetupSelection.safeDefaults)
    }

    // MARK: - FirstRunCoordinator persists themeMode

    func testBeginSetupPersistsThemeMode() async {
        let url = temporaryURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)

        var selection = GuidedSetupSelection.safeDefaults
        selection.themeMode = "dark"

        let result = await coordinator.beginSetup(selection)
        XCTAssertEqual(result, .success)
        XCTAssertEqual(coordinator.settings.themeMode, "dark")

        // Verify it persisted to disk.
        let loaded = SyxxSettingsStore.load(from: url)
        XCTAssertEqual(loaded.themeMode, "dark")
    }

    func testSkipWithDefaultsPersistsSystemThemeMode() {
        let url = temporaryURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)
        XCTAssertEqual(coordinator.skipWithDefaults(), .success)
        XCTAssertEqual(coordinator.settings.themeMode, "system")

        let loaded = SyxxSettingsStore.load(from: url)
        XCTAssertEqual(loaded.themeMode, "system")
    }

    func testBeginSetupPersistsLightThemeMode() async {
        let url = temporaryURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)

        var selection = GuidedSetupSelection.safeDefaults
        selection.themeMode = "light"

        let result = await coordinator.beginSetup(selection)
        XCTAssertEqual(result, .success)

        let loaded = SyxxSettingsStore.load(from: url)
        XCTAssertEqual(loaded.themeMode, "light")
    }

    // MARK: - ContentView resolvedScheme logic (pure function test)

    func testResolvedSchemeSystemReturnsNil() {
        // "system" → nil (follow OS)
        let mode = SyxxThemeMode(rawValueOrSystem: "system")
        let scheme: ColorScheme? = resolvedScheme(for: mode)
        XCTAssertNil(scheme, "System theme must return nil to follow macOS appearance")
    }

    func testResolvedSchemeDarkReturnsDark() {
        let mode = SyxxThemeMode(rawValueOrSystem: "dark")
        let scheme: ColorScheme? = resolvedScheme(for: mode)
        XCTAssertEqual(scheme, .dark)
    }

    func testResolvedSchemeLightReturnsLight() {
        let mode = SyxxThemeMode(rawValueOrSystem: "light")
        let scheme: ColorScheme? = resolvedScheme(for: mode)
        XCTAssertEqual(scheme, .light)
    }

    /// Mirror of ContentView.resolvedScheme logic for unit-testability
    /// without needing to instantiate the full SwiftUI view hierarchy.
    private func resolvedScheme(for mode: SyxxThemeMode) -> ColorScheme? {
        switch mode {
        case .dark: return .dark
        case .light: return .light
        case .system: return nil
        }
    }

    // MARK: - SyxxSettingsStore.didSaveNotification

    func testSavePostsNotification() {
        let url = temporaryURL()
        defer { try? FileManager.default.removeItem(at: url.deletingLastPathComponent()) }

        let expectation = expectation(description: "didSave notification posted")
        let observer = NotificationCenter.default.addObserver(
            forName: SyxxSettingsStore.didSaveNotification,
            object: nil,
            queue: .main
        ) { _ in
            expectation.fulfill()
        }
        defer { NotificationCenter.default.removeObserver(observer) }

        var settings = SyxxSettings()
        settings.themeMode = "dark"
        XCTAssertTrue(SyxxSettingsStore.save(settings, to: url))

        wait(for: [expectation], timeout: 2.0)
    }
}

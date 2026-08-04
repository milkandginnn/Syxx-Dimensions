import XCTest
@testable import SyxxApp

/// Wave 2a source contracts for layout-only shell changes.
/// These are intentionally narrow guards because SwiftUI view geometry has no runtime
/// measurement seam in the current test target. Each assertion includes its red mutation.
final class DimensionsShellLayoutTests: XCTestCase {
    private func source(_ fileName: String) throws -> String {
        let sourceRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent() // SyxxAppTests
            .deletingLastPathComponent() // Tests
            .deletingLastPathComponent() // macOS
            .appendingPathComponent("Sources/SyxxApp", isDirectory: true)
        return try String(
            contentsOf: sourceRoot.appendingPathComponent(fileName),
            encoding: .utf8
        )
    }

    func testDashboardHasNoOuterFullPanelScrollContainer() throws {
        let dashboard = try source("DashboardView.swift")

        // Failure mutation: restoring PurpleScrollView(geometry: .fullPanel) around the
        // Dashboard makes this assertion red. The compact nested activity log is intentional.
        XCTAssertFalse(
            dashboard.contains("PurpleScrollView(geometry: .fullPanel)"),
            "The Dashboard must fit the design floor without an outer full-panel scroll container."
        )
    }

    func testShellOwnsTheThirtyFivePointPaneOrigin() throws {
        let content = try source("ContentView.swift")

        // Failure mutation: changing the shell constant or routing a pane without paneContent
        // makes at least one of these assertions red and reintroduces a double or missing inset.
        XCTAssertTrue(content.contains("private static let paneOrigin: CGFloat = 35"))
        XCTAssertTrue(content.contains(".padding(.horizontal, Self.paneOrigin)"))
        XCTAssertTrue(content.contains(".padding(.top, Self.paneOrigin)"))
        XCTAssertTrue(content.contains("paneContent(DashboardView())"))
    }

    func testSidebarIdentityAndRowsMatchTheLockedShellContract() throws {
        let content = try source("ContentView.swift")
        let designSystem = try source("Views/Components/IoDesignSystem.swift")

        // Failure mutation: changing the logo gap, wordmark block, eyebrow, or nav role makes
        // this contract red instead of silently drifting from the locked design.
        XCTAssertTrue(content.contains("HStack(alignment: .center, spacing: 5)"))
        XCTAssertTrue(content.contains(".frame(width: 130, height: 65, alignment: .topLeading)"))
        XCTAssertTrue(content.contains("Text(\"Dimensions\")"))
        XCTAssertTrue(content.contains(".foregroundColor(.white)"))
        XCTAssertFalse(content.contains("Text(\"The Six Wardens\")"))
        XCTAssertTrue(designSystem.contains(".font(.navLabel(15))"))
        XCTAssertTrue(designSystem.contains(".frame(height: 36)"))
    }
}

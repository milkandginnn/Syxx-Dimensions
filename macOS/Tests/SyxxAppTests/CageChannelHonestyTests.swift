import XCTest
@testable import SyxxApp
import SyxxKit

/// Guards the three Cage-panel honesty defects the product owner reported with screenshots:
///
///  1. EVERY MODULE'S RESULT RENDERING INSIDE CAGE. `output` is a GLOBAL activity channel every
///     module writes to. The Cage panel used to render `model.output` as if it were the capture's
///     own result, so a Prophet/Mirror/Portal/Glutton/Witness result finishing while Cage was on
///     screen painted into Cage's panel. The Cage panel now binds to `cageResultMessage` (Cage's own
///     `cageOutput` channel). `testNonCageModuleResultCannotReachCagePanel` goes RED if that binding
///     is reverted to `output`.
///
///  3. A CAPTURE THAT NEVER RAN RENDERING AS ONE THAT IS LISTENING. The "listening" line used to be
///     gated on `cageRows.isEmpty`, so it showed before any capture and after a capture that failed
///     to start. It is now gated on `cageTerminalFeed`, which is `.listening` ONLY while a capture is
///     genuinely running. `testListeningNeverShowsWhileNotCapturing` goes RED if that gate is
///     reverted to the row-emptiness check.
final class CageChannelHonestyTests: XCTestCase {

    // MARK: - Defect 1: the shared feed must not reach the Cage surface

    /// Failure case (mutation): revert `cageResultMessage` to `{ output }` (i.e. the Cage panel reads
    /// the shared feed again) → a non-Cage module's text reaches the Cage surface → RED.
    @MainActor
    func testNonCageModuleResultCannotReachCagePanel() {
        let model = AppModel()

        // A non-Cage module writes the shared activity feed while Cage is on screen.
        model.output = "🟢 A record for github.com resolved to 140.82.112.3"
        XCTAssertTrue(model.cageResultMessage.isEmpty,
                      "a non-Cage module's result must never reach the Cage panel")

        model.output = "Mirror: vantage comparison failed"
        XCTAssertTrue(model.cageResultMessage.isEmpty,
                      "a non-Cage module failure must never reach the Cage panel")

        // Cage's own channel is what the panel shows.
        model.cageOutput = "Captured 3 DNS packet(s)."
        XCTAssertEqual(model.cageResultMessage, "Captured 3 DNS packet(s).",
                       "the Cage panel must show the capture's own result")
    }

    /// The shared Dashboard log must still receive Cage's messages — the fix separates channels, it
    /// does not silence the activity terminal.
    @MainActor
    func testCageOutputStillFeedsDashboardLog() {
        let model = AppModel()
        model.cageOutput = "No packets were detected (a quiet network, or authorization was not granted)."
        XCTAssertTrue(model.dashboardLog.contains { $0.text.contains("No packets were detected") },
                      "a Cage message must still appear in the Dashboard activity log")
    }

    // MARK: - Defect 3: "listening" only while genuinely capturing

    /// Failure case (mutation): revert `cageTerminalFeed` to
    /// `cageRows.isEmpty ? .listening : .rows` → the "listening" line appears while idle / failed /
    /// finished-empty → RED.
    @MainActor
    func testListeningNeverShowsWhileNotCapturing() {
        let model = AppModel()

        // Never run (idle), no rows: must NOT read as listening.
        model.cageCapturing = false
        model.cageRows = []
        XCTAssertNotEqual(model.cageTerminalFeed, .listening,
                          "a capture that never ran must not render as listening")
        XCTAssertEqual(model.cageTerminalFeed, .none)

        // Failed to start (message set, capture not running, no rows): must NOT read as listening.
        model.cageOutput = "Could not start capture. Helper installation failed."
        model.cageCapturing = false
        model.cageRows = []
        XCTAssertNotEqual(model.cageTerminalFeed, .listening,
                          "a capture that failed to start must never render as listening")

        // Finished with nothing captured: must NOT read as listening.
        model.cageOutput = "No packets were detected (a quiet network, or authorization was not granted)."
        model.cageCapturing = false
        model.cageRows = []
        XCTAssertNotEqual(model.cageTerminalFeed, .listening,
                          "a finished, empty capture must not render as listening")
    }

    /// The positive half, so the gate cannot be satisfied by never returning `.listening` at all:
    /// a genuinely running capture with no rows yet IS legitimately listening.
    @MainActor
    func testListeningShowsOnlyWhileCapturingWithNoRows() {
        let model = AppModel()
        model.cageCapturing = true
        model.cageRows = []
        XCTAssertEqual(model.cageTerminalFeed, .listening,
                       "a running capture with no rows yet is legitimately listening")

        // Once real rows arrive, the feed shows them, not the listening placeholder.
        model.cageRows = [CageRow(time: "12:00:00", domain: "example.com", note: "",
                                  process: "mDNSResponder", resolver: "8.8.8.8", severity: .info)]
        XCTAssertEqual(model.cageTerminalFeed, .rows,
                       "captured rows must show as rows, not the listening placeholder")
    }
}

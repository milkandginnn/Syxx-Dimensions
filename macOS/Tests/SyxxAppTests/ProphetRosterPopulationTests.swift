import XCTest
@testable import SyxxApp
@testable import SyxxKit

/// Prophet resolver roster (owner directive 2026-08-02 "Prophet resolvers populate").
///
/// The roster's population logic is extracted into the pure static functions
/// `ProphetView.resolverAnswerState(for:)` and `ProphetView.prophetRosterEntries(localServer:responses:)`
/// so it can be proven without SwiftUI geometry (same seam pattern as
/// `PortalView.resolversPaneEmptyCopy`). Every test states the mutation that turns it red.
///
/// The honesty gate under test: a leg with no measurement is `.notQueried` (never rendered
/// blank-as-clean), an unreachable / empty leg is `.unknown` (never a usable address, never
/// green), and only a real address is `.measured`. No leg is ever fabricated as clean.
final class ProphetRosterPopulationTests: XCTestCase {

    private func response(_ name: String, _ ip: String) -> ProphetResult.ResolverResponse {
        ProphetResult.ResolverResponse(resolver: name, ip: ip)
    }

    // MARK: - resolverAnswerState: the honesty gate for a single leg

    func testMissingAnswerIsNotQueriedNeverMeasured() {
        XCTAssertEqual(ProphetView.resolverAnswerState(for: nil), .notQueried)
        // Failure case: mapping nil to .measured("") (or any measured value) makes an
        // un-measured, pre-scan leg render as if it returned a real address.
        XCTAssertNotEqual(ProphetView.resolverAnswerState(for: nil), .measured(""))
    }

    func testUnreachableLegIsUnknownNeverMeasured() {
        let state = ProphetView.resolverAnswerState(for: "[unreachable]")
        XCTAssertEqual(state, .unknown("[unreachable]"))
        // Failure case: classifying "[unreachable]" as .measured would present a failed leg as a
        // usable answer — a false clean. This assertion goes red if that regresses.
        if case .measured = state {
            XCTFail("an unreachable leg must never be .measured")
        }
    }

    func testNoRecordsLegIsUnknownNeverMeasured() {
        let state = ProphetView.resolverAnswerState(for: "no records")
        XCTAssertEqual(state, .unknown("no records"))
        if case .measured = state {
            XCTFail("a no-records leg must never be .measured")
        }
    }

    func testRealAddressIsMeasured() {
        XCTAssertEqual(
            ProphetView.resolverAnswerState(for: "104.16.132.229"),
            .measured("104.16.132.229")
        )
    }

    // MARK: - prophetRosterEntries: which resolvers show, and each leg's state

    func testPreScanWithoutMeasuredServerShowsPublicOraclesAllPending() {
        let entries = ProphetView.prophetRosterEntries(localServer: "", responses: nil)

        // Only the public oracles — an unmeasured Local resolver is never presented as known.
        XCTAssertEqual(entries.count, ProphetOracleRoster.publicOracles.count)
        XCTAssertFalse(
            entries.contains { $0.name.hasPrefix("Local") },
            "a Local resolver that has not been measured must not appear (would render 'Local ()')"
        )
        // Every leg is pending before a scan — never a fabricated or blank-as-clean value.
        XCTAssertTrue(entries.allSatisfy { $0.answer == .notQueried })
        // Failure case: defaulting a missing measurement to .measured, or prepending a Local()
        // entry from an empty server, makes one of the assertions above red.
    }

    func testPreScanWithMeasuredServerShowsTwelveWithLocalFirstAllPending() {
        let entries = ProphetView.prophetRosterEntries(localServer: "192.168.1.1", responses: nil)

        XCTAssertEqual(entries.count, ProphetOracleRoster.publicOracles.count + 1)
        XCTAssertEqual(entries.first?.name, "Local (192.168.1.1)")
        XCTAssertTrue(entries.allSatisfy { $0.answer == .notQueried })
    }

    func testPostScanPopulatesMeasuredAndUnknownLegsHonestly() {
        // A real run: Local + Cloudflare returned addresses; Google was unreachable.
        let responses = [
            response("Local (192.168.1.1)", "104.16.132.229"),
            response("Cloudflare (1.1.1.1)", "104.16.132.229"),
            response("Google (8.8.8.8)", "[unreachable]")
        ]
        let entries = ProphetView.prophetRosterEntries(localServer: "192.168.1.1", responses: responses)

        func answer(_ name: String) -> ProphetView.ResolverAnswerState? {
            entries.first { $0.name == name }?.answer
        }

        XCTAssertEqual(answer("Local (192.168.1.1)"), .measured("104.16.132.229"))
        XCTAssertEqual(answer("Cloudflare (1.1.1.1)"), .measured("104.16.132.229"))
        // The unreachable leg stays unknown — never promoted to a measured address.
        XCTAssertEqual(answer("Google (8.8.8.8)"), .unknown("[unreachable]"))
        // A resolver the run did not report is pending, not blank-as-clean.
        XCTAssertEqual(answer("Quad9 (9.9.9.9)"), .notQueried)

        // Honesty invariant: no leg is ever fabricated as a usable address it did not return.
        let measuredNames = Set(entries.compactMap { entry -> String? in
            if case .measured = entry.answer { return entry.name }
            return nil
        })
        XCTAssertFalse(measuredNames.contains("Google (8.8.8.8)"))
        XCTAssertFalse(measuredNames.contains("Quad9 (9.9.9.9)"))
        // Failure case: if an absent or unreachable leg were mapped to .measured, these
        // membership checks go red.
    }
}

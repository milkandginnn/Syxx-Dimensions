import XCTest
@testable import SyxxApp

/// Phase 3.4: Cage capture ticker uses cageEventsCount % 1000.
final class DashboardTickerTests: XCTestCase {

    func testZero() {
        XCTAssertEqual(DashboardWardenState.cageCaptureDisplayCount(0), 0)
        XCTAssertEqual(DashboardWardenState.cageCaptureLabel(0), "Captures: 0")
    }

    func testNineHundredNinetyNine() {
        XCTAssertEqual(DashboardWardenState.cageCaptureDisplayCount(999), 999)
    }

    func testOneThousandRollsToZero() {
        XCTAssertEqual(DashboardWardenState.cageCaptureDisplayCount(1000), 0)
        XCTAssertEqual(DashboardWardenState.cageCaptureLabel(1000), "Captures: 0")
    }

    func testOneThousandOne() {
        XCTAssertEqual(DashboardWardenState.cageCaptureDisplayCount(1001), 1)
    }

    func testNegativeClampsToZero() {
        XCTAssertEqual(DashboardWardenState.cageCaptureDisplayCount(-5), 0)
    }

    /// Failure case: naive display of full count would not roll over.
    func testModuloIsRequired() {
        let raw = 1000
        XCTAssertNotEqual(raw, DashboardWardenState.cageCaptureDisplayCount(raw))
        XCTAssertEqual(raw % 1000, DashboardWardenState.cageCaptureDisplayCount(raw))
    }

    func testInconclusiveToneIsNeverSuccess() {
        XCTAssertEqual(DashboardWardenState.chipTone(for: "INCONCLUSIVE"), .inconclusive)
        XCTAssertEqual(DashboardWardenState.chipTone(for: "DEGRADED"), .inconclusive)
        XCTAssertNotEqual(DashboardWardenState.chipTone(for: "INCONCLUSIVE"), .success)
    }

    func testTamperingToneIsThreat() {
        XCTAssertEqual(DashboardWardenState.chipTone(for: "TAMPERING DETECTED"), .threat)
        XCTAssertNotEqual(DashboardWardenState.chipTone(for: "TAMPERING DETECTED"), .success)
    }
}

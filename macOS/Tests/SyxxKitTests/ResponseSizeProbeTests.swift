import XCTest
@testable import SyxxKit

/// Offline tests for the response-size / amplification datapoint (Workstream H · F9). Supporting
/// telemetry: it flags only a *sustained* run of large responses, not a lone large DNSSEC/TXT answer.
final class ResponseSizeProbeTests: XCTestCase {

    func testNormalSizes() {
        let sizes = Array(repeating: 120, count: 10) + [300, 450]
        XCTAssertEqual(ResponseSizeProbe.audit(responseSizes: sizes).status, .normal)
    }

    func testSustainedLargeIsOversized() {
        // Explicit [Int]: `[literal] + [literal]` is ambiguous — IndexPath is also
        // ExpressibleByArrayLiteral and defines `+`, and CI's toolchain resolved it that way
        // ("cannot convert IndexPath", then "type 'Equatable' has no member 'oversized'").
        let sizes: [Int] = [100, 120, 150, 200, 180, 90] + [2100, 3000, 2500, 4096]   // 4 of 10 ≥ 2048
        let a = ResponseSizeProbe.audit(responseSizes: sizes)
        XCTAssertEqual(a.status, .oversized)
        XCTAssertEqual(a.oversizedCount, 4)
        XCTAssertEqual(a.maxBytes, 4096)
    }

    func testLoneLargeAnswerIsNotFlagged() {
        // A single big DNSSEC-style answer among many normal responses must NOT cry wolf.
        let sizes = Array(repeating: 150, count: 11) + [3000]
        XCTAssertEqual(ResponseSizeProbe.audit(responseSizes: sizes).status, .normal)
    }

    /// Boundary: a response exactly AT largeThreshold counts as large (>=); one byte under does not.
    /// Guards an off-by-one flip of >= to > in the size filter that the in-band cases can't catch.
    func testResponseSizeThresholdBoundary() {
        let atThreshold = Array(repeating: ResponseSizeProbe.largeThreshold, count: ResponseSizeProbe.minSamples)
        XCTAssertEqual(ResponseSizeProbe.audit(responseSizes: atThreshold).status, .oversized,
            "responses exactly at the large threshold must count as large (>=)")
        let justUnder = Array(repeating: ResponseSizeProbe.largeThreshold - 1, count: ResponseSizeProbe.minSamples)
        XCTAssertEqual(ResponseSizeProbe.audit(responseSizes: justUnder).status, .normal,
            "responses one byte under the threshold must not count as large")
    }

    func testInsufficientDataIsNeverClean() {
        XCTAssertEqual(ResponseSizeProbe.audit(responseSizes: [100, 2500, 3000]).status, .insufficientData)
    }
}

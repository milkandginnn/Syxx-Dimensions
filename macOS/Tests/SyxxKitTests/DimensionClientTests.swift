import XCTest
@testable import SyxxKit

/// Offline tests for the Dimension API client (injected transport — no network).
final class DimensionClientTests: XCTestCase {
    private let base = URL(string: "https://dim.example")!

    func testAuthoritativeDecodes() async {
        let json = #"{"name":"example.com","type":"A","conclusion":"authoritative","authoritative":{"answers":["93.184.216.34"],"rcode":0,"signed":true},"delegation":[{"zone":"."}]}"#
        let transport: DimensionClient.Transport = { _ in (Data(json.utf8), 200) }
        let c = DimensionClient(baseURL: base, licenseKey: "LIC-123", transport: transport)
        let outcome = await c.authoritative(domain: "example.com")
        // D081: Verify the outcome is .fetched, not nil — callers must pattern-match.
        if case .fetched(let gt) = outcome {
            XCTAssertEqual(gt.result, "authoritative")
            XCTAssertEqual(gt.authoritative?.answers, ["93.184.216.34"])
            XCTAssertEqual(gt.authoritative?.signed, true)
        } else {
            XCTFail("Expected .fetched(GroundTruth) but got \(outcome)")
        }
    }

    func testNon200ReturnsTransportFailure() async {
        let transport: DimensionClient.Transport = { _ in (Data("{}".utf8), 402) }   // entitlement required
        let c = DimensionClient(baseURL: base, transport: transport)
        let outcome = await c.authoritative(domain: "x.com")
        // D081: Non-200 is .transportFailure, NOT nil or .fetched.
        if case .transportFailure = outcome {
            // correct
        } else {
            XCTFail("Expected .transportFailure but got \(outcome)")
        }
    }

    func testUnreachableTransportReturnsTransportFailure() async {
        let transport: DimensionClient.Transport = { _ in nil }
        let c = DimensionClient(baseURL: base, transport: transport)
        let outcome = await c.authoritative(domain: "x.com")
        // D081: Network failure is .transportFailure, NOT nil.
        if case .transportFailure = outcome {
            // correct
        } else {
            XCTFail("Expected .transportFailure but got \(outcome)")
        }
    }

    func testDiffDecodes() async {
        let json = #"{"domain":"example.com","type":"A","localAnswers":["9.9.9.9"],"match":false,"assessment":"divergent","reference":"authoritative"}"#
        let transport: DimensionClient.Transport = { _ in (Data(json.utf8), 200) }
        let c = DimensionClient(baseURL: base, licenseKey: "K", transport: transport)
        let outcome = await c.diff(domain: "example.com", localAnswers: ["9.9.9.9"])
        // D081: Verify the outcome is .fetched, not nil.
        if case .fetched(let d) = outcome {
            XCTAssertEqual(d.assessment, "divergent")
            XCTAssertEqual(d.match, false)
            XCTAssertEqual(d.reference, "authoritative")
        } else {
            XCTFail("Expected .fetched(DiffAssessment) but got \(outcome)")
        }
    }

    func testMalformedResponseReturnsDecodeFailure() async {
        let transport: DimensionClient.Transport = { _ in (Data("not json at all".utf8), 200) }
        let c = DimensionClient(baseURL: base, transport: transport)
        let outcome = await c.authoritative(domain: "x.com")
        // D081: Undecodable response is .decodeFailure, NOT nil or .fetched.
        if case .decodeFailure = outcome {
            // correct
        } else {
            XCTFail("Expected .decodeFailure but got \(outcome)")
        }
    }
}

import XCTest
@testable import SyxxKit

/// Offline tests for the SVCB/HTTPS presence datapoint (Workstream H · A3).
final class SVCBProbeTests: XCTestCase {

    private static func https(_ q: DNSQuery) -> DNSResponse {
        DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 65, ttl: 300, rdata: "1 . alpn=h3")])
    }
    private static func noHTTPS(_ q: DNSQuery) -> DNSResponse { DNSResponse(status: .noData) }

    func testPresentWhenAllResolversServeIt() async {
        let query: SVCBProbe.QueryFn = { q in Self.https(q) }
        let r = await SVCBProbe.probe(domain: "example.com", resolvers: ["1.1.1.1", "8.8.8.8"], query: query)
        XCTAssertEqual(r.status, .present)
        XCTAssertEqual(r.presentAt.count, 2)
    }

    func testAbsentWhenNoneServeIt() async {
        let query: SVCBProbe.QueryFn = { q in Self.noHTTPS(q) }
        let r = await SVCBProbe.probe(domain: "example.com", resolvers: ["1.1.1.1", "8.8.8.8"], query: query)
        XCTAssertEqual(r.status, .absent)
    }

    func testDivergentWhenOneStripsIt() async {
        // 1.1.1.1 serves the HTTPS record; 8.8.8.8 returns none → stripping signal.
        let query: SVCBProbe.QueryFn = { q in q.server == "1.1.1.1" ? Self.https(q) : Self.noHTTPS(q) }
        let r = await SVCBProbe.probe(domain: "example.com", resolvers: ["1.1.1.1", "8.8.8.8"], query: query)
        XCTAssertEqual(r.status, .divergent)
        XCTAssertEqual(r.presentAt, ["1.1.1.1"])
        XCTAssertEqual(r.absentAt, ["8.8.8.8"])
    }

    func testInconclusiveWhenAllFail() async {
        let query: SVCBProbe.QueryFn = { _ in DNSResponse(status: .timeout) }
        let r = await SVCBProbe.probe(domain: "example.com", resolvers: ["1.1.1.1", "8.8.8.8"], query: query)
        XCTAssertEqual(r.status, .inconclusive)
        XCTAssertEqual(r.failedAt.count, 2)
    }

    func testClassifyDirect() {
        XCTAssertEqual(SVCBProbe.classify(present: ["a"], absent: ["b"], failed: []).status, .divergent)
        XCTAssertEqual(SVCBProbe.classify(present: ["a", "b"], absent: [], failed: []).status, .present)
        XCTAssertEqual(SVCBProbe.classify(present: [], absent: ["a", "b"], failed: []).status, .absent)
        XCTAssertEqual(SVCBProbe.classify(present: [], absent: [], failed: ["a"]).status, .inconclusive)
    }
}

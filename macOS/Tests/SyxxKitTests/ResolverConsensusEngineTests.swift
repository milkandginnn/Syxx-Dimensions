import XCTest
@testable import SyxxKit

/// Stream A: offline coverage for the unified `ResolverConsensusEngine` — the single canonical
/// roster, the symmetric peer-consensus derivation, and the shared last-run store. The live fan-out
/// needs a network and is exercised end-to-end elsewhere; these tests pin the pure wiring that makes
/// the Kit share ONE roster + ONE store. Twin of the Windows `ResolverConsensusEngineTests`.
final class ResolverConsensusEngineTests: XCTestCase {

    private func r(_ name: String, _ ip: String) -> ResolverConsensusEngine.PeerResponse {
        ResolverConsensusEngine.PeerResponse(resolver: name, ip: ip)
    }

    // MARK: - One canonical roster: the façades are projections, never separate lists

    func testPublicResolversAreProjectionOfEngineRoster() {
        // macOS Mirror keeps the full 11-resolver declaration; every entry must resolve to the same
        // IP the single engine roster holds — no hand-maintained copy.
        XCTAssertEqual(VantageComparer.publicResolvers.count, ResolverConsensusEngine.publicResolvers.count)
        for (name, ip) in VantageComparer.publicResolvers {
            let engineIp = ResolverConsensusEngine.publicResolvers.first { $0.name == name }?.ip
            XCTAssertEqual(engineIp, ip, "\(name) IP must come from the single engine roster")
        }
        // DoH endpoints are exactly the engine's canonical DoH map.
        XCTAssertEqual(VantageComparer.dohEndpoints, ResolverConsensusEngine.dohEndpoints)
    }

    func testRosterIsLocalFirstAndTwelveEntries() {
        let roster = ResolverConsensusEngine.roster(localServer: "192.168.1.1")
        XCTAssertEqual(roster.count, ResolverConsensusEngine.publicResolvers.count + 1)
        XCTAssertEqual(roster.first?.name, "Local (192.168.1.1)")
        XCTAssertEqual(roster.first?.ip, "192.168.1.1")
    }

    // MARK: - Symmetric peer-consensus: agree / disagree / insufficient / inconclusive

    func testClassifyPeerAllSameIpIsAgree() {
        let responses = [
            r("Local (192.168.1.1)", "93.184.216.34"),
            r("Cloudflare (1.1.1.1)", "93.184.216.34"),
            r("Google (8.8.8.8)", "93.184.216.34")
        ]
        XCTAssertEqual(ResolverConsensusEngine.classifyPeer(responses), .agree)
    }

    func testClassifyPeerDifferentIpsIsDisagree() {
        let responses = [
            r("Local (192.168.1.1)", "93.184.216.34"),
            r("Cloudflare (1.1.1.1)", "10.0.0.5")
        ]
        XCTAssertEqual(ResolverConsensusEngine.classifyPeer(responses), .disagree)
    }

    func testClassifyPeerSingleReachableIsInsufficient() {
        let responses = [
            r("Local (192.168.1.1)", "93.184.216.34"),
            r("Cloudflare (1.1.1.1)", "[unreachable]")
        ]
        XCTAssertEqual(ResolverConsensusEngine.classifyPeer(responses), .insufficient)
    }

    func testClassifyPeerNoneReachableIsInconclusiveNeverAgree() {
        // HONESTY: an all-unreachable run is inconclusive, never silently promoted to agreement.
        let responses = [
            r("Local (192.168.1.1)", "[unreachable]"),
            r("Cloudflare (1.1.1.1)", "[unreachable]")
        ]
        XCTAssertEqual(ResolverConsensusEngine.classifyPeer(responses), .inconclusive)
    }

    // MARK: - Shared last-run store

    func testStoreRecordsAndReadsBackKeyedByDomainCaseInsensitively() {
        ResolverConsensusStore.clear()
        XCTAssertNil(ResolverConsensusStore.lastRun("example.com"))

        let snap = ResolverConsensusSnapshot(
            domain: "example.com", timestamp: Date(),
            peerConsensus: .agree, reachableCount: 12, localDelta: nil)
        ResolverConsensusStore.record(snap)

        // DNS names are case-insensitive; the store must read back either casing.
        XCTAssertEqual(ResolverConsensusStore.lastRun("EXAMPLE.com"), snap)
        XCTAssertEqual(ResolverConsensusStore.lastRun("example.com")?.peerConsensus, .agree)
    }
}

import XCTest
@testable import SyxxKit

// ONE-OFF fixture generator for WS-B5 (Ubuntu port plan Section 2). NOT part of the standing
// suite: it makes live DNS queries and is deleted once the fixture is captured. It exists so the
// probe-parity fixture is a REAL capture produced by the actual probe layer (DNSResolver +
// VantageComparer.evaluate), never a hand-authored synthetic mock. Run with:
//   swift test --filter ProbeParityFixtureGenerator
// then delete this file; the committed artifact is corpus/probe-parity/probe-parity.json.
final class ProbeParityFixtureGenerator: XCTestCase {

    private struct FxRecord: Codable { let name: String; let type: UInt16; let ttl: UInt32; let rdata: String }
    private struct FxOutcome: Codable { let status: String; let records: [FxRecord] }
    private struct FxExpected: Codable { let redirection: Bool; let inconclusive: Bool; let degraded: Bool }
    private struct FxVector: Codable {
        let id: String
        let probe: String
        let description: String
        let provenance: String
        let expected: FxExpected
        let localOutcome: FxOutcome
        let publicOutcomes: [String: FxOutcome]
        let dohOutcomes: [String: FxOutcome]?
    }
    private struct FxFile: Codable {
        let _schema: String
        let _note: String
        let _todo: [String]
        let vectors: [FxVector]
    }

    private func fx(_ o: ResolverQueryOutcome) -> FxOutcome {
        FxOutcome(status: o.status.rawValue,
                  records: o.records.map { FxRecord(name: $0.name, type: $0.type, ttl: $0.ttl, rdata: $0.rdata) })
    }

    // Set SYXX_GENERATE_FIXTURES=1 to actually run; otherwise skipped so a stray `swift test`
    // never fires live queries.
    func testGenerateProbeParityFixture() async throws {
        guard ProcessInfo.processInfo.environment["SYXX_GENERATE_FIXTURES"] == "1" else {
            throw XCTSkip("set SYXX_GENERATE_FIXTURES=1 to capture live fixtures")
        }

        let domain = "example.com"

        // Live public baselines from the three public resolvers (REAL captures).
        var publics: [String: ResolverQueryOutcome] = [:]
        publics["Cloudflare"] = ResolverQueryOutcome(response: await DNSResolver.queryUDPDetailed(
            DNSQuery(name: domain, type: 1, server: "1.1.1.1")))
        publics["Google"] = ResolverQueryOutcome(response: await DNSResolver.queryUDPDetailed(
            DNSQuery(name: domain, type: 1, server: "8.8.8.8")))
        publics["Quad9"] = ResolverQueryOutcome(response: await DNSResolver.queryUDPDetailed(
            DNSQuery(name: domain, type: 1, server: "9.9.9.9")))

        // Local leg pointed at 192.0.2.1 (TEST-NET-1, RFC 5737, unroutable). On a clean network this
        // times out; on THIS capture network it was answered by a transparent on-path :53 interceptor
        // with example.com's real IPs (an honest real-world finding). The answer overlaps the public
        // consensus, so evaluate() correctly reads it as AGREE, not redirection.
        let localIntercept = ResolverQueryOutcome(response: await DNSResolver.queryUDPDetailed(
            DNSQuery(name: domain, type: 1, server: "192.0.2.1")))
        let vAgree = VantageComparer.evaluate(localOutcome: localIntercept, publicOutcomes: publics, dohOutcomes: nil)
        let vecAgree = FxVector(
            id: "AGREE-onpath-intercept-benign",
            probe: "VantageComparer.evaluate",
            description: "A local query was answered by a transparent on-path :53 interceptor with an "
                + "answer that overlaps the public consensus. Contract: an answer overlapping the "
                + "public union is AGREE (no redirection) — evaluate() must not cry wolf on a "
                + "non-rewriting interceptor; a rewriting one is caught by the B010 DoH cross-check.",
            provenance: "REAL capture via DNSResolver.queryUDPDetailed on macOS. Local leg pointed at "
                + "192.0.2.1 (TEST-NET-1, RFC 5737, unroutable). This capture network transparently "
                + "intercepts outbound UDP/53 and answered with example.com's real Cloudflare IPs; the "
                + "public legs are live answers from 1.1.1.1 / 8.8.8.8 / 9.9.9.9.",
            expected: FxExpected(redirection: vAgree.redirection, inconclusive: vAgree.inconclusive, degraded: vAgree.degraded),
            localOutcome: fx(localIntercept),
            publicOutcomes: publics.mapValues(fx),
            dohOutcomes: nil)

        // Timeout fixture: a content-free timeout local leg (a timeout carries zero response bytes,
        // so {status: timeout, records: []} is its complete honest representation) against the SAME
        // REAL public baselines captured above. Contract: a timed-out local leg is INCONCLUSIVE even
        // when the world answered — never collapsed to clean.
        let localTimeout = ResolverQueryOutcome(records: [], status: .timeout)
        let v1 = VantageComparer.evaluate(localOutcome: localTimeout, publicOutcomes: publics, dohOutcomes: nil)
        XCTAssertTrue(v1.inconclusive, "a timed-out local leg must be inconclusive")
        let vec1 = FxVector(
            id: "TIMEOUT-local-timeout",
            probe: "VantageComparer.evaluate",
            description: "The local resolver leg timed out (no response) while the three public "
                + "baselines answered. Contract: a timeout is INCONCLUSIVE, never collapsed to a "
                + "clean 'no redirection' result.",
            provenance: "Local leg is the content-free timeout state (a timeout carries zero response "
                + "bytes: {status: timeout, records: []}). Public legs are REAL live captures from "
                + "1.1.1.1 / 8.8.8.8 / 9.9.9.9 for example.com. No fabricated record content.",
            expected: FxExpected(redirection: v1.redirection, inconclusive: v1.inconclusive, degraded: v1.degraded),
            localOutcome: fx(localTimeout),
            publicOutcomes: publics.mapValues(fx),
            dohOutcomes: nil)

        // Second, fully content-free timeout fixture: every leg timed out (a UDP/53-blocked network).
        let allTimeout = ResolverQueryOutcome(records: [], status: .timeout)
        let publicsAllTimeout: [String: ResolverQueryOutcome] = [
            "Cloudflare": allTimeout, "Google": allTimeout, "Quad9": allTimeout]
        let v2 = VantageComparer.evaluate(localOutcome: allTimeout, publicOutcomes: publicsAllTimeout, dohOutcomes: nil)
        let vec2 = FxVector(
            id: "TIMEOUT-all-legs",
            probe: "VantageComparer.evaluate",
            description: "Every leg timed out (a fully UDP/53-blocked network). Contract: no baseline "
                + "and a timed-out local leg is INCONCLUSIVE, never clean.",
            provenance: "Content-free honest state: a timeout carries zero response bytes, so every "
                + "outcome is {status: timeout, records: []}. Reproducible on any network that blocks "
                + "UDP/53; no fabricated record content.",
            expected: FxExpected(redirection: v2.redirection, inconclusive: v2.inconclusive, degraded: v2.degraded),
            localOutcome: fx(allTimeout),
            publicOutcomes: publicsAllTimeout.mapValues(fx),
            dohOutcomes: nil)

        let file = FxFile(
            _schema: "probe-parity fixture v1 (WS-B5). VantageComparer.evaluate tri-state replay.",
            _note: "Single source of truth for the standing probe-layer three-way parity harness. "
                + "Read by BOTH Windows/Syxx.Tests/ProbeParityReplayTests.cs and "
                + "macOS/Tests/SyxxKitTests/ProbeParityReplayTests.swift. Status strings are Swift "
                + "DNSResponseStatus rawValues (camelCase); the C# loader normalizes onto its "
                + "PascalCase enum. Each vector is a REAL capture or a content-free honest state; "
                + "NEVER a synthetic mock. 'expected' is produced by the real evaluate() at capture "
                + "time and must match on every platform.",
            _todo: [
                "REWRITE-local-hijack: PENDING one real hijacked-resolver capture (a local resolver "
                + "that rewrites an answer to a disjoint IP -> redirection=true). Cannot be authored "
                + "by hand without fabricating the rewritten records (a synthetic mock, forbidden by "
                + "the honesty spine). Capture on a network with a known DNS hijacker, then add the "
                + "vector here. Tracked in corpus/probe-parity/REWRITE.TODO.md."
            ],
            vectors: [vecAgree, vec1, vec2])

        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        let data = try enc.encode(file)

        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
        let out = root.appendingPathComponent("corpus/probe-parity/probe-parity.json")
        try data.write(to: out)
        print("WROTE FIXTURE: \(out.path) (\(data.count) bytes)")
        print(String(data: data, encoding: .utf8) ?? "<non-utf8>")
    }
}

import XCTest
@testable import SyxxKit

/// D100 — regression coverage for the TTL/type "representative record" tie-break that used to
/// depend on Dictionary/LINQ enumeration order. When multiple public resolvers agree on the same
/// rdata but report different TTLs (the ordinary case on live DNS — TTLs count down independently
/// per resolver cache), `SignedEvidencePayload.build` must always pick the SAME representative TTL
/// and type for a given input set, regardless of what order the candidates are encountered in.
/// Before the fix this depended on `comparison.publicResolverOutcomes.values` enumeration order
/// (Swift randomizes Dictionary iteration per process), so re-signing/re-verifying the identical
/// input across process launches could pick a different TTL each time and make an honest,
/// unmodified snapshot flip between VALID and DISPLAY MISMATCH.
final class D100TtlTieBreakDeterminismTests: XCTestCase {

    /// Directly reproduces the defect shape: several DNSRecords sharing one rdata but different
    /// TTLs, arriving in the SAME resolver's record list but in different array orders (this
    /// dimension of ordering is under our direct control, unlike Dictionary iteration order, so it
    /// is the cleanest reproducible probe for the underlying "pick a representative" logic).
    func testRepresentativeTtlIsOrderIndependent_PicksMinimum() throws {
        let rdata = "93.184.216.34"
        let high = DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: rdata)
        let mid  = DNSRecord(name: "example.com", type: 1, ttl: 120, rdata: rdata)
        let low  = DNSRecord(name: "example.com", type: 1, ttl: 60,  rdata: rdata)

        let orderings: [[DNSRecord]] = [
            [high, mid, low], [low, mid, high], [mid, low, high],
            [high, low, mid], [mid, high, low], [low, high, mid],
        ]

        var seenTtls = Set<UInt32>()
        for ordering in orderings {
            let comparison = SyxxKit.ComparisonResult(
                localServer: "1.1.1.1",
                localRecords: [low],
                publicResolverResults: ["Cloudflare": ordering],
                isRedirectionDetected: false)
            let payload = SignedEvidencePayload.build(
                domain: "example.com", comparison: comparison,
                timestampMillis: 1_700_000_000_000, expiresAtMillis: nil)
            XCTAssertEqual(payload.comparisonResult.authoritativeTtls, [60],
                "must deterministically pick the MINIMUM ttl among agreeing candidates, regardless of input order")
            seenTtls.insert(payload.comparisonResult.authoritativeTtls?.first ?? 0)
        }
        XCTAssertEqual(seenTtls, [60], "every ordering must resolve to the identical representative TTL")
    }

    /// Multi-resolver shape: three resolvers agree on the same rdata but each reports a different
    /// TTL. Swaps which physical resolver key carries which TTL (rather than relying on Dictionary
    /// iteration order, which is fixed-but-unknown within a single process) and asserts the BUILT
    /// BYTES are byte-identical across every permutation — the property `verifySnapshotDetailed`
    /// relies on when it rebuilds the payload from the same comparison shape in a different process.
    func testRepresentativeTtlIsIdenticalAcrossResolverKeyPermutations() throws {
        let rdata = "93.184.216.34"
        let ttls: [UInt32] = [300, 60, 120]
        let keys = ["Cloudflare", "Google", "Quad9"]

        func permutations<T>(_ array: [T]) -> [[T]] {
            if array.count <= 1 { return [array] }
            var result: [[T]] = []
            for (i, element) in array.enumerated() {
                var rest = array
                rest.remove(at: i)
                for perm in permutations(rest) {
                    result.append([element] + perm)
                }
            }
            return result
        }

        var canonicalBytesSeen = Set<Data>()
        for perm in permutations(ttls) {
            var publicResults: [String: [DNSRecord]] = [:]
            for (key, ttl) in zip(keys, perm) {
                publicResults[key] = [DNSRecord(name: "example.com", type: 1, ttl: ttl, rdata: rdata)]
            }
            let comparison = SyxxKit.ComparisonResult(
                localServer: "1.1.1.1",
                localRecords: [DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: rdata)],
                publicResolverResults: publicResults,
                isRedirectionDetected: false)
            let payload = SignedEvidencePayload.build(
                domain: "example.com", comparison: comparison,
                timestampMillis: 1_700_000_000_000, expiresAtMillis: nil)
            XCTAssertEqual(payload.comparisonResult.authoritativeTtls, [60])
            canonicalBytesSeen.insert(try payload.canonicalBytes())
        }
        XCTAssertEqual(canonicalBytesSeen.count, 1,
            "the signed bytes must be byte-identical across every resolver/TTL assignment permutation")
    }
}

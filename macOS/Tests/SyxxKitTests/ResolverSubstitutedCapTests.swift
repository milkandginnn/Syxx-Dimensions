import XCTest
@testable import SyxxKit

/// F-01 / B174 — when the system resolver is undetectable, Syxx substitutes a public resolver.
/// Mirror's whole method is a local-versus-public comparison, so under substitution local IS public
/// and the comparison degenerates into comparing a resolver against itself. The Mirror section must
/// therefore be capped at `.degraded` and can never come back clean on that evidence.
///
/// The cap has existed here since B174 (`Synthesizer.swift:772`) and had **no test on either
/// platform** — found while porting it to C# as F-01. This is that test.
///
/// Twin of Windows/dotNET/Syxx.Tests/ResolverSubstitutedCapTests.cs.
final class ResolverSubstitutedCapTests: XCTestCase {

    private static let fixedNow = Date(timeIntervalSince1970: 1_752_268_477)

    /// A comparison that reaches Mirror's `.negative` branch: not inconclusive, no redirection,
    /// not degraded, and with local records present so the B136 "compared nothing" guard does not
    /// divert it to `.inconclusive`.
    private func inputsWithCleanMirror() -> SectionInputs {
        let record = DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: "93.184.216.34")
        var inputs = SectionInputs()
        inputs.comparison = ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [record],
            publicResolverResults: ["Cloudflare": [record]],
            isRedirectionDetected: false,
            isInconclusive: false,
            isDegraded: false
        )
        return inputs
    }

    private func mirrorStatus(_ c: Conclusion) throws -> SectionStatus {
        let entry = try XCTUnwrap(c.sectionStatuses.first { $0.section == .mirror })
        return entry.status
    }

    func testResolverSubstitutedCapsMirrorAtDegraded() throws {
        // Precondition, asserted rather than assumed: without substitution these inputs really do
        // produce a clean Mirror. If that ever stops holding, the cap assertion below would pass
        // vacuously and prove nothing.
        let uncapped = Synthesizer.conclude(from: inputsWithCleanMirror(), now: Self.fixedNow)
        XCTAssertEqual(try mirrorStatus(uncapped), .negative,
                       "precondition: unsubstituted inputs must yield a clean Mirror")

        var inputs = inputsWithCleanMirror()
        inputs.resolverSubstituted = true
        let capped = Synthesizer.conclude(from: inputs, now: Self.fixedNow)

        XCTAssertEqual(try mirrorStatus(capped), .degraded,
                       "substituted evidence must never leave Mirror clean")
    }

    /// The cap lowers a clean Mirror; it must not raise or otherwise disturb a section already at
    /// or below `.degraded`, which would be the cap inventing severity.
    func testResolverSubstitutedDoesNotDisturbANonCleanMirror() throws {
        var baselineInputs = inputsWithCleanMirror()
        baselineInputs.comparison = nil
        let baseline = try mirrorStatus(Synthesizer.conclude(from: baselineInputs, now: Self.fixedNow))

        var substituted = inputsWithCleanMirror()
        substituted.comparison = nil
        substituted.resolverSubstituted = true

        XCTAssertEqual(try mirrorStatus(Synthesizer.conclude(from: substituted, now: Self.fixedNow)),
                       baseline)
    }
}

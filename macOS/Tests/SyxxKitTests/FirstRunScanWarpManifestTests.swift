import XCTest
@testable import SyxxKit

/// G-03 — the first-run Mirror fold must not drop the content-integrity result.
/// Twin of the corresponding cases in `Windows/dotNET/Syxx.Tests/FirstRunScanTests.cs`.
///
/// `FirstRunScan.merge` folded only the RETIRED `warp` shim out of ProbePlan's Mirror inputs.
/// Nothing populates that shim (`ContentWarp.swift:21`); ProbePlan fills `warpManifest` alone
/// (`ProbePlan.swift:107,155`). Synthesis has always read `warpManifest`
/// (`Synthesizer.swift:737`), so the fold was the entire defect: Mirror's content check was absent
/// from every first-run scan — the first thing a new user ever sees — and no test noticed, because
/// the only caller is `runAsync`, which dials real resolvers and cannot be exercised by the suite.
/// The fix carries `warpManifest`; `merge` is internal so this file can reach it.
final class FirstRunScanWarpManifestTests: XCTestCase {

    /// A manifest the §5 lattice has already concluded on. Not a hand-waved fixture:
    /// `.tamperingDetected` with an HTML byte mismatch is the case that MUST reach the user.
    private func tamperingManifest() -> WarpManifestResult {
        WarpManifestResult(
            originURL: "https://github.com/",
            finalURL: nil,
            keyId: "warp-test-key",
            signatureState: .verified,
            manifestState: .parsed,
            freshnessState: .fresh,
            htmlState: .mismatch,
            declaredHtmlSha256: String(repeating: "a", count: 64),
            fetchedHtmlSha256: String(repeating: "b", count: 64),
            assets: [],
            conclusion: .tamperingDetected,
            inconclusiveReason: nil)
    }

    func testMerge_carriesThePopulatedWarpManifest_notJustTheRetiredShim() {
        var into = SectionInputs()
        var from = SectionInputs()
        from.warpManifest = tamperingManifest()

        FirstRunScan.merge(&into, from)

        XCTAssertNotNil(into.warpManifest)
        XCTAssertEqual(into.warpManifest?.conclusion, .tamperingDetected)
        XCTAssertEqual(into.warpManifest?.htmlState, .mismatch)
        XCTAssertEqual(into.warpManifest?.keyId, "warp-test-key")
    }

    func testMerge_nilManifest_doesNotErasePreviouslyGatheredEvidence() {
        // The fold's other honesty rule: absent evidence never overwrites present evidence.
        var into = SectionInputs()
        into.warpManifest = tamperingManifest()

        FirstRunScan.merge(&into, SectionInputs())

        XCTAssertNotNil(into.warpManifest)
    }

    func testFirstRunScan_surfacesContentTampering_onceTheManifestSurvivesTheFold() {
        // The user-visible half of G-03. With the manifest folded in, the real engine raises
        // Mirror `.scriptMismatch` evidence; while the fold dropped it, a first-run scan against a
        // tampered page reported nothing at all about content.
        var inputs = SectionInputs()
        var mirror = SectionInputs()
        mirror.warpManifest = tamperingManifest()
        FirstRunScan.merge(&inputs, mirror)

        let r = FirstRunScan.summarize(inputs)

        XCTAssertTrue(r.conclusion.evidence.contains {
            $0.section == .mirror && $0.evidenceId == .scriptMismatch
        }, "content tampering under a verified, fresh manifest must reach the conclusion")
        XCTAssertNotEqual(r.conclusion.determination?.state, .clean,
                          "content tampering from a verified, fresh manifest must never read clean")
    }
}

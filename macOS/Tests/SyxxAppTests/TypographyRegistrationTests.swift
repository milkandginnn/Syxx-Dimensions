import AppKit
import CoreText
import XCTest
@testable import SyxxApp

/// Phase 0 — proof that the bundled typography actually REGISTERS and RESOLVES.
///
/// Why this file exists separately from `TypographyResourcesTests`: that suite proves the
/// `.ttf` files can be *located* and that they *declare* the right PostScript names. It never
/// registered them, so it could not catch the real defect — faces moved into family
/// subfolders, `Bundle.module.url(forResource:withExtension:)` stopped resolving (it does not
/// recurse), `CTFontManagerRegisterFontsForURL` was never called, its `CFError` was discarded
/// with `_ =`, and every `Font.custom(...)` fell back to the system font with no error
/// anywhere. The app "worked". It just silently stopped being the designed product.
///
/// Every test below states its mutation failure case inline.
final class TypographyRegistrationTests: XCTestCase {

    // MARK: - The load-bearing assertion

    /// Runs the PRODUCTION registration path and requires every declared face to resolve.
    func testEveryBundledFontResolvesAfterRegistration() {
        let report = SyxxTypography.registerBundledFonts()

        XCTAssertEqual(
            report.outcome, .allRegistered,
            "Font registration did not fully succeed. Failures:\n"
            + report.failures.map { "  ✗ " + $0.diagnostic }.joined(separator: "\n")
        )
        XCTAssertTrue(report.failures.isEmpty, "Unexpected failures: \(report.failures)")
        XCTAssertEqual(report.registered.count, SyxxTypography.bundledFonts.count)

        // Independent of the report: ask AppKit directly, one face at a time.
        for font in SyxxTypography.bundledFonts {
            let resolved = NSFont(name: font.postScriptName, size: 13)
            XCTAssertNotNil(
                resolved,
                "'\(font.postScriptName)' does not resolve after registration — "
                + "every Font.custom(\"\(font.postScriptName)\", …) is silently rendering "
                + "as the system font."
            )
            // NSFont(name:) must have handed back the face we asked for, not a near-match.
            XCTAssertEqual(resolved?.fontName, font.postScriptName)
        }
        // Failure case: delete or move Sources/SyxxApp/Resources/Jost-Light.ttf into a
        // family subfolder → bundledResourceURL returns nil → .resourceMissing →
        // outcome becomes .partiallyRegistered → this test goes RED.
    }

    /// The above assertion is only meaningful if `NSFont(name:)` refuses to substitute.
    /// If AppKit silently returned Helvetica for an unknown name, the test would be a no-op.
    func testNSFontDoesNotSubstituteForAnUnknownPostScriptName() {
        XCTAssertNil(
            NSFont(name: "Not-A-Syxx-Font-9f3c", size: 13),
            "NSFont(name:) substituted for an unknown face; the resolution assertions above "
            + "would then pass even with zero fonts registered."
        )
        // Failure case: if this ever starts returning a font, every resolution test in this
        // file is void and must be rewritten against CTFontDescriptor matching instead.
    }

    // MARK: - Failure-path proof (the detector must be able to detect)

    func testMissingResourceIsReportedAsResourceMissingAndNotSilentlyIgnored() {
        let bogus = SyxxBundledFont(resourceName: "Syxx-No-Such-Face-4b21",
                                    postScriptName: "Syxx-No-Such-Face-4b21")
        let report = SyxxTypography.register([bogus])

        XCTAssertEqual(report.outcome, .noneRegistered)
        XCTAssertTrue(report.registered.isEmpty)
        XCTAssertEqual(report.failures.count, 1)
        XCTAssertEqual(report.failures.first?.reason, .resourceMissing)
        XCTAssertTrue(report.failures.first?.diagnostic.contains("non-recursive") == true,
                      "The diagnostic must name the actual root cause, not just 'not found'.")
        // Failure case: make register() skip unresolvable names (the old `if let url` shape)
        // → failures is empty, outcome flips to .noneRegistered-with-no-reason → fails.
    }

    func testPartialRegistrationIsNeitherSuccessNorFailure() {
        // One real face + one broken face. A Bool return would have to lie in one direction;
        // the tri-state must report .partiallyRegistered.
        let good = SyxxBundledFont(resourceName: "Cinzel-Regular", postScriptName: "Cinzel-Regular")
        let bad = SyxxBundledFont(resourceName: "Syxx-No-Such-Face-7c90",
                                  postScriptName: "Syxx-No-Such-Face-7c90")
        let report = SyxxTypography.register([good, bad])

        XCTAssertEqual(report.outcome, .partiallyRegistered)
        XCTAssertEqual(report.registered, ["Cinzel-Regular"])
        XCTAssertEqual(report.failures.count, 1)
        XCTAssertNotEqual(report.outcome, .allRegistered)
        XCTAssertNotEqual(report.outcome, .noneRegistered)
        // Failure case: collapse FontRegistrationOutcome to a Bool, or treat any success as
        // overall success → .allRegistered → fails.
    }

    func testManifestPostScriptNameMismatchIsReportedNotSwallowed() {
        // Real file, wrong declared PostScript name: the file registers fine, but the name
        // the UI asks for will never resolve. This must surface as a failure, not a pass.
        let mislabelled = SyxxBundledFont(resourceName: "Cinzel-Regular",
                                          postScriptName: "Cinzel-Regular-Typo")
        let report = SyxxTypography.register([mislabelled])

        XCTAssertEqual(report.outcome, .noneRegistered)
        XCTAssertEqual(report.failures.first?.reason, .postScriptNameUnresolved)
        // Failure case: verify registration by CTFontManager's return value instead of by
        // NSFont resolution → the mislabelled face reports success → fails.
    }

    // MARK: - Packaging invariant that caused the regression

    func testEveryDeclaredFaceSitsAtTheResourceBundleRoot() throws {
        let root = try XCTUnwrap(Bundle.module.resourceURL).resolvingSymlinksInPath()
                                                           .standardizedFileURL
        for font in SyxxTypography.bundledFonts {
            let url = try XCTUnwrap(
                SyxxTypography.bundledResourceURL(named: font.resourceName),
                "\(font.resourceName).ttf is not resolvable at all"
            )
            let parent = url.resolvingSymlinksInPath().standardizedFileURL
                            .deletingLastPathComponent()
            XCTAssertEqual(
                parent.path, root.path,
                "\(font.resourceName).ttf lives at \(parent.path), not the resource root. "
                + "Bundle.module.url(forResource:withExtension:) does not recurse, so a face "
                + "in a family subfolder is unreachable at runtime."
            )
        }
        // Failure case: move Cinzel-Bold.ttf into Resources/Cinzel/ and delete the flat copy
        // → parent becomes …/Resources/Cinzel → fails. This is the exact regression that
        // shipped silently before Phase 0.
    }

    func testAllBundledFamiliesAreRepresentedAfterRegistration() {
        SyxxTypography.registerBundledFonts()
        let families = Set(SyxxTypography.bundledFonts.compactMap {
            NSFont(name: $0.postScriptName, size: 13)?.familyName
        })
        // Cormorant SC (card labels) and Cormorant Garamond (body) are distinct families
        // from the same upstream project; both must resolve, not just one.
        for expected in ["Cinzel", "Jost", "Cormorant SC", "Cormorant Garamond",
                         "EB Garamond", "JetBrains Mono", "Spectral", "Marcellus SC"] {
            XCTAssertTrue(
                families.contains(where: { $0.localizedCaseInsensitiveContains(expected) }),
                "Family '\(expected)' is absent after registration. Resolved families: \(families.sorted())"
            )
        }
        // Failure case: drop all three JetBrainsMono faces from Resources → the mono family
        // vanishes → fails. (Guards against a whole family silently disappearing while the
        // per-face count still looks plausible.)
    }

    // MARK: - Licences must ship with the faces (OFL requirement)

    func testEveryOFLLicenceShipsBesideTheFaces() throws {
        for name in SyxxTypography.bundledLicenseFiles {
            let url = try XCTUnwrap(SyxxTypography.bundledLicenseURL(named: name),
                                    "Missing OFL licence \(name); shipping the face without it "
                                    + "violates the SIL Open Font License.")
            let text = try String(contentsOf: url, encoding: .utf8)
            XCTAssertTrue(text.contains("SIL OPEN FONT LICENSE"),
                          "\(name) does not contain the OFL text")
        }
        // Failure case: delete OFL-Jost.txt, or replace it with an empty file → fails.
    }
}

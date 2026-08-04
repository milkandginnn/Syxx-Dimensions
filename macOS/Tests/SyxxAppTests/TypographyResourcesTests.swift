import CoreText
import SwiftUI
import XCTest
@testable import SyxxApp

/// Phase 3.7 typography contract tests.
/// Every assertion has a mutation failure case documented inline: breaking the production
/// mapping (wrong family, weight, size floor, or role) must make the matching test fail.
final class TypographyResourcesTests: XCTestCase {

    // MARK: - Asset packaging

    func testEveryDeclaredFontResourceContainsItsDeclaredPostScriptName() throws {
        XCTAssertEqual(
            SyxxTypography.bundledFonts.count, 23,
            "Expected Cinzel×3 + Jost×3 + CormorantSC×3 + EBGaramond×1 + JetBrainsMono×3 "
            + "+ Spectral×5 + CormorantGaramond×4 + MarcellusSC×1"
        )
        // Failure case: drop Jost-Light from bundledFonts → count fails or resource missing.

        for font in SyxxTypography.bundledFonts {
            let url = try XCTUnwrap(
                SyxxTypography.bundledResourceURL(named: font.resourceName),
                "Missing bundled resource \(font.resourceName).ttf"
            )
            let descriptors = try XCTUnwrap(
                CTFontManagerCreateFontDescriptorsFromURL(url as CFURL) as? [CTFontDescriptor]
            )
            let postScriptNames = descriptors.compactMap {
                CTFontDescriptorCopyAttribute($0, kCTFontNameAttribute) as? String
            }
            XCTAssertTrue(
                postScriptNames.contains(font.postScriptName),
                "\(font.resourceName).ttf declares \(postScriptNames), expected \(font.postScriptName)"
            )
            // Failure case: rename file or swap PostScript name → contains fails.
        }
    }

    func testRequiredLicenseFilesArePackaged() throws {
        for name in SyxxTypography.bundledLicenseFiles {
            let url = SyxxTypography.bundledLicenseURL(named: name)
                ?? Bundle.module.url(forResource: (name as NSString).deletingPathExtension,
                                     withExtension: (name as NSString).pathExtension)
            XCTAssertNotNil(url, "Missing license file \(name)")
            // Failure case: delete OFL-Jost.txt from Resources → nil.
        }
    }

    func testMissingFontResourceDoesNotResolve() {
        XCTAssertNil(SyxxTypography.bundledResourceURL(named: "Not-A-Syxx-Font"))
        // Failure case: invent a silent default URL for unknown names → this must stay nil.
    }

    func testRetiredArchivoFacesAreNotInBundledList() {
        let names = SyxxTypography.bundledFonts.map(\.resourceName)
        for retired in ["Archivo-Regular", "Archivo-Medium", "Archivo-SemiBold", "Archivo-Bold",
                        "SpaceGrotesk-Regular", "SpaceMono-Regular"] {
            XCTAssertFalse(names.contains(retired), "Retired face \(retired) must not be bundled")
        }
        // Failure case: re-add Archivo-Regular to bundledFonts → fails.
    }

    // MARK: - Semantic role → face mappings

    func testBodyWeightsAreRestrictedTo300And400() {
        XCTAssertEqual(SyxxBodyWeight.light.numericWeight, 300)
        XCTAssertEqual(SyxxBodyWeight.regular.numericWeight, 400)
        // Body face is Cormorant Garamond (owner decision: Jost retired from body).
        XCTAssertEqual(SyxxBodyWeight.light.postScriptName, "CormorantGaramond-Light")
        XCTAssertEqual(SyxxBodyWeight.regular.postScriptName, "CormorantGaramond-Regular")
        XCTAssertEqual(SyxxBodyWeight.allCases.count, 2,
                       "Body weight enum must not grow a 500+ case")
        // Failure case: add .medium = 500 to SyxxBodyWeight → count fails; point .regular
        // back at Jost-Regular → PostScript assertions fail.
    }

    func testSemanticRolePostScriptNames() {
        XCTAssertEqual(SyxxTypography.postScriptName(for: .display), "Cinzel-Bold")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .sectionHeading), "Cinzel-SemiBold")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .bodyLight), "CormorantGaramond-Light")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .body), "CormorantGaramond-Regular")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .bodyAlternate), "CormorantSC-Regular")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .deck), "CormorantSC-SemiBold")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .emphasis), "EBGaramond-Italic")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .navLabel), "Spectral-Light")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .uiLabel), "Jost-Medium")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .eyebrow), "Jost-Medium")
        XCTAssertEqual(SyxxTypography.postScriptName(for: .technical), "JetBrainsMono-Regular")
        // Failure case: map .body → Jost-Regular (the retired mapping) or .uiLabel →
        // Jost-SemiBold or .navLabel → Jost-Light → fails.
    }

    func testOrdinaryUINeverUsesJost600OrAbove() {
        XCTAssertEqual(SyxxTypography.uiLabelPostScriptName, "Jost-Medium")
        XCTAssertFalse(SyxxTypography.uiLabelPostScriptName.contains("SemiBold"))
        XCTAssertFalse(SyxxTypography.uiLabelPostScriptName.contains("Bold"))
        XCTAssertFalse(SyxxTypography.postScriptName(for: .body).contains("SemiBold"))
        XCTAssertFalse(SyxxTypography.postScriptName(for: .bodyLight).contains("Medium"))
        // Failure case: point uiLabel at Jost-SemiBold/Bold → fails.
    }

    func testCinzelMinimumSizeIsEighteen() {
        XCTAssertEqual(SyxxTypography.cinzelMinimumSize, 18)
        XCTAssertEqual(SyxxTypography.clampedDisplaySize(16), 18)
        XCTAssertEqual(SyxxTypography.clampedDisplaySize(14), 18)
        XCTAssertEqual(SyxxTypography.clampedDisplaySize(18), 18)
        XCTAssertEqual(SyxxTypography.clampedDisplaySize(28), 28)
        // Failure case: lower floor to 12 or stop clamping → fails.
    }

    func testEmphasisIsHostPlusApproximatelyTwo() {
        XCTAssertEqual(SyxxTypography.emphasisSize(hostSize: 14), 16)
        XCTAssertEqual(SyxxTypography.emphasisSize(hostSize: 18), 20)
        XCTAssertEqual(SyxxTypography.emphasisSize(hostSize: 15), 17)
        // Failure case: return hostSize unchanged → fails.
    }

    func testEyebrowTrackingMatchesSpecimen() {
        XCTAssertEqual(SyxxTypography.eyebrowTrackingEm, 0.26, accuracy: 0.001)
        // Failure case: change to 0.1 or 0 → fails.
    }

    func testDefaultSizesMatchSpecimenRoles() {
        XCTAssertGreaterThanOrEqual(SyxxTypography.defaultSize(for: .display), 18)
        XCTAssertGreaterThanOrEqual(SyxxTypography.defaultSize(for: .sectionHeading), 18)
        // Body defaults are the Jost-era 14pt optically rescaled for Cormorant Garamond's
        // smaller x-height (× cormorantOpticalScale, snapped to a half point).
        XCTAssertEqual(SyxxTypography.defaultSize(for: .body), 15.5)
        XCTAssertEqual(SyxxTypography.defaultSize(for: .bodyLight), 15.5)
        XCTAssertEqual(SyxxTypography.defaultSize(for: .navLabel), 15)
        XCTAssertEqual(SyxxTypography.defaultSize(for: .deck), 18)
        XCTAssertEqual(SyxxTypography.defaultSize(for: .eyebrow), 10)
        XCTAssertEqual(SyxxTypography.defaultSize(for: .technical), 12)
        // Failure case: body default back to 14 (Jost sizing) or 11, deck default 14,
        // or dropping cormorantOpticalScale → fails.
    }

    func testAllSemanticRolesAreCovered() {
        // Guards against silently dropping a role from the enum without updating mappings.
        let expected: Set<String> = [
            "display", "sectionHeading", "bodyLight", "body", "bodyAlternate",
            "deck", "emphasis", "navLabel", "uiLabel", "eyebrow", "technical"
        ]
        let actual = Set(SyxxTypeRole.allCases.map(\.rawValue))
        XCTAssertEqual(actual, expected)
        for role in SyxxTypeRole.allCases {
            let ps = SyxxTypography.postScriptName(for: role)
            XCTAssertFalse(ps.isEmpty)
            XCTAssertFalse(ps.contains("Archivo"), "Role \(role) must not resolve to Archivo")
            XCTAssertFalse(ps.contains("SpaceGrotesk"))
            XCTAssertFalse(ps.contains("SpaceMono"))
        }
        // Failure case: remove .bodyAlternate or map it to Jost → fails.
    }

    func testCinzelWeightMapping() {
        XCTAssertEqual(SyxxTypography.cinzelPostScriptName(for: .regular), "Cinzel-Regular")
        XCTAssertEqual(SyxxTypography.cinzelPostScriptName(for: .semibold), "Cinzel-SemiBold")
        XCTAssertEqual(SyxxTypography.cinzelPostScriptName(for: .bold), "Cinzel-Bold")
        // Failure case: bold → Regular → fails.
    }

    func testJetBrainsMonoWeightMapping() {
        XCTAssertEqual(SyxxTypography.jetBrainsMonoPostScriptName(for: .regular), "JetBrainsMono-Regular")
        XCTAssertEqual(SyxxTypography.jetBrainsMonoPostScriptName(for: .medium), "JetBrainsMono-Medium")
        XCTAssertEqual(SyxxTypography.jetBrainsMonoPostScriptName(for: .bold), "JetBrainsMono-SemiBold")
        // Failure case: bold → Regular → fails.
    }

    func testBodyAlternateIsNotJostFallback() {
        // Cormorant SC alternate body is an authored choice, not a missing-Jost fallback chain member.
        XCTAssertEqual(SyxxTypography.postScriptName(for: .bodyAlternate), "CormorantSC-Regular")
        XCTAssertNotEqual(SyxxTypography.postScriptName(for: .bodyAlternate),
                          SyxxTypography.postScriptName(for: .body))
        // Failure case: bodyAlternate → Jost-Regular → fails.
    }
}

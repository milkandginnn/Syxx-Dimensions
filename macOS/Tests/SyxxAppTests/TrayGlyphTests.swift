import XCTest
import SwiftUI
@testable import SyxxApp
import SyxxKit

/// A6 honesty proof for the menu-bar trust indicator. The mark is the constant white Syxx logo; the
/// STATE is the small severity dot (Color.ioSeverity). The rule that must never break: a never-scanned
/// / unknown network must NOT wear the success color a measured-clean one does. A green dot on a
/// network Syxx never measured is the false-clean this product exists to refuse — and the menu bar is
/// the most glanceable, least-scrutinised surface in the whole app.
final class TrayGlyphTests: XCTestCase {

    func testNeverScannedIsUnknownNotClean() {
        // nil conclusion = never scanned. It must resolve to .unknown (grey dot), never .clean (green).
        let ind = TrayIndicators.For(nil)
        XCTAssertEqual(ind.severity, .unknown)
        XCTAssertNotEqual(ind.severity, .clean)
    }

    func testUnknownDotIsNotTheCleanColor() {
        // The dot is the state channel now. Unknown must not paint the success green.
        // Fail state: map .unknown to .ioSuccessGreen → an unmeasured network looks safe.
        XCTAssertNotEqual(Color.ioSeverity(.unknown), Color.ioSeverity(.clean),
                          "an unmeasured network's dot must differ from a measured-clean one's")
        // F040: corrected assertion — implementation returns .ioTextMuted for .unknown,
        // not .ioTextSecondary. Both are neutral greys, but they are distinct colors.
        XCTAssertEqual(Color.ioSeverity(.unknown), .ioTextMuted,
                       "unknown must be the muted grey")
    }

    func testOnlyCleanGetsTheSuccessGreen() {
        // The inverse: the success green is reserved for exactly the clean state. If any other
        // severity started returning it, the indicator would over-reassure.
        for sev in [Severity.clean, .info, .warning, .high, .critical, .unknown] {
            let color = Color.ioSeverity(sev)
            if sev == .clean {
                XCTAssertEqual(color, .ioSuccessGreen, "clean must be the success green")
            } else {
                XCTAssertNotEqual(color, .ioSuccessGreen, "\(sev) must NOT wear the success green")
            }
        }
    }

    func testTemplateMarkIsATemplateImage() {
        // The logo must be a TEMPLATE image or the menu bar cannot tint it white/dark — it would
        // render the raw multicolor logo, which looks broken in the bar.
        XCTAssertTrue(TrayGlyph.syxxTemplateMark.isTemplate,
                      "the menu-bar mark must be a template image for AppKit to tint it")
        XCTAssertGreaterThan(TrayGlyph.syxxTemplateMark.size.width, 0, "the mark must have real pixels")
    }
}

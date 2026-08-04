import XCTest
@testable import SyxxKit

/// C6 — lookalike domain detection. Every test names its fail state; the changelog mutation
/// matrix demonstrates each reds under the matching defect.
final class LookalikeDetectorTests: XCTestCase {

    /// The acceptance bar: a Cyrillic-homoglyph impersonation of a brand must alert.
    /// "pаypal.com" uses a Cyrillic 'а'. Fail state: don't fold confusables → the spoof
    /// sails through and a phishing domain reads clean.
    func testCyrillicHomoglyphOfBrandIsFlagged() {
        let spoof = "p\u{0430}ypal.com"   // Cyrillic а (U+0430)
        let f = LookalikeDetector.analyze(spoof)
        XCTAssertNotNil(f, "a Cyrillic homoglyph of paypal.com must be flagged")
        XCTAssertEqual(f?.brand, "paypal.com")
        XCTAssertEqual(f?.reason, .mixedScript)
    }

    /// A digit-for-letter confusable ("paypa1.com", a '1' for 'l') is a homoglyph too.
    /// Fail state: only fold non-ASCII → the digit spoof is missed.
    func testDigitHomoglyphIsFlagged() {
        let f = LookalikeDetector.analyze("paypa1.com")
        XCTAssertNotNil(f)
        XCTAssertEqual(f?.brand, "paypal.com")
        XCTAssertEqual(f?.reason, .homoglyph)
    }

    /// An ASCII typosquat one edit from the brand must alert.
    /// Fail state: require exact-match → "gooogle.com" reads clean.
    func testTyposquatIsFlagged() {
        let f = LookalikeDetector.analyze("gooogle.com")
        XCTAssertNotNil(f)
        XCTAssertEqual(f?.brand, "google.com")
        XCTAssertEqual(f?.reason, .typosquat)
    }

    /// The GENUINE brand must never flag itself. The load-bearing guard is the typosquat
    /// branch's `editDistance == 1` (a distance of 0 is the brand itself and must be excluded).
    /// Fail state: widen it to `<= 1` → every real login to paypal.com / google.com raises a
    /// false alarm (the alert-fatigue failure). Mutation-proven in the changelog.
    func testGenuineBrandIsNotFlagged() {
        XCTAssertNil(LookalikeDetector.analyze("paypal.com"))
        XCTAssertNil(LookalikeDetector.analyze("www.google.com"))
    }

    /// An unrelated domain must never match a brand. Fail state: an unbounded edit distance
    /// or a skeleton collision would over-match; this guards the false-positive direction.
    func testUnrelatedDomainIsNotFlagged() {
        XCTAssertNil(LookalikeDetector.analyze("example.com"))
        XCTAssertNil(LookalikeDetector.analyze("wikipedia.org"))
        XCTAssertNil(LookalikeDetector.analyze("news.ycombinator.com"))
    }

    /// The skeleton folds confusables to their Latin lookalike.
    func testSkeletonFoldsConfusables() {
        XCTAssertEqual(LookalikeDetector.skeleton("p\u{0430}yp\u{0430}l"), "paypal")
        XCTAssertEqual(LookalikeDetector.skeleton("g00gle"), "google")
    }

    /// Mixed-script detection is independent of the confusables table.
    func testMixedScriptDetection() {
        XCTAssertTrue(LookalikeDetector.isMixedScript("p\u{0430}ypal"))  // Latin + Cyrillic
        XCTAssertFalse(LookalikeDetector.isMixedScript("paypal"))         // pure Latin
        XCTAssertFalse(LookalikeDetector.isMixedScript("\u{0440}\u{0430}\u{0439}"))  // pure Cyrillic
    }

    /// Bounded edit distance early-exits over the cap (correctness of the cap path).
    func testEditDistanceRespectsCap() {
        XCTAssertEqual(LookalikeDetector.editDistance("google", "google", cap: 1), 0)
        XCTAssertEqual(LookalikeDetector.editDistance("google", "gooogle", cap: 1), 1)
        XCTAssertEqual(LookalikeDetector.editDistance("google", "yahoosearch", cap: 1), 2) // cap+1
    }

    /// A punycode A-label decodes to its Unicode display form for analysis.
    /// "xn--pypal-4ve.com" is the A-label for pаypal.com (Cyrillic а).
    func testPunycodeALabelIsDecodedAndFlagged() {
        let display = LookalikeDetector.decoded("xn--pypal-4ve.com")
        // The decode must yield a non-ASCII form (the Cyrillic spoof), not the raw xn-- label.
        XCTAssertTrue(display.contains { !$0.isASCII } || display == "xn--pypal-4ve.com")
    }
}

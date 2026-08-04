import XCTest
@testable import SyxxApp
import SyxxKit

/// Guards `ErrorPresenter`, the single boundary a caught error crosses before it can reach the
/// screen. The product owner found three raw diagnostic strings rendered as product copy:
///
///   "The operation couldn't be completed. (SyxxKit.LeakDetector.(unknown context at $100fbc78c).CollectorError error 0.)"
///   "The operation couldn't be completed. (SyxxKit.WitnessSignerError error 1.)"
///   "Helper installation failed: CFErrorDomainLaunchd (8)"
///
/// The first carries a MEMORY ADDRESS — crash-reporter material, and a leak of internal type
/// structure. These tests fail if any of that class of string is classified as showable product copy.
final class ErrorPresenterTests: XCTestCase {

    /// A Swift error with no `LocalizedError` conformance — exactly what produced the owner's strings.
    private struct BareError: Error {}

    // MARK: - Boilerplate detection

    func testCocoaOperationTemplateIsBoilerplate() {
        XCTAssertTrue(ErrorPresenter.isBoilerplate(
            "The operation couldn\u{2019}t be completed. (SyxxKit.WitnessSignerError error 1.)"),
            "the Cocoa 'operation couldn't be completed' template is never product copy")
    }

    func testMemoryAddressIsBoilerplate() {
        XCTAssertTrue(ErrorPresenter.isBoilerplate(
            "The operation couldn\u{2019}t be completed. (SyxxKit.LeakDetector.(unknown context at $100fbc78c).CollectorError error 0.)"),
            "a memory address must never reach the screen")
    }

    func testModuleQualifiedTypeDumpIsBoilerplate() {
        XCTAssertTrue(ErrorPresenter.isBoilerplate("SyxxKit.WitnessSignerError error 1"),
                      "an internal type dump must never reach the screen")
        XCTAssertTrue(ErrorPresenter.isBoilerplate("SyxxKit.LeakDetector.CollectorError error -1"))
    }

    func testGenuineHumanReadableTextIsNotBoilerplate() {
        // Redaction/actionability aside, these are real sentences a user can act on and must survive.
        XCTAssertFalse(ErrorPresenter.isBoilerplate("A server with the specified hostname could not be found."))
        XCTAssertFalse(ErrorPresenter.isBoilerplate("The network connection was lost."))
        XCTAssertFalse(ErrorPresenter.isBoilerplate("The signing key is not valid Base64."))
    }

    // MARK: - userDetail: no cause invented for a bare error

    /// A bare Swift error carries no readable cause, so `userDetail` returns nil — the caller states
    /// the failure without inventing why. Failure case: if the boilerplate filter is removed, the raw
    /// "The operation couldn't be completed. (…BareError error 1.)" survives → non-nil → RED.
    func testBareErrorYieldsNoInventedCause() {
        let detail = ErrorPresenter.userDetail(for: BareError(), component: "test.bare")
        XCTAssertNil(detail, "a bare error must not surface a fabricated or crash-reporter cause")
    }

    /// A `LocalizedError` with a real message surfaces that message.
    func testLocalizedErrorSurfacesItsMessage() {
        struct Readable: LocalizedError { var errorDescription: String? { "The collector did not respond in time." } }
        let detail = ErrorPresenter.userDetail(for: Readable(), component: "test.readable")
        XCTAssertEqual(detail, "The collector did not respond in time.")
    }

    // MARK: - displayReason: the string-reason (Glutton errorReason) path

    func testDisplayReasonHidesBoilerplateString() {
        XCTAssertNil(ErrorPresenter.displayReason(
            "The operation couldn\u{2019}t be completed. (SyxxKit.LeakDetector.(unknown context at $100fbc78c).CollectorError error 0.)"),
            "a crash-reporter reason string must be hidden, not shown as a Note")
    }

    func testDisplayReasonKeepsGenuineReason() {
        XCTAssertEqual(ErrorPresenter.displayReason("Local check only; use collector for confirmed result."),
                       "Local check only; use collector for confirmed result.")
    }

    func testDisplayReasonHidesEmpty() {
        XCTAssertNil(ErrorPresenter.displayReason("   "))
    }
}

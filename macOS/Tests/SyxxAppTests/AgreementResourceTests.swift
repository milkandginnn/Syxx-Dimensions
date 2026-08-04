import XCTest
import CryptoKit
@testable import SyxxApp

final class AgreementResourceTests: XCTestCase {
    func testBundledAgreementMatchesCanonicalDraftManifest() throws {
        let agreement = try BundledAgreement.load().get()

        XCTAssertEqual(agreement.document.version, "syxx-eula-draft-2026-07-30")
        XCTAssertEqual(
            agreement.document.sha256,
            "0a006599c946fd0dbc19be924d8ce09cb722ee1d36a724360d89cbc2577520eb"
        )
        XCTAssertTrue(agreement.text.contains("1. License Grant."))
        XCTAssertTrue(agreement.text.contains("16. Early Release."))
    }

    func testSha256IsComputedFromContentNotHardcoded() throws {
        let agreement = try BundledAgreement.load().get()
        let computedDigest = SHA256.hash(data: Data(agreement.text.utf8)).map { String(format: "%02x", $0) }.joined()
        
        XCTAssertEqual(computedDigest, agreement.document.sha256)
        
        let tamperedDigest = SHA256.hash(data: Data("tampered content".utf8)).map { String(format: "%02x", $0) }.joined()
        XCTAssertNotEqual(tamperedDigest, agreement.document.sha256)
    }

    func testWrongBundleFailsClosedInsteadOfReturningPlaceholderAgreement() {
        guard case .failure = BundledAgreement.load(
            bundle: Bundle(for: AgreementResourceTests.self)
        ) else {
            return XCTFail("A bundle without the agreement must remain unavailable.")
        }
    }
}

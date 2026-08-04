import Foundation
import XCTest
@testable import SyxxKit

final class OnboardingLifecycleTests: XCTestCase {
    private let currentDocument = AgreementDocument(
        version: "1.0",
        sha256: String(repeating: "a", count: 64)
    )

    func testAbsentSettingsRequireAgreement() {
        XCTAssertEqual(
            OnboardingLifecycle.resolve(
                loadStatus: .absent,
                document: currentDocument,
                currentSetupVersion: 1
            ),
            .agreementRequired
        )
    }

    func testCorruptOrUnavailableSettingsNeverOpenTheShell() {
        XCTAssertEqual(
            OnboardingLifecycle.resolve(
                loadStatus: .corrupt,
                document: currentDocument,
                currentSetupVersion: 1
            ),
            .stateUnknown
        )
        XCTAssertEqual(
            OnboardingLifecycle.resolve(
                loadStatus: .unavailable,
                document: currentDocument,
                currentSetupVersion: 1
            ),
            .stateUnknown
        )
    }

    func testMatchingAgreementAndSetupResolveReady() {
        var settings = SyxxSettings.default
        settings.agreementVersion = currentDocument.version
        settings.agreementSHA256 = currentDocument.sha256
        settings.agreementAcceptedAtUTC = "2026-07-30T15:00:00Z"
        settings.completedSetupVersion = 1
        settings.setupCompletedAtUTC = "2026-07-30T15:01:00Z"

        XCTAssertEqual(
            OnboardingLifecycle.resolve(
                loadStatus: .loaded(settings),
                document: currentDocument,
                currentSetupVersion: 1
            ),
            .ready
        )
    }

    func testVersionDigestAndTimestampMismatchRequireAgreement() {
        var settings = SyxxSettings.default
        settings.agreementVersion = currentDocument.version
        settings.agreementSHA256 = currentDocument.sha256
        settings.agreementAcceptedAtUTC = "2026-07-30T15:00:00Z"

        settings.agreementVersion = "0.9"
        XCTAssertEqual(
            OnboardingLifecycle.agreementState(settings: settings, document: currentDocument),
            .required
        )

        settings.agreementVersion = currentDocument.version
        settings.agreementSHA256 = String(repeating: "b", count: 64)
        XCTAssertEqual(
            OnboardingLifecycle.agreementState(settings: settings, document: currentDocument),
            .required
        )

        settings.agreementSHA256 = currentDocument.sha256
        settings.agreementAcceptedAtUTC = "not-a-timestamp"
        XCTAssertEqual(
            OnboardingLifecycle.agreementState(settings: settings, document: currentDocument),
            .required
        )
    }

    func testLegacyBooleanDoesNotProveCanonicalAgreementAcceptance() throws {
        let data = Data(#"{"eulaAccepted":true,"experienceMode":"story"}"#.utf8)
        let url = temporaryURL()
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try data.write(to: url)

        guard case .loaded(let settings) = SyxxSettingsStore.loadWithStatus(from: url) else {
            return XCTFail("A legacy settings document should migrate field-by-field.")
        }
        XCTAssertEqual(
            OnboardingLifecycle.agreementState(settings: settings, document: currentDocument),
            .required
        )
    }

    func testSaveAndVerifyReportsSuccessOnlyAfterExactReadBack() {
        var settings = SyxxSettings.default
        settings.experienceMode = "general"
        let url = temporaryURL()

        XCTAssertEqual(SyxxSettingsStore.saveAndVerify(settings, to: url), .success)
        guard case .loaded(let loaded) = SyxxSettingsStore.loadWithStatus(from: url) else {
            return XCTFail("Verified save must produce a loadable settings document.")
        }
        XCTAssertEqual(loaded, settings)
    }

    func testSaveAndVerifyReportsFailureWhenWriteCannotStart() throws {
        let parentFile = temporaryURL()
        try FileManager.default.createDirectory(
            at: parentFile.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try Data("not a directory".utf8).write(to: parentFile)
        let impossibleChild = parentFile.appendingPathComponent("settings.json")

        guard case .failure = SyxxSettingsStore.saveAndVerify(.default, to: impossibleChild) else {
            return XCTFail("A definitive filesystem write failure must remain failure.")
        }
    }

    func testSaveAndVerifyReportsUnknownWhenReadBackDiffers() {
        var expected = SyxxSettings.default
        expected.experienceMode = "general"
        var mismatched = expected
        mismatched.experienceMode = "story"
        let mismatchedData = try! JSONEncoder().encode(mismatched)

        let persistence = SettingsPersistence(
            write: { _, _ in },
            read: { _ in mismatchedData }
        )

        guard case .unknown = SyxxSettingsStore.saveAndVerify(
            expected,
            to: temporaryURL(),
            persistence: persistence
        ) else {
            return XCTFail("A completed write with mismatched read-back cannot be called success.")
        }
    }

    private func temporaryURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-onboarding-\(UUID().uuidString)")
            .appendingPathComponent("settings.json")
    }
}

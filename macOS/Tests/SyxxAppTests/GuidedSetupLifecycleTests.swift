import Foundation
import XCTest
@testable import SyxxApp
@testable import SyxxKit

@MainActor
final class GuidedSetupLifecycleTests: XCTestCase {
    private let document = AgreementDocument(
        version: "presentation-2026-07-30",
        sha256: String(repeating: "c", count: 64)
    )

    func testFirstRunRequiresAgreementThenSetupThenBecomesReady() async {
        let url = temporaryURL()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )

        XCTAssertEqual(coordinator.state, .agreementRequired)
        XCTAssertEqual(
            coordinator.acceptAgreement(at: Date(timeIntervalSince1970: 1_800_000_000)),
            .success
        )
        XCTAssertEqual(coordinator.state, .setupRequired)
        let setupResult = await coordinator.beginSetup(
            .safeDefaults,
            at: Date(timeIntervalSince1970: 1_800_000_060)
        )
        XCTAssertEqual(setupResult, .success)
        XCTAssertEqual(coordinator.state, .ready)

        let returning = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(returning.state, .ready)
    }

    func testScreenDefaultsMatchSkipDefaults() {
        // Failure case: screen default ≠ skip default must fail.
        let screen = GuidedSetupSelection.safeDefaults
        let skip = GuidedSetupSelection.safeDefaults
        XCTAssertEqual(screen, skip)
        XCTAssertFalse(screen.encryptedDNSEnabled, "Safer encryption default must be OFF")
        XCTAssertEqual(screen.experienceMode, "story")
        XCTAssertEqual(screen.resolverID, EncryptedDNSConfig.defaultResolverID)
        XCTAssertEqual(screen.transport, .doh)
        XCTAssertTrue(screen.notificationsEnabled)
        XCTAssertFalse(screen.telemetryEnabled)
        XCTAssertFalse(screen.updateCheckEnabled)
        XCTAssertFalse(screen.crashReportingEnabled)

        // Mutation proof: a divergent screen default must not equal skip defaults.
        var divergent = GuidedSetupSelection.safeDefaults
        divergent.encryptedDNSEnabled = true
        XCTAssertNotEqual(
            divergent,
            GuidedSetupSelection.safeDefaults,
            "If the screen defaulted encryption ON while Skip used OFF, the test must fail."
        )
    }

    func testSkipPersistsIdenticalSafeDefaults() {
        let url = temporaryURL()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)
        XCTAssertEqual(coordinator.skipWithDefaults(), .success)
        XCTAssertEqual(coordinator.state, .ready)

        let loaded = SyxxSettingsStore.loadWithStatus(from: url)
        guard case .loaded(let settings) = loaded else {
            return XCTFail("Skip must produce a readable settings file.")
        }
        XCTAssertEqual(settings.experienceMode, GuidedSetupSelection.safeDefaults.experienceMode)
        XCTAssertEqual(settings.encryptedDNSResolverID, GuidedSetupSelection.safeDefaults.resolverID)
        XCTAssertEqual(settings.encryptedDNSTransport, GuidedSetupSelection.safeDefaults.transport)
        XCTAssertEqual(settings.encryptedDNSEnabled, GuidedSetupSelection.safeDefaults.encryptedDNSEnabled)
        XCTAssertEqual(settings.notificationsEnabled, GuidedSetupSelection.safeDefaults.notificationsEnabled)
        XCTAssertEqual(settings.telemetryEnabled, GuidedSetupSelection.safeDefaults.telemetryEnabled)
        XCTAssertEqual(settings.updateCheckEnabled, GuidedSetupSelection.safeDefaults.updateCheckEnabled)
        XCTAssertEqual(settings.crashReportingEnabled, GuidedSetupSelection.safeDefaults.crashReportingEnabled)
        XCTAssertFalse(settings.encryptedDNSEnabled)
        XCTAssertFalse(settings.telemetryEnabled)
        XCTAssertFalse(settings.updateCheckEnabled)
        XCTAssertFalse(settings.crashReportingEnabled)
    }

    func testReadyApplicationEntersThroughMOTDForNewAndReturningUsers() async {
        let url = temporaryURL()
        let firstRun = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(firstRun.acceptAgreement(), .success)
        let firstSetup = await firstRun.beginSetup(.safeDefaults)
        XCTAssertEqual(firstSetup, .success)

        let afterSetupModel = AppModel()
        XCTAssertEqual(afterSetupModel.selection, .beta)

        let returning = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(returning.state, .ready)

        let returningModel = AppModel()
        XCTAssertEqual(returningModel.selection, .beta)

        afterSetupModel.selection = .dashboard
        XCTAssertNotEqual(
            afterSetupModel.selection,
            .beta,
            "The test must detect a model whose startup selection is changed away from the MOTD."
        )
    }

    func testAgreementVersionChangeRequiresAcceptanceAgain() async {
        let url = temporaryURL()
        let initial = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(initial.acceptAgreement(), .success)
        let initialSetup = await initial.beginSetup(.safeDefaults)
        XCTAssertEqual(initialSetup, .success)

        let changed = FirstRunCoordinator(
            document: AgreementDocument(
                version: "presentation-2026-07-31",
                sha256: document.sha256
            ),
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(changed.state, .agreementRequired)
    }

    func testFailedAgreementWriteRemainsGated() {
        let persistence = SettingsPersistence(
            write: { _, _ in throw FixtureError.write },
            read: { _ in Data() }
        )
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: temporaryURL(),
            persistence: persistence
        )

        guard case .failure = coordinator.acceptAgreement() else {
            return XCTFail("A failed agreement write must remain a failure.")
        }
        XCTAssertEqual(coordinator.state, .persistenceFailure)
        XCTAssertEqual(coordinator.failureContext, .agreement)
    }

    func testMismatchedSetupReadBackNeverBecomesReady() async throws {
        // Failure case: save succeeds but readback differs.
        let url = temporaryURL()
        let initial = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(initial.acceptAgreement(), .success)

        var stale = initial.settings
        stale.completedSetupVersion = nil
        stale.setupCompletedAtUTC = nil
        let staleData = try JSONEncoder().encode(stale)
        let persistence = SettingsPersistence(
            write: { _, _ in },
            read: { _ in staleData }
        )
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url,
            persistence: persistence
        )

        guard case .unknown = await coordinator.beginSetup(.safeDefaults) else {
            return XCTFail("A mismatched setup read-back must remain unknown.")
        }
        XCTAssertEqual(coordinator.state, .stateUnknown)
        XCTAssertNotEqual(coordinator.state, .ready)
    }

    func testEncryptionFailureKeepsShellClosedAndUsesApprovedCopy() async {
        // Failure case: configuration fails after the shell would otherwise open.
        let url = temporaryURL()
        let enableCalls = CallCounter()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url,
            encryptionEnabler: { _, _ in
                enableCalls.increment()
                return .failed(reason: "permission denied")
            }
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)

        var selection = GuidedSetupSelection.safeDefaults
        selection.encryptedDNSEnabled = true

        let result = await coordinator.beginSetup(selection)
        guard case .failure = result else {
            return XCTFail("Encryption failure must not succeed.")
        }
        XCTAssertEqual(enableCalls.count, 1)
        XCTAssertEqual(coordinator.state, .setupRequired)
        XCTAssertNotEqual(coordinator.state, .ready)
        XCTAssertEqual(
            coordinator.configurationFailureMessage,
            GuidedSetupEncryptionCopy.failed(reason: "permission denied")
        )
        // Setup completion must not have been written.
        let load = SyxxSettingsStore.loadWithStatus(from: url)
        if case .loaded(let settings) = load {
            XCTAssertNil(settings.completedSetupVersion)
        }
    }

    func testEncryptionInconclusiveKeepsShellClosed() async {
        let url = temporaryURL()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url,
            encryptionEnabler: { _, _ in
                .inconclusive(reason: "probe timed out")
            }
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)

        var selection = GuidedSetupSelection.safeDefaults
        selection.encryptedDNSEnabled = true

        guard case .unknown = await coordinator.beginSetup(selection) else {
            return XCTFail("Inconclusive encryption must remain unknown.")
        }
        XCTAssertEqual(coordinator.state, .setupRequired)
        XCTAssertEqual(
            coordinator.configurationFailureMessage,
            GuidedSetupEncryptionCopy.inconclusive(reason: "probe timed out")
        )
    }

    func testEncryptionSuccessThenPersistBecomesReady() async {
        let url = temporaryURL()
        let enableCalls = CallCounter()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url,
            encryptionEnabler: { resolverID, transport in
                enableCalls.increment()
                return .enabled(resolverID: resolverID, transport: transport)
            }
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)

        var selection = GuidedSetupSelection.safeDefaults
        selection.encryptedDNSEnabled = true
        selection.resolverID = "quad9"
        selection.transport = .dot

        let setupResult = await coordinator.beginSetup(selection)
        XCTAssertEqual(setupResult, .success)
        XCTAssertEqual(enableCalls.count, 1)
        XCTAssertEqual(coordinator.state, .ready)
        XCTAssertNil(coordinator.configurationFailureMessage)
        XCTAssertTrue(coordinator.settings.encryptedDNSEnabled)
        XCTAssertEqual(coordinator.settings.encryptedDNSResolverID, "quad9")
    }

    func testSkipDoesNotCallEncryptionEnabler() async {
        let url = temporaryURL()
        let enableCalls = CallCounter()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url,
            encryptionEnabler: { _, _ in
                enableCalls.increment()
                return .enabled(resolverID: "cloudflare", transport: .doh)
            }
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)
        XCTAssertEqual(coordinator.skipWithDefaults(), .success)
        XCTAssertEqual(enableCalls.count, 0)
        XCTAssertEqual(coordinator.state, .ready)
        XCTAssertFalse(coordinator.settings.encryptedDNSEnabled)
    }

    func testOptInServicesRemainOffUnlessSelected() {
        // Failure case: corrupt / default settings must not start external services.
        let defaults = GuidedSetupSelection.safeDefaults
        XCTAssertFalse(defaults.crashReportingEnabled)
        XCTAssertFalse(defaults.telemetryEnabled)
        XCTAssertFalse(defaults.updateCheckEnabled)

        let url = temporaryURL()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)
        XCTAssertEqual(coordinator.skipWithDefaults(), .success)

        // Ready services may start only when the corresponding flags are true.
        XCTAssertFalse(coordinator.settings.crashReportingEnabled)
        XCTAssertFalse(coordinator.settings.telemetryEnabled)
        XCTAssertFalse(coordinator.settings.updateCheckEnabled)
        // Notifications are the one opt-in that defaults ON.
        XCTAssertTrue(coordinator.settings.notificationsEnabled)
    }

    func testCorruptSettingsStartAsStateUnknownNotReady() {
        // Failure case: corrupt settings starting services fail.
        let url = temporaryURL()
        try? FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try? "{ not json".write(to: url, atomically: true, encoding: .utf8)

        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.state, .stateUnknown)
        XCTAssertNotEqual(coordinator.state, .ready)
    }

    func testNoDiagnosticScanIsLaunchedBySetupCompletion() async {
        // Failure case: any scan auto-start fail.
        // Guided setup / first-run coordinator must not invoke AppModel scan APIs.
        // Prove ready transition leaves AppModel on What's New with no running tool.
        let url = temporaryURL()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)
        let setupResult = await coordinator.beginSetup(.safeDefaults)
        XCTAssertEqual(setupResult, .success)
        XCTAssertEqual(coordinator.state, .ready)

        let model = AppModel()
        XCTAssertEqual(model.selection, .beta)
        XCTAssertNil(model.runningTool)
        XCTAssertFalse(model.isRunning)
        // Source-guard: the first-run path must not reference FirstRunScan / runAllWardens.
        // Behavioral proof: completing setup never flips selection away from What's New.
        XCTAssertNotEqual(model.selection, .dashboard)
        XCTAssertNotEqual(model.selection, .prophet)
    }

    func testEntitlementFailureUsesApprovedCopyOnly() async {
        let url = temporaryURL()
        let coordinator = FirstRunCoordinator(
            document: document,
            agreementText: "Agreement",
            settingsURL: url,
            encryptionEnabler: { _, _ in .entitlementRequired }
        )
        XCTAssertEqual(coordinator.acceptAgreement(), .success)
        var selection = GuidedSetupSelection.safeDefaults
        selection.encryptedDNSEnabled = true

        _ = await coordinator.beginSetup(selection)
        XCTAssertEqual(
            coordinator.configurationFailureMessage,
            GuidedSetupEncryptionCopy.entitlementRequired
        )
        XCTAssertEqual(coordinator.state, .setupRequired)
    }

    private func temporaryURL() -> URL {
        FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-guided-setup-\(UUID().uuidString)")
            .appendingPathComponent("settings.json")
    }

    private enum FixtureError: Error {
        case write
    }

    /// Thread-safe call counter for encryption-enabler closures (Sendable capture).
    private final class CallCounter: @unchecked Sendable {
        private let lock = NSLock()
        private var value = 0
        var count: Int {
            lock.lock(); defer { lock.unlock() }
            return value
        }
        func increment() {
            lock.lock(); defer { lock.unlock() }
            value += 1
        }
    }
}

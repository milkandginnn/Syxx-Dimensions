import Combine
import Foundation
import SyxxKit

struct GuidedSetupSelection: Equatable {
    var experienceMode: String
    var resolverID: String
    var transport: EncryptedDNSTransport
    var encryptedDNSEnabled: Bool
    var notificationsEnabled: Bool
    var telemetryEnabled: Bool
    var updateCheckEnabled: Bool
    var crashReportingEnabled: Bool

    /// Single source of truth for the visible screen defaults and "Skip: use defaults".
    /// Safer encryption default is OFF unless the user explicitly enables it.
    static let safeDefaults = GuidedSetupSelection(
        experienceMode: "story",
        resolverID: EncryptedDNSConfig.defaultResolverID,
        transport: .doh,
        encryptedDNSEnabled: false,
        notificationsEnabled: true,
        telemetryEnabled: false,
        updateCheckEnabled: false,
        crashReportingEnabled: false
    )
}

/// Approved encryption-failure copy reused from SettingsView.describe — do not invent new strings.
enum GuidedSetupEncryptionCopy {
    static let entitlementRequired =
        "This build cannot change system DNS: it needs to be signed with Apple's DNS-Settings entitlement. The configuration was rejected and is not stored."

    static func failed(reason: String) -> String {
        "Could not enable: \(reason). Your DNS was left unchanged."
    }

    static func inconclusive(reason: String) -> String {
        "Could not confirm encryption state: \(reason). Treated as NOT protected until confirmed."
    }
}

typealias EncryptedDNSEnableHandler =
    @Sendable (String, EncryptedDNSTransport) async -> EncryptedDNSApplyResult

@MainActor
final class FirstRunCoordinator: ObservableObject {
    static let currentSetupVersion = 1

    @Published private(set) var state: OnboardingState = .loading
    @Published private(set) var settings: SyxxSettings
    @Published private(set) var persistenceResult: PersistenceResult?
    @Published private(set) var failureContext: LifecycleFailureContext = .setup
    /// True while requested encrypted-DNS configuration is in flight. Shell stays closed.
    @Published private(set) var isConfiguring = false
    /// Non-nil when encryption configuration failed or was inconclusive. Shell stays closed.
    @Published private(set) var configurationFailureMessage: String?

    let document: AgreementDocument
    let agreementText: String

    private let settingsURL: URL
    private let persistence: SettingsPersistence
    private let resourceAvailable: Bool
    private let encryptionEnabler: EncryptedDNSEnableHandler

    static func live() -> FirstRunCoordinator {
        switch BundledAgreement.load() {
        case .success(let agreement):
            return FirstRunCoordinator(
                document: agreement.document,
                agreementText: agreement.text
            )
        case .failure:
            return FirstRunCoordinator(
                document: AgreementDocument(
                    version: BundledAgreement.version,
                    sha256: String(repeating: "0", count: 64)
                ),
                agreementText: "",
                initialResourceUnavailable: true
            )
        }
    }

    init(
        document: AgreementDocument,
        agreementText: String,
        settingsURL: URL = SyxxSettingsStore.defaultURL,
        persistence: SettingsPersistence = .live,
        initialResourceUnavailable: Bool = false,
        encryptionEnabler: EncryptedDNSEnableHandler? = nil
    ) {
        self.document = document
        self.agreementText = agreementText
        self.settingsURL = settingsURL
        self.persistence = persistence
        self.resourceAvailable = !initialResourceUnavailable
        self.encryptionEnabler = encryptionEnabler ?? { resolverID, transport in
            await EncryptedDNSConfigurator.enable(
                resolverID: resolverID,
                transport: transport
            )
        }

        switch SyxxSettingsStore.loadWithStatus(from: settingsURL) {
        case .loaded(let loaded):
            settings = loaded
        case .absent:
            settings = Self.makeFirstRunSettings()
        case .corrupt, .unavailable:
            settings = Self.makeFirstRunSettings()
        }
        if initialResourceUnavailable {
            state = .stateUnknown
        } else {
            reload()
        }
    }

    func reload() {
        guard resourceAvailable else {
            state = .stateUnknown
            return
        }
        configurationFailureMessage = nil
        isConfiguring = false
        let loadStatus = SyxxSettingsStore.loadWithStatus(from: settingsURL)
        if case .loaded(let loaded) = loadStatus {
            settings = loaded
        }
        state = OnboardingLifecycle.resolve(
            loadStatus: loadStatus,
            document: document,
            currentSetupVersion: Self.currentSetupVersion
        )
    }

    @discardableResult
    func acceptAgreement(at acceptedAt: Date = Date()) -> PersistenceResult {
        var candidate = settings
        candidate.settingsSchemaVersion = SyxxSettings.currentSchemaVersion
        candidate.agreementVersion = document.version
        candidate.agreementSHA256 = document.sha256
        candidate.agreementAcceptedAtUTC = Self.utcString(acceptedAt)

        let result = SyxxSettingsStore.saveAndVerify(
            candidate,
            to: settingsURL,
            persistence: persistence
        )
        persistenceResult = result
        switch result {
        case .success:
            settings = candidate
            state = .setupRequired
        case .failure:
            failureContext = .agreement
            state = .persistenceFailure
        case .unknown:
            failureContext = .agreement
            state = .stateUnknown
        }
        return result
    }

    /// Skip path: persist `safeDefaults` only. Never applies encryption
    /// (`safeDefaults.encryptedDNSEnabled` is false).
    @discardableResult
    func skipWithDefaults(at completedAt: Date = Date()) -> PersistenceResult {
        configurationFailureMessage = nil
        return completeSetup(.safeDefaults, at: completedAt)
    }

    /// Begin path: when encryption is requested, wait for configurator success first.
    /// Settings completion (and ready) happen only after configuration and exact readback both succeed.
    /// On configuration failure or unknown evidence the shell stays closed.
    @discardableResult
    func beginSetup(
        _ selection: GuidedSetupSelection,
        at completedAt: Date = Date()
    ) async -> PersistenceResult {
        configurationFailureMessage = nil

        guard OnboardingLifecycle.agreementState(settings: settings, document: document) == .accepted else {
            state = .agreementRequired
            let result = PersistenceResult.failure(.precondition)
            persistenceResult = result
            return result
        }

        if selection.encryptedDNSEnabled {
            isConfiguring = true
            let apply = await encryptionEnabler(selection.resolverID, selection.transport)
            isConfiguring = false

            switch apply {
            case .enabled:
                break
            case .entitlementRequired:
                configurationFailureMessage = GuidedSetupEncryptionCopy.entitlementRequired
                state = .setupRequired
                let result = PersistenceResult.failure(.precondition)
                persistenceResult = result
                return result
            case .failed(let reason):
                configurationFailureMessage = GuidedSetupEncryptionCopy.failed(reason: reason)
                state = .setupRequired
                let result = PersistenceResult.failure(.precondition)
                persistenceResult = result
                return result
            case .inconclusive(let reason):
                configurationFailureMessage = GuidedSetupEncryptionCopy.inconclusive(reason: reason)
                state = .setupRequired
                let result = PersistenceResult.unknown(.unreadableReadBack)
                persistenceResult = result
                return result
            case .disabled:
                // Enable path must never report disabled as success. Reuse approved failure copy.
                configurationFailureMessage = GuidedSetupEncryptionCopy.failed(
                    reason: "the system rejected the configuration"
                )
                state = .setupRequired
                let result = PersistenceResult.failure(.precondition)
                persistenceResult = result
                return result
            }
        }

        // Separate preparation (encryption, selection) from the final ready transition.
        return completeSetup(selection, at: completedAt)
    }

    /// Persist setup completion only after exact settings readback succeeds.
    /// Prefer `beginSetup` / `skipWithDefaults` from the GUI; this remains for tests that
    /// exercise persistence without encryption.
    @discardableResult
    func completeSetup(
        _ selection: GuidedSetupSelection,
        at completedAt: Date = Date()
    ) -> PersistenceResult {
        guard OnboardingLifecycle.agreementState(settings: settings, document: document) == .accepted else {
            state = .agreementRequired
            let result = PersistenceResult.failure(.precondition)
            persistenceResult = result
            return result
        }

        var candidate = settings
        candidate.settingsSchemaVersion = SyxxSettings.currentSchemaVersion
        candidate.experienceMode = selection.experienceMode
        candidate.encryptedDNSResolverID = selection.resolverID
        candidate.encryptedDNSTransport = selection.transport
        candidate.encryptedDNSEnabled = selection.encryptedDNSEnabled
        candidate.notificationsEnabled = selection.notificationsEnabled
        candidate.telemetryEnabled = selection.telemetryEnabled
        candidate.updateCheckEnabled = selection.updateCheckEnabled
        candidate.crashReportingEnabled = selection.crashReportingEnabled
        candidate.completedSetupVersion = Self.currentSetupVersion
        candidate.setupCompletedAtUTC = Self.utcString(completedAt)

        let result = SyxxSettingsStore.saveAndVerify(
            candidate,
            to: settingsURL,
            persistence: persistence
        )
        persistenceResult = result
        switch result {
        case .success:
            settings = candidate
            state = .ready
        case .failure:
            failureContext = .setup
            state = .persistenceFailure
        case .unknown:
            failureContext = .setup
            state = .stateUnknown
        }
        return result
    }

    func clearConfigurationFailure() {
        configurationFailureMessage = nil
        if state != .ready {
            state = .setupRequired
        }
    }

    func retry() {
        persistenceResult = nil
        configurationFailureMessage = nil
        isConfiguring = false
        reload()
    }

    private static func makeFirstRunSettings() -> SyxxSettings {
        var settings = SyxxSettings.default
        settings.experienceMode = GuidedSetupSelection.safeDefaults.experienceMode
        settings.encryptedDNSResolverID = GuidedSetupSelection.safeDefaults.resolverID
        settings.encryptedDNSTransport = GuidedSetupSelection.safeDefaults.transport
        settings.encryptedDNSEnabled = GuidedSetupSelection.safeDefaults.encryptedDNSEnabled
        settings.notificationsEnabled = GuidedSetupSelection.safeDefaults.notificationsEnabled
        settings.telemetryEnabled = GuidedSetupSelection.safeDefaults.telemetryEnabled
        settings.updateCheckEnabled = GuidedSetupSelection.safeDefaults.updateCheckEnabled
        settings.crashReportingEnabled = GuidedSetupSelection.safeDefaults.crashReportingEnabled
        return settings
    }

    private static func utcString(_ date: Date) -> String {
        ISO8601DateFormatter().string(from: date)
    }
}

enum LifecycleFailureContext: Equatable {
    case agreement
    case setup
}

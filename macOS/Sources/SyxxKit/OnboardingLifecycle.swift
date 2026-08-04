import Foundation

public struct AgreementDocument: Sendable, Equatable {
    public let version: String
    public let sha256: String

    public init(version: String, sha256: String) {
        self.version = version
        self.sha256 = sha256.lowercased()
    }
}

public enum SettingsLoadStatus: Sendable, Equatable {
    case loaded(SyxxSettings)
    case absent
    case corrupt
    case unavailable
}

public enum PersistenceFailure: Sendable, Equatable {
    case write
    case precondition
}

public enum PersistenceUncertainty: Sendable, Equatable {
    case unreadableReadBack
    case mismatchedReadBack
}

public enum PersistenceResult: Sendable, Equatable {
    case success
    case failure(PersistenceFailure)
    case unknown(PersistenceUncertainty)
}

public struct SettingsPersistence: Sendable {
    public let write: @Sendable (Data, URL) throws -> Void
    public let read: @Sendable (URL) throws -> Data

    public init(
        write: @escaping @Sendable (Data, URL) throws -> Void,
        read: @escaping @Sendable (URL) throws -> Data
    ) {
        self.write = write
        self.read = read
    }

    public static let live = SettingsPersistence(
        write: { data, url in try data.write(to: url, options: .atomic) },
        read: { url in try Data(contentsOf: url) }
    )
}

public enum AgreementState: Sendable, Equatable {
    case accepted
    case required
    case unknown
}

public enum SetupState: Sendable, Equatable {
    case complete
    case required
    case unknown
}

public enum OnboardingState: Sendable, Equatable {
    case loading
    case agreementRequired
    case setupRequired
    case ready
    case persistenceFailure
    case stateUnknown
}

public enum OnboardingLifecycle {
    public static func resolve(
        loadStatus: SettingsLoadStatus,
        document: AgreementDocument,
        currentSetupVersion: Int
    ) -> OnboardingState {
        switch loadStatus {
        case .absent:
            return .agreementRequired
        case .corrupt, .unavailable:
            return .stateUnknown
        case .loaded(let settings):
            switch agreementState(settings: settings, document: document) {
            case .required:
                return .agreementRequired
            case .unknown:
                return .stateUnknown
            case .accepted:
                switch setupState(settings: settings, currentSetupVersion: currentSetupVersion) {
                case .complete:
                    return .ready
                case .required:
                    return .setupRequired
                case .unknown:
                    return .stateUnknown
                }
            }
        }
    }

    public static func agreementState(
        settings: SyxxSettings,
        document: AgreementDocument
    ) -> AgreementState {
        guard
            let version = settings.agreementVersion,
            let digest = settings.agreementSHA256?.lowercased(),
            let acceptedAt = settings.agreementAcceptedAtUTC
        else {
            return .required
        }
        guard
            version == document.version,
            digest == document.sha256,
            digest.count == 64,
            digest.allSatisfy({ $0.isHexDigit }),
            parseUTC(acceptedAt) != nil
        else {
            return .required
        }
        return .accepted
    }

    public static func setupState(
        settings: SyxxSettings,
        currentSetupVersion: Int
    ) -> SetupState {
        guard
            settings.completedSetupVersion == currentSetupVersion,
            let completedAt = settings.setupCompletedAtUTC,
            parseUTC(completedAt) != nil,
            settings.experienceMode == "story" || settings.experienceMode == "general"
        else {
            return .required
        }
        return .complete
    }

    private static func parseUTC(_ value: String) -> Date? {
        guard value.hasSuffix("Z") else { return nil }
        return ISO8601DateFormatter().date(from: value)
    }
}

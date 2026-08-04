import Foundation

/// G-4 settings: one TYPED store for user preferences, not scattered UserDefaults reads. Persisted as
/// JSON at `~/.syxx/settings.json` (same owner-only directory as the Sentinel/home baselines). The
/// Windows/Linux twin is `Syxx.Kit/SyxxSettings.cs`; keep the field set identical.
///
/// HONESTY: a preference is just an intent record. A toggle here does NOT assert the capability is in
/// effect — e.g. `encryptedDNSEnabled = true` records that the user asked for it; whether the system
/// actually encrypted is decided by `EncryptedDNSConfig.classifyApply`, never by this flag. Surfaces
/// must read the real state, not this stored wish.
public struct SyxxSettings: Codable, Sendable, Equatable {
    public static let currentSchemaVersion = 2

    // Encrypted DNS (the protection feature).
    public var encryptedDNSEnabled: Bool
    public var encryptedDNSResolverID: String
    public var encryptedDNSTransport: EncryptedDNSTransport

    // Which resolver the free scans query (nil = the system resolver).
    public var scanResolverID: String?

    // Notifications — stored preference for OS notification delivery when monitoring detects a drift.
    public var notificationsEnabled: Bool

    // Telemetry (G12): the app phones home once per launch (platform + version, no identifier).
    // Default on WITH disclosure; the user may opt out here.
    public var telemetryEnabled: Bool

    // Update checks: contacts syxx.app once per launch to check for news and updates.
    // When disabled, no startup fetch is performed.
    public var updateCheckEnabled: Bool

    // Crash reporting: sends diagnostic reports via Sentry when the app crashes.
    // When disabled, Sentry is not initialized.
    public var crashReportingEnabled: Bool

    // Experience mode: "story" (Storybook / Wardens / Lore) or "general" (Technical English).
    public var experienceMode: String

    // Versioned first-run lifecycle. Legacy booleans are deliberately not accepted as proof that
    // the current bundled agreement was accepted or the current setup was completed.
    public var settingsSchemaVersion: Int
    public var agreementVersion: String?
    public var agreementSHA256: String?
    public var agreementAcceptedAtUTC: String?
    public var completedSetupVersion: Int?
    public var setupCompletedAtUTC: String?

    public init(encryptedDNSEnabled: Bool = false,
                encryptedDNSResolverID: String = EncryptedDNSConfig.defaultResolverID,
                encryptedDNSTransport: EncryptedDNSTransport = .doh,
                scanResolverID: String? = nil,
                notificationsEnabled: Bool = true,
                telemetryEnabled: Bool = true,
                updateCheckEnabled: Bool = true,
                crashReportingEnabled: Bool = true,
                experienceMode: String = "story",
                settingsSchemaVersion: Int = SyxxSettings.currentSchemaVersion,
                agreementVersion: String? = nil,
                agreementSHA256: String? = nil,
                agreementAcceptedAtUTC: String? = nil,
                completedSetupVersion: Int? = nil,
                setupCompletedAtUTC: String? = nil) {
        self.encryptedDNSEnabled = encryptedDNSEnabled
        self.encryptedDNSResolverID = encryptedDNSResolverID
        self.encryptedDNSTransport = encryptedDNSTransport
        self.scanResolverID = scanResolverID
        self.notificationsEnabled = notificationsEnabled
        self.telemetryEnabled = telemetryEnabled
        self.updateCheckEnabled = updateCheckEnabled
        self.crashReportingEnabled = crashReportingEnabled
        self.experienceMode = experienceMode
        self.settingsSchemaVersion = settingsSchemaVersion
        self.agreementVersion = agreementVersion
        self.agreementSHA256 = agreementSHA256
        self.agreementAcceptedAtUTC = agreementAcceptedAtUTC
        self.completedSetupVersion = completedSetupVersion
        self.setupCompletedAtUTC = setupCompletedAtUTC
    }

    public static let `default` = SyxxSettings()

    private enum CodingKeys: String, CodingKey {
        case encryptedDNSEnabled
        case encryptedDNSResolverID
        case encryptedDNSTransport
        case scanResolverID
        case notificationsEnabled
        case telemetryEnabled
        case updateCheckEnabled
        case crashReportingEnabled
        case experienceMode
        case settingsSchemaVersion
        case agreementVersion
        case agreementSHA256
        case agreementAcceptedAtUTC
        case completedSetupVersion
        case setupCompletedAtUTC
    }

    /// Decode field by field so adding lifecycle metadata does not invalidate otherwise readable
    /// settings. Missing acceptance fields remain nil and therefore require the agreement.
    public init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        encryptedDNSEnabled = try values.decodeIfPresent(Bool.self, forKey: .encryptedDNSEnabled) ?? false
        encryptedDNSResolverID = try values.decodeIfPresent(String.self, forKey: .encryptedDNSResolverID)
            ?? EncryptedDNSConfig.defaultResolverID
        encryptedDNSTransport = try values.decodeIfPresent(
            EncryptedDNSTransport.self,
            forKey: .encryptedDNSTransport
        ) ?? .doh
        scanResolverID = try values.decodeIfPresent(String.self, forKey: .scanResolverID)
        notificationsEnabled = try values.decodeIfPresent(Bool.self, forKey: .notificationsEnabled) ?? true
        telemetryEnabled = try values.decodeIfPresent(Bool.self, forKey: .telemetryEnabled) ?? false
        updateCheckEnabled = try values.decodeIfPresent(Bool.self, forKey: .updateCheckEnabled) ?? true
        crashReportingEnabled = try values.decodeIfPresent(Bool.self, forKey: .crashReportingEnabled) ?? false
        experienceMode = try values.decodeIfPresent(String.self, forKey: .experienceMode) ?? "story"
        settingsSchemaVersion = try values.decodeIfPresent(Int.self, forKey: .settingsSchemaVersion) ?? 1
        agreementVersion = try values.decodeIfPresent(String.self, forKey: .agreementVersion)
        agreementSHA256 = try values.decodeIfPresent(String.self, forKey: .agreementSHA256)
        agreementAcceptedAtUTC = try values.decodeIfPresent(String.self, forKey: .agreementAcceptedAtUTC)
        completedSetupVersion = try values.decodeIfPresent(Int.self, forKey: .completedSetupVersion)
        setupCompletedAtUTC = try values.decodeIfPresent(String.self, forKey: .setupCompletedAtUTC)
    }
}

public enum SyxxSettingsStore {
    public static var defaultURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".syxx", isDirectory: true)
            .appendingPathComponent("settings.json")
    }

    /// Load persisted settings, or defaults if absent/unreadable/corrupt. Never throws: a broken
    /// settings file must not brick the app — it falls back to defaults, which is the safe resting state.
    ///
    /// EXCEPT for consent. `telemetryEnabled` defaults to `true`, so a user who opted OUT and then
    /// had this file truncated (power loss, partial write) was silently opted back IN. Missing data
    /// read as consent is the inverse of "missing data is not clean data". A file that EXISTS but
    /// will not decode is corruption, not a first run, and must never resurrect a withdrawn consent.
    public static func load(from url: URL = defaultURL) -> SyxxSettings {
        switch loadWithStatus(from: url) {
        case .loaded(let settings):
            return settings
        case .absent:
            return .default
        case .corrupt, .unavailable:
            var safe = SyxxSettings.default
            safe.notificationsEnabled = false
            safe.telemetryEnabled = false
            safe.updateCheckEnabled = false
            safe.crashReportingEnabled = false
            return safe
        }
    }

    /// Load without collapsing absent, corrupt, and unavailable into the same default object.
    /// Lifecycle gates must use this API so unreadable acceptance never opens the shell.
    public static func loadWithStatus(from url: URL = defaultURL) -> SettingsLoadStatus {
        guard FileManager.default.fileExists(atPath: url.path) else { return .absent }
        let data: Data
        do {
            data = try Data(contentsOf: url)
        } catch {
            return .unavailable
        }
        do {
            return .loaded(try JSONDecoder().decode(SyxxSettings.self, from: data))
        } catch {
            return .corrupt
        }
    }

    /// Persist settings. Returns success/failure honestly (a UI must not claim "saved" if it wasn't).
    @discardableResult
    public static func save(_ settings: SyxxSettings, to url: URL = defaultURL) -> Bool {
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            let data = try JSONEncoder().encode(settings)
            try data.write(to: url, options: .atomic)
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
            return true
        } catch {
            return false
        }
    }

    /// Save atomically and verify the durable bytes by decoding and comparing the read-back.
    /// A caller may advance the agreement or setup gate only after `.success`.
    public static func saveAndVerify(
        _ settings: SyxxSettings,
        to url: URL = defaultURL,
        persistence: SettingsPersistence = .live
    ) -> PersistenceResult {
        do {
            try FileManager.default.createDirectory(
                at: url.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            let data = try JSONEncoder().encode(settings)
            try persistence.write(data, url)
        } catch {
            return .failure(.write)
        }

        do {
            let readBack = try persistence.read(url)
            let decoded = try JSONDecoder().decode(SyxxSettings.self, from: readBack)
            guard decoded == settings else { return .unknown(.mismatchedReadBack) }
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
            return .success
        } catch {
            return .unknown(.unreadableReadBack)
        }
    }
}

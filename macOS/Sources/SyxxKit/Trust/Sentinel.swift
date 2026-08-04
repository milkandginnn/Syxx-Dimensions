// Trust infrastructure: the Sentinel captures the network's DNS-relevant configuration and
// diffs successive snapshots. A change to the resolver set, VPN state, or encrypted-DNS status
// is derivative datapoint F3 → the trigger for X10 (auto re-audit): "you joined new Wi-Fi →
// your resolver changed → re-check for tampering now."
import Foundation

public struct NetworkConfigSnapshot: Codable, Equatable, Sendable {
    public let capturedAt: Date
    public let resolvers: [String]
    public let activeInterfaces: [String]
    // E051: tri-state preserves .unknown so a transition from known-no to unknown is visible
    // to diff() and triggers re-audit, rather than collapsing to false like the old Bool.
    public let vpnActive: ConditionState
    // C090: tri-state preserves .unknown so a loopback proxy (upstream not verifiable) or
    // tunneled DNS is not reported as "plaintext" — the old Bool collapsed .unknown to false,
    // which armed a fabricated "Encrypted DNS stopped being used" alert.
    public let dnsEncrypted: ConditionState

    public init(capturedAt: Date = Date(), resolvers: [String], activeInterfaces: [String],
                vpnActive: ConditionState, dnsEncrypted: ConditionState) {
        self.capturedAt = capturedAt
        self.resolvers = resolvers
        self.activeInterfaces = activeInterfaces
        self.vpnActive = vpnActive
        self.dnsEncrypted = dnsEncrypted
    }

    /// Capture the current DNS-relevant configuration from the live system.
    public static func capture() -> NetworkConfigSnapshot {
        NetworkConfigSnapshot(
            resolvers: NetworkKeyState.getSystemDNSServers().sorted(),
            activeInterfaces: NetworkKeyState.getActiveInterfaces().sorted(),
            vpnActive: NetworkKeyState.vpnStatus().state,
            dnsEncrypted: NetworkKeyState.encryptedDNSStatus().state
        )
    }
}

public enum ConfigChangeKind: String, Codable, Sendable {
    case resolversChanged = "Resolvers changed"
    case interfacesChanged = "Active interfaces changed"
    case vpnChanged = "VPN state changed"
    case encryptedDNSChanged = "Encrypted-DNS state changed"
}

public struct ConfigChange: Hashable, Codable, Sendable {
    public let kind: ConfigChangeKind
    public let detail: String
    public init(kind: ConfigChangeKind, detail: String) {
        self.kind = kind
        self.detail = detail
    }
}

public enum Sentinel {
    /// Diff two snapshots into a list of change events. An empty list means the DNS-relevant
    /// configuration is unchanged; any entry is a reason to re-run the tampering/leak checks.
    public static func diff(previous: NetworkConfigSnapshot, current: NetworkConfigSnapshot) -> [ConfigChange] {
        var changes: [ConfigChange] = []

        if previous.resolvers != current.resolvers {
            changes.append(ConfigChange(kind: .resolversChanged,
                detail: "\(previous.resolvers.joined(separator: ", ")) -> \(current.resolvers.joined(separator: ", "))"))
        }
        if previous.activeInterfaces != current.activeInterfaces {
            changes.append(ConfigChange(kind: .interfacesChanged,
                detail: "\(previous.activeInterfaces.joined(separator: ", ")) -> \(current.activeInterfaces.joined(separator: ", "))"))
        }
        if previous.vpnActive != current.vpnActive {
            changes.append(ConfigChange(kind: .vpnChanged,
                detail: "\(previous.vpnActive.rawValue) -> \(current.vpnActive.rawValue)"))
        }
        if previous.dnsEncrypted != current.dnsEncrypted {
            // C090: tri-state diff — .unknown is a distinct outcome from .no (plaintext).
            // A yes→unknown transition is not the same as yes→no; the former means encryption
            // could not be determined, the latter means it is confirmed absent.
            changes.append(ConfigChange(kind: .encryptedDNSChanged,
                detail: "\(previous.dnsEncrypted.rawValue) -> \(current.dnsEncrypted.rawValue)"))
        }
        return changes
    }

    // MARK: - Persistence (last snapshot)

    public static var defaultURL: URL {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx", isDirectory: true)
        return base.appendingPathComponent("sentinel-snapshot.json")
    }

    public static func saveSnapshot(_ snapshot: NetworkConfigSnapshot, to url: URL = Sentinel.defaultURL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        let data = try JSONEncoder().encode(snapshot)
        try data.write(to: url, options: .atomic)
        // Owner-only: the snapshot lists your resolvers and interface names.
        // F023: propagate permission-setting failure instead of silently discarding it —
        // a world-readable snapshot leaks resolver/interface topology (PII).
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
        // D030: sign the snapshot with a Keychain-stored HMAC key so a local attacker who writes
        // the file cannot suppress config-change/drift re-audit with an unsigned baseline.
        IntegrityTag.signAndWrite(data, for: url)
    }

    public static func loadSnapshot(from url: URL = Sentinel.defaultURL) -> NetworkConfigSnapshot? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        // D030: reject unsigned or tampered baselines — a missing/mismatched HMAC means the file
        // was written by something other than Syxx (or the HMAC key was rotated). The caller treats
        // nil as "no baseline" and will re-capture on the next check.
        guard IntegrityTag.verify(data, for: url) else { return nil }
        return try? JSONDecoder().decode(NetworkConfigSnapshot.self, from: data)
    }

    /// C093: distinguishes "file absent" from "file present but corrupt/tampered" from "loaded".
    /// The old `loadSnapshot` collapsed all three to `nil`, so a truncated or tampered baseline
    /// was silently re-baselined as a first run and the evidence destroyed. Callers that need
    /// to act on corrupt baselines (preserve the file, raise a warning) should use this instead.
    public enum LoadOutcome {
        case absent
        case corrupt(String)
        case loaded(NetworkConfigSnapshot)
    }

    public static func loadSnapshotOutcome(from url: URL = Sentinel.defaultURL) -> LoadOutcome {
        let data: Data
        do {
            data = try Data(contentsOf: url)
        } catch {
            // File truly absent vs unreadable (permission denied, I/O error) — both are
            // "we have no baseline to compare against" but the reason matters for diagnostics.
            let nsError = error as NSError
            if nsError.domain == NSCocoaErrorDomain && nsError.code == NSFileReadNoSuchFileError {
                return .absent
            }
            return .corrupt("Could not read baseline file: \(error.localizedDescription)")
        }
        // D030: reject unsigned or tampered baselines.
        guard IntegrityTag.verify(data, for: url) else {
            return .corrupt("Baseline file failed integrity check (unsigned or tampered)")
        }
        do {
            let snapshot = try JSONDecoder().decode(NetworkConfigSnapshot.self, from: data)
            return .loaded(snapshot)
        } catch {
            return .corrupt("Baseline file could not be decoded: \(error.localizedDescription)")
        }
    }

    /// C093: rename a corrupt baseline to a timestamped `.corrupt-<ts>.json` so the evidence
    /// is preserved for forensics rather than being overwritten by the next tick.
    public static func quarantineCorruptBaseline(at url: URL) {
        let timestamp = ISO8601DateFormatter().string(from: Date())
            .replacingOccurrences(of: ":", with: "-")
        let quarantined = url.deletingPathExtension()
            .appendingPathExtension("corrupt-\(timestamp)")
            .appendingPathExtension("json")
        try? FileManager.default.moveItem(at: url, to: quarantined)
    }

    /// Erase the saved baseline snapshot and its HMAC sibling.
    public static func forget(at url: URL = Sentinel.defaultURL) {
        try? FileManager.default.removeItem(at: url)
        IntegrityTag.deleteHMAC(for: url)
    }
}

/// C5 — Home network baseline. The user marks their home network as known-good ONCE; every other
/// network is then diffed against it, so "this café differs from home in N ways" lists the real
/// deltas instead of a bare "changed". Distinct from the rolling `Sentinel` snapshot (which is
/// then-vs-now); this is home-vs-here and does not roll forward.
public enum HomeBaseline {

    /// The outcome of comparing the current network to the saved home baseline.
    public struct Comparison: Codable, Sendable, Equatable {
        /// False when no home baseline has been set — the comparison could not be made. NEVER
        /// reported as "matches home": an absent baseline is unknown, not clean.
        public let hasBaseline: Bool
        /// The DNS-relevant differences between home and the current network (empty ⇒ identical).
        public let differences: [ConfigChange]
        /// True only when a baseline exists AND there are zero differences.
        public var matchesHome: Bool { hasBaseline && differences.isEmpty }
        public let summary: String
    }

    /// Pure core: compare the current snapshot to the home baseline. When `home` is nil, the result
    /// is `hasBaseline == false` with no differences claimed (honest: we cannot assert a match or a
    /// mismatch against a baseline that does not exist). The mutation-tested heart of C5.
    public static func compare(home: NetworkConfigSnapshot?, current: NetworkConfigSnapshot) -> Comparison {
        guard let home else {
            return Comparison(hasBaseline: false, differences: [],
                              summary: "No home baseline set — run `syxx sentinel --set-home` on your home network first.")
        }
        let diffs = Sentinel.diff(previous: home, current: current)
        let summary: String
        if diffs.isEmpty {
            summary = "This network matches your home baseline."
        } else {
            summary = "This network differs from home in \(diffs.count) way\(diffs.count == 1 ? "" : "s")."
        }
        return Comparison(hasBaseline: true, differences: diffs, summary: summary)
    }

    // MARK: - Persistence (home baseline, distinct file from the rolling snapshot)

    public static var defaultURL: URL {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx", isDirectory: true)
        return base.appendingPathComponent("home-baseline.json")
    }

    public static func save(_ snapshot: NetworkConfigSnapshot, to url: URL = HomeBaseline.defaultURL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        try JSONEncoder().encode(snapshot).write(to: url, options: .atomic)
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }

    public static func load(from url: URL = HomeBaseline.defaultURL) -> NetworkConfigSnapshot? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(NetworkConfigSnapshot.self, from: data)
    }

    /// C093 (M8-24): richer load outcome for the home baseline — same absent/corrupt/loaded
    /// distinction as `Sentinel.loadSnapshotOutcome`.
    public static func loadOutcome(from url: URL = HomeBaseline.defaultURL) -> Sentinel.LoadOutcome {
        let data: Data
        do {
            data = try Data(contentsOf: url)
        } catch {
            let nsError = error as NSError
            if nsError.domain == NSCocoaErrorDomain && nsError.code == NSFileReadNoSuchFileError {
                return .absent
            }
            return .corrupt("Could not read home baseline: \(error.localizedDescription)")
        }
        do {
            let snapshot = try JSONDecoder().decode(NetworkConfigSnapshot.self, from: data)
            return .loaded(snapshot)
        } catch {
            return .corrupt("Home baseline could not be decoded: \(error.localizedDescription)")
        }
    }

    public static func forget(at url: URL = HomeBaseline.defaultURL) {
        try? FileManager.default.removeItem(at: url)
    }
}

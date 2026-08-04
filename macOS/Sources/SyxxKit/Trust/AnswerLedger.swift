// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Core), extended with
// JSON persistence so it functions as a real passive-DNS ledger across runs.
//
// Trust infrastructure / passive DNS: record the answers a domain returns over time and flag
// when today's answer diverges from history — the basis of derivative datapoint X5
// (answer-reputation drift), which catches a plausible-looking poisoning the point-in-time
// vantage comparison would miss.
import Foundation

public struct AnswerEntry: Identifiable, Hashable, Codable, Sendable {
    public let id: UUID
    public let domain: String
    public let queryType: String
    public let answer: String
    public let resolver: String
    public let ttl: Int
    public let capturedAt: Date
    // C110: per-network key so drift detection is scoped to the network the answer was observed
    // on. Without this, moving from home to a café produces a "DNS answer changed" .warning
    // attributing a geo-DNS difference to drift, and a poisoned answer recorded at the café
    // becomes the baseline the home network is compared against. nil for legacy entries.
    public let networkKey: String?

    public init(id: UUID = UUID(), domain: String, queryType: String, answer: String,
                resolver: String, ttl: Int, capturedAt: Date = Date(),
                networkKey: String? = nil) {
        self.id = id
        self.domain = domain
        self.queryType = queryType
        self.answer = answer
        self.resolver = resolver
        self.ttl = ttl
        self.capturedAt = capturedAt
        self.networkKey = networkKey
    }
}

public struct AnswerDrift: Identifiable, Hashable, Codable, Sendable {
    public let id: UUID
    public let domain: String
    public let queryType: String
    public let previousAnswer: String
    public let newAnswer: String
    public let resolver: String
    public let detectedAt: Date

    public init(id: UUID = UUID(), domain: String, queryType: String, previousAnswer: String,
                newAnswer: String, resolver: String, detectedAt: Date) {
        self.id = id
        self.domain = domain
        self.queryType = queryType
        self.previousAnswer = previousAnswer
        self.newAnswer = newAnswer
        self.resolver = resolver
        self.detectedAt = detectedAt
    }
}

public final class AnswerLedger: @unchecked Sendable {
    // C110: the backing storage is private — public accessors take the lock so a concurrent
    // read from another thread cannot race an in-flight insert. The old `public private(set)`
    // properties allowed unguarded reads on a @unchecked Sendable type.
    private var _entries: [AnswerEntry] = []
    private var _drifts: [AnswerDrift] = []
    private let lock = NSLock()

    /// Thread-safe snapshot of all entries.
    public var entries: [AnswerEntry] {
        lock.lock()
        defer { lock.unlock() }
        return _entries
    }

    /// Thread-safe snapshot of all drifts.
    public var drifts: [AnswerDrift] {
        lock.lock()
        defer { lock.unlock() }
        return _drifts
    }

    /// C103: tri-state result from `record`. The old API returned `AnswerDrift?` which collapsed
    /// "first-seen" (no prior history) and "consistent" (matches last recorded) into `nil` — a
    /// first observation was consumed as evidence of stability. The enum makes the distinction
    /// explicit so callers can render "recording began for this domain" instead of silence.
    public enum LedgerObservation: Hashable, Sendable {
        case firstSeen
        case consistent
        /// C102: `isDisjoint` is true when the old and new answer sets share no member — that
        /// is the poisoning-relevant signal. Overlapping changes (CDN rotation, round-robin)
        /// are reported with `isDisjoint == false` so callers can downgrade severity.
        case drifted(AnswerDrift, isDisjoint: Bool)
    }

    public init() {}

    /// Record an observed answer set. `answers` is the full set of rdata strings from the DNS
    /// response (C102: the old API accepted a single `answer: String` which was typically
    /// `response.records.first` — one arbitrary element of a rotated RRset — so CDN round-robin
    /// triggered drift alerts on almost every tick).
    ///
    /// Drift detection compares sorted answer sets and only flags a change when the old and new
    /// sets are disjoint (no common member). An overlapping change is still recorded but flagged
    /// as `isDisjoint: false` so the caller can distinguish CDN rotation from poisoning.
    ///
    /// C103: returns `LedgerObservation` instead of `AnswerDrift?` — `.firstSeen` is never
    /// silently treated as "stable".
    @discardableResult
    // C110: `networkKey` scopes drift detection to the network the answer was observed on.
    // When provided, drift is only compared against entries from the same network — a first
    // observation on a new network is `firstSeen`, not drift. nil preserves legacy behaviour.
    public func record(domain: String, queryType: String, answers: [String],
                       resolver: String, ttl: Int,
                       networkKey: String? = nil) -> LedgerObservation {
        // C102: store the full sorted answer set so drift compares sets, not single records.
        let sortedAnswers = answers.sorted()
        let joinedAnswers = sortedAnswers.joined(separator: ",")
        let entry = AnswerEntry(domain: domain.lowercased(), queryType: queryType,
                                answer: joinedAnswers, resolver: resolver, ttl: ttl,
                                networkKey: networkKey)
        lock.lock()
        let observation = detectDrift(entry: entry)
        _entries.insert(entry, at: 0)
        // C103: eviction must preserve at least one entry per (networkKey, domain, queryType)
        // so a key's anchor is not silently evicted by unrelated traffic, which would make the
        // next observation "first-seen" and mask a real drift.
        if _entries.count > 1000 { trimEntriesPreservingLastPerKey(cap: 1000) }
        lock.unlock()
        return observation
    }

    /// Backward-compatible single-answer overload — wraps one answer in a one-element set.
    @discardableResult
    public func record(domain: String, queryType: String, answer: String,
                       resolver: String, ttl: Int,
                       networkKey: String? = nil) -> LedgerObservation {
        record(domain: domain, queryType: queryType, answers: [answer],
               resolver: resolver, ttl: ttl, networkKey: networkKey)
    }

    public func history(for domain: String) -> [AnswerEntry] {
        lock.lock()
        let result = _entries.filter { $0.domain == domain.lowercased() }
        lock.unlock()
        return result
    }

    public func drifts(for domain: String) -> [AnswerDrift] {
        lock.lock()
        let result = _drifts.filter { $0.domain == domain.lowercased() }
        lock.unlock()
        return result
    }

    /// C102: drift compares sorted answer sets. A disjoint result (no common member) is the
    /// poisoning-relevant signal; an overlapping change is CDN rotation.
    /// C103: returns `LedgerObservation` so `.firstSeen` is distinct from `.consistent`.
    /// C110: key on (networkKey, domain, queryType) when networkKey is available — a first
    /// observation on a new network is `firstSeen`, not drift.
    @discardableResult
    private func detectDrift(entry: AnswerEntry) -> LedgerObservation {
        guard let last = _entries.first(where: {
            $0.domain == entry.domain && $0.queryType == entry.queryType
                && (entry.networkKey == nil || $0.networkKey == entry.networkKey)
        }) else {
            return .firstSeen
        }
        guard last.answer != entry.answer else { return .consistent }

        let oldSet = Set(last.answer.split(separator: ",").map(String.init))
        let newSet = Set(entry.answer.split(separator: ",").map(String.init))
        let isDisjoint = oldSet.isDisjoint(with: newSet)

        let drift = AnswerDrift(domain: entry.domain, queryType: entry.queryType,
                                previousAnswer: last.answer, newAnswer: entry.answer,
                                resolver: entry.resolver, detectedAt: Date())
        _drifts.insert(drift, at: 0)
        if _drifts.count > 200 { _drifts = Array(_drifts.prefix(200)) }
        return .drifted(drift, isDisjoint: isDisjoint)
    }

    /// C103: trim entries to `cap`, but always preserve the most recent entry for each
    /// (domain, queryType) key so an eviction cannot turn a real drift into a "first-seen" nil.
    private func trimEntriesPreservingLastPerKey(cap: Int) {
        guard _entries.count > cap else { return }
        var seenKeys = Set<String>()
        var anchors: [Int] = []
        for (i, e) in _entries.enumerated() {
            let key = "\(e.domain)|\(e.queryType)"
            if !seenKeys.contains(key) {
                seenKeys.insert(key)
                anchors.append(i)
            }
        }
        let anchorSet = Set(anchors)
        var kept: [AnswerEntry] = []
        for (i, e) in _entries.enumerated() {
            if kept.count < cap || anchorSet.contains(i) {
                kept.append(e)
            }
        }
        _entries = kept
    }

    // MARK: - Persistence

    private struct Persisted: Codable {
        let entries: [AnswerEntry]
        let drifts: [AnswerDrift]
    }

    /// Answers/drifts older than this are pruned on load and save — the ledger is a rolling
    /// window for drift detection, not a permanent browsing history.
    public static let maxEntryAge: TimeInterval = 30 * 86400

    public static var defaultURL: URL {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx", isDirectory: true)
        return base.appendingPathComponent("answer-ledger.json")
    }

    /// C103: age pruning must not remove the last remaining entry for a (domain, queryType)
    /// key — doing so silently converts a real drift into a "first-seen" nil on the next
    /// observation. Stale anchors are kept regardless of age so drift detection always has a
    /// prior answer to compare against.
    private func pruneAged(now: Date = Date()) {
        let cutoff = now.addingTimeInterval(-AnswerLedger.maxEntryAge)
        var seenKeys = Set<String>()
        var result: [AnswerEntry] = []
        for entry in _entries {
            let key = "\(entry.domain)|\(entry.queryType)"
            let isAnchor = !seenKeys.contains(key)
            if isAnchor { seenKeys.insert(key) }
            if entry.capturedAt >= cutoff || isAnchor {
                result.append(entry)
            }
        }
        _entries = result
        _drifts = _drifts.filter { $0.detectedAt >= cutoff }
    }

    public func save(to url: URL = AnswerLedger.defaultURL) throws {
        lock.lock()
        pruneAged()
        let persisted = Persisted(entries: _entries, drifts: _drifts)
        lock.unlock()
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        let data = try JSONEncoder().encode(persisted)
        try data.write(to: url, options: .atomic)
        // Owner-only: this file records domains resolved and their IPs — a browsing-adjacent trail.
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
        // D030: sign the ledger with a Keychain-stored HMAC key so a local attacker who writes
        // the file cannot suppress drift detection with an unsigned baseline.
        IntegrityTag.signAndWrite(data, for: url)
    }

    @discardableResult
    public func load(from url: URL = AnswerLedger.defaultURL) -> Bool {
        guard let data = try? Data(contentsOf: url) else { return false }
        // D030: reject unsigned or tampered baselines — a missing/mismatched HMAC means the file
        // was written by something other than Syxx. The caller treats false as "no history" and
        // will re-capture on the next check.
        guard IntegrityTag.verify(data, for: url),
              let decoded = try? JSONDecoder().decode(Persisted.self, from: data) else { return false }
        lock.lock()
        _entries = decoded.entries
        _drifts = decoded.drifts
        pruneAged()
        lock.unlock()
        return true
    }

    /// Erase all retained history, in memory and on disk, including the HMAC sibling.
    public func forget(at url: URL = AnswerLedger.defaultURL) {
        lock.lock()
        _entries.removeAll()
        _drifts.removeAll()
        lock.unlock()
        try? FileManager.default.removeItem(at: url)
        IntegrityTag.deleteHMAC(for: url)
    }
}

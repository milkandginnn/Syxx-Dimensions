import Foundation

// A5 (ROADMAP "Implementation plan v2" — continuous monitoring). Turns Sentinel's snapshot/diff
// primitives into a monitoring TICK: compare this network's DNS-relevant configuration against the
// last known baseline, decide whether anything drifted, and raise alerts a user can act on.
//
// Built on the existing Trust/ primitives (NetworkConfigSnapshot.capture, Sentinel.diff,
// Sentinel.save/loadSnapshot) — this adds the decision + alert layer, not new capture logic.
//
// PURE/LIVE SPLIT: evaluate() is pure and holds every honesty-relevant decision; tickOnce() only
// does the IO. The untestable part is kept as thin as possible.
//
// D-5: tickOnce() now performs lightweight sample DNS queries against the system resolvers and
// records the answers in AnswerLedger. If an answer differs from the last recorded answer for the
// same domain, a drift alert is raised. This makes per-network DNS history real — without it,
// there is no data to summarize or compare.
//
// HONESTY CONTRACT (Protocol §1/§10):
//   * NO BASELINE IS NOT "STABLE". A first run has nothing to compare against — that is
//     `.baselineEstablished`, never `.noChange`. Reporting "no changes detected" when we have never
//     looked before is the monitoring equivalent of a false clean, and it is the single most likely
//     way this feature would lie.
//   * A drift is reported as a REASON TO RE-CHECK, not as a result. Sentinel sees configuration,
//     not tampering: a resolver change might be a coffee-shop DHCP lease or an attack, and this
//     layer must not pretend to know which. Only a re-run of the disciplines can say.
//   * Losing protection is escalated above gaining it: encrypted -> plaintext and VPN active ->
//     inactive are the transitions that expose a user, and they are .warning, not .info.
//   * DNS failure for any sample domain must not crash the monitoring loop. Failed queries are
//     silently skipped — a timeout or transport error is not evidence of tampering and must not
//     produce a false result in either direction.
//
// Byte-parity twin of Windows `Syxx.Kit/Trust/ContinuousMonitor.cs` — the strings MUST match.
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md:42).

/// What a monitoring tick concluded. `.baselineEstablished` is deliberately distinct from
/// `.noChange` — "we had nothing to compare against" must never render as "nothing changed".
public enum MonitorState: String, Codable, Sendable {
    case baselineEstablished, noChange, changed
}

/// A user-facing monitoring alert. Severity reuses the shared palette so a monitor alert and a
/// result badge cannot disagree about what amber means.
public struct MonitorAlert: Equatable, Sendable {
    public let severity: Severity
    public let headline: String
    public let detail: String

    public init(severity: Severity, headline: String, detail: String) {
        self.severity = severity
        self.headline = headline
        self.detail = detail
    }
}

/// The outcome of one tick.
public struct MonitorTick: Equatable, Sendable {
    public let state: MonitorState
    public let changes: [ConfigChange]
    public let alerts: [MonitorAlert]
    public let summary: String
    public let recheckRecommended: Bool
    // C111: when this tick ran. Without a timestamp, a surface showing "last checked" cannot
    // distinguish "checked, nothing changed" from "did not check" — and the scheduler's
    // lastChange latch cannot say "something changed at HH:MM".
    public let capturedAt: Date

    public init(state: MonitorState, changes: [ConfigChange], alerts: [MonitorAlert],
                summary: String, recheckRecommended: Bool,
                capturedAt: Date = Date()) {
        self.state = state
        self.changes = changes
        self.alerts = alerts
        self.summary = summary
        self.recheckRecommended = recheckRecommended
        self.capturedAt = capturedAt
    }
}

public enum ContinuousMonitor {

    // D-5: Well-known domains sampled each tick to build per-network answer history.
    // A-records only; lightweight enough to run in under a second on any connection.
    static let sampleDomains = ["apple.com", "google.com", "cloudflare.com"]

    // D-5: Injectable DNS resolution function. Production uses the real DNSResolver;
    // tests inject fakes. The closure receives (domain, type, server) and returns a
    // DNSResponse or nil on complete failure. Throwing closures are caught per-domain.
    public typealias DNSResolveFunc = @Sendable (String, UInt16, String) async throws -> DNSResponse?

    /// The PURE decision: given the previous baseline (nil on a first run) and the current
    /// snapshot, decide what drifted and what to tell the user. No IO, no clock, no network.
    public static func evaluate(previous: NetworkConfigSnapshot?, current: NetworkConfigSnapshot) -> MonitorTick {
        // C111: propagate the snapshot's capturedAt into the tick so surfaces can show when
        // the tick ran.
        let now = current.capturedAt
        // Honesty gate — never report "no change" when there was nothing to compare against.
        guard let previous else {
            return MonitorTick(
                state: .baselineEstablished, changes: [], alerts: [],
                summary: "Baseline recorded for this network. Nothing has been compared yet — monitoring starts from now.",
                recheckRecommended: false, capturedAt: now)
        }

        let changes = Sentinel.diff(previous: previous, current: current)
        if changes.isEmpty {
            return MonitorTick(
                state: .noChange, changes: changes, alerts: [],
                summary: "No change to this network's DNS configuration since the last check.",
                recheckRecommended: false, capturedAt: now)
        }

        let alerts = changes.map { toAlert($0, previous: previous, current: current) }
        let what = changes.map { $0.kind.rawValue }.joined(separator: "; ")
        return MonitorTick(
            state: .changed, changes: changes, alerts: alerts,
            summary: "This network's DNS configuration changed (\(what)). A configuration change is not proof of tampering — re-run the checks to find out what it means.",
            recheckRecommended: true, capturedAt: now)
    }

    /// Map a configuration change to an alert. A change that REMOVES protection is a warning; a
    /// change that adds it, or is merely different, is informational. Neither is a tamper result —
    /// Sentinel observes configuration, not honesty (that is what a re-check is for).
    private static func toAlert(_ change: ConfigChange,
                                previous: NetworkConfigSnapshot,
                                current: NetworkConfigSnapshot) -> MonitorAlert {
        switch change.kind {
        case .encryptedDNSChanged:
            // C090: tri-state — distinguish confirmed-loss (.yes→.no) from indeterminate
            // (.yes→.unknown or .no→.unknown). Only confirmed loss is a warning; unknown
            // transitions are informational with neutral wording.
            let lost = previous.dnsEncrypted == .yes && current.dnsEncrypted == .no
            let nowUnknown = current.dnsEncrypted == .unknown
            let headline: String
            let detail: String
            let severity: Severity
            if lost {
                severity = .warning
                headline = "Encrypted DNS stopped being used on this network."
                detail = "Your lookups are now visible to the network. Re-check, and turn encrypted DNS back on if you did not intend this."
            } else if nowUnknown {
                severity = .info
                headline = "Encrypted DNS state is now unknown."
                detail = "Syxx could not determine whether DNS is encrypted (loopback proxy or VPN tunnel). This is not a confirmed loss of protection."
            } else {
                severity = .info
                headline = "Encrypted DNS is now in use on this network."
                detail = change.detail
            }
            return MonitorAlert(severity: severity, headline: headline, detail: detail)

        case .vpnChanged:
            // E051: tri-state — VPN is "lost" when it was confirmed active and is now either
            // confirmed inactive or unknown (both mean the tunnel can no longer be relied on).
            let lost = previous.vpnActive == .yes && current.vpnActive != .yes
            let headline: String
            if lost {
                headline = current.vpnActive == .unknown
                    ? "VPN state is now unknown."
                    : "Your VPN is no longer active."
            } else {
                headline = "A VPN is now active."
            }
            return MonitorAlert(
                severity: lost ? .warning : .info,
                headline: headline,
                detail: lost
                    ? "Traffic that you may expect to be tunnelled is not. Re-check for leaks before trusting this network."
                    : change.detail)

        case .resolversChanged:
            return MonitorAlert(
                severity: .warning,
                headline: "The DNS resolvers on this network changed.",
                detail: "\(change.detail). This is normal when you join a new network, and it is also what a hijack looks like — re-run the checks to tell the difference.")

        case .interfacesChanged:
            return MonitorAlert(severity: .info, headline: change.kind.rawValue, detail: change.detail)
        }
    }

    /// One live tick: load the stored baseline, capture the current configuration, decide, and
    /// persist the new baseline. IO only — the decision is evaluate().
    ///
    /// D-5: After the config snapshot diff, performs lightweight sample DNS queries against the
    /// system resolvers and records the answers in AnswerLedger. Drifts (answers that differ from
    /// the last recorded answer for the same domain) appear as additional alerts in the tick.
    ///
    /// - Parameters:
    ///   - url: where the sentinel snapshot is stored.
    ///   - persist: false to evaluate without moving the baseline (a dry run).
    ///   - ledger: an injected AnswerLedger for testing; nil uses a fresh real ledger.
    ///   - resolve: an injected DNS resolution function for testing; nil uses the real DNSResolver.
    @discardableResult
    public static func tickOnce(
        at url: URL = Sentinel.defaultURL,
        persist: Bool = true,
        ledger: AnswerLedger? = nil,
        resolve: DNSResolveFunc? = nil
    ) async -> MonitorTick {
        // C093: use the richer load outcome so a corrupt/tampered baseline is distinguished from
        // a truly absent one — the old code collapsed both to nil and re-baselined silently.
        let outcome = Sentinel.loadSnapshotOutcome(from: url)
        var extraAlerts: [MonitorAlert] = []
        let previous: NetworkConfigSnapshot?
        switch outcome {
        case .absent:
            previous = nil
        case .loaded(let snapshot):
            previous = snapshot
        case .corrupt(let reason):
            previous = nil
            // C093: do NOT overwrite the corrupt file — rename it so the evidence is preserved.
            Sentinel.quarantineCorruptBaseline(at: url)
            extraAlerts.append(MonitorAlert(
                severity: .warning,
                headline: "The monitoring baseline was unreadable or tampered.",
                detail: "\(reason). The corrupt file has been quarantined and a fresh baseline will be established. This could indicate a local attacker resetting the baseline to hide changes."))
        }

        let current = NetworkConfigSnapshot.capture()
        let tick = evaluate(previous: previous, current: current)

        // D-5: Sample DNS queries → AnswerLedger. Drifts become additional alerts.
        let sampling = await sampleDNSAndRecord(
            resolvers: current.resolvers, resolve: resolve, ledger: ledger)

        // C094: propagate persist failures so a failed write is not silently reported as success.
        // The old code used `try?` and lost the error, so a read-only home directory or full disk
        // caused the monitor to report "baseline recorded" forever without ever persisting one.
        var persistError: String? = nil
        if persist {
            do {
                try Sentinel.saveSnapshot(current, to: url)
            } catch {
                persistError = error.localizedDescription
                extraAlerts.append(MonitorAlert(
                    severity: .warning,
                    headline: "Could not save the monitoring baseline.",
                    detail: "Write to \(url.path) failed: \(error.localizedDescription). Changes cannot be detected until this is fixed."))
            }
        }

        let allAlerts = tick.alerts + extraAlerts + sampling.alerts

        // C089: when answer-drift alerts exist, the tick must not claim .noChange.
        let hasDrifts = !sampling.alerts.isEmpty
        let state: MonitorState = hasDrifts && tick.state == .noChange ? .changed : tick.state
        let summary: String
        if hasDrifts && tick.state == .noChange {
            summary = "DNS answers changed for one or more sampled domains, even though the network configuration did not change. This is the passive poisoning signal — re-run the checks."
        } else if sampling.succeeded == 0 && sampling.attempted > 0 {
            // C095: total sampling failure is inconclusive, not clean.
            summary = tick.summary + " (DNS sampling was inconclusive this round — no answers could be verified.)"
        } else if persistError != nil {
            summary = tick.summary + " (Baseline could not be persisted — see warning above.)"
        } else {
            summary = tick.summary
        }

        return MonitorTick(
            state: state,
            changes: tick.changes,
            alerts: allAlerts,
            summary: summary,
            recheckRecommended: tick.recheckRecommended || hasDrifts || !extraAlerts.isEmpty)
    }

    /// C095: result of one DNS sampling round. The old function returned `[MonitorAlert]` only,
    /// so a round where every query failed was indistinguishable from a round where everything
    /// was stable. The struct carries enough information for the tick to report "3/3 checked"
    /// or "0/3 checked — inconclusive".
    struct DNSSamplingRecord: Sendable {
        let alerts: [MonitorAlert]
        let attempted: Int
        let succeeded: Int
    }

    // D-5: Perform sample DNS queries against the first system resolver, record each answer
    // in the ledger, and return drift alerts for any answers that changed. Pure given its
    // inputs (no disk/network IO of its own) — the resolve function and ledger are injected.
    //
    // Honesty: a DNS failure or empty response for one domain does NOT affect the other domains
    // and does NOT produce an alert. A missing response is inconclusive for that domain, not
    // evidence of tampering or cleanliness.
    //
    // C095: returns a DNSSamplingRecord so callers can distinguish "0/3 checked" (inconclusive)
    // from "3/3 checked, all stable" (clean). The old `[MonitorAlert]` return made these
    // indistinguishable — a total DNS blackout rendered as a clean tick.
    //
    // C102: records the full answer set (all rdata, sorted) and uses disjoint-set drift detection
    // so CDN round-robin (overlapping answer sets) does not fire a .warning alert every tick.
    // Disjoint answer sets are still .warning; overlapping changes are .info.
    //
    // C103: `.firstSeen` is not treated as silence — it means recording just began and there is
    // no prior anchor to compare against.
    static func sampleDNSAndRecord(
        resolvers: [String],
        resolve: DNSResolveFunc?,
        ledger: AnswerLedger?,
        ledgerURL: URL = AnswerLedger.defaultURL
    ) async -> DNSSamplingRecord {
        guard let firstResolver = resolvers.first else {
            return DNSSamplingRecord(alerts: [], attempted: 0, succeeded: 0)
        }
        let activeLedger = ledger ?? AnswerLedger()
        // C110: when a ledger is injected (testing), do NOT load from or save to the user's
        // real ledger file — the old code always loaded from and saved to defaultURL regardless
        // of injection, overwriting the developer's real answer history from tests.
        let isInjected = ledger != nil
        // C103: track whether the ledger loaded successfully — if the file was present but
        // corrupt, we should not overwrite it with an empty in-memory ledger.
        let loaded = isInjected ? true : activeLedger.load(from: ledgerURL)

        // C110: derive a network key from the current resolver set so drift detection is scoped
        // to the network the answer was observed on. A sorted hash of resolvers is sufficient —
        // the same network produces the same DHCP lease and thus the same resolver set.
        let networkKey = resolvers.sorted().joined(separator: ",")

        var alerts: [MonitorAlert] = []
        var attempted = 0
        var succeeded = 0
        for domain in sampleDomains {
            attempted += 1
            do {
                let response: DNSResponse?
                if let resolve {
                    response = try await resolve(domain, 1, firstResolver)
                } else {
                    response = await DNSResolver.queryUDPDetailed(
                        DNSQuery(name: domain, type: 1, server: firstResolver))
                }
                guard let response, response.status == .success,
                      !response.records.isEmpty else { continue }
                succeeded += 1

                // C102: pass the full answer set, not just .first. A CDN-fronted name returns
                // a rotated RRset; recording one arbitrary element made drift fire on rotation.
                let allAnswers = response.records.map(\.rdata)
                let minTTL = response.records.map { Int($0.ttl) }.min() ?? 300

                // C110: pass the network key so drift is scoped to this network.
                let observation = activeLedger.record(
                    domain: domain, queryType: "A",
                    answers: allAnswers, resolver: firstResolver,
                    ttl: minTTL, networkKey: networkKey)

                if case .drifted(let drift, let isDisjoint) = observation {
                    // C102: disjoint answer sets = poisoning-relevant, .warning.
                    // Overlapping change = likely CDN rotation, .info.
                    alerts.append(MonitorAlert(
                        severity: isDisjoint ? .warning : .info,
                        headline: isDisjoint
                            ? "DNS answer set for \(domain) changed completely"
                            : "DNS answer set for \(domain) changed (overlapping)",
                        detail: "Was \(drift.previousAnswer), now \(drift.newAnswer) via \(drift.resolver)"))
                }
                // C103: .firstSeen and .consistent produce no alert — .firstSeen means
                // recording just began and there is no anchor to compare against.
            } catch {
                // DNS failure for a sample domain must not crash the loop
            }
        }

        // C095: if no samples succeeded and we attempted some, flag inconclusive — the caller
        // must not render this as a clean tick.
        if succeeded == 0 && attempted > 0 {
            alerts.append(MonitorAlert(
                severity: .info,
                headline: "DNS sampling was inconclusive this round.",
                detail: "Could not sample any DNS answers (\(attempted) attempted, 0 succeeded). This check cannot confirm whether answers are stable — it is inconclusive, not clean."))
        }

        // C110: only save when not injected (testing) — an injected ledger must not touch disk.
        // C103: only save the ledger if it loaded successfully — overwriting a corrupt file
        // with an empty ledger destroys passive-DNS history.
        if !isInjected && (loaded || activeLedger.entries.isEmpty == false) {
            try? activeLedger.save(to: ledgerURL)
        }
        return DNSSamplingRecord(alerts: alerts, attempted: attempted, succeeded: succeeded)
    }
}

import Foundation

#if canImport(Darwin)
import Darwin
#endif

// MARK: - DNS transport state (the third question)
//
// Syxx asks three DIFFERENT questions about DNS transport. They can legitimately disagree, and a
// disagreement is itself a finding. Collapsing them is how the app came to display a transport it
// had never observed.
//
//   Q1 CONFIGURED  — is the resolver configuration declaring an encrypted transport?
//                    `NetworkKeyState.encryptedDNSStatus()` (NetworkKeyState.swift:556) — inference
//                    from `scutil --dns`. Answers "what is the machine SET UP to do".
//   Q2 REACHABLE   — can an encrypted DNS endpoint be reached from this network?
//                    `EncryptedTransportProbe.probe()` (EncryptedTransportProbe.swift:48) — a live
//                    DoH query to a public endpoint. Answers "is encrypted DNS possible here".
//   Q3 OBSERVED    — are MY DNS queries actually leaving this machine encrypted?
//                    THIS FILE. Answers the only question a user actually asked. Neither Q1 nor Q2
//                    can answer it: a machine can be configured for DoH and still fall back to
//                    plaintext, and a reachable DoH endpoint is a property of the network, not of
//                    the query path.
//
// Q3 is answerable ONLY by observing the query path. This file holds the state contract and the
// pure determination core; the observers that feed it live elsewhere (`FlowTransportObserver` for
// the unprivileged tier, a privileged packet observer for the definitive tier).
//
// HONESTY SPINE, enforced structurally rather than by convention:
//   • Two independent tri-states, never a Bool and never one collapsed value. A tunnel being up is
//     NOT evidence that DNS is encrypted — a VPN routinely carries cleartext port 53 to the
//     provider's resolver, and detecting exactly that is Glutton's job.
//   • Silence is not evidence. A window with no observed DNS activity is UNDETERMINED, never
//     "encrypted" and never "cleartext".
//   • No observer, no privilege, no sample ⇒ UNDETERMINED. Absence of an observer must never read
//     as either answer.
//   • A sample carries its own observation time and validity window and is readable ONLY through
//     `reading(...)`, which requires a clock. There is no stored `state` property a consumer can
//     read without proving freshness, so a stale value cannot be rendered as current
//     (MEMORY.md:1809).
//   • Positive and negative evidence are NOT symmetric. Confirming cleartext needs one observation.
//     Confirming encrypted needs full coverage of the window plus affirmative encrypted-DNS
//     activity plus zero cleartext — because a gap in coverage can hide a cleartext query.

/// A three-valued signal. Never a `Bool`: every observation path here fails routinely (no
/// observer, no privilege, no traffic in the window, sample in flight, interface just changed) and
/// a boolean has nowhere to put those but `false`, which would read as a measurement that says
/// "not encrypted".
public enum TransportSignal: String, Codable, Sendable, Equatable {
    /// Affirmatively observed to be the case.
    case confirmedTrue
    /// Affirmatively observed NOT to be the case.
    case confirmedFalse
    /// Not established. This is the resting state, and the only honest state when the evidence is
    /// absent, incomplete, expired, or self-contradictory.
    case undetermined
}

/// Why a signal holds the value it holds. Machine-readable codes, not display copy: a surface must
/// render owner-approved wording keyed off these, never the diagnostic strings in `TransportEvidence`.
public enum TransportReason: String, Codable, Sendable, Equatable {
    // MARK: confirmedFalse (DNS observed in cleartext)
    /// A DNS query was observed leaving this host in cleartext (port 53, off-host destination).
    case cleartextQueryObserved

    // MARK: confirmedTrue (DNS observed encrypted)
    /// DNS activity was observed over an encrypted DNS transport, with no cleartext in the window.
    case encryptedTransportObserved

    // MARK: VPN
    /// A tunnel interface carries a routable address.
    case tunnelInterfaceActive
    /// No tunnel interface carries a routable address.
    case noTunnelInterfaceActive

    // MARK: undetermined
    /// Nothing has been observed yet.
    case notYetObserved
    /// No observer is available: the privileged helper is absent, not running, denied, or the
    /// unprivileged sampler could not be executed. NEVER an answer about encryption.
    case observerUnavailable
    /// The observer ran but failed (sampler error, parse failure, capture aborted).
    case observationFailed
    /// The observer ran and saw no DNS activity at all in the window. Silence is not cleartext.
    case noDNSActivityObserved
    /// DNS activity occurred, but the transport could not be observed with the available method.
    /// The common case: an unconnected UDP socket, whose peer address exists only per datagram and
    /// is therefore invisible to every socket-level tool. Requires the packet-observation tier.
    case dnsActivityTransportUnobservable
    /// The sample is older than its validity window, or the clock moved in a way that makes its age
    /// untrustworthy. Expires to undetermined; never falls back to its last value.
    case sampleExpired
    /// The network changed (interface set or resolver set) since the sample was taken, so the
    /// sample describes a different path than the one in effect now.
    case networkChanged
    /// The requested window is not fully covered by samples, so a gap could hide a cleartext query.
    case incompleteWindowCoverage
    /// Samples within the window disagree — the state changed mid-window. Reporting either value
    /// next to results gathered across the whole window would be a fabricated correlation.
    case stateChangedDuringWindow
}

/// How an observation was made. Recorded so a consumer can weigh it and so "we never looked" is
/// distinguishable from "we looked and saw nothing".
public enum TransportObservationMethod: String, Codable, Sendable, Equatable {
    /// Definitive tier: DNS packets observed on the wire (requires privilege).
    case packetObservation
    /// Corroborating tier: process-attributed socket/flow deltas (no privilege). Can confirm
    /// cleartext DNS over TCP/53 and encrypted DNS flows; CANNOT see the peer of an unconnected
    /// UDP socket, so it cannot confirm or exclude cleartext UDP/53 on its own.
    case flowObservation
    /// Interface enumeration (`getifaddrs`) — the VPN signal only.
    case interfaceEnumeration
    /// No observation was made.
    case none
}

/// What was observed, in counts. Carries no domain names and no packet payload: the observers are
/// specified to read transport metadata only, and this struct is the whole retained record.
public struct TransportEvidence: Codable, Sendable, Equatable {
    public let method: TransportObservationMethod
    /// DNS exchanges observed leaving this host in cleartext (port 53, non-loopback peer).
    public let cleartextOffHost: Int
    /// DNS exchanges observed over an encrypted DNS transport (DoT 853, DoQ 8853, or DoH 443 to a
    /// configured encrypted-resolver address), attributed to a non-Syxx process.
    public let encryptedDNS: Int
    /// DNS exchanges observed to a loopback peer — an on-host resolver proxy hop. This is NOT
    /// "leaving the machine in cleartext"; the proxy's own upstream transport is a separate
    /// observation and is undetermined unless separately observed.
    public let cleartextLoopback: Int
    /// DNS activity that occurred but whose transport the method could not see (unconnected UDP).
    public let transportUnobservable: Int
    /// Interfaces the observation covered. Empty means coverage is unknown, not "all".
    public let interfaces: [String]
    /// Diagnostic detail for logs and delta reports. NOT approved user-facing copy: any surface
    /// must render owner-approved wording keyed off `TransportReason`, not this string.
    public let detail: String

    public init(method: TransportObservationMethod,
                cleartextOffHost: Int = 0,
                encryptedDNS: Int = 0,
                cleartextLoopback: Int = 0,
                transportUnobservable: Int = 0,
                interfaces: [String] = [],
                detail: String = "") {
        self.method = method
        self.cleartextOffHost = cleartextOffHost
        self.encryptedDNS = encryptedDNS
        self.cleartextLoopback = cleartextLoopback
        self.transportUnobservable = transportUnobservable
        self.interfaces = interfaces
        self.detail = detail
    }

    public static let none = TransportEvidence(method: .none, detail: "no observation")

    /// True when this evidence records no observation at all. A state with no evidence is
    /// undetermined by definition.
    public var isEmpty: Bool {
        method == .none
            || (cleartextOffHost == 0 && encryptedDNS == 0
                && cleartextLoopback == 0 && transportUnobservable == 0)
    }
}

// MARK: - Monotonic clock

/// Monotonic timestamp source for staleness checks.
///
/// Wall-clock time is attacker- and user-manipulable: moving the clock backwards would make an
/// arbitrarily old sample look fresh, which is precisely the "cache staleness into a claim" failure
/// MEMORY.md:1809 forbids. `CLOCK_MONOTONIC_RAW` cannot be set, and on Darwin it continues to
/// advance while the system is asleep, so a sample taken before a multi-hour sleep correctly
/// expires on wake instead of being served as current.
public enum MonotonicClock {
    public static func nowNanos() -> UInt64 {
        #if canImport(Darwin)
        return clock_gettime_nsec_np(CLOCK_MONOTONIC_RAW)
        #else
        var ts = timespec()
        clock_gettime(CLOCK_MONOTONIC, &ts)
        return UInt64(ts.tv_sec) * 1_000_000_000 + UInt64(ts.tv_nsec)
        #endif
    }
}

// MARK: - Sample

/// One observation tick. Two independent signals, each with its own evidence, plus the timestamps
/// that make staleness checkable.
///
/// The signals are deliberately NOT modelled as a single enum of (dns / vpn / both): that shape
/// cannot express "undetermined" per signal and collapses the distinction the moment one of the two
/// cannot be observed. All nine combinations are meaningful; four of them are named in the tests.
public struct DNSTransportSample: Codable, Sendable, Equatable {
    /// Are this host's DNS queries leaving encrypted? `confirmedTrue` = encrypted,
    /// `confirmedFalse` = cleartext, `undetermined` = not established.
    public let dnsEncryption: TransportSignal
    public let dnsReason: TransportReason
    public let dnsEvidence: TransportEvidence

    /// Is a VPN tunnel carrying traffic? Independent of `dnsEncryption` in both directions: a
    /// confirmed tunnel must never fill in an unobserved DNS state, and vice versa.
    public let vpn: TransportSignal
    public let vpnReason: TransportReason
    public let vpnEvidence: TransportEvidence

    /// Wall-clock observation time, for display and for signed evidence.
    public let observedAt: Date
    /// Monotonic observation time, for the staleness check. Not manipulable.
    public let observedAtMonotonicNanos: UInt64
    /// How long this sample may be treated as current.
    public let validFor: TimeInterval
    /// Identity of the network path the sample describes (interface set + resolver set). A change
    /// invalidates the sample immediately, without waiting for the validity window to elapse.
    public let networkFingerprint: String

    public init(dnsEncryption: TransportSignal,
                dnsReason: TransportReason,
                dnsEvidence: TransportEvidence,
                vpn: TransportSignal,
                vpnReason: TransportReason,
                vpnEvidence: TransportEvidence,
                observedAt: Date,
                observedAtMonotonicNanos: UInt64,
                validFor: TimeInterval,
                networkFingerprint: String) {
        self.dnsEncryption = dnsEncryption
        self.dnsReason = dnsReason
        self.dnsEvidence = dnsEvidence
        self.vpn = vpn
        self.vpnReason = vpnReason
        self.vpnEvidence = vpnEvidence
        self.observedAt = observedAt
        self.observedAtMonotonicNanos = observedAtMonotonicNanos
        self.validFor = validFor
        self.networkFingerprint = networkFingerprint
    }

    /// Age in seconds against a monotonic reading, or `nil` when `now` precedes the observation
    /// (which cannot happen with a monotonic clock and therefore means the reading is not
    /// trustworthy — treated as expired by `reading`).
    public func age(nowMonotonicNanos now: UInt64) -> TimeInterval? {
        guard now >= observedAtMonotonicNanos else { return nil }
        return TimeInterval(now - observedAtMonotonicNanos) / 1_000_000_000
    }
}

// MARK: - Reading

/// The ONLY way to read a sample's signals. Requires a clock and the current network fingerprint,
/// so a consumer cannot obtain a value without proving it is current and describes this network.
public struct DNSTransportReading: Sendable, Equatable {
    public let dnsEncryption: TransportSignal
    public let dnsReason: TransportReason
    public let dnsEvidence: TransportEvidence
    public let vpn: TransportSignal
    public let vpnReason: TransportReason
    public let vpnEvidence: TransportEvidence
    /// The sample this reading came from, or `nil` when there was none or it had expired. `nil`
    /// with both signals `undetermined` is the correct resting state.
    public let observedAt: Date?
    public let age: TimeInterval?

    /// Both signals undetermined for the given reason, with no evidence.
    public static func undetermined(_ reason: TransportReason) -> DNSTransportReading {
        DNSTransportReading(dnsEncryption: .undetermined, dnsReason: reason, dnsEvidence: .none,
                            vpn: .undetermined, vpnReason: reason, vpnEvidence: .none,
                            observedAt: nil, age: nil)
    }

    /// Menubar-glyph convenience: the glyph animates when EITHER signal is confirmed true, per
    /// MEMORY.md:1808 (it must loop when a third-party VPN is doing the encrypting, not only when
    /// Syxx is). This either-or is correct for the glyph and ONLY for the glyph — Prophet, Mirror
    /// and the Dashboard graph must read the two signals separately. Undetermined stops the
    /// animation; it never continues the last state.
    public var glyphShouldAnimate: Bool {
        dnsEncryption == .confirmedTrue || vpn == .confirmedTrue
    }
}

extension DNSTransportSample {
    /// Resolve this sample against the current clock and network identity.
    ///
    /// Expires to undetermined when: the monotonic age exceeds `validFor`; the monotonic reading
    /// precedes the observation (impossible for a sound clock, so untrustworthy); or the network
    /// fingerprint has changed. A stale sample NEVER returns its last value.
    public func reading(nowMonotonicNanos now: UInt64,
                        networkFingerprint current: String) -> DNSTransportReading {
        guard let age = age(nowMonotonicNanos: now) else {
            return .undetermined(.sampleExpired)
        }
        if age > validFor {
            return .undetermined(.sampleExpired)
        }
        if current != networkFingerprint {
            return .undetermined(.networkChanged)
        }
        return DNSTransportReading(dnsEncryption: dnsEncryption, dnsReason: dnsReason,
                                   dnsEvidence: dnsEvidence,
                                   vpn: vpn, vpnReason: vpnReason, vpnEvidence: vpnEvidence,
                                   observedAt: observedAt, age: age)
    }
}

// MARK: - Determination core

/// The pure determination: evidence in, tri-states out. Kept free of I/O so every branch — including
/// every undetermined branch — is exhaustively testable, and so the same rules govern the
/// privileged and unprivileged observers.
public enum DNSTransportDetermination {

    /// Classify one observation window's DNS-transport evidence.
    ///
    /// - Parameters:
    ///   - evidence: what the observer saw. `nil` means no observer ran.
    ///   - observerAvailable: whether an observer could run at all (privilege present, tool
    ///     present, helper alive). False ⇒ undetermined, never an answer.
    ///   - observationSucceeded: whether the observer completed. False ⇒ undetermined.
    public static func classifyDNS(evidence: TransportEvidence?,
                                   observerAvailable: Bool,
                                   observationSucceeded: Bool) -> (TransportSignal, TransportReason) {
        guard observerAvailable else { return (.undetermined, .observerUnavailable) }
        guard observationSucceeded, let e = evidence else {
            return (.undetermined, .observationFailed)
        }
        // Cleartext dominates. One observed cleartext query answers the user's question with a
        // "no" for at least some of their traffic — the same all-must-be-encrypted rule D056
        // applies to the configured question (NetworkKeyState.swift:572).
        if e.cleartextOffHost > 0 {
            return (.confirmedFalse, .cleartextQueryObserved)
        }
        // Encrypted requires AFFIRMATIVE evidence of encrypted DNS activity. "We saw no cleartext"
        // is not evidence of encryption; it is equally consistent with no DNS having happened.
        if e.encryptedDNS > 0 {
            return (.confirmedTrue, .encryptedTransportObserved)
        }
        // Activity happened but this method could not see its transport (unconnected UDP peer, or
        // an on-host proxy hop whose upstream we did not observe).
        if e.transportUnobservable > 0 || e.cleartextLoopback > 0 {
            return (.undetermined, .dnsActivityTransportUnobservable)
        }
        return (.undetermined, .noDNSActivityObserved)
    }

    /// Map the existing VPN tri-state (`NetworkKeyState.vpnStatus()`, NetworkKeyState.swift:432)
    /// onto the published signal without flattening it. `.unknown` stays undetermined.
    public static func classifyVPN(_ state: ConditionState) -> (TransportSignal, TransportReason) {
        switch state {
        case .yes:     return (.confirmedTrue, .tunnelInterfaceActive)
        case .no:      return (.confirmedFalse, .noTunnelInterfaceActive)
        case .unknown: return (.undetermined, .observationFailed)
        }
    }
}

// MARK: - Window summary (Prophet / Mirror correlation)

/// The transport state AS IT WAS during a module's scan window.
///
/// Prophet and Mirror must not read the live value at redraw time: a pane showing the current
/// transport next to results gathered under a different one is a fabricated correlation. They read
/// this, computed over the interval their queries actually ran in.
///
/// This is a SHARED INPUT, not a shared decision. Prophet asks a symmetric peer-consensus question
/// across its oracles; Mirror asks an asymmetric local-versus-baseline question. Each draws its own
/// conclusion from these facts; neither delegates its conclusion to the other or to this type.
public struct DNSTransportWindow: Codable, Sendable, Equatable {
    public let dnsEncryption: TransportSignal
    public let dnsReason: TransportReason
    public let vpn: TransportSignal
    public let vpnReason: TransportReason
    /// Fraction of the expected sample slots that were actually present (0…1). Below 1.0 a gap
    /// could hide a cleartext query, so `dnsEncryption` can never be `confirmedTrue`.
    public let coverage: Double
    public let sampleCount: Int
    public let expectedSampleCount: Int
    public let start: Date
    public let end: Date
    /// Aggregate counts across the window, for a module that wants to cite the evidence itself.
    public let cleartextOffHost: Int
    public let encryptedDNS: Int
    public let transportUnobservable: Int

    public init(dnsEncryption: TransportSignal, dnsReason: TransportReason,
                vpn: TransportSignal, vpnReason: TransportReason,
                coverage: Double, sampleCount: Int, expectedSampleCount: Int,
                start: Date, end: Date,
                cleartextOffHost: Int = 0, encryptedDNS: Int = 0, transportUnobservable: Int = 0) {
        self.dnsEncryption = dnsEncryption
        self.dnsReason = dnsReason
        self.vpn = vpn
        self.vpnReason = vpnReason
        self.coverage = max(0.0, min(1.0, coverage))
        self.sampleCount = sampleCount
        self.expectedSampleCount = expectedSampleCount
        self.start = start
        self.end = end
        self.cleartextOffHost = cleartextOffHost
        self.encryptedDNS = encryptedDNS
        self.transportUnobservable = transportUnobservable
    }

    /// A window with no samples at all.
    public static func unobserved(start: Date, end: Date, expected: Int,
                                  reason: TransportReason = .notYetObserved) -> DNSTransportWindow {
        DNSTransportWindow(dnsEncryption: .undetermined, dnsReason: reason,
                           vpn: .undetermined, vpnReason: reason,
                           coverage: 0, sampleCount: 0, expectedSampleCount: expected,
                           start: start, end: end)
    }
}

// MARK: - Timeline (Dashboard graph)

/// One slot of the Dashboard time series. A slot with no sample is `.undetermined(.notYetObserved)`
/// and MUST render visibly distinct from cleartext — a gap in the series is not a measurement.
/// Slots are emitted for every expected interval, so a renderer cannot interpolate across a gap
/// without noticing it: the gap is a value in the array, not a missing element.
public struct DNSTransportSlot: Codable, Sendable, Equatable {
    public let index: Int
    public let dnsEncryption: TransportSignal
    public let dnsReason: TransportReason
    public let vpn: TransportSignal
    public let vpnReason: TransportReason
    /// Wall-clock time of the sample that filled this slot; `nil` for a gap.
    public let observedAt: Date?

    public var isGap: Bool { observedAt == nil }
}

// MARK: - Store

/// Bounded, thread-safe store of observation samples. Feeds all four consumers from one observation
/// so no module re-derives its own transport state:
///   • menubar glyph — `current(...)` plus a change token to start/stop the animation;
///   • Dashboard graph — `timeline(...)`, gaps explicit;
///   • Prophet — `window(from:to:)` over its own scan interval;
///   • Mirror — the same, over its own scan interval.
///
/// Retention is bounded by count and by time; nothing is written to disk by this type.
public final class DNSTransportStateStore: @unchecked Sendable {

    /// Observation cadence. 5 s per the owner's spec.
    public static let sampleInterval: TimeInterval = 5
    /// A sample may be treated as current for three intervals. Past that it is `sampleExpired`,
    /// not its last value.
    public static let validityWindow: TimeInterval = 15
    /// Graph retention: 5 minutes at a 5 s cadence = 60 slots.
    public static let retention: TimeInterval = 300
    public static let maxSamples = 120

    private let lock = NSLock()
    private var samples: [DNSTransportSample] = []
    private var changeToken: UInt64 = 0

    public init() {}

    /// Record one observation tick, evicting samples beyond the retention bound.
    public func record(_ sample: DNSTransportSample) {
        lock.lock(); defer { lock.unlock() }
        samples.append(sample)
        if samples.count > Self.maxSamples {
            samples.removeFirst(samples.count - Self.maxSamples)
        }
        let cutoff = sample.observedAtMonotonicNanos
            &- UInt64(Self.retention * 1_000_000_000)
        if sample.observedAtMonotonicNanos > UInt64(Self.retention * 1_000_000_000) {
            samples.removeAll { $0.observedAtMonotonicNanos < cutoff }
        }
        changeToken &+= 1
    }

    /// Increments on every recorded sample. A consumer that caches a reading can compare tokens to
    /// know whether its cached value is still the one the store holds.
    public var token: UInt64 {
        lock.lock(); defer { lock.unlock() }
        return changeToken
    }

    /// The current reading. Returns undetermined when there is no sample, when the newest sample
    /// has expired, or when the network changed since it was taken.
    public func current(nowMonotonicNanos now: UInt64 = MonotonicClock.nowNanos(),
                        networkFingerprint: String) -> DNSTransportReading {
        lock.lock()
        let latest = samples.last
        lock.unlock()
        guard let latest else { return .undetermined(.notYetObserved) }
        return latest.reading(nowMonotonicNanos: now, networkFingerprint: networkFingerprint)
    }

    /// The Dashboard series: one slot per `sampleInterval` ending at `now`, gaps included as
    /// explicit undetermined slots.
    public func timeline(nowMonotonicNanos now: UInt64 = MonotonicClock.nowNanos(),
                         networkFingerprint: String,
                         slots: Int = 60) -> [DNSTransportSlot] {
        lock.lock()
        let snapshot = samples
        lock.unlock()
        let intervalNanos = UInt64(Self.sampleInterval * 1_000_000_000)
        var out: [DNSTransportSlot] = []
        out.reserveCapacity(slots)
        for i in 0..<slots {
            // Slot 0 is the oldest.
            let slotsBack = UInt64(slots - i)
            guard now >= slotsBack &* intervalNanos else {
                out.append(DNSTransportSlot(index: i, dnsEncryption: .undetermined,
                                            dnsReason: .notYetObserved, vpn: .undetermined,
                                            vpnReason: .notYetObserved, observedAt: nil))
                continue
            }
            let lower = now - slotsBack &* intervalNanos
            let upper = lower &+ intervalNanos
            let inSlot = snapshot.last {
                $0.observedAtMonotonicNanos >= lower && $0.observedAtMonotonicNanos < upper
            }
            guard let s = inSlot, s.networkFingerprint == networkFingerprint else {
                out.append(DNSTransportSlot(index: i, dnsEncryption: .undetermined,
                                            dnsReason: inSlot == nil ? .notYetObserved : .networkChanged,
                                            vpn: .undetermined,
                                            vpnReason: inSlot == nil ? .notYetObserved : .networkChanged,
                                            observedAt: nil))
                continue
            }
            out.append(DNSTransportSlot(index: i, dnsEncryption: s.dnsEncryption,
                                        dnsReason: s.dnsReason, vpn: s.vpn, vpnReason: s.vpnReason,
                                        observedAt: s.observedAt))
        }
        return out
    }

    /// The transport state as it was across a module's scan window, for Prophet and Mirror.
    ///
    /// - Parameters:
    ///   - startMonotonicNanos / endMonotonicNanos: the module's actual query interval.
    public func window(startMonotonicNanos start: UInt64,
                       endMonotonicNanos end: UInt64,
                       startWall: Date,
                       endWall: Date,
                       networkFingerprint: String) -> DNSTransportWindow {
        lock.lock()
        let snapshot = samples
        lock.unlock()
        let covering = snapshot.filter {
            $0.observedAtMonotonicNanos >= start && $0.observedAtMonotonicNanos <= end
                && $0.networkFingerprint == networkFingerprint
        }
        let span = end >= start ? TimeInterval(end - start) / 1_000_000_000 : 0
        // At least one sample is expected for any non-zero window.
        let expected = max(1, Int((span / Self.sampleInterval).rounded(.up)))
        return DNSTransportDetermination.summarize(samples: covering,
                                                   expectedSampleCount: expected,
                                                   start: startWall, end: endWall)
    }
}

extension DNSTransportDetermination {

    /// Aggregate a set of samples into the window a module reads.
    ///
    /// Asymmetric on purpose:
    ///   • `confirmedFalse` (cleartext) needs ONE sample that saw cleartext. Positive evidence does
    ///     not need full coverage — a gap cannot un-observe a query that was observed.
    ///   • `confirmedTrue` (encrypted) needs FULL coverage, at least one sample with affirmative
    ///     encrypted-DNS activity, and zero cleartext. A gap could hide a cleartext query, so
    ///     incomplete coverage can never yield "encrypted".
    ///   • Samples that disagree mean the state changed mid-window; reporting either value beside
    ///     whole-window results would be a fabricated correlation.
    public static func summarize(samples: [DNSTransportSample],
                                 expectedSampleCount: Int,
                                 start: Date, end: Date) -> DNSTransportWindow {
        guard !samples.isEmpty else {
            return .unobserved(start: start, end: end, expected: expectedSampleCount)
        }
        let coverage = min(1.0, Double(samples.count) / Double(max(1, expectedSampleCount)))
        let cleartext = samples.reduce(0) { $0 + $1.dnsEvidence.cleartextOffHost }
        let encrypted = samples.reduce(0) { $0 + $1.dnsEvidence.encryptedDNS }
        let unobservable = samples.reduce(0) { $0 + $1.dnsEvidence.transportUnobservable }

        let dns: (TransportSignal, TransportReason)
        if samples.contains(where: { $0.dnsEncryption == .confirmedFalse }) {
            dns = (.confirmedFalse, .cleartextQueryObserved)
        } else if coverage < 1.0 {
            dns = (.undetermined, .incompleteWindowCoverage)
        } else if samples.allSatisfy({ $0.dnsEncryption == .confirmedTrue }) {
            dns = (.confirmedTrue, .encryptedTransportObserved)
        } else if samples.contains(where: { $0.dnsEncryption == .confirmedTrue }) {
            // Some ticks confirmed encrypted, others were undetermined: the undetermined ticks may
            // have carried cleartext we did not see. Not "mostly encrypted" — undetermined.
            dns = (.undetermined, .stateChangedDuringWindow)
        } else {
            dns = (.undetermined, samples.first?.dnsReason ?? .noDNSActivityObserved)
        }

        // VPN: unanimity or nothing. A tunnel that came up or dropped mid-scan must not be reported
        // as a stable condition of the scan.
        let vpnStates = Set(samples.map { $0.vpn })
        let vpn: (TransportSignal, TransportReason)
        if vpnStates.count > 1 {
            vpn = (.undetermined, .stateChangedDuringWindow)
        } else if let only = vpnStates.first, only != .undetermined {
            vpn = (only, samples[0].vpnReason)
        } else {
            vpn = (.undetermined, samples[0].vpnReason)
        }

        return DNSTransportWindow(dnsEncryption: dns.0, dnsReason: dns.1,
                                  vpn: vpn.0, vpnReason: vpn.1,
                                  coverage: coverage, sampleCount: samples.count,
                                  expectedSampleCount: expectedSampleCount,
                                  start: start, end: end,
                                  cleartextOffHost: cleartext, encryptedDNS: encrypted,
                                  transportUnobservable: unobservable)
    }
}

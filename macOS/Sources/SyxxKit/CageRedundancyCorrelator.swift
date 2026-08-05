import Foundation

// MARK: - Cage redundancy correlator (two independent captures of the same traffic)
//
// When both the daemon-owned capture (Stream A: in-process BPF in DeepSyxx) and Cage (the existing
// BPF capture via DNSCapture/SyxxCageCLI) run concurrently against the same interface, they observe
// the SAME DNS traffic through two independent capture paths. This correlator compares the two
// streams and produces a result.
//
// WHY THIS EXISTS. A single capture path is a single point of failure: a missed packet, a dropped
// BPF buffer, or (adversarially) a capture-evasion technique that splits behaviour by capture
// method all read as "nothing happened" from one path. Two independent paths that AGREE is stronger
// evidence than one path alone; two that DISAGREE is itself a forensic finding — never silently
// averaged into a single "probably fine" (the honesty spine, DNSTransportState.swift:29-44).
//
// PLATFORM SCOPE. The correlator is PURE logic — no I/O, no root, no BPF — so it lives in SyxxKit
// and is exhaustively testable. A future Windows Cage twin (Windows/dotNET/Syxx.Kit/DNSCapture.cs)
// can reuse it unchanged; only the capture gathering is platform-specific. Mirrors the separation
// in DNSPathConfirmationEngine (DNSPathConfirmation.swift) and DNSTransportDetermination
// (DNSTransportState.swift:311).

/// A query identity shared by both capture streams. The match key is `(qname, qtype, xid)`: the
/// question name, the question type, and the DNS transaction id. Two captures of the same outbound
/// query agree on all three; a re-used xid with a different question is drift, not a match.
public struct CageQueryKey: Codable, Sendable, Equatable, Hashable {
    public let qname: String
    public let qtype: String
    public let xid: UInt16

    public init(qname: String, qtype: String, xid: UInt16) {
        self.qname = qname
        self.qtype = qtype
        self.xid = xid
    }
}

/// One query as seen by one capture stream, with its capture timestamp for the jitter check.
public struct CageSeenQuery: Codable, Sendable, Equatable {
    public let key: CageQueryKey
    /// Wall-clock capture timestamp parsed from the record (MonitorReportAnalyzer.parseTimestamp,
    /// MonitorReportAnalyzer.swift:87). `nil` when the timestamp did not parse — recorded honestly
    /// so a jitter comparison is not fabricated against a missing clock.
    public let timestamp: Date?

    public init(key: CageQueryKey, timestamp: Date?) {
        self.key = key
        self.timestamp = timestamp
    }
}

/// The evidence behind a `CageRedundancyResult`. Each disagreement subtype is a DISTINCT fact, not
/// a boolean "mismatch" — a daemon-only query, a cage-only query, and an xid drift are three
/// different forensic signals (capture evasion, splitting, or a broken capture path).
public struct CageRedundancyEvidence: Codable, Sendable, Equatable {
    /// Queries present in BOTH streams (same qname+qtype+xid), with the timestamp delta between the
    /// two captures. A large delta on a "matched" query is itself suspicious (capture buffering).
    public let matched: [MatchedQuery]
    /// Queries the daemon saw but Cage did not. Cage missed them — a dropped BPF buffer, a
    /// capture-evasion technique that hides from Cage's path, or Cage not actually capturing.
    public let daemonOnly: [CageQueryKey]
    /// Queries Cage saw but the daemon did not. The daemon missed them — symmetric to the above.
    public let cageOnly: [CageQueryKey]
    /// Same xid in both streams but a DIFFERENT qname/qtype — a re-used transaction id with a
    /// different question, or a capture path corrupting the question section. Never counted as
    /// "matched"; reported as its own signal.
    public let xidDrift: [XidDrift]

    public struct MatchedQuery: Codable, Sendable, Equatable {
        public let key: CageQueryKey
        /// `daemon.timestamp - cage.timestamp`, in milliseconds. `nil` when either timestamp was
        /// unparsable (so a delta is never fabricated against a missing clock).
        public let deltaMs: Double?
        public init(key: CageQueryKey, deltaMs: Double?) {
            self.key = key
            self.deltaMs = deltaMs
        }
    }

    public struct XidDrift: Codable, Sendable, Equatable {
        public let xid: UInt16
        public let daemonQname: String?
        public let daemonQtype: String?
        public let cageQname: String?
        public let cageQtype: String?
        public init(xid: UInt16, daemonQname: String?, daemonQtype: String?,
                    cageQname: String?, cageQtype: String?) {
            self.xid = xid
            self.daemonQname = daemonQname
            self.daemonQtype = daemonQtype
            self.cageQname = cageQname
            self.cageQtype = cageQtype
        }
    }

    public init(matched: [MatchedQuery], daemonOnly: [CageQueryKey],
                cageOnly: [CageQueryKey], xidDrift: [XidDrift]) {
        self.matched = matched
        self.daemonOnly = daemonOnly
        self.cageOnly = cageOnly
        self.xidDrift = xidDrift
    }

    public var matchedCount: Int { matched.count }
    public var daemonOnlyCount: Int { daemonOnly.count }
    public var cageOnlyCount: Int { cageOnly.count }
    public var xidDriftCount: Int { xidDrift.count }
}

/// The result. Three-valued so "we could not compare" is never read as "the captures agreed".
public enum CageRedundancyResult: Codable, Sendable, Equatable {
    /// Both captures saw the same queries (with at most the recorded timestamp deltas). Stronger
    /// evidence than a single capture alone.
    case agree(CageRedundancyEvidence)
    /// The captures disagree — one saw queries the other did not, or xids drifted. Itself a
    /// forensic finding; never silently averaged into "agree".
    case disagree(CageRedundancyEvidence)
    /// The comparison could not be made: one or both streams were empty, or neither captured any
    /// outbound query. NOT "agree" — silence is not agreement (DNSTransportState.swift:33-36).
    case incomparable(reason: String)
}

/// The pure correlator. No I/O, no root, no capture — give it two streams of captured records and
/// it produces the result. Reuses `DNSCaptureRecord` (DNSCapture.swift:171) so the input type is
/// the same one every other Cage consumer uses.
public enum CageRedundancyCorrelator {

    /// Correlate two capture streams.
    ///
    /// - Parameters:
    ///   - daemon: records from the daemon-owned capture (Stream A).
    ///   - cage: records from the Cage capture (DNSCapture/SyxxCageCLI).
    ///   - jitterMs: maximum tolerated |daemon.ts - cage.ts| for a "matched" query, in milliseconds.
    ///     A match whose delta exceeds this is still counted as matched (same key) but the delta is
    ///     recorded in evidence; the caller decides whether a large delta is suspicious. Defaults
    ///     to 1000 ms (generous — two independent BPF captures of the same packet should agree to
    ///     within microseconds, but clock skew and buffer latency make a tight bound flaky).
    public static func correlate(daemon: [DNSCaptureRecord],
                                 cage: [DNSCaptureRecord],
                                 jitterMs: Double = 1000) -> CageRedundancyResult{
        let daemonQueries = outboundQueries(daemon)
        let cageQueries = outboundQueries(cage)

        // Incomparable when there is nothing to compare. This is the honest resting state — a
        // capture that saw nothing cannot corroborate or refute another (DNSTransportState.swift:85).
        if daemonQueries.isEmpty && cageQueries.isEmpty {
            return .incomparable(reason: "neither capture observed any outbound DNS query")
        }
        if daemonQueries.isEmpty {
            return .incomparable(reason: "daemon capture observed no outbound DNS query; Cage observed \(cageQueries.count)")
        }
        if cageQueries.isEmpty {
            return .incomparable(reason: "Cage capture observed no outbound DNS query; daemon observed \(daemonQueries.count)")
        }

        // Index by full key for intersection/difference, and by xid alone for drift detection.
        var daemonByKey: [CageQueryKey: CageSeenQuery] = [:]
        var daemonByXid: [UInt16: CageSeenQuery] = [:]
        for q in daemonQueries {
            daemonByKey[q.key] = q
            // First-wins for the by-xid index; a duplicate xid within one stream is itself odd but
            // is not the cross-stream drift this detector is named for.
            daemonByXid[q.key.xid] = daemonByXid[q.key.xid] ?? q
        }
        var cageByKey: [CageQueryKey: CageSeenQuery] = [:]
        var cageByXid: [UInt16: CageSeenQuery] = [:]
        for q in cageQueries {
            cageByKey[q.key] = q
            cageByXid[q.key.xid] = cageByXid[q.key.xid] ?? q
        }

        let daemonKeys = Set(daemonByKey.keys)
        let cageKeys = Set(cageByKey.keys)
        let matchedKeys = daemonKeys.intersection(cageKeys)
        let daemonOnlyKeys = daemonKeys.subtracting(cageKeys)
        let cageOnlyKeys = cageKeys.subtracting(daemonKeys)

        // xid drift: same xid in both streams but the (qname,qtype) differs. Detected via the xid
        // index minus the exact-key matches.
        var xidDrift: [CageRedundancyEvidence.XidDrift] = []
        let daemonXids = Set(daemonByXid.keys)
        let cageXids = Set(cageByXid.keys)
        for xid in daemonXids.intersection(cageXids) {
            guard let d = daemonByXid[xid], let c = cageByXid[xid] else { continue }
            if d.key.qname != c.key.qname || d.key.qtype != c.key.qtype {
                xidDrift.append(.init(xid: xid,
                                      daemonQname: d.key.qname, daemonQtype: d.key.qtype,
                                      cageQname: c.key.qname, cageQtype: c.key.qtype))
            }
        }

        var matched: [CageRedundancyEvidence.MatchedQuery] = []
        matched.reserveCapacity(matchedKeys.count)
        for key in matchedKeys {
            guard let d = daemonByKey[key], let c = cageByKey[key] else { continue }
            let delta: Double?
            if let dt = d.timestamp, let ct = c.timestamp {
                delta = dt.timeIntervalSince(ct) * 1000   // daemon - cage, in ms
            } else {
                delta = nil   // never fabricate a delta against a missing clock
            }
            matched.append(.init(key: key, deltaMs: delta))
        }

        let evidence = CageRedundancyEvidence(
            matched: matched.sorted { $0.key.xid < $1.key.xid },
            daemonOnly: Array(daemonOnlyKeys).sorted { $0.xid < $1.xid },
            cageOnly: Array(cageOnlyKeys).sorted { $0.xid < $1.xid },
            xidDrift: xidDrift.sorted { $0.xid < $1.xid })

        // Agree ONLY when there is at least one match and zero disagreement of any kind. Any
        // daemon-only, cage-only, or xid-drift is a disagreement — never averaged away.
        if !matched.isEmpty, evidence.daemonOnlyCount == 0,
           evidence.cageOnlyCount == 0, evidence.xidDriftCount == 0 {
            return .agree(evidence)
        }
        return .disagree(evidence)
    }

    /// Extract outbound queries from a capture stream, keyed by (qname, qtype, xid). An outbound
    /// query is a record whose direction is "Outbound Query" (NetworkContextReport.direction,
    /// DNSCapture.swift:150) OR whose packet QR is "Query" (DNSPacketReport.qr, DNSCapture.swift:92)
    /// — either signal is accepted because a future capture path may populate only one.
    static func outboundQueries(_ records: [DNSCaptureRecord]) -> [CageSeenQuery] {
        var out: [CageSeenQuery] = []
        out.reserveCapacity(records.count)
        for r in records {
            let isOutbound = r.network.direction.contains("Outbound Query")
                || r.packet.qr.lowercased() == "query"
            guard isOutbound, let q = r.packet.questions.first else { continue }
            let key = CageQueryKey(qname: q.questionName, qtype: q.questionType, xid: r.packet.xid)
            out.append(CageSeenQuery(key: key, timestamp: MonitorReportAnalyzer.parseTimestamp(r.timestamp)))
        }
        return out
    }
}

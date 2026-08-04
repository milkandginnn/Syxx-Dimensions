import Foundation

// ROADMAP Workstream H · datapoint F3 — TXID & source-port entropy audit.
//
// Sourced from the SyxxCage CAPTURE stream (never the query API): it needs a *population* of packets,
// which only a capture provides. It reads the transaction ID and source port of the host's OUTBOUND
// queries and judges how well they are randomized.
//
// Layer note (important): on a local capture these are the LOCAL STUB / OS resolver's transaction IDs
// and ephemeral source ports — NOT the upstream recursive's. So this measures *stub/OS randomization
// quality* — a spoofability posture (weak local randomization makes the host easier to spoof) — and it
// is NOT direct proof of upstream cache poisoning. Weighted as a weak/supporting signal and surfaced as
// a posture caveat (degraded), never a threat finding on its own, never a silent clean.
//
//   • good source-port + TXID spread   → .wellRandomized (clean posture).
//   • a fixed/near-fixed source port or
//     fixed/sequential TXIDs            → .weakRandomization (degraded posture + weak spoofing tilt).
//   • too few captured queries          → .insufficientData (never rendered clean).

public struct StubEntropyAudit: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case wellRandomized     // good source-port and TXID randomization — healthy local posture
        case weakRandomization  // fixed/low-entropy source port or TXID — spoofability posture (weak)
        case insufficientData   // too few captured queries to judge (never rendered clean)
        case untested
    }

    public let status: Status
    public let sampleCount: Int
    public let distinctSourcePorts: Int
    public let distinctTransactionIDs: Int
    public let detail: String

    public init(status: Status, sampleCount: Int = 0, distinctSourcePorts: Int = 0,
                distinctTransactionIDs: Int = 0, detail: String = "") {
        self.status = status
        self.sampleCount = sampleCount
        self.distinctSourcePorts = distinctSourcePorts
        self.distinctTransactionIDs = distinctTransactionIDs
        self.detail = detail
    }

    public static let untested = StubEntropyAudit(status: .untested, detail: "not run")
}

public enum StubEntropyProbe {
    /// Below this many captured queries the spread is not meaningful — report insufficientData, never
    /// a clean posture.
    public static let minSamples = 8

    /// Audit an already-extracted population of outbound-query observations. Pure + offline-testable.
    public static func audit(outboundQueries q: [(xid: UInt16, sourcePort: UInt16)]) -> StubEntropyAudit {
        guard q.count >= minSamples else {
            return StubEntropyAudit(status: .insufficientData, sampleCount: q.count,
                detail: "only \(q.count) captured outbound queries (< \(minSamples)) — not enough to judge randomization; unmeasured, not clean")
        }
        let ports = q.map { $0.sourcePort }
        let xids = q.map { $0.xid }
        let distinctPorts = Set(ports).count
        let distinctXids = Set(xids).count

        // Clear, low-false-positive weakness tests only (this is a weak supporting signal — it must not
        // cry wolf on healthy randomization):
        //   • a single source port reused across every query (classic fixed-port exposure), or
        //   • transaction IDs that barely vary or march sequentially.
        let fixedPort = distinctPorts == 1
        let lowXidEntropy = distinctXids <= max(1, q.count / 4)
        let sequentialXids = isMostlySequential(xids)

        if fixedPort || lowXidEntropy || sequentialXids {
            var why: [String] = []
            if fixedPort { why.append("all \(q.count) queries used one source port") }
            if lowXidEntropy { why.append("only \(distinctXids) distinct transaction IDs across \(q.count) queries") }
            if sequentialXids { why.append("transaction IDs are largely sequential") }
            return StubEntropyAudit(status: .weakRandomization, sampleCount: q.count,
                distinctSourcePorts: distinctPorts, distinctTransactionIDs: distinctXids,
                detail: why.joined(separator: "; "))
        }
        return StubEntropyAudit(status: .wellRandomized, sampleCount: q.count,
            distinctSourcePorts: distinctPorts, distinctTransactionIDs: distinctXids,
            detail: "\(distinctPorts) distinct source ports and \(distinctXids) distinct transaction IDs across \(q.count) queries")
    }

    /// Extract outbound-query (xid, sourcePort) pairs from a capture-record population and audit them.
    public static func audit(records: [DNSCaptureRecord]) -> StubEntropyAudit {
        let q: [(UInt16, UInt16)] = records.compactMap { rec in
            guard rec.network.direction.lowercased().contains("outbound") else { return nil }
            return (rec.packet.xid, rec.network.sourcePort)
        }
        return audit(outboundQueries: q)
    }

    /// True when, after sorting, most adjacent transaction IDs differ by exactly 1 — a predictable,
    /// counter-like generator rather than a randomized one.
    static func isMostlySequential(_ values: [UInt16]) -> Bool {
        let sorted = Set(values).sorted()
        guard sorted.count >= 4 else { return false }
        var stepByOne = 0
        for i in 1..<sorted.count where sorted[i] == sorted[i - 1] + 1 { stepByOne += 1 }
        return Double(stepByOne) / Double(sorted.count - 1) >= 0.7
    }
}

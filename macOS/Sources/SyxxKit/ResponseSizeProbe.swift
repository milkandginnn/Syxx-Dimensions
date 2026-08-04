import Foundation

// ROADMAP Workstream H · datapoint F9 — response-size / amplification tracking.
//
// Sourced from the SyxxCage CAPTURE stream (needs the per-packet payload byte length, persisted as
// `DNSPacketReport.payloadBytes`). It looks at the sizes of the host's INBOUND responses.
//
// This is *supporting telemetry*, not a result driver: response size legitimately explains truncation,
// DNSSEC overhead, and TCP fallback, and large responses are frequently benign (DNSSEC, big TXT/SPF,
// ANY). So it only flags a *sustained* pattern of large responses — the shape of amplification / covert
// DNS tunnelling — and even then contributes a weak, tilt-only localCompromise nudge that never decides.
//
//   • ordinary response sizes                     → .normal (clean).
//   • a sustained run of anomalously large
//     responses (not a one-off DNSSEC answer)      → .oversized (weak localCompromise tilt).
//   • too few sized responses to judge             → .insufficientData (never rendered clean).

public struct ResponseSizeAudit: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case normal            // response sizes are within the ordinary DNS range
        case oversized         // a sustained run of large responses — weak amplification / tunnelling hint
        case insufficientData  // too few sized responses to judge (never rendered clean)
        case untested
    }

    public let status: Status
    public let sampleCount: Int
    public let maxBytes: Int
    public let oversizedCount: Int
    public let detail: String

    public init(status: Status, sampleCount: Int = 0, maxBytes: Int = 0,
                oversizedCount: Int = 0, detail: String = "") {
        self.status = status
        self.sampleCount = sampleCount
        self.maxBytes = maxBytes
        self.oversizedCount = oversizedCount
        self.detail = detail
    }

    public static let untested = ResponseSizeAudit(status: .untested, detail: "not run")
}

public enum ResponseSizeProbe {
    /// Below this many sized responses the distribution is not meaningful.
    public static let minSamples = 8
    /// Bytes beyond which a UDP DNS response is unusually large (past the common ~1232 EDNS default and
    /// most legitimate answers). A single such response is unremarkable; a *run* of them is the signal.
    public static let largeThreshold = 2048

    /// Audit an already-extracted population of response payload sizes. Pure + offline-testable.
    public static func audit(responseSizes: [Int]) -> ResponseSizeAudit {
        let sized = responseSizes.filter { $0 > 0 }
        guard sized.count >= minSamples else {
            return ResponseSizeAudit(status: .insufficientData, sampleCount: sized.count,
                detail: "only \(sized.count) sized responses (< \(minSamples)) — not enough to judge; unmeasured, not clean")
        }
        let maxB = sized.max() ?? 0
        let over = sized.filter { $0 >= largeThreshold }.count
        // Require a sustained fraction (or several), not a lone large DNSSEC/TXT answer — keeps this
        // weak signal from crying wolf on legitimate big responses.
        let oversized = over >= max(2, sized.count / 5)
        if oversized {
            return ResponseSizeAudit(status: .oversized, sampleCount: sized.count, maxBytes: maxB, oversizedCount: over,
                detail: "\(over) of \(sized.count) responses were ≥ \(largeThreshold) bytes (max \(maxB)) — a sustained large-response pattern consistent with amplification or covert DNS tunnelling")
        }
        return ResponseSizeAudit(status: .normal, sampleCount: sized.count, maxBytes: maxB, oversizedCount: over,
            detail: "response sizes within the ordinary range (max \(maxB) bytes across \(sized.count) responses)")
    }

    /// Extract inbound-response payload sizes from a capture-record population and audit them.
    public static func audit(records: [DNSCaptureRecord]) -> ResponseSizeAudit {
        let sizes: [Int] = records.compactMap { rec in
            guard rec.network.direction.lowercased().contains("inbound") else { return nil }
            return rec.packet.payloadBytes
        }
        return audit(responseSizes: sizes)
    }
}

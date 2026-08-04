import Foundation

// B1 (ROADMAP "Implementation plan v2" — shareable signed incident report). Renders a
// WitnessSnapshot as a human-readable, tamper-evident text page that a user can share,
// save, or paste.
//
// Builds on WitnessSigner/SignedEvidencePayload + A2 PlainExplanation copy. The cryptographic
// seal (signature, public key, key ID) is embedded in the report, so altering any rendered
// field breaks the Ed25519 signature — "altering one byte breaks the seal."
//
// HONESTY CONTRACT (Protocol §1/§10):
//   * The result is rendered from the ComparisonResult, never fabricated.
//   * An inconclusive/degraded comparison is rendered honestly, never as "matches".
//   * If no Conclusion is provided, the report omits the A2/A3 explanation rather than
//     fabricating one — "no data" never reads as "nothing wrong".
//
// Byte-parity twin of Windows `Syxx.Kit/Synthesis/SignedIncidentReport.cs`.
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md:42).

/// Renders a `WitnessSnapshot` as a shareable, human-readable signed incident report.
public enum SignedIncidentReport {

    private static let rule = String(repeating: "─", count: 66)
    private static let thinRule = String(repeating: "·", count: 66)

    /// Render a full incident report as text.
    /// - Parameter snapshot: The signed witness snapshot to render.
    /// - Parameter explanation: Optional A2 plain-language explanation to include in the report.
    /// - Returns: A formatted text report suitable for saving or sharing.
    public static func render(snapshot: WitnessSnapshot, explanation: PlainExplanation? = nil) -> String {
        var lines: [String] = []
        // ── Header ──
        lines.append(rule)
        lines.append("           SYXX SIGNED INCIDENT REPORT")
        lines.append(rule)
        lines.append("")

        // ── Metadata ──
        lines.append("  REPORT METADATA")
        lines.append(thinRule)
        let df = ISO8601DateFormatter()
        df.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        lines.append("    Generated:      \(df.string(from: snapshot.timestamp))")
        lines.append("    Domain:         \(snapshot.targetDomain)")
        lines.append("    Format Version: \(snapshot.formatVersion)")
        if let expires = snapshot.expiresAt {
            lines.append("    Expires:        \(df.string(from: expires))")
        } else {
            lines.append("    Expires:        Never")
        }
        lines.append("")

        // ── Conclusion ──
        lines.append("  CONCLUSION")
        lines.append(thinRule)
        let comparison = snapshot.comparison
        // B157 + B179: an empty comparison compared nothing — the flags are all false because
        // nothing was observed, not because the answers agreed. A "MATCHES" result on an empty
        // set is Ed25519-signed and presented as third-party-verifiable, making it a forgeable
        // authoritative claim. Guard before the flag chain.
        // B179: use the TYPED outcome fields (covered by the display-integrity guard in
        // WitnessSigner.verifySnapshotDetailed), not the legacy localRecords/publicResolverResults
        // which are not in the signed canonical payload and can be edited without breaking the seal.
        if comparison.localOutcome.records.isEmpty && comparison.publicResolverOutcomes.isEmpty {
            lines.append("    Conclusion:   INCONCLUSIVE. Nothing was compared.")
        } else if comparison.isRedirectionDetected {
            lines.append("    Conclusion:   DIVERGES. Answers differ from reference.")
        } else if comparison.isDegraded {
            lines.append("    Conclusion:   DEGRADED. Some evidence was unreliable.")
        } else if comparison.isInconclusive {
            lines.append("    Conclusion:   INCONCLUSIVE. Could not determine trustworthiness.")
        } else {
            lines.append("    Conclusion:   MATCHES. Answers match reference.")
        }
        lines.append("    Degraded:     \(comparison.isDegraded ? "Yes" : "No")")
        lines.append("    Inconclusive: \(comparison.isInconclusive ? "Yes" : "No")")
        lines.append("")

        // ── Decision (A1/A2/A3) ──
        if let expl = explanation {
            lines.append("  DECISION")
            lines.append(thinRule)
            lines.append("    \(expl.narrative)")
            lines.append("")
        }

        // ── Evidence Summary ──
        lines.append("  EVIDENCE SUMMARY")
        lines.append(thinRule)
        lines.append("    Local Resolver:   \(comparison.localServer)")

        let publicResolvers = Array(comparison.publicResolverOutcomes.keys).sorted()
        if !publicResolvers.isEmpty {
            lines.append("    Public Resolvers: \(publicResolvers.joined(separator: ", "))")
        }

        let localIps = comparison.localOutcome.records.map { $0.rdata }.sorted().joined(separator: ", ")
        if !localIps.isEmpty {
            lines.append("    Local Answer(s):  \(localIps)")
        }

        let refIps = comparison.publicResolverOutcomes.values
            .flatMap { $0.records }
            .map { $0.rdata }
            .sorted()
            .joined(separator: ", ")
        if !refIps.isEmpty {
            lines.append("    Reference IP(s): \(refIps)")
        } else if comparison.isDegraded || comparison.isInconclusive {
            lines.append("    Reference IP(s): (no reliable reference available)")
        }

        // B252: pre-v4 snapshots carry DoH outcomes in the display only — NOT covered by the
        // signature. Render them with a clear "unsigned" marker so readers are not invited to
        // trust them. At v4+ DoH evidence is canonical and signed, so the marker is omitted.
        let dohIsUnsigned = comparison.dohOutcomes != nil
            && !comparison.dohOutcomes!.isEmpty
            && snapshot.formatVersion < SignedEvidencePayload.currentFormatVersion
        if dohIsUnsigned, let dohOutcomes = comparison.dohOutcomes {
            lines.append("")
            lines.append("    ⚠ DoH Cross-Check (UNSIGNED — not covered by the Ed25519 seal):")
            for (provider, outcome) in dohOutcomes.sorted(by: { $0.key < $1.key }) {
                let status = outcome.status == .success ? "records" : outcome.status.rawValue
                let ips = outcome.records.map { $0.rdata }.joined(separator: ", ")
                lines.append("      \(provider): \(status)\(ips.isEmpty ? "" : " (\(ips))")")
            }
        }
        lines.append("")

        // ── Cryptographic Seal ──
        lines.append("  CRYPTOGRAPHIC SEAL")
        lines.append(thinRule)
        lines.append("    Key ID:     \(snapshot.keyId)")
        lines.append("    Public Key: \(snapshot.publicKey.prefix(48))…")
        lines.append("    Signature:  \(snapshot.signature.prefix(32))…")
        lines.append("")

        // ── Verification ──
        lines.append("  VERIFICATION")
        lines.append(thinRule)
        lines.append("    Run:  syxx witness verify <file>")
        lines.append("    Or:   syxx witness report <file>")
        lines.append("")
        lines.append("    \(UnicodeScalar(0x26A0)!)  Tamper-evident: altering signed data will break the")
        lines.append("       Ed25519 signature. Independent verification requires only the")
        lines.append("       public key shown above.")
        // B252: note that DoH data is unsigned when the snapshot predates v4 (otherwise it is signed)
        if dohIsUnsigned {
            lines.append("    \(UnicodeScalar(0x26A0)!)  DoH cross-check data is NOT signed — it can be")
            lines.append("       altered without breaking the seal.")
        }
        lines.append("")
        lines.append(rule)

        return lines.joined(separator: "\n")
    }
}

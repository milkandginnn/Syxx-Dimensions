import Foundation

// A1 (the "so what" layer), re-derived onto the matrix determinative engine.
//
// Turns a Conclusion into ONE plain-language sentence of what it MEANS for the user plus the single
// most important action. Previously switched on the removed `Conclusion.diagnosis` enum; now reads
// `Conclusion.determination: DeterminativeResult?` (owner directive 2026-07-21). Public API and
// consumers (ConclusionsView, SyxxCLI, FirstRunScan) are unchanged.
//
// HONESTY CONTRACT: an inconclusive, hard-gated, or no-determination result NEVER reads as "safe",
// and its action is always "get a real result", never reassurance. A threat carrying only `probable`
// confidence, or riding over a skipped check, is `qualified` — the surface shows the caveat without
// softening the action (better-safe).
//
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md). Not yet ported to
// Windows/Ubuntu.

/// The A1 "so what" presentation of a Conclusion: what it means, and the one action that matters.
public struct ResultAction: Equatable, Sendable {
    public let meaning: String
    public let primaryAction: String
    public let severity: Severity
    public let qualified: Bool

    public init(meaning: String, primaryAction: String, severity: Severity, qualified: Bool) {
        self.meaning = meaning
        self.primaryAction = primaryAction
        self.severity = severity
        self.qualified = qualified
    }
}

public enum ResultActions {
    /// Standard caveat appended when a result is `qualified` — a limited-confidence threat read.
    public static let confidenceCaveat =
        "Confidence is limited; treat this as a strong signal to act on, not a certainty."

    public static func For(_ c: Conclusion) -> ResultAction {
        let incomplete = c.hasHardGatedSection

        // No determination, or an explicit abstention → UNFINISHED, never safe.
        guard let det = c.determination else {
            return ResultAction(
                meaning: "The determinative engine could not evaluate this run: this is an unfinished result, not a clean one.",
                primaryAction: "Re-run the checks, then synthesize again for a real conclusion.",
                severity: .unknown, qualified: true)
        }

        // `.untested` MUST be handled before the threat fall-through below. These are `if`
        // guards, not an exhaustive `switch`, so an unhandled state reaches the default threat
        // arm — where an empty diagnoses list renders "Your DNS shows signs of tampering." at
        // `.high`. That is a fabricated threat result for a scan that never ran: the most
        // severe form of the untested/inconclusive/threat conflation. (Windows twin:
        // ResultAction.cs.)
        if det.state == .untested {
            return ResultAction(
                meaning: "No checks have run yet, so there is nothing to judge: this is an unmeasured result, not a clean one.",
                primaryAction: "Run a scan, then synthesize again for a real conclusion.",
                severity: .unknown, qualified: true)
        }

        if det.state == .inconclusive {
            let overhang = det.suppressed.isEmpty ? "" :
                " A possible threat signal was seen but could not be verified."
            return ResultAction(
                meaning: "The checks could not determine whether your DNS is trustworthy: this is an unfinished result, not a clean one." + overhang,
                primaryAction: "Run the checks that were skipped or failed, then synthesize again for a real conclusion.",
                severity: .unknown, qualified: true)
        }

        if det.state == .clean {
            if incomplete {
                return ResultAction(
                    meaning: "No tampering was detected, but confidence is limited: some checks were inconclusive or did not run.",
                    primaryAction: "Run the checks that were skipped, then synthesize again to confirm.",
                    severity: .unknown, qualified: true)
            }
            return ResultAction(
                meaning: "No tampering was detected: your DNS answers match trusted vantage points and show no signs of interception or leakage.",
                primaryAction: "No action needed. Re-check periodically, and turn on encrypted DNS (DoH/DoT) if it isn't already.",
                severity: .clean, qualified: false)
        }

        // threatDetected — tailor by the top diagnosis id.
        let top = det.diagnoses.first
        let id = top?.id.rawValue ?? ""
        // `qualified` when the top diagnosis is only probable, or a required check was skipped.
        let qualified = incomplete || (top?.confidence != .conclusive)

        var meaning: String
        let action: String
        let severity: Severity

        switch id {
        case "dnsSpoofing":
            meaning = "Your DNS answers are being altered: the addresses you receive don't match what trusted resolvers return, which can send your traffic to the wrong place."
            action = "Switch to a trusted DoH/DoT resolver and avoid logins or payments on this network until it's clean. Capture a Witness snapshot as evidence."
            severity = .critical
        case "censorshipBlocking":
            meaning = "Some domains are being blocked or redirected: the responses look filtered rather than genuine."
            action = "Use encrypted DNS, an alternate resolver, or a VPN to bypass the filtering."
            severity = .high
        case "transportDowngrade":
            meaning = "Your private DNS path is being forced down to plaintext: something on the network is stopping encrypted DNS from working."
            action = "Use a DoH/DoT provider the network can't downgrade, and check whether your ISP or VPN is forcing a resolver."
            severity = .high
        case "dnsInterception":
            meaning = "A device on the path is transparently intercepting your DNS: queries are being answered by something other than the resolver you chose."
            action = "Identify the device via Portal / LAN mapping, switch to DoT/DoH to bypass it, and contact your network admin."
            severity = .critical
        case "contentTampering":
            meaning = "The content coming back shows signs of tampering: what you receive isn't what the origin sent."
            action = "Verify TLS/HSTS, avoid sensitive sites on this network, and use a trusted VPN."
            severity = .critical
        case "dnsPrivacyLeak":
            meaning = "Your DNS queries are leaking: some lookups are bypassing your private/encrypted path and are visible to others on the network."
            action = "Enable your VPN kill-switch / DNS-leak protection and re-run the leak test."
            severity = .high
        case "localCompromise":
            meaning = "The signs point to this machine itself: a local process or configuration is manipulating DNS or exfiltrating data."
            action = "Run a malware scan, isolate the host, and preserve the Cage capture as evidence."
            severity = .critical
        default:
            meaning = top.map { "We believe this is \($0.displayName.lowercased())." }
                ?? "Your DNS shows signs of tampering."
            action = "Switch to a trusted encrypted DNS resolver and preserve the evidence."
            severity = .high
        }

        if qualified {
            meaning += " " + confidenceCaveat
        }
        return ResultAction(meaning: meaning, primaryAction: action, severity: severity, qualified: qualified)
    }
}

using System.Linq;

namespace Syxx.Kit;

// A1 (the "so what" layer), re-derived onto the matrix determinative engine.
//
// MATRIX-MIGRATION-PLAN.md Phase 3.6 — the Windows twin of
// macOS/Sources/SyxxKit/Synthesis/ResultAction.swift.
//
// Turns a Conclusion into ONE plain-language sentence of what it MEANS for the user plus the single
// most important action. Previously switched on the removed `Conclusion.Diagnosis` enum; now reads
// `Conclusion.Determination: DeterminativeResult?` (owner directive 2026-07-21).
//
// HONESTY CONTRACT: an inconclusive, hard-gated, or no-determination result NEVER reads as "safe",
// and its action is always "get a real conclusion", never reassurance. A threat carrying only `probable`
// confidence, or riding over a skipped check, is `qualified` — the surface shows the caveat without
// softening the action (better-safe).
//
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md).

/// The A1 "so what" presentation of a Conclusion: what it means, and the one action that matters.
/// <paramref name="Qualified"/> is true when confidence is limited or a required check did not run.
public sealed record ResultAction(string Meaning, string PrimaryAction, Severity Severity, bool Qualified);

public static class ResultActions
{
    /// Standard caveat appended when a result is `qualified` — a limited-confidence threat read.
    public const string ConfidenceCaveat =
        "Confidence is limited; treat this as a strong signal to act on, not a certainty.";

    public static ResultAction For(Conclusion c)
    {
        bool incomplete = c.HasHardGatedSection;

        // No determination, or an explicit abstention → UNFINISHED, never safe.
        if (c.Determination is not { } det)
        {
            return new ResultAction(
                "The determinative engine could not evaluate this run: this is an unfinished conclusion, not a clean one.",
                "Re-run the checks, then synthesize again for a real conclusion.",
                Severity.Unknown, Qualified: true);
        }

        // Untested MUST be handled before the threat fall-through below. These are `if` guards,
        // not an exhaustive switch, so an unhandled state reaches the `default:` arm — where an
        // empty Diagnoses list yields `top == null` and renders "Your DNS shows signs of
        // tampering." at Severity.High. That is a fabricated threat conclusion for a scan that
        // never ran: the most severe form of the Untested/Inconclusive/threat conflation.
        if (det.State == DeterminativeState.Untested)
        {
            return new ResultAction(
                "No checks have run yet, so there is nothing to judge: this is an unmeasured conclusion, not a clean one.",
                "Run a scan, then synthesize again for a real conclusion.",
                Severity.Unknown, Qualified: true);
        }

        if (det.State == DeterminativeState.Inconclusive)
        {
            var overhang = det.Suppressed.Count == 0 ? "" :
                " A possible threat signal was seen but could not be verified.";
            return new ResultAction(
                "The checks could not determine whether your DNS is trustworthy: this is an unfinished conclusion, not a clean one." + overhang,
                "Run the checks that were skipped or failed, then synthesize again for a real conclusion.",
                Severity.Unknown, Qualified: true);
        }

        if (det.State == DeterminativeState.Clean)
        {
            if (incomplete)
            {
                return new ResultAction(
                    "No tampering was detected, but confidence is limited: some checks were inconclusive or did not run.",
                    "Run the checks that were skipped, then synthesize again to confirm.",
                    Severity.Unknown, Qualified: true);
            }
            return new ResultAction(
                "No tampering was detected: your DNS answers match trusted vantage points and show no signs of interception or leakage.",
                "No action needed. Re-check periodically, and turn on encrypted DNS (DoH/DoT) if it isn't already.",
                Severity.Clean, Qualified: false);
        }

        // threatDetected — tailor by the top diagnosis id.
        var top = det.Diagnoses.FirstOrDefault();
        var id = top?.Id.Value ?? "";
        // `qualified` when the top diagnosis is only probable, or a required check was skipped.
        bool qualified = incomplete || top?.Confidence != DiagnosisConfidence.Conclusive;

        string meaning;
        string action;
        Severity severity;

        switch (id)
        {
            case "dnsSpoofing":
                meaning = "Your DNS answers are being altered: the addresses you receive don't match what trusted resolvers return, which can send your traffic to the wrong place.";
                action = "Switch to a trusted DoH/DoT resolver and avoid logins or payments on this network until it's clean. Capture a Witness snapshot as evidence.";
                severity = Severity.Critical;
                break;
            case "censorshipBlocking":
                meaning = "Some domains are being blocked or redirected: the responses look filtered rather than genuine.";
                action = "Use encrypted DNS, an alternate resolver, or a VPN to bypass the filtering.";
                severity = Severity.High;
                break;
            case "transportDowngrade":
                meaning = "Your private DNS path is being forced down to plaintext: something on the network is stopping encrypted DNS from working.";
                action = "Use a DoH/DoT provider the network can't downgrade, and check whether your ISP or VPN is forcing a resolver.";
                severity = Severity.High;
                break;
            case "dnsInterception":
                meaning = "A device on the path is transparently intercepting your DNS: queries are being answered by something other than the resolver you chose.";
                action = "Identify the device via Portal / LAN mapping, switch to DoT/DoH to bypass it, and contact your network admin.";
                severity = Severity.Critical;
                break;
            case "contentTampering":
                meaning = "The content coming back shows signs of tampering: what you receive isn't what the origin sent.";
                action = "Verify TLS/HSTS, avoid sensitive sites on this network, and use a trusted VPN.";
                severity = Severity.Critical;
                break;
            case "dnsPrivacyLeak":
                meaning = "Your DNS queries are leaking: some lookups are bypassing your private/encrypted path and are visible to others on the network.";
                action = "Enable your VPN kill-switch / DNS-leak protection and re-run the leak test.";
                severity = Severity.High;
                break;
            case "localCompromise":
                meaning = "The signs point to this machine itself: a local process or configuration is manipulating DNS or exfiltrating data.";
                action = "Run a malware scan, isolate the host, and preserve the Cage capture as evidence.";
                severity = Severity.Critical;
                break;
            default:
                meaning = top is not null
                    ? $"We believe this is {top.DisplayName.ToLowerInvariant()}."
                    : "Your DNS shows signs of tampering.";
                action = "Switch to a trusted encrypted DNS resolver and preserve the evidence.";
                severity = Severity.High;
                break;
        }

        if (qualified)
            meaning += " " + ConfidenceCaveat;

        return new ResultAction(meaning, action, severity, qualified);
    }
}
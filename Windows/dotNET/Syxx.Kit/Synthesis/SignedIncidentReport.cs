using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

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
//   * If no Conclusion is provided, the report omits the A2 explanation rather than
//     fabricating one — "no data" never reads as "nothing wrong".
//
// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/SignedIncidentReport.swift.
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md:42).

/// Renders a <see cref="WitnessSnapshot"/> as a shareable, human-readable signed incident report.
public static class SignedIncidentReport
{
    private static readonly string Rule = new string('\u2500', 66);
    private static readonly string ThinRule = new string('\u00B7', 66);

    /// Render a full incident report as text.
    /// <param name="snapshot">The signed witness snapshot to render.</param>
    /// <param name="explanation">Optional A2 plain-language explanation to include in the report.</param>
    /// <returns>A formatted text report suitable for saving or sharing.</returns>
    public static string Render(WitnessSnapshot snapshot, PlainExplanation? explanation = null)
    {
        var lines = new List<string>();

        // ── Header ──
        lines.Add(Rule);
        lines.Add("           SYXX SIGNED INCIDENT REPORT");
        lines.Add(Rule);
        lines.Add("");

        // ── Metadata ──
        lines.Add("  REPORT METADATA");
        lines.Add(ThinRule);
        lines.Add($"    Generated:      {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss 'UTC'}");
        lines.Add($"    Domain:         {snapshot.TargetDomain}");
        lines.Add($"    Format Version: {snapshot.FormatVersion}");
        lines.Add(snapshot.ExpiresAt is not null
            ? $"    Expires:        {snapshot.ExpiresAt:yyyy-MM-dd HH:mm:ss 'UTC'}"
            : "    Expires:        Never");
        lines.Add("");

        // ── Result ──
        lines.Add("  CONCLUSION");
        lines.Add(ThinRule);
        var comparison = snapshot.Comparison;
        // B157: an empty comparison (no local records, no public results) compared nothing —
        // the flags are all false because nothing was observed, not because the answers agreed.
        // A "MATCHES" result on an empty set is Ed25519-signed and presented as third-party-
        // verifiable, making it a forgeable authoritative claim. Guard before the flag chain.
        if ((comparison.LocalRecords is null || comparison.LocalRecords.Count == 0) &&
            (comparison.PublicResolverResults is null || comparison.PublicResolverResults.Count == 0))
            lines.Add("    Conclusion:   INCONCLUSIVE. Nothing was compared.");
        else if (comparison.IsRedirectionDetected)
            lines.Add("    Conclusion:   DIVERGES. Answers differ from reference.");
        else if (comparison.IsDegraded)
            lines.Add("    Conclusion:   DEGRADED. Some evidence was unreliable.");
        else if (comparison.IsInconclusive)
            lines.Add("    Conclusion:   INCONCLUSIVE. Could not determine trustworthiness.");
        else
            lines.Add("    Conclusion:   MATCHES. Answers match reference.");
        lines.Add($"    Degraded:     {(comparison.IsDegraded ? "Yes" : "No")}");
        lines.Add($"    Inconclusive: {(comparison.IsInconclusive ? "Yes" : "No")}");
        lines.Add("");

        // ── Decision (A2 explanation) ──
        if (explanation is not null)
        {
            lines.Add("  DECISION");
            lines.Add(ThinRule);
            lines.Add($"    {explanation.Narrative}");
            lines.Add("");
        }

        // ── Evidence Summary ──
        lines.Add("  EVIDENCE SUMMARY");
        lines.Add(ThinRule);
        lines.Add($"    Local Resolver:   {comparison.LocalServer}");

        var publicResolvers = comparison.PublicResolverOutcomes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (publicResolvers.Count > 0)
            lines.Add($"    Public Resolvers: {string.Join(", ", publicResolvers)}");

        var localIps = comparison.LocalOutcome.Records
            .Select(r => r.Rdata)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        if (localIps.Count > 0)
            lines.Add($"    Local Answer(s):  {string.Join(", ", localIps)}");

        var refIps = comparison.PublicResolverOutcomes.Values
            .SelectMany(v => v.Records)
            .Select(r => r.Rdata)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        if (refIps.Count > 0)
            lines.Add($"    Reference IP(s): {string.Join(", ", refIps)}");
        else if (comparison.IsDegraded || comparison.IsInconclusive)
            lines.Add("    Reference IP(s): (no reliable reference available)");

        // B252: pre-v4 snapshots carry DoH outcomes in the display only — NOT covered by the
        // signature. Render them with a clear "unsigned" marker so readers are not invited to
        // trust them. At v4+ DoH evidence is canonical and signed, so the marker is omitted.
        bool dohIsUnsigned = comparison.DohOutcomes is not null &&
                             comparison.DohOutcomes.Count > 0 &&
                             snapshot.FormatVersion < SignedEvidencePayload.CurrentFormatVersion;
        if (dohIsUnsigned)
        {
            lines.Add("");
            lines.Add("    \u26A0 DoH Cross-Check (UNSIGNED \u2014 not covered by the Ed25519 seal):");
            foreach (var (provider, outcome) in (comparison.DohOutcomes ?? []).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var status = outcome.Status == DNSResponseStatus.Success ? "records" : outcome.Status.ToString();
                var ips = string.Join(", ", outcome.Records.Select(r => r.Rdata));
                lines.Add($"      {provider}: {status}{(string.IsNullOrEmpty(ips) ? "" : $" ({ips})")}");
            }
        }
        lines.Add("");

        // ── Cryptographic Seal ──
        lines.Add("  CRYPTOGRAPHIC SEAL");
        lines.Add(ThinRule);
        lines.Add($"    Key ID:     {snapshot.KeyId}");
        lines.Add($"    Public Key: {Truncate(snapshot.PublicKey, 48)}…");
        lines.Add($"    Signature:  {Truncate(snapshot.Signature, 32)}…");
        lines.Add("");

        // ── Verification ──
        lines.Add("  VERIFICATION");
        lines.Add(ThinRule);
        lines.Add("    Run:  syxx witness verify <file>");
        lines.Add("    Or:   syxx witness report <file>");
        lines.Add("");
        lines.Add("    \u26A0  Tamper-evident: altering signed data will break the");
        lines.Add("       Ed25519 signature. Independent verification requires only the");
        lines.Add("       public key shown above.");
        // B252: note that DoH data is unsigned when the snapshot predates v4 (otherwise it is signed)
        if (dohIsUnsigned)
        {
            lines.Add("    \u26A0  DoH cross-check data is NOT signed \u2014 it can be");
            lines.Add("       altered without breaking the seal.");
        }
        lines.Add("");
        lines.Add(Rule);

        return string.Join("\n", lines);
    }

    private static string Truncate(string value, int maxLen)
        => value.Length <= maxLen ? value : value[..maxLen];
}

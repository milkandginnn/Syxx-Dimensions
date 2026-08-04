using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit
{
    /// <summary>
    /// Pure, UI-free determinative core of the Witness "testify" (seal) flow. Parity twin of macOS
    /// SyxxKit/Trust/WitnessTestifyPlanner.swift.
    ///
    /// WHY THIS EXISTS (DELTAS.md D093 + the 2026-07-22 extraction refactor): the seal-and-drift logic
    /// below was hand-inlined into GUI view-models on three platforms. During one such hand-duplication
    /// the drift-population step was silently DROPPED on Windows (D093) — a signed testimony against an
    /// actively-redirected domain reported zero drift in Conclusions, and nothing caught it until a
    /// dedicated code review did, because the logic lived only inside <c>SyxxUI</c>, a WinUI 3 project
    /// that no CI job can compile or unit-test (D092/D101). Relocating the pure steps into this Kit type
    /// — which DOES build and unit-test on CI (Syxx.Kit is plain net10.0, exercised on the Linux legs) —
    /// means a future drop of the drift step fails a test instead of shipping.
    ///
    /// SCOPE IS DELIBERATELY NARROW. This owns exactly the three steps that are pure, deterministic, and
    /// identical across every platform: (5) sign the LIVE comparison, (6) interpret the redirection
    /// result, (7) build the <see cref="AnswerDrift"/>. It does NOT own domain-empty validation, key
    /// presence/loading, the async vantage-comparison probe, command lifecycle, or any observable-property
    /// write. Those are platform- and UI-coupled and legitimately DIVERGENT (different user-visible
    /// strings, different key stores Keychain/DPAPI, and Windows folds empty-domain into its null-comparison
    /// "Nothing to witness" path rather than emitting a distinct message as macOS does) and must stay in
    /// each view-model.
    /// </summary>
    public static class WitnessTestifyPlanner
    {
        /// <summary>
        /// Result of a successful seal: the signed snapshot plus the drift the redirection result
        /// implies (<c>null</c> when no redirection was detected). Callers materialize
        /// <c>Drift is null ? new List&lt;AnswerDrift&gt;() : new List&lt;AnswerDrift&gt; { Drift }</c>
        /// into their SectionInputs.Drifts, exactly as the former inlined block did.
        /// </summary>
        public sealed class Outcome
        {
            public WitnessSnapshot Snapshot { get; }
            public AnswerDrift? Drift { get; }
            public Outcome(WitnessSnapshot snapshot, AnswerDrift? drift)
            {
                Snapshot = snapshot;
                Drift = drift;
            }
        }

        /// <summary>
        /// Steps 5-7. Signs <paramref name="comparison"/> for <paramref name="domain"/>, then derives
        /// the drift from the same comparison. Propagates whatever
        /// <see cref="WitnessSigner.CreateSignedSnapshot(string, ComparisonResult, string, DateTime?)"/>
        /// throws (invalid key, etc.) so the caller's existing catch keeps producing its existing
        /// "Could not create witness snapshot: …" message — behavior unchanged. <paramref name="now"/>
        /// is injectable ONLY so a unit test can pin <see cref="AnswerDrift.DetectedAt"/>; it defaults
        /// to <see cref="DateTime.UtcNow"/>, matching the former inlined block.
        /// </summary>
        public static Outcome Seal(string domain, string local, ComparisonResult comparison,
                                   string privateKeyBase64, DateTime? now = null)
        {
            var snapshot = WitnessSigner.CreateSignedSnapshot(domain, comparison, privateKeyBase64);
            var drift = DriftIfRedirected(domain, local, comparison, now);
            return new Outcome(snapshot, drift);
        }

        /// <summary>
        /// Steps 6-7 in isolation — the exact block D093 dropped, now single-sourced and directly
        /// unit-testable without a signing key. Pure: returns the <see cref="AnswerDrift"/> when
        /// redirection was detected, otherwise <c>null</c>. Field derivation is preserved verbatim from
        /// the former inlined block (PreviousAnswer = first public resolver's joined rdata; NewAnswer =
        /// local resolver's joined rdata; Resolver = <paramref name="local"/>).
        /// </summary>
        public static AnswerDrift? DriftIfRedirected(string domain, string local, ComparisonResult comparison,
                                                     DateTime? now = null)
        {
            if (!comparison.IsRedirectionDetected) return null;
            var localIPs = string.Join(", ", comparison.LocalOutcome.Records.Select(r => r.Rdata));
            // C154: deterministic selection — Dictionary.Values has no guaranteed enumeration
            // order, so the same evidence could produce different PreviousAnswer values across runs.
            // Order by resolver key (ordinal) to ensure byte-identical drift for identical evidence.
            var firstPublic = comparison.PublicResolverOutcomes
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Value)
                .FirstOrDefault();
            var publicIPs = firstPublic is null
                ? string.Empty
                : string.Join(", ", firstPublic.Records.Select(r => r.Rdata));
            return new AnswerDrift
            {
                Domain = domain,
                QueryType = "A",
                PreviousAnswer = publicIPs,
                NewAnswer = localIPs,
                Resolver = local,
                DetectedAt = now ?? DateTime.UtcNow
            };
        }
    }
}

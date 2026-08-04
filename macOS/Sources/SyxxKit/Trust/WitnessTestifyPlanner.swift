import Foundation

/// Pure, UI-free determinative core of the Witness "testify" (seal) flow.
///
/// WHY THIS EXISTS (DELTAS.md D093 + this refactor): the seal-and-drift logic below was previously
/// hand-inlined, verbatim, in TWO macOS call sites — `AppModel.witnessTestify()` and
/// `AppModel.runWitness()` — and hand-duplicated a third time into Windows `AppViewModel`. During one
/// such hand-duplication the drift-population step (step 7) was silently DROPPED on Windows (D093):
/// a signed testimony against an actively-redirected domain reported zero drift in Conclusions, and
/// nothing caught it until a dedicated code review did — because the logic lived only inside a WinUI3
/// GUI view-model that no CI job can compile or unit-test (D092/D101). Relocating steps 5-7 into this
/// Kit type gives them a single home with a CI-runnable unit test (`swift test` here, Syxx.Kit on
/// Linux CI on Windows), so a future drop of the drift step fails a test instead of shipping.
///
/// SCOPE IS DELIBERATELY NARROW. This owns exactly the three steps that are pure, deterministic, and
/// identical across every platform:
///   (5) sign the LIVE comparison, (6) interpret the redirection result, (7) build the `AnswerDrift`.
/// It does NOT own domain-empty validation, key presence/loading, the async vantage-comparison probe,
/// task lifecycle/cancellation, or any `@Published` write. Those steps are platform- and UI-coupled
/// and legitimately DIVERGENT (different user-visible strings — even between the two macOS call sites —
/// different key stores Keychain/DPAPI, sync-vs-async guards, ProbeRunner budget wrapping) and must
/// stay in each view-model. Forcing them into a shared shape would either change behavior or reduce to
/// a parameter-heavy shell that shares control flow while injecting all content — a worse abstraction
/// than the honest duplication it would replace.
public enum WitnessTestifyPlanner {

    /// Result of a successful seal: the signed snapshot plus the drift the redirection result implies
    /// (`nil` when no redirection was detected). Callers assign `outcome.drift.map { [$0] } ?? []` to
    /// their `SectionInputs.drifts`, exactly as the two inlined blocks did.
    public struct Outcome: Sendable {
        public let snapshot: WitnessSnapshot
        public let drift: AnswerDrift?
        public init(snapshot: WitnessSnapshot, drift: AnswerDrift?) {
            self.snapshot = snapshot
            self.drift = drift
        }
    }

    /// Steps 5-7. Signs `comparison` for `domain`, then derives the drift from the same comparison.
    /// Throws whatever `WitnessSigner.createSignedSnapshot` throws (invalid key, etc.) so the caller's
    /// existing catch keeps producing its existing "Could not create witness snapshot: …" message —
    /// behavior unchanged. `now` is injectable ONLY so a unit test can pin `AnswerDrift.detectedAt`;
    /// it defaults to the wall clock, matching the call sites' `Date()`.
    public static func seal(
        domain: String,
        local: String,
        comparison: ComparisonResult,
        privateKeyBase64: String,
        now: Date = Date()
    ) throws -> Outcome {
        let snapshot = try WitnessSigner.createSignedSnapshot(
            domain: domain, comparison: comparison, privateKeyBase64: privateKeyBase64)
        let drift = driftIfRedirected(domain: domain, local: local, comparison: comparison, now: now)
        return Outcome(snapshot: snapshot, drift: drift)
    }

    /// Steps 6-7 in isolation — the exact block D093 dropped, now single-sourced and directly
    /// unit-testable without a signing key. Pure: returns the `AnswerDrift` when redirection was
    /// detected, otherwise `nil`. Field derivation is preserved verbatim from the former inlined
    /// blocks (previousAnswer = first public resolver's joined rdata; newAnswer = local resolver's
    /// joined rdata; resolver = `local`).
    public static func driftIfRedirected(
        domain: String,
        local: String,
        comparison: ComparisonResult,
        now: Date = Date()
    ) -> AnswerDrift? {
        guard comparison.isRedirectionDetected else { return nil }
        // C107: gate on degraded/inconclusive — a drift from a degraded comparison is not a
        // clean measurement and must not be presented as one.
        guard !comparison.isDegraded, !comparison.isInconclusive else { return nil }
        let localIPs = comparison.localOutcome.records.map { $0.rdata }.joined(separator: ", ")
        // B225 + C107: Swift Dictionary.values is randomized per process — picking `.first` makes
        // the "reference answer" of a drift record nondeterministic across runs (and divergent
        // from the Windows twin). Stable-ise by sorting on the resolver key with byte-ordinal
        // comparison, matching the convention used in SignedEvidencePayload for TTL selection.
        // C107: prefer the first outcome that actually has records so an empty-string "previous
        // answer" is never fabricated from a failed resolver whose records array is empty.
        let stableFirstOutcome = comparison.publicResolverOutcomes
            .sorted { $0.key.utf8.lexicographicallyPrecedes($1.key.utf8) }
            .first(where: { !$0.value.records.isEmpty })?.value
        let publicIPs = stableFirstOutcome?.records
            .map { $0.rdata }.joined(separator: ", ")
        // C107: refuse to build the drift when no public resolver returned records — an empty
        // previousAnswer presented as a measured prior is a fabricated measurement (§4).
        guard let publicIPs, !publicIPs.isEmpty else { return nil }
        return AnswerDrift(domain: domain, queryType: "A", previousAnswer: publicIPs,
                           newAnswer: localIPs, resolver: local, detectedAt: now)
    }
}

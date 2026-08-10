import Foundation

// A4 (ROADMAP "Implementation plan v2" — five-minute first-run). One call → an orchestrated scan
// across the disciplines that need no configuration → a synthesized Conclusion and an A3 report
// card. This is the "one button, full scan, graded result before any configuration" entry point,
// shared by the CLI and the GUI so neither re-implements probe orchestration.
//
// HONESTY CONTRACT (Protocol §1/§10, DoW #3): a probe that fails, times out, or cannot run leaves
// its input nil — the synthesis engine then raises the section untested/inconclusive and the
// result is hard-gated. A scan that could not measure the network yields `.ungraded`, never a
// letter. Nothing here fabricates a datapoint to make the scan "complete".
//
// Byte-parity twin of Windows `Syxx.Kit/Synthesis/FirstRunScan.cs`.

/// The outcome of a first-run scan: the synthesized conclusion, its report card, and which
/// disciplines actually produced usable evidence (for honest "what we could/couldn't measure").
public struct FirstRunResult: Sendable {
    public let conclusion: Conclusion
    public let card: ReportCard
    public let ran: [String]
    public let skipped: [String]
    /// The evidence the scan actually gathered, so a caller can merge it into a live session rather
    /// than re-running every probe. (The Windows twin owes the same field — see FirstRunScan.cs.)
    public let inputs: SectionInputs

    public init(conclusion: Conclusion, card: ReportCard, ran: [String], skipped: [String],
                inputs: SectionInputs) {
        self.conclusion = conclusion
        self.card = card
        self.ran = ran
        self.skipped = skipped
        self.inputs = inputs
    }
}

public enum FirstRunScan {
    /// The default target for a zero-configuration scan. A well-known, DNSSEC-signed, widely
    /// mirrored name so a first run needs no input from the user.
    public static let defaultDomain = "github.com"

    /// The LIVE half of a first run: probe, then hand the evidence to `summarize`.
    ///
    /// macOS had only the pure half until 2026-07-17 — `summarize` existed, was tested, and nothing
    /// could feed it, so A4 was unreachable on this platform while Windows shipped `RunAsync`.
    ///
    /// Composed from the four `ProbePlan` entry points the GUI already drives rather than
    /// re-orchestrating probe-by-probe (as the Windows twin does). Same disciplines, same budgets,
    /// and — the reason that matters — a first run exercises the exact code paths the individual
    /// buttons do, so the two can never disagree about what a probe returns.
    ///
    /// Every step is failure-tolerant by omission: a discipline that cannot run leaves its inputs
    /// nil, `summarize` reports it as unmeasured, and the engine hard-gates the result. Nothing is
    /// invented to make the scan look complete.
    public static func runAsync(domain: String? = nil,
                                isPro: Bool = false,
                                progress: (@Sendable (String) -> Void)? = nil) async -> FirstRunResult {
        let target = (domain?.trimmingCharacters(in: .whitespacesAndNewlines)).flatMap {
            $0.isEmpty ? nil : $0
        } ?? defaultDomain

        let servers = NetworkKeyState.getSystemDNSServers()
        // No system resolver is itself a finding, not a reason to guess. But a scan with no resolver
        // at all measures nothing, so fall back to a public one and let Portal report the anomaly.
        // B174: record the substitution so downstream artefacts never call the substitute "local".
        var resolverWasSubstituted = false
        let localResolver = servers.first ?? {
            SyxxDiagnostics.breadcrumb(
                category: "firstrun.resolver",
                message: "System resolver undetectable — falling back to 8.8.8.8",
                level: .warning)
            resolverWasSubstituted = true
            return "8.8.8.8"
        }()

        var inputs = SectionInputs()
        inputs.resolverSubstituted = resolverWasSubstituted

        progress?("Prophet — checking whether your resolver is telling the truth…")
        merge(&inputs, await ProbePlan.runProphet(domain: target, resolver: localResolver))

        progress?("Mirror — comparing your answers against independent vantage points…")
        // isPro: false — a first run must work before anyone has paid. The Pro path adds server
        // corroboration; its absence lowers completeness, it does not fabricate a clean result.
        merge(&inputs, await ProbePlan.runMirror(domain: target, localResolver: localResolver, isPro: isPro))

        progress?("Portal — mapping this network and its encrypted-DNS path…")
        merge(&inputs, await ProbePlan.runPortal())

        progress?("Glutton — checking whether your lookups are leaking…")
        merge(&inputs, await ProbePlan.runGlutton(servers: servers))

        inputs.targetDomain = target
        progress?("Synthesizing…")
        return summarize(inputs)
    }

    /// Fold a ProbePlan result into the accumulator. Non-nil wins; a nil NEVER overwrites evidence
    /// another discipline already produced — that would silently delete a measurement.
    ///
    /// Internal rather than private on purpose (G-03): every caller is `runAsync`, which dials real
    /// resolvers and so is untestable by construction — which is how this function came to omit
    /// `warpManifest` with no test noticing. Anything that can silently drop a datapoint has to be
    /// reachable from the suite. Parity twin: `FirstRunScan.MergeMirror` on Windows.
    static func merge(_ into: inout SectionInputs, _ from: SectionInputs) {
        if let v = from.resolution         { into.resolution = v }
        if let v = from.dnssec             { into.dnssec = v }
        if let v = from.nxCanary           { into.nxCanary = v }
        if let v = from.dnssecCanary       { into.dnssecCanary = v }
        if let v = from.latency            { into.latency = v }
        if let v = from.stability          { into.stability = v }
        if let v = from.wildcardSynthesis  { into.wildcardSynthesis = v }
        if let v = from.denial             { into.denial = v }
        if let v = from.dualStack          { into.dualStack = v }
        if let v = from.edns0              { into.edns0 = v }
        if let v = from.versionBind        { into.versionBind = v }
        if let v = from.stubEntropy        { into.stubEntropy = v }
        if let v = from.comparison         { into.comparison = v }
        if let v = from.leak               { into.leak = v }
        if let v = from.ecs                { into.ecs = v }
        // Retired WARP_ASSEMBLY shim; ProbePlan populates nothing into it (ContentWarp.swift:21).
        // Folded only so removing the shim stays a separate, deliberate change.
        if let v = from.warp               { into.warp = v }
        // G-03: WARP_MANIFEST_V1 is the LIVE content-integrity result and the only one ProbePlan
        // actually fills (ProbePlan.swift:107,155). Synthesis has always read it
        // (Synthesizer.swift:737); it was this fold that dropped it, so Mirror's content check was
        // absent from every first-run scan — the first thing a new user ever sees.
        if let v = from.warpManifest       { into.warpManifest = v }
        if let v = from.authoritative      { into.authoritative = v }
        if let v = from.svcb               { into.svcb = v }
        if let v = from.ttlAnomaly         { into.ttlAnomaly = v }
        if let v = from.cnameChain         { into.cnameChain = v }
        if let v = from.serverGroundTruth  { into.serverGroundTruth = v }
        if let v = from.network            { into.network = v }
        if let v = from.encryptedTransport { into.encryptedTransport = v }
        // B171: openResolver is populated by ProbePlan.runPortal() but was silently dropped
        // here — the open-resolver/amplification Advisory never reached the Conclusion.
        if let v = from.openResolver       { into.openResolver = v }
        if let v = from.targetDomain       { into.targetDomain = v }
        if let v = from.targetQueryType    { into.targetQueryType = v }
        // C200: transportObservation is populated by Prophet/Mirror probes and must survive
        // the fold — without it, synthesis loses the transport-path context for the run.
        if let v = from.transportObservation { into.transportObservation = v }
    }

    /// The PURE half of a first run: given whatever evidence the probes actually produced, decide
    /// which disciplines really contributed, synthesize, and grade.
    ///
    /// Deliberately separated from the live probing so the decision logic has a seam and is testable
    /// without a network — a discipline counts as having run only if it produced a non-nil
    /// datapoint, so a failed or timed-out probe is reported as unmeasured rather than silently
    /// treated as clean.
    public static func summarize(_ inputs: SectionInputs) -> FirstRunResult {
        var ran: [String] = []
        var skipped: [String] = []

        func track(_ produced: Bool, _ name: String) {
            if produced { ran.append(name) } else { skipped.append(name) }
        }

        // Prophet needs at least a resolution or a canary to have said anything at all.
        track(inputs.resolution != nil || inputs.nxCanary != nil, "Prophet")
        track(inputs.comparison != nil, "Mirror")
        track(inputs.network != nil, "Portal")
        // B172: Glutton's primary datapoint is `leak` — ProbePlan.runGlutton always sets it
        // and may leave `ecs` nil on timeout. Track by either input, matching EvidenceAdapters.
        track(inputs.leak != nil || inputs.ecs != nil, "Glutton")

        let conclusion = Synthesizer.conclude(from: inputs)
        return FirstRunResult(conclusion: conclusion, card: ReportCards.For(conclusion),
                              ran: ran, skipped: skipped, inputs: inputs)
    }
}

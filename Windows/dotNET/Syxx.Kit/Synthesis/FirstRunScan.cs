using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syxx.Kit;

// A4 (ROADMAP "Implementation plan v2" — five-minute first-run). One call → an orchestrated scan
// across the disciplines that need no configuration → a synthesized Conclusion and an A3 report
// card. This is the "one button, full scan, graded result before any configuration" entry point,
// shared by the CLI and the GUI so neither re-implements probe orchestration.
//
// Console-free by design: progress is reported via IProgress<string> so any surface can render it.
//
// HONESTY CONTRACT (Protocol §1/§10, DoW #3): a probe that fails, times out, or cannot run leaves
// its input NULL — the synthesis engine then raises the section Untested/Inconclusive and the
// result is hard-gated. A scan that could not measure the network yields Grade.Ungraded, never a
// letter. Nothing here fabricates a datapoint to make the scan "complete".
//
// Windows-parity twin owed on macOS (SyxxKit/Synthesis/FirstRunScan.swift).

/// The outcome of a first-run scan: the synthesized conclusion, its report card, and which
/// disciplines actually produced usable evidence (for honest "what we could/couldn't measure").
public sealed record FirstRunResult(
    Conclusion Conclusion,
    ReportCard Card,
    IReadOnlyList<string> Ran,
    IReadOnlyList<string> Skipped,
    // The evidence the scan gathered, so a caller can merge it into a live session rather than
    // re-running every probe. Parity with macOS FirstRunResult.inputs (added 2026-07-17): the GUI
    // there feeds it back into the session so the component cards reflect the scan.
    SectionInputs Inputs,
    // F033: disciplines that ran but produced inconclusive data, separated from those that
    // produced definitive (positive/negative) results. A probe that ran but could not resolve
    // is not the same as a probe that found clean or tampered evidence.
    IReadOnlyList<string>? Inconclusive = null);

public static class FirstRunScan
{
    /// The default target for a zero-configuration scan. A well-known, DNSSEC-signed, widely
    /// mirrored name so a first run needs no input from the user.
    public const string DefaultDomain = "github.com";

    /// Runs the no-configuration disciplines (Prophet, Mirror, Portal, Glutton-local) against
    /// <paramref name="domain"/> and synthesizes a result + grade. Never throws for probe failure:
    /// a failed probe stays null and is surfaced honestly as an unmeasured section.
    ///
    /// <param name="isPro">If true, enables Pro-level Mirror corroboration (Dimension server).
    /// Parity twin of macOS FirstRunScan.runAsync(domain:isPro:progress:).</param>
    public static async Task<FirstRunResult> RunAsync(
        string? domain = null,
        bool isPro = false,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        domain = string.IsNullOrWhiteSpace(domain) ? DefaultDomain : domain.Trim();
        var inputs = new SectionInputs();

        var servers = NetworkKeyState.GetSystemDNSServers();
        string localResolver = servers.FirstOrDefault();
        if (localResolver == null)
        {
            Console.WriteLine("⚠️ System resolver undetectable — falling back to 8.8.8.8");
            localResolver = "8.8.8.8";
        }

        // ---- Prophet — is the resolver telling the truth? -------------------------------------
        progress?.Report("Prophet — checking whether your resolver is telling the truth…");
        ct.ThrowIfCancellationRequested();
        var pBudget = ProbeBudget.DefaultProphet;

        inputs.Resolution = await ProbeRunner.RunAsync<DNSResponse>("queryA", pBudget, () =>
            DNSResolver.QueryUDPDetailedAsync(new DNSQuery(domain, 1, localResolver)));
        try { inputs.Dnssec = await DNSSECVerifier.CheckStatusAsync(domain, localResolver); }
        catch { /* leave null — an unmeasured DNSSEC state is not a clean one */ }

        inputs.NxCanary = await ProbeRunner.RunAsync<DNSCanaries.CanaryResult>("nxCanary", pBudget, () =>
            DNSCanaries.NxdomainHijackAsync(localResolver));
        inputs.DnssecCanary = await ProbeRunner.RunAsync<DNSCanaries.CanaryResult>("dnssecCanary", pBudget, () =>
            DNSCanaries.DnssecControlAsync(localResolver));
        inputs.Denial = await ProbeRunner.RunAsync<DenialOfExistence>("denial", pBudget, () =>
            DenialOfExistenceProbe.Probe(name: domain, qtype: 1, server: localResolver));
        inputs.WildcardSynthesis = await ProbeRunner.RunAsync<WildcardSynthesis>("wildcard", pBudget, () =>
            WildcardSynthesisProbe.Probe(domain: domain, resolverUnderTest: localResolver));
        inputs.Edns0 = await ProbeRunner.RunAsync<EDNS0Capability>("edns0", pBudget, () =>
            EDNS0Probe.Probe(domain: domain, server: localResolver));

        // ---- Mirror — does it match independent vantage points? --------------------------------
        // Delegates to ProbePlan.RunMirrorAsync (parity with macOS FirstRunScan), which handles the
        // full Mirror section including vantage comparison, authoritative cross-check, SVCB/TTL/CNAME,
        // content warp, and Pro-level Dimension server corroboration when isPro is true.
        progress?.Report("Mirror — comparing your answers against independent vantage points…");
        ct.ThrowIfCancellationRequested();
        var mirrorInputs = await ProbePlan.RunMirrorAsync(domain, localResolver, isPro: isPro);
        MergeMirror(inputs, mirrorInputs);
        inputs.TargetDomain = domain;

        // ---- Portal — what does this network look like? ----------------------------------------
        progress?.Report("Portal — mapping this network and its encrypted-DNS path…");
        ct.ThrowIfCancellationRequested();
        try { inputs.Network = await NetworkKeyState.GenerateReportAsync(); }
        catch { /* leave null */ }
        inputs.EncryptedTransport = await ProbeRunner.RunAsync<EncryptedTransport>("encryptedTransport",
            ProbeBudget.DefaultPortal, () => EncryptedTransportProbe.Probe());

        // ---- Glutton — is anything leaking? (local-only; no collector in a first run) -----------
        progress?.Report("Glutton — checking whether your lookups are leaking…");
        ct.ThrowIfCancellationRequested();
        if (servers.Count == 0)
        {
            // Refusing to fall back to public resolvers is the honest move — it leaves Glutton
            // unmeasured rather than testing something that isn't the user's actual path.
        }
        else
        {
            inputs.Ecs = await ProbeRunner.RunAsync<ECSLeak>("ecs", ProbeBudget.DefaultGlutton, () =>
                ECSProbe.Probe(server: localResolver));
        }

        progress?.Report("Synthesizing a result…");
        return Summarize(inputs);
    }

    /// Folds the Mirror section's evidence into the scan accumulator. Non-null wins; a null NEVER
    /// overwrites evidence already gathered — that would silently delete a measurement.
    ///
    /// A SEAM ON PURPOSE (G-03). This was seven inline lines inside <see cref="RunAsync"/>, which
    /// dials real resolvers and so is untestable by construction. That is exactly how the copy came
    /// to omit <c>WarpManifest</c> — the live content-integrity result — without a single test
    /// noticing: the scan compiled, ran, and simply reported no content check at all. Anything that
    /// can silently drop a datapoint belongs behind a callable seam. Parity twin: the Swift
    /// `FirstRunScan.merge` (which had the identical omission).
    internal static void MergeMirror(SectionInputs into, SectionInputs from)
    {
        if (from.Comparison is not null) into.Comparison = from.Comparison;
        if (from.Authoritative is not null) into.Authoritative = from.Authoritative;
        if (from.ServerGroundTruth is not null) into.ServerGroundTruth = from.ServerGroundTruth;
        if (from.Svcb is not null) into.Svcb = from.Svcb;
        if (from.TtlAnomaly is not null) into.TtlAnomaly = from.TtlAnomaly;
        if (from.CnameChain is not null) into.CnameChain = from.CnameChain;
        // Retired WARP_ASSEMBLY shim. ProbePlan populates nothing into it (ContentWarp.cs:21-24);
        // copied only so removing the shim stays a separate, deliberate change.
        if (from.Warp is not null) into.Warp = from.Warp;
        // G-03: WARP_MANIFEST_V1 is the LIVE content-integrity result and the only one ProbePlan
        // actually fills (ProbePlan.cs:106,144). Synthesis has always read it (Synthesizer.cs:919);
        // it was the copy that dropped it, so Mirror's content check was absent from every
        // first-run scan — the first thing a new user ever sees.
        if (from.WarpManifest is not null) into.WarpManifest = from.WarpManifest;
    }

    /// The PURE half of a first run: given whatever evidence the probes actually produced,
    /// decide which disciplines really contributed, synthesize, and grade.
    ///
    /// This is deliberately separated from <see cref="RunAsync"/> so the decision logic has a
    /// seam and is testable without a network. `RunAsync` is untestable by construction (it dials
    /// real resolvers); everything that could get the HONESTY wrong lives here instead — a
    /// discipline counts as having run only if it produced a non-null datapoint, so a failed or
    /// timed-out probe is reported as unmeasured rather than silently treated as clean.
    public static FirstRunResult Summarize(SectionInputs inputs)
    {
        var ran = new List<string>();
        var skipped = new List<string>();
        var inconclusive = new List<string>();

        // Prophet needs at least a resolution or a canary to have said anything at all.
        Track(inputs.Resolution is not null || inputs.NxCanary is not null, "Prophet", ran, skipped);
        Track(inputs.Comparison is not null, "Mirror", ran, skipped);
        Track(inputs.Network is not null, "Portal", ran, skipped);
        Track(inputs.Ecs is not null, "Glutton", ran, skipped);

        var conclusion = Synthesizer.Conclude(inputs);

        // F033: reclassify probes that ran but whose section ended Inconclusive as a separate
        // category, so a first-run report can distinguish "ran and resolved" from "ran but could
        // not resolve" without collapsing both onto "Ran."
        var sectionStatusByName = conclusion.SectionStatuses
            .ToDictionary(s => s.Section.ToString(), s => s.Status, StringComparer.OrdinalIgnoreCase);
        foreach (var name in ran.ToList())
        {
            if (sectionStatusByName.TryGetValue(name, out var status) && status == SectionStatus.Inconclusive)
            {
                ran.Remove(name);
                inconclusive.Add(name);
            }
        }

        return new FirstRunResult(conclusion, ReportCards.For(conclusion), ran, skipped, inputs, inconclusive);
    }

    private static void Track(bool produced, string name, List<string> ran, List<string> skipped)
    {
        if (produced) ran.Add(name); else skipped.Add(name);
    }
}

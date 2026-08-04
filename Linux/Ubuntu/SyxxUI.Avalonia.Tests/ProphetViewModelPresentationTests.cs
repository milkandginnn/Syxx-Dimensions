using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Syxx.Kit;
using SyxxUI.Avalonia.ViewModels;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Focused, deterministic, offline coverage for the Prophet pane's two honesty-critical
/// presentation mappings: the DNSSEC tri-state (unsigned / configured / broken / inconclusive)
/// and the resolver consensus evaluation (agree / disagree / insufficient / inconclusive).
///
/// Both mappings are pure functions of VM state (no network, no Avalonia render host), so they
/// can be exercised headless. Every test documents the mutation that turns it red (AGENTS.md
/// Protocol §3: "if you can break the code and the test still passes, the test is broken").
///
/// HONESTY SCOPE: these tests prove the MAPPING is correct (right title, right colour, right
/// status label). They do NOT prove the upstream DNSSEC query or the 12-oracle fan-out returns
/// the right DNSSECStatus / ProphetResult on a real network. That requires a Linux host with
/// open UDP:53 (flagged in the report, not testable in this environment).
/// </summary>
public class ProphetViewModelPresentationTests
{
    // --- Colour constants (must match ProphetViewModel's static brush fields exactly) ---
    private static readonly Color Teal     = Color.Parse("#2dd4bf");
    // Prophet module highlight (owner directive 2026-08-02), parity with macOS ioProphetBlue.
    private static readonly Color ProphetBlue = Color.Parse("#00b4ff");
    private static readonly Color Amber    = Color.Parse("#F2B33D");
    private static readonly Color Red      = Color.Parse("#FF5C7A");
    private static readonly Color TextSec  = Color.Parse("#c9c1e6");

    private static Color BrushColor(IBrush b) => ((SolidColorBrush)b).Color;

    private static ProphetViewModel NewVm(string domain = "example.com") => new() { Domain = domain };

    private static ProphetResult MakeResult(int? medianMs, params (string name, string ip)[] responses)
    {
        var list = responses.Select(r => new ProphetResult.ResolverResponse(r.name, r.ip)).ToList();
        return new ProphetResult(list, new ProphetResult.ConsensusData(list.Count, medianMs));
    }

    // =========================================================================
    //  DNSSEC TRI-STATE PRESENTATION MAPPING (ProphetView.swift:339-371 twin)
    // =========================================================================

    [Fact]
    public void Dnssec_Unsigned_MapsToAmberWarning()
    {
        var vm = NewVm();
        vm.DnssecStatus = DNSSECStatus.Unsigned;

        Assert.Equal("No DNSSEC: zone unsigned", vm.DnssecTitle);
        Assert.Equal("unsigned", vm.DnssecStatusLabel);
        Assert.Equal(Amber, BrushColor(vm.DnssecBrush));
        // MUTATION: change the Unsigned case to TealBrush or RedBrush -> this assertion fails.
        Assert.NotEqual(ProphetBlue, BrushColor(vm.DnssecBrush));
        Assert.NotEqual(Red, BrushColor(vm.DnssecBrush));
    }

    [Fact]
    public void Dnssec_RecordsPresentUnverified_MapsToProphetBlueNeverRedOrValidated()
    {
        var vm = NewVm();
        vm.DnssecStatus = DNSSECStatus.RecordsPresentUnverified;

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1835).
        Assert.Equal("DNSSEC is rightly set for this domain.", vm.DnssecTitle);
        Assert.Equal("configured \u00b7 unverified", vm.DnssecStatusLabel);
        Assert.Equal(ProphetBlue, BrushColor(vm.DnssecBrush));

        // HONESTY (CF-08/A2): the strongest positive state carries the Prophet blue, NEVER red, and
        // the copy must say "unverified", NEVER "validated" / "secure" / "protected".
        // MUTATION: change the RecordsPresentUnverified case to RedBrush -> the ProphetBlue assertion
        // above fails AND the NotEqual(Red) assertion below fails.
        Assert.NotEqual(Red, BrushColor(vm.DnssecBrush));
        Assert.DoesNotContain("validated", vm.DnssecTitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", vm.DnssecTitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protected", vm.DnssecTitle, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unverified", vm.DnssecStatusLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dnssec_SignatureExpired_MapsToRed()
    {
        var vm = NewVm();
        vm.DnssecStatus = DNSSECStatus.SignatureExpiredOrNotYetValid;

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1837). The alarming wording
        // is warranted for a genuinely broken signer, unlike the merely unsigned case.
        Assert.StartsWith("DNSSEC is not rightly set for this domain;", vm.DnssecTitle,
            StringComparison.Ordinal);
        Assert.Equal("broken", vm.DnssecStatusLabel);
        Assert.Equal(Red, BrushColor(vm.DnssecBrush));
        // MUTATION: change the SignatureExpiredOrNotYetValid case to the success colour -> fails.
        Assert.NotEqual(ProphetBlue, BrushColor(vm.DnssecBrush));
    }

    [Fact]
    public void Dnssec_Inconclusive_MapsToTextSecondaryNeverRed()
    {
        // HONESTY: a failed DNSSEC query is Inconclusive (text-secondary), NEVER red. Red reads
        // as an accusation against a domain Syxx simply could not reach.
        var vm = NewVm();
        vm.DnssecStatus = DNSSECStatus.Inconclusive;

        Assert.Equal("DNSSEC status inconclusive", vm.DnssecTitle);
        Assert.Equal("inconclusive", vm.DnssecStatusLabel);
        Assert.Equal(TextSec, BrushColor(vm.DnssecBrush));
        // MUTATION: change the Inconclusive case to RedBrush -> the TextSec assertion fails AND
        // the NotEqual(Red) assertion below fails.
        Assert.NotEqual(Red, BrushColor(vm.DnssecBrush));
    }

    [Fact]
    public void Dnssec_Description_UsesRealKitDescribe_NeverFabricated()
    {
        // The mono status block binds to DNSSECVerifier.Describe (the real Kit description), not a
        // hardcoded string. MUTATION: replace DnssecDescription with a hardcoded "secure" -> the
        // DoesNotContain("secure") assertion fails.
        var vm = NewVm();
        vm.DnssecStatus = DNSSECStatus.RecordsPresentUnverified;
        Assert.Equal(
            DNSSECVerifier.Describe(DNSSECStatus.RecordsPresentUnverified),
            vm.DnssecDescription);
        Assert.DoesNotContain("secure", vm.DnssecDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dnssec_NullStatus_HasNoResultAndEmptyPresentation()
    {
        var vm = NewVm();
        Assert.False(vm.HasDnssecResult);
        Assert.Equal("", vm.DnssecTitle);
        Assert.Equal("", vm.DnssecStatusLabel);
        Assert.Equal("", vm.DnssecDescription);
    }

    // =========================================================================
    //  CONSENSUS EVALUATION (ProphetView.swift:127-159 twin)
    //  Tri-state: agree / disagree / insufficient / inconclusive, computed from actual IPs.
    // =========================================================================

    [Fact]
    public void Consensus_AllAgree_MapsToTealNoTampering()
    {
        var vm = NewVm("example.com");
        vm.ProphetConsensusResult = MakeResult(42,
            ("Local (192.168.1.1)", "93.184.216.34"),
            ("Cloudflare (1.1.1.1)", "93.184.216.34"),
            ("Google (8.8.8.8)", "93.184.216.34"),
            ("Quad9 (9.9.9.9)", "93.184.216.34"),
            ("OpenDNS (208.67.222.222)", "93.184.216.34"),
            ("AdGuard (94.140.14.14)", "93.184.216.34"),
            ("NextDNS (45.90.28.0)", "93.184.216.34"),
            ("Control D (76.76.2.0)", "93.184.216.34"),
            ("Comodo (8.26.56.26)", "93.184.216.34"),
            ("Level3 (4.2.2.1)", "93.184.216.34"),
            ("Verisign (64.6.64.6)", "93.184.216.34"),
            ("DNS.WATCH (84.200.69.80)", "93.184.216.34"));

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1827), served by the
        // shared Kit rather than a Linux-local copy.
        Assert.Equal("No tampering doth abide;", vm.ConsensusHeadlinePrimary);
        Assert.Equal("the 12 resolvers are of one accord.", vm.ConsensusHeadlineSecondary);
        Assert.True(vm.HasConsensusHeadlineSecondary);
        Assert.Equal("12 / 12 agree \u00b7 42ms median", vm.ConsensusFraction);
        Assert.Equal(ProphetBlue, BrushColor(vm.ConsensusBrush));
        // MUTATION: change the Agree case to AmberBrush -> the ProphetBlue assertion fails.
        Assert.NotEqual(Amber, BrushColor(vm.ConsensusBrush));
    }

    [Fact]
    public void Consensus_AllUnreachable_MapsToInconclusiveTextSecondary()
    {
        var vm = NewVm("example.com");
        vm.ProphetConsensusResult = MakeResult(null,
            ("Local (192.168.1.1)", "[unreachable]"),
            ("Cloudflare (1.1.1.1)", "[unreachable]"),
            ("Google (8.8.8.8)", "[unreachable]"),
            ("Quad9 (9.9.9.9)", "[unreachable]"),
            ("OpenDNS (208.67.222.222)", "[unreachable]"),
            ("AdGuard (94.140.14.14)", "[unreachable]"),
            ("NextDNS (45.90.28.0)", "[unreachable]"),
            ("Control D (76.76.2.0)", "[unreachable]"),
            ("Comodo (8.26.56.26)", "[unreachable]"),
            ("Level3 (4.2.2.1)", "[unreachable]"),
            ("Verisign (64.6.64.6)", "[unreachable]"),
            ("DNS.WATCH (84.200.69.80)", "[unreachable]"));

        Assert.Equal("Inconclusive: no reachable resolver", vm.ConsensusHeadlinePrimary);
        // An unmeasured run never borrows the lore voice of either conclusion.
        Assert.False(vm.HasConsensusHeadlineSecondary);
        // B231: an unmeasured latency reads "not measured", never "0ms" and never an em-dash
        // (banned in user-facing text per AGENTS.md).
        Assert.Equal("0 / 0 reachable \u00b7 not measured", vm.ConsensusFraction);
        Assert.Equal(TextSec, BrushColor(vm.ConsensusBrush));
        // MUTATION: change the Inconclusive case to RedBrush -> the TextSec assertion fails.
        Assert.NotEqual(Red, BrushColor(vm.ConsensusBrush));
    }

    [Fact]
    public void Consensus_SingleReachable_MapsToInsufficient()
    {
        var vm = NewVm("example.com");
        vm.ProphetConsensusResult = MakeResult(15,
            ("Local (192.168.1.1)", "93.184.216.34"),
            ("Cloudflare (1.1.1.1)", "[unreachable]"),
            ("Google (8.8.8.8)", "[unreachable]"),
            ("Quad9 (9.9.9.9)", "[unreachable]"),
            ("OpenDNS (208.67.222.222)", "[unreachable]"),
            ("AdGuard (94.140.14.14)", "[unreachable]"),
            ("NextDNS (45.90.28.0)", "[unreachable]"),
            ("Control D (76.76.2.0)", "[unreachable]"),
            ("Comodo (8.26.56.26)", "[unreachable]"),
            ("Level3 (4.2.2.1)", "[unreachable]"),
            ("Verisign (64.6.64.6)", "[unreachable]"),
            ("DNS.WATCH (84.200.69.80)", "[unreachable]"));

        Assert.Equal("Insufficient: only 1 resolver reachable", vm.ConsensusHeadlinePrimary);
        Assert.False(vm.HasConsensusHeadlineSecondary);
        Assert.Equal("1 / 1 (single vantage) \u00b7 15ms median", vm.ConsensusFraction);
        Assert.Equal(TextSec, BrushColor(vm.ConsensusBrush));
    }

    [Fact]
    public void Consensus_ResolversDisagree_MapsToAmberPossibleTampering()
    {
        var vm = NewVm("example.com");
        vm.ProphetConsensusResult = MakeResult(30,
            ("Local (192.168.1.1)", "93.184.216.34"),
            ("Cloudflare (1.1.1.1)", "93.184.216.34"),
            ("Google (8.8.8.8)", "10.0.0.5"),
            ("Quad9 (9.9.9.9)", "93.184.216.34"),
            ("OpenDNS (208.67.222.222)", "[unreachable]"),
            ("AdGuard (94.140.14.14)", "[unreachable]"),
            ("NextDNS (45.90.28.0)", "[unreachable]"),
            ("Control D (76.76.2.0)", "[unreachable]"),
            ("Comodo (8.26.56.26)", "[unreachable]"),
            ("Level3 (4.2.2.1)", "[unreachable]"),
            ("Verisign (64.6.64.6)", "[unreachable]"),
            ("DNS.WATCH (84.200.69.80)", "[unreachable]"));

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1829).
        Assert.Equal("Tampering doth yet abide;", vm.ConsensusHeadlinePrimary);
        Assert.Equal("the twelve resolvers are not of one accord.", vm.ConsensusHeadlineSecondary);
        // 4 reachable (Local, Cloudflare, Google, Quad9), 2 unique IPs -> "0 / 4 agree"
        Assert.Equal("0 / 4 agree \u00b7 30ms median", vm.ConsensusFraction);
        Assert.Equal(Amber, BrushColor(vm.ConsensusBrush));
        // MUTATION: change the Disagree case to the success colour -> the Amber assert fails.
        Assert.NotEqual(ProphetBlue, BrushColor(vm.ConsensusBrush));
    }

    [Fact]
    public void Consensus_NullResult_HasNoResultAndShowsEmptyState()
    {
        var vm = NewVm();
        Assert.False(vm.HasConsensusResult);
        Assert.Empty(vm.ResolverResponses);
        Assert.Equal("", vm.ConsensusHeadlinePrimary);
        Assert.False(vm.HasConsensusHeadlineSecondary);
    }

    [Fact]
    public void Consensus_B231_NullLatencyRendersAsNotMeasuredNeverZeroMs()
    {
        // B231: an unmeasured latency reads "not measured". A fabricated "0ms" would read as a
        // perfect measurement of something never measured, and the em-dash the Kit previously
        // emitted here is banned in user-facing text (AGENTS.md).
        var vm = NewVm("example.com");
        vm.ProphetConsensusResult = MakeResult(null,
            ("Local (192.168.1.1)", "93.184.216.34"),
            ("Cloudflare (1.1.1.1)", "93.184.216.34"),
            ("Google (8.8.8.8)", "93.184.216.34"),
            ("Quad9 (9.9.9.9)", "93.184.216.34"),
            ("OpenDNS (208.67.222.222)", "93.184.216.34"),
            ("AdGuard (94.140.14.14)", "93.184.216.34"),
            ("NextDNS (45.90.28.0)", "93.184.216.34"),
            ("Control D (76.76.2.0)", "93.184.216.34"),
            ("Comodo (8.26.56.26)", "93.184.216.34"),
            ("Level3 (4.2.2.1)", "93.184.216.34"),
            ("Verisign (64.6.64.6)", "93.184.216.34"),
            ("DNS.WATCH (84.200.69.80)", "93.184.216.34"));

        Assert.Contains("not measured", vm.ConsensusFraction, StringComparison.Ordinal);
        Assert.DoesNotContain("0ms", vm.ConsensusFraction, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2014", vm.ConsensusFraction, StringComparison.Ordinal);
    }
}
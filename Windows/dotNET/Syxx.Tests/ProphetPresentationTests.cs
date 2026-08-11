using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Focused, deterministic, offline tests for the shared <see cref="ProphetPresentation"/> Kit
/// helper (parity twin of macOS ProphetView.evaluateConsensus + prophetConsensusLabel +
/// dnssecPresentation, ProphetView.swift:127-211, 326-371). The view-layer copy lives here in
/// the Kit so both Windows (WinUI) and Linux (Avalonia) consume one source of truth and the
/// tri-state mapping is testable without a UI host.
///
/// HONESTY assertions (binding, per AGENTS.md Honesty Protocol):
/// - RecordsPresentUnverified must NEVER say "validated"/"secure"/"protected".
/// - Inconclusive must NEVER be red (red reads as an accusation against a domain Syxx could not
///   reach); it is IoTextSecondaryBrush.
/// - Consensus is computed from actual returned IPs, not resolver count alone.
/// - An unmeasured median latency renders as "not measured", never "0ms" (B231).
///
/// Every test carries an explicit failure case (Protocol §3). The mutation that turns each
/// assertion red is documented in the comment above the assertion.
/// </summary>
public class ProphetPresentationTests
{
    // ── Consensus evaluation (EvaluateConsensus) ──────────────────────────────────

    [Fact]
    public void Consensus_AllUnreachable_IsInconclusive_NeverAgree()
    {
        var responses = new List<ProphetResult.ResolverResponse>
        {
            new("Local (1.1.1.1)", "[unreachable]"),
            new("Google (8.8.8.8)", "[unreachable]"),
        };
        var result = ProphetPresentation.EvaluateConsensus(responses);

        Assert.Equal(ProphetPresentation.ResolverConsensus.Inconclusive, result.State);
        Assert.Equal(0, result.ActiveCount);
        // Failure case: if the filter dropped "[unreachable]" instead of keeping it, an
        // implementation that counted all rows would report ActiveCount=2 and this asserts red.
        Assert.Empty(result.UniqueIPs);
    }

    [Fact]
    public void Consensus_SingleReachable_IsInsufficient_NeverAgree()
    {
        // Need >= 2 reachable for "agree"; one oracle alone cannot constitute consensus.
        var responses = new List<ProphetResult.ResolverResponse>
        {
            new("Local (1.1.1.1)", "140.82.114.3"),
            new("Google (8.8.8.8)", "[unreachable]"),
        };
        var result = ProphetPresentation.EvaluateConsensus(responses);

        Assert.Equal(ProphetPresentation.ResolverConsensus.Insufficient, result.State);
        Assert.Equal(1, result.ActiveCount);
        // Failure case: lowering the consensus threshold to 1 would turn this into Agree and the
        // Inconclusive/Insufficient guard reds.
        Assert.NotEqual(ProphetPresentation.ResolverConsensus.Agree, result.State);
    }

    [Fact]
    public void Consensus_TwoReachableSameIP_IsAgree()
    {
        var responses = new List<ProphetResult.ResolverResponse>
        {
            new("Local (1.1.1.1)", "140.82.114.3"),
            new("Google (8.8.8.8)", "140.82.114.3"),
        };
        var result = ProphetPresentation.EvaluateConsensus(responses);

        Assert.Equal(ProphetPresentation.ResolverConsensus.Agree, result.State);
        Assert.Equal(2, result.ActiveCount);
        Assert.Single(result.UniqueIPs);
    }

    [Fact]
    public void Consensus_ReachableDifferentIPs_IsDisagree()
    {
        var responses = new List<ProphetResult.ResolverResponse>
        {
            new("Local (1.1.1.1)", "140.82.114.3"),
            new("Google (8.8.8.8)", "192.0.2.1"),
            new("Cloudflare", "[unreachable]"),
        };
        var result = ProphetPresentation.EvaluateConsensus(responses);

        Assert.Equal(ProphetPresentation.ResolverConsensus.Disagree, result.State);
        Assert.Equal(2, result.ActiveCount);
        // Failure case: a comparison that ignored the IP values and only checked counts would
        // report Agree here; the distinct-IP assertion catches that.
        Assert.Equal(2, result.UniqueIPs.Count);
    }

    // ── Consensus presentation (BuildConsensusLabel) ───────────────────────────────

    [Fact]
    public void ConsensusLabel_Agree_IsProphetBlue_NeverTextSecondary()
    {
        var consensus = new ProphetPresentation.ConsensusResult(
            ProphetPresentation.ResolverConsensus.Agree, 3, new[] { "140.82.114.3" });
        var pres = ProphetPresentation.BuildConsensusLabel(consensus, "github.com", 42);

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1827).
        Assert.Equal("No tampering doth abide;", pres.HeadlinePrimary);
        Assert.Equal("the 12 resolvers are of one accord.", pres.HeadlineSecondary);
        Assert.Equal("IoProphetBlueBrush", pres.ColorKey);
        Assert.Contains("42ms median", pres.AgreementFraction);
        // Failure case: swapping the Agree colour to IoTextSecondaryBrush reds this NotEqual.
        Assert.NotEqual("IoTextSecondaryBrush", pres.ColorKey);
    }

    [Fact]
    public void ConsensusLabel_Disagree_IsWarningAmber_NeverProphetBlue()
    {
        var consensus = new ProphetPresentation.ConsensusResult(
            ProphetPresentation.ResolverConsensus.Disagree, 3,
            new[] { "140.82.114.3", "192.0.2.1" });
        var pres = ProphetPresentation.BuildConsensusLabel(consensus, "github.com", null);

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1829).
        Assert.Equal("Tampering doth yet abide;", pres.HeadlinePrimary);
        Assert.Equal("the twelve resolvers are not of one accord.", pres.HeadlineSecondary);
        Assert.Equal("IoWarningAmberBrush", pres.ColorKey);
        // B231: an unmeasured latency must read "not measured", never a fabricated "0ms".
        Assert.Contains("not measured", pres.AgreementFraction);
        Assert.DoesNotContain("0ms", pres.AgreementFraction);
        // Failure case: a "disagree is the success colour" inversion reds this NotEqual.
        Assert.NotEqual("IoProphetBlueBrush", pres.ColorKey);
    }

    [Fact]
    public void ConsensusLabel_Inconclusive_IsTextSecondary_NeverAccusatoryRed()
    {
        var consensus = new ProphetPresentation.ConsensusResult(
            ProphetPresentation.ResolverConsensus.Inconclusive, 0, Array.Empty<string>());
        var pres = ProphetPresentation.BuildConsensusLabel(consensus, "github.com", null);

        Assert.Equal("IoTextSecondaryBrush", pres.ColorKey);
        // Failure case: a red colour for Inconclusive reads as an accusation against a domain the
        // probe could not reach; this NotEqual catches that regression.
        Assert.NotEqual("IoCriticalRedBrush", pres.ColorKey);
    }

    [Theory]
    [InlineData(ProphetPresentation.ResolverConsensus.Inconclusive, 0)]
    [InlineData(ProphetPresentation.ResolverConsensus.Insufficient, 1)]
    public void ConsensusLabel_UnmeasuredStates_NeverBorrowTheLoreVoice(
        ProphetPresentation.ResolverConsensus state, int activeCount)
    {
        var consensus = new ProphetPresentation.ConsensusResult(
            state, activeCount, Array.Empty<string>());
        var pres = ProphetPresentation.BuildConsensusLabel(consensus, "github.com", null);

        // HONESTY: an unmeasured or under-measured run must not read as either the success or the
        // failure conclusion. Run 2 is the lore run, so it must be absent, and run 1 must not carry
        // either lore sentence.
        Assert.Null(pres.HeadlineSecondary);
        Assert.DoesNotContain("doth", pres.HeadlinePrimary);
        // Failure case: giving Inconclusive/Insufficient a lore headline reds all three asserts.
    }

    // ── DNSSEC presentation (BuildDnssecPresentation) ──────────────────────────────

    [Fact]
    public void Dnssec_Unsigned_IsWarningAmber_NeverValidated()
    {
        var pres = ProphetPresentation.BuildDnssecPresentation(DNSSECStatus.Unsigned, "example.com");

        Assert.Equal("IoWarningAmberBrush", pres.ColorKey);
        Assert.Equal("unsigned", pres.StatusLabel);
        // HONESTY: the strongest positive state is never "validated"/"secure"/"protected".
        Assert.DoesNotContain("validated", pres.Title);
        Assert.DoesNotContain("secure", pres.Title);
        Assert.DoesNotContain("protected", pres.Title);
    }

    [Fact]
    public void Dnssec_RecordsPresentUnverified_IsProphetBlue_ButSaysUnverified()
    {
        var pres = ProphetPresentation.BuildDnssecPresentation(
            DNSSECStatus.RecordsPresentUnverified, "cloudflare.com");

        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1835).
        Assert.Equal("DNSSEC is rightly set for this domain.", pres.Title);
        // Strongest positive state carries the Prophet blue, in parity with the agreeing consensus
        // card, BUT the copy must still say "unverified".
        Assert.Equal("IoProphetBlueBrush", pres.ColorKey);
        Assert.Contains("unverified", pres.StatusLabel);
        // FATAL: must NEVER claim full chain validation (CF-08/A2 is not done here).
        Assert.DoesNotContain("validated", pres.Title);
        Assert.DoesNotContain("secure", pres.Title);
        Assert.DoesNotContain("protected", pres.Title);
        Assert.Contains("not performed", pres.Detail);
        // Failure case: flipping the colour to IoWarningAmberBrush reds this Equal; flipping the
        // copy to "validated" reds the DoesNotContain above.
    }

    [Fact]
    public void Dnssec_SignatureExpiredOrNotYetValid_IsCriticalRed()
    {
        var pres = ProphetPresentation.BuildDnssecPresentation(
            DNSSECStatus.SignatureExpiredOrNotYetValid, "example.com");

        Assert.Equal("IoCriticalRedBrush", pres.ColorKey);
        Assert.Equal("broken", pres.StatusLabel);
        // Owner lore, verbatim from the macOS gold standard (MEMORY.md:1837). The alarming wording
        // is warranted for a genuinely broken signer, unlike the merely unsigned case.
        Assert.StartsWith("DNSSEC is not rightly set for this domain;", pres.Title,
            StringComparison.Ordinal);
        Assert.Contains("stale-signature", pres.Detail);
        // Failure case: rendering this as amber would soften a genuinely broken signer; reds this.
        Assert.NotEqual("IoWarningAmberBrush", pres.ColorKey);
    }

    [Fact]
    public void Dnssec_Inconclusive_IsTextSecondary_NeverAccusatoryRed()
    {
        var pres = ProphetPresentation.BuildDnssecPresentation(DNSSECStatus.Inconclusive, "example.com");

        Assert.Equal("IoTextSecondaryBrush", pres.ColorKey);
        Assert.Equal("inconclusive", pres.StatusLabel);
        // HONESTY (E144 parity): a failed DNSSEC query is NOT an accusation. Red reads as
        // accusation; this NotEqual catches a red-softening inversion.
        Assert.NotEqual("IoCriticalRedBrush", pres.ColorKey);
        Assert.DoesNotContain("validated", pres.Title);
        Assert.DoesNotContain("secure", pres.Title);
    }

    [Fact]
    public void Dnssec_EmptyDomain_RendersAsThisDomain_NeverBareBraces()
    {
        // A null/empty domain must substitute "this domain" (ProphetPresentation.cs:141), never
        // interpolate to a leading-space-only " returned no..." with no subject.
        var pres = ProphetPresentation.BuildDnssecPresentation(DNSSECStatus.Unsigned, "");

        Assert.StartsWith("this domain", pres.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("{}", pres.Detail);
        // Failure case: removing the `display` substitution guard would interpolate the empty
        // domain to a leading space; this Assert.False reds.
        Assert.False(pres.Detail.StartsWith(" ", StringComparison.Ordinal),
            "empty domain interpolated to a leading-space subject; the 'this domain' guard is missing");
    }

    [Theory]
    [InlineData(DNSSECStatus.Unsigned)]
    [InlineData(DNSSECStatus.RecordsPresentUnverified)]
    [InlineData(DNSSECStatus.SignatureExpiredOrNotYetValid)]
    [InlineData(DNSSECStatus.Inconclusive)]
    public void Dnssec_AllStates_NeverEmitBannedWord(DNSSECStatus status)
    {
        var pres = ProphetPresentation.BuildDnssecPresentation(status, "example.com");
        // AGENTS.md: the banned word must never appear in user-facing text.
        Assert.DoesNotContain("verdict", pres.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verdict", pres.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verdict", pres.StatusLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DNSSECStatus.Unsigned)]
    [InlineData(DNSSECStatus.RecordsPresentUnverified)]
    [InlineData(DNSSECStatus.SignatureExpiredOrNotYetValid)]
    [InlineData(DNSSECStatus.Inconclusive)]
    public void Dnssec_AllStates_NoEmDashesInUserFacingText(DNSSECStatus status)
    {
        // AGENTS.md / MEMORY.md: no em-dashes (U+2014) in user-facing output text.
        var pres = ProphetPresentation.BuildDnssecPresentation(status, "example.com");
        Assert.DoesNotContain("\u2014", pres.Title);
        Assert.DoesNotContain("\u2014", pres.Detail);
        Assert.DoesNotContain("\u2014", pres.StatusLabel);
    }

    [Theory]
    [InlineData(ProphetPresentation.ResolverConsensus.Inconclusive, 0)]
    [InlineData(ProphetPresentation.ResolverConsensus.Insufficient, 1)]
    [InlineData(ProphetPresentation.ResolverConsensus.Agree, 3)]
    [InlineData(ProphetPresentation.ResolverConsensus.Disagree, 3)]
    public void ConsensusLabel_AllStates_NoEmDashes_AndNoBannedWord(
        ProphetPresentation.ResolverConsensus state, int activeCount)
    {
        // This is the guard that was missing: the unmeasured-latency fragment previously rendered
        // as a bare em-dash, which AGENTS.md bans in user-facing text. Both the measured and the
        // unmeasured path are checked, because only the unmeasured one carried the defect.
        foreach (int? latency in new int?[] { null, 42 })
        {
            var consensus = new ProphetPresentation.ConsensusResult(
                state, activeCount, new[] { "140.82.114.3", "192.0.2.1" });
            var pres = ProphetPresentation.BuildConsensusLabel(consensus, "github.com", latency);

            foreach (var text in new[]
                     { pres.HeadlinePrimary, pres.HeadlineSecondary ?? "", pres.Detail, pres.AgreementFraction })
            {
                Assert.DoesNotContain("\u2014", text);
                Assert.DoesNotContain("verdict", text, StringComparison.OrdinalIgnoreCase);
            }
        }
        // Failure case: restoring the em-dash latency fragment reds every unmeasured iteration.
    }
}
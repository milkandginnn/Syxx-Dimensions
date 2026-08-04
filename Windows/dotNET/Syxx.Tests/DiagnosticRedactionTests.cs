using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Guards the last line of defence between a diagnostic and a privacy incident.
///
/// THE STAKE. This product's promise is that your DNS queries stay yours. A queried domain reaching
/// the issue tracker is not a bug report — it is the exact harm the product exists to prevent, and
/// it arrives disguised as ordinary telemetry, so nothing downstream would flag it. Resolver IPs
/// identify an ISP or an employer; home paths carry the account name.
///
/// WHY THESE TESTS EXIST AT THE KIT LEVEL. Redaction is consumed by three .NET surfaces (WinUI
/// client, CLI, Avalonia client). Implemented per-sink it would drift, and the drift would be
/// silent — the platform with the weaker pattern leaks and every test still passes. It also cannot
/// be tested inside SyxxUI at all: WinUI does not build on a non-Windows host, so redaction living
/// there would be redaction nobody can run.
///
/// Each test names the mutation that must turn it red.
/// </summary>
public class DiagnosticRedactionTests
{
    // ── The things that must never survive ──────────────────────────────────────────────────────

    /// Failure case: delete the IPv4 pattern → the address survives → red.
    [Fact]
    public void StripsIPv4Addresses()
    {
        var o = DiagnosticRedaction.Apply("query to 192.168.1.77 timed out");
        Assert.DoesNotContain("192.168.1.77", o);
        Assert.Contains("<ip>", o);
    }

    /// The highest-severity leak of the set: a queried domain is a browsing history.
    /// Failure case: delete the domain pattern → the hostname survives → red.
    [Fact]
    public void StripsDomainNames()
    {
        var o = DiagnosticRedaction.Apply("resolution failed for private-intranet.example.com");
        Assert.DoesNotContain("private-intranet", o);
        Assert.Contains("<domain>", o);
    }

    /// Failure case: delete the IPv6 pattern → red.
    [Fact]
    public void StripsIPv6Addresses()
    {
        var o = DiagnosticRedaction.Apply("upstream 2001:4860:4860::8888 refused");
        Assert.DoesNotContain("2001:4860", o);
    }

    /// Both platforms' home paths carry the account name. Failure case: delete either path
    /// pattern → the username survives on that platform → red.
    [Fact]
    public void StripsHomeDirectoryPathsOnBothPlatforms()
    {
        var win = DiagnosticRedaction.Apply(@"could not write C:\Users\jane\.syxx\capture.pcap");
        Assert.DoesNotContain("jane", win);

        var nix = DiagnosticRedaction.Apply("could not write /home/jane/.syxx/capture.pcap");
        Assert.DoesNotContain("jane", nix);
    }

    // ── The thing that must survive ─────────────────────────────────────────────────────────────

    /// Redaction that destroys diagnostic value gets routed around, and a sink nobody uses protects
    /// nobody. The machine-readable tokens that make an issue actionable must come through intact.
    /// Failure case: broaden a pattern until it eats these → red.
    [Fact]
    public void KeepsNonIdentifyingDiagnosticText()
    {
        var o = DiagnosticRedaction.Apply(
            "probe_deadline after 2500ms, attempt 2 of 2, transport=udp53");
        Assert.Contains("probe_deadline", o);
        Assert.Contains("attempt 2 of 2", o);
        Assert.Contains("udp53", o);
    }

    /// Error-class and enum names must survive — they are the fingerprint inputs.
    [Fact]
    public void KeepsErrorClassTokens()
    {
        var o = DiagnosticRedaction.Apply("engine.no_determination kind=DuplicateSectionInBundle");
        Assert.Contains("DuplicateSectionInBundle", o);
    }

    // ── Shape ───────────────────────────────────────────────────────────────────────────────────

    /// A mixed line is the realistic case: strip the identifiers, keep the diagnosis.
    [Fact]
    public void RedactsIdentifiersWhileKeepingTheDiagnosis()
    {
        var o = DiagnosticRedaction.Apply(
            "probe_deadline resolving shop.internal.corp via 10.0.0.53 after 2500ms");
        Assert.DoesNotContain("shop.internal.corp", o);
        Assert.DoesNotContain("10.0.0.53", o);
        Assert.Contains("probe_deadline", o);
        Assert.Contains("2500ms", o);
    }

    /// Null and empty must not throw: this runs inside a catch block, and a redactor that throws
    /// while reporting an error destroys the report it was called to protect.
    [Fact]
    public void HandlesNullAndEmptyWithoutThrowing()
    {
        Assert.Equal(string.Empty, DiagnosticRedaction.Apply(null));
        Assert.Equal(string.Empty, DiagnosticRedaction.Apply(string.Empty));
    }

    /// Idempotent: redacting twice must not corrupt the placeholders, since a message can pass
    /// through both a sink and a breadcrumb path.
    [Fact]
    public void IsIdempotent()
    {
        var once = DiagnosticRedaction.Apply("failed for example.com at 1.1.1.1");
        var twice = DiagnosticRedaction.Apply(once);
        Assert.Equal(once, twice);
    }
}

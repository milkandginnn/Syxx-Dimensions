using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Windows twin of macOS <c>TelemetryRedactionTests.swift</c>. Guards the rule that a queried DNS
/// name never leaves the device as telemetry.
///
/// WHY THE TWIN MATTERS MORE THAN THE ORIGINAL. macOS had this guard and Windows did not, which is
/// the shape of every parity defect in this codebase: the protection exists, the suite is green,
/// and the platform nobody wrote a test for is the one that leaks. A privacy regression here would
/// arrive looking like ordinary telemetry — no crash, no failed assertion, just browsing history
/// accumulating in a span store. There is no downstream check that would catch it.
///
/// WHAT IS AT STAKE. A DNS query stream IS a browsing history. This product's entire promise is
/// that yours stays yours; exporting it to diagnose the tool that protects it would invert the
/// product. Resolver IPs are nearly as bad — a non-public resolver address identifies an ISP, an
/// employer, or a household.
/// </summary>
public class TelemetryRedactionTests
{
    private static string? Attr(System.Collections.Generic.List<(string, object?)> attrs, string key)
        => attrs.FirstOrDefault(a => a.Item1 == key).Item2?.ToString();

    // ── The name must never appear ──────────────────────────────────────────────────────────────

    /// The core guarantee. Failure case: emit the domain under any key → red.
    [Fact]
    public void UserDomainIsNeverEmittedInAnyAttributeValue()
    {
        var attrs = TelemetryRedaction.ForDomain("private-intranet.example.com");

        Assert.DoesNotContain(attrs, a =>
            a.Item2?.ToString()?.Contains("private-intranet", System.StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(attrs, a =>
            a.Item2?.ToString()?.Contains("example.com", System.StringComparison.OrdinalIgnoreCase) == true);
    }

    /// A user domain is classed, not named — the class is what makes the span diagnosable at all.
    /// Failure case: drop the classification and emit nothing → the span loses its meaning → red.
    [Fact]
    public void UserDomainIsClassifiedRatherThanNamed()
    {
        var attrs = TelemetryRedaction.ForDomain("shop.internal.corp");
        var cls = Attr(attrs, "dns.target_class");

        // Token value is `userSupplied`, matching macOS SyxxDiagnostics.swift:176 exactly.
        // (The earlier telemetry spec proposed `user_supplied`; the shipped contract on BOTH
        // platforms is camelCase, and the shipped contract is what a collector query must match.)
        Assert.Equal("userSupplied", cls);
    }

    /// Shape metrics survive: they carry the malformed-name and long-name diagnostics without
    /// carrying the name. Failure case: remove them → the redaction becomes useless for debugging
    /// and engineers route around it → red.
    [Fact]
    public void ShapeMetricsSurviveRedaction()
    {
        var attrs = TelemetryRedaction.ForDomain("a.b.example.com");

        Assert.NotNull(Attr(attrs, "dns.name.labels"));
        Assert.NotNull(Attr(attrs, "dns.name.length"));
    }

    // ── Syxx-owned constants are safe to name ───────────────────────────────────────────────────

    /// Domains Syxx itself chose say nothing about the user, so naming them exactly is safe AND
    /// useful — it is how a canary failure is told apart from a user-query failure.
    /// Failure case: redact these too → canary diagnostics become unreadable → red.
    [Fact]
    public void SyxxOwnedDomainsAreNamedExactly()
    {
        Assert.True(TelemetryRedaction.IsSyxxOwned("github.com"));
        var attrs = TelemetryRedaction.ForDomain("github.com");
        // `syxxOwned`, matching macOS SyxxDiagnostics.swift:175.
        Assert.Equal("syxxOwned", Attr(attrs, "dns.target_class"));
    }

    /// The negative half, so the allowlist cannot be widened into a leak.
    [Fact]
    public void ArbitraryDomainsAreNotTreatedAsSyxxOwned()
    {
        Assert.False(TelemetryRedaction.IsSyxxOwned("evil.example"));
        Assert.False(TelemetryRedaction.IsSyxxOwned("github.com.attacker.example"));
    }

    // ── Resolver addresses ──────────────────────────────────────────────────────────────────────

    /// A known public resolver may be named: it identifies a service, not a person, and which
    /// resolver was used is a primary diagnostic.
    [Fact]
    public void KnownPublicResolversAreNamed()
    {
        var attrs = TelemetryRedaction.ForResolver("1.1.1.1");
        Assert.Contains(attrs, a => a.Item2?.ToString() == "1.1.1.1");
    }

    /// A private or ISP resolver identifies an employer or a household and must be classed only.
    /// Failure case: emit it verbatim → red.
    [Fact]
    public void NonPublicResolverAddressesAreClassifiedNotNamed()
    {
        var attrs = TelemetryRedaction.ForResolver("192.168.1.77");

        Assert.DoesNotContain(attrs, a => a.Item2?.ToString() == "192.168.1.77");
        Assert.NotNull(Attr(attrs, "dns.server_class"));
    }

    // ── Parity with macOS ───────────────────────────────────────────────────────────────────────

    /// The attribute KEYS must match the macOS twin exactly. A collector query written against one
    /// platform has to work on the other; divergent keys mean a dashboard that silently shows one
    /// platform's data and calls it the fleet.
    [Fact]
    public void AttributeKeysMatchTheMacOSTwin()
    {
        var keys = TelemetryRedaction.ForDomain("shop.internal.corp").Select(a => a.Item1).ToList();

        Assert.Contains("dns.target_class", keys);
        Assert.Contains("dns.name.labels", keys);
        Assert.Contains("dns.name.length", keys);
        // The name key from the pre-redaction schema must be gone on both platforms.
        Assert.DoesNotContain("dns.hostname", keys);
    }
}

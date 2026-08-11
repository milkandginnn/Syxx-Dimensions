using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Structural safeguard for the Witness testify decision core (DELTAS.md D093 + the 2026-07-22
/// extraction refactor). D093 was exactly this: the drift-population step got silently DROPPED on
/// Windows during hand-duplication of the GUI view-model, undetected because the logic lived only
/// inside SyxxUI (WinUI 3), which no CI job can compile or unit-test (D092/D101). This suite pins the
/// now-single-sourced pure core (<see cref="WitnessTestifyPlanner"/>), which lives in Syxx.Kit (plain
/// net10.0) and therefore DOES run on the Linux CI legs — so a future drop of the drift step fails a
/// test here instead of shipping. Parity twin: macOS Tests/SyxxKitTests/WitnessTestifyPlannerTests.swift.
///
/// Deliberately placed in its OWN file rather than in SyxxKitTests.cs, which a concurrent flaky-test
/// fix (D102) may be editing.
/// </summary>
public class WitnessTestifyPlannerTests
{
    private static ComparisonResult MakeComparison(bool redirected, string[] localRdata, string[] publicRdata)
    {
        List<DNSRecord> Records(string[] rdata)
        {
            var list = new List<DNSRecord>();
            foreach (var r in rdata)
                list.Add(new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = r });
            return list;
        }

        var localRecords = Records(localRdata);
        var publicRecords = Records(publicRdata);
        return new ComparisonResult
        {
            LocalServer = "192.168.1.1",
            LocalRecords = localRecords,
            Result = redirected ? VantageComparisonResult.RedirectionDetected : VantageComparisonResult.Consistent,
            LocalOutcome = new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = localRecords },
            PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
            {
                { "Cloudflare", new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = publicRecords } }
            }
        };
    }

    // D093: redirection detected MUST yield a drift, with the fields derived exactly as the former
    // inlined block did (PreviousAnswer = public rdata, NewAnswer = local rdata).
    [Fact]
    public void DriftIfRedirected_OnRedirection_ProducesDriftWithExpectedFields()
    {
        var comparison = MakeComparison(redirected: true,
            localRdata: new[] { "10.0.0.5" }, publicRdata: new[] { "93.184.216.34" });

        var drift = WitnessTestifyPlanner.DriftIfRedirected("example.com", "192.168.1.1", comparison);

        Assert.NotNull(drift);
        Assert.Equal("example.com", drift!.Domain);
        Assert.Equal("A", drift.QueryType);
        Assert.Equal("93.184.216.34", drift.PreviousAnswer);
        Assert.Equal("10.0.0.5", drift.NewAnswer);
        Assert.Equal("192.168.1.1", drift.Resolver);
    }

    // No redirection => null (caller materializes an empty drift list). The honest, no-false-positive side.
    [Fact]
    public void DriftIfRedirected_NoRedirection_ReturnsNull()
    {
        var comparison = MakeComparison(redirected: false,
            localRdata: new[] { "93.184.216.34" }, publicRdata: new[] { "93.184.216.34" });

        Assert.Null(WitnessTestifyPlanner.DriftIfRedirected("example.com", "192.168.1.1", comparison));
    }

    // Seal() must sign AND carry the drift through in one shot — the exact contract WitnessTestifyAsync
    // now relies on. A real Ed25519 keypair, no network.
    [Fact]
    public void Seal_SignsAndCarriesDrift()
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        var comparison = MakeComparison(redirected: true,
            localRdata: new[] { "10.0.0.5" }, publicRdata: new[] { "93.184.216.34" });

        var outcome = WitnessTestifyPlanner.Seal("example.com", "192.168.1.1", comparison, priv);

        Assert.False(string.IsNullOrEmpty(outcome.Snapshot.Signature));
        Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(outcome.Snapshot));
        Assert.NotNull(outcome.Drift);
        Assert.Equal("10.0.0.5", outcome.Drift!.NewAnswer);
    }

    // An unusable (empty) key must propagate the signer's throw so the caller's existing catch renders
    // the existing "Could not create witness snapshot: …" message — the seal must not swallow it.
    [Fact]
    public void Seal_PropagatesSigningError()
    {
        var comparison = MakeComparison(redirected: false,
            localRdata: new[] { "1.1.1.1" }, publicRdata: new[] { "1.1.1.1" });

        Assert.Throws<WitnessSignerException>(() =>
            WitnessTestifyPlanner.Seal("example.com", "192.168.1.1", comparison, ""));
    }
}

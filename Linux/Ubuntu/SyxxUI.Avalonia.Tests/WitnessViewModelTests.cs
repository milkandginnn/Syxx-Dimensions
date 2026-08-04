using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using Syxx.Kit;
using SyxxUI.Avalonia.ViewModels;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Regression coverage for the three failure branches of WitnessViewModel.SignAsync (DELTAS.md
/// B195 cites this ViewModel as the reference honest-failure-rendering implementation that macOS
/// and Windows were retrofitted to match — this suite is what makes that claim verifiable instead
/// of asserted). Each test drives the real SignCommand through the constructor's injected resolve
/// / sign delegates (see WitnessViewModel's test-seam constructor) so no live network resolve or
/// NSec keypair generation is required.
/// </summary>
public class WitnessViewModelTests
{
    private static ComparisonResult MakeComparison() => new()
    {
        LocalServer = "127.0.0.1",
        IsInconclusive = false,
        IsRedirectionDetected = false
    };

    private static List<DNSRecord> Records(params string[] rdata) =>
        rdata.Select(r => new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = r }).ToList();

    // A live comparison that FLAGS redirection, with the local/public outcomes populated so the shared
    // WitnessTestifyPlanner.DriftIfRedirected can derive real previous/new answers.
    private static ComparisonResult MakeRedirectedComparison()
    {
        var publicRecords = Records("93.184.216.34");
        var localRecords = Records("10.0.0.5");
        return new ComparisonResult
        {
            LocalServer = "192.168.1.1",
            LocalRecords = localRecords,
            IsInconclusive = false,
            IsRedirectionDetected = true,
            LocalOutcome = new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = localRecords },
            PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
            {
                { "Cloudflare", new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = publicRecords } }
            }
        };
    }

    [Fact]
    public async Task SignAsync_NoComparisonResult_SetsNothingToWitnessMessage()
    {
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = null }),
            createSnapshot: (_, _, _) => throw new InvalidOperationException("must not be called when there is no comparison to sign"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(
            "Nothing to witness — the domain did not resolve, so there is no result to seal.",
            vm.SignatureLine);
        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.True(vm.HasResult);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task SignAsync_ResolveThrows_SetsFailureMessage_NotStuckRunning()
    {
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => throw new InvalidOperationException("simulated transport failure"),
            createSnapshot: (_, _, _) => throw new InvalidOperationException("must not be called when resolve throws"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal("Could not resolve to witness: simulated transport failure", vm.SignatureLine);
        Assert.NotEqual("Resolving and sealing…", vm.SignatureLine);
        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.False(vm.IsRunning);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public async Task SignAsync_SigningThrows_SetsFailureMessage()
    {
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
            createSnapshot: (_, _, _) => throw new WitnessSignerException("simulated signer failure"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal("Signing failed: simulated signer failure", vm.SignatureLine);
        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.False(vm.IsRunning);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public async Task SignAsync_RedirectionDetected_PopulatesDriftFromPlanner()
    {
        // D105: a signed testimony against an actively-redirected domain must now carry the same drift
        // the other two clients do — derived through the shared Syxx.Kit WitnessTestifyPlanner. Sign with
        // the real WitnessSigner so the seal genuinely succeeds.
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeRedirectedComparison() }),
            createSnapshot: (d, c, k) => WitnessSigner.CreateSignedSnapshot(d, c, k));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Signed, vm.TestimonyState);
        Assert.NotNull(vm.Drift);
        Assert.Equal("example.com", vm.Drift!.Domain);
        Assert.Equal("A", vm.Drift.QueryType);
        Assert.Equal("93.184.216.34", vm.Drift.PreviousAnswer);
        Assert.Equal("10.0.0.5", vm.Drift.NewAnswer);
        Assert.False(string.IsNullOrEmpty(vm.Drift.Resolver));
    }

    [Fact]
    public async Task SignAsync_NoRedirection_LeavesDriftNull()
    {
        // Honest negative: a seal over a comparison with NO redirection must NOT manufacture a drift.
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
            createSnapshot: (d, c, k) => WitnessSigner.CreateSignedSnapshot(d, c, k));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Signed, vm.TestimonyState);
        Assert.Null(vm.Drift);
    }

    // ── #21 persistence: the identity survives a relaunch and is NEVER minted per-run ─────────────

    [Fact]
    public async Task SignAsync_ReusesTheSameSigningKeyAcrossRelaunch_AndNeverShowsThePrivateKey()
    {
        // Persistence is proven at the ViewModel contract level: a single injected key source (the
        // load-or-create seam production wires to the LinuxSecretStore-backed WitnessKeyStore) is
        // shared across two ViewModel instances — a simulated relaunch. WitnessKeyStore.KeyPath is a
        // hardcoded real per-user data path that is NOT injectable, so the on-disk file is not
        // exercised here; the closure below stands in for it so the test touches no real key file.
        string? persistedKey = null;
        Func<string> keySeam = () => persistedKey ??= WitnessSigner.GenerateKeyPair().PrivateKey;

        var privsSeenBySigner = new List<string>();
        WitnessSnapshot? lastSnapshot = null;
        WitnessSnapshot RecordingSign(string d, ComparisonResult c, string k)
        {
            privsSeenBySigner.Add(k);
            lastSnapshot = WitnessSigner.CreateSignedSnapshot(d, c, k);
            return lastSnapshot;
        }

        // Run 1 — first launch establishes the identity.
        var vm1 = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
            createSnapshot: RecordingSign,
            loadOrCreateSigningKey: keySeam);
        vm1.Domain = "example.com";
        await vm1.SignCommand.ExecuteAsync(null);
        Assert.Equal(WitnessTestimonyState.Signed, vm1.TestimonyState);
        var identity1 = vm1.IdentityLine;
        var snapshot1 = lastSnapshot!;

        // Run 2 — a fresh ViewModel (relaunch) sharing the same persisted key source.
        var vm2 = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
            createSnapshot: RecordingSign,
            loadOrCreateSigningKey: keySeam);
        vm2.Domain = "example.com";
        await vm2.SignCommand.ExecuteAsync(null);
        Assert.Equal(WitnessTestimonyState.Signed, vm2.TestimonyState);
        var snapshot2 = lastSnapshot!;

        // The signer received the IDENTICAL private key on both runs — ephemeral per-run minting is
        // gone. (Against the old GenerateKeyPair()-per-sign code these two would differ.)
        Assert.Equal(2, privsSeenBySigner.Count);
        Assert.Equal(privsSeenBySigner[0], privsSeenBySigner[1]);

        // Same identity → same KeyId → run-2 seal verifies under run-1's public identity.
        Assert.Equal(snapshot1.KeyId, snapshot2.KeyId);
        Assert.Equal(snapshot1.PublicKey, snapshot2.PublicKey);
        Assert.Equal(identity1, vm2.IdentityLine);
        Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(snapshot2));

        // The displayed identity shows the PUBLIC key + key id, and never the raw private key.
        Assert.Contains(snapshot1.PublicKey, identity1, StringComparison.Ordinal);
        Assert.Contains(snapshot1.KeyId, identity1, StringComparison.Ordinal);
        Assert.DoesNotContain(privsSeenBySigner[0], identity1, StringComparison.Ordinal);
        Assert.DoesNotContain(privsSeenBySigner[0], vm1.SignatureLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignAsync_KeyStoreFailure_RendersFailed_NotAnUnsignedSeal()
    {
        // An honest failure to reach the key store must be a Failed outcome — never a silent
        // fall-through to signing with nothing.
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
            createSnapshot: (_, _, _) => throw new InvalidOperationException("must not sign without a key"),
            loadOrCreateSigningKey: () => throw new InvalidOperationException("keyring unavailable"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.False(vm.IsRunning);
        Assert.True(vm.HasResult);
        Assert.False(string.IsNullOrWhiteSpace(vm.SignatureLine));
        Assert.Empty(vm.IdentityLine);
    }

    // ── #22 external verify + #23 render honesty: the 3-tier severity switch ──────────────────────

    private static readonly string GreenHex = "#FF3FD08A";  // opaque #3FD08A
    private static readonly string AmberHex = "#FFF2B33D";
    private static readonly string RedHex = "#FFFF4D6D";

    private static string ColorOf(IBrush brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color.ToString().ToUpperInvariant();

    [Theory]
    [InlineData(SnapshotVerification.Valid, "#FF3FD08A")]
    [InlineData(SnapshotVerification.Expired, "#FFF2B33D")]
    [InlineData(SnapshotVerification.Stale, "#FFF2B33D")]
    [InlineData(SnapshotVerification.FutureDated, "#FFFF4D6D")]
    [InlineData(SnapshotVerification.UntrustedSigner, "#FFFF4D6D")]
    [InlineData(SnapshotVerification.DisplayMismatch, "#FFFF4D6D")]
    [InlineData(SnapshotVerification.InvalidSignature, "#FFFF4D6D")]
    [InlineData(SnapshotVerification.MalformedInput, "#FFFF4D6D")]
    [InlineData(SnapshotVerification.UnsupportedFormat, "#FFFF4D6D")]
    public void ApplyVerification_MapsEachOutcomeToItsSeverityTier(SnapshotVerification result, string expectedHex)
    {
        var vm = NewIdleViewModel();

        vm.ApplyVerification(result);

        Assert.Equal(expectedHex, ColorOf(vm.VerifyBrush));
        Assert.False(string.IsNullOrWhiteSpace(vm.VerifyLine));
    }

    [Fact]
    public void ApplyVerification_GivesEveryOutcomeADistinctMessage()
    {
        // E021: non-Valid states are forensically different and must not collapse into one message.
        var vm = NewIdleViewModel();
        var lines = new List<string>();
        foreach (SnapshotVerification result in Enum.GetValues<SnapshotVerification>())
        {
            vm.ApplyVerification(result);
            lines.Add(vm.VerifyLine);
        }

        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Fact]
    public void VerifyExternal_ValidPastedTestimony_RendersGreen()
    {
        var (_, snap) = SignFresh();
        var vm = NewIdleViewModel();
        vm.ExternalSnapshotJson = JsonSerializer.Serialize(snap);

        vm.VerifyExternalCommand.Execute(null);

        Assert.Equal(GreenHex, ColorOf(vm.VerifyBrush));
        Assert.Contains("valid", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyExternal_ExpiredTestimony_RendersAmber()
    {
        var (_, snap) = SignFresh(expiresAt: DateTime.UtcNow.AddMinutes(-10));
        var vm = NewIdleViewModel();
        vm.ExternalSnapshotJson = JsonSerializer.Serialize(snap);

        vm.VerifyExternalCommand.Execute(null);

        Assert.Equal(AmberHex, ColorOf(vm.VerifyBrush));
        Assert.Contains("Expired", vm.VerifyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyExternal_TamperedSignature_RendersRedInvalidSignature()
    {
        var (_, snap) = SignFresh();
        snap.Signature = FlipFirstBase64Char(snap.Signature);
        var vm = NewIdleViewModel();
        vm.ExternalSnapshotJson = JsonSerializer.Serialize(snap);

        vm.VerifyExternalCommand.Execute(null);

        Assert.Equal(RedHex, ColorOf(vm.VerifyBrush));
        Assert.Contains("INVALID", vm.VerifyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyExternal_TamperedDisplayField_RendersRedDisplayMismatch()
    {
        var (_, snap) = SignFresh();
        snap.TargetDomain = "attacker-swapped.example";  // display mirror rewritten; signed bytes intact
        var vm = NewIdleViewModel();
        vm.ExternalSnapshotJson = JsonSerializer.Serialize(snap);

        vm.VerifyExternalCommand.Execute(null);

        Assert.Equal(RedHex, ColorOf(vm.VerifyBrush));
        Assert.Contains("Display mismatch", vm.VerifyLine, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyExternal_GarbageInput_RendersRedMalformed()
    {
        var vm = NewIdleViewModel();
        vm.ExternalSnapshotJson = "this is not a signed testimony";

        vm.VerifyExternalCommand.Execute(null);

        Assert.Equal(RedHex, ColorOf(vm.VerifyBrush));
        Assert.Contains("malformed", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyExternal_EmptyInput_PromptsWithoutClaimingAVerdict()
    {
        var vm = NewIdleViewModel();
        vm.ExternalSnapshotJson = "   ";

        vm.VerifyExternalCommand.Execute(null);

        // No verdict for no input: a caution-tier prompt, not green/valid and not red/forged.
        Assert.Equal(AmberHex, ColorOf(vm.VerifyBrush));
        Assert.DoesNotContain("valid", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);
    }

    private static WitnessViewModel NewIdleViewModel() => new(
        runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
        createSnapshot: (d, c, k) => WitnessSigner.CreateSignedSnapshot(d, c, k));

    private static (string priv, WitnessSnapshot snap) SignFresh(DateTime? expiresAt = null)
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        var snap = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv, expiresAt);
        return (priv, snap);
    }

    private static string FlipFirstBase64Char(string base64)
    {
        char first = base64[0];
        char replacement = first == 'A' ? 'B' : 'A';
        return replacement + base64.Substring(1);
    }
}

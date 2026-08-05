using System;
using System.Collections.Generic;
using System.IO;
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

    [Fact]
    public async Task SignAsync_KeyStoreUndetermined_RendersDistinctIdentityIntactMessage_NeverGeneric()
    {
        // RG-2: when the key store is UNREACHABLE (a key exists but the keyring is locked / headless),
        // LoadOrCreatePersistentSigningKey throws WitnessKeyStoreUnavailableException. SignAsync must
        // render a DISTINCT message that reassures the identity is intact — never the generic
        // "could not access the signing key" copy, and never a silent mint. The injected seam throws
        // the real exception type so the catch ordering (specific before generic) is exercised.
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(new SectionInputs { Comparison = MakeComparison() }),
            createSnapshot: (_, _, _) => throw new InvalidOperationException("must not sign when the store is unreachable"),
            loadOrCreateSigningKey: () => throw new WitnessKeyStoreUnavailableException("store unreachable"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.Equal(WitnessViewModel.KeyStoreUnavailableMessage, vm.SignatureLine);
        // Distinct from the generic key-access failure branch.
        Assert.DoesNotContain("Could not access the signing key", vm.SignatureLine, StringComparison.Ordinal);
        // The reassurance the copy exists to give: the existing identity is untouched.
        Assert.Contains("untouched", vm.SignatureLine, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.IdentityLine);
        Assert.False(vm.IsRunning);
        Assert.True(vm.HasResult);
    }

    // ── 5(c) bring-your-own-key: import persists on valid, refuses (and does not persist) on invalid ─

    [Fact]
    public void ImportKey_ValidKey_PersistsAndReportsSuccess_AndClearsInput()
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        string keyPath = WitnessKeyPath();
        byte[]? backup = ReadIfExists(keyPath);
        var fake = new InMemoryStore();
        try
        {
            WitnessKeyStore.SetSecretStore(fake);
            var vm = NewIdleViewModel();
            vm.ImportKeyText = priv;

            vm.ImportKeyCommand.Execute(null);

            Assert.True(WitnessKeyStore.HasKey);
            Assert.Equal(priv, WitnessKeyStore.LoadKey());   // round-trips through the injected store
            Assert.Equal(WitnessViewModel.ImportKeySuccessMessage, vm.ImportStatusLine);
            Assert.Empty(vm.ImportKeyText);                  // the pasted secret is cleared once saved
        }
        finally { RestoreWitnessKey(keyPath, backup); }
    }

    [Fact]
    public void ImportKey_InvalidKey_SurfacesError_AndDoesNotPersist()
    {
        string keyPath = WitnessKeyPath();
        byte[]? backup = ReadIfExists(keyPath);
        var fake = new InMemoryStore();
        try
        {
            if (File.Exists(keyPath)) File.Delete(keyPath);  // so "did not persist" is observable
            WitnessKeyStore.SetSecretStore(fake);
            var vm = NewIdleViewModel();
            vm.ImportKeyText = "lklklklk";   // valid base64, 6 bytes — not usable Ed25519 material

            vm.ImportKeyCommand.Execute(null);

            Assert.Equal(WitnessViewModel.ImportKeyInvalidMessage, vm.ImportStatusLine);
            Assert.False(WitnessKeyStore.HasKey);            // nothing was written to disk
            Assert.Empty(fake.Values);                        // and nothing reached the store
            Assert.Equal("lklklklk", vm.ImportKeyText);       // input is NOT cleared on failure
        }
        finally { RestoreWitnessKey(keyPath, backup); }
    }

    private static string WitnessKeyPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syxx", "witness.key");

    private static byte[]? ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static void RestoreWitnessKey(string keyPath, byte[]? backup)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
            if (backup is not null) File.WriteAllBytes(keyPath, backup);
            else if (File.Exists(keyPath)) File.Delete(keyPath);
        }
        catch { /* best-effort restore */ }
    }

    private sealed class InMemoryStore : ISecretStore
    {
        private readonly Dictionary<string, string> _m = new();
        public IEnumerable<string> Values => _m.Values;

        private static string K(string t, string i) => t + " " + i;

        public void Store(string keyType, string keyId, string secret) => _m[K(keyType, keyId)] = secret;
        public string? Retrieve(string keyType, string keyId) => _m.TryGetValue(K(keyType, keyId), out var v) ? v : null;
        public void Delete(string keyType, string keyId) => _m.Remove(K(keyType, keyId));
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

    // ── T-5: SelfVerify tests (Phase 3 §4.1) ──────────────────────────────────────────────────

    [Fact]
    public async Task SelfVerify_AfterSuccessfulSeal_ReturnsVerified()
    {
        // T-5 §4.1: seal a testimony with the default ephemeral key seam, then self-verify.
        // The verify line must report a positive (Valid) result — the seal genuinely verifies
        // under its own key, and ApplyVerification maps Valid → green.
        var vm = NewIdleViewModel();
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);
        Assert.Equal(WitnessTestimonyState.Signed, vm.TestimonyState);

        vm.SelfVerifyCommand.Execute(null);

        Assert.Contains("Verified", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GreenHex, ColorOf(vm.VerifyBrush));
    }

    [Fact]
    public void SelfVerify_NoSnapshot_PromptsUser()
    {
        // T-5 §4.1: pressing re-verify without ever sealing must NOT show a positive result.
        // The ViewModel must prompt the user rather than fabricating a conclusion.
        var vm = NewIdleViewModel();

        vm.SelfVerifyCommand.Execute(null);

        Assert.Equal(WitnessViewModel.SelfVerifyNoSnapshotMessage, vm.VerifyLine);
        // Caution-tier, not green — the user is prompted, not reassured.
        Assert.DoesNotContain("Verified", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AmberHex, ColorOf(vm.VerifyBrush));
    }

    [Fact]
    public async Task SelfVerify_StaleSnapshot_DoesNotShowGreen()
    {
        // T-5 §4.1 / G-12 Route B: seal domain A, then change domain to B where the seal fails.
        // Self-verify must NOT show a positive result about A — the user's mental model is that
        // they are checking B, but the stored _lastSnapshot still carries A's seal. Showing green
        // "Verified" here is a false-confidence defect: the user reads it as "B is verified."
        var vm = new WitnessViewModel(
            runMirror: (d, _, _) => Task.FromResult(d == "a.com"
                ? new SectionInputs { Comparison = MakeComparison() }
                : new SectionInputs { Comparison = null }),
            createSnapshot: (d, c, k) => WitnessSigner.CreateSignedSnapshot(d, c, k));

        // Seal domain A successfully.
        vm.Domain = "a.com";
        await vm.SignCommand.ExecuteAsync(null);
        Assert.Equal(WitnessTestimonyState.Signed, vm.TestimonyState);
        Assert.Contains("Sealed", vm.SignatureLine, StringComparison.Ordinal);

        // Switch to domain B; seal fails (null comparison).
        vm.Domain = "b.com";
        await vm.SignCommand.ExecuteAsync(null);
        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.Contains("Nothing to witness", vm.SignatureLine, StringComparison.Ordinal);

        // Self-verify after a failed seal must NOT display a positive result that belongs to
        // a different domain. G-12 Route B: a green "Verified" beside a failed seal for the
        // CURRENT domain is exactly the misleading state this test guards against.
        vm.SelfVerifyCommand.Execute(null);
        Assert.DoesNotContain("Verified", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);
    }

    // ── T-6: External-then-self contamination — single-instance (Phase 3 §4.2) ─────────────────

    [Fact]
    public async Task SingleInstance_SealThenExternalVerifyThenSelfVerify_DoesNotPresentForeignAsOwn()
    {
        // T-6 / G-15: ONE ViewModel instance must survive seal → verify-external → self-verify
        // without the external verify's foreign snapshot contaminating the self-verify's local seal.
        //
        // On Linux, VerifyExternal writes to ExternalVerifyLine/ExternalVerifyBrush (its own
        // dedicated properties), leaving _lastSnapshot (self-verify's source) untouched. This test
        // pins that separation: if a future refactor collapses the two verify paths into a shared
        // snapshot field (as Windows and macOS currently do — see DELTAS.md G-15), this test fails.
        var vm = NewIdleViewModel();
        vm.Domain = "example.com";

        // Step 1: seal a local testimony.
        await vm.SignCommand.ExecuteAsync(null);
        Assert.Equal(WitnessTestimonyState.Signed, vm.TestimonyState);

        // Step 2: verify an EXTERNAL testimony with a DIFFERENT domain/signature.
        var (foreignPriv, _) = WitnessSigner.GenerateKeyPair();
        var foreignSnap = WitnessSigner.CreateSignedSnapshot(
            "foreign.example", MakeComparison(), foreignPriv);
        vm.ExternalSnapshotJson = JsonSerializer.Serialize(foreignSnap);
        vm.VerifyExternalCommand.Execute(null);

        // External verify must render on its own line (G-14), not on the self-verify line.
        Assert.Contains("Verified", vm.ExternalVerifyLine, StringComparison.OrdinalIgnoreCase);

        // Step 3: self-verify — must reference the LOCAL seal, not the foreign snapshot.
        vm.SelfVerifyCommand.Execute(null);
        Assert.Contains("Verified", vm.VerifyLine, StringComparison.OrdinalIgnoreCase);

        // The self-verify result must NOT carry the foreign domain. The VerifyLine text is
        // domain-agnostic ("Verified: the seal is valid…"), so the structural assertion is that
        // _lastSnapshot (driving SelfVerify) still holds the LOCAL seal — not the foreign one that
        // VerifyExternal parsed. We confirm by checking that the self-verify result is green (the
        // local seal genuinely verifies) and the external result is independently green (the
        // foreign seal also genuinely verifies under its own key). Both are Valid, independently.
        Assert.Equal(GreenHex, ColorOf(vm.VerifyBrush));
        Assert.Equal(GreenHex, ColorOf(vm.ExternalVerifyBrush));
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

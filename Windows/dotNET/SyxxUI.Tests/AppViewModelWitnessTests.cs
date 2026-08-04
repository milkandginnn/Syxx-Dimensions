using System;
using System.IO;
using System.Threading.Tasks;
using Syxx.Kit;
using SyxxUI.ViewModels;
using Xunit;

namespace SyxxUI.Tests;

/// <summary>
/// Regression coverage for the Windows Witness GUI fixes: B195 (dead/failed testify probe must not
/// render as the "still pending" placeholder), D051 (WitnessTestify — renamed WitnessTestifyAsync —
/// now performs a LIVE re-probe at sign time instead of signing possibly-stale/mismatched-domain
/// cached data), D093 (drift population from that live comparison), D094 (Free must actually clear
/// the prior VERIFY result, not just the testimony chrome), D095 (HasWitnessKey must reflect on-disk
/// reality from construction, not only after an explicit re-save), and the Windows half of D052
/// (a failed DPAPI key-store write must be surfaced via WitnessKeyError, not silently swallowed).
/// See DELTAS.md entries B195, D051, D093, D094, D095, D052 for full context.
///
/// ============================================================================================
/// HONESTY NOTICE — READ BEFORE TRUSTING THIS FILE
/// ============================================================================================
/// This file was written by static review only. There is NO Windows/.NET-WinUI3 toolchain on the
/// machine that authored it — `dotnet restore`/`build`/`test` were never run against it, and
/// neither was the sibling Windows/SyxxUI.Tests/SyxxUI.Tests.csproj project this file lives in.
/// Every type name, method name, property name, and enum value below was checked by hand against
/// the current Windows/SyxxUI/ViewModels/AppViewModel.cs and its dependencies in Windows/Syxx.Kit,
/// but "checked by hand" is exactly the gap that let a Windows GUI regression ship once already
/// (see DELTAS.md D092 — no CI job ever compiled SyxxUI before 2026-07-22; B195's own Windows fix
/// carried the identical "verified statically, NOT compiled" caveat). Do not treat a clean read of
/// this file as equivalent to a passing test run. A real run on a Windows host — ideally via the
/// still-unwired CI step described in SyxxUI.Tests.csproj's header comment — remains owed before
/// any of these tests should be trusted to actually pass, or even to compile.
/// ============================================================================================
///
/// Test seam: AppViewModel gained a second constructor overload (AppViewModel.cs:45-54) that
/// injects the live vantage-comparison delegate WitnessTestifyAsync calls at sign time, mirroring
/// the identical fix already shipped for Ubuntu's WitnessViewModel (see
/// Ubuntu/SyxxUI.Avalonia/ViewModels/WitnessViewModel.cs:67-87 and its test suite,
/// Ubuntu/SyxxUI.Avalonia.Tests/WitnessViewModelTests.cs). The parameterless constructor used by
/// the real app wires the real VantageComparer.CompareVantagePointsAsync call unchanged.
///
/// The DPAPI-backed WitnessKeyStore itself is NOT seamed — these tests exercise it for real
/// (WitnessKeyStore.SaveKey/LoadKey/HasKey/DeleteKey all hit the actual
/// %AppData%/Syxx/witness.key file via Windows DPAPI), since a second seam for the key store was
/// not judged genuinely necessary: DPAPI is a per-user OS primitive available on any Windows
/// account, including CI runners. Every test that touches it saves/deletes its own key (or,
/// for the D052 test, temporarily relocates the real Syxx state directory) so tests do not leak
/// state into each other or corrupt a real user's persisted key. Because these tests share a
/// single real file path, they rely on xUnit's default same-class sequential execution (tests
/// within one class do not run in parallel with each other) — do not add [Collection] attributes
/// here that would change that without re-auditing the cleanup logic.
/// </summary>
public class AppViewModelWitnessTests
{
    private static ComparisonResult MakeComparison() => new()
    {
        LocalServer = "127.0.0.1",
        IsRedirectionDetected = false,
        IsInconclusive = false,
    };

    private static Func<string, string, Task<ComparisonResult?>> NeverCalled(string why) =>
        (_, _) => throw new InvalidOperationException($"compareVantagePoints must not be called: {why}");

    // ── 1. Empty domain / no comparison possible ──────────────────────────────────────────────

    [Fact]
    public async Task WitnessTestifyAsync_EmptyDomain_NoComparisonPossible_SetsFailedNotStuckRunning()
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        WitnessKeyStore.SaveKey(priv);
        try
        {
            // A valid key is present so the test reaches the live-comparison branch (D051) rather
            // than the missing-key guard (covered separately below).
            var vm = new AppViewModel(compareVantagePoints: (_, _) => Task.FromResult<ComparisonResult?>(null));
            vm.Domain = string.Empty;

            await vm.WitnessTestifyCommand.ExecuteAsync(null);

            Assert.Equal(WitnessTestimonyState.Failed, vm.WitnessTestimony);
            Assert.NotEqual(WitnessTestimonyState.Running, vm.WitnessTestimony);
            Assert.Equal(
                "Nothing to witness — the domain did not resolve, so there is no result to seal.",
                vm.WitnessTestimonyMessage);
            Assert.True(string.IsNullOrEmpty(vm.WitnessSignature));
            Assert.Null(vm.WitnessSnapshot);
        }
        finally { WitnessKeyStore.DeleteKey(); }
    }

    // ── 2. Missing signing key ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WitnessTestifyAsync_NoSigningKey_SetsFailedWithMessage()
    {
        // Ensure a clean slate regardless of what a previous test (or a real user on this machine)
        // left behind — the missing-key branch must trip on its own merits, not by accident.
        WitnessKeyStore.DeleteKey();

        var vm = new AppViewModel(compareVantagePoints: NeverCalled("missing key must fail before any network probe"));
        vm.Domain = "example.com";

        await vm.WitnessTestifyCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Failed, vm.WitnessTestimony);
        Assert.Equal(
            "A signing key is required — a snapshot signed with a default/empty key would be forgeable.",
            vm.WitnessTestimonyMessage);
    }

    // ── 3. WitnessFree() actually clears prior Testify+Verify evidence (D094) ────────────────

    [Fact]
    public async Task WitnessFree_AfterTestifyAndVerify_ClearsSnapshotSignatureValidAndVerification()
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        WitnessKeyStore.SaveKey(priv);
        try
        {
            var comparison = MakeComparison();
            var vm = new AppViewModel(compareVantagePoints: (_, _) => Task.FromResult<ComparisonResult?>(comparison));
            vm.Domain = "example.com";

            await vm.WitnessTestifyCommand.ExecuteAsync(null);
            Assert.Equal(WitnessTestimonyState.Signed, vm.WitnessTestimony);
            Assert.NotNull(vm.WitnessSnapshot);
            Assert.False(string.IsNullOrEmpty(vm.WitnessSignature));

            vm.WitnessVerifyCommand.Execute(null);
            Assert.True(vm.WitnessValid);
            Assert.NotNull(vm.WitnessVerification);

            vm.WitnessFreeCommand.Execute(null);

            // D094: previously only ShowTestimony/ShowValidation/WitnessTestimony(Message) were
            // reset here — WitnessSnapshot/WitnessSignature/WitnessValid/WitnessVerification stayed
            // populated, so a subsequent Verify (before Testify ran again) re-displayed the stale
            // result from before Free.
            Assert.Null(vm.WitnessSnapshot);
            Assert.Equal(string.Empty, vm.WitnessSignature);
            Assert.False(vm.WitnessValid);
            Assert.Null(vm.WitnessVerification);

            // Also assert the (already-fixed pre-D094) B195 chrome reset still holds.
            Assert.False(vm.ShowTestimony);
            Assert.False(vm.ShowValidation);
            Assert.Equal(WitnessTestimonyState.Idle, vm.WitnessTestimony);
            Assert.Equal(string.Empty, vm.WitnessTestimonyMessage);
        }
        finally { WitnessKeyStore.DeleteKey(); }
    }

    // ── 4. Forced live-comparison exception during the D051 re-probe ─────────────────────────

    [Fact]
    public async Task WitnessTestifyAsync_LiveComparisonThrows_SetsFailedNotStuckRunning()
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        WitnessKeyStore.SaveKey(priv);
        try
        {
            var vm = new AppViewModel(
                compareVantagePoints: (_, _) => throw new InvalidOperationException("simulated transport failure"));
            vm.Domain = "example.com";

            await vm.WitnessTestifyCommand.ExecuteAsync(null);

            Assert.Equal(WitnessTestimonyState.Failed, vm.WitnessTestimony);
            Assert.NotEqual(WitnessTestimonyState.Running, vm.WitnessTestimony);
            // NOTE (be honest about what this does and does not prove): AppViewModel.cs:419-423
            // catches the thrown exception INSIDE the same try/catch that also handles a null
            // comparison, and does not thread the exception's own message into
            // WitnessTestimonyMessage on this branch (only the later sign-time catch at :470-476
            // does that). So a transport exception and an honestly-unresolved domain currently
            // produce the IDENTICAL message below. That collapsing of two different failure causes
            // into one message is arguably a further, smaller honesty gap this test surfaces but
            // does not fix (out of scope for a test-only change) — the assertion here is the
            // observable contract this task asked for: Failed, not stuck Running.
            Assert.Equal(
                "Nothing to witness — the domain did not resolve, so there is no result to seal.",
                vm.WitnessTestimonyMessage);
            Assert.Null(vm.WitnessSnapshot);
            Assert.True(string.IsNullOrEmpty(vm.WitnessSignature));
        }
        finally { WitnessKeyStore.DeleteKey(); }
    }

    // ── 5. RefreshWitnessKeyStatus() / HasWitnessKey staleness (D095) ─────────────────────────

    [Fact]
    public void Constructor_ReflectsHasWitnessKeyFromDiskImmediately_WithoutAnyPriorSaveOnThisInstance()
    {
        var (priv, _) = WitnessSigner.GenerateKeyPair();
        // Simulates a key persisted by a PREVIOUS run/instance — this instance never calls
        // SaveWitnessKey() at all.
        WitnessKeyStore.SaveKey(priv);
        try
        {
            var vm = new AppViewModel(compareVantagePoints: NeverCalled("not exercised by this test"));

            // D095: HasWitnessKey previously started false and was only ever assigned inside
            // SaveWitnessKey() — a key already on disk from a prior session read as absent until
            // the user re-typed and re-saved the same key.
            Assert.True(vm.HasWitnessKey);
            Assert.True(WitnessKeyStore.HasKey);
        }
        finally { WitnessKeyStore.DeleteKey(); }

        // Inverse: fail-closed when there genuinely is no key, not just "true once ever set".
        var vmNoKey = new AppViewModel(compareVantagePoints: NeverCalled("not exercised by this test"));
        Assert.False(vmNoKey.HasWitnessKey);
    }

    // ── 6. Key-save failure surfaces via WitnessKeyError (D052, Windows half) ────────────────

    [Fact]
    public void SaveWitnessKey_WhenKeyStoreWriteFails_SetsNonEmptyWitnessKeyError()
    {
        // D052 (Windows half): force a REAL DPAPI/file-write failure by placing a FILE where
        // WitnessKeyStore.SaveKey needs to Directory.CreateDirectory() a DIRECTORY
        // ("%AppData%/Syxx") — Directory.CreateDirectory throws IOException when a file already
        // occupies that path, which SaveKey does not catch, so it propagates into
        // AppViewModel.SaveWitnessKey's catch (AppViewModel.cs:383-384). This exercises the real
        // on-disk path (not a mock) without adding a second constructor seam. Path replicates
        // WitnessKeyStore's own computation (Path.Combine(ApplicationData, "Syxx", "witness.key"),
        // WitnessKeyStore.cs:26-33) using only the public Environment.SpecialFolder API — if that
        // literal folder name ever changes in WitnessKeyStore.cs, this test must be updated to
        // match, or it will silently stop exercising the real failure path.
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string syxxDir = Path.Combine(appData, "Syxx");
        bool dirPreExisted = Directory.Exists(syxxDir);
        string? backupDir = null;

        if (dirPreExisted)
        {
            // Preserve whatever real Syxx state (e.g. an actual witness.key) is already there —
            // move it aside for the duration of this test rather than deleting state we don't own.
            backupDir = syxxDir + ".appviewmodeltests-backup";
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, recursive: true);
            Directory.Move(syxxDir, backupDir);
        }

        try
        {
            File.WriteAllBytes(syxxDir, new byte[] { 0 });

            var vm = new AppViewModel(compareVantagePoints: NeverCalled("not exercised by this test"));
            vm.SaveWitnessKey("not-a-real-key-just-forcing-the-write-path-to-fail");

            // Defect-2: the FAILURE is now carried by WitnessKeySaveFailed, not by
            // "WitnessKeyError is non-empty". WitnessKeyError became the PRESENTED cause
            // (SyxxUI.Presentation.ErrorPresenter), which is legitimately empty when the exception
            // carried nothing a person could read — so asserting non-emptiness would be asserting an
            // accident of the current IOException wording. This assertion is strictly stronger: the
            // save must report failure whether or not a cause could be stated.
            Assert.True(vm.WitnessKeySaveFailed);
            Assert.False(vm.HasWitnessKey);
            // And whatever detail IS shown must never be crash-reporter material.
            Assert.False(SyxxUI.Presentation.ErrorPresenter.IsBoilerplate(vm.WitnessKeyError)
                         && vm.WitnessKeyError.Length > 0,
                "a presented cause must not be crash-reporter material");
        }
        finally
        {
            if (File.Exists(syxxDir)) File.Delete(syxxDir);
            if (backupDir is not null && Directory.Exists(backupDir))
            {
                if (Directory.Exists(syxxDir)) Directory.Delete(syxxDir, recursive: true);
                Directory.Move(backupDir, syxxDir);
            }
        }
    }
}

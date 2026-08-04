using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Syxx.Kit;
using SyxxUI.Avalonia.ViewModels;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Cross-platform Witness "testify" (seal) terminal-outcome PARITY harness — Ubuntu (Avalonia) half.
///
/// Final piece of the B195..D103 Witness hardening arc. Loads the shared single-source-of-truth fixture
/// <c>corpus/witness-testify-parity/witness-testify-parity.json</c> — the same file the macOS and Windows
/// halves load — and asserts the Linux client matches it, in the corpus/stage0 + corpus/probe-parity
/// convention.
///
/// The Linux client is uniquely able to drive BOTH layers on a non-Windows host: the <c>planner</c> layer
/// via the shared <see cref="WitnessTestifyPlanner"/> in Syxx.Kit (the very same net10.0 assembly Windows
/// uses, referenced by SyxxUI.Avalonia), and the <c>viewModel</c> layer via <see cref="WitnessViewModel"/>'s
/// injected resolve/sign seams (no live network or NSec keypair needed). So this half executes the true
/// cross-platform core AND the Linux pre-flight guards for real.
///
/// The pre-flight guards are DELIBERATELY divergent (D103): on the Linux client empty-domain is a silent
/// Idle no-op (not the explicit Failed macOS/Windows surface), and there is NO missing-key branch at all
/// (a fresh ephemeral keypair is generated per sign — key persistence is the documented G16 gap). The
/// fixture records those per-platform expectations; this loader asserts Ubuntu's own entries and documents
/// the n_a one (never a silent pass).
///
/// A missing / empty fixture THROWS — a vacuously-green parity test is the green-that-lies the honesty
/// spine forbids.
/// </summary>
public class WitnessTestifyParityTests
{
    private sealed record Fixture(List<Scenario>? Scenarios);
    private sealed record Scenario(
        string Id,
        string Layer,
        PlannerInputs? Planner,
        PlannerExpected? Expected,
        bool? ProducesDrift,
        Dictionary<string, PlatformExpectation>? PerPlatform);
    private sealed record PlannerInputs(bool ValidKey, bool RedirectionDetected);
    private sealed record PlannerExpected(string OutcomeState, bool ProducesDrift);
    private sealed record PlatformExpectation(string OutcomeState, bool LoaderDrives, string SkipReason);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string FixturePath([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Linux/Ubuntu/SyxxUI.Avalonia.Tests/WitnessTestifyParityTests.cs, so
        // dir/../../.. == repo root. The tree moved from <repo>/Ubuntu to <repo>/Linux/Ubuntu,
        // which added a level here; a stale count silently loses the golden vector.
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("witness-testify parity: cannot resolve source directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
        var path = Path.Combine(repoRoot, "corpus", "witness-testify-parity", "witness-testify-parity.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"witness-testify-parity fixture not found at {path}. Single source of truth is missing.", path);
        return path;
    }

    private static IReadOnlyList<Scenario> Scenarios()
    {
        var json = File.ReadAllText(FixturePath());
        var fixture = JsonSerializer.Deserialize<Fixture>(json, JsonOpts)
                      ?? throw new InvalidOperationException("witness-testify-parity fixture deserialized to null.");
        if (fixture.Scenarios is null || fixture.Scenarios.Count == 0)
            throw new InvalidOperationException("witness-testify-parity fixture contains no scenarios — refusing to run vacuously.");
        return fixture.Scenarios;
    }

    // ── Planner layer (shared Syxx.Kit — TRUE parity with Windows/macOS) ────────────────────────────

    private static ComparisonResult MakeComparison(bool redirected)
    {
        static List<DNSRecord> Records(string[] rdata) =>
            rdata.Select(r => new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = r }).ToList();

        var publicRecords = Records(new[] { "93.184.216.34" });
        var localRecords = redirected ? Records(new[] { "10.0.0.5" }) : publicRecords;
        return new ComparisonResult
        {
            LocalServer = "192.168.1.1",
            LocalRecords = localRecords,
            IsRedirectionDetected = redirected,
            LocalOutcome = new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = localRecords },
            PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
            {
                { "Cloudflare", new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = publicRecords } }
            }
        };
    }

    [Fact]
    public void PlannerLayerMatchesFixture()
    {
        var asserted = 0;
        foreach (var s in Scenarios().Where(s => s.Layer == "planner"))
        {
            Assert.NotNull(s.Planner);
            Assert.NotNull(s.Expected);
            var comparison = MakeComparison(s.Planner!.RedirectionDetected);
            var key = s.Planner.ValidKey ? WitnessSigner.GenerateKeyPair().Item1 : "";

            switch (s.Expected!.OutcomeState)
            {
                case "signed":
                    var outcome = WitnessTestifyPlanner.Seal("example.com", "192.168.1.1", comparison, key);
                    Assert.False(string.IsNullOrEmpty(outcome.Snapshot.Signature),
                        $"{s.Id}: a signed outcome must carry a real Ed25519 signature");
                    // Syxx.Kit's VerifySnapshot now returns the forensic tri-state
                    // (SnapshotVerification), not a bool — this project did not compile against the
                    // current Kit until this line was updated. The assertion is UNCHANGED in
                    // strength: only Valid passes, and every other outcome (Expired, Stale,
                    // Untrusted, Forged, MalformedInput…) now fails LOUDLY with its own name instead
                    // of collapsing into "false".
                    Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(outcome.Snapshot));
                    Assert.Equal(s.Expected.ProducesDrift, outcome.Drift is not null);
                    break;
                case "failed":
                    Assert.Throws<WitnessSignerException>(() =>
                        WitnessTestifyPlanner.Seal("example.com", "192.168.1.1", comparison, key));
                    Assert.False(s.Expected.ProducesDrift, $"{s.Id}: a failed seal must never claim a drift");
                    break;
                default:
                    Assert.Fail($"{s.Id}: unexpected planner outcomeState '{s.Expected.OutcomeState}' — planner terminal states are only signed/failed");
                    break;
            }
            asserted++;
        }
        Assert.True(asserted > 0, "no planner scenarios were exercised — fixture drift or a vacuous run");
    }

    // ── View-model layer (Ubuntu WitnessViewModel pre-flight guards, driven via its injected seams) ──

    private static Func<string, string, bool, Task<SectionInputs>> NeverResolve(string why) =>
        (_, _, _) => throw new InvalidOperationException($"runMirror must not be called: {why}");
    private static Func<string, ComparisonResult, string, WitnessSnapshot> NeverSign(string why) =>
        (_, _, _) => throw new InvalidOperationException($"createSnapshot must not be called: {why}");

    private static void AssertTerminal(WitnessViewModel vm, string outcomeState, string id)
    {
        switch (outcomeState)
        {
            case "failed":
                Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
                Assert.NotEqual(WitnessTestimonyState.Running, vm.TestimonyState);
                Assert.True(vm.HasResult, $"{id}: a Failed outcome must still HAS a result (B195)");
                break;
            case "idle":
                Assert.Equal(WitnessTestimonyState.Idle, vm.TestimonyState);
                Assert.False(vm.IsRunning, $"{id}: an idle no-op must not be left Running");
                break;
            case "signed":
                Assert.Equal(WitnessTestimonyState.Signed, vm.TestimonyState);
                break;
            default:
                Assert.Fail($"{id}: unsupported outcomeState '{outcomeState}'");
                break;
        }
    }

    [Fact]
    public async Task ViewModelLayerMatchesFixture_Ubuntu()
    {
        var driven = new List<string>();
        var skipped = new List<(string, string)>();
        foreach (var s in Scenarios().Where(s => s.Layer == "viewModel"))
        {
            Assert.NotNull(s.PerPlatform);
            Assert.True(s.PerPlatform!.TryGetValue("ubuntu", out var ub), $"{s.Id}: missing ubuntu expectation");
            if (!ub!.LoaderDrives)
            {
                Assert.False(string.IsNullOrEmpty(ub.SkipReason),
                    $"{s.Id}: a skipped scenario must carry a skipReason (honesty: no silent pass)");
                skipped.Add((s.Id, ub.SkipReason));
                continue;
            }

            switch (s.Id)
            {
                case "empty-domain":
                {
                    // Linux client: empty domain is a silent early-return no-op → stays Idle.
                    var vm = new WitnessViewModel(
                        NeverResolve("empty domain must not resolve"),
                        NeverSign("empty domain must not sign"));
                    vm.Domain = "";
                    await vm.SignCommand.ExecuteAsync(null);
                    AssertTerminal(vm, ub.OutcomeState, s.Id);
                    break;
                }
                case "no-comparison-result":
                {
                    // Live resolve yields no comparison → Failed ("Nothing to witness …"), never signed.
                    var vm = new WitnessViewModel(
                        (_, _, _) => Task.FromResult(new SectionInputs { Comparison = null }),
                        NeverSign("no comparison means nothing to sign"));
                    vm.Domain = "example.com";
                    await vm.SignCommand.ExecuteAsync(null);
                    AssertTerminal(vm, ub.OutcomeState, s.Id);
                    Assert.Equal(
                        "Nothing to witness — the domain did not resolve, so there is no result to seal.",
                        vm.SignatureLine);
                    break;
                }
                case "redirection-detected-signed":
                {
                    // D105: a live comparison that FLAGS redirection is sealed by the real SignAsync,
                    // which now routes the drift through the shared Syxx.Kit WitnessTestifyPlanner and
                    // stores it on WitnessViewModel.Drift. Sign via the real WitnessSigner so the seal
                    // genuinely succeeds; assert the view-model surfaces the drift (parity with the
                    // planner-layer 'redirection-detected' scenario, now proven at the VIEW-MODEL layer
                    // on Linux — the exact wiring D093 dropped on Windows).
                    var redirected = MakeComparison(redirected: true);
                    var vm = new WitnessViewModel(
                        (_, _, _) => Task.FromResult(new SectionInputs { Comparison = redirected }),
                        (d, c, k) => WitnessSigner.CreateSignedSnapshot(d, c, k));
                    vm.Domain = "example.com";
                    await vm.SignCommand.ExecuteAsync(null);
                    AssertTerminal(vm, ub.OutcomeState, s.Id);
                    Assert.True(s.ProducesDrift == true,
                        $"{s.Id}: fixture must mark this signed-with-drift scenario producesDrift=true");
                    Assert.NotNull(vm.Drift);
                    Assert.Equal("93.184.216.34", vm.Drift!.PreviousAnswer);
                    Assert.Equal("10.0.0.5", vm.Drift.NewAnswer);
                    Assert.False(string.IsNullOrEmpty(vm.Drift.Resolver),
                        $"{s.Id}: a real drift must record the resolver it was observed on");
                    break;
                }
                default:
                    Assert.Fail($"{s.Id}: fixture marks ubuntu loaderDrives=true but this loader has no driver for it");
                    break;
            }
            driven.Add(s.Id);
        }
        Assert.Contains("empty-domain", driven);
        Assert.Contains("no-comparison-result", driven);
        Assert.Contains("redirection-detected-signed", driven);
    }
}

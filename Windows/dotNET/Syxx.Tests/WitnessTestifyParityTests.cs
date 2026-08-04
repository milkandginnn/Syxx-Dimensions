using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Cross-platform Witness "testify" (seal) terminal-outcome PARITY harness — Windows half.
///
/// Final piece of the B195..D103 Witness hardening arc. Loads the shared single-source-of-truth fixture
/// <c>corpus/witness-testify-parity/witness-testify-parity.json</c> — the same file the macOS
/// (macOS/Tests/SyxxAppTests/WitnessTestifyParityTests.swift) and Ubuntu
/// (Ubuntu/SyxxUI.Avalonia.Tests/WitnessTestifyParityTests.cs) halves load — and asserts Windows
/// behavior matches it, in the exact convention corpus/stage0 and corpus/probe-parity already use.
///
/// LAYERS: this file drives the <c>planner</c> scenarios against the pure <see cref="WitnessTestifyPlanner"/>
/// in Syxx.Kit — the same net10.0 assembly Ubuntu references and the byte-twin of macOS SyxxKit — which
/// DOES compile and run on the Linux CI legs (and this authoring host), so the TRUE cross-platform core is
/// executed here. The <c>viewModel</c> scenarios (empty-domain, missing-key, no-comparison-result) live in
/// AppViewModel in SyxxUI (WinUI 3), which no non-Windows CI host can build (D101); the fixture marks them
/// <c>windows.loaderDrives=false</c> and this loader documents that (never a silent pass) while their
/// behavior stays pinned by Windows/SyxxUI.Tests/AppViewModelWitnessTests.cs.
///
/// Deliberately its OWN file (not SyxxKitTests.cs). It references ONLY WitnessTestifyPlanner / WitnessSigner
/// / ComparisonResult — never Conclusion/Hypothesis — so it compiles cleanly even while the rest of
/// Syxx.Tests is red under the unrelated in-flight Synthesis refactor (Conclusion.Result→Determination,
/// Hypothesis.Inconclusive removed), and can be run in isolation exactly as the D103 planner tests were.
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
        // thisFile = <repo>/Windows/dotNET/Syxx.Tests/WitnessTestifyParityTests.cs — three levels
        // below the repo root. The Windows tree moved Windows/Syxx.Tests → Windows/dotNET/Syxx.Tests
        // during the matrix cutover; the old dir/../.. resolved to <repo>/Windows and this harness
        // threw FileNotFoundException on every run (correctly refusing to pass vacuously).
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
            Result = redirected ? VantageComparisonResult.RedirectionDetected : VantageComparisonResult.Consistent,
            LocalOutcome = new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = localRecords },
            PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
            {
                { "Cloudflare", new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = publicRecords } }
            }
        };
    }

    // The shared, offline, deterministic core — TRUE parity with macOS/Ubuntu (same Syxx.Kit on Win/Ubuntu).
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
                    // Assert.True, not Assert.Equal: xUnit's 3-arg Assert.Equal overload takes an
                    // IEqualityComparer, not a message, so the message form does not compile.
                    // The scenario id is worth keeping, so compare explicitly and carry it.
                    Assert.True(SnapshotVerification.Valid == WitnessSigner.VerifySnapshot(outcome.Snapshot),
                        $"{s.Id}: the sealed snapshot must self-verify");
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

    // View-model scenarios cannot be driven from Syxx.Tests (AppViewModel is in SyxxUI/WinUI3, D101).
    // Assert the fixture is honest about that — every windows viewModel entry is loaderDrives=false and
    // carries a non-empty skipReason pointing at where the behavior IS pinned — so a future edit that
    // flips one to loaderDrives=true here (without adding a driver) fails loudly instead of passing empty.
    [Fact]
    public void ViewModelLayer_WindowsDeferredToSyxxUiTests_IsDocumentedNotSilentlySkipped()
    {
        var viewModel = Scenarios().Where(s => s.Layer == "viewModel").ToList();
        Assert.True(viewModel.Count > 0, "fixture must carry viewModel scenarios");
        foreach (var s in viewModel)
        {
            Assert.NotNull(s.PerPlatform);
            Assert.True(s.PerPlatform!.TryGetValue("windows", out var win), $"{s.Id}: missing windows expectation");
            Assert.False(win!.LoaderDrives,
                $"{s.Id}: windows viewModel scenarios are not runnable in Syxx.Tests (SyxxUI/WinUI3, D101) — if a driver is added, wire it here");
            Assert.False(string.IsNullOrEmpty(win.SkipReason),
                $"{s.Id}: a deferred scenario must document WHERE its behavior is pinned (honesty: no silent skip)");
        }
    }
}

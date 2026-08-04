using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// [B251] Windows half of the cross-platform witness GOLDEN VECTOR.
///
/// Loads the checked-in, macOS-PRODUCED snapshots in <c>corpus/witness-crossplatform/</c> — the
/// same files the macOS half (macOS/Tests/SyxxKitTests/WitnessCrossPlatformGoldenVectorTests.swift)
/// loads — and asserts this platform can read and verify them.
///
/// This is the regression guard the seam actually needed. Both suites were green while
/// <c>syxx witness verify &lt;macOS-produced file&gt;</c> could not so much as parse the file on
/// Windows, because neither suite ever read an artifact the other platform wrote. A converter unit
/// test would not have caught it either; only a shared artifact does.
///
/// A missing or unreadable fixture THROWS. A vacuously-green parity test is the green-that-lies.
/// </summary>
public class WitnessCrossPlatformGoldenVectorTests
{
    /// The production read path: exactly the options `syxx witness verify` uses
    /// (Windows/dotNET/Syxx/Commands/WitnessCommand.cs). Deliberately NOT a bespoke set of options,
    /// so the test exercises what shipping code does.
    private static readonly JsonSerializerOptions ProductionReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<object[]> Vectors() => new[]
    {
        new object[] { "witness-macos-degraded.json" },
        new object[] { "witness-macos-answers.json" },
        // B252: v2 legacy answers snapshot — a verbatim copy of the pre-v4 file. Both platforms'
        // verifiers must keep accepting it (v2 = errorMessage inside signed bytes, no canonical
        // DoH) while v4 fixtures carry the new canonical fields.
        new object[] { "witness-macos-answers-v2legacy.json" },
    };

    private static string CorpusDir([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Windows/dotNET/Syxx.Tests/WitnessCrossPlatformGoldenVectorTests.cs
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("witness golden vector: cannot resolve source directory.");
        return Path.Combine(Path.GetFullPath(Path.Combine(dir, "..", "..", "..")),
                            "corpus", "witness-crossplatform");
    }

    private static string ReadVector(string name)
    {
        string path = Path.Combine(CorpusDir(), name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"witness golden vector missing at {path}.", path);
        string json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"witness golden vector {name} is empty — refusing to pass vacuously.");
        return json;
    }

    private static WitnessSnapshot Load(string name)
        => JsonSerializer.Deserialize<WitnessSnapshot>(ReadVector(name), ProductionReaderOptions)
           ?? throw new InvalidOperationException($"witness golden vector {name} deserialized to null.");

    [Theory]
    [MemberData(nameof(Vectors))]
    public void MacOsProducedSnapshotVerifiesOnWindows(string name)
    {
        var snapshot = Load(name);
        Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(snapshot));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void MacOsProducedSnapshotRetainsItsDisplayEvidence(string name)
    {
        var snapshot = Load(name);

        // The status strings must have survived the read as typed values, not been defaulted.
        // (default(DNSResponseStatus) is Success, so "everything read as Success" is the shape a
        // silently-broken reader takes.)
        Assert.NotEmpty(snapshot.Comparison.PublicResolverOutcomes);
        Assert.NotNull(snapshot.Comparison.DohOutcomes);
        Assert.NotEmpty(snapshot.Comparison.DohOutcomes!);

        // The display timestamp must project onto the signed millisecond exactly; that equality IS
        // the D083 guard, and getting it wrong turns honest evidence into a DISPLAY MISMATCH.
        Assert.Equal(snapshot.Evidence.Timestamp,
            (ulong)new DateTimeOffset(snapshot.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds());
        Assert.Equal(snapshot.FormatVersion, snapshot.Evidence.FormatVersion);
        Assert.Equal(snapshot.TargetDomain, snapshot.Evidence.QueryId);
    }

    [Fact]
    public void TheVectorSetCoversEveryStatusWireSpelling()
    {
        // Guards the fixtures themselves: if a future edit narrows them to the two or three statuses
        // a lucky capture happened to contain, a reader that mishandles the rest would sail through.
        var seen = new HashSet<DNSResponseStatus>();
        foreach (var row in Vectors())
        {
            var s = Load((string)row[0]);
            seen.Add(s.Comparison.LocalOutcome.Status);
            foreach (var o in s.Comparison.PublicResolverOutcomes.Values) seen.Add(o.Status);
            foreach (var o in s.Comparison.DohOutcomes ?? new()) seen.Add(o.Value.Status);
        }
        var missing = Enum.GetValues<DNSResponseStatus>().Where(v => !seen.Contains(v)).ToList();
        Assert.True(missing.Count == 0,
            $"golden vectors no longer exercise: {string.Join(", ", missing)}");
    }

    [Fact]
    public void ErrorTextCrossesTheBoundaryIntact()
    {
        // The degraded capture carries real Foundation error text including U+2019, and v2 signs it.
        var snapshot = Load("witness-macos-degraded.json");
        var texts = snapshot.Comparison.PublicResolverOutcomes.Values
            .Select(o => o.ErrorMessage).Where(t => !string.IsNullOrEmpty(t)).ToList();
        Assert.NotEmpty(texts);
        Assert.Contains(texts, t => t!.Contains('’'));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void TamperingTheDisplayedAnswerIsRejected(string name)
    {
        // FAILURE CASE. Proves the vectors pass because the signature and the display-integrity
        // guard hold, not because the verifier says Valid to whatever it is handed. The answer set
        // is signed evidence, so rewriting any displayed rdata must be caught.
        var snapshot = Load(name);
        var victim = snapshot.Comparison.PublicResolverOutcomes
            .FirstOrDefault(kv => kv.Value.Records.Count > 0);
        var target = victim.Value is not null
            ? snapshot.Comparison.PublicResolverOutcomes[victim.Key]
            : snapshot.Comparison.LocalOutcome;
        if (target.Records.Count == 0)
        {
            // The degraded capture has no records anywhere; injecting one is the equivalent tamper.
            target.Records.Add(new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = "6.6.6.6" });
        }
        else
        {
            target.Records[0].Rdata = "6.6.6.6";
        }
        Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(snapshot));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void PromotingAFailedResolverToSuccessIsRejected(string name)
    {
        // FAILURE CASE. Rewriting a resolver that failed into one that answered flips the signed
        // per-resolver `success` bit, so the display-integrity guard must reject it. This is the
        // tamper that most directly manufactures false confidence.
        var snapshot = Load(name);
        var failed = snapshot.Comparison.PublicResolverOutcomes
            .Concat(snapshot.Comparison.DohOutcomes ?? new())
            .FirstOrDefault(kv => kv.Value.DidFail);
        Assert.NotNull(failed.Value);   // both vectors must contain at least one failed leg

        failed.Value!.Status = DNSResponseStatus.Success;
        failed.Value.ErrorMessage = null;
        var result = WitnessSigner.VerifySnapshotDetailed(snapshot);

        if (snapshot.Comparison.PublicResolverOutcomes.ContainsKey(failed.Key)
            && ReferenceEquals(snapshot.Comparison.PublicResolverOutcomes[failed.Key], failed.Value))
        {
            Assert.Equal(SnapshotVerification.DisplayMismatch, result);
        }
        else
        {
            // The leg lived in DohOutcomes, which B252 records as unsigned and unchecked. Asserting
            // Valid here is not an endorsement — it pins the CURRENT, DEFECTIVE behaviour so that
            // closing B252 turns this assertion red and forces the test to be updated deliberately.
            Assert.Equal(SnapshotVerification.Valid, result);
        }
    }

    [Fact]
    public void KnownGap_B252_RewritingDoHEvidenceIsNeverValid()
    {
        // B252, post-fix characterization. `comparison.dohOutcomes` is the DISPLAY mirror of the
        // DoH evidence; the display-integrity rebuild must reject a rewritten block. (At v2 the
        // canonical evidence.comparisonResult.dohOutcomes is null, so this rewrite could still
        // verify — B252's defect — and the display guard was the only backstop. With v4 canonical
        // DoH signing the signature-level guard is asserted separately in
        // TamperingCanonicalDoHEvidenceIsRejected.) A rewritten DoH block must NEVER read Valid.
        var snapshot = Load("witness-macos-degraded.json");
        var key = snapshot.Comparison.DohOutcomes!.Keys.First();
        snapshot.Comparison.DohOutcomes[key] = new ResolverQueryOutcome
        {
            Status = DNSResponseStatus.Success,
            Records = new List<DNSRecord> { new() { Name = "example.com", Type = 1, Ttl = 300, Rdata = "6.6.6.6" } },
        };
        Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(snapshot));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void TamperingTheDisplayTimestampIsRejected(string name)
    {
        // FAILURE CASE for the date converter specifically: a converter that quietly lost precision
        // would make this assertion pass for the wrong reason, so the sibling
        // MacOsProducedSnapshotRetainsItsDisplayEvidence pins the untampered equality too.
        var snapshot = Load(name);
        snapshot.Timestamp = snapshot.Timestamp.AddMinutes(1);
        Assert.Equal(SnapshotVerification.DisplayMismatch, WitnessSigner.VerifySnapshotDetailed(snapshot));
    }

    [Fact]
    public void AnUnknownStatusInAVectorIsRejectedRatherThanDefaulted()
    {
        // FAILURE CASE. A hostile or corrupt snapshot must not read as a run where every resolver
        // answered successfully.
        string mutated = ReadVector("witness-macos-degraded.json")
            .Replace("\"transportError\"", "\"someStatusFromTheFuture\"");
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<WitnessSnapshot>(mutated, ProductionReaderOptions));
    }

    // ---- Combined golden vectors (Phase 0) ----
    //
    // These read the checked-in corrupted artifacts — deliberately NOT members of Vectors() (the
    // "must be Valid" set). Each combines two conditions so this platform's crypto-LAST verdict
    // (Expired) is caught masking the real one. macOS reads them correctly today; Windows/Linux read
    // Expired until Phase 1a reorders the Kit crypto-first.
    // See docs/PROPOSAL-witness-crypto-first-2026-08-03.md §4.

    [Fact]
    public void GVA_ForgedExpired_IsInvalidSignature_NotExpired()
    {
        // GV-A: forged signature (sig[0]^=0xFF) over a snapshot whose signed ExpiresAt is past.
        var snapshot = Load("witness-forged-expired.json");
        var v = WitnessSigner.VerifySnapshotDetailed(snapshot);
        Assert.Equal(SnapshotVerification.InvalidSignature, v);
        Assert.NotEqual(SnapshotVerification.Expired, v);
    }

    [Fact]
    public void GVB_DisplayTamperedExpired_IsDisplayMismatch_NotExpired()
    {
        // GV-B: honest signature + past ExpiresAt + display timestamp edited 60s post-sign.
        var snapshot = Load("witness-display-tampered-expired.json");
        var v = WitnessSigner.VerifySnapshotDetailed(snapshot);
        Assert.Equal(SnapshotVerification.DisplayMismatch, v);
        Assert.NotEqual(SnapshotVerification.Expired, v);
    }
}

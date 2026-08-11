using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// [B251] Windows half of the cross-platform witness wire contract.
///
/// The defect these guard was invisible to BOTH suites while both were green, because it lived in
/// the seam between them: macOS emitted the Swift <c>rawValue</c> <c>"transportError"</c>, the .NET
/// reader had no converter for <c>DNSResponseStatus</c>, and
/// <c>syxx witness verify &lt;macOS file&gt;</c> died with
/// <c>The JSON value could not be converted to Syxx.Kit.DNSResponseStatus.
/// Path: $.comparison.dohOutcomes.Cloudflare.status</c>. A converter unit test alone would still
/// not have caught it, so <see cref="WitnessCrossPlatformGoldenVectorTests"/> pins the real
/// artifact; these tests pin the mapping the artifact depends on.
/// </summary>
public class WitnessCrossPlatformWireTests
{
    private static readonly JsonSerializerOptions ReaderOptions = new() { PropertyNameCaseInsensitive = true };

    // ---------------------------------------------------------------- DNSResponseStatus

    /// The macOS rawValues, transcribed from macOS/Sources/SyxxKit/DNSResolver.swift:96-107.
    /// The macOS twin test (DNSResponseStatusWireParityTests.swift) asserts the same list against
    /// the Swift enum, so a rename on either side breaks a test on that side.
    public static readonly (DNSResponseStatus Status, string Wire)[] ExpectedWire =
    {
        (DNSResponseStatus.Success, "success"),
        (DNSResponseStatus.NoData, "noData"),
        (DNSResponseStatus.NXDomain, "nxDomain"),
        (DNSResponseStatus.ServFail, "servFail"),
        (DNSResponseStatus.Refused, "refused"),
        (DNSResponseStatus.Timeout, "timeout"),
        (DNSResponseStatus.Truncated, "truncated"),
        (DNSResponseStatus.ResponseIdMismatch, "responseIdMismatch"),
        (DNSResponseStatus.MalformedResponse, "malformedResponse"),
        (DNSResponseStatus.TransportError, "transportError"),
    };

    [Fact]
    public void EveryStatusHasTheMacOSWireSpelling()
    {
        var members = Enum.GetValues<DNSResponseStatus>();
        Assert.Equal(ExpectedWire.Length, members.Length);
        foreach (var (status, wire) in ExpectedWire)
        {
            string json = JsonSerializer.Serialize(status);
            Assert.Equal($"\"{wire}\"", json);
            Assert.Equal(status, JsonSerializer.Deserialize<DNSResponseStatus>(json));
        }
    }

    [Fact]
    public void ReadsTheExactPathThatBrokeTheRealCli()
    {
        // Verbatim shape of comparison.dohOutcomes.Cloudflare from a real macOS witness.json.
        const string json = """
        {"records":[],"status":"transportError","errorDescription":"The network connection was lost."}
        """;
        var outcome = JsonSerializer.Deserialize<ResolverQueryOutcome>(json, ReaderOptions);
        Assert.NotNull(outcome);
        Assert.Equal(DNSResponseStatus.TransportError, outcome!.Status);
        Assert.Equal("The network connection was lost.", outcome.ErrorMessage);
        Assert.True(outcome.DidFail);
    }

    [Fact]
    public void ReadsLegacyWindowsOrdinalsAndPascalCase()
    {
        // Snapshots written by Windows builds before B251 carried the bare CLR ordinal.
        Assert.Equal(DNSResponseStatus.TransportError, JsonSerializer.Deserialize<DNSResponseStatus>("9"));
        Assert.Equal(DNSResponseStatus.Success, JsonSerializer.Deserialize<DNSResponseStatus>("0"));
        // ...and anything written with JsonStringEnumConverter's default PascalCase policy.
        Assert.Equal(DNSResponseStatus.NXDomain, JsonSerializer.Deserialize<DNSResponseStatus>("\"NXDomain\""));
    }

    [Fact]
    public void UnknownStatusIsRejected_NeverDefaultedToSuccess()
    {
        // FAILURE CASE. `default(DNSResponseStatus)` is Success — the single most dangerous value to
        // fall back to, because it converts an unreadable status into "the resolver answered fine".
        var ex = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DNSResponseStatus>("\"definitelyNotAStatus\""));
        Assert.Contains("Unrecognised DNS response status", ex.Message);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DNSResponseStatus>("99"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DNSResponseStatus>("true"));
    }

    // ---------------------------------------------------------------- error text wire name

    [Fact]
    public void ErrorTextUsesTheMacOSWireNameAndStillReadsTheLegacyOne()
    {
        var outcome = new ResolverQueryOutcome { Status = DNSResponseStatus.Timeout, ErrorMessage = "boom" };
        string json = JsonSerializer.Serialize(outcome);
        Assert.Contains("\"errorDescription\":\"boom\"", json);
        Assert.DoesNotContain("\"errorMessage\"", json);
        // DidFail is derived from Status; emitting it would put a second, forgeable copy of the
        // same fact on the wire (macOS emits no such field).
        Assert.DoesNotContain("didFail", json);

        var legacy = JsonSerializer.Deserialize<ResolverQueryOutcome>(
            """{"records":[],"status":5,"errorMessage":"older windows build"}""", ReaderOptions);
        Assert.Equal("older windows build", legacy!.ErrorMessage);
    }

    [Fact]
    public void DroppingTheErrorTextWouldBreakTheDisplayIntegrityGuard()
    {
        // FAILURE CASE, and the reason the wire name matters beyond cosmetics: SignedEvidencePayload
        // v2 signs the error text, and VerifySnapshotDetailed rebuilds the canonical payload from
        // the DISPLAY outcomes. A reader that drops errorDescription rebuilds a null and rejects an
        // honest snapshot as DisplayMismatch.
        var withText = new ResolverQueryOutcome { Status = DNSResponseStatus.Timeout, ErrorMessage = "the query timed out" };
        var withoutText = new ResolverQueryOutcome { Status = DNSResponseStatus.Timeout, ErrorMessage = null };

        var a = SignedEvidencePayload.Build("example.com", Comparison(withText), 1_700_000_000_000UL, null);
        var b = SignedEvidencePayload.Build("example.com", Comparison(withoutText), 1_700_000_000_000UL, null);

        Assert.False(ResolverQueryOutcomeCanonical.SequenceCanonicalEquals(
            a.ResolverQueryOutcomes, b.ResolverQueryOutcomes));

        static ComparisonResult Comparison(ResolverQueryOutcome o) => new()
        {
            LocalServer = "10.0.0.1",
            LocalOutcome = o,
            PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>(),
            Result = VantageComparisonResult.Inconclusive,
        };
    }

    // ---------------------------------------------------------------- timestamps

    [Fact]
    public void TimestampIsWrittenAsFoundationReferenceDateSeconds()
    {
        var snapshot = new WitnessSnapshot
        {
            Timestamp = new DateTime(2026, 7, 29, 16, 32, 57, 431, DateTimeKind.Utc),
        };
        string json = JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        // Swift's default JSONEncoder writes Date as a bare number, never an ISO-8601 string.
        Assert.DoesNotContain("2026-07-29T", json);
        Assert.Contains("\"timestamp\":807035577", json);
    }

    [Fact]
    public void ReadsBothFoundationSecondsAndLegacyIso8601()
    {
        var fromDouble = JsonSerializer.Deserialize<WitnessSnapshot>(
            """{"timestamp":807035577.431233}""", ReaderOptions)!;
        var fromIso = JsonSerializer.Deserialize<WitnessSnapshot>(
            """{"timestamp":"2026-07-29T16:32:57.4312330Z"}""", ReaderOptions)!;
        Assert.Equal(
            new DateTimeOffset(fromIso.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds(),
            new DateTimeOffset(fromDouble.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds());
    }

    [Fact]
    public void MillisecondAgreementSurvivesEveryTickOffsetInAMillisecond()
    {
        // FAILURE CASE for the D102 defect class. D083 requires
        //   ToUnixTimeMilliseconds(display) == evidence.timestamp
        // on Windows and the mirror-image truncation on macOS, and BOTH truncate. A naive double
        // conversion lands within ~4e-4 ms of the instant, so roughly one snapshot in a thousand
        // would truncate to the neighbouring millisecond and read as DISPLAY MISMATCH — an honest
        // snapshot failing on a timing coin-flip. Sweep every sub-millisecond tick offset plus a
        // spread of whole milliseconds; all 10 000 must agree in both directions.
        var baseTime = new DateTime(2026, 7, 29, 16, 32, 57, 0, DateTimeKind.Utc);
        int checked_ = 0;
        for (int ms = 0; ms < 1000; ms++)
        {
            for (long subTicks = 0; subTicks < TimeSpan.TicksPerMillisecond; subTicks += 1000)
            {
                var t = baseTime.AddTicks(ms * TimeSpan.TicksPerMillisecond + subTicks);
                long signedMillis = new DateTimeOffset(t).ToUnixTimeMilliseconds();

                double wire = AppleReferenceDateJsonConverter.ToAppleReferenceSeconds(t);
                // What macOS will compute from the number we wrote:
                Assert.Equal(signedMillis, AppleReferenceDateJsonConverter.MacOsTruncatedUnixMillis(wire));
                // ...and what we compute when reading that same number back:
                var roundTripped = AppleReferenceDateJsonConverter.FromAppleReferenceSeconds(wire);
                Assert.Equal(signedMillis, new DateTimeOffset(roundTripped).ToUnixTimeMilliseconds());
                checked_++;
            }
        }
        Assert.Equal(10_000, checked_);
    }

    [Fact]
    public void NonFiniteAndPre1970TimestampsAreRejected()
    {
        // FAILURE CASE. macOS converts with UInt64(...), which traps on a negative value, so a
        // pre-epoch display timestamp could only ever produce a snapshot macOS must reject.
        Assert.Throws<JsonException>(() => AppleReferenceDateJsonConverter.FromAppleReferenceSeconds(double.NaN));
        Assert.Throws<JsonException>(() => AppleReferenceDateJsonConverter.FromAppleReferenceSeconds(double.PositiveInfinity));
        Assert.Throws<JsonException>(() => AppleReferenceDateJsonConverter.FromAppleReferenceSeconds(-1e12));
    }

    // ---------------------------------------------------------------- E162 transport partition

    [Fact]
    public void BothTransportsOfTheSameResolverAreRetained()
    {
        // E162. `syxx witness create` died with "An item with the same key has already been added.
        // Key: Cloudflare" because the Do53 and DoH legs of one resolver were flattened into a
        // single name-keyed dictionary. The fix must keep BOTH observations: collapsing them with a
        // last-write-wins would discard the DoH answer, which is the entire evidentiary basis of the
        // B010 cross-transport tampering check.
        var do53Answer = Outcome(DNSResponseStatus.Success, "93.184.216.34");
        var dohAnswer = Outcome(DNSResponseStatus.Success, "10.0.0.5");   // deliberately different

        var (do53, doh) = VantageComparer.PartitionByTransport(new[]
        {
            (DnsVantageTransport.Do53, "Cloudflare", do53Answer),
            (DnsVantageTransport.Do53, "Google", Outcome(DNSResponseStatus.Success, "93.184.216.34")),
            (DnsVantageTransport.DoH, "Cloudflare", dohAnswer),
            (DnsVantageTransport.DoH, "Google", Outcome(DNSResponseStatus.Timeout)),
        });

        Assert.Equal(2, do53.Count);
        Assert.Equal(2, doh.Count);
        Assert.Equal("93.184.216.34", do53["Cloudflare"].Records.Single().Rdata);
        Assert.Equal("10.0.0.5", doh["Cloudflare"].Records.Single().Rdata);
        // The disagreeing DoH observation must still be visible to the B010 check.
        Assert.NotEqual(do53["Cloudflare"].Records.Single().Rdata, doh["Cloudflare"].Records.Single().Rdata);
    }

    [Fact]
    public void DuplicateWithinOneTransportIsRefused_NotSilentlyDropped()
    {
        // FAILURE CASE. Two Do53 observations of one resolver in a single run means the probe layer
        // queried the same vantage twice; quietly keeping one is exactly the silent evidence loss
        // this delta is about.
        Assert.Throws<InvalidOperationException>(() => VantageComparer.PartitionByTransport(new[]
        {
            (DnsVantageTransport.Do53, "Cloudflare", Outcome(DNSResponseStatus.Success, "93.184.216.34")),
            (DnsVantageTransport.Do53, "Cloudflare", Outcome(DNSResponseStatus.Success, "10.0.0.5")),
        }));
    }

    private static ResolverQueryOutcome Outcome(DNSResponseStatus status, params string[] rdata) => new()
    {
        Status = status,
        Records = rdata.Select(r => new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = r }).ToList(),
    };
}

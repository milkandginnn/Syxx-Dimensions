using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// STAGE5-CONTRACT §14 "Shared DTO/parity tests" — the POPULATED-object cross-platform byte-parity
/// regression, the C# twin of `Stage5PopulatedParityTests.swift`. Unlike the hand-built `FixtureV1()`,
/// this fixture is the FROZEN canonical JSON that the PRODUCTION `Synthesizer.Conclude(inputs, now:)`
/// path emits for the canonical poisoned-local scenario (local 192.168.1.1 -> 127.0.0.1, three public
/// resolvers -> 93.184.216.34, redirection detected, now = 1752268477). It was captured ONCE and
/// committed as a static golden; these tests LOAD it and round-trip it — they never regenerate it from
/// Conclude() at test time. The Swift twin embeds the IDENTICAL literal, so a green run on each
/// platform proves both decode the SAME 4749-byte object and re-encode the SAME bytes. Each happy-path
/// assertion is paired with a mutation that proves it can fail.
public sealed class Stage5PopulatedParityTests
{
    /// The exact 4749-byte canonical object the production path emits for the poisoned-local scenario.
    /// Byte-for-byte identical to the Swift twin's `populatedFixtureJSON`.
    private const string PopulatedFixtureJson =
        """{"answerMatrix":[{"comparison":{"classification":"discrepancy","consensusAnswers":["93.184.216.34"],"localVantageIds":["local"],"outlierVantageIds":["local"],"referenceVantageIds":["cloudflare","google","quad9"]},"entries":[{"canonicalAnswers":["127.0.0.1"],"observationId":"obs:mirror:0:local","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"192.168.1.1","freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"canonicalAnswers":["93.184.216.34"],"observationId":"obs:mirror:0:cloudflare","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"cloudflare"}},{"canonicalAnswers":["93.184.216.34"],"observationId":"obs:mirror:0:google","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"8.8.8.8","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"google"}},{"canonicalAnswers":["93.184.216.34"],"observationId":"obs:mirror:0:quad9","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"9.9.9.9","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"quad9"}}],"queryName":"example.com.","queryType":1,"rowId":"row:mirror:0"}],"completeness":{"criticalAvailable":1,"criticalExpected":3,"expected":6,"expectedProfileId":"profile.sections.v0","observed":1},"dataRegime":"partialData","determination":{"basis":"ordinaryCredalGate","confidence":{"kind":"belief","target":"mechanism:unresolved","value":0.8646647167633873},"mechanismClass":"unresolved","referralRequired":true,"referralToken":"stage5.referral.mechanism_unresolved","state":"tampered","withinClass":[{"displayOrder":0,"hypothesis":"spoofing","share":0.45697995778887185},{"displayOrder":1,"hypothesis":"interception_onPath","share":0.45697995778887185},{"displayOrder":2,"hypothesis":"censorship","share":0.017208016884451293},{"displayOrder":3,"hypothesis":"interception_transport","share":0.017208016884451293},{"displayOrder":4,"hypothesis":"contentTampering","share":0.017208016884451293},{"displayOrder":5,"hypothesis":"privacyLeak","share":0.017208016884451293},{"displayOrder":6,"hypothesis":"localCompromise","share":0.017208016884451293}]},"findings":[],"generatedAt":"2025-07-11T21:14:37.000Z","honesty":{"hardGate":{"blockedVerdicts":["clean"],"requiredSections":["prophet","portal","glutton","cage","witness"],"token":"stage5.hard_gate.required_checks_missing","unavailableDatapointIds":[]},"uncertainty":{"token":"stage5.uncertainty.evidence_thin","value":0.1353352832366127}},"provenance":{"direct":[{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:local","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["127.0.0.1"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"192.168.1.1","freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:cloudflare","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["93.184.216.34"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"cloudflare"}},{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:google","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["93.184.216.34"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"8.8.8.8","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"google"}},{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:quad9","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["93.184.216.34"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"9.9.9.9","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"quad9"}}],"inferred":[],"unavailable":[{"datapointId":"section.prophet","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"prophet"},{"datapointId":"section.portal","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"portal"},{"datapointId":"section.glutton","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"glutton"},{"datapointId":"section.cage","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"cage"},{"datapointId":"section.witness","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"witness"}]},"schemaVersion":1}""";

    private static byte[] FixtureBytes => Encoding.UTF8.GetBytes(PopulatedFixtureJson);

    // Happy path: cross-platform round-trip.
    [Fact]
    public void PopulatedFixtureRoundTripsToParityBytes()
    {
        Assert.Equal(4749, FixtureBytes.Length);
        var decoded = Stage5Presentation.Decode(FixtureBytes);
        Assert.NotNull(decoded);
        Assert.True(decoded!.IsValid, string.Join("; ", decoded.ValidationErrors()));
        Assert.Equal(FixtureBytes, decoded.CanonicalBytes());
    }

    // Semantic anchors — catch a silent structural drift.
    [Fact]
    public void PopulatedFixtureSemanticAnchors()
    {
        var d = Stage5Presentation.Decode(FixtureBytes);
        Assert.NotNull(d);
        Assert.Equal(AnswerComparisonClass.Discrepancy, d!.AnswerMatrix[0].Comparison.Classification);
        Assert.Equal(DeterminationState.Tampered, d.Determination.State);
        Assert.Equal(MechanismClass.Unresolved, d.Determination.MechanismClass);
        Assert.Equal(4, d.Provenance.Direct.Count);
        Assert.Equal(5, d.Provenance.Unavailable.Count);
        Assert.NotEmpty(d.Determination.WithinClass);
        var shareSum = d.Determination.WithinClass.Where(w => w.Share.HasValue).Sum(w => w.Share!.Value);
        Assert.True(Math.Abs(shareSum - 1.0) < 1e-9, $"within-class shares must sum to ~1, got {shareSum}");
    }

    // Negative / mutation cases (each proves a happy-path assertion can fail).

    /// schemaVersion bumped to an unsupported value fails closed at BOTH gates: Decode returns null,
    /// and a raw deserialize that bypasses the guard is rejected by the §13 validator (IsValid false).
    [Fact]
    public void SchemaVersionBumpFailsClosed()
    {
        var bumped = PopulatedFixtureJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
        Assert.Null(Stage5Presentation.Decode(Encoding.UTF8.GetBytes(bumped)));
        var raw = JsonSerializer.Deserialize<Stage5Presentation>(bumped, Stage5Presentation.CanonicalOptions);
        Assert.NotNull(raw);
        Assert.False(raw!.IsValid);
    }

    /// A required enum value that matches no case fails to decode. A pure re-casing is deliberately
    /// avoided (System.Text.Json's enum reader can be case-insensitive, which would make this negative
    /// unable to fail); a clear misspelling fails on both platforms.
    [Fact]
    public void MisspelledEnumValueFailsDecode()
    {
        var mutated = PopulatedFixtureJson.Replace("\"classification\":\"discrepancy\"", "\"classification\":\"discrepency\"");
        Assert.Null(Stage5Presentation.Decode(Encoding.UTF8.GetBytes(mutated)));
    }

    /// Editing a within-class share so the distribution no longer sums to 1 is a §13 violation.
    [Fact]
    public void WithinClassSharesNotSummingToOneAreInvalid()
    {
        var mutated = PopulatedFixtureJson.Replace("\"share\":0.45697995778887185", "\"share\":0.05");
        var d = Stage5Presentation.Decode(Encoding.UTF8.GetBytes(mutated));
        Assert.NotNull(d);
        Assert.NotEmpty(d!.ValidationErrors());
        Assert.Contains(d.ValidationErrors(), e => e.Contains("withinClass shares do not sum to 1"));
    }

    /// Corrupting a direct observation's id leaves an answer entry pointing at a missing observation —
    /// a dangling source link the §13 validator must flag.
    [Fact]
    public void DanglingAnswerObservationLinkIsInvalid()
    {
        var mutated = PopulatedFixtureJson.Replace(
            "\"observationId\":\"obs:mirror:0:local\",\"observedAt\":",
            "\"observationId\":\"obs:mirror:0:corrupted\",\"observedAt\":");
        var d = Stage5Presentation.Decode(Encoding.UTF8.GetBytes(mutated));
        Assert.NotNull(d);
        Assert.NotEmpty(d!.ValidationErrors());
        Assert.Contains(d.ValidationErrors(), e => e.Contains("references missing observation obs:mirror:0:local"));
    }
}

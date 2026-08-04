using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// P1.0 — the additive Stage5Presentation DTO twin (STAGE5-CONTRACT.md §3-§14). Asserts the SAME
/// canonical-JSON literal the Swift test (`Stage5PresentationTests.swift`) asserts, so equality on
/// each platform proves byte-parity between them; plus round-trip, §13 validator accept/reject, an
/// absent object never reading clean, and an unsupported schemaVersion failing closed.
public class Stage5PresentationTests
{
    /// The shared v1 fixture — built IDENTICALLY to the Swift `fixtureV1()`.
    private static Stage5Presentation FixtureV1()
    {
        var localVantage = new VantageIdentity("local", VantageKind.LocalSystem, null, "do53", false, FreshnessState.CurrentRun);
        var public1 = new VantageIdentity("public1", VantageKind.PublicResolver, "1.1.1.1", "do53", false, FreshnessState.CurrentRun);
        var public2 = new VantageIdentity("public2", VantageKind.PublicResolver, "8.8.8.8", "do53", false, FreshnessState.CurrentRun);

        var obsLocal = new DirectObservation("obs.local.a", "target_answer:local", Section.Prophet,
            "2026-07-11T21:14:30.000Z", localVantage, new[] { "203.0.113.5" },
            ObservationValueKind.DnsAnswerSet, new[] { EvidenceId.Redirection });
        var obsPublic = new DirectObservation("obs.public.a", "target_answer:public", Section.Mirror,
            "2026-07-11T21:14:31.000Z", public1, new[] { "198.51.100.10" },
            ObservationValueKind.DnsAnswerSet, System.Array.Empty<EvidenceId>());

        var provenance = new ProvenancePartition(
            new[] { obsLocal, obsPublic },
            new[] { new InferredFinding("find.rewrite.1", MechanismClass.Rewrite, null,
                new[] { "obs.local.a" }, new[] { EvidenceId.Redirection }) },
            new[] { new UnavailableDatapoint("leak", Section.Glutton, UnavailableReason.TimedOut, CopyToken.UnavailableTimedOut) });

        var row = new AnswerMatrixRow("row.target.a", "example.com.", 1, new[]
        {
            new VantageAnswer(localVantage, AnswerStatus.Answered, new[] { "203.0.113.5" }, 0, "obs.local.a"),
            new VantageAnswer(public1, AnswerStatus.Answered, new[] { "198.51.100.10" }, 0, "obs.public.a"),
            new VantageAnswer(public2, AnswerStatus.Answered, new[] { "198.51.100.10" }, 0, null),
        }, new AnswerComparison(AnswerComparisonClass.Discrepancy, new[] { "local" },
            new[] { "public1", "public2" }, new[] { "198.51.100.10" }, new[] { "local" }));

        var honesty = new HonestySignals(
            new HardGateSignal(CopyToken.HardGateRequiredChecksMissing, new[] { VerdictState.Clean },
                new[] { Section.Glutton }, new[] { "leak" }),
            new ScalarHonestySignal(CopyToken.ConflictEvidenceDisagrees, 0.5),
            new ScalarHonestySignal(CopyToken.UncertaintyEvidenceThin, 0.25));

        var determination = new MechanismDetermination(DeterminationState.Tampered, MechanismClass.Rewrite,
            new CredalMeasure(CredalMeasureKind.Belief, 0.4, "mechanism:rewrite"),
            new[]
            {
                new WithinClassEntry(Hypothesis.Spoofing, 0, 0.6),
                new WithinClassEntry(Hypothesis.InterceptionOnPath, 1, 0.4),
            }, DecisionBasis.DecisiveSafetyGate, true, CopyToken.ReferralMechanismUnresolved);

        var findings = new[]
        {
            new PrioritizedFinding("find.rewrite.1", MechanismClass.Rewrite, "find.rewrite.1",
                new[] { "obs.local.a" }, new FindingPriority("syxx.stage5.sd", 1,
                    new PriorityFactors(5, 4, null), 20, 1)),
            new PrioritizedFinding("find.leak.2", MechanismClass.Leak, null,
                new[] { "obs.local.a" }, new FindingPriority("syxx.stage5.sd", 1,
                    new PriorityFactors(3, 3, null), 9, 2)),
        };

        return new Stage5Presentation(1, "2026-07-11T21:14:37.123Z", DataRegime.PartialData,
            new EvidenceCompleteness(4, 6, 2, 3, "full"), provenance, new[] { row }, honesty, determination, findings);
    }

    /// The exact canonical bytes both platforms must emit — IDENTICAL to the Swift literal.
    private const string ExpectedCanonicalJson =
        """{"answerMatrix":[{"comparison":{"classification":"discrepancy","consensusAnswers":["198.51.100.10"],"localVantageIds":["local"],"outlierVantageIds":["local"],"referenceVantageIds":["public1","public2"]},"entries":[{"canonicalAnswers":["203.0.113.5"],"observationId":"obs.local.a","rcode":0,"status":"answered","vantage":{"authenticated":false,"freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"canonicalAnswers":["198.51.100.10"],"observationId":"obs.public.a","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"public1"}},{"canonicalAnswers":["198.51.100.10"],"rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"8.8.8.8","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"public2"}}],"queryName":"example.com.","queryType":1,"rowId":"row.target.a"}],"completeness":{"criticalAvailable":2,"criticalExpected":3,"expected":6,"expectedProfileId":"full","observed":4},"dataRegime":"partialData","determination":{"basis":"decisiveSafetyGate","confidence":{"kind":"belief","target":"mechanism:rewrite","value":0.4},"mechanismClass":"rewrite","referralRequired":true,"referralToken":"stage5.referral.mechanism_unresolved","state":"tampered","withinClass":[{"displayOrder":0,"hypothesis":"spoofing","share":0.6},{"displayOrder":1,"hypothesis":"interception_onPath","share":0.4}]},"findings":[{"findingId":"find.rewrite.1","inferenceId":"find.rewrite.1","mechanismClass":"rewrite","priority":{"factors":{"detectability":4,"severity":5},"modelId":"syxx.stage5.sd","modelVersion":1,"rank":1,"score":20},"sourceObservationIds":["obs.local.a"]},{"findingId":"find.leak.2","mechanismClass":"leak","priority":{"factors":{"detectability":3,"severity":3},"modelId":"syxx.stage5.sd","modelVersion":1,"rank":2,"score":9},"sourceObservationIds":["obs.local.a"]}],"generatedAt":"2026-07-11T21:14:37.123Z","honesty":{"conflict":{"token":"stage5.conflict.evidence_disagrees","value":0.5},"hardGate":{"blockedVerdicts":["clean"],"requiredSections":["glutton"],"token":"stage5.hard_gate.required_checks_missing","unavailableDatapointIds":["leak"]},"uncertainty":{"token":"stage5.uncertainty.evidence_thin","value":0.25}},"provenance":{"direct":[{"datapointId":"target_answer:local","observationId":"obs.local.a","observedAt":"2026-07-11T21:14:30.000Z","rawValues":["203.0.113.5"],"section":"prophet","sourceEvidenceIds":["redirection"],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"datapointId":"target_answer:public","observationId":"obs.public.a","observedAt":"2026-07-11T21:14:31.000Z","rawValues":["198.51.100.10"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"public1"}}],"inferred":[{"findingId":"find.rewrite.1","mechanismClass":"rewrite","sourceEvidenceIds":["redirection"],"sourceObservationIds":["obs.local.a"]}],"unavailable":[{"datapointId":"leak","reason":"timedOut","reasonToken":"stage5.unavailable.timed_out","section":"glutton"}]},"schemaVersion":1}""";

    [Fact]
    public void CanonicalBytesMatchCrossPlatformFixture()
    {
        var json = Encoding.UTF8.GetString(FixtureV1().CanonicalBytes());
        Assert.Equal(ExpectedCanonicalJson, json);
    }

    [Fact]
    public void FixtureRoundTrips()
    {
        var bytes = FixtureV1().CanonicalBytes();
        var decoded = Stage5Presentation.Decode(bytes);
        Assert.NotNull(decoded);
        // Record equality compares IReadOnlyList members by reference, so a decoded copy is never
        // `==` the source; the meaningful round-trip proof is that it re-encodes to the same bytes.
        Assert.Equal(bytes, decoded!.CanonicalBytes());
    }

    [Fact]
    public void ValidFixturePassesValidator() => Assert.Empty(FixtureV1().ValidationErrors());

    [Fact]
    public void DanglingSourceLinkFails()
    {
        var f = FixtureV1();
        var broken = f with
        {
            Provenance = new ProvenancePartition(f.Provenance.Direct,
                new[] { new InferredFinding("find.x", MechanismClass.Rewrite, null,
                    new[] { "obs.does.not.exist" }, System.Array.Empty<EvidenceId>()) },
                f.Provenance.Unavailable)
        };
        Assert.False(broken.IsValid);
    }

    [Fact]
    public void FabricatedConsensusFails()
    {
        var f = FixtureV1();
        var badRow = new AnswerMatrixRow("row.bad", "example.com.", 1, f.AnswerMatrix[0].Entries,
            new AnswerComparison(AnswerComparisonClass.InsufficientReferences, new[] { "local" },
                System.Array.Empty<string>(), new[] { "203.0.113.5" }, System.Array.Empty<string>()));
        var broken = f with { AnswerMatrix = new[] { badRow } };
        Assert.False(broken.IsValid);
    }

    [Fact]
    public void CleanWithBlockingHardGateFails()
    {
        var f = FixtureV1();
        var cleanDet = new MechanismDetermination(DeterminationState.Clean, null, null,
            System.Array.Empty<WithinClassEntry>(), DecisionBasis.OrdinaryCredalGate, false, null);
        var broken = f with { Determination = cleanDet };
        Assert.False(broken.IsValid);
    }

    [Fact]
    public void WrongPriorityScoreFails()
    {
        var f = FixtureV1();
        var badFinding = new PrioritizedFinding("find.rewrite.1", MechanismClass.Rewrite, null,
            new[] { "obs.local.a" }, new FindingPriority("syxx.stage5.sd", 1, new PriorityFactors(5, 4, null), 999, 1));
        var broken = f with { Findings = new[] { badFinding } };
        Assert.False(broken.IsValid);
    }

    [Fact]
    public void LegacyConclusionWithoutStage5DecodesAndIsNotClean()
    {
        // A conclusion persisted before Stage5 existed must decode with Stage5 == null — NOT clean.
        const string json = """{"Verdict":8,"Confidence":0,"Probabilities":[],"Evidence":[],"SectionStatuses":[],"NextSteps":[],"ReferralTriggered":true,"Referral":null,"GeneratedAt":"2026-07-11T00:00:00+00:00"}""";
        var c = JsonSerializer.Deserialize<Conclusion>(json);
        Assert.NotNull(c);
        Assert.Null(c!.Stage5);
        Assert.True(c.IsInconclusive(), "a legacy inconclusive conclusion must decode to a non-committed state");
    }

    [Fact]
    public void UnsupportedSchemaVersionFailsClosed()
    {
        var bytes = FixtureV1().CanonicalBytes();
        var bumped = Encoding.UTF8.GetString(bytes).Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
        Assert.Null(Stage5Presentation.Decode(Encoding.UTF8.GetBytes(bumped)));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// The determinative engine's catch block returns null so no result is fabricated — correct, and
/// honest to the USER. But it was also silent to US: a user got "the engine could not evaluate
/// this run" and nobody ever learned why. A missing determination is how a result goes missing.
///
/// These tests pin that the failure is REPORTED, using a recording sink rather than a live Sentry.
/// That is the point of the vendor-neutral <see cref="IDiagnosticSink"/>: reporting becomes a
/// testable behaviour instead of something you can only verify by watching a dashboard.
///
/// Twin: macOS SyxxKitTests/EngineFailureIsReportedTests.swift.
/// </summary>
[Collection("Diagnostics")]
public class EngineFailureIsReportedTests : IDisposable
{
    private sealed class RecordingSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public int Discards { get; private set; }
        public void Capture(DiagnosticEvent evt) => Events.Add(evt);
        public void Breadcrumb(string category, string message, DiagnosticSeverity level,
                               IReadOnlyDictionary<string, string> data)
        { }
        public void DiscardBuffered() => Discards++;
    }

    private readonly RecordingSink _sink = new();

    public EngineFailureIsReportedTests() => SyxxDiagnostics.Install(_sink);
    public void Dispose() => SyxxDiagnostics.Uninstall();

    /// A duplicate section entry makes EvidenceBundle.Validate() throw, which is the real path to a
    /// null determination. Failure case: delete the Capture call in SynthesisEngine's catch → red.
    [Fact]
    public void EngineFailure_IsCapturedWithNoDeterminationImpact()
    {
        var dup = new[]
        {
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive, 1.0),
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive, 1.0),
        };

        var result = SynthesisEngine.RunDeterminativeEngine(Array.Empty<EvidenceItem>(), dup);

        Assert.Null(result);   // still no fabricated result
        var evt = Assert.Single(_sink.Events);
        Assert.Equal("engine.no_determination", evt.ErrorClass);
        Assert.Equal(MeasurementImpact.NoDetermination, evt.Impact);
        Assert.Equal(DiagnosticSeverity.Error, evt.Severity);
    }

    /// Grouping must key on the error VARIANT. The exception message interpolates section and
    /// indicator ids, so fingerprinting on it would shard one bug across hundreds of issues.
    /// Failure case: fingerprint on ex.Message → the assertion on a stable kind token → red.
    [Fact]
    public void Fingerprint_UsesTheErrorVariantNotTheInterpolatedMessage()
    {
        var dup = new[]
        {
            new SectionStatusEntry(Section.Mirror, SectionStatus.Positive, 1.0),
            new SectionStatusEntry(Section.Mirror, SectionStatus.Positive, 1.0),
        };

        SynthesisEngine.RunDeterminativeEngine(Array.Empty<EvidenceItem>(), dup);

        var evt = Assert.Single(_sink.Events);
        Assert.Equal(2, evt.Fingerprint.Count);
        Assert.Equal("engine.no_determination", evt.Fingerprint[0]);
        Assert.Equal(MatrixEngineErrorKind.DuplicateSectionInBundle.ToString(), evt.Fingerprint[1]);
        // The section id appears in the message but must NOT be in the grouping key.
        Assert.DoesNotContain(evt.Fingerprint, f => f.Contains("mirror", StringComparison.OrdinalIgnoreCase));
    }

    /// PII discipline: the payload carries shape, never user data. Failure case: add the domain or
    /// a resolver address to Extra → red.
    [Fact]
    public void Payload_CarriesShapeNotUserData()
    {
        var dup = new[]
        {
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive, 1.0),
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive, 1.0),
        };

        SynthesisEngine.RunDeterminativeEngine(Array.Empty<EvidenceItem>(), dup);

        var evt = Assert.Single(_sink.Events);
        Assert.True(evt.Extra!.ContainsKey("section_statuses"));
        Assert.True(evt.Tags!.ContainsKey("syxx.engine_error_kind"));
        var blob = string.Join("|", evt.Extra.Values) + "|" + string.Join("|", evt.Tags.Values);
        Assert.DoesNotContain(".", blob.Replace("Syxx.Kit.", "", StringComparison.Ordinal),
            StringComparison.Ordinal);   // no hostnames or dotted-quad addresses
    }

    /// A successful evaluation must report nothing. Failure case: capture unconditionally → red,
    /// which would drown the issue stream in non-events.
    [Fact]
    public void SuccessfulEvaluation_ReportsNothing()
    {
        var ok = new[] { new SectionStatusEntry(Section.Prophet, SectionStatus.Untested, 0.0) };

        SynthesisEngine.RunDeterminativeEngine(Array.Empty<EvidenceItem>(), ok);

        Assert.Empty(_sink.Events);
    }

    /// Consent withdrawal must detach the sink AND discard, not flush. Failure case: make
    /// Uninstall flush, or leave the sink attached → red.
    [Fact]
    public void Uninstall_DetachesAndDiscards_SoNothingIsSentAfterOptOut()
    {
        SyxxDiagnostics.Uninstall();
        Assert.False(SyxxDiagnostics.IsActive);
        Assert.Equal(1, _sink.Discards);

        var dup = new[]
        {
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive, 1.0),
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive, 1.0),
        };
        SynthesisEngine.RunDeterminativeEngine(Array.Empty<EvidenceItem>(), dup);

        Assert.Empty(_sink.Events);   // opted out: nothing captured at all
    }
}

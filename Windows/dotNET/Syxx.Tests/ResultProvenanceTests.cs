using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Windows twin of macOS ResultProvenanceTests. A fleet-wide rise in inconclusive results was
/// uninvestigable: errors were recorded and results were recorded, but nothing linked them.
///
/// Also pins the standing invariant — a section that is untested/invalidated/inconclusive with NO
/// diagnostic recorded against it means some path degraded a measurement silently, which is the
/// defect class this project exists to prevent, made mechanically detectable on every run.
/// </summary>
/// The diagnostics sink is a process-global by design, so any test class that installs one must
/// not run concurrently with another that does. xUnit parallelizes across collections; sharing a
/// collection name serializes these.
[CollectionDefinition("Diagnostics", DisableParallelization = true)]
public class DiagnosticsCollection { }

[Collection("Diagnostics")]
public class ResultProvenanceTests : IDisposable
{
    private sealed class RecordingSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = new();
        public List<(string Category, DiagnosticSeverity Level, IReadOnlyDictionary<string, string> Data)> Crumbs { get; } = new();
        public void Capture(DiagnosticEvent evt) => Events.Add(evt);
        public void Breadcrumb(string category, string message, DiagnosticSeverity level,
                               IReadOnlyDictionary<string, string> data)
            => Crumbs.Add((category, level, data));
        public void DiscardBuffered() { }
    }

    private readonly RecordingSink _sink = new();

    public ResultProvenanceTests()
    {
        SyxxDiagnostics.Install(_sink);
        SyxxDiagnostics.Run = new RunContext();
    }

    public void Dispose()
    {
        SyxxDiagnostics.Run = null;
        SyxxDiagnostics.Uninstall();
    }

    private static Conclusion Make(IReadOnlyList<SectionStatusEntry> statuses,
                                   DeterminativeResult? determination)
        => new(DateTimeOffset.UnixEpoch, statuses, Array.Empty<EvidenceItem>(),
               Array.Empty<Advisory>(), determination);

    /// A null determination is "the engine could not run" — categorically NOT Inconclusive.
    /// Failure case: coerce null to "inconclusive" → red.
    [Fact]
    public void NullDetermination_ReportsNoDetermination_NotInconclusive()
    {
        Synthesizer.ReportResultProvenance(Make(Array.Empty<SectionStatusEntry>(), null));

        var data = _sink.Crumbs.Single().Data;
        Assert.Equal("no_determination", data["syxx.result.state"]);
        Assert.NotEqual("inconclusive", data["syxx.result.state"]);
    }

    /// All four engine states must survive distinctly onto the wire.
    [Fact]
    public void AllFourResultStatesAreDistinctOnTheWire()
    {
        foreach (var state in new[]
                 {
                     DeterminativeState.Untested, DeterminativeState.Inconclusive,
                     DeterminativeState.Clean, DeterminativeState.ThreatDetected,
                 })
        {
            _sink.Crumbs.Clear();
            var det = new DeterminativeResult(state, new List<DiagnosisScore>(),
                                              new List<SuppressedDiagnosis>(), "");
            Synthesizer.ReportResultProvenance(Make(Array.Empty<SectionStatusEntry>(), det));
            Assert.Equal(state.RawValue(), _sink.Crumbs.Single().Data["syxx.result.state"]);
        }
    }

    /// THE INVARIANT. Failure case: delete the unattributed loop → red.
    [Fact]
    public void UnattributedDegradation_IsItsOwnIssue()
    {
        var statuses = new[] { new SectionStatusEntry(Section.Cage, SectionStatus.Untested, 0.0) };
        var det = new DeterminativeResult(DeterminativeState.Inconclusive,
                                          new List<DiagnosisScore>(),
                                          new List<SuppressedDiagnosis>(), "");

        Synthesizer.ReportResultProvenance(Make(statuses, det));

        var evt = Assert.Single(_sink.Events.Where(e => e.ErrorClass == "result.unattributed_degradation"));
        Assert.Equal("cage", evt.Tags!["syxx.section"]);
        Assert.Equal("False", _sink.Crumbs.Single().Data["syxx.attribution_complete"]);
    }

    /// The converse — an explained degradation must NOT be flagged, or the invariant cries wolf.
    [Fact]
    public void AttributedDegradation_IsNotFlagged()
    {
        SyxxDiagnostics.Capture(new DiagnosticEvent(
            ErrorClass: "capture.permission_denied",
            Component: "DNSCapture",
            Section: Section.Cage,
            Impact: MeasurementImpact.UntestedSection,
            Severity: DiagnosticSeverity.Warning,
            Underlying: null,
            Fingerprint: new[] { "capture.permission_denied" }));

        var statuses = new[] { new SectionStatusEntry(Section.Cage, SectionStatus.Untested, 0.0) };
        var det = new DeterminativeResult(DeterminativeState.Inconclusive,
                                          new List<DiagnosisScore>(),
                                          new List<SuppressedDiagnosis>(), "");

        Synthesizer.ReportResultProvenance(Make(statuses, det));

        Assert.Empty(_sink.Events.Where(e => e.ErrorClass == "result.unattributed_degradation"));
        Assert.Equal("True", _sink.Crumbs.Single().Data["syxx.attribution_complete"]);
    }

    /// Abstentions are the interesting case; a decided run is routine.
    [Fact]
    public void Abstention_IsLouderThanADecidedResult()
    {
        var clean = new DeterminativeResult(DeterminativeState.Clean, new List<DiagnosisScore>(),
                                            new List<SuppressedDiagnosis>(), "");
        Synthesizer.ReportResultProvenance(Make(Array.Empty<SectionStatusEntry>(), clean));
        Assert.Equal(DiagnosticSeverity.Warning, _sink.Crumbs.Single().Level);

        _sink.Crumbs.Clear();
        Synthesizer.ReportResultProvenance(Make(Array.Empty<SectionStatusEntry>(), null));
        Assert.Equal(DiagnosticSeverity.Error, _sink.Crumbs.Single().Level);
    }
}

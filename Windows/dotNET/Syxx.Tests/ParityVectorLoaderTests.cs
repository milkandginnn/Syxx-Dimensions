using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// PARITY_VECTOR_V1 loader contract (track cross-platform-parity_20260805, Task 1.4).
/// Twin of macOS/Tests/SyxxKitTests/ParityVectorLoaderTests.swift.
/// </summary>
/// <remarks>
/// <para>Two things are asserted that a loader suite usually omits, both because their absence is
/// how a green run comes to mean nothing:</para>
///
/// <para>1. <b>Every rejection rule has its own test.</b> A loader is mostly rejection logic, and a
/// rejection rule with no test is a rule that may already be a no-op. The five spec rules, the two
/// structural refusals (empty / unreadable directory), the two filename refusals, and the
/// v1-unrepresentable refusal each get one.</para>
///
/// <para>2. <b>The reflection guard on <see cref="SectionInputs"/>.</b> A populated case asserts
/// not only that the fields it names were set, but that every OTHER field was left alone — the
/// evaluated-count discipline of <see cref="MatrixGoldenCorpusTests"/> applied to a struct. Without
/// it, a loader that quietly wrote a default into an unrelated field would pass.</para>
///
/// <para>Fixtures are per-test temp directories. The one exception is
/// <see cref="CanonicalCorpusLoadsWhole"/>, which reads the real corpus — the strongest evidence on
/// the ladder in conductor/workflow.md, because it is the artifact both engines execute.</para>
/// </remarks>
public class ParityVectorLoaderTests
{
    // MARK: - Fixture

    /// <summary>A temp directory that deletes itself. Cases are written by name so a test reads as
    /// the corpus it is describing.</summary>
    private sealed class VectorDir : IDisposable
    {
        public string Path { get; }

        public VectorDir([CallerMemberName] string name = "")
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "syxx-parity-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path);
        }

        public VectorDir Write(string fileName, string json)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), json);
            return this;
        }

        /// <summary>A well-formed case with the given id and <c>sectionInputs</c> body.</summary>
        public VectorDir Case(string fileName, string vectorId, string sectionInputs = "{}",
                              string schemaVersion = "1", string pinned = "1752268477")
            => Write(fileName, $$"""
                {
                  "schemaVersion": {{schemaVersion}},
                  "vectorId": "{{vectorId}}",
                  "pinnedUnixSeconds": {{pinned}},
                  "sectionInputs": {{sectionInputs}}
                }
                """);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a leaked temp dir must never fail a test */ }
        }
    }

    private static ParityVectorException Rejects(Action act)
        => Assert.Throws<ParityVectorException>(act);

    // MARK: - Happy path

    [Fact]
    public void LoadsCasesInSortedOrderWithTheirPinnedClock()
    {
        using var dir = new VectorDir()
            // Written out of order on purpose: enumeration order differs by filesystem, so the
            // ordering claim is only worth something if the input was not already sorted.
            .Case("002_populated.json", "populated",
                  """{"dnssec": "unsigned", "cageAttributionRate": 0.5}""", pinned: "1700000000")
            .Case("001_empty-section-inputs.json", "empty-section-inputs");

        var cases = ParityVectorLoader.Load(dir.Path);

        Assert.Equal(2, cases.Count);
        Assert.Equal(["empty-section-inputs", "populated"], cases.Select(c => c.VectorId));
        Assert.Equal(["001", "002"], cases.Select(c => c.SortKey));
        Assert.Equal(["001_empty-section-inputs.json", "002_populated.json"], cases.Select(c => c.CaseFile));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1752268477), cases[0].PinnedNow);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), cases[1].PinnedNow);

        // The empty case sets nothing at all.
        Assert.Empty(PopulatedFieldNames(cases[0].Inputs));

        // The populated case sets exactly what it names — and nothing else.
        Assert.Equal(DNSSECStatus.Unsigned, cases[1].Inputs.Dnssec);
        Assert.Equal(0.5, cases[1].Inputs.CageAttributionRate);
        Assert.Equal(["CageAttributionRate", "Dnssec"], PopulatedFieldNames(cases[1].Inputs));
    }

    /// <summary>
    /// The names of every <see cref="SectionInputs"/> field left non-default, ordinal-sorted.
    /// </summary>
    /// <remarks>
    /// Reflection is used HERE, in the assertion, precisely because it must never be used in the
    /// loader — see <see cref="IncludeFieldsRegressionGuard"/>. Enumerating all 34 fields rather
    /// than spot-checking a few is what turns "the fields I asked for were set" into "and no others
    /// were", which is the half that catches a loader writing defaults it was never given.
    /// </remarks>
    private static IReadOnlyList<string> PopulatedFieldNames(SectionInputs inputs)
    {
        var fields = typeof(SectionInputs).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(34, fields.Length);   // fails loudly if the model grows and this guard does not

        return fields
            .Where(fi =>
            {
                var v = fi.GetValue(inputs);
                if (v is null) return false;
                // ResolverSubstituted is a non-nullable bool; false is its "not set" state.
                return v is not bool b || b;
            })
            .Select(fi => fi.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The defect the loader exists to not have: <c>JsonSerializerOptions.IncludeFields</c> is false
    /// by default and <see cref="SectionInputs"/> exposes fields, so
    /// <c>JsonSerializer.Deserialize&lt;SectionInputs&gt;</c> returns an all-default instance and
    /// throws nothing. Every section would then read <c>untested</c>, the transcript would diff
    /// clean, and a corpus that exercised nothing would render as coverage.
    /// </summary>
    [Fact]
    public void IncludeFieldsRegressionGuard()
    {
        using var dir = new VectorDir().Case("001_dnssec.json", "dnssec", """{"dnssec": "unsigned"}""");

        var loaded = ParityVectorLoader.Load(dir.Path).Single();

        Assert.Equal(DNSSECStatus.Unsigned, loaded.Inputs.Dnssec);
        Assert.NotNull(loaded.Inputs.Dnssec);   // the reflection path would leave this null
    }

    /// <summary>
    /// The loaded case actually drives the engine: a `dnssec` vector must move Prophet off
    /// <see cref="SectionStatus.Untested"/>. A loader that parsed cleanly but handed the engine an
    /// empty <see cref="SectionInputs"/> would satisfy every other test in this file and produce a
    /// transcript in which nothing was measured.
    /// </summary>
    [Fact]
    public void LoadedCaseDrivesTheEngine()
    {
        using var dir = new VectorDir().Case("001_dnssec.json", "dnssec", """{"dnssec": "unsigned"}""");

        var loaded = ParityVectorLoader.Load(dir.Path).Single();
        var conclusion = Synthesizer.Conclude(loaded.Inputs, now: loaded.PinnedNow);

        var prophet = conclusion.SectionStatuses.Single(s => s.Section == Section.Prophet);
        Assert.NotEqual(SectionStatus.Untested, prophet.Status);
        Assert.Equal(loaded.PinnedNow.UtcDateTime, conclusion.GeneratedAt);
    }

    // MARK: - Spec rule 1 — schemaVersion

    [Fact]
    public void RejectsUnrecognisedSchemaVersion()
    {
        using var dir = new VectorDir().Case("001_v.json", "v", schemaVersion: "2");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_v.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unrecognised schemaVersion 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonIntegerSchemaVersion()
    {
        using var dir = new VectorDir().Case("001_v.json", "v", schemaVersion: "\"1\"");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("unrecognised schemaVersion (not an integer)", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Spec rule 2 — vectorId

    [Fact]
    public void RejectsVectorIdDisagreeingWithFilename()
    {
        using var dir = new VectorDir().Case("001_declared-name.json", "a-different-name");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_declared-name.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'a-different-name'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'declared-name'", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Spec rule 3 — sectionInputs keys

    /// <summary>
    /// `resolverSubstituted` is the case the contract argues from by name: both platforms now carry
    /// the field (spec Amendment A1) but v1's scope is 32, so accepting it here while the twin
    /// dropped it would let a field that caps the Mirror section be recorded as covered by a vector
    /// that never exercised it.
    /// </summary>
    [Fact]
    public void RejectsResolverSubstitutedByName()
    {
        using var dir = new VectorDir().Case("001_sub.json", "sub", """{"resolverSubstituted": true}""");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_sub.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown sectionInputs key 'resolverSubstituted'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("refused rather than dropped", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTransportObservationByName()
    {
        using var dir = new VectorDir().Case("001_obs.json", "obs", """{"transportObservation": {}}""");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("unknown sectionInputs key 'transportObservation'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownNestedMemberByName()
    {
        using var dir = new VectorDir().Case("001_ecs.json", "ecs",
            """{"ecs": {"status": "untested", "detail": "not run", "scopePrefixLenght": 24}}""");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_ecs.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown member 'scopePrefixLenght'", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Spec rule 4 — valid JSON

    [Fact]
    public void RejectsInvalidJson()
    {
        using var dir = new VectorDir().Write("001_broken.json", "{ \"schemaVersion\": 1, ");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_broken.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not valid JSON", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Spec rule 5 — pinnedUnixSeconds

    [Fact]
    public void RejectsMissingPinnedUnixSeconds()
    {
        using var dir = new VectorDir().Write("001_nopin.json", """
            {"schemaVersion": 1, "vectorId": "nopin", "sectionInputs": {}}
            """);

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_nopin.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("pinnedUnixSeconds is unusable — absent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsNonIntegerPinnedUnixSeconds()
    {
        using var dir = new VectorDir().Case("001_pin.json", "pin", pinned: "1752268477.5");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("pinnedUnixSeconds is unusable — not an integer", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Top-level shape

    [Fact]
    public void RejectsUnknownTopLevelKey()
    {
        using var dir = new VectorDir().Write("001_extra.json", """
            {"schemaVersion": 1, "vectorId": "extra", "pinnedUnixSeconds": 1752268477,
             "sectionInputs": {}, "expectedRecord": {}}
            """);

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_extra.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown top-level key 'expectedRecord'", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// System.Text.Json resolves a repeated member by last-wins, so without this the earlier value —
    /// which the author wrote and believed was exercised — disappears with no diagnostic.
    /// </summary>
    [Fact]
    public void RejectsDuplicateJsonMember()
    {
        using var dir = new VectorDir().Case("001_dup.json", "dup",
            """{"dnssec": "unsigned", "dnssec": "inconclusive"}""");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("duplicate JSON member 'dnssec'", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Structural refusals

    [Fact]
    public void RejectsEmptyDirectory()
    {
        using var dir = new VectorDir();

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("contains no case files", ex.Message, StringComparison.Ordinal);
        Assert.Contains("refusing to run vacuously", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingDirectory()
    {
        var missing = Path.Combine(Path.GetTempPath(), "syxx-parity-absent-" + Guid.NewGuid().ToString("N"));

        var ex = Rejects(() => ParityVectorLoader.Load(missing));

        Assert.Contains("could not be read", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateSortPrefix()
    {
        using var dir = new VectorDir()
            .Case("001_alpha.json", "alpha")
            .Case("001_beta.json", "beta");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("duplicate sort prefix '001'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("001_alpha.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("001_beta.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCaseFilenameWithoutSortPrefix()
    {
        using var dir = new VectorDir().Case("no-prefix.json", "no-prefix");

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("no-prefix.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("three-digit sort prefix", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - v1-unrepresentable

    /// <summary>
    /// `drifts` is in the shared 32-field set, so it is not an unknown key — it is a known key this
    /// engine cannot represent, and it says which. <c>AnswerDrift</c> carries a <c>DateTime</c>
    /// (Trust.cs:33) and the contract admits no dates.
    /// </summary>
    [Fact]
    public void RejectsDriftsAsUnrepresentableInV1()
    {
        using var dir = new VectorDir().Case("001_drift.json", "drift", """
            {"drifts": [{"domain": "example.com", "queryType": "A", "previousAnswer": "1.1.1.1",
                         "newAnswer": "2.2.2.2", "resolver": "8.8.8.8", "detectedAt": 0}]}
            """);

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("001_drift.json", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'drifts'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unrepresentable in v1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("carries a date", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The members this engine accepts and then cannot hold. Pinned so the list cannot grow by
    /// accident: each entry is a silent drop that is only tolerable while nothing reads it, and a
    /// new entry appearing without a decision is how that stops being true.
    /// </summary>
    [Fact]
    public void UnrepresentedMembersAreExactlyTheKnownEight()
    {
        Assert.Equal(
        [
            "exfil[].listsEvaluated",
            "network.scopedResolvers[].isMDNS",
            "network.scopedResolvers[].isReachable",
            "network.scopedResolvers[].isSupplemental",
            "resolution.attemptCount",
            "resolution.conflictingCNAMEOwnerCount",
            "resolution.offClassAnswerCount",
            "resolution.spoofedPacketCount",
        ], ParityVectorLoader.UnrepresentedMembers);
    }

    /// <summary>
    /// A member only the C# model has is refused, because the Swift twin's synthesized decoder would
    /// silently ignore it and the same vector would then mean two different things.
    /// </summary>
    [Fact]
    public void RejectsCSharpOnlyMemberSpelling()
    {
        using var dir = new VectorDir().Case("001_leak.json", "leak", """
            {"leak": {"result": "inconclusive", "observedResolvers": [],
                      "transportPinning": "notConfigured", "signatureVerification": "valid"}}
            """);

        var ex = Rejects(() => ParityVectorLoader.Load(dir.Path));

        Assert.Contains("'signatureVerification'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("only one of the two engines", ex.Message, StringComparison.Ordinal);
    }

    // MARK: - Evidence from the shared artifact

    /// <summary>
    /// The corpus both engines execute (plan Task 1.6). This is the only test here whose fixture is
    /// not synthetic, and it is the one that would catch a wire-shape disagreement the hand-written
    /// fixtures above cannot: they were written from the same reading of the model that the loader
    /// was.
    /// </summary>
    [Fact]
    public void CanonicalCorpusLoadsWhole()
    {
        var corpus = Path.Combine(RepoRoot(), "corpus", "parity-vectors");
        if (!Directory.Exists(corpus))
            throw new DirectoryNotFoundException($"Parity vector corpus not found at {corpus}. The corpus is missing.");

        var expected = Directory.GetFiles(corpus, "*.json").Length;
        Assert.True(expected > 0, $"{corpus} holds no .json cases; refusing to pass vacuously.");

        var cases = ParityVectorLoader.Load(corpus);

        // evaluated == case_count, asserted rather than assumed (conductor/workflow.md).
        Assert.Equal(expected, cases.Count);
        Assert.Equal(
            Directory.GetFiles(corpus, "*.json")
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal),
            cases.Select(c => c.CaseFile));

        // Every case must reach the engine, not merely parse.
        int concluded = 0;
        foreach (var c in cases)
        {
            var conclusion = Synthesizer.Conclude(c.Inputs, now: c.PinnedNow);
            Assert.Equal(6, conclusion.SectionStatuses.Count);
            concluded++;
        }
        Assert.Equal(cases.Count, concluded);
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Windows/dotNET/Syxx.Tests/ParityVectorLoaderTests.cs
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("Cannot resolve source directory.");
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }
}

/// <summary>
/// The corpus emitter (track cross-platform-parity_20260805, Task 2.1) — the transcript envelope,
/// its <c>evaluated</c> counter, and the shared record builder.
/// </summary>
/// <remarks>
/// <para>These tests SPAWN THE REAL CLI. That is forced rather than chosen:
/// <c>ParityTranscriptCommand</c> is <c>internal</c> to the <c>Syxx</c> executable and
/// <c>Syxx.Tests.csproj</c> references that project with <c>ReferenceOutputAssembly="false"</c>, so
/// its types are unreachable in-process. Same idiom, and the same reference, as
/// <see cref="WitnessVerifyExitCodeTests"/>.</para>
///
/// <para>It also happens to be the right level: the guarantees under test are about a written file
/// and an exit code, and "refused to write" is not observable from a function return.</para>
/// </remarks>
public sealed class ParityVectorTranscriptEmitterTests : IDisposable
{
    private readonly string _dir;

    public ParityVectorTranscriptEmitterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "syxx-transcript-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a leaked temp dir must never fail a test */ }
    }

    private string Corpus(params (string File, string VectorId, string SectionInputs)[] cases)
    {
        var dir = Path.Combine(_dir, "corpus-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        foreach (var (file, id, inputs) in cases)
        {
            File.WriteAllText(Path.Combine(dir, file), $$"""
                {
                  "schemaVersion": 1,
                  "vectorId": "{{id}}",
                  "pinnedUnixSeconds": 1752268477,
                  "sectionInputs": {{inputs}}
                }
                """);
        }
        return dir;
    }

    // MARK: - The envelope

    [Fact]
    public void EmitsOneRecordPerCaseInCaseOrder()
    {
        var corpus = Corpus(
            ("002_populated.json", "populated", """{"dnssec": "unsigned"}"""),
            ("001_empty.json", "empty", "{}"),
            ("003_ecs.json", "ecs", """{"ecs": {"status": "forwarded", "scopePrefixLength": 24, "detail": "scope /24"}}"""));
        var outPath = Path.Combine(_dir, "transcript.json");

        var run = RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={outPath}");
        Assert.True(run.Code == 0, $"expected exit 0, got {run.Code}.\n{run.Output}");

        var root = JsonNode.Parse(File.ReadAllText(outPath))!.AsObject();
        Assert.Equal(1, (int)root["schemaVersion"]!);
        Assert.Equal(3, (int)root["caseCount"]!);
        Assert.Equal(3, (int)root["evaluated"]!);

        var records = root["records"]!.AsArray();
        Assert.Equal(3, records.Count);
        Assert.Equal(["empty", "populated", "ecs"],
                     records.Select(r => (string)r!.AsObject()["vectorId"]!));

        // Envelope key order is part of the contract with the Swift twin's reader.
        Assert.Equal(["schemaVersion", "caseCount", "evaluated", "fingerprint", "records"],
                     root.Select(kv => kv.Key));
    }

    /// <summary>
    /// `evaluated` must equal the number of case FILES on disk, not merely the length of whatever
    /// the emitter chose to write. Deriving it from the records array would make the two agree by
    /// construction — which is how a harness comes to report full coverage of a corpus it partly
    /// skipped, the defect this envelope exists to close.
    /// </summary>
    [Fact]
    public void EvaluatedEqualsTheNumberOfCaseFilesOnDisk()
    {
        var corpus = Corpus(
            ("001_a.json", "a", "{}"),
            ("002_b.json", "b", "{}"),
            ("003_c.json", "c", "{}"),
            ("004_d.json", "d", "{}"));
        var outPath = Path.Combine(_dir, "t.json");

        var run = RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={outPath}");
        Assert.True(run.Code == 0, $"expected exit 0, got {run.Code}.\n{run.Output}");

        var onDisk = Directory.GetFiles(corpus, "*.json").Length;
        var root = JsonNode.Parse(File.ReadAllText(outPath))!.AsObject();

        Assert.Equal(4, onDisk);
        Assert.Equal(onDisk, (int)root["caseCount"]!);
        Assert.Equal(onDisk, (int)root["evaluated"]!);
        Assert.Equal(onDisk, root["records"]!.AsArray().Count);
    }

    // MARK: - The engine actually sees the inputs

    /// <summary>
    /// The emitter's whole purpose: a populated case must produce a DIFFERENT record from the empty
    /// one. If the loader parsed a case and then handed the engine an empty `SectionInputs`, every
    /// record in an 11-case transcript would be identical, the cross-platform diff would be clean,
    /// and a corpus that exercised nothing would render as coverage.
    /// </summary>
    [Fact]
    public void PopulatedCaseProducesADifferentRecordThanTheEmptyCase()
    {
        var corpus = Corpus(
            ("001_empty-section-inputs.json", "empty-section-inputs", "{}"),
            ("002_dnssec.json", "dnssec", """{"dnssec": "unsigned"}"""));
        var outPath = Path.Combine(_dir, "t.json");

        var run = RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={outPath}");
        Assert.True(run.Code == 0, $"expected exit 0, got {run.Code}.\n{run.Output}");

        var records = JsonNode.Parse(File.ReadAllText(outPath))!.AsObject()["records"]!.AsArray();
        var empty = records[0]!.AsObject();
        var populated = records[1]!.AsObject();

        Assert.Equal(6, (int)empty["evidenceCompleteness"]!["untestedSections"]!);
        Assert.Equal("untested", (string)empty["determinationState"]!);

        // Prophet ran, so one fewer section is untested and the run no longer reads as never-run.
        Assert.Equal(5, (int)populated["evidenceCompleteness"]!["untestedSections"]!);
        Assert.NotEqual((string)empty["determinationState"]!, (string)populated["determinationState"]!);
    }

    // MARK: - No partial success

    [Fact]
    public void MalformedCaseRefusesTheWholeTranscript()
    {
        var corpus = Corpus(
            ("001_good.json", "good", "{}"),
            ("002_bad.json", "bad", """{"resolverSubstituted": true}"""));
        var outPath = Path.Combine(_dir, "t.json");

        var run = RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={outPath}");

        Assert.Equal(3, run.Code);
        Assert.Contains("002_bad.json", run.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(outPath), "a refused corpus must leave no transcript behind");
    }

    [Fact]
    public void EmptyCorpusRefusesToWrite()
    {
        var corpus = Path.Combine(_dir, "empty-corpus");
        Directory.CreateDirectory(corpus);
        var outPath = Path.Combine(_dir, "t.json");

        var run = RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={outPath}");

        Assert.Equal(3, run.Code);
        Assert.False(File.Exists(outPath), "a zero-case corpus must not produce a transcript");
    }

    // MARK: - The shared builder

    /// <summary>
    /// The refactor's actual invariant: both paths build records through ONE builder, so a record
    /// emitted alone and a record emitted inside the envelope have the same members in the same
    /// order. A second copy of the builder would drift here first, and the cross-platform diff would
    /// blame the engines for it.
    /// </summary>
    [Fact]
    public void SingleVectorAndCorpusRecordsShareOneShape()
    {
        var singleOut = Path.Combine(_dir, "single.json");
        var single = RunSyxx("parity-transcript", $"--out={singleOut}");
        Assert.True(single.Code == 0, $"expected exit 0, got {single.Code}.\n{single.Output}");

        var corpus = Corpus(("001_empty-section-inputs.json", "empty-section-inputs", "{}"));
        var corpusOut = Path.Combine(_dir, "corpus.json");
        var run = RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={corpusOut}");
        Assert.True(run.Code == 0, $"expected exit 0, got {run.Code}.\n{run.Output}");

        var singleRoot = JsonNode.Parse(File.ReadAllText(singleOut))!.AsObject();
        var corpusRecord = JsonNode.Parse(File.ReadAllText(corpusOut))!
            .AsObject()["records"]!.AsArray()[0]!.AsObject();

        Assert.Equal(singleRoot.Select(kv => kv.Key), corpusRecord.Select(kv => kv.Key));

        // Same inputs and the same clock, so the two records are identical value-for-value too.
        Assert.Equal(singleRoot.ToJsonString(), corpusRecord.ToJsonString());
    }

    /// <summary>Every transcript ends with exactly one newline, on both paths.</summary>
    [Fact]
    public void TranscriptsEndWithASingleTrailingNewline()
    {
        var singleOut = Path.Combine(_dir, "single.json");
        Assert.Equal(0, RunSyxx("parity-transcript", $"--out={singleOut}").Code);

        var corpus = Corpus(("001_a.json", "a", "{}"));
        var corpusOut = Path.Combine(_dir, "corpus.json");
        Assert.Equal(0, RunSyxx("parity-transcript", $"--vectors={corpus}", $"--out={corpusOut}").Code);

        foreach (var path in new[] { singleOut, corpusOut })
        {
            var text = File.ReadAllText(path);
            Assert.EndsWith("}\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\n\n", text[^2..], StringComparison.Ordinal);
        }
    }

    // MARK: - Process plumbing

    private readonly record struct Run(int Code, string Output);

    private static Run RunSyxx(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(CliAssemblyPath());
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("could not start the Syxx CLI");
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new Run(p.ExitCode, stdout + stderr);
    }

    /// <summary>
    /// Locate the CLI built alongside this test assembly, exactly as
    /// <see cref="WitnessVerifyExitCodeTests"/> does. Throws rather than skipping when it is
    /// missing: a silently-skipped emitter test is indistinguishable from a passing one.
    /// </summary>
    private static string CliAssemblyPath()
    {
        var tfmDir = new DirectoryInfo(AppContext.BaseDirectory);
        string tfm = tfmDir.Name;
        string config = tfmDir.Parent!.Name;
        var solutionDir = tfmDir.Parent!.Parent!.Parent!.Parent!;   // bin -> Syxx.Tests -> dotNET
        string candidate = Path.Combine(solutionDir.FullName, "Syxx", "bin", config, tfm, "Syxx.dll");
        if (File.Exists(candidate)) return candidate;

        throw new FileNotFoundException(
            $"the Syxx CLI was not found at {candidate}. Build it with " +
            "`dotnet build Syxx/Syxx.csproj` first. Failing rather than skipping on purpose.",
            candidate);
    }
}

using System;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // MonitorReportAnalyzer had ZERO tests (audit 2026-07-16) — ROADMAP TOP-TEN #10. It is
    // verdict-bearing AND it parses untrusted input: NDJSON off disk becomes SectionInputs becomes
    // a full Conclusion. Both halves of that matter:
    //
    //   * an EMPTY or unparsable capture must NEVER read as clean — the same A024-shaped lie the
    //     server tells if it signs `leaked:false` on zero observations. "We captured nothing" and
    //     "we captured proof of safety" are not the same sentence.
    //   * ParseNDJSON swallows every exception per-line (`catch { }`), so malformed input silently
    //     yields FEWER records rather than an error. That is the right call for a best-effort
    //     forensic parser, but it means a truncated file degrades quietly — and the verdict built
    //     on it must stay honest about how little it saw.
    public class MonitorReportAnalyzerTests
    {
        private static string Line(string domain = "example.com", string direction = "outbound",
                                  string ts = "2026-07-16T12:00:00Z")
            => "{\"Process\":{\"Name\":\"curl\"}," +
               $"\"Timestamp\":\"{ts}\"," +
               $"\"Packet\":{{\"Questions\":[{{\"Question Name\":\"{domain}\"}}]}}," +
               $"\"Network\":{{\"Direction\":\"{direction}\",\"Source Port\":54321,\"Dest Port\":53}}}}";

        // MARK: - The honesty gate

        [Fact]
        public void EmptyCapture_IsNeverClean()
        {
            var a = MonitorReportAnalyzer.Analyze("");

            Assert.Equal(0, a.RecordCount);
            Assert.False(a.Conclusion.ReadsClean(), "an empty/unparsable capture must never commit to clean");
            // And every surface built on it must agree.
            Assert.Equal(Grade.Ungraded, ReportCards.For(a.Conclusion).Grade);
            Assert.NotEqual(Severity.Clean, TrayIndicators.For(a.Conclusion).Severity);
        }

        [Fact]
        public void WhitespaceOnlyCapture_IsNeverClean()
        {
            var a = MonitorReportAnalyzer.Analyze("\n\n   \n\t\n");
            Assert.Equal(0, a.RecordCount);
            Assert.False(a.Conclusion.ReadsClean(), "an empty/unparsable capture must never commit to clean");
        }

        [Fact]
        public void EntirelyUnparsableCapture_IsNeverClean_AndNeverThrows()
        {
            // A truncated or corrupted file must degrade to "we saw nothing", not to "all good"
            // and not to a crash.
            var a = MonitorReportAnalyzer.Analyze("{not json\n}}}\n<html>oops</html>\n\0\x01");
            Assert.Equal(0, a.RecordCount);
            Assert.False(a.Conclusion.ReadsClean(), "an empty/unparsable capture must never commit to clean");
        }

        // MARK: - Hostile / malformed input must not throw

        [Theory]
        [InlineData("")]
        [InlineData("\n")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"Packet\":null}")]
        [InlineData("{\"Network\":{\"Direction\":null}}")]          // NRE'd StubEntropyProbe:60 before the boundary fix
        [InlineData("{\"Network\":{\"Direction\":\"outbound\"},\"Packet\":null}")]
        [InlineData("{\"Network\":null,\"Packet\":null,\"Process\":null,\"Timestamp\":null}")]
        [InlineData("{\"Network\":{\"Direction\":\"outbound\",\"Source IP\":null,\"Dest IP\":null}}")]
        [InlineData("{\"Packet\":{\"Questions\":[]},\"Network\":{\"Direction\":\"outbound\"}}")]
        [InlineData("{\"Timestamp\":\"not-a-date\",\"Network\":{\"Direction\":\"outbound\"},\"Packet\":{\"Questions\":[{\"Question Name\":\"a.com\"}]}}")]
        public void MalformedLines_NeverThrow(string line)
        {
            var ex = Record.Exception(() => MonitorReportAnalyzer.Analyze(line));
            Assert.Null(ex);
        }

        [Fact]
        public void AGiantSingleLine_DoesNotThrow()
        {
            var huge = "{\"Packet\":{\"Questions\":[{\"Question Name\":\"" + new string('a', 100_000) + ".com\"}]}," +
                       "\"Network\":{\"Direction\":\"outbound\"},\"Timestamp\":\"2026-07-16T12:00:00Z\"}";
            Assert.Null(Record.Exception(() => MonitorReportAnalyzer.Analyze(huge)));
        }

        // MARK: - Parsing is honest about what it kept

        [Fact]
        public void ParseNDJSON_SkipsBadLinesButKeepsGoodOnes()
        {
            // The mutation guard for the empty-capture tests: real records DO parse. Without this,
            // "parse nothing, ever" would satisfy every honesty test above.
            var text = string.Join("\n", "{bad", Line("a.com"), "", "also-bad", Line("b.com"));
            var records = MonitorReportAnalyzer.ParseNDJSON(text);

            Assert.Equal(2, records.Count);
            Assert.Equal("a.com", records[0].Packet.Questions[0].QuestionName);
            Assert.Equal("b.com", records[1].Packet.Questions[0].QuestionName);
        }

        [Fact]
        public void RecordCount_ReflectsOnlyWhatActuallyParsed_NotTheLineCount()
        {
            // Honesty about coverage: a 5-line file where 3 lines are junk saw TWO packets, and the
            // report must say two — not five, and not silently imply full coverage.
            var text = string.Join("\n", "{bad", Line("a.com"), "junk", Line("b.com"), "]]]");
            var a = MonitorReportAnalyzer.Analyze(text);
            Assert.Equal(2, a.RecordCount);
        }

        [Fact]
        public void DirectionSplit_CountsOutboundAndInboundSeparately()
        {
            var text = string.Join("\n", Line("a.com", "outbound"), Line("b.com", "outbound"),
                                         Line("c.com", "inbound"));
            var a = MonitorReportAnalyzer.Analyze(text);

            Assert.Equal(3, a.RecordCount);
            Assert.Equal(2, a.OutboundQueries);
            Assert.Equal(1, a.InboundResponses);
            Assert.Equal(a.RecordCount, a.OutboundQueries + a.InboundResponses);
        }

        [Fact]
        public void SectionInputsFrom_EmptyRecords_LeavesAuditsUntested_NotClean()
        {
            // The seam the Conclusion is built from: no records means the probes have nothing to
            // audit, and "untested" must propagate rather than a clean default.
            var inputs = MonitorReportAnalyzer.SectionInputsFrom(new System.Collections.Generic.List<DNSCaptureRecord>());
            var c = Synthesizer.Conclude(inputs);
            Assert.False(c.ReadsClean(), "a capture-only run must never commit to clean");
            Assert.True(c.HasHardGatedSection, "a capture-only run with no records must stay hard-gated");
        }

        [Fact]
        public void Analyze_IsDeterministic_ForTheSameInput()
        {
            var text = string.Join("\n", Line("a.com"), Line("b.com"));
            var now = DateTimeOffset.UnixEpoch;
            Assert.Equal(MonitorReportAnalyzer.Analyze(text, now).Conclusion.Diagnosis,
                         MonitorReportAnalyzer.Analyze(text, now).Conclusion.Diagnosis);
            Assert.Equal(MonitorReportAnalyzer.Analyze(text, now).Conclusion.Conclusivity,
                         MonitorReportAnalyzer.Analyze(text, now).Conclusion.Conclusivity);
        }
    }
}

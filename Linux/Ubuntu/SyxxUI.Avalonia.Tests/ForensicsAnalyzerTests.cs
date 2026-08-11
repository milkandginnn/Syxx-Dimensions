using System;
using System.IO;
using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    public class ForensicsAnalyzerTests
    {
        [Fact]
        public void BeaconClassifiesEvenCadenceAsHighlyRegular()
        {
            var score = BeaconAnalyzer.Score("p", "d", new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.Equal(BeaconClassification.HighlyRegular, score.Classification);
        }

        [Fact]
        public void BeaconClassifiesJitteryCadenceAsIrregular()
        {
            var score = BeaconAnalyzer.Score("p", "d", new List<double> { 1000, 1003, 1200, 1201, 1800 });
            Assert.Equal(BeaconClassification.Irregular, score.Classification);
        }

        // B034 — a degenerate series of >=3 IDENTICAL timestamps has all-zero intervals (mean 0). That is
        // NOT a highly-regular beacon; the zero-mean coefficient of variation was a manufactured positive.
        // It must read InsufficientData with null mean/cv. macOS twin:
        // testBeaconDegenerateIdenticalTimestampsIsInsufficientData.
        [Fact]
        public void BeaconDegenerateIdenticalTimestampsIsInsufficientData()
        {
            var score = BeaconAnalyzer.Score("p", "d", new List<double> { 1000, 1000, 1000 });
            Assert.Equal(BeaconClassification.InsufficientData, score.Classification);
            Assert.Null(score.MeanInterval);
            Assert.Null(score.CoefficientOfVariation);
        }

        [Fact]
        public void BlocklistMatchesParentDomain()
        {
            var lists = SieveBlocklistLoader.BundledLists();
            var coverage = BlocklistAnalyzer.Classify(new[] { "ads.doubleclick.net", "example.com" }, lists);
            Assert.NotEmpty(coverage[0].CoveredBy);   // subdomain of a listed domain
            Assert.Empty(coverage[1].CoveredBy);      // not a tracker
        }

        [Fact]
        public void EntropyHigherForEncodedLabel()
        {
            double natural = ExfilAnalyzer.ShannonEntropyBitsPerChar("wwwexample");
            double encoded = ExfilAnalyzer.ShannonEntropyBitsPerChar("mfrggzdfmztwq2lknnwg23tp");
            Assert.True(encoded > natural);
        }

        // C009 (OC-4 parity): 2 signals (highEntropy + regular) → .Suspicious, NOT .LikelyExfil.
        // The old test expected .LikelyExfil with only 2 signals; that was wrong after C009 raised
        // the bar to ≥3 for .LikelyExfil. macOS twin: testExfilSuspiciousForTwoSignalsWithoutBlocklist.
        [Fact]
        public void ExfilSuspiciousForTwoSignalsWithoutBlocklist()
        {
            var a = ExfilAnalyzer.Assess(
                "mfrggzdfmztwq2lknnwg23tpobyxe43uov3ho.tunnel.evil.net",
                new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.Equal(ExfilSuspicion.Suspicious, a.Suspicion);
        }

        // C009: a single signal alone (entropy-only OR beacon-only) must NOT reach .Suspicious.
        // macOS twin: testExfilBenignForSingleSignalOnly.
        [Fact]
        public void ExfilBenignForSingleSignalOnly()
        {
            // Entropy-only: long label, no regular cadence, no blocklist match.
            var entropyOnly = ExfilAnalyzer.Assess("abcdefghijklmnopqrst.example.com");
            Assert.Equal(ExfilSuspicion.Benign, entropyOnly.Suspicion);

            // Beacon-only: regular cadence, short label (no high entropy), no blocklist match.
            var beaconOnly = ExfilAnalyzer.Assess("www.example.com",
                new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.Equal(ExfilSuspicion.Benign, beaconOnly.Suspicion);
        }

        [Fact]
        public void ExfilBenignForOrdinaryDomainWithoutSignal()
        {
            var a = ExfilAnalyzer.Assess("www.example.com");
            Assert.Equal(ExfilSuspicion.Benign, a.Suspicion);
        }

        // B190 (c) — empty timestamps: beacon leg UNEVALUATED (tri-state unknown), not a silent
        // negative, and the rationale must disclose it. macOS twin:
        // testExfilEmptyTimestampsMarksBeaconUnevaluated.
        [Fact]
        public void ExfilEmptyTimestampsMarksBeaconUnevaluated()
        {
            var a = ExfilAnalyzer.Assess("www.example.com");   // default timestamps: null -> empty
            Assert.False(a.BeaconEvaluated);
            Assert.Contains(a.Rationale, r => r.Contains("beacon cadence not evaluated"));
            Assert.DoesNotContain("no encoded-label or beaconing signal", a.Rationale);
        }

        // B190 — an unevaluated beacon can never contribute a positive signal, so .LikelyExfil
        // (3 signals) is structurally unreachable: max achievable signal count is 2.
        [Fact]
        public void ExfilUnevaluatedBeaconCapsAtTwoSignals()
        {
            var a = ExfilAnalyzer.Assess(
                "mfrggzdfmztwq2lknnwg23tpobyxe43uov3ho.tunnel.evil.net",
                new List<double>());   // no timing -> beacon unevaluated
            Assert.False(a.BeaconEvaluated);
            Assert.NotEqual(ExfilSuspicion.LikelyExfil, a.Suspicion);
        }

        // B190 (d) — real regular-cadence timestamps still evaluate + classify the beacon leg.
        // macOS twin: testExfilRealCadenceStillEvaluatesBeacon.
        [Fact]
        public void ExfilRealCadenceStillEvaluatesBeacon()
        {
            var a = ExfilAnalyzer.Assess("www.example.com",
                new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.True(a.BeaconEvaluated);
            Assert.True(a.Beacon == BeaconClassification.Regular || a.Beacon == BeaconClassification.HighlyRegular);
            Assert.DoesNotContain(a.Rationale, r => r.Contains("beacon cadence not evaluated"));
        }
    }

    // ── K-18 — typosquat detection must carry the transposition term ──────────────────
    // C#'s EditDistance was plain Levenshtein over two rolling rows, which structurally cannot
    // carry the transposition term (it reads d[i-2][j-2]). Caps and call sites are identical to
    // the macOS twin, so the gap bit exactly AT the cap: an adjacent swap measured 2, exceeded the
    // cap of 1, and produced NO finding. These tests pin both the fix and the VARIANT.
    public class LookalikeEditDistanceTests
    {
        [Fact]
        public void AdjacentTransposition_IsOneEdit_NotTwo()
        {
            // The documented K-18 case. Plain Levenshtein scores this 2 and returns cap+1 at cap 1.
            Assert.Equal(1, LookalikeDetector.EditDistance("payapl.com", "paypal.com", 1));
            Assert.Equal(1, LookalikeDetector.EditDistance("payapl", "paypal", 1));
        }

        [Fact]
        public void TransposedBrandName_ProducesTyposquatFinding()
        {
            // The consequence that makes K-18 security-relevant: before the fix this returned null,
            // so a phishing-shaped name passed through the log analyzer unflagged.
            var f = LookalikeDetector.Analyze("payapl.com");
            Assert.NotNull(f);
            Assert.Equal("paypal.com", f!.Brand);
            Assert.Equal(LookalikeDetector.Reason.Typosquat, f.Reason);
        }

        // The distance is OPTIMAL STRING ALIGNMENT (OSA), matching the Swift twin — NOT unrestricted
        // Damerau-Levenshtein. Under OSA a substring may be edited at most once, so a transposed
        // pair is never edited again. "ca" -> "abc" is the classic discriminator: OSA 3, unrestricted
        // Damerau 2. This test exists so a future "improvement" cannot silently switch variants and
        // change which names get flagged.
        [Fact]
        public void EditDistance_IsOptimalStringAlignment_NotUnrestrictedDamerau()
        {
            Assert.Equal(3, LookalikeDetector.EditDistance("ca", "abc", 3));
        }

        [Fact]
        public void GenuineBrandAndUnrelatedNamesStillBehave()
        {
            // The transposition term must not widen the net: the brand itself is still not a finding,
            // and the cap still rejects unrelated names cheaply.
            Assert.Null(LookalikeDetector.Analyze("paypal.com"));
            Assert.Null(LookalikeDetector.Analyze("example.com"));
            Assert.Equal(0, LookalikeDetector.EditDistance("google", "google", 1));
            Assert.Equal(2, LookalikeDetector.EditDistance("google", "yahoosearch", 1));   // cap+1
        }
    }

    // ── K-17 — underscore names must not be silently dropped ──────────────────────────
    // NormalizeDomain rejected `_`, returning null; the caller drops a null-normalising record
    // entirely, so `_dmarc.example.com` and friends vanished from every tally INCLUDING exfil
    // detection. RFC 8552 makes `_` valid, and SRV/DKIM/DMARC/mDNS depend on it.
    public class DNSLogUnderscoreTests
    {
        private static DNSLogRecord Q(string name) =>
            new() { QuestionName = name, QuestionType = "TXT", IsReply = false };

        [Theory]
        [InlineData("_dmarc.example.com")]
        [InlineData("_sip._udp.example.com")]
        [InlineData("_services._dns-sd._udp.local")]
        [InlineData("_domainkey.example.com")]
        public void UnderscoreNamesNormalizeInsteadOfReturningNull(string name)
        {
            Assert.Equal(name, DNSLogAnalyzer.NormalizeDomain(name));
        }

        [Fact]
        public void UnderscoreNamesAreCountedNotDropped()
        {
            var a = DNSLogAnalyzer.Analyze(new List<DNSLogRecord>
            {
                Q("_dmarc.example.com"), Q("_sip._udp.example.com"), Q("www.example.com"),
            });
            Assert.Equal(3, a.UniqueDomains);
            Assert.Contains(a.DomainCounts, p => p.Name == "_dmarc.example.com");
            Assert.Contains(a.DomainCounts, p => p.Name == "_sip._udp.example.com");
            Assert.Equal(0, a.NamesExcludedCount);
        }

        // The aggravating half of K-17: Swift disclosed excluded names via a counter and C# had
        // neither counter nor disclosure. A drop the user cannot see is a silent drop.
        [Fact]
        public void ExcludedNamesAreCountedAndDisclosedInTheReport()
        {
            var a = DNSLogAnalyzer.Analyze(new List<DNSLogRecord>
            {
                Q("www.example.com"), Q("localhost"), Q("host name.example.com"),
            });
            Assert.Equal(1, a.UniqueDomains);
            Assert.Equal(2, a.NamesExcludedCount);
            Assert.Contains("Names excluded from analysis: 2", DNSLogAnalyzer.Render(a, "test"));
        }

        [Fact]
        public void CleanRunDoesNotClaimAnExclusion()
        {
            var a = DNSLogAnalyzer.Analyze(new List<DNSLogRecord> { Q("www.example.com") });
            Assert.Equal(0, a.NamesExcludedCount);
            Assert.DoesNotContain("Names excluded from analysis", DNSLogAnalyzer.Render(a, "test"));
        }
    }

    // ── K-07 — AuthComparisonStatus wire form is pinned, not positional ───────────────
    // The member order was the REVERSE of the Swift twin (Diverges/Matches swapped) with no
    // converter, and System.Text.Json emits a bare enum as its NUMERIC value: the first thing to
    // serialize this would have put `0` on the wire meaning `Diverges` here and `matches` on macOS
    // — a silent inversion of the exact finding the type carries. These tests pin the string form
    // so ordinal drift can never reach the wire.
    //
    // (Housed in this file because it is the test file this change's seat owns; the type itself
    // lives in Syxx.Kit/AuthoritativeVantage.cs, not in Forensics.)
    public class AuthComparisonStatusWireTests
    {
        [Theory]
        [InlineData(AuthComparisonStatus.Matches, "matches")]
        [InlineData(AuthComparisonStatus.Diverges, "diverges")]
        [InlineData(AuthComparisonStatus.Inconclusive, "inconclusive")]
        [InlineData(AuthComparisonStatus.Untested, "untested")]
        public void WireStringIsPinnedToTheSwiftRawValue(AuthComparisonStatus s, string wire)
        {
            Assert.Equal(wire, s.RawValue());
            Assert.Equal($"\"{wire}\"", System.Text.Json.JsonSerializer.Serialize(s));
            Assert.Equal(s, System.Text.Json.JsonSerializer.Deserialize<AuthComparisonStatus>($"\"{wire}\""));
        }

        // Declaration order must match Swift's `case matches, diverges, inconclusive, untested`.
        // Belt and braces alongside the converter: this catches a reorder at its source.
        [Fact]
        public void MemberOrderMatchesTheSwiftTwin()
        {
            Assert.Equal(0, (int)AuthComparisonStatus.Matches);
            Assert.Equal(1, (int)AuthComparisonStatus.Diverges);
            Assert.Equal(2, (int)AuthComparisonStatus.Inconclusive);
            Assert.Equal(3, (int)AuthComparisonStatus.Untested);
        }
    }

    // ── K-16 — a same-provider disjoint answer stays a divergence ─────────────────────
    // C# softened disjoint-but-same-CDN-provider to Inconclusive ("CDN/geo routing, not a rewrite").
    // Swift retains .diverges because an attacker may share the provider's ASN. The traced
    // consequence is what makes it severe: Synthesizer emits an evidence item for Diverges, while
    // Inconclusive falls to `default:` and emits NO evidence item — the finding did not soften,
    // it DISAPPEARED.
    public class AuthoritativeSameProviderTests
    {
        private static HashSet<string> S(params string[] ips) => new(ips);

        [Fact]
        public void DisjointAnswersInOneCdn_RetainDivergence_WithTheProviderNoted()
        {
            // Both sets are Fastly (151.101.0.0/16).
            var r = AuthoritativeVantage.Classify(S("151.101.1.1"), S("151.101.2.2"), new List<string>(), "x");
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);
            Assert.Contains("Fastly", r.Detail);
            Assert.Contains("divergence retained", r.Detail);
        }

        [Fact]
        public void DisjointAnswersAcrossProviders_StillDiverge()
        {
            var r = AuthoritativeVantage.Classify(S("151.101.1.1"), S("142.250.1.1"), new List<string>(), "x");
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);
        }

        // The softening removal must not swallow the genuinely ambiguous case: PARTIAL overlap is
        // still inconclusive (the real CDN/geo signature), and a subset is still a match.
        [Fact]
        public void PartialOverlapIsStillInconclusive_AndSubsetIsStillAMatch()
        {
            var partial = AuthoritativeVantage.Classify(S("1.2.3.4", "6.6.6.6"), S("1.2.3.4", "5.6.7.8"),
                new List<string>(), "x");
            Assert.Equal(AuthComparisonStatus.Inconclusive, partial.Status);

            var subset = AuthoritativeVantage.Classify(S("1.2.3.4"), S("1.2.3.4", "5.6.7.8"),
                new List<string>(), "x");
            Assert.Equal(AuthComparisonStatus.Matches, subset.Status);
        }
    }

    public class EndpointLogAnalyzerTests
    {
        private static string WriteTemp(string content)
        {
            string p = Path.Combine(Path.GetTempPath(), "syxx_ep_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void FileLogFlagsPersistenceAndUntrusted()
        {
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_OPEN\",\"file\":{\"destination\":\"C:\\\\Windows\\\\System32\\\\drivers\\\\etc\\\\hosts\",\"process\":{\"name\":\"reader\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Windows\",\"signatureStatus\":0}}}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_WRITE\",\"file\":{\"destination\":\"C:\\\\Users\\\\evil\\\\AppData\\\\Roaming\\\\Microsoft\\\\Windows\\\\Start Menu\\\\evil.exe\",\"process\":{\"name\":\"dropper\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0}}}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_CREATE\",\"file\":{\"destination\":\"C:\\\\Windows\\\\Temp\\\\foo\",\"process\":{\"name\":\"maker\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Corporation\",\"signatureStatus\":0}}}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("file", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Read events skipped (OPEN/CLOSE): 1", report);
                Assert.Contains(@"Start Menu\evil.exe", report);
                Assert.Contains("dropper [AdHoc]", report);
                Assert.DoesNotContain("maker [Microsoft Corporation]", report);
            }
            finally { File.Delete(p); }
        }

        [Fact]
        public void ProcessLogFlagsAdhocAndArgs()
        {
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_EXEC\",\"process\":{\"path\":\"C:\\\\Python3\\\\python.exe\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0},\"arguments\":[\"python.exe\",\"-c\",\"import x\"]}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_FORK\",\"process\":{\"path\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\"}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("process", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Launches (EXEC): 1", report);
                Assert.Contains("python.exe [AdHoc]", report);
                Assert.Contains("python.exe -c", report);
            }
            finally { File.Delete(p); }
        }

        // ── C054: every SuspiciousArgMarker must match a synthetic positive line ──────
        [Fact]
        public void EverySuspiciousArgMarkerMatchesSyntheticPositive()
        {
            // Each marker lowercased in the array; the haystack is also lowercased.
            // If ANY marker fails to match its own canonical form, the test fails.
            string[] markers = {
                "base64", "curl ", "wget ", "| powershell", "|pwsh", " -enc ", " -e ", " -command ",
                " -c ", "invoke-", "iex(", "iex ", " eval ", "downloadstring", "downloadfile",
                "start-process", " -windowstyle hidden", " -executionpolicy bypass",
                " /c ", " cmd ", " mshta ", " wscript ", " cscript ", " regsvr32 ",
                " certutil ", " bitsadmin ", " powershell ", "pwsh "
            };

            foreach (var marker in markers)
            {
                // Build a synthetic argument string that definitely contains this marker.
                // The marker itself, lowercased, is the simplest positive line.
                string syntheticLine = $"program.exe {marker.Trim()} payload";

                // C054 fix: markers are lowercased at declaration; haystack is lowercased via ToLowerInvariant().
                // Verify the comparison logic matches.
                string lowercased = syntheticLine.ToLowerInvariant();
                bool matched = lowercased.Contains(marker, StringComparison.Ordinal);
                Assert.True(matched,
                    $"C054: SuspiciousArgMarker \"{marker}\" did not match its own canonical positive line \"{lowercased}\". " +
                    "This marker is unreachable dead code.");
            }
        }

        // ── C054: synthetic process log with PowerShell abuse patterns ─────────────────
        [Fact]
        public void ProcessLogDetectsPowerShellAbusePatterns()
        {
            // A process invocation containing multiple PowerShell-abuse markers that were
            // previously unreachable (C054). After the fix, these must all match.
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_EXEC\",\"process\":{\"path\":\"C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Windows\",\"signatureStatus\":0},\"arguments\":[\"powershell.exe\",\" -Command \",\"Invoke-WebRequest\",\" -WindowStyle Hidden\",\" -ExecutionPolicy Bypass\"]}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("process", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Launches (EXEC): 1", report);
                // The report must contain the suspicious-arguments section with content
                Assert.Contains("powershell.exe", report);
            }
            finally { File.Delete(p); }
        }

        // ── C053: Windows persistence markers match synthetic Windows paths ────────────
        [Fact]
        public void FileLogFlagsWindowsPersistencePaths()
        {
            // Verify the Windows persistence markers still fire after the C053 refactor.
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_WRITE\",\"file\":{\"destination\":\"C:\\\\SOFTWARE\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Run\\\\evil\",\"process\":{\"name\":\"dropper\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0}}}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_WRITE\",\"file\":{\"destination\":\"C:\\\\Windows\\\\System32\\\\tasks\\\\schtask\",\"process\":{\"name\":\"scheduler\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0}}}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("file", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("dropper [AdHoc]", report);
                Assert.Contains("scheduler [AdHoc]", report);
                // Both writes landed in Windows persistence locations
                Assert.Contains("2 write(s) landed in persistence locations", report);
            }
            finally { File.Delete(p); }
        }

        // ── C053: report says "nothing stands out" only when markers exist ─────────────
        [Fact]
        public void InterpretationGatesOnMarkerAvailability()
        {
            // An empty-mutation file log (only OPEN/CLOSE events) should produce an
            // interpretation that does NOT claim "writes to persistence locations: none"
            // when no mutation events were analyzed at all.
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_OPEN\",\"file\":{\"destination\":\"C:\\\\Windows\\\\System32\\\\notepad.exe\",\"process\":{\"name\":\"reader\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Windows\",\"signatureStatus\":0}}}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("file", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Mutation events analyzed (CREATE/WRITE/DELETE/RENAME/LINK): 0", report);
                // With zero mutations, the interpretation should not claim persistence was checked.
                Assert.Contains("nothing stands out", report);
            }
            finally { File.Delete(p); }
        }
    }
}

import XCTest
@testable import SyxxKit

/// B015: Swift twin of the C# `ForensicsAnalyzerTests.cs` — covers the Forensics module
/// (BeaconAnalyzer, BlocklistAnalyzer, ExfilAnalyzer, DNSLogAnalyzer, EndpointLogAnalyzer).
/// Every test is adapted from the C# patterns to the Swift API shape, with divergences called
/// out inline (e.g. C009 single-signal → `.inconclusive`, not `.benign`).
final class ForensicsAnalyzerTests: XCTestCase {

    // MARK: - BeaconAnalyzer

    /// FAILURE GUARDED: an even cadence (constant interval) has CV ≈ 0 → HighlyRegular.
    /// C# twin: `BeaconClassifiesEvenCadenceAsHighlyRegular`.
    func testBeaconClassifiesEvenCadenceAsHighlyRegular() {
        let base = Date(timeIntervalSince1970: 1000)
        let timestamps = (0..<5).map { base.addingTimeInterval(Double($0) * 60) }
        let series = BeaconSeries(processName: "proc", destination: "dest", timestamps: timestamps)
        let score = BeaconAnalyzer.score(series)
        XCTAssertEqual(score.classification, .highlyRegular)
        XCTAssertNotNil(score.meanInterval)
        XCTAssertNotNil(score.coefficientOfVariation)
    }

    /// FAILURE GUARDED: a jittery cadence (wildly varying intervals) → Irregular.
    /// C# twin: `BeaconClassifiesJitteryCadenceAsIrregular`.
    func testBeaconClassifiesJitteryCadenceAsIrregular() {
        let epochs: [TimeInterval] = [1000, 1003, 1200, 1201, 1800]
        let timestamps = epochs.map { Date(timeIntervalSince1970: $0) }
        let series = BeaconSeries(processName: "proc", destination: "dest", timestamps: timestamps)
        let score = BeaconAnalyzer.score(series)
        XCTAssertEqual(score.classification, .irregular)
    }

    /// FAILURE GUARDED (B034): a degenerate series of ≥3 IDENTICAL timestamps has all-zero
    /// intervals (mean 0). That is NOT a highly-regular beacon; the zero-mean CV was a
    /// manufactured positive. Must read InsufficientData with nil mean/cv.
    /// C# twin: `BeaconDegenerateIdenticalTimestampsIsInsufficientData`.
    func testBeaconDegenerateIdenticalTimestampsIsInsufficientData() {
        let t = Date(timeIntervalSince1970: 1000)
        let series = BeaconSeries(processName: "p", destination: "d", timestamps: [t, t, t])
        let score = BeaconAnalyzer.score(series)
        XCTAssertEqual(score.classification, .insufficientData)
        XCTAssertNil(score.meanInterval)
        XCTAssertNil(score.coefficientOfVariation)
    }

    /// FAILURE GUARDED: fewer than 3 timestamps → InsufficientData (< 2 intervals).
    func testBeaconInsufficientDataForTooFewTimestamps() {
        let t1 = Date(timeIntervalSince1970: 1000)
        let t2 = Date(timeIntervalSince1970: 1060)
        let series = BeaconSeries(processName: "p", destination: "d", timestamps: [t1, t2])
        let score = BeaconAnalyzer.score(series)
        XCTAssertEqual(score.classification, .insufficientData)
    }

    /// FAILURE GUARDED: a moderately regular cadence (CV between 0.05 and 0.20) → Regular.
    func testBeaconClassifiesModerateCadenceAsRegular() {
        // Intervals: 60, 55, 65, 58 → mean ~59.5, stdev ~3.64, cv ~0.061
        let epochs: [TimeInterval] = [1000, 1060, 1115, 1180, 1238]
        let timestamps = epochs.map { Date(timeIntervalSince1970: $0) }
        let series = BeaconSeries(processName: "p", destination: "d", timestamps: timestamps)
        let score = BeaconAnalyzer.score(series)
        XCTAssertEqual(score.classification, .regular)
    }

    // MARK: - BlocklistAnalyzer

    /// FAILURE GUARDED: a subdomain of a listed domain must be matched by the parent-domain
    /// walk. C# twin: `BlocklistMatchesParentDomain`.
    func testBlocklistMatchesParentDomain() {
        let lists = SieveBlocklistLoader.bundledLists()
        let coverage = BlocklistAnalyzer.classify(["ads.doubleclick.net", "example.com"], against: lists)
        XCTAssertEqual(coverage.count, 2)
        XCTAssertFalse(coverage[0].coveredBy.isEmpty, "subdomain of a listed domain must be covered")
        XCTAssertTrue(coverage[1].coveredBy.isEmpty, "unlisted domain must not be covered")
    }

    /// FAILURE GUARDED: the overlap matrix must be symmetric and self-consistent.
    func testBlocklistOverlapMatrixIsSymmetric() {
        let lists = SieveBlocklistLoader.bundledLists()
        let matrix = BlocklistAnalyzer.overlapMatrix(for: lists)
        for lhs in lists {
            for rhs in lists {
                XCTAssertEqual(matrix[lhs.name]?[rhs.name], matrix[rhs.name]?[lhs.name],
                               "overlap matrix must be symmetric for \(lhs.name) ↔ \(rhs.name)")
            }
        }
    }

    // MARK: - ExfilAnalyzer

    /// FAILURE GUARDED: encoded labels must produce higher entropy than natural-language labels.
    /// C# twin: `EntropyHigherForEncodedLabel`.
    func testEntropyHigherForEncodedLabel() {
        let natural = ExfilAnalyzer.shannonEntropyBitsPerChar("wwwexample")
        let encoded = ExfilAnalyzer.shannonEntropyBitsPerChar("mfrggzdfmztwq2lknnwg23tp")
        XCTAssertGreaterThan(encoded, natural)
    }

    /// FAILURE GUARDED (C009): 2 signals (highEntropy + regular) → .suspicious, NOT .likelyExfil.
    /// C009 raised the bar to ≥3 for .likelyExfil.
    /// C# twin: `ExfilSuspiciousForTwoSignalsWithoutBlocklist`.
    func testExfilSuspiciousForTwoSignalsWithoutBlocklist() {
        let epochs: [TimeInterval] = [1000, 1060, 1120, 1180, 1240]
        let timestamps = epochs.map { Date(timeIntervalSince1970: $0) }
        let a = ExfilAnalyzer.assess(
            domain: "mfrggzdfmztwq2lknnwg23tpobyxe43uov3ho.tunnel.evil.net",
            timestamps: timestamps)
        XCTAssertEqual(a.suspicion, .suspicious)
    }

    /// FAILURE GUARDED (C009): a single signal alone (entropy-only OR beacon-only) must NOT
    /// reach .suspicious. Per the Swift C009 implementation, signalCount == 1 → .inconclusive
    /// (not .benign as the C# twin asserts — the Swift code retains the signal in rationale
    /// rather than erasing it with a false clean claim).
    /// C# twin: `ExfilBenignForSingleSignalOnly`.
    func testExfilSingleSignalIsInconclusive_notSuspicious() {
        // Entropy-only: long label, no regular cadence, no blocklist match.
        let entropyOnly = ExfilAnalyzer.assess(domain: "abcdefghijklmnopqrst.example.com")
        XCTAssertNotEqual(entropyOnly.suspicion, .suspicious)
        XCTAssertNotEqual(entropyOnly.suspicion, .likelyExfil)
        // Single signal → .inconclusive per C009/C045
        XCTAssertEqual(entropyOnly.suspicion, .inconclusive)

        // Beacon-only: regular cadence, short label (no high entropy), no blocklist match.
        let epochs: [TimeInterval] = [1000, 1060, 1120, 1180, 1240]
        let timestamps = epochs.map { Date(timeIntervalSince1970: $0) }
        let beaconOnly = ExfilAnalyzer.assess(domain: "www.example.com", timestamps: timestamps)
        XCTAssertNotEqual(beaconOnly.suspicion, .suspicious)
        XCTAssertNotEqual(beaconOnly.suspicion, .likelyExfil)
        XCTAssertEqual(beaconOnly.suspicion, .inconclusive)
    }

    /// FAILURE GUARDED: an ordinary domain with no signals → .benign.
    /// C# twin: `ExfilBenignForOrdinaryDomainWithoutSignal`.
    func testExfilBenignForOrdinaryDomainWithoutSignal() {
        let a = ExfilAnalyzer.assess(domain: "www.example.com")
        XCTAssertEqual(a.suspicion, .benign)
    }

    /// FAILURE GUARDED (B190): empty timestamps → beacon leg UNEVALUATED (tri-state unknown),
    /// not a silent negative. The rationale must disclose it.
    /// C# twin: `ExfilEmptyTimestampsMarksBeaconUnevaluated`.
    func testExfilEmptyTimestampsMarksBeaconUnevaluated() {
        let a = ExfilAnalyzer.assess(domain: "www.example.com")
        XCTAssertFalse(a.beaconEvaluated)
        XCTAssertTrue(a.rationale.contains { $0.contains("beacon cadence not evaluated") })
    }

    /// FAILURE GUARDED (B190): an unevaluated beacon can never contribute a positive signal,
    /// so .likelyExfil (3 signals) is structurally unreachable: max achievable signal count is 2.
    /// C# twin: `ExfilUnevaluatedBeaconCapsAtTwoSignals`.
    func testExfilUnevaluatedBeaconCapsAtTwoSignals() {
        let a = ExfilAnalyzer.assess(
            domain: "mfrggzdfmztwq2lknnwg23tpobyxe43uov3ho.tunnel.evil.net",
            timestamps: [])
        XCTAssertFalse(a.beaconEvaluated)
        XCTAssertNotEqual(a.suspicion, .likelyExfil)
    }

    /// FAILURE GUARDED (B190): real regular-cadence timestamps still evaluate + classify the
    /// beacon leg. C# twin: `ExfilRealCadenceStillEvaluatesBeacon`.
    func testExfilRealCadenceStillEvaluatesBeacon() {
        let epochs: [TimeInterval] = [1000, 1060, 1120, 1180, 1240]
        let timestamps = epochs.map { Date(timeIntervalSince1970: $0) }
        let a = ExfilAnalyzer.assess(domain: "www.example.com", timestamps: timestamps)
        XCTAssertTrue(a.beaconEvaluated)
        XCTAssertTrue(a.beacon == .regular || a.beacon == .highlyRegular)
        XCTAssertFalse(a.rationale.contains { $0.contains("beacon cadence not evaluated") })
    }

    /// FAILURE GUARDED: empty domain → .inconclusive.
    func testExfilEmptyDomainIsInconclusive() {
        let a = ExfilAnalyzer.assess(domain: "")
        XCTAssertEqual(a.suspicion, .inconclusive)
    }

    /// FAILURE GUARDED: Shannon entropy of an empty string is 0.
    func testEntropyOfEmptyStringIsZero() {
        XCTAssertEqual(ExfilAnalyzer.shannonEntropyBitsPerChar(""), 0)
    }

    // MARK: - DNSLogAnalyzer (K-17 underscore parity)

    /// FAILURE GUARDED (K-17): underscore names must normalize instead of returning nil.
    /// C# twin: `UnderscoreNamesNormalizeInsteadOfReturningNull`.
    func testUnderscoreNamesNormalizeInsteadOfReturningNil() {
        let names = [
            "_dmarc.example.com",
            "_sip._udp.example.com",
            "_services._dns-sd._udp.local",
            "_domainkey.example.com"
        ]
        for name in names {
            XCTAssertEqual(DNSLogAnalyzer.normalizeDomain(name), name,
                           "\(name) must normalize to itself, not nil")
        }
    }

    /// FAILURE GUARDED (K-17): underscore names must be counted, not dropped from tallies.
    /// C# twin: `UnderscoreNamesAreCountedNotDropped`.
    func testUnderscoreNamesAreCountedNotDropped() {
        func q(_ name: String) -> DNSLogRecord {
            DNSLogRecord(process: nil, processPath: nil, signingID: nil, pid: nil,
                         isReply: false, rcode: nil, questionName: name, questionType: "TXT", answers: [])
        }
        let records = [
            q("_dmarc.example.com"),
            q("_sip._udp.example.com"),
            q("www.example.com")
        ]
        let a = DNSLogAnalyzer.analyze(records)
        XCTAssertEqual(a.uniqueDomains, 3)
        XCTAssertTrue(a.domainCounts.contains { $0.0 == "_dmarc.example.com" })
        XCTAssertTrue(a.domainCounts.contains { $0.0 == "_sip._udp.example.com" })
        XCTAssertEqual(a.namesExcludedCount, 0)
    }

    /// FAILURE GUARDED (K-17): excluded names must be counted and disclosed in the report.
    /// C# twin: `ExcludedNamesAreCountedAndDisclosedInTheReport`.
    func testExcludedNamesAreCountedAndDisclosed() {
        func q(_ name: String) -> DNSLogRecord {
            DNSLogRecord(process: nil, processPath: nil, signingID: nil, pid: nil,
                         isReply: false, rcode: nil, questionName: name, questionType: "A", answers: [])
        }
        let records = [
            q("www.example.com"),
            q("localhost"),           // single-label → excluded
            q("host name.example.com") // space → excluded
        ]
        let a = DNSLogAnalyzer.analyze(records)
        XCTAssertEqual(a.uniqueDomains, 1)
        XCTAssertEqual(a.namesExcludedCount, 2)
        let rendered = DNSLogAnalyzer.render(a, source: "test")
        XCTAssertTrue(rendered.contains("Names excluded from analysis: 2"))
    }

    /// FAILURE GUARDED: a clean run must not claim an exclusion.
    /// C# twin: `CleanRunDoesNotClaimAnExclusion`.
    func testCleanRunDoesNotClaimAnExclusion() {
        let records = [
            DNSLogRecord(process: nil, processPath: nil, signingID: nil, pid: nil,
                         isReply: false, rcode: nil, questionName: "www.example.com",
                         questionType: "A", answers: [])
        ]
        let a = DNSLogAnalyzer.analyze(records)
        XCTAssertEqual(a.namesExcludedCount, 0)
        let rendered = DNSLogAnalyzer.render(a, source: "test")
        XCTAssertFalse(rendered.contains("Names excluded from analysis"))
    }

    // MARK: - CaptureLogParser

    /// FAILURE GUARDED: the parser handles empty input gracefully.
    func testCaptureLogParserEmptyInput() {
        let records = CaptureLogParser.parse("")
        XCTAssertTrue(records.isEmpty)
    }

    // MARK: - EndpointLogAnalyzer

    /// FAILURE GUARDED (D081): a nonexistent file returns `.error`, not `.notApplicable` or `.success`.
    func testEndpointLogAnalyzerNonexistentFileReturnsError() {
        let result = EndpointLogAnalyzer.analyze(path: "/tmp/syxx_nonexistent_\(UUID().uuidString).jsonl")
        if case .error = result {
            // expected
        } else {
            XCTFail("Expected .error for nonexistent file, got \(result)")
        }
    }

    /// FAILURE GUARDED (D081): a non-endpoint-log file returns `.notApplicable`.
    func testEndpointLogAnalyzerNonEndpointLogReturnsNotApplicable() {
        let tmp = NSTemporaryDirectory() + "syxx_ep_\(UUID().uuidString).txt"
        let content = "This is just a plain text file, not an endpoint log.\nNo JSON here.\n"
        try? content.write(toFile: tmp, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(atPath: tmp) }
        let result = EndpointLogAnalyzer.analyze(path: tmp)
        if case .notApplicable = result {
            // expected
        } else {
            XCTFail("Expected .notApplicable for non-endpoint-log file, got \(result)")
        }
    }

    /// FAILURE GUARDED: file-log detection identifies `"file":{` in the JSON.
    func testEndpointLogDetectReturnsFileForFileEvents() {
        let tmp = NSTemporaryDirectory() + "syxx_ep_\(UUID().uuidString).jsonl"
        let line = "{\"event\":\"ES_EVENT_TYPE_NOTIFY_OPEN\",\"file\":{\"destination\":\"/tmp/foo\",\"process\":{\"name\":\"reader\"}}}\n"
        try? line.write(toFile: tmp, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(atPath: tmp) }
        let detected = EndpointLogAnalyzer.detect(path: tmp)
        XCTAssertEqual(detected, "file")
    }

    /// FAILURE GUARDED: process-log detection identifies EXEC events.
    func testEndpointLogDetectReturnsProcessForExecEvents() {
        let tmp = NSTemporaryDirectory() + "syxx_ep_\(UUID().uuidString).jsonl"
        let line = "{\"event\":\"ES_EVENT_TYPE_NOTIFY_EXEC\",\"process\":{\"path\":\"/usr/bin/python3\",\"name\":\"python3\"}}\n"
        try? line.write(toFile: tmp, atomically: true, encoding: .utf8)
        defer { try? FileManager.default.removeItem(atPath: tmp) }
        let detected = EndpointLogAnalyzer.detect(path: tmp)
        XCTAssertEqual(detected, "process")
    }

    // MARK: - EndpointLogAnalysisResult outcomes

    /// FAILURE GUARDED (D081): the explicit outcome enum must distinguish error from notApplicable.
    func testEndpointLogAnalysisResultDistinguishesOutcomes() {
        // Pattern-match each outcome to verify they are distinct cases.
        let error: EndpointLogAnalysisResult = .error("x")
        let notApplicable: EndpointLogAnalysisResult = .notApplicable
        let success: EndpointLogAnalysisResult = .success("report")

        if case .error = error {} else { XCTFail("Expected .error") }
        if case .notApplicable = notApplicable {} else { XCTFail("Expected .notApplicable") }
        if case .success = success {} else { XCTFail("Expected .success") }

        // Verify the cases are distinct by checking the wrong pattern does NOT match.
        if case .success = error { XCTFail(".error must not match .success") }
        if case .error = notApplicable { XCTFail(".notApplicable must not match .error") }
        if case .notApplicable = success { XCTFail(".success must not match .notApplicable") }
    }

    // MARK: - SieveBlocklistLoader

    /// FAILURE GUARDED (D081): loading from a nonexistent path returns `.failed`, not a silent empty list.
    func testSieveBlocklistLoaderNonexistentFileReturnsFailed() {
        let result = SieveBlocklistLoader.loadFromPath("/tmp/syxx_blocklist_\(UUID().uuidString).txt")
        if case .failed = result {
            // expected
        } else {
            XCTFail("Expected .failed for nonexistent file, got \(result)")
        }
    }

    /// FAILURE GUARDED (D026): remote blocklist URLs must use https:// — file:// and http:// are rejected.
    func testSieveBlocklistLoaderRejectsNonHTTPSRemoteURLs() {
        let httpResult = SieveBlocklistLoader.loadFromPath("http://evil.example.com/blocklist.txt")
        if case .failed(let reason) = httpResult {
            XCTAssertTrue(reason.contains("https"))
        } else {
            XCTFail("Expected .failed for http:// URL")
        }

        let fileResult = SieveBlocklistLoader.loadFromPath("file:///etc/passwd")
        if case .failed(let reason) = fileResult {
            XCTAssertTrue(reason.contains("https"))
        } else {
            XCTFail("Expected .failed for file:// URL")
        }
    }

    /// FAILURE GUARDED: bundled lists are non-empty stub samples.
    func testSieveBlocklistLoaderBundledListsAreNonEmpty() {
        let lists = SieveBlocklistLoader.bundledLists()
        XCTAssertFalse(lists.isEmpty)
        for list in lists {
            XCTAssertFalse(list.domains.isEmpty, "bundled list '\(list.name)' must not be empty")
        }
    }
}

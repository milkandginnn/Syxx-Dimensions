import XCTest
@testable import SyxxKit

/// PARITY_VECTOR_V1 loader contract (docs/parity-vector-v1-spec.md; track
/// cross-platform-parity_20260805, Task 1.3).
///
/// Two disciplines run through every test here:
///
/// 1. **Assert the precondition first.** Where a test claims "X causes a rejection", it also shows
///    that the same case WITHOUT X loads. Otherwise a loader that rejected everything — including
///    valid corpora — would pass every rejection test.
/// 2. **Assert the message, not just the throw.** The contract is that a rejection NAMES the case
///    and the reason. A test that only asserts `XCTAssertThrowsError` would stay green if the
///    loader threw an anonymous error, and the CI operator reading a red build would learn nothing.
///
/// Fixtures are written to a fresh temp directory per test. Nothing here reads or writes `corpus/`.
final class ParityVectorLoaderTests: XCTestCase {

    private var dir: URL!

    override func setUpWithError() throws {
        dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("parity-vectors-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        if let dir { try? FileManager.default.removeItem(at: dir) }
    }

    // MARK: - Fixtures

    private static let pinned = 1_752_268_477
    private static let altPinned = 1_700_000_000

    private func write(_ name: String, _ json: String) throws {
        try json.write(to: dir.appendingPathComponent(name), atomically: true, encoding: .utf8)
    }

    /// Deliberately written out of order (003, 001, 002) so a loader that returned
    /// directory-enumeration order could not pass the ordering assertions by luck.
    private func writeValidCorpus() throws {
        try write("003_target-context.json", """
            {"schemaVersion":1,"vectorId":"target-context","pinnedUnixSeconds":\(Self.altPinned),\
            "sectionInputs":{"targetDomain":"example.com","targetQueryType":1}}
            """)
        try write("001_empty-section-inputs.json", """
            {"schemaVersion":1,"vectorId":"empty-section-inputs","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{}}
            """)
        try write("002_dnssec-unsigned.json", """
            {"schemaVersion":1,"vectorId":"dnssec-unsigned","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"dnssec":"unsigned","cageAttributionRate":0.5}}
            """)
    }

    /// Load and require a rejection, returning its rendered message. Fails the test (rather than
    /// trapping) when the load unexpectedly succeeds.
    private func rejectionMessage(file: StaticString = #filePath, line: UInt = #line) throws -> String {
        do {
            let cases = try ParityVectorLoader.load(directory: dir)
            XCTFail("expected a rejection; loaded \(cases.count) case(s): \(cases.map(\.vectorId))",
                    file: file, line: line)
            throw XCTSkip("no rejection to inspect")
        } catch let error as ParityVectorLoader.LoadError {
            return error.description
        }
    }

    // MARK: - Happy path

    func testLoadsEveryCaseInSortedOrderWithPerCaseClock() throws {
        try writeValidCorpus()
        let cases = try ParityVectorLoader.load(directory: dir)

        XCTAssertEqual(cases.count, 3, "the loader must report exactly the corpus it was given")
        XCTAssertEqual(cases.map(\.vectorId),
                       ["empty-section-inputs", "dnssec-unsigned", "target-context"],
                       "case order is the sorted filename list, not enumeration order")
        XCTAssertEqual(cases.map(\.sortKey), ["001", "002", "003"])
        XCTAssertEqual(cases.map(\.caseFile),
                       ["001_empty-section-inputs.json",
                        "002_dnssec-unsigned.json",
                        "003_target-context.json"])

        // The clock is built from pinnedUnixSeconds and nothing else, per case.
        XCTAssertEqual(cases[0].pinnedNow, Date(timeIntervalSince1970: Double(Self.pinned)))
        XCTAssertEqual(cases[1].pinnedNow, Date(timeIntervalSince1970: Double(Self.pinned)))
        XCTAssertEqual(cases[2].pinnedNow, Date(timeIntervalSince1970: Double(Self.altPinned)),
                       "each case pins its own clock; case 3 uses a different epoch")
    }

    func testPopulatedFieldsReachSectionInputsAndUnpopulatedOnesStayNil() throws {
        try writeValidCorpus()
        let cases = try ParityVectorLoader.load(directory: dir)

        // Precondition: the `{}` case really does leave these absent. Without this, the positive
        // assertions below could not distinguish "the loader set the field" from "the field is
        // always set".
        let empty = cases[0].inputs
        XCTAssertNil(empty.dnssec, "precondition: sectionInputs {} must leave every field absent")
        XCTAssertNil(empty.cageAttributionRate)
        XCTAssertNil(empty.targetDomain)
        XCTAssertNil(empty.targetQueryType)

        let populated = cases[1].inputs
        XCTAssertEqual(populated.dnssec, .unsigned)
        XCTAssertEqual(populated.cageAttributionRate, 0.5)
        XCTAssertNil(populated.targetDomain, "a field the vector omitted must stay nil")
        XCTAssertNil(populated.resolution)

        let context = cases[2].inputs
        XCTAssertEqual(context.targetDomain, "example.com")
        XCTAssertEqual(context.targetQueryType, 1)
        XCTAssertNil(context.dnssec)
    }

    // MARK: - The loaded inputs actually reach the engine

    func testLoadedInputsDriveTheProductionEngine() throws {
        try writeValidCorpus()
        let cases = try ParityVectorLoader.load(directory: dir)
        let dnssecCase = try XCTUnwrap(cases.first { $0.vectorId == "dnssec-unsigned" })

        // Precondition: with EMPTY inputs the Prophet section is untested. If that stopped being
        // true, the assertion below would pass without the loaded field having done anything.
        let baseline = Synthesizer.conclude(from: SectionInputs(), now: dnssecCase.pinnedNow)
        XCTAssertEqual(try prophetStatus(baseline), .untested,
                       "precondition: empty SectionInputs must leave Prophet untested")

        let loaded = Synthesizer.conclude(from: dnssecCase.inputs, now: dnssecCase.pinnedNow)
        XCTAssertNotEqual(try prophetStatus(loaded), .untested,
                          "a loaded dnssec value must reach the engine and move Prophet off untested")
        XCTAssertEqual(try prophetStatus(loaded), .inconclusive,
                       "DNSSECStatus.unsigned is an inconclusive Prophet datapoint (Synthesizer.swift:154)")
    }

    private func prophetStatus(_ c: Conclusion) throws -> SectionStatus {
        try XCTUnwrap(c.sectionStatuses.first { $0.section == .prophet }).status
    }

    // MARK: - Rejection rule 1: schemaVersion

    func testUnrecognisedSchemaVersionIsRejectedByName() throws {
        try write("001_future.json", """
            {"schemaVersion":2,"vectorId":"future","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_future.json"), message)
        XCTAssertTrue(message.contains("schemaVersion"), message)
        XCTAssertTrue(message.contains("2"), message)
    }

    // MARK: - Rejection rule 2: vectorId vs filename

    func testVectorIdDisagreeingWithFilenameIsRejected() throws {
        // Precondition: the same bytes under the matching filename load cleanly, so the rejection
        // below is attributable to the disagreement and nothing else.
        try write("001_agrees.json", """
            {"schemaVersion":1,"vectorId":"agrees","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        XCTAssertEqual(try ParityVectorLoader.load(directory: dir).count, 1)

        try FileManager.default.removeItem(at: dir.appendingPathComponent("001_agrees.json"))
        try write("001_on-disk.json", """
            {"schemaVersion":1,"vectorId":"declared","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_on-disk.json"), message)
        XCTAssertTrue(message.contains("declared"), message)
        XCTAssertTrue(message.contains("on-disk"), message)
    }

    // MARK: - Rejection rule 3: unknown sectionInputs key

    /// `resolverSubstituted` is the key the spec calls out by name: it is a real Swift field that
    /// v1 cannot express, so the Swift loader accepting it is precisely the failure that would let
    /// a coverage hole render as a clean diff.
    func testSwiftOnlyFieldResolverSubstitutedIsRejectedNotAccepted() throws {
        try write("001_substituted.json", """
            {"schemaVersion":1,"vectorId":"substituted","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"resolverSubstituted":true}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_substituted.json"), message)
        XCTAssertTrue(message.contains("resolverSubstituted"), message)
    }

    func testSwiftOnlyFieldTransportObservationIsRejected() throws {
        try write("001_transport.json", """
            {"schemaVersion":1,"vectorId":"transport","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"transportObservation":{}}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_transport.json"), message)
        XCTAssertTrue(message.contains("transportObservation"), message)
    }

    func testUnknownSectionInputsKeyIsRejectedByName() throws {
        try write("001_typo.json", """
            {"schemaVersion":1,"vectorId":"typo","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"dnsec":"unsigned"}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_typo.json"), message)
        XCTAssertTrue(message.contains("dnsec"), message)
    }

    // MARK: - Rejection: unknown NESTED member (F-27)

    /// The same corpus must not load on one engine and be refused by the other. Before this check,
    /// a misspelled member inside a nested object was silently dropped here — Swift's synthesized
    /// `Decodable` ignores members it does not recognise — while the C# twin refused it, so a vector
    /// could claim to exercise a probe member that nothing read.
    func testUnknownNestedMemberIsRejectedByPath() throws {
        try write("001_nested-typo.json", """
            {"schemaVersion":1,"vectorId":"nested-typo","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"ecs":{"status":"forwarded","scopePrefixLenght":24,"detail":"typo"}}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_nested-typo.json"), message)
        XCTAssertTrue(message.contains("ecs.scopePrefixLenght"), message)
    }

    /// The check must not fire on correctly-spelled members, or the corpus stops loading.
    func testCorrectlySpelledNestedMembersLoad() throws {
        try write("001_nested-ok.json", """
            {"schemaVersion":1,"vectorId":"nested-ok","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"ecs":{"status":"forwarded","scopePrefixLength":24,"detail":"ok"}}}
            """)
        let cases = try ParityVectorLoader.load(directory: dir)
        XCTAssertEqual(cases.count, 1)
        XCTAssertEqual(cases[0].inputs.ecs?.scopePrefixLength, 24)
    }

    // MARK: - Rejection: unknown top-level key

    func testUnknownTopLevelKeyIsRejectedByName() throws {
        try write("001_extra.json", """
            {"schemaVersion":1,"vectorId":"extra","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{},"expectedState":"untested"}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_extra.json"), message)
        XCTAssertTrue(message.contains("expectedState"), message)
    }

    // MARK: - Rejection rule 4: not valid JSON

    func testInvalidJSONIsRejectedByName() throws {
        try write("001_broken.json", "{\"schemaVersion\":1,\"vectorId\":")
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_broken.json"), message)
        XCTAssertTrue(message.contains("JSON"), message)
    }

    // MARK: - Rejection rule 5: pinnedUnixSeconds

    func testMissingPinnedUnixSecondsIsRejected() throws {
        try write("001_noclock.json", """
            {"schemaVersion":1,"vectorId":"noclock","sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_noclock.json"), message)
        XCTAssertTrue(message.contains("pinnedUnixSeconds"), message)
        XCTAssertTrue(message.contains("absent"), message)
    }

    func testNonIntegerPinnedUnixSecondsIsRejected() throws {
        try write("001_fractional.json", """
            {"schemaVersion":1,"vectorId":"fractional","pinnedUnixSeconds":1752268477.5,"sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_fractional.json"), message)
        XCTAssertTrue(message.contains("pinnedUnixSeconds"), message)
        XCTAssertTrue(message.contains("not an integer"), message)
    }

    func testStringPinnedUnixSecondsIsRejected() throws {
        try write("001_stringclock.json", """
            {"schemaVersion":1,"vectorId":"stringclock","pinnedUnixSeconds":"1752268477","sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_stringclock.json"), message)
        XCTAssertTrue(message.contains("not an integer"), message)
    }

    // MARK: - Structural rejections

    /// Zero cases is a failure, not a vacuous pass. A loader that returned `[]` here would let an
    /// empty corpus render as a clean, fully-agreeing parity run on both platforms.
    func testEmptyDirectoryIsAnErrorNotAnEmptyCorpus() throws {
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains(dir.path), message)
        XCTAssertTrue(message.contains("no case files"), message)
    }

    func testDirectoryOfOnlyNonCaseFilesIsStillEmpty() throws {
        try write("README.md", "# vectors")
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("no case files"), message)
    }

    func testUnreadableDirectoryIsAnErrorNotAnEmptyCorpus() throws {
        let missing = dir.appendingPathComponent("does-not-exist", isDirectory: true)
        XCTAssertThrowsError(try ParityVectorLoader.load(directory: missing)) { error in
            guard let e = error as? ParityVectorLoader.LoadError else {
                return XCTFail("expected a LoadError, got \(error)")
            }
            XCTAssertTrue(e.description.contains("could not be read"), e.description)
            XCTAssertTrue(e.description.contains(missing.path), e.description)
        }
    }

    func testDuplicateSortPrefixIsRejectedNamingBothFiles() throws {
        // Precondition: distinct prefixes over the same two ids load fine, so the rejection is
        // attributable to the collision and not to the case contents.
        try write("003_alpha.json", """
            {"schemaVersion":1,"vectorId":"alpha","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        try write("004_beta.json", """
            {"schemaVersion":1,"vectorId":"beta","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        XCTAssertEqual(try ParityVectorLoader.load(directory: dir).count, 2)

        try FileManager.default.removeItem(at: dir.appendingPathComponent("004_beta.json"))
        try write("003_beta.json", """
            {"schemaVersion":1,"vectorId":"beta","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("003"), message)
        XCTAssertTrue(message.contains("003_alpha.json"), message)
        XCTAssertTrue(message.contains("003_beta.json"), message)
    }

    /// A `.json` file that does not carry the `NNN_` prefix is refused rather than ignored: it is
    /// a case someone believed was in the corpus, and dropping it silently is the defect class
    /// this whole track exists to close.
    func testMisnamedJSONCaseFileIsRejectedNotIgnored() throws {
        try write("001_good.json", """
            {"schemaVersion":1,"vectorId":"good","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        try write("extra-case.json", """
            {"schemaVersion":1,"vectorId":"extra-case","pinnedUnixSeconds":\(Self.pinned),"sectionInputs":{}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("extra-case.json"), message)
        XCTAssertTrue(message.contains("NNN_"), message)
    }

    // MARK: - A known field with the wrong payload is named, never skipped

    func testUndecodableKnownFieldIsRejectedNamingCaseAndKey() throws {
        try write("001_badenum.json", """
            {"schemaVersion":1,"vectorId":"badenum","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"dnssec":"definitely-not-a-status"}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("001_badenum.json"), message)
        XCTAssertTrue(message.contains("dnssec"), message)
    }

    // MARK: - Hand-decoded types

    /// `nxCanary`, `leak` and `exfil` have no `Codable` conformance, so their wire shape exists
    /// only in the loader. This pins it — if it drifts, this test is where it shows.
    func testHandDecodedFieldsRoundTripIntoSectionInputs() throws {
        try write("001_handdecoded.json", """
            {"schemaVersion":1,"vectorId":"handdecoded","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{\
            "nxCanary":{"section":"prophet","status":"positive","detail":"two random names shared one address",\
            "evidence":{"section":"prophet","evidenceId":"nxCanaryHijack",\
            "observation":"observed","rationale":"because"}},\
            "leak":{"result":"confirmed","observedResolvers":["9.9.9.9"],"transportPinning":"enforced"},\
            "exfil":[{"domain":"a.example.com","longestLabel":"a","labelLength":1,\
            "entropyBitsPerChar":5.5,"beacon":"Regular","beaconEvaluated":true,\
            "flaggedByLists":[],"listsEvaluated":false,"suspicion":"Suspicious","rationale":["high entropy"]}]}}
            """)
        let cases = try ParityVectorLoader.load(directory: dir)
        let inputs = try XCTUnwrap(cases.first).inputs

        let canary = try XCTUnwrap(inputs.nxCanary)
        XCTAssertEqual(canary.section, .prophet)
        XCTAssertEqual(canary.status, .positive)
        XCTAssertEqual(canary.evidence?.evidenceId, .nxCanaryHijack)

        let leak = try XCTUnwrap(inputs.leak)
        XCTAssertEqual(leak.result, .confirmed)
        XCTAssertEqual(leak.observedResolvers, ["9.9.9.9"])
        XCTAssertEqual(leak.transportPinning, .enforced)

        let exfil = try XCTUnwrap(inputs.exfil)
        XCTAssertEqual(exfil.count, 1)
        XCTAssertEqual(exfil.first?.suspicion, .suspicious)
        XCTAssertEqual(exfil.first?.beacon, .regular)
    }

    /// `GroundTruth` renames two members on the wire (DimensionClient.swift:35-39). A vector using
    /// the SWIFT spellings must fail rather than decode two fields as absent — this is the single
    /// most likely place for the Swift and C# vectors to disagree.
    func testServerGroundTruthUsesWireNamesNotSwiftNames() throws {
        try write("001_groundtruth.json", """
            {"schemaVersion":1,"vectorId":"groundtruth","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"serverGroundTruth":{"name":"example.com","type":"A",\
            "conclusion":"authoritative","authoritative":{"answers":["93.184.216.34"],"rcode":0,"signed":true}}}}
            """)
        let inputs = try XCTUnwrap(ParityVectorLoader.load(directory: dir).first).inputs
        let truth = try XCTUnwrap(inputs.serverGroundTruth)
        XCTAssertEqual(truth.domain, "example.com")
        XCTAssertEqual(truth.result, "authoritative")

        try FileManager.default.removeItem(at: dir.appendingPathComponent("001_groundtruth.json"))
        try write("001_groundtruth.json", """
            {"schemaVersion":1,"vectorId":"groundtruth","pinnedUnixSeconds":\(Self.pinned),\
            "sectionInputs":{"serverGroundTruth":{"domain":"example.com","type":"A",\
            "result":"authoritative","authoritative":null}}}
            """)
        let message = try rejectionMessage()
        XCTAssertTrue(message.contains("serverGroundTruth"), message)
    }
}

import XCTest
@testable import SyxxKit

final class LANLifecycleTests: XCTestCase {
    func testInvalidARPStateIsIncompleteRatherThanReachable() throws {
        let line = "? (192.168.50.44) at 00:00:00:00:00:00 invalid on en0 ifscope [ethernet]"

        let entry = try XCTUnwrap(ARPTableParser.parseLine(line))

        XCTAssertEqual(entry.state, .incomplete)
    }

    func testMalformedMACIsRejected() {
        let line = "? (192.168.50.44) at aa:bb:cc:dd:ee:gg on en0 ifscope [ethernet]"

        XCTAssertNil(ARPTableParser.parseLine(line))
    }

    func testUnpaddedMACIsCanonicalizedToTwoDigitLowercaseOctets() throws {
        let line = "? (192.168.50.44) at A:b:0C:d:0:eF on en0 ifscope [ethernet]"

        let entry = try XCTUnwrap(ARPTableParser.parseLine(line))

        XCTAssertEqual(entry.macAddress, "0a:0b:0c:0d:00:ef")
        XCTAssertEqual(entry.state, .reachable)
    }

    func testRecognizedARPStatesRetainMeasuredMeaning() throws {
        let reachable = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.1) at aa:bb:cc:dd:ee:01 on en0 ifscope [ethernet]"
        ))
        let permanent = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.2) at aa:bb:cc:dd:ee:02 on en0 permanent [ethernet]"
        ))
        let incomplete = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.3) at (incomplete) on en0 ifscope [ethernet]"
        ))
        // Explicit reachable-family tokens (Linux ip-neigh / Windows dynamic style) also map.
        let explicitReachable = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.4) at aa:bb:cc:dd:ee:04 on en0 ifscope reachable [ethernet]"
        ))
        let dynamicReachable = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.5) at aa:bb:cc:dd:ee:05 on en0 dynamic [ethernet]"
        ))

        XCTAssertEqual(reachable.state, .reachable)
        XCTAssertEqual(permanent.state, .permanent)
        XCTAssertEqual(incomplete.state, .incomplete)
        XCTAssertEqual(explicitReachable.state, .reachable)
        XCTAssertEqual(dynamicReachable.state, .reachable)
    }

    /// C141: an unrecognized state token with an otherwise-valid MAC and fields must remain
    /// `.unknown`. Pre-fix, `state(for:)` returned `.reachable` whenever the MAC canonicalized
    /// (`if canonicalMACAddress(macAddress) != nil { return .reachable }`), so any unknown
    /// token was silently promoted to reachable.
    ///
    /// FAILURE / MUTATION: restore that unconditional reachable fallback and this turns red.
    func testUnrecognizedARPStateTokenRemainsUnknown() throws {
        let unrecognized = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.44) at aa:bb:cc:dd:ee:ff on en0 ifscope mystate [ethernet]"
        ))
        let localized = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.45) at aa:bb:cc:dd:ee:fe on en0 ifscope dynamisch [ethernet]"
        ))
        let expired = try XCTUnwrap(ARPTableParser.parseLine(
            "? (192.168.50.46) at aa:bb:cc:dd:ee:fd on en0 ifscope expired [ethernet]"
        ))

        XCTAssertEqual(unrecognized.state, .unknown,
            "unrecognized state token must not collapse to reachable")
        XCTAssertEqual(localized.state, .unknown,
            "localized type word is not a measured reachable state")
        XCTAssertEqual(expired.state, .unknown,
            "unrecognized 'expired' token must remain unknown until explicitly mapped")
        // Fields otherwise valid: MAC canonicalized, IP and interface present.
        XCTAssertEqual(unrecognized.macAddress, "aa:bb:cc:dd:ee:ff")
        XCTAssertEqual(unrecognized.ipAddress, "192.168.50.44")
        XCTAssertEqual(unrecognized.interface, "en0")
    }

    func testMixedValidAndMalformedRowsReportRejectedCandidate() {
        let output = """
            ? (192.168.50.1) at aa:bb:cc:dd:ee:01 on en0 ifscope [ethernet]
            ? (192.168.50.2) at aa:bb:cc:dd:ee:gg on en0 ifscope [ethernet]
            """

        let result = ARPTableParser.parseWithDiagnostics(output)

        XCTAssertEqual(result.entries.count, 1)
        XCTAssertEqual(result.candidateCount, 2)
        XCTAssertEqual(result.rejectedCandidateCount, 1)
    }

    func testSystemCommandRunnerCapturesStdoutStderrAndExitCode() throws {
        let result = try SystemCommandRunner().run(
            "/bin/sh",
            arguments: ["-c", "printf stdout-value; printf stderr-value >&2; exit 7"],
            timeout: 2
        )

        XCTAssertTrue(result.stdout.contains("stdout-value"))
        XCTAssertTrue(result.stderr.contains("stderr-value"))
        XCTAssertEqual(result.exitCode, 7)
        XCTAssertFalse(result.timedOut)
    }

    func testSystemCommandRunnerTerminatesAtDeadline() throws {
        let startedAt = Date()

        let result = try SystemCommandRunner().run(
            "/bin/sh",
            arguments: ["-c", "sleep 5"],
            timeout: 0.1
        )

        XCTAssertTrue(result.timedOut)
        XCTAssertLessThan(Date().timeIntervalSince(startedAt), 3)
    }

    func testSystemCommandRunnerTerminatesDescendantsAtDeadline() throws {
        let marker = FileManager.default.temporaryDirectory
            .appendingPathComponent("syxx-lan-orphan-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: marker) }
        let script = "(sleep 0.6; printf orphan > \"$1\") & wait"

        let result = try SystemCommandRunner().run(
            "/bin/sh",
            arguments: ["-c", script, "sh", marker.path],
            timeout: 0.1
        )
        Thread.sleep(forTimeInterval: 0.9)

        XCTAssertTrue(result.timedOut)
        XCTAssertFalse(FileManager.default.fileExists(atPath: marker.path))
    }

    func testSystemCommandRunnerRejectsNonPositiveTimeoutWithoutTrap() {
        for timeout in [0.0, -1.0] {
            XCTAssertThrowsError(try SystemCommandRunner().run(
                "/usr/bin/true",
                arguments: [],
                timeout: timeout
            ))
        }
    }

    func testSnapshotNonEmptyUnparseableOutputIsUnreadableAndPassesTimeout() throws {
        let runner = StubCommandRunner(result: CommandResult(
            stdout: "localized output that contains no neighbour rows",
            stderr: "",
            exitCode: 0,
            timedOut: false
        ))

        let snapshot = try LANCartographer(commandRunner: runner).snapshot(timeout: 0.321)

        XCTAssertEqual(runner.observedTimeout, 0.321, accuracy: 0.0001)
        XCTAssertEqual(snapshot.status, .unreadable)
        XCTAssertFalse(snapshot.reason?.isEmpty ?? true)
        XCTAssertTrue(snapshot.devices.isEmpty)
    }

    func testSnapshotNonzeroExitPreservesDiagnosticAsUnreadableReason() throws {
        let runner = StubCommandRunner(result: CommandResult(
            stdout: "",
            stderr: "access denied",
            exitCode: 5,
            timedOut: false
        ))

        let snapshot = try LANCartographer(commandRunner: runner).snapshot()

        XCTAssertEqual(snapshot.status, .unreadable)
        XCTAssertTrue(snapshot.reason?.contains("5") ?? false)
        XCTAssertTrue(snapshot.reason?.contains("access denied") ?? false)
    }

    func testSnapshotTimeoutWithParsedRowsPreservesPartialEvidence() throws {
        let runner = StubCommandRunner(result: CommandResult(
            stdout: "? (192.168.50.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]",
            stderr: "",
            exitCode: nil,
            timedOut: true
        ))

        let snapshot = try LANCartographer(commandRunner: runner).snapshot()

        XCTAssertEqual(snapshot.status, .partial)
        XCTAssertFalse(snapshot.reason?.isEmpty ?? true)
        XCTAssertEqual(snapshot.devices.count, 1)
    }

    func testSnapshotSuccessWithParsedRowsIsComplete() throws {
        let runner = StubCommandRunner(result: CommandResult(
            stdout: "? (192.168.50.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]",
            stderr: "",
            exitCode: 0,
            timedOut: false
        ))

        let snapshot = try LANCartographer(commandRunner: runner).snapshot()

        XCTAssertEqual(snapshot.status, .complete)
        XCTAssertNil(snapshot.reason)
        XCTAssertEqual(snapshot.devices.count, 1)
    }

    func testSnapshotNonPositiveTimeoutIsUnreadableWithoutInvokingRunner() throws {
        let runner = StubCommandRunner(result: CommandResult(
            stdout: "? (192.168.50.1) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]",
            stderr: "",
            exitCode: 0,
            timedOut: false
        ))
        let cartographer = LANCartographer(commandRunner: runner)

        let zero = try cartographer.snapshot(timeout: 0)
        let negative = try cartographer.snapshot(timeout: -1)

        XCTAssertEqual(zero.status, .unreadable)
        XCTAssertEqual(negative.status, .unreadable)
        XCTAssertEqual(runner.invocationCount, 0)
    }

    func testSnapshotMixedValidAndMalformedRowsIsPartial() throws {
        let runner = StubCommandRunner(result: CommandResult(
            stdout: """
                ? (192.168.50.1) at aa:bb:cc:dd:ee:01 on en0 ifscope [ethernet]
                ? (192.168.50.2) at aa:bb:cc:dd:ee:gg on en0 ifscope [ethernet]
                """,
            stderr: "",
            exitCode: 0,
            timedOut: false
        ))

        let snapshot = try LANCartographer(commandRunner: runner).snapshot()

        XCTAssertEqual(snapshot.status, .partial)
        XCTAssertEqual(snapshot.devices.count, 1)
        XCTAssertEqual(snapshot.rejectedCandidateCount, 1)
        XCTAssertTrue(snapshot.reason?.contains("1") ?? false)
    }

    func testDiffUsesFullDeviceIdentityAndReportsDuplicateMACBindings() throws {
        let capture = """
            ? (192.168.50.10) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]
            ? (192.168.50.2) at aa:bb:cc:dd:ee:ff on en0 ifscope [ethernet]
            """
        let devices = ARPTableParser.parse(capture).map(LANDevice.init(entry:))
        let first = devices[0]
        let previous = try LANSnapshot.complete(devices: [first])
        let current = try LANSnapshot.complete(devices: devices)

        let diff = LANCartographer.diff(previous: previous, current: current)

        XCTAssertEqual(devices.count, 2)
        XCTAssertEqual(diff.newDevices.map(\.ipAddress), ["192.168.50.2"])
        XCTAssertTrue(diff.missingDevices.isEmpty)
        XCTAssertTrue(diff.changedAddresses.isEmpty)
        XCTAssertEqual(diff.duplicateMacBindings.count, 1)
        XCTAssertEqual(diff.duplicateMacBindings[0].macAddress, "aa:bb:cc:dd:ee:ff")
        XCTAssertEqual(diff.duplicateMacBindings[0].ipAddresses, ["192.168.50.2", "192.168.50.10"])
    }

    func testDiffDuplicateMACInPreviousSnapshotDoesNotTrap() throws {
        let first = device(ipAddress: "192.168.50.1", macAddress: "aa:bb:cc:dd:ee:ff")
        let second = device(ipAddress: "192.168.50.2", macAddress: "aa:bb:cc:dd:ee:ff")
        let previous = try LANSnapshot.complete(devices: [first, second])
        let current = try LANSnapshot.complete(devices: [first, second])

        let diff = LANCartographer.diff(previous: previous, current: current)

        XCTAssertTrue(diff.newDevices.isEmpty)
        XCTAssertTrue(diff.missingDevices.isEmpty)
        XCTAssertEqual(diff.duplicateMacBindings.count, 1)
    }

    func testDiffNonCompleteSnapshotsDoNotInferMissingDevices() throws {
        let observed = device(ipAddress: "192.168.50.1", macAddress: "aa:bb:cc:dd:ee:ff")
        let other = device(ipAddress: "192.168.50.2", macAddress: "aa:bb:cc:dd:ee:02")
        let complete = try LANSnapshot.complete(devices: [observed])
        let partial = try LANSnapshot.partial(devices: [other], reason: "truncated")
        let unreadable = try LANSnapshot.unreadable(reason: "unreadable")

        XCTAssertTrue(LANCartographer.diff(previous: complete, current: partial).missingDevices.isEmpty)
        XCTAssertTrue(LANCartographer.diff(previous: partial, current: complete).missingDevices.isEmpty)
        XCTAssertTrue(LANCartographer.diff(previous: complete, current: unreadable).missingDevices.isEmpty)
        XCTAssertTrue(LANCartographer.diff(previous: unreadable, current: complete).missingDevices.isEmpty)
    }

    func testDiffIncompletePlaceholdersAreNotDuplicateMACBindings() throws {
        let first = device(
            ipAddress: "192.168.50.1",
            macAddress: "(incomplete)",
            state: .incomplete
        )
        let second = device(
            ipAddress: "192.168.50.2",
            macAddress: "(incomplete)",
            state: .incomplete
        )
        let snapshot = try LANSnapshot.complete(devices: [first, second])

        let diff = LANCartographer.diff(previous: snapshot, current: snapshot)

        XCTAssertTrue(diff.duplicateMacBindings.isEmpty)
    }

    func testDiffMalformedMACsAreNotDuplicateMacBindings() throws {
        let first = device(
            ipAddress: "192.168.50.1",
            macAddress: "aa:bb:cc:dd:ee:gg",
            state: .unknown
        )
        let second = device(
            ipAddress: "192.168.50.2",
            macAddress: "aa:bb:cc:dd:ee:gg",
            state: .unknown
        )
        let snapshot = try LANSnapshot.complete(devices: [first, second])

        let diff = LANCartographer.diff(previous: snapshot, current: snapshot)

        XCTAssertTrue(diff.duplicateMacBindings.isEmpty)
    }

    func testSnapshotFactoriesRejectCompleteEmptyAndContradictoryStates() {
        let observed = device(ipAddress: "192.168.50.1", macAddress: "aa:bb:cc:dd:ee:ff")

        XCTAssertThrowsError(try LANSnapshot.complete(devices: []))
        XCTAssertThrowsError(try LANSnapshot.partial(devices: [], reason: "truncated"))
        XCTAssertThrowsError(try LANSnapshot.partial(devices: [observed], reason: ""))
        XCTAssertThrowsError(try LANSnapshot.create(
            devices: [observed],
            status: .complete,
            reason: "contradictory reason"
        ))
        XCTAssertThrowsError(try LANSnapshot.create(
            devices: [observed],
            status: .unreadable,
            reason: "contradictory devices"
        ))
    }

    func testSnapshotDecodingRejectsContradictoryState() throws {
        let observed = device(ipAddress: "192.168.50.1", macAddress: "aa:bb:cc:dd:ee:ff")
        let valid = try LANSnapshot.complete(devices: [observed])
        let encoded = try JSONEncoder().encode(valid)
        var object = try XCTUnwrap(
            JSONSerialization.jsonObject(with: encoded) as? [String: Any]
        )
        object["status"] = LANSnapshotStatus.unreadable.rawValue
        object["reason"] = "contradictory devices"
        let contradictory = try JSONSerialization.data(withJSONObject: object)

        XCTAssertThrowsError(try JSONDecoder().decode(LANSnapshot.self, from: contradictory))
    }

    private func device(
        ipAddress: String,
        macAddress: String,
        interface: String = "en0",
        state: ARPEntryState = .reachable
    ) -> LANDevice {
        LANDevice(entry: ARPEntry(
            host: nil,
            ipAddress: ipAddress,
            macAddress: macAddress,
            interface: interface,
            state: state,
            rawLine: ""
        ))
    }

    private final class StubCommandRunner: CommandRunning, @unchecked Sendable {
        private let result: CommandResult
        private(set) var observedTimeout: TimeInterval = 0
        private(set) var invocationCount = 0

        init(result: CommandResult) {
            self.result = result
        }

        func run(
            _ executable: String,
            arguments: [String],
            timeout: TimeInterval
        ) throws -> CommandResult {
            invocationCount += 1
            observedTimeout = timeout
            return result
        }
    }
}

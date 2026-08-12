import XCTest
import Foundation
@testable import SyxxApp
import SyxxKit

/// Cross-platform Witness "testify" (seal) terminal-outcome PARITY harness — macOS half.
///
/// This is the final piece of the B195..D103 Witness hardening arc. It proves the macOS behavior
/// matches the shared single-source-of-truth fixture
/// `corpus/witness-testify-parity/witness-testify-parity.json`, which the Windows
/// (Windows/Syxx.Tests/WitnessTestifyParityTests.cs) and Ubuntu
/// (Ubuntu/SyxxUI.Avalonia.Tests/WitnessTestifyParityTests.cs) halves load line-for-line too — the same
/// convention already used by corpus/stage0 and corpus/probe-parity.
///
/// TWO LAYERS (see the fixture's `_note`):
///   • `planner` scenarios drive the pure SyxxKit `WitnessTestifyPlanner` (byte-twin of Windows/Ubuntu's
///     Syxx.Kit planner) — TRUE parity: identical inputs, identical terminal outcome + drift on all three.
///   • `viewModel` scenarios drive the pre-flight guards that live ABOVE the planner in `AppModel`
///     (steps 1-4, legitimately per-platform per D103). macOS drives the two that are synchronous and
///     network-free (empty-domain, missing-key); no-comparison-result is only reachable with real
///     network egress on macOS (no comparer seam) and is pinned by AppModelWitnessTests instead — this
///     loader documents that skip via the fixture's `loaderDrives=false` / `skipReason`, never a silent pass.
///
/// A missing / empty / malformed fixture THROWS — a vacuously-green parity test is exactly the
/// green-that-lies the honesty spine forbids.
final class WitnessTestifyParityTests: XCTestCase {

    // MARK: - Fixture model

    private struct Fixture: Decodable { let scenarios: [Scenario] }
    private struct Scenario: Decodable {
        let id: String
        let layer: String                     // "planner" | "viewModel"
        let planner: PlannerInputs?           // present iff layer == planner
        let expected: PlannerExpected?        // present iff layer == planner (shared across platforms)
        let producesDrift: Bool?              // viewModel layer carries drift at the top level
        let perPlatform: [String: PlatformExpectation]?  // present iff layer == viewModel
    }
    private struct PlannerInputs: Decodable { let validKey: Bool; let redirectionDetected: Bool }
    private struct PlannerExpected: Decodable { let outcomeState: String; let producesDrift: Bool }
    private struct PlatformExpectation: Decodable {
        let outcomeState: String
        let loaderDrives: Bool
        let skipReason: String
    }

    enum FixtureError: Error, CustomStringConvertible {
        case notFound(String)
        case empty
        var description: String {
            switch self {
            case .notFound(let p): return "witness-testify-parity fixture not found at \(p). Single source of truth is missing."
            case .empty: return "witness-testify-parity fixture contains no scenarios — refusing to run vacuously."
            }
        }
    }

    private static func loadScenarios() throws -> [Scenario] {
        // #filePath = <repo>/macOS/Tests/SyxxAppTests/WitnessTestifyParityTests.swift — same depth as
        // the corpus/stage0 harness's resolution (SyxxAppTests → Tests → macOS → repo root).
        let repoRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxAppTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
        let url = repoRoot
            .appendingPathComponent("corpus")
            .appendingPathComponent("witness-testify-parity")
            .appendingPathComponent("witness-testify-parity.json")
        guard FileManager.default.fileExists(atPath: url.path) else { throw FixtureError.notFound(url.path) }
        let data = try Data(contentsOf: url)
        let fixture = try JSONDecoder().decode(Fixture.self, from: data)
        guard !fixture.scenarios.isEmpty else { throw FixtureError.empty }
        return fixture.scenarios
    }

    // MARK: - Planner harness (shared, offline, deterministic)

    // `SyxxKit.` qualifier is load-bearing (Foundation bridges NSComparisonResult as unqualified
    // `ComparisonResult`, which would collide). Same construction the sibling
    // WitnessTestifyPlannerTests uses, so the fixture drives the identical real core.
    private func makeComparison(redirected: Bool) -> SyxxKit.ComparisonResult {
        let localRecords  = ["10.0.0.5"].map { DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: $0) }
        let publicRecords = ["93.184.216.34"].map { DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: $0) }
        // For the no-redirection case, make local == public so the fields are honest either way.
        let local = redirected ? localRecords : publicRecords
        return SyxxKit.ComparisonResult(
            localServer: "192.168.1.1",
            localRecords: local,
            publicResolverResults: ["Cloudflare": publicRecords],
            isRedirectionDetected: redirected,
            localOutcome: ResolverQueryOutcome(records: local),
            publicResolverOutcomes: ["Cloudflare": ResolverQueryOutcome(records: publicRecords)]
        )
    }

    func testPlannerLayerMatchesFixture() throws {
        var asserted = 0
        for s in try Self.loadScenarios() where s.layer == "planner" {
            let inputs = try XCTUnwrap(s.planner, "planner scenario \(s.id) missing `planner` inputs")
            let expected = try XCTUnwrap(s.expected, "planner scenario \(s.id) missing `expected`")
            let comparison = makeComparison(redirected: inputs.redirectionDetected)
            let key = inputs.validKey ? WitnessSigner.generateKeyPair().0 : ""

            if expected.outcomeState == "signed" {
                let outcome = try WitnessTestifyPlanner.seal(
                    domain: "example.com", local: "192.168.1.1", comparison: comparison, privateKeyBase64: key)
                XCTAssertFalse(outcome.snapshot.signature.isEmpty, "\(s.id): a signed outcome must carry a real Ed25519 signature")
                XCTAssertEqual(try WitnessSigner.verifySnapshot(outcome.snapshot), .valid, "\(s.id): the sealed snapshot must self-verify")
                XCTAssertEqual(outcome.drift != nil, expected.producesDrift, "\(s.id): drift presence must match the fixture")
            } else if expected.outcomeState == "failed" {
                XCTAssertThrowsError(try WitnessTestifyPlanner.seal(
                    domain: "example.com", local: "192.168.1.1", comparison: comparison, privateKeyBase64: key),
                    "\(s.id): fixture expects a failed outcome (signer must throw)")
                XCTAssertFalse(expected.producesDrift, "\(s.id): a failed seal must never claim a drift")
            } else {
                XCTFail("\(s.id): unexpected planner outcomeState '\(expected.outcomeState)' — planner terminal states are only signed/failed")
            }
            asserted += 1
        }
        XCTAssertGreaterThan(asserted, 0, "no planner scenarios were exercised — fixture drift or a vacuous run")
    }

    // MARK: - View-model harness (macOS-specific pre-flight guards)

    // AppModel is @MainActor; WitnessKeyManager is a non-isolated enum backed by the real Keychain, so
    // (exactly as AppModelWitnessTests does) back up/restore whatever real signing key exists so a normal
    // run leaves the developer's key untouched.
    private var backedUpKey: String?

    /// Whether `setUpWithError` was actually able to take a backup. Only then may `tearDown` write.
    private var backupWasTaken = false

    override func setUpWithError() throws {
        try super.setUpWithError()
        // No probe, no backup — and therefore no restore. `loadKey()` swallows its OSStatus and
        // returns nil where the data-protection keychain is unreachable, so an unguarded backup here
        // reads as "there was no key" and would license `tearDown` to DELETE one.
        guard WitnessKeychainTestSupport.isDataProtectionKeychainWritable else { return }
        backedUpKey = WitnessKeyManager.loadKey()
        backupWasTaken = true
    }

    override func tearDownWithError() throws {
        // Restores the ORIGINAL bytes verbatim rather than through `saveKey`, which now validates
        // its input. A key persisted by a build from before that validation existed may be invalid,
        // and routing the restore through `saveKey` would throw on exactly those machines and leave
        // the developer's stored key destroyed by a test run. See WitnessKeychainTestSupport.
        //
        // Guarded on the probe because `tearDown` runs even when `setUpWithError` threw `XCTSkip`.
        if backupWasTaken {
            WitnessKeychainTestSupport.restore(backedUpKey)
        }
        backedUpKey = nil
        backupWasTaken = false
        try super.tearDownWithError()
    }

    @MainActor
    private func assertTerminalState(_ model: AppModel, matches outcomeState: String, _ id: String) {
        switch outcomeState {
        case "failed":
            if case .failed = model.witnessTestimonyState { /* ok */ }
            else { XCTFail("\(id): expected .failed, got \(model.witnessTestimonyState)") }
            // never stuck Running, never idle-collapsed
            XCTAssertNotEqual(model.witnessTestimonyState, .running, "\(id): must not be stuck Running")
            XCTAssertNotEqual(model.witnessTestimonyState, .idle, "\(id): a real failure must surface, not collapse to idle")
        case "idle":
            XCTAssertEqual(model.witnessTestimonyState, .idle, "\(id): expected idle")
        case "signed":
            XCTAssertEqual(model.witnessTestimonyState, .signed, "\(id): expected signed")
        default:
            XCTFail("\(id): unsupported outcomeState '\(outcomeState)'")
        }
    }

    @MainActor
    func testViewModelLayerMatchesFixture_macOS() throws {
        // Both drivable scenarios write the real Keychain (`generateAndStoreKey` / `deleteKey`), so
        // this arm needs the data-protection keychain. The planner arm above does not and still runs.
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        var drivenIds: [String] = []
        var skipped: [(String, String)] = []
        for s in try Self.loadScenarios() where s.layer == "viewModel" {
            let mac = try XCTUnwrap(s.perPlatform?["macos"], "viewModel scenario \(s.id) missing macos expectation")
            guard mac.loaderDrives else { skipped.append((s.id, mac.skipReason)); continue }

            switch s.id {
            case "empty-domain":
                // A key is present so the empty-domain guard (checked first) is unambiguously what fires.
                try WitnessKeyManager.generateAndStoreKey()
                let model = AppModel()
                model.domain = ""
                model.witnessTestify()
                assertTerminalState(model, matches: mac.outcomeState, s.id)
                // whitespace-only must trip the same guard, not silently "succeed"
                model.domain = "   "
                model.witnessTestify()
                assertTerminalState(model, matches: mac.outcomeState, s.id)

            case "missing-key":
                WitnessKeyManager.deleteKey()
                let model = AppModel()
                model.domain = "example.com"
                model.witnessTestify()
                assertTerminalState(model, matches: mac.outcomeState, s.id)

            default:
                XCTFail("\(s.id): fixture marks macos loaderDrives=true but this loader has no offline driver for it")
            }
            drivenIds.append(s.id)
        }
        // Non-vacuous: macOS must actually drive the two synchronous guards.
        XCTAssertTrue(drivenIds.contains("empty-domain"), "macOS must drive empty-domain")
        XCTAssertTrue(drivenIds.contains("missing-key"), "macOS must drive missing-key")
        // Surface (do not silently drop) the documented network-gated skip.
        for (id, reason) in skipped {
            XCTAssertFalse(reason.isEmpty, "\(id): a skipped scenario must carry a skipReason (honesty: no silent pass)")
        }
    }
}

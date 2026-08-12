import XCTest
@testable import SyxxApp
import SyxxKit

/// Regression coverage for the Witness GUI fix round of 2026-07-22: B195 (a dead/aborted testify
/// probe rendering as the pending "Not yet signed" placeholder), D096 (`witnessFree()` not actually
/// clearing the snapshot/signature/valid/result), D097 (Run-All-Wardens / the generic runner
/// routing Witness through `runWitness()`, which never touched the published testimony state and
/// swallowed the real thrown error), D098 (an untracked `Task` in `witnessTestify()` letting a rapid
/// re-testify race two Tasks, or a post-`Free` completion resurrect cleared state), and D052 (macOS
/// half — a Keychain save failure being silently dropped by `try?`).
///
/// This file was the single biggest coverage gap flagged across the Witness investigation: there was
/// no test file for `AppModel` at all before this one.
///
/// Phase 4.1 / B255: deterministic unit contracts inject `AppModel.witnessVantageSeam` so signing
/// and failure classification do not depend on host network. Real-network integration remains in
/// separately named tests (`testRealNetwork…`) and is not required for the unit suite to go green.
///
/// Keychain hygiene: `WitnessKeyManager` is backed by the REAL macOS Keychain item the shipped app
/// uses (service `com.milkandgin.syxx`, account `witness-key`) — there is no way to redirect it to an
/// isolated test account without modifying `WitnessKeyManager.swift` (key-handling production code,
/// not authorized for this task). `setUp`/`tearDown` back up whatever key already existed (if any) and
/// restore it after every test, so a normal test run leaves the developer's real signing key exactly
/// as it found it. A hard crash mid-test (not a normal assertion failure — `tearDown` still runs on
/// those) could in principle skip the restore; this is a known, narrow residual risk of testing
/// Keychain-backed code this way.
///
/// Both the backup and the restore are gated on `WitnessKeychainTestSupport
/// .isDataProtectionKeychainWritable`, a live once-per-run `SecItemAdd` probe. An unsigned xctest
/// bundle carries no keychain-access-groups entitlement and cannot touch the data-protection
/// keychain the product uses, so every test here that must write a real key skips with that reason
/// rather than failing on its first statement. The probe is runtime, not a build flag: sign the
/// bundle and these tests execute for real with no source change.
final class AppModelWitnessTests: XCTestCase {

    // Not MainActor-isolated on purpose: WitnessKeyManager is a plain, non-isolated enum (its
    // Keychain access is guarded by its own internal NSLock, not the main actor), and XCTestCase's
    // setUp/tearDown overrides are themselves nonisolated — keeping this property nonisolated avoids
    // an isolation mismatch on those overrides. Only the AppModel-touching helpers/tests below are
    // marked @MainActor, since AppModel itself is a @MainActor type.
    private var backedUpKey: String?

    /// Whether `setUpWithError` was actually able to take a backup. Only then may `tearDown` write.
    private var backupWasTaken = false

    override func setUpWithError() throws {
        try super.setUpWithError()
        // No probe, no backup — and therefore no restore. `loadKey()` swallows its OSStatus and
        // returns nil on a host that cannot read the data-protection keychain, so an unguarded
        // backup here reads as "there was no key" and would license `tearDown` to DELETE one.
        guard WitnessKeychainTestSupport.isDataProtectionKeychainWritable else { return }
        backedUpKey = WitnessKeyManager.loadKey()
        backupWasTaken = true
    }

    override func tearDownWithError() throws {
        // Restores the ORIGINAL bytes verbatim, not through `saveKey`. `saveKey` now validates its
        // input, and a key persisted by a build from before that validation existed may be invalid
        // (the reported case: the literal `lklklklk`, valid base64, 6 bytes where Ed25519 needs 32).
        // Routing the restore through `saveKey` would throw on exactly those machines and leave the
        // developer's stored key destroyed by a test run. Test scaffolding must leave the machine as
        // it found it, valid or not.
        //
        // Guarded on the probe because `tearDown` runs even when `setUpWithError` threw `XCTSkip`.
        // Restoring a backup that was never taken is how a nil "backup" turns into a real deletion.
        if backupWasTaken {
            WitnessKeychainTestSupport.restore(backedUpKey)
        }
        backedUpKey = nil
        backupWasTaken = false
        try super.tearDownWithError()
    }

    // MARK: - Helpers

    /// Polls `model.witnessTestimonyState` until it reaches a terminal state (`.signed` or
    /// `.failed`), or fails the test if it never does within `timeout` seconds. Treats BOTH `.idle`
    /// and `.running` as "not yet terminal": `witnessTestify()` flips to `.running` synchronously at
    /// the call site, but `run()`/`runWitness()` only flips it once the launched `Task` actually
    /// starts executing, which can leave the state briefly at `.idle` immediately after the call —
    /// stopping on `.idle` there would race the Task and falsely read "already done". A real DNS
    /// round-trip (UDP + DoH, six resolvers) is expected to finish well inside this window on any
    /// machine with normal network egress.
    @MainActor
    private func waitUntilTerminal(_ model: AppModel, timeout: Double = 30) async throws {
        let deadline = Date().addingTimeInterval(timeout)
        while true {
            switch model.witnessTestimonyState {
            case .idle, .running:
                if Date() > deadline {
                    XCTFail("witnessTestimonyState never reached a terminal state within \(timeout)s (stuck at \(model.witnessTestimonyState)) — either a real hang/regression, or this machine cannot reach 1.1.1.1/8.8.8.8/9.9.9.9")
                    return
                }
                try await Task.sleep(nanoseconds: 200_000_000)
            case .signed, .failed:
                return
            }
        }
    }

    // MARK: - 1. Empty domain -> .failed, never .running/.idle

    @MainActor
    func testEmptyDomainYieldsFailedNotRunningOrIdle() throws {
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()

        model.domain = ""
        model.witnessTestify()
        guard case .failed(let msg) = model.witnessTestimonyState else {
            return XCTFail("expected .failed for domain \"\", got \(model.witnessTestimonyState)")
        }
        XCTAssertEqual(msg, "Enter a domain to attest.")

        // Whitespace-only must trim to empty and take the same guard (not silently "succeed").
        model.domain = "   "
        model.witnessTestify()
        guard case .failed(let msg2) = model.witnessTestimonyState else {
            return XCTFail("expected .failed for whitespace-only domain, got \(model.witnessTestimonyState)")
        }
        XCTAssertEqual(msg2, "Enter a domain to attest.")
        XCTAssertNotEqual(model.witnessTestimonyState, .running)
        XCTAssertNotEqual(model.witnessTestimonyState, .idle)
    }

    // MARK: - 2. Missing signing key -> .failed with the correct message

    @MainActor
    func testMissingSigningKeyYieldsFailedWithExpectedMessage() {
        WitnessKeyManager.deleteKey()
        let model = AppModel()
        model.domain = "example.com"
        model.witnessTestify()
        XCTAssertEqual(
            model.witnessTestimonyState,
            .failed("A signing key is required — a snapshot signed with a default/empty key would be forgeable.")
        )
    }

    // MARK: - 3. D098 — Free during an in-flight testify must not resurrect state

    @MainActor
    func testFreeDuringInFlightTestifyDoesNotResurrectState() async throws {
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        model.domain = "example.com"

        model.witnessTestify()   // launches the real async Task; network legs are now in flight
        model.witnessFree()      // free immediately, well before the network calls can return

        XCTAssertEqual(model.witnessTestimonyState, .idle, "Free must clear state synchronously")
        XCTAssertNil(model.witnessSnapshot)
        XCTAssertEqual(model.witnessSignature, "")

        // Give the (cancelled) in-flight Task's real network calls time to actually finish and reach
        // its completion guard. This is exactly the window the D098 fix protects: before that fix,
        // this sleep is where the stale Task's completion handler would write witnessSnapshot /
        // witnessSignature and flip witnessTestimonyState back to .signed, resurrecting state the
        // user explicitly cleared with Free. Without this sleep the test would be vacuous — it would
        // pass before AND after the fix, because it would never actually wait for the orphaned
        // completion to fire.
        try await Task.sleep(nanoseconds: 10_000_000_000)

        XCTAssertEqual(model.witnessTestimonyState, .idle, "a completion firing after Free must not resurrect state (D098)")
        XCTAssertNil(model.witnessSnapshot, "Free must stay cleared even after the cancelled task's async work completes (D098)")
        XCTAssertEqual(model.witnessSignature, "")
    }

    // MARK: - 4. D098 — rapid re-testify must not corrupt state (last click wins, cleanly)

    /// HONESTY NOTE on this test's actual detection power (found while verifying these tests are not
    /// vacuous, per the task's own instruction to check that): reverting the D098 fix in a throwaway
    /// scratch copy of this package and re-running `testFreeDuringInFlightTestifyDoesNotResurrectState`
    /// reliably FAILED (proving that test genuinely catches the regression). Re-running THIS test
    /// against the same reverted copy, however, still PASSED — because without any artificial delay,
    /// task A ("example.com") and task B ("iana.org") both do the same real network work started
    /// microseconds apart, so B (started later) also tends to finish later and "wins" the race by
    /// sheer timing even with no cancellation logic at all. There is no seam in production code to
    /// force A to finish after B deterministically without adding a network-delay injection point,
    /// which is out of scope here. This test is therefore a real, non-fabricated check of an
    /// invariant that COULD be violated by exactly this bug — but its power to reliably catch a
    /// regression here specifically is probabilistic, not guaranteed, unlike test 3 above, which is
    /// the deterministic, verified-to-fail-without-the-fix proof of D098. Keeping this test anyway:
    /// it exercises the real rapid-re-testify code path and would still catch a regression that
    /// changes relative timing (e.g. a slower first domain), just not this exact one for certain.
    @MainActor
    func testRapidReTestifyDoesNotCorruptState() async throws {
        // Keychain reachability only. The live-DNS requirement below is a SEPARATE condition and is
        // still asserted, not skipped — see WitnessKeychainTestSupport's honesty boundary note.
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()

        model.domain = "example.com"
        model.witnessTestify()          // task A starts against "example.com"
        model.domain = "iana.org"
        model.witnessTestify()          // must cancel task A before starting task B against "iana.org"

        try await waitUntilTerminal(model)

        switch model.witnessTestimonyState {
        case .signed:
            let snap = try XCTUnwrap(model.witnessSnapshot)
            XCTAssertEqual(snap.targetDomain, "iana.org",
                           "D098: the superseded first task's result must never land — only the second (last) testify's result may be written")
            XCTAssertEqual(model.witnessSignature, snap.signature)
        case .failed(let msg):
            // Even if the second call itself failed for a real network reason, the FIRST
            // (superseded) call's snapshot must never have landed either.
            XCTAssertNil(model.witnessSnapshot, "a cancelled, superseded task must not have written a snapshot (failure was: \(msg))")
        default:
            XCTFail("expected a terminal state after both calls settled, got \(model.witnessTestimonyState)")
        }
    }

    // MARK: - 5. D096 — Free after a successful Testify+Verify actually clears state

    @MainActor
    func testFreeAfterSuccessfulVerifyClearsSnapshotSoVerifyReportsNothing() async throws {
        // Keychain reachability only. The F039 "fail, don't skip, if we never reach .signed" rule
        // below is a SEPARATE condition (live DNS egress) and is deliberately left as a failure.
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        model.domain = "example.com"
        model.witnessTestify()
        try await waitUntilTerminal(model)

        // F039: fail rather than skip if the witness pipeline doesn't reach .signed.
        // An unreachable resolver on a CI runner is itself a fact worth failing on —
        // skipping on the product's own state makes "broken witness" indistinguishable
        // from "no network", and both read green.
        guard case .signed = model.witnessTestimonyState else {
            XCTFail("D096 regression guard: witnessTestify did not reach .signed (state: \(model.witnessTestimonyState)). Cannot exercise the Free-after-Verify regression without a real signed snapshot. On CI, this is a network/pipeline failure worth failing on, not skipping.")
            return
        }

        model.witnessVerify()
        XCTAssertEqual(model.witnessResult, .valid)
        XCTAssertNotNil(model.witnessSnapshot)

        model.witnessFree()
        XCTAssertNil(model.witnessSnapshot, "D096: Free must null the snapshot")
        XCTAssertEqual(model.witnessSignature, "", "D096: Free must clear the signature")
        XCTAssertFalse(model.witnessValid, "D096: Free must clear the valid flag")
        XCTAssertEqual(model.witnessResult, .notVerified, "D096: Free must reset the result")

        // The regression: before D096, calling Verify here (with no re-Testify) would silently
        // re-show the OLD .valid result, because witnessSnapshot was never nulled by Free and
        // witnessVerify()'s `guard let snap = witnessSnapshot` never tripped.
        model.witnessVerify()
        XCTAssertEqual(model.witnessResult, .notVerified,
                        "D096 regression: Verify after Free must report nothing to verify, not resurrect the prior result")
        XCTAssertFalse(model.witnessValid)
    }

    // MARK: - 6. D097 — Run-All-Wardens / the generic runner routes through real state

    /// `runAllWardens()` and the generic single-tool `run()` both call the same private
    /// `runWitness()` (AppModel.swift:838-946) via `execute(.witness)`. Driving it through `run()`
    /// with `selection = .witness` exercises the identical shared code path `runAllWardens()` uses,
    /// without paying the cost of also running Prophet/Portal/Mirror/Glutton in the same test.
    @MainActor
    func testRunWitnessSuccessPathSetsRealSignedState() async throws {
        // Keychain reachability only. The F039 "fail, don't skip, if we never reach .signed" rule
        // below is a SEPARATE condition (live DNS egress) and is deliberately left as a failure.
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        model.domain = "example.com"
        model.selection = .witness
        model.run()
        try await waitUntilTerminal(model)

        // F039: fail rather than skip — see comment above.
        guard case .signed = model.witnessTestimonyState else {
            XCTFail("D097 regression guard: runWitness() did not reach .signed (state: \(model.witnessTestimonyState)). On CI, this is a network/pipeline failure worth failing on, not skipping.")
            return
        }
        XCTAssertNotNil(model.witnessSnapshot, "D097: a successful Run-All/generic-runner pass must populate the real snapshot")
        XCTAssertFalse(model.witnessSignature.isEmpty)
        XCTAssertTrue(model.showTestimony, "D097: the Witness tab must reflect that a run actually happened")
        XCTAssertTrue(model.output.contains("Signed witness snapshot for"),
                      "output should describe the real result, got: \(model.output)")
    }

    @MainActor
    func testRunWitnessFailurePathSetsRealFailedState() async throws {
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        model.domain = ""   // empty domain -> synchronous guard failure inside runWitness()
        model.selection = .witness
        model.run()
        try await waitUntilTerminal(model)

        XCTAssertEqual(model.witnessTestimonyState, .failed("Enter a domain."),
                        "D097: runWitness()'s own empty-domain guard message must be threaded into real state")
    }

    /// B255 deterministic unit: malformed signing key + injected successful vantage comparison.
    /// No host network. Failure case: inject `.noResult` instead of `.fixed` → wrong classification.
    @MainActor
    func testRunWitnessThreadsRealErrorNotGenericString() async throws {
        // Planting the legacy bad key is itself a data-protection keychain write, so this test needs
        // the same reachability as the `generateAndStoreKey` ones.
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        // A key that is present (so the "no key" guard is not what fires) but is not valid base64 /
        // not a valid Ed25519 private key, so WitnessSigner.createSignedSnapshot throws for real
        // inside runWitness()'s sign-snapshot ProbeRunner closure.
        //
        // Planted RAW, bypassing `WitnessKeyManager.saveKey`, because save-time validation now
        // rejects this string at the point of entry. That is not a reason to drop this test: save-
        // time validation does not retroactively clean keys that were ALREADY stored, so an existing
        // installation can still hold unusable key material, and the signing path must keep failing
        // honestly and legibly for those users. This reproduces precisely that installed state.
        XCTAssertTrue(WitnessKeychainTestSupport.writeRawKeyBypassingValidation("not-a-valid-base64-key!!!"),
                      "could not plant a legacy invalid key; the rest of this test would be vacuous")
        let model = AppModel()
        // B255: inject a completed vantage comparison so leg 2 (signing) always runs.
        model.witnessVantageSeam = .fixed(Self.cleanComparisonFixture(domain: "example.com"))
        model.domain = "example.com"
        model.selection = .witness
        model.run()
        try await waitUntilTerminal(model, timeout: 10)

        // NOT a skip. With a malformed key planted and a fixed successful comparison, no environment
        // can legitimately produce `.signed`: that would mean WitnessSigner accepted
        // "not-a-valid-base64-key!!!" as Ed25519 key material.
        guard case .failed(let msg) = model.witnessTestimonyState else {
            XCTFail("expected the malformed signing key to make signing fail; got \(model.witnessTestimonyState)")
            return
        }
        // Must not be the vantage-leg classification — that would mean the seam was ignored and
        // leg 2 never ran.
        XCTAssertNotEqual(msg, "Witness failed: vantage comparison did not return a result.",
                          "B255: injected comparison must reach the signing path")
        // D097 Fix 2 intent preserved: reuse the approved snapshot-creation prefix, and never fall
        // back to the old fixed generic string that hid the fact a real error was thrown.
        XCTAssertTrue(msg.hasPrefix("Could not create witness snapshot"),
                      "D097 Fix 2: must reuse the approved snapshot-creation prefix, got: \(msg)")
        XCTAssertNotEqual(msg, "Witness failed: could not create signed snapshot.",
                          "D097 regression: must not fall back to the old fixed generic string when a real error was thrown")
        XCTAssertFalse(ErrorPresenter.isBoilerplate(msg),
                       "owner Defect 2: crash-reporter boilerplate must never reach the Witness surface, got: \(msg)")
    }

    /// B255: injected probe-nil must surface the exact degraded failure classification.
    /// Failure case: change the production nil-branch string → this assertion goes red.
    @MainActor
    func testRunWitnessNoResultClassifiesAsVantageDidNotReturn() async throws {
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        model.witnessVantageSeam = .noResult
        model.domain = "example.com"
        model.selection = .witness
        model.run()
        try await waitUntilTerminal(model, timeout: 5)

        XCTAssertEqual(
            model.witnessTestimonyState,
            .failed("Witness failed: vantage comparison did not return a result."),
            "B255: probe-nil must map to the exact vantage-did-not-return classification"
        )
        XCTAssertNil(model.witnessSnapshot)
    }

    /// B255: injected comparison failure must use the snapshot-creation prefix, not the nil-branch.
    @MainActor
    func testRunWitnessInjectedComparisonFailureUsesSnapshotPrefix() async throws {
        try WitnessKeychainTestSupport.skipUnlessDataProtectionKeychainWritable()
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        model.witnessVantageSeam = .failed("resolver baseline unreachable")
        model.domain = "example.com"
        model.selection = .witness
        model.run()
        try await waitUntilTerminal(model, timeout: 5)

        guard case .failed(let msg) = model.witnessTestimonyState else {
            return XCTFail("expected .failed, got \(model.witnessTestimonyState)")
        }
        XCTAssertEqual(msg, "Could not create witness snapshot: resolver baseline unreachable")
        XCTAssertNotEqual(msg, "Witness failed: vantage comparison did not return a result.")
    }

    /// Shared fixture: a completed, non-redirection comparison for injected unit paths.
    private static func cleanComparisonFixture(domain: String) -> SyxxKit.ComparisonResult {
        let record = DNSRecord(name: domain, type: 1, ttl: 300, rdata: "93.184.216.34")
        return SyxxKit.ComparisonResult(
            localServer: "8.8.8.8",
            localRecords: [record],
            publicResolverResults: [
                "Cloudflare": [record],
                "Google": [record],
                "Quad9": [record]
            ],
            isRedirectionDetected: false,
            isInconclusive: false,
            isDegraded: false
        )
    }

    // MARK: - 7. D052 (macOS half) — key-save failure surfacing

    /// D052 fixed `saveWitnessKey` (AppModel.swift:1283-1300) so a REAL Keychain write failure
    /// surfaces as `witnessTestimonyState = .failed("Could not save the signing key to the
    /// Keychain: …")` instead of being silently swallowed by the old `try?`. Proving that branch
    /// honestly requires `WitnessKeyManager.saveKey` (or the underlying `SecItemAdd`/`SecItemUpdate`)
    /// to actually throw.
    ///
    /// This test previously DOCUMENTED THE DEFECT AS INTENDED BEHAVIOUR. It asserted that
    /// "a syntactically-fine-but-cryptographically-invalid key must not itself surface a save
    /// failure — format is validated at sign time, not save time", because `saveKey`'s only throw
    /// site guarded `base64Key.data(using: .utf8)`, which a native Swift `String` can never fail.
    /// Deferring the check to sign time is exactly how the literal string `lklklklk` (valid base64,
    /// 6 bytes, where Ed25519 needs 32) came to sit in a real Keychain and only surface, months
    /// later, as an unreadable signing error. `WitnessKeyManager.saveKey` now validates at the point
    /// of entry, so the old assertions have been INVERTED, not softened.
    ///
    /// That also closes the gap this test used to document: `saveKey` now has a throw site a caller
    /// can reach, so `AppModel.saveWitnessKey`'s D052 `catch` branch is provable without touching
    /// the developer's real Keychain at all (the rejection happens before any Keychain call).
    ///
    /// STILL NOT COVERED, honestly: the specific sub-case of a genuine Keychain WRITE failure
    /// (`SecItemAdd`/`SecItemUpdate` returning non-success on a locked keychain, ACL denial, disk
    /// full). Forcing that still needs either manipulation of the developer's real login keychain
    /// lock state (unsafe, not reliably restorable) or a write-function injection seam in
    /// `WitnessKeyManager`. What is proven below is the catch branch and its surfaced state, not
    /// that specific cause.
    @MainActor
    func testSaveWitnessKeyRejectsInvalidKeyMaterialAndSurfacesIt() throws {
        let model = AppModel()
        XCTAssertEqual(model.witnessTestimonyState, .idle)
        let storedKeyBefore = WitnessKeyManager.loadKey()

        let saved = model.saveWitnessKey("not-a-valid-base64-key!!!")

        XCTAssertFalse(saved, "unusable key material must be rejected at save time, not persisted")
        XCTAssertEqual(WitnessKeyManager.loadKey(), storedKeyBefore,
                       "a rejected key must not replace or delete the stored key")

        guard case .failed(let msg) = model.witnessTestimonyState else {
            return XCTFail("a rejected key must surface as a real failure state, got \(model.witnessTestimonyState)")
        }
        XCTAssertFalse(ErrorPresenter.isBoilerplate(msg),
                       "crash-reporter boilerplate must never reach the Witness surface, got: \(msg)")
        XCTAssertTrue(msg.contains("Ed25519"),
                      "the message must say what was actually wrong with the key, got: \(msg)")

        // KNOWN WORDING DEFECT, flagged not asserted-as-good. `AppModel.saveWitnessKey` prefixes
        // every failure with "Could not save the signing key to the Keychain: ", so a rejection
        // caused by the KEY MATERIAL currently reads as though the Keychain were at fault. The
        // appended cause corrects it, but the prefix mis-attributes. Fixing it is a user-facing copy
        // change in SyxxApp and needs owner approval, so it is recorded here rather than silently
        // changed. This assertion pins the CURRENT text so the follow-up is not forgotten.
        XCTAssertTrue(msg.hasPrefix("Could not save the signing key to the Keychain: "),
                      "prefix drifted; re-check the mis-attribution note above. Got: \(msg)")
    }
}

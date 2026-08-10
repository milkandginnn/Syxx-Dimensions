import XCTest
import Security
@testable import SyxxApp
import SyxxKit

/// M-01 — the Linux → macOS adoption of RG-2 (and the same defect the .NET side closed as G-02a).
///
/// THE DEFECT THIS PINS. `WitnessKeyManager.hasKey` maps `.undetermined -> false`
/// (`WitnessKeyManager.swift:38-44`) and its own doc comment tells callers that need to tell "no
/// key" from "could not ask" to use `checkKeyPresence()`. `AppModel.witnessVerify()` called `hasKey`
/// anyway and set `.noKey`, which the Witness pane rendered as *"no signing key on this machine.
/// **Enter your key above, then verify again.**"* A user with a merely LOCKED Keychain was therefore
/// told they had no key and instructed to type one in — and `saveWitnessKey` wrote it with no
/// presence check at all, destroying the identity every testimony already sealed on that machine
/// verifies against. Unrecoverable, on a product whose entire value is that its evidence holds up.
///
/// The tests below are about the WRITE half, because that is the half that destroys data. The
/// `.undetermined` READ half cannot be produced on demand — it needs a Keychain that genuinely
/// refuses (locked, auth denied, prompt cancelled), which no in-process API can force. What IS
/// pinned here is that the two absence states are distinct values, so a later "simplification" back
/// to a single `.noKey` fails this file rather than silently reinstating the defect.
///
/// KEYCHAIN HYGIENE. Identical to `AppModelWitnessTests`: these exercise the REAL Keychain item the
/// shipped app uses (service `com.milkandgin.syxx`, account `witness-key`), because
/// `WitnessKeyManager` has no redirection seam and adding one would mean editing production key
/// handling. `setUp`/`tearDown` back up whatever key the developer already had and restore the
/// original bytes verbatim, so a normal run leaves the machine as it found it. A hard crash
/// mid-test could skip the restore; that is the known, narrow residual risk of testing
/// Keychain-backed code this way.
final class AppModelWitnessKeyPresenceTests: XCTestCase {

    private var backedUpKey: String?

    override func setUpWithError() throws {
        try super.setUpWithError()
        WitnessKeyManager.useDataProtectionKeychain = false
        backedUpKey = WitnessKeyManager.loadKey()
    }

    override func tearDownWithError() throws {
        WitnessKeychainTestSupport.restore(backedUpKey)
        backedUpKey = nil
        try super.tearDownWithError()
    }

    /// A second, valid, DIFFERENT Ed25519 private key to attempt an overwrite with.
    private func freshPrivateKey() -> String {
        WitnessSigner.generateKeyPair().privateKey
    }

    // MARK: - 1. The destructive core: an existing key is never replaced without consent

    @MainActor
    func testSavingOverAnExistingKeyIsRefusedAndLeavesTheStoredKeyIntact() throws {
        try WitnessKeyManager.generateAndStoreKey()
        let original = try XCTUnwrap(WitnessKeyManager.loadKey(), "setup: a key must be stored")
        let replacement = freshPrivateKey()
        XCTAssertNotEqual(original, replacement, "setup: the two keys must actually differ")

        let model = AppModel()
        let saved = model.saveWitnessKey(replacement)

        XCTAssertFalse(saved, "a write over an existing identity must not report success")
        XCTAssertTrue(model.witnessKeyOverwritePending,
                      "the view needs to know a confirmation is owed")
        WitnessKeyManager.invalidateCacheForTesting()
        XCTAssertEqual(WitnessKeyManager.loadKey(), original,
                       "M-01 regression: the stored signing key was replaced without confirmation — "
                       + "every previously sealed testimony would now fail to verify")
    }

    // MARK: - 2. Explicit consent still works — the gate is a prompt, not a wall

    @MainActor
    func testConfirmedOverwriteReplacesTheStoredKey() throws {
        try WitnessKeyManager.generateAndStoreKey()
        let original = try XCTUnwrap(WitnessKeyManager.loadKey())
        let replacement = freshPrivateKey()

        let model = AppModel()
        XCTAssertFalse(model.saveWitnessKey(replacement), "first attempt must ask")
        XCTAssertTrue(model.saveWitnessKey(replacement, confirmOverwrite: true),
                      "an explicitly confirmed replacement must go through")

        WitnessKeyManager.invalidateCacheForTesting()
        XCTAssertEqual(WitnessKeyManager.loadKey(), replacement)
        XCTAssertNotEqual(WitnessKeyManager.loadKey(), original)
        XCTAssertTrue(model.hasWitnessKey)
    }

    // MARK: - 3. The first key on a machine is written with no prompt at all

    @MainActor
    func testFirstKeyOnACleanMachineSavesWithoutAConfirmation() {
        WitnessKeychainTestSupport.deleteKey()
        let model = AppModel()
        let key = freshPrivateKey()

        XCTAssertEqual(model.witnessKeyWriteGate(), .proceed,
                       "an empty Keychain must not demand a confirmation — there is nothing to lose")
        XCTAssertTrue(model.saveWitnessKey(key))
        XCTAssertFalse(model.witnessKeyOverwritePending)
        WitnessKeyManager.invalidateCacheForTesting()
        XCTAssertEqual(WitnessKeyManager.loadKey(), key)
    }

    // MARK: - 4. Generate Keypair is the same destruction with fewer keystrokes

    @MainActor
    func testGenerateKeypairDoesNotSilentlyReplaceAnExistingIdentity() throws {
        try WitnessKeyManager.generateAndStoreKey()
        let original = try XCTUnwrap(WitnessKeyManager.loadKey())

        let model = AppModel()
        let generated = model.generateWitnessKey()

        XCTAssertFalse(generated, "Generate Keypair must not overwrite an existing identity unasked")
        XCTAssertTrue(model.witnessKeyOverwritePending)
        WitnessKeyManager.invalidateCacheForTesting()
        XCTAssertEqual(WitnessKeyManager.loadKey(), original,
                       "M-01 regression: one click replaced the signing key with no confirmation")

        XCTAssertTrue(model.generateWitnessKey(confirmOverwrite: true))
        WitnessKeyManager.invalidateCacheForTesting()
        XCTAssertNotEqual(WitnessKeyManager.loadKey(), original)
    }

    // MARK: - 5. A confirmed-absent Keychain still gets the "enter your key" invitation

    @MainActor
    func testVerifyWithNoSnapshotIsNotVerifiedRatherThanAKeyClaim() {
        WitnessKeychainTestSupport.deleteKey()
        let model = AppModel()
        model.witnessVerify()
        // With no snapshot, the key question is not even reached — "nothing to verify" is the
        // honest answer and must not be dressed up as a key problem (D069/B049).
        XCTAssertEqual(model.witnessResult, .notVerified)
    }

    // MARK: - 6. The tri-state must stay a tri-state (spine #3)

    func testConfirmedAbsentAndUndeterminedAreDistinctResults() {
        let absent = AppModel.WitnessResult.noKey(.confirmedAbsent)
        let undetermined = AppModel.WitnessResult.noKey(.undetermined(errSecInteractionNotAllowed))

        XCTAssertNotEqual(absent, undetermined,
                          "M-01: 'there is no key' and 'the Keychain could not be read' are different "
                          + "facts. Collapsing them is the defect — only one of them justifies telling "
                          + "the user to enter a new key, and the other one destroys their identity.")
        XCTAssertEqual(undetermined,
                       .noKey(.undetermined(errSecInteractionNotAllowed)),
                       "the OSStatus must be carried, not discarded")
        XCTAssertNotEqual(AppModel.WitnessResult.noKey(.undetermined(errSecInteractionNotAllowed)),
                          .noKey(.undetermined(errSecAuthFailed)))
    }

    // MARK: - 7. The write gate reads the tri-state, not the collapsing `hasKey`

    @MainActor
    func testWriteGateDemandsConfirmationWhileAKeyIsPresent() throws {
        try WitnessKeyManager.generateAndStoreKey()
        let model = AppModel()
        XCTAssertEqual(model.witnessKeyWriteGate(), .needsOverwriteConfirmation)
    }
}

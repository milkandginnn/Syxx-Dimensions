import Foundation
import Security
import XCTest
import SyxxKit

/// Raw Keychain access for the witness signing key, for TESTS ONLY.
///
/// WHY THIS EXISTS. `WitnessKeyManager.saveKey` now validates its input (rejects anything that is
/// not exactly 32 base64-decoded bytes of Ed25519 key material) so that garbage can no longer be
/// persisted and then fail, opaquely, at signing time months later. That validation creates two
/// needs the supported API deliberately cannot serve:
///
///   1. RESTORE FIDELITY. The witness test classes back up whatever key the developer's real
///      Keychain already holds and restore it in `tearDown`. If that pre-existing key was written by
///      a build from BEFORE the validation existed, it may be invalid, and restoring it through
///      `saveKey` would now throw. Losing or silently replacing a developer's stored key because a
///      test ran is not acceptable, so the restore writes the original bytes back verbatim.
///
///   2. REPRODUCING A LEGACY BAD KEY. Save-time validation does NOT retroactively clean keys that
///      were already stored. An existing installation can still hold an unusable key (the reported
///      case: the literal string `lklklklk`, valid base64, 6 bytes where Ed25519 needs 32), and the
///      signing path must still fail honestly and legibly for those users. Reaching that state now
///      requires bypassing the validator, which is exactly what a pre-validation build did.
///
/// This is test scaffolding, NOT a production seam: it lives in the test target, nothing in
/// `SyxxKit` or `SyxxApp` can call it, and no validation in production key handling is weakened.
/// The service/account pair is the one `WitnessKeyManager` uses and is already documented verbatim
/// in `AppModelWitnessTests`.
enum WitnessKeychainTestSupport {

    private static let service = "com.milkandgin.syxx"
    private static let account = "witness-key"

    /// MUST mirror `WitnessKeyManager.keychainQuery` (SyxxKit/WitnessKeyManager.swift) attribute for
    /// attribute — most of all `kSecUseDataProtectionKeychain`.
    ///
    /// DATA-LOSS DEFECT this pins closed. Without that key, macOS routes the request to the LEGACY
    /// file-based keychain, while the product routes to the DATA-PROTECTION keychain. `service` and
    /// `account` match, so nothing looks wrong — but they are two different stores. The witness test
    /// classes back up the existing key through `WitnessKeyManager.loadKey()` (data-protection) and
    /// restore it here; with the stores mismatched, `restore` deposited a developer's real signing key
    /// into a store the app never reads, and `deleteKey()` deleted from that same wrong store. The app
    /// then reported no key, silently and unrecoverably. It fired from `tearDown`, so skipping the
    /// tests did not prevent it: `XCTSkip` thrown in `setUpWithError` still runs `tearDown`.
    ///
    /// On a host that cannot reach the data-protection keychain (see
    /// `isDataProtectionKeychainWritable`) the writes below now fail honestly with
    /// `errSecMissingEntitlement` (-34018) instead of succeeding against the wrong store. That is the
    /// intended outcome: a loud no-op beats a silent write to a store nothing reads.
    private static var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
            kSecUseDataProtectionKeychain as String: true
        ]
    }

    /// Writes `raw` as the stored witness key WITHOUT validating it, mimicking what a build from
    /// before the save-time check would have persisted.
    /// - Returns: `true` if the write succeeded.
    @discardableResult
    static func writeRawKeyBypassingValidation(_ raw: String) -> Bool {
        guard let data = raw.data(using: .utf8) else { return false }
        let query = baseQuery
        let status: OSStatus
        if SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess {
            status = SecItemUpdate(query as CFDictionary,
                                   [kSecValueData as String: data] as CFDictionary)
        } else {
            var addQuery = query
            addQuery[kSecValueData as String] = data
            status = SecItemAdd(addQuery as CFDictionary, nil)
        }
        guard status == errSecSuccess else { return false }
        // WitnessKeyManager caches the key per launch and writes through on save. A raw write goes
        // behind that cache, so it must be invalidated or `loadKey()` would return a stale value.
        WitnessKeyManager.invalidateCacheForTesting()
        return true
    }

    /// Deletes the stored witness key, if any.
    static func deleteKey() {
        SecItemDelete(baseQuery as CFDictionary)
        WitnessKeyManager.invalidateCacheForTesting()
    }

    /// Restores a backed-up key verbatim, whether or not it would pass validation today.
    /// Use in `tearDown` so a test run leaves the developer's Keychain exactly as it found it.
    static func restore(_ backedUpKey: String?) {
        if let key = backedUpKey {
            _ = writeRawKeyBypassingValidation(key)
        } else {
            deleteKey()
        }
    }

    // MARK: - Data-protection keychain availability

    /// The reason surfaced by `skipUnlessDataProtectionKeychainWritable()`. Named so the skip text
    /// says what is actually wrong rather than "keychain error".
    static let unavailableReason = """
        the data-protection keychain is unavailable to this test bundle: the SecItemAdd probe failed, \
        which on macOS means the xctest bundle is unsigned and therefore carries no \
        keychain-access-groups entitlement (errSecMissingEntitlement, -34018). WitnessKeyManager \
        stores the signing key there, so no test that must write or read a real witness key can run \
        here. Sign the test bundle with a valid identity and these tests execute for real.
        """

    /// Whether this process can actually WRITE the data-protection keychain, measured once per test
    /// run by a live `SecItemAdd` against a throwaway service that no product code ever touches.
    ///
    /// Deliberately a runtime probe and not a compile-time flag or an environment opt-out: a build
    /// flag would keep skipping on a host that CAN do this (a signed bundle, a CI runner with an
    /// identity), which is precisely the host where the coverage matters most. `static let` gives
    /// lazy, once-only, thread-safe evaluation.
    static let isDataProtectionKeychainWritable: Bool = probeDataProtectionKeychain()

    private static func probeDataProtectionKeychain() -> Bool {
        // A unique service per run so a probe item can never collide with, or be mistaken for, the
        // real witness item — and so a leaked probe from a crashed run cannot make this return true.
        let probeQuery: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "com.milkandgin.syxx.keychain-probe.\(UUID().uuidString)",
            kSecAttrAccount as String: "probe",
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
            kSecUseDataProtectionKeychain as String: true
        ]
        var addQuery = probeQuery
        addQuery[kSecValueData as String] = Data("probe".utf8)
        guard SecItemAdd(addQuery as CFDictionary, nil) == errSecSuccess else { return false }
        SecItemDelete(probeQuery as CFDictionary)
        return true
    }

    /// Skips the calling test when the data-protection keychain cannot be written on this host.
    ///
    /// HONESTY BOUNDARY. This covers exactly one condition — keychain reachability. It is NOT a
    /// stand-in for network availability: the witness tests that additionally need real DNS egress
    /// (`testRunWitnessSuccessPathSetsRealSignedState`,
    /// `testFreeAfterSuccessfulVerifyClearsSnapshotSoVerifyReportsNothing`,
    /// `testRapidReTestifyDoesNotCorruptState`) were deliberately written to FAIL rather than skip
    /// when the pipeline cannot reach `.signed`, and that behaviour is left intact. On a signed host
    /// with no egress they still go red, as intended.
    static func skipUnlessDataProtectionKeychainWritable() throws {
        try XCTSkipUnless(isDataProtectionKeychainWritable, unavailableReason)
    }
}

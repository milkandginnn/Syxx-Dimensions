import Foundation
import Security
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

    private static var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly
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
}

import Foundation
import Security

/// Manages the Ed25519 witness signing key in the macOS Keychain.
///
/// The private key is stored as a generic-password item with
/// `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` in the data-protection keychain
/// (`kSecUseDataProtectionKeychain`), limiting its exposure to this device while unlocked.
///
/// Zeroization is best-effort: `loadKey()` returns a base64 `String`; callers
/// should zeroize any `Data` they decode from it as soon as the signing
/// operation completes. Swift may still make internal copies beyond the
/// caller's control — Keychain storage is the primary defence.
public enum WitnessKeyManager {

    private static let bundleIdentifier = "com.milkandgin.syxx"
    private static let account = "witness-key"

    // MARK: - Keychain presence tri-state

    /// D084: tri-state result for Keychain queries. `absent` means the item genuinely does not
    /// exist (errSecItemNotFound). `undetermined` means the Keychain could not be queried
    /// (locked, auth failed, user cancelled, I/O error) — collapsing this to "absent" fabricated
    /// a definite "no key" from a transient condition.
    public enum KeyPresence {
        case present
        case absent
        case undetermined(OSStatus)
    }

    // MARK: - Public API

    /// Whether a witness signing key is present in the Keychain.
    ///
    /// D084: `false` now genuinely means "confirmed absent" (errSecItemNotFound). Keychain
    /// errors (locked, auth failed, etc.) are surfaced via `checkKeyPresence()` — callers that
    /// need to distinguish "no key" from "could not ask" should use that method instead.
    public static var hasKey: Bool {
        switch checkKeyPresence() {
        case .present: return true
        case .absent: return false
        case .undetermined: return false
        }
    }

    /// D084: tri-state Keychain presence check. Distinguishes "genuinely absent" from
    /// "could not read the Keychain" so the UI can surface an honest message rather than
    /// a fabricated "no key".
    public static func checkKeyPresence() -> KeyPresence {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecReturnData as String: false,
            kSecUseDataProtectionKeychain as String: true
        ]
        let status = SecItemCopyMatching(query as CFDictionary, nil)
        if status == errSecSuccess { return .present }
        if status == errSecItemNotFound { return .absent }
        return .undetermined(status)
    }

    // Per-launch cache for the signing key. Same reason as LicenseManager's keychain cache: an
    // ad-hoc/unsigned build prompts for the login password on every keychain access, and Witness
    // signing loads the key repeatedly. Cached on first load; write-through on save, cleared on
    // delete. Only this app writes the key, so the cache cannot go stale behind us. A Developer-ID
    // signed build stops the prompt entirely; this is the pre-signing mitigation.
    private static let _keyLock = NSLock()
    // Two-level optional: .none = not loaded yet; .some(nil) = loaded, absent; .some(x) = loaded key.
    nonisolated(unsafe) private static var _keyCache: String?? = .none

    /// Loads the witness signing key from the Keychain.
    /// - Returns: The base64-encoded Ed25519 private key, or `nil` if no key is stored.
    public static func loadKey() -> String? {
        _keyLock.lock()
        if let cached = _keyCache {          // already loaded this session (value may be nil = absent)
            _keyLock.unlock()
            return cached
        }
        _keyLock.unlock()

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecUseDataProtectionKeychain as String: true
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        let key = (status == errSecSuccess) ? (result as? Data).flatMap { String(data: $0, encoding: .utf8) } : nil

        // D084: only cache successful reads and confirmed absence (errSecItemNotFound).
        // A transient Keychain error (locked, auth failed, user cancelled) must NOT be
        // cached as "absent" — that would make one blocked prompt suppress signing for
        // the entire process lifetime.
        if status == errSecSuccess || status == errSecItemNotFound {
            _keyLock.lock()
            _keyCache = .some(key)
            _keyLock.unlock()
        }
        return key
    }

    /// Saves the witness signing key to the Keychain.
    ///
    /// The key is validated BEFORE it is written. Storing an unvalidated string defers the failure
    /// all the way to signing time, which is how the literal `lklklklk` (valid base64, 6 bytes, not
    /// the 32 an Ed25519 private key requires) came to sit in the Keychain and only surface as an
    /// opaque signer error long after the user could connect it to what they typed. The check is
    /// `WitnessSigner.validatePrivateKey`, the same one the signer applies, so save-time acceptance
    /// and sign-time acceptance cannot drift apart.
    ///
    /// - Parameter base64Key: A base64-encoded Ed25519 private key (32 raw bytes).
    /// - Throws: `WitnessSignerError.emptyKey` / `.invalidPrivateKey` if the input is not usable
    ///   Ed25519 signing key material; `LicenseManagerError.keychainError` on Keychain failure.
    public static func saveKey(_ base64Key: String) throws {
        try WitnessSigner.validatePrivateKey(base64: base64Key)
        guard let data = base64Key.data(using: .utf8) else {
            throw LicenseManagerError.invalidKeyEncoding
        }
        try writeKeychainData(data, account: account)
        _keyLock.lock(); _keyCache = .some(base64Key); _keyLock.unlock()   // write-through
    }

    /// Drops the per-launch key cache so the next `loadKey()` re-reads the Keychain.
    ///
    /// TEST SUPPORT ONLY, and deliberately narrow: it clears an in-memory cache and nothing else. It
    /// grants no ability to store a key, weakens no validation, and cannot make an invalid key
    /// usable. It exists because the witness test classes must write the Keychain item directly to
    /// reproduce a key persisted by a build from before save-time validation existed, and such a
    /// write goes behind this cache. Do not call it from product code: the cache cannot go stale in
    /// normal operation, since only this type writes the item.
    public static func invalidateCacheForTesting() {
        _keyLock.lock(); _keyCache = .none; _keyLock.unlock()
    }

    /// Deletes the witness signing key from the Keychain, if present.
    ///
    /// D084: returns `true` only when the key was actually deleted or was already absent.
    /// A locked keychain or other error returns `false` — the old code discarded the status
    /// and set the cache to known-absent, which meant the private key was still on disk
    /// while the app behaved as if it were destroyed.
    @discardableResult
    public static func deleteKey() -> Bool {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecUseDataProtectionKeychain as String: true
        ]
        let status = SecItemDelete(query as CFDictionary)
        // Only clear the cache when the delete actually succeeded or the item was already gone.
        if status == errSecSuccess || status == errSecItemNotFound {
            _keyLock.lock(); _keyCache = .some(nil); _keyLock.unlock()
            return true
        }
        // Delete failed (locked keychain, auth error, etc.) — do NOT mark as absent.
        return false
    }

    /// Generates a new Ed25519 keypair and stores the private key in the Keychain.
    /// - Returns: The base64-encoded public key (the caller may display this).
    /// - Throws: `LicenseManagerError.keychainError` on Keychain failure.
    @discardableResult
    public static func generateAndStoreKey() throws -> String {
        let (priv, pub) = WitnessSigner.generateKeyPair()
        try saveKey(priv)
        return pub
    }

    // MARK: - Keychain plumbing

    private static func keychainQuery(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
            // B188: use the data-protection keychain so the accessibility class is enforced.
            kSecUseDataProtectionKeychain as String: true
        ]
    }

    private static func writeKeychainData(_ data: Data, account: String) throws {
        // D052: update-when-exists instead of a non-atomic delete-then-add. The old code called
        // SecItemDelete then SecItemAdd with no lock, leaving a window where the key was gone but
        // the new one not yet written. This mirrors LicenseManager.writeKeychainData: if the item
        // exists, update it in place (atomic); otherwise add it.
        let query = keychainQuery(account: account)
        if SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess {
            let update: [String: Any] = [kSecValueData as String: data]
            let status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
            guard status == errSecSuccess else {
                throw LicenseManagerError.keychainError(status)
            }
        } else {
            var addQuery = query
            addQuery[kSecValueData as String] = data
            let status = SecItemAdd(addQuery as CFDictionary, nil)
            guard status == errSecSuccess else {
                throw LicenseManagerError.keychainError(status)
            }
        }
    }
}

import Foundation
import Security
import CryptoKit

#if canImport(Darwin)
import Darwin
#endif

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

    /// When `true`, Keychain queries include `kSecUseDataProtectionKeychain` so the
    /// accessibility class (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`) is enforced.
    /// This requires the `keychain-access-groups` entitlement, which `.xctest` bundles
    /// do not have. Set to `false` in test `setUp()` to avoid `errSecMissingEntitlement`.
    nonisolated(unsafe) public static var useDataProtectionKeychain = true

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
    /// - Returns: `true` only when the Keychain confirms the item exists. Returns `false` for
    ///   both **confirmed absent** (`errSecItemNotFound`) and **undetermined** (Keychain locked,
    ///   auth failed, user cancelled, I/O error). The two false-cases are deliberately collapsed
    ///   here: callers that need to distinguish "no key" from "could not ask" MUST use
    ///   `checkKeyPresence()` instead, which returns the full `KeyPresence` tri-state.
    ///
    /// G-17: product code (AppModel.swift) uses `checkKeyPresence()` directly. This property
    /// exists for simple boolean contexts (e.g. test guards) where the collapse is acceptable.
    public static var hasKey: Bool {
        switch checkKeyPresence() {
        case .present: return true
        case .absent: return false
        case .undetermined: return false   // G-17: deliberate collapse — see checkKeyPresence()
        }
    }

    /// D084: tri-state Keychain presence check. Distinguishes "genuinely absent" from
    /// "could not read the Keychain" so the UI can surface an honest message rather than
    /// a fabricated "no key".
    public static func checkKeyPresence() -> KeyPresence {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecReturnData as String: false
        ]
        if useDataProtectionKeychain {
            query[kSecUseDataProtectionKeychain as String] = true
        }
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

        let query: [String: Any] = {
            var q: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: bundleIdentifier,
                kSecAttrAccount as String: account,
                kSecReturnData as String: true,
                kSecMatchLimit as String: kSecMatchLimitOne
            ]
            if useDataProtectionKeychain {
                q[kSecUseDataProtectionKeychain as String] = true
            }
            return q
        }()
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        // E029: unwrap device-specific key wrapping if present, with backward compatibility for
        // legacy plain base64 keys stored before E029.
        let key: String? = {
            guard status == errSecSuccess, let raw = result as? Data else { return nil }
            if let rawStr = String(data: raw, encoding: .utf8), rawStr.hasPrefix(wrappedKeyPrefix) {
                let wrappedData = raw.dropFirst(wrappedKeyPrefix.utf8.count)
                guard let unwrapped = unwrapKey(Data(wrappedData)),
                      let keyStr = String(data: unwrapped, encoding: .utf8) else { return nil }
                return keyStr
            }
            // Legacy plain base64 key (pre-E029) — use as-is for backward compatibility.
            return String(data: raw, encoding: .utf8)
        }()

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
        guard let plaintext = base64Key.data(using: .utf8) else {
            throw LicenseManagerError.invalidKeyEncoding
        }
        // E029: wrap the signing key with a device-specific AES-GCM key before Keychain storage.
        // The wrapped form is prefixed with "wrapped:v1:" so loadKey() can distinguish it from
        // legacy plain base64 keys stored before E029.
        let data: Data
        if let wrapped = wrapKey(plaintext) {
            data = Data(wrappedKeyPrefix.utf8) + wrapped
        } else {
            // Wrapping unavailable (non-macOS or IOKit failure) — store as plain base64 (legacy format).
            data = plaintext
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
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account
        ]
        if useDataProtectionKeychain {
            query[kSecUseDataProtectionKeychain as String] = true
        }
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

    // MARK: - E029: Device-specific key wrapping

    /// Prefix for wrapped keys stored in Keychain, distinguishing from legacy plain base64 keys.
    /// Keys stored before E029 lack this prefix and are read as-is for backward compatibility.
    private static let wrappedKeyPrefix = "wrapped:v1:"

    /// E029: Derives a 256-bit AES wrapping key from this device's hardware UUID using HKDF-SHA256.
    /// The wrapping key is unique to this machine — a Keychain item copied to another device
    /// cannot be unwrapped because the hardware UUID differs. Returns nil when the hardware UUID
    /// is unavailable (non-macOS platform or IOKit failure).
    private static func deriveWrappingKey() -> SymmetricKey? {
        #if os(macOS)
        let platformExpert = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("IOPlatformExpertDevice"))
        guard platformExpert > 0 else { return nil }
        defer { IOObjectRelease(platformExpert) }
        guard let uuid = IORegistryEntryCreateCFProperty(platformExpert, kIOPlatformUUIDKey as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? String,
              !uuid.isEmpty else { return nil }
        let ikm = SymmetricKey(data: Data(uuid.utf8))
        let salt = Data("com.milkandgin.syxx.witness-key-wrap.v1".utf8)
        return HKDF<SHA256>.deriveKey(inputKeyMaterial: ikm, salt: salt, outputByteCount: 32)
        #else
        return nil
        #endif
    }

    /// E029: Wraps the signing key plaintext with AES-GCM using the device-specific wrapping key.
    /// Returns the combined sealed box (nonce + ciphertext + tag), or nil if wrapping is unavailable.
    private static func wrapKey(_ plaintext: Data) -> Data? {
        guard let wrappingKey = deriveWrappingKey() else { return nil }
        guard let sealed = try? AES.GCM.seal(plaintext, using: wrappingKey) else { return nil }
        return sealed.combined
    }

    /// E029: Unwraps a previously wrapped signing key using the device-specific AES-GCM key.
    /// Returns the original plaintext, or nil if unwrapping fails (wrong device, corrupted data).
    private static func unwrapKey(_ wrapped: Data) -> Data? {
        guard let wrappingKey = deriveWrappingKey() else { return nil }
        guard let sealedBox = try? AES.GCM.SealedBox(combined: wrapped) else { return nil }
        return try? AES.GCM.open(sealedBox, using: wrappingKey)
    }

    // MARK: - Keychain plumbing

    private static func keychainQuery(account: String) -> [String: Any] {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        ]
        if useDataProtectionKeychain {
            query[kSecUseDataProtectionKeychain as String] = true
        }
        return query
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

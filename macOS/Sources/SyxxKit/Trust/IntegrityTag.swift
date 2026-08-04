import Foundation
import CryptoKit
import Security

/// D030: HMAC-SHA256 integrity tag for baseline/sentinel snapshot files.
/// The signing key is stored in the macOS Keychain with `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`,
/// ensuring it never leaves the device and is not accessible to other processes without Keychain access.
/// A local attacker who can write the snapshot file cannot forge the HMAC because they cannot access
/// the Keychain-stored key (unless they already have Keychain access, which is a higher privilege).
///
/// The HMAC is written as a sibling `.hmac` file alongside the snapshot, so the snapshot format
/// remains unchanged and backward-compatible. On load, the HMAC is verified; if missing or mismatched,
/// the snapshot is rejected as untrusted (returns nil rather than silently trusting unsigned data).
public enum CacheIntegrityVerification: Sendable, Equatable {
    case valid
    case invalid
    case unavailable
}

enum IntegrityTag {
    private static let keychainAccount = "com.syxx.integrity-tag"
    private static let hmacFileExtension = "hmac"

    /// Compute HMAC-SHA256 of `data` using the Keychain-stored key. Returns nil if the key
    /// cannot be loaded. Use this for in-memory integrity checks (e.g. UserDefaults caching).
    static func sign(_ data: Data) -> Data? {
        guard let key = loadOrCreateKey() else { return nil }
        let hmac = HMAC<SHA256>.authenticationCode(for: data, using: key)
        return Data(hmac)
    }

    /// Verify the HMAC of `data` against a previously-computed HMAC. Returns true only if the
    /// HMAC matches. Use this for in-memory integrity checks (e.g. UserDefaults caching).
    static func verify(_ data: Data, hmac: Data) -> Bool {
        verifyStatus(data, hmac: hmac) == .valid
    }

    static func verifyStatus(_ data: Data, hmac: Data) -> CacheIntegrityVerification {
        guard let key = loadOrCreateKey() else { return .unavailable }
        let computed = HMAC<SHA256>.authenticationCode(for: data, using: key)
        return Data(computed) == hmac ? .valid : .invalid
    }

    /// Compute HMAC-SHA256 of `data` using the Keychain-stored key and write it to `<url>.hmac`.
    /// Fails silently if the Keychain key cannot be read or written (caller should log/warn).
    static func signAndWrite(_ data: Data, for url: URL) {
        guard let hmacData = sign(data) else { return }
        let hmacHex = hmacData.map { String(format: "%02x", $0) }.joined()
        let hmacURL = url.appendingPathExtension(Self.hmacFileExtension)
        try? hmacHex.write(to: hmacURL, atomically: true, encoding: .utf8)
        // Owner-only: the HMAC file is a security artifact.
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: hmacURL.path)
    }

    /// Verify the HMAC of `data` against the sibling `.hmac` file. Returns true only if the HMAC
    /// file exists, is readable, and matches the computed HMAC. Returns false if the HMAC file is
    /// missing, unreadable, or mismatched — the caller must treat this as untrusted data.
    static func verify(_ data: Data, for url: URL) -> Bool {
        guard let key = loadOrCreateKey() else { return false }
        let hmacURL = url.appendingPathExtension(Self.hmacFileExtension)
        guard let storedHex = try? String(contentsOf: hmacURL, encoding: .utf8) else { return false }
        let hmac = HMAC<SHA256>.authenticationCode(for: data, using: key)
        let computedHex = hmac.map { String(format: "%02x", $0) }.joined()
        // Constant-time comparison to prevent timing attacks (defense-in-depth; the key is not
        // attacker-controlled, but the pattern is cheap and correct).
        return constantTimeEqual(computedHex, storedHex)
    }

    /// Delete the HMAC sibling file when the snapshot is forgotten.
    static func deleteHMAC(for url: URL) {
        let hmacURL = url.appendingPathExtension(Self.hmacFileExtension)
        try? FileManager.default.removeItem(at: hmacURL)
    }

    // MARK: - Keychain

    private static func loadOrCreateKey() -> SymmetricKey? {
        if let existing = loadKey() { return existing }
        return createKey()
    }

    private static func loadKey() -> SymmetricKey? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "SyxxIntegrityTag",
            kSecAttrAccount as String: keychainAccount,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data, data.count == 32 else {
            return nil
        }
        return SymmetricKey(data: data)
    }

    private static func createKey() -> SymmetricKey? {
        let key = SymmetricKey(size: .bits256)
        let data: Data = key.withUnsafeBytes { Data($0) }
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "SyxxIntegrityTag",
            kSecAttrAccount as String: keychainAccount,
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
        ]
        // Delete any stale item first (SecItemAdd fails if the item already exists).
        SecItemDelete(query as CFDictionary)
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else { return nil }
        return key
    }

    // MARK: - Constant-time comparison

    private static func constantTimeEqual(_ a: String, _ b: String) -> Bool {
        let aBytes = Array(a.utf8)
        let bBytes = Array(b.utf8)
        guard aBytes.count == bBytes.count else { return false }
        var result: UInt8 = 0
        for i in 0..<aBytes.count {
            result |= aBytes[i] ^ bBytes[i]
        }
        return result == 0
    }
}

public enum CacheIntegrityTag {
    public static func sign(_ data: Data) -> Data? {
        IntegrityTag.sign(data)
    }

    public static func verify(_ data: Data, hmac: Data) -> CacheIntegrityVerification {
        IntegrityTag.verifyStatus(data, hmac: hmac)
    }
}

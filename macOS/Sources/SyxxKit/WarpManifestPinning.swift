import Foundation
import CryptoKit

/// WARP_MANIFEST_V1 §7 — out-of-band pinned trust set for the signed content-integrity manifest.
///
/// Mirrors `WitnessPinning`: the trusted signing key(s) are loaded OUT OF BAND (env var + a
/// persisted config/key-file), NEVER from `/api/pubkey` at runtime. `/api/pubkey` is fetched over
/// the same channel an attacker may control, so it can never be the trust root — a valid signature
/// by a non-pinned key is `untrustedSigner` (→ INCONCLUSIVE), never a match (the D011 rule already
/// enforced for Witness/Glutton: valid-but-untrusted must not render green).
///
/// A pinned entry binds a `key_id` (lowercase hex of the FULL 32-byte raw Ed25519 public key — see
/// `deriveKeyId`; this matches the corpus and the server) to the raw Ed25519 public-key bytes the
/// client verifies against.
///
/// OWNER DELIVERABLE: distribute/bake the pinned manifest key(s) into clients exactly as Witness
/// keys are. Nothing is hardcoded here.
public enum WarpManifestPinning {
    /// UserDefaults key for GUI-persisted trusted manifest keys (shared with the app surface).
    private static let userDefaultsKey = "warp_manifest_trusted_keys"
    /// Environment variable for trusted manifest keys (CLI / deployment).
    private static let envKey = "SYXX_WARP_MANIFEST_KEYS"

    /// Returns the pinned trust set as `key_id -> raw Ed25519 public-key bytes`.
    ///
    /// Sources, in order: the `warp_manifest_trusted_keys` UserDefaults value (shared with the GUI),
    /// else the `SYXX_WARP_MANIFEST_KEYS` environment variable, else a persisted key file at
    /// `~/.syxx/warp_manifest_trusted_keys.txt`. An EMPTY map means no trust root is configured —
    /// every signer is then `untrustedSigner` (INCONCLUSIVE), never trusted.
    public static func configuredTrustedManifestKeys() -> [String: Data] {
        var raw = UserDefaults.standard.string(forKey: userDefaultsKey) ?? ""
        if raw.isEmpty { raw = ProcessInfo.processInfo.environment[envKey] ?? "" }
        if raw.isEmpty { raw = persistedTrustedManifestKeys() ?? "" }
        return parse(raw)
    }

    /// Returns the persisted trusted-key file contents, or nil if none exists.
    public static func persistedTrustedManifestKeys() -> String? {
        let home = FileManager.default.homeDirectoryForCurrentUser
        let keyFile = home.appendingPathComponent(".syxx/warp_manifest_trusted_keys.txt")
        return try? String(contentsOf: keyFile, encoding: .utf8)
    }

    /// Pure parser (testable, no I/O). Accepts, comma/space/newline/tab separated:
    ///   - a 64-char lowercase-hex `key_id` (the raw 32-byte Ed25519 public key, hex-encoded) — this
    ///     is the canonical corpus/production form; the public key IS `hexdecode(key_id)`.
    ///   - `keyid=base64pubkey` / `keyid:base64pubkey` — an explicit pin (key_id verbatim, base64 key).
    ///   - `base64pubkey` — a bare raw public key; its key_id is DERIVED as the lowercase hex of the
    ///     raw public key, matching the server's `key_id` scheme.
    /// Entries whose public-key component is not a valid 32-byte Ed25519 key are dropped (fail-closed:
    /// an unusable pin is not a trust anchor).
    public static func parse(_ raw: String) -> [String: Data] {
        var out: [String: Data] = [:]
        let parts = raw.split(whereSeparator: { $0 == "," || $0 == " " || $0 == "\n" || $0 == "\t" || $0 == "\r" })
        for part in parts {
            let token = String(part)
            if token.isEmpty { continue }

            // Explicit `keyid=base64` / `keyid:base64` pin. IMPORTANT: base64 of a 32-byte key
            // always ends in exactly one '=' pad, so a bare-base64 token also CONTAINS '='. Only
            // treat the separator as an explicit pin when the remainder is non-empty AND parses as
            // a valid key; otherwise fall through to the bare forms. (The old first-'=' split
            // consumed the pad, parsed an empty key, and silently emptied every documented
            // bare-base64 trust set → permanent INCONCLUSIVE.)
            if let sep = token.firstIndex(where: { $0 == "=" || $0 == ":" }),
               sep > token.startIndex, token.index(after: sep) < token.endIndex {
                let keyId = String(token[..<sep])
                let pubToken = String(token[token.index(after: sep)...])
                if let pubData = validPubKey(base64: pubToken) {
                    out[keyId] = pubData
                    continue
                }
            }

            // Bare 64-char hex key_id (the public key hex-encoded).
            if let pubData = validPubKey(hex: token) {
                out[token.lowercased()] = pubData
                continue
            }

            // Bare base64 public key → derive key_id from the raw bytes.
            if let pubData = validPubKey(base64: token) {
                out[deriveKeyId(rawPublicKey: pubData)] = pubData
            }
        }
        return out
    }

    /// The server's `key_id` derivation: lowercase hex of the raw 32-byte Ed25519 public key.
    public static func deriveKeyId(rawPublicKey: Data) -> String {
        rawPublicKey.map { String(format: "%02x", $0) }.joined()
    }

    private static func validPubKey(base64: String) -> Data? {
        guard let d = Data(base64Encoded: base64), d.count == 32,
              (try? Curve25519.Signing.PublicKey(rawRepresentation: d)) != nil else { return nil }
        return d
    }

    private static func validPubKey(hex: String) -> Data? {
        guard hex.count == 64 else { return nil }
        var bytes = [UInt8](); bytes.reserveCapacity(32)
        var idx = hex.startIndex
        while idx < hex.endIndex {
            let next = hex.index(idx, offsetBy: 2)
            guard let b = UInt8(hex[idx..<next], radix: 16) else { return nil }
            bytes.append(b); idx = next
        }
        let d = Data(bytes)
        guard (try? Curve25519.Signing.PublicKey(rawRepresentation: d)) != nil else { return nil }
        return d
    }
}

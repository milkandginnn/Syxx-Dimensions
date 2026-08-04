import Foundation
import CryptoKit

/// A signed evidence snapshot of DNS tampering.
///
/// The signed content is the canonical `evidence` (E011) — a cross-platform byte-stable DTO whose
/// Ed25519 signature verifies on either platform. `comparison`, `targetDomain`, `timestamp`, and
/// `expiresAt` are display mirrors of the signed evidence; `verifySnapshot` re-checks that the
/// cross-platform-stable projection of `comparison` (result, answer sets) and `targetDomain` still
/// agree with the signed `evidence`, so tampering a display field is rejected.
public struct WitnessSnapshot: Codable, Sendable {
    /// Signed payload schema version. Currently 2. (B026 — absorbed into E011.)
    public let formatVersion: Int
    public let timestamp: Date
    /// Optional absolute expiry mirrored from the signed payload. (D005 — absorbed into E011.)
    public let expiresAt: Date?
    public let targetDomain: String
    public let comparison: ComparisonResult
    /// The canonical, cross-platform byte-stable content the signature actually covers (E011).
    public let evidence: SignedEvidencePayload
    public let signature: String
    /// Base64-encoded Ed25519 public key — anyone can verify without holding the private key.
    public let publicKey: String
    /// Key identifier (SHA-256 hash of the public key) for rotation support.
    public let keyId: String

    public init(
        formatVersion: Int,
        timestamp: Date,
        expiresAt: Date?,
        targetDomain: String,
        comparison: ComparisonResult,
        evidence: SignedEvidencePayload,
        signature: String,
        publicKey: String,
        keyId: String
    ) {
        self.formatVersion = formatVersion
        self.timestamp = timestamp
        self.expiresAt = expiresAt
        self.targetDomain = targetDomain
        self.comparison = comparison
        self.evidence = evidence
        self.signature = signature
        self.publicKey = publicKey
        self.keyId = keyId
    }
}

/// Failures of witness signing and snapshot verification.
///
/// `LocalizedError` conformance is load-bearing, not cosmetic. Without it, Foundation bridges a
/// bare Swift error to "The operation couldn't be completed. (SyxxKit.WitnessSignerError error N.)",
/// which is what actually reached the owner's screen. That string is crash-reporter material: it
/// tells the user nothing, leaks internal type structure, and is filtered out entirely by
/// `SyxxApp/ErrorPresenter.isBoilerplate`, leaving the surface with a bare "could not" and no cause.
///
/// HONESTY RULE for these strings: each one states WHAT failed, established from the guard that
/// throws it. None of them asserts WHY (we cannot know whether a key is bad because it was typed as
/// a passphrase, truncated, or pasted from the wrong place) and none prescribes a remedy that
/// presumes a cause.
public enum WitnessSignerError: Error, LocalizedError {
    case emptyKey
    case invalidPrivateKey
    case encodingFailed
    case missingPublicKey
    case unsupportedVersion
    case expired

    public var errorDescription: String? {
        switch self {
        case .emptyKey:
            return "No signing key was supplied."
        case .invalidPrivateKey:
            return "The signing key is not valid Ed25519 key material. An Ed25519 private key is exactly 32 bytes, written as base64."
        case .encodingFailed:
            return "The evidence could not be encoded into the payload that gets signed."
        case .missingPublicKey:
            return "The snapshot carries no public key, so its signature cannot be checked."
        case .unsupportedVersion:
            return "The snapshot uses a payload format this build of Syxx does not support."
        case .expired:
            return "The snapshot is past the expiry time recorded inside its signed payload."
        }
    }
}

public enum WitnessSigner {

    /// E021: the distinct, non-collapsible outcomes of witness-snapshot verification. Everything
    /// except `.valid` fails verification, but the REASONS are forensically different (an expired
    /// honest snapshot is not a forged one) and surfaces must render them distinctly.
    /// Parity with Windows `WitnessSigner.SnapshotVerification`.
    public enum SnapshotVerification: Sendable {
        case valid
        /// Missing public key, undecodable base64, or a thrown parse failure.
        case malformedInput
        /// Snapshot format version this verifier does not understand.
        case unsupportedFormat
        /// The signed ExpiresAt has passed.
        case expired
        /// Older than the caller-supplied max age.
        case stale
        /// B085: signed timestamp is ahead of local now beyond skew tolerance.
        case futureDated
        /// D011: signature verifies but the signer is not in the caller's trust set.
        case untrustedSigner
        /// Display fields do not match the signed evidence (tampered presentation).
        case displayMismatch
        /// The Ed25519 signature check itself failed.
        case invalidSignature
    }

    /// Generates a new Ed25519 keypair.
    /// Returns (privateKeyBase64, publicKeyBase64).
    public static func generateKeyPair() -> (privateKey: String, publicKey: String) {
        let privateKey = Curve25519.Signing.PrivateKey()
        let publicKey = privateKey.publicKey
        return (
            privateKey: privateKey.rawRepresentation.base64EncodedString(),
            publicKey: publicKey.rawRepresentation.base64EncodedString()
        )
    }

    /// Decodes and validates a base64-encoded Ed25519 private key, returning the live key.
    ///
    /// This is the SINGLE definition of "is this usable signing key material", shared by the signing
    /// path and by `WitnessKeyManager.saveKey` so that anything accepted at save time is guaranteed
    /// to be accepted at sign time. Splitting those two checks is what let the literal string
    /// `lklklklk` (valid base64, 6 bytes) sit in the Keychain until a signing attempt months later
    /// failed with an opaque error.
    ///
    /// Fail-closed: `Data(base64Encoded:)` with default options rejects non-alphabet bytes, the
    /// length check rejects anything that is not exactly 32 raw bytes, and the final
    /// `Curve25519.Signing.PrivateKey` construction is CryptoKit's own acceptance test, so the
    /// validator cannot be more permissive than the signer.
    ///
    /// MEASURED CAVEAT (macOS 26.5, 2026-07-29): that decoder rejects LEADING and TRAILING
    /// whitespace but SKIPS whitespace BETWEEN base64 characters, so a key pasted with a wrapped
    /// line break still decodes. Left as-is deliberately: the validator and the signer share this one
    /// decoder, so their accept sets are identical and no key can be saved that cannot later sign.
    /// Tightening it would newly reject a real, working 32-byte key someone pasted with a line break
    /// in it, and the ambiguity is a canonicalization nicety rather than a security hole (an attacker
    /// who can write the Keychain item already holds the key). Pinned by
    /// `WitnessKeyValidationTests.testValidatePrivateKey_embeddedWhitespaceIsSkippedByFoundation`.
    static func validatedPrivateKey(fromBase64 privateKeyBase64: String) throws -> Curve25519.Signing.PrivateKey {
        guard !privateKeyBase64.isEmpty else {
            throw WitnessSignerError.emptyKey
        }
        guard let keyData = Data(base64Encoded: privateKeyBase64), keyData.count == 32 else {
            throw WitnessSignerError.invalidPrivateKey
        }
        do {
            return try Curve25519.Signing.PrivateKey(rawRepresentation: keyData)
        } catch {
            // CryptoKit rejected 32 bytes we accepted. Report it as invalid key material rather than
            // letting a CryptoKitError escape with its own bridge string.
            throw WitnessSignerError.invalidPrivateKey
        }
    }

    /// Throws unless `privateKeyBase64` is usable Ed25519 signing key material. Same contract as
    /// `validatedPrivateKey(fromBase64:)`; use this when the key itself is not needed.
    public static func validatePrivateKey(base64 privateKeyBase64: String) throws {
        _ = try validatedPrivateKey(fromBase64: privateKeyBase64)
    }

    /// B183: default expiry for witness snapshots — 30 days from creation. A snapshot without
    /// an explicit expiry is indefinitely replayable as current evidence; a non-nil default
    /// ensures every signed snapshot has a finite lifetime. Callers may override via `expiresAt`.
    public static let defaultExpiry: TimeInterval = 30 * 24 * 60 * 60  // 30 days

    /// Creates and signs a witness snapshot of DNS data. `privateKeyBase64` must be a
    /// base64-encoded Ed25519 private key (32 raw bytes). Callers must generate a keypair
    /// with `generateKeyPair()` and store the private key securely — never use a default key.
    public static func createSignedSnapshot(domain: String, comparison: ComparisonResult, privateKeyBase64: String, expiresAt: Date? = nil) throws -> WitnessSnapshot {
        let privateKey = try validatedPrivateKey(fromBase64: privateKeyBase64)
        let publicKey = privateKey.publicKey
        let pubKeyBase64 = publicKey.rawRepresentation.base64EncodedString()
        let keyId = SHA256.hash(data: publicKey.rawRepresentation).prefix(8).map { String(format: "%02x", $0) }.joined()

        let timestamp = Date()
        // D102-follow-up: truncate (NOT round) to match Windows' `ToUnixTimeMilliseconds()`
        // convention, which truncates sub-millisecond fractions rather than rounding them.
        let timestampMillis = UInt64(timestamp.timeIntervalSince1970 * 1000)
        // B183: when the caller does not provide an explicit expiry, apply the default so the
        // snapshot is not indefinitely replayable. The default matches the verifier's defaultMaxAge
        // (D005) so a snapshot with no explicit expiry and a verification with no maxAge agree.
        let effectiveExpiry = expiresAt ?? timestamp.addingTimeInterval(defaultExpiry)
        let expiresAtMillis: UInt64? = UInt64(effectiveExpiry.timeIntervalSince1970 * 1000)
        let evidence = SignedEvidencePayload.build(
            domain: domain,
            comparison: comparison,
            timestampMillis: timestampMillis,
            expiresAtMillis: expiresAtMillis,
            formatVersion: SignedEvidencePayload.currentFormatVersion)
        let payload = try evidence.canonicalBytes()
        let signature = try privateKey.signature(for: payload)
        let signatureBase64 = signature.base64EncodedString()

        return WitnessSnapshot(
            formatVersion: SignedEvidencePayload.currentFormatVersion,
            timestamp: timestamp,
            expiresAt: effectiveExpiry,
            targetDomain: domain,
            comparison: comparison,
            evidence: evidence,
            signature: signatureBase64,
            publicKey: pubKeyBase64,
            keyId: keyId
        )
    }

    /// Verifies a witness snapshot using the Ed25519 public key embedded in the snapshot.
    /// No secret key is needed — any third party can verify with the public key alone.
    /// Returns the forensic tri-state `SnapshotVerification` (never a bare Bool) so callers
    /// can distinguish expired honest snapshots from forged ones.
    /// - Parameters:
    ///   - maxAge: if set, snapshots older than this (seconds) fail verification.
    ///   - trustedPublicKeys: if set and non-empty, the snapshot's public key must be a member.
    ///
    /// D050: removed the old Bool+throws mapping so the public API always returns a tri-state
    /// enum instead of collapsing distinct forensic reasons into true/false/throw.
    /// D089: this used to be a second, hand-written copy of the entire check sequence that had
    /// drifted from `verifySnapshotDetailed` — notably it never received the B085 future-dated /
    /// clock-skew guard, so `syxx witness verify <file>` (this overload) could render a
    /// future-dated/clock-forged snapshot as valid while `--detailed` on the SAME file correctly
    /// rejected it. Now a thin mapping over `verifySnapshotDetailed` so the two entry points can
    /// never disagree again.
    public static func verifySnapshot(_ snapshot: WitnessSnapshot, maxAge: TimeInterval? = nil, trustedPublicKeys: [String]? = nil) -> SnapshotVerification {
        verifySnapshotDetailed(snapshot, maxAge: maxAge, trustedPublicKeys: trustedPublicKeys)
    }

    /// D005: default maximum age for snapshots without an explicit expiresAt.
    /// 30 days balances evidence freshness against practical retention needs.
    /// A stolen key can forge snapshots with arbitrary past timestamps, but only
    /// within this window (unless the caller supplies a stricter maxAge).
    public static let defaultMaxAge: TimeInterval = 30 * 24 * 60 * 60  // 30 days

    /// E021: detailed verification returning the forensic tri-state. Parity with Windows
    /// `WitnessSigner.VerifySnapshotDetailed`. Surfaces MUST use this instead of the Bool
    /// overload so that expired/untrusted/forged are rendered distinctly.
    ///
    /// D005: when neither `expiresAt` (in the snapshot) nor `maxAge` (caller-supplied) is set,
    /// applies `defaultMaxAge` (30 days) so snapshots without explicit expiry are not valid forever.
    public static func verifySnapshotDetailed(
        _ snapshot: WitnessSnapshot,
        maxAge: TimeInterval? = nil,
        trustedPublicKeys: [String]? = nil,
        now: Date = Date()
    ) -> SnapshotVerification {
        // B211: auto-load witness trusted keys when the caller does not supply them, so the trust
        // check engages with zero call-site wiring. Empty set continues to mean "untracked".
        let effectiveTrustedKeys: [String]? = trustedPublicKeys ?? {
            let auto = WitnessPinning.configuredTrustedWitnessKeys()
            return auto.isEmpty ? nil : Array(auto)
        }()
        // B214/E020: accept v2 (with errorMessage), v3 (errorMessage-free), and v4 (B252:
        // errorMessage-free + canonical dohOutcomes + perResolverAnswers). The migration
        // requires both platforms' verifiers to accept a version before either signer emits it.
        let supported = snapshot.formatVersion == 2
            || snapshot.formatVersion == SignedEvidencePayload.errorMessageFreeFormatVersion
            || snapshot.formatVersion == SignedEvidencePayload.currentFormatVersion
        guard supported else {
            return .unsupportedFormat
        }

        // B184: cryptographic verification MUST precede time-based and trust checks.
        // A wholly fabricated snapshot with a past expiresAt previously returned .expired —
        // asserting the file is an authentic-but-lapsed document. No conclusion before evidence:
        // the signature must be valid before any claim about the snapshot's age or trust anchor.

        // 1. Parse public key and signature material.
        guard let pubKeyData = Data(base64Encoded: snapshot.publicKey), !pubKeyData.isEmpty else {
            return .malformedInput
        }
        guard let publicKey = try? Curve25519.Signing.PublicKey(rawRepresentation: pubKeyData),
              let sigData = Data(base64Encoded: snapshot.signature) else {
            return .malformedInput
        }
        guard let payload = try? snapshot.evidence.canonicalBytes() else {
            return .malformedInput
        }

        // 2. Verify the Ed25519 signature — the load-bearing check.
        guard publicKey.isValidSignature(sigData, for: payload) else {
            return .invalidSignature
        }

        // 3. B099: derive the key ID from the verified public key and reject any serialized
        // mismatch. An attacker cannot relabel a valid snapshot with a different keyId without
        // this catching it.
        let recomputedKeyId = SHA256.hash(data: pubKeyData).prefix(8).map { String(format: "%02x", $0) }.joined()
        if snapshot.keyId != recomputedKeyId {
            return .displayMismatch
        }

        // 4. Display-integrity guard (B037 + D083).
        // B214: rebuild with the snapshot's own formatVersion so v3 snapshots rebuild without
        // errorMessage. For v2 snapshots, errorMessage is platform-divergent free text and must
        // be excluded from the outcomes comparison.
        let rebuilt = SignedEvidencePayload.build(
            domain: snapshot.targetDomain,
            comparison: snapshot.comparison,
            timestampMillis: snapshot.evidence.timestamp,
            expiresAtMillis: snapshot.evidence.expiresAt,
            formatVersion: snapshot.formatVersion)
        // D083: also validate the DISPLAY timestamp, expiresAt and formatVersion.
        // D102-follow-up: truncate (not round) — must use the SAME conversion as the sign-side
        // (createSignedSnapshot, above) or an honest snapshot with a sub-millisecond-fraction
        // timestamp fails this display-integrity guard as a false displayMismatch.
        let displayTimestampMillis2 = UInt64(snapshot.timestamp.timeIntervalSince1970 * 1000)
        let displayExpiresAtMillis2 = snapshot.expiresAt.map { UInt64($0.timeIntervalSince1970 * 1000) }
        // B214: for v2 snapshots, compare outcomes without errorMessage (platform-divergent).
        // At v3+, errorMessage is nil on both sides so the full equality check is correct.
        let outcomesMatch: Bool
        if snapshot.formatVersion >= SignedEvidencePayload.errorMessageFreeFormatVersion {
            outcomesMatch = rebuilt.resolverQueryOutcomes == snapshot.evidence.resolverQueryOutcomes
        } else {
            outcomesMatch = rebuilt.resolverQueryOutcomes.strippingErrorMessage
                == snapshot.evidence.resolverQueryOutcomes.strippingErrorMessage
        }
        guard rebuilt.queryId == snapshot.evidence.queryId,
              rebuilt.comparisonResult == snapshot.evidence.comparisonResult,
              outcomesMatch,
              // B098/B252: per-resolver answer records bind answer ownership — a display-only
              // rewrite of who answered what must not verify as authentic (compared at every version).
              rebuilt.perResolverAnswers == snapshot.evidence.perResolverAnswers,
              // B252: canonical DoH evidence is compared only at v4+: pre-v4 evidence never
              // carried dohOutcomes, so comparing here would reject honest legacy snapshots.
              (snapshot.formatVersion < SignedEvidencePayload.currentFormatVersion
               || rebuilt.dohOutcomes == snapshot.evidence.dohOutcomes),
              displayTimestampMillis2 == snapshot.evidence.timestamp,
              displayExpiresAtMillis2 == snapshot.evidence.expiresAt,
              snapshot.formatVersion == snapshot.evidence.formatVersion else {
            return .displayMismatch
        }

        // 5. Trust-set check (D011 + B211) — signature is valid but signer may not be trusted.
        if let trusted = effectiveTrustedKeys, !trusted.isEmpty {
            if !trusted.contains(snapshot.publicKey) {
                return .untrustedSigner
            }
        }

        // 6. Time-based checks — only meaningful on a cryptographically verified snapshot.
        // D102-follow-up: truncate, matching the sign-side conversion above (and Windows'
        // `ToUnixTimeMilliseconds()`), so `nowMillis` uses the same convention as `evidence.timestamp`.
        let nowMillis = UInt64(now.timeIntervalSince1970 * 1000)
        if let expiresAtMillis = snapshot.evidence.expiresAt, nowMillis > expiresAtMillis {
            return .expired
        }
        // D005: apply maxAge if caller supplied one, otherwise apply defaultMaxAge when the
        // snapshot has no explicit expiresAt. This prevents snapshots from being valid forever.
        let effectiveMaxAge = maxAge ?? (snapshot.evidence.expiresAt == nil ? defaultMaxAge : nil)
        if let effectiveMaxAge {
            let maxAgeMillis = UInt64(max(0, effectiveMaxAge) * 1000)
            if nowMillis > snapshot.evidence.timestamp && (nowMillis - snapshot.evidence.timestamp) > maxAgeMillis {
                return .stale
            }
        }
        // B085: future-dated — signed timestamp is ahead of local now beyond skew tolerance (300s).
        let skewToleranceMillis: UInt64 = 300_000
        if snapshot.evidence.timestamp > nowMillis && (snapshot.evidence.timestamp - nowMillis) > skewToleranceMillis {
            return .futureDated
        }

        return .valid
    }
}

// MARK: - B211: Witness trust-set auto-loading

/// Auto-loads trusted witness signing keys from environment and persisted storage, mirroring
/// the pattern `CollectorPinning.configuredTrustedSigningKeys()` uses for collector keys.
/// An empty set means no trust set is configured — the signer is untracked, not trusted.
public enum WitnessPinning {
    /// UserDefaults key for persisted witness trusted keys.
    private static let userDefaultsKey = "witness_trusted_keys"
    /// Environment variable for witness trusted keys (CLI / deployment).
    private static let envKey = "SYXX_WITNESS_TRUSTED_KEYS"

    /// Returns the set of trusted witness public keys (base64-encoded Ed25519 public keys).
    ///
    /// Sources, in order: the `witness_trusted_keys` UserDefaults key (shared with the GUI),
    /// else the `SYXX_WITNESS_TRUSTED_KEYS` environment variable (CLI / deployment).
    /// Comma-, space-, or newline-separated base64 public keys. An empty set means no trust
    /// check — the signer is untracked.
    ///
    /// OWNER DELIVERABLE: the actual witness public key(s) are a deployment input and are
    /// intentionally NOT hardcoded here.
    public static func configuredTrustedWitnessKeys() -> Set<String> {
        var raw = UserDefaults.standard.string(forKey: userDefaultsKey) ?? ""
        if raw.isEmpty { raw = ProcessInfo.processInfo.environment[envKey] ?? "" }

        let parts = raw.split(whereSeparator: { $0 == "," || $0 == " " || $0 == "\n" || $0 == "\t" })
        return Set(parts.map(String.init).filter { !$0.isEmpty })
    }
}

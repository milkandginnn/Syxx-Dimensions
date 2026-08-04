import XCTest
import CryptoKit
@testable import SyxxKit

// E021: `WitnessSigner.verifySnapshotDetailed` had ZERO tests (audit 2026-07-16) — despite its own
// docstring saying "Surfaces MUST use this instead of the Bool overload so that expired/untrusted/
// forged are rendered distinctly". The entire nine-case tri-state was unverified: a regression
// collapsing `.expired` or `.untrustedSigner` into `.valid` would let an expired or untrusted
// snapshot render as authentic evidence, and nothing would go red.
//
// That distinction is the whole point of the Witness: an EXPIRED HONEST record and a FORGED one
// must never look alike. Each state below is asserted DISTINCTLY, not merely as "not valid".
final class WitnessVerificationDetailedTests: XCTestCase {

    private var keyPair: (privateKey: String, publicKey: String) { WitnessSigner.generateKeyPair() }

    // Mirrors the helper the existing witness tests use — `ComparisonResult` needs the
    // SyxxKit. qualifier (it is ambiguous with another type in scope) and takes DNSRecords.
    private func makeComparison(domain: String = "example.com", redirected: Bool = false) -> SyxxKit.ComparisonResult {
        let record = DNSRecord(name: domain, type: 1, ttl: 60, rdata: "93.184.216.34")
        return SyxxKit.ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [record],
            publicResolverResults: ["Cloudflare": [record]],
            isRedirectionDetected: redirected
        )
    }

    private func signed(expiresAt: Date? = nil) throws -> (WitnessSnapshot, String) {
        let (priv, pub) = keyPair
        let snap = try WitnessSigner.createSignedSnapshot(
            domain: "example.com", comparison: makeComparison(),
            privateKeyBase64: priv, expiresAt: expiresAt)
        return (snap, pub)
    }

    // MARK: - The affirmative case (baseline: without it, "always invalid" would pass everything)

    func testGenuineFreshSnapshotIsValid() throws {
        let (snap, _) = try signed()
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snap), .valid)
    }

    // MARK: - Honest-but-unusable states — these must NOT read as forged

    func testExpiredSnapshotIsExpired_NotInvalidSignature() throws {
        // The signature is perfectly good; only the validity window has passed. Reporting this as
        // invalidSignature would accuse an honest record of forgery.
        let (snap, _) = try signed(expiresAt: Date(timeIntervalSinceNow: -60))
        let v = WitnessSigner.verifySnapshotDetailed(snap)

        XCTAssertEqual(v, .expired)
        XCTAssertNotEqual(v, .invalidSignature, "an expired honest record must not read as forged")
        XCTAssertNotEqual(v, .valid)
    }

    func testStaleSnapshotIsStale_NotExpired_NorForged() throws {
        // No expiry set, but older than the caller's freshness bar. Distinct from .expired: the
        // signer set no window; the CALLER decided it is too old.
        let (snap, _) = try signed()
        let v = WitnessSigner.verifySnapshotDetailed(
            snap, maxAge: 1, now: Date(timeIntervalSinceNow: 3600))

        XCTAssertEqual(v, .stale)
        XCTAssertNotEqual(v, .expired)
        XCTAssertNotEqual(v, .invalidSignature)
    }

    func testFutureDatedSnapshotIsFutureDated() throws {
        // B085: an honest signer cannot produce a record from the future — but it is a clock/forgery
        // suspicion, reported as its own state rather than mislabelled as a bad signature.
        let (snap, _) = try signed()
        let v = WitnessSigner.verifySnapshotDetailed(snap, now: Date(timeIntervalSinceNow: -3600))

        XCTAssertEqual(v, .futureDated)
        XCTAssertNotEqual(v, .valid)
    }

    func testSmallClockSkewIsToleratedAndStillValid() throws {
        // Mutation guard for the test above: the skew window must be a WINDOW, not "any future
        // timestamp is future-dated". 60s ahead is inside the 300s tolerance.
        let (snap, _) = try signed()
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snap, now: Date(timeIntervalSinceNow: -60)),
                       .valid, "a 60s clock skew is within the 300s tolerance and must stay valid")
    }

    // MARK: - Trust — D011

    func testSignerOutsideTheTrustSetIsUntrustedSigner_NotInvalidSignature() throws {
        // The crux: the signature verifies mathematically. The key is simply not one we trust.
        // Collapsing this into .invalidSignature would claim tampering we cannot evidence.
        let (snap, _) = try signed()
        let v = WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: ["some-other-key"])

        XCTAssertEqual(v, .untrustedSigner)
        XCTAssertNotEqual(v, .invalidSignature, "an untrusted signer is not a forger")
        XCTAssertNotEqual(v, .valid)
    }

    func testSignerInsideTheTrustSetIsValid() throws {
        let (snap, pub) = try signed()
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: [pub]), .valid)
    }

    func testNoTrustSetConfiguredDoesNotImplyUntrusted() throws {
        // An unconfigured trust set means the check was not performed — it must not silently
        // reject every signer.
        let (snap, _) = try signed()
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: nil), .valid)
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: []), .valid)
    }

    // MARK: - Genuinely suspect states

    func testUnsupportedFormatIsItsOwnState() throws {
        let (snap, _) = try signed()
        let wrongVersion = WitnessSnapshot(
            formatVersion: 99, timestamp: snap.timestamp, expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain, comparison: snap.comparison,
            evidence: snap.evidence, signature: snap.signature,
            publicKey: snap.publicKey, keyId: snap.keyId)

        let v = WitnessSigner.verifySnapshotDetailed(wrongVersion)
        XCTAssertEqual(v, .unsupportedFormat)
        XCTAssertNotEqual(v, .valid, "an unreadable format must never verify")
        XCTAssertNotEqual(v, .invalidSignature, "we cannot claim forgery on a format we cannot parse")
    }

    func testMalformedPublicKeyIsMalformedInput_NotInvalidSignature() throws {
        let (snap, _) = try signed()
        let broken = WitnessSnapshot(
            formatVersion: snap.formatVersion, timestamp: snap.timestamp, expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain, comparison: snap.comparison,
            evidence: snap.evidence, signature: snap.signature,
            publicKey: "!!!not-base64!!!", keyId: snap.keyId)

        let v = WitnessSigner.verifySnapshotDetailed(broken)
        XCTAssertEqual(v, .malformedInput)
        XCTAssertNotEqual(v, .valid)
    }

    func testForgedSignatureIsInvalidSignature() throws {
        // The one case where "tampered" IS the honest answer: a real key, a real payload, and a
        // signature that does not match it.
        let (snap, _) = try signed()
        let otherKey = Curve25519.Signing.PrivateKey()
        let bogusSig = try otherKey.signature(for: Data("something else".utf8)).base64EncodedString()
        let forged = WitnessSnapshot(
            formatVersion: snap.formatVersion, timestamp: snap.timestamp, expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain, comparison: snap.comparison,
            evidence: snap.evidence, signature: bogusSig,
            publicKey: snap.publicKey, keyId: snap.keyId)

        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(forged), .invalidSignature)
    }

    func testDisplayMismatchIsDetected_TamperedPresentation() throws {
        // B037: the DISPLAYED comparison no longer matches the SIGNED evidence. The signature is
        // still valid over the original payload, so only the display-integrity guard catches this.
        let (snap, _) = try signed()
        let swapped = WitnessSnapshot(
            formatVersion: snap.formatVersion, timestamp: snap.timestamp, expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain,
            comparison: makeComparison(redirected: true),   // display now claims redirection
            evidence: snap.evidence,                        // ...but the signed evidence says otherwise
            signature: snap.signature, publicKey: snap.publicKey, keyId: snap.keyId)

        let v = WitnessSigner.verifySnapshotDetailed(swapped)
        XCTAssertEqual(v, .displayMismatch)
        XCTAssertNotEqual(v, .valid, "a presentation that contradicts the signed evidence must never verify")
    }

    // MARK: - D083: display timestamp / expiresAt / formatVersion must match signed fields

    func testD083_TamperedDisplayTimestamp_IsDisplayMismatch() throws {
        // D083: edit snapshot.timestamp (the unsecured display Date) to a different date.
        // Without the fix this still verifies as VALID — the guard used the SIGNED timestamp
        // to rebuild and never compared the display one. Mutation: remove the displayTimestamp
        // guard from WitnessSigner.verifySnapshotDetailed and this test must go RED.
        let (snap, _) = try signed()
        let fabricatedDate = Date(timeIntervalSinceNow: -86400 * 365)   // one year ago
        XCTAssertNotEqual(fabricatedDate.timeIntervalSince1970, snap.timestamp.timeIntervalSince1970,
                          "precondition: dates must differ for the mutation to be observable")
        let tampered = WitnessSnapshot(
            formatVersion: snap.formatVersion,
            timestamp: fabricatedDate,      // EDITED — different from the signed evidence.timestamp
            expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain, comparison: snap.comparison,
            evidence: snap.evidence, signature: snap.signature,
            publicKey: snap.publicKey, keyId: snap.keyId)

        let v = WitnessSigner.verifySnapshotDetailed(tampered)
        XCTAssertEqual(v, .displayMismatch,
                       "editing the display timestamp must produce displayMismatch, not valid")
        XCTAssertNotEqual(v, .valid,
                          "a snapshot with an edited timestamp must NEVER verify as valid")
    }

    func testD083_TamperedDisplayFormatVersion_IsDisplayMismatch() throws {
        let (snap, _) = try signed()
        // The display formatVersion must match the signed evidence formatVersion.
        // We simulate an envelope altered to claim version 3 (errorMessageFreeFormatVersion).
        // Since all properties of WitnessSnapshot are public, we can just initialize a new one
        // with the tampered formatVersion directly. No JSON-patching is necessary.
        let tampered = WitnessSnapshot(
            formatVersion: SignedEvidencePayload.errorMessageFreeFormatVersion,
            timestamp: snap.timestamp,
            expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain,
            comparison: snap.comparison,
            evidence: snap.evidence,
            signature: snap.signature,
            publicKey: snap.publicKey,
            keyId: snap.keyId)

        let result = WitnessSigner.verifySnapshotDetailed(tampered)
        XCTAssertEqual(result, .displayMismatch, "editing the display formatVersion must produce displayMismatch")
        XCTAssertNotEqual(result, .valid)
    }

    // MARK: - The forensic distinction itself

    func testEveryFailureStateIsDistinct_ExpiredIsNotForged() throws {
        // The property the Witness exists for: these must not all collapse into one "bad" bucket.
        let (fresh, pub) = try signed()
        let (expiredSnap, _) = try signed(expiresAt: Date(timeIntervalSinceNow: -60))

        let states: [WitnessSigner.SnapshotVerification] = [
            WitnessSigner.verifySnapshotDetailed(fresh),
            WitnessSigner.verifySnapshotDetailed(expiredSnap),
            WitnessSigner.verifySnapshotDetailed(fresh, maxAge: 1, now: Date(timeIntervalSinceNow: 3600)),
            WitnessSigner.verifySnapshotDetailed(fresh, now: Date(timeIntervalSinceNow: -3600)),
            WitnessSigner.verifySnapshotDetailed(fresh, trustedPublicKeys: ["not-\(pub)"]),
        ]
        XCTAssertEqual(Set(states.map { "\($0)" }).count, states.count,
                       "valid/expired/stale/futureDated/untrustedSigner must be five DISTINCT states")
    }

    // MARK: - Combined states (Phase 0 GV-A/A′/B)
    //
    // Every case above exercises ONE failure in isolation, so a crypto-LAST ordering never shows. A
    // combined snapshot proves the invariant: a non-crypto verdict (`.expired`/`.untrustedSigner`)
    // must NEVER be returned ahead of the crypto/display failure it would mask. macOS is crypto-first
    // and passes these today; they go RED on a crypto-LAST Kit until Phase 1a lands.
    // See docs/PROPOSAL-witness-crypto-first-2026-08-03.md §4.

    private func forgingFirstSignatureByte(of snap: WitnessSnapshot) throws -> WitnessSnapshot {
        var sigBytes = try XCTUnwrap(Data(base64Encoded: snap.signature))
        sigBytes[0] ^= 0xFF
        return WitnessSnapshot(
            formatVersion: snap.formatVersion, timestamp: snap.timestamp, expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain, comparison: snap.comparison, evidence: snap.evidence,
            signature: sigBytes.base64EncodedString(), publicKey: snap.publicKey, keyId: snap.keyId)
    }

    func testGVA_ForgedAndExpired_IsInvalidSignature_NotExpired() throws {
        // GV-A: forged signature AND a past ExpiresAt. Crypto-first => .invalidSignature.
        let (snap, _) = try signed(expiresAt: Date(timeIntervalSinceNow: -60))
        let v = WitnessSigner.verifySnapshotDetailed(try forgingFirstSignatureByte(of: snap))
        XCTAssertEqual(v, .invalidSignature)
        XCTAssertNotEqual(v, .expired, "a forged signature must not read as an honestly-expired record")
    }

    func testGVAPrime_ForgedAndUntrusted_IsInvalidSignature_NotUntrusted() throws {
        // GV-A′: forged signature signed by a key OUTSIDE the trust set. Crypto-first =>
        // .invalidSignature; a trust-before-crypto verifier returns .untrustedSigner and masks it.
        let (snap, _) = try signed()
        let v = WitnessSigner.verifySnapshotDetailed(
            try forgingFirstSignatureByte(of: snap), trustedPublicKeys: ["some-other-key"])
        XCTAssertEqual(v, .invalidSignature)
        XCTAssertNotEqual(v, .untrustedSigner, "a forged signature must not read as merely untrusted")
    }

    func testGVB_DisplayTamperedAndExpired_IsDisplayMismatch_NotExpired() throws {
        // GV-B: honest signature + past ExpiresAt + display timestamp edited 60s post-sign.
        // Display-integrity is checked before time => .displayMismatch, not the masking .expired.
        let (snap, _) = try signed(expiresAt: Date(timeIntervalSinceNow: -60))
        let tampered = WitnessSnapshot(
            formatVersion: snap.formatVersion, timestamp: snap.timestamp.addingTimeInterval(60),
            expiresAt: snap.expiresAt, targetDomain: snap.targetDomain, comparison: snap.comparison,
            evidence: snap.evidence, signature: snap.signature, publicKey: snap.publicKey, keyId: snap.keyId)
        let v = WitnessSigner.verifySnapshotDetailed(tampered)
        XCTAssertEqual(v, .displayMismatch)
        XCTAssertNotEqual(v, .expired, "an edited display field must not read as an honestly-expired record")
    }

    // [B099] A valid snapshot RELABELED with a different keyId must be rejected: verifySnapshotDetailed
    // recomputes the keyId from the verified public key (WitnessSigner.swift:293). This has always held
    // on macOS; the twin pins it and gives the Windows/Linux port (WitnessSigner.cs) a matching assertion.
    func testB099_RelabeledKeyId_IsDisplayMismatch() throws {
        let (snap, _) = try signed()
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snap), .valid)
        let flipped = (snap.keyId.first == "0" ? "1" : "0") + String(snap.keyId.dropFirst())
        XCTAssertNotEqual(flipped, snap.keyId, "precondition: the relabeled keyId must differ")
        let relabeled = WitnessSnapshot(
            formatVersion: snap.formatVersion, timestamp: snap.timestamp, expiresAt: snap.expiresAt,
            targetDomain: snap.targetDomain, comparison: snap.comparison, evidence: snap.evidence,
            signature: snap.signature, publicKey: snap.publicKey, keyId: flipped)
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(relabeled), .displayMismatch)
    }
}

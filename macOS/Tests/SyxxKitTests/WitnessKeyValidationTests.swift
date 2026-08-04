import XCTest
import CryptoKit
import Security
@testable import SyxxKit

/// Two defects, one root: an error that could not be read, and key material that was never checked.
///
/// (1) `WitnessSignerError` was a bare `Error`, so Foundation bridged it to
///     "The operation couldn't be completed. (SyxxKit.WitnessSignerError error 1.)" — which is what
///     the owner actually saw on screen. `SyxxApp/ErrorPresenter.isBoilerplate` correctly refuses to
///     show that string, so the surface was left stating a failure with NO cause at all.
///
/// (2) `WitnessKeyManager.saveKey` stored whatever it was handed. The literal string `lklklklk` is
///     valid base64 and decodes to 6 bytes; Ed25519 needs 32. It was persisted silently and the
///     failure only appeared at signing time, months later, as that unreadable error.
///
/// These tests are the floor under both fixes. They are NOT proof the product is honest — that came
/// from running the signing path against the actual 6-byte key and reading the message.
///
/// KEYCHAIN SAFETY: every `saveKey` case below is a REJECTION case, and rejection happens before any
/// Keychain WRITE, so on a correct build no test here ever stores anything. The "accepts a real key"
/// direction is asserted through the pure validator instead, deliberately. `setUp`/`tearDown` still
/// snapshot and restore the real Keychain item as a safety net for the case where the validation is
/// ever removed or bypassed: this suite must then go red, never destroy a developer's signing key.
final class WitnessKeyValidationTests: XCTestCase {

    /// The exact 6-byte string found in the owner's Keychain. Valid base64, wrong length, and the
    /// literal reproduction of the reported failure.
    private static let sixByteKey = "lklklklk"

    // MARK: - Keychain safety net

    // `WitnessKeyManager` is backed by the REAL Keychain item the shipped app uses. Every `saveKey`
    // case in this file is a rejection, so on a correct build nothing here ever reaches a Keychain
    // WRITE. This backup/restore exists for the case where that stops being true: if the save-time
    // validation is ever removed or bypassed, the rejection tests would start writing garbage into a
    // developer's real signing key. The test must go red in that situation, never destructive.
    //
    // The restore writes the original bytes back verbatim rather than via `saveKey`, because a key
    // persisted by a build from before validation existed may itself be invalid and would now be
    // rejected on the way back in.
    private var backedUpKey: String?

    override func setUpWithError() throws {
        try super.setUpWithError()
        backedUpKey = WitnessKeyManager.loadKey()
    }

    override func tearDownWithError() throws {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "com.milkandgin.syxx",
            kSecAttrAccount as String: "witness-key",
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        ]
        if let key = backedUpKey, let data = key.data(using: .utf8) {
            if SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess {
                SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
            } else {
                var addQuery = query
                addQuery[kSecValueData as String] = data
                SecItemAdd(addQuery as CFDictionary, nil)
            }
        } else {
            SecItemDelete(query as CFDictionary)
        }
        WitnessKeyManager.invalidateCacheForTesting()
        backedUpKey = nil
        try super.tearDownWithError()
    }

    // MARK: - Legibility helpers (mirrors SyxxApp/ErrorPresenter.isBoilerplate)

    /// True when `s` is crash-reporter material rather than something a person can read: the Cocoa
    /// "operation couldn't be completed" template, or a module-qualified type dump.
    private func isBoilerplate(_ s: String) -> Bool {
        if s.contains("operation couldn't be completed") || s.contains("operation couldn\u{2019}t be completed") {
            return true
        }
        return s.range(of: "[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z0-9_]+)+ error -?[0-9]+",
                       options: .regularExpression) != nil
    }

    private func assertLegible(_ error: Error, _ label: String, file: StaticString = #filePath, line: UInt = #line) {
        let localized = error.localizedDescription
        XCTAssertFalse(localized.isEmpty, "\(label): localizedDescription is empty", file: file, line: line)
        XCTAssertFalse(isBoilerplate(localized),
                       "\(label): localizedDescription is crash-reporter boilerplate, not something a user can read: \(localized)",
                       file: file, line: line)
    }

    // MARK: - Job 1: WitnessSignerError renders as readable text

    func testWitnessSignerError_everyCaseIsLegible() {
        let all: [(WitnessSignerError, String)] = [
            (.emptyKey, "emptyKey"),
            (.invalidPrivateKey, "invalidPrivateKey"),
            (.encodingFailed, "encodingFailed"),
            (.missingPublicKey, "missingPublicKey"),
            (.unsupportedVersion, "unsupportedVersion"),
            (.expired, "expired")
        ]
        // Guards the enum against silent growth: a case added without an errorDescription would
        // still be legible via the switch's exhaustiveness, but a case added without being listed
        // here would go untested. The count is the tripwire.
        XCTAssertEqual(all.count, 6, "a WitnessSignerError case was added or removed without updating this table")

        for (error, label) in all {
            // Cast, don't call directly: referencing `errorDescription` on the concrete type would
            // make removal of the conformance a COMPILE error, and a test that only fails to compile
            // has not demonstrated that it detects the defect behaviourally. Via the protocol
            // existential, dropping the conformance makes this go red at runtime instead.
            let described = (error as Error as? LocalizedError)?.errorDescription
            XCTAssertNotNil(described, "\(label): does not conform to LocalizedError with an errorDescription")
            XCTAssertFalse(described?.isEmpty ?? true, "\(label): empty errorDescription")
            assertLegible(error, label)
        }
    }

    /// The `.invalidPrivateKey` text must convey that the key is not valid Ed25519 key material.
    /// It must NOT assert a cause: we cannot know whether the key was typed as a passphrase,
    /// truncated, or pasted from the wrong place, and guessing would be a confident lie.
    func testInvalidPrivateKey_namesEd25519KeyMaterialAndAssertsNoCause() throws {
        // `as?`, not `as`: see the note in testWitnessSignerError_everyCaseIsLegible — a static
        // upcast would turn the mutation into a compile error rather than a behavioural failure.
        let text = try XCTUnwrap((WitnessSignerError.invalidPrivateKey as Error as? LocalizedError)?.errorDescription)
        XCTAssertTrue(text.contains("Ed25519"), "must name the key type that failed: \(text)")
        XCTAssertTrue(text.contains("32"), "must state the required size: \(text)")

        // No invented cause and no cause-dependent remedy.
        for forbidden in ["password", "passphrase", "because", "you typed", "you pasted"] {
            XCTAssertFalse(text.lowercased().contains(forbidden),
                           "must not assert an unestablished cause ('\(forbidden)'): \(text)")
        }
    }

    /// The end-to-end reproduction. Signing with the owner's actual 6-byte key must fail (the crypto
    /// guard is correct and must stay) AND the message the surface receives must be readable.
    func testCreateSignedSnapshot_withSixByteKey_failsWithLegibleMessage() {
        let record = DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: "93.184.216.34")
        let comparison = SyxxKit.ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [record],
            publicResolverResults: ["Cloudflare": [record]],
            isRedirectionDetected: false)

        XCTAssertThrowsError(
            try WitnessSigner.createSignedSnapshot(domain: "example.com",
                                                   comparison: comparison,
                                                   privateKeyBase64: Self.sixByteKey)
        ) { error in
            XCTAssertEqual(error as? WitnessSignerError, .invalidPrivateKey)
            assertLegible(error, "createSignedSnapshot(6-byte key)")
        }
    }

    // MARK: - Job 1 (audit): DNSResolverError reaches `syxx nslook` via localizedDescription

    func testDNSResolverError_everyCaseIsLegible() {
        let all: [(DNSResolverError, String)] = [
            (.invalidName("DNS name is empty"), "invalidName"),
            (.malformedResponse("Malformed DNS response"), "malformedResponse"),
            (.timeout, "timeout"),
            (.transport("DNS transport error"), "transport")
        ]
        XCTAssertEqual(all.count, 4, "a DNSResolverError case was added or removed without updating this table")

        for (error, label) in all {
            // `as?` for the same reason as the WitnessSignerError table above: the mutation must be
            // detectable at runtime, not merely fail to compile.
            XCTAssertNotNil((error as Error as? LocalizedError)?.errorDescription,
                            "\(label): does not conform to LocalizedError with an errorDescription")
            assertLegible(error, label)
        }
    }

    /// The associated value is the only thing we actually observed; it must be carried through
    /// verbatim rather than replaced by a generic sentence.
    func testDNSResolverError_carriesTheObservedDetailVerbatim() throws {
        let detail = "A-label 'xn--!!' failed punycode validation"
        let text = try XCTUnwrap((DNSResolverError.invalidName(detail) as Error as? LocalizedError)?.errorDescription)
        XCTAssertTrue(text.contains(detail), "the observed detail was dropped: \(text)")
    }

    // MARK: - Job 2: the validator itself

    func testValidatePrivateKey_acceptsAFreshlyGeneratedKeypair() throws {
        // Not over-strict: a validator that rejected everything would pass every rejection test
        // below while breaking the product outright.
        for _ in 0..<8 {
            let (priv, _) = WitnessSigner.generateKeyPair()
            XCTAssertNoThrow(try WitnessSigner.validatePrivateKey(base64: priv))
        }
    }

    func testValidatePrivateKey_rejectsTheSixByteKey() {
        XCTAssertThrowsError(try WitnessSigner.validatePrivateKey(base64: Self.sixByteKey)) { error in
            XCTAssertEqual(error as? WitnessSignerError, .invalidPrivateKey)
        }
    }

    func testValidatePrivateKey_rejectsEmpty() {
        XCTAssertThrowsError(try WitnessSigner.validatePrivateKey(base64: "")) { error in
            XCTAssertEqual(error as? WitnessSignerError, .emptyKey)
        }
    }

    /// Adversarial shapes. Off-by-one lengths are the interesting ones: 31 and 33 bytes are both
    /// well-formed base64 and both unusable as Ed25519 private keys.
    func testValidatePrivateKey_rejectsAdversarialShapes() {
        let thirtyOne = Data(repeating: 0x41, count: 31).base64EncodedString()
        let thirtyThree = Data(repeating: 0x41, count: 33).base64EncodedString()
        let valid32 = Data(repeating: 0x41, count: 32).base64EncodedString()

        let cases: [(String, String)] = [
            (thirtyOne, "31 bytes"),
            (thirtyThree, "33 bytes"),
            ("not base64 at all!", "non-base64 characters"),
            ("lklklkl", "base64 with invalid padding"),
            (String(valid32.dropLast()), "truncated base64"),
            ("  \(valid32)  ", "surrounding whitespace must not be silently trimmed"),
            ("\u{0000}", "NUL byte"),
            (String(repeating: "A", count: 100_000), "oversized input")
        ]
        for (input, label) in cases {
            XCTAssertThrowsError(try WitnessSigner.validatePrivateKey(base64: input),
                                 "accepted unusable key material (\(label))") { error in
                XCTAssertTrue(error is WitnessSignerError,
                              "\(label): leaked a non-WitnessSignerError: \(type(of: error))")
            }
        }

        // The control: exactly 32 bytes, cleanly encoded, is accepted.
        XCTAssertNoThrow(try WitnessSigner.validatePrivateKey(base64: valid32))
    }

    /// CHARACTERIZATION, NOT ENDORSEMENT. Measured on macOS 26.5 (2026-07-29):
    /// `Data(base64Encoded:)` with default options REJECTS leading and trailing whitespace but
    /// SKIPS whitespace that appears between base64 characters, so a key pasted with a wrapped line
    /// break still decodes to the same 32 bytes.
    ///
    /// This is deliberately left as-is rather than tightened. The validator and the signer share one
    /// decoder, so both accept exactly the same set and no key can be saved that cannot later sign,
    /// which is the invariant that actually matters. Tightening it would newly reject a real,
    /// working 32-byte key that a user happened to paste with a line break in it. The ambiguity is a
    /// canonicalization nicety, not a security hole: an attacker who can write the Keychain item
    /// already holds the key.
    ///
    /// The test exists so that a future change to Foundation's decoder is discovered here rather
    /// than in the field.
    func testValidatePrivateKey_embeddedWhitespaceIsSkippedByFoundation() throws {
        let valid32 = Data(repeating: 0x41, count: 32).base64EncodedString()
        let wrapped = valid32.replacingOccurrences(of: "A", with: "A\n")

        XCTAssertNoThrow(try WitnessSigner.validatePrivateKey(base64: wrapped),
                         "Foundation's decoder behaviour changed; revisit the strictness decision")

        // And the signer must agree, because they share the decoder. If these ever diverge, a key
        // could be accepted at save time and rejected at signing time, which is the exact
        // deferred-failure defect this whole change closes.
        let record = DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: "93.184.216.34")
        let comparison = SyxxKit.ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [record],
            publicResolverResults: ["Cloudflare": [record]],
            isRedirectionDetected: false)
        XCTAssertNoThrow(try WitnessSigner.createSignedSnapshot(
            domain: "example.com", comparison: comparison, privateKeyBase64: wrapped))
    }

    /// The invariant the shared validator exists to guarantee: save-time acceptance and sign-time
    /// acceptance cannot drift apart. Anything the validator accepts, the signer must also accept.
    func testValidatorAndSignerAgree() throws {
        let record = DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: "93.184.216.34")
        let comparison = SyxxKit.ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [record],
            publicResolverResults: ["Cloudflare": [record]],
            isRedirectionDetected: false)

        let candidates = [
            WitnessSigner.generateKeyPair().privateKey,
            Data(repeating: 0x00, count: 32).base64EncodedString(),
            Data(repeating: 0xFF, count: 32).base64EncodedString(),
            Self.sixByteKey,
            "",
            "not base64 at all!"
        ]
        for candidate in candidates {
            let validatorAccepts = (try? WitnessSigner.validatePrivateKey(base64: candidate)) != nil
            let signerAccepts = (try? WitnessSigner.createSignedSnapshot(
                domain: "example.com", comparison: comparison, privateKeyBase64: candidate)) != nil
            XCTAssertEqual(validatorAccepts, signerAccepts,
                           "validator and signer disagree on a key — this is the deferred-failure defect")
        }
    }

    // MARK: - Job 2: saveKey rejects at the point of entry

    /// Every case here is a rejection, so `saveKey` throws before it reaches the Keychain. If the
    /// validation is removed, these go red by NOT throwing (and only then would a write occur).
    func testSaveKey_rejectsUnusableKeyMaterialBeforeStoringIt() {
        XCTAssertThrowsError(try WitnessKeyManager.saveKey(Self.sixByteKey)) { error in
            XCTAssertEqual(error as? WitnessSignerError, .invalidPrivateKey,
                           "the 6-byte key must be rejected at save time, not deferred to signing time")
        }
        XCTAssertThrowsError(try WitnessKeyManager.saveKey("not base64 at all!")) { error in
            XCTAssertEqual(error as? WitnessSignerError, .invalidPrivateKey)
        }
        XCTAssertThrowsError(try WitnessKeyManager.saveKey(Data(repeating: 0x41, count: 31).base64EncodedString())) { error in
            XCTAssertEqual(error as? WitnessSignerError, .invalidPrivateKey)
        }
        XCTAssertThrowsError(try WitnessKeyManager.saveKey("")) { error in
            XCTAssertEqual(error as? WitnessSignerError, .emptyKey)
        }
    }
}

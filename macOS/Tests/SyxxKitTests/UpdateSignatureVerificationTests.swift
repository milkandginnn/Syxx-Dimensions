import XCTest
import Foundation
import CryptoKit
@testable import SyxxKit

/// The update channel is a supply-chain path: whoever can serve the appcast can serve code.
/// Until 2026-07-29 `UpdateService.verifySignature` decoded the signature, rebuilt the canonical
/// payload, discarded both and returned `true` with the comment "trust HTTPS transport for now".
/// Any well-formed manifest carrying ANY signature value, including 64 zero bytes, installed.
///
/// These tests exist to make that specific failure impossible to reintroduce quietly. The two
/// that matter most are `testReplayedSignatureFromAnotherManifestIsRefused` and
/// `testManifestAlteredAfterSigningIsRefused`: an implementation that checks "is there a
/// signature" rather than "does this signature cover this content" passes everything else and
/// fails those.
///
/// The vectors are produced by the SERVER'S canonicalizer
/// (`Server/syxx_server/appcast.py:_canon`, via `corpus/appcast-crossplatform/generate.py`), not
/// by this platform's, so a macOS/Windows/server three-way divergence has somewhere to surface.
/// Windows asserts against the same file in
/// `Windows/dotNET/Syxx.Tests/UpdateSignatureVerificationTests.cs`.
final class UpdateSignatureVerificationTests: XCTestCase {

    // MARK: - Fixture loading

    private struct Vectors: Decodable {
        struct CanonicalVector: Decodable {
            let name: String
            let why: String
            let raw: String
            let canonicalUtf8Base64: String
            let canonicalSha256: String
        }
        struct SignatureVector: Decodable {
            let name: String
            let why: String
            let raw: String
            let expect: String
        }
        let testPublicKeyBase64: String
        let decoyPublicKeyBase64: String
        let canonicalVectors: [CanonicalVector]
        let signatureVectors: [SignatureVector]
    }

    private static func fixtureURL() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
            .appendingPathComponent("corpus")
            .appendingPathComponent("appcast-crossplatform")
            .appendingPathComponent("appcast-vectors.json")
    }

    private func loadVectors() throws -> Vectors {
        let url = Self.fixtureURL()
        guard FileManager.default.fileExists(atPath: url.path) else {
            XCTFail("appcast golden vectors missing at \(url.path)")
            throw CocoaError(.fileNoSuchFile)
        }
        let data = try Data(contentsOf: url)
        guard !data.isEmpty else {
            XCTFail("appcast golden vectors are empty — refusing to pass vacuously")
            throw CocoaError(.fileReadCorruptFile)
        }
        let vectors = try JSONDecoder().decode(Vectors.self, from: data)
        // A fixture that quietly shrank to nothing would turn every test below green.
        XCTAssertGreaterThanOrEqual(vectors.canonicalVectors.count, 5, "canonical vectors were removed")
        XCTAssertGreaterThanOrEqual(vectors.signatureVectors.count, 15, "signature vectors were removed")
        return vectors
    }

    private func expected(_ token: String) -> AppcastVerification {
        switch token {
        case "verified": return .verified
        case "signatureMismatch": return .refusedSignatureMismatch
        case "signatureMalformed": return .refusedSignatureMalformed
        case "signatureMissing": return .refusedSignatureMissing
        case "malformedManifest": return .refusedMalformedManifest
        default:
            XCTFail("unknown expectation token '\(token)' in the fixture")
            return .refusedMalformedManifest
        }
    }

    // MARK: - Canonical bytes must match the server, byte for byte

    /// The signature covers the SERVER'S canonical bytes. If this platform computes different
    /// bytes it either rejects every honest update or, if the divergence is on the other side,
    /// verifies bytes it does not act on. Neither is survivable, and only a server-produced
    /// fixture can catch it — a test against our own canonicalizer would be green either way.
    func testCanonicalBytesMatchTheServerProducer() throws {
        let vectors = try loadVectors()
        for vector in vectors.canonicalVectors {
            let rawData = Data(vector.raw.utf8)
            guard var root = (try? JSONSerialization.jsonObject(with: rawData)) as? [String: Any] else {
                XCTFail("\(vector.name): fixture raw text is not a JSON object")
                continue
            }
            root.removeValue(forKey: "signature")
            let produced = try AppcastCanonicalJSON.canonicalBytes(of: root)
            let expectedBytes = Data(base64Encoded: vector.canonicalUtf8Base64)!
            XCTAssertEqual(
                produced, expectedBytes,
                """
                \(vector.name) (\(vector.why)):
                  server:  \(String(data: expectedBytes, encoding: .utf8) ?? "<non-utf8>")
                  swift:   \(String(data: produced, encoding: .utf8) ?? "<non-utf8>")
                """)
        }
    }

    /// Non-ASCII specifically: Python's `json.dumps` defaults to `ensure_ascii=True`, so the
    /// server signs `\uXXXX` escapes. `JSONSerialization` with `.sortedKeys` emits raw UTF-8 and
    /// would silently diverge here — which is why the canonicalizer is hand-rolled.
    func testNonASCIIReleaseNotesAreEscapedLikePython() throws {
        let vectors = try loadVectors()
        guard let vector = vectors.canonicalVectors.first(where: { $0.name == "non-ascii-release-notes" }) else {
            return XCTFail("non-ascii-release-notes vector missing from the fixture")
        }
        var root = (try! JSONSerialization.jsonObject(with: Data(vector.raw.utf8))) as! [String: Any]
        root.removeValue(forKey: "signature")
        let produced = String(data: try AppcastCanonicalJSON.canonicalBytes(of: root), encoding: .utf8)!
        XCTAssertTrue(produced.contains("\\u00e9"), "accented character was not escaped as Python does")
        XCTAssertTrue(produced.contains("\\ud83d\\udd12"), "astral character was not emitted as a surrogate pair")
        XCTAssertFalse(produced.contains("é"), "raw UTF-8 leaked into the canonical bytes")
    }

    /// Values with no repr the server, Swift and .NET are guaranteed to agree on get refused
    /// rather than guessed at.
    func testNonIntegerNumberHasNoCanonicalFormAndIsRefused() {
        XCTAssertThrowsError(try AppcastCanonicalJSON.canonicalBytes(of: ["publishedAt": 1.5]))
    }

    // MARK: - The six required cases

    /// Case 1: a valid signature over the exact canonical bytes is accepted.
    func testValidSignatureIsAccepted() throws {
        let vectors = try loadVectors()
        for vector in vectors.signatureVectors where vector.expect == "verified" {
            let result = AppcastVerifier.verify(
                rawManifest: Data(vector.raw.utf8),
                pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
            XCTAssertEqual(result, .verified, "\(vector.name): \(vector.why)")
        }
    }

    /// Case 2: a REAL signature, lifted from a different manifest, must be refused. This is the
    /// case a "is a signature present?" implementation passes and a real verifier fails.
    func testReplayedSignatureFromAnotherManifestIsRefused() throws {
        let vectors = try loadVectors()
        guard let vector = vectors.signatureVectors.first(where: {
            $0.name == "replayed-signature-from-other-manifest"
        }) else {
            return XCTFail("replay vector missing from the fixture")
        }
        let result = AppcastVerifier.verify(
            rawManifest: Data(vector.raw.utf8),
            pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
        XCTAssertEqual(result, .refusedSignatureMismatch, vector.why)
    }

    /// Case 6: one field mutated after signing. Same class of defect as case 2, from the other
    /// direction: the signature is authentic, the content is not what was signed.
    func testManifestAlteredAfterSigningIsRefused() throws {
        let vectors = try loadVectors()
        for name in ["version-altered-after-signing", "package-url-altered", "package-hash-altered"] {
            guard let vector = vectors.signatureVectors.first(where: { $0.name == name }) else {
                XCTFail("\(name) vector missing from the fixture")
                continue
            }
            let result = AppcastVerifier.verify(
                rawManifest: Data(vector.raw.utf8),
                pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
            XCTAssertEqual(result, .refusedSignatureMismatch, "\(name): \(vector.why)")
        }
    }

    /// Case 3: well-formed base64 garbage is refused.
    func testGarbageSignatureIsRefused() throws {
        let vectors = try loadVectors()
        for name in ["garbage-signature-correct-length", "garbage-signature-wrong-length", "signature-not-base64"] {
            guard let vector = vectors.signatureVectors.first(where: { $0.name == name }) else {
                XCTFail("\(name) vector missing from the fixture")
                continue
            }
            let result = AppcastVerifier.verify(
                rawManifest: Data(vector.raw.utf8),
                pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
            XCTAssertEqual(result, expected(vector.expect), "\(name): \(vector.why)")
            XCTAssertNotEqual(result, .verified, "\(name) must never verify")
        }
    }

    /// Case 4: a stripped, empty, or non-string signature is refused.
    func testStrippedOrEmptySignatureIsRefused() throws {
        let vectors = try loadVectors()
        for name in ["signature-empty", "signature-field-absent", "signature-not-a-string"] {
            guard let vector = vectors.signatureVectors.first(where: { $0.name == name }) else {
                XCTFail("\(name) vector missing from the fixture")
                continue
            }
            let result = AppcastVerifier.verify(
                rawManifest: Data(vector.raw.utf8),
                pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
            XCTAssertEqual(result, .refusedSignatureMissing, "\(name): \(vector.why)")
        }
    }

    /// Case 5: no pinned key means nothing can be proven, so the update is REFUSED — not
    /// allowed through, not warned about. A perfectly valid manifest is the right input here:
    /// the point is that validity is irrelevant when there is no trust anchor.
    func testNoPinnedKeyRefusesEvenAValidManifest() throws {
        let vectors = try loadVectors()
        guard let valid = vectors.signatureVectors.first(where: { $0.name == "valid" }) else {
            return XCTFail("valid vector missing from the fixture")
        }
        let raw = Data(valid.raw.utf8)

        // Sanity: this same manifest verifies when the key IS pinned, so a refusal below is
        // attributable to the missing pin and not to a broken vector.
        XCTAssertEqual(
            AppcastVerifier.verify(rawManifest: raw, pinnedPublicKeysBase64: [vectors.testPublicKeyBase64]),
            .verified)

        XCTAssertEqual(AppcastVerifier.verify(rawManifest: raw, pinnedPublicKeysBase64: []),
                       .refusedNoPinnedKey)
        XCTAssertEqual(AppcastVerifier.verify(rawManifest: raw, pinnedPublicKeysBase64: [""]),
                       .refusedNoPinnedKey)
        XCTAssertEqual(AppcastVerifier.verify(rawManifest: raw, pinnedPublicKeysBase64: ["not base64!!"]),
                       .refusedNoPinnedKey)
        // Right encoding, wrong length: 31 bytes.
        XCTAssertEqual(
            AppcastVerifier.verify(rawManifest: raw,
                                   pinnedPublicKeysBase64: [Data(repeating: 0x41, count: 31).base64EncodedString()]),
            .refusedNoPinnedKey)
        // One good pin and one corrupt pin: refuse the whole set rather than verifying against
        // the survivor, because the build's trust anchor is not what its author thought.
        XCTAssertEqual(
            AppcastVerifier.verify(rawManifest: raw,
                                   pinnedPublicKeysBase64: [vectors.testPublicKeyBase64, "garbage"]),
            .refusedNoPinnedKey)
    }

    /// The shipping build pins the dedicated update public key (DEV key until production
    /// rotation is owner-gated). Corpus vectors are signed by the fixture TEST key, which is
    /// deliberately different — so the shipping pin must REFUSE them (wrong key), never accept
    /// an unpinned signer. Missing/empty pin still refuses.
    func testShippingBuildPinsUpdateKeyAndRefusesUnpinnedSignatures() throws {
        let vectors = try loadVectors()
        guard let valid = vectors.signatureVectors.first(where: { $0.name == "valid" }) else {
            return XCTFail("valid vector missing from the fixture")
        }
        XCTAssertFalse(AppcastVerifier.pinnedPublicKeysBase64.isEmpty,
                       "Phase 3.2 requires a pinned update public key")
        // Corpus "valid" is signed by the fixture test key, not the shipping pin.
        XCTAssertEqual(AppcastVerifier.verify(rawManifest: Data(valid.raw.utf8)),
                       .refusedSignatureMismatch,
                       "shipping pin must not accept the fixture test key")
        // Explicit empty pin still refuses (no trust anchor).
        XCTAssertEqual(AppcastVerifier.verify(rawManifest: Data(valid.raw.utf8),
                                              pinnedPublicKeysBase64: []),
                       .refusedNoPinnedKey)
    }

    /// A manifest signed by the DEV update private key must verify against the shipping pin.
    func testDevUpdateKeyManifestVerifiesAgainstShippingPin() throws {
        // Server/dev-keys public key must match AppcastVerifier.pinnedPublicKeysBase64.
        let pin = try XCTUnwrap(AppcastVerifier.pinnedPublicKeysBase64.first)
        XCTAssertEqual(pin, "6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=")

        // Build a minimal unsigned manifest, sign with the DEV seed (same derivation as
        // Server/dev-keys), and verify with the shipping pin. Failure case: wrong key.
        let unsigned: [String: Any] = [
            "platform": "mac",
            "version": "9.9.9",
            "build": "dev",
            "minVersion": "1.0.0",
            "critical": false,
            "releaseNotes": "dev pin check",
            "packages": [[
                "url": "https://syxx.app/dist/Syxx-9.9.9.dmg",
                "sha256": String(repeating: "ab", count: 32),
                "size": 100,
                "type": "dmg",
            ]],
            "publishedAt": 1_753_747_200,
        ]
        let payload = try AppcastCanonicalJSON.canonicalBytes(of: unsigned)
        // DEV seed = SHA256("syxx-dev-only-update-signing-key-v1-NOT-PRODUCTION")
        var hasher = SHA256()
        hasher.update(data: Data("syxx-dev-only-update-signing-key-v1-NOT-PRODUCTION".utf8))
        let seed = Data(hasher.finalize())
        let priv = try Curve25519.Signing.PrivateKey(rawRepresentation: seed)
        let sig = try priv.signature(for: payload)
        var signed = unsigned
        signed["signature"] = Data(sig).base64EncodedString()
        let raw = try JSONSerialization.data(withJSONObject: signed)
        XCTAssertEqual(AppcastVerifier.verify(rawManifest: raw), .verified)

        // Mutation: alter version after signing → refuse.
        var tampered = signed
        tampered["version"] = "0.0.1"
        let tamperedRaw = try JSONSerialization.data(withJSONObject: tampered)
        XCTAssertEqual(AppcastVerifier.verify(rawManifest: tamperedRaw), .refusedSignatureMismatch)
    }

    // MARK: - Other refusals

    /// A valid Ed25519 signature from a key that simply is not ours. Pinning is the whole point.
    func testSignatureFromUnpinnedKeyIsRefused() throws {
        let vectors = try loadVectors()
        guard let vector = vectors.signatureVectors.first(where: { $0.name == "signed-by-unpinned-key" }) else {
            return XCTFail("unpinned-key vector missing from the fixture")
        }
        let raw = Data(vector.raw.utf8)
        XCTAssertEqual(
            AppcastVerifier.verify(rawManifest: raw, pinnedPublicKeysBase64: [vectors.testPublicKeyBase64]),
            .refusedSignatureMismatch, vector.why)
        // ...and verifies once that key IS the pinned one, proving the vector is well-formed
        // and the refusal above is about pinning rather than a malformed signature.
        XCTAssertEqual(
            AppcastVerifier.verify(rawManifest: raw, pinnedPublicKeysBase64: [vectors.decoyPublicKeyBase64]),
            .verified)
    }

    func testMalformedManifestBodiesAreRefused() throws {
        let vectors = try loadVectors()
        for name in ["not-json", "json-array-not-object", "float-valued-field"] {
            guard let vector = vectors.signatureVectors.first(where: { $0.name == name }) else {
                XCTFail("\(name) vector missing from the fixture")
                continue
            }
            XCTAssertEqual(
                AppcastVerifier.verify(rawManifest: Data(vector.raw.utf8),
                                       pinnedPublicKeysBase64: [vectors.testPublicKeyBase64]),
                .refusedMalformedManifest, "\(name): \(vector.why)")
        }
    }

    /// Every vector in the fixture, so a newly added vector is exercised without editing this
    /// file, and so no vector can be added that nothing asserts on.
    func testEverySignatureVectorMatchesItsExpectation() throws {
        let vectors = try loadVectors()
        for vector in vectors.signatureVectors {
            let result = AppcastVerifier.verify(
                rawManifest: Data(vector.raw.utf8),
                pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
            XCTAssertEqual(result, expected(vector.expect), "\(vector.name): \(vector.why)")
        }
    }

    /// The manifest handed to the rest of the app must come from the signed bytes.
    func testVerifiedManifestIsDecodedFromTheSignedBytes() throws {
        let vectors = try loadVectors()
        guard let valid = vectors.signatureVectors.first(where: { $0.name == "valid" }) else {
            return XCTFail("valid vector missing from the fixture")
        }
        let outcome = AppcastVerifier.verifiedManifest(
            rawManifest: Data(valid.raw.utf8),
            pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
        XCTAssertEqual(outcome.verification, .verified)
        XCTAssertEqual(outcome.manifest?.version, "2.1.0")
        XCTAssertEqual(outcome.manifest?.packages.first?.url, "https://syxx.app/dist/Syxx-2.1.0.dmg")

        // Refusals never hand back a manifest, so a caller cannot use one by mistake.
        for vector in vectors.signatureVectors where vector.expect != "verified" {
            let refused = AppcastVerifier.verifiedManifest(
                rawManifest: Data(vector.raw.utf8),
                pinnedPublicKeysBase64: [vectors.testPublicKeyBase64])
            XCTAssertNil(refused.manifest, "\(vector.name) was refused but still returned a manifest")
        }
    }

    // MARK: - The artifact, not just the manifest

    /// A signed manifest pointing at an unverifiable binary is the same hole one step later.
    func testPackageWithoutAFullSHA256IsRefused() {
        let good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f"
        let url = "https://syxx.app/dist/Syxx-2.1.0.dmg"

        XCTAssertNoThrow(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: url, sha256: good, size: 1, type: "dmg")))

        // Empty hash must refuse (historical server defect A264).
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: url, sha256: "", size: 1, type: "dmg")))
        // Truncated, and non-hex of the right length.
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: url, sha256: String(good.prefix(63)), size: 1, type: "dmg")))
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: url, sha256: String(repeating: "z", count: 64), size: 1, type: "dmg")))
    }

    /// Zero-byte packages are not installable — refuse before download.
    func testZeroSizePackageIsRefused() {
        let good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f"
        let url = "https://syxx.app/dist/Syxx-2.1.0.dmg"
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: url, sha256: good, size: 0, type: "dmg")))
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: url, sha256: good, size: -1, type: "dmg")))
    }

    /// Authenticated package type must match the URL extension; mismatch refuses install.
    func testPackageTypeExtensionMismatchIsRefused() {
        let good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f"
        // type=dmg but URL ends in .zip
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: "https://syxx.app/dist/Syxx.zip", sha256: good, size: 1, type: "dmg")))
        // unknown type
        XCTAssertThrowsError(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: "https://syxx.app/dist/Syxx.exe", sha256: good, size: 1, type: "exe")))
        // happy: matching type/ext
        XCTAssertNoThrow(try UpdateService.validatedDownloadURL(
            for: AppcastPackage(url: "https://syxx.app/dist/Syxx.dmg", sha256: good, size: 1, type: "dmg")))
    }

    /// The manifest must not be able to redirect the download anywhere it likes.
    func testDownloadURLIsConstrainedToProductHosts() {
        let good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f"
        func attempt(_ url: String) throws -> URL {
            try UpdateService.validatedDownloadURL(
                for: AppcastPackage(url: url, sha256: good, size: 1, type: "dmg"))
        }
        XCTAssertNoThrow(try attempt("https://syxx.app/dist/Syxx.dmg"))
        XCTAssertNoThrow(try attempt("https://dist.syxx.app/Syxx.dmg"))

        XCTAssertThrowsError(try attempt("https://evil.example/Syxx.dmg"))
        XCTAssertThrowsError(try attempt("http://syxx.app/Syxx.dmg"))
        XCTAssertThrowsError(try attempt("file:///tmp/Syxx.dmg"))
        XCTAssertThrowsError(try attempt("https://syxx.app.evil.example/Syxx.dmg"))
        XCTAssertThrowsError(try attempt("https://user:pass@syxx.app/Syxx.dmg"))
        XCTAssertThrowsError(try attempt("https://syxx.app:8443/Syxx.dmg"))
        XCTAssertThrowsError(try attempt(""))
    }
}

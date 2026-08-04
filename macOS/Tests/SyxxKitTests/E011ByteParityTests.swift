import XCTest
import Foundation
import CryptoKit
@testable import SyxxKit

/// E011 — the marquee cross-platform trust bar. The canonical signed evidence must serialize to
/// byte-identical UTF-8 on macOS (Swift) and Windows (C#), so an Ed25519 signature made on one
/// platform verifies on the other. The C# twin (`SignedEvidencePayloadTests`) asserts the SAME
/// `ExpectedCanonicalJson` literal and verifies the SAME Swift-produced signature over its own
/// independently-built bytes — proving byte-parity and cross-platform verification together.
final class E011ByteParityTests: XCTestCase {

    /// Fixed 32-byte Ed25519 seed (base64 of 32 * 0x42), identical on both platforms.
    static let fixedPrivKeyB64 = Data(repeating: 0x42, count: 32).base64EncodedString()

    /// The exact canonical bytes both platforms must emit. Exercises: `/` unescaped, `+` unescaped,
    /// a non-ASCII (raw UTF-8) value in `queryId`, sorted keys (every level: `authoritativeSet`,
    /// `conclusion`, `vantageSet`), sorted set arrays, omitted nulls (no `expiresAt`, no
    /// `dohOutcomes`, no `errorMessage`/`responseTimeMs` on the first outcome).
    static let expectedCanonicalJson =
        #"{"comparisonResult":{"authoritativeSet":["2606:2800::1","93.184.216.34"],"conclusion":"diverges","vantageSet":["10.0.0.1"]},"formatVersion":2,"queryId":"münchen/a+b.example","resolverQueryOutcomes":[{"resolverId":"1.1.1.1","success":true},{"errorMessage":"timeout","resolverId":"Cloudflare","success":false}],"timestamp":1704067200000}"#

    /// A Swift-produced Ed25519 public key + signature over the canonical bytes above (fixed key).
    /// Ed25519 signing is deterministic (RFC 8032), so this literal verifies over exactly these
    /// bytes and no others; the C# test verifies THIS Swift signature over its own bytes to prove
    /// Swift→Windows cross-verification. Regenerated 2026-08-01 over the corrected (fully-sorted)
    /// bytes; the public key is unchanged and still matches the fixed seed.
    static let swiftPubKeyB64 = "IVL40Zt5HSRFMkLhXy6rbLfP+ntqXtMAl5YOBpiB2xI="
    static let swiftSignatureB64 =
        "geqJGAQLxxQRJJ7P0QBvO0b/3nY78W6FASwtCnXByLTDmLWmXWpIRmqpffC1hblZUmZFd3TnRiNa77b99jk1Dw=="

    static func makeFixturePayload() -> SignedEvidencePayload {
        SignedEvidencePayload(
            formatVersion: 2,
            timestamp: 1_704_067_200_000,
            queryId: "münchen/a+b.example",
            resolverQueryOutcomes: [
                ResolverQueryOutcomeCanonical(resolverId: "1.1.1.1", success: true, errorMessage: nil, responseTimeMs: nil),
                ResolverQueryOutcomeCanonical(resolverId: "Cloudflare", success: false, errorMessage: "timeout", responseTimeMs: nil)
            ],
            comparisonResult: ComparisonResultCanonical(
                result: "diverges",
                authoritativeSet: ["2606:2800::1", "93.184.216.34"],
                vantageSet: ["10.0.0.1"]),
            expiresAt: nil,
            dohOutcomes: nil)
    }

    func testCanonicalBytesMatchCrossPlatformFixture() throws {
        let bytes = try Self.makeFixturePayload().canonicalBytes()
        XCTAssertEqual(String(data: bytes, encoding: .utf8), Self.expectedCanonicalJson)
    }

    func testFixedKeyRoundTripsLocally() throws {
        let bytes = try Self.makeFixturePayload().canonicalBytes()
        let key = try Curve25519.Signing.PrivateKey(rawRepresentation: Data(base64Encoded: Self.fixedPrivKeyB64)!)
        let sig = try key.signature(for: bytes)
        XCTAssertTrue(key.publicKey.isValidSignature(sig, for: bytes))
    }

    /// The hardcoded Swift signature must verify over the freshly-built canonical bytes (guards
    /// against a future serialization drift silently invalidating the published fixture).
    func testHardcodedSwiftSignatureVerifies() throws {
        let bytes = try Self.makeFixturePayload().canonicalBytes()
        let pub = try Curve25519.Signing.PublicKey(rawRepresentation: Data(base64Encoded: Self.swiftPubKeyB64)!)
        let sig = Data(base64Encoded: Self.swiftSignatureB64)!
        XCTAssertTrue(pub.isValidSignature(sig, for: bytes))
    }

    /// Windows→macOS cross-verification: an Ed25519 signature produced by the C# implementation
    /// (NSec, fixed 0x42 seed) over ITS canonical bytes must verify against Swift's independently
    /// built bytes. If the two platforms' serializations differed by one byte this would fail.
    /// (Regenerated 2026-08-01 over the corrected bytes; the derived public key matches
    /// `swiftPubKeyB64`, confirming the same keypair.)
    static let cSharpSignatureB64 =
        "//YQj4M6UmOOkClfTI9TcS2J+KenHuvYlvP6MU3h4Mm0ie8Ula3srsF/oQl0KU5tfE1KVnCHX+593VbN72fUCw=="
    func testHardcodedCSharpSignatureVerifies() throws {
        let bytes = try Self.makeFixturePayload().canonicalBytes()
        let pub = try Curve25519.Signing.PublicKey(rawRepresentation: Data(base64Encoded: Self.swiftPubKeyB64)!)
        let sig = Data(base64Encoded: Self.cSharpSignatureB64)!
        XCTAssertTrue(pub.isValidSignature(sig, for: bytes))
    }

    /// Sorted-array + sorted-key determinism must not depend on input order.
    func testSetOrderingIsDeterministic() throws {
        let a = ComparisonResultCanonical(result: "matches", authoritativeSet: SignedEvidencePayload.sortedUnique(["9.9.9.9", "1.1.1.1", "8.8.8.8"]), vantageSet: nil)
        let b = ComparisonResultCanonical(result: "matches", authoritativeSet: SignedEvidencePayload.sortedUnique(["8.8.8.8", "1.1.1.1", "9.9.9.9"]), vantageSet: nil)
        XCTAssertEqual(a.authoritativeSet, b.authoritativeSet)
        XCTAssertEqual(a.authoritativeSet, ["1.1.1.1", "8.8.8.8", "9.9.9.9"])
    }

    // MARK: - Failure cases — these MUST go RED if canonical serialization or Ed25519 integrity breaks.

    /// The core integrity property: the SAME payload with only the result flipped (diverges -> matches)
    /// must (a) serialize to DIFFERENT bytes and (b) fail to verify against a signature made over the
    /// original. If the serializer dropped `result`, or verification were slack, this goes red.
    func testTamperedResultBreaksSignature() throws {
        let original = Self.makeFixturePayload()
        let originalBytes = try original.canonicalBytes()
        let key = try Curve25519.Signing.PrivateKey(rawRepresentation: Data(base64Encoded: Self.fixedPrivKeyB64)!)
        let sig = try key.signature(for: originalBytes)
        XCTAssertTrue(key.publicKey.isValidSignature(sig, for: originalBytes))   // sanity: real signature over real bytes

        let tampered = SignedEvidencePayload(
            formatVersion: original.formatVersion,
            timestamp: original.timestamp,
            queryId: original.queryId,
            resolverQueryOutcomes: original.resolverQueryOutcomes,
            comparisonResult: ComparisonResultCanonical(
                result: "matches",
                authoritativeSet: original.comparisonResult.authoritativeSet,
                vantageSet: original.comparisonResult.vantageSet),
            expiresAt: original.expiresAt,
            dohOutcomes: original.dohOutcomes)
        let tamperedBytes = try tampered.canonicalBytes()
        XCTAssertNotEqual(tamperedBytes, originalBytes, "flipping the result must change the signed bytes")
        XCTAssertFalse(key.publicKey.isValidSignature(sig, for: tamperedBytes), "signature must NOT verify over tampered evidence")
    }

    /// Mutating an answer IP in the authoritative set must change the signed bytes (no field silently
    /// excluded from the signature).
    func testTamperedAnswerSetChangesBytes() throws {
        let originalBytes = try Self.makeFixturePayload().canonicalBytes()
        let tampered = SignedEvidencePayload(
            formatVersion: 2, timestamp: 1_704_067_200_000, queryId: "münchen/a+b.example",
            resolverQueryOutcomes: Self.makeFixturePayload().resolverQueryOutcomes,
            comparisonResult: ComparisonResultCanonical(
                result: "diverges",
                authoritativeSet: ["2606:2800::1", "6.6.6.6"],   // attacker-swapped one IP
                vantageSet: ["10.0.0.1"]),
            expiresAt: nil, dohOutcomes: nil)
        XCTAssertNotEqual(try tampered.canonicalBytes(), originalBytes)
    }

    /// A forged (non-signature) blob must be rejected.
    func testForgedSignatureIsRejected() throws {
        let bytes = try Self.makeFixturePayload().canonicalBytes()
        let pub = try Curve25519.Signing.PublicKey(rawRepresentation: Data(base64Encoded: Self.swiftPubKeyB64)!)
        var forged = Data(count: 64); forged[0] = 0x01
        XCTAssertFalse(pub.isValidSignature(forged, for: bytes))
    }

    /// A genuine signature verified under a DIFFERENT public key must be rejected.
    func testSignatureUnderWrongKeyIsRejected() throws {
        let bytes = try Self.makeFixturePayload().canonicalBytes()
        let signingKey = try Curve25519.Signing.PrivateKey(rawRepresentation: Data(base64Encoded: Self.fixedPrivKeyB64)!)
        let sig = try signingKey.signature(for: bytes)
        let wrongKey = Curve25519.Signing.PrivateKey()
        XCTAssertFalse(wrongKey.publicKey.isValidSignature(sig, for: bytes))
    }

    // MARK: - B215: Build()-exercising tests with authoritativeTtls/authoritativeTypes

    /// B215: the original golden vector constructs the payload by hand and never calls `build()`.
    /// This test exercises `build()` with authoritative records carrying TTLs and types (B098/D100
    /// fields), proving the builder correctly populates `authoritativeTtls`/`authoritativeTypes`
    /// and that the canonical bytes include them.
    func testBuildWithAuthoritativeTtlsAndTypes() throws {
        let authRecords = [
            DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34"),
            DNSRecord(name: "example.com", type: 28, ttl: 600, rdata: "2606:2800::1")
        ]
        let comparison = ComparisonResult(
            localServer: "10.0.0.1",
            localRecords: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")],
            publicResolverResults: ["Cloudflare": authRecords],
            isRedirectionDetected: false,
            localOutcome: ResolverQueryOutcome(
                records: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")]),
            publicResolverOutcomes: [
                "Cloudflare": ResolverQueryOutcome(records: authRecords)
            ])
        let payload = SignedEvidencePayload.build(
            domain: "example.com",
            comparison: comparison,
            timestampMillis: 1_704_067_200_000,
            expiresAtMillis: nil)
        let bytes = try payload.canonicalBytes()
        let json = String(data: bytes, encoding: .utf8)!
        // B098/D100: authoritativeTtls and authoritativeTypes must be present in canonical output.
        XCTAssertTrue(json.contains("authoritativeTtls"), "canonical JSON must include authoritativeTtls")
        XCTAssertTrue(json.contains("authoritativeTypes"), "canonical JSON must include authoritativeTypes")
        // Verify the values: ordered by rdata ascending — "2606:2800::1" < "93.184.216.34" by UTF-8
        // ('2' < '9'), so the IPv6 record (type 28, ttl 600) comes first.
        XCTAssertNotNil(payload.comparisonResult.authoritativeTtls)
        XCTAssertNotNil(payload.comparisonResult.authoritativeTypes)
        XCTAssertEqual(payload.comparisonResult.authoritativeTtls, [600, 300])
        XCTAssertEqual(payload.comparisonResult.authoritativeTypes, [28, 1])
    }

    /// B215: `build()` with a failed resolver outcome must produce canonical bytes that include
    /// the errorMessage. This test would go red if the B214 v2→v3 errorMessage drop were applied
    /// to v2 (the default formatVersion). It also proves the builder correctly handles the
    /// errorDescription → errorMessage mapping.
    func testBuildWithFailedResolverOutcome() throws {
        let comparison = ComparisonResult(
            localServer: "10.0.0.1",
            localRecords: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")],
            publicResolverResults: [:],
            isRedirectionDetected: true,
            localOutcome: ResolverQueryOutcome(
                records: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")]),
            publicResolverOutcomes: [
                "Cloudflare": ResolverQueryOutcome(
                    records: [],
                    status: .timeout,
                    errorDescription: "connection timed out")
            ])
        let payload = SignedEvidencePayload.build(
            domain: "example.com",
            comparison: comparison,
            timestampMillis: 1_704_067_200_000,
            expiresAtMillis: nil)
        // At v2 (default), errorMessage is included.
        let cfOutcome = payload.resolverQueryOutcomes.first { $0.resolverId == "Cloudflare" }
        XCTAssertNotNil(cfOutcome)
        XCTAssertEqual(cfOutcome?.errorMessage, "connection timed out")
        XCTAssertFalse(cfOutcome?.success ?? true)

        // At v3, errorMessage is dropped.
        let v3Payload = SignedEvidencePayload.build(
            domain: "example.com",
            comparison: comparison,
            timestampMillis: 1_704_067_200_000,
            expiresAtMillis: nil,
            formatVersion: SignedEvidencePayload.errorMessageFreeFormatVersion)
        let v3CfOutcome = v3Payload.resolverQueryOutcomes.first { $0.resolverId == "Cloudflare" }
        XCTAssertNil(v3CfOutcome?.errorMessage, "v3 must drop errorMessage from signed bytes")
        XCTAssertEqual(v3Payload.formatVersion, SignedEvidencePayload.errorMessageFreeFormatVersion)
    }

    /// B215: per-resolver answer records (B098) must be included in the canonical output when
    /// built through `build()`. The original golden vector omits these entirely.
    func testBuildIncludesPerResolverAnswers() throws {
        let comparison = ComparisonResult(
            localServer: "10.0.0.1",
            localRecords: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")],
            publicResolverResults: ["Cloudflare": [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")]],
            isRedirectionDetected: false,
            localOutcome: ResolverQueryOutcome(
                records: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")]),
            publicResolverOutcomes: [
                "Cloudflare": ResolverQueryOutcome(
                    records: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")])
            ])
        let payload = SignedEvidencePayload.build(
            domain: "example.com",
            comparison: comparison,
            timestampMillis: 1_704_067_200_000,
            expiresAtMillis: nil)
        XCTAssertNotNil(payload.perResolverAnswers, "build() must include perResolverAnswers")
        XCTAssertEqual(payload.perResolverAnswers?.count, 2, "local + Cloudflare")
        let bytes = try payload.canonicalBytes()
        let json = String(data: bytes, encoding: .utf8)!
        XCTAssertTrue(json.contains("perResolverAnswers"), "canonical JSON must include perResolverAnswers")
    }

    // MARK: - B252: v4 canonical DoH signing anchors

    /// The v4 golden literal. Identical on both platforms (the C# twin asserts the SAME literal
    /// over independently-built bytes). Exercises the B252 additions: canonical `dohOutcomes`
    /// (typed success derived from status, resolverId-sorted, errorMessage-free) and
    /// `perResolverAnswers` (records sorted by name/type/ttl/rdata, resolver list sorted by id),
    /// plus the v3 semantics (no free-text errorMessage in outcomes).
    static let expectedV4CanonicalJson =
        #"{"comparisonResult":{"authoritativeSet":["93.184.216.34"],"authoritativeTtls":[216],"authoritativeTypes":[1],"conclusion":"matches","vantageSet":["93.184.216.34"]},"dohOutcomes":{"outcomes":[{"resolverId":"Cloudflare","success":true},{"resolverId":"Google","success":false}]},"formatVersion":4,"perResolverAnswers":[{"records":[{"name":"example.com","rdata":"93.184.216.34","ttl":300,"type":1}],"resolverId":"1.1.1.1"},{"records":[{"name":"example.com","rdata":"93.184.216.34","ttl":300,"type":1}],"resolverId":"10.0.0.1"},{"records":[{"name":"example.com","rdata":"93.184.216.34","ttl":216,"type":1}],"resolverId":"Cloudflare"}],"queryId":"münchen/a+b.example","resolverQueryOutcomes":[{"resolverId":"1.1.1.1","success":true},{"resolverId":"10.0.0.1","success":true},{"resolverId":"Cloudflare","success":true}],"timestamp":1704067200000}"#

    /// B252: C#-produced signature (NSec, fixed 0x42 seed) over ITS byte-identical canonical bytes
    /// — must verify against Swift's independently-built bytes. Proves Windows→macOS verification
    /// at v4 including the new canonical DoH + per-resolver fields.
    static let v4CSharpSignatureB64 =
        "lzilx0FgTfuDAUWTIffMi+l3h8yV1bxw/rjYb3N4ViucJTpTn4Z7r+19EMnD+5ioEVEhUEhMzIiDBrfD7inyDg=="

    /// B252: Swift-produced signature over the v4 canonical bytes (fixed 0x42 seed).
    static let v4SwiftSignatureB64 =
        "8YjrILke7PagrzwE0CovFvj4KWzx81kJ2ioNl2WsMDXBlsquprHyDjCqcln/6TvqnHQPFEYmuvwzzaMEk6diCQ=="

    static func makeV4FixturePayload() -> SignedEvidencePayload {
        SignedEvidencePayload(
            formatVersion: SignedEvidencePayload.currentFormatVersion,
            timestamp: 1_704_067_200_000,
            queryId: "münchen/a+b.example",
            resolverQueryOutcomes: [
                ResolverQueryOutcomeCanonical(resolverId: "1.1.1.1", success: true, errorMessage: nil, responseTimeMs: nil),
                ResolverQueryOutcomeCanonical(resolverId: "10.0.0.1", success: true, errorMessage: nil, responseTimeMs: nil),
                ResolverQueryOutcomeCanonical(resolverId: "Cloudflare", success: true, errorMessage: nil, responseTimeMs: nil),
            ],
            comparisonResult: ComparisonResultCanonical(
                result: "matches",
                authoritativeSet: ["93.184.216.34"],
                vantageSet: ["93.184.216.34"],
                authoritativeTtls: [216],
                authoritativeTypes: [1]),
            expiresAt: nil,
            dohOutcomes: DoHOutcomesCanonical(outcomes: [
                ResolverQueryOutcomeCanonical(resolverId: "Cloudflare", success: true, errorMessage: nil, responseTimeMs: nil),
                ResolverQueryOutcomeCanonical(resolverId: "Google", success: false, errorMessage: nil, responseTimeMs: nil),
            ]),
            perResolverAnswers: [
                ResolverAnswerRecordsCanonical(resolverId: "1.1.1.1", records: [
                    .init(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34"),
                ]),
                ResolverAnswerRecordsCanonical(resolverId: "10.0.0.1", records: [
                    .init(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34"),
                ]),
                ResolverAnswerRecordsCanonical(resolverId: "Cloudflare", records: [
                    .init(name: "example.com", type: 1, ttl: 216, rdata: "93.184.216.34"),
                ]),
            ])
    }

    func testV4CanonicalBytesMatchCrossPlatformFixture() throws {
        let bytes = try Self.makeV4FixturePayload().canonicalBytes()
        XCTAssertEqual(String(data: bytes, encoding: .utf8), Self.expectedV4CanonicalJson)
    }

    /// The hardcoded Swift v4 signature must verify over the freshly-built canonical bytes.
    func testV4HardcodedSwiftSignatureVerifies() throws {
        let bytes = try Self.makeV4FixturePayload().canonicalBytes()
        let pub = try Curve25519.Signing.PublicKey(rawRepresentation: Data(base64Encoded: Self.swiftPubKeyB64)!)
        let sig = Data(base64Encoded: Self.v4SwiftSignatureB64)!
        XCTAssertTrue(pub.isValidSignature(sig, for: bytes))
    }

    /// Windows→macOS cross-verification at v4: the C#-produced signature must verify over Swift's
    /// bytes, proving the new dohOutcomes/perResolverAnswers serialize byte-identically.
    func testV4HardcodedCSharpSignatureVerifies() throws {
        let bytes = try Self.makeV4FixturePayload().canonicalBytes()
        let pub = try Curve25519.Signing.PublicKey(rawRepresentation: Data(base64Encoded: Self.swiftPubKeyB64)!)
        let sig = Data(base64Encoded: Self.v4CSharpSignatureB64)!
        XCTAssertTrue(pub.isValidSignature(sig, for: bytes))
    }

    /// B252 marquee failure case: rewriting the CANONICAL DoH evidence (signed at v4) must
    /// invalidate the signature. This is the signature-level guard the display KnownGap
    /// (`testKnownGap_B252_rewritingDoHEvidenceIsNeverValid`) points at — a flipped DoH outcome
    /// (Google transportError → success) changes the signed bytes and breaks the seal.
    func testTamperingCanonicalDoHEvidenceIsRejected() throws {
        let original = Self.makeV4FixturePayload()
        let originalBytes = try original.canonicalBytes()
        let key = try Curve25519.Signing.PrivateKey(rawRepresentation: Data(base64Encoded: Self.fixedPrivKeyB64)!)
        let sig = try key.signature(for: originalBytes)
        XCTAssertTrue(key.publicKey.isValidSignature(sig, for: originalBytes))   // sanity

        let tampered = SignedEvidencePayload(
            formatVersion: original.formatVersion,
            timestamp: original.timestamp,
            queryId: original.queryId,
            resolverQueryOutcomes: original.resolverQueryOutcomes,
            comparisonResult: original.comparisonResult,
            expiresAt: original.expiresAt,
            dohOutcomes: DoHOutcomesCanonical(outcomes: [
                ResolverQueryOutcomeCanonical(resolverId: "Cloudflare", success: true, errorMessage: nil, responseTimeMs: nil),
                ResolverQueryOutcomeCanonical(resolverId: "Google", success: true, errorMessage: nil, responseTimeMs: nil),
            ]),
            perResolverAnswers: original.perResolverAnswers)
        let tamperedBytes = try tampered.canonicalBytes()
        XCTAssertNotEqual(tamperedBytes, originalBytes, "flipping the canonical DoH outcome must change the signed bytes")
        XCTAssertFalse(key.publicKey.isValidSignature(sig, for: tamperedBytes), "signature must NOT verify over rewritten canonical DoH evidence")
    }

    /// B098/B252 failure case: swapping answer records between resolvers (equal aggregate answers,
    /// changed ownership) must invalidate the signature — perResolverAnswers binds ownership.
    func testTamperingCanonicalPerResolverAnswersIsRejected() throws {
        let original = Self.makeV4FixturePayload()
        let originalBytes = try original.canonicalBytes()
        let key = try Curve25519.Signing.PrivateKey(rawRepresentation: Data(base64Encoded: Self.fixedPrivKeyB64)!)
        let sig = try key.signature(for: originalBytes)
        XCTAssertTrue(key.publicKey.isValidSignature(sig, for: originalBytes))   // sanity

        // Attacker swaps the 216-ttl record (Cloudflare's) into 1.1.1.1's list and vice versa.
        let tampered = SignedEvidencePayload(
            formatVersion: original.formatVersion,
            timestamp: original.timestamp,
            queryId: original.queryId,
            resolverQueryOutcomes: original.resolverQueryOutcomes,
            comparisonResult: original.comparisonResult,
            expiresAt: original.expiresAt,
            dohOutcomes: original.dohOutcomes,
            perResolverAnswers: [
                ResolverAnswerRecordsCanonical(resolverId: "1.1.1.1", records: [
                    .init(name: "example.com", type: 1, ttl: 216, rdata: "93.184.216.34"),
                ]),
                ResolverAnswerRecordsCanonical(resolverId: "10.0.0.1", records: [
                    .init(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34"),
                ]),
                ResolverAnswerRecordsCanonical(resolverId: "Cloudflare", records: [
                    .init(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34"),
                ]),
            ])
        let tamperedBytes = try tampered.canonicalBytes()
        XCTAssertNotEqual(tamperedBytes, originalBytes, "swapping answer ownership must change the signed bytes")
        XCTAssertFalse(key.publicKey.isValidSignature(sig, for: tamperedBytes), "signature must NOT verify over rewritten per-resolver answers")
    }
}

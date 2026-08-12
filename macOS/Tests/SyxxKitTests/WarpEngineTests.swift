import XCTest
import Foundation
import CryptoKit
import Compression
@testable import SyxxKit

/// macOS half of the WARP_MANIFEST_V1 client oracle (docs/warp-manifest-v1-spec.md §§4–9). Loads the
/// shared `warp-manifest-vectors/` corpus — the SAME directory the Windows half
/// (Windows/dotNET/Syxx.Tests/WarpEngineTests.cs) and the Python server load — and asserts this
/// platform's `WarpEngine` reaches the EXACT expected conclusion + per-asset states + signature/
/// manifest/freshness/HTML states. Fetch, clock, and pinned keys are injected so the suite is fully
/// offline. The redirect fail-closed paths (not exercised by the corpus, which uses redirect_policy
/// "none") are covered by dedicated injected-fetch unit tests below.
final class WarpEngineTests: XCTestCase {

    private static func corpusDir() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
            .appendingPathComponent("warp-manifest-vectors")
    }

    // MARK: - Corpus loader

    private func caseNames() -> [String] {
        let dir = Self.corpusDir()
        guard let names = try? FileManager.default.contentsOfDirectory(atPath: dir.path) else { return [] }
        return names.filter { $0.range(of: #"^\d\d\d_"#, options: .regularExpression) != nil }.sorted()
    }

    /// Derive the absolute URL a client-vantage input file is served at (spec §8): a sanitized
    /// cross-origin name like `cdn.example.test__vendor.js` → `https://cdn.example.test/vendor.js`.
    private static func urlForInput(_ filename: String) -> String {
        if let r = filename.range(of: "__") {
            let host = String(filename[..<r.lowerBound])
            let rest = String(filename[r.upperBound...])
            return "https://\(host)/\(rest)"
        }
        return "https://example.test/\(filename)"
    }

    /// Apple `COMPRESSION_ZLIB` decodes RFC-1951 raw DEFLATE; gzip = 10-byte header + DEFLATE +
    /// 8-byte trailer, so strip the wrapper and inflate.
    private static func gunzip(_ data: Data) -> Data? {
        guard data.count > 18 else { return nil }
        let deflate = data.subdata(in: 10..<(data.count - 8))
        let cap = 1 << 16
        var dst = [UInt8](repeating: 0, count: cap)
        let written = deflate.withUnsafeBytes { srcRaw -> Int in
            guard let src = srcRaw.bindMemory(to: UInt8.self).baseAddress else { return 0 }
            return dst.withUnsafeMutableBufferPointer { d in
                compression_decode_buffer(d.baseAddress!, cap, src, deflate.count, nil, COMPRESSION_ZLIB)
            }
        }
        return written > 0 ? Data(dst.prefix(written)) : nil
    }

    /// `@unchecked Sendable`: every stored property is an immutable `let`, and `expected` holds only
    /// JSON-derived immutable values. The corpus closures below are `@Sendable`, so under Swift 6
    /// strict concurrency the struct must declare conformance or the test target will not compile.
    private struct CaseData: @unchecked Sendable {
        let envelope: Data
        let origin: String
        let nowMs: UInt64
        let nonce: String?
        let pinnedKeys: [String: Data]
        let bytesByURL: [String: [UInt8]]
        let originHTML: [UInt8]
        let expected: [String: Any]
    }

    /// Loads one case. Returns nil (with XCTSkip semantics via the caller) if the case needs a
    /// transport codec this harness lacks (brotli).
    private func loadCase(_ name: String) throws -> CaseData? {
        let dir = Self.corpusDir().appendingPathComponent(name)
        let envelope = try Data(contentsOf: dir.appendingPathComponent("envelope.json"))
        let origin = (try String(contentsOf: dir.appendingPathComponent("requested_origin"), encoding: .utf8))
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let nowMs = UInt64((try String(contentsOf: dir.appendingPathComponent("now_ms"), encoding: .utf8))
            .trimmingCharacters(in: .whitespacesAndNewlines)) ?? 0
        let nonce = (try? String(contentsOf: dir.appendingPathComponent("client_nonce"), encoding: .utf8))?
            .trimmingCharacters(in: .whitespacesAndNewlines)

        // Pinned keys: trusted_key_ids are the lowercase-hex raw public keys, so the pubkey IS
        // hexdecode(key_id). WarpManifestPinning.parse builds the same [keyId: rawpub] map.
        let pinnedRaw = try Data(contentsOf: dir.appendingPathComponent("pinned_keys.json"))
        var pinnedKeys: [String: Data] = [:]
        if let obj = try JSONSerialization.jsonObject(with: pinnedRaw) as? [String: Any],
           let ids = obj["trusted_key_ids"] as? [String] {
            pinnedKeys = WarpManifestPinning.parse(ids.joined(separator: " "))
        }

        // fetch_meta: which input files arrived under a Content-Encoding the transport must decode.
        var fetchMeta: [String: String] = [:]
        if let fm = try? Data(contentsOf: dir.appendingPathComponent("fetch_meta.json")),
           let obj = try? JSONSerialization.jsonObject(with: fm) as? [String: String] {
            fetchMeta = obj
        }
        if fetchMeta.values.contains(where: { $0.lowercased() == "br" }) {
            // Foundation ships no brotli codec; skip (production Swift fetch must add one for §8).
            return nil
        }

        let inputsDir = dir.appendingPathComponent("inputs")
        var bytesByURL: [String: [UInt8]] = [:]
        var originHTML: [UInt8] = []
        let files = (try? FileManager.default.contentsOfDirectory(atPath: inputsDir.path)) ?? []
        for f in files {
            var data = try Data(contentsOf: inputsDir.appendingPathComponent(f))
            if fetchMeta[f]?.lowercased() == "gzip", let un = Self.gunzip(data) { data = un }
            if f == "origin.html" { originHTML = Array(data) }
            else { bytesByURL[Self.urlForInput(f)] = Array(data) }
        }

        let expected = (try JSONSerialization.jsonObject(with: Data(contentsOf: dir.appendingPathComponent("expected.json"))) as? [String: Any]) ?? [:]
        return CaseData(envelope: envelope, origin: origin, nowMs: nowMs, nonce: nonce,
                        pinnedKeys: pinnedKeys, bytesByURL: bytesByURL, originHTML: originHTML, expected: expected)
    }

    // MARK: - The corpus oracle test

    func testCorpusOracle() async throws {
        let names = caseNames()
        XCTAssertFalse(names.isEmpty, "warp-manifest-vectors corpus is missing — a vacuously-green oracle is the green that lies.")
        for name in names {
            guard let c = try loadCase(name) else { continue }   // skipped (missing codec)
            let fetch: @Sendable (WarpAssetRequest) async -> WarpFetchOutcome = { req in
                if req.isHTML {
                    return c.originHTML.isEmpty ? .failed : .fetched(bytes: c.originHTML, terminalURL: req.url)
                }
                guard let bytes = c.bytesByURL[req.url] else { return .failed }
                return .fetched(bytes: bytes, terminalURL: req.url)   // corpus policy is "none": no redirect.
            }
            let result = await WarpEngine.verify(originURL: c.origin, envelopeBytes: c.envelope,
                                                 pinnedKeys: c.pinnedKeys, nonce: c.nonce, nowMs: c.nowMs, fetch: fetch)

            XCTAssertEqual(result.conclusion.rawValue, c.expected["conclusion"] as? String, "[\(name)] conclusion")
            XCTAssertEqual(result.signatureState.rawValue, c.expected["signatureState"] as? String, "[\(name)] signatureState")
            if let m = c.expected["manifestState"] as? String {
                let mapped = m == "notEvaluated" && result.manifestState == .notEvaluated ? m : result.manifestState.rawValue
                XCTAssertEqual(mapped, m, "[\(name)] manifestState")
            }
            XCTAssertEqual(mapFreshness(result.freshnessState), c.expected["freshnessState"] as? String, "[\(name)] freshnessState")
            XCTAssertEqual(mapContent(result.htmlState), c.expected["htmlState"] as? String, "[\(name)] htmlState")

            let expAssets = (c.expected["assets"] as? [[String: Any]]) ?? []
            XCTAssertEqual(result.assets.count, expAssets.count, "[\(name)] asset count")
            for (i, ea) in expAssets.enumerated() where i < result.assets.count {
                XCTAssertEqual(result.assets[i].url, ea["url"] as? String, "[\(name)] asset[\(i)] url")
                XCTAssertEqual(mapContent(result.assets[i].state), ea["state"] as? String, "[\(name)] asset[\(i)] state")
            }
        }
    }

    /// Corpus `freshnessState`/`htmlState` use `notEvaluated`; our enum rawValue is also `notEvaluated`.
    private func mapFreshness(_ s: WarpFreshnessState) -> String { s.rawValue }
    private func mapContent(_ s: WarpContentState) -> String { s.rawValue }

    // MARK: - Parity anchor (C#-vs-Swift): the shared corpus is the oracle both twins assert

    /// The "all good" case pins the exact tuple both twins must produce from the identical fixture.
    /// The C# twin (WarpEngineTests.AllGood_IsVerifiedMatch) asserts the byte-identical tuple.
    func testParityAllGood() async throws {
        guard let c = try loadCase("001_all_good") else { return XCTFail("001_all_good missing") }
        let fetch: @Sendable (WarpAssetRequest) async -> WarpFetchOutcome = { req in
            if req.isHTML { return .fetched(bytes: c.originHTML, terminalURL: req.url) }
            return c.bytesByURL[req.url].map { .fetched(bytes: $0, terminalURL: req.url) } ?? .failed
        }
        let r = await WarpEngine.verify(originURL: c.origin, envelopeBytes: c.envelope,
                                        pinnedKeys: c.pinnedKeys, nonce: c.nonce, nowMs: c.nowMs, fetch: fetch)
        XCTAssertEqual(r.conclusion, .verifiedMatch)
        XCTAssertEqual(r.signatureState, .verified)
        XCTAssertEqual(r.manifestState, .parsed)
        XCTAssertEqual(r.freshnessState, .fresh)
        XCTAssertEqual(r.htmlState, .match)
        XCTAssertTrue(r.assets.allSatisfy { $0.state == .match })
    }

    // MARK: - Pure lattice (§5)

    func testLatticePrecedence() {
        // Invalid signature is tampering regardless of everything else.
        XCTAssertEqual(WarpEngine.conclude(signature: .invalidSignature, manifest: .notEvaluated,
            freshness: .notEvaluated, html: .notEvaluated, assets: []), .tamperingDetected)
        // Untrusted signer never becomes a match.
        XCTAssertEqual(WarpEngine.conclude(signature: .untrustedSigner, manifest: .notEvaluated,
            freshness: .notEvaluated, html: .notEvaluated, assets: []), .inconclusive)
        // Verified+fresh, html match, asset mismatch → tampering (mismatch outranks).
        XCTAssertEqual(WarpEngine.conclude(signature: .verified, manifest: .parsed, freshness: .fresh,
            html: .match, assets: [.match, .mismatch]), .tamperingDetected)
        // Verified+fresh, all match, one server-declared-unfetchable → inconclusive (never match).
        XCTAssertEqual(WarpEngine.conclude(signature: .verified, manifest: .parsed, freshness: .fresh,
            html: .match, assets: [.match, .fetchFailedDeclared]), .inconclusive)
        // Verified+fresh, all match → match.
        XCTAssertEqual(WarpEngine.conclude(signature: .verified, manifest: .parsed, freshness: .fresh,
            html: .match, assets: [.match]), .verifiedMatch)
    }

    // MARK: - Injected redirect fail-closed tests (§6) — not covered by the corpus

    /// Helper: sign a manifest dict in-test (deterministic Ed25519) and return (envelope, pinnedKeys,
    /// origin, keyId). Uses the canonical serialization (sorted keys, compact) the server uses.
    private func makeSignedEnvelope(_ manifest: [String: Any]) throws -> (envelope: Data, pinned: [String: Data], origin: String) {
        let priv = Curve25519.Signing.PrivateKey()
        let pubRaw = priv.publicKey.rawRepresentation
        let keyId = pubRaw.map { String(format: "%02x", $0) }.joined()
        let B = try JSONSerialization.data(withJSONObject: manifest, options: [.sortedKeys])
        let sig = try priv.signature(for: B)
        let env: [String: Any] = ["manifest_b64": B.base64EncodedString(),
                                  "signature_ed25519": Data(sig).base64EncodedString(),
                                  "key_id": keyId]
        let envData = try JSONSerialization.data(withJSONObject: env)
        return (envData, [keyId: pubRaw], manifest["origin_url"] as? String ?? "")
    }

    func testSameOriginRedirectTerminalMismatchIsFetchErrorInconclusive() async throws {
        let origin = "https://example.test/"
        let htmlBytes: [UInt8] = Array("<html>ok</html>".utf8)
        let assetBytes: [UInt8] = Array("console.log(1)".utf8)
        let manifest: [String: Any] = [
            "warp_manifest_version": "WARP_MANIFEST_V1",
            "origin_url": origin, "final_url": origin,
            "html_sha256": WarpEngine.sha256Hex(htmlBytes),
            "asset_classes_in_scope": ["HTML", "JS"],
            "assets": [["url": "https://example.test/app.js", "final_url": "https://example.test/app.js",
                        "class": "JS", "sha256": WarpEngine.sha256Hex(assetBytes), "bytes": assetBytes.count]],
            "timestamp": 1_754_179_200_000, "freshness_seconds": 300,
            "redirect_policy": "same-origin", "allowed_redirects": [], "nonce": NSNull()]
        let (env, pinned, o) = try makeSignedEnvelope(manifest)
        let fetch: @Sendable (WarpAssetRequest) async -> WarpFetchOutcome = { req in
            if req.isHTML { return .fetched(bytes: htmlBytes, terminalURL: origin) }
            // The asset arrives via a redirect to a DIFFERENT terminal than final_url → fail closed.
            return .fetched(bytes: assetBytes, terminalURL: "https://example.test/app.v2.js")
        }
        let r = await WarpEngine.verify(originURL: o, envelopeBytes: env, pinnedKeys: pinned,
                                        nonce: nil, nowMs: 1_754_179_200_000, fetch: fetch)
        XCTAssertEqual(r.signatureState, .verified)
        XCTAssertEqual(r.freshnessState, .fresh)
        XCTAssertEqual(r.assets.first?.state, .fetchError, "unexpected redirect terminal must be fetchError")
        XCTAssertEqual(r.conclusion, .inconclusive, "redirect fail-closed is never TAMPERING and never MATCH")
    }

    func testNoneRedirectIsFetchErrorInconclusive() async throws {
        let origin = "https://example.test/"
        let htmlBytes: [UInt8] = Array("<html>ok</html>".utf8)
        let assetBytes: [UInt8] = Array("body{}".utf8)
        let manifest: [String: Any] = [
            "warp_manifest_version": "WARP_MANIFEST_V1",
            "origin_url": origin, "final_url": origin,
            "html_sha256": WarpEngine.sha256Hex(htmlBytes),
            "asset_classes_in_scope": ["HTML", "CSS"],
            "assets": [["url": "https://example.test/x.css", "class": "CSS",
                        "sha256": WarpEngine.sha256Hex(assetBytes), "bytes": assetBytes.count]],
            "timestamp": 1_754_179_200_000, "freshness_seconds": 300,
            "redirect_policy": "none", "allowed_redirects": [], "nonce": NSNull()]
        let (env, pinned, o) = try makeSignedEnvelope(manifest)
        let fetch: @Sendable (WarpAssetRequest) async -> WarpFetchOutcome = { req in
            if req.isHTML { return .fetched(bytes: htmlBytes, terminalURL: origin) }
            // policy none: any terminal != request URL means a redirect happened → fail closed.
            return .fetched(bytes: assetBytes, terminalURL: "https://example.test/x.css?v=2")
        }
        let r = await WarpEngine.verify(originURL: o, envelopeBytes: env, pinnedKeys: pinned,
                                        nonce: nil, nowMs: 1_754_179_200_000, fetch: fetch)
        XCTAssertEqual(r.assets.first?.state, .fetchError)
        XCTAssertEqual(r.conclusion, .inconclusive)
    }

    // MARK: - Pinning derivation

    func testPinningHexKeyIdRoundTrips() {
        let priv = Curve25519.Signing.PrivateKey()
        let pubRaw = priv.publicKey.rawRepresentation
        let keyId = WarpManifestPinning.deriveKeyId(rawPublicKey: pubRaw)
        XCTAssertEqual(keyId.count, 64)
        let parsed = WarpManifestPinning.parse(keyId)
        XCTAssertEqual(parsed[keyId], pubRaw, "a bare hex key_id must resolve to its own raw public key")
    }
}

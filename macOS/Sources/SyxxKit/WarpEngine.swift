import Foundation
import CryptoKit

// =====================================================================================
// WARP_MANIFEST_V1 — signed-manifest content-integrity CLIENT engine (docs/warp-manifest-v1-spec.md).
//
// Behavioral twin of Windows/dotNET/Syxx.Kit/WarpEngine.cs, oracle-checked against the shared
// warp-manifest-vectors/ corpus. The client NEVER parses HTML and NEVER resolves a URL (§1): the
// server is the only parser. This engine verifies the Ed25519 signature over the received bytes
// BEFORE any JSON parse (§4), checks freshness/origin/nonce, fetches each DECLARED asset from the
// client's own vantage under the declared redirect policy (§6), hashes raw bytes, compares to the
// manifest, and applies the fail-closed conclusion lattice (§5). "verdict" is banned — result/conclusion.
// =====================================================================================

/// §4 signature-stage outcome. `malformed` is reserved for an unparseable TRANSPORT ENVELOPE (a
/// malformed INNER manifest after a valid signature is `WarpManifestState.malformed`, not this).
public enum WarpSignatureState: String, Sendable, Codable {
    case verified
    case untrustedSigner
    case invalidSignature
    case malformed
}

/// §4.4 inner-manifest parse outcome. `notEvaluated` when the signature stage never reached the
/// parse (untrusted/invalid/envelope-malformed).
public enum WarpManifestState: String, Sendable, Codable {
    case parsed
    case malformed
    case notEvaluated
}

/// §4.5 freshness/origin/nonce outcome. `notEvaluated` when the manifest was never parsed.
public enum WarpFreshnessState: String, Sendable, Codable {
    case fresh
    case expired
    case futureDated
    case originMismatch
    case replayNonceMismatch
    case notEvaluated
}

/// Per-content comparison state (HTML or a single asset).
/// - `fetchError`: the CLIENT could not fetch it (network/SSRF/redirect fail-closed) — missing data.
/// - `fetchFailedDeclared`: the SERVER recorded `fetch_status:"fetch_failed"` (no sha256) — recorded,
///   never client-fetched, forces INCONCLUSIVE (§3).
/// - `notEvaluated`: comparison was never reached (signature/freshness failed).
/// Neither `fetchError` nor `fetchFailedDeclared` ever becomes a clean match.
public enum WarpContentState: String, Sendable, Codable {
    case match
    case mismatch
    case fetchError
    case fetchFailedDeclared
    case notEvaluated
}

/// §5 fail-closed conclusion lattice output.
public enum WarpConclusion: String, Sendable, Codable {
    case tamperingDetected = "TAMPERING_DETECTED"
    case inconclusive = "INCONCLUSIVE"
    case verifiedMatch = "VERIFIED_MATCH"
}

/// §6 redirect policy (part of the protocol, fail-closed).
public enum WarpRedirectPolicy: String, Sendable, Codable {
    case none
    case sameOrigin
    case list

    public static func parse(_ raw: String?) -> WarpRedirectPolicy {
        switch (raw ?? "").lowercased() {
        case "same-origin", "sameorigin": return .sameOrigin
        case "list": return .list
        default: return .none   // fail closed: unknown / absent → strictest
        }
    }
}

/// One declared asset's comparison outcome. `fetchedBytes` is DISPLAY-ONLY (retained for the diff
/// overlay on a mismatch); it never feeds the lattice.
public struct WarpAssetResult: Sendable, Codable {
    public let url: String
    public let declaredSha256: String?
    public let fetchedSha256: String?
    public let byteLen: Int?
    public let state: WarpContentState
    public let assetClass: String
    public let fetchedBytes: [UInt8]?

    public init(url: String, declaredSha256: String?, fetchedSha256: String?, byteLen: Int?,
                state: WarpContentState, assetClass: String, fetchedBytes: [UInt8]? = nil) {
        self.url = url
        self.declaredSha256 = declaredSha256
        self.fetchedSha256 = fetchedSha256
        self.byteLen = byteLen
        self.state = state
        self.assetClass = assetClass
        self.fetchedBytes = fetchedBytes
    }
}

/// The full client-side result of verifying one signed manifest. Retains display-only raw blobs
/// (`rawManifestJSON`, `localHtmlBytes`, per-asset `fetchedBytes`) so the UI stream can render the
/// `TextDiff` overlay — this engine does NOT build the diff.
public struct WarpManifestResult: Sendable, Codable {
    public let originURL: String
    public let finalURL: String?
    public let keyId: String?
    public let signatureState: WarpSignatureState
    public let manifestState: WarpManifestState
    public let freshnessState: WarpFreshnessState
    public let htmlState: WarpContentState
    public let declaredHtmlSha256: String?
    public let fetchedHtmlSha256: String?
    public let assets: [WarpAssetResult]
    public let conclusion: WarpConclusion
    public let inconclusiveReason: String?
    /// Display-only: the decoded manifest JSON text, populated ONLY after a valid signature.
    public let rawManifestJSON: String?
    /// Display-only: the locally-fetched origin HTML bytes, retained for the overlay diff.
    public let localHtmlBytes: [UInt8]?

    public init(originURL: String, finalURL: String?, keyId: String?,
                signatureState: WarpSignatureState, manifestState: WarpManifestState,
                freshnessState: WarpFreshnessState, htmlState: WarpContentState,
                declaredHtmlSha256: String?, fetchedHtmlSha256: String?,
                assets: [WarpAssetResult], conclusion: WarpConclusion, inconclusiveReason: String?,
                rawManifestJSON: String? = nil, localHtmlBytes: [UInt8]? = nil) {
        self.originURL = originURL
        self.finalURL = finalURL
        self.keyId = keyId
        self.signatureState = signatureState
        self.manifestState = manifestState
        self.freshnessState = freshnessState
        self.htmlState = htmlState
        self.declaredHtmlSha256 = declaredHtmlSha256
        self.fetchedHtmlSha256 = fetchedHtmlSha256
        self.assets = assets
        self.conclusion = conclusion
        self.inconclusiveReason = inconclusiveReason
        self.rawManifestJSON = rawManifestJSON
        self.localHtmlBytes = localHtmlBytes
    }
}

/// What the injected fetch closure is asked to retrieve. The client fetches EXACTLY `url` from its
/// own vantage under `redirectPolicy` (§6) and returns the terminal URL for reconciliation.
public struct WarpAssetRequest: Sendable {
    public let url: String
    public let finalURL: String?
    public let redirectPolicy: WarpRedirectPolicy
    public let allowedRedirects: [String]
    public let assetClass: String
    public let isHTML: Bool
}

/// Outcome of a single client-vantage fetch. `failed` = network error, SSRF block, or a redirect
/// the policy forbids (fail closed). `fetched` carries the raw envelope-decoded bytes and the
/// terminal (post-redirect) URL the client actually landed on.
public enum WarpFetchOutcome: Sendable {
    case fetched(bytes: [UInt8], terminalURL: String)
    case failed
}

public enum WarpEngine {

    // MARK: - §5 conclusion lattice (one pure function)

    /// Fail-closed conclusion lattice (§5). Confirmed tampering (an invalid signature, or a real
    /// byte mismatch under a verified+fresh manifest) OUTRANKS missing data; missing/unverifiable
    /// data never upgrades to a clean match.
    public static func conclude(signature: WarpSignatureState,
                                manifest: WarpManifestState,
                                freshness: WarpFreshnessState,
                                html: WarpContentState,
                                assets: [WarpContentState]) -> WarpConclusion {
        // A present-but-non-verifying signature is positive interference evidence.
        if signature == .invalidSignature { return .tamperingDetected }
        // Untrusted signer / unparseable envelope → cannot conclude.
        if signature == .untrustedSigner || signature == .malformed { return .inconclusive }
        // signature == .verified below. A malformed inner manifest is unverifiable content.
        if manifest != .parsed { return .inconclusive }
        // Any freshness failure → inconclusive.
        if freshness != .fresh { return .inconclusive }
        // Verified + fresh: a real byte mismatch (HTML or any asset) is tampering.
        if html == .mismatch || assets.contains(.mismatch) { return .tamperingDetected }
        // Verified + fresh, no mismatch, but something could not be verified → inconclusive.
        if html != .match { return .inconclusive }
        if assets.contains(where: { $0 != .match }) { return .inconclusive }
        // Verified + fresh + HTML matches + every declared asset fetched and matched.
        return .verifiedMatch
    }

    // MARK: - §4 verification (mandatory sequence)

    /// Verify a signed manifest envelope (§4) and produce the full result. Fully deterministic and
    /// offline given the injected `fetch` closure and `nowMs` clock. `pinnedKeys` maps
    /// `key_id -> raw Ed25519 public-key bytes` (see `WarpManifestPinning`). Because the corpus/
    /// production `key_id` is the lowercase hex of the raw public key, resolving the envelope's
    /// key_id in `pinnedKeys` is exactly the pinned-trust-set check (§7).
    public static func verify(
        originURL: String,
        envelopeBytes: Data,
        pinnedKeys: [String: Data],
        nonce: String? = nil,
        nowMs: UInt64,
        fetch: @Sendable (WarpAssetRequest) async -> WarpFetchOutcome
    ) async -> WarpManifestResult {

        func stopped(signature: WarpSignatureState, manifest: WarpManifestState,
                     freshness: WarpFreshnessState, reason: String?, keyId: String? = nil,
                     rawManifestJSON: String? = nil,
                     declaredHtmlSha256: String? = nil, finalURL: String? = nil) -> WarpManifestResult {
            let conclusion = conclude(signature: signature, manifest: manifest, freshness: freshness,
                                      html: .notEvaluated, assets: [])
            return WarpManifestResult(originURL: originURL, finalURL: finalURL, keyId: keyId,
                signatureState: signature, manifestState: manifest, freshnessState: freshness,
                htmlState: .notEvaluated, declaredHtmlSha256: declaredHtmlSha256, fetchedHtmlSha256: nil,
                assets: [], conclusion: conclusion, inconclusiveReason: reason,
                rawManifestJSON: rawManifestJSON)
        }

        // §4.1 — parse the transport envelope { manifest_b64, signature_ed25519, key_id }.
        guard let env = (try? JSONSerialization.jsonObject(with: envelopeBytes)) as? [String: Any],
              let manifestB64 = env["manifest_b64"] as? String,
              let sigB64 = env["signature_ed25519"] as? String,
              let keyId = env["key_id"] as? String else {
            return stopped(signature: .malformed, manifest: .notEvaluated, freshness: .notEvaluated,
                           reason: "The manifest envelope could not be parsed.")
        }
        guard let manifestBytes = Data(base64Encoded: manifestB64) else {
            return stopped(signature: .malformed, manifest: .notEvaluated, freshness: .notEvaluated,
                           reason: "manifest_b64 is not valid base64.", keyId: keyId)
        }
        guard let sigBytes = Data(base64Encoded: sigB64) else {
            return stopped(signature: .malformed, manifest: .notEvaluated, freshness: .notEvaluated,
                           reason: "signature_ed25519 is not valid base64.", keyId: keyId)
        }

        // §4.2 — resolve key_id in the PINNED set. Unknown key → untrustedSigner (never /api/pubkey).
        guard let pubKeyRaw = pinnedKeys[keyId] else {
            return stopped(signature: .untrustedSigner, manifest: .notEvaluated, freshness: .notEvaluated,
                reason: "Manifest signed by key_id '\(keyId)', which is not in the pinned trust set.", keyId: keyId)
        }
        guard let publicKey = try? Curve25519.Signing.PublicKey(rawRepresentation: pubKeyRaw) else {
            return stopped(signature: .untrustedSigner, manifest: .notEvaluated, freshness: .notEvaluated,
                reason: "Pinned public key for key_id '\(keyId)' is unusable.", keyId: keyId)
        }

        // §4.3 — Ed25519-verify over the DECODED manifest bytes, BEFORE any JSON parse.
        guard publicKey.isValidSignature(sigBytes, for: manifestBytes) else {
            // A present-but-invalid signature is tampering evidence (§5), not merely inconclusive.
            return WarpManifestResult(originURL: originURL, finalURL: nil, keyId: keyId,
                signatureState: .invalidSignature, manifestState: .notEvaluated, freshnessState: .notEvaluated,
                htmlState: .notEvaluated, declaredHtmlSha256: nil, fetchedHtmlSha256: nil, assets: [],
                conclusion: .tamperingDetected, inconclusiveReason: nil)
        }

        // §4.4 — only now parse the (authenticated) manifest JSON.
        let rawManifestJSON = String(data: manifestBytes, encoding: .utf8)
        guard let manifest = (try? JSONSerialization.jsonObject(with: manifestBytes)) as? [String: Any] else {
            return stopped(signature: .verified, manifest: .malformed, freshness: .notEvaluated,
                reason: "Signature verified but the manifest JSON is unparseable.", keyId: keyId,
                rawManifestJSON: rawManifestJSON)
        }

        let manifestOrigin = manifest["origin_url"] as? String ?? ""
        let finalURL = manifest["final_url"] as? String
        let declaredHtmlSha = (manifest["html_sha256"] as? String)?.lowercased()
        let timestamp = uintValue(manifest["timestamp"])
        let freshnessSeconds = uintValue(manifest["freshness_seconds"]) ?? 0
        let policy = WarpRedirectPolicy.parse(manifest["redirect_policy"] as? String)
        let allowedRedirects = (manifest["allowed_redirects"] as? [Any])?.compactMap { $0 as? String } ?? []
        let manifestNonce = manifest["nonce"] as? String
        let declaredAssets = (manifest["assets"] as? [[String: Any]]) ?? []

        // §4.5 — freshness / origin / nonce (only meaningful on a verified, parsed manifest).
        let freshness = evaluateFreshness(nowMs: nowMs, timestamp: timestamp, freshnessSeconds: freshnessSeconds,
                                          manifestOrigin: manifestOrigin, requestedOrigin: originURL,
                                          clientNonce: nonce, manifestNonce: manifestNonce)
        if freshness != .fresh {
            // Any freshness failure is INCONCLUSIVE regardless of content — do not fetch, assets empty.
            return stopped(signature: .verified, manifest: .parsed, freshness: freshness,
                reason: freshnessReason(freshness), keyId: keyId, rawManifestJSON: rawManifestJSON,
                declaredHtmlSha256: declaredHtmlSha, finalURL: finalURL)
        }

        // §4.6 — fetch the origin HTML from the client vantage, hash, compare to html_sha256.
        var htmlState: WarpContentState = .notEvaluated
        var fetchedHtmlSha: String? = nil
        var localHtmlBytes: [UInt8]? = nil
        if let declaredHtmlSha {
            let req = WarpAssetRequest(url: manifestOrigin.isEmpty ? originURL : manifestOrigin,
                                       finalURL: finalURL, redirectPolicy: policy,
                                       allowedRedirects: allowedRedirects, assetClass: "HTML", isHTML: true)
            switch await fetch(req) {
            case .failed:
                htmlState = .fetchError
            case .fetched(let bytes, let terminal):
                if !redirectReconciles(policy: policy, requestURL: req.url, finalURL: finalURL, terminalURL: terminal) {
                    htmlState = .fetchError
                } else {
                    let sha = sha256Hex(bytes)
                    fetchedHtmlSha = sha
                    localHtmlBytes = bytes
                    htmlState = (sha == declaredHtmlSha) ? .match : .mismatch
                }
            }
        } else {
            htmlState = .fetchError   // manifest declared no HTML hash — cannot verify the page
        }

        // §4.6 — fetch every DECLARED asset, hash raw bytes, compare.
        var assetResults: [WarpAssetResult] = []
        for a in declaredAssets {
            let url = a["url"] as? String ?? ""
            let cls = a["class"] as? String ?? ""
            let declaredSha = (a["sha256"] as? String)?.lowercased()
            let assetFinal = a["final_url"] as? String
            let serverFetchFailed = (a["fetch_status"] as? String)?.lowercased() == "fetch_failed"

            // An asset the SERVER could not fetch (no sha256) is recorded, never dropped, and forces
            // INCONCLUSIVE — never a clean pass (§3).
            if serverFetchFailed || declaredSha == nil {
                assetResults.append(WarpAssetResult(url: url, declaredSha256: declaredSha,
                    fetchedSha256: nil, byteLen: nil, state: .fetchFailedDeclared, assetClass: cls))
                continue
            }

            let req = WarpAssetRequest(url: url, finalURL: assetFinal, redirectPolicy: policy,
                                       allowedRedirects: allowedRedirects, assetClass: cls, isHTML: false)
            switch await fetch(req) {
            case .failed:
                assetResults.append(WarpAssetResult(url: url, declaredSha256: declaredSha,
                    fetchedSha256: nil, byteLen: nil, state: .fetchError, assetClass: cls))
            case .fetched(let bytes, let terminal):
                if !redirectReconciles(policy: policy, requestURL: url, finalURL: assetFinal, terminalURL: terminal) {
                    assetResults.append(WarpAssetResult(url: url, declaredSha256: declaredSha,
                        fetchedSha256: nil, byteLen: bytes.count, state: .fetchError, assetClass: cls))
                } else {
                    let sha = sha256Hex(bytes)
                    let matches = (sha == declaredSha)
                    assetResults.append(WarpAssetResult(url: url, declaredSha256: declaredSha,
                        fetchedSha256: sha, byteLen: bytes.count,
                        state: matches ? .match : .mismatch, assetClass: cls,
                        // Retain bytes ONLY on a mismatch, for the overlay diff.
                        fetchedBytes: matches ? nil : bytes))
                }
            }
        }

        let conclusion = conclude(signature: .verified, manifest: .parsed, freshness: .fresh,
                                  html: htmlState, assets: assetResults.map { $0.state })
        let reason: String?
        switch conclusion {
        case .verifiedMatch, .tamperingDetected: reason = nil
        case .inconclusive:
            if htmlState == .fetchError { reason = "The origin page could not be fetched from this vantage." }
            else if assetResults.contains(where: { $0.state == .fetchError }) {
                reason = "One or more declared assets could not be fetched from this vantage."
            } else if assetResults.contains(where: { $0.state == .fetchFailedDeclared }) {
                reason = "The manifest declared an asset the server could not fetch; missing data is never a clean pass."
            } else { reason = "Content could not be conclusively verified." }
        }

        return WarpManifestResult(originURL: originURL, finalURL: finalURL, keyId: keyId,
            signatureState: .verified, manifestState: .parsed, freshnessState: .fresh, htmlState: htmlState,
            declaredHtmlSha256: declaredHtmlSha, fetchedHtmlSha256: fetchedHtmlSha, assets: assetResults,
            conclusion: conclusion, inconclusiveReason: reason,
            rawManifestJSON: rawManifestJSON, localHtmlBytes: localHtmlBytes)
    }

    // MARK: - Freshness + redirect helpers

    /// B085-style 300 000 ms clock-skew tolerance, matching WitnessSigner.
    static let skewToleranceMillis: UInt64 = 300_000

    static func evaluateFreshness(nowMs: UInt64, timestamp: UInt64?, freshnessSeconds: UInt64,
                                  manifestOrigin: String, requestedOrigin: String,
                                  clientNonce: String?, manifestNonce: String?) -> WarpFreshnessState {
        guard let timestamp else { return .expired }   // no timestamp = cannot establish freshness
        if nowMs > timestamp && (nowMs - timestamp) > freshnessSeconds * 1000 { return .expired }
        if timestamp > nowMs && (timestamp - nowMs) > skewToleranceMillis { return .futureDated }
        if manifestOrigin != requestedOrigin { return .originMismatch }
        if let clientNonce, manifestNonce != clientNonce { return .replayNonceMismatch }
        return .fresh
    }

    static func freshnessReason(_ f: WarpFreshnessState) -> String {
        switch f {
        case .expired: return "The manifest is past its freshness window (expired)."
        case .futureDated: return "The manifest timestamp is in the future beyond the allowed clock skew."
        case .originMismatch: return "The manifest origin does not match the requested origin."
        case .replayNonceMismatch: return "The manifest nonce does not echo the client nonce (possible replay)."
        case .fresh, .notEvaluated: return "The manifest freshness could not be confirmed."
        }
    }

    /// §6 terminal-URL reconciliation. `none` forbids any redirect (terminal must equal the request);
    /// `same-origin`/`list` require the terminal to equal the declared `final_url` (the closure has
    /// already enforced which hops were permitted). Any deviation → the caller marks the fetch failed.
    static func redirectReconciles(policy: WarpRedirectPolicy, requestURL: String,
                                   finalURL: String?, terminalURL: String) -> Bool {
        switch policy {
        case .none:
            return terminalURL == requestURL
        case .sameOrigin, .list:
            if let finalURL { return terminalURL == finalURL }
            return terminalURL == requestURL
        }
    }

    // MARK: - Small helpers

    static func sha256Hex(_ bytes: [UInt8]) -> String {
        SHA256.hash(data: Data(bytes)).map { String(format: "%02x", $0) }.joined()
    }

    private static func uintValue(_ any: Any?) -> UInt64? {
        if let n = any as? UInt64 { return n }
        if let n = any as? Int { return n >= 0 ? UInt64(n) : nil }
        if let n = any as? Double { return n >= 0 ? UInt64(n) : nil }
        if let n = any as? NSNumber { return n.uint64Value }
        if let s = any as? String { return UInt64(s) }
        return nil
    }

    // MARK: - §9 diff overlay (display only, never feeds the lattice)

    /// Fetch the server's byte-exact CLEAN copy of ONE asset for the side-by-side overlay via
    /// `GET /api/warp/raw?url=<abs>&raw=1` through the hardened DimensionClient path. Returns the
    /// raw bytes and the server-declared SHA-256 (`X-Syxx-Warp-Sha256`), or nil on any failure.
    /// This NEVER mutates bytes and NEVER contributes to the conclusion (§9).
    public static func fetchServerCleanAsset(url absoluteURL: String, client: DimensionClient) async -> (bytes: [UInt8], sha256: String?)? {
        await client.warpRawAsset(absoluteURL: absoluteURL)
    }

    // MARK: - Production wiring (fetch the envelope + verify with the SSRF-safe vantage fetch)

    /// End-to-end production check: pin the trust set out of band (§7), fetch the signed manifest
    /// envelope from the server, and verify it with the SSRF-safe client-vantage fetch. When no key
    /// is pinned, the signer is `untrustedSigner` → INCONCLUSIVE (never a match). When the server is
    /// unreachable, the result is INCONCLUSIVE (missing data is never a clean pass).
    public static func runVantageCheck(origin: String, client: DimensionClient,
                                       nonce: String? = nil,
                                       pinnedKeys: [String: Data]? = nil,
                                       nowMs: UInt64? = nil) async -> WarpManifestResult {
        let pinned = pinnedKeys ?? WarpManifestPinning.configuredTrustedManifestKeys()
        let now = nowMs ?? UInt64(Date().timeIntervalSince1970 * 1000)
        guard let envelope = await client.warpManifest(origin: origin, nonce: nonce) else {
            // No signed baseline could be retrieved — honest INCONCLUSIVE, never a silent pass.
            return WarpManifestResult(originURL: origin, finalURL: nil, keyId: nil,
                signatureState: .malformed, manifestState: .notEvaluated, freshnessState: .notEvaluated,
                htmlState: .notEvaluated, declaredHtmlSha256: nil, fetchedHtmlSha256: nil, assets: [],
                conclusion: .inconclusive,
                inconclusiveReason: "The signed manifest could not be retrieved from the server.")
        }
        return await verify(originURL: origin, envelopeBytes: envelope, pinnedKeys: pinned,
                            nonce: nonce, nowMs: now, fetch: vantageFetch)
    }

    // D023: short-timeout ephemeral session — hostile origins must not slow-loris the client.
    private static let vantageSession: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 10
        config.timeoutIntervalForResource = 15
        config.waitsForConnectivity = false
        return URLSession(configuration: config, delegate: nil, delegateQueue: nil)
    }()

    /// SSRF-safe client-vantage fetch that honors the §6 redirect policy. TLS SNI + certificate
    /// validation stay bound to the ORIGINAL HOSTNAME (the request URL keeps the hostname — the fix
    /// for the retired `pinnedRequest`, which rewrote the host to the IP literal and so validated the
    /// certificate against the IP). SSRF is enforced by resolving+classifying the host to a public IP
    /// before the fetch and re-validating each redirect hop. Fail closed: any non-2xx, any disallowed
    /// redirect, or any non-public host → `.failed`.
    static let vantageFetch: @Sendable (WarpAssetRequest) async -> WarpFetchOutcome = { req in
        guard let url = URL(string: req.url), let scheme = url.scheme?.lowercased(),
              scheme == "http" || scheme == "https", url.host != nil else {
            return .failed
        }
        guard await validatePublicHost(url) else { return .failed }

        let delegate = WarpRedirectController(policy: req.redirectPolicy, allowed: Set(req.allowedRedirects))
        let session = URLSession(configuration: vantageSession.configuration, delegate: delegate, delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        var request = URLRequest(url: url)   // hostname preserved → TLS SNI/cert bind to hostname.
        request.httpMethod = "GET"
        guard let (data, resp) = try? await session.data(for: request),
              let http = resp as? HTTPURLResponse, (200...299).contains(http.statusCode) else {
            return .failed
        }
        if delegate.blockedRedirect { return .failed }   // a policy-forbidden hop was attempted.
        let terminal = http.url?.absoluteString ?? req.url
        return .fetched(bytes: Array(data), terminalURL: terminal)
    }

    /// Resolve+classify `url`'s host to confirm it is a PUBLIC IP (SSRF gate). An IP literal is
    /// classified directly; a hostname is resolved through the system resolver and every address
    /// must be public. Fail closed.
    static func validatePublicHost(_ url: URL) async -> Bool {
        guard let host = url.host else { return false }
        let cls = IPClassifier.classify(host)
        if cls != .invalid { return cls == .publicRoutable }
        guard let resolver = NetworkKeyState.getSystemDNSServers().first else { return false }
        do {
            let a = try await DNSResolver.queryUDP(DNSQuery(name: host, type: 1, server: resolver))
            let aaaa = try await DNSResolver.queryUDP(DNSQuery(name: host, type: 28, server: resolver))
            let ips = (a.filter { $0.type == 1 } + aaaa.filter { $0.type == 28 }).map { $0.rdata }
            if ips.isEmpty { return false }
            for ip in ips where IPClassifier.classify(ip) != .publicRoutable { return false }
            return true
        } catch {
            return false
        }
    }
}

/// §6 redirect-policy enforcer for the client-vantage fetch. Blocks any hop the policy forbids
/// (fail closed); an attempted forbidden hop is recorded so the caller can mark the fetch failed.
final class WarpRedirectController: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
    private let policy: WarpRedirectPolicy
    private let allowed: Set<String>
    private(set) var blockedRedirect = false

    init(policy: WarpRedirectPolicy, allowed: Set<String>) {
        self.policy = policy
        self.allowed = allowed
    }

    func urlSession(_ session: URLSession, task: URLSessionTask,
                    willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest,
                    completionHandler: @escaping (URLRequest?) -> Void) {
        guard let from = task.originalRequest?.url ?? response.url, let to = request.url else {
            blockedRedirect = true; completionHandler(nil); return
        }
        let ok: Bool
        switch policy {
        case .none:
            ok = false   // no redirect permitted.
        case .sameOrigin:
            ok = from.scheme == to.scheme && from.host == to.host && (from.port ?? -1) == (to.port ?? -1)
        case .list:
            ok = allowed.contains(to.absoluteString)
        }
        if ok {
            completionHandler(request)
        } else {
            blockedRedirect = true
            completionHandler(nil)
        }
    }
}

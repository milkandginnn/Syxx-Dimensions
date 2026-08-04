import Foundation

// Client for the Dimension server's truth/diagnostics endpoints (ROADMAP Workstream S). Additive: this
// only *offers* server-confirmed ground truth to callers; it changes no existing result behaviour until
// something wires it in. Mirrors the LeakDetector collector pattern — ephemeral URLSession, the license
// key attached as `X-Syxx-License`, and an injectable transport so it is fully offline-testable.

/// D081: Distinguish operational failure from a genuine server response. Callers can tell
/// "server unreachable / bad data" from "server confirmed ground truth" without inspecting
/// nil-coalescing chains.
public enum DimensionOutcome<T: Sendable>: Sendable {
    case fetched(T)            // server responded with valid, decodable data
    case transportFailure      // network error, non-200, or nil request — could not reach server
    case decodeFailure         // server responded but data was undecodable (schema mismatch)
}

/// The zone's own authoritative answer for a name, from `/api/authoritative` — ground truth an
/// interfering resolver cannot fabricate. `delegation` is intentionally not decoded here.
public struct GroundTruth: Codable, Sendable {
    public struct Authoritative: Codable, Sendable {
        public let answers: [String]
        public let rcode: Int
        public let signed: Bool?     // zone published DNSKEY at the authoritative (nil = couldn't check)
        public init(answers: [String], rcode: Int, signed: Bool?) {
            self.answers = answers; self.rcode = rcode; self.signed = signed
        }
    }
    public let domain: String
    public let type: String
    public let result: String       // "authoritative" | "inconclusive"
    public let authoritative: Authoritative?

    /// Server returns `name`, client stores as `domain`.  C004. The `result` field keeps the
    /// byte-stable wire key "conclusion" so the server contract is unchanged. Twin: C# GroundTruth.Result.
    enum CodingKeys: String, CodingKey {
        case domain = "name"
        case type, authoritative
        case result = "conclusion"
    }
}

/// Assessment from `/api/diff` — the app's own answers vs. authoritative ground truth + a clean vantage.
public struct DiffAssessment: Codable, Sendable {
    public let domain: String
    public let type: String
    public let match: Bool?
    public let assessment: String    // "match" | "divergent" | "partial" | "inconclusive"
    public let reference: String     // "authoritative" | "cleanVantage" | "none"
}

public struct DimensionClient: Sendable {
    public typealias Transport = @Sendable (URLRequest) async -> (data: Data, status: Int)?

    public let baseURL: URL
    public let licenseKey: String?
    let transport: Transport

    private static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 8
        c.timeoutIntervalForResource = 12
        c.waitsForConnectivity = false
        return URLSession(configuration: c)
    }()

    public init(baseURL: URL, licenseKey: String? = nil, transport: Transport? = nil) {
        self.baseURL = baseURL
        self.licenseKey = licenseKey
        self.transport = transport ?? { req in
            guard let (data, resp) = try? await DimensionClient.session.data(for: req),
                  let http = resp as? HTTPURLResponse else { return nil }
            return (data, http.statusCode)
        }
    }

    private func request(_ path: String, query: [URLQueryItem] = [], method: String = "GET",
                         body: Data? = nil) -> URLRequest? {
        guard var comps = URLComponents(url: baseURL.appendingPathComponent(path),
                                        resolvingAgainstBaseURL: false) else { return nil }
        if !query.isEmpty { comps.queryItems = query }
        guard let url = comps.url else { return nil }
        var req = URLRequest(url: url)
        req.httpMethod = method
        // D027: reject non-https before attaching credentials to prevent license key leakage
        // in cleartext over http://.
        if let k = licenseKey, !k.isEmpty {
            guard url.scheme?.lowercased() == "https" else { return nil }
            req.setValue(k, forHTTPHeaderField: "X-Syxx-License")
        }
        if let body { req.httpBody = body; req.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        return req
    }

    // D081: Returns DimensionOutcome to distinguish "transport failed" from "decode failed"
    // from "successfully fetched." Callers MUST handle all three cases.
    private func get<T: Decodable>(_ type: T.Type, _ req: URLRequest?) async -> DimensionOutcome<T> {
        guard let req else { return .transportFailure }
        guard let (data, status) = await transport(req) else { return .transportFailure }
        guard status == 200 else { return .transportFailure }
        guard let decoded = try? JSONDecoder().decode(T.self, from: data) else { return .decodeFailure }
        return .fetched(decoded)
    }

    /// Server-confirmed authoritative answer for `domain`/`qtype` (root→authoritative walk).
    /// Canonicalizes the domain before sending (C004).
    public func authoritative(domain: String, type: String = "A") async -> DimensionOutcome<GroundTruth> {
        guard let canonical = DomainCanonical.canonicalizeOrNil(domain) else { return .decodeFailure }
        return await get(GroundTruth.self, request("api/authoritative",
                  query: [.init(name: "domain", value: canonical), .init(name: "type", value: type)]))
    }

    /// Ask the server to diff the app's own `localAnswers` against ground truth + a clean vantage.
    /// Canonicalizes the domain before sending (C004).
    public func diff(domain: String, type: String = "A", localAnswers: [String]) async -> DimensionOutcome<DiffAssessment> {
        guard let canonical = DomainCanonical.canonicalizeOrNil(domain) else { return .decodeFailure }
        let payload = ["domain": canonical, "type": type, "localAnswers": localAnswers] as [String: Any]
        guard let body = try? JSONSerialization.data(withJSONObject: payload) else { return .decodeFailure }
        return await get(DiffAssessment.self, request("api/diff", method: "POST", body: body))
    }

    // MARK: - WARP_MANIFEST_V1 transport (spec §2 + §9)

    /// §2 — fetch the signed manifest envelope for `origin`: `GET /api/warp/manifest?url=<origin>
    /// [&nonce=<nonce>]` (Pro-gated identically to the existing warp routes). Returns the raw envelope
    /// bytes (the `{manifest_b64, signature_ed25519, key_id}` JSON) on HTTP 200, else nil. The license
    /// key is attached only over HTTPS (via `request(_:)`'s D027 gate).
    public func warpManifest(origin: String, nonce: String? = nil) async -> Data? {
        var query = [URLQueryItem(name: "url", value: origin)]
        if let nonce, !nonce.isEmpty { query.append(URLQueryItem(name: "nonce", value: nonce)) }
        guard let req = request("api/warp/manifest", query: query) else { return nil }
        guard let (data, status) = await transport(req), status == 200 else { return nil }
        return data
    }

    /// §9 — fetch the server's byte-exact CLEAN copy of ONE asset for the diff overlay:
    /// `GET /api/warp/raw?url=<abs>&raw=1` (byte-exact mode returning `application/octet-stream` +
    /// `X-Syxx-Warp-Sha256`). Returns the raw bytes and the server-declared SHA-256 (display only —
    /// never re-hashed as client input, never feeds the lattice). The license key is attached only
    /// over HTTPS. Uses the response headers directly, so it does not go through the JSON transport.
    public func warpRawAsset(absoluteURL: String) async -> (bytes: [UInt8], sha256: String?)? {
        guard let req = request("api/warp/raw",
                                query: [URLQueryItem(name: "url", value: absoluteURL),
                                        URLQueryItem(name: "raw", value: "1")]) else { return nil }
        guard let (data, resp) = try? await DimensionClient.session.data(for: req),
              let http = resp as? HTTPURLResponse, http.statusCode == 200 else { return nil }
        let sha = http.value(forHTTPHeaderField: "X-Syxx-Warp-Sha256")
        return (Array(data), sha)
    }
}

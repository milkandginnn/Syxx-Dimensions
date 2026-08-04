import CryptoKit
import Foundation
import Security

/// Collector TLS-transport pinning posture for a leak test (D003).
///
/// This describes the *transport identity* check on the collector's HTTPS
/// endpoint — distinct from the Ed25519 attestation-signature check
/// (``LeakTestResult/signatureValid``).
public enum CollectorTransportPinning: String, Sendable {
    /// SPKI pins were configured and the collector's leaf certificate matched one.
    case enforced
    /// No SPKI pins were configured; only standard system TLS validation was applied.
    case notConfigured
    /// SPKI pins were configured but the collector's leaf certificate did NOT match
    /// (or the chain failed PKI validation). The transport was refused, fail-closed.
    case failed
}

/// The collector-verified outcome of a DNS leak test.
public enum LeakResult: String, Sendable {
    /// The collector confirmed at least one query reached an unexpected resolver.
    case confirmed
    /// The collector observed queries and confirmed none reached an unexpected resolver.
    case notDetected
    /// The test finished, but evidence is indeterminate / ambiguous.
    case inconclusive
    /// The test returned logically impossible or domain-violating data.
    case invalid
    /// An execution error or network failure prevented the test from completing.
    case error
}

/// Which collector request could not be completed. The step matters to honesty: a failure at
/// `.token` means no DNS query was ever dispatched, while a failure at `.verify` means the
/// queries WERE sent but their result could not be collected. Neither is evidence of no leak.
public enum CollectorStep: String, Sendable {
    /// `POST /api/leak/token` — before any DNS query is dispatched.
    case token
    /// `GET /api/leak/verify` — after the DNS queries were dispatched.
    case verify
}

/// Why a collector-confirmed leak test produced no evidence.
///
/// The collector's HTTP status is a DISTINCT state, not noise: "you are not licensed" (402),
/// "you asked too often" (429), "your credentials were rejected" (401/403) and "the collector
/// fell over" (5xx) demand different user outcomes. Collapsing them into one opaque error — as
/// this client did before — renders a working paywall as a broken product. The raw status is
/// preserved on every case so nothing has to be guessed downstream.
///
/// This is never a leak/no-leak signal. Every case means the same thing about DNS: nothing was
/// observed, therefore nothing can be concluded. Absence of observation is not evidence of absence.
public enum CollectorFailure: Sendable, Equatable {
    /// HTTP 402 — the collector requires a Syxx Pro entitlement for this route. Nothing broke;
    /// the feature is gated. This is a correct, expected answer from a correctly configured server.
    case entitlementRequired(status: Int)
    /// HTTP 401 / 403 — the collector rejected the supplied credentials.
    case authenticationFailed(status: Int)
    /// HTTP 429 — the collector is rate limiting this caller.
    case rateLimited(status: Int)
    /// HTTP 5xx — the collector reported a server-side error.
    case serverError(status: Int)
    /// Any other non-200 status. The code is carried verbatim; no cause is inferred from it,
    /// because none has been established.
    case unexpectedStatus(Int)
    /// The collector answered 200 but the body could not be decoded, or the response was not HTTP.
    case malformedResponse
    /// D003 — the collector's TLS identity could not be verified (SPKI pin / PKI failure).
    case collectorIdentityUnverified
    /// The request never reached the collector (timeout, offline, DNS failure, connection reset).
    case transportFailure

    /// The HTTP status the collector returned, when the failure was an HTTP response at all.
    /// `nil` for transport, identity and decode failures, where no status exists to report.
    public var httpStatus: Int? {
        switch self {
        case .entitlementRequired(let s), .authenticationFailed(let s),
             .rateLimited(let s), .serverError(let s):
            return s
        case .unexpectedStatus(let s):
            return s
        case .malformedResponse, .collectorIdentityUnverified, .transportFailure:
            return nil
        }
    }

    /// True only for HTTP 402: the request was refused because the caller lacks a Syxx Pro
    /// entitlement. Surfaces should offer an upgrade path here, NOT an error state — nothing
    /// about the product failed.
    public var requiresEntitlement: Bool {
        if case .entitlementRequired = self { return true }
        return false
    }

    // MARK: - User-visible copy

    /// Plain-English reason for this failure at `step`.
    ///
    /// PENDING OWNER APPROVAL (proposed 2026-07-29) — every sentence below reaches the screen and
    /// the CLI. The one exception is ``CollectorPinning/pinFailureReason``, which the owner
    /// approved on 2026-07-11 and which is reused verbatim.
    ///
    /// Two rules hold every string here:
    /// 1. No cause is asserted that the code has not established. An unrecognised status reports
    ///    the status, not a guess about what it meant.
    /// 2. Every sentence closes the honesty gap the status opens: the reader is told that no leak
    ///    evidence exists, so no case can be misread as "no leak detected".
    public func reason(step: CollectorStep) -> String {
        if case .collectorIdentityUnverified = self {
            // Approved 2026-07-11; already self-contained, so it takes no step suffix.
            return CollectorPinning.pinFailureReason
        }
        return "\(cause) \(Self.consequence(step: step))"
    }

    /// The half of the sentence that states WHAT the collector did.
    private var cause: String {
        switch self {
        case .entitlementRequired:
            return "A Syxx Pro license is required for the collector-confirmed leak test."
        case .authenticationFailed(let status):
            return "The collector rejected the credentials supplied (HTTP \(status))."
        case .rateLimited(let status):
            return "The collector is rate limiting requests from this device (HTTP \(status))."
        case .serverError(let status):
            return "The collector reported a server error (HTTP \(status))."
        case .unexpectedStatus(let status):
            // Deliberately states the status and nothing else: the code has established no cause.
            return "The collector request failed with HTTP status \(status)."
        case .malformedResponse:
            return "The collector's response could not be read."
        case .transportFailure:
            return "The collector could not be reached."
        case .collectorIdentityUnverified:
            return CollectorPinning.pinFailureReason
        }
    }

    /// The half of the sentence that states what it means for the evidence. This is the part that
    /// stops any failure from reading as a clean result.
    private static func consequence(step: CollectorStep) -> String {
        switch step {
        case .token:
            return "No leak test was performed, so nothing is known about your DNS resolvers."
        case .verify:
            return "The test queries were sent but their result could not be collected, so nothing is known about your DNS resolvers."
        }
    }
}

/// Full result returned by ``LeakDetector/runCollectorTest(baseDomain:servers:collectorBaseURL:authToken:licenseKey:pollDelay:pinSet:trustedPublicKeys:)``.
public struct LeakTestResult: Sendable {
    public let result: LeakResult
    /// Resolver IPs the collector recorded as having forwarded the test query.
    public let observedResolvers: [String]
    /// Ed25519 signature produced by the collector — tamper-evident evidence.
    /// `nil` when the collector was not reachable or did not return a signature.
    public let signature: String?
    /// The collector's public key (base64 DER SubjectPublicKeyInfo), present when
    /// `signature` is present.  Embed or fetch this to verify offline.
    public let publicKey: String?
    /// Opaque key identifier (first 8 bytes of public key, base64-encoded).
    public let keyId: String?
    /// The canonical JSON bytes the collector signed (base64-encoded).  The
    /// verifier uses these bytes directly so no field-by-field reconstruction
    /// is needed — eliminating a common class of cross-platform serialization
    /// mismatches.
    public let signedPayload: String?
    /// Whether the signature (if present) passed verification with the supplied key.
    /// `nil` when no signature is present or no key was supplied for verification.
    public let signatureValid: Bool?
    public let errorReason: String?
    /// Collector TLS-transport pinning posture for this run (D003). A `.failed`
    /// value means the collector's identity could not be verified, so the result
    /// is forced to `.inconclusive` — a leak/no-leak result cannot be trusted.
    public let transportPinning: CollectorTransportPinning
    /// D011: whether the signing public key was present in the configured trust set.
    /// `nil` — no trust set was configured (transparently untracked),
    /// `true`  — the signer's key is a member of the trust set,
    /// `false` — the signer's key is NOT in the trust set (signature is crypto-valid
    ///           but the signer is not trusted). Never `true` when `signatureValid` is
    ///           not also `true`.
    public let signatureKeyTrusted: Bool?
    /// Why the collector produced no evidence, when it produced none. `nil` when the collector
    /// answered the request (whatever the leak outcome was), and `nil` when the run never
    /// reached the collector at all (no resolvers configured, every DNS dispatch failed).
    ///
    /// Read this INSTEAD OF pattern-matching ``errorReason``: it is the structured, testable form
    /// of the same fact, and it is the only thing that lets a surface tell "requires Syxx Pro"
    /// apart from "the collector fell over". Never a leak/no-leak signal in either case.
    public let collectorFailure: CollectorFailure?

    public init(result: LeakResult,
                observedResolvers: [String] = [],
                signature: String? = nil,
                publicKey: String? = nil,
                keyId: String? = nil,
                signedPayload: String? = nil,
                signatureValid: Bool? = nil,
                errorReason: String? = nil,
                transportPinning: CollectorTransportPinning = .notConfigured,
                signatureKeyTrusted: Bool? = nil,
                collectorFailure: CollectorFailure? = nil) {
        self.result = result
        self.observedResolvers = observedResolvers
        self.signature = signature
        self.publicKey = publicKey
        self.keyId = keyId
        self.signedPayload = signedPayload
        self.signatureValid = signatureValid
        self.errorReason = errorReason
        self.transportPinning = transportPinning
        self.signatureKeyTrusted = signatureKeyTrusted
        self.collectorFailure = collectorFailure
    }
}

/// DNS Leak Detector.
///
/// Generates unique one-time subdomains, dispatches them through the system's
/// DNS resolvers, and — when a collector URL is provided — calls the server's
/// signed-token API to obtain cryptographically attested evidence of which
/// upstream resolvers forwarded the query.
public enum LeakDetector {

    // MARK: - URLSession for collector API calls (SPKI-pinned, D003)

    /// Builds an ephemeral URLSession whose server-trust challenges are gated by
    /// `pins` (base64 SHA-256 of the leaf certificate's DER SubjectPublicKeyInfo).
    /// An empty `pins` set means standard system TLS validation only (unpinned).
    /// A configured-but-non-matching leaf is refused fail-closed.
    static func makeCollectorSession(pins: Set<String>) -> URLSession {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = 8.0
        cfg.timeoutIntervalForResource = 10.0
        cfg.requestCachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        let delegate = CollectorPinningDelegate(pins: pins)
        return URLSession(configuration: cfg, delegate: delegate, delegateQueue: nil)
    }

    // MARK: - Public API

    /// Generates a randomized high-entropy test token subdomain.
    public static func generateTestSubdomain(baseDomain: String = "leak.syxx.app") -> String {
        let uuid = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        return "id-\(uuid).\(baseDomain)"
    }

    /// Performs a complete collector-confirmed leak test.
    ///
    /// 1. Requests a signed one-time token from `collectorBaseURL/api/leak/token`.
    /// 2. Dispatches DNS queries for the returned subdomain through `servers`.
    /// 3. Waits `pollDelay` for the authoritative server to record observations.
    /// 4. Verifies via `collectorBaseURL/api/leak/verify` and returns a signed result.
    ///
    /// A network failure at any step returns `.inconclusive`, never `.notDetected`.
    ///
    /// Authentication: pass a session `authToken` (sent as `Authorization: Bearer`),
    /// a `licenseKey` (sent as `X-Syxx-License`), or both — the collector accepts either.
    ///
    /// D003: the collector TLS transport is SPKI-pinned when `pinSet` (or the configured
    /// pins) is non-empty; a pin/PKI failure resolves to `.inconclusive` with
    /// `transportPinning == .failed`, never a trusted leak/no-leak result.
    ///
    /// D011: when `trustedPublicKeys` is non-empty, a crypto-valid signature from a key
    /// NOT in the set is reported as `signatureValid==true` + `signatureKeyTrusted==false`
    /// — the signature is genuine but the signer is not trusted, which is distinct from a
    /// forged/invalid signature. When `trustedPublicKeys` is nil or empty, no trust check
    /// is performed and `signatureKeyTrusted` remains nil.
    public static func runCollectorTest(
        baseDomain: String = "leak.syxx.app",
        servers: [String],
        collectorBaseURL: URL,
        authToken: String? = nil,
        licenseKey: String? = nil,
        pollDelay: Duration = .seconds(3),
        pinSet: Set<String>? = nil,
        trustedPublicKeys: Set<String>? = nil,
        expectedResolvers: Set<String>? = nil
    ) async -> LeakTestResult {
        // D003 — collector TLS transport pinning. `enforced` when pins are configured,
        // `notConfigured` (transparently unpinned) otherwise. A pin/PKI failure during a
        // request downgrades this to `.failed` and the result to `.inconclusive`.
        let pins = pinSet ?? CollectorPinning.configuredPins()
        // WS3-A.3: the signer trust set engages even when the caller (GUI/CLI) passes nothing,
        // exactly like the transport pins above — otherwise D011 is inert in the shipped app.
        let trustedKeys = trustedPublicKeys ?? CollectorPinning.configuredTrustedSigningKeys()
        let posture: CollectorTransportPinning = pins.isEmpty ? .notConfigured : .enforced

        guard !servers.isEmpty else {
            return LeakTestResult(result: .error,
                                  errorReason: "No system DNS resolvers detected.",
                                  transportPinning: posture)
        }

        let session = makeCollectorSession(pins: pins)
        defer { session.finishTasksAndInvalidate() }

        // Step 1 — obtain signed token from collector.
        var tokenLabel: String
        var subdomain: String
        do {
            let result = try await requestToken(session: session,
                                                collectorBaseURL: collectorBaseURL,
                                                baseDomain: baseDomain,
                                                authToken: authToken,
                                                licenseKey: licenseKey)
            tokenLabel = result.token
            subdomain  = result.subdomain
        } catch {
            SyxxDiagnostics.breadcrumb(category: "leak.token", message: "Leak token request failed", level: .warning, data: ["error": "\(error)"])
            // Distinct states stay distinct. Previously every non-200 collapsed here into one
            // `.error` carrying a Foundation bridge dump, so a working paywall (402) read exactly
            // like a collapsed collector (5xx). The status the throw site captured is now carried
            // through to the caller instead of being discarded.
            let failure = classify(error)
            return LeakTestResult(result: result(for: failure),
                                  errorReason: failure.reason(step: .token),
                                  transportPinning: failure == .collectorIdentityUnverified ? .failed : posture,
                                  collectorFailure: failure)
        }

        // Step 2 — trigger DNS queries through system resolvers.
        let dispatchResults = await triggerQueries(subdomain: subdomain, servers: servers)
        // KTRU-17: if every dispatch failed, don't proceed to verify — execution error.
        if dispatchResults.values.allSatisfy({ !$0 }) {
            return LeakTestResult(result: .error,
                                  errorReason: "All DNS dispatch probes failed; no queries were sent to the authoritative server.",
                                  transportPinning: posture)
        }

        // Step 3 — give the authoritative server time to record observations.
        try? await Task.sleep(for: pollDelay)

        // Step 4 — verify with collector and return signed attestation.
        do {
            let result = try await verifyToken(session: session,
                                               collectorBaseURL: collectorBaseURL,
                                               token: tokenLabel,
                                               authToken: authToken,
                                               licenseKey: licenseKey)

            // B091: derive result exclusively from the signed payload, not the unsigned
            // outer `result.leaked` field. If the signed payload is missing or unparseable,
            // the result is inconclusive — no trustworthy leak/no-leak claim is possible.
            let signedLeaked = extractSignedLeaked(from: result.signedPayload)
            // B180: derive observedResolvers from the signed payload, not the unsigned outer
            // field. A relay that preserves the signedPayload/signature but rewrites the outer
            // observedResolvers would otherwise yield a valid signature beside a forged list.
            let signedResolvers = extractSignedObservedResolvers(from: result.signedPayload)
                ?? result.observedResolvers

            // Compute signature / trust-set state once, shared across all return paths.
            let sigValid: Bool?
            let sigKeyTrusted: Bool?
            if let sig = result.signature, let pub = result.publicKey, !sig.isEmpty, !pub.isEmpty {
                let tempBuilt = LeakTestResult(result: .inconclusive,
                                               observedResolvers: result.observedResolvers,
                                               signature: sig,
                                               publicKey: pub,
                                               keyId: result.keyId,
                                               signedPayload: result.signedPayload)
                sigValid = verify(tempBuilt)
                sigKeyTrusted = sigValid == true
                    ? checkTrustSet(publicKey: pub, trustedPublicKeys: trustedKeys)
                    : nil
            } else {
                sigValid = nil
                sigKeyTrusted = nil
            }

            // Parity with Windows LeakDetector.DeriveResult: a signature that FAILS verification
            // is a forged or corrupted attestation and can support no result at all. Without
            // this gate the forged payload flowed into reconcileResult and could return
            // .notDetected, a false clean rendered green in GluttonView.
            if sigValid == false {
                return LeakTestResult(result: .inconclusive,
                                      observedResolvers: signedResolvers,
                                      signature: result.signature,
                                      publicKey: result.publicKey,
                                      keyId: result.keyId,
                                      signedPayload: result.signedPayload,
                                      signatureValid: sigValid,
                                      errorReason: "Collector attestation signature is invalid; result cannot be trusted.",
                                      transportPinning: posture,
                                      signatureKeyTrusted: sigKeyTrusted)
            }

            guard let leaked = signedLeaked else {
                return LeakTestResult(result: .inconclusive,
                                      observedResolvers: signedResolvers,
                                      signature: result.signature,
                                      publicKey: result.publicKey,
                                      keyId: result.keyId,
                                      signedPayload: result.signedPayload,
                                      signatureValid: sigValid,
                                      errorReason: "Signed payload is missing or malformed; cannot derive a verified leak result.",
                                      transportPinning: posture,
                                      signatureKeyTrusted: sigKeyTrusted)
            }

            // C026: pass dispatch awareness so reconcileResult can distinguish "no leak"
            // from "queries dispatched but collector observed nothing."
            let anyDispatched = dispatchResults.values.contains(where: { $0 })
            // B180: feed reconcileResult from the SIGNED resolver list, not the unsigned outer.
            let reconciled = reconcileResult(leaked: leaked,
                                             observedResolvers: signedResolvers,
                                             expectedResolvers: expectedResolvers,
                                             probesDispatched: anyDispatched)

            // D004: a valid signature from an UNTRUSTED key (not in the trust set) cannot
            // support a result. The signature is cryptographically valid but the signer
            // is not recognized — this is the trust-anchor gap D011 describes. Without this
            // gate, a MITM with their own Ed25519 key could forge a trusted-looking result.
            if sigKeyTrusted == false {
                return LeakTestResult(result: .inconclusive,
                                      observedResolvers: signedResolvers,
                                      signature: result.signature,
                                      publicKey: result.publicKey,
                                      keyId: result.keyId,
                                      signedPayload: result.signedPayload,
                                      signatureValid: sigValid,
                                      errorReason: "Collector signature is valid but the signing key is not trusted; result cannot be verified.",
                                      transportPinning: posture,
                                      signatureKeyTrusted: sigKeyTrusted)
            }

            // D003: when pins are not configured, the transport is unpinned (standard TLS only).
            // A MITM with a valid CA-issued certificate could forge the collector's response.
            // The result is returned but marked as untrusted via transportPinning: .notConfigured.
            // For security-sensitive runs, callers should check transportPinning and treat
            // .notConfigured as inconclusive. The caveat is surfaced in errorReason.
            let finalReason: String
            if posture == .notConfigured {
                finalReason = "\(reconciled.reason ?? "") [\(CollectorPinning.unconfiguredCaveat)]"
            } else {
                finalReason = reconciled.reason ?? ""
            }

            return LeakTestResult(result: reconciled.result,
                                  observedResolvers: signedResolvers,
                                  signature: result.signature,
                                  publicKey: result.publicKey,
                                  keyId: result.keyId,
                                  signedPayload: result.signedPayload,
                                  signatureValid: sigValid,
                                  errorReason: finalReason,
                                  transportPinning: posture,
                                  signatureKeyTrusted: sigKeyTrusted)
        } catch {
            SyxxDiagnostics.breadcrumb(category: "leak.verify", message: "Leak verify request failed", level: .warning, data: ["error": "\(error)"])
            // Identical collapse to the token step, fixed identically. The `.verify` step means
            // the DNS queries WERE dispatched, so its copy says so rather than claiming no test ran.
            let failure = classify(error)
            return LeakTestResult(result: result(for: failure),
                                  errorReason: failure.reason(step: .verify),
                                  transportPinning: failure == .collectorIdentityUnverified ? .failed : posture,
                                  collectorFailure: failure)
        }
    }

    /// A cancelled URLError from the pinned collector session means the server-trust
    /// challenge was refused (bad PKI chain or SPKI-pin mismatch) — the only reason this
    /// session ever cancels. Treat it as a transport-identity failure, never a network hiccup.
    static func isTransportIdentityFailure(_ error: Error) -> Bool {
        (error as? URLError)?.code == .cancelled
    }

    // MARK: - Signature verification

    /// Verify a collector's Ed25519-signed leak attestation.
    ///
    /// Returns `true` when the signature is cryptographically valid for the
    /// attested payload.  Always returns `false` (never crashes) on malformed
    /// base64, bad public-key DER, or a nil/missing signature — consistent with
    /// the tri-state / inconclusive ethos.
    public static func verify(_ result: LeakTestResult) -> Bool {
        guard let sigB64 = result.signature,
              let pubB64 = result.publicKey,
              let payloadB64 = result.signedPayload,
              let sigRaw = Data(base64Encoded: normalizeBase64(sigB64)),
              let pubRaw = Data(base64Encoded: pubB64),
              let payloadData = Data(base64Encoded: payloadB64)
        else { return false }
        do {
            let pubKey = try Curve25519.Signing.PublicKey(rawRepresentation: pubRaw)
            return pubKey.isValidSignature(sigRaw, for: payloadData)
        } catch {
            return false
        }
    }

    /// D057: normalize url-safe/unpadded base64 to standard padded form so a
    /// collector using either alphabet can be verified without fabricating a fail.
    private static func normalizeBase64(_ s: String) -> String {
        let r = s.replacingOccurrences(of: "-", with: "+")
                 .replacingOccurrences(of: "_", with: "/")
        let pad = (4 - r.count % 4) % 4
        return r + String(repeating: "=", count: pad)
    }

    // MARK: - Result reconciliation
    /// The two must agree, or neither can be stood behind: a "leak" the collector
    /// cannot attribute to any resolver, or a "no leak" that nonetheless attests
    /// observed resolvers, is internally inconsistent and downgrades to `.inconclusive`
    /// rather than emitting a leak/no-leak the evidence does not support.
    /// Pure and offline-testable (no network).
    ///
    /// C026: `probesDispatched` indicates whether DNS queries were actually sent. When probes
    /// were dispatched but the collector recorded zero observations, the result is inconclusive
    /// (not clean) — the queries may have been blackholed by censorship or a hostile network.
    public static func reconcileResult(leaked: Bool,
                                        observedResolvers: [String],
                                  expectedResolvers: Set<String>? = nil,
                                  probesDispatched: Bool = false) -> (result: LeakResult, reason: String?) {
        if leaked && observedResolvers.isEmpty {
            return (.invalid, "Collector reported a leak but attested no resolver; impossible result.")
        }
        if !leaked && !observedResolvers.isEmpty {
            return (.invalid, "Collector reported no leak yet attested observed resolvers; impossible result.")
        }
        // C031: when the caller provides a baseline of expected resolver egress IPs,
        // every observed resolver that matches the baseline is NOT a leak — it is the
        // VPN/encrypted DNS provider the user intended to use.
        if leaked, let expected = expectedResolvers, !expected.isEmpty {
            let unexpected = observedResolvers.filter { !expected.contains($0) }
            if unexpected.isEmpty {
                return (.notDetected, "All observed resolvers match your expected provider baseline.")
            }
            return (.confirmed, "\(unexpected.count) resolver(s) outside your expected baseline: \(unexpected.joined(separator: ", "))")
        }
        // C026: probes were dispatched but the collector observed zero — cannot distinguish
        // "no leak" from "queries never arrived" (blackhole, censorship, hostile resolver).
        // Force inconclusive instead of a false-clean "no leak detected."
        if !leaked && observedResolvers.isEmpty && probesDispatched {
            return (.inconclusive, "Queries were dispatched but the collector recorded no observations — possible blackhole or censorship.")
        }
        // C031: without an expected-resolver policy, a "leak confirmed" is meaningless — every
        // working recursive resolver produces observations. Return the observations as evidence
        // but mark the result inconclusive rather than asserting a leak the protocol cannot
        // actually distinguish from normal DNS recursion.
        if leaked && (expectedResolvers == nil || expectedResolvers!.isEmpty) {
            let resolverList = observedResolvers.isEmpty ? "unknown" : observedResolvers.joined(separator: ", ")
            return (.inconclusive, "Collector observed queries from: \(resolverList). No expected-resolver baseline configured to confirm a leak — provide expected resolvers for a definitive result.")
        }
        return (leaked ? .confirmed : .notDetected, nil)
    }

    // MARK: - Internal helpers

    /// Trigger queries for leak testing.
    public static func triggerQueries(subdomain: String, servers: [String]) async -> [String: Bool] {
        var results: [String: Bool] = [:]
        for server in servers {
            do {
                let query = DNSQuery(name: subdomain, type: 1, server: server)
                _ = try await DNSResolver.queryUDP(query)
                results[server] = true
            } catch {
                results[server] = false
            }
        }
        return results
    }

    // MARK: - Collector HTTP calls

    private struct TokenResponse: Decodable {
        let token: String
        let subdomain: String
    }

    private struct VerifyResponse: Decodable {
        let leaked: Bool?
        let observedResolvers: [String]
        let signature: String?
        let publicKey: String?
        let keyId: String?
        let signedPayload: String?
        let inconclusive: Bool?
    }

    /// A collector request that did not yield a usable body.
    ///
    /// Conforms to `LocalizedError` DELIBERATELY. Without it, `localizedDescription` on these
    /// cases produced the Foundation NSError bridge template — "The operation couldn't be
    /// completed. (SyxxKit.LeakDetector.(unknown context at $100fbc78c).CollectorError error 0.)"
    /// — which shipped to a user's screen. That string names no cause, leaks internal type
    /// structure and a memory address, and hid the HTTP status the enum was already carrying.
    enum CollectorError: Error, LocalizedError, Equatable {
        /// The collector answered with a non-200 HTTP status. The status is preserved so the
        /// caller can classify it; it must never be discarded at the catch boundary again.
        case badStatus(Int)
        /// The collector answered 200 but the body did not decode.
        case badJSON(Error)
        /// The response was not an HTTP response at all (no status exists to report).
        case notHTTP

        var errorDescription: String? {
            switch self {
            case .badStatus(let code):
                // States the status, asserts no cause. Classification into paywall / rate limit /
                // server error happens in `CollectorFailure.classify(status:)`, not here.
                return "The collector request failed with HTTP status \(code)."
            case .badJSON:
                return "The collector's response could not be read."
            case .notHTTP:
                return "The collector returned a response that was not HTTP."
            }
        }

        static func == (lhs: CollectorError, rhs: CollectorError) -> Bool {
            switch (lhs, rhs) {
            case (.badStatus(let a), .badStatus(let b)): return a == b
            case (.badJSON, .badJSON): return true
            case (.notHTTP, .notHTTP): return true
            default: return false
            }
        }
    }

    // MARK: - Collector failure classification

    /// Classify a non-200 collector status into a distinct outcome.
    ///
    /// Pure and offline-testable. 402 is the intended paywall on `/api/leak/token` (Server
    /// `app.py:1583` → `585-588`, `config.ENFORCE_WEB` at `config.py:314`); it is a CORRECT
    /// answer from a correctly configured collector, not a defect, and it must not be reported
    /// as one. Anything unrecognised keeps its status verbatim rather than being guessed at.
    /// Twin of Windows `LeakDetector.ClassifyStatus`.
    public static func classifyStatus(_ status: Int) -> CollectorFailure {
        switch status {
        case 402: return .entitlementRequired(status: status)
        case 401, 403: return .authenticationFailed(status: status)
        case 429: return .rateLimited(status: status)
        case 500...599: return .serverError(status: status)
        default: return .unexpectedStatus(status)
        }
    }

    /// Classify a thrown collector error into a distinct outcome.
    ///
    /// Order matters: the transport-identity check runs FIRST so a pin/PKI refusal can never be
    /// downgraded to a generic transport hiccup. Anything not recognised resolves to
    /// `.transportFailure` — the request did not come back — which asserts no cause it has not
    /// established. Twin of Windows `LeakDetector.Classify`.
    static func classify(_ error: Error) -> CollectorFailure {
        if isTransportIdentityFailure(error) { return .collectorIdentityUnverified }
        if let collectorError = error as? CollectorError {
            switch collectorError {
            case .badStatus(let status): return classifyStatus(status)
            case .badJSON, .notHTTP: return .malformedResponse
            }
        }
        return .transportFailure
    }

    /// The result a collector failure resolves to.
    ///
    /// `.inconclusive` for an entitlement gate: nothing broke, the feature is simply gated, so
    /// rendering it as an execution error would tell the user their product is faulty when it is
    /// not. `.error` for everything else, which genuinely did prevent the test from completing.
    /// NEVER `.notDetected` or `.confirmed` in either case — no query was observed, so no leak
    /// claim of any kind is available. Twin of Windows `LeakDetector.ResultFor`.
    public static func result(for failure: CollectorFailure) -> LeakResult {
        failure.requiresEntitlement ? .inconclusive : .error
    }

    /// Applies whichever credentials the caller supplied: a session token becomes an
    /// `Authorization: Bearer` header, a license key becomes `X-Syxx-License`.
    /// D027: rejects non-https URLs before attaching credentials to prevent leakage in cleartext.
    private static func applyAuth(_ req: inout URLRequest, authToken: String?, licenseKey: String?) {
        // D027: only attach credentials over https to prevent cleartext leakage.
        guard req.url?.scheme?.lowercased() == "https" else { return }
        if let token = authToken {
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        if let key = licenseKey {
            req.setValue(key, forHTTPHeaderField: "X-Syxx-License")
        }
    }

    private static func requestToken(session: URLSession,
                                     collectorBaseURL: URL,
                                     baseDomain: String,
                                     authToken: String?,
                                     licenseKey: String?) async throws -> TokenResponse {
        let url = collectorBaseURL
            .appendingPathComponent("api/leak/token")
            .appendingQueryItem("zone", value: baseDomain)
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        applyAuth(&req, authToken: authToken, licenseKey: licenseKey)
        req.setValue("0", forHTTPHeaderField: "Content-Length")
        let (data, response) = try await session.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw CollectorError.notHTTP }
        guard http.statusCode == 200 else { throw CollectorError.badStatus(http.statusCode) }
        do {
            return try JSONDecoder().decode(TokenResponse.self, from: data)
        } catch {
            throw CollectorError.badJSON(error)
        }
    }

    private static func verifyToken(session: URLSession,
                                    collectorBaseURL: URL,
                                    token: String,
                                    authToken: String?,
                                    licenseKey: String?) async throws -> VerifyResponse {
        let url = collectorBaseURL
            .appendingPathComponent("api/leak/verify")
            .appendingQueryItem("token", value: token)
        var req = URLRequest(url: url)
        applyAuth(&req, authToken: authToken, licenseKey: licenseKey)
        let (data, response) = try await session.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw CollectorError.notHTTP }
        guard http.statusCode == 200 else { throw CollectorError.badStatus(http.statusCode) }
        do {
            return try JSONDecoder().decode(VerifyResponse.self, from: data)
        } catch {
            throw CollectorError.badJSON(error)
        }
    }

    /// D011: check whether a signing key is a member of the configured trust set.
    /// Returns `true` when the key is trusted, `false` when it is NOT in the set, and `nil` when no
    /// trust set is configured (the trust check was not performed — which is NOT the same as
    /// untrusted, and must never be collapsed into `false`).
    ///
    /// Extracted so the trust decision is testable: the previous tests asserted `Set.contains` on a
    /// literal they had just inserted, so the real logic here had no coverage at all — inverting it
    /// would have reported an untrusted signer's attestation as trusted with a green suite.
    /// Twin of Windows `LeakDetector.CheckTrustSet`.
    public static func checkTrustSet(publicKey: String?, trustedPublicKeys: Set<String>?) -> Bool? {
        guard let publicKey, let trustedPublicKeys, !trustedPublicKeys.isEmpty else { return nil }
        return trustedPublicKeys.contains(publicKey)
    }

    /// B091: extract the signed `leaked` result from the signed payload JSON.
    ///
    /// The collector's Ed25519 signature covers the canonical JSON bytes in `signedPayload`.
    /// This method returns the `leaked` boolean from that signed JSON, so the result is
    /// derived exclusively from the attested data — never from the unsigned outer response
    /// fields (``VerifyResponse/leaked``).
    ///
    /// Returns `nil` when the payload is missing, not valid base64, not valid JSON, or
    /// does not contain enough information to determine leak status. All these cases force
    /// an inconclusive result.
    ///
    /// B181: the server never emits a `"leaked"` key — it provides `queriesObserved` (Bool) and
    /// `observedResolvers` ([String]). When `queriesObserved` is true AND `observedResolvers` is
    /// non-empty, the collector attested that queries were observed (leaked = true). When both are
    /// false/empty the server returns the unsigned inconclusive dict (no signedPayload at all),
    /// so this path is never reached with queriesObserved=false.
    static func extractSignedLeaked(from signedPayload: String?) -> Bool? {
        guard let payloadB64 = signedPayload,
              let payloadData = Data(base64Encoded: normalizeBase64(payloadB64)),
              let json = try? JSONSerialization.jsonObject(with: payloadData) as? [String: Any]
        else { return nil }
        // Direct `leaked` field (forward-compatible if the server adds it).
        if let leaked = json["leaked"] as? Bool { return leaked }
        // B181: derive from `queriesObserved` + `observedResolvers`.
        if let queriesObserved = json["queriesObserved"] as? Bool {
            let resolvers = (json["observedResolvers"] as? [String]) ?? []
            return queriesObserved && !resolvers.isEmpty
        }
        return nil
    }

    /// B180: extract `observedResolvers` from the signed payload. The server signs this field
    /// inside the canonical JSON (see leak.py `signed` dict). The outer `observedResolvers` is
    /// unsigned and can be rewritten by a relay while preserving the valid signature.
    /// Returns `nil` when the payload is missing, not valid, or lacks the field.
    static func extractSignedObservedResolvers(from signedPayload: String?) -> [String]? {
        guard let payloadB64 = signedPayload,
              let payloadData = Data(base64Encoded: normalizeBase64(payloadB64)),
              let json = try? JSONSerialization.jsonObject(with: payloadData) as? [String: Any],
              let resolvers = json["observedResolvers"] as? [String]
        else { return nil }
        return resolvers
    }
}

// MARK: - Collector TLS transport pinning (D003)

/// SPKI certificate-pinning support for the leak-collector transport.
///
/// The pin is the base64-encoded SHA-256 of the leaf certificate's DER-encoded
/// SubjectPublicKeyInfo — identical across platforms to .NET's
/// `X509Certificate2.PublicKey.ExportSubjectPublicKeyInfo()` and to
/// `openssl x509 -pubkey | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | base64`
/// (RFC 7469 "pin-sha256"). So the owner computes ONE pin value that works on macOS and Windows.
public enum CollectorPinning {

    // These two strings are USER-VISIBLE and were approved by the user (2026-07-11).
    /// Reason surfaced when the collector's TLS identity could not be verified (pin/PKI failure).
    public static let pinFailureReason =
        "Collector identity could not be verified (certificate pinning failed); this leak result cannot be trusted."
    /// One-time caveat surfaced when no pins are configured (transparently unpinned).
    public static let unconfiguredCaveat =
        "Collector-identity pinning is not configured; the collector connection is unpinned (standard TLS validation only)."

    /// The configured pin set (primary + backup, to survive certificate rotation).
    ///
    /// Sources, in order: the `collector_spki_pins` UserDefaults key (shared with the GUI, the
    /// same pattern as `dimension_base_url`), else the `SYXX_COLLECTOR_PINS` environment variable
    /// (CLI / deployment). Comma-, space-, or newline-separated. An empty set means unpinned.
    ///
    /// OWNER DELIVERABLE: the actual pin value(s) for the real collector are a deployment input
    /// (like `SYXX_COLLECTOR_PRIVATE_KEY`) and are intentionally NOT hardcoded here.
    public static func configuredPins() -> Set<String> {
        var raw = UserDefaults.standard.string(forKey: "collector_spki_pins") ?? ""
        if raw.isEmpty { raw = ProcessInfo.processInfo.environment["SYXX_COLLECTOR_PINS"] ?? "" }
        let parts = raw.split(whereSeparator: { $0 == "," || $0 == " " || $0 == "\n" || $0 == "\t" })
        return Set(parts.map(String.init).filter { !$0.isEmpty })
    }

    /// The configured trusted signing keys (collector Ed25519 public keys).
    ///
    /// Sources, in order: the `collector_trusted_keys` UserDefaults key (shared with the GUI),
    /// else the `SYXX_COLLECTOR_TRUSTED_KEYS` environment variable (CLI / deployment),
    /// else the persisted key from syncTrustedSigningKey (WS3-02).
    /// Comma-, space-, or newline-separated base64 public keys. An empty set means no trust check.
    ///
    /// OWNER DELIVERABLE: the actual collector public key(s) are a deployment input and are
    /// intentionally NOT hardcoded here.
    public static func configuredTrustedSigningKeys() -> Set<String> {
        var raw = UserDefaults.standard.string(forKey: "collector_trusted_keys") ?? ""
        if raw.isEmpty { raw = ProcessInfo.processInfo.environment["SYXX_COLLECTOR_TRUSTED_KEYS"] ?? "" }
        
        // WS3-02: fall back to persisted synced key if manual config is empty
        if raw.isEmpty, let persistedKey = persistedTrustedSigningKey() {
            raw = persistedKey
        }
        
        let parts = raw.split(whereSeparator: { $0 == "," || $0 == " " || $0 == "\n" || $0 == "\t" })
        return Set(parts.map(String.init).filter { !$0.isEmpty })
    }

    // MARK: - WS3-02: Trust store sync from /api/pubkey

    /// Fetches the collector's public key from the well-known endpoint and persists it.
    ///
    /// This enables automatic trust set updates without manual configuration. The fetched
    /// key is persisted to UserDefaults for offline use. Call this periodically (e.g., on app launch
    /// or daily) to keep the trust set current.
    ///
    /// B185: uses the SPKI-pinned session (not `URLSession.shared`), validates the key is a
    /// 32-byte Ed25519 public key, and verifies that `keyId` matches the SHA-256 prefix of the
    /// raw key bytes. Surfaces key rotation as a diagnostic event rather than silently overwriting.
    ///
    /// - Parameter collectorBaseURL: The base URL of the collector server (e.g., from config).
    /// - Returns: The fetched public key, or nil if the fetch failed or the key did not validate.
    public static func syncTrustedSigningKey(collectorBaseURL: URL) async -> String? {
        let pubkeyURL = collectorBaseURL.appendingPathComponent("api/pubkey")

        // B185: fetch through the pinned session — an unpinned URLSession.shared would let a
        // MITM with a CA-issued certificate inject a forged trust anchor.
        let pins = CollectorPinning.configuredPins()
        let session = LeakDetector.makeCollectorSession(pins: pins)
        defer { session.finishTasksAndInvalidate() }

        var request = URLRequest(url: pubkeyURL)
        request.timeoutInterval = 10.0

        do {
            let (data, response) = try await session.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                return nil
            }
            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let publicKey = json["publicKey"] as? String,
                  let keyId = json["keyId"] as? String,
                  let rotationTimestamp = json["rotationTimestamp"] as? Int else {
                return nil
            }

            // B185: validate the key is a 32-byte Ed25519 public key and keyId matches.
            guard let keyBytes = Data(base64Encoded: publicKey), keyBytes.count == 32 else {
                SyxxDiagnostics.breadcrumb(
                    category: "leak.trust_sync",
                    message: "Fetched public key is not a valid 32-byte Ed25519 key — refusing to persist",
                    level: .error)
                return nil
            }
            let computedKeyId = SHA256.hash(data: keyBytes).prefix(8).map { String(format: "%02x", $0) }.joined()
            guard keyId == computedKeyId else {
                SyxxDiagnostics.breadcrumb(
                    category: "leak.trust_sync",
                    message: "Fetched keyId '\(keyId)' does not match SHA-256 prefix '\(computedKeyId)' — refusing to persist",
                    level: .error)
                return nil
            }

            // B185: surface key rotation — compare to previously persisted key.
            let previousKey = UserDefaults.standard.string(forKey: "collector_trusted_key")
            if let previous = previousKey, previous != publicKey {
                SyxxDiagnostics.breadcrumb(
                    category: "leak.trust_sync",
                    message: "Collector signing key rotated (previous keyId: \(UserDefaults.standard.string(forKey: "collector_trusted_key_id") ?? "unknown"))",
                    level: .warning)
            }

            // Persist to UserDefaults for offline use
            UserDefaults.standard.set(publicKey, forKey: "collector_trusted_key")
            UserDefaults.standard.set(keyId, forKey: "collector_trusted_key_id")
            UserDefaults.standard.set(rotationTimestamp, forKey: "collector_trusted_key_timestamp")

            return publicKey
        } catch {
            SyxxDiagnostics.breadcrumb(
                category: "leak.trust_sync",
                message: "Trust sync failed: \(error.localizedDescription)",
                level: .warning)
            return nil
        }
    }

    /// Returns the persisted trusted signing key, or nil if none exists.
    ///
    /// This is the offline fallback when syncTrustedSigningKey cannot reach the server.
    public static func persistedTrustedSigningKey() -> String? {
        UserDefaults.standard.string(forKey: "collector_trusted_key")
    }

    /// The pin (base64 SHA-256 of DER SubjectPublicKeyInfo) for a certificate's DER bytes.
    /// Returns `nil` on any parse failure — the caller treats that fail-closed as a mismatch.
    public static func spkiPin(forCertificateDER der: Data) -> String? {
        guard let spki = extractSPKI([UInt8](der)) else { return nil }
        return Data(SHA256.hash(data: Data(spki))).base64EncodedString()
    }

    /// Pure, offline-testable fail-closed decision.
    ///
    /// - PKI chain validation MUST pass first (pinning is *in addition to* PKI, never a
    ///   replacement — a self-signed impostor is rejected even if the attacker knew a pin).
    /// - No pins configured → accept via standard validation (`.notConfigured`).
    /// - Pins configured → accept only if the leaf pin is in the set (`.enforced`); otherwise
    ///   refuse (`.failed`).
    public static func decide(trustPassed: Bool,
                              leafPin: String?,
                              pins: Set<String>) -> CollectorTransportPinning {
        guard trustPassed else { return .failed }
        if pins.isEmpty { return .notConfigured }
        if let leafPin, pins.contains(leafPin) { return .enforced }
        return .failed
    }

    /// The leaf (end-entity) certificate of an evaluated trust, or `nil`.
    static func leafCertificate(_ trust: SecTrust) -> SecCertificate? {
        (SecTrustCopyCertificateChain(trust) as? [SecCertificate])?.first
    }

    // MARK: DER walker — extracts the SubjectPublicKeyInfo TLV per RFC 5280.

    /// Reads one DER TLV at `offset`. Bounds-checked; returns `nil` on malformed input.
    private static func readTLV(_ d: [UInt8], _ offset: Int)
        -> (tag: UInt8, contentStart: Int, contentLen: Int, next: Int)? {
        var p = offset
        guard p < d.count else { return nil }
        let tag = d[p]; p += 1
        guard p < d.count else { return nil }
        let first = d[p]; p += 1
        var len = 0
        if first < 0x80 {
            len = Int(first)
        } else {
            let n = Int(first & 0x7f)
            guard n >= 1, n <= 4, p + n <= d.count else { return nil }
            for _ in 0..<n { len = (len << 8) | Int(d[p]); p += 1 }
        }
        guard len >= 0, p + len <= d.count else { return nil }
        return (tag, p, len, p + len)
    }

    /// Extracts the full SubjectPublicKeyInfo TLV bytes from a certificate's DER.
    /// TBSCertificate order: [0]version?, serialNumber, signature, issuer, validity, subject,
    /// subjectPublicKeyInfo — so SPKI is the 5th element after the serial number.
    static func extractSPKI(_ der: [UInt8]) -> [UInt8]? {
        guard let cert = readTLV(der, 0), cert.tag == 0x30 else { return nil }        // Certificate
        guard let tbs = readTLV(der, cert.contentStart), tbs.tag == 0x30 else { return nil } // TBS
        var p = tbs.contentStart
        let end = tbs.contentStart + tbs.contentLen
        guard var cur = readTLV(der, p) else { return nil }
        if cur.tag == 0xA0 {                                                          // [0] version
            p = cur.next
            guard let next = readTLV(der, p) else { return nil }
            cur = next
        }
        guard cur.tag == 0x02 else { return nil }                                     // serialNumber
        p = cur.next
        for _ in 0..<4 {                                       // signature, issuer, validity, subject
            guard p < end, let e = readTLV(der, p) else { return nil }
            p = e.next
        }
        guard p < end, let spki = readTLV(der, p), spki.tag == 0x30 else { return nil } // SPKI SEQUENCE
        return Array(der[p..<spki.next])
    }
}

/// URLSession delegate that enforces collector SPKI pinning fail-closed (D003).
///
/// `pins` is immutable for the session's lifetime, so this is safe to hand to the
/// (Sendable) URLSession.
final class CollectorPinningDelegate: NSObject, URLSessionDelegate, @unchecked Sendable {
    let pins: Set<String>
    init(pins: Set<String>) { self.pins = pins }

    func urlSession(_ session: URLSession,
                    didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              let trust = challenge.protectionSpace.serverTrust else {
            completionHandler(.performDefaultHandling, nil)
            return
        }
        var cfError: CFError?
        let trustPassed = SecTrustEvaluateWithError(trust, &cfError)
        var leafPin: String?
        if trustPassed, let leaf = CollectorPinning.leafCertificate(trust) {
            leafPin = CollectorPinning.spkiPin(forCertificateDER: SecCertificateCopyData(leaf) as Data)
        }
        switch CollectorPinning.decide(trustPassed: trustPassed, leafPin: leafPin, pins: pins) {
        case .enforced, .notConfigured:
            completionHandler(.useCredential, URLCredential(trust: trust))
        case .failed:
            // FAIL CLOSED — refuse the connection rather than trust an unverified collector.
            completionHandler(.cancelAuthenticationChallenge, nil)
        }
    }
}

// MARK: - URL helpers

private extension URL {
    func appendingQueryItem(_ name: String, value: String) -> URL {
        guard var comps = URLComponents(url: self, resolvingAgainstBaseURL: false) else { return self }
        var items = comps.queryItems ?? []
        items.append(URLQueryItem(name: name, value: value))
        comps.queryItems = items
        return comps.url ?? self
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NSec.Cryptography;

namespace Syxx.Kit
{
    /// <summary>
    /// Collector TLS-transport pinning posture for a leak test (D003). Describes the
    /// <em>transport identity</em> check on the collector's HTTPS endpoint — distinct from
    /// the Ed25519 attestation-signature check (<see cref="LeakTestResult.SignatureValid"/>).
    /// </summary>
    public enum CollectorTransportPinning
    {
        /// <summary>SPKI pins were configured and the collector's leaf certificate matched one.</summary>
        Enforced,
        /// <summary>No SPKI pins were configured; only standard system TLS validation was applied.</summary>
        NotConfigured,
        /// <summary>
        /// SPKI pins were configured but the collector's leaf certificate did NOT match
        /// (or the chain failed PKI validation). The transport was refused, fail-closed.
        /// </summary>
        Failed
    }

    /// <summary>The collector-verified outcome of a DNS leak test.</summary>
    public enum LeakResult
    {
        /// <summary>The collector confirmed at least one query reached an unexpected resolver.</summary>
        Confirmed,
        /// <summary>The collector observed no queries for this token (no leak detected).</summary>
        NotDetected,
        /// <summary>The test could not complete or be verified (network error, no collector, etc.).</summary>
        Inconclusive,
        /// <summary>P-5 D1: the result payload is structurally impossible (malformed, missing fields).</summary>
        Invalid,
        /// <summary>P-5 D1: an execution error prevented the test from completing.</summary>
        Error
    }

    /// <summary>
    /// Which collector request could not be completed. The step matters to honesty: a failure at
    /// <see cref="Token"/> means no DNS query was ever dispatched, while a failure at
    /// <see cref="Verify"/> means the queries WERE sent but their result could not be collected.
    /// Neither is evidence of no leak. Twin of macOS <c>CollectorStep</c>.
    /// </summary>
    public enum CollectorStep
    {
        /// <summary><c>POST /api/leak/token</c> — before any DNS query is dispatched.</summary>
        Token,
        /// <summary><c>GET /api/leak/verify</c> — after the DNS queries were dispatched.</summary>
        Verify
    }

    /// <summary>
    /// Why a collector-confirmed leak test produced no evidence. Twin of macOS
    /// <c>CollectorFailure</c>.
    ///
    /// The collector's HTTP status is a DISTINCT state, not noise: "you are not licensed" (402),
    /// "you asked too often" (429), "your credentials were rejected" (401/403) and "the collector
    /// fell over" (5xx) demand different user outcomes. This client used to fold every one of them
    /// into a single <c>HttpRequestException("HTTP nnn")</c> that nothing ever classified, so a
    /// working paywall read exactly like a collapsed server.
    ///
    /// This is never a leak/no-leak signal. Every kind means the same thing about DNS: nothing was
    /// observed, therefore nothing can be concluded.
    /// </summary>
    public enum CollectorFailureKind
    {
        /// <summary>HTTP 402 — the collector requires a Syxx Pro entitlement for this route.
        /// Nothing broke; the feature is gated. A correct answer from a correct server.</summary>
        EntitlementRequired,
        /// <summary>HTTP 401 / 403 — the collector rejected the supplied credentials.</summary>
        AuthenticationFailed,
        /// <summary>HTTP 429 — the collector is rate limiting this caller.</summary>
        RateLimited,
        /// <summary>HTTP 5xx — the collector reported a server-side error.</summary>
        ServerError,
        /// <summary>Any other non-200 status, carried verbatim; no cause is inferred.</summary>
        UnexpectedStatus,
        /// <summary>The collector answered 200 but the body could not be decoded.</summary>
        MalformedResponse,
        /// <summary>D003 — the collector's TLS identity could not be verified.</summary>
        CollectorIdentityUnverified,
        /// <summary>The request never reached the collector (timeout, offline, reset).</summary>
        TransportFailure
    }

    /// <summary>
    /// A classified collector failure and the HTTP status that produced it. The status is preserved
    /// so nothing downstream has to guess. Twin of macOS <c>CollectorFailure</c>.
    /// </summary>
    public sealed class CollectorFailure : IEquatable<CollectorFailure>
    {
        public CollectorFailureKind Kind { get; }
        /// <summary>
        /// The HTTP status the collector returned, when the failure was an HTTP response at all.
        /// <c>null</c> for transport, identity and decode failures, where no status exists to report.
        /// </summary>
        public int? HttpStatus { get; }

        public CollectorFailure(CollectorFailureKind kind, int? httpStatus = null)
        {
            Kind = kind;
            HttpStatus = httpStatus;
        }

        /// <summary>
        /// True only for HTTP 402: the request was refused because the caller lacks a Syxx Pro
        /// entitlement. Surfaces should offer an upgrade path here, NOT an error state — nothing
        /// about the product failed.
        /// </summary>
        public bool RequiresEntitlement => Kind == CollectorFailureKind.EntitlementRequired;

        // MARK: - User-visible copy

        /// <summary>
        /// Plain-English reason for this failure at <paramref name="step"/>.
        ///
        /// PENDING OWNER APPROVAL (proposed 2026-07-29) — every sentence below reaches the screen
        /// and the CLI, and is byte-identical to the macOS twin. The one exception is
        /// <see cref="CollectorPinning.PinFailureReason"/>, approved 2026-07-11 and reused verbatim.
        ///
        /// Two rules hold every string here:
        /// 1. No cause is asserted that the code has not established. An unrecognised status
        ///    reports the status, not a guess about what it meant.
        /// 2. Every sentence closes the honesty gap the status opens: the reader is told that no
        ///    leak evidence exists, so no case can be misread as "no leak detected".
        /// </summary>
        public string Reason(CollectorStep step)
        {
            if (Kind == CollectorFailureKind.CollectorIdentityUnverified)
            {
                // Approved 2026-07-11; already self-contained, so it takes no step suffix.
                return CollectorPinning.PinFailureReason;
            }
            return Cause + " " + Consequence(step);
        }

        /// <summary>The half of the sentence that states WHAT the collector did.</summary>
        private string Cause => Kind switch
        {
            CollectorFailureKind.EntitlementRequired =>
                "A Syxx Pro license is required for the collector-confirmed leak test.",
            CollectorFailureKind.AuthenticationFailed =>
                $"The collector rejected the credentials supplied (HTTP {HttpStatus}).",
            CollectorFailureKind.RateLimited =>
                $"The collector is rate limiting requests from this device (HTTP {HttpStatus}).",
            CollectorFailureKind.ServerError =>
                $"The collector reported a server error (HTTP {HttpStatus}).",
            // Deliberately states the status and nothing else: the code has established no cause.
            CollectorFailureKind.UnexpectedStatus =>
                $"The collector request failed with HTTP status {HttpStatus}.",
            CollectorFailureKind.MalformedResponse =>
                "The collector's response could not be read.",
            CollectorFailureKind.TransportFailure =>
                "The collector could not be reached.",
            _ => CollectorPinning.PinFailureReason
        };

        /// <summary>
        /// The half of the sentence that states what it means for the evidence. This is the part
        /// that stops any failure from reading as a clean result.
        /// </summary>
        private static string Consequence(CollectorStep step) => step == CollectorStep.Token
            ? "No leak test was performed, so nothing is known about your DNS resolvers."
            : "The test queries were sent but their result could not be collected, so nothing is known about your DNS resolvers.";

        public bool Equals(CollectorFailure? other) =>
            other is not null && Kind == other.Kind && HttpStatus == other.HttpStatus;
        public override bool Equals(object? obj) => Equals(obj as CollectorFailure);
        public override int GetHashCode() => HashCode.Combine(Kind, HttpStatus);
        public override string ToString() =>
            HttpStatus is null ? Kind.ToString() : $"{Kind}({HttpStatus})";
    }

    // E049: detailed signature verification outcome, replacing the collapsed bool that
    // previously made unsigned, malformed, tampered, and crypto-error evidence indistinguishable.
    public enum LeakSignatureVerification
    {
        /// <summary>The Ed25519 signature is cryptographically valid for the attested payload.</summary>
        Valid,
        /// <summary>No signature data was provided (collector sent no signature).</summary>
        NoSignature,
        /// <summary>The signature failed cryptographic verification — evidence was tampered.</summary>
        InvalidSignature,
        /// <summary>Signature or key data was malformed (bad base64, wrong key length, etc.).</summary>
        MalformedData
    }

    /// <summary>Full result returned by <see cref="LeakDetector.RunCollectorTestAsync"/>.</summary>
    public class LeakTestResult
    {
        public LeakResult Result { get; set; }
        /// <summary>Resolver IPs the collector recorded as having forwarded the test query.</summary>
        public List<string> ObservedResolvers { get; set; } = new List<string>();
        /// <summary>
        /// Ed25519 signature produced by the collector — tamper-evident evidence.
        /// <c>null</c> when the collector was not reachable or did not return a signature.
        /// </summary>
        public string? Signature { get; set; }
        /// <summary>The collector's public key (base64 raw 32-byte Ed25519 public key).</summary>
        public string? PublicKey { get; set; }
        /// <summary>Opaque key identifier (first 8 bytes of public key, base64-encoded).</summary>
        public string? KeyId { get; set; }
        /// <summary>Canonical JSON payload bytes that were signed (base64-encoded).</summary>
        public string? SignedPayload { get; set; }
        /// <summary>Whether the signature (if present) passed verification with the supplied key.</summary>
        public bool? SignatureValid { get; set; }
        // E049: detailed verification outcome distinguishing no-signature, malformed, invalid,
        // and valid states — previously collapsed into SignatureValid's bool?. Kept alongside
        // SignatureValid so existing consumers continue to work.
        public LeakSignatureVerification? SignatureVerification { get; set; }
        public string? ErrorReason { get; set; }
        /// <summary>
        /// Collector TLS-transport pinning posture for this run (D003). A <see
        /// cref="CollectorTransportPinning.Failed"/> value means the collector's identity could
        /// not be verified, so the result is forced to <see cref="LeakResult.Inconclusive"/> —
        /// a leak/no-leak result cannot be trusted.
        /// </summary>
        public CollectorTransportPinning TransportPinning { get; set; } = CollectorTransportPinning.NotConfigured;
        /// <summary>
        /// D011: whether the signing public key was present in the configured trust set.
        /// <c>null</c> — no trust set was configured (transparently untracked),
        /// <c>true</c>  — the signer's key is a member of the trust set,
        /// <c>false</c> — the signer's key is NOT in the trust set (signature is crypto-valid
        /// but the signer is not trusted). Never <c>true</c> when <see cref="SignatureValid"/>
        /// is not also <c>true</c>.
        /// </summary>
        public bool? SignatureKeyTrusted { get; set; }
        /// <summary>
        /// Why the collector produced no evidence, when it produced none. <c>null</c> when the
        /// collector answered the request (whatever the leak outcome was), and <c>null</c> when the
        /// run never reached the collector at all (no resolvers configured, every dispatch failed).
        ///
        /// Read this INSTEAD OF pattern-matching <see cref="ErrorReason"/>: it is the structured,
        /// testable form of the same fact, and it is the only thing that lets a surface tell
        /// "requires Syxx Pro" apart from "the collector fell over". Twin of macOS
        /// <c>LeakTestResult.collectorFailure</c>.
        /// </summary>
        public CollectorFailure? CollectorFailure { get; set; }
    }

    /// <summary>
    /// DNS Leak Detector.
    ///
    /// Generates unique one-time subdomains, dispatches them through the system's
    /// DNS resolvers, and — when a collector URL is provided — calls the server's
    /// signed-token API to obtain cryptographically attested evidence of which
    /// upstream resolvers forwarded the query.
    /// </summary>
    public static class LeakDetector
    {
        /// <summary>
        /// Builds an <see cref="HttpClient"/> whose server-certificate validation is gated by
        /// <paramref name="pins"/> (base64 SHA-256 of the leaf certificate's DER
        /// SubjectPublicKeyInfo). An empty set means standard system TLS validation only
        /// (unpinned). A configured-but-non-matching leaf is refused fail-closed (D003).
        /// </summary>
        internal static HttpClient MakeCollectorClient(ISet<string> pins)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                {
                    bool chainOk = errors == SslPolicyErrors.None;
                    string? leafPin = cert != null ? CollectorPinning.SpkiPin(cert) : null;
                    // Accept only when PKI passes AND (unpinned OR the leaf pin matches).
                    return CollectorPinning.Accept(chainOk, leafPin, pins);
                }
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // MARK: - Public API

        public static string GenerateTestSubdomain(string baseDomain = "leak.syxx.app")
        {
            string token = Guid.NewGuid().ToString("N").ToLower();
            return $"{token}.{baseDomain}";
        }

        /// <summary>
        /// Performs a complete collector-confirmed leak test.
        ///
        /// 1. Requests a signed one-time token from <c>collectorBaseURL/api/leak/token</c>.
        /// 2. Dispatches DNS queries for the returned subdomain through <paramref name="servers"/>.
        /// 3. Waits <paramref name="pollDelayMs"/> ms for the authoritative server to record observations.
        /// 4. Verifies via <c>collectorBaseURL/api/leak/verify</c> and returns a signed result.
        ///
        /// A network failure at any step returns <see cref="LeakResult.Inconclusive"/>, never
        /// <see cref="LeakResult.NotDetected"/>.
        ///
        /// Authentication: pass a session <paramref name="authToken"/> (sent as
        /// <c>Authorization: Bearer</c>), a <paramref name="licenseKey"/> (sent as
        /// <c>X-Syxx-License</c>), or both — the collector accepts either.
        /// </summary>
        public static async Task<LeakTestResult> RunCollectorTestAsync(
            string baseDomain,
            List<string> servers,
            string collectorBaseURL,
            string? authToken = null,
            string? licenseKey = null,
            int pollDelayMs = 3000,
            ISet<string>? pinsOverride = null,
            ISet<string>? trustedPublicKeys = null,
            IReadOnlyCollection<string>? expectedResolvers = null)
        {
            // D003 — collector TLS transport pinning. `Enforced` when pins are configured,
            // `NotConfigured` (transparently unpinned) otherwise. A pin/PKI failure during a
            // request downgrades this to `Failed` and the result to `Inconclusive`.
            var pins = pinsOverride ?? CollectorPinning.ConfiguredPins();
            // WS3-A.3: the signer trust set engages even when the caller (GUI/CLI) passes nothing,
            // exactly like the transport pins above — otherwise D011 is inert in the shipped app.
            trustedPublicKeys ??= CollectorPinning.ConfiguredTrustedSigningKeys();
            var posture = pins.Count == 0
                ? CollectorTransportPinning.NotConfigured
                : CollectorTransportPinning.Enforced;

            if (servers.Count == 0)
            {
                return new LeakTestResult
                {
                    Result = LeakResult.Inconclusive,
                    ErrorReason = "No system DNS resolvers detected.",
                    TransportPinning = posture
                };
            }

            using var client = MakeCollectorClient(pins);

            // Step 1 — obtain signed token from collector.
            string tokenLabel, subdomain;
            try
            {
                var tokenResp = await RequestTokenAsync(client, collectorBaseURL, baseDomain, authToken, licenseKey);
                tokenLabel = tokenResp.Token;
                subdomain  = tokenResp.Subdomain;
            }
            catch (Exception ex)
            {
                SyxxDiagnostics.Breadcrumb(category: "leak.token", message: "Leak token request failed", level: DiagnosticSeverity.Warning, data: new Dictionary<string, string> { ["error"] = ex.GetType().Name });
                // Distinct states stay distinct. Previously every non-200 collapsed here into one
                // Inconclusive carrying "Token request failed: HTTP nnn", so a working paywall (402)
                // read exactly like a collapsed collector (5xx). A pin/PKI mismatch aborts the TLS
                // handshake and is classified FIRST, so an on-path impostor is never downgraded to
                // a generic network hiccup.
                var failure = Classify(ex);
                return new LeakTestResult
                {
                    Result = ResultFor(failure),
                    ErrorReason = failure.Reason(CollectorStep.Token),
                    TransportPinning = failure.Kind == CollectorFailureKind.CollectorIdentityUnverified
                        ? CollectorTransportPinning.Failed
                        : posture,
                    CollectorFailure = failure
                };
            }

            // Step 2 — trigger DNS queries through system resolvers.
            var dispatchResults = await TriggerQueriesAsync(subdomain, servers);
            // E007: abort early when every dispatch failed — no point polling the server.
            if (dispatchResults.Values.All(sent => !sent))
            {
                return new LeakTestResult
                {
                    Result = LeakResult.Inconclusive,
                    ErrorReason = "All DNS dispatches failed; no queries reached any resolver.",
                    TransportPinning = posture
                };
            }

            // Step 3 — give the authoritative server time to record observations.
            await Task.Delay(pollDelayMs);

            // Step 4 — verify with collector and return signed attestation.
            try
            {
                var verifyResp = await VerifyTokenAsync(client, collectorBaseURL, tokenLabel, authToken, licenseKey);
                var outerObserved = verifyResp.ObservedResolvers ?? new List<string>();
                var built = new LeakTestResult
                {
                    ObservedResolvers = outerObserved,
                    Signature = verifyResp.Signature,
                    PublicKey = verifyResp.PublicKey,
                    KeyId = verifyResp.KeyId,
                    SignedPayload = verifyResp.SignedPayload,
                    TransportPinning = posture
                };
                // E049: use VerifyDetailed to capture the full verification outcome, then
                // derive the backward-compatible bool? for existing consumers.
                var sigVerification = built.Signature != null && built.PublicKey != null && built.SignedPayload != null
                    ? VerifyDetailed(built)
                    : (LeakSignatureVerification?)null;
                built.SignatureVerification = sigVerification;
                bool? sigValid = sigVerification.HasValue
                    ? sigVerification.Value == LeakSignatureVerification.Valid
                    : null;
                built.SignatureValid = sigValid;
                // D011: when a trust set is configured, check the signing key is a member.
                if (sigValid == true)
                    built.SignatureKeyTrusted = CheckTrustSet(built.PublicKey, trustedPublicKeys);

                // B216: pass tokenLabel and baseDomain so the signed attestation is bound to the request.
                var (dResult, dObserved, dReason) = DeriveResult(
                    sigValid, built.SignedPayload, verifyResp.Leaked, outerObserved, expectedResolvers,
                    tokenLabel, baseDomain);
                built.Result = dResult;
                built.ObservedResolvers = dObserved;
                built.ErrorReason = dReason;
                return built;
            }
            catch (Exception ex)
            {
                SyxxDiagnostics.Breadcrumb(category: "leak.verify", message: "Leak verify request failed", level: DiagnosticSeverity.Warning, data: new Dictionary<string, string> { ["error"] = ex.GetType().Name });
                // Identical collapse to the token step, fixed identically. The Verify step means the
                // DNS queries WERE dispatched, so its copy says so rather than claiming no test ran.
                var failure = Classify(ex);
                return new LeakTestResult
                {
                    Result = ResultFor(failure),
                    ErrorReason = failure.Reason(CollectorStep.Verify),
                    TransportPinning = failure.Kind == CollectorFailureKind.CollectorIdentityUnverified
                        ? CollectorTransportPinning.Failed
                        : posture,
                    CollectorFailure = failure
                };
            }
        }

        /// <summary>
        /// A pinned collector request that fails the TLS handshake throws an
        /// <see cref="AuthenticationException"/> (wrapped in an
        /// <see cref="HttpRequestException"/>) — the only reason this client's handshake is
        /// refused is a bad PKI chain or an SPKI-pin mismatch. Treat it as a transport-identity
        /// failure, never a routine network hiccup.
        /// </summary>
        internal static bool IsTransportIdentityFailure(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is AuthenticationException) return true;
            }
            return false;
        }

        // MARK: - Collector failure classification

        /// <summary>
        /// Classify a non-200 collector status into a distinct outcome.
        ///
        /// Pure and offline-testable. 402 is the intended paywall on <c>/api/leak/token</c> (Server
        /// <c>app.py:1583</c> → <c>585-588</c>, <c>config.ENFORCE_WEB</c> at <c>config.py:314</c>);
        /// it is a CORRECT answer from a correctly configured collector, not a defect, and it must
        /// not be reported as one. Anything unrecognised keeps its status verbatim rather than
        /// being guessed at. Twin of macOS <c>LeakDetector.classifyStatus</c>.
        /// </summary>
        public static CollectorFailure ClassifyStatus(int status) => status switch
        {
            402 => new CollectorFailure(CollectorFailureKind.EntitlementRequired, status),
            401 or 403 => new CollectorFailure(CollectorFailureKind.AuthenticationFailed, status),
            429 => new CollectorFailure(CollectorFailureKind.RateLimited, status),
            >= 500 and <= 599 => new CollectorFailure(CollectorFailureKind.ServerError, status),
            _ => new CollectorFailure(CollectorFailureKind.UnexpectedStatus, status)
        };

        /// <summary>
        /// Classify a thrown collector exception into a distinct outcome.
        ///
        /// Order matters: the transport-identity check runs FIRST so a pin/PKI refusal can never be
        /// downgraded to a generic transport hiccup. Anything not recognised resolves to
        /// <see cref="CollectorFailureKind.TransportFailure"/> — the request did not come back —
        /// which asserts no cause it has not established. Twin of macOS <c>LeakDetector.classify</c>.
        /// </summary>
        internal static CollectorFailure Classify(Exception ex)
        {
            if (IsTransportIdentityFailure(ex))
                return new CollectorFailure(CollectorFailureKind.CollectorIdentityUnverified);
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is CollectorStatusException status) return ClassifyStatus(status.StatusCode);
                if (e is CollectorMalformedResponseException)
                    return new CollectorFailure(CollectorFailureKind.MalformedResponse);
            }
            return new CollectorFailure(CollectorFailureKind.TransportFailure);
        }

        /// <summary>
        /// The result a collector failure resolves to.
        ///
        /// <see cref="LeakResult.Inconclusive"/> for an entitlement gate: nothing broke, the feature
        /// is simply gated, so rendering it as an execution error would tell the user their product
        /// is faulty when it is not. <see cref="LeakResult.Error"/> for everything else, which
        /// genuinely did prevent the test from completing. NEVER <see cref="LeakResult.NotDetected"/>
        /// or <see cref="LeakResult.Confirmed"/> in either case — no query was observed, so no leak
        /// claim of any kind is available. Twin of macOS <c>LeakDetector.result(for:)</c>.
        /// </summary>
        public static LeakResult ResultFor(CollectorFailure failure) =>
            failure.RequiresEntitlement ? LeakResult.Inconclusive : LeakResult.Error;

        // MARK: - Signature verification

        // E049: detailed signature verification replacing the collapsed bool that made
        // unsigned, malformed, tampered, and crypto-error evidence indistinguishable.
        // Each failure mode is a distinct enum value so downstream surfaces can render
        // honest, non-contradictory diagnostic text.
        /// <summary>
        /// Verify a collector's Ed25519-signed leak attestation with a detailed outcome.
        /// Returns <see cref="LeakSignatureVerification.Valid"/> when the signature is
        /// cryptographically valid. Returns distinct enum values for no-signature,
        /// malformed data, and invalid signature — never collapses all failures into false.
        /// </summary>
        public static LeakSignatureVerification VerifyDetailed(LeakTestResult result)
        {
            if (result.Signature is null || result.PublicKey is null || result.SignedPayload is null)
                return LeakSignatureVerification.NoSignature;
            try
            {
                var sigBytes = Convert.FromBase64String(NormalizeBase64(result.Signature)!);
                var pubBytes = Convert.FromBase64String(result.PublicKey);
                var payloadBytes = Convert.FromBase64String(result.SignedPayload);
                var pubKey = NSec.Cryptography.PublicKey.Import(
                    SignatureAlgorithm.Ed25519,
                    pubBytes,
                    KeyBlobFormat.RawPublicKey);
                return SignatureAlgorithm.Ed25519.Verify(pubKey, payloadBytes, sigBytes)
                    ? LeakSignatureVerification.Valid
                    : LeakSignatureVerification.InvalidSignature;
            }
            catch
            {
                // A transient parsing error, bad base64, or wrong key format is malformed data —
                // distinct from a deliberate tamper signal (InvalidSignature).
                return LeakSignatureVerification.MalformedData;
            }
        }

        /// <summary>
        /// Verify a collector's Ed25519-signed leak attestation (backward-compatible bool wrapper).
        /// Prefer <see cref="VerifyDetailed"/> for new code — this collapses four distinct states
        /// into two.
        /// </summary>
        public static bool Verify(LeakTestResult result)
        {
            return VerifyDetailed(result) == LeakSignatureVerification.Valid;
        }

        /// D057: normalize url-safe/unpadded base64 to standard padded form so a
        /// collector using either alphabet can be verified without fabricating a fail.
        private static string? NormalizeBase64(string? s)
        {
            if (s is null) return null;
            var sb = s.Replace('-', '+').Replace('_', '/');
            int pad = (4 - sb.Length % 4) % 4;
            return sb + new string('=', pad);
        }

        /// D011: check whether a signing key is a member of the configured trust set.
        /// Returns <c>true</c> when the key is trusted, <c>false</c> when the key is not in the
        /// set, and <c>null</c> when no trust set is configured (trust check was not performed).
        /// This is the pure-logic helper; the full <see cref="RunCollectorTestAsync"/> pipeline
        /// calls this after crypto verification succeeds.
        public static bool? CheckTrustSet(string? publicKey, ISet<string>? trustedPublicKeys)
        {
            if (publicKey is null || trustedPublicKeys is null || trustedPublicKeys.Count == 0)
                return null;
            return trustedPublicKeys.Contains(publicKey);
        }

        // MARK: - Signed attestation (B091)

        /// <summary>
        /// The strict DTO of the result-bearing fields inside the collector's signed payload.
        /// Both fields are REQUIRED — a signed payload missing either cannot support any result
        /// and parses to null (→ Inconclusive), never to a defaulted false/empty.
        /// </summary>
        internal sealed class SignedLeakAttestation
        {
            [JsonPropertyName("leaked")]
            public bool? Leaked { get; set; }
            [JsonPropertyName("observedResolvers")]
            public List<string>? ObservedResolvers { get; set; }
            // B216: binding fields — the signed attestation must be bound to the request that
            // produced it. A valid attestation for a different token or zone, replayed by a relay,
            // would otherwise pass the outer/signed mismatch check.
            [JsonPropertyName("token")]
            public string? Token { get; set; }
            [JsonPropertyName("zone")]
            public string? Zone { get; set; }
            [JsonPropertyName("resolverCount")]
            public int? ResolverCount { get; set; }
        }

        /// <summary>
        /// B091: parse the base64 signed payload into the strict DTO. Returns null on undecodable
        /// base64, non-JSON bytes, or a payload missing either result-bearing field.
        /// Call ONLY after <see cref="Verify"/> has validated the signature over these bytes.
        /// </summary>
        internal static SignedLeakAttestation? ParseSignedAttestation(string signedPayloadBase64)
        {
            try
            {
                var bytes = Convert.FromBase64String(NormalizeBase64(signedPayloadBase64)!);
                var dto = JsonSerializer.Deserialize<SignedLeakAttestation>(bytes, _jsonOptions);
                if (dto?.Leaked is null || dto.ObservedResolvers is null) return null;
                return dto;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// B091: every result-bearing field comes from the SIGNED payload, never the outer JSON.
        /// A relay can preserve a valid signature while flipping the outer fields, so:
        /// valid signature → parse the signed DTO strictly and derive the result from ITS fields,
        /// rejecting any outer/signed mismatch as Inconclusive (forged outer); invalid signature →
        /// Inconclusive (a broken attestation supports nothing); no signature at all → legacy
        /// unauthenticated path (SignatureValid == null gates display per D004), result from the
        /// outer fields as before. Pure and offline-testable.
        /// </summary>
        internal static (LeakResult Result, List<string> Observed, string? Reason) DeriveResult(
            bool? sigValid, string? signedPayloadBase64, bool outerLeaked, List<string> outerObserved,
            IReadOnlyCollection<string>? expectedResolvers,
            string? tokenLabel = null, string? baseDomain = null)
        {
            if (sigValid == true)
            {
                var signed = signedPayloadBase64 is null ? null : ParseSignedAttestation(signedPayloadBase64);
                if (signed is null)
                    return (LeakResult.Inconclusive, outerObserved,
                        "Collector attestation could not be parsed; result cannot be trusted.");
                bool outerMatchesSigned = outerLeaked == signed.Leaked
                    && outerObserved.OrderBy(x => x, StringComparer.Ordinal)
                        .SequenceEqual(signed.ObservedResolvers!.OrderBy(x => x, StringComparer.Ordinal));
                if (!outerMatchesSigned)
                    return (LeakResult.Inconclusive, signed.ObservedResolvers!,
                        "Collector response does not match its signed attestation; possible tampering in transit.");
                // B216: bind the attestation to the request. A valid attestation for a different
                // token or zone, replayed by a relay, would otherwise pass the mismatch check.
                if (tokenLabel is not null && signed.Token is not null
                    && !string.Equals(signed.Token, tokenLabel, StringComparison.Ordinal))
                    return (LeakResult.Inconclusive, signed.ObservedResolvers!,
                        "Collector attestation token does not match the request; possible replay.");
                if (baseDomain is not null && signed.Zone is not null
                    && !string.Equals(signed.Zone, baseDomain, StringComparison.Ordinal))
                    return (LeakResult.Inconclusive, signed.ObservedResolvers!,
                        "Collector attestation zone does not match the tested domain; possible replay.");
                // B216: internally inconsistent attestation — resolverCount must agree with the
                // observedResolvers list length.
                if (signed.ResolverCount.HasValue
                    && signed.ResolverCount.Value != signed.ObservedResolvers!.Count)
                    return (LeakResult.Inconclusive, signed.ObservedResolvers!,
                        "Collector attestation is internally inconsistent (resolverCount disagrees with observedResolvers).");
                var (sResult, sReason) = ReconcileResult(signed.Leaked!.Value, signed.ObservedResolvers!, expectedResolvers);
                return (sResult, signed.ObservedResolvers!, sReason);
            }
            if (sigValid == false)
                return (LeakResult.Inconclusive, outerObserved,
                    "Collector attestation signature is invalid; result cannot be trusted.");
            // B091 (sigValid == null — no signature): macOS parity — ALWAYS derive result
            // from the signed payload if available, never from unsigned outer fields.
            // Only fall back to outer fields when no signed payload exists at all.
            if (signedPayloadBase64 is not null)
            {
                var unsignedSigned = ParseSignedAttestation(signedPayloadBase64);
                if (unsignedSigned is not null)
                {
                    var (v, r) = ReconcileResult(unsignedSigned.Leaked!.Value, unsignedSigned.ObservedResolvers!, expectedResolvers);
                    return (v, unsignedSigned.ObservedResolvers!, r);
                }
            }
            return (LeakResult.Inconclusive, outerObserved,
                "Signed payload is missing or malformed; cannot derive a verified leak result.");
        }

        // MARK: - Result reconciliation

        /// <summary>
        /// Reconcile the collector's <c>leaked</c> boolean against its attested resolver set.
        /// The two must agree, or neither can be stood behind: a "leak" the collector cannot
        /// attribute to any resolver, or a "no leak" that nonetheless attests observed resolvers,
        /// is internally inconsistent and downgrades to Inconclusive rather than emitting a
        /// leak/no-leak the evidence does not support. Pure and offline-testable (no network).
        ///
        /// C031: <paramref name="expectedResolvers"/> is the caller's baseline of resolver egress
        /// IPs that are SUPPOSED to appear at the collector (e.g. the VPN provider's published
        /// resolver addresses). When provided, an observation set fully contained in the baseline
        /// is expected behavior, not a leak; only an observation OUTSIDE the baseline confirms one.
        /// Null/empty baseline preserves the original semantics (any observation = leak).
        /// </summary>
        public static (LeakResult Result, string? Reason) ReconcileResult(
            bool leaked, List<string> observedResolvers, IReadOnlyCollection<string>? expectedResolvers = null)
        {
            if (leaked && observedResolvers.Count == 0)
                return (LeakResult.Inconclusive, "Collector reported a leak but attested no resolver; cannot attribute.");
            if (!leaked && observedResolvers.Count > 0)
                return (LeakResult.Inconclusive, "Collector reported no leak yet attested observed resolvers; inconsistent.");
            if (leaked && expectedResolvers is { Count: > 0 })
            {
                var baseline = expectedResolvers as ISet<string> ?? new HashSet<string>(expectedResolvers);
                bool allExpected = observedResolvers.All(baseline.Contains);
                if (allExpected)
                    return (LeakResult.NotDetected,
                        "All observed resolvers are in your expected set; this is normal resolution, not a leak.");
            }
            return (leaked ? LeakResult.Confirmed : LeakResult.NotDetected, null);
        }

        // MARK: - Internal helpers

        /// <summary>Trigger queries for leak testing.</summary>
        public static async Task<Dictionary<string, bool>> TriggerQueriesAsync(
            string subdomain, List<string> servers)
        {
            var results = new Dictionary<string, bool>();
            foreach (var server in servers)
            {
                try
                {
                    var query = new DNSQuery(subdomain, 1, server);
                    var resp = await DNSResolver.QueryUDPDetailedAsync(query);
                    // B030 Fix A: count dispatch success ONLY for answered statuses.
                    // Timeout / ResponseIdMismatch / Malformed / TransportError / Truncated
                    // do NOT count as dispatched — the E007 all-failed guard must be reachable.
                    results[server] = resp.Status is DNSResponseStatus.Success
                        or DNSResponseStatus.NoData
                        or DNSResponseStatus.NXDomain
                        or DNSResponseStatus.Refused
                        or DNSResponseStatus.ServFail;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    results[server] = false;
                }
            }
            return results;
        }

        // MARK: - Collector HTTP calls

        /// <summary>
        /// The collector answered with a non-200 HTTP status. The status is CARRIED, not merely
        /// formatted into a message: the previous <c>HttpRequestException($"HTTP {code}")</c> left
        /// the status readable only by parsing English, so nothing ever classified it.
        /// Twin of macOS <c>CollectorError.badStatus</c>.
        /// </summary>
        internal sealed class CollectorStatusException : Exception
        {
            public int StatusCode { get; }
            public CollectorStatusException(int statusCode)
                : base($"The collector request failed with HTTP status {statusCode}.")
                => StatusCode = statusCode;
        }

        /// <summary>
        /// The collector answered 200 but the body did not decode. Twin of macOS
        /// <c>CollectorError.badJSON</c> / <c>CollectorError.notHTTP</c>.
        /// </summary>
        internal sealed class CollectorMalformedResponseException : Exception
        {
            public CollectorMalformedResponseException(Exception? inner = null)
                : base("The collector's response could not be read.", inner) { }
        }

        private class TokenResponse
        {
            [JsonPropertyName("token")]
            public string Token { get; set; } = string.Empty;
            [JsonPropertyName("subdomain")]
            public string Subdomain { get; set; } = string.Empty;
        }

        private class VerifyResponse
        {
            [JsonPropertyName("leaked")]
            public bool Leaked { get; set; }
            [JsonPropertyName("observedResolvers")]
            public List<string>? ObservedResolvers { get; set; }
            [JsonPropertyName("signature")]
            public string? Signature { get; set; }
            [JsonPropertyName("publicKey")]
            public string? PublicKey { get; set; }
            [JsonPropertyName("keyId")]
            public string? KeyId { get; set; }
            [JsonPropertyName("signedPayload")]
            public string? SignedPayload { get; set; }
        }

        /// <summary>
        /// Applies whichever credentials the caller supplied: a session token becomes an
        /// <c>Authorization: Bearer</c> header, a license key becomes <c>X-Syxx-License</c>.
        /// </summary>
        private static void ApplyAuth(HttpRequestMessage req, string? authToken, string? licenseKey)
        {
            if (authToken != null)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }
            if (licenseKey != null)
            {
                req.Headers.Add("X-Syxx-License", licenseKey);
            }
        }

        private static async Task<TokenResponse> RequestTokenAsync(
            HttpClient client, string collectorBaseURL, string baseDomain, string? authToken, string? licenseKey)
        {
            string url = collectorBaseURL.TrimEnd('/') + "/api/leak/token?zone=" +
                         Uri.EscapeDataString(baseDomain);
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            ApplyAuth(req, authToken, licenseKey);
            req.Content = new ByteArrayContent(Array.Empty<byte>());

            var response = await client.SendAsync(req);
            // Exactly 200, matching macOS: a 2xx that is not 200 (e.g. 204 No Content) carries no
            // token and must not be treated as success on one platform and failure on the other.
            if ((int)response.StatusCode != 200)
            {
                throw new CollectorStatusException((int)response.StatusCode);
            }
            var json = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonSerializer.Deserialize<TokenResponse>(json, _jsonOptions)
                       ?? throw new CollectorMalformedResponseException();
            }
            catch (JsonException ex)
            {
                throw new CollectorMalformedResponseException(ex);
            }
        }

        private static async Task<VerifyResponse> VerifyTokenAsync(
            HttpClient client, string collectorBaseURL, string token, string? authToken, string? licenseKey)
        {
            string url = collectorBaseURL.TrimEnd('/') + "/api/leak/verify?token=" +
                         Uri.EscapeDataString(token);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(req, authToken, licenseKey);  // server is stateless — re-send credentials on verify

            var response = await client.SendAsync(req);
            if ((int)response.StatusCode != 200)
            {
                throw new CollectorStatusException((int)response.StatusCode);
            }
            var json = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonSerializer.Deserialize<VerifyResponse>(json, _jsonOptions)
                       ?? throw new CollectorMalformedResponseException();
            }
            catch (JsonException ex)
            {
                throw new CollectorMalformedResponseException(ex);
            }
        }
    }

    /// <summary>
    /// SPKI certificate-pinning support for the leak-collector transport (D003).
    ///
    /// The pin is the base64-encoded SHA-256 of the leaf certificate's DER-encoded
    /// SubjectPublicKeyInfo — identical across platforms to macOS's SecCertificate SPKI hash and to
    /// <c>openssl x509 -pubkey | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | base64</c>
    /// (RFC 7469 "pin-sha256"). So the owner computes ONE pin value that works on Windows and macOS.
    /// </summary>
    public static class CollectorPinning
    {
        // These two strings are USER-VISIBLE and were approved by the user (2026-07-11).
        /// <summary>Reason surfaced when the collector's TLS identity could not be verified.</summary>
        public const string PinFailureReason =
            "Collector identity could not be verified (certificate pinning failed); this leak result cannot be trusted.";
        /// <summary>One-time caveat surfaced when no pins are configured (transparently unpinned).</summary>
        public const string UnconfiguredCaveat =
            "Collector-identity pinning is not configured; the collector connection is unpinned (standard TLS validation only).";

        /// <summary>
        /// The configured pin set (primary + backup, to survive certificate rotation), from the
        /// <c>SYXX_COLLECTOR_PINS</c> environment variable (the same pattern as
        /// <see cref="SyxxConfig.DimensionBaseURL"/>). Comma-, space-, or newline-separated. An
        /// empty set means unpinned.
        ///
        /// OWNER DELIVERABLE: the actual pin value(s) for the real collector are a deployment input
        /// (like <c>SYXX_COLLECTOR_PRIVATE_KEY</c>) and are intentionally NOT hardcoded here.
        /// </summary>
        public static ISet<string> ConfiguredPins()
        {
            // Read from persisted file first (Windows equivalent of UserDefaults),
            // then fall back to the environment variable — matching the Swift twin's
            // three-tier fallback (UserDefaults → env var).
            string raw = PersistedCollectorPins() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                raw = Environment.GetEnvironmentVariable("SYXX_COLLECTOR_PINS") ?? string.Empty;
            var parts = raw.Split(new[] { ',', ' ', '\n', '\t', '\r' },
                                  StringSplitOptions.RemoveEmptyEntries);
            return new HashSet<string>(parts);
        }

        /// <summary>
        /// Returns the persisted collector pins file contents, or null if none exists.
        /// Twin of the Swift UserDefaults-based config read in LeakDetector.swift.
        /// </summary>
        private static string? PersistedCollectorPins()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var pinFile = System.IO.Path.Combine(appData, "Syxx", "collector_spki_pins.txt");
                return System.IO.File.Exists(pinFile) ? System.IO.File.ReadAllText(pinFile) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// WS3-A.3 / D011: the configured Ed25519 signer trust set (base64 raw public keys), from
        /// <c>SYXX_COLLECTOR_TRUSTED_KEYS</c> — same deployment pattern as
        /// <see cref="ConfiguredPins"/>. Auto-loaded by <see cref="LeakDetector.RunCollectorTestAsync"/>
        /// when the caller passes no explicit set, so the signer-trust check engages in the shipped
        /// GUI and CLI without per-call-site wiring. Empty set = no trust restriction (any
        /// cryptographically valid signer accepted; SignatureKeyTrusted stays null).
        /// </summary>
        public static ISet<string> ConfiguredTrustedSigningKeys()
        {
            string raw = Environment.GetEnvironmentVariable("SYXX_COLLECTOR_TRUSTED_KEYS") ?? string.Empty;
            
            // WS3-02: fall back to persisted synced key if manual config is empty
            if (string.IsNullOrEmpty(raw))
            {
                raw = PersistedTrustedSigningKey() ?? string.Empty;
            }
            
            var parts = raw.Split(new[] { ',', ' ', '\n', '\t', '\r' },
                                  StringSplitOptions.RemoveEmptyEntries);
            return new HashSet<string>(parts);
        }

        // MARK: - WS3-02: Trust store sync from /api/pubkey

        /// <summary>
        /// Fetches the collector's public key from the well-known endpoint and persists it.
        /// </summary>
        /// <param name="collectorBaseURL">The base URL of the collector server.</param>
        /// <returns>The fetched public key, or null if the fetch failed.</returns>
        public static async Task<string?> SyncTrustedSigningKeyAsync(Uri collectorBaseURL)
        {
            try
            {
                using var client = LeakDetector.MakeCollectorClient(CollectorPinning.ConfiguredPins());
                var pubkeyUrl = new Uri(collectorBaseURL, "api/pubkey");
                var response = await client.GetAsync(pubkeyUrl);
                response.EnsureSuccessStatusCode();
                
                var json = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
                var publicKey = json.GetProperty("publicKey").GetString();
                var keyId = json.GetProperty("keyId").GetString();
                var rotationTimestamp = json.GetProperty("rotationTimestamp").GetInt32();
                
                if (publicKey == null || keyId == null)
                    return null;

                // B185: Validate the fetched public key is a valid 32-byte Ed25519 key
                // and that keyId matches SHA-256(key)[0:8] hex prefix.
                byte[] keyBytes;
                try
                {
                    keyBytes = Convert.FromBase64String(publicKey);
                }
                catch (FormatException)
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: public key is not valid base64, discarding");
                    return null;
                }
                if (keyBytes.Length != 32)
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: public key is {keyBytes.Length} bytes (expected 32), discarding");
                    return null;
                }
                var keyHash = System.Security.Cryptography.SHA256.HashData(keyBytes);
                var expectedKeyId = Convert.ToHexString(keyHash, 0, 4).ToLowerInvariant(); // first 8 hex chars = 4 bytes
                if (!string.Equals(keyId, expectedKeyId, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: keyId mismatch — got {keyId}, expected {expectedKeyId}, discarding");
                    return null;
                }
                
                // Persist to registry for offline use (Windows equivalent of UserDefaults)
                // For now, we'll use a simple file-based approach since registry access requires more setup
                // In production, this should use proper Windows registry or secure storage
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var configDir = System.IO.Path.Combine(appData, "Syxx");
                System.IO.Directory.CreateDirectory(configDir);
                var keyFile = System.IO.Path.Combine(configDir, "collector_trusted_key.txt");

                // M4: Key rotation surfacing — emit a diagnostic breadcrumb if the key changed.
                if (System.IO.File.Exists(keyFile))
                {
                    var previousKey = System.IO.File.ReadAllText(keyFile).Trim();
                    if (!string.Equals(previousKey, publicKey, StringComparison.Ordinal))
                    {
                        var previousPrefix = previousKey.Length > 8 ? previousKey[..8] : previousKey;
                        System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: trusted key rotated (previous keyId prefix: {previousPrefix}, new keyId: {keyId})");
                    }
                }
                System.IO.File.WriteAllText(keyFile, publicKey);
                
                return publicKey;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the persisted trusted signing key, or null if none exists.
        /// </summary>
        public static string? PersistedTrustedSigningKey()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var keyFile = System.IO.Path.Combine(appData, "Syxx", "collector_trusted_key.txt");
                if (System.IO.File.Exists(keyFile))
                {
                    return System.IO.File.ReadAllText(keyFile);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The pin (base64 SHA-256 of DER SubjectPublicKeyInfo) for a leaf certificate. Returns
        /// <c>null</c> on any failure — the caller treats that fail-closed as a mismatch.
        /// </summary>
        public static string? SpkiPin(X509Certificate2 cert)
        {
            try
            {
                byte[] spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
                return Convert.ToBase64String(SHA256.HashData(spki));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Pure, offline-testable fail-closed decision.
        /// <list type="bullet">
        /// <item>PKI chain validation MUST pass first (pinning is <em>in addition to</em> PKI, never
        /// a replacement — a self-signed impostor is rejected even if the attacker knew a pin).</item>
        /// <item>No pins configured → accept via standard validation.</item>
        /// <item>Pins configured → accept only if the leaf pin is in the set; otherwise refuse.</item>
        /// </list>
        /// </summary>
        public static bool Accept(bool chainOk, string? leafPin, ISet<string> pins)
        {
            if (!chainOk) return false;
            if (pins.Count == 0) return true;
            return leafPin != null && pins.Contains(leafPin);
        }
    }
}

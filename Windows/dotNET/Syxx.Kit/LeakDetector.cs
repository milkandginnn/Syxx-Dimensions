using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NSec.Cryptography;

namespace Syxx.Kit
{
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
}

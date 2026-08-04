using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NSec.Cryptography;

namespace Syxx.Kit
{
    // =================================================================================================
    // WARP_MANIFEST_V1 — signed-manifest content-integrity CLIENT engine (docs/warp-manifest-v1-spec.md).
    //
    // Behavioral twin of macOS/Sources/SyxxKit/WarpEngine.swift, oracle-checked against the shared
    // warp-manifest-vectors/ corpus. The client NEVER parses HTML and NEVER resolves a URL (§1): the
    // server is the only parser. This engine verifies the Ed25519 signature over the received bytes
    // BEFORE any JSON parse (§4), checks freshness/origin/nonce, fetches each DECLARED asset from the
    // client's own vantage under the declared redirect policy (§6), hashes raw bytes, compares to the
    // manifest, and applies the fail-closed conclusion lattice (§5). "verdict" is banned — result/conclusion.
    // =================================================================================================

    /// <summary>§4 signature-stage outcome. <c>Malformed</c> is reserved for an unparseable TRANSPORT
    /// ENVELOPE (a malformed INNER manifest after a valid signature is <see cref="WarpManifestState"/>).</summary>
    public enum WarpSignatureState { Verified, UntrustedSigner, InvalidSignature, Malformed }

    /// <summary>§4.4 inner-manifest parse outcome.</summary>
    public enum WarpManifestState { Parsed, Malformed, NotEvaluated }

    /// <summary>§4.5 freshness/origin/nonce outcome.</summary>
    public enum WarpFreshnessState { Fresh, Expired, FutureDated, OriginMismatch, ReplayNonceMismatch, NotEvaluated }

    /// <summary>Per-content comparison state. <c>FetchError</c> = client could not fetch (network/SSRF/
    /// redirect fail-closed); <c>FetchFailedDeclared</c> = server recorded fetch_failed (no sha256).
    /// Neither ever becomes a clean match.</summary>
    public enum WarpContentState { Match, Mismatch, FetchError, FetchFailedDeclared, NotEvaluated }

    /// <summary>§5 fail-closed conclusion lattice output.</summary>
    public enum WarpConclusion { TAMPERING_DETECTED, INCONCLUSIVE, VERIFIED_MATCH }

    /// <summary>§6 redirect policy (fail-closed).</summary>
    public enum WarpRedirectPolicy { None, SameOrigin, List }

    /// <summary>One declared asset's comparison outcome. <c>FetchedBytes</c> is DISPLAY-ONLY (retained
    /// for the diff overlay on a mismatch); it never feeds the lattice.</summary>
    public sealed class WarpAssetResult
    {
        public string Url { get; set; } = "";
        public string? DeclaredSha256 { get; set; }
        public string? FetchedSha256 { get; set; }
        public int? ByteLen { get; set; }
        public WarpContentState State { get; set; }
        public string AssetClass { get; set; } = "";
        public byte[]? FetchedBytes { get; set; }
    }

    /// <summary>The full client-side result of verifying one signed manifest. Retains display-only raw
    /// blobs so the UI stream can render the <c>TextDiff</c> overlay — this engine does NOT build it.</summary>
    public sealed class WarpManifestResult
    {
        public string OriginUrl { get; set; } = "";
        public string? FinalUrl { get; set; }
        public string? KeyId { get; set; }
        public WarpSignatureState SignatureState { get; set; }
        public WarpManifestState ManifestState { get; set; }
        public WarpFreshnessState FreshnessState { get; set; }
        public WarpContentState HtmlState { get; set; }
        public string? DeclaredHtmlSha256 { get; set; }
        public string? FetchedHtmlSha256 { get; set; }
        public List<WarpAssetResult> Assets { get; set; } = new List<WarpAssetResult>();
        public WarpConclusion Conclusion { get; set; }
        public string? InconclusiveReason { get; set; }
        /// <summary>Display-only: decoded manifest JSON text, populated ONLY after a valid signature.</summary>
        public string? RawManifestJson { get; set; }
        /// <summary>Display-only: locally-fetched origin HTML bytes, retained for the overlay diff.</summary>
        public byte[]? LocalHtmlBytes { get; set; }
    }

    /// <summary>What the injected fetch closure retrieves. The client fetches EXACTLY <c>Url</c> from
    /// its own vantage under <c>RedirectPolicy</c> (§6) and returns the terminal URL.</summary>
    public sealed class WarpAssetRequest
    {
        public string Url { get; set; } = "";
        public string? FinalUrl { get; set; }
        public WarpRedirectPolicy RedirectPolicy { get; set; }
        public List<string> AllowedRedirects { get; set; } = new List<string>();
        public string AssetClass { get; set; } = "";
        public bool IsHtml { get; set; }
    }

    /// <summary>Outcome of one client-vantage fetch. <c>Failed</c> = network error, SSRF block, or a
    /// forbidden redirect (fail closed). Otherwise raw envelope-decoded bytes + terminal URL.</summary>
    public sealed class WarpFetchOutcome
    {
        public bool Failed { get; private set; }
        public byte[] Bytes { get; private set; } = Array.Empty<byte>();
        public string TerminalUrl { get; private set; } = "";

        public static WarpFetchOutcome Fetched(byte[] bytes, string terminalUrl)
            => new WarpFetchOutcome { Failed = false, Bytes = bytes, TerminalUrl = terminalUrl };
        public static readonly WarpFetchOutcome FailedOutcome = new WarpFetchOutcome { Failed = true };
    }

    public static class WarpEngine
    {
        private static readonly SignatureAlgorithm Ed25519 = SignatureAlgorithm.Ed25519;

        /// <summary>B085-style 300 000 ms clock-skew tolerance, matching WitnessSigner.</summary>
        internal const ulong SkewToleranceMillis = 300_000;

        // ---- §5 conclusion lattice (one pure function) ----

        /// <summary>Fail-closed conclusion lattice (§5). Confirmed tampering (invalid signature, or a
        /// real byte mismatch under a verified+fresh manifest) OUTRANKS missing data; missing/
        /// unverifiable data never upgrades to a clean match.</summary>
        public static WarpConclusion Conclude(WarpSignatureState signature, WarpManifestState manifest,
            WarpFreshnessState freshness, WarpContentState html, IReadOnlyList<WarpContentState> assets)
        {
            if (signature == WarpSignatureState.InvalidSignature) return WarpConclusion.TAMPERING_DETECTED;
            if (signature == WarpSignatureState.UntrustedSigner || signature == WarpSignatureState.Malformed)
                return WarpConclusion.INCONCLUSIVE;
            // signature == Verified
            if (manifest != WarpManifestState.Parsed) return WarpConclusion.INCONCLUSIVE;
            if (freshness != WarpFreshnessState.Fresh) return WarpConclusion.INCONCLUSIVE;
            if (html == WarpContentState.Mismatch || assets.Any(a => a == WarpContentState.Mismatch))
                return WarpConclusion.TAMPERING_DETECTED;
            if (html != WarpContentState.Match) return WarpConclusion.INCONCLUSIVE;
            if (assets.Any(a => a != WarpContentState.Match)) return WarpConclusion.INCONCLUSIVE;
            return WarpConclusion.VERIFIED_MATCH;
        }

        // ---- §4 verification (mandatory sequence) ----

        /// <summary>Verify a signed manifest envelope (§4) and produce the full result. Fully
        /// deterministic and offline given the injected <paramref name="fetch"/> closure and
        /// <paramref name="nowMs"/> clock. <paramref name="pinnedKeys"/> maps
        /// <c>key_id -&gt; raw Ed25519 public-key bytes</c> (see <see cref="WarpManifestPinning"/>).</summary>
        public static async Task<WarpManifestResult> VerifyAsync(
            string originUrl,
            byte[] envelopeBytes,
            IReadOnlyDictionary<string, byte[]> pinnedKeys,
            string? nonce,
            ulong nowMs,
            Func<WarpAssetRequest, Task<WarpFetchOutcome>> fetch)
        {
            WarpManifestResult Stopped(WarpSignatureState sig, WarpManifestState man, WarpFreshnessState fresh,
                string? reason, string? keyId = null, string? rawJson = null,
                string? declaredHtmlSha = null, string? finalUrl = null)
            {
                var conclusion = Conclude(sig, man, fresh, WarpContentState.NotEvaluated, Array.Empty<WarpContentState>());
                return new WarpManifestResult
                {
                    OriginUrl = originUrl, FinalUrl = finalUrl, KeyId = keyId,
                    SignatureState = sig, ManifestState = man, FreshnessState = fresh,
                    HtmlState = WarpContentState.NotEvaluated, DeclaredHtmlSha256 = declaredHtmlSha,
                    Conclusion = conclusion, InconclusiveReason = reason, RawManifestJson = rawJson
                };
            }

            // §4.1 — parse the transport envelope { manifest_b64, signature_ed25519, key_id }.
            string manifestB64, sigB64, keyId;
            try
            {
                using var env = JsonDocument.Parse(envelopeBytes);
                var root = env.RootElement;
                if (!root.TryGetProperty("manifest_b64", out var mb) || mb.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("signature_ed25519", out var sb) || sb.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("key_id", out var kb) || kb.ValueKind != JsonValueKind.String)
                {
                    return Stopped(WarpSignatureState.Malformed, WarpManifestState.NotEvaluated,
                        WarpFreshnessState.NotEvaluated, "The manifest envelope could not be parsed.");
                }
                manifestB64 = mb.GetString()!; sigB64 = sb.GetString()!; keyId = kb.GetString()!;
            }
            catch
            {
                return Stopped(WarpSignatureState.Malformed, WarpManifestState.NotEvaluated,
                    WarpFreshnessState.NotEvaluated, "The manifest envelope could not be parsed.");
            }

            byte[] manifestBytes, sigBytes;
            try { manifestBytes = Convert.FromBase64String(manifestB64); }
            catch
            {
                return Stopped(WarpSignatureState.Malformed, WarpManifestState.NotEvaluated,
                    WarpFreshnessState.NotEvaluated, "manifest_b64 is not valid base64.", keyId);
            }
            try { sigBytes = Convert.FromBase64String(sigB64); }
            catch
            {
                return Stopped(WarpSignatureState.Malformed, WarpManifestState.NotEvaluated,
                    WarpFreshnessState.NotEvaluated, "signature_ed25519 is not valid base64.", keyId);
            }

            // §4.2 — resolve key_id in the PINNED set. Unknown key → untrustedSigner (never /api/pubkey).
            if (!pinnedKeys.TryGetValue(keyId, out var pubKeyRaw))
            {
                return Stopped(WarpSignatureState.UntrustedSigner, WarpManifestState.NotEvaluated,
                    WarpFreshnessState.NotEvaluated,
                    $"Manifest signed by key_id '{keyId}', which is not in the pinned trust set.", keyId);
            }
            PublicKey publicKey;
            try { publicKey = PublicKey.Import(Ed25519, pubKeyRaw, KeyBlobFormat.RawPublicKey); }
            catch
            {
                return Stopped(WarpSignatureState.UntrustedSigner, WarpManifestState.NotEvaluated,
                    WarpFreshnessState.NotEvaluated, $"Pinned public key for key_id '{keyId}' is unusable.", keyId);
            }

            // §4.3 — Ed25519-verify over the DECODED manifest bytes, BEFORE any JSON parse.
            if (!Ed25519.Verify(publicKey, manifestBytes, sigBytes))
            {
                return new WarpManifestResult
                {
                    OriginUrl = originUrl, KeyId = keyId,
                    SignatureState = WarpSignatureState.InvalidSignature,
                    ManifestState = WarpManifestState.NotEvaluated,
                    FreshnessState = WarpFreshnessState.NotEvaluated,
                    HtmlState = WarpContentState.NotEvaluated,
                    Conclusion = WarpConclusion.TAMPERING_DETECTED
                };
            }

            // §4.4 — only now parse the (authenticated) manifest JSON.
            string rawManifestJson = Encoding.UTF8.GetString(manifestBytes);
            JsonElement manifest;
            JsonDocument manifestDoc;
            try { manifestDoc = JsonDocument.Parse(manifestBytes); manifest = manifestDoc.RootElement; }
            catch
            {
                return Stopped(WarpSignatureState.Verified, WarpManifestState.Malformed,
                    WarpFreshnessState.NotEvaluated, "Signature verified but the manifest JSON is unparseable.",
                    keyId, rawManifestJson);
            }

            using (manifestDoc)
            {
                string manifestOrigin = GetString(manifest, "origin_url") ?? "";
                string? finalUrl = GetString(manifest, "final_url");
                string? declaredHtmlSha = GetString(manifest, "html_sha256")?.ToLowerInvariant();
                ulong? timestamp = GetUInt(manifest, "timestamp");
                ulong freshnessSeconds = GetUInt(manifest, "freshness_seconds") ?? 0;
                var policy = ParsePolicy(GetString(manifest, "redirect_policy"));
                var allowedRedirects = GetStringArray(manifest, "allowed_redirects");
                string? manifestNonce = GetString(manifest, "nonce");

                // §4.5 — freshness / origin / nonce (only meaningful on a verified, parsed manifest).
                var freshness = EvaluateFreshness(nowMs, timestamp, freshnessSeconds, manifestOrigin, originUrl,
                    nonce, manifestNonce);
                if (freshness != WarpFreshnessState.Fresh)
                {
                    return Stopped(WarpSignatureState.Verified, WarpManifestState.Parsed, freshness,
                        FreshnessReason(freshness), keyId, rawManifestJson, declaredHtmlSha, finalUrl);
                }

                // §4.6 — fetch the origin HTML, hash, compare to html_sha256.
                var htmlState = WarpContentState.NotEvaluated;
                string? fetchedHtmlSha = null;
                byte[]? localHtmlBytes = null;
                if (declaredHtmlSha != null)
                {
                    var req = new WarpAssetRequest
                    {
                        Url = string.IsNullOrEmpty(manifestOrigin) ? originUrl : manifestOrigin,
                        FinalUrl = finalUrl, RedirectPolicy = policy,
                        AllowedRedirects = allowedRedirects, AssetClass = "HTML", IsHtml = true
                    };
                    var outcome = await fetch(req).ConfigureAwait(false);
                    if (outcome.Failed) htmlState = WarpContentState.FetchError;
                    else if (!RedirectReconciles(policy, req.Url, finalUrl, outcome.TerminalUrl))
                        htmlState = WarpContentState.FetchError;
                    else
                    {
                        var sha = Sha256Hex(outcome.Bytes);
                        fetchedHtmlSha = sha; localHtmlBytes = outcome.Bytes;
                        htmlState = (sha == declaredHtmlSha) ? WarpContentState.Match : WarpContentState.Mismatch;
                    }
                }
                else htmlState = WarpContentState.FetchError;

                // §4.6 — fetch every DECLARED asset, hash raw bytes, compare.
                var assetResults = new List<WarpAssetResult>();
                if (manifest.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in assetsEl.EnumerateArray())
                    {
                        string url = GetString(a, "url") ?? "";
                        string cls = GetString(a, "class") ?? "";
                        string? declaredSha = GetString(a, "sha256")?.ToLowerInvariant();
                        string? assetFinal = GetString(a, "final_url");
                        bool serverFetchFailed = string.Equals(GetString(a, "fetch_status"), "fetch_failed",
                            StringComparison.OrdinalIgnoreCase);

                        if (serverFetchFailed || declaredSha == null)
                        {
                            assetResults.Add(new WarpAssetResult
                            {
                                Url = url, DeclaredSha256 = declaredSha, AssetClass = cls,
                                State = WarpContentState.FetchFailedDeclared
                            });
                            continue;
                        }

                        var req = new WarpAssetRequest
                        {
                            Url = url, FinalUrl = assetFinal, RedirectPolicy = policy,
                            AllowedRedirects = allowedRedirects, AssetClass = cls, IsHtml = false
                        };
                        var outcome = await fetch(req).ConfigureAwait(false);
                        if (outcome.Failed)
                        {
                            assetResults.Add(new WarpAssetResult
                            {
                                Url = url, DeclaredSha256 = declaredSha, AssetClass = cls,
                                State = WarpContentState.FetchError
                            });
                        }
                        else if (!RedirectReconciles(policy, url, assetFinal, outcome.TerminalUrl))
                        {
                            assetResults.Add(new WarpAssetResult
                            {
                                Url = url, DeclaredSha256 = declaredSha, AssetClass = cls,
                                ByteLen = outcome.Bytes.Length, State = WarpContentState.FetchError
                            });
                        }
                        else
                        {
                            var sha = Sha256Hex(outcome.Bytes);
                            bool matches = sha == declaredSha;
                            assetResults.Add(new WarpAssetResult
                            {
                                Url = url, DeclaredSha256 = declaredSha, FetchedSha256 = sha,
                                ByteLen = outcome.Bytes.Length, AssetClass = cls,
                                State = matches ? WarpContentState.Match : WarpContentState.Mismatch,
                                FetchedBytes = matches ? null : outcome.Bytes
                            });
                        }
                    }
                }

                var assetStates = assetResults.Select(r => r.State).ToList();
                var conclusion = Conclude(WarpSignatureState.Verified, WarpManifestState.Parsed,
                    WarpFreshnessState.Fresh, htmlState, assetStates);
                string? reason = null;
                if (conclusion == WarpConclusion.INCONCLUSIVE)
                {
                    if (htmlState == WarpContentState.FetchError)
                        reason = "The origin page could not be fetched from this vantage.";
                    else if (assetStates.Contains(WarpContentState.FetchError))
                        reason = "One or more declared assets could not be fetched from this vantage.";
                    else if (assetStates.Contains(WarpContentState.FetchFailedDeclared))
                        reason = "The manifest declared an asset the server could not fetch; missing data is never a clean pass.";
                    else reason = "Content could not be conclusively verified.";
                }

                return new WarpManifestResult
                {
                    OriginUrl = originUrl, FinalUrl = finalUrl, KeyId = keyId,
                    SignatureState = WarpSignatureState.Verified, ManifestState = WarpManifestState.Parsed,
                    FreshnessState = WarpFreshnessState.Fresh, HtmlState = htmlState,
                    DeclaredHtmlSha256 = declaredHtmlSha, FetchedHtmlSha256 = fetchedHtmlSha,
                    Assets = assetResults, Conclusion = conclusion, InconclusiveReason = reason,
                    RawManifestJson = rawManifestJson, LocalHtmlBytes = localHtmlBytes
                };
            }
        }

        // ---- Freshness + redirect helpers ----

        internal static WarpFreshnessState EvaluateFreshness(ulong nowMs, ulong? timestamp, ulong freshnessSeconds,
            string manifestOrigin, string requestedOrigin, string? clientNonce, string? manifestNonce)
        {
            if (timestamp == null) return WarpFreshnessState.Expired;
            ulong ts = timestamp.Value;
            if (nowMs > ts && (nowMs - ts) > freshnessSeconds * 1000) return WarpFreshnessState.Expired;
            if (ts > nowMs && (ts - nowMs) > SkewToleranceMillis) return WarpFreshnessState.FutureDated;
            if (!string.Equals(manifestOrigin, requestedOrigin, StringComparison.Ordinal))
                return WarpFreshnessState.OriginMismatch;
            if (clientNonce != null && !string.Equals(manifestNonce, clientNonce, StringComparison.Ordinal))
                return WarpFreshnessState.ReplayNonceMismatch;
            return WarpFreshnessState.Fresh;
        }

        private static string FreshnessReason(WarpFreshnessState f) => f switch
        {
            WarpFreshnessState.Expired => "The manifest is past its freshness window (expired).",
            WarpFreshnessState.FutureDated => "The manifest timestamp is in the future beyond the allowed clock skew.",
            WarpFreshnessState.OriginMismatch => "The manifest origin does not match the requested origin.",
            WarpFreshnessState.ReplayNonceMismatch => "The manifest nonce does not echo the client nonce (possible replay).",
            _ => "The manifest freshness could not be confirmed."
        };

        /// <summary>§6 terminal-URL reconciliation. <c>None</c> forbids any redirect; <c>SameOrigin</c>/
        /// <c>List</c> require the terminal to equal the declared <c>final_url</c>.</summary>
        internal static bool RedirectReconciles(WarpRedirectPolicy policy, string requestUrl, string? finalUrl, string terminalUrl)
        {
            switch (policy)
            {
                case WarpRedirectPolicy.None:
                    return string.Equals(terminalUrl, requestUrl, StringComparison.Ordinal);
                default:
                    if (finalUrl != null) return string.Equals(terminalUrl, finalUrl, StringComparison.Ordinal);
                    return string.Equals(terminalUrl, requestUrl, StringComparison.Ordinal);
            }
        }

        public static WarpRedirectPolicy ParsePolicy(string? raw) => (raw ?? "").ToLowerInvariant() switch
        {
            "same-origin" or "sameorigin" => WarpRedirectPolicy.SameOrigin,
            "list" => WarpRedirectPolicy.List,
            _ => WarpRedirectPolicy.None
        };

        // ---- Small helpers ----

        internal static string Sha256Hex(byte[] bytes)
            => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static string? GetString(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static ulong? GetUInt(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && ulong.TryParse(v.GetString(), out var s)) return s;
            return null;
        }

        private static List<string> GetStringArray(JsonElement el, string name)
        {
            var outList = new List<string>();
            if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var item in v.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) outList.Add(item.GetString()!);
            return outList;
        }

        // ---- §9 diff overlay (display only, never feeds the lattice) ----

        /// <summary>Fetch the server's byte-exact CLEAN copy of ONE asset for the side-by-side overlay
        /// via <c>GET /api/warp/raw?url=&lt;abs&gt;&amp;raw=1</c> through the hardened DimensionClient
        /// path. Returns raw bytes + the server-declared SHA-256, or null on failure. NEVER mutates
        /// bytes and NEVER feeds the conclusion (§9).</summary>
        public static Task<(byte[] Bytes, string? Sha256)?> FetchServerCleanAssetAsync(
            string baseUrl, string absoluteUrl, string? licenseKey = null)
            => DimensionClient.WarpRawAssetAsync(baseUrl, absoluteUrl, licenseKey);

        // ---- Production wiring (fetch the envelope + verify with the SSRF-safe vantage fetch) ----

        /// <summary>End-to-end production check: pin the trust set out of band (§7), fetch the signed
        /// manifest envelope from the server, and verify it with the SSRF-safe client-vantage fetch.
        /// No pinned key → <c>UntrustedSigner</c> → INCONCLUSIVE; unreachable server → INCONCLUSIVE.</summary>
        public static async Task<WarpManifestResult> RunVantageCheckAsync(string origin, string baseUrl,
            string? licenseKey = null, string? nonce = null,
            IReadOnlyDictionary<string, byte[]>? pinnedKeys = null, ulong? nowMs = null)
        {
            var pinned = pinnedKeys ?? WarpManifestPinning.ConfiguredTrustedManifestKeys();
            ulong now = nowMs ?? (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var envelope = await DimensionClient.WarpManifestAsync(baseUrl, origin, nonce, licenseKey);
            if (envelope == null)
            {
                return new WarpManifestResult
                {
                    OriginUrl = origin,
                    SignatureState = WarpSignatureState.Malformed,
                    ManifestState = WarpManifestState.NotEvaluated,
                    FreshnessState = WarpFreshnessState.NotEvaluated,
                    HtmlState = WarpContentState.NotEvaluated,
                    Conclusion = WarpConclusion.INCONCLUSIVE,
                    InconclusiveReason = "The signed manifest could not be retrieved from the server."
                };
            }
            return await VerifyAsync(origin, envelope, pinned, nonce, now, VantageFetchAsync);
        }

        /// <summary>
        /// Hardened client-vantage fetcher: the connection is pinned to a SSRF-VALIDATED public IP
        /// while TLS SNI + certificate validation stay bound to the ORIGINAL HOSTNAME (the request URI
        /// keeps the hostname; only the socket dials the IP — the "validated-IP connection, hostname
        /// TLS" behavior). Auto-redirect is off; the §6 redirect policy is applied manually per hop.
        /// </summary>
        private static readonly HttpClient _vantageHttp = CreateVantageClient();

        private static HttpClient CreateVantageClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = async (ctx, ct) =>
                {
                    string host = ctx.DnsEndPoint.Host;
                    int port = ctx.DnsEndPoint.Port;
                    string? ip = await DimensionClient.ResolveValidatedPublicIpAsync(host).ConfigureAwait(false);
                    if (ip == null)
                        throw new IOException("SSRF protection: host does not resolve to a public IP address.");
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(IPAddress.Parse(ip), port, ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                }
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        internal static async Task<WarpFetchOutcome> VantageFetchAsync(WarpAssetRequest req)
        {
            try
            {
                string current = req.Url;
                for (int hop = 0; hop < 6; hop++)
                {
                    if (!Uri.TryCreate(current, UriKind.Absolute, out var u)
                        || (u.Scheme != "http" && u.Scheme != "https"))
                        return WarpFetchOutcome.FailedOutcome;

                    using var msg = new HttpRequestMessage(HttpMethod.Get, current);
                    using var resp = await _vantageHttp.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead)
                                                       .ConfigureAwait(false);
                    int sc = (int)resp.StatusCode;
                    if (sc >= 300 && sc < 400)
                    {
                        var loc = resp.Headers.Location;
                        if (loc == null) return WarpFetchOutcome.FailedOutcome;
                        var target = loc.IsAbsoluteUri ? loc : new Uri(u, loc);
                        bool ok = req.RedirectPolicy switch
                        {
                            WarpRedirectPolicy.SameOrigin =>
                                target.Scheme == u.Scheme && target.Host == u.Host && target.Port == u.Port,
                            WarpRedirectPolicy.List => req.AllowedRedirects.Contains(target.AbsoluteUri),
                            _ => false   // None → no redirect permitted (fail closed).
                        };
                        if (!ok) return WarpFetchOutcome.FailedOutcome;
                        current = target.AbsoluteUri;
                        continue;
                    }
                    if (sc < 200 || sc >= 300) return WarpFetchOutcome.FailedOutcome;
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return WarpFetchOutcome.Fetched(bytes, current);
                }
                return WarpFetchOutcome.FailedOutcome;   // too many hops → fail closed.
            }
            catch
            {
                return WarpFetchOutcome.FailedOutcome;
            }
        }
    }
}

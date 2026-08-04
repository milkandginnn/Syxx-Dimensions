using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// <summary>The zone's own authoritative answer for a name, from /api/authoritative.</summary>
    public class GroundTruth
    {
        [JsonPropertyName("name")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("conclusion")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("authoritative")]
        public GroundTruthAuthoritative? Authoritative { get; set; }
    }

    public class GroundTruthAuthoritative
    {
        [JsonPropertyName("answers")]
        public List<string> Answers { get; set; } = new List<string>();

        [JsonPropertyName("rcode")]
        public int Rcode { get; set; }

        [JsonPropertyName("signed")]
        public bool? Signed { get; set; }
    }

    /// <summary>Assessment from /api/diff — the app's own answers vs. authoritative ground truth.</summary>
    public class DiffAssessment
    {
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("match")]
        public bool? Match { get; set; }

        [JsonPropertyName("assessment")]
        public string Assessment { get; set; } = string.Empty;

        [JsonPropertyName("reference")]
        public string Reference { get; set; } = string.Empty;
    }

    public static class DimensionClient
    {
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Server-confirmed authoritative answer for domain/qtype. Canonicalizes domain (C004).</summary>
        public static async Task<GroundTruth?> AuthoritativeAsync(string baseURL, string domain, string type = "A", string? licenseKey = null)
        {
            var canonical = DomainCanonical.CanonicalizeOrNull(domain);
            if (canonical is null) return null;
            try
            {
                string url = $"{baseURL.TrimEnd('/')}/api/authoritative?domain={Uri.EscapeDataString(canonical)}&type={Uri.EscapeDataString(type)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(licenseKey))
                {
                    // D027 parity with macOS DimensionClient.swift:84 — reject non-https before
                    // attaching credentials, so the license key never travels in cleartext. Fail
                    // closed: a non-https base URL with a license key present aborts the request.
                    if (!string.Equals(req.RequestUri?.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                        return null;
                    req.Headers.Add("X-Syxx-License", licenseKey);
                }

                var response = await _http.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GroundTruth>(json, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Ask the server to diff the app's own localAnswers against ground truth. Canonicalizes domain (C004).</summary>
        public static async Task<DiffAssessment?> DiffAsync(string baseURL, string domain, string type, List<string> localAnswers, string? licenseKey = null)
        {
            var canonical = DomainCanonical.CanonicalizeOrNull(domain);
            if (canonical is null) return null;
            try
            {
                string url = $"{baseURL.TrimEnd('/')}/api/diff";
                var payload = new Dictionary<string, object>
                {
                    ["domain"] = canonical,
                    ["type"] = type,
                    ["localAnswers"] = localAnswers
                };
                string bodyJson = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrEmpty(licenseKey))
                {
                    // D027 parity with macOS DimensionClient.swift:84 — reject non-https before
                    // attaching the license key so it never travels in cleartext. Fail closed.
                    if (!string.Equals(req.RequestUri?.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                        return null;
                    req.Headers.Add("X-Syxx-License", licenseKey);
                }

                var response = await _http.SendAsync(req);
                if (!response.IsSuccessStatusCode)
                    return null;

                var respJson = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<DiffAssessment>(respJson, _jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        // ---- WARP_MANIFEST_V1 transport (spec §2 + §9) ----

        /// <summary>
        /// Hardened HttpClient for warp fetches: the connection is pinned to a SSRF-VALIDATED public
        /// IP (D019 — closes the TOCTOU/DNS-rebind window), while TLS SNI + certificate validation
        /// stay bound to the ORIGINAL HOSTNAME because the request URI keeps the hostname and only the
        /// socket dials the IP. This is the C# twin of the corrected macOS behavior (the old code
        /// rewrote the URL host to the IP literal, which bound TLS validation to the IP). Redirects
        /// are disabled here; §6 redirect policy is enforced by the caller.
        /// </summary>
        private static readonly HttpClient _hardenedHttp = CreateHardenedClient();

        private static HttpClient CreateHardenedClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = async (ctx, ct) =>
                {
                    string host = ctx.DnsEndPoint.Host;
                    int port = ctx.DnsEndPoint.Port;
                    string? ip = await ResolveValidatedPublicIpAsync(host).ConfigureAwait(false);
                    if (ip == null)
                        throw new IOException("SSRF protection: host does not resolve to a public IP address.");
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(IPAddress.Parse(ip), port, ct).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        }

        /// <summary>Resolve <paramref name="host"/> to a validated PUBLIC IP (or null). Mirrors the
        /// SSRF discipline used elsewhere: an IP literal is classified directly; a hostname is resolved
        /// through the system resolver and every returned address must be public. Fail closed.</summary>
        internal static async Task<string?> ResolveValidatedPublicIpAsync(string host)
        {
            var direct = IPClassifier.Classify(host);
            if (direct != IPAddressClass.Invalid)
                return direct == IPAddressClass.PublicRoutable ? host : null;

            var resolvers = NetworkKeyState.GetSystemDNSServers();
            if (resolvers.Count == 0) return null;
            try
            {
                var a = await DNSResolver.QueryUDPAsync(new DNSQuery(host, 1, resolvers[0]));
                var aaaa = await DNSResolver.QueryUDPAsync(new DNSQuery(host, 28, resolvers[0]));
                var ips = a.Where(r => r.Type == 1).Concat(aaaa.Where(r => r.Type == 28)).Select(r => r.Rdata).ToList();
                if (ips.Count == 0) return null;
                foreach (var ip in ips)
                    if (IPClassifier.Classify(ip) != IPAddressClass.PublicRoutable) return null;
                return ips[0];
            }
            catch
            {
                return null;
            }
        }

        /// <summary>§2 — fetch the signed manifest envelope for <paramref name="origin"/>:
        /// <c>GET /api/warp/manifest?url=&lt;origin&gt;[&amp;nonce=&lt;nonce&gt;]</c> (Pro-gated). Returns
        /// the raw envelope bytes on HTTP 200, else null. The license key is attached only over HTTPS.</summary>
        public static async Task<byte[]?> WarpManifestAsync(string baseUrl, string origin, string? nonce = null, string? licenseKey = null)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/api/warp/manifest?url={Uri.EscapeDataString(origin)}";
                if (!string.IsNullOrEmpty(nonce)) url += $"&nonce={Uri.EscapeDataString(nonce)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(licenseKey))
                {
                    if (!string.Equals(req.RequestUri?.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return null;
                    req.Headers.Add("X-Syxx-License", licenseKey);
                }
                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsByteArrayAsync();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>§9 — fetch the server's byte-exact CLEAN copy of ONE asset for the diff overlay:
        /// <c>GET /api/warp/raw?url=&lt;abs&gt;&amp;raw=1</c> (byte-exact mode: <c>application/octet-stream</c>
        /// + <c>X-Syxx-Warp-Sha256</c>). Returns raw bytes + the server-declared SHA-256 (display only —
        /// never re-hashed, never feeds the lattice). Uses the hardened IP-pinned/hostname-TLS client.</summary>
        public static async Task<(byte[] Bytes, string? Sha256)?> WarpRawAssetAsync(string baseUrl, string absoluteUrl, string? licenseKey = null)
        {
            try
            {
                string url = $"{baseUrl.TrimEnd('/')}/api/warp/raw?url={Uri.EscapeDataString(absoluteUrl)}&raw=1";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(licenseKey))
                {
                    if (!string.Equals(req.RequestUri?.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return null;
                    req.Headers.Add("X-Syxx-License", licenseKey);
                }
                using var resp = await _hardenedHttp.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                string? sha = resp.Headers.TryGetValues("X-Syxx-Warp-Sha256", out var vals) ? vals.FirstOrDefault() : null;
                return (bytes, sha);
            }
            catch
            {
                return null;
            }
        }
    }
}

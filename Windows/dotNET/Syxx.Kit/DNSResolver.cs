// DNSResolver.cs — data model types (DNSQuery, DNSRecord, DNSResponse, etc.) and
// orchestration methods (QueryUDP*, ResolveSecurely*, QueryDoH*).
// B005: wire-format parsing lives in DNSResolver.Parsing.cs; low-level transport in DNSResolver.Transport.cs.

using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Syxx.Kit
{
    public class DNSQuery
    {
        public string Name { get; }
        public ushort Type { get; }
        public string Server { get; }
        public ushort Port { get; }
        /// Query CLASS. Defaults to 1 (IN). Set to 3 (CHAOS) for resolver-fingerprinting queries such as
        /// version.bind/hostname.bind TXT (datapoint F2). Additive + defaulted -> every existing call site
        /// keeps IN semantics unchanged.
        public ushort QClass { get; }
        /// When non-null, the query carries an EDNS0 OPT record advertising this UDP payload size (datapoint
        /// F1). null = no OPT (classic query, unchanged behaviour).
        public ushort? Edns0UDPSize { get; }
        /// When set (with Edns0UDPSize), the OPT record carries an EDNS Client Subnet option for the
        /// /24 of this IPv4 address (datapoint F10). Used to probe whether the resolver forwards ECS.
        public string? Edns0ClientSubnetV4 { get; }
        /// When true (requires Edns0UDPSize), the OPT record sets the DNSSEC OK (DO) bit so the resolver
        /// returns the covering RRSIG records. Without it a resolver returns DNSKEY but no RRSIG, and a
        /// bare QTYPE=RRSIG query is not answered by real resolvers (A030/A036).
        public bool Edns0DODNSSEC { get; }

        public DNSQuery(string name, ushort type = 1, string server = "8.8.8.8", ushort port = 53,
                        ushort qclass = 1, ushort? edns0UDPSize = null, string? edns0ClientSubnetV4 = null,
                        bool edns0DODNSSEC = false)
        {
            Name = name;
            Type = type;
            Server = server;
            Port = port;
            QClass = qclass;
            Edns0UDPSize = edns0UDPSize;
            Edns0ClientSubnetV4 = edns0ClientSubnetV4;
            Edns0DODNSSEC = edns0DODNSSEC;
        }
    }

    public class DNSRecord
    {
        public string Name { get; set; } = string.Empty;
        public ushort Type { get; set; }
        public uint Ttl { get; set; }
        public string Rdata { get; set; } = string.Empty;

        /// Human-readable type name (e.g. "A", "AAAA", "MX") for display.
        /// Twin of macOS DNSRecord.typeName (DNSResolver.swift) — keep the tables identical.
        public string TypeName => Type switch
        {
            1 => "A",
            2 => "NS",
            5 => "CNAME",
            6 => "SOA",
            12 => "PTR",
            15 => "MX",
            16 => "TXT",
            28 => "AAAA",
            33 => "SRV",
            48 => "DNSKEY",
            65 => "HTTPS",
            257 => "CAA",
            _ => $"TYPE{Type}"
        };
    }

    /// <summary>
    /// The distinct outcomes a DNS lookup can produce. Keeping these separate prevents a network
    /// failure from being mistaken for an authoritative "no such record" answer.
    /// </summary>
    /// <remarks>
    /// [B251] The converter is attached to the TYPE, not to a caller's JsonSerializerOptions, so
    /// every reader and writer of this enum — CLI, GUI, tests — uses the cross-platform wire
    /// spelling without having to remember to opt in. That is the defect: `syxx witness verify`
    /// built its own bare `JsonSerializerOptions`, so a macOS-produced snapshot could not be read
    /// at all. DECLARATION ORDER IS ALSO A WIRE CONTRACT (older Windows builds wrote the ordinal)
    /// and must stay identical to macOS/Sources/SyxxKit/DNSResolver.swift:96-107.
    /// </remarks>
    [JsonConverter(typeof(DNSResponseStatusJsonConverter))]
    public enum DNSResponseStatus
    {
        Success,
        NoData,
        NXDomain,
        ServFail,
        Refused,
        Timeout,
        Truncated,
        ResponseIdMismatch,
        MalformedResponse,
        TransportError,
        RcodeOther
    }

    public enum SecureResolutionResult
    {
        Secure,
        Inconclusive
    }

    public class SecureResolutionResponse
    {
        public SecureResolutionResult Result { get; set; }
        public List<DNSRecord> Records { get; set; } = new List<DNSRecord>();
        public string Provenance { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// EDNS0 characteristics read back from a response's OPT pseudo-record (datapoint F1). Its mere
    /// presence means the resolver speaks EDNS0; null on DNSResponse.Edns means it did not echo OPT.
    public record EDNSResponseInfo(
        ushort UdpPayloadSize,          // advertised requestor UDP size (OPT CLASS field)
        byte Version,                   // EDNS version (0 for EDNS0)
        byte ExtendedRCode,             // upper 8 bits of the extended RCODE (16 = BADVERS)
        byte? EcsScopePrefixLength      // SCOPE PREFIX-LENGTH of an echoed ECS option (F10); null if no ECS
    );

    public class DNSResponse
    {
        public DNSResponseStatus Status { get; set; }
        public List<DNSRecord> Records { get; set; } = new List<DNSRecord>();
        /// Authority-section records (SOA / NSEC / NSEC3 / NS), preserved so a genuine authenticated
        /// negative answer can be told apart from an injected block-page NXDOMAIN (datapoint A6).
        /// Empty on positive answers and transport failures. Additive + defaulted -> older code and
        /// any persisted responses stay valid. Mirrors the macOS DNSResponse.authorityRecords contract.
        public List<DNSRecord> AuthorityRecords { get; set; } = new List<DNSRecord>();
        /// EDNS0 info from the response's OPT record (datapoint F1); null if the resolver did not echo
        /// OPT (no EDNS0 support) or on transport failure. Additive + defaulted.
        public EDNSResponseInfo? Edns { get; set; }
        /// B073/B071 — EDNS presence is tri-state, not a bool. Edns != null = OPT present and parsed;
        /// Edns == null && !EdnsMalformed = OPT genuinely absent (legacy resolver, no EDNS0);
        /// Edns == null && EdnsMalformed = the additional section declared records but the OPT/section
        /// could not be parsed, so EDNS support is UNKNOWN (inconclusive) — never read this as "absent".
        /// Parity with macOS DNSResponse.ednsMalformed.
        public bool EdnsMalformed { get; set; }
        /// A6/B071 — the authority section declared records (NSCOUNT > 0) but not all could be parsed.
        /// A negative answer with a malformed authority is inconclusive, not a genuine "bare" denial
        /// (which is an EMPTY authority). Parity with macOS DNSResponse.authorityMalformed.
        public bool AuthorityMalformed { get; set; }
        /// B070 — in-class address RRs whose owner name is neither the question name nor a CNAME
        /// target reachable from it. These off-name/smuggled records are dropped from Records and
        /// counted here. Parity with macOS DNSResponse.offNameAnswerCount.
        public int OffNameAnswerCount { get; set; }
        public ushort ResponseCode { get; set; }
        public bool IsAuthoritative { get; set; }
        public bool IsTruncated { get; set; }
        public bool RecursionAvailable { get; set; }
        public bool AuthenticatedData { get; set; }
        public int OffTypeAnswerCount { get; set; }
        public string? ErrorMessage { get; set; }
        /// Raw wire size of the response datagram in bytes, when known (used for amplification-factor
        /// measurement - Portal Open Resolver probe). null on injected/mock responses. Additive + defaulted.
        public int? ResponseByteLength { get; set; }
        /// True when the query could not reach or parse a resolver answer at all
        /// (as opposed to receiving a valid negative answer such as NXDOMAIN/NODATA).
        /// Parity with macOS DNSResponse.didFail.
        public bool DidFail => Status is DNSResponseStatus.Timeout
            or DNSResponseStatus.TransportError
            or DNSResponseStatus.MalformedResponse
            or DNSResponseStatus.ResponseIdMismatch
            or DNSResponseStatus.Truncated
            or DNSResponseStatus.ServFail
            or DNSResponseStatus.Refused;
    }

    public static partial class DNSResolver
    {
        // B319: removed per-instance Timeout from the shared HttpClient. A static 8-second
        // timeout was global across all DoH queries and could not be overridden per-call without
        // creating a new client (which causes socket exhaustion). Per-call timeouts are now
        // applied via CancellationTokenSource in QueryDoHDetailedAsync below.
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        /// Default timeout for DoH queries. Callers can override by passing a CancellationToken
        /// with their own deadline.
        private static readonly TimeSpan DefaultDoHTimeout = TimeSpan.FromSeconds(8);

        public static async Task<List<DNSRecord>> QueryUDPAsync(DNSQuery query, CancellationToken ct = default)
        {
            return (await QueryUDPDetailedAsync(query, ct)).Records;
        }

        // B318: increased from 2 to 3 — single retry insufficient against attacker sending
        // spoofed response per attempt. Random jitter added between retries below.
        private const int MaxUDPAttempts = 3;

        public static async Task<DNSResponse> QueryUDPDetailedAsync(DNSQuery query, CancellationToken ct = default)
        {
            // PII: the queried NAME is the user's browsing history and is never exported; only
            // its class and shape go on the wire. A DNS-integrity tool must not become a second
            // surveillance channel. (macOS twin: DNSResolver.redactedAttrs)
            using var span = SyxxTracer.StartSpan("dns.query.udp", ActivityKind.Client,
                TelemetryRedaction.SpanAttrs(query.Name, query.Type, "udp",
                                             resolver: query.Server, port: (int)query.Port));
            string? lastError = null;
            // C007: track spoofed packets across attempts so the observation survives a later
            // deadline expiry (parity with macOS DNSResolver.swift:422-535). A question-mismatch
            // or ID-mismatch is positive evidence of injection and must surface as
            // ResponseIdMismatch, never collapse into a plain Timeout.
            int spoofCount = 0;
            for (int attempt = 0; attempt < MaxUDPAttempts; attempt++)
            {
                // F006: check for cooperative cancellation at transport layer
                if (ct.IsCancellationRequested)
                {
                    span?.SetStatus(ActivityStatusCode.Error, "cancelled");
                    return new DNSResponse { Status = DNSResponseStatus.Timeout, ErrorMessage = "cancelled" };
                }

                byte[] packet;
                ushort transactionId;
                try
                {
                    packet = MakeQueryPacket(query.Name, query.Type, out transactionId,
                        qclass: query.QClass,
                        edns0UDPSize: query.Edns0UDPSize,
                        edns0ClientSubnetV4: query.Edns0ClientSubnetV4,
                        dnssecOK: query.Edns0DODNSSEC);
                }
                // C131: catch ArgumentException alongside FormatException — ToAsciiName throws
                // ArgumentException for unencodable IDN names, and it must not propagate out of
                // the query builder. macOS returns .malformedResponse for both.
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    span?.SetStatus(ActivityStatusCode.Error, "malformed packet");
                    return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = ex.Message };
                }

                try
                {
                    var response = await SendUDPAsync(packet, query.Server, query.Port);
                    var parsed = ParseResponsePacketDetailed(response, query.Type, transactionId);

                    // KDNS-02: validate response question section matches query
                    if (parsed.Status != DNSResponseStatus.ResponseIdMismatch)
                    {
                        if (!ResponseQuestionMatches(response, query.Name, query.Type))
                        {
                            spoofCount++; // C007: record the spoof observation (Swift C062)
                            if (attempt + 1 < MaxUDPAttempts)
                            {
                                lastError = "Response question does not match query (spoofed)";
                                continue;
                            }
                            span?.SetStatus(ActivityStatusCode.Error, "response question mismatch");
                            return new DNSResponse { Status = DNSResponseStatus.ResponseIdMismatch, ErrorMessage = "Response question does not match query" };
                        }
                    }

                    // Truncation → retry over TCP once.
                    if (parsed.Status == DNSResponseStatus.Truncated)
                    {
                        try
                        {
                            var tcpResponse = await SendTCPAsync(packet, query.Server, query.Port);
                            var tcpParsed = ParseResponsePacketDetailed(tcpResponse, query.Type, transactionId);
                            span?.SetTag("dns.fallback", "tcp");
                            span?.SetTag("dns.answers", tcpParsed.Records.Count);
                            // Classify from the response's OWN status, never from "we got here".
                            SyxxTracer.SetOutcomeFor(span, tcpParsed.Status);
                            return tcpParsed;
                        }
                        catch (Exception tcpEx)
                        {
                            span?.SetStatus(ActivityStatusCode.Error, "tcp fallback failed");
                            return new DNSResponse
                            {
                                Status = DNSResponseStatus.Truncated,
                                IsTruncated = true,
                                ErrorMessage = "UDP truncated and TCP fallback failed: " + tcpEx.Message
                            };
                        }
                    }

                    // A stray/spoofed packet with wrong ID: retry rather than trust it.
                    if (parsed.Status == DNSResponseStatus.ResponseIdMismatch && attempt + 1 < MaxUDPAttempts)
                    {
                        spoofCount++; // C007: count the ID-mismatch as a spoof observation (Swift C062)
                        // C007: never overwrite a spoof observation with a weaker error (Swift C062)
                        if (lastError == null || !lastError.Contains("spoofed", StringComparison.Ordinal))
                        {
                            lastError = parsed.ErrorMessage;
                        }
                        // B318: random-delay jitter between retries to reduce the window for
                        // an attacker to predict when the next query lands and pre-seed a spoof.
                        await Task.Delay(Random.Shared.Next(20, 120), ct).ConfigureAwait(false);
                        continue;
                    }
                    span?.SetTag("dns.answers", parsed.Records.Count);
                    span?.SetTag("dns.rcode", parsed.ResponseCode);
                    SyxxTracer.SetOutcomeFor(span, parsed.Status);
                    return parsed;
                }
                catch (TimeoutException ex)
                {
                    // C007: a spoof observation must not be overwritten by a follow-on timeout (Swift C062)
                    if (lastError == null || !lastError.Contains("spoofed", StringComparison.Ordinal))
                    {
                        lastError = ex.Message;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // C007: never overwrite a spoof observation with a weaker follow-on error (Swift C062)
                    if (lastError == null || !lastError.Contains("spoofed", StringComparison.Ordinal))
                    {
                        lastError = ex.Message;
                    }
                }
            }
            // C007: spoof + deadline expiry = evidence-bearing response, not plain timeout.
            // Parity with macOS DNSResolver.swift:528-535 — the spoof observation outranks the
            // timeout so the tampering signal is not hidden.
            if (spoofCount > 0)
            {
                string spoofMessage = $"{spoofCount} spoofed packet(s) observed, then {lastError ?? "no answer"}";
                SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive, spoofMessage, evidence: false);
                return new DNSResponse { Status = DNSResponseStatus.ResponseIdMismatch, ErrorMessage = spoofMessage };
            }
            SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive,
                                  lastError ?? "udp_query_failed", evidence: false);
            return new DNSResponse { Status = DNSResponseStatus.Timeout, ErrorMessage = lastError ?? "UDP query failed." };
        }

        /// <summary>
        /// Transport failures return an INCONCLUSIVE response rather than throwing — a resolver
        /// that cannot verify must never read as evidence of anything. Only cancellation
        /// propagates. <paramref name="dohUrl"/> is parameterized (defaulted) so tests can
        /// exercise the failure path offline.
        /// </summary>
        public static async Task<SecureResolutionResponse> ResolveSecurelyAsync(DNSQuery query,
            string dohUrl = "https://cloudflare-dns.com/dns-query")
        {
            // D122: this operation was traced on macOS (DNSResolver.swift:300-311) and NOT here, so
            // secure-resolution failures were invisible to every dashboard and alert built on the
            // macOS span. Span name and attribute names are the macOS ones verbatim — a renamed
            // twin is the same blind spot with extra steps.
            //
            // PII: the queried NAME is the user's browsing history and is never exported. Only its
            // class and shape go on the wire (TelemetryRedaction). A DNS-integrity tool must not
            // become a second surveillance channel. (macOS twin: DNSResolver.redactedAttrs.)
            using var span = SyxxTracer.StartSpan("dns.resolve.secure", ActivityKind.Client,
                TelemetryRedaction.SpanAttrs(query.Name, query.Type, "doh"));

            var response = await ResolveSecurelyCoreAsync(query, dohUrl, span);

            // Classified in ONE place, mirroring the macOS `classify:` closure: the response carries
            // its own tri-state and the span must not invent a different one. A per-return SetOutcome
            // would leave whichever branch got missed indistinguishable from a deliberate Unset.
            if (response.Result == SecureResolutionResult.Inconclusive)
            {
                SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive, response.Reason,
                                      evidence: false);
            }
            else
            {
                SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Success, evidence: true);
            }
            return response;
        }

        private static async Task<SecureResolutionResponse> ResolveSecurelyCoreAsync(DNSQuery query,
            string dohUrl, Activity? span)
        {
            var servers = NetworkKeyState.GetSystemDNSServers();
            if (servers.Exists(s => IPClassifier.Classify(s) == IPAddressClass.Loopback))
            {
                span?.SetTag("dns.inconclusive", "loopback_proxy");
                return new SecureResolutionResponse
                {
                    Result = SecureResolutionResult.Inconclusive,
                    Records = new List<DNSRecord>(),
                    Provenance = "Local Loopback Proxy",
                    Reason = "Local loopback DNS proxy present; upstream transport security cannot be verified."
                };
            }
            try
            {
                // QueryDoHDetailedAsync never throws on transport failure — it encodes it in the
                // status (Timeout/TransportError/MalformedResponse/ResponseIdMismatch) and returns
                // empty records. Inspect the status directly; a try/catch around QueryDoHAsync would
                // be dead code for exactly the failures we must surface as INCONCLUSIVE.
                var resp = await QueryDoHDetailedAsync(query.Name, query.Type, dohUrl);
                bool transportFailed = resp.Status is DNSResponseStatus.Timeout
                    or DNSResponseStatus.TransportError
                    or DNSResponseStatus.MalformedResponse
                    or DNSResponseStatus.ResponseIdMismatch
                    or DNSResponseStatus.Truncated
                    or DNSResponseStatus.ServFail
                    or DNSResponseStatus.Refused;
                if (transportFailed)
                {
                    span?.SetTag("dns.inconclusive", "transport_error");
                    return new SecureResolutionResponse
                    {
                        Result = SecureResolutionResult.Inconclusive,
                        Records = new List<DNSRecord>(),
                        Provenance = "DoH (Cloudflare)",
                        Reason = "Secure transport could not be verified: " + (resp.ErrorMessage ?? resp.Status.ToString())
                    };
                }
                span?.SetTag("dns.answers", resp.Records.Count);
                return new SecureResolutionResponse
                {
                    Result = SecureResolutionResult.Secure,
                    Records = resp.Records,
                    Provenance = "DoH (Cloudflare)"
                };
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not a measurement. It propagates unclassified — the span is
                // disposed Unset rather than being scored as a failure that never happened.
                throw;
            }
            catch (Exception ex)
            {
                span?.SetTag("dns.inconclusive", "transport_error");
                return new SecureResolutionResponse
                {
                    Result = SecureResolutionResult.Inconclusive,
                    Records = new List<DNSRecord>(),
                    Provenance = "DoH (Cloudflare)",
                    Reason = "Secure transport could not be verified: " + ex.Message
                };
            }
        }

        public static async Task<List<DNSRecord>> QueryDoHAsync(string name, ushort type, string dohUrl)
        {
            return (await QueryDoHDetailedAsync(name, type, dohUrl)).Records;
        }

        public static async Task<DNSResponse> QueryDoHDetailedAsync(string name, ushort type, string dohUrl,
            TimeSpan? timeout = null)
        {
            // The DoH endpoint URL is a provider constant, not user data, so it stays verbatim.
            using var span = SyxxTracer.StartSpan("dns.query.doh", ActivityKind.Client,
                TelemetryRedaction.SpanAttrs(name, type, "doh", dohEndpoint: dohUrl));

            try
            {
                // C131: moved MakeQueryPacket inside try so that ArgumentException (from ToAsciiName
                // for unencodable IDN) and FormatException (from ValidateDomainName) are caught and
                // returned as MalformedResponse, matching macOS .malformedResponse.
                var packet = MakeQueryPacket(name, type, out var transactionId);
                var content = new ByteArrayContent(packet);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/dns-message");

                var request = new HttpRequestMessage(HttpMethod.Post, dohUrl)
                {
                    Content = content
                };
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/dns-message"));

                // B319: per-call timeout via CancellationTokenSource instead of relying on the
                // shared static HttpClient.Timeout. This allows callers to override the default
                // 8-second timeout without creating a new HttpClient instance.
                using var cts = new CancellationTokenSource(timeout ?? DefaultDoHTimeout);
                var response = await _httpClient.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();

                var responseBytes = await response.Content.ReadAsByteArrayAsync();
                var parsed = ParseResponsePacketDetailed(responseBytes, type, transactionId);

                // KDNS-02: validate response question section matches query
                if (parsed.Status != DNSResponseStatus.ResponseIdMismatch)
                {
                    if (!ResponseQuestionMatches(responseBytes, name, type))
                    {
                        span?.SetStatus(ActivityStatusCode.Error, "response question mismatch");
                        return new DNSResponse { Status = DNSResponseStatus.ResponseIdMismatch, ErrorMessage = "Response question does not match query" };
                    }
                }
                span?.SetTag("dns.answers", parsed.Records.Count);
                span?.SetTag("dns.rcode", parsed.ResponseCode);
                span?.SetStatus(ActivityStatusCode.Ok);
                return parsed;
            }
            catch (TaskCanceledException ex)
            {
                SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive, "timeout", evidence: false);
                return new DNSResponse { Status = DNSResponseStatus.Timeout, ErrorMessage = ex.Message };
            }
            // C131: catch ArgumentException alongside FormatException — the packet builder can
            // throw either, and both must become MalformedResponse, never a propagated exception.
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                span?.SetStatus(ActivityStatusCode.Error, "malformed response");
                return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = ex.Message };
            }
            catch (HttpRequestException ex)
            {
                span?.SetStatus(ActivityStatusCode.Error, "transport error");
                return new DNSResponse { Status = DNSResponseStatus.TransportError, ErrorMessage = ex.Message };
            }
            catch (Exception ex)
            {
                span?.SetStatus(ActivityStatusCode.Error, "transport error");
                return new DNSResponse { Status = DNSResponseStatus.TransportError, ErrorMessage = ex.Message };
            }
        }
    }
}

import Foundation
import Network
import OpenTelemetryApi

// MARK: - Data Structures
//
// Core types for DNS queries, responses, and metadata. These structures define the
// wire-level protocol representation and are shared across all transport layers.

/// A structure representing a DNS Query.
public struct DNSQuery: Sendable {
    public let name: String
    public let type: UInt16
    public let server: String
    public let port: UInt16
    /// Query CLASS. Defaults to 1 (IN). Set to 3 (CHAOS) for resolver-fingerprinting queries such as
    /// `version.bind`/`hostname.bind` TXT (datapoint F2). Additive + defaulted → every existing call
    /// site keeps IN semantics unchanged.
    public let qclass: UInt16
    /// When set, the query carries an EDNS0 OPT record advertising this UDP payload size (datapoint
    /// F1). `nil` = no OPT (classic query, unchanged behaviour).
    public let edns0UDPSize: UInt16?
    /// When set (with `edns0UDPSize`), the OPT record carries an EDNS Client Subnet option for the
    /// /24 of this IPv4 address (datapoint F10). Used to probe whether the resolver forwards ECS.
    public let edns0ClientSubnetV4: String?
    /// When true (requires `edns0UDPSize`), the OPT record sets the DNSSEC OK (DO) bit so the
    /// resolver returns the covering RRSIG records. Without it a resolver returns DNSKEY but no
    /// RRSIG, and a bare QTYPE=RRSIG query is not answered by real resolvers (A030/A036).
    public let edns0DODNSSEC: Bool

    public init(name: String, type: UInt16 = 1, server: String = "8.8.8.8", port: UInt16 = 53,
                qclass: UInt16 = 1,
                edns0UDPSize: UInt16? = nil, edns0ClientSubnetV4: String? = nil,
                edns0DODNSSEC: Bool = false) {
        self.name = name
        self.type = type
        self.server = server
        self.port = port
        self.qclass = qclass
        self.edns0UDPSize = edns0UDPSize
        self.edns0ClientSubnetV4 = edns0ClientSubnetV4
        self.edns0DODNSSEC = edns0DODNSSEC
    }
}

/// EDNS0 characteristics read back from a response's OPT pseudo-record (datapoint F1). Its mere
/// presence means the resolver speaks EDNS0; `nil` on `DNSResponse.edns` means it did not echo OPT.
public struct EDNSResponseInfo: Codable, Sendable, Equatable {
    public let udpPayloadSize: UInt16   // advertised requestor UDP size (OPT CLASS field)
    public let version: UInt8           // EDNS version (0 for EDNS0)
    public let extendedRCode: UInt8     // upper 8 bits of the extended RCODE (16 = BADVERS)
    /// SCOPE PREFIX-LENGTH of an echoed EDNS Client Subnet option (datapoint F10); `nil` if the
    /// response carried no ECS option. A non-zero scope means the resolver applied/forwarded the
    /// subnet — i.e. it leaks the client subnet to authoritative servers. Optional → decode-safe.
    public let ecsScopePrefixLength: UInt8?
    public init(udpPayloadSize: UInt16, version: UInt8, extendedRCode: UInt8, ecsScopePrefixLength: UInt8? = nil) {
        self.udpPayloadSize = udpPayloadSize
        self.version = version
        self.extendedRCode = extendedRCode
        self.ecsScopePrefixLength = ecsScopePrefixLength
    }
}

/// A structure representing a DNS Record answer.
public struct DNSRecord: Codable, Hashable, Sendable {
    public let name: String
    public let type: UInt16
    public let ttl: UInt32
    public let rdata: String

    public init(name: String, type: UInt16, ttl: UInt32, rdata: String) {
        self.name = name
        self.type = type
        self.ttl = ttl
        self.rdata = rdata
    }

    /// Human-readable type name (e.g. "A", "AAAA", "MX") for display.
    public var typeName: String {
        switch type {
        case 1:  return "A"
        case 2:  return "NS"
        case 5:  return "CNAME"
        case 6:  return "SOA"
        case 12: return "PTR"
        case 15: return "MX"
        case 16: return "TXT"
        case 28: return "AAAA"
        case 33: return "SRV"
        case 48: return "DNSKEY"
        case 65: return "HTTPS"
        case 257: return "CAA"
        default: return "TYPE\(type)"
        }
    }
}

/// The distinct outcomes a DNS lookup can produce. Keeping these separate prevents
/// a network failure from being mistaken for an authoritative "no such record" answer,
/// which is the difference between an inconclusive check and a false "secure" result.
public enum DNSResponseStatus: String, Codable, Sendable {
    case success
    case noData            // RCODE 0 but zero answers of the requested type (NODATA)
    case nxDomain          // RCODE 3
    case servFail          // RCODE 2
    case refused           // RCODE 5
    case timeout
    case truncated         // TC bit set and no usable TCP fallback
    case responseIdMismatch
    case malformedResponse
    case transportError
    /// C069: the resolver answered with a recognised but non-success RCODE that is not
    /// NXDOMAIN/SERVFAIL/REFUSED (e.g. FORMERR=1, NOTIMP=4, BADVERS=16). Distinct from
    /// `.transportError` so "the resolver answered with an error" is not the same state as
    /// "no answer was received".
    case rcodeOther
}

/// Errors raised while constructing or transporting a DNS query.
///
/// `LocalizedError` conformance is required, not cosmetic: `recordsOrThrow()` re-throws these out of
/// the throwing `[DNSRecord]` API that the CLI tools use, and `SyxxCLI/Commands/SyxxNSLook.swift:89`
/// prints `error.localizedDescription` directly. Without the conformance a plain query timeout
/// printed as "The operation couldn't be completed. (SyxxKit.DNSResolverError error 2.)" — the same
/// crash-dump class of defect as `WitnessSignerError`.
///
/// Each string states only what the guard that threw it established. The associated values are
/// carried through verbatim, never re-interpreted into a cause we have not observed.
public enum DNSResolverError: Error, LocalizedError, Sendable {
    case invalidName(String)
    case malformedResponse(String)
    case timeout
    case transport(String)

    public var errorDescription: String? {
        switch self {
        case .invalidName(let detail):
            return "This is not a usable DNS name: \(detail)"
        case .malformedResponse(let detail):
            return "The DNS response could not be parsed: \(detail)"
        case .timeout:
            return "The DNS query timed out with no answer."
        case .transport(let detail):
            return "The DNS query did not complete: \(detail)"
        }
    }
}

/// A typed DNS response carrying status metadata alongside the records.
public struct DNSResponse: Codable, Sendable {
    public let status: DNSResponseStatus
    public let records: [DNSRecord]
    /// Authority-section records (SOA / NSEC / NSEC3 / NS), preserved so a genuine authenticated
    /// negative answer can be told apart from an injected block-page NXDOMAIN (datapoint A6). Empty
    /// on positive answers and on transport failures. Additive + defaulted → older call sites and
    /// any persisted responses remain valid.
    public let authorityRecords: [DNSRecord]
    /// EDNS0 info from the response's OPT record (datapoint F1); `nil` if the resolver did not echo
    /// OPT (no EDNS0 support) or on transport failure. Additive + defaulted + decode-tolerant.
    public let edns: EDNSResponseInfo?
    /// B073/B071 — EDNS presence is tri-state, not a bool. `edns != nil` = OPT present and parsed;
    /// `edns == nil && !ednsMalformed` = OPT genuinely absent (legacy resolver, no EDNS0);
    /// `edns == nil && ednsMalformed` = the additional section declared records but the OPT/section
    /// could not be parsed, so EDNS support is UNKNOWN (inconclusive) — never read this as "absent".
    public let ednsMalformed: Bool
    /// A6/B071 — the authority section declared records (NSCOUNT > 0) but not all could be parsed.
    /// A negative answer with a malformed authority is inconclusive, not a genuine "bare" denial
    /// (which is an EMPTY authority). Consumers must not treat malformed authority as absent-denial.
    public let authorityMalformed: Bool
    public let offTypeAnswerCount: Int       // answers whose type ≠ queried type (KDNS-06)
    /// B070 — in-class address RRs whose owner name is neither the question name nor a CNAME target
    /// reachable from it. These are off-name/smuggled records; they are dropped from `records` and
    /// counted here so a consumer can flag the injection attempt instead of resolving a spoofed address.
    public let offNameAnswerCount: Int
    /// C057 — answer RRs whose CLASS does not match the queried QCLASS. A response whose entire
    /// answer section was dropped by the class filter must be inconclusive, never a silent `.noData`.
    public let offClassAnswerCount: Int
    /// C072 — count of CNAME owner names that appeared with multiple different targets in the
    /// answer section. Two CNAMEs for one owner are invalid DNS and an injection signature;
    /// surfaced so consumers can flag the conflict instead of silently last-wins.
    public let conflictingCNAMEOwnerCount: Int
    public let responseCode: UInt16
    public let isAuthoritative: Bool
    public let isTruncated: Bool
    public let recursionAvailable: Bool
    public let authenticatedData: Bool
    public let errorMessage: String?
    /// Raw wire size of the response datagram in bytes, when known (used for amplification-factor
    /// measurement — Portal Open Resolver probe). `nil` on injected/mock responses. Additive + defaulted.
    public let responseByteLength: Int?
    /// C062 — count of packets received during this query whose question section did not match
    /// the expected query (positive observation of injection). Surfaced regardless of final status
    /// so a query that saw a spoofed packet and then timed out is not silently reported as `.timeout`.
    /// Additive + defaulted → older call sites and persisted responses remain valid.
    public let spoofedPacketCount: Int
    /// C070 — the number of UDP attempts made before the final response was produced. A value > 1
    /// means at least one attempt was lost (timeout or spoofed), so the elapsed time includes a
    /// ~2 s retry delay and must not be treated as a clean RTT sample. Additive + defaulted.
    public let attemptCount: Int

    public init(
        status: DNSResponseStatus,
        records: [DNSRecord] = [],
        authorityRecords: [DNSRecord] = [],
        edns: EDNSResponseInfo? = nil,
        ednsMalformed: Bool = false,
        authorityMalformed: Bool = false,
        offTypeAnswerCount: Int = 0,
        offNameAnswerCount: Int = 0,
        offClassAnswerCount: Int = 0,
        conflictingCNAMEOwnerCount: Int = 0,
        responseCode: UInt16 = 0,
        isAuthoritative: Bool = false,
        isTruncated: Bool = false,
        recursionAvailable: Bool = false,
        authenticatedData: Bool = false,
        errorMessage: String? = nil,
        responseByteLength: Int? = nil,
        spoofedPacketCount: Int = 0,
        attemptCount: Int = 1
    ) {
        self.status = status
        self.records = records
        self.authorityRecords = authorityRecords
        self.edns = edns
        self.ednsMalformed = ednsMalformed
        self.authorityMalformed = authorityMalformed
        self.offTypeAnswerCount = offTypeAnswerCount
        self.offNameAnswerCount = offNameAnswerCount
        self.offClassAnswerCount = offClassAnswerCount
        self.conflictingCNAMEOwnerCount = conflictingCNAMEOwnerCount
        self.responseCode = responseCode
        self.isAuthoritative = isAuthoritative
        self.isTruncated = isTruncated
        self.recursionAvailable = recursionAvailable
        self.authenticatedData = authenticatedData
        self.errorMessage = errorMessage
        self.responseByteLength = responseByteLength
        self.spoofedPacketCount = spoofedPacketCount
        self.attemptCount = attemptCount
    }

    /// Decodes tolerantly: a persisted response written before `authorityRecords` existed simply
    /// decodes to an empty authority set instead of failing.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        status = try c.decode(DNSResponseStatus.self, forKey: .status)
        records = try c.decodeIfPresent([DNSRecord].self, forKey: .records) ?? []
        authorityRecords = try c.decodeIfPresent([DNSRecord].self, forKey: .authorityRecords) ?? []
        edns = try c.decodeIfPresent(EDNSResponseInfo.self, forKey: .edns)
        ednsMalformed = try c.decodeIfPresent(Bool.self, forKey: .ednsMalformed) ?? false
        authorityMalformed = try c.decodeIfPresent(Bool.self, forKey: .authorityMalformed) ?? false
        offTypeAnswerCount = try c.decodeIfPresent(Int.self, forKey: .offTypeAnswerCount) ?? 0
        offNameAnswerCount = try c.decodeIfPresent(Int.self, forKey: .offNameAnswerCount) ?? 0
        offClassAnswerCount = try c.decodeIfPresent(Int.self, forKey: .offClassAnswerCount) ?? 0
        conflictingCNAMEOwnerCount = try c.decodeIfPresent(Int.self, forKey: .conflictingCNAMEOwnerCount) ?? 0
        responseCode = try c.decodeIfPresent(UInt16.self, forKey: .responseCode) ?? 0
        isAuthoritative = try c.decodeIfPresent(Bool.self, forKey: .isAuthoritative) ?? false
        isTruncated = try c.decodeIfPresent(Bool.self, forKey: .isTruncated) ?? false
        recursionAvailable = try c.decodeIfPresent(Bool.self, forKey: .recursionAvailable) ?? false
        authenticatedData = try c.decodeIfPresent(Bool.self, forKey: .authenticatedData) ?? false
        errorMessage = try c.decodeIfPresent(String.self, forKey: .errorMessage)
        responseByteLength = try c.decodeIfPresent(Int.self, forKey: .responseByteLength)
        spoofedPacketCount = try c.decodeIfPresent(Int.self, forKey: .spoofedPacketCount) ?? 0
        attemptCount = try c.decodeIfPresent(Int.self, forKey: .attemptCount) ?? 1
    }

    /// True when the query did not produce a usable answer — either a transport/parse
    /// failure or an RCODE error (SERVFAIL, REFUSED, etc.). This is broader than a pure
    /// transport failure: a SERVFAIL or REFUSED means the resolver *responded* with an
    /// error code, not that the network path failed. Named `didNotSucceed` rather than
    /// `didFail` to make the RCODE inclusion explicit at every call site.
    public var didNotSucceed: Bool {
        switch status {
        case .timeout, .transportError, .malformedResponse, .responseIdMismatch,
             .truncated, .servFail, .refused, .rcodeOther:
            return true
        default:
            return false
        }
    }
}

/// E030: typed tri-state for secure resolution result, replacing the two independent Bools
/// (isSecure, isInconclusive) that could represent impossible states (e.g., true/true).
/// Parity with Windows SecureResolutionResult enum. Hostile/stale Codable input cannot
/// represent invalid combinations.
public enum SecureResolutionResult: String, Codable, Sendable {
    case secure
    case inconclusive
}

public struct SecureResolutionResponse: Codable, Sendable {
    public let records: [DNSRecord]
    public let result: SecureResolutionResult
    public let provenance: String
    public let reason: String?
}

// MARK: - DNS Resolver (Orchestration)
//
// Top-level resolver coordinating transport selection (UDP/TCP/DoH), query construction,
// response validation, and telemetry. Public API methods live here; implementation detail
// is in the Wire Format and Transport sections below.

/// DNS Resolver supporting UDP (with TCP fallback) and DoH (DNS-over-HTTPS).
public enum DNSResolver {

    /// Bounded URLSession used for DoH so requests cannot hang on default timeouts.
    private static let dohSession: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 5.0
        config.timeoutIntervalForResource = 8.0
        config.requestCachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        return URLSession(configuration: config)
    }()

    private static let udpTimeoutNanos: UInt64 = 2_000_000_000
    private static let maxUDPAttempts = 2

    /// Resolves the query securely. If system DNS is loopback, returns inconclusive since transport cannot be verified. Otherwise uses DoH.
    /// Transport failures return an INCONCLUSIVE response rather than throwing — a resolver that
    /// cannot verify must never read as evidence of anything. Only cancellation propagates.
    /// `dohURLString` is parameterized (defaulted) so tests can exercise the failure path offline.
    /// Span attributes for a DNS operation with the queried name REDACTED.
    ///
    /// The name itself never leaves the machine: `TelemetryRedaction` emits a target class plus
    /// label-count/length/IDN shape, which is enough to diagnose malformed- and long-name failures
    /// without disclosing what the user looked up. Resolver addresses are emitted verbatim only
    /// when they are known public resolvers; a home, ISP, or corporate resolver is identifying and
    /// is reduced to a class token.
    static func redactedAttrs(domain: String,
                              type: UInt16,
                              transport: String,
                              resolver: String? = nil,
                              port: Int? = nil,
                              dohEndpoint: String? = nil) -> [String: AttributeValue] {
        var attrs: [String: AttributeValue] = [
            "dns.type": .int(Int(type)),
            "dns.transport": .string(transport)
        ]
        for (k, v) in TelemetryRedaction.attributes(forDomain: domain) {
            attrs[k] = .string(v)
        }
        if let resolver {
            for (k, v) in TelemetryRedaction.attributes(forResolver: resolver) {
                attrs[k] = .string(v)
            }
        }
        if let port { attrs["dns.port"] = .int(port) }
        if let dohEndpoint { attrs["dns.server"] = .string(dohEndpoint) }
        return attrs
    }

    public static func resolveSecurely(_ query: DNSQuery,
                                       dohURLString: String = "https://cloudflare-dns.com/dns-query") async throws -> SecureResolutionResponse {
        // PII: the queried NAME is the user's browsing history and is never exported. Only its
        // class and shape go on the wire (TelemetryRedaction). A DNS-integrity tool must not
        // become a second surveillance channel.
        return try await SyxxTracer.span("dns.resolve.secure", kind: .client,
                                         attributes: Self.redactedAttrs(
            domain: query.name, type: query.type, transport: "doh"),
            classify: { (r: SecureResolutionResponse) in
                // The response carries its own typed result; the span must not invent a different one.
                r.result == .inconclusive ? (.inconclusive, r.reason) : (.success, nil)
            }) { span in
            let servers = NetworkKeyState.getSystemDNSServers()
            if servers.contains(where: { IPClassifier.classify($0) == .loopback }) {
                span.setAttribute(key: "dns.inconclusive", value: "loopback_proxy")
                return SecureResolutionResponse(
                    records: [],
                    result: .inconclusive,
                    provenance: "Local Loopback Proxy",
                    reason: "Local loopback DNS proxy present; upstream transport security cannot be verified."
                )
            }
            guard let dohURL = URL(string: dohURLString) else {
                span.setAttribute(key: "dns.inconclusive", value: "invalid_doh_url")
                return SecureResolutionResponse(
                    records: [],
                    result: .inconclusive,
                    provenance: "DoH (Cloudflare)",
                    reason: "Invalid DoH URL configured"
                )
            }
            do {
                let records = try await queryDoH(name: query.name, type: query.type, url: dohURL)
                span.setAttribute(key: "dns.answers", value: records.count)
                return SecureResolutionResponse(
                    records: records,
                    result: .secure,
                    provenance: "DoH (Cloudflare)",
                    reason: nil
                )
            } catch is CancellationError {
                throw CancellationError()
            } catch let error as URLError where error.code == .cancelled {
                throw error
            } catch {
                span.setAttribute(key: "dns.inconclusive", value: "transport_error")
                return SecureResolutionResponse(
                    records: [],
                    result: .inconclusive,
                    provenance: "DoH (Cloudflare)",
                    reason: "Secure transport could not be verified: \(error.localizedDescription)"
                )
            }
        }
    }

    /// Queries the resolver using standard UDP, returning just the records for callers
    /// that do not need status metadata.
    public static func queryUDP(_ query: DNSQuery) async throws -> [DNSRecord] {
        try (await queryUDPDetailed(query)).recordsOrThrow()
    }

    /// Queries the resolver using UDP and returns the full typed response, never throwing
    /// for protocol-level negatives (NXDOMAIN, NODATA, timeouts) — those are encoded in the status.
    public static func queryUDPDetailed(_ query: DNSQuery) async -> DNSResponse {
        return await SyxxTracer.span("dns.query.udp", kind: .client,
                                     attributes: Self.redactedAttrs(
            domain: query.name, type: query.type, transport: "udp",
            resolver: query.server, port: Int(query.port)),
            classify: { (r: DNSResponse) in
                // The response's own status is the authority. `.success`/`.noData`/`.nxDomain`/
                // `.refused`/`.servFail` are MEASURED answers — the resolver replied, and a
                // negative answer is the product working, not a telemetry error. Everything else
                // means we could not read an answer at all.
                switch r.status {
                case .success, .noData:                       return (.success, nil)
                case .nxDomain, .servFail, .refused, .rcodeOther: return (.failure, r.status.rawValue)
                case .timeout, .truncated, .responseIdMismatch,
                     .malformedResponse, .transportError:     return (.inconclusive, r.status.rawValue)
                }
            }) { span in
        var lastError: String? = nil
        // C062: track spoofed packets across attempts so the observation survives a later
        // timeout. A question-mismatch or ID-mismatch is positive evidence of injection.
        var spoofCount = 0
        // B032: a dedicated Bool for spoof detection. The old code pattern-matched on
        // `lastError?.contains("spoofed")` — a human-readable string that a reworded error
        // message would silently break, letting a follow-on timeout overwrite a genuine spoof
        // observation. The flag is set exactly where a spoof is detected and is immune to
        // message wording.
        var sawSpoof = false
        for attempt in 0..<maxUDPAttempts {
            // F006: check for cooperative cancellation at transport layer
            if Task.isCancelled {
                return DNSResponse(status: .timeout, errorMessage: "cancelled",
                                   spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
            }
            // KDNS-02: fresh transaction ID and packet per attempt (spoofed retry immunity)
            let packet: Data
            let transactionID: UInt16
            do {
                (packet, transactionID) = try makeQueryPacketWithID(name: query.name, type: query.type, qclass: query.qclass, edns0UDPSize: query.edns0UDPSize, edns0ClientSubnetV4: query.edns0ClientSubnetV4, dnssecOK: query.edns0DODNSSEC)
            } catch let DNSResolverError.invalidName(message) {
                return DNSResponse(status: .malformedResponse, errorMessage: message,
                                   spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
            } catch {
                return DNSResponse(status: .malformedResponse, errorMessage: error.localizedDescription,
                                   spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
            }

            do {
                let response = try await sendUDP(packet, to: query.server, port: query.port)
                let parsed = parseResponsePacketDetailed(response, requestedType: query.type, expectedTransactionID: transactionID, expectedClass: query.qclass)

                // KDNS-02: validate the response question section matches what we queried
                if parsed.status != .responseIdMismatch {
                    if !responseQuestionMatches(response, expectedName: query.name, expectedType: query.type) {
                        spoofCount += 1  // C062: record the spoof observation
                        sawSpoof = true  // B032: flag, not string-matching
                        if attempt + 1 < maxUDPAttempts {
                            // C062: never overwrite a spoof observation with a weaker error
                            if lastError == nil || !sawSpoof {
                                lastError = "Response question does not match query (spoofed)"
                            }
                            continue
                        }
                        return DNSResponse(status: .responseIdMismatch,
                                           errorMessage: "Response question does not match query",
                                           spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
                    }
                }

                // Truncation → retry over TCP once.
                if parsed.status == .truncated {
                    do {
                        let tcpResponse = try await sendTCP(packet, to: query.server, port: query.port)
                        let tcpParsed = parseResponsePacketDetailed(tcpResponse, requestedType: query.type, expectedTransactionID: transactionID, expectedClass: query.qclass)
                        // C063: apply the same KDNS-02 question-section check on the TCP
                        // response as on UDP.
                        if tcpParsed.status != .responseIdMismatch {
                            if !responseQuestionMatches(tcpResponse, expectedName: query.name, expectedType: query.type) {
                                return DNSResponse(status: .responseIdMismatch,
                                                   errorMessage: "TCP response question does not match query",
                                                   spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
                            }
                        }
                        // C062/C070: carry spoof/attempt counts through the TCP fallback
                        return DNSResponse(status: tcpParsed.status, records: tcpParsed.records,
                                           authorityRecords: tcpParsed.authorityRecords, edns: tcpParsed.edns,
                                           ednsMalformed: tcpParsed.ednsMalformed, authorityMalformed: tcpParsed.authorityMalformed,
                                           offTypeAnswerCount: tcpParsed.offTypeAnswerCount, offNameAnswerCount: tcpParsed.offNameAnswerCount,
                                           offClassAnswerCount: tcpParsed.offClassAnswerCount, responseCode: tcpParsed.responseCode,
                                           isAuthoritative: tcpParsed.isAuthoritative, isTruncated: tcpParsed.isTruncated,
                                           recursionAvailable: tcpParsed.recursionAvailable, authenticatedData: tcpParsed.authenticatedData,
                                           errorMessage: tcpParsed.errorMessage, responseByteLength: tcpParsed.responseByteLength,
                                           spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
                    } catch {
                        return DNSResponse(status: .truncated, isTruncated: true,
                                           errorMessage: "UDP truncated and TCP fallback failed: \(error.localizedDescription)",
                                           spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
                    }
                }

                // A stray/spoofed packet with the wrong ID: retry rather than trust it.
                if parsed.status == .responseIdMismatch && attempt + 1 < maxUDPAttempts {
                    spoofCount += 1  // C062: count the ID-mismatch as a spoof observation
                    sawSpoof = true  // B032: flag, not string-matching
                    if lastError == nil || !sawSpoof {
                        lastError = parsed.errorMessage
                    }
                    continue
                }
                // C062/C070: carry spoof/attempt counts through the successful return
                return DNSResponse(status: parsed.status, records: parsed.records,
                                   authorityRecords: parsed.authorityRecords, edns: parsed.edns,
                                   ednsMalformed: parsed.ednsMalformed, authorityMalformed: parsed.authorityMalformed,
                                   offTypeAnswerCount: parsed.offTypeAnswerCount, offNameAnswerCount: parsed.offNameAnswerCount,
                                   offClassAnswerCount: parsed.offClassAnswerCount, responseCode: parsed.responseCode,
                                   isAuthoritative: parsed.isAuthoritative, isTruncated: parsed.isTruncated,
                                   recursionAvailable: parsed.recursionAvailable, authenticatedData: parsed.authenticatedData,
                                   errorMessage: parsed.errorMessage, responseByteLength: parsed.responseByteLength,
                                   spoofedPacketCount: spoofCount, attemptCount: attempt + 1)
            } catch is CancellationError {
                lastError = "cancelled"
            } catch DNSResolverError.timeout {
                // C062: a spoof observation must not be overwritten by a follow-on timeout
                if !sawSpoof {
                    lastError = "UDP timeout"
                }
            } catch {
                if !sawSpoof {
                    lastError = error.localizedDescription
                }
            }
        }
        // C062: spoof + timeout = evidence-bearing response, not plain timeout
        if spoofCount > 0 {
            return DNSResponse(status: .responseIdMismatch,
                               errorMessage: "\(spoofCount) spoofed packet(s) observed, then \(lastError ?? "no answer")",
                               spoofedPacketCount: spoofCount, attemptCount: maxUDPAttempts)
        }
        return DNSResponse(status: .timeout, errorMessage: lastError ?? "UDP query failed",
                           spoofedPacketCount: spoofCount, attemptCount: maxUDPAttempts)
        }  // close SyxxTracer.span closure
    }

    /// Queries the resolver using DNS-over-HTTPS (DoH), returning just records.
    public static func queryDoH(name: String, type: UInt16, url: URL) async throws -> [DNSRecord] {
        try (await queryDoHDetailed(name: name, type: type, url: url)).recordsOrThrow()
    }

    /// Queries the resolver using DoH and returns the full typed response.
    public static func queryDoHDetailed(name: String, type: UInt16, url: URL) async -> DNSResponse {
        // The DoH endpoint URL is a Syxx/provider constant, not user data, so it stays verbatim.
        return await SyxxTracer.span("dns.query.doh", kind: .client,
                                     attributes: Self.redactedAttrs(
            domain: name, type: type, transport: "doh", dohEndpoint: url.absoluteString),
            classify: { (r: DNSResponse) in
                // The response's own status is the authority. `.success`/`.noData`/`.nxDomain`/
                // `.refused`/`.servFail` are MEASURED answers — the resolver replied, and a
                // negative answer is the product working, not a telemetry error. Everything else
                // means we could not read an answer at all.
                switch r.status {
                case .success, .noData:                       return (.success, nil)
                case .nxDomain, .servFail, .refused, .rcodeOther: return (.failure, r.status.rawValue)
                case .timeout, .truncated, .responseIdMismatch,
                     .malformedResponse, .transportError:     return (.inconclusive, r.status.rawValue)
                }
            }) { span in
            let packet: Data
            let transactionID: UInt16
            do {
                (packet, transactionID) = try makeQueryPacketWithID(name: name, type: type)
            } catch let DNSResolverError.invalidName(message) {
                span.setAttribute(key: "dns.error", value: "invalid_name")
                return DNSResponse(status: .malformedResponse, errorMessage: message)
            } catch {
                span.setAttribute(key: "dns.error", value: "packet_build_failed")
                return DNSResponse(status: .malformedResponse, errorMessage: error.localizedDescription)
            }

            var request = URLRequest(url: url)
            request.httpMethod = "POST"
            request.setValue("application/dns-message", forHTTPHeaderField: "Content-Type")
            request.setValue("application/dns-message", forHTTPHeaderField: "Accept")
            request.httpBody = packet
            request.timeoutInterval = 5.0

            do {
                let (data, response) = try await dohSession.data(for: request)
                guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                    let code = (response as? HTTPURLResponse)?.statusCode ?? -1
                    span.setAttribute(key: "dns.http_status", value: code)
                    return DNSResponse(status: .transportError, errorMessage: "DoH HTTP status \(code)")
                }
                let parsed = parseResponsePacketDetailed(data, requestedType: type, expectedTransactionID: transactionID)
                span.setAttribute(key: "dns.response_status", value: parsed.status.rawValue)
                span.setAttribute(key: "dns.answers", value: parsed.records.count)
                // KDNS-02: validate question section matches for DoH responses too
                if parsed.status != .responseIdMismatch {
                    if !responseQuestionMatches(data, expectedName: name, expectedType: type) {
                        return DNSResponse(status: .responseIdMismatch, errorMessage: "DoH response question does not match query")
                    }
                }
                return parsed
            } catch let error as URLError where error.code == .timedOut {
                span.setAttribute(key: "dns.error", value: "timeout")
                return DNSResponse(status: .timeout, errorMessage: error.localizedDescription)
            } catch {
                span.setAttribute(key: "dns.error", value: "transport_error")
                return DNSResponse(status: .transportError, errorMessage: error.localizedDescription)
            }
        }
    }

    // MARK: Wire Format — Packet Construction
    //
    // Builds DNS query packets from structured input: header, question section (with IDNA
    // punycode conversion), and optional EDNS0 OPT record. Pure byte-level encoding with
    // no transport dependencies.

    /// Legacy packet builder used by tests/tools that do not need the transaction ID.
    /// C132: now throws instead of returning an empty Data on validation failure, so an invalid
    /// name is never silently converted to a zero-byte "query" that a caller might send or measure.
    public static func makeQueryPacket(name: String, type: UInt16) throws -> Data {
        try makeQueryPacketWithID(name: name, type: type).0
    }

    /// Builds a DNS query packet with a random transaction ID and validated name.
    public static func makeQueryPacketWithID(name: String, type: UInt16,
                                             qclass: UInt16 = 1,
                                             edns0UDPSize: UInt16? = nil,
                                             edns0ClientSubnetV4: String? = nil,
                                             dnssecOK: Bool = false) throws -> (packet: Data, transactionID: UInt16) {
        let name = try toASCIIName(name)
        try validateDomainName(name)
        let transactionID = UInt16.random(in: 0...UInt16.max)

        var data = Data()
        // Header
        data.append(UInt8((transactionID >> 8) & 0xff))
        data.append(UInt8(transactionID & 0xff))
        data.append(contentsOf: [0x01, 0x00]) // Flags: Standard query, recursion desired
        data.append(contentsOf: [0x00, 0x01]) // Questions: 1
        data.append(contentsOf: [0x00, 0x00]) // Answer RRs: 0
        data.append(contentsOf: [0x00, 0x00]) // Authority RRs: 0
        data.append(contentsOf: edns0UDPSize != nil ? [0x00, 0x01] : [0x00, 0x00]) // Additional RRs (1 = EDNS0 OPT)

        // Question: Name
        let labels = name.trimmingCharacters(in: CharacterSet(charactersIn: ".")).split(separator: ".")
        for label in labels {
            guard let labelData = label.data(using: .utf8) else {
                throw DNSResolverError.invalidName("Label is not valid UTF-8")
            }
            data.append(UInt8(labelData.count))
            data.append(labelData)
        }
        data.append(0x00) // Null byte terminating domain name

        // Question: Type & Class
        data.append(contentsOf: [UInt8((type >> 8) & 0xff), UInt8(type & 0xff)]) // Type
        data.append(contentsOf: [UInt8((qclass >> 8) & 0xff), UInt8(qclass & 0xff)]) // Class (1 = IN, 3 = CHAOS for F2)

        // EDNS0 OPT pseudo-record in the additional section (datapoint F1) — a minimal capability
        // probe: root name, advertised UDP payload size, version 0, DO bit set only when DNSSEC
        // records are requested, no options.
        if let udp = edns0UDPSize {
            data.append(0x00)                                                     // NAME: root
            data.append(contentsOf: [0x00, 0x29])                                 // TYPE: OPT (41)
            data.append(contentsOf: [UInt8((udp >> 8) & 0xff), UInt8(udp & 0xff)]) // CLASS: UDP payload size
            // TTL: ext-rcode 0, version 0, flags. DO bit (0x8000 in the flags word) asks the resolver
            // to return DNSSEC RRSIG records (A030/A036); otherwise flags are 0.
            data.append(contentsOf: dnssecOK ? [0x00, 0x00, 0x80, 0x00] : [0x00, 0x00, 0x00, 0x00])
            // RDATA: optionally one EDNS Client Subnet option (F10) for the /24 of the given IPv4.
            if let ecs = edns0ClientSubnetV4, let o = ipv4OctetsForECS(ecs) {
                let ecsOption: [UInt8] = [0x00, 0x08,          // OPTION-CODE: ECS (8)
                                          0x00, 0x07,          // OPTION-LENGTH: 7
                                          0x00, 0x01,          // FAMILY: IPv4
                                          0x18,                // SOURCE PREFIX-LENGTH: 24
                                          0x00,                // SCOPE PREFIX-LENGTH: 0 (query)
                                          o.0, o.1, o.2]       // /24 address (3 octets)
                data.append(contentsOf: [UInt8((ecsOption.count >> 8) & 0xff), UInt8(ecsOption.count & 0xff)]) // RDLENGTH
                data.append(contentsOf: ecsOption)
            } else {
                data.append(contentsOf: [0x00, 0x00])                             // RDLENGTH: 0
            }
        }

        return (data, transactionID)
    }

    /// Converts an internationalized (non-ASCII) domain name to its IDNA/punycode ASCII form,
    /// per-label, so IDN test domains are queried on the wire exactly as a resolver expects and
    /// tampering/censorship of non-ASCII domains can be detected. ASCII names pass through.
    ///
    /// Applies NFC normalization and IDNA/UTS-46 mapping per KDNS-09/KDNS-10:
    /// - NFC-normalizes the entire input (so decomposed "café" and precomposed "café" produce
    ///   the same A-label).
    /// - If a label is already a valid A-label (`xn--...`), verifies it decodes correctly and
    ///   normalizes; avoids double-encoding.
    /// - Non-ASCII labels are case-folded and NFC-normalized before punycode encoding.
    static func toASCIIName(_ name: String) throws -> String {
        // Step 1: NFC normalize the entire input
        let nfc = name.precomposedStringWithCanonicalMapping
        guard nfc.unicodeScalars.contains(where: { $0.value > 127 }) else { return nfc }
        // C121: split on the full IDNA separator set (U+002E, U+3002, U+FF0E, U+FF61) per RFC 3490,
        // not just ASCII dot. Without this, ideographic full stops are punycoded inside the label,
        // yielding a single label instead of multiple labels (e.g., 例え。テスト.jp becomes one label).
        let idnaSeparators = CharacterSet(charactersIn: ".\u{3002}\u{FF0E}\u{FF61}")
        let trimmed = nfc.trimmingCharacters(in: idnaSeparators)
        var out: [String] = []
        for label in trimmed.components(separatedBy: idnaSeparators) {
            if label.isEmpty { continue }  // skip empty labels from consecutive separators
            if label.hasPrefix("xn--") || label.hasPrefix("XN--") {
                // Already an A-label; validate it decodes correctly to avoid double-encoding.
                let ace = String(label)
                // Verify the A-label is well-formed by attempting decode (lowercased).
                let lower = ace.lowercased()
                guard lower.hasPrefix("xn--") else {
                    throw DNSResolverError.invalidName("Invalid A-label prefix in '\(label)'")
                }
                let puny = String(lower.dropFirst(4))
                guard let _ = punycodeDecode(puny) else {
                    throw DNSResolverError.invalidName("A-label '\(label)' failed punycode validation")
                }
                out.append(lower)
            } else if label.unicodeScalars.allSatisfy({ $0.value < 128 }) {
                out.append(String(label))
            } else {
                // Non-ASCII label: case-fold (lowercase) + NFC, then encode
                let folded = label.lowercased().precomposedStringWithCanonicalMapping
                guard let encoded = punycodeEncode(folded) else {
                    throw DNSResolverError.invalidName("Label '\(label)' is not encodable as IDNA/punycode")
                }
                // C121: assert no label byte is 0x2E after encoding (RFC 3490 §3.1).
                guard !encoded.contains(".") else {
                    throw DNSResolverError.invalidName("Encoded label '\(encoded)' contains a dot")
                }
                out.append("xn--" + encoded)
            }
        }
        return out.joined(separator: ".")
    }

    /// RFC 3492 punycode encoder for a single label (without the `xn--` prefix). Pure/testable.
    /// Input should be NFC-normalized and case-folded before calling.
    static func punycodeEncode(_ input: String) -> String? {
        let base = 36, tmin = 1, tmax = 26, skew = 38, damp = 700, initialBias = 72, initialN = 128
        let scalars = Array(input.unicodeScalars)
        var output: [Character] = []
        var n = initialN
        var delta = 0
        var bias = initialBias

        // Emit basic (ASCII) code points first.
        var basicCount = 0
        for s in scalars where s.value < 128 {
            output.append(Character(s))
            basicCount += 1
        }
        var handled = basicCount
        if basicCount > 0 { output.append("-") }

        func digitToChar(_ d: Int) -> Character {
            // 0..25 -> a..z, 26..35 -> 0..9
            d < 26 ? Character(UnicodeScalar(UInt8(d + 0x61))) : Character(UnicodeScalar(UInt8(d - 26 + 0x30)))
        }
        func adapt(_ deltaIn: Int, _ numPoints: Int, _ firstTime: Bool) -> Int {
            var d = firstTime ? deltaIn / damp : deltaIn / 2
            d += d / numPoints
            var k = 0
            while d > ((base - tmin) * tmax) / 2 {
                d /= (base - tmin)
                k += base
            }
            return k + (((base - tmin + 1) * d) / (d + skew))
        }

        let total = scalars.count
        while handled < total {
            // Smallest code point >= n.
            var m = Int.max
            for s in scalars where Int(s.value) >= n { m = min(m, Int(s.value)) }
            if m - n > (Int.max - delta) / (handled + 1) { return nil } // overflow guard
            delta += (m - n) * (handled + 1)
            n = m
            for s in scalars {
                let c = Int(s.value)
                if c < n {
                    delta += 1
                    if delta == 0 { return nil }
                }
                if c == n {
                    var q = delta
                    var k = base
                    while true {
                        let t = k <= bias ? tmin : (k >= bias + tmax ? tmax : k - bias)
                        if q < t { break }
                        output.append(digitToChar(t + ((q - t) % (base - t))))
                        q = (q - t) / (base - t)
                        k += base
                    }
                    output.append(digitToChar(q))
                    bias = adapt(delta, handled + 1, handled == basicCount)
                    delta = 0
                    handled += 1
                }
            }
            delta += 1
            n += 1
        }
        return String(output)
    }

    /// Minimal RFC 3492 punycode decoder used only to validate existing A-labels so that
    /// `toASCIIName` can detect malformed or double-encoded `xn--` labels.  Returns the
    /// decoded Unicode string on success, nil on any invalid input.
    static func punycodeDecode(_ input: String) -> String? {
        guard !input.isEmpty else { return nil }
        let base = 36, tmin = 1, tmax = 26, skew = 38, damp = 700, initialBias = 72, initialN = 128

        // Split on last hyphen: basic chars before it, deltas after.
        var basic: [Character] = []
        var deltaStr: Substring
        if let hyphen = input.lastIndex(of: "-") {
            basic = Array(input[..<hyphen])
            deltaStr = input[input.index(after: hyphen)...]
        } else {
            basic = []
            deltaStr = Substring(input)
        }

        // Verify all basic chars are ASCII letters/digits (punycode digits)
        for c in basic { guard c.isASCII && (c.isLetter || c.isNumber) else { return nil } }

        var output = basic
        var n = initialN
        var bias = initialBias

        func adapt(_ deltaIn: Int, _ numPoints: Int, _ firstTime: Bool) -> Int {
            var d = firstTime ? deltaIn / damp : deltaIn / 2
            d += d / numPoints
            var k = 0
            while d > ((base - tmin) * tmax) / 2 {
                d /= (base - tmin)
                k += base
            }
            return k + (((base - tmin + 1) * d) / (d + skew))
        }

        var deltaChars = Array(deltaStr)
        while !deltaChars.isEmpty {
            var delta = 0
            var w = 1
            var k = base
            while true {
                guard let c = deltaChars.first else { return nil }; deltaChars.removeFirst()
                let digit: Int
                if c >= "a" && c <= "z" { digit = Int(c.asciiValue! - 97) }          // 0-25
                else if c >= "0" && c <= "9" { digit = Int(c.asciiValue! - 22) }     // 26-35
                else if c >= "A" && c <= "Z" { digit = Int(c.asciiValue! - 65) }     // 0-25
                else { return nil }
                delta += digit * w
                let t = k <= bias ? tmin : (k >= bias + tmax ? tmax : k - bias)
                if digit < t { break }
                w *= (base - t)
                guard w <= Int.max / (base - t) else { return nil } // overflow
                k += base
            }
            guard delta >= 0 else { return nil }

            let n2 = n + delta / (output.count + 1)
            guard n2 >= n else { return nil } // overflow
            n = n2
            let insertPos = delta % (output.count + 1)
            guard let scalar = UnicodeScalar(n) else { return nil }
            output.insert(Character(scalar), at: insertPos)
            bias = adapt(delta, output.count, output.count == basic.count + 1)
        }
        return String(output)
    }

    /// Enforces RFC 1035 label (≤63 bytes) and name (≤255 bytes on the wire) limits.
    public static func validateDomainName(_ name: String) throws {
        let trimmed = name.trimmingCharacters(in: .whitespaces).trimmingCharacters(in: CharacterSet(charactersIn: "."))
        guard !trimmed.isEmpty else {
            throw DNSResolverError.invalidName("DNS name is empty")
        }
        // Wire form is 253 characters max (255 bytes including the length octets and root label).
        if trimmed.utf8.count > 253 {
            throw DNSResolverError.invalidName("DNS name exceeds 253 bytes")
        }
        for label in trimmed.split(separator: ".", omittingEmptySubsequences: false) {
            let count = label.utf8.count
            if count == 0 || count > 63 {
                throw DNSResolverError.invalidName("DNS label length is invalid (\(count) bytes)")
            }
        }
    }

    // MARK: Wire Format — Response Parsing
    //
    // Decodes raw DNS response packets into typed structures: header flags, RCODE, answer/
    // authority/additional sections, EDNS0 OPT, and name decompression. Validation and
    // filtering (off-type, off-name, off-class) are applied here. Pure byte-level decoding
    // with no transport dependencies.

    /// Legacy parse helper returning only records.
    public static func parseResponsePacket(_ data: Data) -> [DNSRecord] {
        parseResponsePacketDetailed(data).records
    }

    /// Parses a DNS response into typed status metadata, filtering to the requested type
    /// and failing safely on malformed packets.
    public static func parseResponsePacketDetailed(_ data: Data, requestedType: UInt16? = nil, expectedTransactionID: UInt16? = nil, expectedClass: UInt16 = 1) -> DNSResponse {
        guard data.count >= 12 else {
            return DNSResponse(status: .malformedResponse, errorMessage: "DNS response shorter than header")
        }
        // Index into Data using its own indices safely.
        let bytes = [UInt8](data)

        let transactionID = (UInt16(bytes[0]) << 8) | UInt16(bytes[1])
        if let expected = expectedTransactionID, transactionID != expected {
            return DNSResponse(status: .responseIdMismatch, errorMessage: "DNS transaction ID mismatch")
        }

        let flags = (UInt16(bytes[2]) << 8) | UInt16(bytes[3])
        let qr = (flags & 0x8000) != 0
        guard qr else {
            return DNSResponse(status: .malformedResponse, errorMessage: "Not a response packet (QR bit is 0)")
        }
        let truncated = (flags & 0x0200) != 0
        let responseCode = flags & 0x000F
        let isAuthoritative = (flags & 0x0400) != 0
        let recursionAvailable = (flags & 0x0080) != 0
        let authenticatedData = (flags & 0x0020) != 0

        let answerCount = (Int(bytes[6]) << 8) | Int(bytes[7])
        let questionCount = (Int(bytes[4]) << 8) | Int(bytes[5])
        let authorityCount = (Int(bytes[8]) << 8) | Int(bytes[9])

        // Best-effort authority-section parse (A6): walk past the question + answer sections to the
        // authority records (SOA / NSEC / NS carried on negative answers). Best-effort by design — a
        // failure here never downgrades a valid answer to `.malformedResponse`; it yields an empty set.
        func parseAuthority() -> [DNSRecord] {
            guard let afterQuestions = skipSection(in: bytes, startingAt: 12, questionCount: questionCount) else { return [] }
            let afterAnswers = parseRecordsBestEffort(in: bytes, startingAt: afterQuestions, count: answerCount).next
            return parseRecordsBestEffort(in: bytes, startingAt: afterAnswers, count: authorityCount).records
        }

        // Best-effort EDNS0 OPT parse (F1): walk to the additional section and read the resolver's OPT
        // pseudo-record, if any. `nil` means the resolver did not echo OPT (no EDNS0).
        let additionalCount = (Int(bytes[10]) << 8) | Int(bytes[11])
        func parseEDNS() -> (info: EDNSResponseInfo?, malformed: Bool) {
            // additionalCount declared records but we cannot even reach the additional section → the
            // packet is corrupt and EDNS support is UNKNOWN, never a confident "absent".
            guard let afterQuestions = skipSection(in: bytes, startingAt: 12, questionCount: questionCount) else { return (nil, true) }
            let afterAnswers = parseRecordsBestEffort(in: bytes, startingAt: afterQuestions, count: answerCount).next
            let afterAuthority = parseRecordsBestEffort(in: bytes, startingAt: afterAnswers, count: authorityCount).next
            return findOPTRecord(in: bytes, startingAt: afterAuthority, count: additionalCount)
        }

        // B072: combine the EDNS extended RCODE (upper 8 bits from the OPT TTL) with the
        // base RCODE so BADVERS (16) is not read as success (RCODE 0).
        // B073/B071: distinguish "no OPT" (absent — a legacy resolver) from "additional section
        // declared records but the OPT could not be parsed" (malformed → EDNS support UNKNOWN).
        // `ednsMalformed` carries that third state so a malformed OPT is never rendered as a
        // confident "EDNS absent / not supported".
        let ednsResult = additionalCount > 0 ? parseEDNS() : (info: EDNSResponseInfo?.none, malformed: false)
        let ednsInfo = ednsResult.info
        let ednsMalformed = ednsResult.malformed
        let combinedResponseCode = ednsInfo.map { responseCode | (UInt16($0.extendedRCode) << 4) } ?? responseCode

        // C128: truncated responses must still populate EDNS and header-derived fields so the
        // response is not discarded and the extended RCODE is reported (matching Windows).
        if truncated {
            let authRecs = parseAuthority()
            return DNSResponse(status: .truncated, authorityRecords: authRecs,
                               edns: ednsInfo, ednsMalformed: ednsMalformed,
                               authorityMalformed: authorityCount > 0 && authRecs.count < authorityCount,
                               responseCode: combinedResponseCode, isAuthoritative: isAuthoritative,
                               isTruncated: true, recursionAvailable: recursionAvailable,
                               authenticatedData: authenticatedData,
                               responseByteLength: bytes.count)
        }

        // C069: all non-success RCODE branches populate the parsed evidence (edns, authority
        // records, responseByteLength) so the response is not discarded just because the
        // resolver answered with an error. The evidence is already parsed — dropping it was
        // a data-loss bug, not a deliberate design choice.
        switch combinedResponseCode {
        case 0: break
        case 2:
            let authRecs = parseAuthority()
            return DNSResponse(status: .servFail, authorityRecords: authRecs,
                               edns: ednsInfo, ednsMalformed: ednsMalformed,
                               authorityMalformed: authorityCount > 0 && authRecs.count < authorityCount,
                               responseCode: combinedResponseCode, isAuthoritative: isAuthoritative,
                               recursionAvailable: recursionAvailable, authenticatedData: authenticatedData,
                               responseByteLength: bytes.count)
        case 3:
            let authRecs = parseAuthority()
            return DNSResponse(status: .nxDomain, authorityRecords: authRecs,
                               edns: ednsInfo, ednsMalformed: ednsMalformed,
                               authorityMalformed: authorityCount > 0 && authRecs.count < authorityCount,
                               responseCode: combinedResponseCode, isAuthoritative: isAuthoritative,
                               recursionAvailable: recursionAvailable, authenticatedData: authenticatedData,
                               responseByteLength: bytes.count)
        case 5:
            let authRecs = parseAuthority()
            return DNSResponse(status: .refused, authorityRecords: authRecs,
                               edns: ednsInfo, ednsMalformed: ednsMalformed,
                               authorityMalformed: authorityCount > 0 && authRecs.count < authorityCount,
                               responseCode: combinedResponseCode, isAuthoritative: isAuthoritative,
                               recursionAvailable: recursionAvailable, authenticatedData: authenticatedData,
                               responseByteLength: bytes.count)
        default:
            // C069: distinct status for "resolver answered with an error RCODE" — not the same
            // state as "no answer received" (.transportError). Includes FORMERR (1), NOTIMP (4),
            // BADVERS (16), NOTAUTH (9), NOTZONE (10), etc.
            let authRecs = parseAuthority()
            return DNSResponse(status: .rcodeOther, authorityRecords: authRecs,
                               edns: ednsInfo, ednsMalformed: ednsMalformed,
                               authorityMalformed: authorityCount > 0 && authRecs.count < authorityCount,
                               responseCode: combinedResponseCode, isAuthoritative: isAuthoritative,
                               recursionAvailable: recursionAvailable, authenticatedData: authenticatedData,
                               errorMessage: "DNS RCODE \(combinedResponseCode)",
                               responseByteLength: bytes.count)
        }

        if answerCount == 0 {
            let authRecs = parseAuthority()
            return DNSResponse(
                status: .noData,
                authorityRecords: authRecs,
                edns: ednsInfo,
                ednsMalformed: ednsMalformed,
                authorityMalformed: authorityCount > 0 && authRecs.count < authorityCount,
                responseCode: combinedResponseCode,
                isAuthoritative: isAuthoritative,
                recursionAvailable: recursionAvailable,
                authenticatedData: authenticatedData,
                responseByteLength: bytes.count
            )
        }

        var offset = 12
        // Capture the first question's owner name (B070 — the root of the valid owner set) and
        // its QCLASS (C057: the class filter must compare against the queried class, not a
        // hardcoded 1/IN) while skipping the rest of the question section.
        var questionName: String? = nil
        var questionClass: UInt16 = expectedClass
        for i in 0..<questionCount {
            let qn: (name: String, nextOffset: Int)
            do {
                qn = try parseName(in: bytes, offset: offset)
            } catch {
                return DNSResponse(status: .malformedResponse, errorMessage: "\(error)")
            }
            if i == 0 { questionName = qn.name.lowercased() }
            offset = qn.nextOffset
            guard offset + 4 <= bytes.count else {
                return DNSResponse(status: .malformedResponse, errorMessage: "Question section exceeds packet bounds")
            }
            // C057: extract QCLASS from the first question so the answer-section class filter
            // compares against the queried class (CHAOS=3, IN=1) instead of a hardcoded 1.
            if i == 0 {
                let qtype = (UInt16(bytes[offset]) << 8) | UInt16(bytes[offset + 1])
                let qclass = (UInt16(bytes[offset + 2]) << 8) | UInt16(bytes[offset + 3])
                _ = qtype // reserved for future use; requestedType is the caller's filter
                questionClass = qclass
            }
            offset += 4 // type + class
        }

        // Phase 1 — parse every answer RR (owner name, type, class, ttl, rdata, and for a CNAME its
        // decoded target name). A strict bounds/parse failure fails the whole response, never a
        // silent success.
        struct RawAnswer {
            let name: String; let ownerLower: String; let type: UInt16; let cls: UInt16
            let ttl: UInt32; let rdata: String; let cnameTarget: String?
        }
        var raws: [RawAnswer] = []
        var parsedCount = 0
        // D020: cap answer count at 256 to prevent CPU DoS from hostile responses with thousands of records.
        let maxAnswers = 256
        guard answerCount <= maxAnswers else {
            return DNSResponse(status: .malformedResponse,
                               errorMessage: "Answer count \(answerCount) exceeds maximum \(maxAnswers) (CPU DoS protection)")
        }
        // D020: global pointer-jump budget across all names in this response (1024 jumps max).
        let budget = JumpBudget(1024)
        for _ in 0..<answerCount {
            guard offset < bytes.count else { break }
            let name: String
            do {
                let parsed = try parseName(in: bytes, offset: offset, jumpBudget: budget)
                name = parsed.name
                offset = parsed.nextOffset
            } catch {
                return DNSResponse(status: .malformedResponse, errorMessage: "\(error)")
            }

            guard offset + 10 <= bytes.count else {
                return DNSResponse(status: .malformedResponse, errorMessage: "Answer header exceeds packet bounds")
            }
            let type = (UInt16(bytes[offset]) << 8) | UInt16(bytes[offset + 1])
            let cls  = (UInt16(bytes[offset + 2]) << 8) | UInt16(bytes[offset + 3])
            let ttl = (UInt32(bytes[offset + 4]) << 24) |
                      (UInt32(bytes[offset + 5]) << 16) |
                      (UInt32(bytes[offset + 6]) << 8) |
                      UInt32(bytes[offset + 7])
            let rdLength = (Int(bytes[offset + 8]) << 8) | Int(bytes[offset + 9])
            offset += 10

            guard offset + rdLength <= bytes.count else {
                return DNSResponse(status: .malformedResponse, errorMessage: "RDATA exceeds packet bounds")
            }
            let rdataStr = formatRData(bytes: bytes, offset: offset, rdLength: rdLength, type: type)
            // A CNAME's RDATA is a single (possibly compressed) domain name — decode it so the owner
            // chain can be tracked (B070). Non-CNAME RRs carry no chain target.
            // D020: pass the jump budget to prevent CPU DoS.
            // C065: pass endLimit so the linear walk stays within the declared RDLENGTH.
            let cnameTarget = (cls == 1 && type == 5)
                ? (try? parseName(in: bytes, offset: offset, jumpBudget: budget, endLimit: offset + rdLength))?.name.lowercased()
                : nil
            raws.append(RawAnswer(name: name, ownerLower: name.lowercased(), type: type, cls: cls,
                                  ttl: ttl, rdata: rdataStr, cnameTarget: cnameTarget))
            offset += rdLength
            parsedCount += 1
        }

        // B068/A077: declared ANCOUNT > actually parsed → corrupt/truncated, never .success or .noData
        if parsedCount < answerCount {
            return DNSResponse(status: .malformedResponse,
                               errorMessage: "Declared ANCOUNT \(answerCount) exceeds parsed records \(parsedCount)")
        }

        // B070 — build the set of owner names that legitimately belong to this answer: the question
        // name plus every CNAME target reachable from it (fixpoint, so an out-of-order CNAME chain is
        // still resolved). An in-class address RR whose owner is outside this set is a smuggled
        // off-name record and must not be surfaced as a resolved address.
        var validOwners = Set<String>()
        if let q = questionName { validOwners.insert(q) }
        // C072: detect conflicting CNAMEs — two CNAME records with the same owner but different
        // targets are invalid DNS and an injection signature. Count them so they leave a trace
        // instead of silently widening validOwners with both targets.
        var conflictingCNAMEOwnerCount = 0
        var cnameTargetsByOwner: [String: String] = [:]
        for r in raws where r.cls == questionClass && r.type == 5 {
            if let t = r.cnameTarget {
                if let existing = cnameTargetsByOwner[r.ownerLower], existing != t {
                    conflictingCNAMEOwnerCount += 1
                } else {
                    cnameTargetsByOwner[r.ownerLower] = t
                }
            }
        }
        var grew = true
        while grew {
            grew = false
            for r in raws where r.cls == questionClass && r.type == 5 {
                if validOwners.contains(r.ownerLower), let t = r.cnameTarget, !validOwners.contains(t) {
                    validOwners.insert(t)
                    grew = true
                }
            }
        }

        // Phase 2 — apply the class (B069/C057), owner-name (B070) and type filters.
        var records: [DNSRecord] = []
        var offTypeCount = 0
        var offNameCount = 0
        var offClassCount = 0  // C057: count class-rejected RRs so they leave a trace instead of vanishing
        for r in raws {
            // C057: reject off-class RRs by comparing against the queried QCLASS (not a hardcoded
            // 1/IN). A CHAOS (class 3) response has CHAOS-class answers that were previously
            // silently dropped because the filter was `r.cls != 1`.
            if r.cls != questionClass {
                offClassCount += 1
                continue
            }
            // B070/C059: an in-class RR whose owner is not in the question/CNAME chain is off-name.
            // Originally enforced only for A/AAAA (types 1/28); C059 extends this to DNSKEY (48) and
            // RRSIG (46) so a DNSSEC record from an unrelated zone cannot ride in the answer section
            // and defeat the stripping canary. (Only enforced when the question name is known, so a
            // parse gap never drops everything.)
            if (r.type == 1 || r.type == 28 || r.type == 46 || r.type == 48) && !validOwners.isEmpty && !validOwners.contains(r.ownerLower) {
                offNameCount += 1
                continue
            }
            // Surface answers of the requested type (M04), CNAMEs (needed for chain-following) and
            // RRSIGs (the DNSSEC signature companion of a DO-enabled answer — it carries no address
            // and never substitutes the queried answer, so it is not off-type poisoning; the DNSSEC
            // verifier needs it to check the covering signature over the DNSKEY RRset — A030/A036).
            // Count off-type answers separately so callers can detect poisoning attempts (KDNS-06).
            if requestedType == nil || r.type == requestedType! || r.type == 5 /* CNAME */ || r.type == 46 /* RRSIG */ {
                records.append(DNSRecord(name: r.name, type: r.type, ttl: r.ttl, rdata: r.rdata))
            } else {
                offTypeCount += 1
            }
        }

        // Records of the exact requested type (excluding CNAME glue) determine success/NODATA.
        let answerRecords = requestedType == nil
            ? records
            : records.filter { $0.type == requestedType! }

        // C057: when the class filter dropped ALL answer RRs (offClassCount > 0, no surviving
        // records), the response is inconclusive — the answer existed but was in a different
        // class. This prevents a CHAOS-class TXT response from being silently reported as
        // `.noData` (which reads as "resolver has no banner" when the banner was discarded).
        let status: DNSResponseStatus
        if answerRecords.isEmpty && offClassCount > 0 && records.isEmpty {
            status = .malformedResponse
        } else {
            status = answerRecords.isEmpty ? .noData : .success
        }

        // `offset` now sits at the start of the authority section (after the last answer).
        let afterAnswers = parseRecordsBestEffort(in: bytes, startingAt: offset, count: authorityCount)
        return DNSResponse(
            status: status,
            records: records,
            authorityRecords: afterAnswers.records,
            edns: ednsInfo,
            ednsMalformed: ednsMalformed,
            authorityMalformed: authorityCount > 0 && afterAnswers.records.count < authorityCount,
            offTypeAnswerCount: offTypeCount,
            offNameAnswerCount: offNameCount,
            offClassAnswerCount: offClassCount,
            conflictingCNAMEOwnerCount: conflictingCNAMEOwnerCount,
            responseCode: combinedResponseCode,
            isAuthoritative: isAuthoritative,
            isTruncated: truncated,
            recursionAvailable: recursionAvailable,
            authenticatedData: authenticatedData,
            responseByteLength: bytes.count
        )
    }

    /// Skips `questionCount` question entries (name + type + class), returning the offset just past
    /// them, or nil if the section runs out of bounds. Used to locate the answer/authority sections.
    private static func skipSection(in bytes: [UInt8], startingAt offset: Int, questionCount: Int) -> Int? {
        var off = offset
        for _ in 0..<questionCount {
            guard let next = try? skipName(in: bytes, offset: off) else { return nil }
            off = next + 4                       // QTYPE + QCLASS
            guard off <= bytes.count else { return nil }
        }
        return off
    }

    /// Parses up to `count` resource records starting at `offset`, stopping early on any bounds/parse
    /// error (best-effort — used for the authority section, which must never fail the whole response).
    /// Returns the records parsed and the offset just past them.
    private static func parseRecordsBestEffort(in bytes: [UInt8], startingAt offset: Int, count: Int) -> (records: [DNSRecord], next: Int) {
        var records: [DNSRecord] = []
        var off = offset
        for _ in 0..<count {
            guard off < bytes.count, let parsed = try? parseName(in: bytes, offset: off) else { break }
            var p = parsed.nextOffset
            guard p + 10 <= bytes.count else { break }
            let type = (UInt16(bytes[p]) << 8) | UInt16(bytes[p + 1])
            let ttl = (UInt32(bytes[p + 4]) << 24) | (UInt32(bytes[p + 5]) << 16) |
                      (UInt32(bytes[p + 6]) << 8) | UInt32(bytes[p + 7])
            let rdLength = (Int(bytes[p + 8]) << 8) | Int(bytes[p + 9])
            p += 10
            guard p + rdLength <= bytes.count else { break }
            records.append(DNSRecord(name: parsed.name, type: type, ttl: ttl,
                                     rdata: formatRData(bytes: bytes, offset: p, rdLength: rdLength, type: type)))
            off = p + rdLength
        }
        return (records, off)
    }

    /// Scans up to `count` additional-section records from `offset` for an EDNS0 OPT record (type 41).
    /// For OPT, the CLASS field carries the requestor UDP size and the TTL carries version + extended
    /// RCODE (RFC 6891). Best-effort; nil if no OPT is present.
    private static func findOPTRecord(in bytes: [UInt8], startingAt offset: Int, count: Int) -> (info: EDNSResponseInfo?, malformed: Bool) {
        var off = offset
        for _ in 0..<count {
            // The additional section declared `count` records but one could not be parsed within
            // bounds. We cannot confirm an OPT is absent → malformed (EDNS support UNKNOWN), never
            // a confident "no EDNS".
            guard off < bytes.count, let parsed = try? parseName(in: bytes, offset: off) else { return (nil, true) }
            var p = parsed.nextOffset
            guard p + 10 <= bytes.count else { return (nil, true) }
            let type = (UInt16(bytes[p]) << 8) | UInt16(bytes[p + 1])
            let cls  = (UInt16(bytes[p + 2]) << 8) | UInt16(bytes[p + 3])
            let ttl  = (UInt32(bytes[p + 4]) << 24) | (UInt32(bytes[p + 5]) << 16) |
                       (UInt32(bytes[p + 6]) << 8) | UInt32(bytes[p + 7])
            let rdLength = (Int(bytes[p + 8]) << 8) | Int(bytes[p + 9])
            p += 10
            guard p + rdLength <= bytes.count else { return (nil, true) }
            if type == 41 {   // OPT
                return (EDNSResponseInfo(udpPayloadSize: cls,
                                         version: UInt8((ttl >> 16) & 0xff),
                                         extendedRCode: UInt8((ttl >> 24) & 0xff),
                                         ecsScopePrefixLength: ecsScope(in: bytes, rdataStart: p, rdLength: rdLength)), false)
            }
            off = p + rdLength
        }
        // Walked every declared additional record cleanly and found no OPT → genuinely absent.
        return (nil, false)
    }

    /// Scans an OPT record's RDATA (a sequence of `[code(2)][len(2)][data]` options) for an EDNS
    /// Client Subnet option (code 8) and returns its SCOPE PREFIX-LENGTH (F10). A non-zero scope means
    /// the resolver applied/forwarded the subnet. `nil` if no ECS option is present.
    private static func ecsScope(in bytes: [UInt8], rdataStart: Int, rdLength: Int) -> UInt8? {
        var o = rdataStart
        let end = rdataStart + rdLength
        while o + 4 <= end {
            let code = (Int(bytes[o]) << 8) | Int(bytes[o + 1])
            let len  = (Int(bytes[o + 2]) << 8) | Int(bytes[o + 3])
            let dataStart = o + 4
            guard dataStart + len <= end else { break }
            if code == 8 && len >= 4 {   // ECS: family(2) + srcPrefix(1) + scopePrefix(1) + address
                return bytes[dataStart + 3]   // SCOPE PREFIX-LENGTH
            }
            o = dataStart + len
        }
        return nil
    }

    /// First three octets of a dotted IPv4 address (for a /24 ECS option); nil if not a valid IPv4.
    private static func ipv4OctetsForECS(_ ip: String) -> (UInt8, UInt8, UInt8)? {
        let parts = ip.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4 else { return nil }
        let octets = parts.compactMap { UInt8($0) }
        guard octets.count == 4 else { return nil }
        return (octets[0], octets[1], octets[2])
    }

    private static func formatRData(bytes: [UInt8], offset: Int, rdLength: Int, type: UInt16) -> String {
        if type == 1 && rdLength == 4 { // A Record (IPv4)
            return "\(bytes[offset]).\(bytes[offset + 1]).\(bytes[offset + 2]).\(bytes[offset + 3])"
        } else if type == 28 && rdLength == 16 { // AAAA Record (IPv6)
            return canonicalIPv6(Array(bytes[offset..<(offset + 16)]))
        }
        // P-3 H1: decode name-type RDATA (CNAME, NS, PTR, DNAME) instead of hex-dumping.
        // The parseName function handles DNS compression pointers within the full packet.
        // C065: pass endLimit so the linear walk cannot read past the declared RDLENGTH.
        let nameTypes: Set<UInt16> = [2, 5, 12, 39]  // NS, CNAME, PTR, DNAME
        if nameTypes.contains(type), let parsed = try? parseName(in: bytes, offset: offset, endLimit: offset + rdLength) {
            return parsed.name.lowercased()
        }
        return bytes[offset..<(offset + rdLength)].map { String(format: "%02x", $0) }.joined()
    }

    /// Renders 16 raw bytes as a canonical, `::`-compressed lowercase IPv6 string via `inet_ntop`,
    /// so the same address always compares equal to a reference resolver's rendering instead of
    /// producing spurious mismatches (a naive `%x`-per-group join does not compress zero runs).
    static func canonicalIPv6(_ bytes16: [UInt8]) -> String {
        guard bytes16.count == 16 else {
            return bytes16.map { String(format: "%02x", $0) }.joined()
        }
        var addr = in6_addr()
        withUnsafeMutableBytes(of: &addr) { raw in
            for i in 0..<16 { raw[i] = bytes16[i] }
        }
        var buffer = [CChar](repeating: 0, count: Int(INET6_ADDRSTRLEN))
        let result = inet_ntop(AF_INET6, &addr, &buffer, socklen_t(INET6_ADDRSTRLEN))
        if result != nil {
            let end = buffer.firstIndex(of: 0) ?? buffer.endIndex
            return String(decoding: buffer[..<end].map { UInt8(bitPattern: $0) }, as: UTF8.self)
        }
        // Fallback: uncompressed groups.
        var groups: [String] = []
        for i in stride(from: 0, to: 16, by: 2) {
            groups.append(String(format: "%x", (Int(bytes16[i]) << 8) | Int(bytes16[i + 1])))
        }
        return groups.joined(separator: ":")
    }

    private struct DNSParseError: Error, CustomStringConvertible {
        let description: String
    }

    /// Skips a name, guarding against compression pointer loops and out-of-bounds reads.
    private static func skipName(in bytes: [UInt8], offset: Int) throws -> Int {
        var current = offset
        var visited = Set<Int>()
        while current < bytes.count {
            if !visited.insert(current).inserted {
                throw DNSParseError(description: "DNS name compression loop detected")
            }
            let len = Int(bytes[current])
            if len == 0 { return current + 1 }
            if (len & 0xC0) == 0xC0 {
                guard current + 1 < bytes.count else {
                    throw DNSParseError(description: "DNS compression pointer exceeds packet bounds")
                }
                return current + 2
            }
            if (len & 0xC0) != 0 || len > 63 || current + 1 + len > bytes.count {
                throw DNSParseError(description: "Malformed DNS label")
            }
            current += len + 1
        }
        throw DNSParseError(description: "DNS name exceeds packet bounds")
    }

    /// D020: Reference-type wrapper to track pointer-jump budget across all parseName calls
    /// in a single response, preventing O(n^2) CPU DoS from hostile pointer-dense responses.
    private final class JumpBudget {
        var remaining: Int
        init(_ limit: Int) { self.remaining = limit }
        func consume() throws {
            guard remaining > 0 else {
                throw DNSParseError(description: "DNS response exceeds pointer-jump budget (CPU DoS protection)")
            }
            remaining -= 1
        }
    }

    /// Parses a name following compression pointers, with loop and bounds protection.
    /// D020: optional jumpBudget tracks pointer jumps across the whole response to prevent CPU DoS.
    /// C065: optional endLimit bounds the linear (non-pointer) walk through RDATA so a short
    /// CNAME/NS/PTR/DNAME record cannot read past its declared RDLENGTH into following records.
    /// Compression pointers may legitimately target earlier packet offsets, so once a pointer is
    /// followed the bound reverts to the full packet length.
    private static func parseName(in bytes: [UInt8], offset: Int, jumpBudget: JumpBudget? = nil, endLimit: Int? = nil) throws -> (name: String, nextOffset: Int) {
        var parts: [String] = []
        var current = offset
        var jumped = false
        var nextOffset = offset
        var visited = Set<Int>()
        // C065: the linear walk is bounded by endLimit (when provided); pointer-chased walks
        // use the full packet length since compression targets can be anywhere in the packet.
        let linearBound = endLimit ?? bytes.count

        while current < (jumped ? bytes.count : linearBound) {
            if !visited.insert(current).inserted {
                throw DNSParseError(description: "DNS name compression loop detected")
            }
            let len = Int(bytes[current])
            if len == 0 {
                // Root label: the name is complete. (Bug fix: previously `break` fell through to the
                // trailing throw, so EVERY well-formed name raised "unterminated" and no answer or
                // authority record could ever be parsed — masked because no test fed a valid
                // answer-bearing packet through the parser.)
                if !jumped { nextOffset = current + 1 }
                return (parts.joined(separator: "."), nextOffset)
            } else if (len & 0xC0) == 0xC0 {
                guard current + 1 < bytes.count else {
                    throw DNSParseError(description: "DNS compression pointer exceeds packet bounds")
                }
                let pointer = ((len & 0x3F) << 8) | Int(bytes[current + 1])
                guard pointer < bytes.count else {
                    throw DNSParseError(description: "DNS compression pointer targets outside packet")
                }
                // D020: consume one jump from the global budget to prevent CPU DoS.
                try jumpBudget?.consume()
                if !jumped {
                    nextOffset = current + 2
                    jumped = true
                }
                current = pointer
            } else {
                // C065: when not following a pointer, bound the label read against linearBound
                // (which is endLimit for RDATA decoding, or bytes.count otherwise).
                let labelBound = jumped ? bytes.count : linearBound
                if (len & 0xC0) != 0 || len > 63 || current + 1 + len > labelBound {
                    throw DNSParseError(description: "Malformed DNS label")
                }
                let labelBytes = Array(bytes[(current + 1)..<(current + 1 + len)])
                // C064: reject (not drop) labels that are not valid UTF-8. DNS labels are
                // arbitrary octets (RFC 4343); silently dropping non-UTF-8 labels fabricates
                // a shorter name that can bypass the B070 off-name owner filter. A hostile
                // response must produce a malformed result, never a colliding name.
                guard let label = String(bytes: labelBytes, encoding: .utf8) else {
                    throw DNSParseError(description: "DNS label contains non-UTF-8 bytes — rejecting to prevent name fabrication")
                }
                parts.append(label)
                current += len + 1
                if !jumped { nextOffset = current }
            }
        }
        throw DNSParseError(description: "DNS name exceeds packet bounds — unterminated name")
    }

    /// Validates that the DNS response question section matches the expected query name (lowercased)
    /// and type, to detect spoofed responses answering a different question (KDNS-02).
    /// Returns `true` if the question is structurally valid and matches; `false` on parse failure or
    /// mismatch.
    /// Validates that the response's question section matches the expected name, type, and class.
    /// C129: also validates QDCOUNT == 1 (a standard DNS response has exactly one question) and
    /// compares QCLASS against the expected class to prevent acceptance of responses in a different
    /// class (e.g., CHAOS answers to IN queries).
    private static func responseQuestionMatches(_ data: Data, expectedName: String, expectedType: UInt16, expectedClass: UInt16 = 1) -> Bool {
        let bytes = [UInt8](data)
        guard bytes.count >= 12 else { return false }
        // C129: QDCOUNT must be 1 for a standard response (RFC 1035 §4.1.2).
        let qdcount = (Int(bytes[4]) << 8) | Int(bytes[5])
        guard qdcount == 1 else { return false }
        // Parse question name starting at offset 12 (after the 12-byte header).
        guard let qname = try? parseName(in: bytes, offset: 12) else { return false }
        let qnameLower = qname.name.lowercased()
        // P-3 H2: punycode-encode the expected name so IDN names (e.g. münchen.de)
        // match the A-label form (xn--mnchen-3ya.de) in the DNS response.
        let expectedASCII = (try? toASCIIName(expectedName)) ?? expectedName
        let expected = expectedASCII.trimmingCharacters(in: CharacterSet(charactersIn: ".")).lowercased()
        guard qnameLower == expected else { return false }
        // Read question type and class (4 bytes after the name)
        let typeOffset = qname.nextOffset
        guard typeOffset + 4 <= bytes.count else { return false }
        let qtype = (UInt16(bytes[typeOffset]) << 8) | UInt16(bytes[typeOffset + 1])
        guard qtype == expectedType else { return false }
        // C129: validate QCLASS matches the expected class (IN=1, CHAOS=3, etc.)
        let qclass = (UInt16(bytes[typeOffset + 2]) << 8) | UInt16(bytes[typeOffset + 3])
        guard qclass == expectedClass else { return false }
        return true
    }

    // MARK: Wire Format — Shared Response Validation
    //
    // Common validation patterns shared by UDP, TCP, and DoH transports. These methods
    // ensure response integrity (question section matches query) before returning parsed
    // results to callers.

    /// Validates that a parsed response's question section matches the expected query.
    /// Returns the response unchanged if validation passes, or a `.responseIdMismatch` error
    /// response if the question does not match (indicating a spoofed or misdirected response).
    /// Used by UDP, TCP fallback, and DoH paths to centralize the KDNS-02 validation logic.
    static func validateResponseQuestion(
        _ data: Data,
        parsed: DNSResponse,
        expectedName: String,
        expectedType: UInt16,
        transportLabel: String = ""
    ) -> DNSResponse {
        // Skip validation if the response already failed to parse (ID mismatch or malformed)
        guard parsed.status != .responseIdMismatch else { return parsed }
        guard responseQuestionMatches(data, expectedName: expectedName, expectedType: expectedType) else {
            let prefix = transportLabel.isEmpty ? "" : "\(transportLabel) "
            return DNSResponse(status: .responseIdMismatch,
                               errorMessage: "\(prefix)response question does not match query")
        }
        return parsed
    }

    // MARK: Transport — UDP / TCP / DoH
    //
    // Low-level network transport layer. Sends raw DNS packets over UDP (with TCP fallback
    // for truncation) and DoH (DNS-over-HTTPS). Each transport handles connection lifecycle,
    // timeout enforcement, and cancellation propagation. These methods are transport-only;
    // they do not parse or validate responses (see Wire Format above).

    private static func sendUDP(_ packet: Data, to server: String, port: UInt16) async throws -> Data {
        let endpoint = NWEndpoint.hostPort(host: NWEndpoint.Host(server), port: NWEndpoint.Port(integerLiteral: port))
        let connection = NWConnection(to: endpoint, using: .udp)
        let gate = ContinuationGate()

        return try await withThrowingTaskGroup(of: Data.self) { group in
            group.addTask {
                // C058 Fix 3: wrap in withTaskCancellationHandler so when the parent task group
                // (ProbeRunner deadline) cancels this task, the NWConnection is torn down
                // immediately instead of waiting for the internal timeout to fire. Without this,
                // a 2.5s probe deadline doesn't free the budget until the 2s UDP timeout completes.
                try await withTaskCancellationHandler(operation: {
                    try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Error>) in
                    connection.stateUpdateHandler = { state in
                        switch state {
                        case .ready:
                            connection.send(content: packet, completion: .contentProcessed({ error in
                                if let error = error {
                                    if gate.tryClose() { continuation.resume(throwing: error) }
                                    connection.cancel()
                                    return
                                }
                                connection.receiveMessage { content, _, _, error in
                                    if let error = error {
                                        if gate.tryClose() { continuation.resume(throwing: error) }
                                    } else if let content = content {
                                        if gate.tryClose() { continuation.resume(returning: content) }
                                    } else {
                                        if gate.tryClose() { continuation.resume(throwing: DNSResolverError.transport("No data received")) }
                                    }
                                    connection.cancel()
                                }
                            }))
                        case .failed(let error):
                            if gate.tryClose() { continuation.resume(throwing: error) }
                            connection.cancel()
                        case .waiting(let error):
                            if gate.tryClose() { continuation.resume(throwing: error) }
                            connection.cancel()
                        case .cancelled:
                            if gate.tryClose() { continuation.resume(throwing: DNSResolverError.timeout) }
                        default:
                            break
                        }
                    }
                    connection.start(queue: .global())
                    }
                }, onCancel: {
                    connection.cancel()
                })
            }

            // Timeout task.
            group.addTask {
                try await Task.sleep(nanoseconds: udpTimeoutNanos)
                connection.cancel()
                throw DNSResolverError.timeout
            }

            defer { group.cancelAll() }
            guard let result = try await group.next() else {
                throw DNSResolverError.timeout
            }
            return result
        }
    }

    private static func sendTCP(_ packet: Data, to server: String, port: UInt16) async throws -> Data {
        // DNS over TCP is length-prefixed with a 16-bit big-endian length.
        let framed: Data = {
            var f = Data()
            f.append(UInt8((packet.count >> 8) & 0xff))
            f.append(UInt8(packet.count & 0xff))
            f.append(packet)
            return f
        }()

        let endpoint = NWEndpoint.hostPort(host: NWEndpoint.Host(server), port: NWEndpoint.Port(integerLiteral: port))
        let connection = NWConnection(to: endpoint, using: .tcp)

        return try await withThrowingTaskGroup(of: Data.self) { group in
            group.addTask {
                // C058 Fix 3: cancellation-aware, same as sendUDP above.
                try await withTaskCancellationHandler(operation: {
                    try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Data, Error>) in
                    let gate = ContinuationGate()
                    connection.stateUpdateHandler = { state in
                        switch state {
                        case .ready:
                            connection.send(content: framed, completion: .contentProcessed({ error in
                                if let error = error {
                                    if gate.tryClose() { continuation.resume(throwing: error) }
                                    connection.cancel()
                                    return
                                }
                                readTCPResponse(connection, gate: gate, continuation: continuation)
                            }))
                        case .failed(let error):
                            if gate.tryClose() { continuation.resume(throwing: error) }
                            connection.cancel()
                        case .waiting(let error):
                            if gate.tryClose() { continuation.resume(throwing: error) }
                            connection.cancel()
                        default:
                            break
                        }
                    }
                    connection.start(queue: .global())
                    }
                }, onCancel: {
                    connection.cancel()
                })
            }
            group.addTask {
                try await Task.sleep(nanoseconds: udpTimeoutNanos)
                connection.cancel()
                throw DNSResolverError.timeout
            }
            defer { group.cancelAll() }
            guard let result = try await group.next() else {
                throw DNSResolverError.timeout
            }
            return result
        }
    }

    private static func readTCPResponse(_ connection: NWConnection, gate: ContinuationGate, continuation: CheckedContinuation<Data, Error>) {
        // Read the 2-byte length prefix, then exactly that many bytes.
        connection.receive(minimumIncompleteLength: 2, maximumLength: 2) { lengthData, _, _, error in
            if let error = error {
                if gate.tryClose() { continuation.resume(throwing: error) }
                connection.cancel()
                return
            }
            guard let lengthData = lengthData, lengthData.count == 2 else {
                if gate.tryClose() { continuation.resume(throwing: DNSResolverError.malformedResponse("Missing TCP length prefix")) }
                connection.cancel()
                return
            }
            let expected = (Int(lengthData[lengthData.startIndex]) << 8) | Int(lengthData[lengthData.startIndex + 1])
            guard expected > 0 else {
                if gate.tryClose() { continuation.resume(throwing: DNSResolverError.malformedResponse("Zero-length TCP DNS response")) }
                connection.cancel()
                return
            }
            connection.receive(minimumIncompleteLength: expected, maximumLength: expected) { payload, _, _, error in
                if let error = error {
                    if gate.tryClose() { continuation.resume(throwing: error) }
                } else if let payload = payload, payload.count == expected {
                    if gate.tryClose() { continuation.resume(returning: payload) }
                } else {
                    if gate.tryClose() { continuation.resume(throwing: DNSResolverError.malformedResponse("Incomplete TCP DNS response")) }
                }
                connection.cancel()
            }
        }
    }
}

// MARK: - Concurrency Helpers

/// A thread-safe one-shot gate ensuring a checked continuation is resumed at most once,
/// closing the multiple-resume window in the UDP/TCP state handlers (M06).
final class ContinuationGate: @unchecked Sendable {
    private let lock = NSLock()
    private var closed = false

    func tryClose() -> Bool {
        lock.lock()
        defer { lock.unlock() }
        if closed { return false }
        closed = true
        return true
    }
}

// MARK: - Response Bridging

private extension DNSResponse {
    /// Bridges the typed response back to the throwing `[DNSRecord]` API used by existing tools.
    func recordsOrThrow() throws -> [DNSRecord] {
        switch status {
        case .success, .noData, .nxDomain, .refused, .servFail:
            return records
        case .rcodeOther:
            // C069: the resolver answered with a non-success RCODE (FORMERR, NOTIMP, BADVERS, etc.).
            // Return whatever records came with it (often empty); the caller can inspect
            // responseCode for the specific RCODE.
            return records
        case .timeout:
            throw DNSResolverError.timeout
        case .malformedResponse:
            throw DNSResolverError.malformedResponse(errorMessage ?? "Malformed DNS response")
        case .responseIdMismatch:
            throw DNSResolverError.transport(errorMessage ?? "DNS response ID mismatch")
        case .truncated:
            throw DNSResolverError.transport(errorMessage ?? "DNS response truncated")
        case .transportError:
            throw DNSResolverError.transport(errorMessage ?? "DNS transport error")
        }
    }
}

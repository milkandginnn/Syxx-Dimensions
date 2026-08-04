import Foundation
import Security
import Darwin   // proc_pidpath: resolve a kernel-supplied pid to its executable path

public struct ProcessInfoReport: Codable, Sendable {
    /// `nil` in every field below means genuinely unknown — never guessed or fabricated. A
    /// forensic report that invents "mDNSResponder" for every unattributable packet launders
    /// whatever process actually made the query (M27); reporting unknown honestly preserves that
    /// uncertainty instead of hiding it.
    public let path: String?
    public let signingID: String?
    public let pid: Int?
    public let name: String?
    public let isAttributed: Bool
    /// D031: confidence level of the attribution. Kernel-supplied (pktap) pids are `.high`;
    /// post-hoc lsof lookups are `.low` (the port may have been recycled since the packet was
    /// captured). Surfaces must render low-confidence attributions distinctly (e.g. "process X
    /// (low confidence)") so the user knows the attribution is a best-effort guess, not a fact.
    public let confidence: AttributionConfidence

    public enum AttributionConfidence: String, Codable, Sendable {
        /// Kernel-supplied pid delivered in-flow by the pktap header at capture time.
        case high
        /// Post-hoc lsof port lookup — the socket may have been recycled since the packet was
        /// captured, so this is a best-effort guess, not a fact (D031).
        case low
    }

    enum CodingKeys: String, CodingKey {
        case path
        case signingID = "signing ID"
        case pid
        case name
        case isAttributed = "attributed"
        case confidence
    }

    public init(path: String?, signingID: String?, pid: Int?, name: String?,
                confidence: AttributionConfidence = .high) {
        self.path = path
        self.signingID = signingID
        self.pid = pid
        self.name = name
        self.isAttributed = pid != nil
        self.confidence = confidence
    }

    public static let unknown = ProcessInfoReport(path: nil, signingID: nil, pid: nil, name: nil)
}

public struct DNSQuestionReport: Codable, Sendable {
    public let questionName: String
    public let questionClass: String
    public let questionType: String

    enum CodingKeys: String, CodingKey {
        case questionName = "Question Name"
        case questionClass = "Question Class"
        case questionType = "Question Type"
    }

    public init(questionName: String, questionClass: String, questionType: String) {
        self.questionName = questionName
        self.questionClass = questionClass
        self.questionType = questionType
    }
}

public struct DNSAnswerReport: Codable, Sendable {
    public let name: String
    public let type: String
    public let ttl: UInt32
    public let rdata: String

    enum CodingKeys: String, CodingKey {
        case name = "Name"
        case type = "Type"
        case ttl = "TTL"
        case rdata = "Rdata"
    }

    public init(name: String, type: String, ttl: UInt32, rdata: String) {
        self.name = name
        self.type = type
        self.ttl = ttl
        self.rdata = rdata
    }
}

public struct DNSPacketReport: Codable, Sendable {
    public let opcode: String
    public let qr: String
    public let questions: [DNSQuestionReport]
    public let answers: [DNSAnswerReport]
    public let ra: String
    public let rcode: String
    public let rd: String
    public let xid: UInt16
    public let tc: String
    public let aa: String
    /// DNS payload byte length of this packet (datapoint F9 — response-size / amplification telemetry).
    /// Optional so reports written before this field simply decode to `nil` instead of failing.
    public let payloadBytes: Int?
    /// C023: `true` when the parser broke out of question/answer loops early due to a bounds or
    /// parse error — the report is a partial decode, not a complete representation of the packet.
    /// Downstream consumers must treat partial reports as inconclusive evidence, never as a
    /// genuine empty-section result. `nil` / absent in older reports means unknown.
    public let isPartial: Bool?

    enum CodingKeys: String, CodingKey {
        case opcode = "Opcode"
        case qr = "QR"
        case questions = "Questions"
        case answers = "Answers"
        case ra = "RA"
        case rcode = "Rcode"
        case rd = "RD"
        case xid = "XID"
        case tc = "TC"
        case aa = "AA"
        case payloadBytes = "Payload Bytes"
        case isPartial = "Partial Parse"
    }

    public init(opcode: String, qr: String, questions: [DNSQuestionReport], answers: [DNSAnswerReport], ra: String, rcode: String, rd: String, xid: UInt16, tc: String, aa: String, payloadBytes: Int? = nil, isPartial: Bool? = nil) {
        self.opcode = opcode
        self.qr = qr
        self.questions = questions
        self.answers = answers
        self.ra = ra
        self.rcode = rcode
        self.rd = rd
        self.xid = xid
        self.tc = tc
        self.aa = aa
        self.payloadBytes = payloadBytes
        self.isPartial = isPartial
    }
}

/// The IP/UDP transport context a DNS packet arrived in — the piece the old capture omitted
/// entirely. Without resolver IP, ports, and direction, a forensic report cannot show that DNS
/// traffic went to an unexpected/hijacked resolver (M25).
public struct NetworkContextReport: Codable, Sendable {
    public let ipVersion: Int
    public let sourceIP: String
    public let destIP: String
    public let sourcePort: UInt16
    public let destPort: UInt16
    public let direction: String // "Outbound Query" or "Inbound Response", from the local host's perspective

    enum CodingKeys: String, CodingKey {
        case ipVersion = "IP Version"
        case sourceIP = "Source IP"
        case destIP = "Dest IP"
        case sourcePort = "Source Port"
        case destPort = "Dest Port"
        case direction = "Direction"
    }

    public init(ipVersion: Int, sourceIP: String, destIP: String, sourcePort: UInt16, destPort: UInt16, direction: String) {
        self.ipVersion = ipVersion
        self.sourceIP = sourceIP
        self.destIP = destIP
        self.sourcePort = sourcePort
        self.destPort = destPort
        self.direction = direction
    }
}

public struct DNSCaptureRecord: Codable, Sendable {
    public let process: ProcessInfoReport
    public let timestamp: String
    public let packet: DNSPacketReport
    public let network: NetworkContextReport

    enum CodingKeys: String, CodingKey {
        case process = "Process"
        case timestamp = "Timestamp"
        case packet = "Packet"
        case network = "Network"
    }

    public init(process: ProcessInfoReport, timestamp: String, packet: DNSPacketReport, network: NetworkContextReport) {
        self.process = process
        self.timestamp = timestamp
        self.packet = packet
        self.network = network
    }
}

/// The user-facing **raw-queries export** — the "queries in JSON" companion to the interpreted report
/// (interpretation first, raw evidence second). A privacy-considered view of each captured DNS packet:
/// the query essentials plus answers, keyed by process **name only** — never the full process path or
/// signing ID the forensic dump retains — so a user can keep and analyse their own DNS activity without
/// the sensitive process-path metadata that motivates erasing the full capture.
public struct DNSQueryExport: Codable, Sendable {
    public let timestamp: String
    public let direction: String
    public let domain: String
    public let type: String
    public let recordClass: String
    public let rcode: String
    public let transactionId: UInt16
    public let resolver: String
    public let process: String?
    public let answers: [DNSAnswerReport]

    enum CodingKeys: String, CodingKey {
        case timestamp, direction, domain, type
        case recordClass = "class"
        case rcode, transactionId, resolver, process, answers
    }

    /// Build the raw-query view from a captured record (resolver = the port-53 side; process **name** only).
    public init(record: DNSCaptureRecord) {
        let q = record.packet.questions.first
        self.timestamp = record.timestamp
        self.direction = record.network.direction
        self.domain = q?.questionName ?? "unknown"
        self.type = q?.questionType ?? "unknown"
        self.recordClass = q?.questionClass ?? "IN"
        self.rcode = record.packet.rcode
        self.transactionId = record.packet.xid
        self.resolver = record.network.sourcePort == 53 ? record.network.sourceIP : record.network.destIP
        self.process = record.process.name
        self.answers = record.packet.answers
    }

    /// Read a JSONL capture file (one `DNSCaptureRecord` per line) and render the raw-query export — a
    /// pretty-printed JSON **array** of `DNSQueryExport`. Malformed lines are skipped best-effort (never
    /// fails the whole export). Returns `nil` only when the file can't be read; an empty capture → `[]`.
    public static func arrayJSON(fromCaptureFile path: String) -> String? {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return nil }
        let dec = JSONDecoder()
        var exports: [DNSQueryExport] = []
        for line in text.split(whereSeparator: \.isNewline) {
            let s = line.trimmingCharacters(in: .whitespaces)
            guard !s.isEmpty, let data = s.data(using: .utf8),
                  let rec = try? dec.decode(DNSCaptureRecord.self, from: data) else { continue }
            exports.append(DNSQueryExport(record: rec))
        }
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
        guard let out = try? enc.encode(exports), let str = String(data: out, encoding: .utf8) else { return nil }
        return str
    }
}

public enum DNSCapture {

    private static let recordTypeNames: [UInt16: String] = [
        1: "A", 2: "NS", 5: "CNAME", 6: "SOA", 12: "PTR", 15: "MX", 16: "TXT", 28: "AAAA", 33: "SRV", 43: "DS", 46: "RRSIG", 48: "DNSKEY"
    ]

    // MARK: - Link-layer frame decoding (D107)

    /// pcap datalink (`DLT_*`) values a macOS Cage capture can present. Mirrors the
    /// `CAGE_DLT_*` constants in `macOS/Sources/Ccage/include/Ccage.h` (SyxxKit cannot import
    /// Ccage; `CageFrameWiringTests` pins both sides to the same raw values).
    public enum LinkLayerType {
        public static let null: Int32 = 0      // BSD loopback / NULL (4-byte AF header)
        public static let en10mb: Int32 = 1    // Ethernet (14-byte header)
        public static let ppp: Int32 = 9       // PPP (4-byte address/control/protocol header)
        public static let raw: Int32 = 12      // Raw IP, no link header (macOS value)
        public static let pktap: Int32 = 149   // Apple PKTAP per-packet metadata header
    }

    /// The honest outcome of locating the IP packet inside one captured link-layer frame.
    /// Every non-`frame` case is distinguishable so the caller can count and disclose it —
    /// a frame is never silently dropped, and never mis-decoded under a wrong assumption.
    public enum LinkFrameDecode: Sendable {
        /// The IP payload was located. `attribution` carries the kernel's PKTAP process
        /// metadata when the capture was a PKTAP one; `nil` otherwise (a plain interface
        /// capture has no in-band attribution).
        case frame(ipPayload: Data, ipVersion: UInt8, attribution: PktapHeader?)
        /// A PKTAP header-only/drop record — kernel metadata, not a packet. Not an error.
        case nonPacketMetadata
        /// The frame is truncated or malformed for the claimed link type. Never guessed at.
        case malformed
        /// A datalink type this decoder has no handler for. This NEVER falls through to the
        /// Ethernet assumption (the D107 bug); the caller must count and surface it.
        case unsupportedLinkType(Int32)
    }

    /// Locate the IP packet (and, for PKTAP, the kernel's process attribution) inside one raw
    /// frame as returned by `pcap_next_ex`, branching on the capture handle's datalink type.
    ///
    /// D107: the previous logic lived inline in `SyxxCage.processPacket` and branched only on
    /// `DLT_NULL` vs. assume-Ethernet, so RAW/PPP/PKTAP frames were mis-decoded from byte 0 and
    /// every genuinely captured packet silently parsed to zero records — indistinguishable from
    /// "no traffic". Unknown DLTs now return `.unsupportedLinkType`, never an Ethernet guess.
    public static func decodeLinkFrame(_ data: Data, dlt: Int32) -> LinkFrameDecode {
        switch dlt {
        case LinkLayerType.null:
            // 4-byte AF header, HOST-endian (we only ever decode live local captures).
            guard data.count > 4 else { return .malformed }
            let s = data.startIndex
            let af = UInt32(data[s]) | (UInt32(data[s + 1]) << 8)
                   | (UInt32(data[s + 2]) << 16) | (UInt32(data[s + 3]) << 24)
            let version: UInt8
            switch af {
            case 2: version = 4       // AF_INET
            case 30: version = 6      // AF_INET6 (Darwin value)
            default: return .malformed
            }
            return .frame(ipPayload: data.subdata(in: (s + 4)..<data.endIndex),
                          ipVersion: version, attribution: nil)

        case LinkLayerType.en10mb:
            // 14-byte Ethernet header; only IPv4/IPv6 ethertypes can carry DNS.
            guard data.count > 14 else { return .malformed }
            let s = data.startIndex
            let etherType = (UInt16(data[s + 12]) << 8) | UInt16(data[s + 13])
            let version: UInt8
            switch etherType {
            case 0x0800: version = 4
            case 0x86DD: version = 6
            default: return .malformed
            }
            return .frame(ipPayload: data.subdata(in: (s + 14)..<data.endIndex),
                          ipVersion: version, attribution: nil)

        case LinkLayerType.ppp:
            // Minimal PPP framing: address/control (0xFF 0x03) + 2-byte protocol.
            guard data.count > 4 else { return .malformed }
            let s = data.startIndex
            let proto = (UInt16(data[s + 2]) << 8) | UInt16(data[s + 3])
            let version: UInt8
            switch proto {
            case 0x0021: version = 4  // PPP IPv4
            case 0x0057: version = 6  // PPP IPv6
            default: return .malformed
            }
            return .frame(ipPayload: data.subdata(in: (s + 4)..<data.endIndex),
                          ipVersion: version, attribution: nil)

        case LinkLayerType.raw:
            // Raw IP: zero link-header bytes — the IP header starts at byte 0.
            guard let first = data.first else { return .malformed }
            let version = first >> 4
            guard version == 4 || version == 6 else { return .malformed }
            return .frame(ipPayload: data, ipVersion: version, attribution: nil)

        case LinkLayerType.pktap:
            guard let header = PktapHeader.parse(data) else { return .malformed }
            guard header.isPacketRecord else { return .nonPacketMetadata }
            // The kernel's own counts: skip the metadata header, then the inner frame's
            // link-layer bytes (`pth_frame_pre_length` sidesteps the inner-DLT ambiguity).
            let ipStart = data.startIndex + header.headerLength + header.framePreLength
            guard ipStart < data.endIndex else { return .malformed }
            let ipPayload = data.subdata(in: ipStart..<data.endIndex)
            guard let first = ipPayload.first else { return .malformed }
            let version = first >> 4
            guard version == 4 || version == 6 else { return .malformed }
            return .frame(ipPayload: ipPayload, ipVersion: version, attribution: header)

        default:
            return .unsupportedLinkType(dlt)
        }
    }

    // MARK: - IP/UDP header parsing (M29)

    /// Parses the IP header (respecting the real IHL for IPv4 instead of assuming 20 bytes,
    /// and supporting the fixed 40-byte IPv6 base header) plus the following UDP or TCP header,
    /// and returns the offset where the DNS payload begins together with its validated length.
    ///
    /// C021: IPv4 fragments (MF=1 or fragment offset>0) are rejected — a non-initial fragment has
    /// arbitrary payload where the transport header is assumed, and reassembling fragments is out
    /// of scope for a per-packet capture parser.
    ///
    /// C020: IPv6 extension headers are walked (bounded, max 8 hops) so Hop-by-Hop, Routing,
    /// Fragment, and Destination Options headers no longer make DNS packets invisible. Fragment
    /// headers (type 44) are rejected explicitly — same reason as IPv4 fragments.
    ///
    /// C022: IPv6 now reads and validates the UDP length field, matching the IPv4 path.
    ///
    /// C024/C029: returns `dnsPayloadLength` derived from the UDP length field (not the captured
    /// buffer tail), so Ethernet padding and trailing bytes are never counted as DNS payload.
    ///
    /// C019: TCP (protocol 6) on port 53 is accepted alongside UDP. The caller must handle the
    /// 2-byte TCP DNS length prefix (see `parseTCPDNSPayload`).
    public static func parseIPUDPHeader(_ data: Data) -> (context: (ipVersion: Int, sourceIP: String, destIP: String, sourcePort: UInt16, destPort: UInt16), payloadOffset: Int, dnsPayloadLength: Int)? {
        guard let firstByte = data.first else { return nil }
        let version = Int(firstByte >> 4)

        if version == 4 {
            guard data.count >= 20 else { return nil }
            let ihl = Int(firstByte & 0x0F) * 4
            guard ihl >= 20, data.count >= ihl + 8 else { return nil }
            let s = data.startIndex
            let protocolNumber = data[s + 9]
            // C021: reject fragmented IPv4 packets. Bytes 6-7 carry flags (bit 5 = MF) and
            // the 13-bit fragment offset. A non-initial fragment has arbitrary bytes where the
            // UDP/TCP header is assumed — parsing it as a transport header fabricates evidence.
            let flagsAndFrag = (UInt16(data[s + 6]) << 8) | UInt16(data[s + 7])
            let mf = (flagsAndFrag & 0x2000) != 0
            let fragmentOffset = flagsAndFrag & 0x1FFF
            guard !mf, fragmentOffset == 0 else { return nil }

            guard protocolNumber == 17 || protocolNumber == 6 else { return nil } // UDP or TCP (C019)
            let isTCP = protocolNumber == 6

            if isTCP {
                // TCP header: minimum 20 bytes. Data offset in byte 12 (upper nibble, in 32-bit words).
                guard data.count >= ihl + 20 else { return nil }
                let tcpDataOffset = Int(data[s + ihl + 12] >> 4) * 4
                guard tcpDataOffset >= 20, data.count >= ihl + tcpDataOffset else { return nil }
                let srcPort = (UInt16(data[s + ihl]) << 8) | UInt16(data[s + ihl + 1])
                let dstPort = (UInt16(data[s + ihl + 2]) << 8) | UInt16(data[s + ihl + 3])
                guard srcPort == 53 || dstPort == 53 else { return nil }
                // TCP DNS uses a 2-byte length prefix before the DNS message. The validated DNS
                // payload length comes from that prefix, read by the caller in parseTCPDNSPayload.
                let dnsStart = ihl + tcpDataOffset
                let remaining = data.count - dnsStart
                guard remaining >= 2 else { return nil } // need at least the 2-byte length prefix
                let tcpDnsLen = (Int(data[s + dnsStart]) << 8) | Int(data[s + dnsStart + 1])
                guard tcpDnsLen >= 12, dnsStart + 2 + tcpDnsLen <= data.count else { return nil }
                let srcIP = "\(data[s+12]).\(data[s+13]).\(data[s+14]).\(data[s+15])"
                let dstIP = "\(data[s+16]).\(data[s+17]).\(data[s+18]).\(data[s+19])"
                return ((4, srcIP, dstIP, srcPort, dstPort), dnsStart + 2, tcpDnsLen)
            }

            // UDP path
            // KDNS-13: validate UDP length field and ensure DNS payload ≥ 12 bytes
            let udpLen = (UInt16(data[s + ihl + 4]) << 8) | UInt16(data[s + ihl + 5])
            guard udpLen >= 8, data.count >= ihl + Int(udpLen) else { return nil }
            // C024/C029: use the validated UDP payload length, not the buffer tail
            let dnsPayloadLen = Int(udpLen) - 8
            guard dnsPayloadLen >= 12 else { return nil }

            let srcIP = "\(data[s+12]).\(data[s+13]).\(data[s+14]).\(data[s+15])"
            let dstIP = "\(data[s+16]).\(data[s+17]).\(data[s+18]).\(data[s+19])"

            let udpStart = s + ihl
            let srcPort = (UInt16(data[udpStart]) << 8) | UInt16(data[udpStart + 1])
            let dstPort = (UInt16(data[udpStart + 2]) << 8) | UInt16(data[udpStart + 3])
            return ((4, srcIP, dstIP, srcPort, dstPort), ihl + 8, dnsPayloadLen)
        } else if version == 6 {
            guard data.count >= 40 else { return nil }
            let s = data.startIndex

            // C077: use inet_ntop for RFC 5952 canonical IPv6 text (:: zero compression,
            // lowercase hex, no leading zeros). The naive groupHex join produced uncompressed
            // addresses that never matched the system resolver list (scutil --dns uses canonical
            // compressed form), causing every IPv6 system resolver to be flagged as off-system.
            func ipv6String(offset: Int) -> String {
                var addr = (
                    data[s + offset], data[s + offset + 1], data[s + offset + 2], data[s + offset + 3],
                    data[s + offset + 4], data[s + offset + 5], data[s + offset + 6], data[s + offset + 7],
                    data[s + offset + 8], data[s + offset + 9], data[s + offset + 10], data[s + offset + 11],
                    data[s + offset + 12], data[s + offset + 13], data[s + offset + 14], data[s + offset + 15]
                )
                var buf = [CChar](repeating: 0, count: Int(INET6_ADDRSTRLEN))
                return withUnsafePointer(to: &addr) { ptr in
                    ptr.withMemoryRebound(to: UInt8.self, capacity: 16) { raw in
                        if inet_ntop(AF_INET6, raw, &buf, socklen_t(INET6_ADDRSTRLEN)) != nil {
                            let end = buf.firstIndex(of: 0) ?? buf.endIndex
                            return String(decoding: buf[..<end].map { UInt8(bitPattern: $0) }, as: UTF8.self)
                        }
                        return stride(from: 0, to: 16, by: 2)
                            .map { i in String(format: "%x", (Int(data[s + offset + i]) << 8) | Int(data[s + offset + i + 1])) }
                            .joined(separator: ":")
                    }
                }
            }
            let srcIP = ipv6String(offset: 8)
            let dstIP = ipv6String(offset: 24)

            // C020: walk IPv6 extension headers (bounded to 8 hops to prevent infinite loops).
            // Known extension header types that can appear before UDP/TCP:
            //   0 = Hop-by-Hop, 43 = Routing, 44 = Fragment, 51 = AH, 60 = Destination Options
            //   59 = No Next Header (means nothing follows — reject)
            var nextHeader = data[s + 6]
            var extOffset = 40 // start after the 40-byte base header
            let maxExtHops = 8
            for _ in 0..<maxExtHops {
                if nextHeader == 17 || nextHeader == 6 { break } // UDP or TCP found
                // Fragment header (44) is fixed 8 bytes and means the packet is fragmented —
                // reject it (same reasoning as C021 for IPv4).
                if nextHeader == 44 { return nil }
                // No Next Header (59) or unknown — cannot find transport.
                if nextHeader == 59 { return nil }
                // Known extension headers: read next-header and hdr-ext-len from bytes 0 and 1.
                // AH (51) uses a different length unit (4-byte multiples) — handle it separately.
                guard extOffset + 2 <= data.count else { return nil }
                let extNext = data[s + extOffset]
                if nextHeader == 51 { // AH: length in 4-byte units, minus 2
                    let ahLen = (Int(data[s + extOffset + 1]) + 2) * 4
                    guard extOffset + ahLen <= data.count else { return nil }
                    nextHeader = extNext
                    extOffset += ahLen
                } else {
                    // Hop-by-Hop (0), Routing (43), Destination Options (60): length in 8-byte units + 8
                    let extLen = (Int(data[s + extOffset + 1]) + 1) * 8
                    guard extOffset + extLen <= data.count else { return nil }
                    nextHeader = extNext
                    extOffset += extLen
                }
            }
            guard nextHeader == 17 || nextHeader == 6 else { return nil } // must end at UDP or TCP
            let isTCP = nextHeader == 6

            if isTCP {
                guard data.count >= extOffset + 20 else { return nil }
                let tcpDataOffset = Int(data[s + extOffset + 12] >> 4) * 4
                guard tcpDataOffset >= 20, data.count >= extOffset + tcpDataOffset else { return nil }
                let srcPort = (UInt16(data[s + extOffset]) << 8) | UInt16(data[s + extOffset + 1])
                let dstPort = (UInt16(data[s + extOffset + 2]) << 8) | UInt16(data[s + extOffset + 3])
                guard srcPort == 53 || dstPort == 53 else { return nil }
                let dnsStart = extOffset + tcpDataOffset
                let remaining = data.count - dnsStart
                guard remaining >= 2 else { return nil }
                let tcpDnsLen = (Int(data[s + dnsStart]) << 8) | Int(data[s + dnsStart + 1])
                guard tcpDnsLen >= 12, dnsStart + 2 + tcpDnsLen <= data.count else { return nil }
                return ((6, srcIP, dstIP, srcPort, dstPort), dnsStart + 2, tcpDnsLen)
            }

            // UDP path
            guard data.count >= extOffset + 8 else { return nil }
            let udpStart = s + extOffset
            // C022: read and validate the UDP length field (matching IPv4 path)
            let udpLen = (UInt16(data[udpStart + 4]) << 8) | UInt16(data[udpStart + 5])
            guard udpLen >= 8 else { return nil }
            let dnsPayloadLen = Int(udpLen) - 8
            guard dnsPayloadLen >= 12 else { return nil }
            // Ensure the validated UDP payload fits within the captured buffer
            guard extOffset + Int(udpLen) <= data.count else { return nil }

            let srcPort = (UInt16(data[udpStart]) << 8) | UInt16(data[udpStart + 1])
            let dstPort = (UInt16(data[udpStart + 2]) << 8) | UInt16(data[udpStart + 3])
            return ((6, srcIP, dstIP, srcPort, dstPort), extOffset + 8, dnsPayloadLen)
        }
        return nil
    }

    // MARK: - DNS payload parsing

    /// Parses a raw UDP DNS packet and extracts its query/response report structures, including
    /// answer records (not just the question section).
    public static func parseDNSPacket(_ data: Data) -> DNSPacketReport? {
        // C088: a bare 12-byte DNS header (QDCOUNT=0, ANCOUNT=0) is a legal packet —
        // e.g. a spoofed/empty response or a malformed reply worth recording. The old
        // `> 12` guard rejected it into the uncounted `.skipped` bucket while the IP-layer
        // guards (KDNS-13) at :361/:376 already use `>= 12`.
        guard data.count >= 12 else { return nil }
        let bytes = [UInt8](data)

        let xid = (UInt16(bytes[0]) << 8) | UInt16(bytes[1])
        let flags = (UInt16(bytes[2]) << 8) | UInt16(bytes[3])

        let qr = (flags & 0x8000) != 0 ? "Response" : "Query"
        let opcodeVal = (flags & 0x7800) >> 11
        let opcode = opcodeVal == 0 ? "Standard" : "Inverse"
        let aa = (flags & 0x0400) != 0 ? "Authoritative" : "Non-Authoritative"
        let tc = (flags & 0x0200) != 0 ? "Truncated" : "Non-Truncated"
        let rd = (flags & 0x0100) != 0 ? "Recursion desired" : "No recursion desired"
        let ra = (flags & 0x0080) != 0 ? "Recursion available" : "No recursion available"
        let rcodeVal = flags & 0x000F
        let rcode: String
        switch rcodeVal {
        case 0: rcode = "No error"
        case 2: rcode = "ServFail"
        case 3: rcode = "NXDomain"
        case 5: rcode = "Refused"
        default: rcode = "Error(\(rcodeVal))"
        }

        let qdCount = (Int(bytes[4]) << 8) | Int(bytes[5])
        let anCount = (Int(bytes[6]) << 8) | Int(bytes[7])

        var offset = 12
        var questions: [DNSQuestionReport] = []
        var questionParseComplete = true
        for _ in 0..<qdCount {
            guard offset < bytes.count else { questionParseComplete = false; break }
            guard let nameResult = try? parseName(in: bytes, offset: offset) else { questionParseComplete = false; break }
            offset = nameResult.nextOffset
            guard offset + 4 <= bytes.count else { questionParseComplete = false; break }
            let qTypeVal = (UInt16(bytes[offset]) << 8) | UInt16(bytes[offset + 1])
            let qClassVal = (UInt16(bytes[offset + 2]) << 8) | UInt16(bytes[offset + 3])
            offset += 4
            questions.append(DNSQuestionReport(
                questionName: nameResult.name,
                questionClass: qClassVal == 1 ? "IN" : "\(qClassVal)",
                questionType: recordTypeNames[qTypeVal] ?? "\(qTypeVal)"
            ))
        }

        var answers: [DNSAnswerReport] = []
        var answerParseComplete = true
        for _ in 0..<anCount {
            guard offset < bytes.count else { answerParseComplete = false; break }
            guard let nameResult = try? parseName(in: bytes, offset: offset) else { answerParseComplete = false; break }
            offset = nameResult.nextOffset
            guard offset + 10 <= bytes.count else { answerParseComplete = false; break }
            let type = (UInt16(bytes[offset]) << 8) | UInt16(bytes[offset + 1])
            let ttl = (UInt32(bytes[offset + 4]) << 24) | (UInt32(bytes[offset + 5]) << 16) | (UInt32(bytes[offset + 6]) << 8) | UInt32(bytes[offset + 7])
            let rdLength = (Int(bytes[offset + 8]) << 8) | Int(bytes[offset + 9])
            offset += 10
            guard offset + rdLength <= bytes.count else { answerParseComplete = false; break }
            let rdata = formatRData(bytes: bytes, offset: offset, rdLength: rdLength, type: type, allBytes: bytes)
            answers.append(DNSAnswerReport(name: nameResult.name, type: recordTypeNames[type] ?? "\(type)", ttl: ttl, rdata: rdata))
            offset += rdLength
        }

        // C023: flag the report as partial when question or answer loops broke early due to
        // bounds/parse errors. Downstream analyzers can then distinguish truncation from genuine
        // empty sections instead of treating both identically.
        let isPartial = !questionParseComplete || !answerParseComplete
            ? true
            : (questions.count < qdCount || answers.count < anCount ? true : nil)

        // A packet with no questions is still meaningful (e.g. a malformed/spoofed response) —
        // it is no longer silently discarded (M24 macOS-equivalent of W22-capture parity).
        return DNSPacketReport(
            opcode: opcode, qr: qr, questions: questions, answers: answers,
            ra: ra, rcode: rcode, rd: rd, xid: xid, tc: tc, aa: aa,
            payloadBytes: data.count,         // F9 — DNS payload wire size (caller slices to validated length)
            isPartial: isPartial
        )
    }

    /// C017: domain-name RDATA types (NS, CNAME, PTR) are now decoded against the full packet
    /// so compression pointers resolve correctly and the produced string is a real DNS name,
    /// not a hex blob. Equal CNAME targets across vantage points now compare equal.
    private static let domainNameRDataTypes: Set<UInt16> = [2 /* NS */, 5 /* CNAME */, 12 /* PTR */]

    private static func formatRData(bytes: [UInt8], offset: Int, rdLength: Int, type: UInt16, allBytes: [UInt8]? = nil) -> String {
        if type == 1 && rdLength == 4 {
            return "\(bytes[offset]).\(bytes[offset+1]).\(bytes[offset+2]).\(bytes[offset+3])"
        } else if type == 28 && rdLength == 16 {
            var groups: [String] = []
            for i in stride(from: 0, to: 16, by: 2) {
                let val = (Int(bytes[offset + i]) << 8) | Int(bytes[offset + i + 1])
                groups.append(String(format: "%x", val))
            }
            return groups.joined(separator: ":")
        } else if domainNameRDataTypes.contains(type), let packet = allBytes {
            // Decode domain-name RDATA using the full packet for compression pointer resolution.
            if let nameResult = try? parseName(in: packet, offset: offset) {
                return nameResult.name
            }
        }
        return bytes[offset..<(offset + rdLength)].map { String(format: "%02x", $0) }.joined()
    }

    private struct DNSParseError: Error {}

    /// C081: escape control characters, DEL, and non-printable code points in a DNS label
    /// so wire-controlled strings (ANSI escape sequences, embedded newlines, backspace) cannot
    /// rewrite terminal lines or inject fabricated rows into the saved report.
    /// Printable ASCII and non-ASCII Unicode are preserved; only the dangerous subset is escaped.
    private static func escapeLabelForPresentation(_ label: String) -> String {
        var result = ""
        for scalar in label.unicodeScalars {
            let v = scalar.value
            if v < 0x20 || v == 0x7F || (v >= 0x80 && v <= 0x9F) {
                // C0 (0x00–0x1F), DEL (0x7F), C1 (0x80–0x9F) — escape to \DDD
                result += String(format: "\\%03d", v)
            } else if scalar == "\\" {
                result += "\\\\"
            } else {
                result.append(Character(scalar))
            }
        }
        return result
    }

    private static func parseName(in bytes: [UInt8], offset: Int) throws -> (name: String, nextOffset: Int) {
        var parts: [String] = []
        var current = offset
        var jumped = false
        var nextOffset = offset
        var visited = Set<Int>()

        while current < bytes.count {
            if !visited.insert(current).inserted { throw DNSParseError() }
            let len = Int(bytes[current])
            if len == 0 {
                if !jumped { nextOffset = current + 1 }
                // C081: escape control characters at parse time so wire-controlled names
                // (ANSI escapes, embedded newlines, backspace) cannot rewrite terminal lines
                // or inject fabricated rows into the saved report.
                return (escapeLabelForPresentation(parts.joined(separator: ".")), nextOffset)
            } else if (len & 0xC0) == 0xC0 {
                guard current + 1 < bytes.count else { throw DNSParseError() }
                let pointer = ((len & 0x3F) << 8) | Int(bytes[current + 1])
                guard pointer < bytes.count else { throw DNSParseError() }
                if !jumped {
                    nextOffset = current + 2
                    jumped = true
                }
                current = pointer
            } else {
                guard (len & 0xC0) == 0, current + 1 + len <= bytes.count else { throw DNSParseError() }
                let labelBytes = Array(bytes[(current + 1)..<(current + 1 + len)])
                if let label = String(bytes: labelBytes, encoding: .utf8) {
                    parts.append(label)
                }
                current += len + 1
                if !jumped { nextOffset = current }
            }
        }
        throw DNSParseError()
    }

    // MARK: - Process attribution (M26/M27/M28)

    private struct CacheEntry {
        let report: ProcessInfoReport
        let cachedAt: Date
    }

    private static let cacheLock = NSLock()
    // D067: keyed by (pid, process start time) so a recycled pid cannot match a stale entry.
    // Bounded to `maxCacheEntries` with LRU-style eviction when full. The old comment
    // ("never returns a stale wrong identity") was false for the pid-reuse-within-TTL case.
    nonisolated(unsafe) private static var pidCache: [String: CacheEntry] = [:]
    private static let cacheTTL: TimeInterval = 5.0
    private static let maxCacheEntries = 1024

    /// Build the cache key from pid and process start time so a recycled pid gets a distinct key.
    private static func cacheKey(pid: Int) -> String {
        var rusage = rusage_info_v2()
        let rc = withUnsafeMutablePointer(to: &rusage) {
            $0.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) {
                proc_pid_rusage(Int32(pid), RUSAGE_INFO_V2, $0)
            }
        }
        if rc == 0 {
            return "\(pid):\(rusage.ri_proc_start_abstime)"
        }
        // Fallback: if proc_pid_rusage fails, use pid alone but with a shorter effective TTL
        return "\(pid):unknown"
    }

    /// Resolve full process detail from a kernel-supplied pid (delivered in-flow by the pktap header),
    /// replacing the old `lsof` port->pid lookup and its socket-lifetime race. The pid and short name
    /// are already attributed by the kernel; this only fills in the executable path and code-signing
    /// identity.
    ///
    /// Honesty: a `nil` pid (the kernel could not attribute the packet) returns `.unknown`, never a
    /// fabricated identity. If the process has since exited so its path can't be read, the kernel's pid
    /// and name are still reported (attributed) with a nil path/signingID — this is exactly the race
    /// the old `lsof` lookup lost, and pktap wins it because attribution happened at capture time.
    ///
    /// D067: the cache key includes process start time so a recycled pid cannot return a stale
    /// identity. The cache is bounded to `maxCacheEntries` to prevent unbounded growth.
    public static func pidToProcessInfo(pid: Int?, name: String?) -> ProcessInfoReport {
        guard let pid = pid else { return .unknown }

        let key = cacheKey(pid: pid)

        cacheLock.lock()
        if let cached = pidCache[key], Date().timeIntervalSince(cached.cachedAt) < cacheTTL {
            cacheLock.unlock()
            return cached.report
        }
        cacheLock.unlock()

        var path: String? = nil
        var buf = [CChar](repeating: 0, count: 4096)   // PROC_PIDPATHINFO_MAXSIZE (4 * MAXPATHLEN)
        let pathLen = proc_pidpath(Int32(pid), &buf, UInt32(buf.count))
        if pathLen > 0 {
            let resolved = String(decoding: buf.prefix(Int(pathLen)).map { UInt8(bitPattern: $0) }, as: UTF8.self)
            if !resolved.isEmpty { path = resolved }
        }
        let resolvedName = name ?? path.map { URL(fileURLWithPath: $0).lastPathComponent }
        let signing = path.flatMap { signingIdentifier(forExecutablePath: $0) }
        let report = ProcessInfoReport(path: path, signingID: signing, pid: pid, name: resolvedName)

        cacheLock.lock()
        // D067: evict expired entries first, then oldest if still at capacity
        if pidCache.count >= maxCacheEntries {
            let now = Date()
            pidCache = pidCache.filter { now.timeIntervalSince($0.value.cachedAt) < cacheTTL }
            if pidCache.count >= maxCacheEntries {
                // Still over capacity — evict oldest half
                let sorted = pidCache.sorted { $0.value.cachedAt < $1.value.cachedAt }
                let toRemove = sorted.prefix(sorted.count / 2)
                for (k, _) in toRemove { pidCache.removeValue(forKey: k) }
            }
        }
        pidCache[key] = CacheEntry(report: report, cachedAt: Date())
        cacheLock.unlock()
        return report
    }

    /// Reads the real code-signing identifier via `SecStaticCode`, instead of fabricating one
    /// from the process name (M28). Returns nil (not a guess) if the binary is unsigned or the
    /// signing information can't be read.
    private static func signingIdentifier(forExecutablePath path: String) -> String? {
        let url = URL(fileURLWithPath: path)
        var staticCode: SecStaticCode?
        guard SecStaticCodeCreateWithPath(url as CFURL, [], &staticCode) == errSecSuccess,
              let code = staticCode else {
            return nil
        }
        var information: CFDictionary?
        guard SecCodeCopySigningInformation(code, SecCSFlags(rawValue: kSecCSSigningInformation), &information) == errSecSuccess,
              let info = information as? [String: Any],
              let identifier = info[kSecCodeInfoIdentifier as String] as? String else {
            return nil
        }
        return identifier
    }

    /// LAST-RESORT fallback for plain (non-pktap) captures only: resolve a UDP port to a
    /// process info report using a post-hoc `lsof` lookup. This is D031's socket-lifetime race —
    /// it maps the packet to whoever holds the port at LOOKUP time, so it can miss (or, worse,
    /// misattribute) a short-lived socket. pktap captures never call this; they use the
    /// kernel-supplied pid via `pidToProcessInfo` instead.
    /// Returns a default "unknown" report when resolution fails (no crash).
    /// D031: results are marked `.low` confidence because the lsof lookup is post-hoc — the port
    /// may have been recycled since the packet was captured.
    public static func getProcessInfo(forPort port: UInt16) -> ProcessInfoReport {
        let task = Process()
        task.executableURL = URL(fileURLWithPath: "/usr/sbin/lsof")
        task.arguments = ["-i", "UDP:\(port)", "-F", "p"]
        let out = Pipe()
        task.standardOutput = out
        do {
            try task.run()
            task.waitUntilExit()
            let output = String(data: out.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
            for line in output.components(separatedBy: .newlines) where line.hasPrefix("p") {
                let pidStr = line.dropFirst()
                if let pid = Int32(pidStr) {
                    // D031: lsof-derived attribution is low-confidence — the port may have been
                    // recycled since the packet was captured.
                    let report = pidToProcessInfo(pid: Int(pid), name: nil)
                    return ProcessInfoReport(path: report.path, signingID: report.signingID,
                                             pid: report.pid, name: report.name, confidence: .low)
                }
            }
        } catch {}
        return .unknown
    }

}

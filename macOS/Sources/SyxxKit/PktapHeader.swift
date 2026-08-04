import Foundation

/// One parsed macOS PKTAP per-packet metadata header (link type `DLT_PKTAP`). The kernel prepends
/// this to every captured packet, carrying its own attribution: the originating process, the
/// *effective* originator behind a delegating proxy such as `mDNSResponder`, the interface, and the
/// direction, followed by the full inner frame. Field offsets and semantics are transcribed from
/// Apple XNU `bsd/net/pktap.h` (the struct lives under `#ifdef PRIVATE`, so there is no userland
/// header to import) and cross-checked against tcpdump `print-pktap.c`. All multi-byte fields are
/// little-endian on every shipping Mac (host byte order, and Apple ships no big-endian hardware).
///
/// We only ever open a *live* capture, which always emits the v1 header. The v2 header (opt-in via
/// libpcap `--apple-pktapv2`, which we never pass) is detected and its inner frame recovered so DNS
/// still parses, but its attribution is reported as unknown rather than risk a wrong identity from
/// its variable-offset string layout.
public struct PktapHeader: Sendable {
    /// Bytes to skip from the record start to reach the inner frame (`pth_length`). Authoritative;
    /// never derive this from `sizeof`, the header has grown across macOS releases.
    public let headerLength: Int
    /// Link type of the encapsulated inner frame (`pth_dlt`).
    public let innerDLT: UInt32
    /// Link-layer header bytes preceding the IP header inside the inner frame (`pth_frame_pre_length`).
    /// The kernel's own count, which sidesteps the DLT_RAW 12-vs-14 ambiguity.
    public let framePreLength: Int
    /// Capturing interface name, e.g. "en5" (`pth_ifname`).
    public let ifname: String
    /// True when the kernel marked this packet outbound (`pth_flags & PTH_FLAG_DIR_OUT`). Exposed for
    /// future use; the emitted record keeps a query/response-derived direction for cross-platform
    /// schema parity, so this is not wired into the record.
    public let isOutbound: Bool
    /// The originating process id: the effective (delegating) pid when present, else the real sender's
    /// pid. `nil` when the kernel could not attribute the packet (never fabricated).
    public let originatorPID: Int?
    /// The originating process short name (effective when present, else real). May be `nil`.
    public let originatorName: String?
    /// True for an actual packet record (`pth_type_next == PTH_TYPE_PACKET`); header-only and drop
    /// records are skipped by the caller.
    public let isPacketRecord: Bool

    // XNU bsd/net/pktap.h constants.
    private static let PTH_TYPE_PACKET: UInt32 = 1
    private static let PTH_FLAG_DIR_OUT: UInt32 = 0x0000_0002
    private static let PTH_FLAG_V2_HDR:  UInt32 = 0x0008_0000
    private static let COMM_MAX = 16     // MAXCOMLEN; the on-wire buffer is 17 (incl. NUL)
    private static let IFNAME_MAX = 24   // PKTAP_IFXNAMESIZE

    /// Parse one raw PKTAP record (`data` = one packet's captured bytes, i.e. what `pcap_next_ex`
    /// returns). Returns `nil` if the buffer is too short or the header is malformed, in which case the
    /// caller emits nothing — an unparseable record is never turned into a fabricated row.
    public static func parse(_ data: Data) -> PktapHeader? {
        let caplen = data.count
        guard caplen >= 40 else { return nil }          // must reach pth_flags @36
        let flags = le32(data, 36)
        let isV2 = (flags & PTH_FLAG_V2_HDR) != 0

        let hdrLen: Int
        let dlt: UInt32
        let pre: Int
        let recordType: UInt32
        var pid: Int? = nil
        var comm: String? = nil

        if isV2 {
            // Fixed block + variable string area, only ever produced with --apple-pktapv2 (unused).
            // Recover the inner frame via the uint8 length; leave attribution unknown.
            let hl = Int(data[data.startIndex])          // pth_length (uint8 in v2)
            guard hl >= 40, hl <= caplen else { return nil }
            hdrLen = hl
            dlt = UInt32(le16(data, 6))                  // pth_dlt (uint16 @6)
            pre = Int(le16(data, 8))                     // pth_frame_pre_length (uint16 @8)
            recordType = PTH_TYPE_PACKET
        } else {
            let hl = Int(le32(data, 0))                  // pth_length
            guard hl >= 56, hl <= caplen else { return nil }   // through pth_pid @52 at minimum
            hdrLen = hl
            recordType = le32(data, 4)                   // pth_type_next
            dlt = le32(data, 8)                          // pth_dlt
            pre = Int(le32(data, 44))                    // pth_frame_pre_length
            let realPID = Int32(bitPattern: le32(data, 52))
            let realComm = cstr(data, 56, COMM_MAX)
            if hl >= 105 {                               // header carries the effective-process fields
                let effPID = Int32(bitPattern: le32(data, 84))
                let effComm = cstr(data, 88, COMM_MAX)
                if effPID >= 0, let ec = effComm, !ec.isEmpty {
                    pid = Int(effPID); comm = ec         // the app behind a delegating sender
                } else if realPID >= 0 {
                    pid = Int(realPID); comm = realComm
                }
            } else if realPID >= 0 {
                pid = Int(realPID); comm = realComm
            }
        }

        return PktapHeader(
            headerLength: hdrLen,
            innerDLT: dlt,
            framePreLength: pre,
            ifname: cstr(data, 12, IFNAME_MAX) ?? "",
            isOutbound: (flags & PTH_FLAG_DIR_OUT) != 0,
            originatorPID: pid,
            originatorName: comm,
            isPacketRecord: recordType == PTH_TYPE_PACKET
        )
    }

    // MARK: - Little-endian readers (callers guarantee the offsets are in-bounds via the caplen guards)

    private static func le32(_ d: Data, _ off: Int) -> UInt32 {
        let i = d.startIndex + off
        return UInt32(d[i]) | (UInt32(d[i + 1]) << 8) | (UInt32(d[i + 2]) << 16) | (UInt32(d[i + 3]) << 24)
    }
    private static func le16(_ d: Data, _ off: Int) -> UInt16 {
        let i = d.startIndex + off
        return UInt16(d[i]) | (UInt16(d[i + 1]) << 8)
    }
    /// A NUL-terminated C string of at most `maxLen` bytes starting at `off`; `nil` if empty or the
    /// start is out of range. Never reads past the buffer.
    private static func cstr(_ d: Data, _ off: Int, _ maxLen: Int) -> String? {
        let start = d.startIndex + off
        guard start < d.endIndex else { return nil }
        let limit = min(d.endIndex, start + maxLen)
        var bytes: [UInt8] = []
        var i = start
        while i < limit {
            let b = d[i]
            if b == 0 { break }
            bytes.append(b)
            i += 1
        }
        return bytes.isEmpty ? nil : String(decoding: bytes, as: UTF8.self)
    }
}

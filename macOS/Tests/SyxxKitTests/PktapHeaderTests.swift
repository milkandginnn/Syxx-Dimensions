import XCTest
@testable import SyxxKit

/// Golden-vector tests for `PktapHeader.parse` — the byte math that turns a raw macOS PKTAP record
/// into process attribution + inner-frame location. These prove the offsets, the effective-vs-real
/// selection, the 16-char comm truncation, the length-based skip, and the honest handling of v2 and
/// malformed buffers. A green suite here is the floor, not the bar; the real bar is a live capture.
final class PktapHeaderTests: XCTestCase {

    // MARK: - v1 buffer builder

    /// Build one synthetic v1 `pktap_header` (default well-formed, 156-byte modern layout) followed by
    /// optional inner-frame bytes. Fields are written at their real XNU offsets, little-endian.
    private func makeV1(
        headerLen: Int = 156,
        recordType: UInt32 = 1,          // PTH_TYPE_PACKET
        dlt: UInt32 = 1,                 // DLT_EN10MB
        ifname: String = "en5",
        flags: UInt32 = 0x0000_0002,     // PTH_FLAG_DIR_OUT
        framePre: UInt32 = 0,
        realPID: Int32 = -1,
        realComm: String = "",
        effPID: Int32 = -1,
        effComm: String = "",
        inner: [UInt8] = []
    ) -> Data {
        var b = [UInt8](repeating: 0, count: headerLen)
        func put32(_ off: Int, _ v: UInt32) {
            b[off] = UInt8(v & 0xff); b[off + 1] = UInt8((v >> 8) & 0xff)
            b[off + 2] = UInt8((v >> 16) & 0xff); b[off + 3] = UInt8((v >> 24) & 0xff)
        }
        func putStr(_ off: Int, _ s: String, _ maxBytes: Int) {
            for (i, c) in Array(s.utf8.prefix(maxBytes)).enumerated() { b[off + i] = c }
        }
        put32(0, UInt32(headerLen))                  // pth_length
        put32(4, recordType)                         // pth_type_next
        put32(8, dlt)                                // pth_dlt
        putStr(12, ifname, 23)                       // pth_ifname (24B)
        put32(36, flags)                             // pth_flags
        put32(44, framePre)                          // pth_frame_pre_length
        put32(52, UInt32(bitPattern: realPID))       // pth_pid
        putStr(56, realComm, 16)                     // pth_comm (17B, 16 chars)
        put32(84, UInt32(bitPattern: effPID))        // pth_epid
        putStr(88, effComm, 16)                      // pth_ecomm (17B, 16 chars)
        return Data(b + inner)
    }

    // MARK: - attribution

    func testRealProcessOnly_noDelegation() {
        let h = PktapHeader.parse(makeV1(realPID: 1234, realComm: "firefox"))
        XCTAssertNotNil(h)
        XCTAssertEqual(h?.originatorPID, 1234)
        XCTAssertEqual(h?.originatorName, "firefox")
        XCTAssertEqual(h?.ifname, "en5")
        XCTAssertTrue(h?.isPacketRecord ?? false)
    }

    func testDelegated_prefersEffectiveOriginator() {
        // The real sender is mDNSResponder; the effective originator is the app that asked.
        let h = PktapHeader.parse(makeV1(realPID: 585, realComm: "mDNSResponder",
                                         effPID: 930, effComm: "firefox"))
        XCTAssertEqual(h?.originatorPID, 930, "effective pid must win over the delegating sender")
        XCTAssertEqual(h?.originatorName, "firefox")
    }

    func testUnattributable_pidMinusOne_isNil() {
        // Kernel could not attribute: both real and effective pid are -1. Never fabricate.
        let h = PktapHeader.parse(makeV1(realPID: -1, realComm: "", effPID: -1, effComm: ""))
        XCTAssertNotNil(h)
        XCTAssertNil(h?.originatorPID)
        XCTAssertNil(h?.originatorName)
    }

    func testEmptyEffectiveName_fallsBackToReal() {
        // Effective pid present but its comm is empty → not a usable delegation, use the real sender.
        let h = PktapHeader.parse(makeV1(realPID: 42, realComm: "configd", effPID: 99, effComm: ""))
        XCTAssertEqual(h?.originatorPID, 42)
        XCTAssertEqual(h?.originatorName, "configd")
    }

    func testCommTruncatedTo16Chars() {
        // Real-world case the user saw: at.obdev.littlesnitch... clamps to "at.obdev.littles".
        let h = PktapHeader.parse(makeV1(realPID: 2596, realComm: "at.obdev.littlesnitch"))
        XCTAssertEqual(h?.originatorName, "at.obdev.littles")
        XCTAssertEqual(h?.originatorName?.count, 16)
    }

    // MARK: - direction, record type, length-based skip

    func testDirectionFromFlags() {
        XCTAssertEqual(PktapHeader.parse(makeV1(flags: 0x2))?.isOutbound, true)   // DIR_OUT
        XCTAssertEqual(PktapHeader.parse(makeV1(flags: 0x1))?.isOutbound, false)  // DIR_IN
    }

    func testNonPacketRecordType_flaggedNotAPacket() {
        let h = PktapHeader.parse(makeV1(recordType: 0))   // PTH_TYPE_NONE
        XCTAssertNotNil(h)
        XCTAssertFalse(h?.isPacketRecord ?? true)
    }

    func testHeaderLengthDrivesInnerFrameSkip() {
        // pth_length must be reported verbatim so the caller skips to the inner frame by length, not
        // by a hardcoded sizeof. A grown header (larger pth_length) still parses the same fields.
        let grown = PktapHeader.parse(makeV1(headerLen: 176, realPID: 7, realComm: "safari",
                                             inner: [0x45, 0x00]))
        XCTAssertEqual(grown?.headerLength, 176)
        XCTAssertEqual(grown?.originatorPID, 7)
        XCTAssertEqual(grown?.originatorName, "safari")
    }

    func testInnerDLTAndFramePreLengthSurfaced() {
        let h = PktapHeader.parse(makeV1(dlt: 12, framePre: 0))   // DLT_RAW, no link header
        XCTAssertEqual(h?.innerDLT, 12)
        XCTAssertEqual(h?.framePreLength, 0)
    }

    // MARK: - malformed / v2

    func testTooShort_returnsNil() {
        XCTAssertNil(PktapHeader.parse(Data([UInt8](repeating: 0, count: 20))))
    }

    func testHeaderLengthExceedingCaplen_returnsNil() {
        // pth_length claims 156 but only 60 bytes were captured → reject rather than over-read.
        var short = makeV1(headerLen: 156)
        short = short.prefix(60)
        XCTAssertNil(PktapHeader.parse(short))
    }

    func testV2_recoversFrameButAttributesUnknown() {
        // v2 header: uint8 length at byte 0, PTH_FLAG_V2_HDR set at the flags offset (36). We only ever
        // hit this via --apple-pktapv2 (never passed); recover the frame, but report attribution unknown
        // rather than trust the variable-offset string layout.
        var b = [UInt8](repeating: 0, count: 64)
        b[0] = 44                                   // pth_length (uint8)
        // pth_dlt uint16 @6, pth_frame_pre_length uint16 @8
        b[6] = 1; b[7] = 0                          // DLT_EN10MB
        b[8] = 14; b[9] = 0                         // frame_pre_length = 14
        // pth_flags @36 with the v2 bit (0x00080000)
        b[36] = 0x00; b[37] = 0x00; b[38] = 0x08; b[39] = 0x00
        let h = PktapHeader.parse(Data(b))
        XCTAssertNotNil(h)
        XCTAssertEqual(h?.headerLength, 44)
        XCTAssertNil(h?.originatorPID, "v2 attribution must be unknown, not a guessed pid")
        XCTAssertEqual(h?.innerDLT, 1)
        XCTAssertEqual(h?.framePreLength, 14)
    }
}

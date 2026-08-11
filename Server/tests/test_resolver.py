"""Unit tests for syxx_server/resolver.py — the core DNS resolution module.

Covers: rcode_name, ip_to_arpa, _encode_name, _build_query, _read_name,
_parse_response, _format_rdata, _in_bailiwick, _parse_doh_json, serial
arithmetic, RRSIG window validation, glue extraction, _question_bytes,
_recv_exact, domain canonicalization, and resolve_authoritative (mocked).

No network calls — all socket I/O is mocked.
"""

import os
import socket
import struct
import time
from unittest.mock import MagicMock, patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

from syxx_server.resolver import (  # noqa: E402
    DNSError,
    MAX_RRSIG_VALIDITY_SPAN,
    RCODE_NAMES,
    TYPE_A,
    TYPE_AAAA,
    TYPE_CAA,
    TYPE_CNAME,
    TYPE_DNSKEY,
    TYPE_MX,
    TYPE_NS,
    TYPE_PTR,
    TYPE_RRSIG,
    TYPE_SOA,
    TYPE_SRV,
    TYPE_TXT,
    _build_query,
    _encode_name,
    _extract_glue,
    _format_rdata,
    _in_bailiwick,
    _MAX_TCP_LENGTH,
    _next_servers_from_glue,
    _parse_doh_json,
    _parse_response,
    _question_bytes,
    _read_name,
    _recv_exact,
    _rrsig_window_valid,
    _serial_lte,
    ip_to_arpa,
    rcode_name,
    resolve_authoritative,
    rrsig_window_status,
)


# ---------------------------------------------------------------------------
# Helpers to build DNS wire-format packets
# ---------------------------------------------------------------------------


def _make_dns_response(
    txid,
    rcode=0,
    aa=False,
    tc=False,
    answers=None,
    qname="example.com",
    qtype=TYPE_A,
    qd=1,
):
    """Build a minimal DNS response packet for testing parsers."""
    flags = 0x8000  # QR=1 (response)
    if aa:
        flags |= 0x0400
    if tc:
        flags |= 0x0200
    flags |= rcode & 0x000F

    an_count = len(answers) if answers else 0
    header = struct.pack(">HHHHHH", txid, flags, qd, an_count, 0, 0)

    # Question section
    question = _encode_name(qname) + struct.pack(">HH", qtype, 1)

    # Answer section
    answer_bytes = b""
    if answers:
        for rtype, rdata in answers:
            # Use a compression pointer to offset 12 (the question name)
            answer_bytes += b"\xc0\x0c"
            answer_bytes += struct.pack(">HHIH", rtype, 1, 300, len(rdata))
            answer_bytes += rdata

    return header + question + answer_bytes


def _ipv4_rdata(ip_str):
    """Convert dotted-quad to 4-byte rdata."""
    return socket.inet_aton(ip_str)


def _ipv6_rdata(ip_str):
    """Convert IPv6 string to 16-byte rdata."""
    return socket.inet_pton(socket.AF_INET6, ip_str)


# =========================================================================
# rcode_name
# =========================================================================


class TestRcodeName:
    def test_known_codes(self):
        assert rcode_name(0) == "NOERROR"
        assert rcode_name(2) == "SERVFAIL"
        assert rcode_name(3) == "NXDOMAIN"
        assert rcode_name(5) == "REFUSED"

    def test_unassigned_code(self):
        result = rcode_name(99)
        assert result == "RCODE99"

    def test_invalid_input_string(self):
        assert rcode_name("garbage") == "UNKNOWN"

    def test_none_input(self):
        assert rcode_name(None) == "UNKNOWN"

    def test_all_defined_codes_have_names(self):
        for code, name in RCODE_NAMES.items():
            assert rcode_name(code) == name


# =========================================================================
# ip_to_arpa
# =========================================================================


class TestIpToArpa:
    def test_ipv4(self):
        assert ip_to_arpa("1.2.3.4") == "4.3.2.1.in-addr.arpa"

    def test_ipv4_loopback(self):
        assert ip_to_arpa("127.0.0.1") == "1.0.0.127.in-addr.arpa"

    def test_ipv6(self):
        result = ip_to_arpa("2001:db8::1")
        assert result.endswith(".ip6.arpa")
        # Full expansion: 2001:0db8:0000:...:0001 → reversed nibbles
        assert "1.0.0.0" in result  # last nibble group

    def test_invalid_ip_raises(self):
        with pytest.raises(DNSError, match="invalid IP"):
            ip_to_arpa("not-an-ip")


# =========================================================================
# _encode_name
# =========================================================================


class TestEncodeName:
    def test_simple_domain(self):
        encoded = _encode_name("example.com")
        # \x07example\x03com\x00
        assert encoded == b"\x07example\x03com\x00"

    def test_trailing_dot_stripped(self):
        assert _encode_name("example.com.") == _encode_name("example.com")

    def test_root_zone(self):
        # "." → rstrip(".") → "" → split(".") → [""] → \x00 (empty label) + \x00 (terminator)
        assert _encode_name(".") == b"\x00\x00"

    def test_label_too_long(self):
        long_label = "a" * 64
        # The IDNA encoder itself raises for labels >= 64 bytes before our
        # explicit check; both are valid rejections.
        with pytest.raises((DNSError, UnicodeEncodeError)):
            _encode_name(f"{long_label}.com")


# =========================================================================
# _build_query
# =========================================================================


class TestBuildQuery:
    def test_returns_txid_and_packet(self):
        txid, packet, qname, qtype, qclass = _build_query("example.com", TYPE_A)
        assert isinstance(txid, int)
        assert 0 <= txid <= 0xFFFF
        assert isinstance(packet, bytes)
        assert len(packet) >= 12  # at minimum a header
        assert qname == "example.com"
        assert qtype == TYPE_A
        assert qclass == 1

    def test_rd_flag_set_by_default(self):
        _, packet, _, _, _ = _build_query("example.com", TYPE_A, rd=True)
        flags = struct.unpack(">H", packet[2:4])[0]
        assert flags & 0x0100  # RD bit

    def test_rd_flag_off_for_iterative(self):
        _, packet, _, _, _ = _build_query("example.com", TYPE_A, rd=False)
        flags = struct.unpack(">H", packet[2:4])[0]
        assert not (flags & 0x0100)

    def test_edns0_opt_present(self):
        """EDNS0 OPT record should be appended (arcount=1)."""
        _, packet, _, _, _ = _build_query("example.com", TYPE_A)
        arcount = struct.unpack(">H", packet[10:12])[0]
        assert arcount == 1

    def test_do_bit_in_edns(self):
        """DO bit in the OPT record should be set when do=True."""
        _, packet, _, _, _ = _build_query("example.com", TYPE_A, do=True)
        # OPT record is at the end: \x00 (root name) + TYPE(41) + CLASS(1232) + TTL(flags) + RDLEN(0)
        # Find the OPT record by scanning for type=41
        opt_type_offset = len(packet) - 10  # TYPE is 2 bytes after the root name byte
        opt_type = struct.unpack(">H", packet[opt_type_offset : opt_type_offset + 2])[0]
        assert opt_type == 41
        opt_flags = struct.unpack(">I", packet[opt_type_offset + 4 : opt_type_offset + 8])[0]
        assert opt_flags & 0x00008000  # DO bit


# =========================================================================
# _read_name
# =========================================================================


class TestReadName:
    def test_simple_uncompressed(self):
        data = b"\x07example\x03com\x00"
        name, offset = _read_name(data, 0)
        assert name == "example.com"
        assert offset == len(data)

    def test_compression_pointer(self):
        # Build data: name at offset 0 = "example.com", then a pointer at offset 12 back to 0
        name_bytes = b"\x07example\x03com\x00"
        data = name_bytes + b"\xc0\x00"  # pointer to offset 0
        name, offset = _read_name(data, len(name_bytes))
        assert name == "example.com"
        assert offset == len(name_bytes) + 2

    def test_truncated_name_raises(self):
        data = b"\x07exa"  # claims 7 bytes but only has 3
        with pytest.raises(DNSError, match="truncated"):
            _read_name(data, 0)

    def test_compression_loop_raises(self):
        # Two pointers that point to each other: offset 0 → offset 2, offset 2 → offset 0
        # But the monotonic rule requires pointer < current offset, so the second
        # pointer at offset 2 targeting offset 0 is fine, but the first at offset 0
        # targeting offset 2 violates monotonicity.
        data = b"\xc0\x02\xc0\x00"
        with pytest.raises(DNSError, match="non-monotonic"):
            _read_name(data, 0)

    def test_empty_data_raises(self):
        with pytest.raises(DNSError):
            _read_name(b"", 0)

    def test_name_exceeding_255_octets(self):
        # Build a name with many labels that exceeds 255 octets total
        labels = []
        total = 0
        while total < 260:
            label = b"\x03abc"  # 3 bytes + 1 length byte = 4 octets
            labels.append(label)
            total += 4
        data = b"".join(labels) + b"\x00"
        with pytest.raises(DNSError, match="255 octets"):
            _read_name(data, 0)


# =========================================================================
# _parse_response
# =========================================================================


class TestParseResponse:
    def test_valid_a_record(self):
        txid = 0x1234
        rdata = _ipv4_rdata("93.184.216.34")
        packet = _make_dns_response(txid, answers=[(TYPE_A, rdata)])
        result = _parse_response(packet, txid, TYPE_A, expected_qname="example.com")
        assert result["rcode"] == 0
        assert len(result["records"]) == 1
        assert result["records"][0]["type"] == "A"
        assert result["records"][0]["value"] == "93.184.216.34"

    def test_txid_mismatch_raises(self):
        packet = _make_dns_response(0x1234)
        with pytest.raises(DNSError, match="txid mismatch"):
            _parse_response(packet, 0x5678, TYPE_A)

    def test_short_header_raises(self):
        with pytest.raises(DNSError, match="short header"):
            _parse_response(b"\x00" * 5, 0, TYPE_A)

    def test_not_a_response_raises(self):
        # Build a packet with QR=0 (query, not response)
        header = struct.pack(">HHHHHH", 0x1234, 0x0000, 1, 0, 0, 0)
        question = _encode_name("example.com") + struct.pack(">HH", TYPE_A, 1)
        with pytest.raises(DNSError, match="not a response"):
            _parse_response(header + question, 0x1234, TYPE_A)

    def test_question_count_zero_raises(self):
        """QDCOUNT=0 must be rejected (A061)."""
        header = struct.pack(">HHHHHH", 0x1234, 0x8000, 0, 0, 0, 0)
        with pytest.raises(DNSError, match="unexpected question count"):
            _parse_response(header, 0x1234, TYPE_A)

    def test_question_mismatch_raises(self):
        packet = _make_dns_response(0x1234, qname="evil.com")
        with pytest.raises(DNSError, match="question mismatch"):
            _parse_response(packet, 0x1234, TYPE_A, expected_qname="example.com")

    def test_nxdomain_rcode(self):
        packet = _make_dns_response(0x1234, rcode=3)
        result = _parse_response(packet, 0x1234, TYPE_A)
        assert result["rcode"] == 3

    def test_truncated_flag(self):
        packet = _make_dns_response(0x1234, tc=True)
        result = _parse_response(packet, 0x1234, TYPE_A)
        assert result["truncated"] is True

    def test_ad_flag(self):
        txid = 0x1234
        flags = 0x8000 | 0x0020  # QR + AD
        header = struct.pack(">HHHHHH", txid, flags, 1, 0, 0, 0)
        question = _encode_name("example.com") + struct.pack(">HH", TYPE_A, 1)
        result = _parse_response(header + question, txid, TYPE_A)
        assert result["ad"] is True

    def test_malformed_a_record_dropped(self):
        """A record with wrong rdlength (not 4 bytes) should be dropped (A115)."""
        txid = 0x1234
        # 3 bytes instead of 4 for an A record
        packet = _make_dns_response(txid, answers=[(TYPE_A, b"\x01\x02\x03")])
        result = _parse_response(packet, txid, TYPE_A)
        assert len(result["records"]) == 0


# =========================================================================
# _format_rdata
# =========================================================================


class TestFormatRdata:
    def test_a_record(self):
        rdata = _ipv4_rdata("1.2.3.4")
        assert _format_rdata(TYPE_A, rdata, b"", 0) == "1.2.3.4"

    def test_a_record_bad_length_returns_none(self):
        assert _format_rdata(TYPE_A, b"\x01\x02", b"", 0) is None

    def test_aaaa_record(self):
        rdata = _ipv6_rdata("::1")
        result = _format_rdata(TYPE_AAAA, rdata, b"", 0)
        assert result == "::1"

    def test_aaaa_record_bad_length_returns_none(self):
        assert _format_rdata(TYPE_AAAA, b"\x01" * 15, b"", 0) is None

    def test_txt_record(self):
        # TXT rdata: one or more length-prefixed strings
        txt = b"\x0bhello world"
        result = _format_rdata(TYPE_TXT, txt, b"", 0)
        assert result == "hello world"

    def test_txt_multiple_strings(self):
        txt = b"\x05hello\x05world"
        result = _format_rdata(TYPE_TXT, txt, b"", 0)
        assert result == "hello world"

    def test_caa_record(self):
        # CAA: flags(1) + tag_len(1) + tag + value
        tag = b"issue"
        value = b"letsencrypt.org"
        rdata = bytes([0, len(tag)]) + tag + value
        result = _format_rdata(TYPE_CAA, rdata, b"", 0)
        assert '0 issue "letsencrypt.org"' == result

    def test_unknown_type_returns_hex(self):
        rdata = b"\xde\xad\xbe\xef"
        result = _format_rdata(9999, rdata, b"", 0)
        assert result == "deadbeef"


# =========================================================================
# _in_bailiwick
# =========================================================================


class TestInBailiwick:
    def test_exact_match(self):
        assert _in_bailiwick("example.com", "example.com") is True

    def test_subdomain(self):
        assert _in_bailiwick("ns1.example.com", "example.com") is True

    def test_out_of_bailiwick(self):
        assert _in_bailiwick("evil.com", "example.com") is False

    def test_sibling_domain(self):
        assert _in_bailiwick("notexample.com", "example.com") is False

    def test_root_zone_contains_everything(self):
        assert _in_bailiwick("anything.com", "") is True
        assert _in_bailiwick("anything.com", ".") is True

    def test_case_insensitive(self):
        assert _in_bailiwick("NS1.Example.COM", "example.com") is True

    def test_trailing_dots_ignored(self):
        assert _in_bailiwick("ns1.example.com.", "example.com.") is True


# =========================================================================
# _parse_doh_json
# =========================================================================


class TestParseDohJson:
    def test_valid_noerror(self):
        data = {"Status": 0, "Answer": [{"type": 1, "data": "1.2.3.4"}]}
        result = _parse_doh_json(data)
        assert result["rcode"] == 0
        assert len(result["records"]) == 1
        assert result["records"][0]["value"] == "1.2.3.4"

    def test_valid_nxdomain(self):
        data = {"Status": 3}
        result = _parse_doh_json(data)
        assert result["rcode"] == 3
        assert result["records"] == []

    def test_non_dict_raises(self):
        with pytest.raises(DNSError, match="unexpected response shape"):
            _parse_doh_json([1, 2, 3])

    def test_missing_status_raises(self):
        with pytest.raises(DNSError, match="missing or invalid Status"):
            _parse_doh_json({})

    def test_bool_status_raises(self):
        with pytest.raises(DNSError, match="missing or invalid Status"):
            _parse_doh_json({"Status": True})

    def test_answer_not_list_raises(self):
        with pytest.raises(DNSError, match="Answer is not a list"):
            _parse_doh_json({"Status": 0, "Answer": "bad"})

    def test_answer_member_not_object_raises(self):
        with pytest.raises(DNSError, match="answer member is not an object"):
            _parse_doh_json({"Status": 0, "Answer": ["bad"]})

    def test_answer_type_invalid_raises(self):
        with pytest.raises(DNSError, match="answer type invalid"):
            _parse_doh_json({"Status": 0, "Answer": [{"type": True, "data": "1.2.3.4"}]})

    def test_answer_data_invalid_raises(self):
        with pytest.raises(DNSError, match="answer data invalid"):
            _parse_doh_json({"Status": 0, "Answer": [{"type": 1, "data": 123}]})

    def test_ad_flag_preserved(self):
        data = {"Status": 0, "AD": True, "Answer": []}
        result = _parse_doh_json(data)
        assert result["ad"] is True


# =========================================================================
# Serial arithmetic (_serial_lte, _rrsig_window_valid)
# =========================================================================


class TestSerialArithmetic:
    def test_serial_lte_normal(self):
        assert _serial_lte(100, 200) is True
        assert _serial_lte(200, 100) is False

    def test_serial_lte_equal(self):
        assert _serial_lte(100, 100) is True

    def test_serial_lte_wrap_around(self):
        # Near 2**32 boundary: 0xFFFFFFF0 is "before" 0x00000010
        assert _serial_lte(0xFFFFFFF0, 0x00000010) is True

    def test_rrsig_window_valid_normal(self):
        now = 1_700_000_000
        inception = now - 3600
        expiration = now + 3600
        assert _rrsig_window_valid(inception, expiration, now) is True

    def test_rrsig_window_expired(self):
        now = 1_700_000_000
        inception = now - 7200
        expiration = now - 3600
        assert _rrsig_window_valid(inception, expiration, now) is False

    def test_rrsig_window_not_yet_valid(self):
        now = 1_700_000_000
        inception = now + 3600
        expiration = now + 7200
        assert _rrsig_window_valid(inception, expiration, now) is False

    def test_rrsig_window_implausible_span_rejected(self):
        """B047: a span exceeding MAX_RRSIG_VALIDITY_SPAN is rejected."""
        now = 1_700_000_000
        inception = now - 100
        expiration = inception + MAX_RRSIG_VALIDITY_SPAN + 1
        assert _rrsig_window_valid(inception, expiration, now) is False

    def test_rrsig_window_expiration_before_inception(self):
        assert _rrsig_window_valid(200, 100, 150) is False


# =========================================================================
# rrsig_window_status
# =========================================================================


class TestRrsigWindowStatus:
    def _make_rrsig_hex(self, type_covered, inception, expiration):
        """Build minimal RRSIG rdata hex with the fields we need."""
        rdata = struct.pack(">HBBIIIII",
                            type_covered,  # type covered
                            8,             # algorithm (RSASHA256)
                            2,             # labels
                            3600,          # original TTL
                            expiration,
                            inception,
                            0,             # key tag
                            0)             # signer (will be truncated, that's ok)
        # Pad to at least 18 bytes
        rdata = rdata.ljust(18, b"\x00")
        return rdata.hex()

    def test_valid(self):
        now = 1_700_000_000
        hex_data = self._make_rrsig_hex(TYPE_DNSKEY, now - 3600, now + 3600)
        assert rrsig_window_status(hex_data, now) == "valid"

    def test_expired(self):
        now = 1_700_000_000
        hex_data = self._make_rrsig_hex(TYPE_DNSKEY, now - 7200, now - 3600)
        assert rrsig_window_status(hex_data, now) == "expired"

    def test_not_yet_valid(self):
        now = 1_700_000_000
        hex_data = self._make_rrsig_hex(TYPE_DNSKEY, now + 3600, now + 7200)
        assert rrsig_window_status(hex_data, now) == "not-yet-valid"

    def test_wrong_type(self):
        now = 1_700_000_000
        hex_data = self._make_rrsig_hex(TYPE_A, now - 3600, now + 3600)
        assert rrsig_window_status(hex_data, now) == "wrong-type"

    def test_unknown_on_short_data(self):
        assert rrsig_window_status("dead", 1_700_000_000) == "unknown"

    def test_unknown_on_invalid_hex(self):
        assert rrsig_window_status("not-hex!", 1_700_000_000) == "unknown"


# =========================================================================
# _extract_glue and _next_servers_from_glue
# =========================================================================


class TestGlueExtraction:
    def test_in_bailiwick_glue_included(self):
        additional = [
            {"owner": "ns1.example.com.", "type": "A", "value": "93.184.216.1"},
            {"owner": "ns2.example.com.", "type": "A", "value": "93.184.216.2"},
        ]
        glue = _extract_glue(additional, "example.com")
        assert glue == {
            "ns1.example.com": "93.184.216.1",
            "ns2.example.com": "93.184.216.2",
        }

    def test_out_of_bailiwick_glue_excluded(self):
        """A063: out-of-bailiwick glue must be discarded."""
        additional = [
            {"owner": "ns1.evil.com.", "type": "A", "value": "10.0.0.1"},
        ]
        glue = _extract_glue(additional, "example.com")
        assert glue == {}

    def test_non_a_records_ignored(self):
        additional = [
            {"owner": "ns1.example.com.", "type": "AAAA", "value": "::1"},
        ]
        glue = _extract_glue(additional, "example.com")
        assert glue == {}

    @patch("syxx_server.resolver.netutil.ip_is_public", return_value=True)
    def test_next_servers_from_glue(self, _mock):
        ns_names = ["ns1.example.com.", "ns2.example.com."]
        glue = {"ns1.example.com": "1.2.3.4", "ns2.example.com": "5.6.7.8"}
        result = _next_servers_from_glue(ns_names, glue)
        assert len(result) == 2
        assert result[0]["ip"] == "1.2.3.4"

    @patch("syxx_server.resolver.netutil.ip_is_public", return_value=False)
    def test_next_servers_filters_non_public(self, _mock):
        """A011: non-public IPs in glue must be filtered."""
        ns_names = ["ns1.example.com."]
        glue = {"ns1.example.com": "10.0.0.1"}
        result = _next_servers_from_glue(ns_names, glue)
        assert result == []


# =========================================================================
# _question_bytes
# =========================================================================


class TestQuestionBytes:
    def test_extracts_txid_and_question(self):
        txid, packet, _, _, _ = _build_query("example.com", TYPE_A)
        txid_bytes, question = _question_bytes(packet)
        assert txid_bytes == struct.pack(">H", txid)
        # Question should contain the encoded name + qtype + qclass
        assert len(question) > 4

    def test_short_packet_raises(self):
        with pytest.raises(DNSError, match="too short"):
            _question_bytes(b"\x00" * 5)


# =========================================================================
# _recv_exact
# =========================================================================


class TestRecvExact:
    def test_connection_closed_raises(self):
        mock_sock = MagicMock()
        mock_sock.recv.return_value = b""
        with pytest.raises(DNSError, match="connection closed"):
            _recv_exact(mock_sock, 10)

    def test_reads_exact_bytes(self):
        mock_sock = MagicMock()
        mock_sock.recv.side_effect = [b"hello", b" world"]
        result = _recv_exact(mock_sock, 11)
        assert result == b"hello world"


# =========================================================================
# resolve_authoritative (mocked — no network calls)
# =========================================================================


class TestResolveAuthoritative:
    """Test the delegation walker with mocked _query_sections."""

    def test_max_depth_exceeded_returns_inconclusive(self):
        """Walker must stop at max_depth and return inconclusive."""
        # Build a referral response that never terminates
        def mock_query_sections(name, qtype, server, timeout, do=False, rd=False, deadline=None):
            return {
                "rcode": 0,
                "aa": False,
                "truncated": False,
                "answers": [],
                "authority": [
                    {"owner": "com.", "type": "NS", "rtype": TYPE_NS, "ttl": 300, "value": "ns1.com"},
                ],
                "additional": [
                    {"owner": "ns1.com.", "type": "A", "rtype": TYPE_A, "ttl": 300, "value": "1.2.3.4"},
                ],
                "parseComplete": True,
            }

        with patch("syxx_server.resolver._query_sections", side_effect=mock_query_sections):
            with patch("syxx_server.resolver.netutil.ip_is_public", return_value=True):
                result = resolve_authoritative("example.com", TYPE_A, max_depth=2)
        assert result["conclusion"] == "inconclusive"
        assert any("max delegation depth" in str(d.get("error", "")) for d in result["delegation"])

    def test_no_reachable_server_returns_inconclusive(self):
        """All servers at a level fail → inconclusive."""
        with patch("syxx_server.resolver._query_sections", side_effect=DNSError("timeout")):
            result = resolve_authoritative("example.com", TYPE_A)
        assert result["conclusion"] == "inconclusive"
        assert any("no reachable" in str(d.get("error", "")) for d in result["delegation"])

    def test_authoritative_answer_returned(self):
        """A direct authoritative answer is returned correctly."""
        def mock_query_sections(name, qtype, server, timeout, do=False, rd=False, deadline=None):
            return {
                "rcode": 0,
                "aa": True,
                "truncated": False,
                "answers": [
                    {"owner": "example.com.", "type": "A", "rtype": TYPE_A, "ttl": 300, "value": "93.184.216.34"},
                ],
                "authority": [],
                "additional": [],
                "parseComplete": True,
            }

        with patch("syxx_server.resolver._query_sections", side_effect=mock_query_sections):
            with patch("syxx_server.resolver._zone_is_signed", return_value=False):
                result = resolve_authoritative("example.com", TYPE_A)
        assert result["conclusion"] == "authoritative"
        assert result["authoritative"]["answers"] == ["93.184.216.34"]

    def test_nxdomain_returned_as_authoritative(self):
        """NXDOMAIN from an authoritative server is a valid ground-truth result."""
        def mock_query_sections(name, qtype, server, timeout, do=False, rd=False, deadline=None):
            return {
                "rcode": 3,
                "aa": True,
                "truncated": False,
                "answers": [],
                "authority": [
                    {"owner": "com.", "type": "SOA", "rtype": TYPE_SOA, "ttl": 300, "value": "ns1.com admin.com 1 2 3 4 5"},
                ],
                "additional": [],
                "parseComplete": True,
            }

        with patch("syxx_server.resolver._query_sections", side_effect=mock_query_sections):
            with patch("syxx_server.resolver._zone_is_signed", return_value=False):
                result = resolve_authoritative("nonexistent.com", TYPE_A)
        assert result["conclusion"] == "authoritative"
        assert result["authoritative"]["rcode"] == 3

    def test_deadline_exceeded_returns_inconclusive(self):
        """An expired deadline must abort the walk immediately."""
        past_deadline = time.monotonic() - 100
        result = resolve_authoritative("example.com", TYPE_A, deadline=past_deadline)
        assert result["conclusion"] == "inconclusive"
        assert any("deadline" in str(d.get("error", "")) for d in result["delegation"])

    def test_incomplete_parse_returns_inconclusive(self):
        """A168: incomplete parse → inconclusive, not authoritative."""
        def mock_query_sections(name, qtype, server, timeout, do=False, rd=False, deadline=None):
            return {
                "rcode": 0,
                "aa": True,
                "truncated": False,
                "answers": [
                    {"owner": "example.com.", "type": "A", "rtype": TYPE_A, "ttl": 300, "value": "1.2.3.4"},
                ],
                "authority": [],
                "additional": [],
                "parseComplete": False,  # incomplete parse
            }

        with patch("syxx_server.resolver._query_sections", side_effect=mock_query_sections):
            result = resolve_authoritative("example.com", TYPE_A)
        assert result["conclusion"] == "inconclusive"
        assert any("incomplete parse" in str(d.get("error", "")) for d in result["delegation"])

    def test_terminal_without_aa_returns_inconclusive(self):
        """A terminal response without AA bit must not be treated as authoritative."""
        def mock_query_sections(name, qtype, server, timeout, do=False, rd=False, deadline=None):
            return {
                "rcode": 0,
                "aa": False,  # no authoritative flag
                "truncated": False,
                "answers": [
                    {"owner": "example.com.", "type": "A", "rtype": TYPE_A, "ttl": 300, "value": "1.2.3.4"},
                ],
                "authority": [],
                "additional": [],
                "parseComplete": True,
            }

        with patch("syxx_server.resolver._query_sections", side_effect=mock_query_sections):
            result = resolve_authoritative("example.com", TYPE_A)
        assert result["conclusion"] == "inconclusive"

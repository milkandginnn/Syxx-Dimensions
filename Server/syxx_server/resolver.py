"""Self-contained DNS client used by the investigation API.

This is an independent implementation (it does NOT import Syxx client code). It
uses random transaction IDs, a hard timeout, TCP fallback on truncation, and
bounds-checked name parsing so hostile responses cannot hang or crash it.
"""
import http.client
import ipaddress
import json
import secrets
import socket
import ssl
import struct
import time
import urllib.parse

from . import netutil

TYPE_A = 1
TYPE_NS = 2
TYPE_CNAME = 5
TYPE_SOA = 6
TYPE_PTR = 12
TYPE_MX = 15
TYPE_TXT = 16
TYPE_AAAA = 28
TYPE_SRV = 33
TYPE_RRSIG = 46
TYPE_DNSKEY = 48
TYPE_CAA = 257

# RCODE labels (RFC 1035 4.1.1, extended by RFC 2136 / RFC 2845 / RFC 6891).
#
# A155: every parser in this module returns `rcode` as an INTEGER — _parse_response
# (flags & 0x000F), _parse_doh_json (the numeric JSON `Status`), query_system
# (hardcoded 0) and _query_sections all do. Consumers that compared it against the
# STRING "NOERROR" were comparing an int to a str: permanently False, and the
# fall-through then called .lower() on the int. Compare the NUMBER, render the NAME.
RCODE_NAMES = {
    0: "NOERROR", 1: "FORMERR", 2: "SERVFAIL", 3: "NXDOMAIN", 4: "NOTIMP",
    5: "REFUSED", 6: "YXDOMAIN", 7: "YXRRSET", 8: "NXRRSET", 9: "NOTAUTH",
    10: "NOTZONE", 11: "DSOTYPENI", 16: "BADVERS", 23: "BADCOOKIE",
}


def rcode_name(rcode):
    """Label for a numeric RCODE. An unassigned code renders as RCODE<n> and an
    unparseable one as UNKNOWN — never as a guess, and never as NOERROR."""
    try:
        n = int(rcode)
    except (TypeError, ValueError):
        return "UNKNOWN"
    return RCODE_NAMES.get(n, "RCODE%d" % n)


# ---------- IP-to-arpa conversion (RFC 1035 for IPv4, RFC 3596 for IPv6) ----------

def ip_to_arpa(ip_str):
    """Convert an IP address string to its reverse-DNS (in-addr.arpa / ip6.arpa) name.
    Raises DNSError on invalid input."""
    try:
        addr = ipaddress.ip_address(ip_str)
    except ValueError:
        raise DNSError("invalid IP address: %s" % ip_str)
    if isinstance(addr, ipaddress.IPv4Address):
        octets = ip_str.split(".")
        return ".".join(reversed(octets)) + ".in-addr.arpa"
    # IPv6: expand to full nibble form, reverse, dot-separate
    full_hex = addr.exploded.replace(":", "")
    return ".".join(reversed(full_hex)) + ".ip6.arpa"


def _addr_family(server):
    """Return the address family for *server* — AF_INET6 for IPv6 addresses, AF_INET otherwise."""
    try:
        addr = ipaddress.ip_address(server)
        return socket.AF_INET6 if addr.version == 6 else socket.AF_INET
    except ValueError:
        return socket.AF_INET

# Maximum plausible RRSIG validity span, in seconds (2 years). Real signers use windows of days to a
# few months; a span approaching the 2**31 serial half-space is implausible and is what a spoofed
# clock would need to wrap a long-expired signature back into "valid" under serial arithmetic (B047).
MAX_RRSIG_VALIDITY_SPAN = 63_072_000

# SD-72: maximum server-declared TCP response length. An attacker-controlled resolver
# can return a 2**16-1 length in the 2-byte TCP prefix, which would make _recv_exact
# try to allocate and read that many bytes. 16384 is well above the largest plausible
# DNS response (65535 bytes with EDNS) and well below an OOM / loop risk.
_MAX_TCP_LENGTH = 16_384

_TYPE_NAMES = {1: "A", 2: "NS", 5: "CNAME", 6: "SOA", 12: "PTR", 15: "MX", 16: "TXT",
               28: "AAAA", 33: "SRV", 46: "RRSIG", 48: "DNSKEY", 257: "CAA"}


class DNSError(Exception):
    pass


def _ip_is_public(ip):
    """True only for publicly-routable unicast IPs. Gates which glue/NS IPs the authoritative
    walker will query, so an attacker-controlled zone can't point us at internal hosts (A011).
    Delegates to the shared netutil gate, which also normalizes IPv4-mapped IPv6 before
    classification (SD-07: the local copy previously read ::ffff:10.0.0.1 as public)."""
    return netutil.ip_is_public(ip)


def _in_bailiwick(name, zone):
    """True if `name` is at or below `zone` (equal to it, or a subdomain). Case-insensitive,
    trailing dots ignored. The root zone (".") contains everything.

    A063: a rogue authoritative can inject a referral or glue record whose owner is NOT within
    the zone it is delegating (an out-of-bailiwick redirect) to steer the walk to attacker
    infrastructure and forge the ground-truth answer. This gate lets the walker discard those."""
    name = (name or "").rstrip(".").lower()
    zone = (zone or "").rstrip(".").lower()
    if zone == "":  # root delegates the whole namespace
        return True
    return name == zone or name.endswith("." + zone)


def _encode_name(name):
    out = b""
    for label in name.rstrip(".").split("."):
        b = label.encode("idna") if label else b""
        if len(b) > 63:
            raise DNSError("label too long")
        out += bytes([len(b)]) + b
    return out + b"\x00"


def _build_query(name, qtype, rd=True, do=False, qclass=1):
    txid = secrets.randbits(16)
    flags = 0x0100 if rd else 0x0000  # RD on for recursive queries; off for iterative (walker)
    # A073: always include an EDNS0 OPT record to advertise a larger UDP payload
    # (1232 bytes — the minimum IPv6 MTU, safe for all networks). Without EDNS0
    # the legacy 512-byte UDP limit causes unnecessary truncation and TCP fallback
    # for responses that carry DNSKEY/RRSIG records or many answers. The DO bit
    # is set only when explicitly requested.
    arcount = 1
    header = struct.pack(">HHHHHH", txid, flags, 1, 0, 0, arcount)
    question = _encode_name(name) + struct.pack(">HH", qtype, qclass)
    packet = header + question
    # EDNS0 OPT pseudo-record: NAME=root(0x00), TYPE=OPT(41), CLASS=UDP payload
    # size(1232), TTL=extended-rcode(0)|version(0)|DO-bit|Z(0), RDLEN=0.
    opt_flags = 0x00008000 if do else 0x00000000
    packet += b"\x00" + struct.pack(">HHIH", 41, 1232, opt_flags, 0)
    return txid, packet, name, qtype, qclass  # caller forwards expected_qname/qtype/qclass for response validation


def _read_name(data, offset):
    """Return (labels_str, next_offset) with compression + loop guards.

    A160: enforce RFC 1035 limits — total decoded name ≤ 255 octets and ≤ 128
    labels. Require every compression pointer to target a strictly lower offset
    than the pointer itself (monotonic rule), making pointer chains linear."""
    labels = []
    jumped = False
    next_offset = offset
    seen = 0
    total_len = 0  # A160: track total decoded name length (octets including dots)
    while True:
        if offset >= len(data):
            raise DNSError("truncated name")
        length = data[offset]
        if length & 0xC0 == 0xC0:
            if offset + 1 >= len(data):
                raise DNSError("truncated pointer")
            pointer = ((length & 0x3F) << 8) | data[offset + 1]
            # A160: monotonic pointer rule — pointer must target strictly lower
            # offset. This makes chains linear and removes the dependence on the
            # 128-iteration loop counter alone.
            if pointer >= offset:
                raise DNSError("non-monotonic compression pointer")
            if not jumped:
                next_offset = offset + 2
            offset = pointer
            jumped = True
            seen += 1
            if seen > 128:
                raise DNSError("compression loop")
            continue
        if length == 0:
            offset += 1
            break
        offset += 1
        if offset + length > len(data):
            raise DNSError("truncated label")
        # A160: enforce RFC 1035 §2.3.4 — name ≤ 255 octets, ≤ 128 labels
        total_len += length + 1  # label bytes + length byte
        if total_len > 255:
            raise DNSError("name exceeds 255 octets")
        if len(labels) >= 127:
            raise DNSError("name exceeds 128 labels")
        labels.append(data[offset:offset + length].decode("utf-8", "ignore"))
        offset += length
    if not jumped:
        next_offset = offset
    return ".".join(labels), next_offset


def _parse_response(data, expected_txid, qtype, expected_qname=None, expected_qclass=1):
    """Parse a DNS response. If expected_qname is given, validates the question section
    matches (case-insensitive qname, qtype, qclass) — A020: wrong-question injection guard."""
    if len(data) < 12:
        raise DNSError("short header")
    txid, flags, qd, an, ns, ar = struct.unpack(">HHHHHH", data[:12])
    if txid != expected_txid:
        raise DNSError("txid mismatch")
    if not (flags & 0x8000):
        raise DNSError("not a response")
    rcode = flags & 0x000F
    truncated = bool(flags & 0x0200)
    ad = bool(flags & 0x0020)
    # A061: a real response to a single-question query always echoes exactly one
    # question. QDCOUNT==0 lets a spoof skip question validation entirely; QDCOUNT>1
    # is malformed for our single-question queries. Reject both outright.
    if qd != 1:
        raise DNSError("unexpected question count: %d" % qd)
    offset = 12
    for _ in range(qd):
        qname, offset = _read_name(data, offset)
        if offset + 4 > len(data):
            # A078/A098: a truncated question section must surface as an honest
            # DNSError (-> inconclusive), never an uncaught struct.error that the
            # request handler turns into a 500 masking the real outcome.
            raise DNSError("truncated question section")
        qtype_parsed, qclass_parsed = struct.unpack(">HH", data[offset:offset + 4])
        offset += 4
        if expected_qname is not None:
            # A020: validate the response answers the question we asked.
            # RFC 4343: qname comparison is case-insensitive.
            if qname.lower() != expected_qname.lower() or qtype_parsed != qtype or qclass_parsed != expected_qclass:
                raise DNSError("question mismatch: asked %s/%d/%d, got %s/%d/%d" %
                               (expected_qname, qtype, expected_qclass, qname, qtype_parsed, qclass_parsed))
        break  # only validate the first question
    records = []
    for _ in range(an):
        _, offset = _read_name(data, offset)
        if offset + 10 > len(data):
            # A077: truncated RR header — the packet is malformed; raise rather
            # than silently returning a partial record set that would be treated
            # as the complete answer.
            raise DNSError("truncated RR header at offset %d" % offset)
        rtype, rclass, ttl, rdlength = struct.unpack(">HHIH", data[offset:offset + 10])
        offset += 10
        rdata_offset = offset
        # A077: verify the full RDATA fits within the packet before slicing.
        # Without this check a truncated rdlength produces a silently short
        # rdata slice and the parser returns a partial answer set as complete.
        if offset + rdlength > len(data):
            raise DNSError("truncated rdlength at offset %d (need %d bytes, have %d)"
                           % (offset, rdlength, len(data) - offset))
        rdata = data[offset:offset + rdlength]
        offset += rdlength
        value = _format_rdata(rtype, rdata, data, rdata_offset)
        # A115: drop records whose rdata failed to format (e.g. short-rdlength A record
        # that would otherwise surface as hex garbage labelled "A"). The record is
        # malformed; silently dropping it is honest — never emit an unparsed value under
        # a parsed type name.
        if value is None:
            continue
        records.append({"type": _TYPE_NAMES.get(rtype, str(rtype)), "value": value})
    return {"rcode": rcode, "truncated": truncated, "ad": ad, "records": records}


def _format_rdata(rtype, rdata, full, rdata_offset):
    """Format raw rdata into a human-readable string. Returns a plain
    formatted string for all record types (including MX, SOA, SRV, CAA).
    Returns None for a length-invalid A/AAAA record (A115: never emit hex
    garbage under a parsed type name). Falls back to hex for any other
    unhandled type."""
    # --- A (IPv4) ---
    if rtype == TYPE_A:
        if len(rdata) == 4:
            return socket.inet_ntop(socket.AF_INET, rdata)
        return None  # A115: short/long rdlength -> malformed, not hex garbage
    # --- AAAA (IPv6) ---
    if rtype == TYPE_AAAA:
        if len(rdata) == 16:
            return socket.inet_ntop(socket.AF_INET6, rdata)  # canonical (:: compressed)
        return None  # A115: short/long rdlength -> malformed, not hex garbage
    # --- NS / CNAME / PTR (single compressed name) ---
    if rtype in (TYPE_NS, TYPE_CNAME, TYPE_PTR):
        try:
            name, _ = _read_name(full, rdata_offset)
            return name
        except Exception:
            # A123: a failed name decode is a malformed record, not hex evidence.
            # Returning None causes the caller to drop the record (A115 pattern)
            # rather than emitting hex garbage labelled as NS/CNAME that would
            # produce a false "divergent" result or corrupt passive DNS.
            return None
    # --- MX (preference + exchange name) ---
    if rtype == TYPE_MX and len(rdata) >= 4:
        try:
            preference = struct.unpack(">H", rdata[:2])[0]
            exchange, _ = _read_name(full, rdata_offset + 2)
            return "%d %s" % (preference, exchange)
        except Exception:
            return rdata.hex()
    # --- TXT (one or more character-strings: length-prefixed) ---
    if rtype == TYPE_TXT and len(rdata) >= 1:
        try:
            strings = []
            off = 0
            while off < len(rdata):
                slen = rdata[off]
                off += 1
                if off + slen > len(rdata):
                    break
                strings.append(rdata[off:off + slen].decode("utf-8", "replace"))
                off += slen
            return " ".join(strings) if strings else rdata.hex()
        except Exception:
            return rdata.hex()
    # --- SOA (mname, rname, serial, refresh, retry, expire, minimum) ---
    if rtype == TYPE_SOA and len(rdata) >= 22:
        try:
            mname, off = _read_name(full, rdata_offset)
            rname, off = _read_name(full, off)
            serial, refresh, retry, expire, minimum = struct.unpack(
                ">IIIII", full[off:off + 20])
            return "%s %s %d %d %d %d %d" % (mname, rname, serial, refresh, retry, expire, minimum)
        except Exception:
            return rdata.hex()
    # --- SRV (priority, weight, port, target name) ---
    if rtype == TYPE_SRV and len(rdata) >= 8:
        try:
            priority, weight, port = struct.unpack(">HHH", rdata[:6])
            target, _ = _read_name(full, rdata_offset + 6)
            return "%d %d %d %s" % (priority, weight, port, target)
        except Exception:
            return rdata.hex()
    # --- CAA (flags byte, tag length, tag string, value bytes) ---
    if rtype == TYPE_CAA and len(rdata) >= 3:
        try:
            flags = rdata[0]
            tag_len = rdata[1]
            if 2 + tag_len > len(rdata):
                return rdata.hex()
            tag = rdata[2:2 + tag_len].decode("ascii", "replace")
            value = rdata[2 + tag_len:].decode("utf-8", "replace")
            return '%d %s "%s"' % (flags, tag, value)
        except Exception:
            return rdata.hex()
    return rdata.hex()


def query(name, qtype, server, timeout=3):
    """Query a single resolver. Returns a parsed dict or raises DNSError.
    Uses connected UDP so the kernel discards off-source spoofed replies (A020)."""
    txid, packet, qname, qtype_, qclass_ = _build_query(name, qtype)
    sock = socket.socket(_addr_family(server), socket.SOCK_DGRAM)
    sock.settimeout(timeout)
    try:
        sock.connect((server, 53))  # connected UDP: kernel filters non-matching source IPs
        sock.send(packet)
        # A169: 65535-byte buffer — max UDP DNS response. The server may send more
        # than the 1232-byte EDNS0 advertisement; a too-small buffer silently crops.
        data = sock.recv(65535)
    except OSError as e:
        # A163: catch all OSError subclasses (socket.timeout, ConnectionRefusedError,
        # ENETUNREACH, EHOSTUNREACH, etc.) so one refusing server does not abort
        # the walk or surface as HTTP 500.
        raise DNSError(str(e) if str(e) else "network error")
    finally:
        sock.close()
    parsed = _parse_response(data, txid, qtype_, expected_qname=qname, expected_qclass=qclass_)
    if parsed["truncated"]:
        parsed = _query_tcp(name, qtype, server, timeout)
    return parsed


def _query_tcp(name, qtype, server, timeout):
    txid, packet, qname, qtype_, qclass_ = _build_query(name, qtype)
    framed = struct.pack(">H", len(packet)) + packet
    sock = socket.socket(_addr_family(server), socket.SOCK_STREAM)
    sock.settimeout(timeout)
    try:
        sock.connect((server, 53))
        sock.sendall(framed)
        length_bytes = _recv_exact(sock, 2)
        (length,) = struct.unpack(">H", length_bytes)
        # SD-72: cap server-declared TCP response length to prevent OOM.
        if length > _MAX_TCP_LENGTH:
            raise DNSError("server-declared TCP length exceeds %d" % _MAX_TCP_LENGTH)
        data = _recv_exact(sock, length)
    finally:
        sock.close()
    return _parse_response(data, txid, qtype_, expected_qname=qname, expected_qclass=qclass_)


def _recv_exact(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise DNSError("connection closed")
        buf += chunk
    return buf


def _question_bytes(wire):
    """Return (txid_bytes, question_bytes) for a DNS message, or raise DNSError.

    The question section is [qname][qtype:2][qclass:2] starting at offset 12. qname in a QUESTION
    is never compressed, so its bytes are stable to compare. We reuse _read_name only to find where
    the name ends; we return the RAW bytes so validation is an exact-match, not a re-parse.
    """
    if len(wire) < 12:
        raise DNSError("query too short for a header")
    _labels, off = _read_name(wire, 12)
    if off + 4 > len(wire):
        raise DNSError("query truncated in question")
    return wire[0:2], wire[12:off + 4]


def forward_wire(query_wire, server, timeout=5):
    """DoH forwarder core: send the EXACT client query bytes to `server:53` and return the EXACT
    response bytes, unchanged. Passthrough, never re-encode (D-1 §Design note) — re-encoding from a
    parsed dict would silently drop EDNS/DNSSEC/unknown RRs and ship a partly-fabricated answer,
    the "fabricated clean answer" the honesty contract forbids.

    Validation, not translation: the response txid AND question section must match the query, or it
    is rejected as a DNSError (an off-question / spoofed reply must never be passed to the client as
    an answer). Connected UDP already makes the kernel drop off-source spoofs (A020); this adds the
    on-question check. TC set → retry over TCP with the 2-byte length prefix.
    """
    txid, question = _question_bytes(query_wire)

    fam = _addr_family(server)
    sock = socket.socket(fam, socket.SOCK_DGRAM)
    sock.settimeout(timeout)
    try:
        sock.connect((server, 53))  # connected UDP: kernel discards non-matching source IPs (A020)
        sock.send(query_wire)
        # A169: use 65535-byte buffer (max UDP DNS response). The previous 4096-byte
        # buffer silently discarded the tail of any larger datagram — the client's
        # EDNS0 OPT may advertise a buffer above 4096, and the upstream is entitled
        # to send that much. A recv into a too-small buffer truncates locally with
        # no TC bit (the truncation happened here, not at the server).
        resp = sock.recv(65535)
    except OSError as e:
        # A163: catch all OSError subclasses so upstream failures are honest DNSError
        raise DNSError(str(e) if str(e) else "upstream network error")
    finally:
        sock.close()

    truncated = len(resp) >= 3 and bool(resp[2] & 0x02)  # TC bit in the flags byte (peer-set only)
    if truncated:
        framed = struct.pack(">H", len(query_wire)) + query_wire
        tsock = socket.socket(fam, socket.SOCK_STREAM)
        tsock.settimeout(timeout)
        try:
            tsock.connect((server, 53))
            tsock.sendall(framed)
            (rlen,) = struct.unpack(">H", _recv_exact(tsock, 2))
            # SD-72: cap server-declared TCP response length to prevent OOM.
            if rlen > _MAX_TCP_LENGTH:
                raise DNSError("server-declared TCP length exceeds %d" % _MAX_TCP_LENGTH)
            resp = _recv_exact(tsock, rlen)
        finally:
            tsock.close()

    # Reject an off-txid or off-question reply rather than hand the client a wrong answer.
    if resp[0:2] != txid:
        raise DNSError("response txid mismatch")
    try:
        _rtxid, rquestion = _question_bytes(resp)
    except DNSError:
        raise DNSError("response has no parseable question")
    if rquestion != question:
        raise DNSError("response question does not match the query")
    return resp


def query_system(name, qtype):
    """Resolve via the host's own configured DNS path (getaddrinfo).

    On locked-down networks that block both UDP/53 and direct connections to
    public-resolver IPs (e.g. NextDNS), this is the one path guaranteed to work,
    because it is the resolver the machine itself uses. Returns only final
    A/AAAA addresses (no rcode/DNSSEC detail).
    """
    if qtype == TYPE_A:
        family, tname = socket.AF_INET, "A"
    elif qtype == TYPE_AAAA:
        family, tname = socket.AF_INET6, "AAAA"
    else:
        raise DNSError("system resolver supports A/AAAA only")
    try:
        infos = socket.getaddrinfo(name, None, family=family, type=socket.SOCK_STREAM)
    except socket.gaierror as e:
        raise DNSError("system: %s" % e)
    values = set()
    for info in infos:
        ip = info[4][0].split("%")[0]  # strip any scope id
        if qtype == TYPE_AAAA:
            # getaddrinfo synthesizes ::ffff:x.x.x.x for v4-only names; those are
            # not real AAAA records, so drop them to avoid false divergence.
            addr = ipaddress.ip_address(ip)
            if addr.ipv4_mapped is not None:
                continue
        values.add(ip)
    return {"rcode": 0, "ad": False, "truncated": False,
            "records": [{"type": tname, "value": v} for v in sorted(values)]}


class _PinnedHTTPSConnection(http.client.HTTPSConnection):
    """HTTPSConnection that dials a caller-vetted IP literal and NEVER re-resolves
    the hostname.

    A067: the stock pattern -- HTTPSConnection(ip) then `conn.host = name` -- makes
    http.client re-run getaddrinfo(name) inside connect() (self.host is both the dial
    target and the SNI value), discarding the vetted IP and reopening the DNS-rebind
    SSRF window. Here `pin_ip` is an already-validated numeric address, so
    socket.create_connection performs no DNS query and cannot be rebound; the real
    hostname is still used for SNI and certificate validation."""

    def __init__(self, pin_ip, port, server_hostname, timeout, context):
        super().__init__(pin_ip, port, timeout=timeout, context=context)
        self._pin_ip = pin_ip
        self._server_hostname = server_hostname

    def connect(self):
        # pin_ip is numeric -> create_connection does not query DNS (no rebind).
        sock = socket.create_connection((self._pin_ip, self.port), self.timeout,
                                        self.source_address)
        self.sock = self._context.wrap_socket(sock, server_hostname=self._server_hostname)


def _parse_doh_json(data):
    """Strictly validate a DoH JSON reply (A039). A resolver or error proxy returning
    {} or a partial/mistyped object must NOT be read as a successful empty answer;
    every missing or wrong-typed field raises DNSError so the vantage stays honestly
    inconclusive instead of joining consensus as fabricated negative evidence.

    A valid NOERROR, NXDOMAIN, or NODATA reply (Status present, Answer absent or a
    typed list) is preserved with its real rcode."""
    if not isinstance(data, dict):
        # A bare array/string/number would make data.get() crash; reject the shape.
        raise DNSError("doh: unexpected response shape")
    status = data.get("Status")
    # bool is a subclass of int; a JSON true/false Status is not a valid rcode.
    if isinstance(status, bool) or not isinstance(status, int) or not (0 <= status <= 65535):
        raise DNSError("doh: missing or invalid Status")
    answer = data.get("Answer", [])
    if not isinstance(answer, list):
        raise DNSError("doh: Answer is not a list")
    records = []
    for a in answer:
        if not isinstance(a, dict):
            raise DNSError("doh: answer member is not an object")
        rtype = a.get("type")
        rdata = a.get("data")
        if isinstance(rtype, bool) or not isinstance(rtype, int):
            raise DNSError("doh: answer type invalid")
        if not isinstance(rdata, str):
            raise DNSError("doh: answer data invalid")
        records.append({"type": _TYPE_NAMES.get(rtype, str(rtype)), "value": rdata})
    return {"rcode": status, "ad": bool(data.get("AD")), "truncated": False, "records": records}


def query_doh(name, qtype, ip, sni, path, timeout=5):
    """Query a resolver over DNS-over-HTTPS (JSON API), pinned to its IP.

    Connecting by IP with the hostname as SNI means this works even when plain
    UDP/53 is blocked (NextDNS, captive filters) and when the DoH host itself
    cannot be resolved via the broken system DNS. Certificate validation still
    matches the real hostname.
    """
    # A067: the connection must dial only the vetted IP. Gate it through the same
    # public-IP check as the authoritative walker so a misconfigured/hostile vantage
    # entry cannot point the server at an internal address.
    if not _ip_is_public(ip):
        raise DNSError("doh: non-public resolver IP %s" % ip)
    q = urllib.parse.urlencode({"name": name, "type": qtype})
    ctx = ssl.create_default_context()
    conn = _PinnedHTTPSConnection(ip, 443, sni, timeout, ctx)
    try:
        conn.putrequest("GET", path + "?" + q, skip_host=True, skip_accept_encoding=True)
        conn.putheader("Host", sni)
        conn.putheader("Accept", "application/dns-json")
        conn.putheader("User-Agent", "SyxxServer/2.0")
        conn.endheaders()
        resp = conn.getresponse()
        if resp.status != 200:
            raise DNSError("doh http %d" % resp.status)
        data = json.loads(resp.read(1 << 20).decode("utf-8", "ignore"))
    except (OSError, ssl.SSLError, ValueError) as e:
        # ValueError covers json.JSONDecodeError for a non-JSON / error body.
        raise DNSError("doh: %s" % e)
    finally:
        conn.close()
    return _parse_doh_json(data)


# ---------------------------------------------------------------------------
# Authoritative ground-truth oracle (ROADMAP Workstream S #1)
#
# Iteratively resolves a name from the root down, following delegations itself (RD=0), so the answer is
# the zone's OWN authoritative data — ground truth an interfering/on-path resolver cannot fabricate. The
# app diffs the client's answer against this. Bounded (depth, per-query timeout, servers tried per level)
# so a hostile or broken delegation can't hang it.
# ---------------------------------------------------------------------------

# Root hints (IANA a–m root servers, IPv4). Enough redundancy that a few being unreachable is fine.
ROOT_SERVERS = [
    "198.41.0.4", "199.9.14.201", "192.33.4.12", "199.7.91.13", "192.203.230.10",
    "192.5.5.241", "192.112.36.4", "198.97.190.53", "192.36.148.17", "192.58.128.30",
    "193.0.14.129", "199.7.83.42", "202.12.27.33",
]

MAX_DELEGATION_DEPTH = 16
MAX_SERVERS_PER_LEVEL = 4


_MAX_RECORDS_PER_SECTION = 256  # A160: cap parsed records per section


def _read_records(data, offset, count):
    """Parse `count` resource records starting at `offset`.
    A160: capped at _MAX_RECORDS_PER_SECTION to prevent amplification from
    attacker-controlled header counts (up to 65535).
    A168: returns (records, next_offset, complete). `complete` is False when the
    section was abandoned early due to a malformed record or truncated data, so
    the caller can treat an incomplete parse as inconclusive rather than
    authoritative."""
    recs = []
    effective_count = min(count, _MAX_RECORDS_PER_SECTION)
    complete = True
    for i in range(effective_count):
        if offset >= len(data):
            complete = (i >= count)  # incomplete only if we expected more
            break
        try:
            owner, offset = _read_name(data, offset)
        except DNSError:
            complete = False
            break
        if offset + 10 > len(data):
            complete = False
            break
        rtype, rclass, ttl, rdlength = struct.unpack(">HHIH", data[offset:offset + 10])
        offset += 10
        rdata_offset = offset
        if offset + rdlength > len(data):
            complete = False
            break
        value = _format_rdata(rtype, data[offset:offset + rdlength], data, rdata_offset)
        # A115: drop length-invalid A/AAAA records (None = malformed, not hex garbage).
        if value is None:
            offset += rdlength
            continue
        recs.append({"owner": owner, "type": _TYPE_NAMES.get(rtype, str(rtype)), "rtype": rtype,
                     "ttl": ttl, "value": value})
        offset += rdlength
    if effective_count < count:
        complete = False  # A160: capped below declared count
    return recs, offset, complete


def _parse_sections(data, expected_txid, expected_qname=None, expected_qtype=None, expected_qclass=1):
    """Parse a response into answer / authority / additional sections (the walker needs referrals+glue).
    Validates the question section matches what was asked when expected_qname is given (A020)."""
    if len(data) < 12:
        raise DNSError("short header")
    txid, flags, qd, an, ns, ar = struct.unpack(">HHHHHH", data[:12])
    if txid != expected_txid:
        raise DNSError("txid mismatch")
    if not (flags & 0x8000):
        raise DNSError("not a response")
    # A061: reject QDCOUNT != 1 (same guard as _parse_response).
    if qd != 1:
        raise DNSError("unexpected question count: %d" % qd)
    offset = 12
    for _ in range(qd):
        qname, offset = _read_name(data, offset)
        if offset + 4 > len(data):
            # A078/A098: truncated question section -> honest DNSError, not an
            # uncaught struct.error that the diff/trace/authoritative routes would
            # surface as a 500 hiding an inconclusive result.
            raise DNSError("truncated question section")
        qtype_parsed, qclass_parsed = struct.unpack(">HH", data[offset:offset + 4])
        offset += 4
        if expected_qname is not None:
            if qname.lower() != expected_qname.lower() or qtype_parsed != expected_qtype or qclass_parsed != expected_qclass:
                raise DNSError("question mismatch: asked %s/%d/%d, got %s/%d/%d" %
                               (expected_qname, expected_qtype, expected_qclass, qname, qtype_parsed, qclass_parsed))
        break  # only validate the first question
    answers, offset, ans_complete = _read_records(data, offset, an)
    authority, offset, auth_complete = _read_records(data, offset, ns)
    additional, offset, addl_complete = _read_records(data, offset, ar)
    # A168: surface parse completeness so the walker can treat an incomplete
    # section as inconclusive rather than authoritative.
    return {"rcode": flags & 0x000F, "aa": bool(flags & 0x0400), "truncated": bool(flags & 0x0200),
            "answers": answers, "authority": authority, "additional": additional,
            "parseComplete": ans_complete and auth_complete and addl_complete}


def _query_sections(name, qtype, server, timeout=3, do=False, rd=False):
    """One query to `server`, parsed into sections; TCP fallback on truncation.
    Uses connected UDP so the kernel discards off-source spoofed replies (A020). `do=True` sets the
    EDNS DO bit so the server includes covering RRSIG records.

    `rd` defaults to False (iterative) because the delegation walker must never hand recursion to an
    upstream. S13: a RECURSIVE vantage (1.1.1.1, 8.8.8.8) answers RD=0 with REFUSED or an empty
    referral, so asking one about DNSSEC requires rd=True."""
    txid, packet, qname, qtype_, qclass_ = _build_query(name, qtype, rd=rd, do=do)
    sock = socket.socket(_addr_family(server), socket.SOCK_DGRAM)
    sock.settimeout(timeout)
    try:
        sock.connect((server, 53))  # connected UDP: kernel filters non-matching source IPs
        sock.send(packet)
        # A169: 65535-byte buffer for consistency with forward_wire
        data = sock.recv(65535)
    except OSError as e:
        # A163: catch all OSError subclasses so one dead server does not end the walk
        raise DNSError(str(e) if str(e) else "network error")
    finally:
        sock.close()
    parsed = _parse_sections(data, txid, expected_qname=qname, expected_qtype=qtype_, expected_qclass=qclass_)
    if parsed["truncated"]:
        framed = struct.pack(">H", len(packet)) + packet
        tsock = socket.socket(_addr_family(server), socket.SOCK_STREAM)
        tsock.settimeout(timeout)
        try:
            tsock.connect((server, 53))
            tsock.sendall(framed)
            (length,) = struct.unpack(">H", _recv_exact(tsock, 2))
            # SD-72: cap server-declared TCP response length to prevent OOM.
            if length > _MAX_TCP_LENGTH:
                raise DNSError("server-declared TCP length exceeds %d" % _MAX_TCP_LENGTH)
            parsed = _parse_sections(_recv_exact(tsock, length), txid,
                                      expected_qname=qname, expected_qtype=qtype_, expected_qclass=qclass_)
        finally:
            tsock.close()
    return parsed


def _serial_lte(a, b):
    """RFC 1982 (SERIAL_BITS = 32) 'a is at or before b': the forward distance a->b, mod 2**32, is
    under 2**31."""
    return ((b - a) & 0xFFFFFFFF) < 0x80000000


def _rrsig_window_valid(inception, expiration, now):
    """A042/B047: is `now` within [inception, expiration] using RFC 1982 serial-number arithmetic
    (mod 2**32), not absolute-epoch comparison, with a sane-span guard against clock-spoofed wrap.
    Checks the validity *window* only; does not cryptographically verify the signature."""
    if not _serial_lte(inception, expiration):
        return False  # expiration precedes inception (serial) -> malformed
    if ((expiration - inception) & 0xFFFFFFFF) > MAX_RRSIG_VALIDITY_SPAN:
        return False  # B047: implausibly wide window (clock-spoof / forged validity)
    now32 = int(now) & 0xFFFFFFFF
    return _serial_lte(inception, now32) and _serial_lte(now32, expiration)


def _parse_rrsig_window(rdata_hex, expected_type_covered=TYPE_DNSKEY):
    """Parse the raw 32-bit inception/expiration out of an RRSIG record's wire rdata (hex). Returns
    (inception, expiration) or None if the typeCovered doesn't match or the rdata is too short.
    Layout: typeCovered(2) algorithm(1) labels(1) originalTTL(4) sigExpiration(4) sigInception(4) ..."""
    try:
        rdata = bytes.fromhex(rdata_hex)
    except (ValueError, TypeError):
        return None
    if len(rdata) < 18:
        return None
    type_covered = (rdata[0] << 8) | rdata[1]
    if type_covered != expected_type_covered:
        return None
    expiration = int.from_bytes(rdata[8:12], "big")
    inception = int.from_bytes(rdata[12:16], "big")
    return inception, expiration


def rrsig_window_status(rdata_hex, now, expected_type_covered=TYPE_DNSKEY):
    """Classify ONE RRSIG's validity WINDOW — never its cryptography.

    Returns 'valid' | 'expired' | 'not-yet-valid' | 'wrong-type' | 'unknown'. A042: a
    not-yet-valid signature is not 'valid'. A043: an RRSIG over some other RRset is
    'wrong-type', not evidence about `expected_type_covered`. B047: an implausibly wide
    window is 'unknown', not 'valid'."""
    try:
        rdata = bytes.fromhex(rdata_hex)
    except (ValueError, TypeError):
        return "unknown"
    if len(rdata) < 18:
        return "unknown"
    if ((rdata[0] << 8) | rdata[1]) != expected_type_covered:
        return "wrong-type"
    window = _parse_rrsig_window(rdata_hex, expected_type_covered)
    if window is None:
        return "unknown"
    inception, expiration = window
    if _rrsig_window_valid(inception, expiration, now):
        return "valid"
    if not _serial_lte(inception, expiration):
        return "unknown"                       # expiration precedes inception -> malformed
    now32 = int(now) & 0xFFFFFFFF
    if not _serial_lte(inception, now32):
        return "not-yet-valid"
    if not _serial_lte(now32, expiration):
        return "expired"
    return "unknown"                           # B047 implausible span: cannot confirm


def zone_signing_status(zone, server, timeout, now=None, rd=False):
    """DNSSEC posture for `zone` as seen at `server`, plus the per-RRSIG window detail.

    Returns ``(posture, rrsig_statuses)``:
      posture True  = DNSKEY present AND at least one RRSIG over the DNSKEY RRset is within its
                      validity window (window observed; signature NOT cryptographically verified).
      posture False = no DNSKEY in the answer — as far as this server is concerned the zone is
                      unsigned.
      posture None  = DNSKEY present but no valid covering RRSIG (stripped / expired / DO ignored),
                      or the query could not be completed — an honest 'cannot confirm', never a
                      false 'signed'.
    `rrsig_statuses` holds one entry per DNSKEY-covering RRSIG (see rrsig_window_status); RRSIGs
    covering other RRsets are excluded rather than counted as evidence (A043).

    The DNSKEY query carries the EDNS DO bit, which is the ONLY way an RRSIG is returned (RFC 4034
    §3.2). `rd=True` is required when `server` is a recursive vantage rather than an authoritative."""
    if now is None:
        now = time.time()
    try:
        resp = _query_sections(zone, TYPE_DNSKEY, server, timeout, do=True, rd=rd)
    except DNSError:
        return None, []
    if not any(r["type"] == "DNSKEY" for r in resp["answers"]):
        return False, []
    # B145: iterate all three response sections for RRSIGs, not just "answers". Some authoritative
    # servers place RRSIGs in the additional or authority section; checking only answers returns
    # None instead of True for signed zones whose RRSIGs span sections.
    all_records = list(resp.get("answers", [])) + list(resp.get("authority", [])) + list(resp.get("additional", []))
    statuses = [s for s in (rrsig_window_status(r["value"], now)
                            for r in all_records if r.get("rtype") == TYPE_RRSIG)
                if s != "wrong-type"]
    return (True if "valid" in statuses else None), statuses


def _zone_is_signed(zone, server, timeout, now=None):
    """Tri-state DNSSEC posture (True signed / False unsigned / None cannot-confirm) asked straight
    at the AUTHORITATIVE `server`, so an on-path resolver can't strip it. Thin wrapper over
    zone_signing_status so there is exactly one implementation of the DNSKEY+RRSIG probe."""
    return zone_signing_status(zone, server, timeout, now)[0]


def resolve_authoritative(name, qtype, timeout=3, max_depth=MAX_DELEGATION_DEPTH, deadline=None):
    """Walk root -> TLD -> authoritative for `name`/`qtype`, returning the authoritative answer plus the
    delegation chain. Never delegates recursion to an upstream resolver, so the result is ground truth.

    A066: `deadline` is a monotonic wall-clock limit for the entire walk (including nested
    sub-walks). When set, each iteration and each recursive call checks it; exceeding it
    aborts to inconclusive rather than pinning the handler thread for minutes."""
    name = (name or "").rstrip(".") or "."
    delegation = []
    servers = [{"ip": ip, "label": ip} for ip in ROOT_SERVERS]
    zone = "."
    for depth in range(max_depth):
        # A066: abort if the shared deadline has passed.
        if deadline is not None and time.monotonic() >= deadline:
            delegation.append({"zone": zone, "server": None, "error": "walk deadline exceeded"})
            return {"name": name, "qtype": qtype, "delegation": delegation,
                    "authoritative": None, "conclusion": "inconclusive"}
        resp, used, used_ip = None, None, None
        for s in servers[:MAX_SERVERS_PER_LEVEL]:
            try:
                # B145: set the DO (DNSSEC OK) bit on authoritative queries so resolvers include
                # DNSSEC records. Without it the server is systematically blind to DNSSEC data.
                resp, used, used_ip = _query_sections(name, qtype, s["ip"], timeout, do=True), s["label"], s["ip"]
                break
            except DNSError:
                continue
        if resp is None:
            delegation.append({"zone": zone, "server": None, "error": "no reachable nameserver at this level"})
            return {"name": name, "qtype": qtype, "delegation": delegation, "authoritative": None, "conclusion": "inconclusive"}

        # A168: an incomplete parse means records were silently dropped by the
        # parser. Treat this as inconclusive — a confident "authoritative" label
        # over a partially-parsed response is a claim ahead of the evidence.
        if not resp.get("parseComplete", True):
            delegation.append({"zone": zone, "server": used,
                               "error": "incomplete parse — records may have been dropped"})
            return {"name": name, "qtype": qtype, "delegation": delegation,
                    "authoritative": None, "conclusion": "inconclusive"}

        has_soa = any(r["type"] == "SOA" for r in resp["authority"])
        if resp["answers"] or resp["rcode"] == 3 or has_soa or (not resp["authority"]):
            # An answer, NXDOMAIN, SOA-backed NODATA, or no-referral terminal response
            # is usable as authoritative ground truth only when the responding server
            # asserts AA. Otherwise it may be a recursive/middlebox answer on the
            # path and must not be treated as truth.
            answers = [r["value"] for r in resp["answers"] if r["rtype"] == qtype]
            if not resp["aa"]:
                delegation.append({"zone": zone, "server": used, "authoritative": False,
                                   "answer": [r["value"] for r in resp["answers"]],
                                   "error": "terminal response without AA bit"})
                return {"name": name, "qtype": qtype, "delegation": delegation,
                        "authoritative": None, "conclusion": "inconclusive"}
            delegation.append({"zone": zone, "server": used, "authoritative": True,
                               "answer": [r["value"] for r in resp["answers"]]})
            # A168: require non-empty answers for rcode 0 "authoritative". An empty
            # answer set with rcode 0 and no SOA in authority is not a proven NODATA
            # — it may be a CNAME-only response or a silently-dropped parse. Use
            # "inconclusive" unless we have actual evidence (answers, NXDOMAIN, or SOA).
            if resp["rcode"] == 3:
                conclusion = "authoritative"
            elif resp["rcode"] == 0 and answers:
                conclusion = "authoritative"
            elif resp["rcode"] == 0 and has_soa:
                conclusion = "authoritative"  # SOA-backed NODATA
            else:
                conclusion = "inconclusive"
            return {"name": name, "qtype": qtype, "delegation": delegation,
                    "authoritative": {"answers": answers, "aa": resp["aa"], "rcode": resp["rcode"],
                                      "dnskeyObserved": _zone_is_signed(zone, used_ip, timeout)},
                    "conclusion": conclusion}

        # A referral: NS records in the authority section, glue A records in additional.
        ns_names = [r["value"] for r in resp["authority"] if r["type"] == "NS"]
        delegated_zone = resp["authority"][0]["owner"] if resp["authority"] else zone

        # A063: reject an out-of-bailiwick delegation. A rogue authoritative for zone Z can
        # answer our query with a referral whose owner is NOT within Z (an upward/sideways
        # redirect) to steer the walk to attacker infrastructure and forge ground truth. The
        # delegated zone must be at or below the zone we actually asked.
        if not _in_bailiwick(delegated_zone, zone):
            delegation.append({"zone": zone, "server": used,
                               "error": "out-of-bailiwick referral to %s" % delegated_zone})
            return {"name": name, "qtype": qtype, "delegation": delegation,
                    "authoritative": None, "conclusion": "inconclusive"}

        delegation.append({"zone": zone, "server": used, "referral": ns_names, "delegatedZone": delegated_zone})

        # A063: only trust glue whose owner is in-bailiwick of the delegated zone. Out-of-bailiwick
        # glue (e.g. an A record for `ns.attacker.net` in a delegation of `example.com`) is discarded;
        # such NS names are instead resolved by an independent walk below, so the additional section
        # cannot be used to inject a redirect.
        # A011: never query a glue/NS IP that isn't publicly routable. A malicious authoritative
        # can return glue like `169.254.169.254` / `10.0.0.53` / `127.0.0.1` to make us probe
        # internal hosts and reflect the answers (SSRF).
        glue = {r["owner"].rstrip("."): r["value"] for r in resp["additional"]
                if r["type"] == "A" and _in_bailiwick(r["owner"], delegated_zone)}
        next_servers = [{"ip": glue[n.rstrip(".")], "label": n}
                        for n in ns_names
                        if n.rstrip(".") in glue and _ip_is_public(glue[n.rstrip(".")])]
        if not next_servers:
            # No glue — resolve a couple of NS names from the root (bounded so it can't loop forever).
            for nsname in ns_names[:2]:
                # A066: abort sub-walk early if the shared deadline has passed.
                if deadline is not None and time.monotonic() >= deadline:
                    break
                try:
                    sub_depth = min(3, max_depth - depth - 1)
                    if sub_depth <= 0:
                        continue
                    sub = resolve_authoritative(nsname, TYPE_A, timeout, sub_depth, deadline=deadline)
                    if sub.get("authoritative") and sub["authoritative"]["answers"]:
                        ip = sub["authoritative"]["answers"][0]
                        if _ip_is_public(ip):
                            next_servers.append({"ip": ip, "label": nsname})
                            break
                except DNSError:
                    continue
        if not next_servers:
            delegation.append({"zone": delegated_zone, "server": None, "error": "could not resolve any nameserver"})
            return {"name": name, "qtype": qtype, "delegation": delegation, "authoritative": None, "conclusion": "inconclusive"}
        servers, zone = next_servers, delegated_zone

    delegation.append({"zone": zone, "server": None, "error": "max delegation depth exceeded"})
    return {"name": name, "qtype": qtype, "delegation": delegation, "authoritative": None, "conclusion": "inconclusive"}

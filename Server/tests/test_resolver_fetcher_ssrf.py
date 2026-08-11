"""SSRF / rebind / bailiwick / parse-crash regressions for resolver.py + fetcher.py.

Covers deltas A067/A088/A090/A100 (DNS-rebind pin), A063 (out-of-bailiwick glue),
A033 (destination-port allowlist), A078/A098 (unguarded question-section unpack),
and A039 (hostile DoH JSON shape). Every test is written to go RED if the specific
fix is reverted (mutation-proven).

Run from Server/:  python3 -m unittest tests.test_resolver_fetcher_ssrf -v
"""

import struct

import pytest

from syxx_server import fetcher, resolver  # noqa: E402


# --------------------------------------------------------------------------- #
# A067/A088/A090/A100 — DNS-rebinding pin (never re-resolve the hostname)      #
# --------------------------------------------------------------------------- #
def _assert_dials_ip_not_hostname(module):
    """The pinned connection must dial the numeric IP and use the hostname ONLY
    for SNI. If connect() ever re-resolves the hostname, getaddrinfo fires and
    the rebind window is back."""
    dialed = {}

    class FakeCtx:
        def wrap_socket(self, sock, server_hostname=None):
            dialed["sni"] = server_hostname
            return f"tls({sock})"

    def fake_cc(addr, timeout=None, source_address=None):
        dialed["addr"] = addr
        return "rawsock"

    def fail_gai(*a, **k):
        raise AssertionError("getaddrinfo called: hostname was re-resolved (rebind window open)")

    orig_cc = module.socket.create_connection
    orig_gai = module.socket.getaddrinfo
    module.socket.create_connection = fake_cc
    module.socket.getaddrinfo = fail_gai
    try:
        conn = module._PinnedHTTPSConnection("203.0.113.7", 443, "rebind.evil.example", 3, FakeCtx())
        conn.connect()
    finally:
        module.socket.create_connection = orig_cc
        module.socket.getaddrinfo = orig_gai

    assert dialed["addr"] == ("203.0.113.7", 443), "must dial the pinned IP, never the hostname"
    assert dialed["sni"] == "rebind.evil.example", (
        "TLS SNI/cert validation must still use the real hostname"
    )
    assert conn.sock == "tls(rawsock)"


def test_resolver_pinned_connection_dials_ip():
    _assert_dials_ip_not_hostname(resolver)


def test_fetcher_pinned_connection_dials_ip():
    _assert_dials_ip_not_hostname(fetcher)


def test_fetcher_one_request_pins_vetted_ip_for_https():
    """_one_request must hand the PinnedHTTPSConnection the vetted IP as the dial
    target and the hostname as SNI — proving the vetted IP is what gets dialed,
    not the rebindable hostname."""
    captured = {}

    class Spy:
        def __init__(self, pin_ip, port, server_hostname, timeout, context):
            captured["pin_ip"] = pin_ip
            captured["sni"] = server_hostname
            captured["port"] = port

        def putrequest(self, *a, **k):
            pass

        def putheader(self, *a, **k):
            pass

        def endheaders(self):
            pass

        def getresponse(self):
            class R:
                status = 200
                _reads = 0

                def getheader(self, _n, default=None):
                    return default

                def read(self, _n):
                    # Chunked body protocol: first call returns payload, subsequent
                    # calls return empty so the reader terminates (A164 loop).
                    self._reads += 1
                    return b"ok" if self._reads == 1 else b""

            return R()

        def close(self):
            pass

    orig_pin = fetcher._PinnedHTTPSConnection
    orig_resolve = fetcher._resolve_public
    fetcher._PinnedHTTPSConnection = Spy
    fetcher._resolve_public = lambda host: "93.184.216.34"
    try:
        fetcher._one_request("https://example.com/path", timeout=1, max_bytes=100)
    finally:
        fetcher._PinnedHTTPSConnection = orig_pin
        fetcher._resolve_public = orig_resolve

    assert captured.get("pin_ip") == "93.184.216.34", (
        "HTTPS fetch must pin the vetted IP, not re-resolve the hostname"
    )
    assert captured.get("sni") == "example.com"
    assert captured.get("port") == 443


def test_query_doh_rejects_non_public_resolver_ip_before_connecting():
    """query_doh must gate the pinned IP through _ip_is_public and refuse a
    private/loopback target WITHOUT opening a connection to it."""
    orig = resolver._PinnedHTTPSConnection

    def never(*a, **k):
        raise AssertionError("connected to a non-public resolver IP")

    resolver._PinnedHTTPSConnection = never
    try:
        with pytest.raises(resolver.DNSError):
            resolver.query_doh("example.com", 1, "127.0.0.1", "dns.evil", "/dns-query")
    finally:
        resolver._PinnedHTTPSConnection = orig


# --------------------------------------------------------------------------- #
# A063 — out-of-bailiwick referral / glue must not redirect the walk           #
# --------------------------------------------------------------------------- #
def _referral(owner_zone, ns_value, glue_owner, glue_ip):
    return {
        "rcode": 0,
        "aa": False,
        "truncated": False,
        "answers": [],
        "authority": [{"owner": owner_zone, "type": "NS", "rtype": resolver.TYPE_NS, "ttl": 3600, "value": ns_value}],
        "additional": [{"owner": glue_owner, "type": "A", "rtype": resolver.TYPE_A, "ttl": 3600, "value": glue_ip}],
    }


def _forged_answer():
    return {
        "rcode": 0,
        "aa": True,
        "truncated": False,
        "answers": [{"owner": "example.com", "type": "A", "rtype": resolver.TYPE_A, "ttl": 60, "value": "6.6.6.6"}],
        "authority": [],
        "additional": [],
    }


@pytest.fixture
def _bailiwick(monkeypatch):
    # Never let the walker make a real DNSKEY query during these mocked walks.
    monkeypatch.setattr(resolver, "_zone_is_signed", lambda zone, srv, timeout: None)
    # Register _query_sections for restore even though the tests reassign it directly.
    monkeypatch.setattr(resolver, "_query_sections", resolver._query_sections)
    return []


def test_in_bailiwick_helper():
    assert resolver._in_bailiwick("ns1.example.com", "example.com")
    assert resolver._in_bailiwick("example.com", "example.com")
    assert resolver._in_bailiwick("anything.net", ".")  # root contains all
    assert not resolver._in_bailiwick("ns.attacker.net", "example.com")
    assert not resolver._in_bailiwick("evil.net", "com")
    assert not resolver._in_bailiwick("notexample.com", "example.com")  # suffix, not subdomain


def test_out_of_bailiwick_glue_is_discarded(_bailiwick):
    """A .com server delegates example.com but supplies glue for ns.evil.net
    (out of bailiwick of example.com). That glue IP must never be queried, and
    with no usable in-bailiwick nameserver the walk is honestly inconclusive."""
    roots = set(resolver.ROOT_SERVERS)
    attacker_ip = "98.76.54.32"
    queried = _bailiwick

    def mock(name, qtype, server, timeout=3, do=False, rd=False, deadline=None):
        queried.append(server)
        if server in roots:
            if name == "example.com":
                return _referral("com", "ns1.com", "ns1.com", "12.34.56.78")
            raise resolver.DNSError(f"root: nothing for {name}")
        if server == "12.34.56.78" and name == "example.com":
            # in-bailiwick delegation, but OUT-of-bailiwick glue owner.
            return _referral("example.com", "ns.evil.net", "ns.evil.net", attacker_ip)
        if server == attacker_ip:
            return _forged_answer()  # reachable ONLY if the glue filter is removed
        raise resolver.DNSError(f"unreachable {server}")

    resolver._query_sections = mock
    result = resolver.resolve_authoritative("example.com", resolver.TYPE_A)
    assert result["conclusion"] == "inconclusive"
    assert result["authoritative"] is None
    assert attacker_ip not in queried, "out-of-bailiwick glue IP was queried -> ground truth is forgeable"


def test_out_of_bailiwick_delegation_is_rejected(_bailiwick):
    """A .com server answers our example.com query with a referral whose owner is
    evil.net (an upward/sideways redirect). The walk must refuse it, not descend."""
    roots = set(resolver.ROOT_SERVERS)
    attacker_ip = "98.76.54.32"
    queried = _bailiwick

    def mock(name, qtype, server, timeout=3, do=False, rd=False, deadline=None):
        queried.append(server)
        if server in roots:
            if name == "example.com":
                return _referral("com", "ns1.com", "ns1.com", "12.34.56.78")
            raise resolver.DNSError(f"root: nothing for {name}")
        if server == "12.34.56.78" and name == "example.com":
            # delegated_zone == evil.net is NOT within the zone we asked (com).
            return _referral("evil.net", "ns.evil.net", "ns.evil.net", attacker_ip)
        if server == attacker_ip:
            return _forged_answer()
        raise resolver.DNSError(f"unreachable {server}")

    resolver._query_sections = mock
    result = resolver.resolve_authoritative("example.com", resolver.TYPE_A)
    assert result["conclusion"] == "inconclusive"
    assert result["authoritative"] is None
    assert attacker_ip not in queried, "out-of-bailiwick referral was followed -> ground truth is forgeable"


def test_in_bailiwick_glue_is_still_followed(_bailiwick):
    """Guard against over-blocking: a legitimate in-bailiwick delegation with
    in-bailiwick glue must still resolve to an authoritative answer."""
    roots = set(resolver.ROOT_SERVERS)

    def mock(name, qtype, server, timeout=3, do=False, rd=False, deadline=None):
        if server in roots and name == "example.com":
            return _referral("com", "ns1.com", "ns1.com", "12.34.56.78")
        if server == "12.34.56.78" and name == "example.com":
            return _referral("example.com", "ns1.example.com", "ns1.example.com", "23.45.67.89")
        if server == "23.45.67.89":
            return _forged_answer()  # here it is the legitimate authoritative
        raise resolver.DNSError(f"unreachable {server}")

    resolver._query_sections = mock
    result = resolver.resolve_authoritative("example.com", resolver.TYPE_A)
    assert result["conclusion"] == "authoritative"
    assert result["authoritative"]["answers"] == ["6.6.6.6"]


# --------------------------------------------------------------------------- #
# A033 — destination-port allowlist (no blind port scanner)                    #
# --------------------------------------------------------------------------- #
def test_nonstandard_port_is_blocked():
    with pytest.raises(fetcher.BlockedError):
        fetcher._one_request("http://example.com:22/", timeout=1, max_bytes=100)
    with pytest.raises(fetcher.BlockedError):
        fetcher._one_request("https://example.com:8443/", timeout=1, max_bytes=100)


def test_redirect_to_disallowed_port_blocked_on_next_hop():
    """Fix 2: the allowlist must be re-checked on every redirect hop, so an
    http->:9000 redirect cannot bypass a first-request-only check."""

    class Resp:
        status = 302

        def getheader(self, name, default=None):
            return "http://example.com:9000/" if name == "Location" else default

        def read(self, _n):
            return b""

    class Conn:
        def __init__(self, *a, **k):
            pass

        def putrequest(self, *a, **k):
            pass

        def putheader(self, *a, **k):
            pass

        def endheaders(self):
            pass

        def getresponse(self):
            return Resp()

        def close(self):
            pass

    orig_resolve = fetcher._resolve_public
    orig_http = fetcher.http.client.HTTPConnection
    fetcher._resolve_public = lambda host: "93.184.216.34"
    fetcher.http.client.HTTPConnection = Conn
    try:
        with pytest.raises(fetcher.BlockedError):
            fetcher.fetch("http://example.com/", timeout=1, max_bytes=100, max_redirects=3)
    finally:
        fetcher._resolve_public = orig_resolve
        fetcher.http.client.HTTPConnection = orig_http


# --------------------------------------------------------------------------- #
# A078/A098 — truncated question section -> DNSError, never struct.error/500   #
# --------------------------------------------------------------------------- #
def _truncated_question_packet(txid=0x1234):
    # QR=1, QDCOUNT=1, then a root-label name (0x00) and NOTHING after it, so the
    # qtype/qclass unpack would read past the end of the buffer.
    header = struct.pack(">HHHHHH", txid, 0x8000, 1, 0, 0, 0)
    return txid, header + b"\x00"


def test_parse_response_truncated_question_is_dnserror():
    txid, pkt = _truncated_question_packet()
    with pytest.raises(resolver.DNSError):
        resolver._parse_response(pkt, txid, resolver.TYPE_A)


def test_parse_sections_truncated_question_is_dnserror():
    txid, pkt = _truncated_question_packet()
    with pytest.raises(resolver.DNSError):
        resolver._parse_sections(pkt, txid)


# --------------------------------------------------------------------------- #
# A039 — hostile / malformed DoH JSON must be inconclusive, never fake-success #
# --------------------------------------------------------------------------- #
def test_empty_object_is_not_successful_empty_answer():
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({})


def test_missing_status_with_answers_is_rejected():
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Answer": [{"type": 1, "data": "1.2.3.4"}]})


def test_boolean_and_string_status_are_rejected():
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": True})
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": "0"})


def test_non_list_answer_is_rejected():
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": 0, "Answer": "1.2.3.4"})


def test_non_object_and_missing_field_answer_members_rejected():
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": 0, "Answer": [42]})
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": 0, "Answer": [{"type": 1}]})  # no data
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": 0, "Answer": [{"data": "x"}]})  # no type
    with pytest.raises(resolver.DNSError):
        resolver._parse_doh_json({"Status": 0, "Answer": [{"type": "A", "data": "x"}]})  # type not int


def test_valid_noerror_answer_preserved():
    out = resolver._parse_doh_json({"Status": 0, "AD": True, "Answer": [{"type": 1, "data": "1.2.3.4"}]})
    assert out["rcode"] == 0
    assert out["ad"]
    assert out["records"] == [{"type": "A", "value": "1.2.3.4"}]


def test_valid_nxdomain_and_nodata_preserved():
    nx = resolver._parse_doh_json({"Status": 3})
    assert nx["rcode"] == 3
    assert nx["records"] == []
    nodata = resolver._parse_doh_json({"Status": 0})
    assert nodata["rcode"] == 0
    assert nodata["records"] == []

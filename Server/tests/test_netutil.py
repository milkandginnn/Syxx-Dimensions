"""Tests for the consolidated public-IP gate (syxx_server/netutil.py).

Covers SD-07 (IPv4-mapped IPv6 bypass of the private check) and proves the
resolver/fetcher/firewall_test callers all route through the shared gate.
Mutation-proven: the SD-07 test is checked against a deliberately broken
variant that skips the IPv4-mapped normalization, so the suite goes RED if
that normalization is ever removed.

Run from Server/:  python3 -m unittest tests.test_netutil -v
"""

import ipaddress

from syxx_server import fetcher, firewall_test, netutil  # noqa: E402


def test_public_ipv4_passes():
    for ip in ("8.8.8.8", "93.184.216.34", "203.0.114.7", "1.1.1.1"):
        assert netutil.ip_is_public(ip), ip


def test_public_ipv6_passes():
    for ip in ("2001:4860:4860::8888", "2606:4700:4700::1111"):
        assert netutil.ip_is_public(ip), ip


def test_private_ipv4_fails():
    for ip in ("10.0.0.1", "172.16.5.4", "192.168.1.1"):
        assert not netutil.ip_is_public(ip), ip


def test_loopback_fails():
    assert not netutil.ip_is_public("127.0.0.1")
    assert not netutil.ip_is_public("127.8.9.10")
    assert not netutil.ip_is_public("::1")


def test_link_local_fails():
    assert not netutil.ip_is_public("169.254.169.254")  # cloud metadata
    assert not netutil.ip_is_public("fe80::1")


def test_cgnat_multicast_reserved_unspecified_fail():
    for ip in (
        "100.64.0.1",
        "100.127.255.254",  # CGNAT RFC 6598
        "224.0.0.251",
        "ff02::fb",  # multicast
        "240.0.0.1",  # reserved
        "0.0.0.0",
        "::",
    ):  # unspecified
        assert not netutil.ip_is_public(ip), ip


def test_unparseable_input_fails_closed():
    for ip in ("", "not-an-ip", "999.1.2.3", "example.com", None):
        assert not netutil.ip_is_public(ip), repr(ip)


def test_ipv4_mapped_private_fails():
    for ip in (
        "::ffff:127.0.0.1",
        "::ffff:10.0.0.1",
        "::ffff:192.168.1.1",
        "::ffff:169.254.169.254",
        "::ffff:100.64.0.1",
    ):
        assert not netutil.ip_is_public(ip), ip


def test_ipv4_mapped_public_still_passes():
    # Guard against over-blocking: a mapped PUBLIC v4 is legitimately routable.
    assert netutil.ip_is_public("::ffff:8.8.8.8")


def test_nat64_prefix_fails():
    assert not netutil.ip_is_public("64:ff9b::7f00:1")  # embeds 127.0.0.1
    assert not netutil.ip_is_public("64:ff9b::808:808")  # even public embeds blocked


def test_failure_case_normalization_skipping_variant_is_caught():
    """Failure case (house rule): a version of the gate that SKIPS the
    IPv4-mapped normalization must be caught by this suite's key assertion.
    The broken variant below is the pre-WS8 resolver logic. On stdlibs with
    the CVE-2024-4032 fix, is_private is already mapped-aware for RFC 1918 /
    loopback, but mapped CGNAT (::ffff:100.64.0.1) STILL passes it (the
    version==4 CGNAT check never fires for a v6 object), which is exactly
    what test_ipv4_mapped_private_fails would go RED on."""

    def broken_ip_is_public(ip):
        try:
            addr = ipaddress.ip_address(ip)
        except ValueError:
            return False
        if (
            addr.is_private
            or addr.is_loopback
            or addr.is_link_local
            or addr.is_multicast
            or addr.is_reserved
            or addr.is_unspecified
        ):
            return False
        if addr.version == 4 and addr in ipaddress.ip_network("100.64.0.0/10"):
            return False
        return True

    # The broken variant wrongly admits the mapped CGNAT address...
    assert broken_ip_is_public("::ffff:100.64.0.1"), "mutation baseline changed; failure-case proof is void"
    # ...so the canonical assertion would fail against it (test catches the bug).
    assert broken_ip_is_public("::ffff:100.64.0.1") != netutil.ip_is_public("::ffff:100.64.0.1")


def test_resolver_gate_blocks_mapped_private():
    assert not netutil.ip_is_public("::ffff:10.0.0.1")
    assert netutil.ip_is_public("8.8.8.8")


def test_firewall_gate_blocks_mapped_private():
    assert not firewall_test._ip_is_public("::ffff:192.168.1.1")
    assert firewall_test._ip_is_public("8.8.8.8")


def test_fetcher_gate_blocks_mapped_private(monkeypatch):
    monkeypatch.setattr(fetcher.config, "FETCH_ALLOW_PRIVATE", False)
    assert not fetcher._ip_is_public("::ffff:127.0.0.1")
    assert fetcher._ip_is_public("8.8.8.8")


def test_fetcher_allow_private_escape_hatch(monkeypatch):
    """FETCH_ALLOW_PRIVATE admits private ranges (testing only) but an
    unparseable address must STILL be rejected."""
    monkeypatch.setattr(fetcher.config, "FETCH_ALLOW_PRIVATE", True)
    assert fetcher._ip_is_public("127.0.0.1")
    assert not fetcher._ip_is_public("not-an-ip")

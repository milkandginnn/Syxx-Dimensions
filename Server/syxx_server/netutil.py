"""Shared network utility functions — IP validation, SSRF guards, pinned TLS.

Canonical home of the public-IP gate that was previously duplicated in
resolver.py (A011), fetcher.py (WS8-FIX) and firewall_test.py. The fetcher
copy normalized IPv4-mapped IPv6 before classification; the resolver and
firewall copies did not, so ::ffff:127.0.0.1 / ::ffff:10.0.0.1 classified
as public IPv6 there (SD-07 bypass). This module keeps the strictest
behavior for every caller and adds the NAT64 well-known prefix.

Also home of PinnedHTTPSConnection — the anti-DNS-rebind HTTPS connection
that was previously duplicated in resolver.py and fetcher.py.
"""

import http.client
import ipaddress
import socket

# NAT64 well-known prefix (RFC 6052): 64:ff9b::/96 embeds an IPv4 address a
# translator will dial, so it is another v6 wrapper around a possibly-internal v4.
_NAT64_NET = ipaddress.ip_network("64:ff9b::/96")
# A172: RFC 8215 local-use NAT64 prefix — embeds an IPv4 the same way as the
# well-known prefix. is_private coverage varies by CPython release, so this is
# explicitly rejected rather than delegated.
_NAT64_LOCAL_NET = ipaddress.ip_network("64:ff9b:1::/48")
# A172: 6to4 (RFC 3056) — embeds an IPv4 address at bytes 2-6. Same hazard:
# is_private does not consistently cover it across interpreter versions.
_6TO4_NET = ipaddress.ip_network("2002::/16")
# CGNAT (RFC 6598) — not covered by is_private on all Python versions in use.
_CGNAT_NET = ipaddress.ip_network("100.64.0.0/10")


def _extract_embedded_ipv4(addr):
    """Extract the embedded IPv4 address from a 6to4 or NAT64 IPv6 address.

    6to4 (2002::/16): IPv4 is at bytes 2-5 (e.g., 2002:6440:0000:: → 100.64.0.0)
    NAT64 (64:ff9b::/96): IPv4 is at bytes 12-15 (e.g., 64:ff9b::ac10:0101 → 172.16.1.1)

    Returns an IPv4Address or None if extraction fails.
    """
    try:
        packed = addr.packed  # 16 bytes
        if addr in _6TO4_NET:
            # 6to4: bytes 2-5 are the IPv4 address
            ipv4_bytes = packed[2:6]
        elif addr in _NAT64_NET or addr in _NAT64_LOCAL_NET:
            # NAT64: last 4 bytes are the IPv4 address
            ipv4_bytes = packed[12:16]
        else:
            return None
        return ipaddress.IPv4Address(ipv4_bytes)
    except (ValueError, ipaddress.AddressValueError):
        return None


def ip_is_public(ip):
    """True only for publicly-routable unicast IPs.

    Handles IPv4-mapped IPv6 (::ffff:127.0.0.1 → normalize to v4 then check),
    NAT64 prefix (64:ff9b::/96), CGNAT (100.64.0.0/10), and all standard
    private/loopback/link-local/multicast/reserved/unspecified ranges.
    Unparseable input is not public (fail closed).
    """
    try:
        addr = ipaddress.ip_address(ip)
    except ValueError:
        return False
    # IPv4-mapped IPv6: normalize to the embedded v4 BEFORE classification,
    # otherwise ::ffff:10.0.0.1 reads as a plain public IPv6 (SD-07).
    if addr.version == 6 and addr.ipv4_mapped:
        addr = addr.ipv4_mapped
    # NAT64 well-known prefix (RFC 6052)
    if addr.version == 6 and addr in _NAT64_NET:
        return False
    # A172: NAT64 local-use prefix (RFC 8215) and 6to4 (RFC 3056) — both embed
    # an IPv4 address and are explicitly rejected so coverage does not depend on
    # the interpreter's ipaddress module version.
    if addr.version == 6 and (addr in _NAT64_LOCAL_NET or addr in _6TO4_NET):
        # E263: Extract the embedded IPv4 from 6to4/NAT64 and check it against
        # all IPv4 ranges (including CGNAT). A 6to4 address like 2002:6440:0000::
        # embeds 100.64.0.0 (CGNAT) which would pass the 6to4 check but the
        # embedded CGNAT address is not public.
        embedded_v4 = _extract_embedded_ipv4(addr)
        if embedded_v4 is not None:
            # Re-check the embedded IPv4 against all IPv4 ranges
            if (
                embedded_v4.is_private
                or embedded_v4.is_loopback
                or embedded_v4.is_link_local
                or embedded_v4.is_multicast
                or embedded_v4.is_reserved
                or embedded_v4.is_unspecified
            ):
                return False
            if embedded_v4 in _CGNAT_NET:
                return False
        # Even without a parseable embedded IPv4, 6to4/NAT64 is not public
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
    # CGNAT (RFC 6598)
    if addr.version == 4 and addr in _CGNAT_NET:
        return False
    return True


class PinnedHTTPSConnection(http.client.HTTPSConnection):
    """HTTPSConnection that dials a caller-vetted IP literal and NEVER re-resolves
    the hostname (A067/A088/A090/A100). The stock pattern -- HTTPSConnection(ip) then
    conn.host = host -- makes http.client re-run getaddrinfo(host) inside connect(),
    discarding the vetted IP and reopening the DNS-rebind SSRF window. Here pin_ip is
    the already-validated numeric address, so socket.create_connection performs no DNS
    query and cannot be rebound; the real hostname is still used for SNI and cert
    validation."""

    def __init__(self, pin_ip, port, server_hostname, timeout, context):
        super().__init__(pin_ip, port, timeout=timeout, context=context)
        self._pin_ip = pin_ip
        self._server_hostname = server_hostname

    def connect(self):
        # pin_ip is numeric -> create_connection does not query DNS (no rebind).
        sock = socket.create_connection((self._pin_ip, self.port), self.timeout, self.source_address)  # type: ignore[unresolved-attribute]
        self.sock = self._context.wrap_socket(sock, server_hostname=self._server_hostname)  # type: ignore[unresolved-attribute]

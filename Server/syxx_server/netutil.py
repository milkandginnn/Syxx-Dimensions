"""Shared network utility functions — IP validation, SSRF guards.

Canonical home of the public-IP gate that was previously duplicated in
resolver.py (A011), fetcher.py (WS8-FIX) and firewall_test.py. The fetcher
copy normalized IPv4-mapped IPv6 before classification; the resolver and
firewall copies did not, so ::ffff:127.0.0.1 / ::ffff:10.0.0.1 classified
as public IPv6 there (SD-07 bypass). This module keeps the strictest
behavior for every caller and adds the NAT64 well-known prefix.
"""

import ipaddress

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

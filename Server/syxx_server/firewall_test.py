"""DNS firewall/filter detection module.

Tests whether a DNS resolver is filtering or blocking domains by querying
known-blocked test domains through both the user's resolver and clean
vantages (1.1.1.1, 8.8.8.8), then comparing results.

Detection signals:
  - NXDOMAIN (rcode 3) on a domain that resolves via clean vantages
  - Sinkhole IPs (0.0.0.0, 127.0.0.1, ::1, 10.0.0.1)
  - REFUSED (rcode 5)

Returns a structured result with per-domain filtering results and an
overall assessment: "filtered", "open", or "inconclusive".

Usage:
    from syxx_server import firewall_test
    result = firewall_test.test("1.2.3.4")
"""

import concurrent.futures
import logging
import secrets

from . import resolver

log = logging.getLogger(__name__)

# Clean vantages assumed to be unfiltered.
# B322: expanded beyond the original two (1.1.1.1, 8.8.8.8) — both are commonly blocked by
# enterprise/ISP DNS filters. Adding Quad9, OpenDNS, and AdGuard provides independent paths
# that are less likely to all be blocked simultaneously.
_CLEAN_VANTAGES = ("1.1.1.1", "8.8.8.8", "9.9.9.9", "208.67.222.222", "94.140.14.14")
# SM-042 + B321: expanded sinkhole addresses. The original 4 missed common filtering
# addresses — 0.0.0.0/8, common router addresses, RFC 5737 documentation ranges,
# IPv4-mapped IPv6 loopback, and 198.18.0.0/15 (used by some DNS filters).
# B321: added ISP-specific private ranges (10.0.0.0/8, 172.16.0.0/12) and RFC 5737
# documentation ranges (192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24) that ISPs use
# for sinkholing blocked domains.
_SINKHOLE_IPS = frozenset({
    "0.0.0.0", "127.0.0.1", "::1", "10.0.0.1",
    # Additional common sinkhole addresses
    "127.0.0.53",  # systemd-resolved stub
    "192.168.1.1",  # common router
    "198.18.0.1", "198.18.0.2",  # some DNS filter appliances
    "::ffff:0:0", "::ffff:127.0.0.1",  # IPv4-mapped IPv6 loopback
    "10.0.0.2",  # some Pi-hole configs
    "10.10.10.1",  # some corporate DNS appliances
    # B321: ISP sinkhole addresses in common private ranges
    "10.0.0.0", "10.255.255.255",  # 10.0.0.0/8 boundary markers
    "172.16.0.0", "172.16.0.1", "172.31.255.255",  # 172.16.0.0/12 ISP sinkholes
    "192.0.2.0", "192.0.2.1",  # RFC 5737 TEST-NET-1 (used by some ISP filters)
    "198.51.100.0", "198.51.100.1",  # RFC 5737 TEST-NET-2
    "203.0.113.0", "203.0.113.1",  # RFC 5737 TEST-NET-3
})


def _ip_is_sinkhole(ip):
    """Check if *ip* is a known sinkhole address. SM-042: also matches the
    full 0.0.0.0/8 block (any 0.x.y.z is a sinkhole signal).
    B321: also matches 10.0.0.0/8, 172.16.0.0/12, 192.0.2.0/24, 198.51.100.0/24,
    and 203.0.113.0/24 — ISP and RFC 5737 ranges used for DNS sinkholing."""
    if ip in _SINKHOLE_IPS:
        return True
    # 0.0.0.0/8 — any address in this block is a sinkhole signal
    if ip.startswith("0."):
        return True
    # B321: 10.0.0.0/8 — ISP private range commonly used for sinkholing
    if ip.startswith("10."):
        return True
    # B321: 172.16.0.0/12 — ISP private range (172.16.x.x through 172.31.x.x)
    if ip.startswith("172."):
        try:
            second_octet = int(ip.split(".")[1])
            if 16 <= second_octet <= 31:
                return True
        except (IndexError, ValueError):
            pass
    # B321: 192.0.2.0/24 (TEST-NET-1), 198.51.100.0/24 (TEST-NET-2), 203.0.113.0/24 (TEST-NET-3)
    if ip.startswith("192.0.2.") or ip.startswith("198.51.100.") or ip.startswith("203.0.113."):
        return True
    return False

_TEST_DOMAINS = [
    # Malware test domains (blocked by security filters)
    {"domain": "eicar.org", "category": "malware", "expected": "blocked"},
    {"domain": "malware.wicar.org", "category": "malware", "expected": "blocked"},
    # Ad tracking (blocked by ad filters)
    {"domain": "doubleclick.net", "category": "ads", "expected": "blocked"},
    {"domain": "adservice.google.com", "category": "ads", "expected": "blocked"},
    # Privacy trackers (blocked by privacy filters)
    {"domain": "track.hubspot.com", "category": "tracker", "expected": "blocked"},
    {"domain": "pixel.facebook.com", "category": "tracker", "expected": "blocked"},
    # CNAME-cloaked trackers (blocked by advanced filters)
    {"domain": "zdtag.com", "category": "cname-cloak", "expected": "blocked"},
    # Known clean domains (should always resolve)
    {"domain": "example.com", "category": "clean", "expected": "resolve"},
    {"domain": "google.com", "category": "clean", "expected": "resolve"},
    {"domain": "cloudflare.com", "category": "clean", "expected": "resolve"},
]


def _ip_is_public(ip):
    """True only for publicly-routable unicast IPs. Prevents SSRF. Delegates to the
    shared netutil gate (adds IPv4-mapped IPv6 normalization the local copy lacked)."""
    from . import netutil

    return netutil.ip_is_public(ip)


def _query_resolver(domain, server, timeout):
    """Query A+AAAA records for domain via server. Returns (status, addresses).

    SM-044: query both A and AAAA — a resolver that filters only IPv4 or only
    IPv6 would escape detection with A-only queries.

    status is one of: "resolved", "nxdomain", "refused", "error", "nodata".
    addresses is a list of IP strings (empty when not resolved).

    A053: the A and AAAA queries are independent — run them concurrently so a
    slow IPv4 query does not stall the IPv6 query (and vice versa).
    """
    def _query_one(qtype):
        """Query a single record type (A or AAAA) via *server*, returning None on failure."""
        try:
            return resolver.query(domain, qtype, server, timeout=timeout)
        except Exception as exc:
            log.debug("query %s/%s via %s failed: %s", domain, "A" if qtype == resolver.TYPE_A else "AAAA", server, exc)
            return None

    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as _ex:
        _futures = {qtype: _ex.submit(_query_one, qtype) for qtype in (resolver.TYPE_A, resolver.TYPE_AAAA)}
        _responses = {qtype: _futures[qtype].result() for qtype in (resolver.TYPE_A, resolver.TYPE_AAAA)}

    all_addrs = []
    last_status = "error"
    for qtype in (resolver.TYPE_A, resolver.TYPE_AAAA):
        resp = _responses[qtype]
        if resp is None:
            continue

        rcode = resp.get("rcode", -1)
        if rcode == 3:
            last_status = "nxdomain"
            continue
        if rcode == 5:
            last_status = "refused"
            continue
        if rcode != 0:
            last_status = "error"
            continue
        type_name = "A" if qtype == resolver.TYPE_A else "AAAA"
        addrs = [r["value"] for r in resp.get("records", []) if r.get("type") == type_name and isinstance(r.get("value"), str)]
        if addrs:
            all_addrs.extend(addrs)

    if all_addrs:
        return "resolved", all_addrs
    if last_status == "nxdomain":
        return "nxdomain", []
    if last_status == "refused":
        return "refused", []
    if last_status == "error":
        return "error", []
    # NOERROR with no records — A159 nodata signal.
    return "nodata", []


def _is_filtered(user_status, user_addrs, clean_status, clean_addrs):
    """Determine whether the user resolver is filtering a domain.

    Returns True if the user resolver blocks/sinkholes a domain that
    resolves cleanly via the clean vantages.
    """
    # If clean vantages also fail, we cannot judge.
    if clean_status != "resolved":
        return False

    # NXDOMAIN on user side but resolves on clean = filtered.
    if user_status == "nxdomain":
        return True

    # REFUSED on user side but resolves on clean = filtered.
    if user_status == "refused":
        return True

    # A159: NODATA (NOERROR with no A records) on user side but resolves on
    # clean = filtered (Pi-hole/AdGuard NODATA mode).
    if user_status == "nodata":
        return True

    # Resolved on user side — check for sinkhole IPs (SM-042: expanded check).
    if user_status == "resolved" and user_addrs:
        if all(_ip_is_sinkhole(a) for a in user_addrs):
            return True

    return False


import time as _time  # noqa: E402 — import placed after runtime setup above it, deliberately

# A162: aggregate wall-clock deadline for the entire test run. 10 domains × 3
# queries × 5s = 150s without a cap; this default is generous but bounded.
_TEST_DEADLINE_SECONDS = 60


def _query_clean_reference(domain, timeout):
    """Query all clean vantages concurrently for a domain.

    Returns (clean_status, clean_addrs, had_reference). had_reference is True
    when at least one vantage resolved the domain successfully.
    """
    def _query_clean_vantage(vantage):
        """Query one clean vantage for *domain*; delegates to ``_query_resolver``."""
        return _query_resolver(domain, vantage, timeout)

    with concurrent.futures.ThreadPoolExecutor(max_workers=len(_CLEAN_VANTAGES)) as _ex:
        _vantage_results = list(_ex.map(_query_clean_vantage, _CLEAN_VANTAGES))

    for cs, ca in _vantage_results:
        if cs == "resolved":
            return cs, ca, True
    return "error", [], False


def _test_one_domain(entry, resolver_ip, timeout):
    """Query one domain via user resolver and clean vantages.

    Returns (result_dict, filtered, user_status, had_clean_ref, clean_ok).
    clean_ok is False when a clean-control domain fails to resolve via the user.
    """
    domain = entry["domain"]
    category = entry["category"]
    expected = entry["expected"]

    user_status, user_addrs = _query_resolver(domain, resolver_ip, timeout)
    clean_status, clean_addrs, had_ref = _query_clean_reference(domain, timeout)
    filtered = _is_filtered(user_status, user_addrs, clean_status, clean_addrs)
    clean_ok = not (expected == "resolve" and user_status != "resolved")

    result = {
        "domain": domain,
        "category": category,
        "userConclusion": user_status,
        "cleanConclusion": clean_status,
        "filtered": filtered,
    }
    return result, filtered, user_status, had_ref, clean_ok


def _compute_assessment(tested, filtered_count, inconclusive_count, clean_reference_count, clean_domains_ok, deadline):
    """Compute the overall firewall test assessment.

    Returns one of "filtered", "open", or "inconclusive".
    """
    if filtered_count >= tested * 0.3:
        return "filtered"
    if clean_reference_count == 0:
        # No reference read succeeded — "no filtering detected" would be an
        # artifact of having measured nothing. Missing data is not clean data.
        return "inconclusive"
    if clean_domains_ok and filtered_count == 0 and inconclusive_count <= tested * 0.2:
        # A159: "open" only when inconclusive measurements ≤ 20% of tested.
        return "open"
    return "inconclusive"


def test(resolver_ip, timeout=5):
    """Test whether a DNS resolver is filtering domains.

    Args:
        resolver_ip: The resolver IP to test.
        timeout: Per-query timeout in seconds (default 5).

    Returns:
        dict with keys: resolver, results, summary, assessment.
        assessment is one of "filtered", "open", or "inconclusive".

    A162: enforces an aggregate wall-clock deadline so the test cannot pin a
    handler thread for minutes when domains are slow to respond.
    """
    if not _ip_is_public(resolver_ip):
        return {
            "resolver": resolver_ip,
            "results": [],
            "summary": {"tested": 0, "filtered": 0, "clean": 0, "inconclusive": 0},
            "assessment": "inconclusive",
            "error": "resolver IP is not a public address",
        }

    results = []
    filtered_count = 0
    clean_count = 0
    inconclusive_count = 0
    clean_domains_ok = True
    clean_reference_count = 0
    deadline = _time.monotonic() + _TEST_DEADLINE_SECONDS

    # E262: Randomize test domain order to reduce fingerprint.
    test_domains = list(_TEST_DOMAINS)
    secrets.SystemRandom().shuffle(test_domains)

    for entry in test_domains:
        if _time.monotonic() > deadline:
            break
        result, filtered, user_status, had_ref, clean_ok = _test_one_domain(entry, resolver_ip, timeout)
        results.append(result)
        if had_ref:
            clean_reference_count += 1
        if filtered:
            filtered_count += 1
        elif user_status in ("error", "nodata"):
            inconclusive_count += 1
        else:
            clean_count += 1
        if not clean_ok:
            clean_domains_ok = False

    tested = len(results)
    assessment = _compute_assessment(tested, filtered_count, inconclusive_count, clean_reference_count, clean_domains_ok, deadline)

    return {
        "resolver": resolver_ip,
        "results": results,
        "summary": {
            "tested": tested,
            "filtered": filtered_count,
            "clean": clean_count,
            "inconclusive": inconclusive_count,
            "deadline_truncated": _time.monotonic() > deadline,
        },
        "assessment": assessment,
    }

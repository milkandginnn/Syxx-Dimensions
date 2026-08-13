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

import logging

from . import resolver

log = logging.getLogger(__name__)

# Clean vantages assumed to be unfiltered.
_CLEAN_VANTAGES = ("1.1.1.1", "8.8.8.8")
# Sinkhole addresses that signal local filtering.
_SINKHOLE_IPS = frozenset({"0.0.0.0", "127.0.0.1", "::1", "10.0.0.1"})

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
    """Query A record for domain via server. Returns (status, addresses).

    status is one of: "resolved", "nxdomain", "refused", "error".
    addresses is a list of IP strings (empty when not resolved).
    """
    try:
        resp = resolver.query(domain, resolver.TYPE_A, server, timeout=timeout)
    except Exception as exc:
        log.debug("query %s via %s failed: %s", domain, server, exc)
        return "error", []

    rcode = resp.get("rcode", -1)
    if rcode == 3:
        return "nxdomain", []
    if rcode == 5:
        return "refused", []
    if rcode != 0:
        return "error", []
    addrs = [r["value"] for r in resp.get("records", [])
             if r.get("type") == "A" and isinstance(r.get("value"), str)]
    if not addrs:
        # A159: NOERROR with no A records is "nodata" — a filtering signal
        # (e.g. Pi-hole/AdGuard NODATA mode), not a generic error.
        return "nodata", []
    return "resolved", addrs


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

    # Resolved on user side — check for sinkhole IPs.
    if user_status == "resolved" and user_addrs:
        if all(a in _SINKHOLE_IPS for a in user_addrs):
            return True

    return False


import time as _time


# A162: aggregate wall-clock deadline for the entire test run. 10 domains × 3
# queries × 5s = 150s without a cap; this default is generous but bounded.
_TEST_DEADLINE_SECONDS = 60


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
    # How many domains we obtained a usable CLEAN-VANTAGE reference for. Without at least one,
    # `_is_filtered` can never return True (it bails when clean_status != "resolved"), so
    # filtered_count is structurally 0 and the run would report "open" — i.e. "not filtering" —
    # precisely on a network that blocks the reference resolvers. That is the filtering case
    # this test exists to catch, so zero references must be inconclusive, never "open".
    clean_reference_count = 0
    # A162: aggregate wall-clock deadline for the entire test
    deadline = _time.monotonic() + _TEST_DEADLINE_SECONDS

    for entry in _TEST_DOMAINS:
        # A162: check deadline before each domain to prevent unbounded runtime
        if _time.monotonic() > deadline:
            break

        domain = entry["domain"]
        category = entry["category"]
        expected = entry["expected"]

        user_status, user_addrs = _query_resolver(domain, resolver_ip, timeout)

        # Query clean vantages and take the first successful result.
        clean_status, clean_addrs = "error", []
        for vantage in _CLEAN_VANTAGES:
            cs, ca = _query_resolver(domain, vantage, timeout)
            if cs == "resolved":
                clean_status, clean_addrs = cs, ca
                clean_reference_count += 1
                break

        filtered = _is_filtered(user_status, user_addrs, clean_status, clean_addrs)

        if filtered:
            filtered_count += 1
        elif user_status in ("error", "nodata"):
            # A159: "nodata" (NOERROR + no A records) that isn't flagged as
            # filtered (e.g. clean vantage also failed) is inconclusive.
            inconclusive_count += 1
        else:
            clean_count += 1

        # Track whether clean-control domains resolve via the user resolver.
        if expected == "resolve" and user_status != "resolved":
            clean_domains_ok = False

        results.append({
            "domain": domain,
            "category": category,
            "userConclusion": user_status,
            "cleanConclusion": clean_status,
            "filtered": filtered,
        })

    tested = len(_TEST_DOMAINS)

    # Assessment logic.
    if filtered_count >= tested * 0.3:
        assessment = "filtered"
    elif clean_reference_count == 0:
        # No reference read succeeded, so "no filtering detected" would be an artifact of having
        # measured nothing. Missing data is not clean data.
        assessment = "inconclusive"
    elif clean_domains_ok and filtered_count == 0 and inconclusive_count <= tested * 0.2:
        # A159: require evidence floor — "open" only when inconclusive measurements
        # are at most 20% of tested domains. Missing data must not render as clean.
        assessment = "open"
    else:
        assessment = "inconclusive"

    return {
        "resolver": resolver_ip,
        "results": results,
        "summary": {
            "tested": tested,
            "filtered": filtered_count,
            "clean": clean_count,
            "inconclusive": inconclusive_count,
        },
        "assessment": assessment,
    }

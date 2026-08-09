"""Lookalike / homoglyph domain permutation scanner.

Generates common typosquatting and homoglyph variants of a target domain,
then checks which ones resolve via DNS to flag potential phishing or brand
impersonation targets.
"""

import string
import time

from . import config, resolver

# Common homoglyph substitutions (single char or short sequence → replacement).
HOMOGLYPH_MAP = {
    "o": "0",
    "0": "o",
    "l": "1",
    "i": "1",
    "1": "l",  # "1"→"i" also valid but dict cannot hold both; "l" is the stronger confusion vector
    "s": "5",
    "5": "s",
    "e": "3",
    "3": "e",
    "a": "4",
    "4": "a",
    "b": "d",
    "d": "b",
    "m": "rn",
    "rn": "m",
    "u": "v",
    "v": "u",
    "w": "vv",
    "vv": "w",
}

# Common TLD alternatives for .com substitutions.
TLD_SUBSTITUTES = ["net", "org", "io", "co", "info", "biz", "xyz", "cc"]


def _split_domain(domain):
    """Return (label, tld) for a domain, handling multi-part TLDs.

    SM-034: the previous rsplit(".", 1) treated "example.co.uk" as label="co",
    tld="uk" — generating permutations of "co" instead of "example". Check the
    two-part TLD set first.
    """
    labels = domain.rstrip(".").lower().split(".")
    if len(labels) < 2:
        return domain, ""
    # Check for known multi-part TLDs
    two_part = ".".join(labels[-2:])
    if two_part in _MULTI_PART_TLDS:
        if len(labels) >= 3:
            label = labels[-3]
            tld = two_part
            # If there are more labels (sub.domain.co.uk), include them in label
            if len(labels) > 3:
                label = ".".join(labels[:-2])
            return label, tld
        return labels[0] if labels else "", two_part
    return ".".join(labels[:-1]), labels[-1]


# A306: import the canonical multi-part TLD set from whois_lookup instead of
# maintaining a divergent local copy. The whois_lookup module has the
# authoritative, expanded list (SM-035).
from .whois_lookup import _TWO_PART_TLDS as _MULTI_PART_TLDS  # noqa: E402


def _omit(label):
    """Yield variants with one character removed at each position."""
    for i in range(len(label)):
        v = label[:i] + label[i + 1 :]
        if v:
            yield v, "omission"


def _substitute(label):
    """Yield variants with single-character homoglyph substitutions."""
    seen = set()
    for i, ch in enumerate(label):
        repl = HOMOGLYPH_MAP.get(ch)
        if repl and repl != ch:
            v = label[:i] + repl + label[i + 1 :]
            if v not in seen:
                seen.add(v)
                yield v, "substitution"


def _insert(label):
    """Yield variants with one adjacent character duplicated.

    SM-033: deduplicate — when two adjacent characters are the same (e.g. "oo"),
    doubling either produces the same string. Use a seen set.
    """
    seen = set()
    for i, ch in enumerate(label):
        if ch in string.ascii_letters:
            v = label[:i] + ch + ch + label[i + 1 :]
            if v not in seen:
                seen.add(v)
                yield v, "insertion"


def _transpose(label):
    """Yield variants with adjacent characters swapped."""
    seen = set()
    for i in range(len(label) - 1):
        v = label[:i] + label[i + 1] + label[i] + label[i + 2 :]
        if v != label and v not in seen:
            seen.add(v)
            yield v, "transposition"


def _hyphenate(label):
    """Yield variants with hyphens added or removed."""
    if "-" in label:
        v = label.replace("-", "")
        if v:
            yield v, "hyphen-removal"
    else:
        for i in range(1, len(label)):
            v = label[:i] + "-" + label[i:]
            yield v, "hyphen-insertion"


def generate_permutations(domain, max_permutations=200):
    """Generate up to max_permutations lookalike variants of domain."""
    label, tld = _split_domain(domain)
    if not label:
        return []

    seen = set()
    results = []

    def _add(fqdn, vtype):
        if fqdn not in seen and fqdn != domain and len(results) < max_permutations:
            seen.add(fqdn)
            results.append((fqdn, vtype))

    # Label-level permutations (same TLD).
    for generator in (_omit, _substitute, _insert, _transpose, _hyphenate):
        for variant, vtype in generator(label):
            fqdn = variant + "." + tld if tld else variant
            _add(fqdn, vtype)

    # TLD substitutions (same label).
    if tld:
        for alt_tld in TLD_SUBSTITUTES:
            if alt_tld != tld:
                _add(f"{label}.{alt_tld}", "tld-substitution")

    return results


def _check_resolution(fqdn, server, timeout):
    """Query A records for fqdn. Returns (resolves, ips) tuple."""
    try:
        resp = resolver.query(fqdn, resolver.TYPE_A, server, timeout=timeout)
    except resolver.DNSError:
        return False, []
    if resp["rcode"] == 3:
        return False, []
    ips = [r["value"] for r in resp.get("records", []) if r.get("type") == "A"]
    return bool(ips), ips


# A274: aggregate scan deadline constant. The previous default of 10s was too
# short for domains with many permutations; 60s gives adequate headroom while
# still bounding the worst case.
_SCAN_DEADLINE_SECONDS = 60

# A274: fallback resolvers when PUBLIC_RESOLVERS is empty.
_FALLBACK_RESOLVERS = ["8.8.8.8", "1.1.1.1", "9.9.9.9"]


def scan(domain, max_permutations=200, timeout=None):
    """Scan for resolving lookalike domains.

    Returns a dict with the original domain, a list of permutation results,
    and a summary with total/resolving/registered counts.

    SM-030: enforces an aggregate wall-clock deadline so the scan cannot pin
    a handler thread for minutes when many permutations are slow to resolve.
    SM-031: uses multiple PUBLIC_RESOLVERS instead of just the first.
    A274: uses _SCAN_DEADLINE_SECONDS as default; parallelizes DNS resolution
    with ThreadPoolExecutor when there are many permutations.
    A287: falls back to _FALLBACK_RESOLVERS when PUBLIC_RESOLVERS is empty.
    """
    if timeout is None:
        timeout = _SCAN_DEADLINE_SECONDS
    domain = domain.lower().strip()
    # A287: try multiple resolvers with fallback.
    servers = config.PUBLIC_RESOLVERS[:3] if config.PUBLIC_RESOLVERS else _FALLBACK_RESOLVERS

    permutations = generate_permutations(domain, max_permutations)
    results = []
    resolving_count = 0
    # SM-030 / A274: aggregate wall-clock deadline.
    deadline = time.monotonic() + timeout

    # A274: for large permutation sets, parallelize DNS lookups with a thread
    # pool to reduce wall-clock time while still respecting the deadline.
    _PARALLEL_THRESHOLD = 50  # only parallelize when there are many variants
    per_query_timeout = min(timeout, 3)

    if len(permutations) >= _PARALLEL_THRESHOLD:
        from concurrent.futures import ThreadPoolExecutor, as_completed

        with ThreadPoolExecutor(max_workers=min(8, len(servers) * 2)) as pool:
            future_map = {}
            for fqdn, vtype in permutations:
                if time.monotonic() > deadline:
                    break
                future_map[pool.submit(_check_resolution_multi, fqdn, servers, per_query_timeout, deadline)] = (
                    fqdn,
                    vtype,
                )
            try:
                remaining = max(0.0, deadline - time.monotonic())
                for fut in as_completed(future_map, timeout=remaining):
                    fqdn, vtype = future_map[fut]
                    try:
                        resolves, ips = fut.result()
                    except Exception:
                        resolves, ips = False, []
                    if resolves:
                        resolving_count += 1
                    results.append(
                        {"variant": fqdn, "type": vtype, "resolves": resolves, "ips": ips}
                    )
            except Exception:  # noqa: S110 — collect what we have so far; timeout is expected
                # Timeout or other error — collect what we have so far.
                pass
    else:
        for fqdn, vtype in permutations:
            if time.monotonic() > deadline:
                break
            resolves, ips = _check_resolution_multi(fqdn, servers, per_query_timeout, deadline)
            if resolves:
                resolving_count += 1
            results.append(
                {"variant": fqdn, "type": vtype, "resolves": resolves, "ips": ips}
            )

    return {
        "domain": domain,
        "permutations": results,
        "summary": {
            "total": len(results),
            "resolving": resolving_count,
            "registered": resolving_count,
            # SM-030: report deadline truncation.
            "deadline_truncated": len(results) < len(permutations),
        },
    }


def _check_resolution_multi(fqdn, servers, per_query_timeout, deadline):
    """A287: check resolution across multiple servers with fallback.

    Tries each server in order; returns on the first that resolves.
    Respects the aggregate deadline.
    """
    for server in servers:
        if time.monotonic() > deadline:
            break
        resolves, ips = _check_resolution(fqdn, server, per_query_timeout)
        if resolves:
            return True, ips
    return False, []

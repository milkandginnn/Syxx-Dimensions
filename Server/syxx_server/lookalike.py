"""Lookalike / homoglyph domain permutation scanner.

Generates common typosquatting and homoglyph variants of a target domain,
then checks which ones resolve via DNS to flag potential phishing or brand
impersonation targets.
"""

import string

from . import config, resolver

# Common homoglyph substitutions (single char or short sequence → replacement).
HOMOGLYPH_MAP = {
    "o": "0",
    "0": "o",
    "l": "1",
    "1": "l",
    "i": "1",
    "1": "i",
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
    """Return (label, tld) for a simple domain like 'example.com'."""
    parts = domain.rsplit(".", 1)
    if len(parts) != 2 or not parts[0] or not parts[1]:
        return domain, ""
    return parts[0], parts[1]


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
    """Yield variants with one adjacent character duplicated."""
    for i, ch in enumerate(label):
        if ch in string.ascii_letters:
            v = label[:i] + ch + ch + label[i + 1 :]
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


def scan(domain, max_permutations=200, timeout=10):
    """Scan for resolving lookalike domains.

    Returns a dict with the original domain, a list of permutation results,
    and a summary with total/resolving/registered counts.
    """
    domain = domain.lower().strip()
    server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "8.8.8.8"

    permutations = generate_permutations(domain, max_permutations)
    results = []
    resolving_count = 0

    for fqdn, vtype in permutations:
        resolves, ips = _check_resolution(fqdn, server, timeout)
        if resolves:
            resolving_count += 1
        results.append(
            {
                "variant": fqdn,
                "type": vtype,
                "resolves": resolves,
                "ips": ips,
            }
        )

    return {
        "domain": domain,
        "permutations": results,
        "summary": {
            "total": len(results),
            "resolving": resolving_count,
            "registered": resolving_count,
        },
    }

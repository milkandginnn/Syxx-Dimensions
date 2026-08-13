"""Email authentication record checker (SPF, DMARC, DKIM).

Queries DNS TXT records via the existing resolver module and parses
results into structured results. All queries go through PUBLIC_RESOLVERS
only (SSRF-safe). DNS failures produce "inconclusive", never crash.
"""

from . import resolver, config

_FALLBACK_SELECTORS = ["google", "default", "selector1", "k1", "s1"]


def check(domain, dkim_selector=None, timeout=10):
    """Check SPF, DMARC, and optionally DKIM for *domain*.

    Returns ``{domain, spf: {record, policy, conclusion},
    dmarc: {record, policy, pct, conclusion},
    dkim: {selector, record, conclusion},
    overall: "pass" | "fail" | "partial"}``.
    """
    server = config.PUBLIC_RESOLVERS[0]
    spf = _check_spf(domain, server, timeout)
    dmarc = _check_dmarc(domain, server, timeout)
    dkim = _check_dkim(domain, dkim_selector, server, timeout)
    overall = _overall(spf["conclusion"], dmarc["conclusion"], dkim["conclusion"])
    return {"domain": domain, "spf": spf, "dmarc": dmarc,
            "dkim": dkim, "overall": overall}


# -- SPF --------------------------------------------------------------------

def _check_spf(domain, server, timeout):
    """Query TXT at *domain* and extract the SPF policy."""
    try:
        result = resolver.query(domain, resolver.TYPE_TXT, server, timeout=timeout)
    except resolver.DNSError:
        return {"record": None, "policy": None, "conclusion": "inconclusive"}
    spf_record = None
    for rec in result.get("records", []):
        val = rec.get("value", "")
        if val.lower().startswith("v=spf1"):
            spf_record = val
            break
    if spf_record is None:
        return {"record": None, "policy": None, "conclusion": "missing"}
    policy = _extract_spf_all(spf_record)
    return {"record": spf_record, "policy": policy,
            "conclusion": _spf_result(policy)}


def _extract_spf_all(record):
    """Return the trailing ``all`` mechanism or *None*."""
    for part in reversed(record.split()):
        if part.lower().endswith("all"):
            return part
    return None


def _spf_result(policy):
    """+all -> fail, ~all -> softfail, -all -> pass, ?all/bare -> neutral."""
    if policy is None:
        return "neutral"
    prefix = policy[0] if policy and policy[0] in "+-~?" else ""
    if prefix == "+":
        return "fail"
    if prefix == "-":
        return "pass"
    if prefix == "~":
        return "softfail"
    if prefix == "?":
        return "neutral"
    # Bare "all" with no qualifier is equivalent to +all (RFC 7208 §4.7).
    if policy.lower() == "all":
        return "fail"
    return "neutral"


# -- DMARC ------------------------------------------------------------------

def _check_dmarc(domain, server, timeout):
    """Query TXT at ``_dmarc.<domain>`` and extract the DMARC policy."""
    try:
        result = resolver.query("_dmarc." + domain, resolver.TYPE_TXT,
                                server, timeout=timeout)
    except resolver.DNSError:
        return {"record": None, "policy": None, "pct": None,
                "conclusion": "inconclusive"}
    dmarc_record = None
    for rec in result.get("records", []):
        val = rec.get("value", "")
        if val.lower().startswith("v=dmarc1"):
            dmarc_record = val
            break
    if dmarc_record is None:
        return {"record": None, "policy": None, "pct": None,
                "conclusion": "missing"}
    tags = _parse_tags(dmarc_record)
    return {"record": dmarc_record, "policy": tags.get("p"),
            "pct": tags.get("pct"),
            "conclusion": _dmarc_result(tags.get("p"), tags.get("rua"))}


def _parse_tags(record):
    """Parse ``;``-separated tags (DMARC or DKIM) into a dict."""
    tags = {}
    for part in record.split(";"):
        part = part.strip()
        if "=" not in part:
            continue
        key, _, value = part.partition("=")
        tags[key.strip().lower()] = value.strip()
    return tags


def _dmarc_result(policy, rua):
    """Evaluate DMARC: p=reject+rua -> pass, p=none-rua -> fail, etc."""
    if policy is None:
        return "missing"
    p = policy.lower()
    has_rua = rua is not None and rua != ""
    if p in ("reject", "quarantine"):
        return "pass" if has_rua else "partial"
    if p == "none":
        return "neutral" if has_rua else "fail"
    return "inconclusive"


# -- DKIM -------------------------------------------------------------------

def _check_dkim(domain, selector, server, timeout):
    """Query DKIM TXT records; tries fallback selectors when none given."""
    selectors = [selector] if selector else _FALLBACK_SELECTORS
    for sel in selectors:
        name = sel + "._domainkey." + domain
        try:
            result = resolver.query(name, resolver.TYPE_TXT, server,
                                    timeout=timeout)
        except resolver.DNSError:
            continue
        for rec in result.get("records", []):
            val = rec.get("value", "")
            if "p=" in val.lower():
                return {"selector": sel, "record": val,
                        "conclusion": _dkim_result(val)}
    return {"selector": selector, "record": None, "conclusion": "missing"}


def _dkim_result(record):
    """Valid key (p= with data) -> pass, revoked (p= empty) -> fail."""
    tags = _parse_tags(record)
    p = tags.get("p")
    if p is None:
        return "fail"
    # RFC 8301 §3.1: empty p= means the key has been revoked.
    return "fail" if p == "" else "pass"


# -- Overall ----------------------------------------------------------------

def _overall(spf_v, dmarc_v, dkim_v):
    """Combine per-protocol results.

    "fail" if any hard fail; "pass" if all are pass/softfail/neutral/partial;
    otherwise "partial".
    """
    results = [spf_v, dmarc_v, dkim_v]
    if any(v == "fail" for v in results):
        return "fail"
    if all(v in ("pass", "softfail", "neutral", "partial") for v in results):
        return "pass"
    return "partial"

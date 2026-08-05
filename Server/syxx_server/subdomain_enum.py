"""Subdomain enumeration via Certificate Transparency logs.

Queries crt.sh to passively enumerate known subdomains for a domain.
No brute-force, no zone transfers, no aggressive scanning.

SSRF-safe: only queries the hardcoded crt.sh host. DNS resolution
uses only PUBLIC_RESOLVERS from config.
"""

import concurrent.futures
import http.client
import json
import os
import re
import ssl
import time
import urllib.parse

from . import config, resolver

_CRTSH_HOST = "crt.sh"
_CRTSH_PATH_TPL = "/?q=%.{domain}&output=json"
_MAX_SUBDOMAINS = 200
_RESOLVE_TIMEOUT = 3
_WILDCARD_RE = re.compile(r"^\*\.")

# SD-109: cap concurrent DNS resolution workers so a single /api/subdomains
# request cannot exhaust the server's thread pool. Also enforces an overall
# wall-clock deadline so the route cannot pin a handler thread for ~10 minutes.
_MAX_RESOLVE_WORKERS = max(1, int(os.environ.get("SYXX_SUBDOMAIN_RESOLVE_WORKERS", "10")))
_ENUMERATE_DEADLINE = max(1, int(os.environ.get("SYXX_SUBDOMAIN_ENUMERATE_DEADLINE", "30")))


class _CrtshError(Exception):
    """crt.sh unreachable, rate-limited, or returned bad data."""


def enumerate(domain, timeout=15, do_resolve=True):
    """Enumerate subdomains for *domain* using CT logs.

    Returns dict with keys: domain, subdomains (list of {name, resolves, ips}),
    total, source, truncated, resolveTime_ms.

    SD-109: DNS resolution is parallelized with a bounded ThreadPoolExecutor
    and an overall wall-clock deadline so a single request cannot pin a handler
    thread for ~10 minutes (200 subdomains × 3s timeout = 600s worst case).
    """
    domain = domain.strip().lower().rstrip(".")
    truncated = False
    raw_names = []
    try:
        raw_names = _query_crtsh(domain, timeout)
    except _CrtshError:
        truncated = True

    subdomains = _extract_subdomains(raw_names, domain)
    if len(subdomains) > _MAX_SUBDOMAINS:
        subdomains = subdomains[:_MAX_SUBDOMAINS]
        truncated = True

    deadline = time.monotonic() + _ENUMERATE_DEADLINE
    t0 = time.monotonic()
    resolve_budget_exhausted = False
    if do_resolve:
        resolved, resolve_budget_exhausted = _resolve_subdomains(subdomains, deadline)
    else:
        resolved = dict.fromkeys(subdomains)

    results = []
    for name in subdomains:
        entry = resolved.get(name)
        if entry is None and do_resolve:
            # A166: name was not queried (deadline-skipped or as_completed timeout) —
            # report as null, not False. Missing data is not a measured negative.
            results.append({"name": name, "resolves": None, "ips": []})
        elif entry is None:
            results.append({"name": name, "resolves": None, "ips": []})
        elif entry:
            results.append({"name": name, "resolves": True, "ips": entry})
        else:
            # A166: queried but no records found — a genuine negative
            results.append({"name": name, "resolves": False, "ips": []})

    # A166: set truncated when the resolution budget was exhausted, so the caller
    # can see that some names may not have been queried.
    if resolve_budget_exhausted:
        truncated = True

    return {
        "domain": domain,
        "subdomains": results,
        "total": len(results),
        "source": "crt.sh",
        "truncated": truncated,
        "resolveTime_ms": int((time.monotonic() - t0) * 1000),
    }


_CRTSH_MAX_BODY = 10 * 1024 * 1024  # A165: 10 MiB cap for crt.sh response body


def _query_crtsh(domain, timeout):
    """Query crt.sh CT log API. Returns list of cert entry dicts or raises _CrtshError.

    A165: read the response body BEFORE closing the connection. The previous
    code closed the connection in a finally block before reading the body; on
    HTTP/1.1 keep-alive servers (crt.sh is one), conn.close() closes the
    HTTPResponse too, so resp.read() returned b"" and json.loads always failed.
    """
    path = _CRTSH_PATH_TPL.format(domain=urllib.parse.quote(domain, safe=""))
    ctx = ssl.create_default_context()
    conn = http.client.HTTPSConnection(_CRTSH_HOST, timeout=timeout, context=ctx)
    try:
        conn.request("GET", path, headers={"Accept": "application/json", "User-Agent": "syxx-server/1.0"})
        resp = conn.getresponse()

        if resp.status == 429:
            raise _CrtshError("rate-limited (429)")
        if resp.status == 503:
            raise _CrtshError("service unavailable (503)")
        if resp.status != 200:
            raise _CrtshError("unexpected status %d" % resp.status)

        # A165: read body BEFORE closing, with an explicit byte cap
        body = resp.read(_CRTSH_MAX_BODY + 1)
        if len(body) > _CRTSH_MAX_BODY:
            raise _CrtshError("response body exceeds %d bytes" % _CRTSH_MAX_BODY)
    except _CrtshError:
        raise
    except Exception as exc:
        raise _CrtshError(f"connection failed: {exc}")
    finally:
        conn.close()

    try:
        entries = json.loads(body.decode("utf-8"))
    except Exception as exc:
        raise _CrtshError(f"invalid response: {exc}")
    if not isinstance(entries, list):
        raise _CrtshError("response is not a JSON array")
    return entries


def _extract_subdomains(entries, domain):
    """Pull unique, non-wildcard subdomain names from crt.sh entries.

    Collects common_name and name_value (newline-separated SANs) from each
    entry, keeps only names that are the domain or a subdomain of it, strips
    wildcard prefixes, and returns a sorted deduplicated list.
    """
    seen = set()
    suffix = "." + domain
    for entry in entries:
        names = []
        cn = entry.get("common_name") or ""
        if cn:
            names.append(cn)
        nv = entry.get("name_value") or ""
        if nv:
            names.extend(nv.split("\n"))
        for raw in names:
            name = raw.strip().lower().rstrip(".")
            if not name:
                continue
            name = _WILDCARD_RE.sub("", name)
            if not name:
                continue
            if name == domain or name.endswith(suffix):
                seen.add(name)
    return sorted(seen)


def _resolve_subdomains(names, deadline):
    """Resolve subdomain names via PUBLIC_RESOLVERS in parallel with a bounded
    worker pool and an overall wall-clock deadline (SD-109).

    A166: returns ({name: ips_or_empty}, budget_exhausted). Names that were
    actually queried map to a list (possibly empty). Names skipped because the
    deadline fired before they were attempted are ABSENT from the dict, so the
    caller can distinguish "queried and empty" from "never queried" (honesty
    spine #2: missing data must not render as a measured negative).
    """
    result = {}
    budget_exhausted = False
    resolvers = config.PUBLIC_RESOLVERS or ["1.1.1.1"]

    def resolve_one(name):
        if deadline is not None and time.monotonic() > deadline:
            return name, None
        ips = _resolve_one_sequential(name, resolvers)
        return name, ips

    with concurrent.futures.ThreadPoolExecutor(max_workers=_MAX_RESOLVE_WORKERS) as pool:
        futures = {pool.submit(resolve_one, name): name for name in names}
        try:
            remaining = max(0.0, deadline - time.monotonic()) if deadline else None
            for fut in concurrent.futures.as_completed(futures, timeout=remaining):
                try:
                    name, ips = fut.result()
                except Exception:
                    continue
                # A166: store all queried names (including empty results).
                # None means deadline-skipped (absent from dict); [] means queried-and-empty.
                if ips is not None:
                    result[name] = ips
        except concurrent.futures.TimeoutError:
            budget_exhausted = True  # A166: signal that resolution was curtailed

    return result, budget_exhausted


def _resolve_one_sequential(name, resolvers):
    """Try to resolve *name* to A/AAAA records. Returns list of IP strings or [].

    (SD-109: this is the inner sequential resolver; the outer
    _resolve_subdomains fans out across names in parallel.)"""
    for server in resolvers:
        for qtype in (resolver.TYPE_A, resolver.TYPE_AAAA):
            try:
                resp = resolver.query(name, qtype, server, timeout=_RESOLVE_TIMEOUT)
            except Exception:
                continue
            if resp.get("rcode") == 0 and resp.get("records"):
                return [a["value"] for a in resp["records"] if a.get("type") in ("A", "AAAA")]
    return []

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

from . import config, investigate, resolver

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
    # A304: monotonic deadline for the entire query including read.
    # The socket-level timeout protects individual reads, but a server
    # dripping data at just under the timeout can still pin the thread.
    query_deadline = time.monotonic() + timeout
    try:
        conn.request("GET", path, headers={"Accept": "application/json", "User-Agent": "syxx-server/1.0"})
        resp = conn.getresponse()

        if resp.status == 429:
            raise _CrtshError("rate-limited (429)")
        if resp.status == 503:
            raise _CrtshError("service unavailable (503)")
        if resp.status != 200:
            raise _CrtshError(f"unexpected status {resp.status}")

        # A304: chunked read with monotonic deadline. Read in chunks and
        # enforce the deadline between reads so a slow server cannot pin
        # the thread beyond the timeout even if individual reads succeed.
        chunks = []
        total = 0
        _READ_CHUNK_SIZE = 65536  # 64 KiB chunks
        while True:
            if time.monotonic() > query_deadline:
                raise _CrtshError(f"query deadline exceeded ({timeout}s)")
            # Adjust socket timeout to remaining deadline for this read.
            remaining = query_deadline - time.monotonic()
            conn.sock.settimeout(min(remaining, timeout))
            chunk = resp.read(_READ_CHUNK_SIZE)
            if not chunk:
                break
            total += len(chunk)
            if total > _CRTSH_MAX_BODY:
                raise _CrtshError(f"response body exceeds {_CRTSH_MAX_BODY} bytes")
            chunks.append(chunk)
        body = b"".join(chunks)
    except _CrtshError:
        raise
    except Exception as exc:
        raise _CrtshError(f"connection failed: {exc}") from exc
    finally:
        conn.close()

    try:
        entries = json.loads(body.decode("utf-8"))
    except Exception as exc:
        raise _CrtshError(f"invalid response: {exc}") from exc
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
    # SM-028: validate extracted names before including them. crt.sh can return
    # names with invalid characters, excessively long labels, or other malformed
    # data that would crash downstream DNS resolution.
    _VALID_NAME_RE = re.compile(r"^(?=.{1,253}$)([a-zA-Z0-9*_-]{1,63}\.)+[a-zA-Z]{2,63}\.?$")
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
            # SM-028: reject names with invalid characters or excessive length.
            if len(name) > 253:
                continue
            if not _VALID_NAME_RE.match(name + "."):
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
                except Exception:  # noqa: S112 — one enumeration candidate failing must not abort the sweep
                    continue
                # A166: store all queried names (including empty results).
                # None means deadline-skipped (absent from dict); [] means queried-and-empty.
                if ips is not None:
                    result[name] = ips
        except concurrent.futures.TimeoutError:
            budget_exhausted = True  # A166: signal that resolution was curtailed

    return result, budget_exhausted


def _resolve_one_sequential(name, resolvers):
    """Try to resolve *name* to A+AAAA records. Returns list of IP strings or [].

    SM-027: collect IPs from BOTH A and AAAA across all resolvers, not just the
    first successful response. The previous code returned on the first resolver
    that answered, missing AAAA records when A succeeded first (and vice versa).
    A289: collect records from ALL resolvers, not just the first that succeeds.
    The previous early-return on first resolver success missed records visible
    only to other resolvers (e.g. geo-split DNS, anycast differences).
    """
    # A303: validate the domain name before querying DNS.
    if not investigate.valid_domain(name):
        return []

    all_ips = []
    for server in resolvers:
        for qtype in (resolver.TYPE_A, resolver.TYPE_AAAA):
            try:
                resp = resolver.query(name, qtype, server, timeout=_RESOLVE_TIMEOUT)
            except Exception:  # noqa: S112 — one enumeration candidate failing must not abort the sweep
                continue
            if resp.get("rcode") == 0 and resp.get("records"):
                for a in resp["records"]:
                    if a.get("type") in ("A", "AAAA"):
                        ip = a["value"]
                        if ip not in all_ips:
                            all_ips.append(ip)
    # A289: return all collected IPs from all resolvers (removed early return).
    return all_ips

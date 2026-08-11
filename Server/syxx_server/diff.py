"""DNS and content diff helpers for the /api/diff endpoint.

A028: extracted from app.py to reduce its size.  A024 documented that the DNS
comparison (_route_diff / _clean_vantage_answers) and the content comparison
(_diff_content) operate on fundamentally different data types — IP-address sets
vs. script-URL / inline-hash sets — with intentionally different semantics.
They share this module because they serve the same endpoint, not because their
algorithms should be unified.
"""

import concurrent.futures
import hashlib
import time
import urllib.parse

from . import config, fetcher, logging_util, resolver, warp

# A027/A096: aggregate wall-clock budget for the clean-vantage recursive fan-out so
# /api/diff cannot be held open across many slow resolver queries.
_CLEAN_VANTAGE_DEADLINE = max(1, config._i("SYXX_CLEAN_VANTAGE_DEADLINE", 6))

# A136: module-level shared bounded thread pool for DNS fan-out.
_CLEAN_VANTAGE_POOL = concurrent.futures.ThreadPoolExecutor(
    max_workers=6, thread_name_prefix="clean-vantage"
)


def _host(u: str) -> str:
    """Extract the lowercased hostname from a URL, or empty string on failure."""
    try:
        return (urllib.parse.urlsplit(u).hostname or "").lower()
    except ValueError:
        return ""


def _diff_content(url, local_html, local_scripts, local_inline_hashes):
    """Content half of /api/diff (Workstream S #2). Fetch the target from the server's clean vantage
    and compare its script structure to what the client saw. HONESTY: a script loaded from a host the
    clean page never referenced is the strong injection signal; same-host asset churn or inline-script
    differences are treated as soft ("divergent") because dynamic pages legitimately vary between
    vantages (A/B tests, personalization, per-request nonces). SSRF-safe via warp/fetcher (public IPs
    only). Returns a dict; never raises.

    A024 NOTE: This function compares *web-page assets* (script URLs + inline hashes)
    using foreign-host detection — a tri-state of injected/divergent/match.  The DNS
    comparison in _route_diff operates on a completely different data type (IP address
    sets) with subset/disjoint/overlap semantics.  The two are intentionally distinct;
    extracting a shared helper would obscure both."""
    try:
        # A041: pass a monotonic deadline so build_baseline cannot pin this
        # handler with unbounded script fetches.
        base = warp.build_baseline(url, deadline=time.monotonic() + config.WARP_BUILD_DEADLINE)
    except fetcher.TruncatedError:
        return {"url": url, "assessment": "inconclusive", "note": "baseline truncated; diff is inconclusive"}
    except fetcher.FetchError as e:
        # A053: log the full error server-side for diagnostics but return a generic
        # note to the client — str(e) can disclose destination IPs, socket errors,
        # and TLS failure modes that reveal the server's outbound fetch topology.
        logging_util.error("diff_clean_fetch_failed", url=url, err=str(e))
        return {"url": url, "assessment": "inconclusive", "note": "clean vantage unreachable"}

    clean_scripts = set(base.get("scripts", []))
    clean_inline = set(base.get("inlineScriptHashes", []))

    if local_html:
        parsed_scripts, _ss, _if, parsed_inlines = warp.parse_assets(local_html, url)
        local_script_set = set(parsed_scripts)
        local_inline = {hashlib.sha256(b.encode("utf-8")).hexdigest() for b in parsed_inlines}
        have_inline = True
    else:
        local_script_set = set(local_scripts or [])
        local_inline = set(local_inline_hashes or [])
        have_inline = bool(local_inline_hashes)

    if not local_script_set and not local_inline and not local_html:
        return {"url": url, "assessment": "inconclusive", "note": "no local content supplied"}

    injected = sorted(local_script_set - clean_scripts)
    missing = sorted(clean_scripts - local_script_set)
    clean_hosts = {_host(u) for u in clean_scripts}
    injected_hosts = sorted({_host(u) for u in injected} - clean_hosts - {""})
    injected_inline = len(local_inline - clean_inline)

    if injected_hosts:
        assessment = "injected"
    elif injected or injected_inline:
        assessment = "divergent"
    elif local_script_set == clean_scripts and (not have_inline or local_inline == clean_inline):
        assessment = "match"
    else:
        assessment = "divergent"

    note = {
        "injected": "script(s) from a host the clean page never served — strong on-path injection signal",
        "divergent": "asset/inline differences; can be legitimate on dynamic pages — not conclusive",
        "match": "clean-vantage script structure matches what the client saw",
    }[assessment]
    return {
        "url": url,
        "cleanScripts": sorted(clean_scripts),
        "localScripts": sorted(local_script_set),
        "injectedScripts": injected,
        "injectedScriptHosts": injected_hosts,
        "missingScripts": missing,
        "injectedInlineCount": injected_inline,
        "match": assessment == "match",
        "assessment": assessment,
        "note": note,
    }


def _diff_result(dns_assessment, content, clean_answers=None, local_set=None):
    """Fold the DNS and content assessments into one clean|tampered|inconclusive result.

    Only a DNS answer the authority doesn't serve AND the clean vantage confirms, or a
    cross-host script injection, is decisive ('tampered').

    S1: round-robin / geo DNS can make the authoritative answer differ from the local
    answer even when no tampering is occurring. Cross-reference with the clean vantage:
    if the clean vantage ALSO disagrees with the authoritative in the same way, it's
    legitimate round-robin, not tampering. Only when the clean vantage matches the
    authoritative (and the local answer diverges from both) is it 'tampered'.
    """
    c = content.get("assessment") if content else None

    # Cross-host script injection is decisive: the clean vantage never served a
    # script from that host. Same-host churn stays divergent/inconclusive above.
    if c == "injected":
        return "tampered"

    if dns_assessment == "divergent":
        # S1: cross-reference with clean vantage. If the clean vantage also disagrees
        # with the authoritative (i.e. the clean vantage matches the local answer),
        # it's legitimate round-robin / geo DNS, not tampering.
        # NOTE (open spec question, do not "fix" without an owner decision): when
        # `clean_answers` is EMPTY the corroborating vantage was unreachable, not disagreeing,
        # and this falls through to a hard "tampered" on one view. That is deliberate —
        # `test_authoritative_disjoint_answers_remain_tampered` pins it — because the
        # AUTHORITATIVE nameserver is itself the ground truth here and a disjoint local answer
        # contradicts it directly. The residual risk is genuine round-robin/geo rotation, which
        # S1 can only rule out via a reachable clean vantage. Downgrading to inconclusive would
        # trade a false-accusation risk for a missed-detection risk; that is a determinative-core
        # policy call for the owner, not an implementation detail.
        if clean_answers is not None and local_set is not None:
            if local_set.issubset(clean_answers) and not local_set.isdisjoint(clean_answers):
                return "inconclusive"  # clean vantage sees same as local → round-robin/geo
        return "tampered"

    if dns_assessment == "match" and c in ("match", None):
        return "clean"
    return "inconclusive"


def _clean_vantage_answers(domain, qtype, qtype_str):
    """Query several public recursive resolvers and return an answer set ONLY when at
    least two INDEPENDENT vantages agree on exactly that set.

    A027: the old code returned the FIRST race-winning resolver's answer. Under a
    shared-upstream poisoning, the poisoned resolver can win the race, so a single
    race-winner could certify 'clean' — a false-secure result. A clean reference now
    requires corroboration: two vantages returning the same non-empty set. If the
    vantages disagree (CDN/geo split) or only one is reachable, there is no usable
    clean reference and the diff stays inconclusive rather than falsely clean.
    The whole fan-out is bounded by a wall-clock deadline (A096)."""
    servers = list(config.PUBLIC_RESOLVERS[:3])
    if len(servers) < 2:
        return set()

    def query_one(server):
        """Query a single clean vantage and return its answer set as a frozenset."""
        clean = resolver.query(domain, qtype, server)
        return frozenset(str(r["value"]).lower() for r in clean["records"] if r["type"] == qtype_str)

    # A136: use the module-level shared pool instead of per-request creation.
    futures = [_CLEAN_VANTAGE_POOL.submit(query_one, srv) for srv in servers]
    counts = {}
    try:
        for fut in concurrent.futures.as_completed(futures, timeout=_CLEAN_VANTAGE_DEADLINE):
            try:
                answers = fut.result()
            except resolver.DNSError:
                continue
            if answers:
                counts[answers] = counts.get(answers, 0) + 1
                if counts[answers] >= 2:  # corroborated by >=2 independent vantages
                    return set(answers)
    except concurrent.futures.TimeoutError:
        pass  # budget exhausted; use whatever corroborated so far
    for answer_set, seen in counts.items():
        if seen >= 2:
            return set(answer_set)
    return set()

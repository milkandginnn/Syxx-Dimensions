"""Run DNS investigations for multiple domains concurrently."""

import concurrent.futures
import time

from . import investigate, resolver  # noqa: F401 — resolver available to sub-modules

_MAX_WORKERS = 5


def _run_one(domain):
    """Investigate a single domain, returning a normalised result dict."""
    result, err = investigate.investigate(domain)
    if err:
        return {"domain": domain, "error": err}
    return {
        "domain": result["domain"],
        "consensus": result.get("consensus"),
        "conclusion": result.get("consensusReason"),
        "ipv4": result.get("ipv4"),
        "ipv6": result.get("ipv6"),
        "dnssec": result.get("dnssec"),
        "error": None,
    }


def investigate_all(domains, timeout=60, max_domains=50):
    """Investigate *domains* concurrently, honouring an aggregate deadline.

    Parameters
    ----------
    domains : list[str]
        Domain names to investigate.
    timeout : float
        Aggregate wall-clock budget in seconds (default 60).
    max_domains : int
        Hard cap on accepted domains (default 50).

    Returns
    -------
    dict with keys: results, total, success, failed, truncated.
    """
    truncated = len(domains) > max_domains
    accepted = domains[:max_domains]

    valid = [(d, investigate.valid_domain(d)) for d in accepted]
    results = [{"domain": d, "error": "invalid domain"} for d, ok in valid if not ok]
    good = [d for d, ok in valid if ok]

    deadline = time.monotonic() + timeout
    with concurrent.futures.ThreadPoolExecutor(max_workers=_MAX_WORKERS) as pool:
        future_map = {pool.submit(_run_one, d): d for d in good}
        for fut in concurrent.futures.as_completed(future_map, timeout=max(deadline - time.monotonic(), 0.01)):
            domain = future_map[fut]
            try:
                results.append(fut.result())
            except Exception as exc:
                results.append({"domain": domain, "error": str(exc)})

        # Cancel anything still pending past the deadline.
        for f in future_map:
            if not f.done():
                f.cancel()
                results.append({"domain": future_map[f], "error": "timed out"})

    success = sum(1 for r in results if r["error"] is None)
    return {
        "results": results,
        "total": len(results),
        "success": success,
        "failed": len(results) - success,
        "truncated": truncated,
    }

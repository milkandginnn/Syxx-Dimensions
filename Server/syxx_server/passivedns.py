"""Passive-DNS ingestion + query index (feature: passive-DNS index).

Every resolution the server observes (investigation queries and leak-collector
lookups) is recorded here, giving a real, queryable history behind the landing
page's record counter and the paid /api/passive-dns endpoint.
"""

import os
import time

from . import db


def _i(name, default):
    """Read an integer config from the environment (A127)."""
    try:
        return int(os.environ.get(name, "").strip())
    except (TypeError, ValueError):
        return default


# A095/A079: per-domain cardinality cap. An attacker who owns a domain can configure
# their nameserver to return thousands of distinct IPs, each creating a new row in the
# passive_dns table within the 30-day retention window. This caps distinct values per
# domain so high-cardinality poisoning is bounded.
_PASSIVE_PER_DOMAIN_MAX = _i("SYXX_PASSIVE_PER_DOMAIN_MAX", 100)


def ingest(domain, rtype, value, source, resolver=None):
    if not domain or not value:
        return
    # A095/A079: refuse to ingest if this domain already has too many distinct records.
    # Upsert (ON CONFLICT UPDATE) still bumps hits/last_seen for existing records,
    # but new distinct values past the cap are silently dropped.
    # A081: replaced N+1 SELECT-all-rows with two lightweight queries —
    # a LIMIT-1 existence check and a capped COUNT subquery.
    if _PASSIVE_PER_DOMAIN_MAX > 0:
        if not db.passive_domain_exists(domain, rtype, value):
            count = db.passive_domain_count_capped(domain, _PASSIVE_PER_DOMAIN_MAX)
            if count >= _PASSIVE_PER_DOMAIN_MAX:
                return
    db.passive_record(domain, rtype, value, source, resolver)


def query(domain, limit=200):
    records = db.passive_query(domain, limit)
    # A023: the /api/passive-dns endpoint is cross-readable (no per-record ownership
    # check), so never return the observing resolver IP. For a leak observation it is
    # submitter PII; for an investigation record it is just a vantage detail the
    # public index has no need to expose. Drop it at the API boundary.
    for r in records:
        r.pop("resolver", None)
    # A080: strip trailing dot in the response domain for consistency with the
    # normalized storage key (db.passive_record/passive_query also rstrip(".")).
    return {"domain": domain.lower().rstrip("."), "count": len(records), "records": records}


def total_records():
    return db.passive_count()


def baseline(domain, rtype=None, now=None):
    """Historical baseline / drift over the observed passive-DNS records (Workstream S #4).

    Returns each observed value's first/last-seen, hits and age, plus a drift signal:
    an ESTABLISHED value (seen >= 7 days, not stale > 30) coexisting with a brand-new
    different value (first seen < 1 day ago, still active) suggests drift.

    A117: drift is a tri-state string, never a bare bool. "Never observed" and "observed
    with no drift" are distinct states — a bare `False` collapses them and violates the
    no-result-before-evidence rule. An `observationDays` floor of 7 days gates the
    "none" result; below that the answer is always "insufficient-data".
    """
    now = now if now is not None else int(time.time())
    # A187 Fix 1: push rtype filter into SQL so the LIMIT applies to the rows
    # actually being analyzed. Request limit+1 to detect truncation (Fix 2).
    _BASELINE_LIMIT = 500
    records = db.passive_query(domain, _BASELINE_LIMIT + 1, rtype=rtype)
    truncated = len(records) > _BASELINE_LIMIT
    if truncated:
        records = records[:_BASELINE_LIMIT]
    if rtype:
        rtype = rtype.upper()
    values = []
    for r in records:
        values.append(
            {
                "value": r["value"],
                "rtype": r["rtype"],
                "firstSeen": r["first_seen"],
                "lastSeen": r["last_seen"],
                "hits": r["hits"],
                "ageDays": round((r["last_seen"] - r["first_seen"]) / 86400.0, 2),
                "staleDays": round((now - r["last_seen"]) / 86400.0, 2),
            }
        )
    # A127: thresholds are configurable via environment variables so operators can
    # tune for fast-rotating CDNs (sub-7-day) without editing source. Defaults match
    # the original hardcoded values for backward compatibility.
    _ESTABLISHED_AGE_DAYS = _i("SYXX_DRIFT_ESTABLISHED_AGE_DAYS", 7)
    _ESTABLISHED_STALE_DAYS = _i("SYXX_DRIFT_ESTABLISHED_STALE_DAYS", 30)
    _NEW_AGE_DAYS = _i("SYXX_DRIFT_NEW_AGE_DAYS", 1)
    _NEW_STALE_DAYS = _i("SYXX_DRIFT_NEW_STALE_DAYS", 1)
    # A040: key by (rtype, value) so A and AAAA populations are compared independently.
    # Previously keyed by value alone, so a normal dual-stack evolution (new AAAA
    # alongside established A) was reported as cross-generation drift.
    established = {
        (v["rtype"], v["value"])
        for v in values
        if v["ageDays"] >= _ESTABLISHED_AGE_DAYS and v["staleDays"] <= _ESTABLISHED_STALE_DAYS
    }
    recently_new = {
        (v["rtype"], v["value"]) for v in values if v["ageDays"] < _NEW_AGE_DAYS and v["staleDays"] < _NEW_STALE_DAYS
    }

    # A117: tri-state drift. "insufficient-data" when no observation spans >= 7 days;
    # "suspected" when an established value coexists with a brand-new different value;
    # "none" when observed long enough with no drift signal.
    # A187 Fix 3: when truncated, drift must be "insufficient-data" with a distinct
    # reason — a partial window cannot produce a trustworthy drift conclusion.
    if truncated:
        drift = "insufficient-data"
    elif not values or not established:
        drift = "insufficient-data"
    elif bool(recently_new - established):
        drift = "suspected"
    else:
        drift = "none"

    return {
        "domain": domain.lower().rstrip("."),
        "type": rtype,
        "recordCount": len(values),
        "values": values,  # A080
        "established": sorted(v for _, v in established),
        "recentlyNew": sorted(v for _, v in (recently_new - established)),
        "drift": drift,
        "observationDays": round(max(v["ageDays"] for v in values) if values else 0, 1),
        # A187 Fix 2: surface truncation so the caller can distinguish "thin
        # observation" from "server-side LIMIT truncated the evidence set".
        "truncated": truncated,
        "totalRecords": len(values) + (1 if truncated else 0),  # lower bound
        "truncationReason": "server-limit" if truncated else None,
    }

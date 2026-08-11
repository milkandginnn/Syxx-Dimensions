"""Leak token, observation, and passive-DNS repository.

Extracted from db.py (A018). All functions are re-exported by db.py for
backward compatibility — callers should continue using ``db.leak_token_create(...)`` etc.
"""

import hashlib
import time


def _hash_token(token):
    """E024: Hash leak tokens with SHA-256 before storage.

    Tokens are one-time use and short-lived, but hashing them before storage
    prevents exposure if the SQLite database is compromised (backup leak,
    filesystem access, etc.). The token is the lookup key, so we hash it
    consistently on both write and read paths."""
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


# ---------------------------------------------------------------------------
# Leak tokens
# ---------------------------------------------------------------------------


def leak_token_create(token, subject, zone, ttl, origin="ip"):
    """Create a leak token. `origin` is 'license' for paid-route tokens (minted by
    /api/leak/token with a license subject) or 'ip' for free-route tokens (minted by
    /api/leaktest/start with a client IP subject). A133: the origin is checked on
    read so the free route cannot serve paid tokens."""
    from .db import _return_conn, _write_conn

    now = int(time.time())
    token_hash = _hash_token(token)
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT OR REPLACE INTO leak_tokens(token,subject,zone,issued_at,expires_at,origin) VALUES(?,?,?,?,?,?)",
            (token_hash, subject, zone, now, now + ttl, origin),
        )
        conn.commit()
    finally:
        _return_conn(conn)


def leak_token_get(token, now=None):
    from .db import _return_conn, connect

    now = int(time.time()) if now is None else now
    token_hash = _hash_token(token)
    conn = connect()
    try:
        row = conn.execute("SELECT * FROM leak_tokens WHERE token=?", (token_hash,)).fetchone()
        if not row:
            return None
        # Enforce the stored expiry on read. Previously expires_at was ignored, so expired
        # tokens still validated and the "expired token" caller path was dead code.
        if row["expires_at"] is not None and row["expires_at"] < now:
            return None
        return dict(row)
    finally:
        _return_conn(conn)


def leak_token_claim(token):
    """Atomically claim a leak token for verification (A111).

    Returns True if this caller won the claim (no prior verifier), False if another
    concurrent verifier already claimed it. Uses the kv table's INSERT OR IGNORE as a
    compare-and-swap so two concurrent verifies cannot both sign a receipt.
    The claim key is cleaned up by leak_token_consume when the token is deleted.
    """
    from .kv_repo import kv_insert_once

    # E024: hash the token for the claim key as well
    token_hash = _hash_token(token)
    return kv_insert_once("leak_claim:" + token_hash, "claimed")


def leak_token_release_claim(token):
    """Release a previously-claimed leak token so the client can retry (A111).

    Called when verify returns an inconclusive result (no observations yet) and the
    token must remain usable for a later poll.
    """
    from .db import _return_conn, _write_conn

    # E024: hash the token for the claim key
    token_hash = _hash_token(token)
    conn = _write_conn()
    try:
        conn.execute("DELETE FROM kv WHERE key=?", ("leak_claim:" + token_hash,))
        conn.commit()
    finally:
        _return_conn(conn)


def leak_token_consume(token):
    """Delete a token and its observations after a successful verify. The leak protocol is a
    one-time closed loop — nothing about a token should outlive its single verification."""
    from .db import _return_conn, _write_conn

    # E024: hash the token for all lookups
    token_hash = _hash_token(token)
    conn = _write_conn()
    try:
        conn.execute("DELETE FROM leak_observations WHERE token=?", (token_hash,))
        conn.execute("DELETE FROM leak_tokens WHERE token=?", (token_hash,))
        # A111: clean up the claim key so a re-verify after consume gets "unknown or expired"
        conn.execute("DELETE FROM kv WHERE key=?", ("leak_claim:" + token_hash,))
        conn.commit()
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Leak observations
# ---------------------------------------------------------------------------


def leak_observation_add(token, resolver_ip, qtype):
    from .db import _return_conn, _write_conn

    # E024: hash the token before storing observations
    token_hash = _hash_token(token)
    conn = _write_conn()
    try:
        # A034: dedup on (token, resolver_ip, qtype) via the unique index built in
        # _migrate. A replayed cleartext lookup of a known, still-valid token bumps
        # observed_at on the existing row instead of appending an unbounded new one.
        conn.execute(
            "INSERT INTO leak_observations(token,resolver_ip,qtype,observed_at) "
            "VALUES(?,?,?,?) "
            "ON CONFLICT(token,resolver_ip,qtype) DO UPDATE SET observed_at=excluded.observed_at",
            (token_hash, resolver_ip, qtype, int(time.time())),
        )
        conn.commit()
    finally:
        _return_conn(conn)


def leak_observations(token):
    from .db import _return_conn, connect

    # E024: hash the token for lookup
    token_hash = _hash_token(token)
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT resolver_ip, qtype, observed_at FROM leak_observations WHERE token=? ORDER BY observed_at ASC",
            (token_hash,),
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Passive DNS
# ---------------------------------------------------------------------------


def passive_record(domain, rtype, value, source, resolver=None):
    from .db import _return_conn, _write_conn

    now = int(time.time())
    conn = _write_conn()
    try:
        # A080: normalize domain by lowercasing AND stripping a single trailing
        # dot. Without the strip, "example.com." and "example.com" create
        # distinct UNIQUE rows and split/miss on lookups.
        norm_domain = domain.lower().rstrip(".")
        conn.execute(
            "INSERT INTO passive_dns(domain,rtype,value,source,resolver,first_seen,last_seen,hits) "
            "VALUES(?,?,?,?,?,?,?,1) "
            "ON CONFLICT(domain,rtype,value) DO UPDATE SET "
            "last_seen=excluded.last_seen, hits=passive_dns.hits+1, "
            "resolver=COALESCE(excluded.resolver, passive_dns.resolver)",
            (norm_domain, rtype, value, source, resolver, now, now),
        )
        conn.commit()
    finally:
        _return_conn(conn)


def passive_query(domain, limit=200, rtype=None):
    """Return passive-DNS records for *domain*, optionally filtered by *rtype*.

    A187 Fix 1: rtype filter is pushed into SQL so the LIMIT applies to the rows
    actually being analyzed. Previously the filter was applied in Python AFTER
    the LIMIT, so a rare rtype on a busy domain could return zero rows while
    hundreds existed.
    """
    from .db import _return_conn, connect

    conn = connect()
    try:
        # A080: strip trailing dot to match the normalization in passive_record.
        norm_domain = domain.lower().rstrip(".")
        if rtype:
            rows = conn.execute(
                "SELECT domain,rtype,value,source,resolver,first_seen,last_seen,hits "
                "FROM passive_dns WHERE domain=? AND rtype=? ORDER BY last_seen DESC LIMIT ?",
                (norm_domain, rtype.upper(), limit),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT domain,rtype,value,source,resolver,first_seen,last_seen,hits "
                "FROM passive_dns WHERE domain=? ORDER BY last_seen DESC LIMIT ?",
                (norm_domain, limit),
            ).fetchall()
        return [dict(r) for r in rows]
    finally:
        _return_conn(conn)


def passive_count():
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT COUNT(*) c FROM passive_dns").fetchone()
        return row["c"] if row else 0
    finally:
        _return_conn(conn)


def passive_domain_exists(domain, rtype, value):
    """Return True if a passive-DNS row exists for (domain, rtype, value).

    A081: lightweight existence check — SELECT 1 with LIMIT 1 instead of
    fetching full rows just to test membership.
    """
    from .db import _return_conn, connect

    conn = connect()
    try:
        norm_domain = domain.lower().rstrip(".")
        row = conn.execute(
            "SELECT 1 FROM passive_dns WHERE domain=? AND rtype=? AND value=? LIMIT 1",
            (norm_domain, rtype, value),
        ).fetchone()
        return row is not None
    finally:
        _return_conn(conn)


def passive_domain_count_capped(domain, cap):
    """Return the number of distinct rows for *domain*, capped at *cap*.

    A081: uses a subquery with LIMIT so SQLite stops scanning after *cap*
    rows — O(cap) instead of O(total) for high-cardinality domains.
    """
    from .db import _return_conn, connect

    conn = connect()
    try:
        norm_domain = domain.lower().rstrip(".")
        row = conn.execute(
            "SELECT COUNT(*) c FROM ("
            "SELECT 1 FROM passive_dns WHERE domain=? LIMIT ?"
            ")",
            (norm_domain, cap),
        ).fetchone()
        return row["c"] if row else 0
    finally:
        _return_conn(conn)

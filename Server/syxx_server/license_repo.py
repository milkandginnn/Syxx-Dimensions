"""License, Keygen, and API key repository.

Extracted from db.py (A018). All functions are re-exported by db.py for
backward compatibility — callers should continue using ``db.license_lookup(...)`` etc.
"""

import hashlib
import hmac as _hmac
import json as _json
import time

from . import logging_util

# ---------------------------------------------------------------------------
# HMAC helpers (used by license_lookup / license_upsert / migration)
# ---------------------------------------------------------------------------


def _license_hmac_key_v1():
    """D053 legacy: raw session secret used as HMAC key (v1 rows)."""
    from . import licensing

    return licensing.secret()


def _license_hmac_key_v2():
    """A184 Fix 3: purpose-bound sub-key derived from the session secret.
    Rotating SYXX_SESSION_SECRET still invalidates v2 rows (the sub-key is
    derived from it), but a future v3 could use an independent license-secret
    env var. The derivation at least decouples the *key material* from the
    session-signing key, so a session-token forgery attack does not also
    forge license rows."""
    from . import licensing

    return _hmac.new(licensing.secret(), b"license-row-v1", hashlib.sha256).digest()


def _license_row_hmac_v1(key, plan, status, expires_at):
    """D053 legacy HMAC: pipe-delimited, covers 4 fields only."""
    msg = "{}|{}|{}|{}".format(key, plan, status, expires_at or "")
    return _hmac.new(_license_hmac_key_v1(), msg.encode("utf-8"), hashlib.sha256).hexdigest()


def _license_row_hmac_v2(key, plan, status, expires_at, note, created_at):
    """A184 Fix 4: JSON-canonical HMAC message covering all mutable row fields.
    Uses the purpose-bound v2 sub-key. JSON with sort_keys ensures field order
    is deterministic and pipe characters in values cannot cause ambiguity."""
    payload = _json.dumps(
        {
            "key": key,
            "plan": plan,
            "status": status,
            "expires_at": expires_at,
            "note": note,
            "created_at": created_at,
        },
        sort_keys=True,
        separators=(",", ":"),
    )
    return _hmac.new(_license_hmac_key_v2(), payload.encode("utf-8"), hashlib.sha256).hexdigest()


def _migrate_license_hmac_v1_to_v2(conn):
    """SEC-049: Migrate v1 HMAC rows to v2 during schema migration.

    This runs once at startup. For each v1 row, verify the v1 HMAC is valid,
    then recompute with the v2 algorithm (purpose-bound sub-key, covering
    note and created_at fields) and update the row.

    Rows that fail v1 verification are logged but left unchanged — they may
    have been inserted by an older server version with a different secret.
    The operator should review and re-provision these manually.
    """
    try:
        rows = conn.execute(
            "SELECT key, plan, status, expires_at, note, created_at, row_hmac, hmac_version "
            "FROM licenses WHERE hmac_version = 1 AND row_hmac IS NOT NULL"
        ).fetchall()
    except Exception:
        # Table may not exist yet on fresh installs
        return

    migrated = 0
    failed = 0
    for row in rows:
        d = dict(row)
        key = d["key"]
        # Verify v1 HMAC
        expected_v1 = _license_row_hmac_v1(key, d["plan"], d["status"], d.get("expires_at"))
        if not _hmac.compare_digest(d["row_hmac"] or "", expected_v1):
            # v1 HMAC doesn't match — log and skip
            logging_util.warn(
                "license_hmac_v1_migration_failed",
                key_prefix=(key or "")[:8],
                detail="v1 HMAC mismatch during migration; row left unchanged",
            )
            failed += 1
            continue
        # Recompute with v2 algorithm
        new_hmac = _license_row_hmac_v2(
            key, d["plan"], d["status"], d.get("expires_at"), d.get("note"), d.get("created_at")
        )
        conn.execute(
            "UPDATE licenses SET row_hmac=?, hmac_version=2 WHERE key=?",
            (new_hmac, key),
        )
        migrated += 1

    if migrated > 0 or failed > 0:
        conn.commit()
        logging_util.info(
            "license_hmac_v1_to_v2_migration",
            migrated=migrated,
            failed=failed,
            total=len(rows),
        )


# ---------------------------------------------------------------------------
# License CRUD
# ---------------------------------------------------------------------------


def license_lookup(key):
    """Look up a license row by key, verifying its HMAC integrity (D053/A184)."""
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT * FROM licenses WHERE key=?", (key,)).fetchone()
        if not row:
            return None
        d = dict(row)
        # D053: verify HMAC before trusting the row. A missing or mismatched
        # HMAC means the row was inserted without cryptographic integrity (e.g.
        # by direct DB write) and must not grant entitlement.
        stored_hmac = d.pop("row_hmac", None)
        hmac_ver = d.pop("hmac_version", 1) or 1
        if not stored_hmac:
            # A184 Fix 2: log the rejection distinctly so an operator who
            # provisions a row by hand can diagnose why it fails.
            logging_util.warn("license_row_unsigned", key_prefix=(d.get("key") or "")[:8])
            return None
        # A184: verify with the version-appropriate algorithm.
        if hmac_ver == 2:
            expected = _license_row_hmac_v2(
                d["key"], d["plan"], d["status"], d.get("expires_at"), d.get("note"), d.get("created_at")
            )
        else:
            expected = _license_row_hmac_v1(d["key"], d["plan"], d["status"], d.get("expires_at"))
        if not _hmac.compare_digest(stored_hmac, expected):
            # A184 Fix 2: distinct log event for HMAC mismatch vs unsigned row.
            logging_util.warn("license_row_hmac_mismatch", key_prefix=(d.get("key") or "")[:8], hmac_version=hmac_ver)
            return None
        return d
    finally:
        _return_conn(conn)


def license_upsert(key, plan="pro", status="active", expires_at=None, note=None):
    """Insert or update a license row with a v2 HMAC over all mutable fields (A184)."""
    from .db import _return_conn, _write_conn

    # A184: compute v2 HMAC over all mutable row fields using the purpose-bound
    # sub-key, so license_lookup can verify integrity on read.
    now = int(time.time())
    row_hmac = _license_row_hmac_v2(key, plan, status, expires_at, note, now)
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO licenses(key,plan,status,expires_at,note,created_at,row_hmac,hmac_version) "
            "VALUES(?,?,?,?,?,?,?,2) ON CONFLICT(key) DO UPDATE SET "
            "plan=excluded.plan, status=excluded.status, "
            "expires_at=excluded.expires_at, note=excluded.note, "
            "row_hmac=excluded.row_hmac, hmac_version=2",
            (key, plan, status, expires_at, note, now, row_hmac),
        )
        conn.commit()
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Keygen license ids (S12)
# ---------------------------------------------------------------------------


def keygen_license_id_set(subject, license_id):
    """Remember the Keygen license resource id observed for *subject* at
    activation time. The scheduler's status sweep must query THIS id: the
    subject itself is a local key hash (lic_<sha256[:16]>), not a Keygen
    resource id, so querying it 404s and read as 'revoked' for every key (S12)."""
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO keygen_licenses(subject,license_id,updated_at) VALUES(?,?,?) "
            "ON CONFLICT(subject) DO UPDATE SET license_id=excluded.license_id, "
            "updated_at=excluded.updated_at",
            (subject, license_id, int(time.time())),
        )
        conn.commit()
    finally:
        _return_conn(conn)


def keygen_license_id_get(subject):
    """Return the stored Keygen license id for *subject*, or None when this
    subject never validated through Keygen (offline/admin keys never store one)."""
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT license_id FROM keygen_licenses WHERE subject=?", (subject,)).fetchone()
        return row["license_id"] if row else None
    finally:
        _return_conn(conn)


def keygen_subject_for_license_id(license_id):
    """Reverse of keygen_license_id_get: the local subject (lic_<sha256[:16]>) that a Keygen
    license resource id belongs to, or None if this server has never seen that license validate.

    Needed because every LOCAL revocation lever is keyed by subject — api_keys.subject is written
    as `lic_<hash>` and api_key_revoke_all_for_subject matches on it — while Stripe and Keygen only
    ever hand us a Keygen resource id. Without this map a refund can suspend the license upstream
    and still leave that customer's API keys live on this server.

    None means UNKNOWN, not "no keys to revoke". A caller must treat it as an unresolved
    revocation to report, never as a completed one: the subject may simply not have activated
    through Keygen on this server yet.
    """
    if not license_id:
        return None
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT subject FROM keygen_licenses WHERE license_id=?", (license_id,)).fetchone()
        return row["subject"] if row else None
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# API Key CRUD
# ---------------------------------------------------------------------------


def api_key_create(subject, label=None, scopes="*", ttl_days=90):
    """Create a new API key. Returns the raw key string (shown once, never stored).

    A186 Fix 2: the `scopes` parameter is accepted for backward compatibility but
    ignored — scopes are stored but never enforced (see licensing.py:entitlement_for).
    Exposing a scope selector that does nothing is worse than not offering one.
    Until scope enforcement ships (Fix 1), all keys are full-authority.
    """
    import secrets as _secrets

    from .db import _return_conn, _write_conn

    raw = "syxx_" + _secrets.token_urlsafe(32)
    key_hash = hashlib.sha256(raw.encode()).hexdigest()
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO api_keys(subject,key_hash,key_prefix,label,scopes,created_at,expires_at) "
            "VALUES(?,?,?,?,?,?,?)",
            # A186: always store "*" regardless of caller input — the column is
            # vestigial until scope enforcement ships, and accepting a non-"*"
            # value would let the key list display a restriction the server
            # does not apply.
            (subject, key_hash, raw[:12], label, "*", now, now + ttl_days * 86400),
        )
        conn.commit()
    finally:
        _return_conn(conn)
    return raw


def api_key_lookup(key_hash):
    """Look up an API key by its SHA-256 hash. Returns dict or None."""
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT * FROM api_keys WHERE key_hash=? AND revoked=0", (key_hash,)).fetchone()
        if not row:
            return None
        if row["expires_at"] and row["expires_at"] < int(time.time()):
            return None
        return dict(row)
    finally:
        _return_conn(conn)


def api_key_touch(key_id):
    """Update last_used timestamp and increment use_count."""
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        conn.execute(
            "UPDATE api_keys SET last_used=?, use_count=use_count+1 WHERE id=?",
            (int(time.time()), key_id),
        )
        conn.commit()
    finally:
        _return_conn(conn)


def api_key_list(subject):
    """List all API keys for a subject (no hashes returned).

    A186 Fix 2: `scopes` is excluded from the response. The column is stored but
    never enforced (licensing.py:entitlement_for hardcodes full authority), so
    returning it would show the user a restriction the server does not apply.
    """
    from .db import _return_conn, connect

    conn = connect()
    try:
        rows = conn.execute(
            "SELECT id,key_prefix,label,created_at,expires_at,last_used,use_count,revoked "
            "FROM api_keys WHERE subject=? ORDER BY created_at DESC",
            (subject,),
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        _return_conn(conn)


def api_key_revoke(key_id, subject):
    """Revoke an API key. Only the owning subject can revoke."""
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        # Return the real row count. Discarding it let the route answer {"revoked": true}
        # for a key that does not exist, and identically for another tenant's key — an
        # unfalsifiable success claim and an id-existence oracle.
        cur = conn.execute("UPDATE api_keys SET revoked=1 WHERE id=? AND subject=?", (key_id, subject))
        revoked = cur.rowcount
        conn.commit()
        return revoked
    finally:
        _return_conn(conn)


def api_key_revoke_all_for_subject(subject):
    """Revoke all API keys for a subject. Called when a license is revoked in Keygen.

    A175: returns the number of rows actually revoked. The caller must branch on
    this count — asserting "keys_revoked" without measuring let zero-row updates
    report success for subjects that had no keys (or for the wrong identifier
    space entirely, as the Keygen webhook did before the app.py fix).
    """
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        cur = conn.execute("UPDATE api_keys SET revoked=1 WHERE subject=? AND revoked=0", (subject,))
        conn.commit()
        return cur.rowcount
    finally:
        _return_conn(conn)


def api_key_subjects_with_active_keys():
    """Return distinct subjects that have at least one active (non-revoked, non-expired) API key.
    Used by the periodic Keygen license sync job."""
    from .db import _return_conn, connect

    now = int(time.time())
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT DISTINCT subject FROM api_keys WHERE revoked=0 AND (expires_at IS NULL OR expires_at > ?)", (now,)
        ).fetchall()
        return [r["subject"] for r in rows]
    finally:
        _return_conn(conn)

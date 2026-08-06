"""SQLite persistence layer (feature: persistent datastore).

WAL mode + a fresh short-lived connection per operation makes this safe to call
from the threaded HTTP server and the DNS thread concurrently. Replaces the old
volatile in-memory ``leak_db`` dict that was lost on every systemd restart.
"""

import hashlib
import hmac as _hmac
import json as _json
import os
import secrets
import sqlite3
import threading
import time

from . import config, logging_util

_init_lock = threading.Lock()
_initialized = False


def _env_int(name, default):
    """Small env-int reader. config.py is owned by another lane, so these
    retention/cap knobs are read here directly. Empty/invalid falls back."""
    try:
        return int(os.environ.get(name, "") or default)
    except (TypeError, ValueError):
        return default


# A052: stripe_evt/stripe_cust kv rows are kept this many days for webhook
# idempotency, then reclaimed by purge_expired. Singleton keys (session_secret,
# beta_digest_last_sent) are never timestamp-purged. 0 disables the kv purge.
_KV_RETENTION_DAYS = _env_int("SYXX_KV_RETENTION_DAYS", 90)
# A079/A091/A095: time-window retention alone cannot bound tables whose growth is
# driven by distinct keys within the window. These are absolute row caps; the
# oldest rows past the cap are reclaimed. 0 disables that cap.
_PASSIVE_MAX_ROWS = _env_int("SYXX_PASSIVE_MAX_ROWS", 500_000)
_AUDIT_MAX_ROWS = _env_int("SYXX_AUDIT_MAX_ROWS", 200_000)
_WARP_CACHE_MAX_ROWS = _env_int("SYXX_WARP_CACHE_MAX_ROWS", 10_000)
# A031: beta_signups retention. Every other PII table has a purge window; this one
# grew forever and the weekly digest re-emailed the full historical list. 0 disables.
_BETA_RETENTION_DAYS = _env_int("SYXX_BETA_RETENTION_DAYS", 365)
# A037: a session-signing secret shorter than this (bytes) or with fewer than this
# many distinct byte values is rejected. Session tokens are HMAC-signed with it, so
# a weak operator secret makes them forgeable (paywall bypass).
_MIN_SECRET_BYTES = 32
_MIN_SECRET_DISTINCT = 8

SCHEMA = """
PRAGMA journal_mode=WAL;

CREATE TABLE IF NOT EXISTS kv (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    created_at INTEGER
);

CREATE TABLE IF NOT EXISTS licenses (
    key        TEXT PRIMARY KEY,
    plan       TEXT NOT NULL DEFAULT 'pro',
    status     TEXT NOT NULL DEFAULT 'active',
    expires_at INTEGER,
    note       TEXT,
    created_at INTEGER NOT NULL,
    row_hmac   TEXT
);

CREATE TABLE IF NOT EXISTS keygen_licenses (
    subject    TEXT PRIMARY KEY,
    license_id TEXT NOT NULL,
    updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS leak_tokens (
    token      TEXT PRIMARY KEY,
    subject    TEXT NOT NULL,
    zone       TEXT NOT NULL,
    issued_at  INTEGER NOT NULL,
    expires_at INTEGER NOT NULL,
    origin     TEXT NOT NULL DEFAULT 'ip'
);

CREATE TABLE IF NOT EXISTS leak_observations (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    token       TEXT NOT NULL,
    resolver_ip TEXT NOT NULL,
    qtype       INTEGER NOT NULL,
    observed_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_leak_obs_token ON leak_observations(token);

CREATE TABLE IF NOT EXISTS passive_dns (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    domain     TEXT NOT NULL,
    rtype      TEXT NOT NULL,
    value      TEXT NOT NULL,
    source     TEXT NOT NULL,
    resolver   TEXT,
    first_seen INTEGER NOT NULL,
    last_seen  INTEGER NOT NULL,
    hits       INTEGER NOT NULL DEFAULT 1,
    UNIQUE(domain, rtype, value)
);
CREATE INDEX IF NOT EXISTS idx_passive_domain ON passive_dns(domain);

CREATE TABLE IF NOT EXISTS warp_cache (
    url        TEXT PRIMARY KEY,
    payload    TEXT NOT NULL,
    created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS audit (
    id      INTEGER PRIMARY KEY AUTOINCREMENT,
    ts      INTEGER NOT NULL,
    subject TEXT,
    route   TEXT NOT NULL,
    status  INTEGER NOT NULL,
    ip      TEXT
);
CREATE INDEX IF NOT EXISTS idx_audit_ts ON audit(ts);

CREATE TABLE IF NOT EXISTS beta_signups (
    email      TEXT PRIMARY KEY,
    interests  TEXT NOT NULL,
    ip         TEXT,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS trial_anchors (
    fingerprint TEXT PRIMARY KEY,
    first_launch INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS api_keys (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    subject    TEXT NOT NULL,
    key_hash   TEXT NOT NULL UNIQUE,
    key_prefix TEXT NOT NULL,
    label      TEXT,
    scopes     TEXT NOT NULL DEFAULT '*',
    created_at INTEGER NOT NULL,
    expires_at INTEGER,
    last_used  INTEGER,
    use_count  INTEGER NOT NULL DEFAULT 0,
    revoked    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_api_keys_subject ON api_keys(subject);
CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON api_keys(key_hash);

CREATE TABLE IF NOT EXISTS monitors (
    id             TEXT PRIMARY KEY,
    subject        TEXT NOT NULL,
    domain         TEXT NOT NULL,
    checks         TEXT NOT NULL,
    interval_hours INTEGER NOT NULL DEFAULT 24,
    notify_email   TEXT,
    created_at     INTEGER NOT NULL,
    last_run       INTEGER,
    next_run       INTEGER NOT NULL,
    active         INTEGER NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS idx_monitors_subject ON monitors(subject);
CREATE INDEX IF NOT EXISTS idx_monitors_next_run ON monitors(next_run);

CREATE TABLE IF NOT EXISTS monitor_results (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    monitor_id TEXT NOT NULL,
    run_at     INTEGER NOT NULL,
    check_type TEXT NOT NULL,
    result    TEXT,
    changes    TEXT,
    raw_json   TEXT
);
CREATE INDEX IF NOT EXISTS idx_monitor_results_monitor ON monitor_results(monitor_id, run_at);
"""


def connect():
    conn = sqlite3.connect(config.DB_PATH, timeout=10)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA busy_timeout=5000")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


def _write_conn():
    """Return a connection primed for writes with an IMMEDIATE transaction.
    This prevents concurrent writers from hitting 'database is locked' under
    WAL mode by ensuring each writer starts an exclusive transaction immediately."""
    conn = connect()
    conn.execute("BEGIN IMMEDIATE")
    return conn


def _write_probe():
    """Write a throwaway row to verify the database is writable.
    Raises if the filesystem is read-only or permissions are wrong."""
    conn = connect()
    try:
        conn.execute("CREATE TABLE IF NOT EXISTS _write_probe (k INTEGER PRIMARY KEY)")
        conn.execute("INSERT OR IGNORE INTO _write_probe(k) VALUES (1)")
        conn.commit()
        conn.execute("DELETE FROM _write_probe WHERE k=1")
        conn.commit()
    finally:
        conn.close()


def _migrate(conn):
    """Idempotent schema migrations that CREATE TABLE IF NOT EXISTS cannot express
    on an already-populated database (adding a column, enforcing a new uniqueness
    constraint on a table that may already hold duplicates)."""
    # A034: enforce (token, resolver_ip, qtype) uniqueness so a replayed lookup of a
    # known token cannot append unbounded observation rows. Dedup any pre-existing
    # rows first (keep the earliest) so the unique index can be built on an old DB.
    conn.execute(
        "DELETE FROM leak_observations WHERE id NOT IN "
        "(SELECT MIN(id) FROM leak_observations GROUP BY token, resolver_ip, qtype)"
    )
    conn.execute(
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_leak_obs_unique ON leak_observations(token, resolver_ip, qtype)"
    )
    # A052: kv rows had no timestamp, so purge_expired could never reclaim
    # stripe_evt/stripe_cust entries. Add created_at to older DBs; new rows set it.
    cols = {r["name"] for r in conn.execute("PRAGMA table_info(kv)").fetchall()}
    if "created_at" not in cols:
        conn.execute("ALTER TABLE kv ADD COLUMN created_at INTEGER")
    # D053: add row_hmac to licenses table for HMAC integrity on existing DBs.
    lic_cols = {r["name"] for r in conn.execute("PRAGMA table_info(licenses)").fetchall()}
    if "row_hmac" not in lic_cols:
        conn.execute("ALTER TABLE licenses ADD COLUMN row_hmac TEXT")
    # A184: add hmac_version so purpose-bound key derivation (v2) can coexist with
    # legacy v1 rows during migration. v1 rows use the session secret directly; v2
    # derives a purpose-bound sub-key and covers note+created_at in the HMAC message.
    if "hmac_version" not in lic_cols:
        conn.execute("ALTER TABLE licenses ADD COLUMN hmac_version INTEGER NOT NULL DEFAULT 1")
    # A133: add origin column to leak_tokens so the free route cannot serve paid
    # tokens. Existing rows default to 'ip' (the free route's origin).
    lt_cols = {r["name"] for r in conn.execute("PRAGMA table_info(leak_tokens)").fetchall()}
    if "origin" not in lt_cols:
        conn.execute("ALTER TABLE leak_tokens ADD COLUMN origin TEXT NOT NULL DEFAULT 'ip'")
    conn.commit()


def init():
    global _initialized
    with _init_lock:
        if _initialized:
            return
        os.makedirs(os.path.dirname(config.DB_PATH) or ".", exist_ok=True)
        # A177: pre-create the DB file at 0600 BEFORE SQLite opens it.
        # sqlite3.connect() creates the file at (0666 & ~umask), typically 0644,
        # and on a fresh deploy the chmod below would run only on the next restart.
        # Pre-creating with an explicit mode closes the world-readable window at
        # first boot — the one moment the ordering matters most.
        fd = os.open(config.DB_PATH, os.O_CREAT | os.O_RDWR, 0o600)
        os.close(fd)
        conn = connect()
        try:
            conn.executescript(SCHEMA)
            conn.commit()
            _migrate(conn)
        finally:
            conn.close()
        # S2: the SQLite DB file must be owner-only readable/writable.
        # Re-chmod after writes in case SQLite or a migration reset the mode.
        # A world-readable DB leaks license keys, session tokens, and passive-DNS
        # history to any local process on the host.
        os.chmod(config.DB_PATH, 0o600)
        # A177: chmod WAL and SHM sidecars — SQLite creates them at umask default
        # (typically 0644), and they contain the same sensitive data as the main DB.
        for suffix in ("-wal", "-shm"):
            sidecar = config.DB_PATH + suffix
            try:
                os.chmod(sidecar, 0o600)
            except FileNotFoundError:
                pass  # sidecar may not exist yet if no writes have occurred
        # A037: fail fast at startup on a weak operator secret rather than at first
        # token mint/verify, so a misconfigured deploy is caught immediately.
        env_secret = os.environ.get("SYXX_SESSION_SECRET")
        if env_secret:
            _validate_env_secret(env_secret)
        _write_probe()
        _initialized = True


# --- kv / secrets ----------------------------------------------------------


def kv_get(key, default=None):
    conn = connect()
    try:
        row = conn.execute("SELECT value FROM kv WHERE key=?", (key,)).fetchone()
        return row["value"] if row else default
    finally:
        conn.close()


def kv_get_with_created_at(key):
    """Return (value, created_at) for a kv row, or (None, None) if absent.

    A181: used by fulfillment to check whether a 'processing' lock has expired
    its lease. A 'processing' state older than the lease is reclaimable — the
    original handler likely crashed between claiming the event and writing the
    terminal state, and the row would otherwise strand the order permanently.
    """
    conn = connect()
    try:
        row = conn.execute("SELECT value, created_at FROM kv WHERE key=?", (key,)).fetchone()
        if not row:
            return None, None
        return row["value"], row["created_at"]
    finally:
        conn.close()


def kv_set(key, value):
    conn = _write_conn()
    try:
        # A052: stamp created_at on first write; keep the original on update so the
        # purge window is measured from when the row first appeared, not last touch.
        conn.execute(
            "INSERT INTO kv(key,value,created_at) VALUES(?,?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            (key, value, int(time.time())),
        )
        conn.commit()
    finally:
        conn.close()


def kv_insert_once(key, value):
    """Atomically reserve a key if it does not already exist.

    Returns True when this call created the row, False when another caller had
    already reserved it. Used before irreversible side effects such as license
    minting.
    """
    conn = _write_conn()
    try:
        cur = conn.execute(
            "INSERT OR IGNORE INTO kv(key,value,created_at) VALUES(?,?,?)", (key, value, int(time.time()))
        )
        conn.commit()
        return cur.rowcount == 1
    finally:
        conn.close()


def kv_find_by_value_prefix(key_prefix, value, limit=100):
    """Find the key matching *key_prefix* whose value EQUALS *value*.
    Returns that key with the prefix stripped, or None when there is no match.

    The match is exact, and deliberately so. This resolves a Keygen license id to the Stripe
    customer whose billing portal will be opened, so a loose match decides WHOSE SUBSCRIPTION a
    caller is shown. It used to be a substring test (`value in row["value"]`), which makes any
    license id that happens to be a substring of another one resolve to the wrong customer, and
    makes an empty value match the first row in the table. Keygen ids are UUIDs so a collision was
    unlikely rather than impossible — but "unlikely to expose the wrong person's billing" is not a
    property worth keeping when equality costs nothing.

    Returning None on no-match is UNKNOWN, not "no subscription exists"; the caller must not read
    it as an entitlement decision.

    O(n) over keys with the given prefix — for infrequent lookups (billing portal navigation),
    not hot paths.
    """
    if not value:
        # An empty needle can only ever produce a wrong answer here; refuse rather than
        # return whichever row happens to sort first.
        return None
    conn = connect()
    try:
        pattern = key_prefix + "%"
        rows = conn.execute(
            "SELECT key, value FROM kv WHERE key LIKE ? LIMIT ?",
            (pattern, limit),
        ).fetchall()
        for row in rows:
            if row["value"] == value:
                return row["key"][len(key_prefix) :]
        return None
    finally:
        conn.close()


def _validate_env_secret(env):
    """A037: reject a too-short or low-entropy SYXX_SESSION_SECRET. Session tokens
    are HMAC-signed with this value, so a weak operator secret makes them forgeable
    (paywall bypass). Fail closed (raise) rather than silently accept a weak secret
    or silently swap in a different one, which mirrors the collector 'no weak key'
    rule. Returns the validated secret bytes."""
    raw = env.encode("utf-8")
    if len(raw) < _MIN_SECRET_BYTES or len(set(raw)) < _MIN_SECRET_DISTINCT:
        raise RuntimeError(
            "SYXX_SESSION_SECRET is too short or low-entropy. Session tokens are "
            "HMAC-signed with it; a weak secret makes them forgeable. Provide at "
            f"least {_MIN_SECRET_BYTES} bytes of high-entropy material (e.g. `openssl rand -hex 32`), "
            "or unset it to let the server generate and persist a strong one."
        )
    return raw


def get_or_create_secret():
    """Server HMAC secret for session tokens and signed leak attestations.

    Uses SYXX_SESSION_SECRET if provided, else a persisted random 256-bit value.
    Never a hardcoded default (mirrors the WitnessSigner 'no default key' rule).
    """
    env = os.environ.get("SYXX_SESSION_SECRET")
    if env:
        return _validate_env_secret(env)
    existing = kv_get("session_secret")
    if existing:
        return bytes.fromhex(existing)
    secret = secrets.token_bytes(32)
    kv_set("session_secret", secret.hex())
    return secret


# --- licenses (offline allowlist) ------------------------------------------


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


def license_lookup(key):
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
        conn.close()


def license_upsert(key, plan="pro", status="active", expires_at=None, note=None):
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
        conn.close()


# --- keygen license ids (S12) -----------------------------------------------


def keygen_license_id_set(subject, license_id):
    """Remember the Keygen license resource id observed for *subject* at
    activation time. The scheduler's status sweep must query THIS id: the
    subject itself is a local key hash (lic_<sha256[:16]>), not a Keygen
    resource id, so querying it 404s and read as 'revoked' for every key (S12)."""
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
        conn.close()


def keygen_license_id_get(subject):
    """Return the stored Keygen license id for *subject*, or None when this
    subject never validated through Keygen (offline/admin keys never store one)."""
    conn = connect()
    try:
        row = conn.execute("SELECT license_id FROM keygen_licenses WHERE subject=?", (subject,)).fetchone()
        return row["license_id"] if row else None
    finally:
        conn.close()


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
    conn = connect()
    try:
        row = conn.execute("SELECT subject FROM keygen_licenses WHERE license_id=?", (license_id,)).fetchone()
        return row["subject"] if row else None
    finally:
        conn.close()


# --- leak tokens / observations --------------------------------------------


def leak_token_create(token, subject, zone, ttl, origin="ip"):
    """Create a leak token. `origin` is 'license' for paid-route tokens (minted by
    /api/leak/token with a license subject) or 'ip' for free-route tokens (minted by
    /api/leaktest/start with a client IP subject). A133: the origin is checked on
    read so the free route cannot serve paid tokens."""
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT OR REPLACE INTO leak_tokens(token,subject,zone,issued_at,expires_at,origin) VALUES(?,?,?,?,?,?)",
            (token, subject, zone, now, now + ttl, origin),
        )
        conn.commit()
    finally:
        conn.close()


def leak_token_get(token, now=None):
    now = int(time.time()) if now is None else now
    conn = connect()
    try:
        row = conn.execute("SELECT * FROM leak_tokens WHERE token=?", (token,)).fetchone()
        if not row:
            return None
        # Enforce the stored expiry on read. Previously expires_at was ignored, so expired
        # tokens still validated and the "expired token" caller path was dead code.
        if row["expires_at"] is not None and row["expires_at"] < now:
            return None
        return dict(row)
    finally:
        conn.close()


def leak_token_claim(token):
    """Atomically claim a leak token for verification (A111).

    Returns True if this caller won the claim (no prior verifier), False if another
    concurrent verifier already claimed it. Uses the kv table's INSERT OR IGNORE as a
    compare-and-swap so two concurrent verifies cannot both sign a receipt.
    The claim key is cleaned up by leak_token_consume when the token is deleted.
    """
    return kv_insert_once("leak_claim:" + token, "claimed")


def leak_token_release_claim(token):
    """Release a previously-claimed leak token so the client can retry (A111).

    Called when verify returns an inconclusive result (no observations yet) and the
    token must remain usable for a later poll.
    """
    conn = _write_conn()
    try:
        conn.execute("DELETE FROM kv WHERE key=?", ("leak_claim:" + token,))
        conn.commit()
    finally:
        conn.close()


def leak_token_consume(token):
    """Delete a token and its observations after a successful verify. The leak protocol is a
    one-time closed loop — nothing about a token should outlive its single verification."""
    conn = _write_conn()
    try:
        conn.execute("DELETE FROM leak_observations WHERE token=?", (token,))
        conn.execute("DELETE FROM leak_tokens WHERE token=?", (token,))
        # A111: clean up the claim key so a re-verify after consume gets "unknown or expired"
        conn.execute("DELETE FROM kv WHERE key=?", ("leak_claim:" + token,))
        conn.commit()
    finally:
        conn.close()


def leak_observation_add(token, resolver_ip, qtype):
    conn = _write_conn()
    try:
        # A034: dedup on (token, resolver_ip, qtype) via the unique index built in
        # _migrate. A replayed cleartext lookup of a known, still-valid token bumps
        # observed_at on the existing row instead of appending an unbounded new one.
        conn.execute(
            "INSERT INTO leak_observations(token,resolver_ip,qtype,observed_at) "
            "VALUES(?,?,?,?) "
            "ON CONFLICT(token,resolver_ip,qtype) DO UPDATE SET observed_at=excluded.observed_at",
            (token, resolver_ip, qtype, int(time.time())),
        )
        conn.commit()
    finally:
        conn.close()


def leak_observations(token):
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT resolver_ip, qtype, observed_at FROM leak_observations WHERE token=? ORDER BY observed_at ASC",
            (token,),
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


# --- passive DNS -----------------------------------------------------------


def passive_record(domain, rtype, value, source, resolver=None):
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
        conn.close()


def passive_query(domain, limit=200, rtype=None):
    """Return passive-DNS records for *domain*, optionally filtered by *rtype*.

    A187 Fix 1: rtype filter is pushed into SQL so the LIMIT applies to the rows
    actually being analyzed. Previously the filter was applied in Python AFTER
    the LIMIT, so a rare rtype on a busy domain could return zero rows while
    hundreds existed.
    """
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
        conn.close()


def passive_count():
    conn = connect()
    try:
        row = conn.execute("SELECT COUNT(*) c FROM passive_dns").fetchone()
        return row["c"] if row else 0
    finally:
        conn.close()


# --- warp cache ------------------------------------------------------------


def warp_cache_get(url, ttl):
    conn = connect()
    try:
        row = conn.execute("SELECT payload, created_at FROM warp_cache WHERE url=?", (url,)).fetchone()
        if not row:
            return None
        if int(time.time()) - row["created_at"] > ttl:
            return None
        return row["payload"]
    finally:
        conn.close()


def warp_cache_put(url, payload):
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO warp_cache(url,payload,created_at) VALUES(?,?,?) "
            "ON CONFLICT(url) DO UPDATE SET payload=excluded.payload, created_at=excluded.created_at",
            (url, payload, int(time.time())),
        )
        conn.commit()
    finally:
        conn.close()


# --- audit -----------------------------------------------------------------


def audit(subject, route, status, ip):
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO audit(ts,subject,route,status,ip) VALUES(?,?,?,?,?)",
            (int(time.time()), subject, route, status, ip),
        )
        conn.commit()
    finally:
        conn.close()


# --- beta signups ----------------------------------------------------------


def beta_reserve(email, interests, ip=None):
    """Record (or update) a beta-reservation for the Nnyx/Zzyn closed betas.

    Idempotent on email: re-submitting updates the interest set and timestamp
    rather than erroring, so the form can be safely resubmitted. `interests` is a
    comma-joined subset of {"nnyx","zzyn"}.
    """
    if len(interests) > 500:
        raise ValueError("interests field too long")
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO beta_signups(email,interests,ip,created_at,updated_at) "
            "VALUES(?,?,?,?,?) ON CONFLICT(email) DO UPDATE SET "
            "interests=excluded.interests, ip=excluded.ip, updated_at=excluded.updated_at",
            (email, interests, ip, now, now),
        )
        conn.commit()
    finally:
        conn.close()


def beta_all():
    """Every beta signup, oldest first — the source for the operator's weekly CSV digest."""
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT email, interests, ip, created_at, updated_at FROM beta_signups ORDER BY created_at ASC"
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


# --- retention / purge -----------------------------------------------------


def _cap_rows(conn, table, order_col, max_rows):
    """Delete the oldest rows (by order_col ASC) so `table` holds at most max_rows.
    Table and column names here are internal constants, never caller input."""
    if max_rows <= 0:
        return
    row = conn.execute(f"SELECT COUNT(*) c FROM {table}").fetchone()  # noqa: S608 — table/column are module constants, never caller input (call sites: db.py _purge_old)
    excess = (row["c"] if row else 0) - max_rows
    if excess > 0:
        conn.execute(
            f"DELETE FROM {table} WHERE rowid IN (SELECT rowid FROM {table} ORDER BY {order_col} ASC LIMIT ?)",  # noqa: S608 — table/column are module constants, never caller input (call sites: db.py _purge_old)
            (excess,),
        )


def backup(dest_dir=None, keep=None, stamp=None):
    """Write a consistent online backup of the live DB, then prune to `keep` most recent.

    THE most dangerous gap on the dedicated box (SD-56 / SHIP-v1 S7-1): the DB holds every license,
    trial anchor, and customer email, on local disk, with nothing copying it off. Host loss = total
    unrecoverable loss. This is step one — a rotating LOCAL backup; an off-host copy is an additive
    follow-up (the operator's rsync/S3 target is a separate decision).

    Uses SQLite's online backup API, which is SAFE ON A LIVE WAL DATABASE — it takes a transactionally
    consistent snapshot without blocking writers or requiring a stop. A file copy of a WAL DB is NOT
    safe (misses the -wal); this is why we do not just `cp`.

    Returns the path written. `stamp` is injectable for tests; production uses a UTC timestamp.
    """
    dest_dir = dest_dir or config.DB_BACKUP_DIR or (os.path.dirname(config.DB_PATH) or ".") + "/backups"
    keep = config.DB_BACKUP_KEEP if keep is None else keep
    # A177: backup dir must be owner-only so retained backup copies aren't world-readable.
    os.makedirs(dest_dir, mode=0o700, exist_ok=True)

    if stamp is None:
        import datetime

        stamp = datetime.datetime.now(datetime.UTC).strftime("%Y%m%dT%H%M%SZ")
    out = os.path.join(dest_dir, f"syxx-server-{stamp}.db")
    tmp = out + ".partial"

    # Snapshot to a temp file, fsync-durable via close, then atomic rename — a reader that lists the
    # dir mid-backup never sees a half-written .db that could be mistaken for a good restore point.
    # A177: pre-create tmp at 0600 so the backup is never world-readable, even momentarily.
    fd = os.open(tmp, os.O_CREAT | os.O_RDWR, 0o600)
    os.close(fd)
    src = sqlite3.connect(config.DB_PATH, timeout=30)
    try:
        dst = sqlite3.connect(tmp)
        try:
            src.backup(dst)  # online, consistent, non-blocking
        finally:
            dst.close()
    finally:
        src.close()
    try:
        os.replace(tmp, out)
    except OSError as e:
        logging_util.error("backup_replace_failed", err=str(e), dest=out)
        raise
    # A177: chmod the backup file to 0600 — a world-readable backup leaks every secret in the DB.
    os.chmod(out, 0o600)

    # Prune: keep the newest `keep` backups (0 disables pruning). Sort by name — the timestamp format
    # is lexicographically ordered, so name-sort == time-sort.
    if keep and keep > 0:
        backups = sorted(f for f in os.listdir(dest_dir) if f.startswith("syxx-server-") and f.endswith(".db"))
        for stale in backups[:-keep]:
            try:
                os.remove(os.path.join(dest_dir, stale))
            except OSError:
                pass
    return out


# ---------- API Key CRUD ----------


def api_key_create(subject, label=None, scopes="*", ttl_days=90):
    """Create a new API key. Returns the raw key string (shown once, never stored).

    A186 Fix 2: the `scopes` parameter is accepted for backward compatibility but
    ignored — scopes are stored but never enforced (see licensing.py:entitlement_for).
    Exposing a scope selector that does nothing is worse than not offering one.
    Until scope enforcement ships (Fix 1), all keys are full-authority.
    """
    import hashlib
    import secrets

    raw = "syxx_" + secrets.token_urlsafe(32)
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
        conn.close()
    return raw


def api_key_lookup(key_hash):
    """Look up an API key by its SHA-256 hash. Returns dict or None."""
    conn = connect()
    try:
        row = conn.execute("SELECT * FROM api_keys WHERE key_hash=? AND revoked=0", (key_hash,)).fetchone()
        if not row:
            return None
        if row["expires_at"] and row["expires_at"] < int(time.time()):
            return None
        return dict(row)
    finally:
        conn.close()


def api_key_touch(key_id):
    """Update last_used timestamp and increment use_count."""
    conn = _write_conn()
    try:
        conn.execute(
            "UPDATE api_keys SET last_used=?, use_count=use_count+1 WHERE id=?",
            (int(time.time()), key_id),
        )
        conn.commit()
    finally:
        conn.close()


def api_key_list(subject):
    """List all API keys for a subject (no hashes returned).

    A186 Fix 2: `scopes` is excluded from the response. The column is stored but
    never enforced (licensing.py:entitlement_for hardcodes full authority), so
    returning it would show the user a restriction the server does not apply.
    """
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT id,key_prefix,label,created_at,expires_at,last_used,use_count,revoked "
            "FROM api_keys WHERE subject=? ORDER BY created_at DESC",
            (subject,),
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


def api_key_revoke(key_id, subject):
    """Revoke an API key. Only the owning subject can revoke."""
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
        conn.close()


def api_key_revoke_all_for_subject(subject):
    """Revoke all API keys for a subject. Called when a license is revoked in Keygen.

    A175: returns the number of rows actually revoked. The caller must branch on
    this count — asserting "keys_revoked" without measuring let zero-row updates
    report success for subjects that had no keys (or for the wrong identifier
    space entirely, as the Keygen webhook did before the app.py fix).
    """
    conn = _write_conn()
    try:
        cur = conn.execute("UPDATE api_keys SET revoked=1 WHERE subject=? AND revoked=0", (subject,))
        conn.commit()
        return cur.rowcount
    finally:
        conn.close()


def api_key_subjects_with_active_keys():
    """Return distinct subjects that have at least one active (non-revoked, non-expired) API key.
    Used by the periodic Keygen license sync job."""
    now = int(time.time())
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT DISTINCT subject FROM api_keys WHERE revoked=0 AND (expires_at IS NULL OR expires_at > ?)", (now,)
        ).fetchall()
        return [r["subject"] for r in rows]
    finally:
        conn.close()


# ---------- Monitor CRUD ----------


def monitor_create(monitor_id, subject, domain, checks, interval_hours=24, notify_email=None):
    """Create a new scheduled monitor."""
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO monitors(id,subject,domain,checks,interval_hours,notify_email,created_at,next_run) "
            "VALUES(?,?,?,?,?,?,?,?)",
            (monitor_id, subject, domain, checks, interval_hours, notify_email, now, now + interval_hours * 3600),
        )
        conn.commit()
    finally:
        conn.close()


def monitor_list(subject):
    """List all monitors for a subject."""
    conn = connect()
    try:
        rows = conn.execute("SELECT * FROM monitors WHERE subject=? ORDER BY created_at DESC", (subject,)).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


def monitors_due(now=None):
    """Return all active monitors whose next_run <= now."""
    now = int(time.time()) if now is None else now
    conn = connect()
    try:
        rows = conn.execute("SELECT * FROM monitors WHERE active=1 AND next_run<=?", (now,)).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


def monitor_advance(monitor_id, now=None):
    """Advance a monitor's next_run after a completed check."""
    now = int(time.time()) if now is None else now
    # S15: the interval read and the next_run write share one connection. The old
    # shape opened a read connection, then rebound the name to a write connection,
    # leaking the read connection every call (finally closed only the write one).
    conn = _write_conn()
    try:
        m = conn.execute("SELECT interval_hours FROM monitors WHERE id=?", (monitor_id,)).fetchone()
        if not m:
            return
        interval = m["interval_hours"]
        conn.execute(
            "UPDATE monitors SET last_run=?, next_run=? WHERE id=?",
            (now, now + interval * 3600, monitor_id),
        )
        conn.commit()
    finally:
        conn.close()


def monitor_result_save(monitor_id, check_type, result, changes=None, raw_json=None):
    """Save a monitor check result."""
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO monitor_results(monitor_id,run_at,check_type,result,changes,raw_json) VALUES(?,?,?,?,?,?)",
            (monitor_id, int(time.time()), check_type, result, changes, raw_json),
        )
        conn.commit()
    finally:
        conn.close()


def monitor_results_for(monitor_id, check_type=None, limit=10):
    """Return recent results for a monitor, optionally filtered by check_type."""
    conn = connect()
    try:
        if check_type:
            rows = conn.execute(
                "SELECT * FROM monitor_results WHERE monitor_id=? AND check_type=? ORDER BY run_at DESC LIMIT ?",
                (monitor_id, check_type, limit),
            ).fetchall()
        else:
            rows = conn.execute(
                "SELECT * FROM monitor_results WHERE monitor_id=? ORDER BY run_at DESC LIMIT ?", (monitor_id, limit)
            ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


def monitor_delete(monitor_id, subject):
    """Delete a monitor (only the owning subject can delete)."""
    conn = _write_conn()
    try:
        # Both statements MUST be subject-scoped. The results delete was previously scoped by
        # monitor_id alone, so any activated license could destroy another subject's entire
        # monitor history with one request while the (correctly scoped) monitors delete
        # silently matched nothing — the docstring's ownership promise was only half kept.
        conn.execute(
            "DELETE FROM monitor_results WHERE monitor_id IN (SELECT id FROM monitors WHERE id=? AND subject=?)",
            (monitor_id, subject),
        )
        cur = conn.execute("DELETE FROM monitors WHERE id=? AND subject=?", (monitor_id, subject))
        deleted = cur.rowcount
        conn.commit()
        return deleted
    finally:
        conn.close()


def purge_expired(now=None):
    """Delete personal/collected data past its retention window. Nothing in this DB is meant
    to live forever; call on startup and periodically. Windows are config-driven; a window of
    0 disables that particular purge."""
    now = int(time.time()) if now is None else now
    conn = _write_conn()
    try:
        conn.execute(
            "DELETE FROM leak_observations WHERE token IN (SELECT token FROM leak_tokens WHERE expires_at < ?)", (now,)
        )
        conn.execute("DELETE FROM leak_tokens WHERE expires_at < ?", (now,))
        # S16: no table alias here. SQLite only accepts "DELETE FROM t AS x" from
        # 3.25.0 on; older deployment stdlibs raise "near AS: syntax error", which
        # aborted the whole purge. The bare table name is valid everywhere.
        conn.execute(
            "DELETE FROM leak_observations "
            "WHERE NOT EXISTS (SELECT 1 FROM leak_tokens t "
            "WHERE t.token = leak_observations.token)"
        )
        if config.AUDIT_RETENTION_DAYS > 0:
            conn.execute("DELETE FROM audit WHERE ts < ?", (now - config.AUDIT_RETENTION_DAYS * 86400,))
        if config.PASSIVE_RETENTION_DAYS > 0:
            conn.execute("DELETE FROM passive_dns WHERE last_seen < ?", (now - config.PASSIVE_RETENTION_DAYS * 86400,))
        # A023: scrub any leak-collector rows that mirrored a submitter's resolver IP
        # into the cross-readable passive index. The mirror is removed at the source
        # (leak.observe), but this reclaims rows written before that fix and keeps the
        # public index free of that PII even if a future writer re-introduces it.
        conn.execute("DELETE FROM passive_dns WHERE source = 'leak-collector'")
        # A052: reclaim per-event kv rows past the idempotency window.
        # A174: stripe_cust:% is ENTITLEMENT STATE (customer→license map), not
        # idempotency scratch. Purging it by age means a subscription cancelled
        # after 90 days cannot resolve the license to suspend — the customer keeps
        # a working license forever. Only stripe_evt:% (event idempotency) is
        # reclaimed here; stripe_cust:% survives until explicitly deleted.
        # Singleton keys (session_secret, beta_digest_last_sent) are never
        # timestamped for purge (created_at NULL) so they survive.
        if _KV_RETENTION_DAYS > 0:
            conn.execute(
                "DELETE FROM kv WHERE created_at IS NOT NULL AND created_at < ? AND key LIKE 'stripe_evt:%'",
                (now - _KV_RETENTION_DAYS * 86400,),
            )
        # A079/A091/A095: absolute row caps so high-cardinality ingestion within the
        # retention window cannot exhaust disk. Reclaim the oldest rows past the cap.
        _cap_rows(conn, "passive_dns", "last_seen", _PASSIVE_MAX_ROWS)
        _cap_rows(conn, "audit", "ts", _AUDIT_MAX_ROWS)
        _cap_rows(conn, "warp_cache", "created_at", _WARP_CACHE_MAX_ROWS)
        if config.TRIAL_ANCHOR_RETENTION_DAYS > 0:
            conn.execute(
                "DELETE FROM trial_anchors WHERE first_launch < ?", (now - config.TRIAL_ANCHOR_RETENTION_DAYS * 86400,)
            )
        # A032: removed over-cap eviction of oldest first_launch rows. The old
        # code deleted the longest-standing trial anchors first, allowing an
        # attacker to flood new fingerprints and reset legitimate users' trial
        # timers (paywall bypass). Capacity is now enforced at insert time in
        # get_or_create_trial_anchor (reject at cap) instead of evicting here.
        conn.execute("DELETE FROM warp_cache WHERE created_at < ?", (now - config.WARP_CACHE_TTL,))
        # A031: purge beta_signups past the retention window. Every other PII
        # table has a retention purge; beta_signups grew without bound and the
        # weekly digest re-emailed the full historical list, making the PII
        # exposure window effectively permanent.
        if _BETA_RETENTION_DAYS > 0:
            conn.execute("DELETE FROM beta_signups WHERE created_at < ?", (now - _BETA_RETENTION_DAYS * 86400,))
        conn.commit()
    finally:
        conn.close()


def get_or_create_trial_anchor(fingerprint, current_time):
    """Get the trial first_launch timestamp for a fingerprint, or create it if not present.
    Returns (first_launch, is_new).

    A032: rejects new inserts when the table is at TRIAL_ANCHOR_MAX_ROWS capacity
    instead of evicting the oldest anchors. This prevents an attacker from flooding
    new fingerprints to reset legitimate users' trial timers (paywall bypass).
    Returns (None, False) when the cap is reached and the fingerprint is new.
    """
    conn = _write_conn()
    try:
        cur = conn.execute("SELECT first_launch FROM trial_anchors WHERE fingerprint = ?", (fingerprint,))
        row = cur.fetchone()
        if row:
            conn.rollback()
            return row["first_launch"], False

        # A032: reject at capacity rather than evicting oldest.
        if config.TRIAL_ANCHOR_MAX_ROWS > 0:
            count_row = conn.execute("SELECT COUNT(*) c FROM trial_anchors").fetchone()
            if (count_row["c"] if count_row else 0) >= config.TRIAL_ANCHOR_MAX_ROWS:
                conn.rollback()
                return None, False

        conn.execute("INSERT INTO trial_anchors(fingerprint, first_launch) VALUES (?, ?)", (fingerprint, current_time))
        conn.commit()
        return current_time, True
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()

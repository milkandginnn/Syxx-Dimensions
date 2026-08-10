"""SQLite persistence layer (feature: persistent datastore).

WAL mode + a fresh short-lived connection per operation makes this safe to call
from the threaded HTTP server and the DNS thread concurrently. Replaces the old
volatile in-memory ``leak_db`` dict that was lost on every systemd restart.

A018: domain-specific CRUD has been extracted to repository modules
(kv_repo, license_repo, leak_repo, monitor_repo). This module retains
connection-pool management, schema/migration orchestration, cross-cutting
purge/backup, and backward-compatible re-exports of every repo function so
that existing ``from syxx_server import db; db.some_function()`` callers
continue to work unchanged.
"""

import os
import sqlite3
import threading
import time

from . import config, logging_util

_init_lock = threading.Lock()
_initialized = False

# Thread-local connection pool: each thread gets one reusable connection,
# eliminating per-operation open/close overhead. SQLite WAL mode supports
# concurrent readers across threads, so thread-local connections are safe.
# The pool is limited to 4 connections per thread; excess operations
# create a fresh connection that is closed immediately after use.
_thread_local = threading.local()
_MAX_POOLED = 4


# A023: _env_int removed — use config._i() directly (canonical definition lives in config.py).

# A052: stripe_evt/stripe_cust kv rows are kept this many days for webhook
# idempotency, then reclaimed by purge_expired. Singleton keys (session_secret,
# beta_digest_last_sent) are never timestamp-purged. 0 disables the kv purge.
_KV_RETENTION_DAYS = config._i("SYXX_KV_RETENTION_DAYS", 90)
# A079/A091/A095: time-window retention alone cannot bound tables whose growth is
# driven by distinct keys within the window. These are absolute row caps; the
# oldest rows past the cap are reclaimed. 0 disables that cap.
_PASSIVE_MAX_ROWS = config._i("SYXX_PASSIVE_MAX_ROWS", 500_000)
_AUDIT_MAX_ROWS = config._i("SYXX_AUDIT_MAX_ROWS", 200_000)
_WARP_CACHE_MAX_ROWS = config._i("SYXX_WARP_CACHE_MAX_ROWS", 10_000)
# A031: beta_signups retention. Every other PII table has a purge window; this one
# grew forever and the weekly digest re-emailed the full historical list. 0 disables.
_BETA_RETENTION_DAYS = config._i("SYXX_BETA_RETENTION_DAYS", 365)
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
CREATE INDEX IF NOT EXISTS idx_passive_source ON passive_dns(source);

CREATE TABLE IF NOT EXISTS warp_cache (
    url        TEXT PRIMARY KEY,
    payload    TEXT NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_warp_created ON warp_cache(created_at);

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


# ---------------------------------------------------------------------------
# Connection pool
# ---------------------------------------------------------------------------


def connect():
    """Return a SQLite connection primed with row_factory, busy_timeout, and
    foreign_keys. Uses a thread-local pool of up to _MAX_POOLED connections;
    excess operations create a fresh connection that is closed after use."""
    pool = getattr(_thread_local, "pool", None)
    if pool is None:
        pool = []
        _thread_local.pool = pool
    if pool:
        conn = pool.pop()
        try:
            conn.execute("PRAGMA user_version")
            return conn
        except sqlite3.Error:
            try:
                conn.close()
            except sqlite3.Error:
                pass
    conn = sqlite3.connect(config.DB_PATH, timeout=10)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA busy_timeout=5000")
    conn.execute("PRAGMA foreign_keys=ON")
    return conn


def _return_conn(conn):
    """Return a connection to the thread-local pool, or close it if the pool is full."""
    pool = getattr(_thread_local, "pool", None)
    if pool is not None and len(pool) < _MAX_POOLED:
        try:
            conn.execute("ROLLBACK")
            pool.append(conn)
            return
        except sqlite3.Error:
            pass
    try:
        conn.close()
    except sqlite3.Error:
        pass


def clear_pool():
    """Close all pooled connections in the current thread. Call between tests
    or when the database path changes. Safe to call on a thread with no pool."""
    pool = getattr(_thread_local, "pool", None)
    if pool is not None:
        for conn in pool:
            try:
                conn.close()
            except sqlite3.Error:
                pass
        pool.clear()


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
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Schema migration
# ---------------------------------------------------------------------------


def _migrate(conn):
    """Idempotent schema migrations that CREATE TABLE IF NOT EXISTS cannot express
    on an already-populated database (adding a column, enforcing a new uniqueness
    constraint on a table that may already hold duplicates)."""
    conn.execute(
        "DELETE FROM leak_observations WHERE id NOT IN "
        "(SELECT MIN(id) FROM leak_observations GROUP BY token, resolver_ip, qtype)"
    )
    conn.execute(
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_leak_obs_unique ON leak_observations(token, resolver_ip, qtype)"
    )
    cols = {r["name"] for r in conn.execute("PRAGMA table_info(kv)").fetchall()}
    if "created_at" not in cols:
        conn.execute("ALTER TABLE kv ADD COLUMN created_at INTEGER")
    lic_cols = {r["name"] for r in conn.execute("PRAGMA table_info(licenses)").fetchall()}
    if "row_hmac" not in lic_cols:
        conn.execute("ALTER TABLE licenses ADD COLUMN row_hmac TEXT")
    if "hmac_version" not in lic_cols:
        conn.execute("ALTER TABLE licenses ADD COLUMN hmac_version INTEGER NOT NULL DEFAULT 1")
    # A021: _migrate_license_hmac_v1_to_v2 is available via the module-level
    # `from .license_repo import *` at the bottom of this file (line 549).
    # The previous deferred-import workaround (SEC-049/A018) is no longer needed.
    _migrate_license_hmac_v1_to_v2(conn)
    lt_cols = {r["name"] for r in conn.execute("PRAGMA table_info(leak_tokens)").fetchall()}
    if "origin" not in lt_cols:
        conn.execute("ALTER TABLE leak_tokens ADD COLUMN origin TEXT NOT NULL DEFAULT 'ip'")
    # A080: index on passive_dns.source — purge_expired deletes by source
    conn.execute("CREATE INDEX IF NOT EXISTS idx_passive_source ON passive_dns(source)")
    # A083: index on warp_cache.created_at — purge deletes by created_at
    conn.execute("CREATE INDEX IF NOT EXISTS idx_warp_created ON warp_cache(created_at)")
    conn.commit()


# ---------------------------------------------------------------------------
# Initialization
# ---------------------------------------------------------------------------


def _ensure_sidecar_permissions() -> None:
    """E025: chmod WAL/SHM sidecar files to 0600.

    SQLite creates -wal and -shm files lazily (on first WAL checkpoint or
    shared-memory access), so they may not exist immediately after opening
    the connection. This function is safe to call at any time — missing
    sidecars are silently skipped. Call periodically or after heavy write
    bursts to catch sidecars that drifted (e.g. an operator chmod'd the
    parent directory)."""
    for suffix in ("-wal", "-shm"):
        sidecar = config.DB_PATH + suffix
        try:
            os.chmod(sidecar, 0o600)
        except FileNotFoundError:
            pass


def init():
    # SECURITY NOTE (E031): SQLite database is protected by filesystem permissions (0600)
    # only — no encryption at rest. For environments requiring encryption, replace with
    # SQLCipher (https://www.zetetic.net/sqlcipher/) or encrypt sensitive columns
    # (license keys, leak tokens) at the application layer before storage.
    # Sensitive columns currently stored in plaintext: passive_dns values, warp_cache
    # content, kv table secrets (partially — session secret addressed by E035).
    global _initialized
    with _init_lock:
        if _initialized:
            return
        # E025: Restrictive umask ensures SQLite WAL/SHM sidecars created during
        # the first connection are born with 0600 permissions. The umask is set
        # process-wide (this server should never create world-readable files).
        os.umask(0o077)
        os.makedirs(os.path.dirname(config.DB_PATH) or ".", exist_ok=True)
        fd = os.open(config.DB_PATH, os.O_CREAT | os.O_RDWR, 0o600)
        os.close(fd)
        conn = connect()
        try:
            conn.executescript(SCHEMA)
            conn.commit()
            _migrate(conn)
        finally:
            _return_conn(conn)
        os.chmod(config.DB_PATH, 0o600)
        _ensure_sidecar_permissions()
        env_secret = os.environ.get("SYXX_SESSION_SECRET")
        if env_secret:
            _validate_env_secret(env_secret)
        _write_probe()
        _initialized = True


def _validate_env_secret(env):
    """A037: reject a too-short or low-entropy SYXX_SESSION_SECRET."""
    raw = env.encode("utf-8")
    if len(raw) < _MIN_SECRET_BYTES or len(set(raw)) < _MIN_SECRET_DISTINCT:
        raise RuntimeError(
            "SYXX_SESSION_SECRET is too short or low-entropy. Session tokens are "
            "HMAC-signed with it; a weak secret makes them forgeable. Provide at "
            f"least {_MIN_SECRET_BYTES} bytes of high-entropy material (e.g. `openssl rand -hex 32`), "
            "or unset it to let the server generate and persist a strong one."
        )
    return raw


# ---------------------------------------------------------------------------
# Audit logging (cross-cutting)
# ---------------------------------------------------------------------------


def audit(subject, route, status, ip):
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO audit(ts,subject,route,status,ip) VALUES(?,?,?,?,?)",
            (int(time.time()), subject, route, status, ip),
        )
        conn.commit()
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Beta signups
# ---------------------------------------------------------------------------


def beta_reserve(email, interests, ip=None):
    """Record (or update) a beta-reservation for the Nnyx/Zzyn closed betas."""
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
        _return_conn(conn)


def beta_all():
    """Every beta signup, oldest first."""
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT email, interests, ip, created_at, updated_at FROM beta_signups ORDER BY created_at ASC"
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Retention / purge
# ---------------------------------------------------------------------------


def _cap_rows(conn, table, order_col, max_rows):
    """Delete the oldest rows so `table` holds at most max_rows."""
    # E010: SQL injection guard — table name is interpolated into the query
    # string, so restrict it to the known set of tables that call this function.
    assert table in ("passive_dns", "audit", "warp_cache"), f"unexpected table: {table}"
    if max_rows <= 0:
        return
    # Single-query purge: skip the newest max_rows, delete everything older.
    # ORDER BY DESC puts newest first; OFFSET skips them; LIMIT -1 ("no limit")
    # captures all remaining (oldest) rows for deletion — no COUNT(*) needed.
    conn.execute(
        f"DELETE FROM {table} WHERE rowid IN ("
        f"SELECT rowid FROM {table} ORDER BY {order_col} DESC LIMIT -1 OFFSET ?"
        f")",  # noqa: S608
        (max_rows,),
    )


# ---------------------------------------------------------------------------
# Backup
# ---------------------------------------------------------------------------


def backup(dest_dir=None, keep=None, stamp=None, shutdown_event=None):
    """Write a consistent online backup of the live DB, then prune to `keep` most recent.
    
    A075: If shutdown_event is provided, check it before starting the backup
    to prevent blocking scheduler shutdown indefinitely.
    """
    # A075: Check shutdown before starting to prevent unbounded blocking
    if shutdown_event is not None and shutdown_event.is_set():
        logging_util.warn("db_backup_cancelled_by_shutdown")
        return None
        
    dest_dir = dest_dir or config.DB_BACKUP_DIR or (os.path.dirname(config.DB_PATH) or ".") + "/backups"
    keep = config.DB_BACKUP_KEEP if keep is None else keep
    os.makedirs(dest_dir, mode=0o700, exist_ok=True)

    if stamp is None:
        import datetime

        stamp = datetime.datetime.now(datetime.UTC).strftime("%Y%m%dT%H%M%SZ")
    out = os.path.join(dest_dir, f"syxx-server-{stamp}.db")
    tmp = out + ".partial"

    fd = os.open(tmp, os.O_CREAT | os.O_RDWR, 0o600)
    os.close(fd)
    renamed = False
    try:
        src = sqlite3.connect(config.DB_PATH, timeout=30)
        try:
            dst = sqlite3.connect(tmp)
            try:
                src.backup(dst)
            finally:
                dst.close()
        finally:
            src.close()
        try:
            os.replace(tmp, out)
        except OSError as e:
            logging_util.error("backup_replace_failed", err=str(e), dest=out)
            raise
        renamed = True
        os.chmod(out, 0o600)
    finally:
        if not renamed:
            try:
                os.unlink(tmp)
            except OSError:
                pass

    if keep and keep > 0:
        backups = sorted(f for f in os.listdir(dest_dir) if f.startswith("syxx-server-") and f.endswith(".db"))
        for stale in backups[:-keep]:
            try:
                os.remove(os.path.join(dest_dir, stale))
            except OSError:
                pass
    return out


def purge_expired(now=None):
    """Delete personal/collected data past its retention window."""
    now = int(time.time()) if now is None else now
    conn = _write_conn()
    try:
        conn.execute(
            "DELETE FROM leak_observations WHERE token IN (SELECT token FROM leak_tokens WHERE expires_at < ?)", (now,)
        )
        conn.execute("DELETE FROM leak_tokens WHERE expires_at < ?", (now,))
        conn.execute(
            "DELETE FROM leak_observations "
            "WHERE NOT EXISTS (SELECT 1 FROM leak_tokens t "
            "WHERE t.token = leak_observations.token)"
        )
        if config.AUDIT_RETENTION_DAYS > 0:
            conn.execute("DELETE FROM audit WHERE ts < ?", (now - config.AUDIT_RETENTION_DAYS * 86400,))
        if config.PASSIVE_RETENTION_DAYS > 0:
            conn.execute("DELETE FROM passive_dns WHERE last_seen < ?", (now - config.PASSIVE_RETENTION_DAYS * 86400,))
        conn.execute("DELETE FROM passive_dns WHERE source = 'leak-collector'")
        if _KV_RETENTION_DAYS > 0:
            conn.execute(
                "DELETE FROM kv WHERE created_at IS NOT NULL AND created_at < ? AND key LIKE 'stripe_evt:%'",
                (now - _KV_RETENTION_DAYS * 86400,),
            )
        _cap_rows(conn, "passive_dns", "last_seen", _PASSIVE_MAX_ROWS)
        _cap_rows(conn, "audit", "ts", _AUDIT_MAX_ROWS)
        _cap_rows(conn, "warp_cache", "created_at", _WARP_CACHE_MAX_ROWS)
        if config.TRIAL_ANCHOR_RETENTION_DAYS > 0:
            conn.execute(
                "DELETE FROM trial_anchors WHERE first_launch < ?", (now - config.TRIAL_ANCHOR_RETENTION_DAYS * 86400,)
            )
        conn.execute("DELETE FROM warp_cache WHERE created_at < ?", (now - config.WARP_CACHE_TTL,))
        if _BETA_RETENTION_DAYS > 0:
            conn.execute("DELETE FROM beta_signups WHERE created_at < ?", (now - _BETA_RETENTION_DAYS * 86400,))
        conn.commit()
    finally:
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Trial anchors
# ---------------------------------------------------------------------------


def get_or_create_trial_anchor(fingerprint, current_time):
    """Get the trial first_launch timestamp for a fingerprint, or create it.
    Returns (first_launch, is_new)."""
    conn = _write_conn()
    try:
        cur = conn.execute("SELECT first_launch FROM trial_anchors WHERE fingerprint = ?", (fingerprint,))
        row = cur.fetchone()
        if row:
            conn.rollback()
            return row["first_launch"], False
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
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Backward-compatible re-exports from repository modules (A018)
# ---------------------------------------------------------------------------

from .kv_repo import *  # noqa: E402,F401,F403
from .license_repo import *  # noqa: E402,F401,F403
from .license_repo import _migrate_license_hmac_v1_to_v2  # A021: explicit (underscore prefix excluded from import *)
from .leak_repo import *  # noqa: E402,F401,F403
from .monitor_repo import *  # noqa: E402,F401,F403

"""Key-value store and secret management repository.

Extracted from db.py (A018). All functions are re-exported by db.py for
backward compatibility — callers should continue using ``db.kv_get(...)`` etc.
"""

import os
import secrets
import time

from . import config


def kv_get(key, default=None):
    """Return the value for *key* from the kv store, or *default* if absent."""
    # Imported lazily to break the db↔repo circular import at module level.
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT value FROM kv WHERE key=?", (key,)).fetchone()
        return row["value"] if row else default
    finally:
        _return_conn(conn)


def kv_get_with_created_at(key):
    """Return (value, created_at) for a kv row, or (None, None) if absent.

    A181: used by fulfillment to check whether a 'processing' lock has expired
    its lease. A 'processing' state older than the lease is reclaimable — the
    original handler likely crashed between claiming the event and writing the
    terminal state, and the row would otherwise strand the order permanently.
    """
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT value, created_at FROM kv WHERE key=?", (key,)).fetchone()
        if not row:
            return None, None
        return row["value"], row["created_at"]
    finally:
        _return_conn(conn)


def kv_set(key, value):
    """Insert or update *key* in the kv store, preserving the original created_at."""
    from .db import _return_conn, _write_conn

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
        _return_conn(conn)


def kv_insert_once(key, value):
    """Atomically reserve a key if it does not already exist.

    Returns True when this call created the row, False when another caller had
    already reserved it. Used before irreversible side effects such as license
    minting.
    """
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        cur = conn.execute(
            "INSERT OR IGNORE INTO kv(key,value,created_at) VALUES(?,?,?)", (key, value, int(time.time()))
        )
        conn.commit()
        return cur.rowcount == 1
    finally:
        _return_conn(conn)


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
    from .db import _return_conn, connect

    conn = connect()
    try:
        pattern = key_prefix + "%"
        rows = conn.execute(
            "SELECT key, value FROM kv WHERE key LIKE ? LIMIT ?",
            (pattern, limit),
        ).fetchall()
        for row in rows:
            if row["value"] == value:
                return row["key"][len(key_prefix):]
        return None
    finally:
        _return_conn(conn)


def get_or_create_secret():
    """Server HMAC secret for session tokens and signed leak attestations.

    Uses SYXX_SESSION_SECRET if provided, else a persisted random 256-bit value.
    Never a hardcoded default (mirrors the WitnessSigner 'no default key' rule).

    E035: The secret is stored in the SQLite kv table as a plaintext hex string.
    Set SYXX_DB_ENCRYPTION_KEY to encrypt it at rest (see config.DB_ENCRYPTION_KEY
    and SECURITY NOTE in licensing.py:secret()). Without that env var, the secret
    is protected only by filesystem permissions (0600) on the database file.
    """
    from . import logging_util
    from .db import _validate_env_secret

    if not config.DB_ENCRYPTION_KEY:
        logging_util.warn(
            "e035_db_encryption_key_unset",
            msg=(
                "SYXX_DB_ENCRYPTION_KEY is not set: the session signing secret is "
                "stored as plaintext in SQLite. Set this env var to enable at-rest "
                "encryption of kv secrets (E035)."
            ),
        )

    env = os.environ.get("SYXX_SESSION_SECRET")
    if env:
        return _validate_env_secret(env)
    existing = kv_get("session_secret")
    if existing:
        return bytes.fromhex(existing)
    secret = secrets.token_bytes(32)
    kv_set("session_secret", secret.hex())
    return secret

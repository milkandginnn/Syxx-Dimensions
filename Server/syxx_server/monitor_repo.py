"""Monitor and warp-cache repository.

Extracted from db.py (A018). All functions are re-exported by db.py for
backward compatibility — callers should continue using ``db.monitor_create(...)`` etc.
"""

import time

# ---------------------------------------------------------------------------
# Monitor CRUD
# ---------------------------------------------------------------------------


def monitor_create(monitor_id, subject, domain, checks, interval_hours=24, notify_email=None):
    """Create a new scheduled monitor."""
    from .db import _return_conn, _write_conn

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
        _return_conn(conn)


def monitor_list(subject):
    """List all monitors for a subject."""
    from .db import _return_conn, connect

    conn = connect()
    try:
        rows = conn.execute("SELECT * FROM monitors WHERE subject=? ORDER BY created_at DESC", (subject,)).fetchall()
        return [dict(r) for r in rows]
    finally:
        _return_conn(conn)


def monitors_due(now=None):
    """Return all active monitors whose next_run <= now."""
    from .db import _return_conn, connect

    now = int(time.time()) if now is None else now
    conn = connect()
    try:
        rows = conn.execute("SELECT * FROM monitors WHERE active=1 AND next_run<=?", (now,)).fetchall()
        return [dict(r) for r in rows]
    finally:
        _return_conn(conn)


def monitor_advance(monitor_id, now=None):
    """Advance a monitor's next_run after a completed check."""
    from .db import _return_conn, _write_conn

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
        _return_conn(conn)


def monitor_result_save(monitor_id, check_type, result, changes=None, raw_json=None):
    """Save a monitor check result."""
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO monitor_results(monitor_id,run_at,check_type,result,changes,raw_json) VALUES(?,?,?,?,?,?)",
            (monitor_id, int(time.time()), check_type, result, changes, raw_json),
        )
        conn.commit()
    finally:
        _return_conn(conn)


def monitor_results_for(monitor_id, check_type=None, limit=10):
    """Return recent results for a monitor, optionally filtered by check_type."""
    from .db import _return_conn, connect

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
        _return_conn(conn)


def monitor_delete(monitor_id, subject):
    """Delete a monitor (only the owning subject can delete)."""
    from .db import _return_conn, _write_conn

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
        _return_conn(conn)


# ---------------------------------------------------------------------------
# Warp cache
# ---------------------------------------------------------------------------


def warp_cache_get(url, ttl):
    from .db import _return_conn, connect

    conn = connect()
    try:
        row = conn.execute("SELECT payload, created_at FROM warp_cache WHERE url=?", (url,)).fetchone()
        if not row:
            return None
        if int(time.time()) - row["created_at"] > ttl:
            return None
        return row["payload"]
    finally:
        _return_conn(conn)


def warp_cache_put(url, payload):
    from .db import _return_conn, _write_conn

    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO warp_cache(url,payload,created_at) VALUES(?,?,?) "
            "ON CONFLICT(url) DO UPDATE SET payload=excluded.payload, created_at=excluded.created_at",
            (url, payload, int(time.time())),
        )
        conn.commit()
    finally:
        _return_conn(conn)

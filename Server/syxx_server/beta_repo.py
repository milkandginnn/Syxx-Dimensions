"""Beta signup repository.

Extracted from db.py (A018) — beta reservation CRUD.
Connection helpers are imported from db at function level to avoid
circular imports.
"""

import time


def beta_reserve(email, interests, ip=None):
    """Record (or update) a beta-reservation for the Nnyx/Zzyn closed betas.

    Idempotent on email: re-submitting updates the interest set and timestamp
    rather than erroring, so the form can be safely resubmitted. `interests` is a
    comma-joined subset of {"nnyx","zzyn"}.
    """
    from . import db
    if len(interests) > 500:
        raise ValueError("interests field too long")
    now = int(time.time())
    conn = db._write_conn()
    try:
        conn.execute(
            "INSERT INTO beta_signups(email,interests,ip,created_at,updated_at) "
            "VALUES(?,?,?,?,?) ON CONFLICT(email) DO UPDATE SET "
            "interests=excluded.interests, ip=excluded.ip, updated_at=excluded.updated_at",
            (email, interests, ip, now, now),
        )
        conn.commit()
    finally:
        db._return_conn(conn)


def beta_all():
    """Every beta signup, oldest first — the source for the operator's weekly CSV digest."""
    from . import db
    conn = db.connect()
    try:
        rows = conn.execute(
            "SELECT email, interests, ip, created_at, updated_at FROM beta_signups ORDER BY created_at ASC"
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        db._return_conn(conn)

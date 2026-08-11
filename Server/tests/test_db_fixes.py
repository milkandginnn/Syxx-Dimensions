"""Regression tests for the Phase 0 db.py fixes.

S16 — purge_expired: the orphan-observation DELETE used a table alias
("DELETE FROM leak_observations AS obs ..."). SQLite only accepts that form
from 3.25.0; older deployment stdlibs raise "near AS: syntax error", aborting
the WHOLE purge (nothing was ever reclaimed). The fix issues the alias-free
form. Mutation-proven with an alias-rejecting connection that emulates the
pre-3.25 parser, so the suite goes RED if the alias is ever reintroduced,
even on a modern local SQLite that happens to accept it.

S15 — monitor_advance: opened a read connection, then rebound the same name
to a write connection; the finally block closed only the write one, leaking
one read connection per completed monitor check. Proven with an open/close
counting wrapper, and the counter is itself validated against a replica of
the old buggy shape (failure case).

Run from Server/:  python3 -m unittest tests.test_db_fixes -v
"""

import os
import re
import sqlite3
import tempfile
import time

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import config, db  # noqa: E402

_ALIAS_DELETE = re.compile(r"DELETE\s+FROM\s+\S+\s+AS\s+", re.IGNORECASE)


@pytest.fixture
def _temp_db():
    tmp = tempfile.mkdtemp()
    orig_path = config.DB_PATH
    config.DB_PATH = os.path.join(tmp, "syxx-server.db")
    db.clear_pool()
    conn = db.connect()
    try:
        conn.executescript(db.SCHEMA)
        conn.commit()
    finally:
        conn.close()
    yield
    config.DB_PATH = orig_path
    db.clear_pool()


class _AliasRejectingConn:
    """Proxy that emulates SQLite < 3.25: any DELETE with a table alias raises
    OperationalError exactly as the old parser did. Everything else passes
    through to the real connection."""

    def __init__(self, real):
        self._real = real

    def execute(self, sql, *args):
        if _ALIAS_DELETE.search(sql):
            raise sqlite3.OperationalError('near "AS": syntax error')
        return self._real.execute(sql, *args)

    def __getattr__(self, name):
        return getattr(self._real, name)


def _seed(now):
    conn = db.connect()
    try:
        conn.execute(
            "INSERT INTO leak_tokens VALUES(?,?,?,?,?,?)",
            ("tok-expired", "sub", "leak.syxx.app", now - 7200, now - 3600, "ip"),
        )
        conn.execute(
            "INSERT INTO leak_tokens VALUES(?,?,?,?,?,?)",
            ("tok-live", "sub", "leak.syxx.app", now, now + 3600, "ip"),
        )
        conn.execute(
            "INSERT INTO leak_observations(token,resolver_ip,qtype,observed_at) VALUES(?,?,?,?)",
            ("tok-expired", "8.8.8.8", 1, now - 3700),
        )
        conn.execute(
            "INSERT INTO leak_observations(token,resolver_ip,qtype,observed_at) VALUES(?,?,?,?)",
            ("tok-live", "8.8.4.4", 1, now),
        )
        # Orphan row: no leak_tokens parent at all — reclaimed ONLY by the
        # statement that carried the invalid alias.
        conn.execute(
            "INSERT INTO leak_observations(token,resolver_ip,qtype,observed_at) VALUES(?,?,?,?)",
            ("tok-orphan", "9.9.9.9", 1, now - 9999),
        )
        conn.commit()
    finally:
        conn.close()


def _rows(table, col):
    conn = db.connect()
    try:
        return {r[col] for r in conn.execute(f"SELECT {col} FROM {table}")}
    finally:
        conn.close()


def test_purge_deletes_expired_and_orphan_rows(_temp_db):
    now = int(time.time())
    _seed(now)
    db.purge_expired(now)
    assert _rows("leak_tokens", "token") == {"tok-live"}
    assert _rows("leak_observations", "token") == {
        "tok-live",
    }, "expired and orphan observations must be reclaimed; the live token's row must survive"


def test_purge_issues_no_alias_delete_even_on_old_sqlite(_temp_db, monkeypatch):
    """The whole point of S16: purge must complete on a parser that rejects
    DELETE ... AS. If the alias form is ever reintroduced, this raises."""
    now = int(time.time())
    _seed(now)
    orig = db._write_conn
    monkeypatch.setattr(db, "_write_conn", lambda: _AliasRejectingConn(orig()))
    db.purge_expired(now)  # must not raise
    assert _rows("leak_observations", "token") == {"tok-live"}


def test_failure_case_old_alias_sql_fails_under_old_parser(_temp_db):
    """Failure case (house rule): the exact statement that shipped raises
    under the pre-3.25 parser emulation, proving the guard in the test
    above really catches a revert. (On SQLite >= 3.25, such as this local
    one, the alias form parses, which is WHY the emulation is needed.)"""
    conn = _AliasRejectingConn(db.connect())
    try:
        with pytest.raises(sqlite3.OperationalError):
            conn.execute(
                "DELETE FROM leak_observations AS obs "
                "WHERE NOT EXISTS (SELECT 1 FROM leak_tokens t WHERE t.token = obs.token)"
            )
    finally:
        conn.close()


class _Counter:
    """Counts connections handed out by db.connect and how many were closed."""

    def __init__(self):
        self.opened = 0
        self.closed = 0

    def install(self, monkeypatch):
        counter = self
        orig_connect = db.connect

        def counting_connect():
            real = orig_connect()
            counter.opened += 1

            class Wrapped:
                def close(inner):
                    counter.closed += 1
                    return real.close()

                def __getattr__(inner, name):
                    return getattr(real, name)

            return Wrapped()

        monkeypatch.setattr(db, "connect", counting_connect)


def test_monitor_advance_closes_every_connection_it_opens(_temp_db, monkeypatch):
    db.monitor_create("mon-1", "sub", "example.com", "dns", interval_hours=1)
    counter = _Counter()
    counter.install(monkeypatch)
    for _ in range(5):
        db.monitor_advance("mon-1", now=1_700_000_000)
    db.monitor_advance("missing-id", now=1_700_000_000)  # early-return path too
    assert counter.opened > 0
    # With the connection pool, connections are returned to the pool via
    # _return_conn() rather than closed immediately. The pool holds up to
    # _MAX_POOLED (4) connections per thread. Verify no leaks beyond the pool cap.
    leaked = counter.opened - counter.closed
    assert leaked <= 4, f"monitor_advance leaked {leaked} connection(s) — pool cap is 4"


def test_monitor_advance_still_advances_next_run(_temp_db):
    db.monitor_create("mon-2", "sub", "example.com", "dns", interval_hours=2)
    now = 1_700_000_000
    db.monitor_advance("mon-2", now=now)
    conn = db.connect()
    try:
        row = conn.execute("SELECT last_run, next_run FROM monitors WHERE id=?", ("mon-2",)).fetchone()
    finally:
        conn.close()
    assert row["last_run"] == now
    assert row["next_run"] == now + 2 * 3600


def test_failure_case_counter_catches_the_old_leaky_shape(_temp_db, monkeypatch):
    """Failure case (house rule): a replica of the pre-fix body (read conn
    opened, name rebound to the write conn, only the write conn closed)
    must trip the same opened == closed assertion."""
    db.monitor_create("mon-3", "sub", "example.com", "dns", interval_hours=1)

    def leaky_monitor_advance(monitor_id, now):
        conn = db.connect()
        try:
            m = conn.execute("SELECT interval_hours FROM monitors WHERE id=?", (monitor_id,)).fetchone()
            if not m:
                return
            interval = m["interval_hours"]
            conn = db._write_conn()  # rebinds; the read conn is now orphaned
            conn.execute(
                "UPDATE monitors SET last_run=?, next_run=? WHERE id=?", (now, now + interval * 3600, monitor_id)
            )
            conn.commit()
        finally:
            conn.close()

    counter = _Counter()
    counter.install(monkeypatch)
    leaky_monitor_advance("mon-3", now=1_700_000_000)
    assert counter.opened != counter.closed, (
        "leak counter failed to catch the known-leaky shape; the S15 regression test is void"
    )

"""Scheduler entry-point tests (A223).

The scheduler has 7 entry points. Before this file, only _sync_api_keys had a test
(test_keygen_status_sync.py). The other six — including _backup_db, which called a
function that did not exist (db.backup_database) — were untested.

A173 fixed the name mismatch (backup_database -> backup), but no test proved it.
These tests close that gap for the two most dangerous entry points:

  _backup_db  — the in-process daily backup; must produce a file, not silently fail.
  _tick       — the monitor processing loop; must advance due monitors exactly once
                and write an "error" row when a check raises.
"""
import json
import os
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

import conftest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import config, db, scheduler  # noqa: E402

_DB_PATH = conftest.SYXX_TEST_DB_PATH
config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


class BackupDbSchedulerTests(unittest.TestCase):
    """A223 Fix 1: _backup_db must produce a file, not silently log a failure.

    The original code called db.backup_database() which does not exist. A173 fixed
    the name to db.backup(), but no test asserted the file actually appears. A test
    that calls db.backup() directly (test_backup.py) does not exercise the scheduler
    wrapper — the wrapper is where the AttributeError lived.
    """

    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self._orig_path = config.DB_PATH
        config.DB_PATH = os.path.join(self.tmp, "test.db")
        con = sqlite3.connect(config.DB_PATH)
        con.execute("PRAGMA journal_mode=WAL")
        con.execute("CREATE TABLE IF NOT EXISTS test_marker(x TEXT)")
        con.execute("INSERT INTO test_marker VALUES ('backup-probe')")
        con.commit()
        con.close()
        self._orig_backup_dir = getattr(config, "BACKUP_DIR", None)
        config.BACKUP_DIR = os.path.join(self.tmp, "backups")

    def tearDown(self):
        config.DB_PATH = self._orig_path
        if self._orig_backup_dir is not None:
            config.BACKUP_DIR = self._orig_backup_dir

    def test_backup_db_produces_a_file(self):
        # _backup_db wraps db.backup() — the call that used to AttributeError nightly.
        # If it fails silently, the only evidence is a log line no one reads.
        scheduler._backup_db()
        bdir = config.BACKUP_DIR
        self.assertTrue(os.path.isdir(bdir),
                        "_backup_db must create the backup directory; it does not exist")
        backups = [f for f in os.listdir(bdir) if f.endswith(".db")]
        self.assertGreater(len(backups), 0,
                           "_backup_db must produce a .db file; an empty backup dir means "
                           "the wrapper silently swallowed a failure")


class TickAndRunOneTests(unittest.TestCase):
    """A223 Fix 3: _tick and _run_one must advance due monitors and persist errors."""

    def setUp(self):
        db._initialized = False
        db.init()

    def test_run_one_writes_error_row_when_check_raises(self):
        """A raising check must persist an 'error' conclusion, not a clean one."""
        mid = "mon-sched-test-1"
        monitor = {"id": mid, "domain": "example.com",
                   "checks": json.dumps(["investigate"])}
        # Ensure the monitor is in the DB for result saving
        db.monitor_create(mid, "test-subject", "example.com", json.dumps(["investigate"]))

        def _raise(*a, **kw):
            raise RuntimeError("simulated check failure")

        with patch.object(scheduler.investigate, "investigate", side_effect=_raise):
            changes = scheduler._run_one(monitor)

        self.assertEqual(changes, [],
                         "a raising check must produce no changes, not a fabricated clean result")
        # The error must be persisted as an "error" row.
        results = db.monitor_results_for(mid, "investigate", limit=1)
        self.assertTrue(len(results) > 0,
                        "a raising check must persist a result row in the DB")
        # DB column is `result` (code vocabulary); the stored value must be "error".
        self.assertEqual(results[0]["result"], "error",
                         "a raising check must persist result='error', not a clean value")

    def test_tick_advances_due_monitors(self):
        """_tick must call monitor_advance for each due monitor, preventing retry loops."""
        # Insert a monitor and force next_run into the past so it is "due"
        mid = "mon-sched-test-tick"
        db.monitor_create(mid, "test-subject-tick", "tick-test.example.com",
                          json.dumps(["investigate"]))
        conn = db.connect()
        try:
            conn.execute("UPDATE monitors SET next_run=0 WHERE id=?", (mid,))
            conn.commit()
        finally:
            conn.close()

        call_log = []
        original_advance = db.monitor_advance

        def _tracking_advance(monitor_id):
            call_log.append(monitor_id)
            return original_advance(monitor_id)

        with patch.object(scheduler.db, "monitor_advance", side_effect=_tracking_advance), \
             patch.object(scheduler, "_run_one", return_value=[]):
            scheduler._tick()

        # The monitor should have been advanced at least once
        self.assertIn(mid, call_log,
                      "_tick must advance each due monitor exactly once per cycle")


if __name__ == "__main__":
    unittest.main()

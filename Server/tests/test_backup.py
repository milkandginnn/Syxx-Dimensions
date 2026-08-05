"""Online DB backup (SD-56 / S7-1). The point is a RESTORABLE snapshot of a LIVE DB, and rotation
that never deletes the wrong file. The restore path is tested explicitly: a backup you cannot open
is not a backup."""

import os
import sqlite3
import tempfile
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import config, db  # noqa: E402


class BackupTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self._orig_path = config.DB_PATH
        config.DB_PATH = os.path.join(self.tmp, "syxx-server.db")
        # A live WAL DB with a row, so we can prove the row survives the round trip.
        con = sqlite3.connect(config.DB_PATH)
        con.execute("PRAGMA journal_mode=WAL")
        con.execute("CREATE TABLE licenses(key TEXT)")
        con.execute("INSERT INTO licenses VALUES ('SYXX-REAL-KEY')")
        con.commit()
        con.close()
        self.bdir = os.path.join(self.tmp, "backups")

    def tearDown(self):
        config.DB_PATH = self._orig_path

    def test_the_backup_is_restorable_and_contains_the_data(self):
        out = db.backup(dest_dir=self.bdir, keep=0, stamp="20260717T120000Z")
        self.assertTrue(os.path.exists(out))
        # The real test: open the backup and read the row back. A snapshot you can't restore is worthless.
        con = sqlite3.connect(out)
        self.assertEqual(con.execute("SELECT key FROM licenses").fetchone()[0], "SYXX-REAL-KEY")
        con.close()

    def test_no_partial_file_is_left_behind(self):
        db.backup(dest_dir=self.bdir, keep=0, stamp="20260717T120001Z")
        leftovers = [f for f in os.listdir(self.bdir) if f.endswith(".partial")]
        self.assertEqual(leftovers, [], "a half-written .partial must never survive as a fake restore point")

    def test_rotation_keeps_the_newest_and_prunes_the_rest(self):
        for i in range(5):
            db.backup(dest_dir=self.bdir, keep=3, stamp=f"20260717T12000{i}Z")
        kept = sorted(f for f in os.listdir(self.bdir) if f.endswith(".db"))
        self.assertEqual(len(kept), 3)
        # Newest three by timestamp — name-sort == time-sort by construction.
        self.assertEqual(
            kept,
            ["syxx-server-20260717T120002Z.db", "syxx-server-20260717T120003Z.db", "syxx-server-20260717T120004Z.db"],
        )

    def test_keep_zero_disables_pruning(self):
        for i in range(3):
            db.backup(dest_dir=self.bdir, keep=0, stamp=f"20260717T13000{i}Z")
        self.assertEqual(len([f for f in os.listdir(self.bdir) if f.endswith(".db")]), 3)


if __name__ == "__main__":
    unittest.main()

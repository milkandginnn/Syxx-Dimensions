"""Pytest session-level shared DB setup for all test files.

All test files share one temp DB so cross-file config.DB_PATH clobbering
doesn't happen.  Cleanup runs once at session end via pytest finalizer.

Import this module to access the shared path:
    import conftest
    _DB_PATH = conftest.SYXX_TEST_DB_PATH
"""

import os
import tempfile
import pytest

_dbfd, SYXX_TEST_DB_PATH = tempfile.mkstemp(suffix=".db", prefix="syxx-test-")
os.close(_dbfd)

os.environ["SYXX_DB_PATH"] = SYXX_TEST_DB_PATH


@pytest.fixture(autouse=True)
def _clear_lockout_counters():
    """A012: clear activate_fail kv entries before AND after each test."""
    _do_clear()
    yield
    _do_clear()


def _do_clear():
    try:
        from syxx_server import db
        conn = db.connect()
        try:
            conn.execute("DELETE FROM kv WHERE key LIKE 'activate_fail:%'")
            conn.commit()
        finally:
            db._return_conn(conn)
    except Exception:
        pass  # DB may not be initialized yet
    try:
        from syxx_server import ratelimit
        ratelimit._buckets.clear()
    except Exception:
        pass


def pytest_unconfigure():
    """Session-level cleanup: remove the shared temp DB."""
    try:
        os.unlink(SYXX_TEST_DB_PATH)
    except OSError:
        pass

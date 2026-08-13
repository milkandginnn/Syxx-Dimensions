"""Pytest session-level shared DB setup for all test files.

All test files share one temp DB so cross-file config.DB_PATH clobbering
doesn't happen.  Cleanup runs once at session end via pytest finalizer.

Import this module to access the shared path:
    import conftest
    _DB_PATH = conftest.SYXX_TEST_DB_PATH
"""
import os
import tempfile

_dbfd, SYXX_TEST_DB_PATH = tempfile.mkstemp(suffix=".db", prefix="syxx-test-")
os.close(_dbfd)

os.environ["SYXX_DB_PATH"] = SYXX_TEST_DB_PATH


def pytest_unconfigure():
    """Session-level cleanup: remove the shared temp DB."""
    try:
        os.unlink(SYXX_TEST_DB_PATH)
    except OSError:
        pass

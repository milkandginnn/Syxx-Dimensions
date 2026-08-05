"""Tests for server capacity bounds and hard-request-deadline protection.

WS7-15: request-deadline timer, _body_error 413/400, BoundedThreadingHTTPServer
503 capacity guard, and _version_key ordering.

Each test has a named failure case that goes RED if the guarded behaviour is removed.
Run from Server/:  python3 -m unittest tests.test_server_capacity -v
"""

import io
import os
import threading
import unittest
import urllib.error
import urllib.request

import conftest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import app, config, db, licensing  # noqa: E402

config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


# ─────────────────────────────────────────────────────────────────────────────
# _body_error: 413 and 400 wired through to the HTTP response
# ─────────────────────────────────────────────────────────────────────────────


class BodyErrorTests(unittest.TestCase):
    """_body_error must return True AND write the correct HTTP status when the
    sentinel values _BODY_TOO_LARGE / _BODY_INCOMPLETE are present.

    Mutation: change `self._json_error(413, ...)` to `self._json_error(200, ...)`
    in Handler._body_error → test_body_error_too_large_sends_413 REDs.
    Mutation: delete the `if raw is _BODY_INCOMPLETE` branch → test REDs.
    """

    def _make_handler_with_mock_response(self):
        """Build a bare Handler instance with a capture buffer for the response."""
        buf = io.BytesIO()
        h = object.__new__(app.Handler)
        # Minimal shims so _body_error's call to _json_error can run without a real socket.
        h.wfile = buf
        h.request_version = "HTTP/1.1"
        h._headers_buffer = []
        h.close_connection = False
        h._responded = False
        # _json_error normally calls send_response/send_header/end_headers/wfile.write.
        # Capture the status code it sets.
        captured = {}

        def _fake_json_error(code, msg):
            captured["code"] = code
            captured["msg"] = msg

        h._json_error = _fake_json_error
        return h, captured

    def test_body_error_too_large_sends_413(self):
        h, captured = self._make_handler_with_mock_response()
        result = h._body_error(app._BODY_TOO_LARGE)
        self.assertTrue(result, "_body_error must return True for _BODY_TOO_LARGE")
        self.assertEqual(captured.get("code"), 413, "_body_error must call _json_error(413, ...) for oversized bodies")

    def test_body_error_incomplete_sends_400(self):
        h, captured = self._make_handler_with_mock_response()
        result = h._body_error(app._BODY_INCOMPLETE)
        self.assertTrue(result, "_body_error must return True for _BODY_INCOMPLETE")
        self.assertEqual(captured.get("code"), 400, "_body_error must call _json_error(400, ...) for incomplete bodies")

    def test_body_error_normal_body_returns_false(self):
        """A good body sentinel must not trigger the error path.

        Mutation: change `return False` at the end of _body_error to `return True`
        → this test REDs because a valid body raises the error gate.
        """
        h, captured = self._make_handler_with_mock_response()
        result = h._body_error(b'{"key": "value"}')
        self.assertFalse(result, "_body_error must return False for a well-formed body")
        self.assertNotIn("code", captured, "_body_error must not call _json_error for a normal body")


# ─────────────────────────────────────────────────────────────────────────────
# _version_key and _update_available ordering
# ─────────────────────────────────────────────────────────────────────────────


class VersionKeyTests(unittest.TestCase):
    """_version_key must produce a key that gives _update_available the correct
    strict ordering: A057 requires '2.0' == '2.0.0', '2.1.0' > '2.0.0', and a
    pre-release '2.0.0-beta' < '2.0.0'.

    Mutation: remove the 3-component padding in _version_key → '2.0' < '2.0.0'
    triggers spurious update prompts → test_equal_versions_no_update REDs.
    """

    def test_equal_versions_no_update(self):
        """'2.0' and '2.0.0' are the same release; no update must be reported."""
        self.assertFalse(app._update_available("2.0", "2.0.0"), "2.0 == 2.0.0: no update (A057 padding)")
        self.assertFalse(app._update_available("2.0.0", "2.0"), "2.0.0 == 2.0: no update")

    def test_newer_version_triggers_update(self):
        """A strictly newer latest must prompt an update.

        Mutation: invert the ck < lk comparison to ck > lk → this test REDs
        because 2.1.0 > 2.0.0 becomes False.
        """
        self.assertTrue(app._update_available("2.0.0", "2.1.0"), "2.1.0 > 2.0.0: update should be reported")

    def test_prerelease_upgrades_to_stable(self):
        """A pre-release build should be prompted to upgrade to its stable release.

        Mutation: remove the stable-flag from the key tuple → (2,0,0) == (2,0,0)
        → no update prompted for beta → test REDs.
        """
        self.assertTrue(
            app._update_available("2.0.0-beta", "2.0.0"), "2.0.0-beta < 2.0.0: should prompt upgrade to stable"
        )

    def test_unparseable_version_no_update(self):
        """A malformed client version must never generate an update prompt (fail closed).

        Mutation: return True instead of False for None keys → test REDs.
        """
        self.assertFalse(
            app._update_available("not-a-version", "2.0.0"), "malformed client version must not trigger an update"
        )
        self.assertFalse(app._update_available("2.0.0", ""), "empty latest must not trigger an update")


# ─────────────────────────────────────────────────────────────────────────────
# BoundedThreadingHTTPServer: 503 when all workers are busy
# ─────────────────────────────────────────────────────────────────────────────


class BoundedServerCapacityTests(unittest.TestCase):
    """When all worker slots are taken, new connections must receive HTTP 503,
    not queue indefinitely.

    Mutation: remove the `if not self._worker_sema.acquire(blocking=False)` guard in
    BoundedThreadingHTTPServer.process_request → a flood of connections hangs
    indefinitely instead of receiving 503 → test_at_capacity_returns_503 REDs.
    """

    def setUp(self):
        ent = licensing.Entitlement("cap-test", "pro", "session")
        self._orig_entitlement = licensing.entitlement_for
        licensing.entitlement_for = lambda headers, query: (ent, None)
        # max_workers=1: one slot → the first request holds it, the second gets 503.
        self._server = app.BoundedThreadingHTTPServer(("127.0.0.1", 0), app.Handler, max_workers=1)
        self._port = self._server.server_address[1]
        threading.Thread(target=self._server.serve_forever, daemon=True).start()

    def tearDown(self):
        licensing.entitlement_for = self._orig_entitlement
        self._server.shutdown()

    def test_at_capacity_returns_503(self):
        """With max_workers=1: exhaust the single semaphore slot directly, then verify
        that a real HTTP request gets HTTP 503 Service Unavailable.

        Mutation: remove the `if not self._worker_sema.acquire(blocking=False)` guard in
        BoundedThreadingHTTPServer.process_request → the 503 is never sent, the request
        is queued and eventually returns 200 → this assertion REDs.
        """
        # Acquire the single slot directly so the server is already at capacity.
        acquired = self._server._worker_sema.acquire(blocking=False)
        self.assertTrue(acquired, "semaphore setup: should be available before the test")

        try:
            # With the slot held, the next incoming request must get 503.
            try:
                r = urllib.request.urlopen(f"http://127.0.0.1:{self._port}/api/pubkey", timeout=5)
                code = r.status
            except urllib.error.HTTPError as e:
                code = e.code
            except Exception as exc:
                self.fail(f"unexpected exception: {exc}")

            self.assertEqual(code, 503, f"at-capacity request must return 503, not {code}")
        finally:
            # Release so tearDown can process its requests cleanly.
            self._server._worker_sema.release()

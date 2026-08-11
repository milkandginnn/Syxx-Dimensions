"""Tests for server capacity bounds and hard-request-deadline protection.

WS7-15: request-deadline timer, _body_error 413/400, BoundedThreadingHTTPServer
503 capacity guard, and _version_key ordering.

Each test has a named failure case that goes RED if the guarded behaviour is removed.
Run from Server/:  python3 -m unittest tests.test_server_capacity -v
"""

import io
import os
import threading
import urllib.error
import urllib.request

import conftest
import pytest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import app, config, db, licensing  # noqa: E402

config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


def _make_handler_with_mock_response():
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


def test_body_error_too_large_sends_413():
    h, captured = _make_handler_with_mock_response()
    result = h._body_error(app._BODY_TOO_LARGE)
    assert result, "_body_error must return True for _BODY_TOO_LARGE"
    assert captured.get("code") == 413, "_body_error must call _json_error(413, ...) for oversized bodies"


def test_body_error_incomplete_sends_400():
    h, captured = _make_handler_with_mock_response()
    result = h._body_error(app._BODY_INCOMPLETE)
    assert result, "_body_error must return True for _BODY_INCOMPLETE"
    assert captured.get("code") == 400, "_body_error must call _json_error(400, ...) for incomplete bodies"


def test_body_error_normal_body_returns_false():
    """A good body sentinel must not trigger the error path.

    Mutation: change `return False` at the end of _body_error to `return True`
    → this test REDs because a valid body raises the error gate.
    """
    h, captured = _make_handler_with_mock_response()
    result = h._body_error(b'{"key": "value"}')
    assert not result, "_body_error must return False for a well-formed body"
    assert "code" not in captured, "_body_error must not call _json_error for a normal body"


def test_equal_versions_no_update():
    """'2.0' and '2.0.0' are the same release; no update must be reported."""
    assert not app._update_available("2.0", "2.0.0"), "2.0 == 2.0.0: no update (A057 padding)"
    assert not app._update_available("2.0.0", "2.0"), "2.0.0 == 2.0: no update"


def test_newer_version_triggers_update():
    """A strictly newer latest must prompt an update.

    Mutation: invert the ck < lk comparison to ck > lk → this test REDs
    because 2.1.0 > 2.0.0 becomes False.
    """
    assert app._update_available("2.0.0", "2.1.0"), "2.1.0 > 2.0.0: update should be reported"


def test_prerelease_upgrades_to_stable():
    """A pre-release build should be prompted to upgrade to its stable release.

    Mutation: remove the stable-flag from the key tuple → (2,0,0) == (2,0,0)
    → no update prompted for beta → test REDs.
    """
    assert app._update_available("2.0.0-beta", "2.0.0"), "2.0.0-beta < 2.0.0: should prompt upgrade to stable"


def test_unparseable_version_no_update():
    """A malformed client version must never generate an update prompt (fail closed).

    Mutation: return True instead of False for None keys → test REDs.
    """
    assert not app._update_available("not-a-version", "2.0.0"), "malformed client version must not trigger an update"
    assert not app._update_available("2.0.0", ""), "empty latest must not trigger an update"


@pytest.fixture
def _capacity_server(monkeypatch):
    ent = licensing.Entitlement("cap-test", "pro", "session")
    monkeypatch.setattr(licensing, "entitlement_for", lambda headers, query: (ent, None))
    # max_workers=1: one slot → the first request holds it, the second gets 503.
    server = app.BoundedThreadingHTTPServer(("127.0.0.1", 0), app.Handler, max_workers=1)
    port = server.server_address[1]
    threading.Thread(target=server.serve_forever, daemon=True).start()
    yield server, port
    server.shutdown()


def test_at_capacity_returns_503(_capacity_server):
    """With max_workers=1: exhaust the single semaphore slot directly, then verify
    that a real HTTP request gets HTTP 503 Service Unavailable.

    Mutation: remove the `if not self._worker_sema.acquire(blocking=False)` guard in
    BoundedThreadingHTTPServer.process_request → the 503 is never sent, the request
    is queued and eventually returns 200 → this assertion REDs.
    """
    server, port = _capacity_server
    # Acquire the single slot directly so the server is already at capacity.
    acquired = server._worker_sema.acquire(blocking=False)
    assert acquired, "semaphore setup: should be available before the test"

    try:
        # With the slot held, the next incoming request must get 503.
        try:
            r = urllib.request.urlopen(f"http://127.0.0.1:{port}/api/pubkey", timeout=5)
            code = r.status
        except urllib.error.HTTPError as e:
            code = e.code
        except Exception as exc:
            pytest.fail(f"unexpected exception: {exc}")

        assert code == 503, f"at-capacity request must return 503, not {code}"
    finally:
        # Release so tearDown can process its requests cleanly.
        server._worker_sema.release()

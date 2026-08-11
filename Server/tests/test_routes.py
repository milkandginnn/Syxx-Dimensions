"""Route-level regression tests: drive the real Handler over HTTP.

Guards two handler bugs that unit tests could never see because nothing
exercised the HTTP layer:

  1. /api/leak/verify with a bad token: the route checked
     `isinstance(result, str)` but leak.verify() returns (None, errmsg) on
     failure, so `result=None` fell through to _send(200, None) -> TypeError
     in _send -> the do_GET catch-all replied on top of the already-buffered
     200 status line. Clients observed HTTP 200 + {"error": "internal error"}
     instead of 404. The check was dead code from the day it was written
     (c84cbfc): verify() has never returned a str first element.

  2. /api/trial/anchor: app.py had no module-level `import time`, so
     int(time.time()) raised NameError on every request (HTTP 500).

Run from Server/:  python3 -m pytest tests/test_routes.py -v
"""

import json
import os
import tempfile
import threading
import time
import urllib.error
import urllib.request

# All test modules share one temp DB (set up by conftest.py) so cross-file
# config.DB_PATH clobbering doesn't happen. We still re-init in each module
# so the tables exist when the module's tests run.
import conftest
import pytest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
# Dev/test escape (A022): allow an ephemeral collector key under test; production
# fails closed without a provisioned SYXX_COLLECTOR_PRIVATE_KEY.
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import app, config, db, leak, licensing, resolver  # noqa: E402

# Force re-init: conftest.py already set SYXX_DB_PATH + called init(), but
# this file may be the first imported — ensure our timeout-based teardown
# doesn't leave the next module with a stale _initialized = True + deleted DB.
config.DB_PATH = _DB_PATH
db._initialized = False
db.init()

SUBJECT = "route-test-device"
_orig_entitlement_for = licensing.entitlement_for
_httpd = None
_port = None


def setup_module():
    global _httpd, _port
    # F030: bind the server BEFORE installing global stubs. If ThreadingHTTPServer raises
    # (e.g. bind denied in a sandbox), the global entitlement_for must not leak into later
    # test modules. try/finally ensures restoration even when construction fails.
    _httpd = app.ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
    _port = _httpd.server_address[1]
    # Every request is entitled: these tests target route logic, not auth.
    ent = licensing.Entitlement(SUBJECT, "pro", "session")
    licensing.entitlement_for = lambda headers, query: (ent, None)
    threading.Thread(target=_httpd.serve_forever, daemon=True).start()


def teardown_module():
    # F030: always restore the global, even when setup_module raised partway through.
    licensing.entitlement_for = _orig_entitlement_for
    if _httpd:
        _httpd.shutdown()
        _httpd.server_close()  # F030: close the socket to avoid ResourceWarning
    # Shared temp DB is cleaned up by conftest.py — don't unlink here.


def _get(path):
    try:
        r = urllib.request.urlopen(f"http://127.0.0.1:{_port}{path}", timeout=5)
        return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        with e:
            return e.code, json.loads(e.read())


def _post(path, obj):
    req = urllib.request.Request(
        f"http://127.0.0.1:{_port}{path}",
        data=json.dumps(obj).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        r = urllib.request.urlopen(req, timeout=5)
        return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        with e:
            return e.code, json.loads(e.read())


def _post_raw(path, data, headers):
    req = urllib.request.Request(f"http://127.0.0.1:{_port}{path}", data=data, headers=headers, method="POST")
    try:
        r = urllib.request.urlopen(req, timeout=5)
        return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        with e:
            return e.code, json.loads(e.read())


def _delete(path):
    req = urllib.request.Request(f"http://127.0.0.1:{_port}{path}", method="DELETE")
    try:
        r = urllib.request.urlopen(req, timeout=5)
        return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        with e:
            return e.code, json.loads(e.read())


def test_v1_keys_alias_routes():
    """do_DELETE must strip the /api/v1/ prefix exactly like do_GET/do_POST.
    Before the fix, DELETE /api/v1/keys/<id> fell through to 404."""
    status, body = _delete("/api/v1/keys/999999")
    assert status == 200, "DELETE /api/v1/keys/<id> must route, not 404"
    # Key 999999 does not exist, so the server must not claim it revoked one.
    # Asserting {"revoked": True} here would have ratified an unfalsifiable success.
    assert body == {"revoked": False, "count": 0}


def test_v1_keys_invalid_id_is_400_not_404():
    # 400 (not 404) proves the alias reached _route_revoke_api_key.
    status, body = _delete("/api/v1/keys/not-a-number")
    assert status == 400
    assert "invalid key id" in body["error"]


def test_v1_monitors_alias_routes():
    status, body = _delete("/api/v1/monitors/nonexistent-id")
    assert status == 200, "DELETE /api/v1/monitors/<id> must route, not 404"
    # Nothing existed to delete, so the server must not claim it deleted something.
    assert body == {"deleted": False, "count": 0}


def test_unversioned_delete_still_routes():
    status, body = _delete("/api/keys/999999")
    assert status == 200
    assert body == {"revoked": False, "count": 0}


def test_unknown_delete_path_is_404():
    status, body = _delete("/api/v1/nope")
    assert status == 404


@pytest.fixture
def stripe_webhook_route():
    """The webhook ROUTE must verify the signature BEFORE fulfilling — a forged event must never
    reach fulfillment (which mints a Keygen license). verify_webhook is unit-tested elsewhere; this
    pins the wiring. Goes RED if the route skips verification."""
    from syxx_server import config, fulfillment

    orig_secret = config.STRIPE_WEBHOOK_SECRET
    config.STRIPE_WEBHOOK_SECRET = "whsec_route"
    orig_handle = fulfillment.handle_event
    handled = []
    fulfillment.handle_event = lambda ev: handled.append(ev.get("id")) or "fulfilled"
    yield handled
    config.STRIPE_WEBHOOK_SECRET = orig_secret
    fulfillment.handle_event = orig_handle


def _sign_webhook(payload, ts, secret=b"whsec_route"):
    import hashlib
    import hmac

    return hmac.new(secret, f"{ts}.".encode() + payload, hashlib.sha256).hexdigest()


def test_forged_signature_is_rejected_and_never_fulfilled(stripe_webhook_route):
    payload = b'{"id":"evt_forged","type":"checkout.session.completed"}'
    status, _ = _post_raw(
        "/api/webhooks/stripe", payload, {"Stripe-Signature": "t=0,v1=deadbeef", "Content-Type": "application/json"}
    )
    assert status == 400
    assert stripe_webhook_route == [], "a forged webhook reached fulfillment -> free license mint"


def test_valid_signature_reaches_fulfillment(stripe_webhook_route):
    import time as _t

    payload = b'{"id":"evt_ok","type":"checkout.session.completed"}'
    ts = int(_t.time())
    hdr = f"t={ts},v1={_sign_webhook(payload, ts)}"
    status, _ = _post_raw(
        "/api/webhooks/stripe", payload, {"Stripe-Signature": hdr, "Content-Type": "application/json"}
    )
    assert status == 200
    assert stripe_webhook_route == ["evt_ok"]


def test_unknown_token_is_404_with_error_label():
    status, body = _get("/api/leak/verify?token=nonexistent.leak.syxx.app")
    assert status == 404
    assert body.get("error") == "unknown or expired token"


def test_missing_token_is_400():
    status, body = _get("/api/leak/verify")
    assert status == 400
    assert "missing token" in body.get("error", "")


@pytest.mark.skipif(not leak._HAS_CRYPTO, reason="cryptography not installed")
def test_success_returns_attestation_then_consumes():
    issued = leak.issue_token(SUBJECT)
    leak.observe(issued["token"], "9.9.9.9", 1)
    status, body = _get("/api/leak/verify?token=" + issued["subdomain"])
    assert status == 200
    assert body["queriesObserved"]
    assert "signature" in body
    # consume() runs after _send() (SRV-050), so poll briefly.
    deadline = time.time() + 2
    while db.leak_token_get(issued["token"]) is not None and time.time() < deadline:
        time.sleep(0.05)
    assert db.leak_token_get(issued["token"]) is None, "token not consumed after successful verify response"
    # And a second verify of the consumed token must now be a 404.
    status, body = _get("/api/leak/verify?token=" + issued["subdomain"])
    assert status == 404


def test_legacy_leak_endpoint_rejects_token_not_owned_by_caller():
    issued = leak.issue_token("other-subject")
    leak.observe(issued["token"], "9.9.9.9", 1)
    status, body = _get("/leak?token=" + issued["subdomain"])
    assert status == 404
    assert body.get("error") == "legacy leak endpoint requires a valid issued token owned by the caller"


def test_legacy_leak_endpoint_returns_only_owned_observations():
    issued = leak.issue_token(SUBJECT)
    leak.observe(issued["token"], "1.1.1.1", 1)
    status, body = _get("/leak?token=" + issued["subdomain"])
    assert status == 200
    assert body["resolverIP"] == "1.1.1.1"


def test_anchor_returns_200_with_server_time():
    # E037: fingerprint must be a UUID or hex string; "fp-route-test" is neither.
    fp = "550e8400-e29b-41d4-a716-446655440001"
    status, body = _post("/api/trial/anchor", {"fingerprint": fp})
    assert status == 200
    assert body["fingerprint"] == fp
    assert isinstance(body["serverTime"], int)


@pytest.fixture
def diff_route_stubs():
    orig_auth = resolver.resolve_authoritative
    orig_clean = app._clean_vantage_answers
    yield
    resolver.resolve_authoritative = orig_auth
    app._clean_vantage_answers = orig_clean


def test_clean_vantage_only_disjoint_answers_are_inconclusive_not_tampered(diff_route_stubs):
    # Production passes deadline=...; accept **kw so the fixture stays compatible.
    def _auth(domain, qtype, **_kw):
        return {
            "name": domain,
            "qtype": qtype,
            "delegation": [],
            "authoritative": None,
            "conclusion": "inconclusive",
        }

    resolver.resolve_authoritative = _auth
    app._clean_vantage_answers = lambda domain, qtype, qtype_str: {"203.0.113.10"}
    status, body = _post(
        "/api/diff",
        {
            "domain": "example.com",
            "type": "A",
            "localAnswers": ["198.51.100.20"],
        },
    )
    assert status == 200
    assert body["assessment"] == "partial"
    assert body["conclusion"] == "inconclusive"


def test_authoritative_disjoint_answers_remain_tampered(diff_route_stubs):
    def _auth(domain, qtype, **_kw):
        return {
            "name": domain,
            "qtype": qtype,
            "delegation": [],
            "authoritative": {"answers": ["203.0.113.10"], "aa": True, "rcode": 0},
            "conclusion": "authoritative",
        }

    resolver.resolve_authoritative = _auth
    app._clean_vantage_answers = lambda domain, qtype, qtype_str: set()
    status, body = _post(
        "/api/diff",
        {
            "domain": "example.com",
            "type": "A",
            "localAnswers": ["198.51.100.20"],
        },
    )
    assert status == 200
    assert body["assessment"] == "divergent"
    assert body["conclusion"] == "tampered"


def test_pubkey_returns_public_key_and_key_id():
    """D004: GET /api/pubkey returns the collector's Ed25519 public key for third-party verification."""
    status, body = _get("/api/pubkey")
    assert status == 200
    assert "publicKey" in body, "pubkey endpoint must return a publicKey field"
    assert "keyId" in body, "pubkey endpoint must return a keyId field"
    assert isinstance(body["publicKey"], str)
    assert len(body["publicKey"]) > 20, "publicKey should be a base64 string of reasonable length"
    assert isinstance(body["keyId"], str)
    assert len(body["keyId"]) > 0, "keyId must be non-empty"


def test_pubkey_matches_collector_key():
    """The pubkey returned by /api/pubkey must match what the collector signs with."""
    status, body = _get("/api/pubkey")
    assert status == 200
    # Ensure the collector_public_key() function (which issues tokens) matches.
    from syxx_server import leak as _leak

    assert body["publicKey"] == _leak.collector_public_key(), (
        "/api/pubkey must return the same key the collector uses for signing"
    )


def _get_html(path, host=None):
    """Fetch a route as raw text (the whats-new routes serve HTML, not JSON)."""
    req = urllib.request.Request(f"http://127.0.0.1:{_port}{path}", method="GET")
    if host:
        req.add_header("Host", host)
    try:
        r = urllib.request.urlopen(req, timeout=5)
        return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        with e:
            return e.code, e.read().decode("utf-8", "replace")


@pytest.fixture
def whatsnew_pages():
    """Point each platform's What's-New at a temp file with a unique marker, so a mis-route is
    unambiguous (the wrong platform's marker appears)."""
    orig = (config.WHATSNEW_MAC, config.WHATSNEW_WIN, config.WHATSNEW_DEB)
    files = []
    markers = {}
    paths = {}
    for key in ("mac", "win", "deb"):
        fd, p = tempfile.mkstemp(suffix=f"-{key}.html")
        marker = f"MARKER_{key.upper()}_ONLY"
        os.write(fd, (f"<!doctype html><p>{marker}</p>").encode())
        os.close(fd)
        files.append(p)
        markers[key] = marker
        paths[key] = p
    config.WHATSNEW_MAC, config.WHATSNEW_WIN, config.WHATSNEW_DEB = paths["mac"], paths["win"], paths["deb"]
    yield markers
    config.WHATSNEW_MAC, config.WHATSNEW_WIN, config.WHATSNEW_DEB = orig
    for p in files:
        try:
            os.unlink(p)
        except OSError:
            pass


def test_query_platform_selects_own_content(whatsnew_pages):
    """The debmotd Linux MOTD (2026-07-19). The routing was a two-way boolean before the Debian
    family became a shipped target, so a Linux client asking `platform=linux` was silently served
    MAC content. These drive the real Handler with distinct per-platform content and prove each
    platform gets its OWN page — the failure state this closes is Linux users reading the Mac letter."""
    # Every accepted Debian-family spelling must serve DEB content, never MAC.
    for plat in ("deb", "debian", "ubuntu", "mint", "linux"):
        status, html = _get_html(f"/api/whats-new?platform={plat}")
        assert status == 200, plat
        assert whatsnew_pages["deb"] in html, f"{plat} must serve the Debian page"
        assert whatsnew_pages["mac"] not in html, f"{plat} must NOT serve the Mac page"


def test_win_and_mac_still_route_correctly(whatsnew_pages):
    _, win = _get_html("/api/whats-new?platform=win")
    assert whatsnew_pages["win"] in win
    _, mac = _get_html("/api/whats-new?platform=mac")
    assert whatsnew_pages["mac"] in mac


def test_unknown_platform_falls_back_to_mac_not_error(whatsnew_pages):
    status, html = _get_html("/api/whats-new?platform=bogus")
    assert status == 200
    assert whatsnew_pages["mac"] in html, "an unrecognized platform falls back to Mac, never 500"


def test_debmotd_host_serves_debian_page(whatsnew_pages):
    # Host-header routing (the dedicated subdomain path) must resolve debmotd → Debian content.
    status, html = _get_html("/", host=config.MOTD_DEB_HOST)
    assert status == 200
    assert whatsnew_pages["deb"] in html, "debmotd.syxx.app must serve the Debian page at /"
    assert whatsnew_pages["mac"] not in html


@pytest.fixture
def unentitled():
    """A118: Paid routes must return 402 when the caller is not entitled.

    The module-level setup_module replaces licensing.entitlement_for with a
    lambda that always returns an entitlement. These tests save the original,
    restore unauthenticated mode, and verify every paid route returns 402.
    Deleting the paywall body must turn these tests RED — per AGENTS.md §3.
    """
    # Save the current (overridden) entitlement_for, then remove the override
    # so routes see the real auth layer with no entitlement.
    saved = licensing.entitlement_for
    licensing.entitlement_for = lambda headers, query: (None, None)
    # Set enforcement flags so paywalled routes require entitlement
    import importlib

    import syxx_server.config as cfg

    saved_environ = {}
    for k in ("SYXX_ENFORCE_NATIVE", "SYXX_ENFORCE_DOH", "SYXX_ENFORCE_WEB"):
        saved_environ[k] = os.environ.get(k)
        os.environ[k] = "true"
    importlib.reload(cfg)
    yield
    licensing.entitlement_for = saved
    for k, v in saved_environ.items():
        if v is None:
            os.environ.pop(k, None)
        else:
            os.environ[k] = v
    importlib.reload(cfg)


def test_warp_returns_402(unentitled):
    status, body = _get("/warp?url=https://example.com")
    assert status == 402, "GET /warp must return 402 without entitlement"


def test_fetch_script_returns_402(unentitled):
    status, body = _get("/fetch_script?url=https://example.com")
    assert status == 402, "GET /fetch_script must return 402"


def test_leak_legacy_returns_402(unentitled):
    status, body = _get("/leak?token=any.leak.syxx.app")
    assert status == 402, "GET /leak must return 402"


def test_investigate_returns_402(unentitled):
    status, body = _get("/api/investigate?domain=example.com")
    assert status == 402, "GET /api/investigate must return 402"


def test_authoritative_returns_402(unentitled):
    status, body = _get("/api/authoritative?domain=example.com")
    assert status == 402, "GET /api/authoritative must return 402"


def test_trace_returns_402(unentitled):
    status, body = _get("/api/trace?domain=example.com")
    assert status == 402, "GET /api/trace must return 402"


def test_baseline_returns_402(unentitled):
    status, body = _get("/api/baseline?domain=example.com")
    assert status == 402, "GET /api/baseline must return 402"


def test_exposure_returns_402(unentitled):
    status, body = _get("/api/exposure?domain=example.com")
    assert status == 402, "GET /api/exposure must return 402"


def test_passive_dns_returns_402(unentitled):
    status, body = _get("/api/passive-dns?domain=example.com")
    assert status == 402, "GET /api/passive-dns must return 402"


def test_leak_verify_returns_402(unentitled):
    status, body = _get("/api/leak/verify?token=any.leak.syxx.app")
    assert status == 402, "GET /api/leak/verify must return 402"


def test_doh_get_returns_402(unentitled):
    status, body = _get("/dns-query?name=example.com&type=A")
    assert status == 402, "GET /dns-query must return 402"


def test_warp_raw_returns_402(unentitled):
    status, body = _get("/api/warp/raw?url=https://example.com")
    assert status == 402, "GET /api/warp/raw must return 402"


def test_leak_token_post_returns_402(unentitled):
    status, body = _post("/api/leak/token", {})
    assert status == 402, "POST /api/leak/token must return 402"


def test_diff_post_returns_402(unentitled):
    status, body = _post("/api/diff", {"domain": "example.com", "type": "A"})
    assert status == 402, "POST /api/diff must return 402"


def test_create_api_key_post_returns_402(unentitled):
    status, body = _post("/api/keys", {"label": "test"})
    assert status == 402, "POST /api/keys must return 402"


def test_bulk_investigate_post_returns_402(unentitled):
    status, body = _post("/api/bulk-investigate", {"domains": ["example.com"]})
    assert status == 402, "POST /api/bulk-investigate must return 402"


def test_lookalike_post_returns_402(unentitled):
    status, body = _post("/api/lookalike", {"domain": "example.com"})
    assert status == 402, "POST /api/lookalike must return 402"


def test_create_monitor_post_returns_402(unentitled):
    status, body = _post("/api/monitors", {"domain": "example.com", "checks": "prophet"})
    assert status == 402, "POST /api/monitors must return 402 without entitlement"


def test_monitor_list_get_returns_402(unentitled):
    status, body = _get("/api/monitors")
    assert status == 402, "GET /api/monitors must return 402 without entitlement"


def test_api_keys_list_get_returns_402(unentitled):
    status, body = _get("/api/keys")
    assert status == 402, "GET /api/keys must return 402 without entitlement"


def test_revoke_api_key_delete_returns_402(unentitled):
    status, body = _delete("/api/keys/1")
    assert status == 402, "DELETE /api/keys/<id> must return 402 without entitlement"


def test_delete_monitor_returns_402(unentitled):
    status, body = _delete("/api/monitors/mon-test-123")
    assert status == 402, "DELETE /api/monitors/<id> must return 402 without entitlement"


def test_doh_post_returns_402(unentitled):
    """A220: POST /dns-query was the one gated route with no 402 test. The DoH POST handler
    goes through _doh_gate which calls _require(query, config.ENFORCE_DOH). Without this test,
    replacing ENFORCE_DOH with False stays green while the POST endpoint stops charging."""
    status, body = _post_raw(
        "/dns-query",
        b"\x00\x00\x01\x00\x00\x01\x00\x00\x00\x00\x00\x00",
        {"Content-Type": "application/dns-message"},
    )
    assert status == 402, "POST /dns-query must return 402 without entitlement"


def test_billing_portal_post_returns_401(unentitled):
    """401, not 402, and deliberately so: the billing portal is account management, not a
    paid feature. It is gated on PROVING WHO YOU ARE (a valid session token), because the
    customer id it opens is derived from that identity. An unauthenticated caller has no
    identity to derive from, so 'unauthorized' is the honest code — but it must still never
    be reachable without credentials, which is what this pins alongside the A118 sweep."""
    status, body = _post("/api/billing/portal", {})
    assert status == 401, "POST /api/billing/portal must refuse an anonymous caller"


def test_leaktest_start_returns_a_token():
    """A219 Fix 2: route-level tests for _route_leaktest_start and _route_leaktest_check.

    The free public leak test at /api/leaktest/start and /api/leaktest/check is a free,
    unauthenticated, IP-subject endpoint that mints tokens and reads them back. Before these
    tests, it had zero coverage. The classifier (dns_sim.py) was moved to docs/ as an
    unimplemented spec — these tests pin the routes that ship."""
    status, body = _get("/api/leaktest/start")
    assert status == 200, "leaktest start must succeed for any caller"
    assert "token" in body, "the response must include a token"


def test_leaktest_check_requires_a_token():
    status, body = _get("/api/leaktest/check")
    assert status == 400, "leaktest check without a token must return 400"


def test_leaktest_check_rejects_an_unknown_token():
    status, body = _get("/api/leaktest/check?token=nonexistent.token.leak.syxx.app")
    assert status == 404, "an unknown token must return 404"


@pytest.fixture
def metrics_env():
    """S4 hardening: /metrics must FAIL CLOSED when no bearer token is configured.

    SYXX_METRICS_BEARER_TOKEN defaults to "", and the endpoint previously treated that
    unconfigured state as "dev mode — allow", serving the full metrics snapshot to any
    anonymous internet caller on every deploy that did not explicitly set the variable.
    Deleting the fail-closed branch must turn these tests RED."""
    saved_token = config.METRICS_BEARER_TOKEN
    saved_dev = os.environ.get("SYXX_DEV_MODE")
    os.environ.pop("SYXX_DEV_MODE", None)
    yield
    config.METRICS_BEARER_TOKEN = saved_token
    if saved_dev is None:
        os.environ.pop("SYXX_DEV_MODE", None)
    else:
        os.environ["SYXX_DEV_MODE"] = saved_dev


def test_unconfigured_token_refuses_rather_than_exposing_metrics(metrics_env):
    config.METRICS_BEARER_TOKEN = ""
    status, body = _get("/metrics")
    assert status == 503, "unconfigured /metrics must refuse, not serve"
    assert "metrics" not in body


def test_unconfigured_with_explicit_dev_optin_serves(metrics_env):
    config.METRICS_BEARER_TOKEN = ""
    os.environ["SYXX_DEV_MODE"] = "1"
    # A058: /metrics now serves Prometheus text exposition, not a JSON dict — fetch
    # the raw body and assert the exposition format (json.loads would raise).
    req = urllib.request.Request(f"http://127.0.0.1:{_port}/metrics", method="GET")
    r = urllib.request.urlopen(req, timeout=5)
    body = r.read().decode("utf-8")
    assert r.status == 200
    assert r.headers.get("Content-Type", "").startswith("text/plain")
    assert "# TYPE" in body and "# HELP" in body


def test_configured_token_serves_prometheus_text(metrics_env):
    config.METRICS_BEARER_TOKEN = "s3cret"
    req = urllib.request.Request(
        f"http://127.0.0.1:{_port}/metrics",
        headers={"Authorization": "Bearer s3cret"},
        method="GET",
    )
    r = urllib.request.urlopen(req, timeout=5)
    body = r.read().decode("utf-8")
    assert r.status == 200
    assert r.headers.get("Content-Type", "").startswith("text/plain")
    assert "# TYPE" in body and "# HELP" in body


def test_configured_token_still_gates(metrics_env):
    config.METRICS_BEARER_TOKEN = "s3cret"
    status, _ = _get("/metrics")
    assert status == 401, "no Authorization header must be rejected"


@pytest.fixture
def tenant_monitors():
    """db.monitor_delete must scope BOTH statements by subject.

    The monitor_results delete was previously scoped by monitor_id alone, so any activated
    license could destroy another subject's entire monitor history with one request while
    the correctly-scoped monitors delete matched nothing. Removing the subject scope from
    the results delete must turn test_foreign_subject_cannot_delete_results RED."""
    mid_a = f"mon-a-{time.time_ns()}"
    mid_b = f"mon-b-{time.time_ns()}"
    mine = "tenant-a"
    theirs = "tenant-b"
    db.monitor_create(mid_a, mine, "a.example", "prophet")
    db.monitor_create(mid_b, theirs, "b.example", "prophet")
    db.monitor_result_save(mid_b, "prophet", "clean")
    return {"mid_b": mid_b, "mine": mine, "theirs": theirs}


def _their_results(mid_b):
    return list(db.monitor_results_for(mid_b))


def test_foreign_subject_cannot_delete_results(tenant_monitors):
    assert _their_results(tenant_monitors["mid_b"]), "fixture must start with a result row"
    deleted = db.monitor_delete(tenant_monitors["mid_b"], tenant_monitors["mine"])  # tenant-a attacks tenant-b
    assert deleted == 0, "must not delete another subject's monitor"
    assert _their_results(tenant_monitors["mid_b"]), "another subject's monitor_results were destroyed"


def test_owner_can_delete_own_monitor_and_results(tenant_monitors):
    deleted = db.monitor_delete(tenant_monitors["mid_b"], tenant_monitors["theirs"])
    assert deleted == 1
    assert not _their_results(tenant_monitors["mid_b"])


def test_diff_result_still_reports_tampered_with_real_corroboration():
    """Missing data is not clean data. Each case previously emitted a confident result
    derived from ZERO successful reads. Reverting any guard must turn these RED."""
    # Guard must not over-fire: a reachable clean vantage that genuinely disagrees
    # still yields tampered.
    v = app._diff_result("divergent", None, clean_answers={"9.9.9.9"}, local_set={"1.2.3.4"})
    assert v == "tampered"


def test_firewall_open_requires_at_least_one_clean_reference():
    from syxx_server import firewall_test

    # Every clean vantage blocked; the user resolver answers normally. _is_filtered can
    # never fire without a reference, so filtered_count is structurally 0 and the old
    # code reported "open" (= not filtering) on the very networks that filter.
    orig = firewall_test._query_resolver

    def fake(domain, ip, timeout):
        if ip in firewall_test._CLEAN_VANTAGES:
            return "error", []
        return "resolved", ["1.2.3.4"]

    firewall_test._query_resolver = fake
    try:
        # MUST be a public IP: test() returns early with "inconclusive" for a
        # non-public resolver, which would make this assertion pass vacuously
        # without ever reaching the assessment logic under test.
        out = firewall_test.test("9.9.9.9")
    finally:
        firewall_test._query_resolver = orig
    assert "error" not in out, "must reach the real assessment logic, not bail early"
    assert out["summary"]["tested"] == len(firewall_test._TEST_DOMAINS), "every test domain must have been probed"
    assert out["assessment"] == "inconclusive", "no reference read succeeded; 'open' would be measured-nothing"


@pytest.fixture
def stripe_webhook_signature_route():
    """POST /api/webhooks/stripe must REJECT an unverifiable event, never process it.

    The webhook is the only path that mints and revokes entitlement, and it is a public,
    unauthenticated URL. If a bad signature were processed "because it is probably genuine",
    anyone on the internet could forge a charge.refunded to revoke a stranger's license, or a
    checkout.session.completed to mint themselves one. Rejection is the whole security model.
    """
    handled = []
    orig_handle = app.fulfillment.handle_event
    app.fulfillment.handle_event = lambda e: handled.append(e) or "fulfilled"
    orig_secret = config.STRIPE_WEBHOOK_SECRET
    config.STRIPE_WEBHOOK_SECRET = "whsec_test_not_a_real_secret"
    yield handled
    app.fulfillment.handle_event = orig_handle
    config.STRIPE_WEBHOOK_SECRET = orig_secret


def _webhook_send(payload, sig):
    return _post_raw("/api/webhooks/stripe", payload, {"Content-Type": "application/json", "Stripe-Signature": sig})


def _webhook_valid_sig(payload, ts=None):
    import hashlib
    import hmac as _h

    ts = ts or str(int(time.time()))
    mac = _h.new(config.STRIPE_WEBHOOK_SECRET.encode(), ts.encode() + b"." + payload, hashlib.sha256).hexdigest()
    return f"t={ts},v1={mac}"


def test_a_correctly_signed_event_is_processed(stripe_webhook_signature_route):
    """The control: without this, every rejection test below could pass vacuously."""
    payload = json.dumps({"id": "evt_ok", "type": "charge.refunded", "data": {"object": {}}}).encode()
    status, body = _webhook_send(payload, _webhook_valid_sig(payload))
    assert status == 200
    assert len(stripe_webhook_signature_route) == 1


def test_a_tampered_signature_is_rejected_and_never_reaches_fulfillment(stripe_webhook_signature_route):
    payload = json.dumps({"id": "evt_bad", "type": "charge.refunded", "data": {"object": {}}}).encode()
    status, body = _webhook_send(payload, f"t={int(time.time())},v1={'0' * 64}")
    assert status == 400
    assert stripe_webhook_signature_route == [], "an unverified event was processed anyway"


def test_a_tampered_BODY_is_rejected(stripe_webhook_signature_route):
    """Signature computed over one payload, a different payload delivered."""
    signed = json.dumps({"id": "evt_1", "type": "charge.refunded", "data": {"object": {"amount": 395}}}).encode()
    sig = _webhook_valid_sig(signed)
    forged = json.dumps(
        {"id": "evt_1", "type": "checkout.session.completed", "data": {"object": {"payment_status": "paid"}}}
    ).encode()
    status, body = _webhook_send(forged, sig)
    assert status == 400
    assert stripe_webhook_signature_route == []


def test_a_missing_signature_header_is_rejected(stripe_webhook_signature_route):
    payload = json.dumps({"id": "evt_2", "type": "charge.refunded", "data": {"object": {}}}).encode()
    status, body = _webhook_send(payload, "")
    assert status == 400
    assert stripe_webhook_signature_route == []


def test_a_replayed_old_timestamp_is_rejected(stripe_webhook_signature_route):
    """A correctly-signed event captured long ago must not be replayable."""
    payload = json.dumps({"id": "evt_3", "type": "charge.refunded", "data": {"object": {}}}).encode()
    old = str(int(time.time()) - 4000)
    status, body = _webhook_send(payload, _webhook_valid_sig(payload, ts=old))
    assert status == 400
    assert stripe_webhook_signature_route == []


def test_an_unset_secret_refuses_rather_than_trusting(stripe_webhook_signature_route):
    """Unconfigured means REFUSE, never 'trust it for now'."""
    config.STRIPE_WEBHOOK_SECRET = ""
    payload = json.dumps({"id": "evt_4", "type": "charge.refunded", "data": {"object": {}}}).encode()
    status, body = _webhook_send(payload, "t=1,v1=" + "0" * 64)
    assert status == 400
    assert stripe_webhook_signature_route == []


@pytest.fixture
def billing_portal():
    """POST /api/billing/portal — Stripe Customer Portal (Phase 2.2 G1).

    This route exists because California B&P 17602(d)(1) requires a subscription bought online to
    be cancellable online; email-only cancellation does not satisfy it.

    The security property under test is that the Stripe customer id is DERIVED from the caller's
    own session and is never accepted from the request. If a caller could name the customer, a
    guessed `cus_` id would open a stranger's invoices, payment methods, and cancel button.
    """
    portal_calls = []
    orig_portal = app.stripe_checkout.create_billing_portal_session
    app.stripe_checkout.create_billing_portal_session = lambda customer_id, return_url=None: (
        portal_calls.append(customer_id) or {"url": "https://billing.stripe.test/session/" + customer_id}
    )
    yield portal_calls
    app.stripe_checkout.create_billing_portal_session = orig_portal


def _session_for(subject):
    token, _exp = licensing.mint_session(subject, "pro")
    return token


def _post_portal(body, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    return _post_raw("/api/billing/portal", json.dumps(body).encode("utf-8"), headers)


def test_anonymous_caller_is_refused(billing_portal):
    status, body = _post_portal({})
    assert status == 401
    assert billing_portal == []


def test_forged_session_token_is_refused(billing_portal):
    status, body = _post_portal({}, token="not.a.real.token")
    assert status == 401
    assert billing_portal == []


def test_a_caller_supplied_customer_id_is_never_used(billing_portal):
    """A valid session for subject X, with a body naming SOMEONE ELSE's customer id.

    The portal must open for X's own customer, never for the one in the body. If this ever
    fails, anyone with any account can open anyone else's billing.
    """
    subject = "lic_portal_owner"
    db.keygen_license_id_set(subject, "lic_owned")
    db.kv_set("stripe_cust:cus_OWNER", "lic_owned")
    db.kv_set("stripe_cust:cus_VICTIM", "lic_victim")
    status, body = _post_portal({"customer": "cus_VICTIM", "customer_id": "cus_VICTIM"}, token=_session_for(subject))
    assert status == 200
    assert billing_portal == ["cus_OWNER"], "the portal opened for a customer named by the CALLER"
    assert "cus_VICTIM" not in billing_portal
    assert "cus_VICTIM" not in body.get("url", "")


def test_a_session_with_no_subscription_cannot_borrow_one_from_the_body(billing_portal):
    """No mapping for this subject. Naming a real customer id in the body must not
    manufacture one — the answer is 'we have no subscription for you', not someone else's."""
    subject = "lic_no_sub_at_all"
    db.kv_set("stripe_cust:cus_VICTIM2", "lic_victim2")
    status, body = _post_portal({"customer": "cus_VICTIM2"}, token=_session_for(subject))
    assert status == 404
    assert billing_portal == [], "an unmapped session must never reach Stripe with a borrowed customer"


def test_a_license_with_no_stripe_customer_is_404_not_a_guess(billing_portal):
    subject = "lic_orphan"
    db.keygen_license_id_set(subject, "lic_orphan_keygen")
    status, body = _post_portal({}, token=_session_for(subject))
    assert status == 404
    assert billing_portal == []


def test_a_stripe_failure_is_503_not_an_empty_url(billing_portal):
    """A 200 with a blank url would render as a working button that goes nowhere."""
    subject = "lic_portal_owner2"
    db.keygen_license_id_set(subject, "lic_owned2")
    db.kv_set("stripe_cust:cus_OWNER2", "lic_owned2")

    def boom(customer_id, return_url=None):
        raise app.stripe_checkout.StripeError("stripe unreachable")

    app.stripe_checkout.create_billing_portal_session = boom
    status, body = _post_portal({}, token=_session_for(subject))
    assert status == 503


def test_an_exact_license_match_is_required_not_a_substring(billing_portal):
    """db.kv_find_by_value_prefix resolves WHOSE billing portal opens. A substring match
    (the original behaviour) lets a license id that is a prefix of another resolve to the
    wrong customer."""
    subject = "lic_substr"
    db.keygen_license_id_set(subject, "lic_ab")
    # A DIFFERENT customer holds a license whose id merely CONTAINS "lic_ab".
    db.kv_set("stripe_cust:cus_OTHER", "lic_abcdef")
    status, body = _post_portal({"customer": "cus_VICTIM2"}, token=_session_for(subject))
    assert status == 404, "a substring match resolved another customer's subscription"
    assert billing_portal == []


# --- A052: ETag / If-None-Match 304 -----------------------------------------


def test_etag_304_on_matching_if_none_match():
    """A052: responses carry an ETag; a matching If-None-Match answers 304 with no body."""
    req = urllib.request.Request(f"http://127.0.0.1:{_port}/api/broadcast", method="GET")
    r = urllib.request.urlopen(req, timeout=5)
    etag = r.headers.get("ETag")
    assert etag, "responses must carry an ETag"
    r.read()
    req2 = urllib.request.Request(
        f"http://127.0.0.1:{_port}/api/broadcast",
        headers={"If-None-Match": etag},
        method="GET",
    )
    try:
        r2 = urllib.request.urlopen(req2, timeout=5)
    except urllib.error.HTTPError as e:
        # urllib raises for 3xx; the 304 itself is the assertion.
        assert e.code == 304
        assert e.read() == b""
    else:
        assert r2.status == 304
        assert r2.read() == b""


# --- A061/A063: keygen webhook replay fence + rate limit ---------------------


def _keygen_sign(payload, secret):
    import hashlib as _h
    import hmac as _hm

    return _hm.new(secret.encode(), payload, _h.sha256).hexdigest()


def test_keygen_webhook_rate_limited_returns_429():
    """A063: per-IP rate limit before signature verification (mirrors Stripe)."""
    from syxx_server import ratelimit as _rl

    orig_secret = config.KEYGEN_WEBHOOK_SECRET
    orig_allow = _rl.allow
    config.KEYGEN_WEBHOOK_SECRET = "test-secret-123456"
    _rl.allow = lambda key, *a, **k: False
    try:
        status, body = _post_raw(
            "/api/webhooks/keygen",
            b'{"event":"license.revoked","data":{"id":"lic_x"}}',
            {"Content-Type": "application/json", "Keygen-Signature": "0" * 64},
        )
        assert status == 429
    finally:
        config.KEYGEN_WEBHOOK_SECRET = orig_secret
        _rl.allow = orig_allow


def test_keygen_webhook_replay_is_deduped():
    """A061: a correctly-signed event replayed within the window is a no-op."""
    orig_secret = config.KEYGEN_WEBHOOK_SECRET
    config.KEYGEN_WEBHOOK_SECRET = "test-secret-123456"
    try:
        payload = json.dumps(
            {"id": f"evt_replay_{time.time_ns()}", "event": "license.revoked", "data": {"id": "lic_replay"}}
        ).encode()
        sig = _keygen_sign(payload, config.KEYGEN_WEBHOOK_SECRET)
        headers = {"Content-Type": "application/json", "Keygen-Signature": sig}
        status1, body1 = _post_raw("/api/webhooks/keygen", payload, headers)
        assert status1 == 200
        status2, body2 = _post_raw("/api/webhooks/keygen", payload, headers)
        assert status2 == 200
        assert body2.get("conclusion") == "duplicate"
    finally:
        config.KEYGEN_WEBHOOK_SECRET = orig_secret


# --- A068/A066/A065/A067: input length bounds ---------------------------------


def test_activate_rejects_overlong_key():
    """A068: license keys longer than 256 chars are rejected before reaching Keygen."""
    status, body = _post("/api/auth/activate", {"key": "K" * 300})
    assert status == 400
    assert "too long" in body.get("error", "")


def test_investigate_rejects_overlong_domain():
    """A066: /api/investigate validates the domain at route level (253-char bound)."""
    status, body = _get("/api/investigate?domain=" + "a" * 300)
    assert status == 400


def test_investigate_rejects_invalid_domain():
    """A066: /api/investigate rejects a malformed domain at route level."""
    status, body = _get("/api/investigate?domain=example")
    assert status == 400


def test_benchmark_rejects_overlong_domain():
    """A065: /api/benchmark enforces the 253-char domain bound like every other route."""
    status, body = _get("/api/benchmark?domain=" + "a" * 254 + ".com")
    assert status == 400


def test_leak_verify_rejects_overlong_token():
    """A067: /api/leak/verify bounds the token length."""
    status, body = _get("/api/leak/verify?token=" + "t" * 200)
    assert status == 400
    assert "too long" in body.get("error", "")


def test_leaktest_check_rejects_overlong_token():
    """A067: /api/leaktest/check bounds the token length."""
    status, body = _get("/api/leaktest/check?token=" + "t" * 200)
    assert status == 400
    assert "too long" in body.get("error", "")


# --- A057: readyz surfaces telemetry health ----------------------------------


def test_readyz_reports_telemetry_health():
    """A057: /readyz surfaces OTel state and Sentry health so a server exporting
    zero telemetry is distinguishable from a healthy one."""
    status, body = _get("/readyz")
    # D-0 semantics: 503 when no listeners are reported (honest), 200 when healthy.
    assert status in (200, 503)
    body_json = json.loads(body) if isinstance(body, str) else body
    assert isinstance(body_json, dict)
    if status == 200:
        # A057: telemetry health is surfaced on the healthy body.
        assert "otel" in body_json
        assert "sentry" in body_json

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

Run from Server/:  python3 -m unittest tests.test_routes -v
"""

import json
import os
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request

# All test modules share one temp DB (set up by conftest.py) so cross-file
# config.DB_PATH clobbering doesn't happen. We still re-init in each module
# so the tables exist when the module's tests run.
import conftest

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


def setUpModule():
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


def tearDownModule():
    # F030: always restore the global, even when setUpModule raised partway through.
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


class DeleteV1AliasTests(unittest.TestCase):
    """do_DELETE must strip the /api/v1/ prefix exactly like do_GET/do_POST.
    Before the fix, DELETE /api/v1/keys/<id> fell through to 404."""

    def test_v1_keys_alias_routes(self):
        status, body = _delete("/api/v1/keys/999999")
        self.assertEqual(status, 200, "DELETE /api/v1/keys/<id> must route, not 404")
        # Key 999999 does not exist, so the server must not claim it revoked one.
        # Asserting {"revoked": True} here would have ratified an unfalsifiable success.
        self.assertEqual(body, {"revoked": False, "count": 0})

    def test_v1_keys_invalid_id_is_400_not_404(self):
        # 400 (not 404) proves the alias reached _route_revoke_api_key.
        status, body = _delete("/api/v1/keys/not-a-number")
        self.assertEqual(status, 400)
        self.assertIn("invalid key id", body["error"])

    def test_v1_monitors_alias_routes(self):
        status, body = _delete("/api/v1/monitors/nonexistent-id")
        self.assertEqual(status, 200, "DELETE /api/v1/monitors/<id> must route, not 404")
        # Nothing existed to delete, so the server must not claim it deleted something.
        self.assertEqual(body, {"deleted": False, "count": 0})

    def test_unversioned_delete_still_routes(self):
        status, body = _delete("/api/keys/999999")
        self.assertEqual(status, 200)
        self.assertEqual(body, {"revoked": False, "count": 0})

    def test_unknown_delete_path_is_404(self):
        status, body = _delete("/api/v1/nope")
        self.assertEqual(status, 404)


class StripeWebhookRouteTests(unittest.TestCase):
    """The webhook ROUTE must verify the signature BEFORE fulfilling — a forged event must never
    reach fulfillment (which mints a Keygen license). verify_webhook is unit-tested elsewhere; this
    pins the wiring. Goes RED if the route skips verification."""

    def setUp(self):
        from syxx_server import config, fulfillment

        self._config, self._fulfillment = config, fulfillment
        self._orig_secret = config.STRIPE_WEBHOOK_SECRET
        config.STRIPE_WEBHOOK_SECRET = "whsec_route"
        self._orig_handle = fulfillment.handle_event
        self.handled = []
        self._fulfillment.handle_event = lambda ev: self.handled.append(ev.get("id")) or "fulfilled"

    def tearDown(self):
        self._config.STRIPE_WEBHOOK_SECRET = self._orig_secret
        self._fulfillment.handle_event = self._orig_handle

    @staticmethod
    def _sign(payload, ts, secret=b"whsec_route"):
        import hashlib
        import hmac

        return hmac.new(secret, f"{ts}.".encode() + payload, hashlib.sha256).hexdigest()

    def test_forged_signature_is_rejected_and_never_fulfilled(self):
        payload = b'{"id":"evt_forged","type":"checkout.session.completed"}'
        status, _ = _post_raw(
            "/api/webhooks/stripe", payload, {"Stripe-Signature": "t=0,v1=deadbeef", "Content-Type": "application/json"}
        )
        self.assertEqual(status, 400)
        self.assertEqual(self.handled, [], "a forged webhook reached fulfillment -> free license mint")

    def test_valid_signature_reaches_fulfillment(self):
        import time as _t

        payload = b'{"id":"evt_ok","type":"checkout.session.completed"}'
        ts = int(_t.time())
        hdr = f"t={ts},v1={self._sign(payload, ts)}"
        status, _ = _post_raw(
            "/api/webhooks/stripe", payload, {"Stripe-Signature": hdr, "Content-Type": "application/json"}
        )
        self.assertEqual(status, 200)
        self.assertEqual(self.handled, ["evt_ok"])


class LeakVerifyRouteTests(unittest.TestCase):
    def test_unknown_token_is_404_with_error_label(self):
        status, body = _get("/api/leak/verify?token=nonexistent.leak.syxx.app")
        self.assertEqual(status, 404)
        self.assertEqual(body.get("error"), "unknown or expired token")

    def test_missing_token_is_400(self):
        status, body = _get("/api/leak/verify")
        self.assertEqual(status, 400)
        self.assertIn("missing token", body.get("error", ""))

    @unittest.skipUnless(leak._HAS_CRYPTO, "cryptography not installed")
    def test_success_returns_attestation_then_consumes(self):
        issued = leak.issue_token(SUBJECT)
        leak.observe(issued["token"], "9.9.9.9", 1)
        status, body = _get("/api/leak/verify?token=" + issued["subdomain"])
        self.assertEqual(status, 200)
        self.assertTrue(body["queriesObserved"])
        self.assertIn("signature", body)
        # consume() runs after _send() (SRV-050), so poll briefly.
        deadline = time.time() + 2
        while db.leak_token_get(issued["token"]) is not None and time.time() < deadline:
            time.sleep(0.05)
        self.assertIsNone(db.leak_token_get(issued["token"]), "token not consumed after successful verify response")
        # And a second verify of the consumed token must now be a 404.
        status, body = _get("/api/leak/verify?token=" + issued["subdomain"])
        self.assertEqual(status, 404)

    def test_legacy_leak_endpoint_rejects_token_not_owned_by_caller(self):
        issued = leak.issue_token("other-subject")
        leak.observe(issued["token"], "9.9.9.9", 1)
        status, body = _get("/leak?token=" + issued["subdomain"])
        self.assertEqual(status, 404)
        self.assertEqual(body.get("error"), "legacy leak endpoint requires a valid issued token owned by the caller")

    def test_legacy_leak_endpoint_returns_only_owned_observations(self):
        issued = leak.issue_token(SUBJECT)
        leak.observe(issued["token"], "1.1.1.1", 1)
        status, body = _get("/leak?token=" + issued["subdomain"])
        self.assertEqual(status, 200)
        self.assertEqual(body["resolverIP"], "1.1.1.1")


class TrialAnchorRouteTests(unittest.TestCase):
    def test_anchor_returns_200_with_server_time(self):
        status, body = _post("/api/trial/anchor", {"fingerprint": "fp-route-test"})
        self.assertEqual(status, 200)
        self.assertEqual(body["fingerprint"], "fp-route-test")
        self.assertIsInstance(body["serverTime"], int)


class DiffRouteTests(unittest.TestCase):
    def setUp(self):
        self._orig_auth = resolver.resolve_authoritative
        self._orig_clean = app._clean_vantage_answers

    def tearDown(self):
        resolver.resolve_authoritative = self._orig_auth
        app._clean_vantage_answers = self._orig_clean

    def test_clean_vantage_only_disjoint_answers_are_inconclusive_not_tampered(self):
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
        self.assertEqual(status, 200)
        self.assertEqual(body["assessment"], "partial")
        self.assertEqual(body["conclusion"], "inconclusive")

    def test_authoritative_disjoint_answers_remain_tampered(self):
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
        self.assertEqual(status, 200)
        self.assertEqual(body["assessment"], "divergent")
        self.assertEqual(body["conclusion"], "tampered")


class PubkeyEndpointTests(unittest.TestCase):
    """D004: GET /api/pubkey returns the collector's Ed25519 public key for third-party verification."""

    def test_pubkey_returns_public_key_and_key_id(self):
        status, body = _get("/api/pubkey")
        self.assertEqual(status, 200)
        self.assertIn("publicKey", body, "pubkey endpoint must return a publicKey field")
        self.assertIn("keyId", body, "pubkey endpoint must return a keyId field")
        self.assertIsInstance(body["publicKey"], str)
        self.assertGreater(len(body["publicKey"]), 20, "publicKey should be a base64 string of reasonable length")
        self.assertIsInstance(body["keyId"], str)
        self.assertGreater(len(body["keyId"]), 0, "keyId must be non-empty")

    def test_pubkey_matches_collector_key(self):
        """The pubkey returned by /api/pubkey must match what the collector signs with."""
        status, body = _get("/api/pubkey")
        self.assertEqual(status, 200)
        # Ensure the collector_public_key() function (which issues tokens) matches.
        from syxx_server import leak as _leak

        self.assertEqual(
            body["publicKey"],
            _leak.collector_public_key(),
            "/api/pubkey must return the same key the collector uses for signing",
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


class WhatsNewPlatformRouteTests(unittest.TestCase):
    """The debmotd Linux MOTD (2026-07-19). The routing was a two-way boolean before the Debian
    family became a shipped target, so a Linux client asking `platform=linux` was silently served
    MAC content. These drive the real Handler with distinct per-platform content and prove each
    platform gets its OWN page — the failure state this closes is Linux users reading the Mac letter."""

    def setUp(self):
        # Point each platform's What's-New at a temp file with a unique marker, so a mis-route is
        # unambiguous (the wrong platform's marker appears).
        from syxx_server import config

        self._orig = (config.WHATSNEW_MAC, config.WHATSNEW_WIN, config.WHATSNEW_DEB)
        self._files = []
        markers = {}
        paths = {}
        for key in ("mac", "win", "deb"):
            fd, p = tempfile.mkstemp(suffix=f"-{key}.html")
            marker = f"MARKER_{key.upper()}_ONLY"
            os.write(fd, (f"<!doctype html><p>{marker}</p>").encode())
            os.close(fd)
            self._files.append(p)
            markers[key] = marker
            paths[key] = p
        self.markers = markers
        config.WHATSNEW_MAC, config.WHATSNEW_WIN, config.WHATSNEW_DEB = paths["mac"], paths["win"], paths["deb"]

    def tearDown(self):
        from syxx_server import config

        config.WHATSNEW_MAC, config.WHATSNEW_WIN, config.WHATSNEW_DEB = self._orig
        for p in self._files:
            try:
                os.unlink(p)
            except OSError:
                pass

    def test_query_platform_selects_own_content(self):
        # Every accepted Debian-family spelling must serve DEB content, never MAC.
        for plat in ("deb", "debian", "ubuntu", "mint", "linux"):
            status, html = _get_html(f"/api/whats-new?platform={plat}")
            self.assertEqual(status, 200, plat)
            self.assertIn(self.markers["deb"], html, f"{plat} must serve the Debian page")
            self.assertNotIn(self.markers["mac"], html, f"{plat} must NOT serve the Mac page")

    def test_win_and_mac_still_route_correctly(self):
        _, win = _get_html("/api/whats-new?platform=win")
        self.assertIn(self.markers["win"], win)
        _, mac = _get_html("/api/whats-new?platform=mac")
        self.assertIn(self.markers["mac"], mac)

    def test_unknown_platform_falls_back_to_mac_not_error(self):
        status, html = _get_html("/api/whats-new?platform=bogus")
        self.assertEqual(status, 200)
        self.assertIn(self.markers["mac"], html, "an unrecognized platform falls back to Mac, never 500")

    def test_debmotd_host_serves_debian_page(self):
        # Host-header routing (the dedicated subdomain path) must resolve debmotd → Debian content.
        from syxx_server import config

        status, html = _get_html("/", host=config.MOTD_DEB_HOST)
        self.assertEqual(status, 200)
        self.assertIn(self.markers["deb"], html, "debmotd.syxx.app must serve the Debian page at /")
        self.assertNotIn(self.markers["mac"], html)


class PaidRoute402Tests(unittest.TestCase):
    """A118: Paid routes must return 402 when the caller is not entitled.

    The module-level setUpModule replaces licensing.entitlement_for with a
    lambda that always returns an entitlement. These tests save the original,
    restore unauthenticated mode, and verify every paid route returns 402.
    Deleting the paywall body must turn these tests RED — per AGENTS.md §3.
    """

    @classmethod
    def setUpClass(cls):
        # Save the current (overridden) entitlement_for, then remove the override
        # so routes see the real auth layer with no entitlement.
        cls._saved = licensing.entitlement_for
        licensing.entitlement_for = lambda headers, query: (None, None)
        # Set enforcement flags so paywalled routes require entitlement
        import importlib

        import syxx_server.config as cfg

        cls._saved_environ = {}
        for k in ("SYXX_ENFORCE_NATIVE", "SYXX_ENFORCE_DOH", "SYXX_ENFORCE_WEB"):
            cls._saved_environ[k] = os.environ.get(k)
            os.environ[k] = "true"
        importlib.reload(cfg)

    @classmethod
    def tearDownClass(cls):
        licensing.entitlement_for = cls._saved
        import importlib

        import syxx_server.config as cfg

        for k, v in cls._saved_environ.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v
        importlib.reload(cfg)

    # --- GET paid routes ---
    def test_warp_returns_402(self):
        status, body = _get("/warp?url=https://example.com")
        self.assertEqual(status, 402, "GET /warp must return 402 without entitlement")

    def test_fetch_script_returns_402(self):
        status, body = _get("/fetch_script?url=https://example.com")
        self.assertEqual(status, 402, "GET /fetch_script must return 402")

    def test_leak_legacy_returns_402(self):
        status, body = _get("/leak?token=any.leak.syxx.app")
        self.assertEqual(status, 402, "GET /leak must return 402")

    def test_investigate_returns_402(self):
        status, body = _get("/api/investigate?domain=example.com")
        self.assertEqual(status, 402, "GET /api/investigate must return 402")

    def test_authoritative_returns_402(self):
        status, body = _get("/api/authoritative?domain=example.com")
        self.assertEqual(status, 402, "GET /api/authoritative must return 402")

    def test_trace_returns_402(self):
        status, body = _get("/api/trace?domain=example.com")
        self.assertEqual(status, 402, "GET /api/trace must return 402")

    def test_baseline_returns_402(self):
        status, body = _get("/api/baseline?domain=example.com")
        self.assertEqual(status, 402, "GET /api/baseline must return 402")

    def test_exposure_returns_402(self):
        status, body = _get("/api/exposure?domain=example.com")
        self.assertEqual(status, 402, "GET /api/exposure must return 402")

    def test_passive_dns_returns_402(self):
        status, body = _get("/api/passive-dns?domain=example.com")
        self.assertEqual(status, 402, "GET /api/passive-dns must return 402")

    def test_leak_verify_returns_402(self):
        status, body = _get("/api/leak/verify?token=any.leak.syxx.app")
        self.assertEqual(status, 402, "GET /api/leak/verify must return 402")

    def test_doh_get_returns_402(self):
        status, body = _get("/dns-query?name=example.com&type=A")
        self.assertEqual(status, 402, "GET /dns-query must return 402")

    def test_warp_raw_returns_402(self):
        status, body = _get("/api/warp/raw?url=https://example.com")
        self.assertEqual(status, 402, "GET /api/warp/raw must return 402")

    # --- POST paid routes ---
    def test_leak_token_post_returns_402(self):
        status, body = _post("/api/leak/token", {})
        self.assertEqual(status, 402, "POST /api/leak/token must return 402")

    def test_diff_post_returns_402(self):
        status, body = _post("/api/diff", {"domain": "example.com", "type": "A"})
        self.assertEqual(status, 402, "POST /api/diff must return 402")

    def test_create_api_key_post_returns_402(self):
        status, body = _post("/api/keys", {"label": "test"})
        self.assertEqual(status, 402, "POST /api/keys must return 402")

    def test_bulk_investigate_post_returns_402(self):
        status, body = _post("/api/bulk-investigate", {"domains": ["example.com"]})
        self.assertEqual(status, 402, "POST /api/bulk-investigate must return 402")

    def test_lookalike_post_returns_402(self):
        status, body = _post("/api/lookalike", {"domain": "example.com"})
        self.assertEqual(status, 402, "POST /api/lookalike must return 402")

    def test_create_monitor_post_returns_402(self):
        status, body = _post("/api/monitors", {"domain": "example.com", "checks": "prophet"})
        self.assertEqual(status, 402, "POST /api/monitors must return 402 without entitlement")

    def test_monitor_list_get_returns_402(self):
        status, body = _get("/api/monitors")
        self.assertEqual(status, 402, "GET /api/monitors must return 402 without entitlement")

    # A208: DELETE and list endpoints were missing 402 tests — every DELETE and
    # the keys list endpoint had no paywall coverage check.
    def test_api_keys_list_get_returns_402(self):
        status, body = _get("/api/keys")
        self.assertEqual(status, 402, "GET /api/keys must return 402 without entitlement")

    def test_revoke_api_key_delete_returns_402(self):
        status, body = _delete("/api/keys/1")
        self.assertEqual(status, 402, "DELETE /api/keys/<id> must return 402 without entitlement")

    def test_delete_monitor_returns_402(self):
        status, body = _delete("/api/monitors/mon-test-123")
        self.assertEqual(status, 402, "DELETE /api/monitors/<id> must return 402 without entitlement")

    def test_doh_post_returns_402(self):
        """A220: POST /dns-query was the one gated route with no 402 test. The DoH POST handler
        goes through _doh_gate which calls _require(query, config.ENFORCE_DOH). Without this test,
        replacing ENFORCE_DOH with False stays green while the POST endpoint stops charging."""
        status, body = _post_raw(
            "/dns-query",
            b"\x00\x00\x01\x00\x00\x01\x00\x00\x00\x00\x00\x00",
            {"Content-Type": "application/dns-message"},
        )
        self.assertEqual(status, 402, "POST /dns-query must return 402 without entitlement")

    def test_billing_portal_post_returns_401(self):
        """401, not 402, and deliberately so: the billing portal is account management, not a
        paid feature. It is gated on PROVING WHO YOU ARE (a valid session token), because the
        customer id it opens is derived from that identity. An unauthenticated caller has no
        identity to derive from, so 'unauthorized' is the honest code — but it must still never
        be reachable without credentials, which is what this pins alongside the A118 sweep."""
        status, body = _post("/api/billing/portal", {})
        self.assertEqual(status, 401, "POST /api/billing/portal must refuse an anonymous caller")


class LeaktestRouteTests(unittest.TestCase):
    """A219 Fix 2: route-level tests for _route_leaktest_start and _route_leaktest_check.

    The free public leak test at /api/leaktest/start and /api/leaktest/check is a free,
    unauthenticated, IP-subject endpoint that mints tokens and reads them back. Before these
    tests, it had zero coverage. The classifier (dns_sim.py) was moved to docs/ as an
    unimplemented spec — these tests pin the routes that ship."""

    def test_leaktest_start_returns_a_token(self):
        status, body = _get("/api/leaktest/start")
        self.assertEqual(status, 200, "leaktest start must succeed for any caller")
        self.assertIn("token", body, "the response must include a token")

    def test_leaktest_check_requires_a_token(self):
        status, body = _get("/api/leaktest/check")
        self.assertEqual(status, 400, "leaktest check without a token must return 400")

    def test_leaktest_check_rejects_an_unknown_token(self):
        status, body = _get("/api/leaktest/check?token=nonexistent.token.leak.syxx.app")
        self.assertEqual(status, 404, "an unknown token must return 404")


class MetricsFailClosedTests(unittest.TestCase):
    """S4 hardening: /metrics must FAIL CLOSED when no bearer token is configured.

    SYXX_METRICS_BEARER_TOKEN defaults to "", and the endpoint previously treated that
    unconfigured state as "dev mode — allow", serving the full metrics snapshot to any
    anonymous internet caller on every deploy that did not explicitly set the variable.
    Deleting the fail-closed branch must turn these tests RED."""

    def setUp(self):
        self._saved_token = config.METRICS_BEARER_TOKEN
        self._saved_dev = os.environ.get("SYXX_DEV_MODE")
        os.environ.pop("SYXX_DEV_MODE", None)

    def tearDown(self):
        config.METRICS_BEARER_TOKEN = self._saved_token
        if self._saved_dev is None:
            os.environ.pop("SYXX_DEV_MODE", None)
        else:
            os.environ["SYXX_DEV_MODE"] = self._saved_dev

    def test_unconfigured_token_refuses_rather_than_exposing_metrics(self):
        config.METRICS_BEARER_TOKEN = ""
        status, body = _get("/metrics")
        self.assertEqual(status, 503, "unconfigured /metrics must refuse, not serve")
        self.assertNotIn("metrics", body)

    def test_unconfigured_with_explicit_dev_optin_serves(self):
        config.METRICS_BEARER_TOKEN = ""
        os.environ["SYXX_DEV_MODE"] = "1"
        status, body = _get("/metrics")
        self.assertEqual(status, 200)
        self.assertIn("metrics", body)

    def test_configured_token_still_gates(self):
        config.METRICS_BEARER_TOKEN = "s3cret"
        status, _ = _get("/metrics")
        self.assertEqual(status, 401, "no Authorization header must be rejected")


class MonitorDeleteTenantScopeTests(unittest.TestCase):
    """db.monitor_delete must scope BOTH statements by subject.

    The monitor_results delete was previously scoped by monitor_id alone, so any activated
    license could destroy another subject's entire monitor history with one request while
    the correctly-scoped monitors delete matched nothing. Removing the subject scope from
    the results delete must turn test_foreign_subject_cannot_delete_results RED."""

    def setUp(self):
        self.mid_a = f"mon-a-{time.time_ns()}"
        self.mid_b = f"mon-b-{time.time_ns()}"
        self.mine = "tenant-a"
        self.theirs = "tenant-b"
        db.monitor_create(self.mid_a, self.mine, "a.example", "prophet")
        db.monitor_create(self.mid_b, self.theirs, "b.example", "prophet")
        db.monitor_result_save(self.mid_b, "prophet", "clean")

    def _their_results(self):
        return list(db.monitor_results_for(self.mid_b))

    def test_foreign_subject_cannot_delete_results(self):
        self.assertTrue(self._their_results(), "fixture must start with a result row")
        deleted = db.monitor_delete(self.mid_b, self.mine)  # tenant-a attacks tenant-b
        self.assertEqual(deleted, 0, "must not delete another subject's monitor")
        self.assertTrue(self._their_results(), "another subject's monitor_results were destroyed")

    def test_owner_can_delete_own_monitor_and_results(self):
        deleted = db.monitor_delete(self.mid_b, self.theirs)
        self.assertEqual(deleted, 1)
        self.assertFalse(self._their_results())


class ZeroEvidenceNeverReadsAsGoodNewsTests(unittest.TestCase):
    """Missing data is not clean data. Each case previously emitted a confident result
    derived from ZERO successful reads. Reverting any guard must turn these RED."""

    def test_diff_result_still_reports_tampered_with_real_corroboration(self):
        # Guard must not over-fire: a reachable clean vantage that genuinely disagrees
        # still yields tampered.
        v = app._diff_result("divergent", None, clean_answers={"9.9.9.9"}, local_set={"1.2.3.4"})
        self.assertEqual(v, "tampered")

    def test_firewall_open_requires_at_least_one_clean_reference(self):
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
        self.assertNotIn("error", out, "must reach the real assessment logic, not bail early")
        self.assertEqual(
            out["summary"]["tested"], len(firewall_test._TEST_DOMAINS), "every test domain must have been probed"
        )
        self.assertEqual(
            out["assessment"], "inconclusive", "no reference read succeeded; 'open' would be measured-nothing"
        )


class StripeWebhookSignatureRouteTests(unittest.TestCase):
    """POST /api/webhooks/stripe must REJECT an unverifiable event, never process it.

    The webhook is the only path that mints and revokes entitlement, and it is a public,
    unauthenticated URL. If a bad signature were processed "because it is probably genuine",
    anyone on the internet could forge a charge.refunded to revoke a stranger's license, or a
    checkout.session.completed to mint themselves one. Rejection is the whole security model.
    """

    def setUp(self):
        self.handled = []
        self._orig_handle = app.fulfillment.handle_event
        app.fulfillment.handle_event = lambda e: self.handled.append(e) or "fulfilled"
        self._orig_secret = config.STRIPE_WEBHOOK_SECRET
        config.STRIPE_WEBHOOK_SECRET = "whsec_test_not_a_real_secret"

    def tearDown(self):
        app.fulfillment.handle_event = self._orig_handle
        config.STRIPE_WEBHOOK_SECRET = self._orig_secret

    def _send(self, payload, sig):
        return _post_raw("/api/webhooks/stripe", payload, {"Content-Type": "application/json", "Stripe-Signature": sig})

    def _valid_sig(self, payload, ts=None):
        import hashlib
        import hmac as _h

        ts = ts or str(int(time.time()))
        mac = _h.new(config.STRIPE_WEBHOOK_SECRET.encode(), ts.encode() + b"." + payload, hashlib.sha256).hexdigest()
        return f"t={ts},v1={mac}"

    def test_a_correctly_signed_event_is_processed(self):
        """The control: without this, every rejection test below could pass vacuously."""
        payload = json.dumps({"id": "evt_ok", "type": "charge.refunded", "data": {"object": {}}}).encode()
        status, body = self._send(payload, self._valid_sig(payload))
        self.assertEqual(status, 200)
        self.assertEqual(len(self.handled), 1)

    def test_a_tampered_signature_is_rejected_and_never_reaches_fulfillment(self):
        payload = json.dumps({"id": "evt_bad", "type": "charge.refunded", "data": {"object": {}}}).encode()
        status, body = self._send(payload, f"t={int(time.time())},v1={'0' * 64}")
        self.assertEqual(status, 400)
        self.assertEqual(self.handled, [], "an unverified event was processed anyway")

    def test_a_tampered_BODY_is_rejected(self):
        """Signature computed over one payload, a different payload delivered."""
        signed = json.dumps({"id": "evt_1", "type": "charge.refunded", "data": {"object": {"amount": 395}}}).encode()
        sig = self._valid_sig(signed)
        forged = json.dumps(
            {"id": "evt_1", "type": "checkout.session.completed", "data": {"object": {"payment_status": "paid"}}}
        ).encode()
        status, body = self._send(forged, sig)
        self.assertEqual(status, 400)
        self.assertEqual(self.handled, [])

    def test_a_missing_signature_header_is_rejected(self):
        payload = json.dumps({"id": "evt_2", "type": "charge.refunded", "data": {"object": {}}}).encode()
        status, body = self._send(payload, "")
        self.assertEqual(status, 400)
        self.assertEqual(self.handled, [])

    def test_a_replayed_old_timestamp_is_rejected(self):
        """A correctly-signed event captured long ago must not be replayable."""
        payload = json.dumps({"id": "evt_3", "type": "charge.refunded", "data": {"object": {}}}).encode()
        old = str(int(time.time()) - 4000)
        status, body = self._send(payload, self._valid_sig(payload, ts=old))
        self.assertEqual(status, 400)
        self.assertEqual(self.handled, [])

    def test_an_unset_secret_refuses_rather_than_trusting(self):
        """Unconfigured means REFUSE, never 'trust it for now'."""
        config.STRIPE_WEBHOOK_SECRET = ""
        payload = json.dumps({"id": "evt_4", "type": "charge.refunded", "data": {"object": {}}}).encode()
        status, body = self._send(payload, "t=1,v1=" + "0" * 64)
        self.assertEqual(status, 400)
        self.assertEqual(self.handled, [])


class BillingPortalRouteTests(unittest.TestCase):
    """POST /api/billing/portal — Stripe Customer Portal (Phase 2.2 G1).

    This route exists because California B&P 17602(d)(1) requires a subscription bought online to
    be cancellable online; email-only cancellation does not satisfy it.

    The security property under test is that the Stripe customer id is DERIVED from the caller's
    own session and is never accepted from the request. If a caller could name the customer, a
    guessed `cus_` id would open a stranger's invoices, payment methods, and cancel button.
    """

    def setUp(self):
        self.portal_calls = []
        self._orig_portal = app.stripe_checkout.create_billing_portal_session
        app.stripe_checkout.create_billing_portal_session = lambda customer_id, return_url=None: (
            self.portal_calls.append(customer_id) or {"url": "https://billing.stripe.test/session/" + customer_id}
        )

    def tearDown(self):
        app.stripe_checkout.create_billing_portal_session = self._orig_portal

    def _session_for(self, subject):
        token, _exp = licensing.mint_session(subject, "pro")
        return token

    def _post_portal(self, body, token=None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        return _post_raw("/api/billing/portal", json.dumps(body).encode("utf-8"), headers)

    # --- authentication ---
    def test_anonymous_caller_is_refused(self):
        status, body = self._post_portal({})
        self.assertEqual(status, 401)
        self.assertEqual(self.portal_calls, [])

    def test_forged_session_token_is_refused(self):
        status, body = self._post_portal({}, token="not.a.real.token")
        self.assertEqual(status, 401)
        self.assertEqual(self.portal_calls, [])

    # --- THE authorization property ---
    def test_a_caller_supplied_customer_id_is_never_used(self):
        """A valid session for subject X, with a body naming SOMEONE ELSE's customer id.

        The portal must open for X's own customer, never for the one in the body. If this ever
        fails, anyone with any account can open anyone else's billing.
        """
        subject = "lic_portal_owner"
        db.keygen_license_id_set(subject, "lic_owned")
        db.kv_set("stripe_cust:cus_OWNER", "lic_owned")
        db.kv_set("stripe_cust:cus_VICTIM", "lic_victim")
        status, body = self._post_portal(
            {"customer": "cus_VICTIM", "customer_id": "cus_VICTIM"}, token=self._session_for(subject)
        )
        self.assertEqual(status, 200)
        self.assertEqual(self.portal_calls, ["cus_OWNER"], "the portal opened for a customer named by the CALLER")
        self.assertNotIn("cus_VICTIM", self.portal_calls)
        self.assertNotIn("cus_VICTIM", body.get("url", ""))

    def test_a_session_with_no_subscription_cannot_borrow_one_from_the_body(self):
        """No mapping for this subject. Naming a real customer id in the body must not
        manufacture one — the answer is 'we have no subscription for you', not someone else's."""
        subject = "lic_no_sub_at_all"
        db.kv_set("stripe_cust:cus_VICTIM2", "lic_victim2")
        status, body = self._post_portal({"customer": "cus_VICTIM2"}, token=self._session_for(subject))
        self.assertEqual(status, 404)
        self.assertEqual(self.portal_calls, [], "an unmapped session must never reach Stripe with a borrowed customer")

    def test_a_license_with_no_stripe_customer_is_404_not_a_guess(self):
        subject = "lic_orphan"
        db.keygen_license_id_set(subject, "lic_orphan_keygen")
        status, body = self._post_portal({}, token=self._session_for(subject))
        self.assertEqual(status, 404)
        self.assertEqual(self.portal_calls, [])

    # --- upstream failure must not read as success ---
    def test_a_stripe_failure_is_503_not_an_empty_url(self):
        """A 200 with a blank url would render as a working button that goes nowhere."""
        subject = "lic_portal_owner2"
        db.keygen_license_id_set(subject, "lic_owned2")
        db.kv_set("stripe_cust:cus_OWNER2", "lic_owned2")

        def boom(customer_id, return_url=None):
            raise app.stripe_checkout.StripeError("stripe unreachable")

        app.stripe_checkout.create_billing_portal_session = boom
        status, body = self._post_portal({}, token=self._session_for(subject))
        self.assertEqual(status, 503)

    def test_an_exact_license_match_is_required_not_a_substring(self):
        """db.kv_find_by_value_prefix resolves WHOSE billing portal opens. A substring match
        (the original behaviour) lets a license id that is a prefix of another resolve to the
        wrong customer."""
        subject = "lic_substr"
        db.keygen_license_id_set(subject, "lic_ab")
        # A DIFFERENT customer holds a license whose id merely CONTAINS "lic_ab".
        db.kv_set("stripe_cust:cus_OTHER", "lic_abcdef")
        status, body = self._post_portal({}, token=self._session_for(subject))
        self.assertEqual(status, 404, "a substring match resolved another customer's subscription")
        self.assertEqual(self.portal_calls, [])


if __name__ == "__main__":
    unittest.main()

"""Wire-format pinning tests (warp-hardening_20260811, Tasks 3.1 + 3.4).

These freeze the exact JSON bytes the lookup-family routes emit (key order,
separators, formatting) and the exact offline-license HMAC wire format. They
are the contract the Phase-3 constant deduplication must NOT change: after
refactoring to a single source of truth, every test below must still pass
WITHOUT modification (adjust the source, never the pin).

The network is fully mocked: the routes are driven through a bare Handler with
``_rate_ok`` and ``_send`` stubbed and the resolver/whois functions replaced by
fixed fixtures, so the only thing under test is the wire shape.
"""

from __future__ import annotations

import concurrent.futures
import hashlib
import hmac as _hmac
import json
import os
from contextlib import ExitStack
from unittest.mock import patch

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

from syxx_server import app, db, licensing, whois_lookup  # noqa: E402


def _bare_handler():
    """A Handler with no socket/connection: routes never touch wfile because
    ``_send`` is stubbed out for the duration of the call."""
    return app.Handler.__new__(app.Handler)


def _wire(call, extra_patches=()):
    """Run *call(handler)* with a recording ``_send`` and a pass-through ``_rate_ok``.
    Returns the (status, body, content_type) of the single response the route emitted."""
    captured = []

    def rec(self, status, body, content_type="application/json", extra_headers=None):
        captured.append((status, body, content_type))

    with ExitStack() as stack:
        stack.enter_context(patch.object(app.Handler, "_rate_ok", lambda self, ent: True))
        stack.enter_context(patch.object(app.Handler, "_send", rec))
        for p in extra_patches:
            stack.enter_context(p)
        call(_bare_handler())
    assert captured, "route emitted no response"
    return captured[0]


_A_RECORD = {"rcode": 0, "ad": True, "truncated": False,
             "records": [{"type": "A", "value": "93.184.216.34"}]}
_PTR_RECORD = {"rcode": 0, "ad": False, "truncated": False,
               "records": [{"type": "PTR", "value": "dns.google"}]}


class TestLookupWire:
    def test_api_lookup_wire(self):
        def call(h):
            h._route_lookup({"domain": ["example.com"], "type": ["A"], "server": ["1.1.1.1"]})

        status, body, ct = _wire(call, [patch("syxx_server.resolver.query", return_value=_A_RECORD)])
        self._assert(status, body, ct)

    @staticmethod
    def _assert(status, body, ct):
        assert status == 200
        assert ct == "application/json"
        assert body == {
            "domain": "example.com", "type": "A", "server": "1.1.1.1",
            "rcode": 0, "rcodeName": "NOERROR", "ad": True,
            "records": [{"type": "A", "value": "93.184.216.34"}], "truncated": False,
        }
        assert json.dumps(body) == (
            '{"domain": "example.com", "type": "A", "server": "1.1.1.1", "rcode": 0, '
            '"rcodeName": "NOERROR", "ad": true, "records": [{"type": "A", "value": '
            '"93.184.216.34"}], "truncated": false}'
        )


class TestReverseDnsWire:
    def test_api_reverse_dns_wire(self):
        def call(h):
            h._route_reverse_dns({"ip": ["8.8.8.8"]})

        status, body, ct = _wire(call, [patch("syxx_server.resolver.query", return_value=_PTR_RECORD)])
        assert status == 200
        assert ct == "application/json"
        assert body == {
            "ip": "8.8.8.8", "arpa": "8.8.8.8.in-addr.arpa",
            "ptr": "dns.google", "allPtr": ["dns.google"], "rcode": 0,
        }
        assert json.dumps(body) == (
            '{"ip": "8.8.8.8", "arpa": "8.8.8.8.in-addr.arpa", "ptr": "dns.google", '
            '"allPtr": ["dns.google"], "rcode": 0}'
        )


class TestPropagationWire:
    # Four pre-resolved results, deliberately NOT label-sorted, with a failure
    # mixed in: the route must sort by label and compute consensus from values.
    _RESULTS = [
        {"server": "8.8.8.8", "label": "Google", "region": "Global (Anycast)",
         "status": "ok", "records": [{"type": "A", "value": "93.184.216.34"}], "time_ms": 12.3},
        {"server": "1.1.1.1", "label": "Cloudflare", "region": "Global (Anycast)",
         "status": "ok", "records": [{"type": "A", "value": "93.184.216.34"}], "time_ms": 9.8},
        {"server": "9.9.9.9", "label": "Quad9", "region": "Global (Anycast)",
         "status": "ok", "records": [{"type": "A", "value": "93.184.216.34"}], "time_ms": 15.1},
        {"server": "156.154.70.1", "label": "Neustar", "region": "US",
         "status": "error", "error": "timed out", "time_ms": 4000.0},
    ]

    def test_api_propagation_wire(self):
        class _FakePool:
            _max_workers = 4

            def __init__(self, results):
                self._results = list(results)

            def submit(self, fn, *a):
                fut = concurrent.futures.Future()
                fut.set_result(self._results.pop(0))
                return fut

        def call(h):
            h._route_propagation({"domain": ["example.com"], "type": ["A"]})

        status, body, ct = _wire(call, [patch.object(app, "_PROPAGATION_POOL", _FakePool(self._RESULTS))])
        assert status == 200
        assert ct == "application/json"
        assert body == {
            "domain": "example.com", "type": "A",
            "resolvers": [
                {"server": "1.1.1.1", "label": "Cloudflare", "region": "Global (Anycast)",
                 "status": "ok", "records": [{"type": "A", "value": "93.184.216.34"}], "time_ms": 9.8},
                {"server": "8.8.8.8", "label": "Google", "region": "Global (Anycast)",
                 "status": "ok", "records": [{"type": "A", "value": "93.184.216.34"}], "time_ms": 12.3},
                {"server": "156.154.70.1", "label": "Neustar", "region": "US",
                 "status": "error", "error": "timed out", "time_ms": 4000.0},
                {"server": "9.9.9.9", "label": "Quad9", "region": "Global (Anycast)",
                 "status": "ok", "records": [{"type": "A", "value": "93.184.216.34"}], "time_ms": 15.1},
            ],
            "consensus": "consistent",
            "uniqueValues": ["93.184.216.34"],
            "totalResolvers": 17,
            "successCount": 3,
            "propagationPct": 17.6,
        }


class TestWhoisWire:
    def test_api_whois_passthrough(self):
        fixture = {"source": "rdap", "domain": "example.com", "registrar": "Example Registrar"}

        def call(h):
            h._route_whois({"domain": ["example.com"]})

        status, body, ct = _wire(call, [patch.object(whois_lookup, "lookup", return_value=fixture)])
        assert status == 200
        assert ct == "application/json"
        assert body == fixture  # passthrough: the route must not reshape WHOIS output


class TestDoHJsonWire:
    def test_dns_query_json_wire(self):
        def call(h):
            h._route_doh_get({"name": ["example.com"], "type": ["A"]})

        status, body, ct = _wire(call, [
            patch.object(app.Handler, "_doh_gate", lambda self, q: (None, True)),
            patch("syxx_server.resolver.query", return_value=_A_RECORD),
        ])
        assert status == 200
        assert ct == "application/json"
        assert body == {
            "Status": 0, "TC": False, "RD": True, "AD": True,
            "Question": [{"name": "example.com", "type": 1}],
            "Answer": [{"name": "example.com", "type": 1, "data": "93.184.216.34"}],
        }
        assert json.dumps(body) == (
            '{"Status": 0, "TC": false, "RD": true, "AD": true, "Question": [{"name": '
            '"example.com", "type": 1}], "Answer": [{"name": "example.com", "type": 1, '
            '"data": "93.184.216.34"}]}'
        )


class TestOfflineLicenseHmacWire:
    """The two modules sharing the offline-license HMAC shape (db.py + licensing.py)
    must reference ONE ordered field list. The pin is the db v1 wire format that
    actually verifies stored rows: pipe-delimited key|plan|status|expires_at,
    keyed with the raw session secret."""

    _KEY = b"pin-test-secret"
    # Distinct per-field values so a field-order mixup cannot accidentally match.
    _MSG = b"K123|enterprise|suspended|1700000000"
    _V1_DIGEST = None  # computed below

    def _digest(self):
        return _hmac.new(self._KEY, self._MSG, hashlib.sha256).hexdigest()

    def test_db_v1_wire_format(self):
        licensing._secret = self._KEY
        try:
            digest = db._license_row_hmac_v1("K123", "enterprise", "suspended", 1700000000)
        finally:
            licensing._secret = None
        # The exact bytes being signed — freezes field ORDER and pipe separators.
        assert digest == self._digest()

    def test_licensing_validator_uses_the_same_ordered_fields(self):
        licensing._secret = self._KEY
        try:
            digest = db._license_row_hmac_v1("K123", "enterprise", "suspended", 1700000000)
            # licensing's current signature is (key, status, expires_at, plan) — see its
            # call site. A field-order divergence (key|status|expires_at|plan) would NOT
            # match db's key|plan|status|expires_at, and this test goes red.
            licensing_digest = licensing._offline_license_hmac(
                "K123", "suspended", 1700000000, "enterprise")
        finally:
            licensing._secret = None
        assert licensing_digest == digest == self._digest()

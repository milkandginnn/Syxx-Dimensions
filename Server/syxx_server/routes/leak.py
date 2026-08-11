"""DNS leak test route methods — LeakRoutes mixin."""

from .. import config, db, leak, logging_util
from .base import _OTEL_TRACER


class LeakRoutes:
    """Leak test routes: leak_legacy, leak_token, leak_verify, leaktest_start, leaktest_check."""

    def _route_leak_legacy(self, query):
        """Backwards-compatible readout of the last resolver for a raw token."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        token = (query.get("token", [""])[0]).strip().split(".")[0].lower()
        span = _OTEL_TRACER.start_span("server.leak_legacy") if _OTEL_TRACER else None
        if span:
            span.set_attribute("leak.token_prefix", token[:8] if len(token) > 8 else token)
        rec = db.leak_token_get(token)
        if ent is None or rec is None or rec["subject"] != ent.subject:
            return self._json_error(404, "legacy leak endpoint requires a valid issued token owned by the caller")
        obs = db.leak_observations(token)
        resolver_ip = obs[-1]["resolver_ip"] if obs else None
        if span:
            span.end()
        self._send(200, {"token": token, "resolverIP": resolver_ip, "observations": obs})

    def _route_leak_token(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        zone = query.get("zone", [None])[0]
        span = _OTEL_TRACER.start_span("server.leak_token") if _OTEL_TRACER else None
        if span:
            span.set_attribute("leak.zone", zone or "")
        # A133: mark paid tokens with origin='license' so the free route cannot
        # serve them. The free route checks origin and refuses non-'ip' tokens.
        result = leak.issue_token(ent.subject, zone, origin="license")
        if span:
            span.end()
        self._send(200, result, extra_headers={"Cache-Control": "no-store"})

    def _route_leak_verify(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        token = (query.get("token", [""])[0]).strip()
        if not token:
            return self._json_error(400, "missing token")
        # A067: upper length bound so a very long token cannot traverse the string
        # ops and DB query path unchecked.
        if len(token) > 128:
            return self._json_error(400, "token too long")
        span = _OTEL_TRACER.start_span("server.leak_verify") if _OTEL_TRACER else None
        if span:
            span.set_attribute("leak.token_prefix", token[:8] if len(token) > 8 else token)
        result, label = leak.verify(token, ent.subject)
        if span:
            span.end()
        if result is None:
            return self._json_error(404, label)
        self._send(200, result)
        # A134: wrap post-response work in try/except so failures don't escape to
        # the catch-all and attempt a second response on an already-answered connection.
        # A178: only consume when verify returned a non-None label. The zero-observation
        # inconclusive path returns label=None so the caller does NOT consume (the token
        # may still receive queries within its TTL). Calling consume(None) was a no-op
        # only by accident of SQL NULL semantics; make the intent explicit.
        try:
            if label:
                leak.consume(label)
        except Exception as e:
            logging_util.error("leak_consume_failed", label=label, err=str(e))

    # --- Free-tier leak test (no auth, rate-limited per IP) -----------------

    def _route_leaktest_start(self, query):
        """Free public leak test: issue a one-time token bound to the caller's IP.
        No license required — the subject is the client IP, not a license subject."""
        if not self._rate_ok(None):
            return
        span = _OTEL_TRACER.start_span("server.leaktest_start") if _OTEL_TRACER else None
        client_ip = self._client_ip()
        zone = config.LEAK_ZONES[0] if config.LEAK_ZONES else "leak.syxx.app"
        if span:
            span.set_attribute("leak.client_ip", client_ip)
            span.set_attribute("leak.zone", zone)
            span.end()
        result = leak.issue_token(client_ip, zone)
        self._send(200, result)

    def _route_leaktest_check(self, query):
        """Free public leak test: return observed resolvers for a token. No auth required."""
        if not self._rate_ok(None):
            return
        token = (query.get("token", [""])[0]).strip()
        if not token:
            return self._json_error(400, "missing token")
        # A067: upper length bound so a very long token cannot traverse the string
        # ops and DB query path unchecked.
        if len(token) > 128:
            return self._json_error(400, "token too long")
        # A133: check the token's origin BEFORE verifying. The free route must
        # refuse to serve tokens minted by the paid route (origin='license'),
        # which carry PII (resolver IPs) and are one-time. An attacker who sees
        # the DNS label in cleartext could otherwise read a paid user's attestation
        # and destroy the token before the legitimate owner polls.
        _label = token.split(".")[0].lower()
        rec = db.leak_token_get(_label)
        if rec and rec.get("origin", "ip") != "ip":
            return self._json_error(403, "token not issued by this endpoint")
        span = _OTEL_TRACER.start_span("server.leaktest_check") if _OTEL_TRACER else None
        if span:
            span.set_attribute("leak.token_prefix", token[:8] if len(token) > 8 else token)
        client_ip = self._client_ip()
        result, label = leak.verify(token, client_ip, allow_any_subject=True)
        if result is None:
            return self._json_error(404, label)
        if span:
            span.end()
        self._send(200, result)
        # A133: never consume() from an unauthenticated route. Destructive state
        # changes must be gated on authentication. The paid route (_route_leak_verify)
        # is the only path that consumes tokens. Free tokens expire via TTL.

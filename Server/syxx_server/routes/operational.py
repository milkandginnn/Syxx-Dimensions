"""Operational and infrastructure route methods — OperationalRoutes mixin."""

import base64
import hashlib
import html as _html
import ipaddress
import json
import os
import posixpath

from .. import (
    appcast,
    broadcast,
    bulk_investigate,
    config,
    db,
    investigate,
    leak,
    listeners,
    logging_util,
    lookalike,
    passivedns,
    ratelimit,
    resolver,
    seo,
)
from .base import (
    _ASSET_TYPES,
    _OTEL_STATE,
    _STATIC_FILES,
    _prometheus_exposition,
    _redact_domain,
    _sentry_healthy,
    _update_available,
)


class OperationalRoutes:
    """Operational routes: health, metrics, broadcast, startup, appcast, whats-new,
    static files, assets, exposure, bulk investigate, lookalike."""

    # Accepted `?platform=` spellings → the canonical platform key. Anything unrecognized falls back
    # to mac (the historical default), never to an arbitrary file.
    _WHATSNEW_PLATFORMS = {
        "win": "win",
        "windows": "win",
        "deb": "deb",
        "debian": "deb",
        "ubuntu": "deb",
        "mint": "deb",
        "linux": "deb",
        "mac": "mac",
        "macos": "mac",
        "osx": "mac",
    }

    def _route_broadcast(self):
        """Message-of-the-day (Workstream D2) — PUBLIC so a security message reaches every user, LITE
        or Pro. Returns the current operator-set broadcast or null; the app shows an unseen id once."""
        if not self._rate_ok(None):
            return
        self._send(200, {"broadcast": broadcast.current()})

    def _route_whats_new(self, query):
        """What's New content (Workstream D3) — PUBLIC, legacy `?platform=` route."""
        raw = (query.get("platform", ["mac"])[0]).strip().lower()
        self._serve_whats_new(self._WHATSNEW_PLATFORMS.get(raw, "mac"))

    def _serve_whats_new(self, platform):
        """Serve the self-contained per-platform What's-New HTML under a strict CSP."""
        if not self._rate_ok(None):
            return
        path = {
            "win": config.WHATSNEW_WIN,
            "deb": config.WHATSNEW_DEB,
        }.get(platform, config.WHATSNEW_MAC)
        limit = 1 << 20
        try:
            with open(path, "rb") as f:
                html = f.read(limit + 1)  # read limit+1 to detect oversize
        except OSError:
            return self._json_error(404, "no what's-new content")
        if len(html) > limit:
            # A112: the file exceeds what we will serve. Fail closed.
            logging_util.error("whats_new_oversize", path=path, limit=limit)
            return self._json_error(500, "what's-new content unavailable")
        # D041: sign the What's-New HTML with the collector's Ed25519 key.
        from .. import leak as _leak

        sig_b64 = _leak.sign_content(html) if hasattr(_leak, "sign_content") else ""
        key_id = _leak.collector_key_id() if hasattr(_leak, "collector_key_id") else ""

        # E033: Content-Digest header (RFC 9530)
        digest = "sha-256=:" + base64.b64encode(hashlib.sha256(html).digest()).decode() + ":"
        self._send(
            200,
            html,
            "text/html; charset=utf-8",
            {
                "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; base-uri 'none'; form-action 'none'",
                "Cache-Control": "public, max-age=300",
                "X-Content-Type-Options": "nosniff",
                "X-Syxx-Signature": sig_b64,
                "X-Syxx-KeyId": key_id,
                "Content-Digest": digest,
            },
        )

    def _route_pubkey(self):
        """Public collector Ed25519 public key (D004)."""
        if not self._rate_ok(None):
            return
        from .. import leak as _leak

        self._send(
            200,
            {
                "publicKey": _leak.collector_public_key(),
                "keyId": _leak.collector_key_id(),
                "warpKeyId": _leak.collector_warp_key_id(),
                "activatedAt": _leak.collector_key_activated_at(),
                "rotationTimestamp": _leak.collector_key_activated_at(),
            },
        )

    def _route_startup(self, query):
        """Consolidated startup check (Workstream D2/D3 + update) — PUBLIC."""
        if not self._rate_ok(None):
            return

        raw = (query.get("platform", ["mac"])[0]).strip().lower()
        plat = self._WHATSNEW_PLATFORMS.get(raw, "mac")
        client_ver = (query.get("version", [""])[0]).strip()
        latest = {"win": config.LATEST_WIN, "deb": config.LATEST_DEB}.get(plat, config.LATEST_MAC)
        update = None
        if latest:
            update = {
                "available": _update_available(client_ver, latest),
                "latest": latest,
                "url": {"win": config.UPDATE_URL_WIN, "deb": config.UPDATE_URL_DEB}.get(plat, config.UPDATE_URL_MAC),
            }
        self._send(
            200,
            {
                "broadcast": broadcast.current(),
                "update": update,
                "whatsNewUrl": f"/api/whats-new?platform={plat}",
            },
        )

    def _route_appcast(self, query):
        """G-1: Custom auto-update appcast (Sparkle-inspired, Ed25519-signed)."""
        if not self._rate_ok(None):
            return
        raw = (query.get("platform", ["mac"])[0]).strip().lower()
        plat = self._WHATSNEW_PLATFORMS.get(raw, "mac")
        manifest = appcast.build_appcast(plat)
        if not manifest:
            return self._json_error(404, "no update available for this platform")
        self._send(200, manifest)

    def _route_readyz(self):
        # D-0: ready iff the DB is reachable AND no known listener is `down` or `unknown`.
        _span = getattr(self, "_current_otel_span", None)

        # --- DB health check ---
        try:
            db.connect().close()
        except Exception as e:
            logging_util.error("readyz_db_unavailable", err=str(e))
            if _span:
                _span.set_attribute("health.db", "error")
                _span.set_attribute("health.ready", False)
            return self._json_error(503, "not ready")
        if _span:
            _span.set_attribute("health.db", "ok")

        # --- Listener / DNS health check ---
        snap = listeners.snapshot()
        down = listeners.down_listeners()
        unknown = listeners.unknown_listeners()  # A196: unknown != healthy
        if _span:
            _span.set_attribute("health.dns_listener", "error" if (down or unknown) else "ok")
            if down:
                _span.set_attribute("health.listeners_down", ",".join(down))
            if unknown:
                _span.set_attribute("health.listeners_unknown", ",".join(unknown))
            _span.set_attribute("health.ready", not bool(down or unknown))
        if down or unknown:
            return self._send(503, {"ready": False, "down": down, "unknown": unknown, "listeners": snap})
        self._send(
            200,
            {
                "ready": True,
                "listeners": snap,
                "otel": _OTEL_STATE,
                "sentry": _sentry_healthy,
            },
        )

    def _route_metrics(self):
        """S4: /metrics requires bearer token auth when configured."""
        token = config.METRICS_BEARER_TOKEN
        if not token:
            # FAIL CLOSED.
            if os.environ.get("SYXX_DEV_MODE") != "1":
                return self._json_error(503, "metrics endpoint not configured")
            return self._send(
                200,
                _prometheus_exposition(logging_util.snapshot()),
                "text/plain; version=0.0.4; charset=utf-8",
            )
        import hmac

        auth = self.headers.get("Authorization", "")
        if not auth.lower().startswith("bearer ") or not hmac.compare_digest(auth[7:].strip(), token):
            return self._json_error(401, "authentication required")
        return self._send(
            200,
            _prometheus_exposition(logging_util.snapshot()),
            "text/plain; version=0.0.4; charset=utf-8",
        )

    # --- static ------------------------------------------------------------
    def _serve_static(self, path):
        name, ctype = _STATIC_FILES[path]
        safe = posixpath.normpath("/" + name.replace("\\", "/")).lstrip("/")
        full = os.path.realpath(os.path.join(config.STATIC_DIR, safe))
        if not full.startswith(os.path.realpath(config.STATIC_DIR) + os.sep):
            return self._json_error(404, "not found")
        if not os.path.isfile(full):
            return self._json_error(404, "not found")
        with open(full, "rb") as f:
            data = f.read()

        # SEO: inject canonical/OG/Twitter/JSON-LD tags into HTML pages.
        extra = {}
        if ctype.startswith("text/html"):
            try:
                html = data.decode("utf-8")
                html = seo.inject_meta_tags(html, path)
                # E-10: inject live pricing from env vars.
                html = html.replace("$3.95", f"${_html.escape(config.SEO_PRICE_MONTHLY, quote=True)}")
                html = html.replace("$19.95", f"${_html.escape(config.SEO_PRICE_YEARLY, quote=True)}")
                data = html.encode("utf-8")
            except UnicodeDecodeError:
                pass

        # Cache-Control for SEO assets — short TTL so updates propagate within a day.
        if path in ("/sitemap.xml", "/robots.txt"):
            extra["Cache-Control"] = "public, max-age=86400"

        self._send(200, data, ctype, extra if extra else None)

    def _serve_asset(self, path):
        """Serve an image from STATIC_DIR/assets/."""
        rel = posixpath.normpath(path.replace("\\", "/")).lstrip("/")
        if not rel.startswith("assets/") or ".." in rel:
            return self._json_error(404, "not found")
        ctype = _ASSET_TYPES.get(os.path.splitext(rel)[1].lower())
        if not ctype:
            return self._json_error(404, "not found")
        full = os.path.realpath(os.path.join(config.STATIC_DIR, rel))
        if not full.startswith(os.path.realpath(config.STATIC_DIR) + os.sep):
            return self._json_error(404, "not found")
        if not os.path.isfile(full):
            return self._json_error(404, "not found")
        with open(full, "rb") as f:
            data = f.read()
        headers = {"Cache-Control": "public, max-age=86400"}
        # SEC-037: Apply restrictive CSP to SVG files
        if ctype == "image/svg+xml":
            headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:"
            headers["X-Content-Type-Options"] = "nosniff"
        self._send(200, data, ctype, headers)

    def _route_exposure(self, query):
        """Outside-in resolver exposure check (Workstream S #5)."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        # A145: apply the per-route rate limit defined in config.py.
        if not ratelimit.allow(
            "exposure:" + self._rate_key(ent),
            capacity=config.EXPOSURE_RATE_CAPACITY,
            refill_millis=config.EXPOSURE_RATE_REFILL_MILLIS,
        ):
            return self._json_error(429, "exposure check limit exceeded")
        ip = self._client_ip()
        try:
            addr = ipaddress.ip_address(ip)
        except ValueError:
            return self._json_error(400, "no usable source address")
        if addr.version != 4 or addr.is_private or addr.is_loopback or addr.is_reserved:
            return self._send(
                200,
                {
                    "ip": ip,
                    "reachable": False,
                    "openResolver": False,
                    "assessment": "not-applicable",
                    "detail": "source is not a public IPv4 address; nothing to test from outside",
                },
            )
        from .base import _OTEL_TRACER

        span = _OTEL_TRACER.start_span("server.exposure") if _OTEL_TRACER else None
        if span:
            span.set_attribute("exposure.ip", ip)
        logging_util.incr("exposure_checks")
        assessment = "inconclusive"
        recursive = False
        try:
            resp = resolver.query("example.com", resolver.TYPE_A, ip, timeout=2)
            if resp.get("records"):
                assessment, recursive = "open-resolver", True
            elif resp.get("rcode") == 5:
                assessment = "closed"
        except (resolver.DNSError, OSError):
            assessment = "inconclusive"
        if span:
            span.set_attribute("exposure.assessment", assessment)
            span.end()
        detail = {
            "open-resolver": (
                "your public IP answered a recursive query from the internet — it is an OPEN "
                "RESOLVER, reachable from the WAN and abusable as a DDoS reflector; disable "
                "recursion for external clients"
            ),
            "closed": (
                "your public IP is reachable on port 53 from the internet but refused to answer a "
                "recursive query, so it is not acting as an open resolver"
            ),
            "inconclusive": (
                "we could not get an answer from your public IP, so we cannot tell whether it "
                "is an open resolver; this is not proof that it is closed"
            ),
        }[assessment]
        self._send(
            200,
            {
                "ip": ip,
                "reachable": recursive,
                "openResolver": recursive,
                "assessment": assessment,
                "detail": detail,
            },
        )

    def _route_bulk_investigate(self):
        """POST /api/bulk-investigate — run investigation on up to 50 domains concurrently."""
        import urllib.parse

        ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        if not ratelimit.allow("bulk:" + ent.subject, capacity=10, refill_millis=3600000):
            return self._json_error(429, "bulk investigation limit: 10 per hour")
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON")
        domains = payload.get("domains")
        if not isinstance(domains, list) or not domains:
            return self._json_error(400, "domains must be a non-empty list")
        if len(domains) > 50:
            return self._json_error(400, "maximum 50 domains per request")
        try:
            result = bulk_investigate.investigate_all(domains)
        except Exception as e:
            logging_util.error("bulk_investigate_failed", err=str(e))
            return self._json_error(502, f"bulk investigation failed: {e}")
        logging_util.incr("bulk_investigations")
        self._send(200, result)

    def _route_lookalike(self):
        """POST /api/lookalike — scan for lookalike/homoglyph domains."""
        import urllib.parse

        ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        if not ratelimit.allow("lookalike:" + ent.subject, capacity=5, refill_millis=3600000):
            return self._json_error(429, "lookalike scan limit: 5 per hour")
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON")
        domain = (payload.get("domain") or "").strip()
        if not domain or not investigate.valid_domain(domain):
            return self._json_error(400, "invalid or missing domain")
        # A153 (SD-88/SD-89): validate maxPermutations as an integer with bounds.
        try:
            max_perm = max(1, min(int(payload.get("maxPermutations", 200)), 500))
        except (ValueError, TypeError, OverflowError):
            return self._json_error(400, "maxPermutations must be an integer between 1 and 500")
        try:
            result = lookalike.scan(domain, max_permutations=max_perm)
        except Exception as e:
            logging_util.error("lookalike_failed", domain=_redact_domain(domain), err=str(e))
            return self._json_error(502, f"lookalike scan failed: {e}")
        logging_util.incr("lookalike_scans")
        self._send(200, result)

"""WARP content-integrity route methods — WarpRoutes mixin."""

import base64
import hashlib
import json

from .. import config, db, fetcher, leak, logging_util, warp
from .base import (
    _OTEL_STATUS_ERROR,
    _OTEL_TRACER,
    _WARP_NONCE_RE,
    _nfc_normalize_strings,
)


class WarpRoutes:
    """WARP content-integrity routes: warp, warp_raw, warp_manifest, warp_assembly, fetch_script."""

    def _route_warp(self, query):
        """DEPRECATED — the unsigned structural baseline (`warp.build_baseline`). Superseded by
        the signed manifest at /api/warp/manifest (WARP_MANIFEST_V1); a declared-but-unsigned
        hash is forgeable by a MITM into a false match. Retained one release because the
        scheduler baseline job and /api/diff content half still consume this shape."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        target = (query.get("url", [""])[0]).strip()
        if not target:
            return self._json_error(400, "missing url")
        # A064: bound the URL length so a 10KB query string cannot pass unchecked.
        if len(target) > 2048:
            return self._json_error(400, "url too long")
        try:
            span = _OTEL_TRACER.start_span("server.warp") if _OTEL_TRACER else None
            result = warp.build_baseline(target)
            if span:
                span.set_attribute("warp.target", target)
            if span:
                span.end()
        except fetcher.BlockedError as e:
            # A078: log the full error server-side; never echo it (it carries resolved
            # IPs / internal hostnames) to the API client.
            logging_util.error("warp_blocked", url=target, err=str(e))
            return self._json_error(400, "target blocked")
        except fetcher.FetchError as e:
            logging_util.error("warp_fetch_failed", url=target, err=str(e))
            return self._json_error(502, "proxy fetch failed")
        db.audit(ent.subject if ent else None, "/warp", 200, self._client_ip())
        self._send(200, result)

    def _route_warp_raw(self, query):
        """Manual Warp: return the server's CLEAN-PATH copy of a page's raw content, so the client
        can diff it side-by-side against what its own network delivered. This is the honest "manual
        diff" primitive — it returns what the SERVER saw over an independent path, and says so; it
        does NOT render a result about the user's connection (that comparison is the user's to make,
        and the app owns the local half). Same vantage caveat as any server-side content check."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        target = (query.get("url", [""])[0]).strip()
        if not target:
            return self._json_error(400, "missing url")
        # A064: bound the URL length so a 10KB query string cannot pass unchecked.
        if len(target) > 2048:
            return self._json_error(400, "url too long")

        # WARP_MANIFEST_V1 §9 diff overlay: ?raw=1 returns the envelope-decoded RAW bytes as
        # application/octet-stream + X-Syxx-Warp-Sha256, bypassing the utf-8-replace text path
        # below (which mutates bytes and must NOT feed a byte-exact diff). Same Pro-gate.
        raw_mode = (query.get("raw", [""])[0]).strip().lower() in ("1", "true", "yes", "on")
        if raw_mode:
            span = _OTEL_TRACER.start_span("server.warp_raw_bytes") if _OTEL_TRACER else None
            if span:
                span.set_attribute("warp.target", target)
            try:
                raw, final = warp.fetch_raw_decoded(target)
            except fetcher.BlockedError as e:
                if span:
                    span.end()
                # A078: log full error server-side; redact from the client body.
                logging_util.error("warp_raw_blocked", url=target, err=str(e))
                return self._json_error(400, "target blocked")
            except fetcher.FetchError as e:
                if span:
                    span.end()
                logging_util.error("warp_raw_fetch_failed", url=target, err=str(e))
                return self._json_error(502, "fetch failed")
            sha = hashlib.sha256(raw).hexdigest()
            if span:
                span.set_attribute("warp.bytes", len(raw))
                span.end()
            db.audit(ent.subject if ent else None, "/warp/raw", 200, self._client_ip())
            return self._send(
                200,
                raw,
                "application/octet-stream",
                extra_headers={"X-Syxx-Warp-Sha256": sha, "X-Syxx-Warp-Final-Url": final},
            )

        span = _OTEL_TRACER.start_span("server.warp_raw") if _OTEL_TRACER else None
        if span:
            span.set_attribute("warp.target", target)
        try:
            data, ctype, final = fetcher.fetch(target)
        except fetcher.BlockedError as e:
            # A078: log full error server-side; redact from the client body.
            logging_util.error("warp_raw_blocked", url=target, err=str(e))
            return self._json_error(400, "target blocked")
        except fetcher.FetchError as e:
            logging_util.error("warp_raw_fetch_failed", url=target, err=str(e))
            return self._json_error(502, "fetch failed")

        low = (ctype or "").lower()
        is_text = (
            low.startswith("text/")
            or "html" in low
            or "json" in low
            or "javascript" in low
            or "xml" in low
            or "css" in low
        )
        if not is_text:
            return self._json_error(415, "content is not text and cannot be diffed: %s" % (ctype or "unknown"))
        try:
            text = data.decode("utf-8", errors="replace")
        except Exception:  # noqa: S110
            return self._json_error(415, "content could not be decoded as text")

        if span:
            span.set_attribute("warp.bytes", len(data))
            span.set_attribute("warp.content_type", ctype)
            span.end()
        db.audit(ent.subject if ent else None, "/warp/raw", 200, self._client_ip())
        self._send(
            200,
            {
                "vantage": "server-clean-path",
                "requestedUrl": target,
                "finalUrl": final,
                "contentType": ctype,
                "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
                "content": text,
            },
        )

    def _route_warp_manifest(self, query):
        """WARP_MANIFEST_V1 signed content-integrity manifest (this server's CLEAN vantage).

        The server is the ONLY parser: warp.build_manifest fetches the initial-load HTML,
        resolves the transitive HTML+JS+CSS+CSS@import closure, and hashes each asset's raw
        bytes (docs/warp-manifest-v1-spec.md §3). This route serializes the inner manifest
        ONCE, base64s it into manifest_b64, and Ed25519-signs the exact base64-decoded bytes
        with the collector key. The client verifies over those bytes and MUST NOT re-serialize.

        Honesty: this attests ONE vantage's bytes, never a result about the user's connection.
        An asset that will not fetch is recorded fetch_status:"fetch_failed" (never dropped),
        forcing the client to INCONCLUSIVE; if the HTML itself will not come down the response
        is an honest 502, never a fabricated manifest. Pro-gated identically to the other warp
        routes."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        target = (query.get("url", [""])[0]).strip()
        if not target:
            return self._json_error(400, "missing url")
        nonce = (query.get("nonce", [""])[0]).strip()
        if nonce:
            if not _WARP_NONCE_RE.match(nonce):
                return self._json_error(400, "invalid nonce: expected <=64 chars of [A-Za-z0-9_-]")
        else:
            nonce = None
        span = _OTEL_TRACER.start_span("server.warp_manifest") if _OTEL_TRACER else None
        if span:
            span.set_attribute("warp.target", target)
        try:
            manifest = warp.build_manifest(target, nonce=nonce)
        except warp.WarpInconclusive as e:
            if span:
                span.end()
            # A078: log full error server-side; redact from the client body.
            logging_util.error("warp_manifest_inconclusive", url=target, err=str(e))
            return self._json_error(502, "manifest inconclusive")
        except fetcher.BlockedError as e:
            if span:
                span.end()
            logging_util.error("warp_manifest_blocked", url=target, err=str(e))
            return self._json_error(400, "target blocked")
        except fetcher.FetchError as e:
            if span:
                span.end()
            logging_util.error("warp_manifest_fetch_failed", url=target, err=str(e))
            return self._json_error(502, "manifest fetch failed")
        # Serialize the inner manifest ONCE; sign the EXACT transmitted bytes (spec §2). NO JCS,
        # NO re-serialization: json.dumps → UTF-8 → base64 into manifest_b64, sign the decoded bytes.
        # NFC-normalize all string values (A011) so that NFD/NFC differences in URLs
        # do not produce different signed bytes across platforms.
        manifest = _nfc_normalize_strings(manifest)
        manifest_bytes = json.dumps(manifest, separators=(",", ":"), sort_keys=True, ensure_ascii=False).encode("utf-8")
        manifest_b64 = base64.b64encode(manifest_bytes).decode("ascii")
        signature = leak.sign_content(manifest_bytes)
        # WARP_MANIFEST_V1 key_id contract: lowercase hex of the RAW 32-byte Ed25519 public key.
        # This is the label the fixture corpus, the client's pinned key set, and the client
        # verifier all use — so server + corpus + pinning agree. It deliberately does NOT reuse
        # leak.collector_key_id() (base64 of the first 8 pubkey bytes), whose scheme is fixed by
        # the leak-attestation protocol and must not change. Derived from the SAME collector key.
        key_id = leak.collector_warp_key_id()
        if span:
            span.set_attribute("warp.assets", len(manifest["assets"]))
            span.end()
        db.audit(ent.subject if ent else None, "/api/warp/manifest", 200, self._client_ip())
        self._send(
            200,
            {
                "manifest_b64": manifest_b64,
                "signature_ed25519": signature,
                "key_id": key_id,
            },
        )

    def _route_warp_assembly(self, query):
        """DEPRECATED — WARP_ASSEMBLY_V1 raw-concatenation is retired (superseded by the signed
        manifest at /api/warp/manifest, WARP_MANIFEST_V1). A 5-agent parity+security audit proved
        three hand-written parsers could not hold byte-parity and that the raw-concatenation
        response was unsigned (a MITM could forge the declared hash into a false match). The
        assembler machinery is deleted; this stub remains for one deprecation release and returns
        410 Gone with a migration pointer — it never fabricates a blob. Pro-gated so the
        deprecation notice is only seen by entitled callers."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        db.audit(ent.subject if ent else None, "/api/warp/assembly", 410, self._client_ip())
        return self._json_error(
            410,
            "WARP_ASSEMBLY_V1 is retired; use /api/warp/manifest (WARP_MANIFEST_V1), a signed manifest",
            {"successor": "/api/warp/manifest", "deprecated": True},
        )

    def _route_fetch_script(self, query):
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        target = (query.get("url", [""])[0]).strip()
        if not target:
            return self._json_error(400, "missing url")
        span = _OTEL_TRACER.start_span("server.fetch_script") if _OTEL_TRACER else None
        if span:
            span.set_attribute("fetch.url", target)
        try:
            data, ctype, _final = fetcher.fetch(target, timeout=config.FETCH_SCRIPT_TIMEOUT)
        except fetcher.BlockedError as e:
            return self._json_error(400, f"target blocked: {e}")
        except fetcher.FetchError as e:
            return self._json_error(502, f"fetch failed: {e}")
        if span:
            span.set_attribute("fetch.bytes", len(data))
            span.set_attribute("fetch.content_type", ctype)
            span.end()
        # A139: serve as text/plain so it cannot be loaded as a <script> tag and execute
        # as same-origin JS. The native app is the only consumer and can parse text/plain.
        self._send(200, data, "text/plain; charset=utf-8")

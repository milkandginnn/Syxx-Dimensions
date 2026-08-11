"""Base Handler class, server infrastructure, and shared utilities.

This module contains:
- BoundedThreadingHTTPServer (semaphore-bounded worker cap)
- Handler base class (non-route methods: CORS, auth, body parsing, HTTP verbs)
- All shared module-level state, constants, and helper functions
- serve() entry point
- Beta email and purge background threads
"""

import base64
import concurrent.futures
import gzip
import hashlib
import hmac
import html as _html
import ipaddress
import json
import os
import posixpath
import re
import socket
import threading
import time
import unicodedata
import urllib.parse
import uuid
from email.utils import parseaddr
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from .. import (
    appcast,
    broadcast,
    bulk_investigate,
    config,
    db,
    email_auth,
    fetcher,
    firewall_test,
    fulfillment,
    investigate,
    leak,
    licensing,
    listeners,
    logging_util,
    lookalike,
    mailer,
    passivedns,
    ratelimit,
    resolver,
    scheduler,
    seo,
    stripe_checkout,
    subdomain_enum,
    warp,
    whois_lookup,
)

try:
    from .. import dnssec_validator
except ImportError:
    dnssec_validator = None  # cryptography library not installed
from .. import __version__

# E037: Fingerprint validation patterns — compiled once at module level rather than per-request.
# UUIDs: "550e8400-e29b-41d4-a716-446655440000"; hex strings: 32–128 lowercase hex chars.
_UUID_RE = re.compile(
    r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
    re.IGNORECASE,
)
_HEX_RE = re.compile(r"^[0-9a-f]{32,128}$", re.IGNORECASE)


def _nfc_normalize_strings(obj):
    """Recursively NFC-normalize all string values in a dict/list tree.

    Ensures that signed bytes from ``json.dumps(..., ensure_ascii=False)``
    are deterministic regardless of whether upstream data carried NFD
    (macOS/IDN) or NFC (typical Linux input) codepoints.
    """
    if isinstance(obj, str):
        return unicodedata.normalize("NFC", obj)
    if isinstance(obj, dict):
        return {k: _nfc_normalize_strings(v) for k, v in obj.items()}
    if isinstance(obj, list):
        return [_nfc_normalize_strings(v) for v in obj]
    return obj


def _redact_domain(domain: str) -> str:
    """E053: Redact domain name for telemetry — hash the registrable part, keep TLD visible.

    This prevents raw domain names (which may identify users or reveal what they are
    investigating) from landing verbatim in OTel spans and structured log entries.
    The registrable part is one-way hashed (SHA-256, 8 hex chars) so repeated queries
    for the same domain produce a consistent but non-reversible identifier; only the
    TLD is kept in cleartext to preserve coarse classification usefulness.

    Examples:
        "example.com"        -> "a6edc906.com"
        "sub.example.com"    -> "...a6edc906.com"
        "example"            -> "a379a6f6.?"
        ""                   -> ""
    """
    if not domain:
        return ""
    parts = domain.rstrip(".").split(".")
    if len(parts) <= 1:
        return hashlib.sha256(domain.encode()).hexdigest()[:8] + ".?"
    tld = parts[-1]
    registrable = ".".join(parts[-2:])
    redacted = hashlib.sha256(registrable.encode()).hexdigest()[:8]
    prefix = "..." if len(parts) > 2 else ""
    return f"{prefix}{redacted}.{tld}"


# --- Sentry crash/error reporting ---
_sentry_healthy = False
try:
    import sentry_sdk  # type: ignore[unresolved-import]

    _sentry_dsn = os.environ.get("SENTRY_DSN", "")
    if _sentry_dsn:
        sentry_sdk.init(
            dsn=_sentry_dsn,
            environment=os.environ.get("SYXX_ENVIRONMENT", "production"),
            release=f"syxx-server@{__version__}",
            traces_sample_rate=0.1,
            send_default_pii=False,
            enable_tracing=True,
        )
        _sentry_healthy = True
except Exception:  # noqa: S110 — best-effort path; failing here must not take the request down
    pass  # Sentry is best-effort; never crash the server for it

# --- OpenTelemetry error reporting to bugsmasher.syxx.app ---
_OTEL_EXPORTER = None
_OTEL_LOGGER = None
_OTEL_STATE = "not attempted"
try:
    # Import paths verified against the pinned opentelemetry 1.44 packages. Two were wrong and
    # both raised ImportError, which the bare `except` below swallowed: telemetry has therefore
    # never exported a single record, and an empty dashboard was indistinguishable from a healthy
    # server. `opentelemetry.logs` does not exist (the module is `_logs`), and OTLPLogExporter
    # lives in the exporter package, not in sdk._logs.export.
    from opentelemetry import _logs as otel_logs  # type: ignore[unresolved-import]
    from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter  # type: ignore[unresolved-import]
    from opentelemetry.exporter.otlp.proto.http.trace_exporter import (
        OTLPSpanExporter,  # type: ignore[unresolved-import]
    )
    from opentelemetry.sdk._logs import LoggerProvider  # type: ignore[unresolved-import]
    from opentelemetry.sdk._logs.export import BatchLogRecordProcessor  # type: ignore[unresolved-import]
    from opentelemetry.sdk.resources import Resource  # type: ignore[unresolved-import]
    from opentelemetry.sdk.trace import TracerProvider  # type: ignore[unresolved-import]
    from opentelemetry.sdk.trace.export import BatchSpanProcessor  # type: ignore[unresolved-import]

    _resource = Resource.create({"service.name": "syxx-server", "service.version": __version__})
    # E051: OTLP endpoint is now configurable via config.OTLP_ENDPOINT (env: SYXX_OTLP_ENDPOINT).
    _endpoint = config.OTLP_ENDPOINT

    _trace_exporter = OTLPSpanExporter(endpoint=f"{_endpoint}/v1/traces")
    _tracer_provider = TracerProvider(resource=_resource)
    _tracer_provider.add_span_processor(BatchSpanProcessor(_trace_exporter))
    otel_trace = __import__("opentelemetry.trace", fromlist=["set_tracer_provider"])
    otel_trace.set_tracer_provider(_tracer_provider)

    _log_exporter = OTLPLogExporter(endpoint=f"{_endpoint}/v1/logs")
    _log_provider = LoggerProvider(resource=_resource)
    _log_provider.add_log_record_processor(BatchLogRecordProcessor(_log_exporter))
    otel_logs.set_logger_provider(_log_provider)
    _OTEL_LOGGER = otel_logs.get_logger("syxx-server")
    _OTEL_STATE = "live"
except Exception as _otel_err:  # noqa: BLE001 - recorded, not swallowed
    # Best-effort, but NEVER silent. preflight/readyz surface this so "no data arriving" can be
    # told apart from "telemetry crashed at import".
    _OTEL_STATE = f"failed: {type(_otel_err).__name__}: {_otel_err}"

# Wire OTel logger into logging_util so all logging_util.error() calls also export via OTLP.
if _OTEL_LOGGER is not None:
    try:
        # `LogRecord` is not re-exported from opentelemetry.sdk._logs in 1.44 (only
        # ReadableLogRecord / ReadWriteLogRecord are); the constructible class lives in
        # _internal. Getting this wrong left logging_util._otel_log_record as None, so the
        # export branch at logging_util.py:34 could never fire even once the imports above
        # succeeded. Failure is recorded, never swallowed.
        from opentelemetry.sdk._logs._internal import LogRecord as _OTelLogRecord  # type: ignore[unresolved-import]

        logging_util._otel_logger = _OTEL_LOGGER
        logging_util._otel_log_record = _OTelLogRecord
        _OTEL_STATE = "live"
    except Exception as _otel_rec_err:  # noqa: BLE001 - recorded, not swallowed
        _OTEL_STATE = f"failed: log-record class unavailable: {type(_otel_rec_err).__name__}: {_otel_rec_err}"

# Nonce shape gate for /api/warp/manifest (WARP_MANIFEST_V1 §4.5 replay binding). ≤64 chars of
# the URL/base64url-safe alphabet; the manifest echoes it verbatim so the client can assert it
# matches what it sent. Rejecting a malformed nonce closed keeps garbage out of the signed bytes.
# Module-level (not a Handler attribute) so route helpers stay callable on lightweight test doubles.
_WARP_NONCE_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")

# Tracer for endpoint-level OTel spans. Created once so every request handler can create spans
# without resolving the tracer on every call. Thread-safe: OTel Context is thread-local.
_OTEL_TRACER = None
_OTEL_STATUS_ERROR = None
if _OTEL_STATE.startswith("live"):
    try:
        import opentelemetry.trace as _otel_trace_mod  # type: ignore[unresolved-import]

        _OTEL_TRACER = _otel_trace_mod.get_tracer("syxx-server")
        _OTEL_STATUS_ERROR = _otel_trace_mod.StatusCode.ERROR
    except Exception:  # noqa: S110
        _OTEL_STATE = "failed: tracer unavailable"

_STATIC_FILES = {
    "/": ("index.html", "text/html; charset=utf-8"),
    "/index.html": ("index.html", "text/html; charset=utf-8"),
    "/purchase/success": ("purchase-success.html", "text/html; charset=utf-8"),
    "/purchase/cancel": ("purchase-cancel.html", "text/html; charset=utf-8"),
    "/leak-test": ("leak-test.html", "text/html; charset=utf-8"),
    "/docs/api.html": ("docs/api.html", "text/html; charset=utf-8"),
    "/docs/help.html": ("docs/help.html", "text/html; charset=utf-8"),
    "/docs/openapi.yaml": ("docs/openapi.yaml", "application/yaml; charset=utf-8"),
    "/leaktest": ("leaktest.html", "text/html; charset=utf-8"),
    "/leaktest/": ("leaktest.html", "text/html; charset=utf-8"),
    "/privacy": ("privacy.html", "text/html; charset=utf-8"),
    "/terms": ("terms.html", "text/html; charset=utf-8"),
    "/syxx-favicon.svg": ("syxx-favicon.svg", "image/svg+xml"),
    "/robots.txt": ("robots.txt", "text/plain; charset=utf-8"),
    "/sitemap.xml": ("sitemap.xml", "application/xml; charset=utf-8"),
    # RFC 9116 security contact.
    "/.well-known/security.txt": (".well-known/security.txt", "text/plain; charset=utf-8"),
}

# A025: SEO pricing constants live in config.py — single source of truth.

# Image assets served from STATIC_DIR/assets/ (logos, screenshots, favicon).
# Extension allowlist doubles as the content-type map — anything else is 404.
_ASSET_TYPES = {
    ".png": "image/png",
    ".svg": "image/svg+xml",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".webp": "image/webp",
    ".ico": "image/x-icon",
}

# Deliberately permissive but bounded: one @, a dot in the domain, no whitespace,
# no control characters. The ASCII flag prevents Unicode lookalike abuse that
# could pass the regex but carry non-printable characters into downstream mail
# utilities (header injection).
_EMAIL_RE = re.compile(r"^[^@\s\x00-\x1f\x7f]+@[^@\s\x00-\x1f\x7f]+\.[^@\s\x00-\x1f\x7f]+$")
_BODY_TOO_LARGE = object()
_BODY_INCOMPLETE = object()

# Route table for GET dispatch: path → (method_name, takes_query).
# Special routes (/healthz, /readyz, /api/stats, docs redirects) are handled
# inline before this table is consulted.
_GET_ROUTES = {
    "/metrics": ("_route_metrics", False),
    "/api/broadcast": ("_route_broadcast", False),
    "/api/whats-new": ("_route_whats_new", True),
    "/api/pubkey": ("_route_pubkey", False),
    "/api/startup": ("_route_startup", True),
    "/api/appcast": ("_route_appcast", True),
    "/api/checkout": ("_route_checkout", True),
    "/warp": ("_route_warp", True),
    "/fetch_script": ("_route_fetch_script", True),
    "/warp/fetch_script": ("_route_fetch_script", True),
    "/leak": ("_route_leak_legacy", True),
    "/api/investigate": ("_route_investigate", True),
    "/api/authoritative": ("_route_authoritative", True),
    "/api/trace": ("_route_trace", True),
    "/api/baseline": ("_route_baseline", True),
    "/api/exposure": ("_route_exposure", True),
    "/api/passive-dns": ("_route_passive", True),
    "/api/leak/verify": ("_route_leak_verify", True),
    "/api/leaktest/start": ("_route_leaktest_start", True),
    "/api/leaktest/check": ("_route_leaktest_check", True),
    "/dns-query": ("_route_doh_get", True),
    "/api/warp/raw": ("_route_warp_raw", True),
    "/api/warp/manifest": ("_route_warp_manifest", True),
    "/api/warp/assembly": ("_route_warp_assembly", True),
    "/api/lookup": ("_route_lookup", True),
    "/api/reverse-dns": ("_route_reverse_dns", True),
    "/api/benchmark": ("_route_benchmark", True),
    "/api/whois": ("_route_whois", True),
    "/api/propagation": ("_route_propagation", True),
    "/api/email-auth": ("_route_email_auth", True),
    "/api/subdomains": ("_route_subdomains", True),
    "/api/firewall-test": ("_route_firewall_test", True),
    "/api/keys": ("_route_list_api_keys", True),
    "/api/monitors": ("_route_list_monitors", True),
    "/api/dnssec": ("_route_dnssec", True),
}

# A138: allowlist of known endpoint prefixes for metrics. Everything else buckets
# to 'other' to prevent unbounded cardinality from attacker-controlled paths.
_KNOWN_ENDPOINT_PREFIXES = {
    "/api/",
    "/dns-query",
    "/assets/",
    "/fetch_script",
    "/warp/",
    "/",
    "/index.html",
    "/robots.txt",
    "/sitemap.xml",
    "/.well-known/",
}


def _valid_email(email):
    """A116: validate email with regex AND parseaddr. The regex permits a comma in
    either half (e.g. 'victim@ok.com,attacker.evil.com'), which email.utils.getaddresses
    expands to two recipients. parseaddr extracts only the first address; reject unless
    the parsed result equals the input."""
    if not _EMAIL_RE.match(email) or len(email) > 254:
        return False
    # Reject any address containing comma or semicolon (recipient-list injection).
    if "," in email or ";" in email:
        return False
    _, parsed = parseaddr(email)
    return parsed == email


# A023: _env_int removed — use config._i() directly (canonical definition lives in config.py).

# A028/A101/A108: cap concurrent worker threads. A bare ThreadingHTTPServer spawns
# one unbounded thread per connection, so parallel hostile requests exhaust the
# process and down the server. This bounds how many requests run at once.
_MAX_WORKERS = max(1, config._i("SYXX_HTTP_MAX_WORKERS", 64))
# A005/A102/A108: absolute per-request wall-clock deadline. connection.settimeout()
# is a PER-OPERATION timeout, so a slow-drip client that trickles one byte just
# under the timeout keeps resetting it and pins a worker forever. This hard cap
# force-closes the socket after a fixed total request lifetime regardless of drip.
_REQUEST_DEADLINE = max(config.HTTP_REQUEST_TIMEOUT, config._i("SYXX_HTTP_REQUEST_DEADLINE", 60))

# A136: module-level shared bounded thread pool for DNS fan-out. Replaces the
# per-request ThreadPoolExecutor instances that escaped the _MAX_WORKERS semaphore
# accounting (64 concurrent handlers × 12 threads = 768 untracked threads).
# Bounded to a fixed size; shared across all concurrent requests.
# A028: _CLEAN_VANTAGE_POOL moved to diff.py alongside _clean_vantage_answers.
_PROPAGATION_POOL = concurrent.futures.ThreadPoolExecutor(max_workers=24, thread_name_prefix="propagation")

# A028: diff helpers re-exported from diff.py for backward-compatible access
# (tests reference app._diff_content, app._diff_result, app._clean_vantage_answers).
from ..diff import _clean_vantage_answers, _diff_content, _diff_result  # noqa: E402


def _paginate(query, items, default_limit=50, max_limit=200):
    """Extract pagination parameters from query and slice items.

    Returns (paged_items, meta) where meta is a dict with page, limit, total,
    hasMore suitable for inclusion in the response body.
    """
    try:
        page = max(1, int(query.get("page", ["1"])[0]))
    except (ValueError, TypeError):
        page = 1
    try:
        limit = min(max_limit, max(1, int(query.get("limit", [str(default_limit)])[0])))
    except (ValueError, TypeError):
        limit = default_limit
    total = len(items)
    start = (page - 1) * limit
    paged = items[start : start + limit]
    has_more = (start + limit) < total
    return paged, {"page": page, "limit": limit, "total": total, "hasMore": has_more}


def _prometheus_exposition(metrics):
    """A058: render the metrics snapshot as Prometheus text exposition format
    (text/plain; version=0.0.4). Each counter becomes a # HELP/# TYPE pair plus
    one sample line; keys are sanitized to the [a-zA-Z_:] exposition alphabet."""
    lines = []
    for name in sorted(metrics):
        safe = re.sub(r"[^a-zA-Z0-9_:]", "_", name)
        lines.append(f"# HELP {safe} {name}")
        lines.append(f"# TYPE {safe} counter")
        lines.append(f"{safe} {int(metrics[name])}")
    return "\n".join(lines) + "\n"


# A028: _diff_result and _clean_vantage_answers moved to diff.py (re-exported above).


def _version_key(v):
    """Parse a version string into a totally-ordered comparison key, or None if it
    cannot be parsed. Handles A057:
      * release numbers are padded to 3 components, so '2.0' and '2' compare EQUAL to
        '2.0.0' (the old code made (2,) < (2,0,0) True -> spurious 'update available');
      * a pre-release ('2.0.0-beta') sorts BELOW the same stable release, so a beta
        IS prompted to upgrade to the stable build of the same numbers (the old code
        stripped '-beta' to (2,0,0) and never prompted).
    Key shape: (release_tuple, stable_flag, prerelease_ids). stable_flag 1 > 0 so a
    pre-release loses to its stable release; prerelease_ids only compares when both
    are pre-releases (stable always carries ())."""
    v = (v or "").strip()
    if not v:
        return None
    core, sep, pre = v.partition("-")
    nums = []
    for part in core.split("."):
        part = part.strip()
        if not part.isdigit():
            return None
        nums.append(int(part))
    if not nums:
        return None
    release = tuple((nums + [0, 0, 0])[:3])
    if not sep:
        return (release, 1, ())  # stable outranks any pre-release
    # SemVer pre-release precedence: numeric identifiers rank below alphanumeric ones.
    ids = tuple((0, int(x), "") if x.isdigit() else (1, 0, x) for x in pre.split("."))
    return (release, 0, ids)


def _update_available(client_ver, latest):
    """True only when BOTH versions parse AND latest strictly exceeds the client. An
    unparseable or missing client version does NOT prompt an update (fail closed to
    'no update' rather than nagging on a malformed version) — A057."""
    ck = _version_key(client_ver)
    lk = _version_key(latest)
    if ck is None or lk is None:
        return False
    return ck < lk


# E039: ThreadingHTTPServer is a development/testing server — NOT production-grade on its own.
# Production deployments MUST run behind a reverse proxy (nginx, caddy, or equivalent) that
# enforces:
#   - Connection limits and accept-queue depth
#   - Per-client request-rate limiting at the network layer
#   - Read/write/idle timeouts at the socket layer
#   - TLS termination (this process is plaintext; TLS is the proxy's responsibility)
#   - DDoS mitigation and IP-level blocking
# BoundedThreadingHTTPServer adds a semaphore-bounded worker cap (A028) on top of the stdlib
# base, but it is NOT a replacement for a hardened production HTTP stack.
# TODO E039: migrate to gunicorn or uvicorn (WSGI/ASGI) for the production HTTP layer;
# BoundedThreadingHTTPServer should become the development/test runner only.
class BoundedThreadingHTTPServer(ThreadingHTTPServer):
    """A028/A101/A108: ThreadingHTTPServer spawns one unbounded thread per connection,
    so concurrent hostile requests exhaust the process and down the server. This bounds
    the number of in-flight worker threads with a semaphore; when saturated it refuses
    new connections immediately (503) instead of piling on threads until the box falls
    over."""

    daemon_threads = True

    def __init__(self, *args, max_workers=None, **kwargs):
        """Initialise with a bounded semaphore capping concurrent worker threads."""
        super().__init__(*args, **kwargs)
        self._worker_sema = threading.BoundedSemaphore(max_workers or _MAX_WORKERS)

    def process_request(self, request, client_address):
        """Dispatch a request or reject it with 503 when at capacity."""
        if not self._worker_sema.acquire(blocking=False):
            # At capacity: refuse fast rather than queue unbounded work.
            try:
                request.sendall(b"HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\nContent-Length: 0\r\n\r\n")
            except OSError:
                pass
            self.shutdown_request(request)
            logging_util.incr("http_at_capacity")
            return
        try:
            super().process_request(request, client_address)
        except Exception:  # noqa: S110
            self._worker_sema.release()
            raise

    def process_request_thread(self, request, client_address):
        """Process a request in a worker thread, always releasing the semaphore on exit."""
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._worker_sema.release()


class Handler(BaseHTTPRequestHandler):
    """HTTP request handler with CORS, security headers, rate limiting, and deadline enforcement."""
    protocol_version = "HTTP/1.1"
    server_version = config.SERVER_NAME

    # Route the default per-request logging through our structured logger.
    def log_message(self, format, *args):
        """Suppress default stderr logging — structured JSON logs via logging_util."""
        return

    def setup(self):
        """Set up the connection with per-request deadline timer (A137)."""
        super().setup()
        self._responded = False  # A134: track whether a response has been sent
        self._deadline_exceeded = False  # A137: flag for routes to poll
        self.connection.settimeout(config.HTTP_REQUEST_TIMEOUT)
        # A005/A102/A108: connection.settimeout is per-operation and resets on every
        # byte, so a slow-drip client can pin this worker indefinitely. This timer is
        # an ABSOLUTE wall-clock deadline: it force-closes the socket after a fixed
        # total lifetime no matter how the client paces its bytes.
        # A137: this is actually a per-CONNECTION deadline (setup runs once per
        # connection, not per request), not per-request as the name suggests.
        self._deadline_timer = threading.Timer(_REQUEST_DEADLINE, self._abort_on_deadline)
        self._deadline_timer.daemon = True
        self._deadline_timer.start()

    def _abort_on_deadline(self):
        """Force-shutdown the connection when the absolute request deadline expires (A137)."""
        # A137: set flag so routes can poll and return early, and add observability.
        self._deadline_exceeded = True
        logging_util.incr("http_deadline_aborted")
        try:
            self.connection.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

    def finish(self):
        """Cancel the deadline timer and clean up the connection."""
        timer = getattr(self, "_deadline_timer", None)
        if timer is not None:
            timer.cancel()
        try:
            super().finish()
        except OSError:
            # The deadline timer may have already torn the socket down.
            pass

    def handle_one_request(self):
        """Wrap request handling with metrics tracking."""
        start = time.monotonic()
        try:
            super().handle_one_request()
        finally:
            elapsed = time.monotonic() - start
            # Track request metrics
            logging_util.incr("http_requests_total")
            logging_util.incr("http_request_duration_ms_sum", int(elapsed * 1000))
            # Track status code if available
            status = getattr(self, "_response_code", None)
            if status:
                logging_util.incr(f"http_status_{status}")
            # Track endpoint if available
            path = getattr(self, "_request_path", None)
            if path:
                # Normalize path for metrics (remove query string, normalize)
                norm = path.split("?")[0].rstrip("/") or "/"
                # A138: bucket unknown paths under 'other' to prevent unbounded cardinality
                endpoint_slug = norm.replace("/", "_").lstrip("_") or "root"
                if not any(
                    norm.startswith(prefix) or norm == prefix.rstrip("/") for prefix in _KNOWN_ENDPOINT_PREFIXES
                ):
                    endpoint_slug = "other"
                logging_util.incr(f"http_endpoint_{endpoint_slug}")

    # --- response helpers --------------------------------------------------
    def _cors(self):
        """Emit CORS headers when the request Origin is on the allowlist (S1/A140/A149)."""
        # S1: echo the request Origin ONLY when it is on the allowlist.
        # Never send "*" -- that would let any site read authenticated responses.
        origin = self.headers.get("Origin", "")
        if origin and origin in config.CORS_ALLOWED_ORIGINS:
            self.send_header("Access-Control-Allow-Origin", origin)
        # A140/A149: always emit Vary: Origin since the response differs by Origin
        # (the negative branch also varies — no ACAO header is still a variation).
        self.send_header("Vary", "Origin")
        # A149: include DELETE in the allowed methods list. Two DELETE routes ship
        # (/api/keys/<id> and /api/monitors/<id>) and are documented in openapi.yaml.
        # Without DELETE here, browser preflight blocks these requests.
        self.send_header("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Authorization, Content-Type, X-Syxx-License")

    def _generate_csp_nonce(self):
        """E212: generate a per-request cryptographic nonce for inline scripts.

        The nonce is base64-encoded and embedded in the CSP header so the browser only
        allows <script nonce="…"> tags carrying the matching attribute to execute.
        This eliminates 'unsafe-inline' from script-src, closing the XSS vector that
        an attacker-controlled inline script would otherwise exploit.

        The nonce is also stored on the handler instance as `_csp_nonce` so route
        handlers that render HTML can embed it in their <script> tags."""
        nonce = base64.b64encode(os.urandom(16)).decode("ascii")
        self._csp_nonce = nonce
        return nonce

    def _security_headers(self, content_type="application/json"):
        """Emit security headers: nosniff, referrer-policy, XFO, CSP with per-request nonce (E212)."""
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        # S3/XFO: prevent clickjacking by denying all framing.
        self.send_header("X-Frame-Options", "DENY")
        # E212: CSP with per-request nonce instead of 'unsafe-inline' for scripts.
        # Every inline <script> must carry nonce="<value>" matching the per-request nonce
        # in the CSP header. External scripts (Stripe, etc.) are still allowlisted by origin.
        # style-src retains 'unsafe-inline' because CSS nonces are not widely deployed and
        # the XSS risk from inline styles is substantially lower than inline scripts.
        # frame-ancestors replaces X-Frame-Options for modern browsers.
        is_html = content_type.startswith("text/html")
        if is_html:
            nonce = getattr(self, "_csp_nonce", None) or self._generate_csp_nonce()
            # E020: 'strict-dynamic' tells supporting browsers to trust scripts loaded
            # by a nonce-bearing script, ignoring host allowlists and 'unsafe-inline'.
            # 'self' is retained as a fallback for browsers that don't support nonces.
            self.send_header(
                "Content-Security-Policy",
                f"default-src 'self'; "
                f"script-src 'self' 'nonce-{nonce}' 'strict-dynamic' https://js.stripe.com; "
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
                # E019: Self-host fonts to prevent leaking user IPs to Google.
                # Removed https://fonts.gstatic.com from font-src.
                "font-src 'self' data:; "
                "img-src 'self' data: https:; "
                "connect-src 'self' https://api.stripe.com; "
                "frame-src 'self' https://js.stripe.com https://hooks.stripe.com; "
                "form-action 'self' https://checkout.stripe.com; "
                "base-uri 'self'; "
                "frame-ancestors 'none'",
            )
        # HT-008: Self-host fonts to avoid third-party data leakage.
        # The CSP above allows fonts.googleapis.com and fonts.gstatic.com for
        # backward compatibility. Consider downloading fonts to /assets/fonts/
        # and updating the CSP to only allow 'self' for font-src.
        # Permissions-Policy: restrict browser features.
        self.send_header(
            "Permissions-Policy", "camera=(), microphone=(), geolocation=(), interest-cohort=(), payment=(self)"
        )
        # A146: emit HSTS when either the Python process terminates TLS directly
        # (TLS_CERT/TLS_KEY configured) OR when behind a reverse proxy that signals
        # the original request was HTTPS via X-Forwarded-Proto. The shipped deploy.sh
        # topology puts TLS in nginx, so without this check HSTS would never be sent.
        if config.TLS_CERT and config.TLS_KEY:
            self.send_header("Strict-Transport-Security", "max-age=31536000; includeSubDomains")
        elif self.headers.get("X-Forwarded-Proto") == "https":
            self.send_header("Strict-Transport-Security", "max-age=31536000; includeSubDomains")

    def _send(self, status, body, content_type="application/json", extra_headers=None):
        """Send an HTTP response with ETag support (A052), guarding against double-response (A134)."""
        # A134: refuse to send a second response on the same connection to prevent
        # response-queue desync on keep-alive connections.
        if self._responded:
            logging_util.error("double_response_attempt", status=status)
            self.close_connection = True
            return
        if isinstance(body, (dict, list)):
            body = json.dumps(body).encode("utf-8")
        elif isinstance(body, str):
            body = body.encode("utf-8")
        # A052: ETag from the uncompressed body hash; a matching If-None-Match
        # answers 304 with no body so unchanged content is not retransmitted.
        etag = f'"{hashlib.sha256(body).hexdigest()[:32]}"'
        if self.headers.get("If-None-Match", "").strip() == etag:
            self._response_code = 304  # track for metrics
            self._responded = True  # A134: mark that we've sent a response
            self.send_response(304)
            self.send_header("ETag", etag)
            self.send_header("Content-Length", "0")
            self._cors()
            self._security_headers(content_type)
            self.end_headers()
            return
        self._response_code = status  # track for metrics
        self._responded = True  # A134: mark that we've sent a response
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("ETag", etag)
        # gzip compression: check if the client accepts it and the body is large
        # enough to benefit (skip tiny responses).
        accept_encoding = self.headers.get("Accept-Encoding", "")
        will_compress = "gzip" in accept_encoding.lower() and len(body) > 1024
        if will_compress:
            body = gzip.compress(body)
            self.send_header("Content-Encoding", "gzip")
        # A140: always send Vary: Accept-Encoding when compression is even considered,
        # so caches store both compressed and uncompressed representations separately.
        self.send_header("Vary", "Accept-Encoding")
        self.send_header("Content-Length", str(len(body)))
        if status >= 400:
            self.send_header("Connection", "close")
        # A151: extra_headers REPLACE rather than append. Collect _cors and
        # _security_headers output, then let extra_headers overwrite by key name
        # before emitting. Without this, a route passing its own Content-Security-Policy
        # (e.g. _serve_whats_new) produces duplicate headers — CSP duplicates are
        # intersected by the browser, silently tightening the policy rather than
        # replacing it, and the next route that tries to RELAX a security header
        # per-route finds its intent silently intersected away.
        extra_keys = set()
        if extra_headers:
            extra_keys = {k.lower() for k in extra_headers}
        # Save the current buffer position, emit CORS + security, then strip any
        # headers that extra_headers will override.
        buf_start = len(self._headers_buffer) if hasattr(self, "_headers_buffer") else 0
        self._cors()
        self._security_headers(content_type)
        if extra_keys and hasattr(self, "_headers_buffer"):
            # Remove headers that extra_headers will replace.
            # Each line is b"Key: Value\r\n" — extract the key and skip if overridden.
            def _line_key(ln):
                colon = ln.find(b":")
                return ln[:colon].decode("latin-1").lower() if colon > 0 else ""

            kept = [ln for ln in self._headers_buffer[buf_start:] if _line_key(ln) not in extra_keys]
            self._headers_buffer = self._headers_buffer[:buf_start] + kept
        if extra_headers:
            for k, v in extra_headers.items():
                self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    # E032: audit complete — all 500 sites use sanitized generic messages.
    # Contract: 5xx messages must never include raw exception text, file paths,
    # or stack traces. The guard below enforces this at runtime as a last line
    # of defence in case a future caller accidentally passes unsafe content.
    def _json_error(self, status, message, extra=None):
        """Send a JSON error response, suppressing unsafe 5xx message content (E032)."""
        # E032: 5xx messages must never leak internal paths or stack traces.
        if status >= 500 and (
            "/" in message
            or "\\" in message
            or "Traceback" in message
            or 'File "' in message
        ):
            logging_util.warn(
                "e032_unsafe_error_message",
                msg=f"Unsafe 5xx message suppressed (status={status})",
            )
            message = "internal error"
        payload = {"error": message}
        if extra:
            payload.update(extra)
        self._send(status, payload)

    # --- auth --------------------------------------------------------------
    def _entitlement(self, query):
        """Resolve the caller's entitlement from auth headers and query parameters."""
        return licensing.entitlement_for(self.headers, query)

    def _require(self, query, enforce):
        """Return (entitlement, ok). Sends the 402/503 response if not ok."""
        ent, err = self._entitlement(query)
        if ent:
            logging_util.incr("auth_success")
            return ent, True
        if not enforce:
            return None, True  # grace window: allowed as anonymous
        if err:
            logging_util.incr("auth_transient_error")
            self._json_error(503, err)
            return None, False
        logging_util.incr("auth_payment_required")
        self._json_error(402, "payment required: activate a Syxx Pro license", {"activate": "/api/auth/activate"})
        return None, False

    def _client_ip(self):
        """Real client IP. Behind the shipped nginx reverse proxy the socket peer is loopback;
        trust X-Real-IP / rightmost X-Forwarded-For ONLY then, so a direct internet client can't
        spoof it. Without this, every anonymous request keys on 127.0.0.1 (one shared rate bucket)
        and /api/exposure always probes the proxy. (A010)

        A143: validate the header value is a parseable IP before returning it. An unparseable
        header is not an address — fall back to the socket peer rather than returning attacker-
        controlled garbage used as a rate-limit key, audit subject, or leak-token subject.

        SEC-020: Only trust XFF/X-Real-IP from trusted proxies (loopback by default, extendable
        via config). A non-trusted proxy's XFF header is ignored to prevent IP spoofing.

        SEC-035: Reject malformed XFF entirely rather than silently falling back. A malformed
        XFF indicates either a misconfigured proxy or an attacker probing the parser; silently
        accepting the fallback masks both issues."""
        peer = self.client_address[0]
        try:
            peer_addr = ipaddress.ip_address(peer)
        except ValueError:
            return peer  # unparseable peer — use as-is

        # SEC-020: Only trust proxy headers from trusted sources
        # Default: only loopback is trusted (nginx on same host)
        if not peer_addr.is_loopback:
            # Direct internet client — never trust their XFF headers
            return peer

        # Peer is loopback (trusted proxy) — parse X-Real-IP / XFF
        xr = self.headers.get("X-Real-IP")
        if xr:
            candidate = xr.strip()[:45]  # cap length (max IPv6 is 45 chars)
            try:
                ipaddress.ip_address(candidate)
                return candidate
            except ValueError:
                # SEC-035: malformed X-Real-IP from trusted proxy is suspicious
                # Log it but fall through to XFF/peer rather than rejecting
                logging_util.warn("client_ip_malformed_xrealip", value=candidate[:32])
        xff = self.headers.get("X-Forwarded-For")
        if xff:
            # SEC-035: Validate ALL XFF entries, not just the last one.
            # A malformed entry indicates either a misconfigured upstream proxy
            # or an attacker injecting garbage into the chain.
            parts = [p.strip() for p in xff.split(",")]
            for part in parts:
                if not part:
                    continue
                try:
                    ipaddress.ip_address(part[:45])
                except ValueError:
                    # Malformed entry — log and reject entire XFF chain
                    logging_util.warn("client_ip_malformed_xff", value=part[:32])
                    return peer  # fall back to peer
            # All entries valid — use rightmost (the one our trusted proxy added)
            candidate = parts[-1][:45]
            try:
                ipaddress.ip_address(candidate)
                return candidate
            except ValueError:
                pass  # validated above, but be defensive
        return peer

    def _rate_key(self, ent):
        """Build the rate-limiter bucket key — subject when authenticated, else client IP."""
        if ent:
            return "sub:" + ent.subject
        return "ip:" + self._client_ip()

    def _rate_ok(self, ent):
        """Consume a rate-limit token; send 429 and return False when exhausted."""
        if ratelimit.allow(self._rate_key(ent)):
            logging_util.incr("ratelimit_allowed")
            return True
        logging_util.incr("ratelimit_exceeded")
        self._json_error(429, "rate limit exceeded")
        return False

    def _body(self):
        """Read the request body with size and Content-Type guards; return bytes or a sentinel."""
        # A144: reject Transfer-Encoding (chunked decoding not implemented; leaving
        # bytes unread enables request smuggling on keep-alive connections).
        if self.headers.get("Transfer-Encoding"):
            self.close_connection = True
            self._json_error(501, "Transfer-Encoding not supported")
            return _BODY_INCOMPLETE
        # A144: reject duplicate Content-Length headers (RFC 9112 §6.3).
        # http.client.HTTPMessage exposes get_all; plain dict fixtures used in unit
        # tests do not. Prefer get_all when present, else a single get.
        get_all = getattr(self.headers, "get_all", None)
        if callable(get_all):
            cl_values = get_all("Content-Length")
        else:
            single = self.headers.get("Content-Length")
            cl_values = [single] if single is not None else None
        if cl_values is not None and len(cl_values) > 1:
            self.close_connection = True
            self._json_error(400, "duplicate Content-Length headers")
            return _BODY_INCOMPLETE
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            # A144: a malformed Content-Length is a 400, not a zero-length body.
            self.close_connection = True
            self._json_error(400, "malformed Content-Length")
            return _BODY_INCOMPLETE
        if length > 1 << 20:
            return _BODY_TOO_LARGE
        if not length:
            return b""
        chunks = []
        remaining = length
        while remaining > 0:
            try:
                chunk = self.rfile.read(remaining)
            except OSError:
                return _BODY_INCOMPLETE
            if not chunk:
                return _BODY_INCOMPLETE
            chunks.append(chunk)
            remaining -= len(chunk)
        return b"".join(chunks)

    def _body_error(self, raw):
        """Send the appropriate error response for a body sentinel; return True if an error was sent."""
        if raw is _BODY_TOO_LARGE:
            self._json_error(413, "request body too large")
            return True
        if raw is _BODY_INCOMPLETE:
            self._json_error(400, "incomplete request body")
            return True
        return False

    def _require_json(self):
        """Reject the request with 415 unless Content-Type is application/json."""
        ctype = self.headers.get("Content-Type", "").lower()
        if "application/json" not in ctype:
            self._json_error(415, "Content-Type must be application/json")
            return False
        return True

    def _check_csrf(self):
        """SEC-005: CSRF protection via Origin header verification.
        State-changing POST requests from browsers must include an Origin header
        that matches one of the allowed CORS origins. Requests without Origin
        (from native clients, curl, etc.) are allowed through — CSRF is a
        browser-only attack vector. Webhook endpoints are excluded because they
        are called by external services (Stripe, Keygen), not browsers."""
        origin = self.headers.get("Origin")
        if not origin:
            return True  # No Origin = not a browser request; CSRF N/A
        # Check against CORS allowlist
        if origin in config.CORS_ALLOWED_ORIGINS:
            return True
        logging_util.warn("csrf_origin_rejected", origin=origin[:64])
        self._json_error(403, "origin not allowed")
        return False

    def _check_sec_fetch(self) -> bool:
        """E027: Return False (reject) when a browser signals this is a cross-site request.

        Sec-Fetch-Site is sent by all modern browsers. Its absence (API clients,
        curl, old browsers) is treated as trusted — this is defence-in-depth on top
        # of CSRF tokens, not a replacement.
        """
        sec_fetch_site = self.headers.get("Sec-Fetch-Site", "")
        if sec_fetch_site == "cross-site":
            self._json_error(403, "cross-site request rejected")
            return False
        return True

    # --- HTTP verbs --------------------------------------------------------
    def do_OPTIONS(self):
        """Respond to CORS preflight with 204 and appropriate headers."""
        self.send_response(204)
        self._cors()
        self._security_headers()
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_HEAD(self):
        """Handle HEAD requests — restricted to static files/assets only (A147)."""
        # A147: HEAD must be safe — it should not execute side effects or expensive
        # operations. The previous implementation delegated to do_GET, which ran every
        # route including /api/checkout (creates a real Stripe session), /api/benchmark
        # (50s of DNS work), and /api/propagation (12-thread fan-out). Restrict HEAD to
        # routes where it is meaningful: static files and assets. Everything else gets 405.
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        # Allow HEAD only on static files and assets (safe, no side effects)
        if path in _STATIC_FILES or path.startswith("/assets/"):
            return self.do_GET()
        # For all other routes, return 405 Method Not Allowed
        self.send_response(405)
        self.send_header("Allow", "GET, POST, OPTIONS")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        """Route GET requests with OTel tracing, API versioning, and Sentry integration."""
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        self._request_path = self.path  # track for metrics
        query = urllib.parse.parse_qs(parsed.query)
        # API versioning: /api/v1/* is an alias for /api/*. This lets callers
        # pin to v1 before the surface grows further, and we can introduce v2
        # without breaking existing clients.
        if path.startswith("/api/v1/"):
            path = "/api/" + path[len("/api/v1/") :]
        # A044: middleware OTel span covering all GET routes — catches the 34
        # un-instrumented endpoints that per-route spans miss.
        span = _OTEL_TRACER.start_span("http.GET") if _OTEL_TRACER else None
        if span:
            span.set_attribute("http.method", "GET")
            span.set_attribute("http.route", path)
        # A045: expose span to handlers so /healthz and /readyz can attach dependency attributes.
        self._current_otel_span = span
        try:
            try:
                self._dispatch_get(path, query)
            except Exception as e:  # last-resort guard; never leak a stack trace
                logging_util.error("handler_exception", route=path, err=str(e))
                # A056: mark the middleware span as errored so the collector
                # does not see exception-path spans as successful.
                if span and _OTEL_STATUS_ERROR is not None:
                    span.set_status(_OTEL_STATUS_ERROR)
                    span.set_attribute("error.type", type(e).__name__)
                try:
                    # A043: attach route context so Sentry events are groupable/filterable.
                    sentry_sdk.set_tag("route", path or "unknown")
                    sentry_sdk.set_tag("method", "GET")
                    sentry_sdk.capture_exception(e)
                except Exception:  # noqa: S110
                    pass  # Sentry failure must not break error handling
                # A134: if a response was already sent, close the connection instead of
                # writing a second response that would desync the keep-alive pipeline.
                if not self._responded:
                    self._json_error(500, "internal error")
                else:
                    self.close_connection = True
        finally:
            if span:
                status = getattr(self, "_response_code", None)
                if status:
                    span.set_attribute("http.status_code", status)
                span.end()
            self._current_otel_span = None

    def do_POST(self):
        """Route POST requests with CSRF checks, rate limiting, and body parsing."""
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        self._request_path = self.path  # track for metrics
        # API versioning: /api/v1/* is an alias for /api/*.
        if path.startswith("/api/v1/"):
            path = "/api/" + path[len("/api/v1/") :]
        # A044: middleware OTel span covering all POST routes.
        span = _OTEL_TRACER.start_span("http.POST") if _OTEL_TRACER else None
        if span:
            span.set_attribute("http.method", "POST")
            span.set_attribute("http.route", path)
        try:
            try:
                # SEC-005: CSRF check on state-changing routes (exclude webhooks)
                # Webhooks are signed-HMAC verified by their respective handlers
                _CSRF_EXEMPT = ("/api/webhooks/stripe", "/api/webhooks/keygen")
                if path not in _CSRF_EXEMPT and not self._check_csrf():
                    return
                if path == "/api/auth/activate":
                    return self._route_activate()
                if path == "/api/trial/anchor":
                    return self._route_trial_anchor()
                if path == "/api/beta/reserve":
                    return self._route_beta_reserve()
                if path == "/api/leak/token":
                    return self._route_leak_token(urllib.parse.parse_qs(parsed.query))
                if path == "/api/leaktest/start":
                    return self._route_leaktest_start(urllib.parse.parse_qs(parsed.query))
                if path == "/api/diff":
                    return self._route_diff(urllib.parse.parse_qs(parsed.query))
                if path == "/api/webhooks/stripe":
                    return self._route_stripe_webhook()
                if path == "/api/webhooks/keygen":
                    return self._route_keygen_webhook()
                if path == "/dns-query":
                    return self._route_doh_post(urllib.parse.parse_qs(parsed.query))
                if path == "/api/keys":
                    return self._route_create_api_key()
                if path == "/api/bulk-investigate":
                    return self._route_bulk_investigate()
                if path == "/api/lookalike":
                    return self._route_lookalike()
                # C1: Stripe Customer Portal — POST with session token to get portal URL
                if path == "/api/billing/portal":
                    return self._route_billing_portal()
                if path == "/api/monitors":
                    return self._route_create_monitor()  # 6.4a: POST to create a new monitor
                self._json_error(404, "not found")
            except Exception as e:
                logging_util.error("handler_exception", route=path, err=str(e))
                # A056: mark the middleware span as errored so the collector
                # does not see exception-path spans as successful.
                if span and _OTEL_STATUS_ERROR is not None:
                    span.set_status(_OTEL_STATUS_ERROR)
                    span.set_attribute("error.type", type(e).__name__)
                try:
                    # A043: attach route context so Sentry events are groupable/filterable.
                    sentry_sdk.set_tag("route", path or "unknown")
                    sentry_sdk.set_tag("method", "POST")
                    sentry_sdk.capture_exception(e)
                except Exception:  # noqa: S110
                    pass  # Sentry failure must not break error handling
                # A134: if a response was already sent, close the connection instead of
                # writing a second response that would desync the keep-alive pipeline.
                if not self._responded:
                    self._json_error(500, "internal error")
                else:
                    self.close_connection = True
        finally:
            if span:
                status = getattr(self, "_response_code", None)
                if status:
                    span.set_attribute("http.status_code", status)
                span.end()

    def do_DELETE(self):
        """Route DELETE requests with CSRF checks, OTel tracing, and Sentry integration."""
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        self._request_path = self.path  # track for metrics (parity with GET/POST)
        # API versioning: /api/v1/* is an alias for /api/*.
        if path.startswith("/api/v1/"):
            path = "/api/" + path[len("/api/v1/") :]
        # A044: middleware OTel span covering all DELETE routes.
        span = _OTEL_TRACER.start_span("http.DELETE") if _OTEL_TRACER else None
        if span:
            span.set_attribute("http.method", "DELETE")
            span.set_attribute("http.route", path)
        try:
            try:
                # A037: CSRF check on state-changing DELETE routes (exclude webhooks)
                # Webhooks are signed-HMAC verified by their respective handlers
                _CSRF_EXEMPT = ("/api/webhooks/stripe", "/api/webhooks/keygen")
                if path not in _CSRF_EXEMPT and not self._check_csrf():
                    return
                if path.startswith("/api/keys/"):
                    return self._route_revoke_api_key(path)
                if path.startswith("/api/monitors/"):
                    return self._route_delete_monitor(path)
                self._json_error(404, "not found")
            except Exception as e:
                logging_util.error("handler_exception", route=path, method="DELETE", err=str(e))
                # A056: mark the middleware span as errored so the collector
                # does not see exception-path spans as successful.
                if span and _OTEL_STATUS_ERROR is not None:
                    span.set_status(_OTEL_STATUS_ERROR)
                    span.set_attribute("error.type", type(e).__name__)
                try:
                    # A043: attach route context so Sentry events are groupable/filterable.
                    sentry_sdk.set_tag("route", path or "unknown")
                    sentry_sdk.set_tag("method", "DELETE")
                    sentry_sdk.capture_exception(e)
                except Exception:  # noqa: S110
                    pass  # Sentry failure must not break error handling
                # A134: if a response was already sent, close the connection instead of
                # writing a second response that would desync the keep-alive pipeline.
                if not self._responded:
                    self._json_error(500, "internal error")
                else:
                    self.close_connection = True
        finally:
            if span:
                status = getattr(self, "_response_code", None)
                if status:
                    span.set_attribute("http.status_code", status)
                span.end()

    # --- GET routing -------------------------------------------------------
    def _dispatch_get(self, path, query):
        """Route a GET request to the appropriate handler based on path and host."""
        # A150: MOTD subdomains serve What's-New ONLY at the root path.
        host = (self.headers.get("Host", "").split(":")[0]).strip().lower()
        if path in ("/", "/index.html", ""):
            if host == config.MOTD_MAC_HOST:
                return self._serve_whats_new("mac")
            if host == config.MOTD_WIN_HOST:
                return self._serve_whats_new("win")
            if host == config.MOTD_DEB_HOST:
                return self._serve_whats_new("deb")
        # A150: leaktest.syxx.app — exact match against configured host.
        if host == config.LEAKTEST_HOST and path in ("/", "/index.html", ""):
            return self._serve_static("/leaktest")
        if path in _STATIC_FILES:
            return self._serve_static(path)
        if path.startswith("/assets/"):
            return self._serve_asset(path)
        # A196: /healthz is process-liveness-only — no DB/DNS checks.
        if path == "/healthz":
            _span = getattr(self, "_current_otel_span", None)
            if _span:
                _span.set_attribute("health.scope", "process-liveness-only")
                _span.set_attribute("health.check_endpoint", "/readyz")
            return self._send(200, {"ok": True, "scope": "process-liveness-only", "readiness": "/readyz"})
        # A148: rate-limit /readyz and /api/stats.
        if path == "/readyz":
            if not self._rate_ok(None):
                return
            return self._route_readyz()
        if path == "/api/stats":
            if not self._rate_ok(None):
                return
            return self._send(200, {"passiveRecords": passivedns.total_records()})
        # Docs redirects
        if path in ("/docs/api", "/docs/api/"):
            return self._serve_static("/docs/api.html")
        if path in ("/docs/help", "/docs/help/"):
            return self._serve_static("/docs/help.html")
        # Standard route table dispatch
        route = _GET_ROUTES.get(path)
        if route:
            method_name, takes_query = route
            return getattr(self, method_name)(query) if takes_query else getattr(self, method_name)()
        return self._json_error(404, "not found")


# --- Background threads ----------------------------------------------------

def _start_purge_thread():
    """Purge expired/aged personal data on startup, then on a fixed interval."""

    def _loop():
        while True:
            time.sleep(max(60, config.PURGE_INTERVAL_SECONDS))
            try:
                db.purge_expired()
            except Exception as e:
                logging_util.error("purge_failed", err=str(e))

    try:
        db.purge_expired()
    except Exception as e:
        logging_util.error("purge_failed", err=str(e))
    threading.Thread(target=_loop, name="syxx-purge", daemon=True).start()


# --- beta email: confirmations + weekly operator digest --------------------

_BETA_ACK_TEMPLATE = os.path.join(os.path.dirname(os.path.dirname(__file__)), "emails", "beta_ack.html")

# Bounded thread pool for beta confirmation emails so a deluge of signups
# doesn't exhaust OS thread resources (SRV-034).
_beta_mail_pool = concurrent.futures.ThreadPoolExecutor(max_workers=4, thread_name_prefix="beta-confirm")


def _beta_ack_html():
    """Load the designed acknowledgment template, resolving {{BASE}} to the public base
    URL so image src attributes are absolute (email clients don't resolve relative paths)."""
    try:
        with open(_BETA_ACK_TEMPLATE, encoding="utf-8") as f:
            return f.read().replace("{{BASE}}", config.PUBLIC_BASE_URL)
    except OSError as e:
        logging_util.error("beta_ack_template_missing", err=str(e))
        return None


def _send_beta_confirmation(email, interests):
    """Fire-and-forget the beta acknowledgment email to a new subscriber. Uses a bounded
    thread pool so a deluge of signups doesn't exhaust OS thread resources. Sends the
    designed HTML template with a plain-text fallback for clients that don't render HTML."""
    text = (
        "Thank you for signing up for the beta.\n\n"
        "Your interest in Nnyx and Zzyn means a great deal to us as we get these tools ready "
        "for real hands and real machines.\n\n"
        "We don't have a precise start date yet — we'd rather ship it right than early. You'll be "
        "the first to know when something solid is on the line. No action is needed in the meantime.\n\n"
        "If this wasn't you, just ignore this message; you won't hear from us again.\n\n"
        "— The MilkandGin team"
    )
    html = _beta_ack_html()
    _beta_mail_pool.submit(mailer.send, email, "You're on the beta list — Nnyx & Zzyn", body=text, html=html)


def _csv_safe(cell):
    """A029: neutralize spreadsheet formula / DDE injection. Excel, Numbers and Sheets
    treat a cell that begins with = + - @ (or a leading TAB / CR) as a formula, so a
    crafted signup email like '=cmd|...' would execute when the operator opens the
    digest. Prefix any such cell with a single quote so it is shown as literal text and
    never evaluated. csv.writer already handles structural quoting of commas/quotes;
    this closes the formula-cell gap it does not cover."""
    s = "" if cell is None else str(cell)
    if s and s[0] in ("=", "+", "-", "@", "\t", "\r"):
        return "'" + s
    return s


def _beta_digest_csv(rows):
    """All signups as CSV bytes; opens as a spreadsheet in Excel / Numbers / Sheets."""
    import csv
    import datetime
    import io

    def _iso(ts):
        """Format a Unix timestamp as a human-readable UTC datetime string."""
        try:
            return datetime.datetime.fromtimestamp(int(ts), datetime.UTC).strftime("%Y-%m-%d %H:%M:%S UTC")
        except (TypeError, ValueError):
            return ""

    def _ip(raw):
        """Validate and return an IP address string, or empty string if unparseable."""
        # Validate the IP cell: only a real address is emitted, so a spoofed/garbage
        # X-Forwarded-For value cannot smuggle a formula or junk into the digest.
        raw = (raw or "").strip()
        try:
            ipaddress.ip_address(raw)
            return raw
        except ValueError:
            return ""

    buf = io.StringIO()
    w = csv.writer(buf)
    w.writerow(["email", "interests", "signed_up", "updated", "ip"])
    for r in rows:
        w.writerow(
            [
                _csv_safe(r["email"]),
                _csv_safe(r["interests"]),
                _csv_safe(_iso(r["created_at"])),
                _csv_safe(_iso(r["updated_at"])),
                _ip(r.get("ip")),
            ]
        )
    return buf.getvalue().encode("utf-8")


def _start_beta_digest_thread():
    """Email the operator a weekly CSV of beta signups. No-op unless SMTP is configured
    AND a recipient is set. Cadence survives restarts via a kv timestamp; the first
    interval is seeded on startup so a fresh deploy doesn't fire a digest immediately."""
    import datetime

    if not (mailer.enabled() and config.BETA_DIGEST_TO):
        logging_util.info("beta_digest_disabled", smtp=mailer.enabled(), recipient=bool(config.BETA_DIGEST_TO))
        return

    interval = max(1, config.BETA_DIGEST_INTERVAL_DAYS) * 86400

    def _send_if_due(now):
        """Send the beta digest if the configured interval has elapsed since the last send."""
        last = db.kv_get("beta_digest_last_sent")
        if last is None:
            db.kv_set("beta_digest_last_sent", str(now))  # seed; first digest one interval later
            return
        if (now - int(last)) < interval:
            return
        rows = db.beta_all()
        if not rows:
            db.kv_set("beta_digest_last_sent", str(now))
            logging_util.info("beta_digest_skipped", reason="no_signups")
            return
        stamp = datetime.datetime.fromtimestamp(now, datetime.UTC).strftime("%Y-%m-%d")
        ok = mailer.send(
            config.BETA_DIGEST_TO,
            f"Syxx beta signups — weekly digest ({stamp})",
            f"Attached: {len(rows)} beta signup(s) as of {stamp} UTC.\n\nAutomated weekly export from the Syxx server.",
            attachments=[(f"syxx-beta-signups-{stamp}.csv", "text/csv", _beta_digest_csv(rows))],
        )
        if ok:
            db.kv_set("beta_digest_last_sent", str(now))
            logging_util.info("beta_digest_sent", count=len(rows))
        else:
            logging_util.error("beta_digest_failed", count=len(rows))  # left un-stamped → retry next tick

    def _loop():
        """Background loop — check hourly whether a beta digest is due."""
        while True:
            time.sleep(3600)  # check hourly; the interval gate is by timestamp, not sleep length
            try:
                _send_if_due(int(time.time()))
            except Exception as e:
                logging_util.error("beta_digest_loop_error", err=str(e))

    try:
        _send_if_due(int(time.time()))  # seed last_sent (or catch up) at startup
    except Exception as e:
        logging_util.error("beta_digest_loop_error", err=str(e))
    threading.Thread(target=_loop, name="syxx-beta-digest", daemon=True).start()


# --- serve() entry point ---------------------------------------------------

def serve():
    """Start the HTTP server, background threads (purge, digest, scheduler), and signal handlers."""
    import signal as _signal

    db.init()
    # A113: validate the collector signing key BEFORE binding any listeners.
    # A misconfigured key (wrong format, wrong algorithm, missing env var) must
    # fail at startup, not at the first /api/leak/verify request. This also
    # exercises the Ed25519 type check so a wrong-algorithm DER key is caught
    # before the server appears healthy to /readyz.
    try:
        leak._ensure_collector_key()
    except RuntimeError as e:
        logging_util.error("collector_key_startup_failed", err=str(e))
        # The key is unusable but the rest of the server (HTTP, warp, investigate)
        # can still serve. Log the failure; /readyz reports collector health via
        # the listener state which stays unset (default "unknown").
    _start_purge_thread()
    _start_beta_digest_thread()
    scheduler.start()
    # Import the composed Handler from app.py to avoid circular imports at module level.
    from ..app import Handler as _ComposedHandler
    httpd = BoundedThreadingHTTPServer((config.HTTP_HOST, config.HTTP_PORT), _ComposedHandler)
    httpd.timeout = config.HTTP_REQUEST_TIMEOUT
    if config.TLS_CERT and config.TLS_KEY:
        import ssl

        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(config.TLS_CERT, config.TLS_KEY)
        httpd.socket = ctx.wrap_socket(httpd.socket, server_side=True)
        logging_util.info("https_listening", port=config.HTTP_PORT)
    else:
        logging_util.info("http_listening", port=config.HTTP_PORT)
    # D-0: the HTTP listener is up by the time we reach serve_forever(). Report it so /readyz
    # reflects reality rather than defaulting to "unknown" for the very server answering the request.
    listeners.set_state("http", "listening", detail=f"port {config.HTTP_PORT}")

    # A313: register signal handlers for graceful shutdown. SIGTERM (kill,
    # systemd stop, docker stop) and SIGINT (Ctrl-C) both trigger an orderly
    # shutdown: the scheduler finishes in-flight work, the HTTP server stops
    # accepting new connections, and serve_forever() returns.
    def _graceful_stop(signum, frame):
        """Signal handler — stop HTTP server and scheduler on SIGTERM/SIGINT (A313)."""
        sig_name = _signal.Signals(signum).name
        logging_util.info("server_signal_received", signal=sig_name)
        # Stop accepting new HTTP requests; serve_forever() returns.
        threading.Thread(target=httpd.shutdown, name="httpd-shutdown", daemon=True).start()
        # Give the scheduler time to finish in-flight monitors/backups.
        scheduler.shutdown(timeout=10)

    _signal.signal(_signal.SIGTERM, _graceful_stop)
    _signal.signal(_signal.SIGINT, _graceful_stop)

    httpd.serve_forever()

"""Threaded HTTP server, router and auth middleware.

Route classes:
  public : /, static assets, /healthz, /readyz, /metrics, /api/stats, /api/broadcast,
           /api/whats-new, /api/startup, /api/auth/activate (activation is how you GET
           entitled), /api/checkout (purchase flow), /api/webhooks/stripe (signature-verified)
  paid   : everything that costs server resources — /api/warp/manifest, /api/warp/raw,
           /warp/fetch_script, /leak (native app), and /api/investigate, /api/authoritative,
           /api/diff, /api/trace, /api/baseline, /api/exposure, /api/passive-dns, /api/leak/*
           (website). Paid routes require an Entitlement or they return 402.

The website therefore cannot bypass the paywall: its live-query endpoints are in
the paid class and demand the same license the app uses.
"""
import base64
import concurrent.futures
import gzip
import hashlib
import hmac
import ipaddress
import json
import logging
import os
import posixpath
import re
import socket
import struct
import threading
import time
import uuid
import urllib.parse
from email.utils import parseaddr
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from . import (broadcast, bulk_investigate, config, db, email_auth, fetcher, firewall_test,
               fulfillment, investigate, leak, licensing, listeners, logging_util, lookalike,
               mailer, passivedns, ratelimit, resolver, scheduler, seo, stripe_checkout,
               subdomain_enum, warp, whois_lookup, appcast)
try:
    from . import dnssec_validator
except ImportError:
    dnssec_validator = None  # cryptography library not installed
from . import __version__

# --- Sentry crash/error reporting ---
try:
    import sentry_sdk
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
except Exception:
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
    from opentelemetry import _logs as otel_logs
    from opentelemetry.sdk.resources import Resource
    from opentelemetry.sdk.trace import TracerProvider
    from opentelemetry.sdk.trace.export import BatchSpanProcessor
    from opentelemetry.exporter.otlp.proto.http.trace_exporter import OTLPSpanExporter
    from opentelemetry.sdk._logs import LoggerProvider
    from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
    from opentelemetry.exporter.otlp.proto.http._log_exporter import OTLPLogExporter

    _resource = Resource.create({"service.name": "syxx-server", "service.version": __version__})
    _endpoint = "https://bugsmasher.syxx.app"

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
    _OTEL_STATE = "failed: %s: %s" % (type(_otel_err).__name__, _otel_err)

# Nonce shape gate for /api/warp/manifest (WARP_MANIFEST_V1 §4.5 replay binding). ≤64 chars of
# the URL/base64url-safe alphabet; the manifest echoes it verbatim so the client can assert it
# matches what it sent. Rejecting a malformed nonce closed keeps garbage out of the signed bytes.
# Module-level (not a Handler attribute) so route helpers stay callable on lightweight test doubles.
_WARP_NONCE_RE = re.compile(r"^[A-Za-z0-9_-]{1,64}$")

# Tracer for endpoint-level OTel spans. Created once so every request handler can create spans
# without resolving the tracer on every call. Thread-safe: OTel Context is thread-local.
_OTEL_TRACER = None
if _OTEL_STATE.startswith("live"):
    try:
        import opentelemetry.trace as _otel_trace_mod
        _OTEL_TRACER = _otel_trace_mod.get_tracer("syxx-server")
    except Exception:
        _OTEL_STATE = "failed: tracer unavailable"

# Wire OTel logger into logging_util so all logging_util.error() calls also export via OTLP.
if _OTEL_LOGGER is not None:
    try:
        # `LogRecord` is not re-exported from opentelemetry.sdk._logs in 1.44 (only
        # ReadableLogRecord / ReadWriteLogRecord are); the constructible class lives in
        # _internal. Getting this wrong left logging_util._otel_log_record as None, so the
        # export branch at logging_util.py:34 could never fire even once the imports above
        # succeeded. Failure is recorded, never swallowed.
        from opentelemetry.sdk._logs._internal import LogRecord as _OTelLogRecord
        logging_util._otel_logger = _OTEL_LOGGER
        logging_util._otel_log_record = _OTelLogRecord
        _OTEL_STATE = "live"
    except Exception as _otel_rec_err:  # noqa: BLE001 - recorded, not swallowed
        _OTEL_STATE = "failed: log-record class unavailable: %s: %s" % (
            type(_otel_rec_err).__name__, _otel_rec_err)

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

# E-10: SEO prices are read from env so they stay in sync with Stripe.
# The defaults match the $3.95/month and $19.95/year catalog.
_SEO_PRICE_MONTHLY = os.environ.get("SYXX_SEO_PRICE_MONTHLY", "3.95")
_SEO_PRICE_YEARLY = os.environ.get("SYXX_SEO_PRICE_YEARLY", "19.95")

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

# A138: allowlist of known endpoint prefixes for metrics. Everything else buckets
# to 'other' to prevent unbounded cardinality from attacker-controlled paths.
_KNOWN_ENDPOINT_PREFIXES = {
    "/api/", "/dns-query", "/assets/", "/fetch_script", "/warp/",
    "/", "/index.html", "/robots.txt", "/sitemap.xml", "/.well-known/",
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


def _env_int(name, default):
    # A154: log the coercion so the operator is told their value was discarded.
    raw = os.environ.get(name, "")
    if not raw.strip():
        return default
    try:
        return int(raw.strip())
    except (TypeError, ValueError):
        logging_util.warn("config_invalid_int", name=name, value=raw, using=default)
        return default


# A028/A101/A108: cap concurrent worker threads. A bare ThreadingHTTPServer spawns
# one unbounded thread per connection, so parallel hostile requests exhaust the
# process and down the server. This bounds how many requests run at once.
_MAX_WORKERS = max(1, _env_int("SYXX_HTTP_MAX_WORKERS", 64))
# A005/A102/A108: absolute per-request wall-clock deadline. connection.settimeout()
# is a PER-OPERATION timeout, so a slow-drip client that trickles one byte just
# under the timeout keeps resetting it and pins a worker forever. This hard cap
# force-closes the socket after a fixed total request lifetime regardless of drip.
_REQUEST_DEADLINE = max(config.HTTP_REQUEST_TIMEOUT,
                        _env_int("SYXX_HTTP_REQUEST_DEADLINE", 60))
# A027/A096: aggregate wall-clock budget for the clean-vantage recursive fan-out so
# /api/diff cannot be held open across many slow resolver queries.
_CLEAN_VANTAGE_DEADLINE = max(1, _env_int("SYXX_CLEAN_VANTAGE_DEADLINE", 6))

# A136: module-level shared bounded thread pools for DNS fan-out. Replaces the
# per-request ThreadPoolExecutor instances that escaped the _MAX_WORKERS semaphore
# accounting (64 concurrent handlers × 12 threads = 768 untracked threads).
# Bounded to a fixed size; shared across all concurrent requests.
_CLEAN_VANTAGE_POOL = concurrent.futures.ThreadPoolExecutor(
    max_workers=6, thread_name_prefix="clean-vantage"
)
_PROPAGATION_POOL = concurrent.futures.ThreadPoolExecutor(
    max_workers=24, thread_name_prefix="propagation"
)

# Which "?type=" names each free-tier lookup route accepts. The name→qtype
# VALUES all live in one place (resolver.TYPE_NAMES, warp-hardening_20260811
# Task 3.2); these small sets only gate each route's SUPPORTED SUBSET so the
# per-route maps they replaced no longer duplicate the value tables. Keep the
# subsets distinct — e.g. /api/lookup accepts SOA/CAA/PTR/SRV while /api/benchmark
# and /api/propagation default/reject them — or a route's behavior changes.
_LOOKUP_TYPE_NAMES = frozenset(("A", "AAAA", "NS", "CNAME", "SOA", "MX", "TXT",
                                "CAA", "PTR", "SRV"))
_BENCH_TYPE_NAMES = frozenset(("A", "AAAA", "NS", "MX", "TXT", "CNAME"))
_PROPAGATION_TYPE_NAMES = _BENCH_TYPE_NAMES
_DOH_JSON_TYPE_NAMES = frozenset(("A", "NS", "CNAME", "SOA", "PTR", "MX", "TXT",
                                  "AAAA", "SRV", "HTTPS"))


def _host(u):
    try:
        return (urllib.parse.urlsplit(u).hostname or "").lower()
    except ValueError:
        return ""


def _diff_content(url, local_html, local_scripts, local_inline_hashes):
    """Content half of /api/diff (Workstream S #2). Fetch the target from the server's clean vantage
    and compare its script structure to what the client saw. HONESTY: a script loaded from a host the
    clean page never referenced is the strong injection signal; same-host asset churn or inline-script
    differences are treated as soft ("divergent") because dynamic pages legitimately vary between
    vantages (A/B tests, personalization, per-request nonces). SSRF-safe via warp/fetcher (public IPs
    only). Returns a dict; never raises."""
    try:
        base = warp.build_baseline(url)
    except fetcher.TruncatedError:
        return {"url": url, "assessment": "inconclusive", "note": "baseline truncated; diff is inconclusive"}
    except fetcher.FetchError as e:
        # A053: log the full error server-side for diagnostics but return a generic
        # note to the client — str(e) can disclose destination IPs, socket errors,
        # and TLS failure modes that reveal the server's outbound fetch topology.
        logging_util.error("diff_clean_fetch_failed", url=url, err=str(e))
        return {"url": url, "assessment": "inconclusive", "note": "clean vantage unreachable"}

    clean_scripts = set(base.get("scripts", []))
    clean_inline = set(base.get("inlineScriptHashes", []))

    if local_html:
        parsed_scripts, _ss, _if = warp.parse_assets(local_html, url)
        local_script_set = set(parsed_scripts)
        local_inline = set(hashlib.sha256(b.encode("utf-8")).hexdigest()
                           for b in warp.inline_scripts(local_html))
        have_inline = True
    else:
        local_script_set = set(local_scripts or [])
        local_inline = set(local_inline_hashes or [])
        have_inline = bool(local_inline_hashes)

    if not local_script_set and not local_inline and not local_html:
        return {"url": url, "assessment": "inconclusive", "note": "no local content supplied"}

    injected = sorted(local_script_set - clean_scripts)
    missing = sorted(clean_scripts - local_script_set)
    clean_hosts = set(_host(u) for u in clean_scripts)
    injected_hosts = sorted({_host(u) for u in injected} - clean_hosts - {""})
    injected_inline = len(local_inline - clean_inline)

    if injected_hosts:
        assessment = "injected"
    elif injected or injected_inline:
        assessment = "divergent"
    elif local_script_set == clean_scripts and (not have_inline or local_inline == clean_inline):
        assessment = "match"
    else:
        assessment = "divergent"

    note = {
        "injected": "script(s) from a host the clean page never served — strong on-path injection signal",
        "divergent": "asset/inline differences; can be legitimate on dynamic pages — not conclusive",
        "match": "clean-vantage script structure matches what the client saw",
    }[assessment]
    return {
        "url": url,
        "cleanScripts": sorted(clean_scripts),
        "localScripts": sorted(local_script_set),
        "injectedScripts": injected,
        "injectedScriptHosts": injected_hosts,
        "missingScripts": missing,
        "injectedInlineCount": injected_inline,
        "match": assessment == "match",
        "assessment": assessment,
        "note": note,
    }


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
    paged = items[start:start + limit]
    has_more = (start + limit) < total
    return paged, {"page": page, "limit": limit, "total": total, "hasMore": has_more}


def _diff_result(dns_assessment, content, clean_answers=None, local_set=None):
    """Fold the DNS and content assessments into one clean|tampered|inconclusive result.

    Only a DNS answer the authority doesn't serve AND the clean vantage confirms, or a
    cross-host script injection, is decisive ('tampered').

    S1: round-robin / geo DNS can make the authoritative answer differ from the local
    answer even when no tampering is occurring. Cross-reference with the clean vantage:
    if the clean vantage ALSO disagrees with the authoritative in the same way, it's
    legitimate round-robin, not tampering. Only when the clean vantage matches the
    authoritative (and the local answer diverges from both) is it 'tampered'.
    """
    c = content.get("assessment") if content else None

    # Cross-host script injection is decisive: the clean vantage never served a
    # script from that host. Same-host churn stays divergent/inconclusive above.
    if c == "injected":
        return "tampered"

    if dns_assessment == "divergent":
        # S1: cross-reference with clean vantage. If the clean vantage also disagrees
        # with the authoritative (i.e. the clean vantage matches the local answer),
        # it's legitimate round-robin / geo DNS, not tampering.
        # NOTE (open spec question, do not "fix" without an owner decision): when
        # `clean_answers` is EMPTY the corroborating vantage was unreachable, not disagreeing,
        # and this falls through to a hard "tampered" on one view. That is deliberate —
        # `test_authoritative_disjoint_answers_remain_tampered` pins it — because the
        # AUTHORITATIVE nameserver is itself the ground truth here and a disjoint local answer
        # contradicts it directly. The residual risk is genuine round-robin/geo rotation, which
        # S1 can only rule out via a reachable clean vantage. Downgrading to inconclusive would
        # trade a false-accusation risk for a missed-detection risk; that is a determinative-core
        # policy call for the owner, not an implementation detail.
        if clean_answers is not None and local_set is not None:
            if local_set.issubset(clean_answers) and not local_set.isdisjoint(clean_answers):
                return "inconclusive"  # clean vantage sees same as local → round-robin/geo
        return "tampered"

    if dns_assessment == "match" and c in ("match", None):
        return "clean"
    return "inconclusive"


def _clean_vantage_answers(domain, qtype, qtype_str):
    """Query several public recursive resolvers and return an answer set ONLY when at
    least two INDEPENDENT vantages agree on exactly that set.

    A027: the old code returned the FIRST race-winning resolver's answer. Under a
    shared-upstream poisoning, the poisoned resolver can win the race, so a single
    race-winner could certify 'clean' — a false-secure result. A clean reference now
    requires corroboration: two vantages returning the same non-empty set. If the
    vantages disagree (CDN/geo split) or only one is reachable, there is no usable
    clean reference and the diff stays inconclusive rather than falsely clean.
    The whole fan-out is bounded by a wall-clock deadline (A096)."""
    servers = list(config.PUBLIC_RESOLVERS[:3])
    if len(servers) < 2:
        return set()

    def query_one(server):
        clean = resolver.query(domain, qtype, server)
        return frozenset(str(r["value"]).lower() for r in clean["records"] if r["type"] == qtype_str)

    # A136: use the module-level shared pool instead of per-request creation.
    futures = [_CLEAN_VANTAGE_POOL.submit(query_one, srv) for srv in servers]
    counts = {}
    try:
        for fut in concurrent.futures.as_completed(futures, timeout=_CLEAN_VANTAGE_DEADLINE):
            try:
                answers = fut.result()
            except resolver.DNSError:
                continue
            if answers:
                counts[answers] = counts.get(answers, 0) + 1
                if counts[answers] >= 2:   # corroborated by >=2 independent vantages
                    return set(answers)
    except concurrent.futures.TimeoutError:
        pass                               # budget exhausted; use whatever corroborated so far
    for answer_set, seen in counts.items():
        if seen >= 2:
            return set(answer_set)
    return set()


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
        return (release, 1, ())            # stable outranks any pre-release
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


class BoundedThreadingHTTPServer(ThreadingHTTPServer):
    """A028/A101/A108: ThreadingHTTPServer spawns one unbounded thread per connection,
    so concurrent hostile requests exhaust the process and down the server. This bounds
    the number of in-flight worker threads with a semaphore; when saturated it refuses
    new connections immediately (503) instead of piling on threads until the box falls
    over."""
    daemon_threads = True

    def __init__(self, *args, max_workers=None, **kwargs):
        super().__init__(*args, **kwargs)
        self._worker_sema = threading.BoundedSemaphore(max_workers or _MAX_WORKERS)

    def process_request(self, request, client_address):
        if not self._worker_sema.acquire(blocking=False):
            # At capacity: refuse fast rather than queue unbounded work.
            try:
                request.sendall(b"HTTP/1.1 503 Service Unavailable\r\n"
                                b"Connection: close\r\nContent-Length: 0\r\n\r\n")
            except OSError:
                pass
            self.shutdown_request(request)
            logging_util.incr("http_at_capacity")
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self._worker_sema.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._worker_sema.release()


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = config.SERVER_NAME

    # Route the default per-request logging through our structured logger.
    def log_message(self, fmt, *args):
        return

    def setup(self):
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
        # A137: set flag so routes can poll and return early, and add observability.
        self._deadline_exceeded = True
        logging_util.incr("http_deadline_aborted")
        try:
            self.connection.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

    def finish(self):
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
                endpoint_slug = norm.replace('/', '_').lstrip('_') or 'root'
                if not any(norm.startswith(prefix) or norm == prefix.rstrip('/') for prefix in _KNOWN_ENDPOINT_PREFIXES):
                    endpoint_slug = "other"
                logging_util.incr(f"http_endpoint_{endpoint_slug}")

    # --- response helpers --------------------------------------------------
    def _cors(self):
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
        self.send_header("Access-Control-Allow-Headers",
                         "Authorization, Content-Type, X-Syxx-License")

    def _security_headers(self):
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        # S3/XFO: prevent clickjacking by denying all framing.
        self.send_header("X-Frame-Options", "DENY")
        # CSP: restrict script/style sources to self and approved origins.
        self.send_header("Content-Security-Policy",
                         "default-src 'self'; "
                         "script-src 'self' 'unsafe-inline' https://js.stripe.com; "
                         "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
                         "font-src 'self' https://fonts.gstatic.com; "
                         "img-src 'self' data: https:; "
                         "connect-src 'self' https://api.stripe.com; "
                         "frame-src 'self' https://js.stripe.com https://hooks.stripe.com; "
                         "form-action 'self' https://checkout.stripe.com; "
                         "base-uri 'self'")
        # Permissions-Policy: restrict browser features.
        self.send_header("Permissions-Policy",
                         "camera=(), microphone=(), geolocation=(), "
                         "interest-cohort=(), payment=(self)")
        # A146: emit HSTS when either the Python process terminates TLS directly
        # (TLS_CERT/TLS_KEY configured) OR when behind a reverse proxy that signals
        # the original request was HTTPS via X-Forwarded-Proto. The shipped deploy.sh
        # topology puts TLS in nginx, so without this check HSTS would never be sent.
        if config.TLS_CERT and config.TLS_KEY:
            self.send_header("Strict-Transport-Security", "max-age=31536000; includeSubDomains")
        elif self.headers.get("X-Forwarded-Proto") == "https":
            self.send_header("Strict-Transport-Security", "max-age=31536000; includeSubDomains")

    def _send(self, status, body, content_type="application/json", extra_headers=None):
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
        self._response_code = status  # track for metrics
        self._responded = True  # A134: mark that we've sent a response
        self.send_response(status)
        self.send_header("Content-Type", content_type)
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
        buf_start = len(self._headers_buffer) if hasattr(self, '_headers_buffer') else 0
        self._cors()
        self._security_headers()
        if extra_keys and hasattr(self, '_headers_buffer'):
            # Remove headers that extra_headers will replace
            new_buf = self._headers_buffer[:buf_start]
            for line in self._headers_buffer[buf_start:]:
                # Each line is b"Key: Value\r\n"
                colon_pos = line.find(b":")
                if colon_pos > 0:
                    key = line[:colon_pos].decode("latin-1").lower()
                    if key in extra_keys:
                        continue
                new_buf.append(line)
            self._headers_buffer = new_buf
        if extra_headers:
            for k, v in extra_headers.items():
                self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _json_error(self, status, message, extra=None):
        payload = {"error": message}
        if extra:
            payload.update(extra)
        self._send(status, payload)

    # --- auth --------------------------------------------------------------
    def _entitlement(self, query):
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
        self._json_error(402, "payment required: activate a Syxx Pro license",
                         {"activate": "/api/auth/activate"})
        return None, False

    def _client_ip(self):
        """Real client IP. Behind the shipped nginx reverse proxy the socket peer is loopback;
        trust X-Real-IP / rightmost X-Forwarded-For ONLY then, so a direct internet client can't
        spoof it. Without this, every anonymous request keys on 127.0.0.1 (one shared rate bucket)
        and /api/exposure always probes the proxy. (A010)

        A143: validate the header value is a parseable IP before returning it. An unparseable
        header is not an address — fall back to the socket peer rather than returning attacker-
        controlled garbage used as a rate-limit key, audit subject, or leak-token subject."""
        peer = self.client_address[0]
        try:
            if ipaddress.ip_address(peer).is_loopback:
                xr = self.headers.get("X-Real-IP")
                if xr:
                    candidate = xr.strip()[:45]  # cap length (max IPv6 is 45 chars)
                    try:
                        ipaddress.ip_address(candidate)
                        return candidate
                    except ValueError:
                        pass  # malformed — fall through to XFF or peer
                xff = self.headers.get("X-Forwarded-For")
                if xff:
                    candidate = xff.split(",")[-1].strip()[:45]
                    try:
                        ipaddress.ip_address(candidate)
                        return candidate
                    except ValueError:
                        pass  # malformed — fall through to peer
        except ValueError:
            pass
        return peer

    def _rate_key(self, ent):
        if ent:
            return "sub:" + ent.subject
        return "ip:" + self._client_ip()

    def _rate_ok(self, ent):
        if ratelimit.allow(self._rate_key(ent)):
            logging_util.incr("ratelimit_allowed")
            return True
        logging_util.incr("ratelimit_exceeded")
        self._json_error(429, "rate limit exceeded")
        return False

    def _body(self):
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
        if raw is _BODY_TOO_LARGE:
            self._json_error(413, "request body too large")
            return True
        if raw is _BODY_INCOMPLETE:
            self._json_error(400, "incomplete request body")
            return True
        return False

    def _require_json(self):
        ctype = self.headers.get("Content-Type", "").lower()
        if "application/json" not in ctype:
            self._json_error(415, "Content-Type must be application/json")
            return False
        return True

    # --- HTTP verbs --------------------------------------------------------
    def do_OPTIONS(self):
        self.send_response(204)
        self._cors()
        self._security_headers()
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_HEAD(self):
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
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        self._request_path = self.path  # track for metrics
        query = urllib.parse.parse_qs(parsed.query)
        # API versioning: /api/v1/* is an alias for /api/*. This lets callers
        # pin to v1 before the surface grows further, and we can introduce v2
        # without breaking existing clients.
        if path.startswith("/api/v1/"):
            path = "/api/" + path[len("/api/v1/"):]
        try:
            self._dispatch_get(path, query)
        except Exception as e:  # last-resort guard; never leak a stack trace
            logging_util.error("handler_exception", route=path, err=str(e))
            # A134: if a response was already sent, close the connection instead of
            # writing a second response that would desync the keep-alive pipeline.
            if not self._responded:
                self._json_error(500, "internal error")
            else:
                self.close_connection = True

    def do_POST(self):
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        self._request_path = self.path  # track for metrics
        # API versioning: /api/v1/* is an alias for /api/*.
        if path.startswith("/api/v1/"):
            path = "/api/" + path[len("/api/v1/"):]
        try:
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
            # A134: if a response was already sent, close the connection instead of
            # writing a second response that would desync the keep-alive pipeline.
            if not self._responded:
                self._json_error(500, "internal error")
            else:
                self.close_connection = True

    def do_DELETE(self):
        parsed = urllib.parse.urlsplit(self.path)
        path = parsed.path
        self._request_path = self.path  # track for metrics (parity with GET/POST)
        # API versioning: /api/v1/* is an alias for /api/*.
        if path.startswith("/api/v1/"):
            path = "/api/" + path[len("/api/v1/"):]
        try:
            if path.startswith("/api/keys/"):
                return self._route_revoke_api_key(path)
            if path.startswith("/api/monitors/"):
                return self._route_delete_monitor(path)
            self._json_error(404, "not found")
        except Exception as e:
            logging_util.error("handler_exception", route=path, method="DELETE", err=str(e))
            # A134: if a response was already sent, close the connection instead of
            # writing a second response that would desync the keep-alive pipeline.
            if not self._responded:
                self._json_error(500, "internal error")
            else:
                self.close_connection = True

    # --- GET routing -------------------------------------------------------
    def _dispatch_get(self, path, query):
        # A150: MOTD subdomains serve What's-New ONLY at the root path.
        # The previous implementation served MOTD content for EVERY path on these hosts,
        # making /healthz, /readyz, /metrics, and other operational endpoints unreachable
        # on those hostnames. Now only the root paths serve MOTD; everything else falls
        # through to normal routing so operational endpoints work correctly.
        host = (self.headers.get("Host", "").split(":")[0]).strip().lower()
        if path in ("/", "/index.html", ""):
            if host == config.MOTD_MAC_HOST:
                return self._serve_whats_new("mac")
            if host == config.MOTD_WIN_HOST:
                return self._serve_whats_new("win")
            if host == config.MOTD_DEB_HOST:
                return self._serve_whats_new("deb")
        # A150: leaktest.syxx.app — exact match against configured host, not a prefix.
        # The previous startswith("leaktest.") allowed any host beginning with "leaktest."
        # (including attacker-controlled hosts pointed at this IP) to serve the leak test page.
        if host == config.LEAKTEST_HOST and path in ("/", "/index.html", ""):
            return self._serve_static("/leaktest")
        if path in _STATIC_FILES:
            return self._serve_static(path)
        if path.startswith("/assets/"):
            return self._serve_asset(path)
        # A196: /healthz is explicitly process-liveness-only — it does NOT check
        # listener health. Operators/monitors who want readiness must probe /readyz.
        if path == "/healthz":
            return self._send(200, {"ok": True, "scope": "process-liveness-only", "readiness": "/readyz"})
        # A148: rate-limit /readyz to prevent unauthenticated DB connection floods.
        # The previous implementation opened and closed a SQLite connection on every
        # request — under a flood this contended with the write path, making the
        # service less ready by hammering the readiness endpoint.
        if path == "/readyz":
            if not self._rate_ok(None):
                return
            return self._route_readyz()
        if path == "/metrics":
            return self._route_metrics()
        # A148: rate-limit /api/stats to prevent unauthenticated DB work floods.
        # passiveRecords is an unindexed COUNT(*) over the passive-DNS table that
        # grows for 30 days — no auth, no limit, no cost to the caller.
        if path == "/api/stats":
            if not self._rate_ok(None):
                return
            return self._send(200, {"passiveRecords": passivedns.total_records()})
        if path == "/api/broadcast":
            return self._route_broadcast()
        if path == "/api/whats-new":
            return self._route_whats_new(query)
        if path == "/api/pubkey":
            return self._route_pubkey()
        if path == "/api/startup":
            return self._route_startup(query)
        # G-1: Custom auto-update appcast (Sparkle-inspired, Ed25519-signed)
        if path == "/api/appcast":
            return self._route_appcast(query)
        if path == "/api/checkout":
            return self._route_checkout(query)
        if path == "/warp":
            return self._route_warp(query)
        if path in ("/fetch_script", "/warp/fetch_script"):
            return self._route_fetch_script(query)
        if path == "/leak":
            return self._route_leak_legacy(query)
        if path == "/api/investigate":
            return self._route_investigate(query)
        if path == "/api/authoritative":
            return self._route_authoritative(query)
        if path == "/api/trace":
            return self._route_trace(query)
        if path == "/api/baseline":
            return self._route_baseline(query)
        if path == "/api/exposure":
            return self._route_exposure(query)
        if path == "/api/passive-dns":
            return self._route_passive(query)
        if path == "/api/leak/verify":
            return self._route_leak_verify(query)
        if path == "/api/leaktest/start":
            return self._route_leaktest_start(query)
        if path == "/api/leaktest/check":
            return self._route_leaktest_check(query)
        if path == "/dns-query":
            return self._route_doh_get(query)
        if path == "/api/warp/raw":
            return self._route_warp_raw(query)
        if path == "/api/warp/manifest":
            return self._route_warp_manifest(query)
        if path == "/api/warp/assembly":
            return self._route_warp_assembly(query)
        # --- Free-tier public utilities (rate-limited per IP, no entitlement) ---
        if path == "/api/lookup":
            return self._route_lookup(query)
        if path == "/api/reverse-dns":
            return self._route_reverse_dns(query)
        if path == "/api/benchmark":
            return self._route_benchmark(query)
        if path == "/api/whois":
            return self._route_whois(query)
        if path == "/api/propagation":
            return self._route_propagation(query)
        if path == "/api/email-auth":
            return self._route_email_auth(query)
        if path == "/api/subdomains":
            return self._route_subdomains(query)
        if path == "/api/firewall-test":
            return self._route_firewall_test(query)
        if path == "/api/keys":
            return self._route_list_api_keys(query)
        if path == "/api/monitors":
            return self._route_list_monitors(query)
        if path == "/api/dnssec":
            return self._route_dnssec(query)
        if path in ("/docs/api", "/docs/api/"):
            return self._serve_static("/docs/api.html")
        if path in ("/docs/help", "/docs/help/"):
            return self._serve_static("/docs/help.html")
        return self._json_error(404, "not found")

    def _route_broadcast(self):
        """Message-of-the-day (Workstream D2) — PUBLIC so a security message reaches every user, LITE
        or Pro. Returns the current operator-set broadcast or null; the app shows an unseen id once."""
        if not self._rate_ok(None):
            return
        self._send(200, {"broadcast": broadcast.current()})

    # Accepted `?platform=` spellings → the canonical platform key. Anything unrecognized falls back
    # to mac (the historical default), never to an arbitrary file.
    _WHATSNEW_PLATFORMS = {
        "win": "win", "windows": "win",
        "deb": "deb", "debian": "deb", "ubuntu": "deb", "mint": "deb", "linux": "deb",
        "mac": "mac", "macos": "mac", "osx": "mac",
    }

    def _route_whats_new(self, query):
        """What's New content (Workstream D3) — PUBLIC, legacy `?platform=` route. The dedicated MOTD
        subdomains (macmotd/winmotd/debmotd.syxx.app) serve the same files at / via Host routing
        (`_dispatch_get`)."""
        raw = (query.get("platform", ["mac"])[0]).strip().lower()
        self._serve_whats_new(self._WHATSNEW_PLATFORMS.get(raw, "mac"))

    def _serve_whats_new(self, platform):
        """Serve the self-contained per-platform What's-New HTML under a strict CSP (no scripts, no external
        fetches), so the page updates with no app release; the app caches it for offline. Shared by the
        legacy `/api/whats-new` route and the MOTD-subdomain Host route.

        `platform` is one of "mac" / "win" / "deb". This was a two-way boolean until the Debian family
        became a shipped target; a boolean could not express a third platform, and adding one by
        overloading it would have silently served Mac content to Linux users."""
        if not self._rate_ok(None):
            return
        path = {
            "win": config.WHATSNEW_WIN,
            "deb": config.WHATSNEW_DEB,
        }.get(platform, config.WHATSNEW_MAC)
        limit = 1 << 20
        try:
            with open(path, "rb") as f:
                html = f.read(limit + 1)   # read limit+1 to detect oversize
        except OSError:
            return self._json_error(404, "no what's-new content")
        if len(html) > limit:
            # A112: the file exceeds what we will serve. Returning the first 1 MiB as a
            # 200 would present a silently truncated (broken) document as complete
            # trusted app content. Fail closed: log and refuse rather than lie.
            logging_util.error("whats_new_oversize", path=path, limit=limit)
            return self._json_error(500, "what's-new content unavailable")
        # D041: sign the What's-New HTML with the collector's Ed25519 key so the client can
        # verify the content integrity before rendering. The signature covers the HTML bytes,
        # preventing a server compromise or MITM from substituting misleading text presented
        # as authoritative app content. The CSP (default-src 'none') blocks script execution,
        # but without a signature the text itself is not integrity-protected.
        # The client verifies this signature before rendering (see RemoteContentView.swift /
        # RemoteContentPanel.xaml.cs).
        from . import leak as _leak
        sig_b64 = _leak.sign_content(html) if hasattr(_leak, 'sign_content') else ""
        key_id = _leak.collector_key_id() if hasattr(_leak, 'collector_key_id') else ""

        self._send(200, html, "text/html; charset=utf-8", {
            "Content-Security-Policy": "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; base-uri 'none'; form-action 'none'",
            "Cache-Control": "public, max-age=300",
            "X-Content-Type-Options": "nosniff",
            "X-Syxx-Signature": sig_b64,
            "X-Syxx-KeyId": key_id,
        })

    def _route_pubkey(self):
        """Public collector Ed25519 public key (D004). Returns the key a third party needs to
        independently verify Syxx collector attestations. PUBLIC (no license required) so a
        Reddit reader can fetch the key and verify a pasted attestation."""
        if not self._rate_ok(None):
            return
        # Lazy import to avoid circular deps; leak.ensure_collector_key() already idempotent.
        from . import leak as _leak
        self._send(200, {
            "publicKey": _leak.collector_public_key(),
            "keyId": _leak.collector_key_id(),
            # A114: the actual key activation time (when this key instance was loaded/generated),
            # NOT the current fetch time. Previously returned int(time.time()) which was the
            # request time, making it impossible to detect key rotation or pin a key epoch.
            "activatedAt": _leak.collector_key_activated_at(),
            "rotationTimestamp": _leak.collector_key_activated_at(),
        })

    def _route_startup(self, query):
        """Consolidated startup check (Workstream D2/D3 + update) — PUBLIC, ONE call so the app doesn't
        phone home per feature: current broadcast + update availability (vs the client's version) + a
        What's New pointer. This is the privacy consolidation from the plan."""
        if not self._rate_ok(None):
            return

        # Three-way, not a boolean: a Debian-family client asking for `platform=linux` used to fall
        # through the `is_win` test and be handed MAC content and the MAC update channel.
        raw = (query.get("platform", ["mac"])[0]).strip().lower()
        plat = self._WHATSNEW_PLATFORMS.get(raw, "mac")
        client_ver = (query.get("version", [""])[0]).strip()
        latest = {"win": config.LATEST_WIN, "deb": config.LATEST_DEB}.get(plat, config.LATEST_MAC)
        update = None
        if latest:
            update = {"available": _update_available(client_ver, latest), "latest": latest,
                      "url": {"win": config.UPDATE_URL_WIN,
                              "deb": config.UPDATE_URL_DEB}.get(plat, config.UPDATE_URL_MAC)}
        self._send(200, {
            "broadcast": broadcast.current(),
            "update": update,
            "whatsNewUrl": "/api/whats-new?platform=%s" % plat,
        })

    def _route_appcast(self, query):
        """G-1: Custom auto-update appcast (Sparkle-inspired, Ed25519-signed).
        Returns a signed manifest with the latest version, download URL, and SHA-256.
        Clients verify the signature before installing anything."""
        if not self._rate_ok(None):
            return
        raw = (query.get("platform", ["mac"])[0]).strip().lower()
        plat = self._WHATSNEW_PLATFORMS.get(raw, "mac")
        manifest = appcast.build_appcast(plat)
        if not manifest:
            return self._json_error(404, "no update available for this platform")
        self._send(200, manifest)

    def _route_checkout(self, query):
        """Start a Stripe Managed-Payments checkout (Workstream D4) — PUBLIC (this IS the purchase flow).
        The app opens `/api/checkout?sku=<sku>` in the system browser; we create the hosted Checkout
        Session for that SKU and 303-redirect the browser to Stripe's page.

        `?product=<prod_…>` is still accepted: shipped app builds send it, and they cannot be
        recalled. It maps to that product's MONTHLY sku — which is what those builds were buying when
        each product had a single price — so an old client keeps working and never lands on a tier it
        did not choose. New clients send ?sku=.
        """
        if not self._rate_ok(None):
            return
        sku = (query.get("sku", [""])[0]).strip()
        if not sku:
            legacy = (query.get("product", [""])[0]).strip()
            if not legacy:
                return self._json_error(400, "missing sku")
            sku = config.sku_for_legacy_product(legacy)
            if not sku:
                return self._json_error(400, "unknown product")
            logging_util.incr("checkouts_legacy_product_param")
        # A035/A084: derive a stable idempotency key from (sku, client_ip, coarse
        # time window) so that retries of the same purchase from the same client
        # reuse the same Stripe session instead of minting a fresh one per request.
        # The time window (1 hour) allows a legitimate re-attempt after user error
        # to produce a new session, while preventing rapid-fire spam from creating
        # unbounded Stripe API sessions.
        window = int(time.time()) // 3600
        client_ip = self.client_address[0]
        idem_key = "checkout-%s-%s-%d" % (sku, hashlib.sha256(client_ip.encode()).hexdigest()[:16], window)
        try:
            url, _sid = stripe_checkout.create_checkout_session(sku, idempotency_key=idem_key)
        except stripe_checkout.StripeError as e:
            logging_util.error("checkout_failed", sku=sku, err=str(e))
            return self._json_error(503, "checkout unavailable")
        if not url:
            return self._json_error(502, "no checkout url from stripe")
        logging_util.incr("checkouts")
        self._send(303, b"", "text/plain", {"Location": url})

    def _route_billing_portal(self):
        """C1: Create a Stripe Customer Portal session so subscribers can cancel, upgrade,
        or update their payment method without operator involvement.

        POST /api/billing/portal
        Requires an active session token (Authorization: Bearer ...).
        The session carries the Stripe customer id stored at purchase time (kv key
        "stripe_cust:<customer_id>").  Returns {"url": "https://billing.stripe.com/..."}
        which the frontend should open in a new tab.

        THE CUSTOMER ID IS NEVER TAKEN FROM THE REQUEST. It is derived, every time, from the
        caller's own session token: session -> subject -> Keygen license id -> Stripe customer.
        Accepting a `customer` field from the body or query would let anyone open anyone else's
        billing portal — read their invoices, change their payment method, cancel their plan —
        with nothing but a guessed `cus_` id. The body below is read ONLY to drain it off the
        socket and is then discarded unparsed; there is deliberately no code path that looks
        inside it.

        (California B&P 17602(d)(1) requires an online-purchased subscription to be cancellable
        online, which is why this route exists at all rather than an email-only cancellation.)
        """
        # Drain and discard the request body. A caller WILL post one (that is the attack this
        # route refuses), and leaving it unread desynchronises the next request on a keep-alive
        # connection — the unread bytes get parsed as the following request line.
        raw = self._body()
        if self._body_error(raw):
            return
        # Entitle the request via the session token so only a logged-in user can reach
        # the billing portal for their own subscription.
        auth = self.headers.get("Authorization", "")
        if not auth.lower().startswith("bearer "):
            return self._json_error(401, "authorization required")
        ent = licensing.verify_session(auth[7:].strip())
        if not ent:
            return self._json_error(401, "invalid or expired session")
        # The entitlement's subject is a license-key hash (lic_<sha256[:16]>).  Walk back
        # to the customer id via the keygen license id, then to the Stripe customer id.
        # (The kv map was set by fulfillment when the license was minted.)
        lic_id = db.keygen_license_id_get(ent.subject)
        if not lic_id:
            return self._json_error(404, "no license mapping found for this session")
        # Scan the KV store for a stripe_cust:<id> entry whose value matches this license.
        # In practice this is O(n) in n subscriptions — fine for the current scale; if it
        # becomes a bottleneck, a reverse map (kv "stripe_lic:<lic_id>" -> customer id) can
        # be written at fulfillment time.
        customer_id = db.kv_find_by_value_prefix("stripe_cust:", lic_id)
        if not customer_id:
            return self._json_error(404, "no subscription found for this session")
        try:
            session = stripe_checkout.create_billing_portal_session(customer_id)
        except stripe_checkout.StripeError as e:
            logging_util.error("billing_portal_failed", err=str(e))
            return self._json_error(503, "billing portal unavailable")
        self._send(200, {"url": session.get("url", "")})

    def _route_stripe_webhook(self):
        """Stripe webhook (Workstream D4) — PUBLIC but SIGNATURE-VERIFIED. Verifies the Stripe-Signature
        HMAC, then hands the event to fulfillment (mint Keygen license + email on payment). Always 200s a
        verified event so Stripe doesn't retry a handled one; fulfillment errors are logged, not raised."""
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            event = stripe_checkout.verify_webhook(raw or b"", self.headers.get("Stripe-Signature", ""))
        except stripe_checkout.StripeError as e:
            logging_util.error("stripe_webhook_verify_failed", err=str(e))
            return self._json_error(400, "signature verification failed")
        try:
            result = fulfillment.handle_event(event)
        except Exception as e:  # never let a fulfillment bug make Stripe retry-storm
            logging_util.error("stripe_fulfillment_error", err=str(e))
            # A181: return 500 for genuinely unhandled exceptions so Stripe's retry
            # schedule engages. The old code returned 200 with result="error", which
            # told Stripe the delivery succeeded — a charged customer with no license
            # and no retry path. A 500 triggers Stripe's exponential backoff.
            return self._json_error(500, "internal fulfillment error")
        self._send(200, {"received": True, "conclusion": result})

    def _route_keygen_webhook(self):
        """Keygen webhook — PUBLIC but SIGNATURE-VERIFIED. When a license is revoked or
        suspended, immediately revokes all API keys for that subject. Verifies HMAC-SHA256
        signature from Keygen before processing. Always 200s verified events."""
        import hashlib, hmac
        if not config.KEYGEN_WEBHOOK_SECRET:
            return self._json_error(501, "webhook not configured")
        raw = self._body()
        if self._body_error(raw):
            return
        # Verify HMAC-SHA256 signature
        sig_header = self.headers.get("Keygen-Signature", "")
        if not sig_header:
            return self._json_error(400, "missing signature")
        expected = hmac.new(
            config.KEYGEN_WEBHOOK_SECRET.encode("utf-8"),
            raw or b"", hashlib.sha256
        ).hexdigest()
        # A148: wrap compare_digest in try/except TypeError. When sig_header
        # contains non-ASCII characters, compare_digest raises TypeError instead
        # of returning False — an unauthenticated caller choosing the server's
        # error class. Validate as hex before comparing.
        try:
            sig_valid = hmac.compare_digest(expected, sig_header)
        except TypeError:
            sig_valid = False
        if not sig_valid:
            logging_util.error("keygen_webhook_verify_failed")
            return self._json_error(400, "signature verification failed")
        try:
            event = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON")
        event_type = event.get("event", "")
        data = event.get("data", {})
        attrs = data.get("attributes", {}) if isinstance(data, dict) else {}
        # Extract the license ID (used as subject in our system)
        lic_id = data.get("id", "") if isinstance(data, dict) else ""
        if not lic_id:
            return self._send(200, {"received": True, "conclusion": "ignored"})
        result = "ignored"
        if event_type in ("license.revoked", "license.suspended", "license.expired"):
            # A175: lic_id is a Keygen resource id, but api_keys.subject is written
            # as the local subject (lic_<sha256[:16]>). Resolve the Keygen id to
            # the local subject via the stored mapping before revoking. Without
            # this, the UPDATE matches zero rows and the response falsely claims
            # keys were revoked.
            local_subject = db.keygen_subject_for_license_id(lic_id)
            if local_subject:
                revoked_count = db.api_key_revoke_all_for_subject(local_subject)
                if revoked_count > 0:
                    result = "keys_revoked"
                else:
                    result = "no_keys_for_subject"
                logging_util.info("keygen_webhook_revoked_keys", event=event_type,
                                  license_id=lic_id, subject=local_subject,
                                  revoked_count=revoked_count)
            else:
                result = "subject_unresolved"
                logging_util.warn("keygen_webhook_subject_unresolved",
                                  event=event_type, license_id=lic_id,
                                  detail="Keygen revocation received but no local "
                                         "subject maps to this license id — API keys "
                                         "were NOT revoked. Resolve manually.")
        elif event_type in ("license.created", "license.activated", "license.renewed"):
            result = "noted"
        self._send(200, {"received": True, "conclusion": result, "event": event_type})

    # --- static ------------------------------------------------------------
    def _serve_static(self, path):
        name, ctype = _STATIC_FILES[path]
        # name is from a fixed allowlist, but normalize defensively.
        safe = posixpath.normpath("/" + name.replace("\\", "/")).lstrip("/")
        full = os.path.realpath(os.path.join(config.STATIC_DIR, safe))
        if not full.startswith(os.path.realpath(config.STATIC_DIR) + os.sep):
            return self._json_error(404, "not found")
        if not os.path.isfile(full):
            return self._json_error(404, "not found")
        with open(full, "rb") as f:
            data = f.read()

        # SEO: inject canonical/OG/Twitter/JSON-LD tags into HTML pages before
        # serving so the on-disk files stay clean and metadata lives in seo.py.
        extra = {}
        if ctype.startswith("text/html"):
            try:
                html = data.decode("utf-8")
                html = seo.inject_meta_tags(html, path)
                # E-10: inject live pricing from env vars so the static HTML never drifts
                # from Stripe. When SYXX_SEO_PRICE_MONTHLY or SYXX_SEO_PRICE_YEARLY are
                # set, every hardcoded $3.95 / $19.95 in the page is replaced with the
                # env values — the JSON-LD structured data is already driven by these
                # same vars via seo.py.
                html = html.replace("$3.95", f"${_SEO_PRICE_MONTHLY}")
                html = html.replace("$19.95", f"${_SEO_PRICE_YEARLY}")
                data = html.encode("utf-8")
            except UnicodeDecodeError:
                pass  # serve as-if if decoding fails

        # Cache-Control for SEO assets — short TTL so updates propagate within a day.
        if path in ("/sitemap.xml", "/robots.txt"):
            extra["Cache-Control"] = "public, max-age=86400"

        self._send(200, data, ctype, extra if extra else None)

    def _serve_asset(self, path):
        """Serve an image from STATIC_DIR/assets/. normpath collapses any '..' so a
        traversal escapes the assets/ prefix and 404s; the extension allowlist bounds
        the content types we will emit."""
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
        self._send(200, data, ctype, {"Cache-Control": "public, max-age=86400"})

    # --- public API --------------------------------------------------------
    def _route_readyz(self):
        # D-0: ready iff the DB is reachable AND no known listener is `down` or `unknown`.
        # A `degraded-fallback` listener (e.g. DNS serving on :5053 not :53) is reported
        # verbatim but does NOT by itself flip `ready` to false — it is degraded, not down,
        # and the operator sees the exact state to act on. Only `down` (failed to serve at
        # all) or `unknown` (never reported — absence of evidence is not evidence of health,
        # A196) are hard not-ready conditions.
        try:
            db.connect().close()
        except Exception as e:
            # A105: never echo the raw exception (it carries the sqlite path / driver detail) in the
            # public 503 body. Log it internally, return a generic readiness failure.
            logging_util.error("readyz_db_unavailable", err=str(e))
            return self._json_error(503, "not ready")

        snap = listeners.snapshot()
        down = listeners.down_listeners()
        unknown = listeners.unknown_listeners()  # A196: unknown != healthy
        if down or unknown:
            # Name the offending listeners so an operator does not have to guess which one fell over.
            return self._send(503, {"ready": False, "down": down, "unknown": unknown, "listeners": snap})
        self._send(200, {"ready": True, "listeners": snap})

    def _route_metrics(self):
        """S4: /metrics requires bearer token auth when configured.
        Returns 401/403 for unauthenticated or invalid requests."""
        token = config.METRICS_BEARER_TOKEN
        if not token:
            # FAIL CLOSED. SYXX_METRICS_BEARER_TOKEN defaults to "" (config.py), so the old
            # "allow when unconfigured" branch served the full metrics snapshot — endpoint hit
            # counts, session mints, paywall 402 rates — to any anonymous caller on every deploy
            # that did not explicitly set the variable. Unconfigured is not authorized: missing
            # config must never be read as permission. Explicit dev opt-in mirrors the precedent
            # in licensing.py (SYXX_DEV_MODE gate).
            if os.environ.get("SYXX_DEV_MODE") != "1":
                return self._json_error(503, "metrics endpoint not configured")
            return self._send(200, {"metrics": logging_util.snapshot()})
        auth = self.headers.get("Authorization", "")
        if not auth.lower().startswith("bearer ") or not hmac.compare_digest(auth[7:].strip(), token):
            return self._json_error(401, "authentication required")
        return self._send(200, {"metrics": logging_util.snapshot()})

    def _route_activate(self):
        if not self._rate_ok(None):
            return
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON body")
        key = (payload.get("key") or "").strip()
        if not key:
            return self._json_error(400, "missing license key")
        ent, err = licensing.validate_license(key)
        if err:
            return self._json_error(503, err)
        if not ent:
            db.audit(None, "/api/auth/activate", 402, self._client_ip())
            return self._json_error(402, "invalid or inactive license key")
        token, exp = licensing.mint_session(ent.subject, ent.plan)
        logging_util.incr("sessions_minted")
        db.audit(ent.subject, "/api/auth/activate", 200, self._client_ip())
        self._send(200, {"token": token, "plan": ent.plan, "expiresAt": exp})

    def _route_trial_anchor(self):
        if not self._rate_ok(None):
            return
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON body")
        fingerprint = (payload.get("fingerprint") or "").strip()
        if not fingerprint:
            return self._json_error(400, "missing fingerprint")
        
        now = int(time.time())
        try:
            first_launch, is_new = db.get_or_create_trial_anchor(fingerprint, now)
            # A032: first_launch is None when the table is at capacity and this
            # fingerprint is new — reject rather than evicting legitimate anchors.
            if first_launch is None:
                return self._json_error(507, "trial anchor capacity reached; try again later")
            self._send(200, {
                "fingerprint": fingerprint,
                "firstLaunch": first_launch,
                "serverTime": now
            })
        except Exception as e:
            logging_util.error("trial_anchor_error", err=str(e))
            self._json_error(500, "internal database error")

    def _route_beta_reserve(self):
        """Public: record interest in the Nnyx/Zzyn closed betas. Rate-limited per IP.

        The signup is persisted (the operator receives a weekly CSV digest of all rows),
        and the subscriber gets a confirmation email. Mail is best-effort and fired on a
        background thread, so it never blocks or fails the request."""
        if not self._rate_ok(None):
            return
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON body")
        email = (payload.get("email") or "").strip().lower()
        interests = payload.get("interests")
        interests = [x for x in interests if x in ("nnyx", "zzyn")] \
            if isinstance(interests, list) else []
        # A116: reject comma-bearing addresses that could expand to multiple recipients.
        if not _valid_email(email):
            return self._json_error(400, "a valid email address is required")
        if not interests:
            return self._json_error(400, "select at least one product (Nnyx or Zzyn)")
        db.beta_reserve(email, ",".join(interests), self._client_ip())
        logging_util.incr("beta_signups")
        # Only claim a confirmation when the server can actually send one.
        confirming = config.BETA_CONFIRM and mailer.enabled()
        if confirming:
            _send_beta_confirmation(email, interests)
        self._send(200, {"ok": True, "interests": interests, "confirmation": confirming})

    # --- paid: native app --------------------------------------------------
    def _route_warp(self, query):
        """DEPRECATED — the unsigned structural baseline (`warp.build_baseline`) is retired
        (superseded by the signed manifest at /api/warp/manifest, WARP_MANIFEST_V1). A
        declared-but-unsigned hash is forgeable by a MITM into a false match. The server-internal
        consumers (scheduler baseline job + /api/diff content half) still use build_baseline
        in-process; this HTTP route returns 410 Gone for one deprecation release — it never
        fabricates a baseline. Pro-gated so the deprecation notice is only seen by entitled
        callers."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        db.audit(ent.subject if ent else None, "/warp", 410, self._client_ip())
        return self._json_error(
            410,
            "WARP_V1 baseline is retired; use /api/warp/manifest (WARP_MANIFEST_V1), a signed manifest",
            {"successor": "/api/warp/manifest", "deprecated": True},
        )

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

        # WARP_MANIFEST_V1 §9 diff overlay: ?raw=1 returns the envelope-decoded RAW bytes as
        # application/octet-stream + X-Syxx-Warp-Sha256, bypassing the utf-8-replace text path
        # below (which mutates bytes and must NOT feed a byte-exact diff). Same Pro-gate.
        raw_mode = (query.get("raw", [""])[0]).strip().lower() in ("1", "true", "yes", "on")
        if raw_mode:
            span = _OTEL_TRACER.start_span("server.warp_raw_bytes") if _OTEL_TRACER else None
            if span: span.set_attribute("warp.target", target)
            try:
                raw, final = warp.fetch_raw_decoded(target)
            except fetcher.BlockedError as e:
                if span: span.end()
                return self._json_error(400, "target blocked: %s" % e)
            except fetcher.FetchError as e:
                if span: span.end()
                return self._json_error(502, "fetch failed: %s" % e)
            sha = hashlib.sha256(raw).hexdigest()
            if span:
                span.set_attribute("warp.bytes", len(raw)); span.end()
            db.audit(ent.subject if ent else None, "/warp/raw", 200, self._client_ip())
            return self._send(200, raw, "application/octet-stream",
                              extra_headers={"X-Syxx-Warp-Sha256": sha,
                                             "X-Syxx-Warp-Final-Url": final})

        span = _OTEL_TRACER.start_span("server.warp_raw") if _OTEL_TRACER else None
        if span: span.set_attribute("warp.target", target)
        try:
            data, ctype, final = fetcher.fetch(target)
        except fetcher.BlockedError as e:
            return self._json_error(400, "target blocked: %s" % e)
        except fetcher.FetchError as e:
            return self._json_error(502, "fetch failed: %s" % e)

        low = (ctype or "").lower()
        is_text = (low.startswith("text/") or "html" in low or "json" in low
                   or "javascript" in low or "xml" in low or "css" in low)
        if not is_text:
            return self._json_error(415, "content is not text and cannot be diffed: %s"
                                    % (ctype or "unknown"))
        try:
            text = data.decode("utf-8", errors="replace")
        except Exception:
            return self._json_error(415, "content could not be decoded as text")

        if span: span.set_attribute("warp.bytes", len(data)); span.set_attribute("warp.content_type", ctype); span.end()
        db.audit(ent.subject if ent else None, "/warp/raw", 200, self._client_ip())
        self._send(200, {
            "vantage": "server-clean-path",
            "requestedUrl": target,
            "finalUrl": final,
            "contentType": ctype,
            "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest(),
            "content": text,
        })

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
            return self._json_error(502, f"manifest inconclusive: {e}")
        except fetcher.BlockedError as e:
            if span:
                span.end()
            return self._json_error(400, f"target blocked: {e}")
        except fetcher.FetchError as e:
            if span:
                span.end()
            return self._json_error(502, f"manifest fetch failed: {e}")
        # Serialize the inner manifest ONCE; sign the EXACT transmitted bytes (spec §2). NO JCS,
        # NO re-serialization: json.dumps → UTF-8 → base64 into manifest_b64, sign the decoded bytes.
        manifest_bytes = json.dumps(
            manifest, separators=(",", ":"), sort_keys=True, ensure_ascii=False
        ).encode("utf-8")
        manifest_b64 = base64.b64encode(manifest_bytes).decode("ascii")
        signature = leak.sign_content(manifest_bytes)
        # WARP_MANIFEST_V1 key_id contract: lowercase hex of the RAW 32-byte Ed25519 public key.
        # This is the label the fixture corpus, the client's pinned key set, and the client
        # verifier all use — so server + corpus + pinning agree. It deliberately does NOT reuse
        # leak.collector_key_id() (base64 of the first 8 pubkey bytes), whose scheme is fixed by
        # the leak-attestation protocol and must not change. Derived from the SAME collector key.
        key_id = base64.b64decode(leak.collector_public_key()).hex()
        if span:
            span.set_attribute("warp.assets", len(manifest["assets"]))
            span.end()
        db.audit(ent.subject if ent else None, "/api/warp/manifest", 200, self._client_ip())
        self._send(200, {
            "manifest_b64": manifest_b64,
            "signature_ed25519": signature,
            "key_id": key_id,
        })

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
        if span: span.set_attribute("fetch.url", target)
        try:
            data, ctype, _final = fetcher.fetch(target, timeout=config.FETCH_SCRIPT_TIMEOUT)
        except fetcher.BlockedError as e:
            return self._json_error(400, "target blocked: %s" % e)
        except fetcher.FetchError as e:
            return self._json_error(502, "fetch failed: %s" % e)
        if span: span.set_attribute("fetch.bytes", len(data)); span.set_attribute("fetch.content_type", ctype); span.end()
        # A139: serve as text/plain so it cannot be loaded as a <script> tag and execute
        # as same-origin JS. The native app is the only consumer and can parse text/plain.
        self._send(200, data, "text/plain; charset=utf-8")

    def _route_leak_legacy(self, query):
        """Backwards-compatible readout of the last resolver for a raw token."""
        ent, ok = self._require(query, config.ENFORCE_NATIVE)
        if not ok or not self._rate_ok(ent):
            return
        token = (query.get("token", [""])[0]).strip().split(".")[0].lower()
        span = _OTEL_TRACER.start_span("server.leak_legacy") if _OTEL_TRACER else None
        if span: span.set_attribute("leak.token_prefix", token[:8] if len(token) > 8 else token)
        rec = db.leak_token_get(token)
        if ent is None or rec is None or rec["subject"] != ent.subject:
            return self._json_error(404, "legacy leak endpoint requires a valid issued token owned by the caller")
        obs = db.leak_observations(token)
        resolver_ip = obs[-1]["resolver_ip"] if obs else None
        if span: span.end()
        self._send(200, {"token": token, "resolverIP": resolver_ip,
                          "observations": obs})

    # --- paid: website API -------------------------------------------------
    def _route_investigate(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        span = _OTEL_TRACER.start_span("server.investigate") if _OTEL_TRACER else None
        if span: span.set_attribute("investigate.domain", domain)
        result, err = investigate.investigate(domain)
        if span: span.end()
        if err:
            return self._json_error(400, err)
        logging_util.incr("investigations")
        self._send(200, result)

    def _route_authoritative(self, query):
        """Ground-truth oracle (Workstream S #1): walk root -> TLD -> authoritative for a name and
        return the zone's own answer + the delegation chain, so the app can diff its local answer
        against data an interfering resolver cannot fake."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        qtype_str = (query.get("type", ["A"])[0]).strip().upper()
        qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA, "NS": resolver.TYPE_NS}
        if qtype_str not in qtypes:
            return self._json_error(400, "unsupported type (A, AAAA, NS)")
        span = _OTEL_TRACER.start_span("server.authoritative") if _OTEL_TRACER else None
        if span: span.set_attribute("dns.domain", domain); span.set_attribute("dns.type", qtype_str)
        try:
            # A066: bound the walk with a shared wall-clock deadline.
            deadline = time.monotonic() + config.AUTH_WALK_DEADLINE_SECONDS
            result = resolver.resolve_authoritative(domain, qtypes[qtype_str],
                                                   deadline=deadline)
        except resolver.DNSError as e:
            if span: span.set_attribute("error", str(e)); span.end()
            return self._json_error(502, "resolution failed: %s" % e)
        if span: span.end()
        result["type"] = qtype_str
        logging_util.incr("authoritative_queries")
        self._send(200, result)

    def _route_diff(self, query):
        """Inside/outside diff (Workstream S #2): the app POSTs what IT saw for a name; the server
        returns the authoritative ground truth plus its own clean-vantage recursive answer and an
        assessment. A divergence the authority does not serve is on-path interception / tampering."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON body")
        domain = (payload.get("domain") or "").strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        qtype_str = (payload.get("type") or "A").strip().upper()
        qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA}
        if qtype_str not in qtypes:
            return self._json_error(400, "unsupported type (A, AAAA)")
        local = payload.get("localAnswers")
        if not isinstance(local, list) or len(local) > 64 or not all(isinstance(x, str) for x in local):
            return self._json_error(400, "localAnswers must be a list of up to 64 strings")
        local_set = set(a.strip().lower() for a in local if a.strip())

        span = _OTEL_TRACER.start_span("server.diff") if _OTEL_TRACER else None
        if span: span.set_attribute("dns.domain", domain); span.set_attribute("dns.type", qtype_str)
        # A038: a resolution failure must surface as inconclusive, not propagate as a crash/500.
        try:
            # A066: bound the walk with a shared wall-clock deadline.
            deadline = time.monotonic() + config.AUTH_WALK_DEADLINE_SECONDS
            auth = resolver.resolve_authoritative(domain, qtypes[qtype_str],
                                                 deadline=deadline)
        except resolver.DNSError:
            auth = {"conclusion": "unreachable"}
        auth_answers = set(a.lower() for a in (auth.get("authoritative") or {}).get("answers", []))
        auth_usable = auth.get("conclusion") == "authoritative" and bool(auth_answers)
        try:
            clean_answers = _clean_vantage_answers(domain, qtypes[qtype_str], qtype_str)
        except resolver.DNSError:
            # A142 Fix 3: type must match every other path (set, not dict).
            clean_answers = set()

        reference = auth_answers if auth_usable else clean_answers
        if not reference or not local_set:
            match, assessment = None, "inconclusive"
        elif local_set <= reference:
            match, assessment = True, "match"
        elif local_set.isdisjoint(reference):
            match, assessment = False, "divergent" if auth_usable else "partial"
        else:
            match, assessment = False, "partial"

        content = None
        url = (payload.get("url") or "").strip()
        if url:
            if not (url.startswith("http://") or url.startswith("https://")):
                return self._json_error(400, "url must be http(s)")
            local_html = payload.get("localHTML")
            if local_html is not None and not isinstance(local_html, str):
                return self._json_error(400, "localHTML must be a string")
            if isinstance(local_html, str) and len(local_html) > 500_000:
                return self._json_error(413, "localHTML too large")
            local_scripts = payload.get("localScripts") if isinstance(payload.get("localScripts"), list) else None
            local_inline = (payload.get("localInlineScriptHashes")
                            if isinstance(payload.get("localInlineScriptHashes"), list) else None)
            content = _diff_content(url, local_html, local_scripts, local_inline)

        result = _diff_result(assessment, content, clean_answers, local_set)
        if span: span.set_attribute("diff.assessment", assessment); span.set_attribute("diff.conclusion", result); span.end()
        logging_util.incr("diffs")
        response = {
            "domain": domain, "type": qtype_str,
            "localAnswers": sorted(local_set),
            "authoritative": {"answers": sorted(auth_answers), "conclusion": auth.get("conclusion")},
            "cleanVantage": {"answers": sorted(clean_answers)},
            # A142 Fix 1: derive the label from the same predicate as the selection —
            # auth_usable, not auth_answers alone. When the authority answered but the walk
            # did not reach an authoritative conclusion, the evidence used was cleanVantage
            # and the label must say so.
            "reference": "authoritative" if auth_usable else ("cleanVantage" if clean_answers else "none"),
            "match": match, "assessment": assessment,
            "conclusion": result,
        }
        if content is not None:
            response["content"] = content
        self._send(200, response)

    def _route_trace(self, query):
        """DNS trace / hierarchy diagnostics (Workstream S #3): surfaces the root->TLD->authoritative
        delegation walk as a diagnostic, so a user can see where resolution breaks or mismatches."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        qtype_str = (query.get("type", ["A"])[0]).strip().upper()
        qtypes = {"A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA, "NS": resolver.TYPE_NS}
        if qtype_str not in qtypes:
            return self._json_error(400, "unsupported type (A, AAAA, NS)")
        span = _OTEL_TRACER.start_span("server.trace") if _OTEL_TRACER else None
        if span: span.set_attribute("dns.domain", domain); span.set_attribute("dns.type", qtype_str)
        # A066: bound the walk with a shared wall-clock deadline.
        deadline = time.monotonic() + config.AUTH_WALK_DEADLINE_SECONDS
        # A152: wrap resolve_authoritative in try/except like _route_authoritative
        # does (SD-41 fix). A DNSError propagating to the catch-all produced a
        # dishonest 500 "internal error" when the truth is "the delegation chain
        # did not answer." For this route, a partial trace is the diagnostic
        # itself — return 200 with result "unreachable" and brokenAt naming the
        # zone that failed, matching what the docstring promises ("where resolution breaks").
        try:
            result = resolver.resolve_authoritative(domain, qtypes[qtype_str],
                                                   deadline=deadline)
        except resolver.DNSError as e:
            if span: span.set_attribute("error", str(e)); span.end()
            logging_util.incr("traces")
            return self._send(200, {"domain": domain, "type": qtype_str,
                                    "delegation": [], "conclusion": "unreachable",
                                    "brokenAt": None, "error": str(e)})
        # A152: use .get for "delegation" to handle partial results gracefully.
        delegation = result.get("delegation", [])
        broken = next((d.get("zone") for d in delegation if d.get("error")), None)
        if span: span.set_attribute("trace.conclusion", result.get("conclusion", "unknown")); span.end()
        logging_util.incr("traces")
        self._send(200, {"domain": domain, "type": qtype_str, "delegation": delegation,
                         "conclusion": result.get("conclusion", "unknown"), "brokenAt": broken})

    def _route_baseline(self, query):
        """Historical baseline / drift (Workstream S #4): the server's memory layer — what a domain
        has resolved to over time, so the app can spot a value that changed after months of stability.
        Reads the passive-DNS store the server already populates; supporting context, never a result."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        rtype = (query.get("type", [""])[0]).strip().upper() or None
        if rtype and rtype not in ("A", "AAAA", "CNAME", "NS", "MX", "TXT"):
            return self._json_error(400, "unsupported type")
        span = _OTEL_TRACER.start_span("server.baseline") if _OTEL_TRACER else None
        if span: span.set_attribute("dns.domain", domain); span.set_attribute("dns.type", rtype or "any")
        result = passivedns.baseline(domain, rtype)
        if span: span.end()
        logging_util.incr("baselines")
        self._send(200, result)

    def _route_exposure(self, query):
        """Outside-in resolver exposure check (Workstream S #5): tests whether the REQUESTER's own
        public IP answers recursive DNS from the internet — the WAN-reachability the client-side Portal
        open-resolver probe cannot confirm from inside. Self-scoped BY CONSTRUCTION: only the connection's
        source IP is probed, never an arbitrary target, so this can never be a scanner."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        # A145: apply the per-route rate limit defined in config.py (5/min by default),
        # not the generic 30/s bucket. The exposure route sends outbound probes from the
        # Syxx host, so the tighter limit prevents abuse as a reflector.
        if not ratelimit.allow("exposure:" + self._rate_key(ent),
                               capacity=config.EXPOSURE_RATE_CAPACITY,
                               refill_millis=config.EXPOSURE_RATE_REFILL_MILLIS):
            return self._json_error(429, "exposure check limit exceeded")
        ip = self._client_ip()
        try:
            addr = ipaddress.ip_address(ip)
        except ValueError:
            return self._json_error(400, "no usable source address")
        if addr.version != 4 or addr.is_private or addr.is_loopback or addr.is_reserved:
            return self._send(200, {"ip": ip, "reachable": False, "openResolver": False,
                                    "assessment": "not-applicable",
                                    "detail": "source is not a public IPv4 address; nothing to test from outside"})
        span = _OTEL_TRACER.start_span("server.exposure") if _OTEL_TRACER else None
        if span: span.set_attribute("exposure.ip", ip)
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
        if span: span.set_attribute("exposure.assessment", assessment); span.end()
        detail = {
            "open-resolver": ("your public IP answered a recursive query from the internet — it is an OPEN "
                              "RESOLVER, reachable from the WAN and abusable as a DDoS reflector; disable "
                              "recursion for external clients"),
            "closed": ("your public IP is reachable on port 53 from the internet but refused to answer a "
                       "recursive query, so it is not acting as an open resolver"),
            "inconclusive": ("we could not get an answer from your public IP, so we cannot tell whether it "
                             "is an open resolver; this is not proof that it is closed"),
        }[assessment]
        self._send(200, {
            "ip": ip, "reachable": recursive, "openResolver": recursive,
            "assessment": assessment, "detail": detail,
        })

    def _route_passive(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain:
            return self._json_error(400, "missing domain")
        # Pagination support: parse page/limit from query string.
        result = passivedns.query(domain)
        records = result.get("records", [])
        paged, meta = _paginate(query, records)
        result["records"] = paged
        result["pagination"] = meta
        self._send(200, result)

    def _route_leak_token(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        zone = (query.get("zone", [None])[0])
        span = _OTEL_TRACER.start_span("server.leak_token") if _OTEL_TRACER else None
        if span: span.set_attribute("leak.zone", zone or "")
        # A133: mark paid tokens with origin='license' so the free route cannot
        # serve them. The free route checks origin and refuses non-'ip' tokens.
        result = leak.issue_token(ent.subject, zone, origin="license")
        if span: span.end()
        self._send(200, result)

    def _route_leak_verify(self, query):
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        token = (query.get("token", [""])[0]).strip()
        if not token:
            return self._json_error(400, "missing token")
        span = _OTEL_TRACER.start_span("server.leak_verify") if _OTEL_TRACER else None
        if span: span.set_attribute("leak.token_prefix", token[:8] if len(token) > 8 else token)
        result, label = leak.verify(token, ent.subject)
        if span: span.end()
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
        if span: span.set_attribute("leak.client_ip", client_ip); span.set_attribute("leak.zone", zone); span.end()
        result = leak.issue_token(client_ip, zone)
        self._send(200, result)

    def _route_leaktest_check(self, query):
        """Free public leak test: return observed resolvers for a token. No auth required."""
        if not self._rate_ok(None):
            return
        token = (query.get("token", [""])[0]).strip()
        if not token:
            return self._json_error(400, "missing token")
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
        if span: span.set_attribute("leak.token_prefix", token[:8] if len(token) > 8 else token)
        client_ip = self._client_ip()
        result, label = leak.verify(token, client_ip, allow_any_subject=True)
        if result is None:
            return self._json_error(404, label)
        if span: span.end()
        self._send(200, result)
        # A133: never consume() from an unauthenticated route. Destructive state
        # changes must be gated on authentication. The paid route (_route_leak_verify)
        # is the only path that consumes tokens. Free tokens expire via TTL.

    # --- Free-tier public utilities (rate-limited per IP, no entitlement) ----

    def _route_lookup(self, query):
        """Free-tier DNS lookup: query a resolver for any record type. Rate-limited per IP,
        no entitlement required. Uses the existing resolver.query() infrastructure."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or len(domain) > 253:
            return self._json_error(400, "missing or invalid domain")
        if not investigate.valid_domain(domain):
            return self._json_error(400, "invalid domain format")

        type_str = (query.get("type", ["A"])[0]).strip().upper()
        if type_str not in _LOOKUP_TYPE_NAMES:
            return self._json_error(400, "unsupported record type")
        qtype = resolver.TYPE_NAMES[type_str]

        server = (query.get("server", [""])[0]).strip()
        if not server:
            server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
        # A141: restrict ?server= to the allowlist of known public resolvers to prevent
        # using this endpoint as an arbitrary DNS proxy/scanner.
        if server not in config.PUBLIC_RESOLVERS:
            return self._json_error(400, "server must be in the allowed resolver list")

        try:
            result = resolver.query(domain, qtype, server, timeout=config.INVESTIGATE_TIMEOUT)
        except resolver.DNSError as e:
            return self._json_error(502, "DNS query failed: %s" % e)

        logging_util.incr("lookups")
        self._send(200, {
            "domain": domain, "type": type_str, "server": server,
            "rcode": result.get("rcode", -1),
            "rcodeName": resolver.rcode_name(result.get("rcode", -1)),
            "ad": result.get("ad", False),
            "records": result.get("records", []),
            "truncated": result.get("truncated", False),
        })

    def _route_reverse_dns(self, query):
        """Free-tier reverse DNS lookup. Converts IP to arpa name and queries PTR."""
        if not self._rate_ok(None):
            return
        ip_str = (query.get("ip", [""])[0]).strip()
        if not ip_str:
            return self._json_error(400, "missing ip parameter")
        try:
            arpa = resolver.ip_to_arpa(ip_str)
        except resolver.DNSError as e:
            return self._json_error(400, str(e))

        server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
        try:
            result = resolver.query(arpa, resolver.TYPE_PTR, server,
                                    timeout=config.INVESTIGATE_TIMEOUT)
        except resolver.DNSError as e:
            return self._json_error(502, "reverse DNS query failed: %s" % e)

        ptr_records = [r["value"] for r in result.get("records", []) if r["type"] == "PTR"]
        logging_util.incr("reverse_dns_lookups")
        self._send(200, {
            "ip": ip_str, "arpa": arpa,
            "ptr": ptr_records[0] if ptr_records else None,
            "allPtr": ptr_records,
            "rcode": result.get("rcode", -1),
        })

    def _route_benchmark(self, query):
        """Free-tier DNS benchmark: time resolution across public resolvers."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain):
            return self._json_error(400, "missing or invalid domain")

        type_str = (query.get("type", ["A"])[0]).strip().upper()
        # Unsupported types fall back to A (benchmark behavior, not an error).
        qtype = resolver.TYPE_NAMES[type_str] if type_str in _BENCH_TYPE_NAMES else resolver.TYPE_A

        try:
            rounds = min(10, max(1, int(query.get("rounds", ["3"])[0])))
        except (ValueError, TypeError):
            rounds = 3

        resolver_labels = {
            "1.1.1.1": "Cloudflare", "8.8.8.8": "Google", "9.9.9.9": "Quad9",
            "208.67.222.222": "OpenDNS", "64.6.64.6": "Verisign",
        }
        servers = config.PUBLIC_RESOLVERS[:6]
        results_by_server = {}
        deadline_start = time.monotonic()
        _BENCHMARK_DEADLINE = 15.0  # S3: aggregate wall-clock cap
        truncated = False  # A135: track whether we stopped early due to deadline

        for server in servers:
            elapsed_total = time.monotonic() - deadline_start
            if elapsed_total > _BENCHMARK_DEADLINE:
                truncated = True  # A135: mark that we stopped early
                break  # S3: stop probing when the aggregate deadline is exceeded
            entries = []
            for _ in range(rounds):
                # A135: check deadline INSIDE the inner loop so we don't run unbounded
                remaining_budget = _BENCHMARK_DEADLINE - (time.monotonic() - deadline_start)
                if remaining_budget <= 0:
                    truncated = True
                    break
                t0 = time.monotonic()
                # A135: derive each query's timeout from remaining budget (capped at 5s)
                query_timeout = min(5.0, remaining_budget)
                try:
                    result = resolver.query(domain, qtype, server, timeout=query_timeout)
                    elapsed = (time.monotonic() - t0) * 1000
                    entries.append({
                        "time_ms": round(elapsed, 2),
                        "rcode": result.get("rcode", -1),
                        "recordCount": len(result.get("records", [])),
                    })
                except resolver.DNSError as e:
                    elapsed = (time.monotonic() - t0) * 1000
                    entries.append({
                        "time_ms": round(elapsed, 2),
                        "rcode": -1, "error": str(e), "recordCount": 0,
                    })
            # A135: if we broke out of the inner loop due to deadline, mark truncation
            if len(entries) < rounds:
                truncated = True
            successes = [e for e in entries if e["rcode"] >= 0]
            results_by_server[server] = {
                "server": server,
                "label": resolver_labels.get(server, server),
                "results": entries,
                "avg_ms": round(sum(e["time_ms"] for e in successes) / len(successes), 2) if successes else None,
                "min_ms": round(min(e["time_ms"] for e in successes), 2) if successes else None,
                "max_ms": round(max(e["time_ms"] for e in successes), 2) if successes else None,
                "failures": len(entries) - len(successes),
            }

        resolver_list = list(results_by_server.values())
        ranked = sorted([r for r in resolver_list if r["avg_ms"] is not None],
                        key=lambda r: r["avg_ms"])
        logging_util.incr("benchmarks")
        self._send(200, {
            "domain": domain, "type": type_str, "rounds": rounds,
            "resolvers": resolver_list,
            "fastest": ranked[0]["server"] if ranked else None,
            "slowest": ranked[-1]["server"] if ranked else None,
            # A135: report truncation honestly so clients know results are partial
            "truncated": truncated,
            "serversTested": len(results_by_server),
            "serversRequested": len(servers),
        })

    def _route_whois(self, query):
        """Free-tier WHOIS/RDAP lookup. Rate-limited per IP, no entitlement required.
        Uses RDAP as primary (structured JSON), falls back to raw WHOIS on port 43.
        The IANA RDAP bootstrap is cached in memory for 24 hours (never fetched per-request)."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        try:
            result = whois_lookup.lookup(domain)
        except Exception as e:
            logging_util.error("whois_failed", domain=domain, err=str(e))
            return self._json_error(502, "WHOIS lookup failed: %s" % e)
        logging_util.incr("whois_lookups")
        self._send(200, result)

    def _route_propagation(self, query):
        """Free-tier DNS propagation checker: query many global resolvers concurrently.
        Uses concurrent.futures with a 12-second aggregate deadline. The pool.shutdown
        cancel_futures parameter requires Python 3.9+; on older versions we skip it."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")

        type_str = (query.get("type", ["A"])[0]).strip().upper()
        if type_str not in _PROPAGATION_TYPE_NAMES:
            return self._json_error(400, "unsupported record type")
        qtype = resolver.TYPE_NAMES[type_str]

        resolvers = config.PROPAGATION_RESOLVERS

        def query_one(entry):
            t0 = time.monotonic()
            try:
                result = resolver.query(domain, qtype, entry["ip"], timeout=4)
                elapsed = (time.monotonic() - t0) * 1000
                return {
                    "server": entry["ip"], "label": entry["label"], "region": entry["region"],
                    "status": "ok", "records": result.get("records", []),
                    "time_ms": round(elapsed, 1),
                }
            except resolver.DNSError as e:
                elapsed = (time.monotonic() - t0) * 1000
                return {
                    "server": entry["ip"], "label": entry["label"], "region": entry["region"],
                    "status": "error", "error": str(e), "time_ms": round(elapsed, 1),
                }

        # A136: use the module-level shared pool instead of per-request creation.
        # Cap resolvers per request to stay within the shared pool budget.
        resolvers_to_use = resolvers[:_PROPAGATION_POOL._max_workers]
        futures = [_PROPAGATION_POOL.submit(query_one, r) for r in resolvers_to_use]
        results = []
        try:
            for fut in concurrent.futures.as_completed(futures, timeout=12):
                # A136 Fix 3: wrap fut.result() so one socket error doesn't discard
                # all already-collected results.
                try:
                    results.append(fut.result())
                except Exception:
                    pass
        except concurrent.futures.TimeoutError:
            pass

        # Compute consensus
        all_values = set()
        for r in results:
            if r["status"] == "ok":
                for rec in r.get("records", []):
                    all_values.add(rec.get("value", ""))
        success_count = sum(1 for r in results if r["status"] == "ok")
        total = len(resolvers)
        propagation_pct = round(success_count / total * 100, 1) if total else 0

        if success_count == 0:
            # Zero resolvers answered, so `all_values` is empty and `len(all_values) <= 1` would
            # have declared "consistent" — agreement asserted over no reads at all, alongside
            # successCount: 0. Nothing was measured, so nothing is consistent.
            consensus = "inconclusive"
        elif len(all_values) <= 1:
            consensus = "consistent"
        elif len(all_values) <= 3:
            consensus = "mostly_consistent"
        else:
            consensus = "divergent"

        logging_util.incr("propagation_checks")
        self._send(200, {
            "domain": domain, "type": type_str,
            "resolvers": sorted(results, key=lambda r: r["label"]),
            "consensus": consensus,
            "uniqueValues": sorted(all_values),
            "totalResolvers": total,
            "successCount": success_count,
            "propagationPct": propagation_pct,
        })

    def _route_email_auth(self, query):
        """Free-tier SPF/DMARC/DKIM email authentication checker. Rate-limited per IP."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        dkim_selector = (query.get("selector", [""])[0]).strip() or None
        try:
            result = email_auth.check(domain, dkim_selector=dkim_selector)
        except Exception as e:
            logging_util.error("email_auth_failed", domain=domain, err=str(e))
            return self._json_error(502, "email auth check failed: %s" % e)
        logging_util.incr("email_auth_checks")
        self._send(200, result)

    def _route_subdomains(self, query):
        """Free-tier subdomain enumeration via Certificate Transparency logs. Rate-limited per IP."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        try:
            result = subdomain_enum.enumerate(domain)
        except Exception as e:
            logging_util.error("subdomain_enum_failed", domain=domain, err=str(e))
            return self._json_error(502, "subdomain enumeration failed: %s" % e)
        logging_util.incr("subdomain_enumerations")
        self._send(200, result)

    def _route_firewall_test(self, query):
        """Free-tier DNS firewall/filter detection test. Rate-limited per IP.
        A141: self-scoped by construction — only tests the caller's own IP, never an
        arbitrary target, so this can never be used as a scanner."""
        if not self._rate_ok(None):
            return
        # A141: removed caller-supplied ?resolver= parameter to make this self-scoped.
        # Always test the connection's source IP, matching /api/exposure's design.
        resolver_ip = self._client_ip()
        try:
            addr = ipaddress.ip_address(resolver_ip)
            if not resolver._ip_is_public(str(addr)):
                return self._json_error(400, "client IP is not a public address")
        except ValueError:
            return self._json_error(400, "invalid client IP address")
        try:
            result = firewall_test.test(resolver_ip)
        except Exception as e:
            logging_util.error("firewall_test_failed", resolver=resolver_ip, err=str(e))
            return self._json_error(502, "firewall test failed: %s" % e)
        logging_util.incr("firewall_tests")
        self._send(200, result)

    # --- Pro-tier APIs (entitlement required) -------------------------------

    def _route_create_api_key(self):
        """POST /api/keys — create a new long-lived API key for programmatic access."""
        ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        if not ratelimit.allow("keycreate:" + ent.subject, capacity=5, refill_millis=86400000):
            return self._json_error(429, "key creation limit: 5 per day")
        if not self._require_json():
            return
        raw = self._body()
        if self._body_error(raw):
            return
        try:
            payload = json.loads(raw or b"{}")
        except ValueError:
            return self._json_error(400, "invalid JSON")
        label = (payload.get("label") or "").strip()[:64]
        # A153 (SD-88/SD-89): validate ttlDays as an integer with bounds.
        # Without try/except, a non-numeric value (e.g. "abc" or {}) returns 500
        # instead of 400. Without a floor, ttlDays:0 mints an already-expired key
        # and the 201 response claims a usable credential was created.
        try:
            ttl = max(1, min(int(payload.get("ttlDays", 90)), 365))
        except (ValueError, TypeError, OverflowError):
            return self._json_error(400, "ttlDays must be an integer between 1 and 365")
        key = db.api_key_create(ent.subject, label=label, ttl_days=ttl)
        logging_util.incr("api_keys_created")
        self._send(201, {"key": key, "label": label, "expiresInDays": ttl,
                         "warning": "store this key now; it will not be shown again"})

    def _route_list_api_keys(self, query):
        """GET /api/keys — list all API keys for the authenticated user."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        keys = db.api_key_list(ent.subject)
        paged, meta = _paginate(query, keys)
        self._send(200, {"keys": paged, "pagination": meta})

    def _route_revoke_api_key(self, path):
        """DELETE /api/keys/<id> — revoke an API key."""
        ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        try:
            key_id = int(path.split("/")[-1])
        except (ValueError, IndexError):
            return self._json_error(400, "invalid key id")
        revoked = db.api_key_revoke(key_id, ent.subject)
        if revoked:
            logging_util.incr("api_keys_revoked")
        # Report what actually happened, matching _route_delete_monitor. An unconditional
        # {"revoked": true} claimed a revocation the server never performed.
        self._send(200, {"revoked": bool(revoked), "count": revoked})

    def _route_bulk_investigate(self):
        """POST /api/bulk-investigate — run investigation on up to 50 domains concurrently."""
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
            return self._json_error(502, "bulk investigation failed: %s" % e)
        logging_util.incr("bulk_investigations")
        self._send(200, result)

    def _route_lookalike(self):
        """POST /api/lookalike — scan for lookalike/homoglyph domains."""
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
        # Without try/except, a non-numeric value returns 500 instead of 400.
        # Without a floor, maxPermutations:-1 produces an empty scan reported as
        # completed — a claim ahead of the evidence.
        try:
            max_perm = max(1, min(int(payload.get("maxPermutations", 200)), 500))
        except (ValueError, TypeError, OverflowError):
            return self._json_error(400, "maxPermutations must be an integer between 1 and 500")
        try:
            result = lookalike.scan(domain, max_permutations=max_perm)
        except Exception as e:
            logging_util.error("lookalike_failed", domain=domain, err=str(e))
            return self._json_error(502, "lookalike scan failed: %s" % e)
        logging_util.incr("lookalike_scans")
        self._send(200, result)

    def _route_list_monitors(self, query):
        """GET /api/monitors — list all scheduled monitors for the authenticated user."""
        ent, ok = self._require(query, config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        self._send(200, {"monitors": db.monitor_list(ent.subject)})

    def _route_delete_monitor(self, path):
        """DELETE /api/monitors/<id> — delete a scheduled monitor."""
        ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
        monitor_id = urllib.parse.unquote(path.split("/")[-1])
        # Report what was actually deleted. An unconditional {"deleted": True} claimed a
        # deletion the server never performed for a nonexistent or foreign id — a result
        # ahead of the evidence, and an existence oracle for other subjects' monitor ids.
        deleted = db.monitor_delete(monitor_id, ent.subject)
        self._send(200, {"deleted": bool(deleted), "count": deleted})

    def _route_create_monitor(self):
        """POST /api/monitors — create a new scheduled monitor."""
        ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
        if not ok or not self._rate_ok(ent):
            return
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
        checks = payload.get("checks", "prophet")
        interval_hours = int(payload.get("interval_hours", 24))
        notify_email = (payload.get("notify_email") or "").strip() or None
        if not domain:
            return self._json_error(400, "missing domain")
        monitor_id = str(uuid.uuid4())
        db.monitor_create(monitor_id, ent.subject, domain, checks, interval_hours, notify_email)
        logging_util.incr("monitors_created")
        self._send(201, {
            "id": monitor_id,
            "domain": domain,
            "checks": checks,
            "interval_hours": interval_hours,
            "notify_email": notify_email,
        })

    def _route_dnssec(self, query):
        """Free-tier DNSSEC chain validator. Walks root→TLD→domain verifying DS/DNSKEY/RRSIG."""
        if not self._rate_ok(None):
            return
        domain = (query.get("domain", [""])[0]).strip()
        if not domain or not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid or missing domain")
        if dnssec_validator is None:
            return self._json_error(501, "DNSSEC validation unavailable (cryptography library not installed)")
        try:
            result = dnssec_validator.validate_chain(domain)
        except Exception as e:
            logging_util.error("dnssec_validation_failed", domain=domain, err=str(e))
            return self._json_error(502, "DNSSEC validation failed: %s" % e)
        logging_util.incr("dnssec_validations")
        self._send(200, result)

    # --- DoH forwarder (encrypted-transports D-1) --------------------------
    # RFC 8484 wireformat (application/dns-message) + the dns-json convenience format. A PAYWALLED
    # forwarder over PUBLIC_RESOLVERS, never an open resolver. NEVER asserts the channel is encrypted
    # (SD-59: this process is plaintext on :8080; TLS is nginx's, not ours) and never rewrites an
    # rcode — an upstream SERVFAIL/NXDOMAIN passes through with its real rcode; a total upstream
    # failure is HTTP 502, never a fabricated NOERROR.

    def _doh_gate(self, query):
        """Shared gate: entitlement (paywalled), optional IP allowlist, rate limit.
        Returns (entitlement, ok). Sends the 402/403/429 itself when not ok."""
        ent, ok = self._require(query, config.ENFORCE_DOH)
        if not ok:
            return None, False
        if config.DOH_ALLOWLIST and self._client_ip() not in config.DOH_ALLOWLIST:
            # Wording pending approval (D-1 approval gate) — logged to outputs-catalog.csv.
            self._json_error(403, "forwarder not enabled for this address")
            return None, False
        if not self._rate_ok(ent):
            return None, False
        return ent, True

    def _doh_forward(self, query_wire):
        """Forward raw query bytes to each PUBLIC_RESOLVER in turn; return raw response bytes, or
        None if all upstreams failed. Passthrough — never re-encoded (resolver.forward_wire)."""
        for server in config.PUBLIC_RESOLVERS:
            try:
                return resolver.forward_wire(query_wire, server, config.DOH_UPSTREAM_TIMEOUT)
            except resolver.DNSError as e:
                logging_util.warn("doh_upstream_failed", server=server, err=str(e))
                continue
        return None

    def _route_doh_get(self, query):
        ent, ok = self._doh_gate(query)
        if not ok:
            return
        dns_param = query.get("dns", [""])[0]
        if dns_param:
            # base64url, no padding (RFC 8484). Restore padding before decode.
            try:
                pad = "=" * (-len(dns_param) % 4)
                wire = base64.urlsafe_b64decode(dns_param + pad)
            except Exception:
                return self._json_error(400, "invalid dns= parameter (want base64url)")
            return self._doh_respond_wire(wire)
        if query.get("name", [""])[0]:
            return self._doh_respond_json(query)
        return self._json_error(400, "provide ?dns= (wireformat) or ?name= (json)")

    def _route_doh_post(self, query):
        ent, ok = self._doh_gate(query)
        if not ok:
            return
        ctype = (self.headers.get("Content-Type", "").split(";")[0]).strip().lower()
        if ctype != "application/dns-message":
            return self._json_error(415, "POST requires Content-Type: application/dns-message")
        raw = self._body()
        if self._body_error(raw):
            return
        if not raw:
            return self._json_error(400, "empty DNS query body")
        return self._doh_respond_wire(raw)

    def _doh_respond_wire(self, wire):
        if len(wire) < 12:
            return self._json_error(400, "DNS query too short")
        resp = self._doh_forward(wire)
        if resp is None:
            # Every upstream failed. 502, never 200-with-empty: "we could not resolve" must not read
            # as "no such record".
            return self._json_error(502, "all upstream resolvers failed")
        self._send(200, resp, "application/dns-message")

    # Supported "?type=" names for the JSON convenience format, from the single
    # resolver.TYPE_NAMES table. The wireformat path carries any type; JSON is a
    # convenience for these. A numeric ?type= is also accepted.
    _DOH_JSON_TYPES = _DOH_JSON_TYPE_NAMES

    def _doh_respond_json(self, query):
        name = (query.get("name", [""])[0]).strip()
        type_param = (query.get("type", ["A"])[0]).strip().upper()
        qtype = resolver.TYPE_NAMES.get(type_param) if type_param in _DOH_JSON_TYPE_NAMES else None
        if qtype is None:
            try:
                qtype = int(type_param)
            except ValueError:
                return self._json_error(400, "unknown type %r" % type_param)
        # A124: iterate ALL configured resolvers (not just [0]) so a single poisoned
        # resolver cannot dictate the answer. Collect answers from each and apply
        # 2-of-3 corroboration before returning, matching the wireformat path's
        # standard (_clean_vantage_answers / A027).
        servers = list(config.PUBLIC_RESOLVERS[:3])
        if not servers:
            return self._json_error(502, "no upstream resolvers configured")
        parsed_results = []
        last_error = None
        for server in servers:
            try:
                parsed = resolver.query(name, qtype, server,
                                        timeout=config.DOH_UPSTREAM_TIMEOUT)
                parsed_results.append(parsed)
            except resolver.DNSError as e:
                last_error = e
                logging_util.warn("doh_json_upstream_failed", name=name,
                                  server=server, err=str(e))
                continue
        if not parsed_results:
            # All upstreams failed. Emit Google-JSON Status:2 (SERVFAIL) under HTTP 200 —
            # never Status:0 with empty answers (a fake clean). Wireformat keeps HTTP 502
            # because an empty dns-message body would look like a truncated success.
            logging_util.warn("doh_json_all_upstreams_failed", name=name,
                              err=str(last_error) if last_error else "unknown")
            return self._send(200, {
                "Status": 2,
                "TC": False,
                "RD": True,
                "AD": False,
                "Question": [{"name": name, "type": qtype}],
                "Comment": "upstream query failed",
            })
        # Use the first successful result; with corroboration, if multiple resolvers
        # returned results, prefer the answer set confirmed by >=2 vantages.
        chosen = parsed_results[0]
        if len(parsed_results) >= 2:
            # _TYPE_NAMES maps numeric types to strings; reverse-map the type_param
            # to match the record's "type" field (which is the formatted string name).
            answer_sets = []
            for p in parsed_results:
                aset = frozenset(
                    str(r["value"]).lower()
                    for r in p.get("records", [])
                    if r.get("type") == type_param
                )
                answer_sets.append(aset)
            # Pick the answer set that appears most often (>=2 agreement preferred).
            from collections import Counter
            counts = Counter(answer_sets)
            best_set, best_count = counts.most_common(1)[0]
            if best_count >= 2:
                # Find the parsed result matching the corroborated set.
                for p in parsed_results:
                    pset = frozenset(
                        str(r["value"]).lower()
                        for r in p.get("records", [])
                        if r.get("type") == type_param
                    )
                    if pset == best_set:
                        chosen = p
                        break
        # The parser returns {rcode, truncated, ad, records:[{type,value}]} — no TTL is captured, so
        # the JSON omits it rather than fabricate one. `ad` is the only DNSSEC signal we actually
        # observed; RA/CD are not measured, so they are not claimed.
        answers = [{"name": name, "type": qtype, "data": r["value"]}
                   for r in chosen.get("records", [])]
        self._send(200, {
            "Status": chosen.get("rcode", 0),
            "TC": chosen.get("truncated", False),
            "RD": True,
            "AD": chosen.get("ad", False),
            "Question": [{"name": name, "type": qtype}],
            "Answer": answers,
        })


def _start_purge_thread():
    """Purge expired/aged personal data on startup, then on a fixed interval."""
    import threading
    import time as _time

    def _loop():
        while True:
            _time.sleep(max(60, config.PURGE_INTERVAL_SECONDS))
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

_BETA_ACK_TEMPLATE = os.path.join(os.path.dirname(__file__), "emails", "beta_ack.html")

# Bounded thread pool for beta confirmation emails so a deluge of signups
# doesn't exhaust OS thread resources (SRV-034).
_beta_mail_pool = concurrent.futures.ThreadPoolExecutor(
    max_workers=4, thread_name_prefix="beta-confirm")


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
    _beta_mail_pool.submit(mailer.send, email, "You're on the beta list — Nnyx & Zzyn",
                           body=text, html=html)


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
        try:
            return datetime.datetime.fromtimestamp(
                int(ts), datetime.timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
        except (TypeError, ValueError):
            return ""

    def _ip(raw):
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
        w.writerow([_csv_safe(r["email"]), _csv_safe(r["interests"]),
                    _csv_safe(_iso(r["created_at"])), _csv_safe(_iso(r["updated_at"])),
                    _ip(r.get("ip"))])
    return buf.getvalue().encode("utf-8")


def _start_beta_digest_thread():
    """Email the operator a weekly CSV of beta signups. No-op unless SMTP is configured
    AND a recipient is set. Cadence survives restarts via a kv timestamp; the first
    interval is seeded on startup so a fresh deploy doesn't fire a digest immediately."""
    import datetime
    import threading
    import time as _time

    if not (mailer.enabled() and config.BETA_DIGEST_TO):
        logging_util.info("beta_digest_disabled",
                          smtp=mailer.enabled(), recipient=bool(config.BETA_DIGEST_TO))
        return

    interval = max(1, config.BETA_DIGEST_INTERVAL_DAYS) * 86400

    def _send_if_due(now):
        last = db.kv_get("beta_digest_last_sent")
        if last is None:
            db.kv_set("beta_digest_last_sent", str(now))   # seed; first digest one interval later
            return
        if (now - int(last)) < interval:
            return
        rows = db.beta_all()
        if not rows:
            db.kv_set("beta_digest_last_sent", str(now))
            logging_util.info("beta_digest_skipped", reason="no_signups")
            return
        stamp = datetime.datetime.fromtimestamp(now, datetime.timezone.utc).strftime("%Y-%m-%d")
        ok = mailer.send(
            config.BETA_DIGEST_TO,
            "Syxx beta signups — weekly digest (%s)" % stamp,
            "Attached: %d beta signup(s) as of %s UTC.\n\n"
            "Automated weekly export from the Syxx server." % (len(rows), stamp),
            attachments=[("syxx-beta-signups-%s.csv" % stamp, "text/csv", _beta_digest_csv(rows))],
        )
        if ok:
            db.kv_set("beta_digest_last_sent", str(now))
            logging_util.info("beta_digest_sent", count=len(rows))
        else:
            logging_util.error("beta_digest_failed", count=len(rows))   # left un-stamped → retry next tick

    def _loop():
        while True:
            _time.sleep(3600)   # check hourly; the interval gate is by timestamp, not sleep length
            try:
                _send_if_due(int(_time.time()))
            except Exception as e:
                logging_util.error("beta_digest_loop_error", err=str(e))

    try:
        _send_if_due(int(_time.time()))   # seed last_sent (or catch up) at startup
    except Exception as e:
        logging_util.error("beta_digest_loop_error", err=str(e))
    threading.Thread(target=_loop, name="syxx-beta-digest", daemon=True).start()


def serve():
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
    httpd = BoundedThreadingHTTPServer((config.HTTP_HOST, config.HTTP_PORT), Handler)
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
    listeners.set_state("http", "listening", detail="port %d" % config.HTTP_PORT)
    httpd.serve_forever()

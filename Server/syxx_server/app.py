"""Threaded HTTP server, router and auth middleware.

Route classes:
  public : /, static assets, /healthz, /readyz, /metrics, /api/stats, /api/broadcast,
           /api/whats-new, /api/startup, /api/auth/activate (activation is how you GET
           entitled), /api/checkout (purchase flow), /api/webhooks/stripe (signature-verified)
  paid   : everything that costs server resources — /warp, /fetch_script, /leak
           (native app), and /api/investigate, /api/authoritative, /api/diff, /api/trace,
           /api/baseline, /api/exposure, /api/passive-dns, /api/leak/* (website). Paid routes
           require an Entitlement or they return 402.

The website therefore cannot bypass the paywall: its live-query endpoints are in
the paid class and demand the same license the app uses.
"""

# --- Re-exports from routes.base for backward compatibility ---
# Tests and external code access these as app.<name>.
from http.server import ThreadingHTTPServer  # noqa: F401 — tests use app.ThreadingHTTPServer

# Submodules re-exported so tests can monkey-patch them (e.g. app.fulfillment.handle_event = ...).
from . import fetcher, fulfillment, stripe_checkout  # noqa: F401

# --- OpenTelemetry error reporting ---
# The OTel initialization block lives in routes/base.py (moved during the modular
# refactoring). The state is re-exported below as _OTEL_STATE, _OTEL_TRACER, etc.
# The except clauses in that block all record failures to _OTEL_STATE rather than
# swallowing them with a bare `except: pass`.

from .routes.base import (
    # Classes
    BoundedThreadingHTTPServer,
    Handler as _BaseHandler,
    # Functions
    serve,
    _valid_email,
    _version_key,
    _update_available,
    _nfc_normalize_strings,
    _redact_domain,
    _paginate,
    _prometheus_exposition,
    _send_beta_confirmation,
    # Sentinel objects
    _BODY_TOO_LARGE,
    _BODY_INCOMPLETE,
    # Module-level state
    _OTEL_STATE,
    _OTEL_TRACER,
    _OTEL_STATUS_ERROR,
    _OTEL_LOGGER,
    _sentry_healthy,
    _PROPAGATION_POOL,
    _MAX_WORKERS,
    _REQUEST_DEADLINE,
    # Constants
    _STATIC_FILES,
    _ASSET_TYPES,
    _KNOWN_ENDPOINT_PREFIXES,
    # Regex patterns
    _UUID_RE,
    _HEX_RE,
    _WARP_NONCE_RE,
    _EMAIL_RE,
    # Diff re-exports (tests access app._diff_content etc.)
    _clean_vantage_answers,
    _diff_content,
    _diff_result,
)

# --- Import route mixins ---
from .routes.dns import DnsRoutes
from .routes.warp import WarpRoutes
from .routes.leak import LeakRoutes
from .routes.utilities import UtilityRoutes
from .routes.account import AccountRoutes
from .routes.doh import DohRoutes
from .routes.operational import OperationalRoutes


class Handler(
    OperationalRoutes,
    AccountRoutes,
    DohRoutes,
    DnsRoutes,
    WarpRoutes,
    LeakRoutes,
    UtilityRoutes,
    _BaseHandler,
):
    """Composed request handler combining all route mixins with the base infrastructure.

    Method resolution order: route mixins are searched before _BaseHandler so that
    route methods (e.g. _route_investigate) resolve from the appropriate mixin.
    Non-route methods (_send, _require, _cors, etc.) resolve from _BaseHandler.
    """

    pass

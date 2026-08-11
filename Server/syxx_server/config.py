"""Environment-driven configuration.

Everything is overridable via environment variables so the daemon can be
configured from the systemd unit without editing code. No secrets are hardcoded;
the session-signing secret is generated and persisted on first run (see db.py).

.. SECURITY NOTE (A079):
   All secrets loaded in this module (Stripe keys, Keygen tokens, SMTP password,
   admin token, webhook HMAC secrets) are stored as plain Python ``str`` objects.
   Python strings are **immutable** — they cannot be overwritten or cleared from
   memory, and they persist in the process heap until the GC reclaims the object
   *and* the allocator returns the page.  This means:

   1. **Core dumps** — A crash dump or ``/proc/<pid>/mem`` read will contain the
      raw secret bytes in cleartext.  Mitigate by disabling core dumps in
      production::

          sysctl -w kernel.core_pattern='|/bin/false'   # Linux (systemd unit)
          ulimit -c 0                                    # shell-level fallback

   2. **Memory-mapped swap** — If the OS swaps anonymous pages, secrets may land
      on disk unencrypted.  Mitigate with encrypted swap or ``mlock`` on the
      process.

   3. **No zero-copy** — Every ``_s()`` call returns a new ``str``; there is no
      ``mlock``/``SecureString`` wrapper in CPython.

   For higher-assurance environments, consider migrating secrets to a dedicated
   secrets manager (HashiCorp Vault, AWS Secrets Manager) and fetching them via
   short-lived subprocess calls so the Python process never holds the plaintext.
   That restructuring is tracked separately and is out of scope for this pass.
"""

import os

# A021: logging_util has no dependency on config (verified), so this import is
# safe at module level.  The previous deferred-import workaround was based on a
# stale assumption that logging_util → config existed.
from . import logging_util


def _b(name, default=False):
    """Read a boolean config from the environment.

    A131: tri-state parsing. Accepts explicit positive (``1``, ``true``,
    ``yes``, ``on``) and negative (``0``, ``false``, ``no``, ``off``) values.
    An unset or empty variable returns ``default``. An unrecognised value
    (e.g. ``enabled``, ``TRUE!``, ``always``) also returns ``default`` and
    logs a warning so the operator can fix the typo — a mis-typed value is
    not silently coerced to the unsafe direction.

    Flags whose secure default is ``True`` (e.g. ``ENFORCE_NATIVE``,
    ``ENFORCE_DOH``) pass ``default=True`` so that an unrecognised value
    does not silently disable enforcement.
    """
    v = os.environ.get(name)
    if v is None or v.strip() == "":
        return default
    normalised = v.strip().lower()
    if normalised in ("1", "true", "yes", "on"):
        return True
    if normalised in ("0", "false", "no", "off"):
        return False
    # A131: unrecognised value — return the default, never silently coerce.
    # A careful operator writing "enabled" or "TRUE!" must not get False on
    # a flag whose secure default is True (that would disable enforcement).
    # C223: route through logging_util for structured JSON logging.
    # A021: logging_util is now imported at module level (no cycle exists).
    logging_util.warn("config_invalid_bool", var=name, value=v, default=default)
    return default


def _i(name, default):
    """Read an integer config from *name*, logging and returning *default* on parse failure (A154)."""
    # A154: log the coercion so the operator is told their value was discarded.
    # A silent fallback is the defect; the fallback itself is defensible. Without
    # logging, `SYXX_RATE_CAPACITY=thirty` silently becomes 30 and the operator
    # believes they changed a limit when they did not.
    raw = os.environ.get(name, "")
    if not raw.strip():
        return default
    try:
        return int(raw.strip())
    except (TypeError, ValueError):
        # C223: route through logging_util for structured JSON logging.
        # A021: logging_util is now imported at module level (no cycle exists).
        logging_util.warn("config_invalid_int", var=name, value=raw, default=default)
        return default


def _s(name, default):
    """Read a string config from *name*, returning *default* when absent or empty."""
    v = os.environ.get(name)
    return v if v is not None and v != "" else default


def _list(name, default):
    """Read a comma-separated list config, deduplicating while preserving order."""
    v = os.environ.get(name)
    if not v:
        return list(default)
    seen = {}
    for x in v.split(","):
        x = x.strip()
        if x and x not in seen:
            seen[x] = True
    return list(seen)


# --- Deployment environment ------------------------------------------------
#: "production", "staging", or "development" (default). Used by validation
#: helpers to gate warnings that are only meaningful in production.
SYXX_ENV = os.environ.get("SYXX_ENV", "development")

# --- Paths -----------------------------------------------------------------
BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATIC_DIR = _s("SYXX_STATIC_DIR", BASE_DIR)  # index.html / favicon live here
DB_PATH = _s("SYXX_DB_PATH", os.path.join(BASE_DIR, "syxx-server.db"))
# Rotating online backups (SD-56 / SHIP-v1 S7-1). Blank dir → "<db-dir>/backups". KEEP=0 disables
# pruning. Local step one; an off-host copy of this dir is the operator's follow-up.
# E035: Optional key for at-rest encryption of sensitive kv table entries (e.g. session_secret).
# When set, kv_set_encrypted / kv_get_encrypted (to be implemented) use AES-GCM with this key.
# Unset = plaintext storage protected by filesystem permissions only (see kv_repo.get_or_create_secret).
DB_ENCRYPTION_KEY = os.environ.get("SYXX_DB_ENCRYPTION_KEY", "")
DB_BACKUP_DIR = _s("SYXX_DB_BACKUP_DIR", "")
DB_BACKUP_KEEP = _i("SYXX_DB_BACKUP_KEEP", 14)
# Operator-edited broadcast / message-of-the-day (Workstream D2). Edit this JSON to push a message
# to the app on next launch with no update; delete/empty it to clear.
BROADCAST_FILE = _s("SYXX_BROADCAST_FILE", os.path.join(BASE_DIR, "broadcast.json"))
# Per-platform What's New / Welcome HTML (Workstream D3) — self-contained (inline CSS, data-URI
# images), rendered in the app's hardened embedded webview. Separate files so mac/win differ.
# The What's-New HTML lives in the deployment dir (/opt/SyxxServer) by default — NOT the repo BASE_DIR,
# so a code checkout doesn't have to carry it and the running server finds it where it's actually placed.
# Override the dir with SYXX_WHATSNEW_DIR, or each file with SYXX_WHATSNEW_MAC / SYXX_WHATSNEW_WIN.
WHATSNEW_DIR = _s("SYXX_WHATSNEW_DIR", "/opt/SyxxServer")
WHATSNEW_MAC = _s("SYXX_WHATSNEW_MAC", os.path.join(WHATSNEW_DIR, "whats-new-mac.html"))
WHATSNEW_WIN = _s("SYXX_WHATSNEW_WIN", os.path.join(WHATSNEW_DIR, "whats-new-win.html"))
WHATSNEW_DEB = _s("SYXX_WHATSNEW_DEB", os.path.join(WHATSNEW_DIR, "whats-new-deb.html"))

# Dedicated per-platform What's-New / MOTD origins. A request whose Host header is one of these serves the
# corresponding whats-new HTML at / (Host routing in app.py `_dispatch_get`), keeping client-operation
# content off the marketing site (syxx.app) and the leak backend (leak.syxx.app).
MOTD_MAC_HOST = _s("SYXX_MOTD_MAC_HOST", "macmotd.syxx.app")
MOTD_WIN_HOST = _s("SYXX_MOTD_WIN_HOST", "winmotd.syxx.app")
# Debian/Ubuntu/Mint family (AGENTS.md:36 names the Linux target as that family, so the host is
# debmotd rather than a generic linmotd — owner decision 2026-07-19).
MOTD_DEB_HOST = _s("SYXX_MOTD_DEB_HOST", "debmotd.syxx.app")
# A150: leaktest host — exact match, not a prefix. The previous startswith("leaktest.")
# allowed any host beginning with "leaktest." (including attacker-controlled hosts) to
# serve the leak test page. Consistent with MOTD_*_HOST pattern above.
LEAKTEST_HOST = _s("SYXX_LEAKTEST_HOST", "leaktest.syxx.app")
# Latest shipped version per platform (Workstream D1/startup). Blank disables the update check until
# the Sparkle/NetSparkle appcast is wired; when set, /api/startup reports availability vs the client's version.
LATEST_MAC = _s("SYXX_LATEST_MAC", "")
LATEST_WIN = _s("SYXX_LATEST_WIN", "")
LATEST_DEB = _s("SYXX_LATEST_DEB", "")
# Default download URLs used by publish_package() when the caller does not pass download_url.
# Must be allowlisted HTTPS and end with the platform package extension (.dmg / .zip / .deb).
# The previous bare https://syxx.app/download defaults cannot publish: they lack a package extension.
UPDATE_URL_MAC = _s("SYXX_UPDATE_URL_MAC", "https://syxx.app/dist/Syxx-mac.dmg")
UPDATE_URL_WIN = _s("SYXX_UPDATE_URL_WIN", "https://syxx.app/dist/Syxx-win.zip")
UPDATE_URL_DEB = _s("SYXX_UPDATE_URL_DEB", "https://syxx.app/dist/Syxx-linux.deb")

# --- G-1 authenticated updater (Phase 3.2) ---------------------------------
# Dedicated Ed25519 update-signing identity. NEVER the collector / leak key.
# Production: set SYXX_UPDATE_PRIVATE_KEY (PKCS8 DER, base64) or
# SYXX_UPDATE_PRIVATE_KEY_PATH. DEV-ONLY fallback under Server/dev-keys/ loads only
# when SYXX_ALLOW_DEV_UPDATE_KEY=1. Production key rotation is owner-gated.
UPDATE_PRIVATE_KEY = _s("SYXX_UPDATE_PRIVATE_KEY", "")
UPDATE_PRIVATE_KEY_PATH = _s("SYXX_UPDATE_PRIVATE_KEY_PATH", "")
ALLOW_DEV_UPDATE_KEY = _b("SYXX_ALLOW_DEV_UPDATE_KEY", False)
UPDATE_DEV_PRIVATE_KEY_PATH = _s(
    "SYXX_UPDATE_DEV_PRIVATE_KEY_PATH",
    os.path.join(BASE_DIR, "dev-keys", "update-signing.priv.b64"),
)
# Per-platform published package metadata (atomic JSON written by publish_package).
UPDATE_PACKAGE_DIR = _s(
    "SYXX_UPDATE_PACKAGE_DIR",
    os.path.join(BASE_DIR, "update-packages"),
)

# --- Stripe Managed Payments (Workstream D4) --------------------------------
# Secrets ALL from env — never hardcode keys. sk_/rk_ secret + whsec_ webhook secret; a blank
# secret disables the checkout/webhook routes.
# A079: STRIPE_SECRET_KEY and STRIPE_WEBHOOK_SECRET are held as Python str (immutable,
# not clearable).  See module-level SECURITY NOTE for core-dump and swap mitigations.
STRIPE_SECRET_KEY = _s("STRIPE_SECRET_KEY", "")
STRIPE_WEBHOOK_SECRET = _s("STRIPE_WEBHOOK_SECRET", "")
# SEC-048: Validate webhook secret length. Stripe webhook secrets are at least
# 24 characters (whsec_ prefix + 24+ chars). A shorter value indicates a typo
# or truncation that would silently accept forged events.
if STRIPE_WEBHOOK_SECRET and len(STRIPE_WEBHOOK_SECRET) < 24:
    import logging
    logging.getLogger("syxx-server").warning(
        "config_stripe_webhook_too_short: STRIPE_WEBHOOK_SECRET is %d chars "
        "(expected >=24); webhook verification will likely fail or accept "
        "forged events. Check your environment configuration.",
        len(STRIPE_WEBHOOK_SECRET),
    )
# A128: configurable webhook timestamp tolerance (seconds). Default 300 matches
# Stripe's own recommendation. Increase if the server's clock source has known
# skew; decrease for tighter replay protection. A clock-skewed server rejects
# ALL valid webhooks silently — log the skew so the operator can diagnose it.
STRIPE_WEBHOOK_TOLERANCE = _i("STRIPE_WEBHOOK_TOLERANCE", 300)
STRIPE_API_VERSION = _s("STRIPE_API_VERSION", "2025-03-31.basil")  # Managed Payments minimum
CHECKOUT_SUCCESS_URL = _s("STRIPE_SUCCESS_URL", "https://syxx.app/purchase/success?session_id={CHECKOUT_SESSION_ID}")
CHECKOUT_CANCEL_URL = _s("STRIPE_CANCEL_URL", "https://syxx.app/purchase/cancel")

# A025: SEO pricing constants — single source of truth for both seo.py and app.py.
# E-10: configurable via env to prevent drift from Stripe catalog.
SEO_PRICE_MONTHLY = _s("SYXX_SEO_PRICE_MONTHLY", "3.95")
SEO_PRICE_YEARLY = _s("SYXX_SEO_PRICE_YEARLY", "19.95")

# Stripe subscription trial (`subscription_data[trial_period_days]` on the Checkout Session).
# The buyer enters payment details, is NOT charged for N days, and Stripe fires
# customer.subscription.deleted if they never convert (which the lapse path already suspends).
#
# DEFAULT 0 = no Stripe trial, i.e. today's shipped behaviour is unchanged. A nonzero default
# would give product away on every purchase from the moment this line merged, and how much
# product to give away is an owner decision, not a deployment one. Set SYXX_STRIPE_TRIAL_DAYS
# to turn it on.
#
# THIS IS NOT THE CLIENTS' TRIAL. The clients run a 3-day LOCAL trial anchored to a device
# fingerprint (macOS/Sources/SyxxKit/LicenseManager.swift:316) that happens BEFORE any checkout
# session exists. No Stripe setting can reconcile that one, because Stripe never sees a local
# trial user.
#
# Negative values are clamped to 0: a negative trial is not a meaningful instruction, and
# forwarding it to Stripe would fail the whole checkout for a buyer who did nothing wrong.
STRIPE_TRIAL_DAYS = max(0, _i("SYXX_STRIPE_TRIAL_DAYS", 0))

# E023: Trial anchor endpoint is aware of VM/container fingerprint bypass.
# The fingerprint is derived from hardware identifiers (MAC address, disk serial, etc.)
# which can be spoofed or regenerated in virtualized environments. This is a deliberate
# enforcement tradeoff: we accept that determined users can reset trials by cloning VMs
# rather than implementing invasive device attestation (TPM/secure enclave checks) that
# would complicate legitimate multi-device usage and require platform-specific code paths.
# The 3-day local trial (macOS/Sources/SyxxKit/LicenseManager.swift:316) is the primary
# trial mechanism; this server-side anchor is a secondary enforcement layer for edge cases.

# Product catalog, keyed by globally-unique Stripe Product ID (live mode). The checkout endpoint
# only sells a product listed here; the webhook maps the sold product back to the Keygen policy its
# license mints under. Prices are deliberately NOT listed — the server reads each product's current
# `default_price` live from Stripe, so a price change in the Dashboard needs no code change/redeploy.
# `mode` (subscription vs payment) is likewise derived from whether that price is recurring.
# Each product's Keygen policy id comes from env; a blank policy → the sale is recorded pending
# (never dropped). Test mode uses different prod_ ids — override the whole table via
# SYXX_STRIPE_PRODUCTS_JSON ({"prod_…": {"label": "...", "policy_env": "SYXX_KEYGEN_POLICY_…"}}).
# A079: KEYGEN_ADMIN_TOKEN is held as Python str (immutable, not clearable from memory).
# A core dump or swap page will contain the raw bearer token.  See module-level SECURITY NOTE.
KEYGEN_ADMIN_TOKEN = _s("SYXX_KEYGEN_ADMIN_TOKEN", "")  # admin bearer token that mints licenses

# G-5: Operator admin token for support tooling (license lookup, key resend, etc.).
# If blank, admin endpoints are disabled. Set via SYXX_ADMIN_TOKEN env var.
# A079: ADMIN_TOKEN has the same immutable-str risk — see module-level SECURITY NOTE.
ADMIN_TOKEN = _s("SYXX_ADMIN_TOKEN", "")

# Keygen webhook + API key sync
# A079: KEYGEN_WEBHOOK_SECRET is held as Python str (immutable, not clearable).
# See module-level SECURITY NOTE for core-dump and swap mitigations.
KEYGEN_WEBHOOK_SECRET = _s("SYXX_KEYGEN_WEBHOOK_SECRET", "")  # HMAC secret for license webhooks
# SEC-048: Validate Keygen webhook secret length. HMAC secrets should be at
# least 16 characters to provide adequate entropy against brute-force attacks.
if KEYGEN_WEBHOOK_SECRET and len(KEYGEN_WEBHOOK_SECRET) < 16:
    import logging
    logging.getLogger("syxx-server").warning(
        "config_keygen_webhook_too_short: SYXX_KEYGEN_WEBHOOK_SECRET is %d chars "
        "(expected >=16); HMAC verification may accept forged events. "
        "Check your environment configuration.",
        len(KEYGEN_WEBHOOK_SECRET),
    )
API_KEY_SYNC_INTERVAL_HOURS = _i("SYXX_API_KEY_SYNC_INTERVAL_HOURS", 24)  # daily sweep

# How many machines one purchased license may be activated on. Stamped onto every minted
# license as Keygen's `maxMachines` attribute (fulfillment._keygen_create_license).
#
# 5 IS NOT AN ARBITRARY NUMBER. It is the figure already promised in the published EULA at
# Server/index.html:513 ("up to five devices they own or control"). A different number here
# would mean the product enforces something the customer was never told, so this constant and
# that sentence must be changed together or not at all.
#
# WHAT THIS DOES AND DOES NOT DO — read before trusting it:
#   * It sets the per-license ceiling Keygen will enforce ONCE machines are activated against
#     the license. Keygen honours a per-license `maxMachines` only when the license's POLICY
#     permits an override; on a policy that does not, the attribute is ignored and the POLICY's
#     own limit applies. That policy is configured in the Keygen dashboard, not in this repo,
#     so this server CANNOT verify the limit is live.
#   * Nothing counts machines yet. Neither client activates a machine against its license
#     (deliberately — macOS/Sources/SyxxKit/LicenseManager.swift:395-397 documents why a
#     fingerprint-scoped validation would read a fresh key as invalid), so no license has any
#     machines to exceed a limit with. Until the client-side activation call lands, this
#     ceiling is DECLARED, not ENFORCED, and must not be described as enforcement.
# 0 disables the attribute entirely (mint exactly as before).
KEYGEN_MAX_MACHINES = max(0, _i("SYXX_KEYGEN_MAX_MACHINES", 5))


# ═══════════════════════════════════════════════════════════════════════════════
# Product Catalog (Stripe SKUs)
# ═══════════════════════════════════════════════════════════════════════════════
#
# This section defines the Syxx product catalog — the mapping from internal SKU
# keys to Stripe product and price IDs. It is kept in config.py (rather than a
# separate catalog.py module) because the catalog is read by stripe_checkout,
# fulfillment, and keygen_policy modules that already import from config; moving
# it would create import churn with no architectural benefit.
#
# Structure:
#   STRIPE_SKUS: dict[str, dict] — keyed by SKU string (e.g. "syxx_mac_monthly_sub")
#     Each entry contains:
#       product_default  — Stripe product ID (compiled-in default)
#       price_default    — Stripe price ID (compiled-in default; blank = unsellable)
#       label            — Human-readable product description
#       policy_env       — Env var for Keygen policy name override
#       prodid_env       — Env var for Stripe product ID override
#       priceid_env      — Env var for Stripe price ID override
#
#   Helper functions:
#     stripe_product_for(sku) → str — resolved product ID (env override or default)
#     stripe_price_for(sku)   → str — resolved price ID (env override or default)
#
# ═══════════════════════════════════════════════════════════════════════════════

# WHY THIS IS KEYED BY SKU AND CARRIES AN EXPLICIT PRICE ID (changed 2026-07-16)
# ------------------------------------------------------------------------------
# It used to be keyed by Stripe PRODUCT id and resolved the product's `default_price` live. That
# works only while each product has exactly ONE price. The catalog now sells tiers as multiple
# PRICES on one product per platform (macOS / Windows / Debian), because a tier does not need its
# own product. Under that catalog `default_price` is whichever tier Stripe happens to consider
# default, so a "yearly" button would have silently charged the monthly price — a wrong charge that
# looks completely successful. The price id is therefore explicit and per-SKU.
#
# The tier CANNOT come from Stripe product metadata: every price on a product shares it. Stripe
# PRICE objects do have their own metadata, but the mapping stays here rather than in the Dashboard
# because it decides WHICH ENTITLEMENT a payment mints — a Dashboard typo must not be able to grant
# a perpetual license for a monthly charge. Licensing fails closed everywhere else (D040/A021); the
# catalog behaves the same way. Price EDITS still need no redeploy: price ids are stable.
#
# Perpetual SKUs are deliberately ABSENT. They were `prod_Uq67AE9DqkN3qA` / `prod_Uq6S3W3QPlvMAy`,
# which no longer exist in the Stripe account — a listed-but-dead SKU raises on the product fetch,
# so the buy button was broken, not merely unavailable. Selling a perpetual license is also a
# promise to keep this server running indefinitely; that is a commercial decision to make later,
# not a dead entry to leave lying in an allow-list.
#
# `price` is BLANK until the price ids exist in Stripe. A blank price is unsellable and says so
# (see stripe_checkout.create_checkout_session) — it never falls back to default_price, because
# falling back is exactly the wrong-charge bug this table exists to prevent.
# Every id is env-overridable so a test-mode catalog needs no code change. The live ids are the
# DEFAULTS rather than required settings: a deploy that sets no Stripe variables still sells the real
# products, and a blank override can never silently empty the catalog.
#
# NOTE the product id is the SAME for a platform's monthly and yearly SKUs — they are two prices on
# ONE product. That is precisely why the product id cannot identify what someone bought, and why the
# price id is the load-bearing field here.
# The table holds only what is FIXED at build time — which env var to read, and the compiled-in
# default product id. The ids themselves are resolved from the environment on every call, exactly
# like keygen_policy_for(). Reading them at import instead would mean a value could only be changed
# by reimporting the module, which is both a bad operator story and untestable without
# importlib.reload — and reloading config regenerates SESSION_SECRET, silently invalidating every
# live session. Config that is read live is config that can be tested and corrected.
def _sku(suffix: str, product_default: str, price_default: str, label: str) -> dict:
    """Build a catalog entry mapping an SKU suffix to its Stripe IDs and env-var names.

    Returns a dict with compiled-in defaults (product_default, price_default, label)
    and the env-var names (policy_env, prodid_env, priceid_env) used for runtime overrides.
    The actual values are resolved live by stripe_product_for() / stripe_price_for().
    """
    return {
        "product_default": product_default,
        "price_default": price_default,
        "label": label,
        "policy_env": "SYXX_KEYGEN_POLICY_" + suffix,
        "prodid_env": "SYXX_STRIPE_PRODID_" + suffix,
        "priceid_env": "SYXX_STRIPE_PRICEID_" + suffix,
    }


# One product per PLATFORM; the tier is the PRICE. Note _MAC appears twice, _WIN twice, _DEB twice
# — that is the point, and it is why the product id cannot identify a purchase.
_MAC, _WIN, _DEB = "prod_Uq652gww87MGYT", "prod_Uq6ODZbCgLWfIU", "prod_Utkr2kcmWzMNNq"

#: Product catalog — maps SKU keys to Stripe product/price IDs and env-var overrides.
#: Six entries: {mac, win, deb} × {monthly, yearly}. Price ID is the load-bearing field
#: (monthly and yearly share a product ID; only the price distinguishes tiers).
STRIPE_SKUS: dict[str, dict] = {
    "syxx_mac_monthly_sub": _sku("MAC_MONTHLY", _MAC, "price_1TqPt10Owf1ALdZDpXWZqB2r", "Syxx (macOS), Monthly"),
    "syxx_mac_yearly_sub": _sku("MAC_YEARLY", _MAC, "price_1Ttwpw0Owf1ALdZDhfZ2LTlz", "Syxx (macOS), Yearly"),
    "syxx_win_monthly_sub": _sku("WIN_MONTHLY", _WIN, "price_1TqQBk0Owf1ALdZDKVoRroPD", "Syxx (Windows), Monthly"),
    "syxx_win_yearly_sub": _sku("WIN_YEARLY", _WIN, "price_1TtxCb0Owf1ALdZDwybXuuz9", "Syxx (Windows), Yearly"),
    "syxx_deb_monthly_sub": _sku("DEB_MONTHLY", _DEB, "price_1TtxLd0Owf1ALdZDD175vTFK", "Syxx (Debian/Mint), Monthly"),
    "syxx_deb_yearly_sub": _sku("DEB_YEARLY", _DEB, "price_1TtxNs0Owf1ALdZDOT0EMfce", "Syxx (Debian/Mint), Yearly"),
}


def stripe_product_for(sku):
    """Stripe product id for `sku`. The live id is the DEFAULT, so a deploy that sets nothing still
    sells the real product and a blank override cannot silently empty the catalog."""
    entry = STRIPE_SKUS.get(sku)
    if not entry:
        return ""
    return os.environ.get(entry["prodid_env"]) or entry["product_default"]


def stripe_price_for(sku):
    """Stripe price id for `sku`, or '' if there is none (→ not sellable; checkout refuses it).

    THIS is the field that says which tier to charge. A platform's monthly and yearly SKUs share one
    product id, so the product cannot identify the tier — resolving a price from the product (its
    `default_price`) charges whichever tier Stripe calls default, for every button.
    """
    entry = STRIPE_SKUS.get(sku)
    if not entry:
        return ""
    return os.environ.get(entry["priceid_env"]) or entry["price_default"]


def validate_stripe_config() -> None:
    """Warn when compiled-in Stripe ID defaults are active in production.

    E050: hardcoded Stripe product/price IDs reveal internal structure.  In
    production every SKU should be overridden via its env var so the compiled-in
    defaults never reach a live Stripe API call.  In development/staging the
    defaults are intentional and this function is a no-op.

    Call this once at server startup (e.g. from app.py or the WSGI entry point).
    """
    if SYXX_ENV != "production":
        return
    import logging as _logging
    _log = _logging.getLogger(__name__)
    for sku, entry in STRIPE_SKUS.items():
        if not os.environ.get(entry["prodid_env"]):
            _log.warning(
                "E050: Stripe product ID for %s uses compiled-in default %s "
                "— set %s in production", sku, entry["product_default"], entry["prodid_env"]
            )
        if entry["price_default"] and not os.environ.get(entry["priceid_env"]):
            _log.warning(
                "E050: Stripe price ID for %s uses compiled-in default %s "
                "— set %s in production", sku, entry["price_default"], entry["priceid_env"]
            )


# Checkout sessions created BEFORE the SKU refactor stamped `metadata[product]`, not
# `metadata[sku]`. A session in flight across the deploy would otherwise resolve to no policy and
# record the sale pending — a real buyer, charged, waiting on a manual mint. Map the two products
# that were live to their monthly SKU for that window. Delete once no pre-refactor session can
# still complete (Checkout sessions expire after 24h).
_LEGACY_PRODUCT_TO_SKU = {
    "prod_Uq652gww87MGYT": "syxx_mac_monthly_sub",
    "prod_Uq6ODZbCgLWfIU": "syxx_win_monthly_sub",
}

# --- Policies with NO Stripe SKU behind them -------------------------------------------------
# These exist in Keygen and are minted WITHOUT a payment event, so nothing in the Stripe catalog
# points at them. They are kept in named tables (rather than loose env vars) so the deploy template
# and the code cannot drift: an env var nothing references is silently dead, which is precisely what
# happened to SYXX_KEYGEN_POLICY_{MAC,WIN}_TRIAL on the 2026-07 production host. test_payments.py
# pins these tables against scripts/deploy.sh.
PLATFORMS = ("macos", "windows", "debian")

# Trials are NOT sold through Stripe — there is no payment to map back from. That means NOTHING
# rate-limits them except machine identity: see MachineFingerprint. A host whose fingerprint is
# InstallScoped rather than Hardware (VMs, containers, any Linux box without the DMI capture helper)
# can mint a fresh trial per reinstall, forever. The issuing endpoint must therefore treat a
# non-node-lockable fingerprint as a REFUSAL, not a warning — an unlimited free tier is the
# commercial equivalent of the false-clean result this codebase refuses everywhere else.
TRIAL_POLICY_ENVS = {
    "macos": "SYXX_KEYGEN_POLICY_MAC_TRIAL",
    "windows": "SYXX_KEYGEN_POLICY_WIN_TRIAL",
    "debian": "SYXX_KEYGEN_POLICY_DEB_TRIAL",
}

# Perpetual licenses are deliberately NOT listed on Stripe (2026-07-16): selling one is a promise to
# keep this server running indefinitely, and that promise should not be made before the product has
# proven itself. The policies still exist so a perpetual license can be minted BY HAND (support,
# comps, early backers) without a code change if that decision is revisited.
PERM_POLICY_ENVS = {
    "macos": "SYXX_KEYGEN_POLICY_MAC_PERM",
    "windows": "SYXX_KEYGEN_POLICY_WIN_PERM",
    "debian": "SYXX_KEYGEN_POLICY_DEB_PERM",
}


def keygen_policy_for(sku):
    """The Keygen policy id a given SKU's license mints under, or '' if that SKU's policy env var
    is unset (→ webhook records the sale pending rather than minting)."""
    entry = STRIPE_SKUS.get(sku)
    return os.environ.get(entry["policy_env"], "") if entry else ""


def sku_for_legacy_product(product_id):
    """SKU for a pre-refactor session that stamped metadata[product]. '' if unrecognized."""
    return _LEGACY_PRODUCT_TO_SKU.get(product_id, "")


def trial_policy_for(platform):
    """Keygen policy id for a trial on `platform`, or '' if unset (→ trials are not issuable)."""
    env = TRIAL_POLICY_ENVS.get(platform)
    return os.environ.get(env, "") if env else ""


def perm_policy_for(platform):
    """Keygen policy id for a perpetual license on `platform`, or '' if unset. Not reachable from
    checkout by design — perpetual licenses are minted by hand, never sold through Stripe."""
    env = PERM_POLICY_ENVS.get(platform)
    return os.environ.get(env, "") if env else ""


# --- HTTP / DNS listeners --------------------------------------------------
# SEC-014: Default to loopback — production MUST set SYXX_HTTP_HOST explicitly
# (typically via systemd unit or deploy.sh). Binding 0.0.0.0 by default exposes
# the server to the network before the reverse proxy is configured.
HTTP_HOST = _s("SYXX_HTTP_HOST", "127.0.0.1")
HTTP_PORT = _i("SYXX_HTTP_PORT", 8080)
HTTP_REQUEST_TIMEOUT = _i("SYXX_HTTP_REQUEST_TIMEOUT", 30)
# E026: 0.0.0.0 is intentional for the authoritative DNS server — it must
# receive queries on every network interface to serve as a public resolver.
# Override via SYXX_DNS_HOST to restrict binding (e.g. 127.0.0.1 for testing).
DNS_HOST = _s("SYXX_DNS_HOST", "0.0.0.0")
DNS_PORT = _i("SYXX_DNS_PORT", 53)
DNS_FALLBACK_PORT = _i("SYXX_DNS_FALLBACK_PORT", 5053)
# A066: hard ceiling on wall-clock time for a single authoritative walk (seconds).
# Prevents a chain of slow hops from pinning a handler thread indefinitely.
AUTH_WALK_DEADLINE_SECONDS = _i("SYXX_AUTH_WALK_DEADLINE", 30)

# Optional built-in TLS (a reverse proxy is preferred; see scripts/deploy.sh).
TLS_CERT = _s("SYXX_TLS_CERT", "")
TLS_KEY = _s("SYXX_TLS_KEY", "")

# --- Licensing / entitlement ----------------------------------------------
# Keygen account the app already validates against (SyxxConfig.accountID). When
# set, license keys are validated live against Keygen; leave empty to rely only
# on the offline allowlist below.
KEYGEN_ACCOUNT_ID = _s("SYXX_KEYGEN_ACCOUNT_ID", "4b50cc83-1664-4626-99b1-fbd4e76e35c3")
KEYGEN_TIMEOUT = _i("SYXX_KEYGEN_TIMEOUT", 8)
KEYGEN_BASE_URL = _s("SYXX_KEYGEN_BASE_URL", "")  # SRV-023: mock server override
KEYGEN_MAX_WORKERS = _i("SYXX_KEYGEN_MAX_WORKERS", 4)  # SRV-019: concurrent validation cap

# E009: HTTPS enforcement for Keygen URLs. A non-HTTPS base URL sends license
# keys and admin tokens in cleartext. Warn always; refuse to start unless the
# operator has explicitly opted in via SYXX_INSECURE_KEYGEN_URL=1.
if KEYGEN_BASE_URL and not KEYGEN_BASE_URL.startswith("https://"):
    import logging as _logging
    _log = _logging.getLogger("syxx-server")
    _log.warning(
        "E009: KEYGEN_BASE_URL (%s) is not HTTPS — license keys and admin "
        "tokens will be sent in cleartext. Set SYXX_INSECURE_KEYGEN_URL=1 "
        "to allow this (development/testing only).",
        KEYGEN_BASE_URL,
    )
    if not _b("SYXX_INSECURE_KEYGEN_URL", False):
        raise RuntimeError(
            f"KEYGEN_BASE_URL ({KEYGEN_BASE_URL}) is not HTTPS. "
            "Set SYXX_INSECURE_KEYGEN_URL=1 to override (dev/test only)."
        )
# Ed25519 public key (hex-encoded) from the Keygen account settings page. When set,
# every validate-key response is cryptographically verified before trusting
# meta.valid — a MITM with a forged body is denied even over valid TLS (A021 Fix 1).
# The same key can be found at /v1/accounts/{id}/public-key.
# Leave empty to skip signature verification (dev/test environments).
KEYGEN_PUBLIC_KEY = _s("SYXX_KEYGEN_PUBLIC_KEY", "")

# Offline / self-issued license allowlist (comma separated). Useful for testing
# or air-gapped deploys. Admin keys added to the DB are honored too.
OFFLINE_LICENSE_KEYS = _list("SYXX_LICENSE_KEYS", [])

# Session token lifetime (seconds) minted after a license validates.
SESSION_TTL = _i("SYXX_SESSION_TTL", 3600)

# --- Data retention (purge windows in days; 0 disables that purge) ----------
# Nothing collected here is meant to live forever. A background job deletes rows
# past these windows; expired leak tokens + observations are always purged.
AUDIT_RETENTION_DAYS = _i("SYXX_AUDIT_RETENTION_DAYS", 7)
PASSIVE_RETENTION_DAYS = _i("SYXX_PASSIVE_RETENTION_DAYS", 30)
TRIAL_ANCHOR_RETENTION_DAYS = _i("SYXX_TRIAL_ANCHOR_RETENTION_DAYS", 365)
TRIAL_ANCHOR_MAX_ROWS = _i("SYXX_TRIAL_ANCHOR_MAX_ROWS", 100000)
PURGE_INTERVAL_SECONDS = _i("SYXX_PURGE_INTERVAL_SECONDS", 3600)

# Paywall enforcement. Web (/api/*) endpoints are ALWAYS enforced so the website
# can never be a free bypass. Native endpoints (/warp, /leak, /fetch_script) are
# enforced by default; an operator may open a compatibility grace window by
# setting SYXX_ENFORCE_NATIVE=false (see README).
ENFORCE_WEB = True
ENFORCE_NATIVE = _b("SYXX_ENFORCE_NATIVE", True)  # A131: fail-closed default

# --- Leak collector --------------------------------------------------------
# Authoritative zone(s) whose leak tokens this server records. Both the syxx.app
# and zzyn.net brands are accepted so no client path is silently ignored.
LEAK_ZONES = _list("SYXX_LEAK_ZONES", ["leak.syxx.app", "leak.zzyn.net"])
LEAK_SINK_A = _s("SYXX_LEAK_SINK_A", "198.51.100.1")
LEAK_SINK_AAAA = _s("SYXX_LEAK_SINK_AAAA", "::1")
LEAK_TOKEN_TTL = _i("SYXX_LEAK_TOKEN_TTL", 900)
DNS_SOA_MNAME = _s("SYXX_DNS_SOA_MNAME", "ns1.syxx.app")
DNS_SOA_RNAME = _s("SYXX_DNS_SOA_RNAME", "hostmaster.syxx.app")

# --- SSRF-safe fetcher ------------------------------------------------------
FETCH_TIMEOUT = _i("SYXX_FETCH_TIMEOUT", 5)
FETCH_SCRIPT_TIMEOUT = _i("SYXX_FETCH_SCRIPT_TIMEOUT", 3)
FETCH_MAX_BYTES = _i("SYXX_FETCH_MAX_BYTES", 5 * 1024 * 1024)
FETCH_MAX_SCRIPTS = _i("SYXX_FETCH_MAX_SCRIPTS", 40)
FETCH_MAX_REDIRECTS = _i("SYXX_FETCH_MAX_REDIRECTS", 3)
# SEC-013: SYXX_FETCH_ALLOW_PRIVATE is gated on SYXX_DEV_MODE. Private-network
# fetches (127.0.0.1, 10.0.0.0/8, etc.) are an SSRF escalation path; they are
# only permitted when the operator has explicitly opted into dev mode.
DEV_MODE = _b("SYXX_DEV_MODE", False)
FETCH_ALLOW_PRIVATE = _b("SYXX_FETCH_ALLOW_PRIVATE") and DEV_MODE  # test escape hatch
WARP_CACHE_TTL = _i("SYXX_WARP_CACHE_TTL", 300)
# A041: aggregate wall-clock budget for build_baseline when called from diff.py.
# Prevents the baseline builder from pinning the /api/diff handler with unbounded
# script fetches. The caller passes time.monotonic() + WARP_BUILD_DEADLINE.
WARP_BUILD_DEADLINE = _i("SYXX_WARP_BUILD_DEADLINE", 15)
# Per-run unique-host cap for the Warp raw-concatenation assembler. Discovery pulls
# a page's declared JS/CSS subresources, which can point at arbitrary hosts; this
# bounds how many DISTINCT hosts a single assembly run may contact (SSRF surface).
# The target host counts as the first. An asset on a host beyond the cap is treated
# as a fetch failure (honest DEGRADED), never silently dropped.
FETCH_MAX_UNIQUE_HOSTS = _i("SYXX_FETCH_MAX_UNIQUE_HOSTS", 20)

# --- WARP_MANIFEST_V1 signed-manifest content-integrity protocol ------------
# (docs/warp-manifest-v1-spec.md). The server is the ONLY parser: it resolves the
# transitive HTML+JS+CSS+CSS@import closure and hashes each asset's raw bytes, then
# signs the manifest. These bound the enumeration so a hostile page cannot make the
# closure walk unbounded work or contact unbounded hosts.
#
# WARP_MANIFEST_MAX_ASSETS: hard ceiling on the number of distinct assets enumerated
# into one manifest (JS + CSS + CSS @import combined; the HTML document is separate).
# Assets beyond the cap are simply not enumerated — a declared boundary, exactly like
# the unique-host cap, never a silent drop that would read as clean.
WARP_MANIFEST_MAX_ASSETS = _i("SYXX_WARP_MANIFEST_MAX_ASSETS", 200)
# WARP_CSS_IMPORT_MAX_DEPTH: how deep the CSS `@import` chain is followed. A stylesheet
# can @import another, which can @import another; this bounds the recursion so an
# import cycle or a deliberately deep chain cannot pin the fetch fan-out. Depth 0 is the
# `<link rel=stylesheet>` itself; each @import hop adds one.
WARP_CSS_IMPORT_MAX_DEPTH = _i("SYXX_WARP_CSS_IMPORT_MAX_DEPTH", 5)
# WARP_MANIFEST_FRESHNESS: the freshness_seconds the manifest declares (spec §3/§4.5).
# The client rejects a manifest older than this. 300s matches the WitnessSigner window.
# A009: server-side clamp at warp._MAX_FRESHNESS_SECONDS (600 s / 10 min). Values
# above that ceiling are accepted here but clamped before emission, so setting
# SYXX_WARP_MANIFEST_FRESHNESS=3600 still produces freshness_seconds=600 in the manifest.
WARP_MANIFEST_FRESHNESS = _i("SYXX_WARP_MANIFEST_FRESHNESS", 300)

# A008 — Wall-clock dependency design-decision record.
# The manifest ``timestamp`` field (warp.py build_manifest) uses time.time()
# (wall-clock / UTC epoch milliseconds). This is intentional and unavoidable:
# the value must be comparable across hosts — the client checks freshness
# against the HTTP Date header and its own system clock, both wall-clock.
# A monotonic clock (time.monotonic()) is per-process and anchored to an
# arbitrary boot epoch, so it cannot be compared between server and client.
# Clock skew between server and client (typically <1 s with NTP) is absorbed
# by the freshness tolerance window (default 300 s above). Operator
# mitigation: keep chrony or systemd-timesyncd running on the server.
# This constant exists so the design decision is grep-able from config.
WARP_MANIFEST_CLOCK_NOTE = (
    "manifest timestamp uses wall-clock time.time(); "
    "cross-host comparison requires wall-clock, not monotonic; "
    "clock skew is bounded by NTP drift and freshness_seconds headroom"
)

# --- Investigation ---------------------------------------------------------
# UDP/53 vantage points (skipped automatically when a network blocks port 53).
PUBLIC_RESOLVERS = _list(
    "SYXX_PUBLIC_RESOLVERS",
    ["1.1.1.1", "8.8.8.8", "9.9.9.9"],
)

# --- Propagation checker ---------------------------------------------------
# Extended resolver list with geographic metadata for /api/propagation.
PROPAGATION_RESOLVERS = [
    {"ip": "1.1.1.1", "label": "Cloudflare", "region": "Global (Anycast)"},
    {"ip": "8.8.8.8", "label": "Google", "region": "Global (Anycast)"},
    {"ip": "9.9.9.9", "label": "Quad9", "region": "Global (Anycast)"},
    {"ip": "208.67.222.222", "label": "OpenDNS", "region": "Global (Anycast)"},
    {"ip": "64.6.64.6", "label": "Verisign", "region": "US"},
    {"ip": "156.154.70.1", "label": "Neustar", "region": "US"},
    {"ip": "199.85.126.10", "label": "Norton", "region": "Global (Anycast)"},
    {"ip": "185.228.168.9", "label": "CleanBrowsing", "region": "EU"},
    {"ip": "94.140.14.14", "label": "AdGuard", "region": "Global (Anycast)"},
    {"ip": "76.76.19.19", "label": "Alternate DNS", "region": "US"},
    {"ip": "101.6.6.6", "label": "DNS.SB", "region": "Asia"},
    {"ip": "119.29.29.29", "label": "DNSPod", "region": "China"},
    {"ip": "223.5.5.5", "label": "AliDNS", "region": "China"},
    {"ip": "168.126.63.1", "label": "KT (Korea Telecom)", "region": "South Korea"},
    {"ip": "203.248.252.2", "label": "LG U+", "region": "South Korea"},
    {"ip": "196.1.114.50", "label": "Afrihost", "region": "Africa"},
    {"ip": "200.221.11.100", "label": "UOL", "region": "Brazil"},
]

# --- DoT listener (encrypted-transports D-2) -------------------------------
# Off until a cert + firewall are staged. Authoritative-leak-only by default; recursive forwarding
# only for explicitly allowlisted source IPs, so :853 is never an open forwarder.
ENABLE_DOT = _b("SYXX_ENABLE_DOT")
# E026: same rationale as DNS_HOST — DoT must listen on all interfaces.
DOT_HOST = _s("SYXX_DOT_HOST", "0.0.0.0")
DOT_PORT = _i("SYXX_DOT_PORT", 853)
DOT_MAX_CONN = _i("SYXX_DOT_MAX_CONN", 128)  # SD-51 cap for the DoT path
DOT_MAX_QUERIES_PER_CONN = _i("SYXX_DOT_MAX_QUERIES_PER_CONN", 100)
DOT_READ_TIMEOUT = _i("SYXX_DOT_READ_TIMEOUT", 10)
DOT_FORWARD_ALLOWLIST = _list("SYXX_DOT_FORWARD_ALLOWLIST", [])
DOT_TLS_CERT = _s("SYXX_DOT_TLS_CERT", "")  # app-terminated TLS (honest attestation)
DOT_TLS_KEY = _s("SYXX_DOT_TLS_KEY", "")

# --- DoH forwarder (encrypted-transports D-1) ------------------------------
# The /dns-query route is a PAYWALLED forwarder (entitlement enforced), not an open resolver:
# an open DoH forwarder on a public host is an abuse magnet. ENFORCE_DOH stays True in production.
ENFORCE_DOH = _b("SYXX_ENFORCE_DOH", True)  # A131: fail-closed default
# Optional second gate: if non-empty, only these client IPs may use the forwarder even when
# entitled. Empty = any entitled caller. (The rate limiter still applies either way.)
DOH_ALLOWLIST = _list("SYXX_DOH_ALLOWLIST", [])
DOH_UPSTREAM_TIMEOUT = _i("SYXX_DOH_UPSTREAM_TIMEOUT", 5)
INVESTIGATE_TIMEOUT = _i("SYXX_INVESTIGATE_TIMEOUT", 3)

# DoH vantage points, pinned to resolver IPs so they work when UDP/53 is blocked
# (e.g. NextDNS) and even when the DoH hostname cannot be resolved locally.
# The host's own resolver (getaddrinfo). On networks that block UDP/53 AND
# direct connections to public-resolver IPs (e.g. NextDNS), this is the only
# vantage guaranteed to work, since it is the DNS path the machine itself uses.
ENABLE_SYSTEM_VANTAGE = _b("SYXX_ENABLE_SYSTEM_VANTAGE")

ENABLE_DOH = _b("SYXX_ENABLE_DOH")
DOH_VANTAGES = [
    {"label": "cloudflare-doh", "ip": "1.1.1.1", "sni": "cloudflare-dns.com", "path": "/dns-query"},
    {"label": "google-doh", "ip": "8.8.8.8", "sni": "dns.google", "path": "/resolve"},
    {"label": "quad9-doh", "ip": "9.9.9.9", "sni": "dns.quad9.net", "path": "/dns-query"},
]
DOH_TIMEOUT = _i("SYXX_DOH_TIMEOUT", 5)

# --- Outbound email (beta confirmations + weekly digest) --------------------
# SMTP is OPTIONAL. If SYXX_SMTP_HOST is empty, all mail is a logged no-op — the
# server never requires mail to run and a mail failure never breaks a request.
# Credentials come from the environment at deploy time; nothing is hardcoded.
SMTP_HOST = _s("SYXX_SMTP_HOST", "")
SMTP_PORT = _i("SYXX_SMTP_PORT", 587)
SMTP_USER = _s("SYXX_SMTP_USER", "")
# A079: SMTP_PASSWORD is held as Python str (immutable, not clearable from memory).
# A core dump or unencrypted swap page will contain the raw password in cleartext.
# Production mitigation: disable core dumps (sysctl kernel.core_pattern='|/bin/false'),
# use encrypted swap, or migrate to a secrets manager fetched via subprocess.
# See module-level SECURITY NOTE.
SMTP_PASSWORD = _s("SYXX_SMTP_PASSWORD", "")
SMTP_STARTTLS = _b("SYXX_SMTP_STARTTLS")  # True: STARTTLS on 587; False: implicit TLS on 465
SMTP_TIMEOUT = _i("SYXX_SMTP_TIMEOUT", 15)  # SRV-032: configurable SMTP timeout
SMTP_INSECURE_CERTS = _b("SYXX_SMTP_INSECURE_CERTS")  # SRV-033: allow self-signed certs
MAIL_FROM = _s("SYXX_MAIL_FROM", "Syxx <noreply@syxx.app>")

# Public base URL for absolute links/images in emails (email clients need absolute
# src, not relative). No trailing slash.
PUBLIC_BASE_URL = _s("SYXX_PUBLIC_BASE_URL", "https://syxx.app").rstrip("/")

# Send each beta subscriber a confirmation email on signup.
BETA_CONFIRM = _b("SYXX_BETA_CONFIRM")
# Weekly CSV digest of all signups, emailed to the operator. Empty recipient
# disables the digest (kept out of source so no personal address is committed).
BETA_DIGEST_TO = _s("SYXX_BETA_DIGEST_TO", "")
BETA_DIGEST_INTERVAL_DAYS = _i("SYXX_BETA_DIGEST_INTERVAL_DAYS", 7)

# --- CORS (S1: replace wildcard with allowlisted origin echo) ---------------
# Comma-separated origins allowed to make cross-origin requests. The server echoes
# the request Origin back ONLY when it matches this list; otherwise no ACAO header
# is sent (the browser blocks the response). Never "*".
CORS_ALLOWED_ORIGINS = _list(
    "SYXX_CORS_ALLOWED_ORIGINS",
    [
        "https://syxx.app",
        "https://www.syxx.app",
        "https://leak.syxx.app",
        "https://leaktest.syxx.app",
        "https://macmotd.syxx.app",
        "https://winmotd.syxx.app",
        "https://debmotd.syxx.app",
    ],
)

# --- Metrics endpoint auth (S4) ---------------------------------------------
# Bearer token required to access /metrics. Empty string disables auth (dev only).
METRICS_BEARER_TOKEN = _s("SYXX_METRICS_BEARER_TOKEN", "")

# --- Rate limiting ----------------------------------------------------------
# Token bucket (integer millitokens): RATE_CAPACITY requests, refilled
# RATE_REFILL_MILLIS per second, per subject (license/session) and, for
# unauthenticated public routes, per IP. Integer math avoids float drift (SRV-046).
RATE_CAPACITY = _i("SYXX_RATE_CAPACITY", 30)
RATE_REFILL_MILLIS = _i("SYXX_RATE_REFILL_MILLIS", 1000)  # token/second in millitokens

# Per-subject rate limit for /api/exposure (S5): 5 requests per minute.
EXPOSURE_RATE_CAPACITY = _i("SYXX_EXPOSURE_RATE_CAPACITY", 5)
EXPOSURE_RATE_REFILL_MILLIS = _i("SYXX_EXPOSURE_RATE_REFILL_MILLIS", 12000)  # 1 per 12s = 5/min

# --- Server ----------------------------------------------------------------
SERVER_NAME = "SyxxServer/2.0"

# --- OpenTelemetry (OTel/OTLP) --------------------------------------------
# E051: OTLP collector endpoint. Configurable via env so the destination can
# change without a code change or redeploy. The compiled-in default points at
# the production bugsmasher collector; set SYXX_OTLP_ENDPOINT in any env where
# telemetry must go elsewhere (staging, self-hosted, off).
OTLP_ENDPOINT = _s("SYXX_OTLP_ENDPOINT", "https://bugsmasher.syxx.app")

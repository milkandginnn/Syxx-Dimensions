"""Account, billing, and subscription route methods — AccountRoutes mixin."""

import hashlib
import hmac
import json
import os
import threading
import time
import urllib.parse
import uuid

from .. import (
    config,
    db,
    fulfillment,
    investigate,
    licensing,
    logging_util,
    mailer,
    ratelimit,
    stripe_checkout,
)
from .base import (
    _HEX_RE,
    _UUID_RE,
    _paginate,
    _send_beta_confirmation,
    _valid_email,
)


class AccountRoutes:
    """Account routes: activate, checkout, billing_portal, webhooks, API keys, monitors, beta."""

    # SEC-021 / A012: Account lockout tracking persisted to the SQLite kv table
    _ACTIVATE_LOCKOUT_THRESHOLD = 10  # failures before lockout
    _ACTIVATE_LOCKOUT_WINDOW = 900  # 15 minutes
    _ACTIVATE_BACKOFF_START = 3    # exponential backoff begins after this many failures
    _ACTIVATE_BACKOFF_CAP = 30     # seconds

    # SEC-006: Per-IP daily limit for trial anchor creation.
    _TRIAL_ANCHOR_IP_COUNTS = {}  # ip -> (count, day_start_time)
    _TRIAL_ANCHOR_IP_DAILY_LIMIT = 5  # max anchors per IP per day
    _TRIAL_ANCHOR_IP_LOCK = threading.Lock()
    # A040: cap the IP tracker size to prevent unbounded memory growth.
    _TRIAL_ANCHOR_IP_MAX_ENTRIES = 10_000

    # A280: recognized check names that the scheduler's _run_one() dispatches.
    _VALID_CHECK_NAMES = frozenset({"investigate", "baseline", "warp"})

    @staticmethod
    def _activate_fail_key(ip: str) -> str:
        return f"activate_fail:{ip}"

    def _get_activate_failures(self, ip: str) -> int:
        """Return the current failure count for *ip*, or 0 if absent/expired."""
        val, created_at = db.kv_get_with_created_at(self._activate_fail_key(ip))
        if val is None:
            return 0
        if time.time() - created_at >= self._ACTIVATE_LOCKOUT_WINDOW:
            # Window expired — delete so the next insert gets a fresh created_at.
            self._delete_activate_fail(ip)
            return 0
        try:
            return json.loads(val)["count"]
        except (json.JSONDecodeError, KeyError, TypeError):
            return 0

    def _record_activate_failure(self, ip: str) -> None:
        """Increment the failure counter, resetting if the window has expired."""
        key = self._activate_fail_key(ip)
        val, created_at = db.kv_get_with_created_at(key)
        if val is not None and (time.time() - created_at >= self._ACTIVATE_LOCKOUT_WINDOW):
            # Window expired — delete so kv_set gets a fresh created_at.
            self._delete_activate_fail(ip)
            val = None

        if val is None:
            count = 1
        else:
            try:
                count = json.loads(val)["count"] + 1
            except (json.JSONDecodeError, KeyError, TypeError):
                count = 1

        db.kv_set(key, json.dumps({"count": count}))

    def _clear_activate_failures(self, ip: str) -> None:
        """Clear failure tracking on successful activation."""
        self._delete_activate_fail(ip)

    def _delete_activate_fail(self, ip: str) -> None:
        key = self._activate_fail_key(ip)
        conn = db._write_conn()
        try:
            conn.execute("DELETE FROM kv WHERE key=?", (key,))
            conn.commit()
        finally:
            db._return_conn(conn)

    def _route_activate(self):
        """SEC-021: Apply account lockout on repeated failed activation attempts.
        After N failed attempts from an IP within the lockout window, reject further
        attempts to prevent brute-force license key guessing."""
        # E027: reject cross-site requests to this mutation endpoint
        if not self._check_sec_fetch():
            return
        if not self._rate_ok(None):
            return
        if not self._require_json():
            return

        # SEC-021 + A012: Check lockout status before processing (kv-persisted)
        client_ip = self._client_ip()
        count = self._get_activate_failures(client_ip)
        # E022: Check lockout before applying the backoff delay — a locked-out IP should
        # receive 429 immediately without blocking a worker thread for the full delay.
        if count >= self._ACTIVATE_LOCKOUT_THRESHOLD:
            logging_util.warn(
                "activate_lockout",
                ip=client_ip[:15],
                failures=count,
            )
            return self._json_error(
                429,
                "too many failed activation attempts; try again later",
            )
        # E022: Progressive delay after lockout check (exponential backoff).
        # Skipped in dev/test mode (SYXX_DEV_MODE=1) to keep test suites fast.
        if count >= self._ACTIVATE_BACKOFF_START and not os.environ.get("SYXX_DEV_MODE"):
            delay = min(2 ** (count - self._ACTIVATE_BACKOFF_START), self._ACTIVATE_BACKOFF_CAP)
            time.sleep(delay)

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
        # A068: upper length bound — a very long key (up to the 1MB body cap) would
        # otherwise be constructed into the Keygen API POST verbatim.
        if len(key) > 256:
            return self._json_error(400, "license key too long")
        ent, err = licensing.validate_license(key)
        if err:
            return self._json_error(503, err)
        if not ent:
            # SEC-021 + A012: Track failed activation attempts (kv-persisted)
            self._record_activate_failure(client_ip)
            db.audit(None, "/api/auth/activate", 402, client_ip)
            return self._json_error(402, "invalid or inactive license key")
        # Success — clear failure counter (A012: delete kv entry)
        self._clear_activate_failures(client_ip)
        token, exp = licensing.mint_session(ent.subject, ent.plan)
        logging_util.incr("sessions_minted")
        db.audit(ent.subject, "/api/auth/activate", 200, client_ip)
        # E213: Session token delivered EXCLUSIVELY as httpOnly cookie — never in JSON body.
        self._send(
            200,
            {"plan": ent.plan, "expiresAt": exp},
            extra_headers={
                "Set-Cookie": f"syxx_session={token}; HttpOnly; Secure; SameSite=Strict; Path=/; Max-Age={config.SESSION_TTL}",
                "Cache-Control": "no-store",
            },
        )

    @classmethod
    def _prune_trial_anchor_ips(cls, day_start: int) -> None:
        """A040: Evict stale entries when the IP tracker exceeds its cap.

        Called inside _TRIAL_ANCHOR_IP_LOCK — must not re-acquire the lock.
        First pass removes entries from a prior day (they can never match again).
        If still over cap, removes the oldest remaining entries until under cap.
        """
        if len(cls._TRIAL_ANCHOR_IP_COUNTS) <= cls._TRIAL_ANCHOR_IP_MAX_ENTRIES:
            return
        # Pass 1: drop entries whose day_start is stale (before today).
        stale_keys = [
            ip
            for ip, (_, stored_day) in cls._TRIAL_ANCHOR_IP_COUNTS.items()
            if stored_day < day_start
        ]
        for ip in stale_keys:
            del cls._TRIAL_ANCHOR_IP_COUNTS[ip]
        # Pass 2: if still over cap, evict oldest entries (by stored day_start,
        # then by insertion order — dict ordering in Python 3.7+ is insertion order).
        if len(cls._TRIAL_ANCHOR_IP_COUNTS) > cls._TRIAL_ANCHOR_IP_MAX_ENTRIES:
            excess = len(cls._TRIAL_ANCHOR_IP_COUNTS) - cls._TRIAL_ANCHOR_IP_MAX_ENTRIES
            # Sort by stored day ascending so oldest days are removed first.
            to_remove = sorted(
                cls._TRIAL_ANCHOR_IP_COUNTS,
                key=lambda ip: cls._TRIAL_ANCHOR_IP_COUNTS[ip][1],
            )[:excess]
            for ip in to_remove:
                del cls._TRIAL_ANCHOR_IP_COUNTS[ip]

    def _check_trial_anchor_ip_limit(self, client_ip: str, day_start: int) -> bool:
        """Return True if the per-IP daily limit is exceeded.

        Acquires _TRIAL_ANCHOR_IP_LOCK, prunes stale entries, and resets the
        counter for a new day. Sends a 429 response and returns True when the
        limit is hit; returns False otherwise.
        """
        with self._TRIAL_ANCHOR_IP_LOCK:
            self._prune_trial_anchor_ips(day_start)
            entry = self._TRIAL_ANCHOR_IP_COUNTS.get(client_ip)
            if entry is not None:
                count, stored_day = entry
                if stored_day != day_start:
                    self._TRIAL_ANCHOR_IP_COUNTS[client_ip] = (0, day_start)
                elif count >= self._TRIAL_ANCHOR_IP_DAILY_LIMIT:
                    logging_util.warn(
                        "trial_anchor_ip_limit",
                        ip=client_ip[:15],
                        count=count,
                    )
                    self._json_error(429, "trial anchor daily limit exceeded")
                    return True
            else:
                self._TRIAL_ANCHOR_IP_COUNTS[client_ip] = (0, day_start)
        return False

    def _increment_trial_anchor_ip_count(self, client_ip: str, day_start: int) -> None:
        """Increment the per-IP trial anchor counter under lock."""
        with self._TRIAL_ANCHOR_IP_LOCK:
            entry = self._TRIAL_ANCHOR_IP_COUNTS.get(client_ip)
            if entry is not None:
                count, stored_day = entry
                if stored_day == day_start:
                    self._TRIAL_ANCHOR_IP_COUNTS[client_ip] = (count + 1, day_start)
                else:
                    self._TRIAL_ANCHOR_IP_COUNTS[client_ip] = (1, day_start)
            else:
                self._TRIAL_ANCHOR_IP_COUNTS[client_ip] = (1, day_start)

    def _route_trial_anchor(self):
        if not self._rate_ok(None):
            return
        if not self._require_json():
            return

        # SEC-006: Check per-IP daily limit before processing
        client_ip = self._client_ip()
        day_start = int(time.time()) // 86400 * 86400  # start of current UTC day
        if self._check_trial_anchor_ip_limit(client_ip, day_start):
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
        # E037: Validate fingerprint format.
        if not (_UUID_RE.match(fingerprint) or _HEX_RE.match(fingerprint)):
            return self._json_error(400, "invalid fingerprint format")

        now = int(time.time())
        try:
            first_launch, is_new = db.get_or_create_trial_anchor(fingerprint, now)
            # A032: first_launch is None when the table is at capacity and this
            # fingerprint is new — reject rather than evicting legitimate anchors.
            if first_launch is None:
                return self._json_error(507, "trial anchor capacity reached; try again later")
            # SEC-006: Increment IP counter on successful anchor creation
            if is_new:
                self._increment_trial_anchor_ip_count(client_ip, day_start)
            self._send(200, {"fingerprint": fingerprint, "firstLaunch": first_launch, "serverTime": now})
        except Exception as e:
            logging_util.error("trial_anchor_error", err=str(e))
            self._json_error(500, "internal database error")

    def _route_beta_reserve(self):
        """Public: record interest in the Nnyx/Zzyn closed betas. Rate-limited per IP."""
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
        interests = [x for x in interests if x in ("nnyx", "zzyn")] if isinstance(interests, list) else []
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

    def _route_checkout(self, query):
        """Start a Stripe Managed-Payments checkout (Workstream D4) — PUBLIC (this IS the purchase flow)."""
        # E027: reject cross-site requests to this mutation endpoint
        if not self._check_sec_fetch():
            return
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
        # A035/A084: derive a stable idempotency key from (sku, client_ip, coarse time window).
        window = int(time.time()) // 3600
        client_ip = self.client_address[0]
        idem_key = f"checkout-{sku}-{hashlib.sha256(client_ip.encode()).hexdigest()[:16]}-{window}"
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
        or update their payment method without operator involvement."""
        # Drain and discard the request body.
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
        lic_id = db.keygen_license_id_get(ent.subject)
        if not lic_id:
            return self._json_error(404, "no license mapping found for this session")
        customer_id = db.kv_find_by_value_prefix("stripe_cust:", lic_id)
        if not customer_id:
            return self._json_error(404, "no subscription found for this session")
        try:
            session = stripe_checkout.create_billing_portal_session(customer_id)
        except stripe_checkout.StripeError as e:
            logging_util.error("billing_portal_failed", err=str(e))
            return self._json_error(503, "billing portal unavailable")
        self._send(200, {"url": session.get("url", "")}, extra_headers={"Cache-Control": "no-store"})

    def _route_stripe_webhook(self):
        """Stripe webhook (Workstream D4) — PUBLIC but SIGNATURE-VERIFIED."""
        # SEC-047: Apply rate limiting before signature verification
        if not ratelimit.allow("webhook:ip:" + self._client_ip()):
            logging_util.warn("stripe_webhook_ratelimited", ip=self._client_ip()[:15])
            return self._json_error(429, "rate limit exceeded")
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
            # schedule engages.
            return self._json_error(500, "internal fulfillment error")
        self._send(200, {"received": True, "conclusion": result}, extra_headers={"Cache-Control": "no-store"})

    def _route_keygen_webhook(self):
        """Keygen webhook — PUBLIC but SIGNATURE-VERIFIED."""
        if not config.KEYGEN_WEBHOOK_SECRET:
            return self._json_error(501, "webhook not configured")
        # A063: per-IP rate limit before signature verification
        if not ratelimit.allow("webhook:ip:" + self._client_ip()):
            logging_util.warn("keygen_webhook_ratelimited", ip=self._client_ip()[:15])
            return self._json_error(429, "rate limit exceeded")
        raw = self._body()
        if self._body_error(raw):
            return
        # Verify HMAC-SHA256 signature
        sig_header = self.headers.get("Keygen-Signature", "")
        if not sig_header:
            return self._json_error(400, "missing signature")
        expected = hmac.new(config.KEYGEN_WEBHOOK_SECRET.encode("utf-8"), raw or b"", hashlib.sha256).hexdigest()
        # A148: wrap compare_digest in try/except TypeError.
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
        # A061: replay fence — deduplicate verified event ids.
        event_id = event.get("id") or hashlib.sha256(raw or b"").hexdigest()[:32]
        dedup_key = "keygen_webhook:" + str(event_id)
        seen, created_at = db.kv_get_with_created_at(dedup_key)
        if seen is not None and (time.time() - created_at) < 3600:
            return self._send(200, {"received": True, "conclusion": "duplicate", "event": event_type})
        db.kv_set(dedup_key, "1")
        data = event.get("data", {})
        lic_id = data.get("id", "") if isinstance(data, dict) else ""
        if not lic_id:
            return self._send(200, {"received": True, "conclusion": "ignored"})
        result = "ignored"
        if event_type in ("license.revoked", "license.suspended", "license.expired"):
            # A175: resolve Keygen id to local subject before revoking.
            local_subject = db.keygen_subject_for_license_id(lic_id)
            if local_subject:
                revoked_count = db.api_key_revoke_all_for_subject(local_subject)
                if revoked_count > 0:
                    result = "keys_revoked"
                else:
                    result = "no_keys_for_subject"
                logging_util.info(
                    "keygen_webhook_revoked_keys",
                    event_type=event_type,
                    license_id=lic_id,
                    subject=local_subject,
                    revoked_count=revoked_count,
                )
            else:
                result = "subject_unresolved"
                logging_util.warn(
                    "keygen_webhook_subject_unresolved",
                    event_type=event_type,
                    license_id=lic_id,
                    detail="Keygen revocation received but no local "
                    "subject maps to this license id — API keys "
                    "were NOT revoked. Resolve manually.",
                )
        elif event_type in ("license.created", "license.activated", "license.renewed"):
            result = "noted"
        self._send(
            200,
            {"received": True, "conclusion": result, "event": event_type},
            extra_headers={"Cache-Control": "no-store"},
        )

    # --- API keys -----------------------------------------------------------

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
        try:
            ttl = max(1, min(int(payload.get("ttlDays", 90)), 365))
        except (ValueError, TypeError, OverflowError):
            return self._json_error(400, "ttlDays must be an integer between 1 and 365")
        key = db.api_key_create(ent.subject, label=label, ttl_days=ttl)
        logging_util.incr("api_keys_created")
        self._send(
            201,
            {
                "key": key,
                "label": label,
                "expiresInDays": ttl,
                "warning": "store this key now; it will not be shown again",
            },
        )

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
        self._send(200, {"revoked": bool(revoked), "count": revoked})

    # --- Monitors -----------------------------------------------------------

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
        # A280: default to valid JSON array instead of bare string "prophet"
        checks_raw = payload.get("checks", '["investigate"]')
        interval_hours = int(payload.get("interval_hours", 24))
        notify_email = (payload.get("notify_email") or "").strip() or None
        if not domain:
            return self._json_error(400, "missing domain")
        # A273: validate domain to prevent SSRF via localhost or metadata IPs.
        if not investigate.valid_domain(domain) or len(domain) > 253:
            return self._json_error(400, "invalid domain")
        # A280: validate checks is a non-empty JSON array with recognized names.
        try:
            checks_list = json.loads(checks_raw) if isinstance(checks_raw, str) else checks_raw
        except (json.JSONDecodeError, TypeError):
            return self._json_error(400, "checks must be a valid JSON array")
        if not isinstance(checks_list, list) or not checks_list:
            return self._json_error(400, "checks must be a non-empty array")
        unrecognized = [c for c in checks_list if c not in self._VALID_CHECK_NAMES]
        if unrecognized:
            return self._json_error(
                400, f"unrecognized check(s): {unrecognized}; valid: {sorted(self._VALID_CHECK_NAMES)}"
            )
        # Normalize to JSON string for consistent storage.
        checks = json.dumps(checks_list)
        # A278: validate notify_email before persisting.
        if notify_email and not _valid_email(notify_email):
            return self._json_error(400, "invalid notify_email")
        monitor_id = str(uuid.uuid4())
        db.monitor_create(monitor_id, ent.subject, domain, checks, interval_hours, notify_email)
        logging_util.incr("monitors_created")
        self._send(
            201,
            {
                "id": monitor_id,
                "domain": domain,
                "checks": checks,
                "interval_hours": interval_hours,
                "notify_email": notify_email,
            },
        )

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
        deleted = db.monitor_delete(monitor_id, ent.subject)
        self._send(200, {"deleted": bool(deleted), "count": deleted})

"""Order fulfillment for verified Stripe webhooks (Workstream D4).

On a completed/succeeded payment, mint a Keygen license under the SKU's policy and email the key; on a
lapsed subscription or failed payment, note it (and revoke, once the customer->license map exists).
Idempotent by Stripe event id so re-delivered webhooks never double-provision. All Keygen-admin bits are
env-configured — with none set, the sale is recorded as PENDING (audit), never silently dropped.
"""
import json
import urllib.error
import urllib.request

from . import config, db, logging_util, mailer

_KEY_EMAIL = (
    "Thanks for your purchase!\n\n"
    "Your Syxx license key:\n\n    %s\n\n"
    "Open Syxx, go to the License view, and paste this key to activate. "
    "Keep it somewhere safe.\n"
)


def _keygen_create_license(policy_id, email):
    """Mint a license under `policy_id` via the Keygen admin API. Returns (key, license_id), or
    (None, None) if admin creds/policy are unset or the call fails (never raises)."""
    if not config.KEYGEN_ADMIN_TOKEN or not policy_id:
        return None, None
    base = (config.KEYGEN_BASE_URL
            or "https://api.keygen.sh/v1/accounts/%s" % config.KEYGEN_ACCOUNT_ID).rstrip("/")
    body = json.dumps({"data": {
        "type": "licenses",
        "attributes": {"metadata": {"email": email}} if email else {},
        "relationships": {"policy": {"data": {"type": "policies", "id": policy_id}}},
    }}).encode()
    req = urllib.request.Request(base + "/licenses", data=body, method="POST")
    req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)
    req.add_header("Content-Type", "application/vnd.api+json")
    req.add_header("Accept", "application/vnd.api+json")
    try:
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT) as r:
            data = json.loads(r.read()).get("data", {})
        return data.get("attributes", {}).get("key"), data.get("id")
    except (urllib.error.HTTPError, OSError, ValueError) as e:
        logging_util.error("keygen_mint_failed", err=str(e))
        return None, None


def _keygen_suspend_license(license_id):
    """Suspend a Keygen license via the admin API (subscription lapse). Returns True on success;
    never raises. No-op (False) if the admin token or license id is missing."""
    if not config.KEYGEN_ADMIN_TOKEN or not license_id:
        return False
    base = (config.KEYGEN_BASE_URL
            or "https://api.keygen.sh/v1/accounts/%s" % config.KEYGEN_ACCOUNT_ID).rstrip("/")
    req = urllib.request.Request(base + "/licenses/%s/actions/suspend" % license_id,
                                 data=b"", method="POST")
    req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)
    req.add_header("Accept", "application/vnd.api+json")
    try:
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT) as r:
            r.read()
        return True
    except (urllib.error.HTTPError, OSError, ValueError) as e:
        logging_util.error("keygen_suspend_failed", err=str(e))
        return False


def _keygen_unsuspend_license(license_id):
    """Re-activate a suspended Keygen license via the admin API (subscription reactivation).
    Returns True on success; never raises. No-op (False) if the admin token or license id is missing."""
    if not config.KEYGEN_ADMIN_TOKEN or not license_id:
        return False
    base = (config.KEYGEN_BASE_URL
            or "https://api.keygen.sh/v1/accounts/%s" % config.KEYGEN_ACCOUNT_ID).rstrip("/")
    req = urllib.request.Request(base + "/licenses/%s/actions/activate" % license_id,
                                 data=b"", method="POST")
    req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)
    req.add_header("Accept", "application/vnd.api+json")
    try:
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT) as r:
            r.read()
        return True
    except (urllib.error.HTTPError, OSError, ValueError) as e:
        logging_util.error("keygen_unsuspend_failed", err=str(e))
        return False


def _log_chargeback(obj, event_id, etype):
    """C4: Log a refund or chargeback event for operator awareness and audit trail.

    Never raises; returns "chargeback-logged" so the event is always acknowledged.
    """
    charge_id = obj.get("id", "") if isinstance(obj, dict) else ""
    amount = obj.get("amount", 0) if isinstance(obj, dict) else 0
    currency = obj.get("currency", "usd") if isinstance(obj, dict) else "usd"
    customer = obj.get("customer", "") if isinstance(obj, dict) else ""
    reason = "refund" if "refund" in etype else "dispute"
    logging_util.warn("stripe_%s" % reason,
                       charge_id=charge_id,
                       customer=customer or "unknown",
                       amount_cents=amount,
                       currency=currency,
                       event_id=event_id,
                       detail="%s event received for charge %s (%d %s)" % (
                           etype, charge_id, amount, currency.upper()))
    db.audit(customer or charge_id, "stripe:%s" % etype, 200, "stripe")
    return "chargeback-logged"


def handle_event(event):
    """Route a VERIFIED Stripe event (caller must have checked the signature). Idempotent by event id."""
    event_id = event.get("id") or ""
    etype = event.get("type", "")
    obj = (event.get("data") or {}).get("object") or {}

    # C4: refund and dispute events are logged and audited (action taken later when
    # the license-revocation path is wired).  Always 200 so Stripe does not retry.
    if etype in ("charge.refunded",):
        return _log_chargeback(obj, event_id, etype)

    if etype in ("dispute.created", "dispute.updated", "dispute.closed"):
        return _log_chargeback(obj, event_id, etype)

    if etype in ("checkout.session.completed", "checkout.session.async_payment_succeeded"):
        paid = (obj.get("payment_status") in ("paid", "no_payment_required")
                or etype.endswith("async_payment_succeeded"))
        if not paid:
            return "await-async"   # delayed method still processing — async_payment_succeeded will follow
        # A013: claim the event ATOMICALLY so two concurrent deliveries can never both mint. A plain
        # kv_get + kv_set is a check-then-act race — both readers see "absent", both proceed, both
        # mint (double-mint). kv_insert_once is a compare-and-set: exactly one caller wins the
        # absent -> "processing" transition. Idempotency states: "minted" = done; "processing" =
        # another delivery is handling it right now; "failed" = a prior attempt hit a Keygen outage
        # (A019) and must be retryable so a charged-but-undelivered license is not permanently dropped.
        if event_id:
            evt_key = "stripe_evt:" + event_id
            if not db.kv_insert_once(evt_key, "processing"):
                existing = db.kv_get(evt_key)
                if existing in ("minted", "processing", "minted-undelivered"):
                    # "minted-undelivered" is TERMINAL FOR MINTING: the license exists, so
                    # re-entering would mint a SECOND one (double-mint) for a single payment. The
                    # key is recoverable via the stored license id; delivery is an operator action.
                    return "duplicate"
                # "failed" (or any non-terminal state) -> re-enter so a Stripe retry can mint the
                # charged-but-undelivered license (A019 retry-after-Keygen-outage path).
                db.kv_set(evt_key, "processing")
        email = (obj.get("customer_details") or {}).get("email") or obj.get("customer_email")
        # Which SKU sold — stamped into the session metadata at checkout.
        meta = obj.get("metadata") or {}
        sku = meta.get("sku") or ""
        if not sku:
            # Pre-refactor sessions stamped metadata[product]. One created just before the deploy
            # can still complete after it (Checkout sessions live ~24h), and resolving it to no
            # policy would leave a CHARGED buyer waiting on a manual mint. Translate instead.
            legacy = meta.get("product") or obj.get("client_reference_id") or ""
            sku = config.sku_for_legacy_product(legacy)
            if sku:
                logging_util.warn("stripe_legacy_product_metadata", product=legacy, sku=sku,
                                  detail="pre-SKU-refactor session fulfilled via the legacy product "
                                         "map; remove the map once these can no longer arrive")
        policy = config.keygen_policy_for(sku)
        key, license_id = _keygen_create_license(policy, email)
        # A047: mailer.send() returns False (never raises) when SMTP is unconfigured or the send
        # fails. Discarding that return meant a paid buyer could be charged, have a license minted,
        # and NEVER RECEIVE IT — while the event was locked "minted" (terminal) so Stripe's
        # redelivery reported "duplicate" and never retried. Silent, and it costs a real customer a
        # real key. Capture it: minting and DELIVERY are two different claims.
        delivered = True
        if key and email:
            delivered = mailer.send(email, "Your Syxx license key", _KEY_EMAIL % key)
        # Remember customer+subscription -> license so a later subscription cancel can
        # suspend the CORRECT license. A025: the old code keyed on stripe_cust:<customer>
        # alone, a single slot overwritten on every purchase. A customer who bought twice
        # (or a lifetime purchase followed by a later cancelled sub) could be locked out
        # of a paid entitlement, or a cancelled subscription could orphan the wrong license.
        if license_id and obj.get("customer"):
            customer = str(obj["customer"])
            subscription_id = obj.get("subscription") or obj.get("id", "")
            # For one-time charges (no subscription_id), use the session id as a sentinel
            # so they are never suspended by a later subscription cancel.
            sub_key = "stripe_sub:" + (subscription_id if subscription_id else ("sess:" + str(obj.get("id", ""))))
            db.kv_set(sub_key, license_id)
            # Also keep the customer->license map for backward compat with the billing portal.
            db.kv_set("stripe_cust:" + customer, license_id)
        # Privacy: audit by the OPAQUE Stripe customer/session id, never the buyer's email — no PII in our DB.
        # A047: a minted-but-undelivered sale is NOT a 200. It is a paying customer with no key,
        # and it must be loud enough for an operator to find and act on.
        if key and not delivered:
            logging_util.warn("license_minted_but_undelivered",
                              license_id=license_id or "", sku=sku or "unknown",
                              detail="license minted; the key email FAILED to send (SMTP unconfigured "
                                     "or send error). The customer has paid and has no key — resend "
                                     "manually from the license id.")
        audit_code = 200 if (key and delivered) else 500
        db.audit(obj.get("customer") or obj.get("id") or "stripe",
                 "fulfill:" + (sku or "unknown"), audit_code, "stripe")
        if event_id:
            # Terminal state: "minted" locks the event as done (later deliveries -> duplicate);
            # "failed" leaves it retryable so a Stripe redelivery after a Keygen outage can mint.
            # "minted-undelivered" is ALSO terminal for minting (the license exists — re-entering
            # would double-mint) but is deliberately a DISTINCT state so it is never mistaken for a
            # completed fulfilment in the KV store or an audit query.
            if key and delivered:
                state = "minted"
            elif key:
                state = "minted-undelivered"
            else:
                state = "failed"
            db.kv_set("stripe_evt:" + event_id, state)
        if not key:
            return "pending-mint"
        return "fulfilled" if delivered else "minted-undelivered"

    if etype in ("customer.subscription.deleted", "invoice.payment_failed",
                 "checkout.session.async_payment_failed"):
        if event_id and not db.kv_insert_once("stripe_evt:" + event_id, "processing"):
            return "duplicate"
        # Only a definitively-ended subscription suspends the license. A single failed invoice is left
        # to Stripe's dunning/retry — if it ultimately gives up, Stripe fires subscription.deleted. This
        # avoids suspending a paying customer over one transient card decline.
        suspended = False
        if etype == "customer.subscription.deleted":
            # A025: suspend the license bound to THIS subscription, not whatever the
            # customer's current single-slot points at. The old code read
            # stripe_cust:<customer> which was overwritten on every purchase.
            subscription_id = obj.get("id") or obj.get("subscription", "")
            license_id = None
            if subscription_id:
                license_id = db.kv_get("stripe_sub:" + str(subscription_id))
            # Fallback: if the per-subscription key doesn't exist (legacy data), try
            # the customer-level key. This is a best-effort migration path.
            if not license_id and obj.get("customer"):
                license_id = db.kv_get("stripe_cust:" + str(obj["customer"]))
            if license_id:
                suspended = _keygen_suspend_license(license_id)
        db.audit(obj.get("customer") or "stripe", "fulfill:lapse:" + etype, 200, "stripe")
        if event_id:
            db.kv_set("stripe_evt:" + event_id, "suspended" if suspended else "lapse")
        return "suspended" if suspended else "lapse-noted"

    # A085: reactivate a suspended license when a subscription resumes or a previously
    # failed payment succeeds. Without this, a cancel-then-resume leaves a paying
    # customer's key suspended.
    if etype in ("customer.subscription.updated", "invoice.paid"):
        if event_id and not db.kv_insert_once("stripe_evt:" + event_id, "processing"):
            return "duplicate"
        reactivated = False
        # For subscription.updated, only reactivate when status becomes active after
        # being past_due, canceled, or incomplete_expired.
        if etype == "customer.subscription.updated":
            status = obj.get("status", "")
            if status in ("active", "trialing"):
                subscription_id = obj.get("id") or ""
                license_id = None
                if subscription_id:
                    license_id = db.kv_get("stripe_sub:" + str(subscription_id))
                if not license_id and obj.get("customer"):
                    license_id = db.kv_get("stripe_cust:" + str(obj["customer"]))
                if license_id:
                    reactivated = _keygen_unsuspend_license(license_id)
        elif etype == "invoice.paid":
            # A previously failed payment now paid — look up the subscription and
            # reactivate its license.
            subscription_id = obj.get("subscription") or ""
            license_id = None
            if subscription_id:
                license_id = db.kv_get("stripe_sub:" + str(subscription_id))
            if not license_id and obj.get("customer"):
                license_id = db.kv_get("stripe_cust:" + str(obj["customer"]))
            if license_id:
                reactivated = _keygen_unsuspend_license(license_id)
        db.audit(obj.get("customer") or "stripe", "fulfill:reactivate:" + etype, 200, "stripe")
        if event_id:
            db.kv_set("stripe_evt:" + event_id, "reactivated" if reactivated else "reactivate-noop")
        return "reactivated" if reactivated else "reactivate-noop"

    return "ignored"

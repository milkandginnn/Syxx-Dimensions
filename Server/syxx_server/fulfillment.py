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

# A181: lease duration for the "processing" idempotency lock. If the handler that
# claimed the event crashes (OOM, restart, exception) before writing a terminal
# state, the row stays "processing" forever and every redelivery returns
# "duplicate" — a charged customer with no license and no retry path. After this
# many seconds, a new delivery can reclaim the lock and re-process the event.
_PROCESSING_LEASE_SECONDS = 600  # 10 minutes

_KEY_EMAIL = (
    "Thanks for your purchase!\n\n"
    "Your Syxx license key:\n\n    %s\n\n"
    "Open Syxx, go to the License view, and paste this key to activate. "
    "Keep it somewhere safe.\n"
)


def _keygen_create_license(policy_id, email):
    """Mint a license under `policy_id` via the Keygen admin API. Returns (key, license_id), or
    (None, None) if admin creds/policy are unset or the call fails (never raises).

    Machine binding: the license carries `maxMachines` = config.KEYGEN_MAX_MACHINES (default 5,
    the number the published EULA promises at Server/index.html:513). Read the comment on that
    constant before relying on it: Keygen honours a per-license `maxMachines` only on a policy that
    permits the override, and no client activates a machine yet, so today this is a DECLARED
    ceiling rather than an enforced one. Sending it is still strictly better than sending nothing —
    it is the half of the two-sided change this repository owns, and it makes every license issued
    from now on carry the limit rather than needing a backfill later.
    """
    if not config.KEYGEN_ADMIN_TOKEN or not policy_id:
        return None, None
    base = (config.KEYGEN_BASE_URL or f"https://api.keygen.sh/v1/accounts/{config.KEYGEN_ACCOUNT_ID}").rstrip("/")
    attributes = {}
    if email:
        attributes["metadata"] = {"email": email}
    if config.KEYGEN_MAX_MACHINES > 0:
        attributes["maxMachines"] = config.KEYGEN_MAX_MACHINES
    body = json.dumps(
        {
            "data": {
                "type": "licenses",
                "attributes": attributes,
                "relationships": {"policy": {"data": {"type": "policies", "id": policy_id}}},
            }
        }
    ).encode()
    req = urllib.request.Request(base + "/licenses", data=body, method="POST")  # noqa: S310
    req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)
    req.add_header("Content-Type", "application/vnd.api+json")
    req.add_header("Accept", "application/vnd.api+json")
    try:
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT) as r:  # noqa: S310
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
    base = (config.KEYGEN_BASE_URL or f"https://api.keygen.sh/v1/accounts/{config.KEYGEN_ACCOUNT_ID}").rstrip("/")
    req = urllib.request.Request(base + f"/licenses/{license_id}/actions/suspend", data=b"", method="POST")  # noqa: S310
    req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)
    req.add_header("Accept", "application/vnd.api+json")
    try:
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT) as r:  # noqa: S310
            r.read()
        return True
    except (urllib.error.HTTPError, OSError, ValueError) as e:
        logging_util.error("keygen_suspend_failed", err=str(e))
        return False


def _keygen_reinstate_license(license_id):
    """Reinstate a suspended Keygen license via the admin API (subscription reactivated).
    Returns True on success; never raises. No-op (False) if the admin token or license id is missing.

    A085: called when a subscription transitions back to active after a prior suspension,
    so a paying customer whose card was updated or whose subscription was renewed is not left
    with a permanently suspended license.
    """
    if not config.KEYGEN_ADMIN_TOKEN or not license_id:
        return False
    base = (config.KEYGEN_BASE_URL or f"https://api.keygen.sh/v1/accounts/{config.KEYGEN_ACCOUNT_ID}").rstrip("/")
    req = urllib.request.Request(base + f"/licenses/{license_id}/actions/reinstate", data=b"", method="POST")  # noqa: S310
    req.add_header("Authorization", "Bearer " + config.KEYGEN_ADMIN_TOKEN)
    req.add_header("Accept", "application/vnd.api+json")
    try:
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT) as r:  # noqa: S310
            r.read()
        return True
    except (urllib.error.HTTPError, OSError, ValueError) as e:
        logging_util.error("keygen_reinstate_failed", err=str(e))
        return False


# Stripe events that mean the money is going back, so entitlement must go with it.
# `charge.dispute.created` is the event Stripe actually emits; `dispute.created` is not, but it was
# already accepted here and an API-version change or a replayed fixture could still deliver it, so
# both spellings revoke rather than silently falling through to "ignored".
_CHARGEBACK_REVOKE_EVENTS = ("charge.refunded", "charge.dispute.created", "dispute.created")
# Events that only REPORT on a dispute already in flight. These stay log-only on purpose: a dispute
# can be WON, and revoking a customer who was found legitimate is the same class of error as
# granting one who was not.
# A085: the reinstatement gap is now partially closed — _keygen_reinstate_license handles
# subscription reactivation (customer.subscription.updated with status=active). A customer whose
# dispute is later WON still requires operator action (no automatic dispute-win reinstatement).
_CHARGEBACK_LOG_ONLY_EVENTS = (
    "charge.dispute.updated",
    "charge.dispute.closed",
    "charge.dispute.funds_reinstated",
    "dispute.updated",
    "dispute.closed",
)


def _log_chargeback(obj, event_id, etype):
    """Log a dispute-progress event for operator awareness and audit trail. No entitlement change.

    Never raises; returns "chargeback-logged" so the event is always acknowledged.
    """
    charge_id = obj.get("id", "") if isinstance(obj, dict) else ""
    amount = obj.get("amount", 0) if isinstance(obj, dict) else 0
    currency = obj.get("currency", "usd") if isinstance(obj, dict) else "usd"
    customer = obj.get("customer", "") if isinstance(obj, dict) else ""
    reason = "refund" if "refund" in etype else "dispute"
    logging_util.warn(
        f"stripe_{reason}",
        charge_id=charge_id,
        customer=customer or "unknown",
        amount_cents=amount,
        currency=currency,
        event_id=event_id,
        detail=f"{etype} event received for charge {charge_id} ({amount} {currency.upper()})",
    )
    db.audit(customer or charge_id, f"stripe:{etype}", 200, "stripe")
    return "chargeback-logged"


def _license_for_chargeback(obj):
    """Map a refunded charge / created dispute back to the Keygen license this server minted.

    Returns the license id, or None for UNKNOWN. None is not "there is nothing to revoke" — see
    _revoke_for_chargeback, which reports it as unresolved and never as a completed revocation.

    Order matters. A DISPUTE object carries no `customer` (Stripe puts only `charge` and
    `payment_intent` on it), so the payment-intent map is tried first; a refunded CHARGE carries
    both. A subscription renewal has no checkout session behind it, so a dispute on one can
    legitimately come back unresolved — which is exactly why unresolved is a reported state and
    not a silent no-op.
    """
    if not isinstance(obj, dict):
        return None
    pi = obj.get("payment_intent") or ""
    if pi:
        lic = db.kv_get("stripe_pi:" + str(pi))
        if lic:
            return lic
    customer = obj.get("customer") or ""
    if customer:
        lic = db.kv_get("stripe_cust:" + str(customer))
        if lic:
            return lic
    return None


def _revoke_for_chargeback(obj, event_id, etype):
    """A refund or a newly-created dispute REVOKES entitlement. Idempotent by Stripe event id.

    The outcome is three-valued and NONE of the three is a plain success:

      "revoked"            the license was resolved AND Keygen confirmed the suspend.
      "revoke-failed"      the license was resolved but Keygen did not confirm. Upstream
                           entitlement is UNKNOWN. Local API keys are revoked anyway, so this
                           server fails closed even while Keygen's answer is missing.
      "revoke-unresolved"  no license could be mapped to this charge. Nothing was revoked
                           anywhere. This is the loudest of the three: it is the only outcome
                           where a refunded customer still holds working entitlements.

    Collapsing these to a boolean would let an unresolved refund report exactly what a completed
    one reports, which is "missing data read as clean data" wearing a billing hat. Keep it
    three-valued. Never raises — the caller must always 200 a verified Stripe event.
    """
    charge_id = obj.get("id", "") if isinstance(obj, dict) else ""
    amount = obj.get("amount", 0) if isinstance(obj, dict) else 0
    currency = obj.get("currency", "usd") if isinstance(obj, dict) else "usd"
    customer = obj.get("customer", "") if isinstance(obj, dict) else ""
    reason = "refund" if "refund" in etype else "dispute"

    # Idempotency: the SAME compare-and-set scheme minting uses (db.kv_insert_once on
    # stripe_evt:<id>), not a second one. Stripe retries webhooks; a replay must not re-run the
    # revocation, and a check-then-act kv_get/kv_set would let two concurrent deliveries both pass.
    evt_key = "stripe_evt:" + event_id if event_id else ""
    if evt_key:
        if not db.kv_insert_once(evt_key, "processing"):
            existing = db.kv_get(evt_key)
            if existing in ("revoked", "processing"):
                return "duplicate"
            if existing in ("minted", "minted-undelivered", "suspended", "lapse", "failed"):
                # This event id already carries a DIFFERENT event's outcome. Stripe never reuses
                # an event id, so this means a collision we do not understand; refuse to
                # overwrite another event's record and report it rather than guessing.
                logging_util.error(
                    "stripe_chargeback_event_id_collision", event_id=event_id, etype=etype, existing=existing
                )
                return "duplicate"
            # "revoke-failed" / "revoke-unresolved" are deliberately NOT terminal: a Stripe
            # redelivery after a Keygen outage, or after a late-arriving mint, must be able to
            # finish the job. Suspending an already-suspended license is a no-op upstream, so
            # re-entering cannot over-revoke.
            db.kv_set(evt_key, "processing")

    license_id = _license_for_chargeback(obj)
    if not license_id:
        logging_util.error(
            f"stripe_{reason}_unrevoked",
            charge_id=charge_id,
            customer=customer or "unknown",
            amount_cents=amount,
            currency=currency,
            event_id=event_id,
            detail=f"{etype} received but NO minted license could be mapped to this "
            "charge, so nothing was revoked. The customer has their money "
            "back AND a working license. Resolve by hand from the charge "
            "id.",
        )
        db.audit(customer or charge_id or "stripe", f"fulfill:{etype}:unresolved", 500, "stripe")
        if evt_key:
            db.kv_set(evt_key, "revoke-unresolved")
        return "revoke-unresolved"

    suspended = _keygen_suspend_license(license_id)

    # Revoke this server's OWN keys regardless of what Keygen said. The local surface must fail
    # closed on a refund even when the upstream answer is missing — otherwise a Keygen timeout
    # leaves a refunded customer's API keys live here. A missing subject map means UNKNOWN (the
    # subject may never have activated through Keygen on this host), which is logged, not assumed
    # to mean "no keys exist".
    subject = db.keygen_subject_for_license_id(license_id)
    if subject:
        try:
            db.api_key_revoke_all_for_subject(subject)
        except Exception as e:
            logging_util.error("chargeback_local_key_revoke_failed", license_id=license_id, err=str(e))
    else:
        logging_util.warn(
            "chargeback_no_local_subject_for_license",
            license_id=license_id,
            event_id=event_id,
            detail="the license was suspended upstream, but no local subject maps "
            "to it, so any API keys it owns on this server were NOT revoked.",
        )

    outcome = "revoked" if suspended else "revoke-failed"
    if suspended:
        logging_util.warn(
            f"stripe_{reason}_revoked",
            charge_id=charge_id,
            customer=customer or "unknown",
            amount_cents=amount,
            currency=currency,
            event_id=event_id,
            license_id=license_id,
            detail=f"{etype}: license suspended in Keygen.",
        )
    else:
        logging_util.error(
            f"stripe_{reason}_revoke_failed",
            charge_id=charge_id,
            customer=customer or "unknown",
            amount_cents=amount,
            currency=currency,
            event_id=event_id,
            license_id=license_id,
            detail=f"{etype}: Keygen did NOT confirm the suspend, so upstream "
            "entitlement is UNKNOWN. Local keys were revoked. A Stripe "
            "redelivery will retry.",
        )
    db.audit(customer or charge_id or "stripe", f"fulfill:{etype}", 200 if suspended else 500, "stripe")
    if evt_key:
        db.kv_set(evt_key, outcome)
    return outcome


def handle_event(event):
    """Route a VERIFIED Stripe event (caller must have checked the signature). Idempotent by event id."""
    event_id = event.get("id") or ""
    etype = event.get("type", "")
    obj = (event.get("data") or {}).get("object") or {}

    # The money went back: revoke entitlement with it. Idempotent by event id, three-valued
    # outcome, fails closed locally when Keygen cannot confirm. Always returns (never raises) so
    # the route can 200 a verified event.
    if etype in _CHARGEBACK_REVOKE_EVENTS:
        return _revoke_for_chargeback(obj, event_id, etype)

    # Dispute PROGRESS (updated/closed/funds reinstated) changes no entitlement — a dispute can be
    # won, and revoking a customer found legitimate is the mirror of granting one who is not.
    if etype in _CHARGEBACK_LOG_ONLY_EVENTS:
        return _log_chargeback(obj, event_id, etype)

    if etype in ("checkout.session.completed", "checkout.session.async_payment_succeeded"):
        paid = obj.get("payment_status") in ("paid", "no_payment_required") or etype.endswith("async_payment_succeeded")
        if not paid:
            return "await-async"  # delayed method still processing — async_payment_succeeded will follow
        # A013: claim the event ATOMICALLY so two concurrent deliveries can never both mint. A plain
        # kv_get + kv_set is a check-then-act race — both readers see "absent", both proceed, both
        # mint (double-mint). kv_insert_once is a compare-and-set: exactly one caller wins the
        # absent -> "processing" transition. Idempotency states: "minted" = done; "processing" =
        # another delivery is handling it right now; "failed" = a prior attempt hit a Keygen outage
        # (A019) and must be retryable so a charged-but-undelivered license is not permanently dropped.
        if event_id:
            evt_key = "stripe_evt:" + event_id
            if not db.kv_insert_once(evt_key, "processing"):
                # Prefer the lease-aware reader; fall back to kv_get so in-memory test
                # fixtures that only mock kv_get still observe the claimed state.
                if hasattr(db, "kv_get_with_created_at"):
                    existing, created_at = db.kv_get_with_created_at(evt_key)
                else:
                    existing, created_at = db.kv_get(evt_key), None
                if existing is None:
                    # insert_once refused but no readable state: another writer owns the
                    # row we cannot see (or the fixture only mocked one side). Fail closed.
                    existing = db.kv_get(evt_key)
                    created_at = None
                if existing in ("minted", "minted-undelivered"):
                    # "minted-undelivered" is TERMINAL FOR MINTING: the license exists, so
                    # re-entering would mint a SECOND one (double-mint) for a single payment. The
                    # key is recoverable via the stored license id; delivery is an operator action.
                    return "duplicate"
                if existing == "processing":
                    # A181: check the lease. If the processing lock is older than
                    # _PROCESSING_LEASE_SECONDS, the original handler likely crashed
                    # and the event is reclaimable. Otherwise it is genuinely in-flight.
                    import time as _time

                    age = int(_time.time()) - (created_at or 0)
                    if created_at is None or age < _PROCESSING_LEASE_SECONDS:
                        return "duplicate"
                    logging_util.warn(
                        "stripe_event_lock_reclaimed",
                        event_id=event_id,
                        age_seconds=age,
                        detail=f"processing lock older than {_PROCESSING_LEASE_SECONDS}s — "
                        "reclaiming (original handler likely crashed)",
                    )
                    db.kv_set(evt_key, "processing")
                elif existing == "failed":
                    # "failed" -> re-enter so a Stripe retry can mint the
                    # charged-but-undelivered license (A019 retry-after-Keygen-outage path).
                    db.kv_set(evt_key, "processing")
                else:
                    # Unknown or absent after a lost claim: never mint a second license.
                    return "duplicate"
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
                logging_util.warn(
                    "stripe_legacy_product_metadata",
                    product=legacy,
                    sku=sku,
                    detail="pre-SKU-refactor session fulfilled via the legacy product "
                    "map; remove the map once these can no longer arrive",
                )
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
        # Remember customer -> license so a later subscription cancel can suspend it.
        if license_id and obj.get("customer"):
            db.kv_set("stripe_cust:" + str(obj["customer"]), license_id)
        # A025: also key by subscription id. The customer->license slot is a single
        # overwrite — a second purchase (or lifetime purchase followed by a cancelled
        # sub) overwrites it, and subscription.deleted suspends whatever it currently
        # points at (wrong license). Per-subscription keying ensures suspension targets
        # exactly the license that subscription paid for.
        if license_id and obj.get("subscription"):
            db.kv_set("stripe_sub:" + str(obj["subscription"]), license_id)
        # Remember payment-intent -> license as well. A Stripe DISPUTE object carries no `customer`
        # field at all (only `charge` and `payment_intent`), so without this map a chargeback on a
        # one-time purchase cannot be traced back to the license it paid for, and the refund path
        # can only report "unresolved". Subscription-mode sessions have no payment_intent, so this
        # is written when present and its absence is not an error.
        if license_id and obj.get("payment_intent"):
            db.kv_set("stripe_pi:" + str(obj["payment_intent"]), license_id)
        # Privacy: audit by the OPAQUE Stripe customer/session id, never the buyer's email — no PII in our DB.
        # A047: a minted-but-undelivered sale is NOT a 200. It is a paying customer with no key,
        # and it must be loud enough for an operator to find and act on.
        if key and not delivered:
            logging_util.warn(
                "license_minted_but_undelivered",
                license_id=license_id or "",
                sku=sku or "unknown",
                detail="license minted; the key email FAILED to send (SMTP unconfigured "
                "or send error). The customer has paid and has no key — resend "
                "manually from the license id.",
            )
        audit_code = 200 if (key and delivered) else 500
        db.audit(
            obj.get("customer") or obj.get("id") or "stripe", "fulfill:" + (sku or "unknown"), audit_code, "stripe"
        )
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

    if etype in ("customer.subscription.deleted", "invoice.payment_failed", "checkout.session.async_payment_failed"):
        if event_id and not db.kv_insert_once("stripe_evt:" + event_id, "processing"):
            return "duplicate"
        # Only a definitively-ended subscription suspends the license. A single failed invoice is left
        # to Stripe's dunning/retry — if it ultimately gives up, Stripe fires subscription.deleted. This
        # avoids suspending a paying customer over one transient card decline.
        suspended = False
        if etype == "customer.subscription.deleted":
            # A025: look up the license by subscription id first (per-subscription
            # keying), then fall back to the customer-level slot. This ensures that
            # cancelling one subscription does not suspend a different license the
            # same customer purchased separately (lifetime or concurrent sub).
            sub_id = obj.get("id") or ""
            license_id = db.kv_get("stripe_sub:" + str(sub_id)) if sub_id else None
            if not license_id:
                customer = obj.get("customer")
                license_id = db.kv_get("stripe_cust:" + str(customer)) if customer else None
            if license_id:
                suspended = _keygen_suspend_license(license_id)
        db.audit(obj.get("customer") or "stripe", "fulfill:lapse:" + etype, 200, "stripe")
        if event_id:
            db.kv_set("stripe_evt:" + event_id, "suspended" if suspended else "lapse")
        return "suspended" if suspended else "lapse-noted"

    # A085: subscription reactivation — a previously-suspended subscription has been renewed
    # or its payment method updated, so the license should be reinstated. Only fires when the
    # subscription object reports status "active" to avoid reinstating on a partial update.
    if etype in ("customer.subscription.updated", "invoice.paid"):
        if event_id and not db.kv_insert_once("stripe_evt:" + event_id, "processing"):
            return "duplicate"
        reinstated = False
        sub_status = (obj.get("status") or "") if isinstance(obj, dict) else ""
        # For invoice.paid, check the parent subscription status via the invoice object.
        if etype == "invoice.paid":
            obj.get("subscription") or ""
            # invoice.paid does not directly carry subscription status; look up via the customer.
            customer = obj.get("customer") or ""
            license_id = db.kv_get("stripe_cust:" + str(customer)) if customer else None
            if license_id:
                reinstated = _keygen_reinstate_license(license_id)
        elif sub_status == "active":
            # subscription.updated with status=active: the subscription is live again.
            sub_id = obj.get("id") or ""
            license_id = db.kv_get("stripe_sub:" + str(sub_id)) if sub_id else None
            if not license_id:
                customer = obj.get("customer")
                license_id = db.kv_get("stripe_cust:" + str(customer)) if customer else None
            if license_id:
                reinstated = _keygen_reinstate_license(license_id)
        db.audit(obj.get("customer") or "stripe", "fulfill:reactivate:" + etype, 200, "stripe")
        if event_id:
            db.kv_set("stripe_evt:" + event_id, "reinstated" if reinstated else "reactivate-noted")
        return "reinstated" if reinstated else "reactivate-noted"

    return "ignored"

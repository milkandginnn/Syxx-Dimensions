"""Regression tests for the D4 payment/licensing path — checkout, fulfillment, suspend-on-cancel.

Pytest-native, no network: the Stripe HTTP layer (`stripe_checkout._request`), the Keygen mint/
suspend helpers, and the db/mailer side effects are all monkeypatched. Run from the Server/ dir:

    python3 -m pytest tests/test_payments.py -v
"""

import json
import os
from unittest.mock import patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import config, db, fulfillment, mailer  # noqa: E402
from syxx_server import stripe_checkout as sc  # noqa: E402

# Live SKUs (from config.STRIPE_SKUS). Tiers are PRICES on one product per platform, so the
# catalog is keyed by SKU: a product id no longer identifies what someone bought.
MAC_MONTHLY = "syxx_mac_monthly_sub"
MAC_YEARLY = "syxx_mac_yearly_sub"
WIN_MONTHLY = "syxx_win_monthly_sub"
WIN_YEARLY = "syxx_win_yearly_sub"
DEB_MONTHLY = "syxx_deb_monthly_sub"
DEB_YEARLY = "syxx_deb_yearly_sub"
# Pre-refactor product ids, still arriving on in-flight sessions and from shipped app builds.
LEGACY_MAC_PRODUCT = "prod_Uq652gww87MGYT"
LEGACY_WIN_PRODUCT = "prod_Uq6ODZbCgLWfIU"


def _evt(eid, etype, obj):
    return {"id": eid, "type": etype, "data": {"object": obj}}


@pytest.fixture
def payment_env():
    """Installs in-memory fakes for every external side effect; restores them after.

    Yields a dict with the fake stores: `kv` (in-memory key/value store), `sent`
    (mailer recipients), and `created_at` (write timestamps). Tests use those in
    place of the old TestCase instance attributes.
    """
    kv = {}
    sent = []
    _orig = {
        "kv_get": db.kv_get,
        "kv_set": db.kv_set,
        "kv_insert_once": db.kv_insert_once,
        "kv_get_with_created_at": getattr(db, "kv_get_with_created_at", None),
        "audit": db.audit,
        "send": mailer.send,
        "mint": fulfillment._keygen_create_license,
        "suspend": fulfillment._keygen_suspend_license,
        "request": sc._request,
    }
    import time as _time

    created_at = {}
    db.kv_get = lambda k, d=None: kv.get(k, d)

    def _kv_set(k, v):
        # Stamp created_at only on first write so lease age tracks the original claim.
        if k not in kv:
            created_at[k] = int(_time.time())
        kv[k] = v

    db.kv_set = _kv_set

    def reserve(k, v):
        if k in kv:
            return False
        kv[k] = v
        created_at[k] = int(_time.time())
        return True

    db.kv_insert_once = reserve

    def _get_with_created(k):
        if k not in kv:
            return None, None
        return kv[k], created_at.get(k)

    db.kv_get_with_created_at = _get_with_created
    db.audit = lambda *a, **k: None
    mailer.send = lambda to, s, b, html=None, attachments=None: sent.append(to) or True

    yield {"kv": kv, "sent": sent, "created_at": created_at}

    db.kv_get, db.kv_set = _orig["kv_get"], _orig["kv_set"]
    db.kv_insert_once, db.audit = _orig["kv_insert_once"], _orig["audit"]
    if _orig["kv_get_with_created_at"] is not None:
        db.kv_get_with_created_at = _orig["kv_get_with_created_at"]
    mailer.send = _orig["send"]
    fulfillment._keygen_create_license = _orig["mint"]
    fulfillment._keygen_suspend_license = _orig["suspend"]
    sc._request = _orig["request"]


def _prices(**overrides):
    """Env giving every SKU its own price id, which config.stripe_price_for() reads live.

    Shipped price ids are "" until they exist in Stripe; these tests are about checkout
    MECHANICS. test_a_sku_without_a_price_id_is_refused covers the blank case explicitly, and
    the preflight reports it at boot.
    """
    env = {e["priceid_env"]: "price_" + sku for sku, e in config.STRIPE_SKUS.items()}
    env.update(overrides)
    return env


def _mock_stripe(recurring=True, active=True):
    calls = []

    def fake(path, method, params=None, idempotency_key=None):
        calls.append((path, method, params))
        if path.startswith("/prices/"):
            return {
                "id": path.split("/prices/")[1],
                "active": active,
                "recurring": {"interval": "month"} if recurring else None,
            }
        return {"id": "cs_1", "url": "https://checkout.stripe.com/cs_1"}

    sc._request = fake
    return calls


def test_unknown_sku_rejected_before_any_http(payment_env):
    calls = _mock_stripe()
    with pytest.raises(sc.StripeError):
        sc.create_checkout_session("syxx_not_ours")
    assert calls == []  # rejected by allow-list, never hit Stripe


def test_recurring_price_yields_subscription_mode(payment_env):
    calls = _mock_stripe(recurring=True)
    with patch.dict(os.environ, _prices()):
        url, sid = sc.create_checkout_session(MAC_MONTHLY)
    assert (url, sid) == ("https://checkout.stripe.com/cs_1", "cs_1")
    p = calls[-1][2]
    assert p["mode"] == "subscription"
    assert p["line_items[0][price]"] == "price_" + MAC_MONTHLY
    assert p["metadata[sku]"] == MAC_MONTHLY
    assert p["client_reference_id"] == MAC_MONTHLY
    assert p["managed_payments[enabled]"] == "true"


def test_each_sku_checks_out_at_ITS_OWN_price_never_a_default(payment_env):
    """THE reason this table is keyed by SKU and carries an explicit price id.

    Tiers are now multiple PRICES on ONE product. The old code fetched the product's
    `default_price`, which is a single price — so with a monthly + yearly price on one product,
    every tier would have checked out at whichever Stripe called default. A buyer clicks Yearly,
    gets charged monthly, and the session looks perfectly successful: no error, no warning, wrong
    money. Assert each SKU carries its OWN price to Stripe, and that no /products/ lookup happens
    at all — a default_price fetch reappearing is the bug returning.
    """
    for sku in (MAC_MONTHLY, MAC_YEARLY, WIN_MONTHLY, WIN_YEARLY, DEB_MONTHLY, DEB_YEARLY):
        calls = _mock_stripe()
        with patch.dict(os.environ, _prices()):
            sc.create_checkout_session(sku)
        assert calls[-1][2]["line_items[0][price]"] == "price_" + sku, sku
        assert calls[-1][2]["metadata[sku]"] == sku, sku
        assert not [c for c in calls if c[0].startswith("/products/")], sku


def test_yearly_does_not_collide_with_monthly(payment_env):
    """Same product id, different tier — correct and expected. What must differ is the resolved
    PRICE and the policy. Under the old product-keyed table these two were indistinguishable:
    one entry, one policy, one price."""
    with patch.dict(os.environ, _prices()):
        for m, y in ((MAC_MONTHLY, MAC_YEARLY), (WIN_MONTHLY, WIN_YEARLY), (DEB_MONTHLY, DEB_YEARLY)):
            assert config.stripe_product_for(m) == config.stripe_product_for(y)
            assert config.stripe_price_for(m) != config.stripe_price_for(y)
            assert config.STRIPE_SKUS[m]["policy_env"] != config.STRIPE_SKUS[y]["policy_env"]


def test_one_time_price_yields_payment_mode(payment_env):
    calls = _mock_stripe(recurring=False)
    with patch.dict(os.environ, _prices()):
        sc.create_checkout_session(MAC_MONTHLY)
    assert calls[-1][2]["mode"] == "payment"


def test_a_sku_without_a_price_id_is_refused(payment_env):
    """A SKU added to the catalog before its price exists in Stripe. It must 503, NOT fall back
    to the product's default price — falling back is how a buyer gets charged for a tier nobody
    selected. Every shipped SKU has a real price id, so this uses a new one; the guard exists
    for the NEXT tier somebody adds."""
    calls = _mock_stripe()
    table = dict(config.STRIPE_SKUS)
    table["syxx_mac_unpriced_sub"] = dict(
        table[MAC_YEARLY], price_default="", priceid_env="SYXX_STRIPE_PRICEID_MAC_UNPRICED"
    )
    with patch.object(config, "STRIPE_SKUS", table):
        with pytest.raises(sc.StripeError):
            sc.create_checkout_session("syxx_mac_unpriced_sub")
    assert calls == []  # refused locally; never reached Stripe


def test_an_archived_price_is_refused_before_the_buyer_commits(payment_env):
    _mock_stripe(active=False)
    with patch.dict(os.environ, _prices()):
        # A225 Fix 3: assert the exception message identifies "archived", not just
        # any StripeError. StripeError is raised for unknown SKU, blank price, HTTP
        # failure, bad JSON, and every signature fault — the message distinguishes them.
        with pytest.raises(sc.StripeError) as ctx:
            sc.create_checkout_session(WIN_YEARLY)
        assert "archived" in str(ctx.value), "the error must name the archived price, not just 'StripeError'"


@pytest.fixture
def fulfillment_env(payment_env):
    os.environ["SYXX_KEYGEN_POLICY_MAC_MONTHLY"] = "pol_mac_monthly"
    os.environ.pop("SYXX_KEYGEN_POLICY_MAC_YEARLY", None)  # yearly policy deliberately unset
    # mint succeeds only for the mac-monthly policy; everything else returns no key.
    fulfillment._keygen_create_license = lambda policy, email: (
        ("KEY-1", "lic_1") if policy == "pol_mac_monthly" else (None, None)
    )
    yield payment_env
    os.environ.pop("SYXX_KEYGEN_POLICY_MAC_MONTHLY", None)


def _session(sku, email="a@b.com", customer="cus_1", status="paid"):
    return {
        "payment_status": status,
        "metadata": {"sku": sku},
        "customer_details": {"email": email},
        "customer": customer,
    }


def _legacy_session(product, email="a@b.com", customer="cus_1", status="paid"):
    """A session created BEFORE the SKU refactor: it stamped metadata[product]."""
    return {
        "payment_status": status,
        "metadata": {"product": product},
        "customer_details": {"email": email},
        "customer": customer,
    }


def test_paid_with_policy_mints_emails_and_maps_customer(fulfillment_env):
    env = fulfillment_env
    r = fulfillment.handle_event(_evt("e1", "checkout.session.completed", _session(MAC_MONTHLY)))
    assert r == "fulfilled"
    assert env["sent"] == ["a@b.com"]
    assert env["kv"]["stripe_evt:e1"] == "minted"
    assert env["kv"]["stripe_cust:cus_1"] == "lic_1"


def test_mint_succeeds_but_email_FAILS_is_never_marked_minted(fulfillment_env):
    """A047: a paid buyer whose key email failed must NOT be recorded as fulfilled.

    Before the fix, mailer.send()'s return was DISCARDED and the event was locked "minted"
    purely because a key existed. Result: customer charged, license minted, key never delivered,
    and Stripe's redelivery reported "duplicate" and never retried. Silent, and it costs a real
    person a real key they paid for.
    """
    env = fulfillment_env
    mailer.send = lambda to, s, b, html=None, attachments=None: False  # SMTP down / unconfigured
    r = fulfillment.handle_event(_evt("e-mail-fail", "checkout.session.completed", _session(MAC_MONTHLY)))

    assert r == "minted-undelivered"
    assert r != "fulfilled", "an undelivered key is not a fulfilled sale"
    assert env["kv"]["stripe_evt:e-mail-fail"] == "minted-undelivered"
    assert env["kv"]["stripe_evt:e-mail-fail"] != "minted"
    # The license id must still be mapped so an operator can resend from it.
    assert env["kv"]["stripe_cust:cus_1"] == "lic_1"


def test_minted_undelivered_does_not_double_mint_on_redelivery(fulfillment_env):
    """The trap in fixing A047: marking the event retryable would make Stripe's redelivery mint
    a SECOND license for ONE payment. "minted-undelivered" is terminal FOR MINTING — the
    license already exists — while still being distinct from a delivered "minted"."""
    mailer.send = lambda to, s, b, html=None, attachments=None: False
    fulfillment.handle_event(_evt("e-dup", "checkout.session.completed", _session(MAC_MONTHLY)))

    mints = []
    orig = fulfillment._keygen_create_license
    fulfillment._keygen_create_license = lambda p, e: mints.append(p) or ("KEY-2", "lic_2")
    try:
        r = fulfillment.handle_event(_evt("e-dup", "checkout.session.completed", _session(MAC_MONTHLY)))
    finally:
        fulfillment._keygen_create_license = orig

    assert r == "duplicate"
    assert mints == [], "a redelivery must NEVER mint a second license for one payment"


def test_successful_delivery_still_marks_minted(fulfillment_env):
    """Mutation guard: the fix must reject only FAILED delivery. Without this, always
    reporting "minted-undelivered" would pass the tests above while breaking every real sale."""
    env = fulfillment_env
    r = fulfillment.handle_event(_evt("e-ok", "checkout.session.completed", _session(MAC_MONTHLY)))
    assert r == "fulfilled"
    assert env["kv"]["stripe_evt:e-ok"] == "minted"


def test_missing_policy_records_pending_never_drops(fulfillment_env):
    env = fulfillment_env
    r = fulfillment.handle_event(_evt("e2", "checkout.session.completed", _session(MAC_YEARLY)))
    assert r == "pending-mint"
    assert env["kv"]["stripe_evt:e2"] == "failed"  # "failed" so Stripe retries re-enter
    assert env["sent"] == []  # no key -> no email


def test_unpaid_session_is_held_for_async(fulfillment_env):
    env = fulfillment_env
    r = fulfillment.handle_event(
        _evt("e3", "checkout.session.completed", _session(MAC_MONTHLY, status="unpaid"))
    )
    assert r == "await-async"
    assert "stripe_evt:e3" not in env["kv"]  # not marked done; async success will follow


def test_async_payment_succeeded_mints_even_without_paid_flag(fulfillment_env):
    s = _session(MAC_MONTHLY, status="unpaid")  # async event doesn't require payment_status
    r = fulfillment.handle_event(_evt("e4", "checkout.session.async_payment_succeeded", s))
    assert r == "fulfilled"


def test_duplicate_event_is_idempotent(fulfillment_env):
    env = fulfillment_env
    fulfillment.handle_event(_evt("e1", "checkout.session.completed", _session(MAC_MONTHLY)))
    r = fulfillment.handle_event(_evt("e1", "checkout.session.completed", _session(MAC_MONTHLY)))
    assert r == "duplicate"
    assert len(env["sent"]) == 1  # not emailed twice


def test_keygen_outage_then_retry_mints_on_second_attempt(fulfillment_env):
    """A019: a Keygen 5xx on the first delivery followed by a Stripe retry must mint
    the license on the retry (not silently drop it as 'duplicate')."""
    env = fulfillment_env
    call_count = [0]

    def mint_fail_then_succeed(policy, email):
        call_count[0] += 1
        if call_count[0] == 1:
            return (None, None)  # first call: Keygen failure
        return ("KEY-RETRY", "lic_retry")  # retry: success

    fulfillment._keygen_create_license = mint_fail_then_succeed

    # First delivery — Keygen fails
    r1 = fulfillment.handle_event(
        _evt("e_retry", "checkout.session.completed", _session(MAC_MONTHLY, customer="cus_retry"))
    )
    assert r1 == "pending-mint"
    assert env["kv"]["stripe_evt:e_retry"] == "failed"  # retryable state
    assert env["sent"] == []  # no email on failure

    # Second delivery (same event id, Stripe retry) — Keygen succeeds
    r2 = fulfillment.handle_event(
        _evt("e_retry", "checkout.session.completed", _session(MAC_MONTHLY, customer="cus_retry"))
    )
    assert r2 == "fulfilled"
    assert env["kv"]["stripe_evt:e_retry"] == "minted"
    assert env["sent"] == ["a@b.com"]  # email sent on retry


def test_keygen_double_retry_still_one_license(fulfillment_env):
    """A019: when both deliveries succeed, exactly one license is minted (idempotent)."""
    mint_count = [0]

    def mint(policy, email):
        mint_count[0] += 1
        return ("KEY-1", "lic_1")

    fulfillment._keygen_create_license = mint

    fulfillment.handle_event(
        _evt("e_double", "checkout.session.completed", _session(MAC_MONTHLY, customer="cus_double"))
    )
    r2 = fulfillment.handle_event(
        _evt("e_double", "checkout.session.completed", _session(MAC_MONTHLY, customer="cus_double"))
    )
    assert r2 == "duplicate"  # second delivery: already minted
    assert mint_count[0] == 1  # only called once


def test_concurrent_deliveries_mint_exactly_once(fulfillment_env):
    """A013: two deliveries of the SAME Stripe event racing in parallel must mint exactly one
    license. The regression was a non-atomic kv_get + kv_set claim: both threads read "absent"
    and both mint (double-mint, double-email). The fix claims the event with the atomic
    compare-and-set kv_insert_once, so exactly one thread wins.

    Mutation-proof, not timing-luck: `racing_get` forces the two readers of the event key to
    rendezvous so a non-atomic claim is GUARANTEED to have both read "absent" before either
    writes. Under the atomic claim, only the LOSING delivery ever reads the event key (the
    winner reserved it via kv_insert_once), so the rendezvous simply times out and returns the
    already-written state — no double mint."""
    import threading
    import time

    env = fulfillment_env
    evt_key = "stripe_evt:e_conc"

    # Atomic reserve modelling the real DB's INSERT OR IGNORE (INSERT-only, serialised).
    reserve_lock = threading.Lock()

    def atomic_reserve(k, v):
        with reserve_lock:
            if k in env["kv"]:
                return False
            env["kv"][k] = v
            return True

    db.kv_insert_once = atomic_reserve

    # Rendezvous the readers of the event key. A non-atomic (get-then-set) claim calls this for
    # BOTH deliveries as the FIRST step: both block until count==2, and the SNAPSHOT is taken
    # while both are still inside racing_get (so before EITHER can write) -> both observe
    # "absent" and both mint (the double-mint the assertions catch). The atomic claim calls
    # this only from the loser after kv_insert_once already failed, so count stays 1, no
    # snapshot is taken, the wait times out, and it reads the winner's live "processing"/
    # "minted" state -> duplicate. No timing luck: the snapshot is what makes it deterministic.
    cond = threading.Condition()
    readers = {"n": 0}

    def racing_get(k, d=None):
        if k == evt_key:
            with cond:
                readers["n"] += 1
                if readers["n"] == 2:
                    # Both deliveries are provably inside the claim now; neither has written yet
                    # (writes happen only after this returns), so the key is genuinely absent.
                    readers["snapshot"] = env["kv"].get(k, d)
                cond.notify_all()
                deadline = time.time() + 0.5
                while readers["n"] < 2 and time.time() < deadline:
                    cond.wait(timeout=max(0.0, deadline - time.time()))
                if "snapshot" in readers:
                    return readers["snapshot"]
        return env["kv"].get(k, d)

    db.kv_get = racing_get

    mint_calls = []
    mint_lock = threading.Lock()

    def mint(policy, email):
        with mint_lock:
            mint_calls.append(policy)
        return ("KEY-C", "lic_c")

    fulfillment._keygen_create_license = mint

    start = threading.Barrier(2, timeout=5)
    results = {}

    def deliver(tag):
        start.wait()  # release both deliveries together to maximise the race window
        results[tag] = fulfillment.handle_event(
            _evt("e_conc", "checkout.session.completed", _session(MAC_MONTHLY, customer="cus_conc"))
        )

    threads = [threading.Thread(target=deliver, args=(t,)) for t in ("a", "b")]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    assert len(mint_calls) == 1, "double-mint: the event claim was not atomic"
    assert sorted(results.values()) == ["duplicate", "fulfilled"]
    assert env["kv"]["stripe_evt:e_conc"] == "minted"
    assert env["sent"] == ["a@b.com"]  # emailed exactly once


def test_subscription_deleted_suspends_mapped_license(fulfillment_env):
    env = fulfillment_env
    env["kv"]["stripe_cust:cus_sub"] = "lic_sub"
    suspended = []
    fulfillment._keygen_suspend_license = lambda lid: suspended.append(lid) or True
    r = fulfillment.handle_event(_evt("d1", "customer.subscription.deleted", {"customer": "cus_sub"}))
    assert r == "suspended"
    assert suspended == ["lic_sub"]
    assert env["kv"]["stripe_evt:d1"] == "suspended"


def test_failed_invoice_does_not_suspend(fulfillment_env):
    env = fulfillment_env
    env["kv"]["stripe_cust:cus_sub"] = "lic_sub"
    suspended = []
    fulfillment._keygen_suspend_license = lambda lid: suspended.append(lid) or True
    r = fulfillment.handle_event(_evt("d2", "invoice.payment_failed", {"customer": "cus_sub"}))
    assert r == "lapse-noted"
    assert suspended == []  # dunning still running — never over one decline


def test_subscription_deleted_without_map_is_noted(fulfillment_env):
    r = fulfillment.handle_event(_evt("d3", "customer.subscription.deleted", {"customer": "cus_none"}))
    assert r == "lapse-noted"


def test_unknown_event_ignored(fulfillment_env):
    assert fulfillment.handle_event(_evt("d4", "payment_intent.created", {})) == "ignored"


@pytest.fixture
def chargeback_env(payment_env):
    """Phase 2.2 G2: a refund or a created dispute must REVOKE entitlement, idempotently.

    Before this, charge.refunded and dispute.created were logged and nothing else, so a refunded
    customer kept a working Keygen license and working API keys indefinitely. These tests pin the
    three-valued outcome, the fail-closed behaviour when Keygen cannot confirm, and the replay
    guard. Deleting the revocation body, or collapsing the outcome to a bool, must turn them RED.
    """
    suspended = []
    locally_revoked = []
    fulfillment._keygen_suspend_license = lambda lid: suspended.append(lid) or True
    orig_subject_for = db.keygen_subject_for_license_id
    orig_revoke_all = db.api_key_revoke_all_for_subject
    # lic_ref belongs to local subject lic_abc; anything else is UNKNOWN (None).
    db.keygen_subject_for_license_id = lambda lid: "lic_abc" if lid == "lic_ref" else None
    db.api_key_revoke_all_for_subject = lambda s: locally_revoked.append(s)
    yield {**payment_env, "suspended": suspended, "locally_revoked": locally_revoked}
    db.keygen_subject_for_license_id = orig_subject_for
    db.api_key_revoke_all_for_subject = orig_revoke_all


def _refund(eid="r1", customer="cus_ref", pi=None):
    obj = {"id": "ch_1", "amount": 395, "currency": "usd", "customer": customer}
    if pi:
        obj["payment_intent"] = pi
    return _evt(eid, "charge.refunded", obj)


def test_refund_revokes_the_mapped_license(chargeback_env):
    """The money went back, so the entitlement must too."""
    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    r = fulfillment.handle_event(_refund())
    assert r == "revoked"
    assert env["suspended"] == ["lic_ref"], "a refunded customer must lose their Keygen license"
    assert env["kv"]["stripe_evt:r1"] == "revoked"


def test_refund_also_revokes_local_api_keys(chargeback_env):
    """Suspending upstream is not enough: this server's own API keys are checked locally."""
    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    fulfillment.handle_event(_refund())
    assert env["locally_revoked"] == ["lic_abc"]


def test_dispute_created_revokes_via_payment_intent(chargeback_env):
    """A Stripe dispute object carries NO customer field — only payment_intent/charge."""
    env = chargeback_env
    env["kv"]["stripe_pi:pi_9"] = "lic_ref"
    r = fulfillment.handle_event(
        _evt(
            "dp1",
            "charge.dispute.created",
            {"id": "dp_1", "amount": 395, "currency": "usd", "payment_intent": "pi_9"},
        )
    )
    assert r == "revoked"
    assert env["suspended"] == ["lic_ref"]


def test_bare_dispute_created_spelling_also_revokes(chargeback_env):
    env = chargeback_env
    env["kv"]["stripe_pi:pi_9"] = "lic_ref"
    r = fulfillment.handle_event(_evt("dp2", "dispute.created", {"payment_intent": "pi_9"}))
    assert r == "revoked"


def test_replayed_refund_does_not_double_apply(chargeback_env):
    """Idempotency: Stripe retries, and a replay must not re-apply."""
    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    first = fulfillment.handle_event(_refund("r_dup"))
    second = fulfillment.handle_event(_refund("r_dup"))
    assert first == "revoked"
    assert second == "duplicate", "a redelivered event must not re-run the revocation"
    assert env["suspended"] == ["lic_ref"], "suspend ran twice: the event claim is not idempotent"


def test_concurrent_deliveries_revoke_exactly_once(chargeback_env):
    """Two simultaneous deliveries of one event: compare-and-set, not check-then-act."""
    import threading

    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    results = {}
    start = threading.Barrier(2)

    def deliver(n):
        start.wait()
        results[n] = fulfillment.handle_event(_refund("r_conc"))

    threads = [threading.Thread(target=deliver, args=(i,)) for i in range(2)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    assert len(env["suspended"]) == 1, "double-revoke: the event claim was not atomic"
    assert sorted(results.values()) == ["duplicate", "revoked"]


def test_unresolvable_refund_is_reported_not_silently_ok(chargeback_env):
    """Fail closed: unknown is never reported as handled.

    No license maps to this charge. The customer has their money back AND a live license.

    That is the one outcome an operator must see, so it may not share a return value with a
    completed revocation.
    """
    env = chargeback_env
    r = fulfillment.handle_event(_refund("r_unres", customer="cus_nobody"))
    assert r == "revoke-unresolved"
    assert r != "revoked"
    assert env["suspended"] == []
    assert env["kv"]["stripe_evt:r_unres"] == "revoke-unresolved"


def test_keygen_failure_is_revoke_failed_never_revoked(chargeback_env):
    """Keygen did not confirm, so upstream entitlement is UNKNOWN — not 'revoked'."""
    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    fulfillment._keygen_suspend_license = lambda lid: False
    r = fulfillment.handle_event(_refund("r_fail"))
    assert r == "revoke-failed"
    assert r != "revoked", "an unconfirmed suspend must never read as a completed revocation"


def test_keygen_failure_still_revokes_local_keys(chargeback_env):
    """Fail CLOSED: this server's own surface locks down even when Keygen is unreachable."""
    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    fulfillment._keygen_suspend_license = lambda lid: False
    fulfillment.handle_event(_refund("r_fail2"))
    assert env["locally_revoked"] == ["lic_abc"]


def test_a_failed_revocation_is_retryable_on_redelivery(chargeback_env):
    """A Keygen outage must not permanently strand the revocation."""
    env = chargeback_env
    env["kv"]["stripe_cust:cus_ref"] = "lic_ref"
    fulfillment._keygen_suspend_license = lambda lid: False
    assert fulfillment.handle_event(_refund("r_retry")) == "revoke-failed"
    fulfillment._keygen_suspend_license = lambda lid: env["suspended"].append(lid) or True
    assert fulfillment.handle_event(_refund("r_retry")) == "revoked"
    assert env["suspended"] == ["lic_ref"]


def test_dispute_closed_does_not_revoke(chargeback_env):
    """A dispute that is merely progressing is not a confirmed negative.

    A dispute can be WON. Revoking a customer found legitimate is the mirror error."""
    env = chargeback_env
    env["kv"]["stripe_pi:pi_9"] = "lic_ref"
    r = fulfillment.handle_event(_evt("dp3", "charge.dispute.closed", {"payment_intent": "pi_9", "status": "won"}))
    assert r == "chargeback-logged"
    assert env["suspended"] == []


@pytest.fixture
def trial_env(payment_env):
    """Phase 2.2 G3: trial_period_days on the Checkout Session.

    Default is 0 (off) so shipped behaviour is unchanged until an operator opts in. When it IS
    set, it must reach Stripe, and it must NOT be attached to a one-time payment session, where
    `subscription_data` is invalid and would fail the whole checkout.
    """
    orig_trial = config.STRIPE_TRIAL_DAYS
    for k, v in {e["priceid_env"]: "price_" + sku for sku, e in config.STRIPE_SKUS.items()}.items():
        os.environ[k] = v
    yield payment_env
    config.STRIPE_TRIAL_DAYS = orig_trial
    for e in config.STRIPE_SKUS.values():
        os.environ.pop(e["priceid_env"], None)


def _capture(recurring=True):
    """Return the params dict the session-create POST would send."""
    seen = {}

    def fake(path, method, params=None, idempotency_key=None):
        if method == "GET":
            return {
                "id": path.rsplit("/", 1)[-1],
                "active": True,
                "recurring": {"interval": "month"} if recurring else None,
            }
        seen.update(params or {})
        return {"url": "https://checkout.stripe.test/s", "id": "cs_1"}

    sc._request = fake
    sc.create_checkout_session(MAC_MONTHLY)
    return seen


def test_default_is_no_trial_so_shipped_behaviour_is_unchanged(trial_env):
    assert config.STRIPE_TRIAL_DAYS == 0, "a nonzero DEFAULT gives product away on every purchase"
    config.STRIPE_TRIAL_DAYS = 0
    assert "subscription_data[trial_period_days]" not in _capture()


def test_configured_trial_reaches_stripe(trial_env):
    config.STRIPE_TRIAL_DAYS = 7
    params = _capture()
    assert params["subscription_data[trial_period_days]"] == "7"


def test_trial_is_stamped_into_metadata_so_it_is_distinguishable_from_a_sale(trial_env):
    """`payment_status: no_payment_required` cannot tell a trial from a 100%-off coupon."""
    config.STRIPE_TRIAL_DAYS = 7
    assert _capture()["metadata[trial_days]"] == "7"


def test_a_one_time_price_never_gets_subscription_data(trial_env):
    """Stripe rejects subscription_data on a `payment` session — it would break checkout."""
    config.STRIPE_TRIAL_DAYS = 7
    params = _capture(recurring=False)
    assert params["mode"] == "payment"
    assert "subscription_data[trial_period_days]" not in params


def test_a_negative_trial_is_clamped_not_forwarded(trial_env):
    os.environ["SYXX_STRIPE_TRIAL_DAYS"] = "-5"
    try:
        import importlib

        importlib.reload(config)
        assert config.STRIPE_TRIAL_DAYS == 0
    finally:
        os.environ.pop("SYXX_STRIPE_TRIAL_DAYS", None)
        import importlib

        importlib.reload(config)


def test_the_configured_limit_matches_the_published_promise(payment_env):
    """Phase 2.2 G4: a minted license carries a machine ceiling.

    See the KEYGEN_MAX_MACHINES comment in config.py: this is a DECLARED ceiling, not proven
    enforcement (no client activates a machine yet, and Keygen honours a per-license override only
    on a policy that permits one). What these tests pin is that the number we send is the number
    the published EULA promises, and that it is actually sent.

    Server/index.html:513 promises "up to five devices". Enforcing a different number
    would mean the product enforces something the customer was never told."""
    assert config.KEYGEN_MAX_MACHINES == 5


def test_mint_sends_max_machines(payment_env):
    sent = {}

    class _Resp:
        def read(self):
            return b'{"data":{"id":"lic_x","attributes":{"key":"KEY-X"}}}'

        def __enter__(self):
            return self

        def __exit__(self, *a):
            return False

    def fake_urlopen(req, timeout=None, context=None):
        sent["body"] = json.loads(req.data.decode())
        return _Resp()

    with (
        patch("syxx_server.fulfillment.urllib.request.urlopen", fake_urlopen),
        patch.object(config, "KEYGEN_ADMIN_TOKEN", "tok"),
        patch.object(config, "KEYGEN_BASE_URL", "https://keygen.test/v1/accounts/acct"),
    ):
        key, lic = fulfillment._keygen_create_license("pol_1", "a@b.com")
    assert (key, lic) == ("KEY-X", "lic_x")
    attrs = sent["body"]["data"]["attributes"]
    assert attrs["maxMachines"] == 5, "a minted license must carry the device ceiling"
    assert attrs["metadata"]["email"] == "a@b.com"


def test_a_zero_limit_omits_the_attribute_entirely(payment_env):
    sent = {}

    class _Resp:
        def read(self):
            return b'{"data":{"id":"lic_x","attributes":{"key":"KEY-X"}}}'

        def __enter__(self):
            return self

        def __exit__(self, *a):
            return False

    def fake_urlopen(req, timeout=None, context=None):
        sent["body"] = json.loads(req.data.decode())
        return _Resp()

    with (
        patch("syxx_server.fulfillment.urllib.request.urlopen", fake_urlopen),
        patch.object(config, "KEYGEN_ADMIN_TOKEN", "tok"),
        patch.object(config, "KEYGEN_MAX_MACHINES", 0),
        patch.object(config, "KEYGEN_BASE_URL", "https://keygen.test/v1/accounts/acct"),
    ):
        fulfillment._keygen_create_license("pol_1", "a@b.com")
    assert "maxMachines" not in sent["body"]["data"]["attributes"]


@pytest.fixture
def legacy_env(payment_env):
    """A checkout session created BEFORE the SKU refactor, completing AFTER it.

    Stripe Checkout sessions live ~24h, so a deploy lands in the middle of real buyers' sessions.
    Those stamped metadata[product], not metadata[sku]. Without translation the webhook resolves no
    policy and records the sale pending: a charged customer, no key, no error anywhere. The refactor
    is not free to ignore them just because the new code is prettier.
    """
    os.environ["SYXX_KEYGEN_POLICY_MAC_MONTHLY"] = "pol_mac_monthly"
    fulfillment._keygen_create_license = lambda policy, email: (
        ("KEY-legacy", "lic_legacy") if policy == "pol_mac_monthly" else (None, None)
    )
    yield payment_env
    os.environ.pop("SYXX_KEYGEN_POLICY_MAC_MONTHLY", None)


def _legacy(product):
    return {
        "payment_status": "paid",
        "metadata": {"product": product},
        "customer_details": {"email": "a@b.com"},
        "customer": "cus_legacy",
    }


def test_an_in_flight_pre_refactor_session_still_mints(legacy_env):
    env = legacy_env
    r = fulfillment.handle_event(_evt("legacy-1", "checkout.session.completed", _legacy(LEGACY_MAC_PRODUCT)))
    assert r == "fulfilled"
    assert env["sent"] == ["a@b.com"]


def test_the_legacy_map_resolves_to_the_monthly_tier(legacy_env):
    """Those sessions were created when each product had ONE price — the monthly one. Mapping
    them anywhere else would hand a buyer a tier they never paid for."""
    assert config.sku_for_legacy_product(LEGACY_MAC_PRODUCT) == MAC_MONTHLY
    assert config.sku_for_legacy_product(LEGACY_WIN_PRODUCT) == WIN_MONTHLY
    assert config.sku_for_legacy_product("prod_unknown") == ""


def test_a_session_with_neither_sku_nor_known_product_does_not_mint(legacy_env):
    """A225 Fix 2: fail closed — an unresolvable sale is recorded as pending-mint, never
    fulfilled. Previously asserted assertNotEqual(r, "fulfilled"), which six other result
    strings satisfy (including "ignored" — silently dropping a paid session). The exact
    expected result is "pending-mint": the session is recorded but no key is minted."""
    env = legacy_env
    r = fulfillment.handle_event(_evt("legacy-2", "checkout.session.completed", _legacy("prod_gone")))
    assert r == "pending-mint", "an unresolvable sale must be pending-mint, not silently ignored"
    assert env["sent"] == []


def test_keygen_policy_lookup_by_sku():
    os.environ["SYXX_KEYGEN_POLICY_WIN_YEARLY"] = "pol_win_yearly"
    try:
        assert config.keygen_policy_for(WIN_YEARLY) == "pol_win_yearly"
        assert config.keygen_policy_for("syxx_bogus") == ""
    finally:
        os.environ.pop("SYXX_KEYGEN_POLICY_WIN_YEARLY", None)


def test_every_sku_is_present_and_has_a_distinct_policy():
    for sku in (MAC_MONTHLY, MAC_YEARLY, WIN_MONTHLY, WIN_YEARLY, DEB_MONTHLY, DEB_YEARLY):
        assert sku in config.STRIPE_SKUS
    envs = [e["policy_env"] for e in config.STRIPE_SKUS.values()]
    # Two SKUs sharing a policy would mint the same entitlement for different money.
    assert len(envs) == len(set(envs))


def test_perpetual_skus_are_not_sellable():
    """Not listed on Stripe by decision (selling one promises this server runs forever), and
    their old product ids no longer exist in the account — a listed SKU would raise on the
    price fetch. The POLICIES still exist so a key can be minted by hand."""
    assert not [s for s in config.STRIPE_SKUS if "perm" in s]
    for platform in config.PLATFORMS:
        assert platform in config.PERM_POLICY_ENVS


def test_trials_have_a_policy_for_every_platform_but_no_sku():
    assert not [s for s in config.STRIPE_SKUS if "trial" in s]
    for platform in config.PLATFORMS:
        assert platform in config.TRIAL_POLICY_ENVS


def test_the_deploy_template_and_the_code_cannot_drift():
    """THE test for the dead-variable class of bug.

    SYXX_KEYGEN_POLICY_{MAC,WIN}_TRIAL sat in the production env file for weeks doing NOTHING:
    no code referenced them. Nothing failed, nothing warned — an operator sets a variable, the
    server ignores it, and the only symptom is a trial that never mints. The inverse is worse:
    a policy the code reads but the template never mentions is a SKU that silently records every
    sale as pending.

    So pin the two lists to each other. Every policy env the code can read must appear in
    scripts/deploy.sh, and every one the template offers must be readable by the code.
    """
    import pathlib
    import re

    deploy = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "deploy.sh"
    assert deploy.is_file(), "cannot find scripts/deploy.sh to pin against"
    text = deploy.read_text()
    in_template = set(
        re.findall(r"^(SYXX_(?:KEYGEN_POLICY|STRIPE_PRODID|STRIPE_PRICEID)_[A-Z_]+)=", text, re.MULTILINE)
    )
    in_code = (
        {e["policy_env"] for e in config.STRIPE_SKUS.values()}
        | {e["prodid_env"] for e in config.STRIPE_SKUS.values()}
        | {e["priceid_env"] for e in config.STRIPE_SKUS.values()}
        | set(config.TRIAL_POLICY_ENVS.values())
        | set(config.PERM_POLICY_ENVS.values())
    )

    assert (
        in_code - in_template == set()
    ), "vars the code reads but deploy.sh never seeds: an operator has no way to "
    "know they exist, so the SKU records sales pending forever"
    assert (
        in_template - in_code == set()
    ), "vars deploy.sh seeds but no code reads: setting them does nothing (this is "
    "exactly what happened to the TRIAL vars in production)"


def test_a_price_variable_is_never_shared_between_skus():
    """BEHAVIOURAL, not label-checking.

    The first version compared each entry's `priceid_env` STRING to its sibling's — the table
    describing itself. Pointing every SKU's resolved price at ONE shared variable left those
    labels untouched, so the test passed while the catalog charged a single price for every
    tier: exactly the bug it existed to catch. Assert the RESOLVED value under a real env.
    """
    env = {e["priceid_env"]: "price_" + sku for sku, e in config.STRIPE_SKUS.items()}
    with patch.dict(os.environ, env):
        resolved = {sku: config.stripe_price_for(sku) for sku in config.STRIPE_SKUS}
    assert len(set(resolved.values())) == len(resolved), f"two SKUs resolved to the SAME price: {resolved!r}"
    for sku, price in resolved.items():
        assert price == "price_" + sku


def test_monthly_and_yearly_share_a_product_but_never_a_price():
    """Same product id is CORRECT — two prices on one product. The price must differ."""
    env = {e["priceid_env"]: "price_" + sku for sku, e in config.STRIPE_SKUS.items()}
    with patch.dict(os.environ, env):
        for m, y in ((MAC_MONTHLY, MAC_YEARLY), (WIN_MONTHLY, WIN_YEARLY), (DEB_MONTHLY, DEB_YEARLY)):
            assert config.stripe_product_for(m) == config.stripe_product_for(y)
            assert config.stripe_price_for(m) != config.stripe_price_for(y)


def test_the_live_product_ids_are_defaults_not_requirements():
    """A deploy that sets no Stripe product variables must still sell the real products: a
    blank override cannot silently empty the catalog."""
    with patch.dict(os.environ, {"SYXX_STRIPE_PRODID_MAC_MONTHLY": ""}):
        assert config.stripe_product_for(MAC_MONTHLY) == "prod_Uq652gww87MGYT"
    with patch.dict(os.environ, {"SYXX_STRIPE_PRODID_MAC_MONTHLY": "prod_testmode"}):
        assert config.stripe_product_for(MAC_MONTHLY) == "prod_testmode"


def test_the_live_price_ids_are_defaults_and_every_sku_has_one():
    """The catalog ships sellable: a deploy that sets no Stripe variables charges the real
    prices. A SKU with no default would 503 its buy button with nothing in the env file to
    explain why."""
    env = {e["priceid_env"]: "" for e in config.STRIPE_SKUS.values()}
    with patch.dict(os.environ, env):
        for sku in config.STRIPE_SKUS:
            assert config.stripe_price_for(sku).startswith("price_"), sku


def test_the_shipped_price_ids_are_the_real_ones_and_are_distinct():
    """Pinned against the Stripe dashboard, 2026-07-16. A typo here charges the wrong tier and
    nothing upstream would catch it — Stripe validates that a price EXISTS, never that it is
    the one the buyer clicked."""
    assert config.stripe_price_for(MAC_MONTHLY) == "price_1TqPt10Owf1ALdZDpXWZqB2r"
    assert config.stripe_price_for(MAC_YEARLY) == "price_1Ttwpw0Owf1ALdZDhfZ2LTlz"
    assert config.stripe_price_for(WIN_MONTHLY) == "price_1TqQBk0Owf1ALdZDKVoRroPD"
    assert config.stripe_price_for(WIN_YEARLY) == "price_1TtxCb0Owf1ALdZDwybXuuz9"
    assert config.stripe_price_for(DEB_MONTHLY) == "price_1TtxLd0Owf1ALdZDD175vTFK"
    assert config.stripe_price_for(DEB_YEARLY) == "price_1TtxNs0Owf1ALdZDOT0EMfce"
    prices = [config.stripe_price_for(s) for s in config.STRIPE_SKUS]
    assert len(set(prices)) == len(prices), "two SKUs share a price id"


def test_an_env_override_beats_the_compiled_default():
    with patch.dict(os.environ, {config.STRIPE_SKUS[MAC_YEARLY]["priceid_env"]: "price_test"}):
        assert config.stripe_price_for(MAC_YEARLY) == "price_test"


@pytest.fixture
def webhook_secret():
    orig = config.STRIPE_WEBHOOK_SECRET
    config.STRIPE_WEBHOOK_SECRET = "whsec_test"
    yield
    config.STRIPE_WEBHOOK_SECRET = orig


def _sign_webhook(payload, ts):
    import hashlib
    import hmac

    return hmac.new(b"whsec_test", f"{ts}.".encode() + payload, hashlib.sha256).hexdigest()


def test_valid_signature_parses_event(webhook_secret):
    payload = b'{"id":"evt_1","type":"checkout.session.completed"}'
    sig = _sign_webhook(payload, 1000)
    event = sc.verify_webhook(payload, f"t=1000,v1={sig}", tolerance=10**12, now=1000)
    assert event["id"] == "evt_1"


def test_secret_rotation_accepts_any_matching_v1_signature(webhook_secret):
    payload = b'{"id":"evt_1","type":"checkout.session.completed"}'
    sig = _sign_webhook(payload, 1000)
    event = sc.verify_webhook(payload, f"t=1000,v1=deadbeef,v1={sig}", tolerance=10**12, now=1000)
    assert event["id"] == "evt_1"


def test_tampered_signature_rejected(webhook_secret):
    with pytest.raises(sc.StripeError):
        sc.verify_webhook(b'{"id":"evt_1"}', "t=1000,v1=deadbeef", tolerance=10**12, now=1000)


def test_replayed_old_timestamp_rejected(webhook_secret):
    payload = b'{"id":"evt_1"}'
    sig = _sign_webhook(payload, 1000)
    # A225 Fix 3: assert the exception message identifies "timestamp", not just
    # any StripeError. The same exception type is raised for malformed headers,
    # signature mismatches, and missing secrets — the message distinguishes them.
    with pytest.raises(sc.StripeError) as ctx:
        sc.verify_webhook(payload, f"t=1000,v1={sig}", tolerance=300, now=1000 + 10_000)
    assert "timestamp" in str(ctx.value), "the error must name the timestamp fault, not just 'StripeError'"


def test_malformed_header_rejected(webhook_secret):
    with pytest.raises(sc.StripeError):
        sc.verify_webhook(b"{}", "garbage", now=1000)


def test_no_signature_header_rejected(webhook_secret):
    """E223: a webhook arriving with no Stripe-Signature header at all must be rejected.
    Without this guard, an attacker who knows the endpoint can POST a forged event body
    and bypass signature verification entirely by simply omitting the header."""
    with pytest.raises(sc.StripeError) as ctx:
        sc.verify_webhook(b'{"id":"evt_1"}', None, now=1000)
    assert "malformed" in str(ctx.value)


def test_empty_signature_header_rejected(webhook_secret):
    """E223: an empty string signature header is functionally absent — must also be rejected."""
    with pytest.raises(sc.StripeError) as ctx:
        sc.verify_webhook(b'{"id":"evt_1"}', "", now=1000)
    assert "malformed" in str(ctx.value)

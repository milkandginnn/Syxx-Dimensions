"""Unit tests for syxx_server/stripe_checkout.py — webhook verification, billing portal,
checkout edge cases, and SPKI pin helpers.

Complements test_payments.py which covers checkout session creation and fulfillment.
This file focuses on:
  - verify_webhook: valid/invalid signatures, timestamp skew, malformed headers
  - create_billing_portal_session: happy path, missing params
  - Trial days metadata injection
  - _extract_spki_sha256_b64 error handling

No real Stripe API calls. All HTTP is monkeypatched.
"""

import hashlib
import hmac
import json
import os
import time
from unittest.mock import patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import config  # noqa: E402
from syxx_server import stripe_checkout as sc  # noqa: E402


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def webhook_env(monkeypatch):
    """Set up a known webhook secret for signature verification tests."""
    secret = "whsec_test_secret_that_is_long_enough_32chars!"
    monkeypatch.setattr(config, "STRIPE_WEBHOOK_SECRET", secret)
    monkeypatch.setattr(config, "STRIPE_WEBHOOK_TOLERANCE", 300)
    return secret


@pytest.fixture
def no_stripe_key(monkeypatch):
    """Temporarily blank the Stripe secret key."""
    monkeypatch.setattr(config, "STRIPE_SECRET_KEY", "")


@pytest.fixture
def mock_request():
    """Replace sc._request with a spy; restores after."""
    calls = []
    orig = sc._request

    def fake(path, method, params=None, idempotency_key=None):
        calls.append((path, method, params, idempotency_key))
        if path.startswith("/prices/"):
            return {"id": path.split("/prices/")[1], "active": True, "recurring": {"interval": "month"}}
        if path == "/checkout/sessions":
            return {"id": "cs_test_1", "url": "https://checkout.stripe.com/cs_test_1"}
        if path == "/billing_portal/sessions":
            return {"id": "bps_1", "url": "https://billing.stripe.com/bps_1"}
        return {}

    sc._request = fake
    yield calls
    sc._request = orig


def _make_signature(secret, payload_bytes, timestamp=None):
    """Build a valid Stripe-Signature header value for testing."""
    ts = str(timestamp or int(time.time()))
    signed = ts.encode() + b"." + payload_bytes
    sig = hmac.new(secret.encode(), signed, hashlib.sha256).hexdigest()
    return f"t={ts},v1={sig}"


# ---------------------------------------------------------------------------
# verify_webhook — signature verification
# ---------------------------------------------------------------------------

class TestVerifyWebhook:
    def test_valid_signature_returns_parsed_event(self, webhook_env):
        payload = json.dumps({"id": "evt_1", "type": "checkout.session.completed"}).encode()
        sig = _make_signature(webhook_env, payload)
        result = sc.verify_webhook(payload, sig)
        assert result["id"] == "evt_1"
        assert result["type"] == "checkout.session.completed"

    def test_invalid_signature_raises(self, webhook_env):
        payload = b'{"id":"evt_1"}'
        sig = _make_signature(webhook_env, payload)
        # Tamper with the payload after signing
        tampered = b'{"id":"evt_EVIL"}'
        with pytest.raises(sc.StripeError, match="signature mismatch"):
            sc.verify_webhook(tampered, sig)

    def test_expired_timestamp_raises(self, webhook_env):
        payload = b'{"id":"evt_1"}'
        old_ts = int(time.time()) - 600  # 10 minutes ago
        sig = _make_signature(webhook_env, payload, timestamp=old_ts)
        with pytest.raises(sc.StripeError, match="timestamp outside tolerance"):
            sc.verify_webhook(payload, sig)

    def test_future_timestamp_beyond_tolerance_raises(self, webhook_env):
        payload = b'{"id":"evt_1"}'
        future_ts = int(time.time()) + 600
        sig = _make_signature(webhook_env, payload, timestamp=future_ts)
        with pytest.raises(sc.StripeError, match="timestamp outside tolerance"):
            sc.verify_webhook(payload, sig)

    def test_malformed_header_no_timestamp(self, webhook_env):
        with pytest.raises(sc.StripeError, match="malformed signature header"):
            sc.verify_webhook(b'{}', "v1=abc123")

    def test_malformed_header_no_v1(self, webhook_env):
        with pytest.raises(sc.StripeError, match="malformed signature header"):
            sc.verify_webhook(b'{}', f"t={int(time.time())}")

    def test_malformed_header_empty(self, webhook_env):
        with pytest.raises(sc.StripeError, match="malformed signature header"):
            sc.verify_webhook(b'{}', "")

    def test_malformed_header_non_numeric_timestamp(self, webhook_env):
        with pytest.raises(sc.StripeError, match="malformed signature header"):
            sc.verify_webhook(b'{}', "t=notanumber,v1=abc")

    def test_missing_webhook_secret_raises(self, monkeypatch):
        monkeypatch.setattr(config, "STRIPE_WEBHOOK_SECRET", "")
        with pytest.raises(sc.StripeError, match="STRIPE_WEBHOOK_SECRET not configured"):
            sc.verify_webhook(b'{}', "t=1,v1=abc")

    def test_custom_tolerance_accepted(self, webhook_env):
        """A timestamp within a generous custom tolerance must pass."""
        payload = b'{"ok":true}'
        ts = int(time.time()) - 200  # 200s ago
        sig = _make_signature(webhook_env, payload, timestamp=ts)
        result = sc.verify_webhook(payload, sig, tolerance=500)
        assert result == {"ok": True}

    def test_explicit_now_parameter(self, webhook_env):
        """The now= parameter lets tests pin the clock."""
        payload = b'{"id":"evt_now"}'
        fixed_now = 1_700_000_000
        sig = _make_signature(webhook_env, payload, timestamp=fixed_now)
        result = sc.verify_webhook(payload, sig, now=fixed_now)
        assert result["id"] == "evt_now"

    def test_multiple_v1_signatures_any_valid(self, webhook_env):
        """Stripe can send multiple v1 signatures; any one matching is sufficient."""
        payload = b'{"id":"evt_multi"}'
        ts = str(int(time.time()))
        valid_sig = hmac.new(
            webhook_env.encode(), ts.encode() + b"." + payload, hashlib.sha256
        ).hexdigest()
        sig_header = f"t={ts},v1=wrong_signature,v1={valid_sig}"
        result = sc.verify_webhook(payload, sig_header)
        assert result["id"] == "evt_multi"


# ---------------------------------------------------------------------------
# create_billing_portal_session
# ---------------------------------------------------------------------------

class TestBillingPortal:
    def test_happy_path(self, mock_request, monkeypatch):
        monkeypatch.setattr(config, "STRIPE_SECRET_KEY", "sk_test_fake")
        result = sc.create_billing_portal_session("cus_123")
        assert result["url"] == "https://billing.stripe.com/bps_1"
        path, method, params, _ = mock_request[-1]
        assert path == "/billing_portal/sessions"
        assert method == "POST"
        assert params["customer"] == "cus_123"

    def test_custom_return_url(self, mock_request, monkeypatch):
        monkeypatch.setattr(config, "STRIPE_SECRET_KEY", "sk_test_fake")
        sc.create_billing_portal_session("cus_123", return_url="https://syxx.app/account")
        _, _, params, _ = mock_request[-1]
        assert params["return_url"] == "https://syxx.app/account"

    def test_missing_customer_id_raises(self, monkeypatch):
        monkeypatch.setattr(config, "STRIPE_SECRET_KEY", "sk_test_fake")
        with pytest.raises(sc.StripeError, match="customer_id is required"):
            sc.create_billing_portal_session("")

    def test_missing_secret_key_raises(self, no_stripe_key):
        with pytest.raises(sc.StripeError, match="STRIPE_SECRET_KEY not configured"):
            sc.create_billing_portal_session("cus_123")


# ---------------------------------------------------------------------------
# create_checkout_session — trial metadata injection
# ---------------------------------------------------------------------------

class TestCheckoutTrial:
    def test_subscription_with_trial_days_includes_metadata(self, mock_request, monkeypatch):
        monkeypatch.setattr(config, "STRIPE_TRIAL_DAYS", 14)
        sc.create_checkout_session("syxx_mac_monthly_sub")
        _, _, params, _ = mock_request[-1]
        assert params["subscription_data[trial_period_days]"] == "14"
        assert params["metadata[trial_days]"] == "14"

    def test_one_time_payment_never_includes_trial(self, mock_request, monkeypatch):
        """Trial data is invalid on payment-mode sessions; must not be injected."""
        monkeypatch.setattr(config, "STRIPE_TRIAL_DAYS", 14)
        # Override the mock to return a one-time price
        orig = sc._request

        def fake_no_recur(path, method, params=None, idempotency_key=None):
            if path.startswith("/prices/"):
                return {"id": "price_123", "active": True, "recurring": None}
            return orig(path, method, params, idempotency_key)

        sc._request = fake_no_recur
        try:
            sc.create_checkout_session("syxx_mac_monthly_sub")
        finally:
            sc._request = orig
        _, _, params, _ = mock_request[-1]
        assert "subscription_data[trial_period_days]" not in params

    def test_zero_trial_days_no_trial_metadata(self, mock_request, monkeypatch):
        monkeypatch.setattr(config, "STRIPE_TRIAL_DAYS", 0)
        sc.create_checkout_session("syxx_mac_monthly_sub")
        _, _, params, _ = mock_request[-1]
        assert "subscription_data[trial_period_days]" not in params


# ---------------------------------------------------------------------------
# create_checkout_session — error paths
# ---------------------------------------------------------------------------

class TestCheckoutErrors:
    def test_unknown_sku_raises(self):
        with pytest.raises(sc.StripeError, match="unknown sku"):
            sc.create_checkout_session("syxx_does_not_exist")

    def test_missing_secret_key_raises(self, no_stripe_key):
        with pytest.raises(sc.StripeError, match="STRIPE_SECRET_KEY not configured"):
            sc.create_checkout_session("syxx_mac_monthly_sub")

    def test_idempotency_key_is_passed(self, mock_request):
        sc.create_checkout_session("syxx_mac_monthly_sub", idempotency_key="my-key-123")
        _, _, _, idem_key = mock_request[-1]
        assert idem_key == "my-key-123"

    def test_auto_generated_idempotency_key_when_none(self, mock_request):
        sc.create_checkout_session("syxx_mac_monthly_sub")
        _, _, _, idem_key = mock_request[-1]
        assert idem_key is not None
        assert len(idem_key) > 0  # UUID was generated


# ---------------------------------------------------------------------------
# _extract_spki_sha256_b64 — SPKI pin extraction
# ---------------------------------------------------------------------------

class TestSPKIExtraction:
    def test_returns_empty_string_on_invalid_der(self):
        """Malformed DER bytes must return '' (fail-closed), never raise."""
        result = sc._extract_spki_sha256_b64(b"not a real certificate")
        assert result == ""

    def test_returns_empty_on_empty_input(self):
        result = sc._extract_spki_sha256_b64(b"")
        assert result == ""

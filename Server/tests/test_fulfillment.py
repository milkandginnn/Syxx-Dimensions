"""Tests for syxx_server.fulfillment — Stripe webhook order fulfillment.

Covers event routing, idempotency, chargeback revocation, and the
three-valued revoke outcome. Keygen and DB calls are mocked.
"""

import os
from unittest.mock import MagicMock, patch, call

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import fulfillment


# --- Event routing ----------------------------------------------------------


class TestHandleEvent:
    def test_checkout_completed_routes_to_mint(self):
        event = {
            "id": "evt_test_1",
            "type": "checkout.session.completed",
            "data": {
                "object": {
                    "payment_status": "paid",
                    "customer": "cus_test",
                    "metadata": {"sku": "syxx_mac_monthly_sub"},
                    "customer_details": {"email": "buyer@example.com"},
                }
            },
        }
        with patch.object(fulfillment, "_keygen_create_license", return_value=("KEY-123", "lic_123")):
            with patch.object(fulfillment.mailer, "send", return_value=True):
                with patch.object(fulfillment.db, "kv_insert_once", return_value=True):
                    with patch.object(fulfillment.db, "kv_set"):
                        with patch.object(fulfillment.db, "kv_get", return_value=None):
                            with patch.object(fulfillment.db, "kv_get_with_created_at", return_value=(None, None)):
                                with patch.object(fulfillment.db, "audit"):
                                    result = fulfillment.handle_event(event)
                                    assert result == "fulfilled"

    def test_unpaid_checkout_returns_await(self):
        event = {
            "id": "evt_test_2",
            "type": "checkout.session.completed",
            "data": {
                "object": {
                    "payment_status": "unpaid",
                    "metadata": {},
                }
            },
        }
        with patch.object(fulfillment.db, "kv_insert_once", return_value=True):
            with patch.object(fulfillment.db, "kv_get", return_value=None):
                with patch.object(fulfillment.db, "kv_get_with_created_at", return_value=(None, None)):
                    result = fulfillment.handle_event(event)
                    assert result == "await-async"

    def test_subscription_deleted_routes_to_suspend(self):
        event = {
            "id": "evt_test_3",
            "type": "customer.subscription.deleted",
            "data": {"object": {"id": "sub_test", "customer": "cus_test"}},
        }
        with patch.object(fulfillment.db, "kv_insert_once", return_value=True):
            with patch.object(fulfillment.db, "kv_get", return_value="lic_123"):
                with patch.object(fulfillment.db, "kv_set"):
                    with patch.object(fulfillment, "_keygen_suspend_license", return_value=True):
                        with patch.object(fulfillment.db, "keygen_subject_for_license_id", return_value=None):
                            with patch.object(fulfillment.db, "audit"):
                                result = fulfillment.handle_event(event)
                                assert result == "suspended"

    def test_unknown_event_type_ignored(self):
        event = {
            "id": "evt_test_4",
            "type": "some.unknown.event",
            "data": {"object": {}},
        }
        result = fulfillment.handle_event(event)
        assert result == "ignored"


# --- Chargeback events -------------------------------------------------------


class TestChargebackEvents:
    def test_chargeback_revoke_events(self):
        assert "charge.refunded" in fulfillment._CHARGEBACK_REVOKE_EVENTS
        assert "charge.dispute.created" in fulfillment._CHARGEBACK_REVOKE_EVENTS
        assert "dispute.created" in fulfillment._CHARGEBACK_REVOKE_EVENTS

    def test_chargeback_log_only_events(self):
        assert "charge.dispute.updated" in fulfillment._CHARGEBACK_LOG_ONLY_EVENTS
        assert "charge.dispute.closed" in fulfillment._CHARGEBACK_LOG_ONLY_EVENTS

    def test_log_only_events_not_in_revoke(self):
        for evt in fulfillment._CHARGEBACK_LOG_ONLY_EVENTS:
            assert evt not in fulfillment._CHARGEBACK_REVOKE_EVENTS


class TestLogChargeback:
    def test_returns_chargeback_logged(self):
        with patch.object(fulfillment.db, "audit"):
            result = fulfillment._log_chargeback(
                {"id": "ch_test", "amount": 395, "currency": "usd", "customer": "cus_test"},
                "evt_test",
                "charge.dispute.updated",
            )
            assert result == "chargeback-logged"

    def test_handles_non_dict_object(self):
        with patch.object(fulfillment.db, "audit"):
            result = fulfillment._log_chargeback("not a dict", "evt_test", "charge.refunded")
            assert result == "chargeback-logged"


# --- License mapping --------------------------------------------------------


class TestLicenseForChargeback:
    def test_maps_via_payment_intent(self):
        with patch.object(fulfillment.db, "kv_get") as mock_kv:
            mock_kv.return_value = "lic_123"
            result = fulfillment._license_for_chargeback({"payment_intent": "pi_test"})
            assert result == "lic_123"

    def test_maps_via_customer(self):
        with patch.object(fulfillment.db, "kv_get") as mock_kv:
            mock_kv.side_effect = lambda k: "lic_456" if "cust" in k else None
            result = fulfillment._license_for_chargeback({"customer": "cus_test"})
            assert result == "lic_456"

    def test_returns_none_when_unmapped(self):
        with patch.object(fulfillment.db, "kv_get", return_value=None):
            result = fulfillment._license_for_chargeback({"payment_intent": "pi_unknown"})
            assert result is None

    def test_non_dict_returns_none(self):
        assert fulfillment._license_for_chargeback("not a dict") is None


# --- Revoke outcomes ---------------------------------------------------------


class TestRevokeForChargeback:
    def test_revoked_on_success(self):
        with patch.object(fulfillment.db, "kv_insert_once", return_value=True):
            with patch.object(fulfillment, "_license_for_chargeback", return_value="lic_123"):
                with patch.object(fulfillment, "_keygen_suspend_license", return_value=True):
                    with patch.object(fulfillment.db, "keygen_subject_for_license_id", return_value="sub_1"):
                        with patch.object(fulfillment.db, "api_key_revoke_all_for_subject"):
                            with patch.object(fulfillment.db, "kv_set"):
                                with patch.object(fulfillment.db, "audit"):
                                    result = fulfillment._revoke_for_chargeback(
                                        {"id": "ch_test"}, "evt_test", "charge.refunded"
                                    )
                                    assert result == "revoked"

    def test_revoke_failed_when_keygen_fails(self):
        with patch.object(fulfillment.db, "kv_insert_once", return_value=True):
            with patch.object(fulfillment, "_license_for_chargeback", return_value="lic_123"):
                with patch.object(fulfillment, "_keygen_suspend_license", return_value=False):
                    with patch.object(fulfillment.db, "keygen_subject_for_license_id", return_value="sub_1"):
                        with patch.object(fulfillment.db, "api_key_revoke_all_for_subject"):
                            with patch.object(fulfillment.db, "kv_set"):
                                with patch.object(fulfillment.db, "audit"):
                                    result = fulfillment._revoke_for_chargeback(
                                        {"id": "ch_test"}, "evt_test", "charge.refunded"
                                    )
                                    assert result == "revoke-failed"

    def test_revoke_unresolved_when_no_license(self):
        with patch.object(fulfillment.db, "kv_insert_once", return_value=True):
            with patch.object(fulfillment, "_license_for_chargeback", return_value=None):
                with patch.object(fulfillment.db, "kv_set"):
                    with patch.object(fulfillment.db, "audit"):
                        result = fulfillment._revoke_for_chargeback(
                            {"id": "ch_test"}, "evt_test", "charge.refunded"
                        )
                        assert result == "revoke-unresolved"

    def test_duplicate_on_replay(self):
        with patch.object(fulfillment.db, "kv_insert_once", return_value=False):
            with patch.object(fulfillment.db, "kv_get", return_value="revoked"):
                result = fulfillment._revoke_for_chargeback(
                    {"id": "ch_test"}, "evt_test", "charge.refunded"
                )
                assert result == "duplicate"


# --- Keygen helpers ----------------------------------------------------------


class TestKeygenHelpers:
    def test_create_license_no_token(self):
        orig = fulfillment.config.KEYGEN_ADMIN_TOKEN
        try:
            fulfillment.config.KEYGEN_ADMIN_TOKEN = ""
            key, lid = fulfillment._keygen_create_license("pol_123", "user@test.com")
            assert key is None
            assert lid is None
        finally:
            fulfillment.config.KEYGEN_ADMIN_TOKEN = orig

    def test_create_license_no_policy(self):
        key, lid = fulfillment._keygen_create_license("", "user@test.com")
        assert key is None
        assert lid is None

    def test_suspend_no_token(self):
        orig = fulfillment.config.KEYGEN_ADMIN_TOKEN
        try:
            fulfillment.config.KEYGEN_ADMIN_TOKEN = ""
            assert fulfillment._keygen_suspend_license("lic_123") is False
        finally:
            fulfillment.config.KEYGEN_ADMIN_TOKEN = orig

    def test_suspend_no_license_id(self):
        assert fulfillment._keygen_suspend_license("") is False

    def test_reinstate_no_token(self):
        orig = fulfillment.config.KEYGEN_ADMIN_TOKEN
        try:
            fulfillment.config.KEYGEN_ADMIN_TOKEN = ""
            assert fulfillment._keygen_reinstate_license("lic_123") is False
        finally:
            fulfillment.config.KEYGEN_ADMIN_TOKEN = orig

    def test_reinstate_no_license_id(self):
        assert fulfillment._keygen_reinstate_license("") is False

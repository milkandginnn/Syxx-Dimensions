"""Tests for syxx_server.config — environment-driven configuration helpers.

Covers the three parsing primitives (_b, _i, _s, _list), the SKU/policy
resolution functions, and the Stripe catalog integrity invariants.
"""

import importlib
import os
from unittest.mock import patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")


# --- _b (boolean parsing) ---------------------------------------------------


class TestB:
    """A131: tri-state boolean parsing with safe defaults."""

    def _reload(self, env):
        with patch.dict(os.environ, env, clear=False):
            from syxx_server import config
            importlib.reload(config)
            return config

    def test_true_values(self):
        from syxx_server.config import _b
        for v in ("1", "true", "yes", "on", "TRUE", "Yes", "ON"):
            with patch.dict(os.environ, {"_TEST_BOOL": v}):
                assert _b("_TEST_BOOL") is True

    def test_false_values(self):
        from syxx_server.config import _b
        for v in ("0", "false", "no", "off", "FALSE", "No", "OFF"):
            with patch.dict(os.environ, {"_TEST_BOOL": v}):
                assert _b("_TEST_BOOL") is False

    def test_unset_returns_default_false(self):
        from syxx_server.config import _b
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("_TEST_UNSET", None)
            assert _b("_TEST_UNSET") is False

    def test_unset_returns_default_true(self):
        from syxx_server.config import _b
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("_TEST_UNSET", None)
            assert _b("_TEST_UNSET", default=True) is True

    def test_empty_string_returns_default(self):
        from syxx_server.config import _b
        with patch.dict(os.environ, {"_TEST_EMPTY": ""}):
            assert _b("_TEST_EMPTY", default=True) is True
            assert _b("_TEST_EMPTY", default=False) is False

    def test_unrecognised_returns_default_not_coerced(self):
        """A131: 'enabled', 'TRUE!', 'always' must NOT silently coerce."""
        from syxx_server.config import _b
        for v in ("enabled", "TRUE!", "always", "2", "maybe"):
            with patch.dict(os.environ, {"_TEST_BAD": v}):
                assert _b("_TEST_BAD", default=True) is True
                assert _b("_TEST_BAD", default=False) is False


# --- _i (integer parsing) ---------------------------------------------------


class TestI:
    def test_valid_int(self):
        from syxx_server.config import _i
        with patch.dict(os.environ, {"_TEST_INT": "42"}):
            assert _i("_TEST_INT", 0) == 42

    def test_empty_returns_default(self):
        from syxx_server.config import _i
        with patch.dict(os.environ, {"_TEST_INT": ""}):
            assert _i("_TEST_INT", 99) == 99

    def test_unset_returns_default(self):
        from syxx_server.config import _i
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("_TEST_INT_UNSET", None)
            assert _i("_TEST_INT_UNSET", 7) == 7

    def test_non_numeric_returns_default(self):
        from syxx_server.config import _i
        with patch.dict(os.environ, {"_TEST_INT_BAD": "thirty"}):
            assert _i("_TEST_INT_BAD", 30) == 30

    def test_whitespace_stripped(self):
        from syxx_server.config import _i
        with patch.dict(os.environ, {"_TEST_INT_WS": "  100  "}):
            assert _i("_TEST_INT_WS", 0) == 100


# --- _s (string) and _list --------------------------------------------------


class TestS:
    def test_returns_value(self):
        from syxx_server.config import _s
        with patch.dict(os.environ, {"_TEST_S": "hello"}):
            assert _s("_TEST_S", "default") == "hello"

    def test_empty_returns_default(self):
        from syxx_server.config import _s
        with patch.dict(os.environ, {"_TEST_S": ""}):
            assert _s("_TEST_S", "fallback") == "fallback"

    def test_unset_returns_default(self):
        from syxx_server.config import _s
        with patch.dict(os.environ, {}, clear=False):
            os.environ.pop("_TEST_S_UNSET", None)
            assert _s("_TEST_S_UNSET", "def") == "def"


class TestList:
    def test_comma_separated(self):
        from syxx_server.config import _list
        with patch.dict(os.environ, {"_TEST_LIST": "a,b,c"}):
            assert _list("_TEST_LIST", []) == ["a", "b", "c"]

    def test_deduplication(self):
        from syxx_server.config import _list
        with patch.dict(os.environ, {"_TEST_LIST": "a,b,a,c,b"}):
            result = _list("_TEST_LIST", [])
            assert result == ["a", "b", "c"]

    def test_empty_returns_default(self):
        from syxx_server.config import _list
        with patch.dict(os.environ, {"_TEST_LIST": ""}):
            assert _list("_TEST_LIST", ["x", "y"]) == ["x", "y"]

    def test_whitespace_stripped(self):
        from syxx_server.config import _list
        with patch.dict(os.environ, {"_TEST_LIST": " a , b , c "}):
            assert _list("_TEST_LIST", []) == ["a", "b", "c"]


# --- SKU / policy resolution ------------------------------------------------


class TestSKUResolution:
    def test_stripe_product_for_known_sku(self):
        from syxx_server import config
        result = config.stripe_product_for("syxx_mac_monthly_sub")
        assert result != ""

    def test_stripe_product_for_unknown_sku(self):
        from syxx_server import config
        assert config.stripe_product_for("nonexistent") == ""

    def test_stripe_price_for_known_sku(self):
        from syxx_server import config
        result = config.stripe_price_for("syxx_mac_monthly_sub")
        assert result != ""
        assert result.startswith("price_")

    def test_stripe_price_for_unknown_sku(self):
        from syxx_server import config
        assert config.stripe_price_for("nonexistent") == ""

    def test_keygen_policy_for_unknown_sku(self):
        from syxx_server import config
        assert config.keygen_policy_for("nonexistent") == ""

    def test_sku_for_legacy_product_known(self):
        from syxx_server import config
        assert config.sku_for_legacy_product("prod_Uq652gww87MGYT") == "syxx_mac_monthly_sub"

    def test_sku_for_legacy_product_unknown(self):
        from syxx_server import config
        assert config.sku_for_legacy_product("prod_NONEXISTENT") == ""

    def test_trial_policy_for_known_platform(self):
        from syxx_server import config
        # Returns '' when env var is unset, which is valid
        result = config.trial_policy_for("macos")
        assert isinstance(result, str)

    def test_trial_policy_for_unknown_platform(self):
        from syxx_server import config
        assert config.trial_policy_for("freebsd") == ""

    def test_perm_policy_for_known_platform(self):
        from syxx_server import config
        result = config.perm_policy_for("macos")
        assert isinstance(result, str)

    def test_perm_policy_for_unknown_platform(self):
        from syxx_server import config
        assert config.perm_policy_for("freebsd") == ""


# --- Catalog integrity ------------------------------------------------------


class TestCatalogIntegrity:
    def test_all_skus_have_product_defaults(self):
        from syxx_server import config
        for sku, entry in config.STRIPE_SKUS.items():
            assert entry["product_default"], f"{sku} missing product_default"

    def test_all_skus_have_price_defaults(self):
        from syxx_server import config
        for sku, entry in config.STRIPE_SKUS.items():
            assert entry["price_default"], f"{sku} missing price_default"
            assert entry["price_default"].startswith("price_"), f"{sku} price_default not a Stripe price id"

    def test_platforms_tuple(self):
        from syxx_server import config
        assert "macos" in config.PLATFORMS
        assert "windows" in config.PLATFORMS
        assert "debian" in config.PLATFORMS

    def test_trial_policy_envs_cover_platforms(self):
        from syxx_server import config
        for p in config.PLATFORMS:
            assert p in config.TRIAL_POLICY_ENVS

    def test_perm_policy_envs_cover_platforms(self):
        from syxx_server import config
        for p in config.PLATFORMS:
            assert p in config.PERM_POLICY_ENVS

    def test_stripe_trial_days_non_negative(self):
        from syxx_server import config
        assert config.STRIPE_TRIAL_DAYS >= 0

    def test_keygen_max_machines_non_negative(self):
        from syxx_server import config
        assert config.KEYGEN_MAX_MACHINES >= 0

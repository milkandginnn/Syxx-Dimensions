"""Tests for syxx_server.ratelimit — per-subject token-bucket rate limiter.

Covers allow/deny decisions, token refill, bucket pruning, and the A083
hard cap on bucket count.
"""

import os
import time
from unittest.mock import patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import ratelimit


@pytest.fixture(autouse=True)
def _clean_buckets():
    """Reset bucket state before each test."""
    ratelimit._buckets.clear()
    ratelimit._last_prune_ms = 0
    yield
    ratelimit._buckets.clear()
    ratelimit._last_prune_ms = 0


class TestAllow:
    def test_first_request_allowed(self):
        assert ratelimit.allow("test-key", capacity=5, refill_millis=1000) is True

    def test_exhaust_capacity(self):
        capacity = 3
        for _ in range(capacity):
            assert ratelimit.allow("exhaust", capacity=capacity, refill_millis=10000) is True
        assert ratelimit.allow("exhaust", capacity=capacity, refill_millis=10000) is False

    def test_separate_keys_independent(self):
        assert ratelimit.allow("key-a", capacity=1, refill_millis=10000) is True
        assert ratelimit.allow("key-b", capacity=1, refill_millis=10000) is True
        # key-a exhausted
        assert ratelimit.allow("key-a", capacity=1, refill_millis=10000) is False
        # key-b exhausted
        assert ratelimit.allow("key-b", capacity=1, refill_millis=10000) is False

    def test_denied_request_stores_bucket(self):
        ratelimit.allow("store-test", capacity=1, refill_millis=10000)
        ratelimit.allow("store-test", capacity=1, refill_millis=10000)
        assert "store-test" in ratelimit._buckets


class TestRefill:
    def test_refill_restores_tokens(self):
        """After enough time passes, tokens should refill."""
        ratelimit.allow("refill", capacity=1, refill_millis=100000)
        assert ratelimit.allow("refill", capacity=1, refill_millis=100000) is False
        # Simulate time passing by directly manipulating the bucket
        tokens, last = ratelimit._buckets["refill"]
        # Move last back by 200ms worth of time (200 * 100000 / 1000 = 20000 millitokens)
        ratelimit._buckets["refill"] = (tokens, last - 200)
        assert ratelimit.allow("refill", capacity=1, refill_millis=100000) is True


class TestPrune:
    def test_stale_buckets_removed(self):
        """Buckets not touched within _STALE_BUCKET_MS are pruned."""
        ratelimit.allow("stale-key", capacity=5, refill_millis=1000)
        assert "stale-key" in ratelimit._buckets
        # Backdate the bucket's last-access time
        tokens, last = ratelimit._buckets["stale-key"]
        ratelimit._buckets["stale-key"] = (tokens, last - ratelimit._STALE_BUCKET_MS - 1000)
        # Force a prune cycle
        ratelimit._last_prune_ms = 0
        ratelimit.allow("trigger-prune", capacity=5, refill_millis=1000)
        assert "stale-key" not in ratelimit._buckets

    def test_max_buckets_cap(self):
        """A083: bucket count is capped at _MAX_BUCKETS after a prune cycle."""
        original_max = ratelimit._MAX_BUCKETS
        try:
            ratelimit._MAX_BUCKETS = 5
            for i in range(10):
                ratelimit.allow(f"cap-{i}", capacity=5, refill_millis=1000)
            # Force a prune cycle — _prune only runs every _PRUNE_INTERVAL_MS
            ratelimit._last_prune_ms = 0
            ratelimit.allow("trigger-prune", capacity=5, refill_millis=1000)
            assert len(ratelimit._buckets) <= 6  # 5 cap + the trigger key
        finally:
            ratelimit._MAX_BUCKETS = original_max


class TestEdgeCases:
    def test_zero_capacity(self):
        """Capacity 0 means no tokens available."""
        assert ratelimit.allow("zero-cap", capacity=0, refill_millis=1000) is False

    def test_zero_refill(self):
        """Zero refill rate means tokens never recover."""
        ratelimit.allow("no-refill", capacity=1, refill_millis=0)
        assert ratelimit.allow("no-refill", capacity=1, refill_millis=0) is False

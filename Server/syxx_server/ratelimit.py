"""Per-subject token-bucket rate limiter (feature: rate limiting).

Keeps a single misbehaving client (or an abusive scan of the open-ended /warp
proxy) from starving the shared daemon. Buckets are keyed by entitlement subject
when authenticated, else by client IP for public routes.
"""
import threading
import time

from . import config

_lock = threading.Lock()
_buckets = {}  # key -> [tokens_millis, last_refill]
_last_prune_ms = 0
_PRUNE_INTERVAL_MS = 30_000
_STALE_BUCKET_MS = 120_000
# A083: hard cap on bucket count to prevent memory exhaustion from a
# high-cardinality burst (botnet, many distinct real peers) within the
# stale-window. When the cap is reached, the oldest (least-recently-used)
# buckets are evicted before new ones are allocated.
_MAX_BUCKETS = 10_000


def _prune(now_ms):
    """Remove buckets that haven't been touched in 2x the refill window to
    prevent unbounded memory growth (SRV-045). A083: also enforce a hard cap
    on bucket count, evicting the least-recently-used entries first."""
    stale_before = now_ms - _STALE_BUCKET_MS
    stale = [key for key, (_tokens, last) in _buckets.items() if last < stale_before]
    for key in stale:
        _buckets.pop(key, None)
    # A083: if stale eviction alone is insufficient, evict the oldest buckets
    # until we're under the cap. This bounds peak memory even under a
    # high-cardinality burst within the stale window.
    if len(_buckets) > _MAX_BUCKETS:
        by_last = sorted(_buckets.items(), key=lambda kv: kv[1][1])
        for key, _ in by_last[:len(_buckets) - _MAX_BUCKETS]:
            _buckets.pop(key, None)


def allow(key, capacity=None, refill_millis=None):
    global _last_prune_ms
    capacity = config.RATE_CAPACITY if capacity is None else capacity
    refill_ms = config.RATE_REFILL_MILLIS if refill_millis is None else refill_millis
    now = int(time.monotonic() * 1000)
    with _lock:
        if now - _last_prune_ms >= _PRUNE_INTERVAL_MS:
            _prune(now)
            _last_prune_ms = now
        tokens_ms, last = _buckets.get(key, (capacity * 1000, now))
        elapsed = now - last
        # A054: credit only whole millitokens earned, and advance `last` by exactly
        # the time whose refill we credited - NOT to `now`. Advancing to `now` on
        # every call (especially a denied one) discards the sub-token remainder:
        # under fast polling with a low refill rate, `elapsed * refill_ms // 1000`
        # truncates to 0, so a client polling faster than the refill threshold would
        # never accrue a token again (indefinite lockout). Keeping the uncredited
        # remainder in `last` lets elapsed keep accumulating until it crosses the
        # threshold, while never double-counting time already credited.
        refill = elapsed * refill_ms // 1000
        tokens_ms = min(capacity * 1000, tokens_ms + refill)
        credited_ms = (refill * 1000 // refill_ms) if refill_ms > 0 else elapsed
        last = last + credited_ms
        if tokens_ms < 1000:
            _buckets[key] = (tokens_ms, last)
            return False
        _buckets[key] = (tokens_ms - 1000, last)
        return True

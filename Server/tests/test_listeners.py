"""D-0 listener health registry + honest /readyz (encrypted-transports server plan).

The point of this module is that a listener can NEVER degrade silently — the exact SD-62 failure
where the DNS collector fell back to :5053 and /healthz still said 200. So the tests assert the
three transitions and, above all, that /readyz turns 503 the moment any listener is `down` and NOT
before.
"""

import os
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
from syxx_server import listeners  # noqa: E402


class RegistryTests(unittest.TestCase):
    def setUp(self):
        # Reset to the import-time default so tests do not leak state into each other.
        for name in ("http", "dns", "doh", "dot", "doq"):
            listeners.set_state(name, "unknown")

    def test_a_never_started_listener_reads_unknown_not_listening(self):
        # The whole reason for the default: absence of a report is not evidence of health.
        self.assertEqual(listeners.snapshot()["doq"]["state"], "unknown")

    def test_the_three_states_round_trip_with_detail(self):
        listeners.set_state("dns", "listening", detail="port 53")
        self.assertEqual(listeners.snapshot()["dns"], {"state": "listening", "detail": "port 53"})
        listeners.set_state("dns", "degraded-fallback", detail="on :5053")
        self.assertEqual(listeners.snapshot()["dns"]["state"], "degraded-fallback")
        listeners.set_state("dns", "down", detail="no port")
        self.assertEqual(listeners.snapshot()["dns"]["state"], "down")

    def test_an_invalid_state_raises_rather_than_corrupting_the_registry(self):
        # A typo'd state silently accepted would defeat down_listeners() and thus /readyz.
        with self.assertRaises(ValueError):
            listeners.set_state("dns", "ok")

    def test_down_listeners_reports_only_the_down_ones(self):
        listeners.set_state("http", "listening")
        listeners.set_state("dns", "degraded-fallback")
        listeners.set_state("dot", "down")
        listeners.set_state("doq", "down")
        self.assertEqual(listeners.down_listeners(), ["doq", "dot"])

    def test_degraded_fallback_is_NOT_down(self):
        # The deliberate design choice: degraded is up-but-wrong, and must not flip /readyz to 503
        # on its own — an operator sees the exact degraded state and acts, but the service that DOES
        # work (HTTP/analysis) is not reported unavailable because DNS fell to a fallback port.
        listeners.set_state("dns", "degraded-fallback")
        self.assertEqual(listeners.down_listeners(), [])


class ReadyzIntegrationTests(unittest.TestCase):
    """Drives the /readyz decision logic the way app.py does, without standing up the HTTP server:
    ready iff DB ok AND no listener down; degraded alone does not fail it."""

    def setUp(self):
        for name in ("http", "dns", "doh", "dot", "doq"):
            listeners.set_state(name, "unknown")

    def _ready(self):
        # Mirror of _route_readyz's decision (DB assumed reachable in these unit tests).
        return not listeners.down_listeners()

    def test_ready_when_nothing_is_down(self):
        listeners.set_state("http", "listening")
        listeners.set_state("dns", "listening")
        self.assertTrue(self._ready())

    def test_ready_stays_true_on_a_degraded_dns(self):
        listeners.set_state("http", "listening")
        listeners.set_state("dns", "degraded-fallback")
        self.assertTrue(self._ready(), "a degraded (not down) listener must not fail readiness")

    def test_not_ready_the_moment_any_listener_is_down(self):
        listeners.set_state("http", "listening")
        listeners.set_state("dns", "down")
        self.assertFalse(self._ready())
        self.assertIn("dns", listeners.down_listeners())


if __name__ == "__main__":
    unittest.main()

"""Tests for shipped-but-untested Dimension logic: broadcast windowing + passive-DNS baseline/drift.

Pure logic, no network: broadcast reads a temp JSON file; baseline's db query is monkeypatched.
Run from Server/:  python3 -m unittest tests.test_dimension -v
"""
import json
import os
import tempfile
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import broadcast, config, db, passivedns  # noqa: E402

DAY = 86400


class BroadcastTests(unittest.TestCase):
    def setUp(self):
        self._orig = config.BROADCAST_FILE
        fd, self._path = tempfile.mkstemp(suffix=".json")
        os.close(fd)
        config.BROADCAST_FILE = self._path

    def tearDown(self):
        config.BROADCAST_FILE = self._orig
        os.unlink(self._path)

    def _write(self, obj):
        with open(self._path, "w", encoding="utf-8") as f:
            json.dump(obj, f)

    def test_active_within_window_returns_whitelisted_fields(self):
        self._write({"id": "x", "title": "T", "body": "B", "severity": "warning",
                     "startsAt": 100, "endsAt": 200, "url": "https://a"})
        r = broadcast.current(now=150)
        self.assertEqual(r["id"], "x")
        self.assertEqual(r["severity"], "warning")
        self.assertEqual(r["url"], "https://a")

    def test_before_start_is_none(self):
        self._write({"id": "x", "startsAt": 100, "endsAt": 200})
        self.assertIsNone(broadcast.current(now=50))

    def test_after_end_is_none(self):
        self._write({"id": "x", "startsAt": 100, "endsAt": 200})
        self.assertIsNone(broadcast.current(now=250))

    def test_missing_id_is_none(self):
        self._write({"title": "no id"})
        self.assertIsNone(broadcast.current(now=150))

    def test_malformed_file_is_none(self):
        with open(self._path, "w", encoding="utf-8") as f:
            f.write("{ not json")
        self.assertIsNone(broadcast.current(now=150))

    def test_unknown_severity_clamped_to_info(self):
        self._write({"id": "x", "severity": "nuclear"})
        self.assertEqual(broadcast.current(now=150)["severity"], "info")

    def test_body_length_bounded(self):
        self._write({"id": "x", "body": "z" * 5000})
        self.assertEqual(len(broadcast.current(now=150)["body"]), 4000)

    def test_no_window_is_always_active(self):
        self._write({"id": "x"})
        self.assertIsNotNone(broadcast.current(now=999_999_999))

    def test_nonstring_url_dropped(self):
        self._write({"id": "x", "url": 12345})
        self.assertIsNone(broadcast.current(now=150)["url"])


class BaselineTests(unittest.TestCase):
    def setUp(self):
        self._orig = db.passive_query

    def tearDown(self):
        db.passive_query = self._orig

    def _records(self, recs):
        # A187: rtype filter is now pushed into SQL, so the mock must simulate it.
        def _mock(domain, limit, rtype=None):
            if rtype:
                return [r for r in recs if r["rtype"] == rtype.upper()]
            return recs
        db.passive_query = _mock

    @staticmethod
    def _rec(value, rtype, first_days_ago, last_days_ago, hits, now):
        return {"value": value, "rtype": rtype, "hits": hits,
                "first_seen": int(now - first_days_ago * DAY),
                "last_seen": int(now - last_days_ago * DAY)}

    def test_drift_when_established_and_new_coexist(self):
        now = 100 * DAY
        self._records([
            self._rec("1.1.1.1", "A", 20, 1, 50, now),      # established: age 19d, stale 1d
            self._rec("9.9.9.9", "A", 0.5, 0.1, 2, now),    # recently new: age <1d, stale <1d
        ])
        b = passivedns.baseline("Example.com", now=now)
        self.assertEqual(b["domain"], "example.com")           # lowercased
        self.assertIn("1.1.1.1", b["established"])
        self.assertIn("9.9.9.9", b["recentlyNew"])
        self.assertEqual(b["drift"], "suspected")

    def test_no_drift_when_only_established(self):
        now = 100 * DAY
        self._records([self._rec("1.1.1.1", "A", 20, 1, 50, now)])
        b = passivedns.baseline("example.com", now=now)
        self.assertEqual(b["drift"], "none")
        self.assertEqual(b["recentlyNew"], [])

    def test_established_excludes_stale_value(self):
        now = 100 * DAY
        self._records([self._rec("1.1.1.1", "A", 60, 40, 50, now)])   # last seen 40d ago -> stale
        b = passivedns.baseline("example.com", now=now)
        self.assertEqual(b["established"], [])

    def test_rtype_filter(self):
        now = 100 * DAY
        self._records([
            self._rec("1.1.1.1", "A", 20, 1, 5, now),
            self._rec("2606::1", "AAAA", 20, 1, 5, now),
        ])
        b = passivedns.baseline("example.com", rtype="a", now=now)
        self.assertEqual(b["recordCount"], 1)
        self.assertEqual(b["type"], "A")

    # --- A117: drift is TRI-STATE. "never observed" must never read as "no drift". ---
    #
    # The bare bool this replaced collapsed three different facts into `False`:
    # "we watched this domain for months and it never changed" and "we have never
    # seen this domain at all" were the same value, and the second is a result
    # rendered before any evidence exists (honesty spine #2 and #3).

    def test_never_observed_is_insufficient_data_not_no_drift(self):
        self._records([])
        b = passivedns.baseline("example.com", now=100 * DAY)
        self.assertEqual(b["drift"], "insufficient-data",
                         "a domain we have never observed cannot be reported drift-free")
        self.assertNotEqual(b["drift"], "none")
        self.assertEqual(b["recordCount"], 0)
        self.assertEqual(b["observationDays"], 0)

    def test_too_short_an_observation_window_is_insufficient_data(self):
        # Two days of history is not enough to say a value is established, so it is
        # not enough to say there is no drift either.
        now = 100 * DAY
        self._records([self._rec("1.1.1.1", "A", 2, 0.1, 4, now)])
        b = passivedns.baseline("example.com", now=now)
        self.assertEqual(b["drift"], "insufficient-data")
        self.assertEqual(b["established"], [])

    def test_only_stale_history_is_insufficient_data_not_no_drift(self):
        # Observed long enough, but nothing seen for 40 days: we do not know what the
        # domain resolves to today, so "none" would be a claim about the present made
        # entirely from the past.
        now = 100 * DAY
        self._records([self._rec("1.1.1.1", "A", 60, 40, 50, now)])
        b = passivedns.baseline("example.com", now=now)
        self.assertEqual(b["drift"], "insufficient-data")

    def test_drift_is_always_one_of_exactly_three_states(self):
        now = 100 * DAY
        cases = {
            "insufficient-data": [],
            "none": [self._rec("1.1.1.1", "A", 20, 1, 50, now)],
            "suspected": [self._rec("1.1.1.1", "A", 20, 1, 50, now),
                          self._rec("9.9.9.9", "A", 0.5, 0.1, 2, now)],
        }
        for expected, recs in cases.items():
            with self.subTest(expected=expected):
                self._records(recs)
                drift = passivedns.baseline("example.com", now=now)["drift"]
                self.assertEqual(drift, expected)
                self.assertNotIsInstance(drift, bool,
                                         "a security-critical result is never a bare bool")


# A204: class must be defined BEFORE `if __name__ == "__main__"`, or
# `python3 tests/test_dimension.py` silently drops it from unittest discovery.
class PassiveDNSPIIBoundaryTests(unittest.TestCase):
    """A023: /api/passive-dns is CROSS-READABLE — no per-record ownership check.

    The observing resolver IP must never cross that boundary: for a leak observation it is
    submitter PII, and the record was written by whoever happened to query. `query()` had no test
    (audit 2026-07-16), so re-introducing `resolver` into the public index would have been silent.
    """

    def test_query_never_returns_the_observing_resolver_ip(self):
        db.passive_record("pii-test.example", "A", "203.0.113.5", "leak", resolver="198.51.100.77")
        out = passivedns.query("pii-test.example")

        self.assertEqual(out["count"], 1, "the record must still be returned — only the PII is dropped")
        for r in out["records"]:
            self.assertNotIn("resolver", r, "submitter resolver IP must never reach the public index")
        # The PII must not leak via any other field either.
        self.assertNotIn("198.51.100.77", json.dumps(out),
                         "the resolver IP must not appear anywhere in the response")

    def test_query_still_returns_the_useful_fields(self):
        """Mutation guard: stripping EVERYTHING would pass the test above."""
        db.passive_record("pii-test2.example", "A", "203.0.113.9", "leak", resolver="198.51.100.77")
        out = passivedns.query("pii-test2.example")
        rec = out["records"][0]
        self.assertEqual(rec["value"], "203.0.113.9")


if __name__ == "__main__":
    unittest.main()


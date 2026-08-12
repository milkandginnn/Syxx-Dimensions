"""W16: binds the server's RRSIG validity-span plausibility guard (B047) to the shared fixture.

The normative span lives in ``corpus/dnssec/validity-span.json`` at the repo root — the SAME file the
C# suite reads (Windows/dotNET/Syxx.Tests/DnssecValiditySpanCorpusTests.cs) and the one a Swift test
can read unchanged. Before this module the server had NO test referencing MAX_RRSIG_VALIDITY_SPAN at
all: three runtimes each carried a bare 63_072_000 literal with nothing binding them together, so any
one of them could be retyped and only that runtime would notice. That is the defect — enforcement
drift, not a wrong value. The value itself is ratified and unchanged.

A missing, empty, or degenerate fixture FAILS rather than skipping: a green that asserts nothing is
the green that lies.

Run from Server/:  OTEL_TRACES_EXPORTER=none uv run pytest tests/test_dnssec_validity_span.py -q
"""
import json
import unittest
from pathlib import Path

from syxx_server import resolver

_FIXTURE = Path(__file__).resolve().parents[2] / "corpus" / "dnssec" / "validity-span.json"

_RATIFIED_SPAN_SECONDS = 63_072_000


def _load():
    if not _FIXTURE.exists():
        raise AssertionError(f"W16 validity-span fixture missing at {_FIXTURE}")
    text = _FIXTURE.read_text(encoding="utf-8")
    if not text.strip():
        raise AssertionError("W16 validity-span fixture is empty — refusing to pass vacuously")
    return json.loads(text)


def _span(case):
    """The window width the guard actually measures: mod-2**32, so wrap-straddling rows are real."""
    return (case["expiration"] - case["inception"]) & 0xFFFFFFFF


class DnssecValiditySpanFixtureTests(unittest.TestCase):
    def setUp(self):
        self.fixture = _load()
        self.limit = self.fixture["normative"]["max_validity_span_seconds"]
        self.cases = self.fixture["cases"]

    def test_configured_limit_matches_ratified_fixture_value(self):
        """Requirement 1: the limit is exactly 63_072_000 in the fixture AND in resolver.py.

        This is the assertion that catches a retyped literal on either side.
        """
        self.assertEqual(_RATIFIED_SPAN_SECONDS, self.limit)
        self.assertEqual(self.limit, resolver.MAX_RRSIG_VALIDITY_SPAN)

    def test_fixture_is_not_degenerate(self):
        """The corpus must exercise both outcomes and both boundary directions.

        Without this, the row-driven test below could pass while asserting nothing interesting.
        """
        self.assertGreaterEqual(len(self.cases), 8, "fixture must carry the full boundary set")
        self.assertTrue(any(c["window_valid"] for c in self.cases))
        self.assertTrue(any(not c["window_valid"] for c in self.cases))
        # Requirements 2 and 3, present as data.
        self.assertTrue(any(_span(c) == self.limit and c["window_valid"] for c in self.cases))
        self.assertTrue(any(_span(c) == self.limit + 1 and not c["window_valid"] for c in self.cases))
        # Requirement 4, present as data: rows whose raw difference is negative, so only mod-2**32
        # arithmetic produces the intended span.
        self.assertTrue(any(
            c["expiration"] < c["inception"] and _span(c) == self.limit and c["window_valid"]
            for c in self.cases))
        self.assertTrue(any(
            c["expiration"] < c["inception"] and _span(c) > self.limit and not c["window_valid"]
            for c in self.cases))

    def test_every_fixture_row_matches_the_guard(self):
        """Requirements 2, 3 and 4: every shared row decides the same way here as in C# and Swift."""
        for case in self.cases:
            with self.subTest(case=case["name"]):
                got = resolver._rrsig_window_valid(case["inception"], case["expiration"], case["now"])
                self.assertEqual(
                    case["window_valid"], got,
                    f"W16 row '{case['name']}': expected window_valid={case['window_valid']}, "
                    f"got {got}. {case['reason']}")

    def test_at_limit_rows_flip_when_nudged_one_second_wider(self):
        """Failure case (house rule): proves the row assertions are not vacuous.

        Widening every accepted at-the-limit window by one second must flip it to rejected. If it does
        not, the span guard is not what decides these rows.
        """
        at_limit = [c for c in self.cases if _span(c) == self.limit and c["window_valid"]]
        self.assertTrue(at_limit, "fixture carries no accepted at-the-limit row to mutate")
        for case in at_limit:
            with self.subTest(case=case["name"]):
                widened = (case["expiration"] + 1) & 0xFFFFFFFF
                self.assertFalse(
                    resolver._rrsig_window_valid(case["inception"], widened, case["now"]),
                    f"W16 row '{case['name']}' widened by one second must be rejected by the span guard")


if __name__ == "__main__":
    unittest.main()

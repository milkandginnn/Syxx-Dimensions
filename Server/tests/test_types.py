"""Phase 0 — canonical TriState result enum.

The four strings "success"/"failure"/"inconclusive"/"unknown" are the ONLY
wire-format values the client kits accept from the server; any other spelling
silently breaks cross-platform JSON parity. These tests pin the exact strings
AND the str-subclass behaviour that lets the enum serialise without a custom
encoder."""

import json
import os
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server.types import TriState  # noqa: E402


class TriStateWireFormatTests(unittest.TestCase):
    def test_exact_wire_strings(self):
        # The whole point: these exact bytes must match the Swift raw values
        # and the C# JSON converter output. A rename here silently breaks
        # cross-platform parity, so the assertion is literal.
        self.assertEqual(TriState.SUCCESS.value, "success")
        self.assertEqual(TriState.FAILURE.value, "failure")
        self.assertEqual(TriState.INCONCLUSIVE.value, "inconclusive")

    def test_serialises_as_bare_string(self):
        # str-subclassing means json.dumps emits the bare string, not the
        # Enum's __repr__ and not a dict. If anyone drops the (str, Enum)
        # base, this test goes RED.
        self.assertEqual(json.dumps(TriState.SUCCESS), '"success"')
        self.assertEqual(json.dumps(TriState.FAILURE), '"failure"')
        self.assertEqual(json.dumps(TriState.INCONCLUSIVE), '"inconclusive"')

    def test_str_returns_value(self):
        self.assertEqual(str(TriState.SUCCESS), "success")
        self.assertEqual(str(TriState.FAILURE), "failure")
        self.assertEqual(str(TriState.INCONCLUSIVE), "inconclusive")

    def test_four_distinct_members(self):
        self.assertEqual(len(TriState), 4)

    def test_failure_case_wrong_spelling_is_caught(self):
        """Failure case (house rule): if anyone retypes a value (e.g. "failed"
        instead of "failure"), the exact-string assertion above must trip.
        Proves the guard is not vacuous."""
        self.assertNotEqual(TriState.FAILURE.value, "failed")
        self.assertNotEqual(TriState.FAILURE.value, "error")
        self.assertNotEqual(TriState.INCONCLUSIVE.value, "unknown")
        self.assertNotEqual(TriState.SUCCESS.value, "ok")


if __name__ == "__main__":
    unittest.main()

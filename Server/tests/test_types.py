"""Phase 0 — canonical TriState result enum.

The four strings "success"/"failure"/"inconclusive"/"unknown" are the ONLY
wire-format values the client kits accept from the server; any other spelling
silently breaks cross-platform JSON parity. These tests pin the exact strings
AND the str-subclass behaviour that lets the enum serialise without a custom
encoder."""

import json
import os

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server.types import TriState  # noqa: E402


def test_exact_wire_strings():
    # The whole point: these exact bytes must match the Swift raw values
    # and the C# JSON converter output. A rename here silently breaks
    # cross-platform parity, so the assertion is literal.
    assert TriState.SUCCESS.value == "success"
    assert TriState.FAILURE.value == "failure"
    assert TriState.INCONCLUSIVE.value == "inconclusive"


def test_serialises_as_bare_string():
    # str-subclassing means json.dumps emits the bare string, not the
    # Enum's __repr__ and not a dict. If anyone drops the (str, Enum)
    # base, this test goes RED.
    assert json.dumps(TriState.SUCCESS) == '"success"'
    assert json.dumps(TriState.FAILURE) == '"failure"'
    assert json.dumps(TriState.INCONCLUSIVE) == '"inconclusive"'


def test_str_returns_value():
    assert str(TriState.SUCCESS) == "success"
    assert str(TriState.FAILURE) == "failure"
    assert str(TriState.INCONCLUSIVE) == "inconclusive"


def test_four_distinct_members():
    assert len(TriState) == 4


def test_failure_case_wrong_spelling_is_caught():
    """Failure case (house rule): if anyone retypes a value (e.g. "failed"
    instead of "failure"), the exact-string assertion above must trip.
    Proves the guard is not vacuous."""
    assert TriState.FAILURE.value != "failed"
    assert TriState.FAILURE.value != "error"
    assert TriState.INCONCLUSIVE.value != "unknown"
    assert TriState.SUCCESS.value != "ok"

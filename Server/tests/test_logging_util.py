"""Unit tests for syxx_server/logging_util.py — structured logging and metrics.

Tests cover:
  - _redact_emails(): strings, dicts, lists, bytes, sets, frozensets, nested
  - info/warn/error(): JSON output structure, field inclusion
  - incr()/snapshot(): metric counters, overflow cap
  - _MAX_METRIC_KEYS: memory-bounded metric keys
  - Email redaction in emitted log lines

No network calls. Stdout is captured via pytest's capsys for log verification.
"""

import json
import threading

import pytest

from syxx_server import logging_util  # noqa: E402


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture(autouse=True)
def _reset_metrics():
    """Clear the metrics dict before and after each test."""
    logging_util._metrics.clear()
    yield
    logging_util._metrics.clear()


def _last_log(capsys):
    """Parse the last JSON line from capsys captured output."""
    captured = capsys.readouterr()
    lines = [l for l in captured.out.strip().split("\n") if l.strip()]
    if not lines:
        return None, ""
    return json.loads(lines[-1]), captured.out


def _all_logs(capsys):
    """Parse all JSON lines from capsys captured output."""
    captured = capsys.readouterr()
    return [json.loads(l) for l in captured.out.strip().split("\n") if l.strip()]


# ---------------------------------------------------------------------------
# _redact_emails — email address redaction
# ---------------------------------------------------------------------------

class TestRedactEmails:
    def test_redacts_email_in_string(self):
        assert logging_util._redact_emails("contact user@example.com now") == \
               "contact <redacted> now"

    def test_redacts_multiple_emails(self):
        result = logging_util._redact_emails("a@b.com and c@d.org")
        assert "a@b.com" not in result
        assert "c@d.org" not in result
        assert result.count("<redacted>") == 2

    def test_preserves_string_without_email(self):
        assert logging_util._redact_emails("no email here") == "no email here"

    def test_redacts_in_dict_values(self):
        d = {"user": "admin@example.com", "action": "login"}
        result = logging_util._redact_emails(d)
        assert result["user"] == "<redacted>"
        assert result["action"] == "login"

    def test_redacts_in_list(self):
        lst = ["hello", "user@domain.com", 42]
        result = logging_util._redact_emails(lst)
        assert result == ["hello", "<redacted>", 42]

    def test_redacts_in_nested_dict(self):
        nested = {"outer": {"inner": "deep@nested.com"}}
        result = logging_util._redact_emails(nested)
        assert result["outer"]["inner"] == "<redacted>"

    def test_redacts_in_bytes(self):
        b = b"SMTP reply for user@example.com accepted"
        result = logging_util._redact_emails(b)
        assert isinstance(result, bytes)
        assert b"user@example.com" not in result
        assert b"<redacted>" in result

    def test_redacts_in_set(self):
        s = {"admin@site.com", "no-email", "user@site.com"}
        result = logging_util._redact_emails(s)
        assert isinstance(result, set)
        assert "admin@site.com" not in result
        assert "user@site.com" not in result
        assert "<redacted>" in result

    def test_redacts_in_frozenset(self):
        fs = frozenset(["a@b.com", "c@d.com"])
        result = logging_util._redact_emails(fs)
        assert isinstance(result, frozenset)
        assert "a@b.com" not in result

    def test_redacts_in_tuple(self):
        t = ("a@b.com", "plain")
        result = logging_util._redact_emails(t)
        assert result == ["<redacted>", "plain"]

    def test_non_string_types_pass_through(self):
        assert logging_util._redact_emails(42) == 42
        assert logging_util._redact_emails(3.14) == 3.14
        assert logging_util._redact_emails(None) is None
        assert logging_util._redact_emails(True) is True

    def test_complex_email_formats(self):
        assert "<redacted>" == logging_util._redact_emails("user.name+tag@sub.domain.co.uk")

    def test_empty_string(self):
        assert logging_util._redact_emails("") == ""

    def test_bytes_non_utf8(self):
        """Non-UTF-8 bytes should not crash; errors='replace' handles it."""
        result = logging_util._redact_emails(b"\xff\xfe user@test.com")
        assert isinstance(result, bytes)


# ---------------------------------------------------------------------------
# info / warn / error — structured log emission
# ---------------------------------------------------------------------------

class TestLogEmission:
    def test_info_emits_json_with_correct_level(self, capsys):
        logging_util.info("test_event", key="value")
        log, _ = _last_log(capsys)
        assert log["level"] == "info"
        assert log["event"] == "test_event"
        assert log["key"] == "value"
        assert "ts" in log

    def test_warn_emits_warn_level(self, capsys):
        logging_util.warn("warning_event", detail="something happened")
        log, _ = _last_log(capsys)
        assert log["level"] == "warn"
        assert log["event"] == "warning_event"

    def test_error_emits_error_level(self, capsys):
        logging_util.error("error_event", code=500)
        log, _ = _last_log(capsys)
        assert log["level"] == "error"
        assert log["code"] == 500

    def test_timestamp_is_numeric(self, capsys):
        logging_util.info("ts_check")
        log, _ = _last_log(capsys)
        assert isinstance(log["ts"], (int, float))
        assert log["ts"] > 1_000_000_000

    def test_email_redacted_in_emitted_log(self, capsys):
        logging_util.info("user_login", email="user@example.com")
        log, raw = _last_log(capsys)
        assert log["email"] == "<redacted>"
        assert "user@example.com" not in raw

    def test_multiple_fields_preserved(self, capsys):
        logging_util.info("multi", a=1, b="two", c=[3])
        log, _ = _last_log(capsys)
        assert log["a"] == 1
        assert log["b"] == "two"
        assert log["c"] == [3]

    def test_non_serializable_objects_use_str_fallback(self, capsys):
        """Objects that json.dumps can't handle fall back to str() via default=str."""
        logging_util.info("obj_test", obj=set([1, 2, 3]))
        log, _ = _last_log(capsys)
        # The set was redacted (it's a set type) — just verify no crash
        assert log["event"] == "obj_test"


# ---------------------------------------------------------------------------
# incr / snapshot — metric counters
# ---------------------------------------------------------------------------

class TestMetrics:
    def test_incr_creates_counter(self):
        logging_util.incr("requests")
        assert logging_util.snapshot()["requests"] == 1

    def test_incr_increments_existing(self):
        logging_util.incr("requests")
        logging_util.incr("requests")
        logging_util.incr("requests")
        assert logging_util.snapshot()["requests"] == 3

    def test_incr_custom_amount(self):
        logging_util.incr("bytes_sent", 1024)
        assert logging_util.snapshot()["bytes_sent"] == 1024

    def test_snapshot_returns_copy(self):
        logging_util.incr("x")
        snap1 = logging_util.snapshot()
        snap1["x"] = 999
        assert logging_util.snapshot()["x"] == 1  # original unchanged

    def test_multiple_independent_counters(self):
        logging_util.incr("a")
        logging_util.incr("b", 5)
        logging_util.incr("a")
        snap = logging_util.snapshot()
        assert snap["a"] == 2
        assert snap["b"] == 5

    def test_snapshot_empty_initially(self):
        assert logging_util.snapshot() == {}


# ---------------------------------------------------------------------------
# _MAX_METRIC_KEYS — memory cap
# ---------------------------------------------------------------------------

class TestMetricKeyCap:
    def test_keys_beyond_cap_are_dropped(self):
        """Beyond _MAX_METRIC_KEYS, new keys increment metrics_keys_dropped."""
        cap = logging_util._MAX_METRIC_KEYS
        # Fill up to the cap
        for i in range(cap):
            logging_util.incr(f"key_{i}")
        assert len(logging_util.snapshot()) == cap

        # One more key should be dropped
        logging_util.incr("overflow_key")
        snap = logging_util.snapshot()
        assert "overflow_key" not in snap
        assert snap["metrics_keys_dropped"] == 1

    def test_existing_keys_still_increment_past_cap(self):
        """Even after the cap is reached, existing keys can still increment."""
        cap = logging_util._MAX_METRIC_KEYS
        logging_util.incr("survivor")
        for i in range(cap):
            logging_util.incr(f"fill_{i}")
        # Now increment the pre-cap key
        logging_util.incr("survivor")
        assert logging_util.snapshot()["survivor"] == 2

    def test_multiple_overflows_counted(self):
        cap = logging_util._MAX_METRIC_KEYS
        for i in range(cap):
            logging_util.incr(f"k_{i}")
        logging_util.incr("over1")
        logging_util.incr("over2")
        logging_util.incr("over3")
        assert logging_util.snapshot()["metrics_keys_dropped"] == 3


# ---------------------------------------------------------------------------
# Thread safety — basic smoke test
# ---------------------------------------------------------------------------

class TestThreadSafety:
    def test_concurrent_incr_produces_correct_total(self):
        """Multiple threads incrementing the same counter must yield the correct total."""
        n_threads = 10
        n_incrs = 1000
        barrier = threading.Barrier(n_threads)

        def worker():
            barrier.wait()
            for _ in range(n_incrs):
                logging_util.incr("concurrent")

        threads = [threading.Thread(target=worker) for _ in range(n_threads)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        assert logging_util.snapshot()["concurrent"] == n_threads * n_incrs

    def test_concurrent_log_emission_no_crash(self, capsys):
        """Multiple threads logging simultaneously must not crash."""
        barrier = threading.Barrier(5)

        def worker(i):
            barrier.wait()
            for j in range(50):
                logging_util.info("concurrent_log", thread=i, iteration=j)

        threads = [threading.Thread(target=worker, args=(i,)) for i in range(5)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        logs = _all_logs(capsys)
        assert len(logs) == 250  # 5 threads × 50 iterations

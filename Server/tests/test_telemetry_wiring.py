"""Telemetry must be provably live, not merely attempted.

THE DEFECT THIS GUARDS: `app.py` wrapped the whole OpenTelemetry initialisation in a bare
`except Exception: pass`. Three separate import errors lived behind it, each individually fatal:

  1. `from opentelemetry import logs`            -> module is `_logs`
  2. `OTLPLogExporter` from `sdk._logs.export`   -> lives in `exporter.otlp.proto.http._log_exporter`
  3. `LogRecord` from `sdk._logs`                -> not re-exported in 1.44, lives in `_internal`

Because the failure was swallowed, `_OTEL_LOGGER` stayed None and `logging_util`'s export branch
was dead for the entire life of the feature. Nothing ever reached the collector, and an empty
error dashboard is indistinguishable from a healthy server. That is the same defect class this
product exists to refuse: a missing measurement rendered as a clean one.

Defect 3 is the reason these tests assert on the WIRING and on a real emitted record rather than
on "did init throw". Fixing 1 and 2 alone leaves `_otel_log_record` None, the export branch still
dead, and every "is telemetry up?" check still green.

Each test names the mutation that must turn it red.
"""

import json
import os
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

from syxx_server import app, logging_util  # noqa: E402


class TelemetryWiringTests(unittest.TestCase):
    """Init must SUCCEED, and its outcome must be observable."""

    def test_otel_init_reports_live_not_silently_failed(self):
        """Failure case: revert any of the three import paths -> _OTEL_STATE becomes
        'failed: ImportError: ...' -> red. Before the fix this was permanently failed and
        nothing anywhere said so."""
        self.assertEqual(
            app._OTEL_STATE, "live", f"OTel init did not reach a live state. _OTEL_STATE={app._OTEL_STATE!r}"
        )

    def test_init_outcome_is_recorded_at_all(self):
        """Whatever happens, the module must record an outcome so 'no data arriving' can be
        told apart from 'init crashed'.

        LIMIT OF THIS TEST, stated because an overclaimed failure case is itself the defect
        class this file exists to catch. It asserts on the RESULTING STATE, and `_OTEL_STATE
        = "live"` is set INSIDE the try block. So when the imports succeed, this passes no
        matter what the except handler does. Mutation-checked: restoring `except Exception:
        pass` leaves all five tests GREEN. The bare-except regression is caught by
        `test_no_bare_except_swallows_otel_init` below, which reads the source instead."""
        self.assertTrue(hasattr(app, "_OTEL_STATE"), "app must record an OTel init outcome, never swallow it")
        self.assertNotEqual(app._OTEL_STATE, "not attempted")

    def test_no_bare_except_swallows_otel_init(self):
        """The ROOT CAUSE guard. Three fatal import errors survived for the life of the
        feature because the whole OTel block sat behind `except Exception: pass`. Every
        state-based assertion in this file is blind to that handler while the imports happen
        to work, so this one reads the source directly.

        Failure case: replace either handler with a bare `except Exception: pass` -> red."""
        import inspect
        import re

        source = inspect.getsource(app)
        start = source.index("# --- OpenTelemetry error reporting")
        end = source.index("_STATIC_FILES", start)
        otel_block = source[start:end]

        swallowing = re.findall(r"except[^\n:]*:\s*\n\s*pass\b", otel_block)
        self.assertEqual(
            swallowing,
            [],
            "the OTel init block swallows its failure with a bare `except: pass`. That is the "
            "defect that hid three fatal ImportErrors: telemetry never exported a record and "
            "nothing anywhere said so. Record the outcome in _OTEL_STATE instead.",
        )

    def test_logging_util_export_branch_is_wired(self):
        """The seam that defect 3 left dead. Both handles are required: the branch at
        logging_util.py reads `_otel_logger`, and constructs `_otel_log_record`.
        Failure case: set either to None -> red."""
        self.assertIsNotNone(
            logging_util._otel_logger, "logging_util._otel_logger is None; error export branch is dead"
        )
        self.assertIsNotNone(
            logging_util._otel_log_record, "logging_util._otel_log_record is None; records cannot be built"
        )

    def test_an_error_actually_reaches_the_otlp_logger(self):
        """The only assertion that proves the whole chain. Wiring can be present while the
        emit path still fails to build a record.

        Failure case: break the LogRecord import, or make logging_util skip the export branch,
        and no record is captured -> red."""
        captured = []
        original_emit = logging_util._otel_logger.emit
        logging_util._otel_logger.emit = captured.append
        try:
            logging_util.error("telemetry_wiring_selftest", route="/selftest", err="synthetic")
        finally:
            logging_util._otel_logger.emit = original_emit

        self.assertEqual(len(captured), 1, "an error() call produced no OTLP record; the export branch is dead")

        record = captured[0]
        body = getattr(record, "body", None)
        self.assertIsNotNone(body, "emitted record carries no body")
        payload = json.loads(body)
        self.assertEqual(payload["event"], "telemetry_wiring_selftest")
        self.assertEqual(payload["level"], "error")
        self.assertEqual(payload["route"], "/selftest")
        self.assertEqual(getattr(record, "severity_text", None), "ERROR")

    def test_non_error_levels_do_not_export(self):
        """The export branch is deliberately scoped to errors. This pins that scope so a later
        change cannot start shipping every info line (and with it, request detail) to the
        collector without the change being noticed.

        Failure case: widen the branch past level == 'error' -> a record is captured -> red."""
        captured = []
        original_emit = logging_util._otel_logger.emit
        logging_util._otel_logger.emit = captured.append
        try:
            logging_util.info("telemetry_wiring_info_selftest", route="/selftest")
        finally:
            logging_util._otel_logger.emit = original_emit

        self.assertEqual(captured, [], "a non-error log was exported to the collector; scope has widened")


if __name__ == "__main__":
    unittest.main()

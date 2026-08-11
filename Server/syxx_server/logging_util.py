"""Structured JSON logging and in-process metric counters.

Replaces the old ``print`` statements and the swallow-everything ``except: pass``
with line-delimited JSON that journald/Loki can index, plus simple monotonic
counters exposed at /metrics.

When OpenTelemetry is available, error-level events are also exported as OTLP
log records to bugsmasher.syxx.app.
"""

import json
import re
import sys
import threading
import time

# A318: separate locks for stdout writes and metric increments so slow I/O
# does not block concurrent metric producers (and vice versa).
_log_lock = threading.Lock()
_metrics_lock = threading.Lock()
_metrics = {}

# Late-bound OTel logger — set by app.py after OTel init.
_otel_logger = None
_otel_log_record = None
_severity_number_error = 17  # OTLP SeverityNumber.ERROR

# A188 Fix 2: email redaction regex. Applied to all string-valued fields before
# serialisation, so no future call site can leak an email address to stdout or
# the OTLP sink. The pattern is intentionally conservative — it matches the
# common addr@domain.tld form and replaces with "<redacted>".
_EMAIL_RE = re.compile(r"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}")


def _redact_emails(obj):
    """Recursively redact email addresses from strings in *obj*.

    E260: Handle bytes, set, and frozenset types. Email addresses in raw
    SMTP responses (bytes) or sets of addresses were passing through
    unredacted because the function only checked str/dict/list/tuple.
    """
    if isinstance(obj, str):
        return _EMAIL_RE.sub("<redacted>", obj)
    if isinstance(obj, bytes):
        # E260: Decode bytes, redact, re-encode. Use errors='replace' to
        # handle non-UTF-8 bytes gracefully.
        try:
            decoded = obj.decode("utf-8", errors="replace")
            return _EMAIL_RE.sub("<redacted>", decoded).encode("utf-8")
        except Exception:
            return obj
    if isinstance(obj, dict):
        return {k: _redact_emails(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [_redact_emails(v) for v in obj]
    if isinstance(obj, (set, frozenset)):
        # E260: Redact each element in sets/frozensets.
        return type(obj)(_redact_emails(v) for v in obj)
    return obj


def _emit(level, event, **fields):
    """Write a structured JSON log line to stdout and forward to OTLP if applicable.

    Redacts email addresses (A188), attaches trace context when available (A059),
    and exports error-level records to the OTLP sink.
    """
    # A188 Fix 2: redact email addresses from all fields before serialisation.
    # This is the single choke point — every log line and every OTLP export
    # passes through here, so no call site can accidentally leak a recipient
    # address to stdout, journald, or the telemetry sink.
    fields = _redact_emails(fields)
    rec = {"ts": round(time.time(), 3), "level": level, "event": event}
    rec.update(fields)
    line = json.dumps(rec, default=str, ensure_ascii=False)
    # A318: use _log_lock for stdout writes only (SRV-047).
    with _log_lock:
        sys.stdout.write(line + "\n")
    # Flush outside the lock so slow disks don't block concurrent producers.
    sys.stdout.flush()
    # Forward error-level events to OTLP if available.
    if level == "error" and _otel_logger is not None and _otel_log_record is not None:
        try:
            body = json.dumps(rec, default=str, ensure_ascii=False)
            # A059: attach the current trace/span context so OTLP error records can be
            # correlated with the trace that produced them. Defensive: the OTel SDK may be
            # absent or the span not recording, in which case the record is emitted without
            # trace context (same as before this change).
            trace_id = None
            span_id = None
            trace_flags = None
            try:
                from opentelemetry import trace as _otel_trace  # type: ignore[unresolved-import]

                _span = _otel_trace.get_current_span()
                if _span.is_recording():
                    _span_ctx = _span.get_span_context()
                    trace_id = _span_ctx.trace_id
                    span_id = _span_ctx.span_id
                    trace_flags = _span_ctx.trace_flags
            except Exception:  # noqa: S110 - best-effort context lookup
                pass
            rec_obj = _otel_log_record(  # type: ignore[call-non-callable]
                body=body,
                severity_number=_severity_number_error,
                severity_text="ERROR",
                trace_id=trace_id,
                span_id=span_id,
                trace_flags=trace_flags,
            )
            _otel_logger.emit(rec_obj)
        except Exception:  # noqa: S110 — best-effort path; failing here must not take the request down
            pass  # telemetry is best-effort


def info(event, **fields):
    """Emit an info-level structured log event."""
    _emit("info", event, **fields)


def warn(event, **fields):
    """Emit a warn-level structured log event."""
    _emit("warn", event, **fields)


def error(event, **fields):
    """Emit an error-level structured log event (also forwarded to OTLP)."""
    _emit("error", event, **fields)


# A138: hard cap on the number of distinct metric keys. Beyond this, new keys are
# dropped and counted under `metrics_keys_dropped` — prevents unauthenticated
# attackers from driving unbounded memory growth via unique request paths.
_MAX_METRIC_KEYS = 512


def incr(name, n=1):
    """Increment the monotonic counter *name* by *n* (A138 key-cap enforced)."""
    # A318: use _metrics_lock (not _log_lock) so metric increments never block
    # on slow stdout writes.
    with _metrics_lock:
        if name in _metrics:
            _metrics[name] = _metrics.get(name, 0) + n
        elif len(_metrics) < _MAX_METRIC_KEYS:
            _metrics[name] = n
        else:
            # Overflow: count under a known key so the loss is observable.
            _metrics["metrics_keys_dropped"] = _metrics.get("metrics_keys_dropped", 0) + n


def snapshot():
    """Return a point-in-time copy of all metric counters."""
    with _metrics_lock:
        return dict(_metrics)

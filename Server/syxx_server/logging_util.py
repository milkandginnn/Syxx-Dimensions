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

_lock = threading.Lock()
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
    """Recursively redact email addresses from strings in *obj*."""
    if isinstance(obj, str):
        return _EMAIL_RE.sub("<redacted>", obj)
    if isinstance(obj, dict):
        return {k: _redact_emails(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple)):
        return [_redact_emails(v) for v in obj]
    return obj


def _emit(level, event, **fields):
    # A188 Fix 2: redact email addresses from all fields before serialisation.
    # This is the single choke point — every log line and every OTLP export
    # passes through here, so no call site can accidentally leak a recipient
    # address to stdout, journald, or the telemetry sink.
    fields = _redact_emails(fields)
    rec = {"ts": round(time.time(), 3), "level": level, "event": event}
    rec.update(fields)
    line = json.dumps(rec, default=str, ensure_ascii=False)
    # Lock only the write call, not the serialization (SRV-047).
    with _lock:
        sys.stdout.write(line + "\n")
    # Flush outside the lock so slow disks don't block concurrent producers.
    sys.stdout.flush()
    # Forward error-level events to OTLP if available.
    if level == "error" and _otel_logger is not None:
        try:
            body = json.dumps(rec, default=str, ensure_ascii=False)
            rec_obj = _otel_log_record(
                body=body,
                severity_number=_severity_number_error,
                severity_text="ERROR",
            )
            _otel_logger.emit(rec_obj)
        except Exception:  # noqa: S110
            pass  # telemetry is best-effort


def info(event, **fields):
    _emit("info", event, **fields)


def warn(event, **fields):
    _emit("warn", event, **fields)


def error(event, **fields):
    _emit("error", event, **fields)


# A138: hard cap on the number of distinct metric keys. Beyond this, new keys are
# dropped and counted under `metrics_keys_dropped` — prevents unauthenticated
# attackers from driving unbounded memory growth via unique request paths.
_MAX_METRIC_KEYS = 512


def incr(name, n=1):
    with _lock:
        if name in _metrics:
            _metrics[name] = _metrics.get(name, 0) + n
        elif len(_metrics) < _MAX_METRIC_KEYS:
            _metrics[name] = n
        else:
            # Overflow: count under a known key so the loss is observable.
            _metrics["metrics_keys_dropped"] = _metrics.get("metrics_keys_dropped", 0) + n


def snapshot():
    with _lock:
        return dict(_metrics)

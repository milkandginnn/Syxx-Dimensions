"""Broadcast / message-of-the-day (Workstream D2).

Reads an operator-edited JSON file (`config.BROADCAST_FILE`) and returns the currently-active
broadcast, or None. Lets an operator push a message to the app on next launch WITHOUT an app update.
The app shows an unseen `id` once (tracked client-side) inside its version range. Fields are
whitelisted + length-bounded so a malformed file can never inject unbounded content.

Example broadcast.json:
    {"id": "2026-07-outage", "title": "Collector maintenance",
     "body": "Leak checks may be briefly unavailable Sat 03:00-04:00 UTC.",
     "severity": "warning", "startsAt": 1751000000, "endsAt": 1751100000,
     "minVersion": "2.0.0", "maxVersion": null, "url": "https://syxx.app/status"}
"""

import json
import time
import urllib.parse

from . import config, logging_util

_SEVERITIES = ("info", "warning", "critical")


def _safe_url(raw):
    """A104: only http(s) broadcast links are allowed. broadcast.json is an
    operator-edited file, but a mistaken or tampered `javascript:` / `data:` /
    `file:` URL that a client launches is an unsafe-navigation risk, so anything
    that is not an http(s) URL is treated as absent (None). urlsplit strips
    embedded tab/newline before scheme parsing, so `java\\tscript:` cannot sneak
    through."""
    if not isinstance(raw, str):
        return None
    raw = raw[:500]
    try:
        scheme = urllib.parse.urlsplit(raw).scheme.lower()
    except ValueError:
        return None
    return raw if scheme in ("http", "https") else None


def current(now=None):
    now = now if now is not None else int(time.time())
    try:
        with open(config.BROADCAST_FILE, encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        return None  # no broadcast file — nothing to say, expected
    except OSError as e:
        # A198: unreadable file (wrong perms, wrong path) must be loud, not silent
        logging_util.error("broadcast_unreadable", err=str(e), path=config.BROADCAST_FILE)
        return None
    except ValueError as e:
        # A198: malformed JSON in an urgent security notice = total silence = worst outcome
        logging_util.error("broadcast_malformed", err=str(e), path=config.BROADCAST_FILE)
        return None
    if not isinstance(data, dict) or not data.get("id"):
        # A198: silently discarded — log so operator notices the misconfigured broadcast
        logging_util.warn("broadcast_rejected", reason="missing or empty id field", path=config.BROADCAST_FILE)
        return None
    starts, ends = data.get("startsAt"), data.get("endsAt")
    if isinstance(starts, int) and now < starts:
        return None
    if isinstance(ends, int) and now > ends:
        return None
    sev = data.get("severity")
    # A198: log when severity is coerced — an operator's "critical" typo silently
    # becoming "info" is a quiet downgrade of an urgent message.
    if sev and sev not in _SEVERITIES:
        logging_util.warn("broadcast_severity_coerced", original=sev, coerced_to="info", broadcast_id=data.get("id"))
    return {
        "id": str(data["id"])[:128],
        "title": str(data.get("title", ""))[:200],
        "body": str(data.get("body", ""))[:4000],
        "severity": sev if sev in _SEVERITIES else "info",
        "url": _safe_url(data.get("url")),
        "minVersion": data["minVersion"] if isinstance(data.get("minVersion"), str) else None,
        "maxVersion": data["maxVersion"] if isinstance(data.get("maxVersion"), str) else None,
    }

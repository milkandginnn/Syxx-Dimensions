"""Per-listener health registry — sub-project D-0 (encrypted transports server plan).

Why this exists
---------------
The server runs multiple network listeners (the HTTP/DoH server, the authoritative DNS collector,
and — once D-2/D-3 land — DoT and DoQ). Before this module, a listener that fell over degraded
SILENTLY: `dns_authoritative` rebinds to :5053 when :53 is taken and NOTHING reports it (SD-62),
so `/healthz` returned 200 while the leak collector was unreachable. That is the same class of
lie the whole product refuses — a surface reading healthy for a component that is down.

So every listener reports its state here, and `/readyz` folds the registry in. A never-started
listener defaults to "unknown", never "listening": absence of a report is not evidence of health.

States (the §3 honesty contract's "listening / degraded-fallback / down", plus unknown):
  - "listening"          — bound and serving on its intended port.
  - "degraded-fallback"  — serving, but not as intended (e.g. DNS on :5053 not :53). Still up,
                           but delegation/clients pointed at the real port will not reach it.
  - "down"               — failed to bind / serve. This is the only state that flips /readyz to 503.
  - "unknown"            — never reported. The safe default; means "we have not heard from it".
"""

import threading

_VALID = {"listening", "degraded-fallback", "down", "unknown"}

# Every listener the server KNOWS about, defaulted to unknown at import. Listing them here (rather
# than letting set_state create keys) means a listener that never starts still appears in the
# snapshot as "unknown" — a silent omission cannot masquerade as "not a problem".
# A316: "dot" and "doq" removed — planned but unimplemented listeners must not
# appear as perpetually "unknown" in /readyz. Re-add when the listeners land.
_KNOWN = ("http", "dns", "doh")

_lock = threading.Lock()
_state = {name: {"state": "unknown", "detail": None} for name in _KNOWN}


def set_state(name, state, detail=None):
    """Record a listener's state. Unknown listener names are accepted (forward-compat for a
    listener added before its _KNOWN entry) but an invalid STATE is a programming error and raises,
    because a typo'd state silently absent from _VALID would defeat the /readyz check."""
    if state not in _VALID:
        raise ValueError(f"invalid listener state {state!r} (want one of {sorted(_VALID)})")
    with _lock:
        _state[name] = {"state": state, "detail": detail}


def snapshot():
    """A copy of the current registry: {name: {"state": ..., "detail": ...}}."""
    with _lock:
        return {name: dict(entry) for name, entry in _state.items()}


def down_listeners():
    """Names of listeners currently `down`. `/readyz` is 503 iff this is non-empty."""
    with _lock:
        return sorted(n for n, e in _state.items() if e["state"] == "down")


def unknown_listeners():
    """A196: Names of listeners still in `unknown` state — never reported in.
    `unknown` means absence of evidence, not evidence of health, so /readyz
    must not return ready:true while any expected listener is unknown."""
    with _lock:
        return sorted(n for n, e in _state.items() if e["state"] == "unknown")

"""DNS integrity self-test ("attack simulation") — the classification core.

What this is
------------
A CONSENTED self-test: the caller authorizes the server to probe the caller's OWN resolver. The
server is authoritative for a controlled probe zone and serves KNOWN, distinctive answers for a
battery of probe names. The client resolves each name through its own resolver and reports what it
received. This module compares what the server SENT against what the client RECEIVED and classifies
each probe — because tampering in transit is exactly the gap between those two.

It is not offensive. It never touches any resolver but the caller's own, never impersonates a real
domain (every probe answer is a reserved TEST-NET / documentation value or a deliberately-nonexistent
name), and only reports observations back to the caller who asked.

Why the core is separate and pure
---------------------------------
This is the honesty-critical half. A DNS self-test that reports "not hijacked" when it merely FAILED
TO TEST is the exact false-clean the whole product refuses — worse here, because the user asked
precisely "confirm whether it has already been hijacked" and would trust a clean answer. So the
classification is pure, seam-tested, and tri-state:

    "clean"        — the probe completed and matched what the server served.
    "tampered"     — the probe completed and DID NOT match; the mechanism is named.
    "inconclusive" — the probe did not complete (no report, timeout, malformed). NEVER "clean".

The overall result is HARD-GATED: if the baseline probe (the one that must work on a healthy
network) is inconclusive, the whole run is inconclusive — you cannot say "your DNS is not hijacked"
when the control measurement itself did not land.
"""

# Reserved, non-routable, non-impersonating answers (RFC 5737 TEST-NET / RFC 3849 doc range). Serving
# these can never be mistaken for hijacking a real name, and a distinctive value makes a substituted
# answer obvious.
_BASELINE_A = "198.51.100.77"        # TEST-NET-2
_BASELINE_AAAA = "2001:db8::5177"    # RFC 3849 documentation range

CLEAN = "clean"
TAMPERED = "tampered"
INCONCLUSIVE = "inconclusive"


# The probe battery. Each entry: what the server serves, and what a mismatch MEANS. `critical` marks
# the control probes whose failure gates the whole run — you cannot certify "not tampered" if these
# did not even complete.
PROBES = [
    {
        "id": "baseline_a",
        "critical": True,
        "kind": "a",
        "expect": _BASELINE_A,
        "asks": "Does your resolver return our answer for an A record unchanged?",
        "tampered_means": "Your resolver returned a DIFFERENT address than we served — the answer "
                          "was rewritten in transit. This is active DNS hijacking; the substituted "
                          "address is shown.",
    },
    {
        "id": "nxdomain_honesty",
        "critical": False,
        "kind": "nxdomain",
        "expect": "NXDOMAIN",
        "asks": "For a name we say does not exist, does your resolver also say it does not exist?",
        "tampered_means": "We serve NXDOMAIN for this name, but your resolver returned an address — "
                          "it is FABRICATING answers for names that do not exist (typo monetization, "
                          "a captive portal, or injection). Honest resolvers return NXDOMAIN.",
    },
    {
        "id": "block_detection",
        "critical": False,
        "kind": "a",
        "expect": _BASELINE_A,
        "asks": "For a name that DOES resolve, do you receive the answer at all?",
        "tampered_means": "We serve a valid answer for this name, but your resolver returned "
                          "NXDOMAIN/SERVFAIL or nothing — the name is being BLOCKED on your path.",
    },
]

_BY_ID = {p["id"]: p for p in PROBES}


def probe_plan():
    """The client-facing plan: what to resolve and what each probe asks. The server fills in the
    actual per-session names (labelled tokens under the probe zone) at issue time; this is the static
    catalogue of what a full run measures."""
    return [{"id": p["id"], "kind": p["kind"], "asks": p["asks"], "critical": p["critical"]}
            for p in PROBES]


def _classify_one(probe, report):
    """Classify a single probe from the client's report. `report` is what the client received:
        {"status": "ok"|"nxdomain"|"servfail"|"timeout"|"error", "values": [ip, ...]}
    or None if the client did not report this probe at all."""
    if report is None:
        return INCONCLUSIVE, "No result was reported for this probe — it did not complete."

    status = report.get("status")
    values = report.get("values") or []

    # A probe that could not run measures nothing. Not clean.
    if status in ("timeout", "error", None):
        return INCONCLUSIVE, "The probe could not be completed (%s) — nothing was measured." % (
            status or "no status")

    if probe["kind"] == "nxdomain":
        # We serve NXDOMAIN. Honest = the client also got NXDOMAIN. An address means fabrication.
        if status == "nxdomain":
            return CLEAN, "Your resolver correctly returned NXDOMAIN."
        if status == "ok" and values:
            return TAMPERED, probe["tampered_means"] + " Fabricated address(es): %s." % ", ".join(values)
        if status == "servfail":
            return INCONCLUSIVE, "SERVFAIL — the resolver errored rather than answering; inconclusive."
        return INCONCLUSIVE, "Unexpected response shape for an NXDOMAIN probe; inconclusive."

    # kind == "a": we serve a known address. Honest = the client received exactly it.
    if status == "ok" and values:
        if probe["expect"] in values:
            return CLEAN, "Your resolver returned our answer unchanged."
        return TAMPERED, probe["tampered_means"] + " We served %s; you received %s." % (
            probe["expect"], ", ".join(values))
    if status in ("nxdomain", "servfail"):
        # For block_detection this IS the finding; for baseline it means the control did not land.
        if probe["id"] == "block_detection":
            return TAMPERED, probe["tampered_means"] + " (Received: %s.)" % status
        return INCONCLUSIVE, "The control answer did not arrive (%s) — cannot judge this probe." % status
    return INCONCLUSIVE, "No usable answer for an A probe; inconclusive."


def classify(reports):
    """Classify a full run. `reports` maps probe id -> the client's report (or omits it).

    Returns:
        {
          "overall": "clean" | "tampered" | "inconclusive",
          "probes": [{"id", "conclusion", "detail", "asks", "critical"}, ...],
          "summary": str,
        }

    Overall rules, in order:
      - Any TAMPERED probe -> overall TAMPERED (a confirmed finding dominates).
      - Else if any CRITICAL probe is INCONCLUSIVE -> overall INCONCLUSIVE (the control did not land,
        so a clean result would be unearned — this is the hard gate).
      - Else -> CLEAN.
    """
    results = []
    for p in PROBES:
        result, detail = _classify_one(p, reports.get(p["id"]))
        results.append({"id": p["id"], "conclusion": result, "detail": detail,
                        "asks": p["asks"], "critical": p["critical"]})

    tampered = [r for r in results if r["conclusion"] == TAMPERED]
    critical_inconclusive = [r for r in results
                             if r["critical"] and r["conclusion"] == INCONCLUSIVE]

    if tampered:
        overall = TAMPERED
        summary = ("DNS tampering CONFIRMED on your path: "
                   + "; ".join(r["id"] for r in tampered) + ".")
    elif critical_inconclusive:
        overall = INCONCLUSIVE
        summary = ("Inconclusive — a control probe (%s) did not complete, so we cannot certify your "
                   "DNS is untampered. This is NOT a clean result; re-run on a stable connection."
                   % ", ".join(r["id"] for r in critical_inconclusive))
    else:
        overall = CLEAN
        summary = "No tampering detected across the probes that completed."

    return {"overall": overall, "probes": results, "summary": summary}


def probe_by_id(pid):
    return _BY_ID.get(pid)

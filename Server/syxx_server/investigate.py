"""Multi-vantage web investigation (feature: real web investigation API).

Backs the website's "Investigate" box with an actual server-side query — this is
a PAID endpoint, so running it from the website requires an activated license
exactly like the app does. It resolves the domain across several public
resolvers, reports consensus as a tri-state result (never a bare "secure"), and
records everything it sees into the passive-DNS index.

DNSSEC is reported as *observed* (DNSKEY/RRSIG presence + AD bit), not
cryptographically validated — labeled honestly, matching the project's stance.
"""

import re
import time

from . import config, passivedns, resolver

# SM-001: support internationalized domain names (IDN). The previous regex
# only matched ASCII labels — any IDN (e.g. "münchen.de") was rejected as
# invalid. Accept non-ASCII Unicode labels; the resolver's _encode_name
# handles IDNA encoding to Punycode before wire transmission.
# A291: accept Punycode TLDs (xn--...) in addition to Unicode/ASCII TLDs.
# The previous TLD segment [a-zA-Z\u00C0-\uFFFF]{2,63} rejected Punycode TLDs
# like xn--fiqs8s (.中国) because they contain digits. Add an xn-- alternative.
_HOSTNAME = re.compile(
    r"^(?=.{1,253}$)"
    r"([a-zA-Z0-9_\u00C0-\uFFFF-]{1,63}\.)+"
    r"(?:xn--[a-zA-Z0-9-]{1,59}|[a-zA-Z\u00C0-\uFFFF]{2,63})\.?$"
)


def _env_int(name, default):
    return config._i(name, default)


# A096: aggregate wall-clock budget for one investigation. Each vantage query is
# bounded per-request, but the fan-out (system + UDP + DoH, for A and AAAA, plus
# DNSSEC) is sequential, so without a total budget a single /api/investigate can
# hold a worker across many slow queries. When the budget is spent, remaining
# vantages are left UNQUERIED and simply absent from the result; they are never
# silently counted as agreeing (that would be a false clean).
_INVESTIGATE_DEADLINE = max(1, _env_int("SYXX_INVESTIGATE_DEADLINE", 15))


def valid_domain(domain):
    return bool(_HOSTNAME.match(domain or ""))


def _vantages():
    """Combined vantage list: the host's own resolver, UDP resolvers, and DoH
    endpoints. On networks that block UDP/53 and public-resolver IPs (e.g.
    NextDNS) the UDP/DoH entries error out and the system vantage carries the
    investigation; on open networks all vantages answer and cross-check."""
    vantages = []
    if config.ENABLE_SYSTEM_VANTAGE:
        vantages.append(("system", "system", None))
    vantages += [("udp", s, s) for s in config.PUBLIC_RESOLVERS]
    if config.ENABLE_DOH:
        vantages += [("doh", d["label"], d) for d in config.DOH_VANTAGES]
    return vantages


def _query_vantage(kind, target, domain, qtype):
    if kind == "system":
        return resolver.query_system(domain, qtype)
    if kind == "udp":
        return resolver.query(domain, qtype, target, timeout=config.INVESTIGATE_TIMEOUT)
    return resolver.query_doh(domain, qtype, target["ip"], target["sni"], target["path"], timeout=config.DOH_TIMEOUT)


def _resolve_all(domain, qtype, deadline=None, vantages=None):
    per_resolver = {}
    vantage_list = vantages if vantages is not None else _vantages()
    for kind, label, target in vantage_list:
        if deadline is not None and time.monotonic() > deadline:
            # A096: aggregate budget spent. Leave the rest of the vantages unqueried
            # and absent — consensus is computed only over vantages that actually
            # answered, so a skipped vantage never counts as agreement.
            break
        try:
            res = _query_vantage(kind, target, domain, qtype)
            values = sorted({r["value"] for r in res["records"] if r["type"] in ("A", "AAAA")})
            # A045: map RCODE to a typed outcome before consensus. SERVFAIL and
            # REFUSED must never collapse into status:"ok" with empty values, or
            # they vanish under majority voting and censorship-by-selective-failure
            # disappears into a clean result. NOERROR with empty values is a
            # legitimate NODATA; anything else is a failure.
            #
            # A155: `rcode` is an INTEGER from every transport. The previous
            # revision compared it against the strings "NOERROR"/"SERVFAIL"/... —
            # all four comparisons were permanently False, control always reached
            # the else branch, and `rcode.lower()` raised AttributeError on an int.
            # Only DNSError was caught, so the exception escaped to the handler and
            # /api/investigate returned HTTP 500 on every vantage that answered.
            rcode = res["rcode"]
            name = resolver.rcode_name(rcode)
            if rcode == 0:
                status = "ok"  # answered; values may be empty (NODATA)
            elif rcode == 3:
                status = "nxdomain"  # answered: this name does not exist
            else:
                status = f"rcode_{name.lower()}"  # SERVFAIL / REFUSED / anything else
            per_resolver[label] = {
                "status": status,
                "transport": kind,
                "rcode": rcode,
                "rcodeName": name,
                "ad": res["ad"],
                "values": values,
            }
            for v in values:
                passivedns.ingest(domain, "A" if qtype == 1 else "AAAA", v, "investigate", label)
        except resolver.DNSError as e:
            per_resolver[label] = {"status": "error", "transport": kind, "error": str(e), "values": []}
        except Exception as e:  # noqa: BLE001 — boundary handler: any exception here must become a reported result, not a crash
            # A155 Fix 2: a bug anywhere in the mapping above must degrade this ONE
            # vantage to an honest error and let consensus report inconclusive — never
            # escape as a 500 that hides the outcome of the vantages that did answer
            # (honesty spine #2: a failure is not an absence of evidence, and it is
            # certainly not a clean result).
            per_resolver[label] = {
                "status": "error",
                "transport": kind,
                "error": f"{type(e).__name__}: {e}",
                "values": [],
            }
    return per_resolver


def _consensus_one(by_vantage):
    """Consensus across vantages for a SINGLE record type.

    S5: uses majority voting rather than requiring unanimity. Unanimity produces
    false-positive "divergent" results for legitimate round-robin / geo DNS where
    different vantages see different IPs from the same authoritative set. A majority
    (>= 50% of reachable vantages with values) agreeing on the same answer set is
    "consistent"; only when no answer set commands a majority is it truly "divergent".
    """
    # A044: a vantage that ANSWERED is evidence even when its answer set is empty.
    # NXDOMAIN ("this name does not exist") and NODATA ("this name exists, but not
    # for this type") are RETAINED here, with their RCODE, because an empty answer
    # standing next to an address is a CONTRADICTION OF EXISTENCE — divergent
    # evidence, not absent evidence. The old filter kept only status=="ok" AND
    # non-empty values, so a selectively-censored vantage silently vanished and the
    # remaining two agreeing vantages produced "consistent".
    # A279: when >50% of vantages are unreachable (errors, not just empty),
    # consensus among the remaining few masks selective censorship. Escalate
    # to 'inconclusive' with a failure rate note before computing agreement.
    total_vantages = len(by_vantage)
    unreachable = sum(1 for d in by_vantage.values() if d.get("status") == "error")
    if total_vantages > 0 and unreachable > total_vantages / 2:
        return (
            "inconclusive",
            f"majority of vantages unreachable ({unreachable}/{total_vantages} failed); "
            "cannot form a reliable consensus — possible selective censorship",
        )

    answered = {k: d for k, d in by_vantage.items() if d.get("status") in ("ok", "nxdomain")}
    if not answered:
        # A045: distinguish all-rcode-failure from all-transport-error, so the user
        # sees SERVFAIL/REFUSED by name rather than a generic "no answer". Names, not
        # numbers: `rcode` 0 is falsy and an int set cannot be joined (A155).
        names = sorted({d["rcodeName"] for d in by_vantage.values() if d.get("rcodeName")})
        if names:
            return "inconclusive", "all vantages returned error RCODEs: {}".format(", ".join(names))
        return "inconclusive", "no vantage returned an answer"
    nxdomain_vantages = sorted(k for k, d in answered.items() if d.get("status") == "nxdomain")
    nodata_vantages = sorted(k for k, d in answered.items() if d.get("status") == "ok" and not d["values"])
    with_vals = {k: d for k, d in answered.items() if d["values"]}
    if not with_vals:
        if nxdomain_vantages and nodata_vantages:
            return "inconclusive", (
                "vantages disagree on whether the name exists at all "
                "(NXDOMAIN: {}; NODATA: {}) and none returned a record".format(
                    ", ".join(nxdomain_vantages), ", ".join(nodata_vantages)
                )
            )
        if nxdomain_vantages:
            return "inconclusive", "all vantages returned NXDOMAIN"
        return "inconclusive", "no records of this type"
    # A044: an existence contradiction — some vantage says the name (or this type)
    # is absent while another returns records — is divergence, whichever way it is
    # signalled. Report the RCODE provenance so the user can see which vantage said
    # what, rather than a bare "divergent".
    if nxdomain_vantages:
        return "divergent", (
            "some vantages returned NXDOMAIN ({}) while others returned records; possible selective censorship".format(
                ", ".join(nxdomain_vantages)
            )
        )
    if nodata_vantages:
        return "divergent", (
            "some vantages returned NOERROR with no records ({}) while others "
            "returned records; possible selective filtering".format(", ".join(nodata_vantages))
        )
    # S5: majority voting — count how many vantages report each answer set.
    answer_counts = {}
    for d in with_vals.values():
        key = frozenset(d["values"])
        answer_counts[key] = answer_counts.get(key, 0) + 1
    total_with_vals = len(with_vals)
    majority_threshold = (total_with_vals // 2) + 1
    best_answer, best_count = max(answer_counts.items(), key=lambda x: x[1])

    if best_count >= majority_threshold:
        # A majority of vantages agree — this is consensus, not divergence.
        # Round-robin / geo DNS may produce a minority that disagrees; that is
        # legitimate, not tampering.
        # A045: any non-ok vantage (transport error OR rcode failure) degrades
        # confidence even when a majority agrees.
        any_non_ok = any(d.get("status") != "ok" for d in by_vantage.values())
        if any_non_ok:
            return "consistent-degraded", f"majority of vantages agree ({best_count}/{total_with_vals}); some vantages unreachable"
        # A290: single-vantage result — cannot cross-check.
        if total_with_vals == 1:
            return "single-vantage", (
                "only one vantage was reachable (this network blocks the others); cannot cross-check for tampering"
            )
        return "consistent", f"vantages agree ({best_count}/{total_with_vals})"
    else:
        # A292: when only 2 vantages answered with values and they disagree,
        # this is likely geo/CDN split rather than tampering. With so few
        # data points, "divergent" overstates confidence in the divergence.
        if total_with_vals <= 2:
            return "consistent-degraded", (
                f"only {total_with_vals} vantage(s) returned values and they disagree; "
                "insufficient data to declare divergence — likely geo/CDN split"
            )
        # No majority with 3+ vantages — genuine disagreement.
        return "divergent", f"vantages disagree — no majority ({best_count}/{total_with_vals} agree); possible geo/CDN split or tampering"


# Worst-to-best ordering so the overall result surfaces the strongest signal.
_RANK = {"divergent": 4, "single-vantage": 3, "consistent-degraded": 2, "consistent": 1, "inconclusive": 0}


def _consensus(a_by_vantage, aaaa_by_vantage):
    va = _consensus_one(a_by_vantage)
    v6 = _consensus_one(aaaa_by_vantage)
    candidates = [v for v in (va, v6) if v[0] != "inconclusive"]
    if not candidates:
        return va[0], va[1], va, v6  # nothing resolved for either type
    overall = max(candidates, key=lambda v: _RANK[v[0]])
    return overall[0], overall[1], va, v6


_DNSSEC_NOTE = "presence-only; not cryptographically validated"


def _dnssec_observed(domain, deadline=None, vantages=None):
    """DNSSEC posture as OBSERVED across vantages — never cryptographically validated.

    S13: the previous revision issued a SECOND query with QTYPE=RRSIG (46). That does
    not work. RRSIGs are returned alongside the RRset they cover, and only when the
    EDNS DO bit is set (RFC 4034 §3.2); RFC 4034 §3.1.5 explicitly says a bare RRSIG
    QTYPE query has no useful semantics, and every real recursive resolver answers it
    with an empty NOERROR. `has_rrsig` was therefore permanently False, so EVERY signed
    zone on the internet reported observed:false — a confident negative derived from a
    query that could never return the evidence. The probe now delegates to
    `resolver.zone_signing_status`, which asks for DNSKEY WITH THE DO BIT SET and reads
    the covering RRSIG out of that same response (rd=True because these vantages are
    recursive, not authoritative).

    A041: evidence is collected from ALL reachable vantages, not just the first. A
    censoring resolver placed first may strip DNSSEC records; it must not suppress
    evidence available from later independent vantages.

    Per-vantage `signed` is a TRI-STATE — "yes" / "no" / "unknown". A vantage whose
    transport cannot carry the DO bit at all (the system resolver via getaddrinfo, and
    the DoH JSON API as used here) reports "unknown", never "no": that transport has no
    way to see an RRSIG, so its silence is absence of evidence, not evidence of absence.
    """
    per_vantage = []
    vantage_list = vantages if vantages is not None else _vantages()
    for kind, label, target in vantage_list:
        if deadline is not None and time.monotonic() > deadline:
            break
        if kind == "doh":
            # A293: DoH DNSSEC probe — query DNSKEY via DoH with DO bit.
            # The DoH JSON API may not carry the DO bit directly, but we can
            # still query for DNSKEY records and check for RRSIG presence.
            try:
                res = resolver.query_doh(
                    domain, resolver.TYPE_DNSKEY if hasattr(resolver, "TYPE_DNSKEY") else 48,
                    target["ip"], target["sni"], target["path"],
                    timeout=config.DOH_TIMEOUT,
                )
                records = res.get("records", [])
                dnskey_records = [r for r in records if r.get("type") in ("DNSKEY", 48)]
                rrsig_records = [r for r in records if r.get("type") in ("RRSIG", 46)]
                if dnskey_records and rrsig_records:
                    signed = "yes"
                    observed = True
                    note = "DNSKEY+RRSIG observed via DoH (presence-only)"
                elif dnskey_records:
                    signed = "no"
                    observed = False
                    note = "DNSKEY present but no RRSIG via DoH"
                else:
                    signed = "no"
                    observed = False
                    note = "no DNSKEY records via DoH"
                per_vantage.append({
                    "signed": signed,
                    "observed": observed,
                    "adBit": res.get("ad", False),
                    "note": note,
                    "rrsigStatuses": [],
                    "transport": kind,
                    "vantage": label,
                })
            except Exception:
                per_vantage.append({
                    "signed": "unknown",
                    "observed": False,
                    "adBit": False,
                    "note": "DoH DNSSEC probe failed",
                    "rrsigStatuses": [],
                    "transport": kind,
                    "vantage": label,
                })
            continue
        if kind != "udp":
            # System resolver: no DO bit -> this vantage cannot answer.
            per_vantage.append(
                {
                    "signed": "unknown",
                    "observed": False,
                    "adBit": False,
                    "note": "transport cannot carry the DNSSEC OK bit",
                    "rrsigStatuses": [],
                    "transport": kind,
                    "vantage": label,
                }
            )
            continue
        posture, statuses = resolver.zone_signing_status(domain, target, config.INVESTIGATE_TIMEOUT, rd=True)
        signed = {True: "yes", False: "no", None: "unknown"}[posture]
        note = _DNSSEC_NOTE
        if posture is None and statuses:
            # DNSKEY present, but nothing inside its validity window: say which way.
            if "expired" in statuses:
                note += " (DNSKEY present; some RRSIGs are expired)"
            elif "not-yet-valid" in statuses:
                note += " (DNSKEY present; some RRSIGs are not yet valid)"
            else:
                note += " (DNSKEY present; no usable covering RRSIG)"
        elif posture is None:
            note = "could not query DNSSEC records"
        per_vantage.append(
            {
                "signed": signed,
                "observed": posture is True,
                "adBit": False,
                "note": note,
                "rrsigStatuses": statuses,
                "transport": kind,
                "vantage": label,
            }
        )

    # A041: signed if ANY reachable vantage saw a DNSKEY with a covering RRSIG inside its
    # validity window. A single stripping vantage must not terminate the search.
    if any(v["signed"] == "yes" for v in per_vantage):
        status = "signed"
    elif any(v["signed"] == "no" for v in per_vantage):
        status = "unsigned"
    else:
        # Honesty spine #2/#3: no vantage could answer. That is UNKNOWN, and it must
        # not be reported with the same value as an observed unsigned zone.
        status = "unknown"
    confirming = [v for v in per_vantage if v["signed"] == "yes"]
    if confirming:
        note, rrsig_statuses = confirming[0]["note"], confirming[0]["rrsigStatuses"]
    elif per_vantage:
        note, rrsig_statuses = per_vantage[0]["note"], per_vantage[0]["rrsigStatuses"]
    else:
        note, rrsig_statuses = "could not query DNSSEC records", []
    # A167: tri-state observed — True if signed, False if unsigned, None if
    # unknown (no vantage could answer). The previous bool rendered "unknown"
    # identically to "this zone is not signed" in the UI.
    if status == "signed":
        observed_val = True
    elif status == "unsigned":
        observed_val = False
    else:
        observed_val = None  # no vantage could answer — unknown, not false
    return {
        "status": status,
        "observed": observed_val,
        "adBit": any(v["adBit"] for v in per_vantage),
        "note": note,
        "rrsigStatuses": rrsig_statuses,
        "vantages": per_vantage,
    }


def investigate(domain):
    domain = (domain or "").strip().lower().rstrip(".")
    if not valid_domain(domain) or len(domain) > 253:
        return None, "invalid domain"
    deadline = time.monotonic() + _INVESTIGATE_DEADLINE
    t0 = time.monotonic()
    vantages = _vantages()
    a = _resolve_all(domain, resolver.TYPE_A, deadline, vantages=vantages)
    aaaa = _resolve_all(domain, resolver.TYPE_AAAA, deadline, vantages=vantages)
    result, reason, (va, va_reason), (v6, v6_reason) = _consensus(a, aaaa)
    # SM-004: include deadline metadata so the caller can see how much budget
    # remained and whether the investigation was truncated.
    elapsed_ms = int((time.monotonic() - t0) * 1000)
    remaining_ms = max(0, int((_INVESTIGATE_DEADLINE - (time.monotonic() - t0)) * 1000))
    # A294: count vantages that were skipped due to deadline exhaustion.
    # These are invisible in the per-vantage dicts — the caller needs to know
    # how many vantages were never queried so they can gauge result completeness.
    all_vantage_count = len(vantages)
    a_queried = len(a)
    aaaa_queried = len(aaaa)
    deadline_skipped_a = max(0, all_vantage_count - a_queried)
    deadline_skipped_aaaa = max(0, all_vantage_count - aaaa_queried)
    return {
        "domain": domain,
        "ipv4": a,
        "ipv6": aaaa,
        "consensus": result,
        "consensusReason": reason,
        "ipv4Consensus": va,
        "ipv6Consensus": v6,
        "dnssec": _dnssec_observed(domain, deadline, vantages=vantages),
        "timing": {
            "elapsed_ms": elapsed_ms,
            "deadline_ms": _INVESTIGATE_DEADLINE * 1000,
            "remaining_ms": remaining_ms,
            # A294: how many vantages were skipped per record type due to deadline.
            "deadline_skipped_ipv4": deadline_skipped_a,
            "deadline_skipped_ipv6": deadline_skipped_aaaa,
        },
    }, None

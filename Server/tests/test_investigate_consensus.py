"""Consensus, RCODE typing, DNSSEC posture and rdlength honesty.

Every class here guards a defect where the server produced a CONFIDENT answer from
evidence it never had, or crashed instead of degrading:

  A155 — `rcode` is an int from every transport; `_resolve_all` compared it to the
         strings "NOERROR"/"SERVFAIL"/... All four comparisons were permanently False,
         the fall-through called `.lower()` on an int, and only DNSError was caught,
         so /api/investigate returned HTTP 500 on every vantage that answered.
  A045 — SERVFAIL/REFUSED collapsed into status:"ok" with empty values.
  A044 — reachable-but-empty vantages (NXDOMAIN / NODATA) were dropped from consensus,
         so a selectively-censored vantage vanished and the rest read "consistent".
  S13  — `_dnssec_observed` asked for QTYPE=RRSIG standalone. Real resolvers answer
         that with SERVFAIL / an empty NOERROR, so every signed zone reported
         observed:false.
  A115 — a record whose rdlength does not match its type width was rendered as a
         parsed A/AAAA carrying hex garbage.

Run from Server/:  python3 -m unittest tests.test_investigate_consensus -v
"""

import os
import struct

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import config, investigate, passivedns, resolver  # noqa: E402


def _resp(rcode, values=(), rtype="A", ad=False):
    return {"rcode": rcode, "ad": ad, "truncated": False, "records": [{"type": rtype, "value": v} for v in values]}


class _StubVantages:
    """Replace the vantage fan-out with a fixed {server: response} map."""

    def __enter__(self):
        self._sys = config.ENABLE_SYSTEM_VANTAGE
        self._doh = config.ENABLE_DOH
        self._pub = config.PUBLIC_RESOLVERS
        self._query = resolver.query
        self._ingest = passivedns.ingest
        config.ENABLE_SYSTEM_VANTAGE = False
        config.ENABLE_DOH = False
        passivedns.ingest = lambda *a, **k: None
        return self

    def __exit__(self, *exc):
        config.ENABLE_SYSTEM_VANTAGE = self._sys
        config.ENABLE_DOH = self._doh
        config.PUBLIC_RESOLVERS = self._pub
        resolver.query = self._query
        passivedns.ingest = self._ingest
        return False

    def responses(self, mapping):
        config.PUBLIC_RESOLVERS = list(mapping)
        resolver.query = lambda name, qtype, server, timeout=None: mapping[server]
        return investigate._resolve_all("example.com", resolver.TYPE_A)


def test_rcode_name_never_guesses_noerror():
    assert resolver.rcode_name(0) == "NOERROR"
    assert resolver.rcode_name(2) == "SERVFAIL"
    assert resolver.rcode_name(3) == "NXDOMAIN"
    assert resolver.rcode_name(5) == "REFUSED"
    # An unassigned code must surface as itself, never fall back to NOERROR.
    assert resolver.rcode_name(14) == "RCODE14"
    assert resolver.rcode_name(None) == "UNKNOWN"
    assert resolver.rcode_name("NOERROR") == "UNKNOWN"


def test_answering_vantage_does_not_raise():
    # The reproduction of A155: a single vantage returning a normal NOERROR
    # answer crashed _resolve_all with AttributeError: 'int' has no 'lower'.
    with _StubVantages() as v:
        per = v.responses({"1.1.1.1": _resp(0, ["1.2.3.4"])})
    assert per["1.1.1.1"]["status"] == "ok"
    assert per["1.1.1.1"]["values"] == ["1.2.3.4"]


def test_every_rcode_maps_to_its_own_typed_status():
    with _StubVantages() as v:
        per = v.responses(
            {
                "a": _resp(0, ["1.2.3.4"]),
                "b": _resp(2),
                "c": _resp(5),
                "d": _resp(3),
                "e": _resp(0),
            }
        )
    assert per["a"]["status"] == "ok"
    assert per["b"]["status"] == "rcode_servfail"
    assert per["c"]["status"] == "rcode_refused"
    assert per["d"]["status"] == "nxdomain"
    assert per["e"]["status"] == "ok"  # NOERROR + no records = NODATA
    assert per["b"]["rcodeName"] == "SERVFAIL"


def test_unexpected_exception_degrades_one_vantage_not_the_request():
    # A155 Fix 2. A bug in the mapping must cost one vantage, never the whole
    # investigation: honesty spine #2, a failure is not an absence of evidence.
    with _StubVantages():
        config.PUBLIC_RESOLVERS = ["boom", "ok"]

        def q(name, qtype, server, timeout=None):
            if server == "boom":
                raise RuntimeError("synthetic mapping bug")
            return _resp(0, ["1.2.3.4"])

        resolver.query = q
        per = investigate._resolve_all("example.com", resolver.TYPE_A)
    assert per["boom"]["status"] == "error"
    assert "RuntimeError" in per["boom"]["error"]
    assert per["ok"]["status"] == "ok"


def test_all_servfail_is_inconclusive_and_names_the_rcode():
    with _StubVantages() as v:
        per = v.responses({"a": _resp(2), "b": _resp(2)})
    state, reason = investigate._consensus_one(per)
    assert state == "inconclusive"
    assert "SERVFAIL" in reason


def test_servfail_alongside_answers_degrades_confidence():
    with _StubVantages() as v:
        per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(5)})
    state, _ = investigate._consensus_one(per)
    assert state == "consistent-degraded", "a REFUSED vantage must not be invisible to consensus"


def test_refused_vantage_is_not_counted_as_an_agreeing_vantage():
    with _StubVantages() as v:
        per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(5), "c": _resp(5)})
    state, reason = investigate._consensus_one(per)
    assert state == "consistent-degraded"
    assert "(1/1)" in reason, "refused vantages must not inflate the agreement count"


def test_selective_nxdomain_is_divergent_not_consistent():
    with _StubVantages() as v:
        per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(3)})
    state, reason = investigate._consensus_one(per)
    assert state == "divergent", (
        "a vantage saying the name does not exist, while two others "
        "return an address, is selective censorship — not agreement"
    )
    assert "c" in reason, "the reason must name the dissenting vantage (RCODE provenance)"


def test_selective_nodata_is_divergent_not_consistent():
    with _StubVantages() as v:
        per = v.responses(
            {"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(0)}
        )  # NOERROR, zero records
    state, reason = investigate._consensus_one(per)
    assert state == "divergent"
    assert "c" in reason


def test_unanimous_nodata_is_inconclusive_not_divergent():
    # The guard must not over-fire: everyone agreeing there are no records of this
    # type is a legitimate, common answer (a v4-only name asked for AAAA).
    with _StubVantages() as v:
        per = v.responses({"a": _resp(0), "b": _resp(0)})
    state, _ = investigate._consensus_one(per)
    assert state == "inconclusive"


def test_existence_disagreement_with_no_records_is_inconclusive():
    with _StubVantages() as v:
        per = v.responses({"a": _resp(3), "b": _resp(0)})
    state, reason = investigate._consensus_one(per)
    assert state == "inconclusive"
    assert "NXDOMAIN" in reason
    assert "NODATA" in reason


def test_genuine_agreement_still_reads_consistent():
    with _StubVantages() as v:
        per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(0, ["1.2.3.4"])})
    state, _ = investigate._consensus_one(per)
    assert state == "consistent"


@pytest.fixture
def _dnssec_env(monkeypatch):
    monkeypatch.setattr(config, "ENABLE_SYSTEM_VANTAGE", False)
    monkeypatch.setattr(config, "ENABLE_DOH", False)


def _posture(monkeypatch, mapping):
    monkeypatch.setattr(config, "PUBLIC_RESOLVERS", list(mapping))
    monkeypatch.setattr(
        resolver, "zone_signing_status", lambda zone, server, timeout, now=None, rd=False: mapping[server]
    )
    return investigate._dnssec_observed("example.com")


def test_signed_zone_is_reported_signed(_dnssec_env, monkeypatch):
    r = _posture(monkeypatch, {"a": (True, ["valid"]), "b": (True, ["valid"])})
    assert r["status"] == "signed"
    assert r["observed"]


def test_unqueryable_zone_is_unknown_not_unsigned(_dnssec_env, monkeypatch):
    # The honesty-critical case: no vantage could answer. "unknown" and "unsigned"
    # are different facts and must not share a value (honesty spine #3).
    r = _posture(monkeypatch, {"a": (None, []), "b": (None, [])})
    assert r["status"] == "unknown"
    assert not r["observed"]


def test_unsigned_zone_is_reported_unsigned(_dnssec_env, monkeypatch):
    r = _posture(monkeypatch, {"a": (False, []), "b": (False, [])})
    assert r["status"] == "unsigned"


def test_one_stripping_vantage_cannot_suppress_a_confirming_one(_dnssec_env, monkeypatch):
    # A041: a censoring resolver must not terminate the search.
    r = _posture(monkeypatch, {"a": (False, []), "b": (True, ["valid"])})
    assert r["status"] == "signed"


def test_expired_signature_is_not_signed_and_says_so(_dnssec_env, monkeypatch):
    r = _posture(monkeypatch, {"a": (None, ["expired"])})
    assert r["status"] == "unknown"
    assert "expired" in r["note"]


def test_transport_without_do_bit_reports_unknown_not_unsigned(_dnssec_env, monkeypatch):
    # A DoH/system vantage cannot carry the DNSSEC OK bit, so it has nothing to
    # say. Its silence must not be recorded as "this zone is unsigned".
    monkeypatch.setattr(config, "ENABLE_SYSTEM_VANTAGE", True)
    r = _posture(monkeypatch, {"a": (True, ["valid"])})
    sysv = [v for v in r["vantages"] if v["transport"] == "system"]
    assert len(sysv) == 1
    assert sysv[0]["signed"] == "unknown"


def test_rrsig_query_type_is_never_used_as_the_probe(_dnssec_env, monkeypatch):
    # S13 in one assertion: the probe must ask for DNSKEY (48), not RRSIG (46).
    asked = []
    monkeypatch.setattr(config, "PUBLIC_RESOLVERS", ["a"])

    def spy(name, qtype, server, timeout=3, do=False, rd=False):
        asked.append((qtype, do, rd))
        raise resolver.DNSError("stub")

    monkeypatch.setattr(resolver, "_query_sections", spy)
    investigate._dnssec_observed("example.com")
    assert asked, "the DNSSEC probe must issue a query"
    assert resolver.TYPE_RRSIG not in [q for q, _, _ in asked], (
        "QTYPE=RRSIG standalone is answered by nothing; it is not a probe"
    )
    assert asked[0] == (resolver.TYPE_DNSKEY, True, True), (
        "must ask DNSKEY with the DO bit set, recursively"
    )


def _rrsig(type_covered, inception, expiration):
    return (
        struct.pack(">HBBI", type_covered, 8, 2, 3600) + struct.pack(">II", expiration, inception) + b"\x00" * 4
    ).hex()


def test_valid_window():
    now = 1_800_000_000
    h = _rrsig(resolver.TYPE_DNSKEY, now - 86400, now + 86400)
    assert resolver.rrsig_window_status(h, now) == "valid"


def test_expired_window():
    now = 1_800_000_000
    h = _rrsig(resolver.TYPE_DNSKEY, now - 200000, now - 100000)
    assert resolver.rrsig_window_status(h, now) == "expired"


def test_not_yet_valid_window():
    now = 1_800_000_000
    h = _rrsig(resolver.TYPE_DNSKEY, now + 100000, now + 200000)
    assert resolver.rrsig_window_status(h, now) == "not-yet-valid"


def test_rrsig_over_another_rrset_is_wrong_type():
    now = 1_800_000_000
    h = _rrsig(resolver.TYPE_A, now - 86400, now + 86400)
    assert resolver.rrsig_window_status(h, now) == "wrong-type"


def test_garbage_is_unknown_never_valid():
    assert resolver.rrsig_window_status("zz", 1) == "unknown"
    assert resolver.rrsig_window_status("00", 1) == "unknown"


def _answer_packet(rtype, rdata, qtype=None):
    """One-answer DNS response for example.com with a caller-chosen rdata length."""
    qtype = rtype if qtype is None else qtype
    txid = 0x1234
    header = struct.pack(">HHHHHH", txid, 0x8180, 1, 1, 0, 0)
    qname = b"\x07example\x03com\x00"
    question = qname + struct.pack(">HH", qtype, 1)
    answer = qname + struct.pack(">HHIH", rtype, 1, 60, len(rdata)) + rdata
    return txid, header + question + answer


def _records(rtype, rdata):
    txid, pkt = _answer_packet(rtype, rdata)
    return resolver._parse_response(pkt, txid, rtype, expected_qname="example.com")["records"]


def test_short_a_record_is_dropped_not_hex():
    recs = _records(resolver.TYPE_A, b"\x01\x02\x03")  # 3 bytes, not 4
    assert recs == [], "a 3-byte A record must not be surfaced at all"


def test_long_a_record_is_dropped_not_hex():
    recs = _records(resolver.TYPE_A, b"\x01\x02\x03\x04\x05")
    assert recs == []


def test_short_aaaa_record_is_dropped_not_hex():
    recs = _records(resolver.TYPE_AAAA, b"\x26\x06")  # 2 bytes, not 16
    assert recs == []


def test_no_record_of_type_a_ever_carries_a_non_address_value():
    for bad in (b"", b"\x01", b"\x01\x02\x03", b"\x01\x02\x03\x04\x05\x06"):
        for r in _records(resolver.TYPE_A, bad):
            assert r["type"] != "A", "hex garbage surfaced under a parsed type name"


def test_well_formed_a_record_still_parses():
    # The guard must not throw away good data.
    recs = _records(resolver.TYPE_A, b"\x01\x02\x03\x04")
    assert recs == [{"type": "A", "value": "1.2.3.4"}]


def test_well_formed_aaaa_record_still_parses():
    recs = _records(resolver.TYPE_AAAA, b"\x26\x06" + b"\x00" * 13 + b"\x01")
    assert recs == [{"type": "AAAA", "value": "2606::1"}]


def test_sections_walker_also_drops_the_malformed_record():
    # resolve_authoritative/_read_records is the second entry point for the same
    # bug: the delegation walker feeds /api/trace and the diff reference.
    txid, pkt = _answer_packet(resolver.TYPE_A, b"\x01\x02\x03")
    parsed = resolver._parse_sections(pkt, txid, expected_qname="example.com", expected_qtype=resolver.TYPE_A)
    assert [r for r in parsed["answers"] if r["type"] == "A"] == []

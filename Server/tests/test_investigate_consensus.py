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
import unittest

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


class RcodeTypingTests(unittest.TestCase):
    """A155: the RCODE is a NUMBER on the wire. Comparing it to a name is always False."""

    def test_rcode_name_never_guesses_noerror(self):
        self.assertEqual(resolver.rcode_name(0), "NOERROR")
        self.assertEqual(resolver.rcode_name(2), "SERVFAIL")
        self.assertEqual(resolver.rcode_name(3), "NXDOMAIN")
        self.assertEqual(resolver.rcode_name(5), "REFUSED")
        # An unassigned code must surface as itself, never fall back to NOERROR.
        self.assertEqual(resolver.rcode_name(14), "RCODE14")
        self.assertEqual(resolver.rcode_name(None), "UNKNOWN")
        self.assertEqual(resolver.rcode_name("NOERROR"), "UNKNOWN")

    def test_answering_vantage_does_not_raise(self):
        # The reproduction of A155: a single vantage returning a normal NOERROR
        # answer crashed _resolve_all with AttributeError: 'int' has no 'lower'.
        with _StubVantages() as v:
            per = v.responses({"1.1.1.1": _resp(0, ["1.2.3.4"])})
        self.assertEqual(per["1.1.1.1"]["status"], "ok")
        self.assertEqual(per["1.1.1.1"]["values"], ["1.2.3.4"])

    def test_every_rcode_maps_to_its_own_typed_status(self):
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
        self.assertEqual(per["a"]["status"], "ok")
        self.assertEqual(per["b"]["status"], "rcode_servfail")
        self.assertEqual(per["c"]["status"], "rcode_refused")
        self.assertEqual(per["d"]["status"], "nxdomain")
        self.assertEqual(per["e"]["status"], "ok")  # NOERROR + no records = NODATA
        self.assertEqual(per["b"]["rcodeName"], "SERVFAIL")

    def test_unexpected_exception_degrades_one_vantage_not_the_request(self):
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
        self.assertEqual(per["boom"]["status"], "error")
        self.assertIn("RuntimeError", per["boom"]["error"])
        self.assertEqual(per["ok"]["status"], "ok")


class A045TypedFailureTests(unittest.TestCase):
    """SERVFAIL/REFUSED are failures, never successful evidence."""

    def test_all_servfail_is_inconclusive_and_names_the_rcode(self):
        with _StubVantages() as v:
            per = v.responses({"a": _resp(2), "b": _resp(2)})
        state, reason = investigate._consensus_one(per)
        self.assertEqual(state, "inconclusive")
        self.assertIn("SERVFAIL", reason)

    def test_servfail_alongside_answers_degrades_confidence(self):
        with _StubVantages() as v:
            per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(5)})
        state, _ = investigate._consensus_one(per)
        self.assertEqual(state, "consistent-degraded", "a REFUSED vantage must not be invisible to consensus")

    def test_refused_vantage_is_not_counted_as_an_agreeing_vantage(self):
        with _StubVantages() as v:
            per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(5), "c": _resp(5)})
        state, reason = investigate._consensus_one(per)
        self.assertEqual(state, "consistent-degraded")
        self.assertIn("(1/1)", reason, "refused vantages must not inflate the agreement count")


class A044EmptyVantageTests(unittest.TestCase):
    """A reachable vantage with an empty answer is DIVERGENT evidence, not absent evidence."""

    def test_selective_nxdomain_is_divergent_not_consistent(self):
        with _StubVantages() as v:
            per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(3)})
        state, reason = investigate._consensus_one(per)
        self.assertEqual(
            state,
            "divergent",
            "a vantage saying the name does not exist, while two others "
            "return an address, is selective censorship — not agreement",
        )
        self.assertIn("c", reason, "the reason must name the dissenting vantage (RCODE provenance)")

    def test_selective_nodata_is_divergent_not_consistent(self):
        with _StubVantages() as v:
            per = v.responses(
                {"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(0)}
            )  # NOERROR, zero records
        state, reason = investigate._consensus_one(per)
        self.assertEqual(state, "divergent")
        self.assertIn("c", reason)

    def test_unanimous_nodata_is_inconclusive_not_divergent(self):
        # The guard must not over-fire: everyone agreeing there are no records of this
        # type is a legitimate, common answer (a v4-only name asked for AAAA).
        with _StubVantages() as v:
            per = v.responses({"a": _resp(0), "b": _resp(0)})
        state, _ = investigate._consensus_one(per)
        self.assertEqual(state, "inconclusive")

    def test_existence_disagreement_with_no_records_is_inconclusive(self):
        with _StubVantages() as v:
            per = v.responses({"a": _resp(3), "b": _resp(0)})
        state, reason = investigate._consensus_one(per)
        self.assertEqual(state, "inconclusive")
        self.assertIn("NXDOMAIN", reason)
        self.assertIn("NODATA", reason)

    def test_genuine_agreement_still_reads_consistent(self):
        with _StubVantages() as v:
            per = v.responses({"a": _resp(0, ["1.2.3.4"]), "b": _resp(0, ["1.2.3.4"]), "c": _resp(0, ["1.2.3.4"])})
        state, _ = investigate._consensus_one(per)
        self.assertEqual(state, "consistent")


class S13DnssecPostureTests(unittest.TestCase):
    """S13: DNSSEC posture must come from a DO-bit DNSKEY query, and be tri-state."""

    def setUp(self):
        self._sys, self._doh = config.ENABLE_SYSTEM_VANTAGE, config.ENABLE_DOH
        self._pub = config.PUBLIC_RESOLVERS
        self._zss = resolver.zone_signing_status
        config.ENABLE_SYSTEM_VANTAGE = False
        config.ENABLE_DOH = False

    def tearDown(self):
        config.ENABLE_SYSTEM_VANTAGE, config.ENABLE_DOH = self._sys, self._doh
        config.PUBLIC_RESOLVERS = self._pub
        resolver.zone_signing_status = self._zss

    def _posture(self, mapping):
        config.PUBLIC_RESOLVERS = list(mapping)
        resolver.zone_signing_status = lambda zone, server, timeout, now=None, rd=False: mapping[server]
        return investigate._dnssec_observed("example.com")

    def test_signed_zone_is_reported_signed(self):
        r = self._posture({"a": (True, ["valid"]), "b": (True, ["valid"])})
        self.assertEqual(r["status"], "signed")
        self.assertTrue(r["observed"])

    def test_unqueryable_zone_is_unknown_not_unsigned(self):
        # The honesty-critical case: no vantage could answer. "unknown" and "unsigned"
        # are different facts and must not share a value (honesty spine #3).
        r = self._posture({"a": (None, []), "b": (None, [])})
        self.assertEqual(r["status"], "unknown")
        self.assertFalse(r["observed"])

    def test_unsigned_zone_is_reported_unsigned(self):
        r = self._posture({"a": (False, []), "b": (False, [])})
        self.assertEqual(r["status"], "unsigned")

    def test_one_stripping_vantage_cannot_suppress_a_confirming_one(self):
        # A041: a censoring resolver must not terminate the search.
        r = self._posture({"a": (False, []), "b": (True, ["valid"])})
        self.assertEqual(r["status"], "signed")

    def test_expired_signature_is_not_signed_and_says_so(self):
        r = self._posture({"a": (None, ["expired"])})
        self.assertEqual(r["status"], "unknown")
        self.assertIn("expired", r["note"])

    def test_transport_without_do_bit_reports_unknown_not_unsigned(self):
        # A DoH/system vantage cannot carry the DNSSEC OK bit, so it has nothing to
        # say. Its silence must not be recorded as "this zone is unsigned".
        config.ENABLE_SYSTEM_VANTAGE = True
        r = self._posture({"a": (True, ["valid"])})
        sysv = [v for v in r["vantages"] if v["transport"] == "system"]
        self.assertEqual(len(sysv), 1)
        self.assertEqual(sysv[0]["signed"], "unknown")

    def test_rrsig_query_type_is_never_used_as_the_probe(self):
        # S13 in one assertion: the probe must ask for DNSKEY (48), not RRSIG (46).
        asked = []
        config.PUBLIC_RESOLVERS = ["a"]

        def spy(name, qtype, server, timeout=3, do=False, rd=False):
            asked.append((qtype, do, rd))
            raise resolver.DNSError("stub")

        orig = resolver._query_sections
        resolver._query_sections = spy
        try:
            investigate._dnssec_observed("example.com")
        finally:
            resolver._query_sections = orig
        self.assertTrue(asked, "the DNSSEC probe must issue a query")
        self.assertNotIn(
            resolver.TYPE_RRSIG,
            [q for q, _, _ in asked],
            "QTYPE=RRSIG standalone is answered by nothing; it is not a probe",
        )
        self.assertEqual(
            asked[0], (resolver.TYPE_DNSKEY, True, True), "must ask DNSKEY with the DO bit set, recursively"
        )


class RrsigWindowStatusTests(unittest.TestCase):
    """A042/A043/B047 window classification, now owned by resolver."""

    @staticmethod
    def _rrsig(type_covered, inception, expiration):
        return (
            struct.pack(">HBBI", type_covered, 8, 2, 3600) + struct.pack(">II", expiration, inception) + b"\x00" * 4
        ).hex()

    def test_valid_window(self):
        now = 1_800_000_000
        h = self._rrsig(resolver.TYPE_DNSKEY, now - 86400, now + 86400)
        self.assertEqual(resolver.rrsig_window_status(h, now), "valid")

    def test_expired_window(self):
        now = 1_800_000_000
        h = self._rrsig(resolver.TYPE_DNSKEY, now - 200000, now - 100000)
        self.assertEqual(resolver.rrsig_window_status(h, now), "expired")

    def test_not_yet_valid_window(self):
        now = 1_800_000_000
        h = self._rrsig(resolver.TYPE_DNSKEY, now + 100000, now + 200000)
        self.assertEqual(resolver.rrsig_window_status(h, now), "not-yet-valid")

    def test_rrsig_over_another_rrset_is_wrong_type(self):
        now = 1_800_000_000
        h = self._rrsig(resolver.TYPE_A, now - 86400, now + 86400)
        self.assertEqual(resolver.rrsig_window_status(h, now), "wrong-type")

    def test_garbage_is_unknown_never_valid(self):
        self.assertEqual(resolver.rrsig_window_status("zz", 1), "unknown")
        self.assertEqual(resolver.rrsig_window_status("00", 1), "unknown")


def _answer_packet(rtype, rdata, qtype=None):
    """One-answer DNS response for example.com with a caller-chosen rdata length."""
    qtype = rtype if qtype is None else qtype
    txid = 0x1234
    header = struct.pack(">HHHHHH", txid, 0x8180, 1, 1, 0, 0)
    qname = b"\x07example\x03com\x00"
    question = qname + struct.pack(">HH", qtype, 1)
    answer = qname + struct.pack(">HHIH", rtype, 1, 60, len(rdata)) + rdata
    return txid, header + question + answer


class A115LyingRdlengthTests(unittest.TestCase):
    """A record whose rdlength does not match its type width is MALFORMED.

    It must never be surfaced as a parsed A/AAAA. The old fall-through hex-encoded the
    short rdata and shipped it under type "A", so `1.2.3` arrived at /api/diff and the
    passive-DNS index as the string "010203" labelled an IPv4 address."""

    def _records(self, rtype, rdata):
        txid, pkt = _answer_packet(rtype, rdata)
        return resolver._parse_response(pkt, txid, rtype, expected_qname="example.com")["records"]

    def test_short_a_record_is_dropped_not_hex(self):
        recs = self._records(resolver.TYPE_A, b"\x01\x02\x03")  # 3 bytes, not 4
        self.assertEqual(recs, [], "a 3-byte A record must not be surfaced at all")

    def test_long_a_record_is_dropped_not_hex(self):
        recs = self._records(resolver.TYPE_A, b"\x01\x02\x03\x04\x05")
        self.assertEqual(recs, [])

    def test_short_aaaa_record_is_dropped_not_hex(self):
        recs = self._records(resolver.TYPE_AAAA, b"\x26\x06")  # 2 bytes, not 16
        self.assertEqual(recs, [])

    def test_no_record_of_type_a_ever_carries_a_non_address_value(self):
        for bad in (b"", b"\x01", b"\x01\x02\x03", b"\x01\x02\x03\x04\x05\x06"):
            with self.subTest(rdlength=len(bad)):
                for r in self._records(resolver.TYPE_A, bad):
                    self.assertNotEqual(r["type"], "A", "hex garbage surfaced under a parsed type name")

    def test_well_formed_a_record_still_parses(self):
        # The guard must not throw away good data.
        recs = self._records(resolver.TYPE_A, b"\x01\x02\x03\x04")
        self.assertEqual(recs, [{"type": "A", "value": "1.2.3.4"}])

    def test_well_formed_aaaa_record_still_parses(self):
        recs = self._records(resolver.TYPE_AAAA, b"\x26\x06" + b"\x00" * 13 + b"\x01")
        self.assertEqual(recs, [{"type": "AAAA", "value": "2606::1"}])

    def test_sections_walker_also_drops_the_malformed_record(self):
        # resolve_authoritative/_read_records is the second entry point for the same
        # bug: the delegation walker feeds /api/trace and the diff reference.
        txid, pkt = _answer_packet(resolver.TYPE_A, b"\x01\x02\x03")
        parsed = resolver._parse_sections(pkt, txid, expected_qname="example.com", expected_qtype=resolver.TYPE_A)
        self.assertEqual([r for r in parsed["answers"] if r["type"] == "A"], [])


if __name__ == "__main__":
    unittest.main()

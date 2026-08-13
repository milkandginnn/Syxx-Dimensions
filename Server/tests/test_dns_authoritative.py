"""Hostile-input tests for the authoritative DNS front-end (dns_authoritative).

This module had ZERO tests (audit 2026-07-16) despite being the most exposed code in the product:
`_build_response` unpacks attacker-controlled UDP off the open internet, and it is the sole caller
of `leak.observe`, which writes to the DB and feeds the passive-DNS index behind drift results.

The bar here is NOT "does it resolve correctly" — it is: a malformed, truncated, or hostile packet
must never raise, never write, and never be answered authoritatively for a zone we do not own. A
crash is a DoS; an out-of-zone AA answer is the server lying about names it has no authority over.

Run from Server/:  venv/bin/python -m pytest tests/test_dns_authoritative.py -q
"""
import os
import struct
import unittest

# A214: use the shared temp DB from conftest.py (loaded by pytest before any test module).
import conftest
_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ["SYXX_LEAK_ZONES"] = "leak.syxx.app"

from syxx_server import config, db, dns_authoritative as dnsauth  # noqa: E402

# A214: force DB_PATH to the conftest-managed temp DB and re-init so tables exist.
config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


def _query(qname="leak.syxx.app", qtype=1, txid=b"\xab\xcd", qd_count=1, rd=1):
    """Build a well-formed DNS query packet."""
    flags = (rd << 8)
    header = txid + struct.pack(">H", flags) + struct.pack(">HHHH", qd_count, 0, 0, 0)
    body = b""
    for label in qname.rstrip(".").split("."):
        body += bytes([len(label)]) + label.encode("ascii")
    body += b"\x00" + struct.pack(">HH", qtype, 1)
    return header + body


class HostileInputTests(unittest.TestCase):
    """Every one of these is a packet an attacker can send to the open port."""

    def test_truncated_packets_never_raise(self):
        # Every prefix of a valid query — the classic fuzz of a length-prefixed parser.
        full = _query()
        for n in range(0, len(full)):
            with self.subTest(prefix_len=n):
                try:
                    resp, leak = dnsauth._build_response(full[:n])
                except Exception as exc:                      # noqa: BLE001
                    self.fail(f"truncated packet of {n} bytes raised {type(exc).__name__}: {exc}")
                # A packet we cannot parse must produce NO answer and NO leak observation.
                self.assertIsNone(resp,
                    f"truncated packet of {n} bytes must not produce a response")
                self.assertIsNone(leak,
                    f"truncated packet of {n} bytes must not produce a leak observation")

    def test_empty_and_tiny_packets_are_dropped(self):
        for data in (b"", b"\x00", b"\xab\xcd", b"\x00" * 11):
            resp, leak = dnsauth._build_response(data)
            self.assertIsNone(resp, f"a {len(data)}-byte packet must not be answered")
            self.assertIsNone(leak)

    def test_qdcount_zero_is_dropped_not_answered(self):
        # A header claiming zero questions has no question to answer; answering anyway would mean
        # fabricating a question we were never asked.
        resp, leak = dnsauth._build_response(_query(qd_count=0))
        self.assertIsNone(resp)
        self.assertIsNone(leak)

    def test_qdcount_lies_about_more_questions_than_present(self):
        # Header says 5 questions, body carries one. Must not read past the buffer.
        try:
            resp, leak = dnsauth._build_response(_query(qd_count=5))
        except Exception as exc:                              # noqa: BLE001
            self.fail(f"lying qdcount raised {type(exc).__name__}: {exc}")
        # A224: outcome must be asserted — a lying qdcount is a malformed packet, and a
        # malformed packet must never record a leak observation. The sibling test
        # (compression_pointer_loop) already models this correctly.
        self.assertIsNone(resp, "a malformed packet (lying qdcount) must not produce a response")
        self.assertIsNone(leak, "a malformed qdcount must never record a leak observation")

    def test_compression_pointer_loop_does_not_hang_or_raise(self):
        # A name whose pointer points at itself: the classic decompression bomb. Must be rejected
        # by the parser's loop guard rather than spinning or blowing the stack.
        header = b"\xab\xcd" + struct.pack(">H", 0x0100) + struct.pack(">HHHH", 1, 0, 0, 0)
        # 0xC00C = pointer to offset 12, which is this very name.
        body = b"\xc0\x0c" + struct.pack(">HH", 1, 1)
        try:
            resp, leak = dnsauth._build_response(header + body)
        except Exception as exc:                              # noqa: BLE001
            self.fail(f"compression loop raised {type(exc).__name__}: {exc}")
        self.assertIsNone(resp, "a compression-loop packet must not produce a response")
        self.assertIsNone(leak, "an unparsable name must never record a leak observation")

    def test_oversized_label_length_is_rejected(self):
        # Label length byte claims 200 bytes but the packet ends — a buffer overread if unguarded.
        header = b"\xab\xcd" + struct.pack(">H", 0x0100) + struct.pack(">HHHH", 1, 0, 0, 0)
        body = bytes([200]) + b"a" * 5
        try:
            resp, leak = dnsauth._build_response(header + body)
        except Exception as exc:                              # noqa: BLE001
            self.fail(f"oversized label raised {type(exc).__name__}: {exc}")
        # A224: the test name says "is rejected" — assert that the rejection actually happened.
        # An oversized label is unparseable; it must not produce a leak observation, and the
        # response (if any) must be a refusal, not an authoritative answer for a garbage name.
        self.assertIsNone(leak, "an oversized label must never record a leak observation")
        self.assertIsNone(resp, "an oversized label must produce no response")


class AuthorityBoundaryTests(unittest.TestCase):
    """The server must only ever speak authoritatively for zones it actually owns."""

    def test_out_of_zone_query_is_refused_without_authority(self):
        resp, leak = dnsauth._build_response(_query(qname="example.com"))
        self.assertIsNotNone(resp, "an out-of-zone query should get a REFUSED, not silence")
        self.assertIsNone(leak, "an out-of-zone query must never record a leak observation")

        flags = struct.unpack(">H", resp[2:4])[0]
        self.assertEqual(flags & 0x000F, 5, "out-of-zone must be RCODE=5 (REFUSED)")
        self.assertEqual(flags & 0x0400, 0, "REFUSED must NOT set the AA bit — we are not authoritative")
        self.assertEqual(flags & 0x8000, 0x8000, "must be a response, not a query")

    def test_txid_is_echoed_so_a_response_cannot_be_cross_matched(self):
        resp, _ = dnsauth._build_response(_query(qname="example.com", txid=b"\x12\x34"))
        self.assertEqual(resp[0:2], b"\x12\x34")

    def test_apex_query_is_answered_authoritatively(self):
        # Mutation guard for the REFUSED tests: the server must still serve its OWN zone with AA,
        # otherwise "refuse everything" would pass the boundary tests above.
        resp, leak = dnsauth._build_response(_query(qname="leak.syxx.app", qtype=2))
        self.assertIsNotNone(resp)
        flags = struct.unpack(">H", resp[2:4])[0]
        self.assertEqual(flags & 0x0400, 0x0400, "our own zone must be answered with AA=1")
        self.assertEqual(flags & 0x000F, 0, "our own zone must not be REFUSED")

    def test_hyphenated_token_probe_and_https_qtype_are_observed(self):
        # Web frontend probes use <token>-<index>-<random>.leak.syxx.app
        # And modern browsers send HTTPS (type 65) queries.
        # Verify that both are correctly recognized and produce a valid leak_hit.
        resp, leak = dnsauth._build_response(_query(qname="mytoken123-0-abc123.leak.syxx.app", qtype=65))
        self.assertIsNotNone(resp)
        self.assertIsNotNone(leak)
        self.assertEqual(leak[0], "mytoken123")
        self.assertEqual(leak[1], 65)


if __name__ == "__main__":
    unittest.main()


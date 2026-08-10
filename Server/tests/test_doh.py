"""D-1 DoH forwarder tests (encrypted-transports server plan).

Two things must hold above all:
  1. HONEST REFUSAL — an unauthenticated caller gets 402, never an answer. An open DoH forwarder
     that answers anyone is both an abuse magnet and a free bypass of the paywall.
  2. PASSTHROUGH INTEGRITY — forward_wire returns the upstream bytes UNCHANGED, and an off-txid or
     off-question reply is REJECTED, never handed to the client as an answer. Re-encoding or
     accepting a mismatched reply is the "fabricated clean answer" the contract forbids.
"""

import os
import struct
from unittest.mock import patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import resolver  # noqa: E402


def _query(name="example.com", qtype=1):
    txid, packet, _qname, _qt, _qclass = resolver._build_query(name, qtype)
    return txid, packet


def _reply_for(query_wire, *, override_txid=None, override_question=None, tc=False):
    """Build a minimal well-formed response that echoes the query's txid+question (or overrides,
    to simulate a spoofed/off-question reply), with one A record."""
    txid = query_wire[0:2] if override_txid is None else override_txid
    _t, question = resolver._question_bytes(query_wire)
    if override_question is not None:
        question = override_question
    flags = 0x8180 | (0x0200 if tc else 0)  # QR+RD+RA, optional TC
    header = txid + struct.pack(">HHHHH", flags, 1, 1, 0, 0)
    # Answer: compressed name pointer to the question (0xC00C), A/IN, ttl 60, 4-byte rdata.
    answer = b"\xc0\x0c" + struct.pack(">HHIH", 1, 1, 60, 4) + bytes([93, 184, 216, 34])
    return header + question + answer


def test_extracts_txid_and_question():
    txid, wire = _query("example.com", 1)
    got_txid, question = resolver._question_bytes(wire)
    assert got_txid == struct.pack(">H", txid)
    assert question.endswith(struct.pack(">HH", 1, 1))  # qtype=A qclass=IN


def test_rejects_a_too_short_message():
    with pytest.raises(resolver.DNSError):
        resolver._question_bytes(b"\x00\x01\x02")


def _run_with_reply(query_wire, reply):
    class _FakeSock:
        def __init__(self, *a, **k):
            pass

        def settimeout(self, t):
            pass

        def connect(self, addr):
            pass

        def send(self, data):
            return len(data)

        def recv(self, n):
            return reply

        def close(self):
            pass

    with patch("syxx_server.resolver.socket.socket", _FakeSock):
        return resolver.forward_wire(query_wire, "1.1.1.1", timeout=1)


def test_returns_upstream_bytes_unchanged():
    _txid, wire = _query()
    reply = _reply_for(wire)
    out = _run_with_reply(wire, reply)
    assert out == reply, "the client must receive the exact upstream bytes"


def test_rejects_a_txid_mismatch():
    # A reply with the wrong txid is a spoof; it must never be passed through as an answer.
    _txid, wire = _query()
    spoof = _reply_for(wire, override_txid=b"\xff\xff")
    with pytest.raises(resolver.DNSError):
        _run_with_reply(wire, spoof)


def test_rejects_an_off_question_reply():
    # Right txid, wrong question (answering a DIFFERENT name) — the cache-poisoning shape. Reject.
    _txid, wire = _query("example.com", 1)
    _t2, otherq = resolver._question_bytes(_query("evil.example", 1)[1])
    off = _reply_for(wire, override_question=otherq)
    with pytest.raises(resolver.DNSError):
        _run_with_reply(wire, off)


def _run_json(query, *, upstream=None, error=None):
    from syxx_server import app

    sent = {}

    class _Double:
        _DOH_JSON_TYPES = app.Handler._DOH_JSON_TYPES

        def _send(self, status, body, *a, **k):
            sent["status"], sent["body"] = status, body

        def _json_error(self, status, msg, *a, **k):
            sent["status"], sent["body"] = status, msg

    with patch("syxx_server.resolver.query", side_effect=error, return_value=upstream) as q:
        if error is not None:
            q.side_effect = error
        app.Handler._doh_respond_json(_Double(), query)
    return sent


def test_upstream_failure_is_status_2_not_a_fake_clean():
    sent = _run_json({"name": ["example.com"], "type": ["A"]}, error=resolver.DNSError("unreachable"))
    assert sent["status"] == 200
    assert sent["body"]["Status"] == 2, "an upstream failure must be Status:2, never 0"
    assert "Answer" not in sent["body"]  # no fabricated empty answer set


def test_real_records_pass_through_with_status_0():
    parsed = {"rcode": 0, "truncated": False, "ad": False, "records": [{"type": "A", "value": "93.184.216.34"}]}
    sent = _run_json({"name": ["example.com"], "type": ["A"]}, upstream=parsed)
    assert sent["body"]["Status"] == 0
    assert sent["body"]["Answer"][0]["data"] == "93.184.216.34"

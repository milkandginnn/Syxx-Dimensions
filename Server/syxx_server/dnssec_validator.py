"""DNSSEC chain-of-trust validator.

Walks root → target, verifying DS↔DNSKEY digests and RRSIG cryptographic
signatures at each delegation step.  Supports RSASHA256 (alg 8) and
ECDSAP256SHA256 (alg 13); other algorithms → inconclusive for that step.

HONESTY (AGENTS.md §1, §10): result is 'valid' ONLY when EVERY step passes.
Any inconclusive step prevents 'valid'.  Verification is tri-state:
True (valid) / False (invalid) / None (unsupported → inconclusive).
"""

import hashlib
import socket
import struct
import time

from .resolver import (
    ROOT_SERVERS,
    TYPE_DNSKEY,
    TYPE_RRSIG,
    DNSError,
    _addr_family,
    _build_query,
    _in_bailiwick,
    _ip_is_public,
    _read_name,
    _recv_exact,
    _rrsig_window_valid,
    resolve_authoritative,
)

TYPE_DS = 43
TYPE_NSEC = 47
TYPE_NSEC3 = 50
TYPE_NSEC3PARAM = 51
TYPE_RRSIG = 46
_ALGO = {
    5: "RSASHA1",
    7: "RSASHA1-NSEC3-SHA1",
    8: "RSASHA256",
    10: "RSASHA512",
    13: "ECDSAP256SHA256",
    14: "ECDSAP384SHA384",
    15: "ED25519",
    16: "ED448",
}
_DIG = {1: "SHA-1", 2: "SHA-256", 4: "SHA-384"}
_MAXD, _MAXS = 16, 4
_cry = False
try:
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import ec, padding

    _cry = True
except ImportError:
    pass


def _nw(name):
    """Canonical (lowercase) DNS wire-format name. Uses IDNA encoding (RFC 3490)
    so internationalized domain names are Punycode-encoded correctly."""
    if name in (".", ""):
        return b"\x00"
    # Strip trailing dot for encoding, then re-append for wire format
    domain = name.rstrip(".")
    try:
        encoded = domain.encode("idna")
    except (UnicodeError, UnicodeDecodeError):
        # IDNA encoding failed — fall back to ASCII with replacement
        encoded = domain.encode("ascii", "replace")
    return encoded.lower() + b"\x00"


_MAX_RECORDS_PER_SECTION = 256  # A160: cap parsed records per section


def _rr_raw(d, off, n):
    rs = []
    for _ in range(min(n, _MAX_RECORDS_PER_SECTION)):
        if off >= len(d):
            break
        try:
            ow, off = _read_name(d, off)
        except DNSError:
            break
        if off + 10 > len(d):
            break
        rt, rc, ttl, rdl = struct.unpack(">HHIH", d[off : off + 10])
        off += 10
        if off + rdl > len(d):
            break
        rs.append({"ow": ow, "rt": rt, "rc": rc, "ttl": ttl, "rd": d[off : off + rdl], "pk": d, "ro": off})
        off += rdl
    return rs, off


def _parse(d, tid, qn=None, qt=None):
    if len(d) < 12:
        raise DNSError("short header")
    t, fl, qd, an, ns, ar = struct.unpack(">HHHHHH", d[:12])
    if t != tid:
        raise DNSError("txid mismatch")
    if not (fl & 0x8000):
        raise DNSError("not a response")
    off = 12
    for _ in range(qd):
        q, off = _read_name(d, off)
        if off + 4 > len(d):
            raise DNSError("truncated question")
        qt2, qc = struct.unpack(">HH", d[off : off + 4])
        off += 4
        if qn and (q.lower() != qn.lower() or qt2 != qt or qc != 1):
            raise DNSError("question mismatch")
        break
    ans, off = _rr_raw(d, off, an)
    auth, off = _rr_raw(d, off, ns)
    addl, off = _rr_raw(d, off, ar)
    return {"rc": fl & 0xF, "aa": bool(fl & 0x400), "tc": bool(fl & 0x200), "ans": ans, "auth": auth, "addl": addl}


def _q(name, qtype, srv, to=5, deadline=None):
    """RD=0, DO=1 query returning raw rdata. TCP fallback on TC.

    A162: accepts an optional monotonic deadline. The per-socket timeout is
    clamped to the remaining budget so the aggregate deadline is honoured
    inside each individual query, not just between phases."""
    # A162: clamp socket timeout to remaining deadline budget
    if deadline is not None:
        remaining = deadline - time.time()
        if remaining <= 0:
            raise DNSError("deadline exceeded")
        to = min(to, remaining)
    tid, pk, qn, qt, qc = _build_query(name, qtype, rd=False, do=True)
    s = socket.socket(_addr_family(srv), socket.SOCK_DGRAM)
    s.settimeout(to)
    # A169: 65535-byte buffer to avoid silent local truncation of oversized UDP replies
    try:
        s.connect((srv, 53))
        s.send(pk)
        d = s.recv(65535)
    except OSError:
        raise DNSError("network error")
    finally:
        s.close()
    p = _parse(d, tid, qn=qn, qt=qt)
    if p["tc"]:
        # A162: re-clamp for TCP fallback
        if deadline is not None:
            remaining = deadline - time.time()
            if remaining <= 0:
                raise DNSError("deadline exceeded")
            to = min(to, remaining)
        fr = struct.pack(">H", len(pk)) + pk
        ts = socket.socket(_addr_family(srv), socket.SOCK_STREAM)
        ts.settimeout(to)
        try:
            ts.connect((srv, 53))
            ts.sendall(fr)
            (ln,) = struct.unpack(">H", _recv_exact(ts, 2))
            # SD-72: cap server-declared TCP length at 16384 bytes.
            # An attacker could send a huge length to exhaust memory.
            if ln > 16384:
                raise DNSError("server-declared TCP length exceeds 16384")
            p = _parse(_recv_exact(ts, ln), tid, qn=qn, qt=qt)
        finally:
            ts.close()
    return p


def _sq(name, qtype, srv, to=5, deadline=None):
    try:
        return _q(name, qtype, srv, to, deadline=deadline)
    except DNSError:
        return None


def _pdk(r):
    if len(r) < 4:
        return None
    f, p, a = struct.unpack(">HBB", r[:4])
    return f, p, a, r[4:]


def _pds(r):
    if len(r) < 4:
        return None
    k, a, d = struct.unpack(">HBB", r[:4])
    return k, a, d, r[4:]


def _psig(r, pk, ro):
    if len(r) < 18:
        return None
    tc, al, lb, ot = struct.unpack(">HBBI", r[:8])
    ex, inc, kt = struct.unpack(">IIH", r[8:18])
    try:
        sg, ne = _read_name(pk, ro + 18)
    except DNSError:
        return None
    ss = ne - ro
    if ss > len(r):
        return None
    return {
        "tc": tc,
        "al": al,
        "lb": lb,
        "ot": ot,
        "ex": ex,
        "inc": inc,
        "kt": kt,
        "sg": sg,
        "sig": r[ss:],
        "hd": r[:18],
        "sn": r[18:ss],
    }


def _vec(pub, sig, d):
    if not _cry or len(pub) != 64:
        return None
    try:
        ec.EllipticCurvePublicNumbers(
            x=int.from_bytes(pub[:32], "big"), y=int.from_bytes(pub[32:], "big"), curve=ec.SECP256R1()
        ).public_key().verify(sig, d, ec.ECDSA(hashes.SHA256()))
        return True
    except Exception:
        return False


def _vrsa(pub, sig, d):
    if not _cry:
        return None
    try:
        serialization.load_der_public_key(pub).verify(sig, d, padding.PKCS1v15(), hashes.SHA256())
        return True
    except Exception:
        return False


def _cv(algo, pub, sig, d):
    if algo == 8:
        return _vrsa(pub, sig, d)
    if algo == 13:
        return _vec(pub, sig, d)
    return None


def _dsm(ds, dk, ow):
    """DS digest match (RFC 4034 §5.1.4). Tri-state."""
    kt, da, dt, dd = ds
    kf, kp, ka, kk = dk
    if da != ka:
        return False
    w = _nw(ow)
    if w is None:
        return None
    h = w + struct.pack(">HBB", kf, kp, ka) + kk
    if dt == 1:
        return hashlib.sha1(h).digest() == dd
    if dt == 2:
        return hashlib.sha256(h).digest() == dd
    if dt == 4:
        return hashlib.sha384(h).digest() == dd
    return None


def _sd(sig, rrs):
    """Build signed data per RFC 4034 §3.1.8.1."""
    out = []
    for r in rrs:
        w = _nw(r["ow"])
        if w is None:
            return None
        out.append(w + struct.pack(">HHIH", r["rt"], r["rc"], sig["ot"], len(r["rd"])) + r["rd"])
    out.sort()
    # Canonicalize signer name: _nw lowercases and produces wire format per RFC 4034
    sn_wire = _nw(sig["sg"])
    if sn_wire is None:
        return None
    return sig["hd"] + sn_wire + b"".join(out)


def _kt(r):
    ac = 0
    for i, b in enumerate(r):
        ac += b << (8 if (i % 2) == 0 else 0)
    ac += (ac >> 16) & 0xFFFF
    return ac & 0xFFFF


def _vsig(sigs, rrs, dkp, dkr, now, zone=None):
    """Verify RRSIG over RRset. Tri-state: True/False/None.
    SD-94: when zone is provided, verify the signer name matches the zone apex."""
    if dkp is None or not sigs or not rrs:
        return None
    dkt = _kt(dkr["rd"])
    unsup = False
    for s in sigs:
        sg = _psig(s["rd"], s["pk"], s["ro"])
        if not sg or sg["tc"] != rrs[0]["rt"] or sg["kt"] != dkt:
            continue
        if not _rrsig_window_valid(sg["inc"], sg["ex"], now):
            continue
        # SD-94: verify the signer name matches the zone apex
        if zone is not None and sg["sg"].rstrip(".").lower() != zone.rstrip(".").lower():
            continue
        d = _sd(sg, rrs)
        if d is None:
            continue
        r = _cv(sg["al"], dkp[3], sg["sig"], d)
        if r is True:
            return True
        if r is None:
            unsup = True
    return None if unsup else False


def _find(name, to, deadline=None):
    """Walk root→name to find authoritative server. Returns (ip, zone).

    A162: accepts an optional deadline so the walk honours the aggregate budget
    from validate_chain. Each query and sub-walk checks the deadline."""
    srvs, z = list(ROOT_SERVERS), "."
    for _ in range(_MAXD):
        # A162: check deadline at each depth level
        if deadline is not None and time.time() > deadline:
            return None, z
        resp, u = None, None
        for s in srvs[:_MAXS]:
            try:
                resp = _q(name, 1, s, to, deadline=deadline)
                u = s
                break
            except DNSError:
                continue
        if resp is None:
            return None, z
        has_soa = any(r["rt"] == 6 for r in resp["auth"])
        if resp["ans"] or resp["rc"] == 3 or has_soa or not resp["auth"]:
            return u, z
        nsd = [r for r in resp["auth"] if r["rt"] == 2]
        if not nsd:
            return None, z
        dz = nsd[0]["ow"]
        if not _in_bailiwick(dz, z):
            return None, z
        gl = {}
        for r in resp["addl"]:
            if r["rt"] == 1 and _in_bailiwick(r["ow"], dz) and len(r["rd"]) == 4:
                ip = socket.inet_ntop(socket.AF_INET, r["rd"])
                if _ip_is_public(ip):
                    gl[r["ow"].rstrip(".")] = ip
        nxt = []
        for r in nsd:
            try:
                nn, _ = _read_name(r["pk"], r["ro"])
            except DNSError:
                continue
            if nn.rstrip(".") in gl:
                nxt.append(gl[nn.rstrip(".")])
        if not nxt:
            for r in nsd[:2]:
                try:
                    nn, _ = _read_name(r["pk"], r["ro"])
                except DNSError:
                    continue
                try:
                    # A162: thread deadline into sub-walks
                    sub = resolve_authoritative(nn, 1, to, 4, deadline=deadline)
                    if sub.get("authoritative") and sub["authoritative"]["answers"]:
                        ip = sub["authoritative"]["answers"][0]
                        if _ip_is_public(ip):
                            nxt.append(ip)
                            break
                except DNSError:
                    continue
        if not nxt:
            return None, z
        srvs, z = nxt, dz
    return None, z


def _step(ch, zone, step, status, **kw):
    """Append a chain step dict."""
    e = {"zone": zone, "step": step, "status": status}
    e.update(kw)
    ch.append(e)


def _ksk(rs):
    """Pick the KSK (SEP bit set) DNSKEY. SD-74: filter by protocol==3."""
    for r in rs:
        p = _pdk(r["rd"])
        if p and (p[0] & 1) and p[1] == 3:
            return r
    # Fallback: try any DNSKEY with protocol==3 (some zones lack SEP bit)
    for r in rs:
        p = _pdk(r["rd"])
        if p and p[1] == 3:
            return r
    return rs[0] if rs else None


def _chk_ans(sigs, arecs, dkrs, now, zone=None):
    if not sigs or not arecs:
        return None
    # SD-74: filter DNSKEYs by protocol==3 before using them for verification
    keys = [(r, _pdk(r["rd"])) for r in dkrs if _pdk(r["rd"]) and _pdk(r["rd"])[1] == 3]
    if not keys:
        return None
    unsup = False
    for dr, dp in keys:
        # A158: pass zone so SD-94 signer-name check applies to answer RRSIGs
        r = _vsig(sigs, arecs, dp, dr, now, zone=zone)
        if r is True:
            return True
        if r is None:
            unsup = True
    return None if unsup else False


def _r(domain, result, signed, algorithm, chain, errors):
    return {
        "domain": domain,
        "conclusion": result,
        "signed": signed,
        "algorithm": algorithm,
        "chain": chain,
        "errors": errors,
    }


# --- public API ---
def validate_chain(domain, qtype=1, timeout=5):
    """Validate DNSSEC chain of trust for *domain*.

    Returns dict: result 'valid' only when EVERY step passes crypto
    verification; 'inconclusive' if any step unverifiable; 'invalid' on
    crypto failure; 'unsigned' if no DNSSEC records found.
    """
    domain = (domain or "").rstrip(".").lower()
    if not domain:
        return _r(domain, "inconclusive", False, None, [], ["empty domain"])
    ch, er, now = [], [], time.time()
    deadline = now + min(timeout * 4, 30.0)  # SD-71: aggregate wall-clock cap
    labels = domain.split(".")
    zones = ["."]
    for i in range(len(labels)):
        z = ".".join(labels[-(i + 1) :]) + "."
        if z not in zones:
            zones.append(z)
    # Root DNSKEY
    if time.time() > deadline:
        return _r(domain, "inconclusive", False, None, ch, ["exceeded aggregate deadline at root"])
    rv, _ = _find(".", timeout, deadline=deadline)
    if rv is None:
        return _r(domain, "inconclusive", False, None, ch, ["no root server"])
    rr = _sq(".", TYPE_DNSKEY, rv, timeout, deadline=deadline)
    if rr is None:
        return _r(domain, "inconclusive", False, None, ch, ["root DNSKEY failed"])
    rdks = [r for r in rr["ans"] if r["rt"] == TYPE_DNSKEY]
    if not rdks:
        _step(ch, ".", "root-dnskey", "invalid", detail="no DNSKEY at root")
        return _r(domain, "unsigned", False, None, ch, er)
    rk = _ksk(rdks)
    rkp = _pdk(rk["rd"])
    if rkp is None:
        _step(ch, ".", "root-dnskey", "inconclusive", detail="bad root DNSKEY")
        return _r(domain, "inconclusive", False, None, ch, er)
    ra = _ALGO.get(rkp[2], "algorithm-%d" % rkp[2])
    _step(ch, ".", "root-dnskey", "valid", algorithm=ra, keyTag=_kt(rk["rd"]), detail=f"root DNSKEY ({ra})")
    pv_s, inv, inc, la = rv, False, False, ra
    apex = "."  # A158: track deepest zone with valid ds-match for answer-path verification
    # Walk zones
    for zone in zones[1:]:
        if time.time() > deadline:
            _step(ch, zone, "deadline", "inconclusive", detail="exceeded aggregate deadline")
            inc = True
            break
        zv, _ = _find(zone.rstrip("."), timeout, deadline=deadline)
        if zv is None:
            _step(ch, zone, "ns-lookup", "inconclusive", detail="no NS")
            inc = True
            break
        dr = _sq(zone, TYPE_DS, pv_s, timeout, deadline=deadline)
        if dr is None:
            _step(ch, zone, "ds-match", "inconclusive", detail="DS query failed")
            inc = True
            break
        dsr = [r for r in dr["ans"] if r["rt"] == TYPE_DS]
        if not dsr:
            _step(ch, zone, "ds-match", "inconclusive", detail="no DS in parent")
            inc = True
            break
        dkr = _sq(zone, TYPE_DNSKEY, zv, timeout, deadline=deadline)
        if dkr is None:
            _step(ch, zone, "dnskey-rrsig", "inconclusive", detail="DNSKEY failed")
            inc = True
            break
        cdks = [r for r in dkr["ans"] if r["rt"] == TYPE_DNSKEY]
        csig = [r for r in dkr["ans"] if r["rt"] == TYPE_RRSIG]
        if not cdks:
            _step(ch, zone, "ds-match", "invalid", detail="no DNSKEY")
            inv = True
            break
        m, mk, mkp = False, None, None
        for ds in dsr:
            dp = _pds(ds["rd"])
            if not dp:
                continue
            for dk in cdks:
                kp = _pdk(dk["rd"])
                if kp and _dsm(dp, kp, zone) is True:
                    m, mk, mkp = True, dk, kp
                    break
            if m:
                break
        if not m:
            _step(ch, zone, "ds-match", "invalid", detail="DS↔DNSKEY mismatch")
            inv = True
            break
        dt = _pds(dsr[0]["rd"])
        _step(
            ch,
            zone,
            "ds-match",
            "valid",
            digestType=_DIG.get(dt[2], "unknown") if dt else "unknown",
            detail="DS↔DNSKEY verified",
        )
        apex = zone  # A158: deepest zone with valid ds-match
        vr = _vsig(csig, cdks, mkp, mk, now, zone=zone)
        if vr is True:
            _step(ch, zone, "dnskey-rrsig", "valid", detail="DNSKEY RRSIG verified")
        elif vr is False:
            _step(ch, zone, "dnskey-rrsig", "invalid", detail="DNSKEY RRSIG failed")
            inv = True
            break
        else:
            _step(ch, zone, "dnskey-rrsig", "inconclusive", detail="DNSKEY RRSIG unverifiable")
            inc = True
        la = _ALGO.get(mkp[2], "algorithm-%d" % mkp[2])
        pv_s = zv
    # A156: compute signed BEFORE the answer-RRSIG block uses it (the old
    # position at line 381 caused UnboundLocalError when the answer path
    # reached the NSEC denial-of-existence check at line 363).
    signed = any(s["step"] == "ds-match" and s["status"] == "valid" for s in ch)
    # Answer RRSIG
    # A158: query DNSKEY at the validated apex (not zones[-1] which is the full
    # queried name — for non-apex names that returns NODATA). Verify the
    # answer-time DNSKEY RRset against the DS-validated key before using those
    # keys for answer RRSIG verification, and pass zone=apex so SD-94's
    # signer-name check applies to the answer path.
    if not inv and pv_s and time.time() <= deadline:
        ar = _sq(domain, qtype, pv_s, timeout, deadline=deadline)
        if ar:
            arec = [r for r in ar["ans"] if r["rt"] == qtype]
            asig = [r for r in ar["ans"] if r["rt"] == TYPE_RRSIG]
            if arec and asig:
                tdk = _sq(apex, TYPE_DNSKEY, pv_s, timeout, deadline=deadline)
                tks = [r for r in tdk["ans"] if r["rt"] == TYPE_DNSKEY] if tdk else []
                # A158: verify the DNSKEY RRset against the DS-validated key
                # before trusting it for answer verification. An on-path server
                # could serve a forged DNSKEY RRset otherwise.
                if tks and mk and mkp and tdk:
                    tks_sigs = [r for r in tdk["ans"] if r["rt"] == TYPE_RRSIG]
                    dnskey_vr = _vsig(tks_sigs, tks, mkp, mk, now, zone=apex)
                    if dnskey_vr is not True:
                        tks = []  # unverified DNSKEY → cannot verify answers
                av = _chk_ans(asig, arec, tks, now, zone=apex)
                st = "valid" if av is True else ("invalid" if av is False else "inconclusive")
                _step(
                    ch,
                    apex,
                    "answer-rrsig",
                    st,
                    detail="answer RRSIG "
                    + ("verified" if av is True else "failed" if av is False else "unverifiable"),
                )
                if av is False:
                    inv = True
                elif av is None:
                    inc = True
            else:
                # No answer records — check for authenticated denial of existence
                # A157: pass ar["auth"] (AUTHORITY section) not ar["ans"] (ANSWER section).
                # NSEC/NSEC3 denial records live in the AUTHORITY section per RFC 4034.
                if signed:
                    denial = verify_denial_of_existence(domain, qtype, ar.get("auth", []))
                    if denial == "valid":
                        _step(
                            ch, apex, "answer-rrsig", "valid", detail="authenticated denial of existence (NSEC/NSEC3)"
                        )
                    elif denial == "invalid":
                        _step(ch, apex, "answer-rrsig", "invalid", detail="denial of existence proof failed")
                        inv = True
                    else:
                        _step(ch, apex, "answer-rrsig", "inconclusive", detail="no answer and no denial proof")
                        inc = True
                else:
                    _step(ch, apex, "answer-rrsig", "inconclusive", detail="no answer" if not arec else "no RRSIG")
                    inc = True
        else:
            _step(ch, apex, "answer-rrsig", "inconclusive", detail="query failed")
            inc = True
    signed = any(s["step"] == "ds-match" and s["status"] == "valid" for s in ch)
    if inv:
        v = "invalid"
    elif inc:
        v = "inconclusive"
    elif signed and all(s["status"] == "valid" for s in ch):
        v = "valid"
    elif not signed:
        v = "unsigned"
    else:
        v = "inconclusive"
    return _r(domain, v, signed, la if signed else None, ch, er)


# ---------------------------------------------------------------------------
# NSEC / NSEC3 denial-of-existence proof validation
# ---------------------------------------------------------------------------


def _read_nsec_bitmap(rd: bytes, off: int, rdlen: int) -> set:
    """Read the NSEC/NSEC3 type bitmap from RDATA. Returns set of covered type numbers."""
    types = set()
    end = off + rdlen
    while off < end:
        if off + 2 > end:
            break
        window = rd[off]
        bitmap_len = rd[off + 1]
        off += 2
        if off + bitmap_len > end:
            break
        for i in range(bitmap_len):
            byte = rd[off + i]
            for bit in range(8):
                if byte & (0x80 >> bit):
                    types.add(window * 256 + i * 8 + bit)
        off += bitmap_len
    return types


def _verify_nsec_denial(rrs: list, name: str, qtype: int) -> str:
    """Verify NSEC denial-of-existence for *name* and *qtype*.

    A157: returns 'inconclusive' until full NSEC validation is implemented.
    The previous implementation had four independent defects:
    1. No RRSIG check — any on-path attacker could inject a bare NSEC RR and
       obtain "valid" without the NSEC RRset being signed by the zone's
       validated DNSKEY.
    2. Wrong section — the caller passed ar["ans"] (ANSWER section) but NSEC
       denial records live in the AUTHORITY section (ar["auth"]).
    3. str vs bytes — rr["ow"] is a decoded str from _read_name but target
       was bytes, so equality was always False and ordering raised TypeError.
    4. Wrong ordering — Python str.__lt__ is not RFC 4034 §6.1 canonical DNS
       name order (label-reversed, length-then-octet).

    Until closest-encloser + next-hashed NSEC3 proof exists with proper
    signature verification via _vsig, we return inconclusive — matching
    _verify_nsec3_denial's honest approach. A result from unauthenticated
    evidence would be a claim ahead of the evidence.
    """
    nsec_rrs = [r for r in rrs if r["rt"] == TYPE_NSEC]
    if not nsec_rrs:
        return "inconclusive"

    # NSEC records are present but full validation is not yet implemented.
    # Return inconclusive rather than risk reporting unsigned NSEC as
    # "authenticated denial of existence."
    return "inconclusive"


def _verify_nsec3_denial(rrs: list, name: str, qtype: int) -> str:
    """Verify NSEC3 denial-of-existence for *name* and *qtype*.

    NSEC3 uses hashed owner names to prevent zone walking. Full NSEC3
    validation requires iterated hashing with the zone's salt and iteration
    count from NSEC3PARAM — this is a structural check only. Returns
    'inconclusive' until full closest-encloser + next-hashed proof is
    implemented.
    """
    nsec3_rrs = [r for r in rrs if r["rt"] == TYPE_NSEC3]
    if not nsec3_rrs:
        return "inconclusive"

    # Structural check passes — NSEC3 records are present and well-formed,
    # but without iterated hashing against the zone's NSEC3PARAM we cannot
    # affirmatively prove denial of existence. Per AGENTS.md §1 (no result
    # before evidence), we return inconclusive.
    return "inconclusive"


def verify_denial_of_existence(name: str, qtype: int, rrs: list) -> str:
    """Verify authenticated denial of existence via NSEC or NSEC3.

    Args:
        name: The queried domain name.
        qtype: The queried RR type (e.g., TYPE_A, TYPE_AAAA).
        rrs: Parsed RR list from the authoritative response.

    Returns:
        'valid' if denial is cryptographically proven.
        'invalid' if the proof is contradictory.
        'inconclusive' if no NSEC/NSEC3 records are present.
    """
    nsec_result = _verify_nsec_denial(rrs, name, qtype)
    if nsec_result == "valid":
        return "valid"

    nsec3_result = _verify_nsec3_denial(rrs, name, qtype)
    if nsec3_result == "valid":
        return "valid"

    if nsec_result == "invalid" or nsec3_result == "invalid":
        return "invalid"

    return "inconclusive"

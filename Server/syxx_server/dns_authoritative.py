"""Authoritative UDP DNS server + leak collector (feature: authoritative DNS).

Replaces the original 'answer everything with A 127.0.0.1' handler. It:
  * parses queries with bounds checks (no IndexError on truncated packets)
  * answers by query type (A -> sink A, AAAA -> sink AAAA, SOA/NS at apex)
  * is authoritative only for the configured leak zones; other names get REFUSED
  * records leak-token lookups (v4 AND v6) via leak.observe into SQLite

Runs on its own thread; a socket error on one datagram is logged, not swallowed
silently, and never takes the loop down.
"""

import socket
import struct
import time

from . import config, leak, listeners, logging_util, resolver

TYPE_A = 1
TYPE_NS = 2
TYPE_SOA = 6
TYPE_AAAA = 28

# A034: per-source-IP rate limit for the DNS collector. A simple sliding-window
# counter prevents a single resolver from flooding the collector with queries.
# Uses a dict of {ip: [count, window_start]} with periodic pruning.
import threading as _threading

_dns_rate_lock = _threading.Lock()
_dns_rate_buckets = {}  # ip -> [count, window_start]
# Allow 50 queries per source IP per 60-second window. Legitimate recursive
# resolvers rarely exceed this; a flood of known-token replays would.
_DNS_RATE_LIMIT = 50
_DNS_RATE_WINDOW = 60


def _dns_rate_ok(source_ip):
    """Return True if source_ip is within the per-source rate limit."""
    now = int(time.time())
    with _dns_rate_lock:
        entry = _dns_rate_buckets.get(source_ip)
        if entry is None:
            _dns_rate_buckets[source_ip] = [1, now]
            return True
        count, window_start = entry
        if now - window_start >= _DNS_RATE_WINDOW:
            # New window
            _dns_rate_buckets[source_ip] = [1, now]
            return True
        if count >= _DNS_RATE_LIMIT:
            return False
        entry[0] = count + 1
        return True


def _encode_name(name):
    out = b""
    for label in name.rstrip(".").split("."):
        b = label.encode("utf-8")
        out += bytes([len(b)]) + b
    return out + b"\x00"


def _zone_for(name):
    """Return the configured leak zone that `name` belongs to, or None.

    A129: the match uses endswith("." + zone) which enforces an exact label
    boundary — the zone suffix must be preceded by a dot (or the name equals
    the zone exactly). This prevents `notleak.syxx.app` from matching zone
    `leak.syxx.app`. DNS delegation ensures the server only receives queries
    for zones delegated to it; names like `sub.leak.syxx.app.evil.com` do NOT
    end with `.leak.syxx.app` (they end with `.evil.com`) and are excluded by
    the suffix check itself. Multi-label subdomains (e.g. `a.b.leak.syxx.app`)
    are correctly accepted as in-zone.
    """
    n = name.lower().rstrip(".")
    for zone in config.LEAK_ZONES:
        if n == zone or n.endswith("." + zone):
            return zone
    return None


def _build_response(data):
    """Return (response_bytes, leak_or_none) where leak is (token, qtype)."""
    if len(data) < 12:
        return None, None
    txid = data[0:2]
    # D063: validate QR=0 (must be a query, not a response), opcode=0 (standard
    # query only), and qdcount=1 (exactly one question). Malformed packets with
    # QR=1, non-zero opcode, or multiple questions are silently dropped to prevent
    # recording observations from non-query traffic or amplification abuse.
    flags_word = struct.unpack(">H", data[2:4])[0]
    qr = (flags_word >> 15) & 1
    opcode = (flags_word >> 11) & 0xF
    if qr != 0 or opcode != 0:
        return None, None
    qd_count = struct.unpack(">H", data[4:6])[0]
    if qd_count != 1:
        return None, None
    try:
        qname, offset = resolver._read_name(data, 12)
        if offset + 4 > len(data):
            return None, None
        qtype, qclass = struct.unpack(">HH", data[offset : offset + 4])
    except resolver.DNSError:
        return None, None
    # D063: reject queries for class != IN (Internet). CHAOS/HESIOD etc. are not
    # relevant to the leak collector and should not be recorded.
    if qclass != 1:
        return None, None

    qname_start = 12  # offset of the question name in the query
    question = data[12 : offset + 4]
    zone = _zone_for(qname)

    # Header flags: QR=1, opcode=0, RD copied from query, RA=0.
    rd = data[2] & 0x01
    if zone is None:
        # Not our zone -> REFUSED, no AA.
        flags = 0x8000 | (rd << 8) | 0x0005
        header = txid + struct.pack(">H", flags) + struct.pack(">HHHH", qd_count, 0, 0, 0)
        return header + question, None

    # Authoritative answer (AA=1).
    labels = qname.lower().rstrip(".").split(".")
    is_apex = qname.lower().rstrip(".") == zone
    has_token = len(labels) > len(zone.split("."))
    # Extract token from first label: web frontend probes use
    # token-index-random.leak.syxx.app; base32 tokens contain no
    # hyphens, so the token is the part before the first hyphen.
    # macOS client uses id-<uuid>.leak.syxx.app — splitting on
    # hyphen yields "id", which observe() safely ignores (not in DB).
    token_candidate = (labels[0].split("-", 1)[0] if "-" in labels[0] else labels[0]) if has_token else None
    # Record leak observations for ALL query types (A, AAAA, HTTPS
    # type 65, etc.) — browsers send HTTPS/SVCB probes that were
    # previously silently dropped, causing "no queries observed".
    leak_hit = (token_candidate, qtype) if token_candidate else None
    answers = b""
    ancount = 0
    rcode = 0

    if qtype == TYPE_A:
        answers += _rr_a(config.LEAK_SINK_A, qname_start)
        ancount = 1
    elif qtype == TYPE_AAAA:
        answers += _rr_aaaa(config.LEAK_SINK_AAAA, qname_start)
        ancount = 1
    elif qtype == TYPE_SOA and is_apex:
        answers += _rr_soa(qname_start)
        ancount = 1
    elif qtype == TYPE_NS and is_apex:
        answers += _rr_ns(config.DNS_SOA_MNAME, qname_start)
        ancount = 1
    else:
        # In-zone but no data for this type: NODATA (NOERROR, 0 answers).
        rcode = 0

    flags = 0x8000 | 0x0400 | (rd << 8) | rcode  # QR + AA + RD + rcode
    header = txid + struct.pack(">H", flags) + struct.pack(">HHHH", qd_count, ancount, 0, 0)
    return header + question + answers, leak_hit


def _rr_a(ip, name_offs=12):
    ptr = bytes([0xC0 | (name_offs >> 8), name_offs & 0xFF])
    return ptr + struct.pack(">HHIH", TYPE_A, 1, 60, 4) + socket.inet_pton(socket.AF_INET, ip)


def _rr_aaaa(ip, name_offs=12):
    ptr = bytes([0xC0 | (name_offs >> 8), name_offs & 0xFF])
    return ptr + struct.pack(">HHIH", TYPE_AAAA, 1, 60, 16) + socket.inet_pton(socket.AF_INET6, ip)


def _rr_ns(nsname, name_offs=12):
    rdata = _encode_name(nsname)
    ptr = bytes([0xC0 | (name_offs >> 8), name_offs & 0xFF])
    return ptr + struct.pack(">HHIH", TYPE_NS, 1, 3600, len(rdata)) + rdata


def _rr_soa(name_offs=12):
    rdata = (
        _encode_name(config.DNS_SOA_MNAME)
        + _encode_name(config.DNS_SOA_RNAME)
        + struct.pack(">IIIII", int(time.time()), 3600, 600, 604800, 60)
    )
    ptr = bytes([0xC0 | (name_offs >> 8), name_offs & 0xFF])
    return ptr + struct.pack(">HHIH", TYPE_SOA, 1, 3600, len(rdata)) + rdata


def _bind():
    """Bind DNS sockets on both IPv4 and IPv6 (dual-stack, A058).

    Returns a list of bound sockets, or an empty list if no port was available
    (e.g. :53 needs root and the fallback is taken by mDNSResponder). In that
    case the leak collector is disabled but the HTTP server keeps running.
    """
    for port in (config.DNS_PORT, config.DNS_FALLBACK_PORT):
        socks = []
        # A058: bind both IPv4 and IPv6 so the collector receives queries from
        # resolvers on either transport. An IPv6-only bind would miss the
        # majority of real-world traffic; IPv4-only misses modern IPv6-only hosts.
        for family, label, bind_addr in (
            (socket.AF_INET, "ipv4", "0.0.0.0"),
            (socket.AF_INET6, "ipv6", "::"),
        ):
            sock = socket.socket(family, socket.SOCK_DGRAM)
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            if family == socket.AF_INET6:
                # Explicitly disable IPV6_V6ONLY so the IPv6 socket does not also
                # try to handle IPv4 (we have a dedicated IPv4 socket for that).
                try:
                    sock.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 1)
                except (AttributeError, OSError):
                    pass  # not all platforms support this
            try:
                sock.bind((bind_addr, port))
                socks.append(sock)
                logging_util.info("dns_bound", family=label, port=port)
            except OSError as e:
                logging_util.warn("dns_bind_failed", family=label, port=port, err=str(e))
                sock.close()
        if not socks:
            continue
        if port == config.DNS_PORT:
            logging_util.info("dns_listening", port=port, sockets=len(socks))
            # D-0 / SD-62: report health into the shared registry. On the intended port this is
            # "listening"; on the fallback it is "degraded-fallback" — because delegation still
            # points at :53, so a collector on :5053 is up but unreachable by real clients, which
            # /readyz must surface rather than hide behind a blanket 200.
            listeners.set_state("dns", "listening", detail="port %d (%d socket(s))" % (port, len(socks)))
        else:
            logging_util.warn("dns_fallback_port", port=port)
            listeners.set_state(
                "dns",
                "degraded-fallback",
                detail="serving on :%d; delegation points at :%d — unreachable by clients" % (port, config.DNS_PORT),
            )
        return socks
    logging_util.error("dns_disabled", reason="no available UDP port; leak collector inactive")
    listeners.set_state("dns", "down", detail="no available UDP port; leak collector inactive")
    return []


def run():
    socks = _bind()
    if not socks:

        def _rebind_loop():
            while True:
                time.sleep(60)
                candidate = _bind()
                if candidate:
                    logging_util.info("dns_rebind_success", port=candidate[0].getsockname()[1], sockets=len(candidate))
                    _serve_dns(candidate)
                    return

        import threading

        logging_util.info("dns_rebind_schedule", detail="will retry bind every 60s")
        threading.Thread(target=_rebind_loop, daemon=True, name="dns-rebind").start()
        return
    _serve_dns(socks)


# A122: errno values that indicate a permanently dead socket rather than a
# transient per-packet failure. EBADF = bad file descriptor (socket was closed
# or never opened); ENOTSOCK = not a socket; EINVAL on recvfrom typically means
# the socket is in an invalid state. These errors will NEVER recover on the
# same descriptor — continuing to loop produces a tight CPU-burning spin while
# the health registry still reports "listening".
_FATAL_RECV_ERRNOS = frozenset(
    {
        9,  # EBADF
        22,  # EINVAL
        38,  # ENOTSOCK (Linux)
        57,  # ENOTSOCK (macOS/Darwin)
    }
)


def _serve_dns(socks):
    """Serve DNS queries on bound sockets (A058: dual-stack via select).

    A122: distinguish transient packet errors from fatal socket errors. A
    fatal error on a socket removes it from the active set. When all sockets
    are dead, set DNS health to "down" and enter a bounded rebind loop with
    exponential backoff rather than spinning forever on dead descriptors while
    health reports "listening".
    """
    import select

    # A122: per-socket consecutive error counters (keyed by fileno).
    sock_errors = {s.fileno(): 0 for s in socks}
    _MAX_CONSECUTIVE_ERRORS = 8  # after this many, the socket is considered dead
    while True:
        try:
            readable, _, _ = select.select(socks, [], [], 1.0)
        except (OSError, ValueError):
            # A socket may have been closed; filter out dead sockets.
            socks = [s for s in socks if s.fileno() >= 0]
            sock_errors = {s.fileno(): 0 for s in socks}
            if not socks:
                # A122: all sockets dead — set health to "down" and rebind.
                listeners.set_state("dns", "down", detail="all sockets dead in select; rebinding")
                _rebind_with_backoff()
                return
            continue
        for sock in readable:
            try:
                data, addr = sock.recvfrom(2048)
                sock_errors[sock.fileno()] = 0  # reset on success
            except OSError as e:
                errno_val = getattr(e, "errno", None) or 0
                fno = sock.fileno()
                sock_errors[fno] = sock_errors.get(fno, 0) + 1
                if errno_val in _FATAL_RECV_ERRNOS or sock_errors[fno] >= _MAX_CONSECUTIVE_ERRORS:
                    # A122: this socket is dead — remove it from the active set.
                    logging_util.error(
                        "dns_fatal_socket_error",
                        err=str(e),
                        errno=errno_val,
                        fileno=fno,
                        consecutive=sock_errors[fno],
                        detail="removing dead socket from active set",
                    )
                    socks = [s for s in socks if s is not sock]
                    sock_errors.pop(fno, None)
                    try:
                        sock.close()
                    except OSError:
                        pass
                    if not socks:
                        # A122: all sockets dead — set health to "down" and rebind.
                        listeners.set_state("dns", "down", detail="all sockets dead; rebinding")
                        _rebind_with_backoff()
                        return
                else:
                    logging_util.error("dns_recv_error", err=str(e), consecutive=sock_errors[fno])
                continue
            try:
                response, leak_hit = _build_response(data)
                if leak_hit is not None:
                    # A034: per-source rate limit on observation recording. The DNS
                    # response is always sent (the client gets a valid answer), but
                    # the DB write is skipped when the source IP has exceeded its
                    # quota, preventing unbounded INSERTs from a single resolver.
                    if _dns_rate_ok(addr[0]):
                        leak.observe(leak_hit[0], addr[0], leak_hit[1])
                    else:
                        logging_util.incr("dns_rate_limited")
                if response:
                    sock.sendto(response, addr)
            except Exception as e:  # never let one packet kill the loop
                logging_util.error("dns_handler_error", err=str(e), peer=addr[0])


def _rebind_with_backoff():
    """A122: attempt to rebind DNS sockets with exponential backoff.

    Bounded: gives up after _MAX_REBIND_ATTEMPTS and leaves the DNS listener
    in the "down" state. On success, resumes serving on the fresh sockets."""
    _MAX_REBIND_ATTEMPTS = 10
    backoff = 1.0  # start at 1 second
    for attempt in range(1, _MAX_REBIND_ATTEMPTS + 1):
        logging_util.info("dns_rebind_attempt", attempt=attempt, backoff_s=backoff)
        time.sleep(backoff)
        new_socks = _bind()
        if new_socks:
            logging_util.info("dns_rebind_success", port=new_socks[0].getsockname()[1], sockets=len(new_socks))
            _serve_dns(new_socks)
            return
        backoff = min(backoff * 2, 60.0)  # cap at 60 seconds
    logging_util.error(
        "dns_rebind_exhausted",
        attempts=_MAX_REBIND_ATTEMPTS,
        detail="could not rebind after %d attempts; collector stays down" % _MAX_REBIND_ATTEMPTS,
    )
    # State remains "down" as set by the caller.

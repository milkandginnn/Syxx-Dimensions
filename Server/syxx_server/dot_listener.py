"""DNS-over-TLS listener on :853 (encrypted-transports D-2).

Scope-limited by design: authoritative-leak-only (the same answers the UDP collector serves via
dns_authoritative._build_response — REFUSED for everything else), and a real recursive forward ONLY
for source IPs in DOT_FORWARD_ALLOWLIST. An open DoT forwarder on a public host is an abuse magnet;
this keeps Syxx's own infra able to use it without becoming one.

Honesty (§3 contract):
  * TLS is TERMINATED HERE. The app holds the TLS session, so any "answered over an encrypted
    channel" claim is honest — unlike an nginx `stream{}` front, where the app would receive
    plaintext and must never attest encryption (SD-59 class).
  * Fail-closed TLS: TLS 1.2 minimum, no custom verify callback. A cert/bind failure sets the
    listener `down` in the shared registry and RETURNS — it never falls back to plaintext Do53.
  * A hard connection cap (SD-51 retirement for this path): DoT keep-alive makes unbounded
    thread-per-connection worse than plain TCP, so the cap is a prerequisite, not a nicety.
"""

import socketserver
import ssl
import struct
import threading
import time

from . import config, dns_authoritative, leak, listeners, logging_util, resolver


class _BoundedThreadingTCPServer(socketserver.ThreadingTCPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, *a, ssl_context=None, **k):
        super().__init__(*a, **k)
        # Non-blocking cap: an over-limit connection is closed immediately rather than queued into
        # an unbounded thread pile (the SD-51 failure). BoundedSemaphore raises if released too many
        # times, catching a leak in the acquire/release pairing.
        self._slots = threading.BoundedSemaphore(config.DOT_MAX_CONN)
        # A119: store the SSL context on the server so the handler can perform the
        # TLS handshake AFTER the slot is acquired, rather than blocking the
        # accept loop on a wrapped listening socket.
        self._ssl_context = ssl_context

    def get_request(self):
        # A119: accept the RAW TCP socket (no TLS handshake here). The listening
        # socket is NOT wrapped with SSL, so accept() returns immediately on a
        # TCP connection without waiting for TLS bytes. The handshake is done
        # in the handler thread after the slot is acquired.
        sock, addr = self.socket.accept()
        if not self._slots.acquire(blocking=False):
            logging_util.incr("dot_conn_rejected")
            try:
                sock.close()
            finally:
                # Raise so the base server does not proceed to process_request on a closed socket.
                raise OSError("dot connection cap reached")
        return sock, addr

    def shutdown_request(self, request):
        try:
            super().shutdown_request(request)
        finally:
            # Always return the slot, even if the handler raised, or the cap leaks to zero.
            try:
                self._slots.release()
            except ValueError:
                pass


class _Handler(socketserver.BaseRequestHandler):
    # A119: maximum time (seconds) allowed for the TLS handshake to complete.
    # A client that connects but sends no TLS ClientHello blocks this handler
    # thread; the timeout prevents it from blocking indefinitely.
    _HANDSHAKE_TIMEOUT = 10

    def handle(self):
        # A119: perform the TLS handshake HERE, after the connection slot has
        # been acquired. The listening socket is plain TCP, so the accept loop
        # is never blocked by a slow/idle TLS client. Apply an absolute timeout
        # so a client that sends no TLS bytes cannot pin this thread.
        if self.server._ssl_context is not None:  # type: ignore[unresolved-attribute]
            self.request.settimeout(self._HANDSHAKE_TIMEOUT)
            try:
                self.request = self.server._ssl_context.wrap_socket(self.request, server_side=True)  # type: ignore[unresolved-attribute]
            except (TimeoutError, ssl.SSLError, OSError) as e:
                logging_util.incr("dot_handshake_failed")
                logging_util.warn("dot_handshake_error", err=str(e), peer=self.client_address[0])
                return  # close the connection; the slot is released by shutdown_request
        self.request.settimeout(config.DOT_READ_TIMEOUT)
        served = 0
        # DoT allows multiple queries per connection (keep-alive). Cap them so one connection cannot
        # monopolise a thread indefinitely.
        while served < config.DOT_MAX_QUERIES_PER_CONN:
            msg = self._read_message()
            if msg is None:
                return
            served += 1
            try:
                reply = self._answer(msg)
            except Exception as e:  # never let one bad query kill the conn
                logging_util.warn("dot_answer_error", err=str(e))
                return
            if reply is None:
                return
            try:
                self.request.sendall(struct.pack(">H", len(reply)) + reply)
            except OSError:
                return

    def _read_message(self):
        """Read one length-prefixed DNS message, or None on EOF/timeout/oversize."""
        try:
            prefix = self._recv_exact(2)
            if prefix is None:
                return None
            (length,) = struct.unpack(">H", prefix)
            if length == 0 or length > 4096:  # a DNS message over TCP is bounded
                return None
            return self._recv_exact(length)
        except (TimeoutError, OSError):
            return None

    def _recv_exact(self, n):
        buf = b""
        while len(buf) < n:
            chunk = self.request.recv(n - len(buf))
            if not chunk:
                return None
            buf += chunk
        return buf

    def _answer(self, msg):
        """Authoritative-leak-only, unless the source IP is allowlisted for forwarding."""
        client_ip = self.client_address[0]
        if client_ip in config.DOT_FORWARD_ALLOWLIST:
            # Trusted infra: forward recursively, passthrough (validated) like the DoH route.
            for server in config.PUBLIC_RESOLVERS:
                try:
                    return resolver.forward_wire(msg, server, config.DOH_UPSTREAM_TIMEOUT)
                except resolver.DNSError:
                    continue
            return None
        # Everyone else: the authoritative collector answer (REFUSED for out-of-zone), recording a
        # leak hit exactly as the UDP path does so DoT queries are collected too.
        response, leak_hit = dns_authoritative._build_response(msg)
        if leak_hit is not None:
            leak.observe(leak_hit[0], client_ip, leak_hit[1])
        return response


def run(stop_event=None):
    """Start the DoT listener. On any setup failure, mark the listener `down` and return — the HTTP
    server and UDP collector keep running; DoT is additive, never a reason to take the box down."""
    if not (config.DOT_TLS_CERT and config.DOT_TLS_KEY):
        listeners.set_state("dot", "down", detail="SYXX_DOT_TLS_CERT/KEY not set")
        logging_util.warn("dot_disabled", reason="no TLS cert configured")
        return
    try:
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.minimum_version = ssl.TLSVersion.TLSv1_2
        ctx.load_cert_chain(config.DOT_TLS_CERT, config.DOT_TLS_KEY)
    except (ssl.SSLError, OSError) as e:
        listeners.set_state("dot", "down", detail=f"cert load failed: {e}")
        logging_util.error("dot_cert_failed", err=str(e))
        return

    # A073: restart loop — mirrors dns_authoritative._rebind_with_backoff so an
    # unexpected serve_forever crash or transient bind failure does not silently
    # kill the DoT daemon.  Bounded by _MAX_RESTART_ATTEMPTS with exponential
    # backoff (1 s → 60 s cap).  Both bind failures and serve crashes share the
    # same retry budget; once exhausted the listener stays "down".
    _MAX_RESTART_ATTEMPTS = 10
    restart_count = 0
    backoff = 1.0

    while True:
        try:
            # A119: pass the SSL context to the server; do NOT wrap the listening
            # socket here. Wrapping it would cause accept() to block on the TLS
            # handshake before the connection cap (_slots) can reject over-limit
            # clients. The handler performs the handshake in its own thread after
            # acquiring a slot.
            server = _BoundedThreadingTCPServer(
                (config.DOT_HOST, config.DOT_PORT), _Handler, ssl_context=ctx
            )
        except OSError as e:
            restart_count += 1
            if restart_count > _MAX_RESTART_ATTEMPTS:
                listeners.set_state(
                    "dot", "down",
                    detail=f"rebind exhausted after {restart_count - 1} attempts",
                )
                logging_util.error(
                    "dot_rebind_exhausted",
                    attempts=restart_count - 1,
                    err=str(e),
                )
                return
            logging_util.warn(
                "dot_rebind_failed",
                err=str(e),
                port=config.DOT_PORT,
                attempt=restart_count,
                backoff_s=backoff,
            )
            time.sleep(backoff)
            backoff = min(backoff * 2, 60.0)
            continue

        listeners.set_state("dot", "listening", detail=f"TLS on :{config.DOT_PORT}")
        logging_util.info("dot_listening", port=config.DOT_PORT)

        try:
            server.serve_forever()
        except Exception as e:
            # Unexpected crash — log, clean up, and restart with backoff.
            restart_count += 1
            logging_util.error(
                "dot_serve_crashed",
                err=str(e),
                restart_count=restart_count,
            )
            try:
                server.server_close()
            except OSError:
                pass
            if restart_count > _MAX_RESTART_ATTEMPTS:
                listeners.set_state(
                    "dot", "down",
                    detail=f"restart exhausted after {restart_count - 1} attempts",
                )
                logging_util.error("dot_restart_exhausted", attempts=restart_count - 1)
                return
            time.sleep(backoff)
            backoff = min(backoff * 2, 60.0)
            continue

        # serve_forever() returned cleanly (shutdown() was called elsewhere) —
        # exit normally without restarting.
        listeners.set_state("dot", "down", detail="serve loop exited")
        server.server_close()
        return

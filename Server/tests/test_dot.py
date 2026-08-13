"""DoT listener (D-2) — a real TLS DNS-over-TCP round trip against a live listener.

Drives an actual _BoundedThreadingTCPServer wrapped in TLS with a self-signed cert, then speaks the
DoT wire protocol (2-byte length prefix + DNS message) over a client TLS socket. Asserts the honesty
scope: authoritative-leak-only answers, REFUSED for out-of-zone, and that the connection cap holds
(SD-51) rather than piling unbounded threads.
"""
import os
import socket
import ssl
import struct
import tempfile
import threading
import time
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import config, dot_listener, resolver  # noqa: E402


def _self_signed():
    """A throwaway cert/key for the test listener. Uses the cryptography lib already required."""
    from cryptography import x509
    from cryptography.x509.oid import NameOID
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    import datetime

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "localhost")])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (x509.CertificateBuilder()
            .subject_name(name).issuer_name(name).public_key(key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - datetime.timedelta(minutes=5))
            .not_valid_after(now + datetime.timedelta(days=1))
            .sign(key, hashes.SHA256()))
    cf = tempfile.NamedTemporaryFile(suffix=".pem", delete=False)
    kf = tempfile.NamedTemporaryFile(suffix=".pem", delete=False)
    cf.write(cert.public_bytes(serialization.Encoding.PEM)); cf.close()
    kf.write(key.private_bytes(serialization.Encoding.PEM,
                               serialization.PrivateFormat.TraditionalOpenSSL,
                               serialization.NoEncryption())); kf.close()
    return cf.name, kf.name


class DotListenerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cert, cls.key = _self_signed()
        config.DOT_TLS_CERT, config.DOT_TLS_KEY = cls.cert, cls.key
        config.DOT_HOST = "127.0.0.1"
        config.DOT_PORT = 0                    # ephemeral; we read the bound port back
        config.DOT_MAX_CONN = 4
        config.DOT_READ_TIMEOUT = 1
        # Build + wrap the server exactly as run() does, but keep the handle so we can read the port.
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(cls.cert, cls.key)
        cls.server = dot_listener._BoundedThreadingTCPServer(("127.0.0.1", 0), dot_listener._Handler)
        cls.port = cls.server.socket.getsockname()[1]
        cls.server.socket = ctx.wrap_socket(cls.server.socket, server_side=True)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        for p in (cls.cert, cls.key):
            try: os.unlink(p)
            except OSError: pass

    def _dot_query(self, name, qtype=1):
        """One DoT exchange: TLS connect, send length-prefixed query, read the answer."""
        raw = socket.create_connection(("127.0.0.1", self.port), timeout=5)
        cctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        cctx.check_hostname = False
        cctx.verify_mode = ssl.CERT_NONE
        tls = cctx.wrap_socket(raw, server_hostname="localhost")
        try:
            _txid, packet, _q, _qt, _qclass = resolver._build_query(name, qtype)
            tls.sendall(struct.pack(">H", len(packet)) + packet)
            prefix = tls.recv(2)
            (length,) = struct.unpack(">H", prefix)
            body = b""
            while len(body) < length:
                body += tls.recv(length - len(body))
            return body
        finally:
            tls.close()

    def test_an_in_zone_leak_name_is_answered_over_tls(self):
        # A name under the leak zone gets the authoritative collector answer — proving the TLS
        # round trip works and the answer is the leak-sink, not a refusal.
        resp = self._dot_query("probe123.leak.syxx.app", 1)
        self.assertGreaterEqual(len(resp), 12)
        rcode = resp[3] & 0x0F
        self.assertEqual(rcode, 0, "in-zone query should be answered NOERROR, not refused")

    def test_an_out_of_zone_name_is_refused_not_answered(self):
        # The honesty scope: this is not an open resolver. example.com is out of zone → REFUSED (5).
        resp = self._dot_query("example.com", 1)
        rcode = resp[3] & 0x0F
        self.assertEqual(rcode, 5, "out-of-zone query must be REFUSED, never a fabricated answer")

    def test_a_connection_flood_does_not_take_the_listener_down(self):
        # SD-51: opening far more connections than the cap must NOT pile unbounded threads or crash
        # the listener. Open a flood, close it, let the capped slots drain (idle handlers hit the
        # 1s read timeout and release), then prove the listener still answers.
        idle = []
        for _ in range(config.DOT_MAX_CONN + 20):
            try:
                idle.append(socket.create_connection(("127.0.0.1", self.port), timeout=2))
            except OSError:
                pass  # excess connections refused at the cap — expected
        for s in idle:
            try: s.close()
            except OSError: pass
        time.sleep(1.4)  # > DOT_READ_TIMEOUT, so any slot held by an idle handler is released
        resp = self._dot_query("probe456.leak.syxx.app", 1)
        self.assertEqual(resp[3] & 0x0F, 0, "listener must survive a connection flood")

    def test_connection_cap_is_enforced_not_just_survivable(self):
        """A209: SD-51 cap must actively REFUSE excess connections, not merely
        survive them. The original test only proved the listener was still alive
        after a flood — it never asserted the cap was enforced. A broken cap
        (e.g. semaphore removed, cap set to sys.maxsize) would still pass that
        test while accepting unbounded connections."""
        max_conn = config.DOT_MAX_CONN
        held = []
        refused_count = 0
        try:
            # Open connections up to the cap — all should succeed
            for i in range(max_conn):
                try:
                    s = socket.create_connection(("127.0.0.1", self.port), timeout=2)
                    held.append(s)
                except OSError:
                    pass  # platform may refuse even under cap — that's acceptable
            # At least some connections should have been accepted
            self.assertGreater(len(held), 0,
                               "at least one connection should be accepted up to the cap")
            # Now try to open more — the cap must refuse at least some
            for _ in range(max_conn):
                try:
                    s = socket.create_connection(("127.0.0.1", self.port), timeout=2)
                    held.append(s)  # if accepted, cap may not be strict on this platform
                except OSError:
                    refused_count += 1
            # The cap must have refused at least one excess connection.
            # On some platforms, accept() may queue extras, but the BoundedSemaphore
            # in _BoundedThreadingTCPServer.get_request should reject them.
            self.assertGreater(refused_count, 0,
                               f"SD-51 cap ({max_conn}) must refuse excess connections; "
                               f"all {max_conn * 2} attempts succeeded — cap is not enforced")
        finally:
            for s in held:
                try:
                    s.close()
                except OSError:
                    pass


if __name__ == "__main__":
    unittest.main()

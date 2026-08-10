"""Tests for syxx_server.dot_listener — DNS-over-TLS listener.

Covers the bounded connection server, handler message parsing,
TLS handshake timeout, and the run() setup/teardown logic.
Network and TLS operations are mocked.
"""

import os
import struct
from unittest.mock import MagicMock, patch, PropertyMock

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import dot_listener, config


# --- _BoundedThreadingTCPServer ---------------------------------------------


class TestBoundedServer:
    def test_daemon_threads(self):
        assert dot_listener._BoundedThreadingTCPServer.daemon_threads is True

    def test_allow_reuse_address(self):
        assert dot_listener._BoundedThreadingTCPServer.allow_reuse_address is True


# --- _Handler message reading -----------------------------------------------


class TestHandlerMessageReading:
    def _make_handler(self):
        """Create a handler instance with a mock request."""
        handler = dot_listener._Handler.__new__(dot_listener._Handler)
        handler.request = MagicMock()
        handler.client_address = ("127.0.0.1", 12345)
        handler.server = MagicMock()
        handler.server._ssl_context = None
        return handler

    def test_read_message_valid(self):
        handler = self._make_handler()
        # DNS message: 4 bytes of data prefixed with 2-byte length
        dns_data = b"\x00\x04test"
        handler.request.recv = MagicMock(side_effect=[dns_data[:2], dns_data[2:]])
        result = handler._read_message()
        assert result == b"test"

    def test_read_message_zero_length(self):
        handler = self._make_handler()
        handler.request.recv = MagicMock(return_value=b"\x00\x00")
        result = handler._read_message()
        assert result is None

    def test_read_message_oversize(self):
        handler = self._make_handler()
        # Length > 4096
        handler.request.recv = MagicMock(return_value=b"\xff\xff")
        result = handler._read_message()
        assert result is None

    def test_read_message_eof(self):
        handler = self._make_handler()
        handler.request.recv = MagicMock(return_value=b"")
        result = handler._read_message()
        assert result is None

    def test_read_message_timeout(self):
        handler = self._make_handler()
        handler.request.recv = MagicMock(side_effect=TimeoutError())
        result = handler._read_message()
        assert result is None

    def test_recv_exact(self):
        handler = self._make_handler()
        handler.request.recv = MagicMock(side_effect=[b"ab", b"cd"])
        result = handler._recv_exact(4)
        assert result == b"abcd"

    def test_recv_exact_eof(self):
        handler = self._make_handler()
        handler.request.recv = MagicMock(side_effect=[b"ab", b""])
        result = handler._recv_exact(4)
        assert result is None


# --- _Handler answer routing -------------------------------------------------


class TestHandlerAnswer:
    def _make_handler(self):
        handler = dot_listener._Handler.__new__(dot_listener._Handler)
        handler.request = MagicMock()
        handler.client_address = ("127.0.0.1", 12345)
        handler.server = MagicMock()
        return handler

    def test_allowlisted_ip_forwards(self):
        handler = self._make_handler()
        handler.client_address = ("10.0.0.1", 12345)
        with patch.object(dot_listener.config, "DOT_FORWARD_ALLOWLIST", ["10.0.0.1"]):
            with patch.object(dot_listener.config, "PUBLIC_RESOLVERS", ["8.8.8.8"]):
                with patch.object(dot_listener.config, "DOH_UPSTREAM_TIMEOUT", 5):
                    with patch.object(dot_listener.resolver, "forward_wire", return_value=b"\x00\x04test"):
                        result = handler._answer(b"\x00\x04test")
                        assert result == b"\x00\x04test"

    def test_non_allowlisted_uses_authoritative(self):
        handler = self._make_handler()
        handler.client_address = ("192.168.1.1", 12345)
        with patch.object(dot_listener.config, "DOT_FORWARD_ALLOWLIST", []):
            with patch.object(dot_listener.dns_authoritative, "_build_response", return_value=(b"reply", None)):
                result = handler._answer(b"query")
                assert result == b"reply"

    def test_leak_hit_observed(self):
        handler = self._make_handler()
        handler.client_address = ("192.168.1.1", 12345)
        with patch.object(dot_listener.config, "DOT_FORWARD_ALLOWLIST", []):
            with patch.object(
                dot_listener.dns_authoritative,
                "_build_response",
                return_value=(b"reply", ("token123", "subdomain")),
            ):
                with patch.object(dot_listener.leak, "observe") as mock_observe:
                    handler._answer(b"query")
                    mock_observe.assert_called_once_with("token123", "192.168.1.1", "subdomain")


# --- run() setup ------------------------------------------------------------


class TestRun:
    def test_no_cert_disables_dot(self):
        orig_cert = config.DOT_TLS_CERT
        orig_key = config.DOT_TLS_KEY
        try:
            config.DOT_TLS_CERT = ""
            config.DOT_TLS_KEY = ""
            with patch.object(dot_listener.listeners, "set_state") as mock_state:
                dot_listener.run()
                mock_state.assert_called_with("dot", "down", detail="SYXX_DOT_TLS_CERT/KEY not set")
        finally:
            config.DOT_TLS_CERT = orig_cert
            config.DOT_TLS_KEY = orig_key

    @patch("ssl.SSLContext")
    def test_cert_load_failure_disables_dot(self, mock_ssl_ctx):
        import ssl as _ssl
        mock_ssl_ctx.return_value.load_cert_chain.side_effect = _ssl.SSLError("bad cert")
        orig_cert = config.DOT_TLS_CERT
        orig_key = config.DOT_TLS_KEY
        try:
            config.DOT_TLS_CERT = "/fake/cert.pem"
            config.DOT_TLS_KEY = "/fake/key.pem"
            with patch.object(dot_listener.listeners, "set_state") as mock_state:
                dot_listener.run()
                calls = mock_state.call_args_list
                assert any("down" in str(c) for c in calls)
        finally:
            config.DOT_TLS_CERT = orig_cert
            config.DOT_TLS_KEY = orig_key


# --- Handshake timeout -------------------------------------------------------


class TestHandshakeTimeout:
    def test_timeout_constant(self):
        assert dot_listener._Handler._HANDSHAKE_TIMEOUT > 0
        assert dot_listener._Handler._HANDSHAKE_TIMEOUT <= 30

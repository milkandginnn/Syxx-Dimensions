"""Tests for syxx_server.mailer — outbound email with SMTP connection reuse.

Covers enabled/disabled state, send() behavior with mocked SMTP,
size limit enforcement, and the never-raises contract.
"""

import os
from unittest.mock import MagicMock, patch, PropertyMock

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import mailer, config


@pytest.fixture(autouse=True)
def _reset_smtp_state():
    """Reset module-level SMTP connection state between tests."""
    mailer._smtp_conn = None
    mailer._smtp_conn_created_at = 0.0
    yield
    mailer._smtp_conn = None
    mailer._smtp_conn_created_at = 0.0


class TestEnabled:
    def test_disabled_when_no_host(self):
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = ""
            assert mailer.enabled() is False
        finally:
            config.SMTP_HOST = orig

    def test_enabled_when_host_set(self):
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            assert mailer.enabled() is True
        finally:
            config.SMTP_HOST = orig


class TestSend:
    def test_returns_false_when_disabled(self):
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = ""
            assert mailer.send("test@example.com", "Subject", "Body") is False
        finally:
            config.SMTP_HOST = orig

    def test_returns_false_for_empty_to(self):
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            assert mailer.send("", "Subject", "Body") is False
        finally:
            config.SMTP_HOST = orig

    @patch.object(mailer, "_get_smtp_connection")
    def test_send_success(self, mock_conn_getter):
        mock_conn = MagicMock()
        mock_conn_getter.return_value = mock_conn
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            result = mailer.send("user@example.com", "Test", "Hello")
            assert result is True
            mock_conn.send_message.assert_called_once()
        finally:
            config.SMTP_HOST = orig

    @patch.object(mailer, "_get_smtp_connection")
    def test_send_with_html(self, mock_conn_getter):
        mock_conn = MagicMock()
        mock_conn_getter.return_value = mock_conn
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            result = mailer.send("user@example.com", "Test", "Hello", html="<b>Hello</b>")
            assert result is True
        finally:
            config.SMTP_HOST = orig

    @patch.object(mailer, "_get_smtp_connection")
    def test_send_with_attachments(self, mock_conn_getter):
        mock_conn = MagicMock()
        mock_conn_getter.return_value = mock_conn
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            result = mailer.send(
                "user@example.com", "Test", "Hello",
                attachments=[("report.csv", "text/csv", b"a,b,c")],
            )
            assert result is True
        finally:
            config.SMTP_HOST = orig

    @patch.object(mailer, "_get_smtp_connection", return_value=None)
    def test_send_connection_failure(self, mock_conn_getter):
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            result = mailer.send("user@example.com", "Test", "Hello")
            assert result is False
        finally:
            config.SMTP_HOST = orig

    @patch.object(mailer, "_get_smtp_connection")
    def test_send_smtp_error_returns_false(self, mock_conn_getter):
        mock_conn = MagicMock()
        mock_conn.send_message.side_effect = Exception("SMTP error")
        mock_conn_getter.return_value = mock_conn
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            result = mailer.send("user@example.com", "Test", "Hello")
            assert result is False
        finally:
            config.SMTP_HOST = orig

    def test_never_raises(self):
        """The send() contract: never raises, always returns bool."""
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            # Even with a broken SMTP setup, send() must not raise
            result = mailer.send("user@example.com", "Test", "Hello")
            assert isinstance(result, bool)
        finally:
            config.SMTP_HOST = orig


class TestSizeLimit:
    @patch.object(mailer, "_get_smtp_connection")
    def test_oversized_email_rejected(self, mock_conn_getter):
        """A314: emails over _MAX_EMAIL_BYTES are rejected."""
        orig = config.SMTP_HOST
        try:
            config.SMTP_HOST = "smtp.example.com"
            huge_body = "x" * (mailer._MAX_EMAIL_BYTES + 1)
            result = mailer.send("user@example.com", "Test", huge_body)
            assert result is False
            mock_conn_getter.assert_not_called()
        finally:
            config.SMTP_HOST = orig


class TestConnectionManagement:
    def test_close_connection_noop_when_none(self):
        """Closing a non-existent connection is a safe no-op."""
        mailer._close_smtp_connection()
        assert mailer._smtp_conn is None

    def test_close_connection_calls_quit(self):
        mock_conn = MagicMock()
        mailer._smtp_conn = mock_conn
        mailer._smtp_conn_created_at = 1000.0
        mailer._close_smtp_connection()
        mock_conn.quit.assert_called_once()
        assert mailer._smtp_conn is None

    def test_close_connection_handles_quit_failure(self):
        mock_conn = MagicMock()
        mock_conn.quit.side_effect = Exception("already dead")
        mailer._smtp_conn = mock_conn
        mailer._smtp_conn_created_at = 1000.0
        mailer._close_smtp_connection()
        assert mailer._smtp_conn is None

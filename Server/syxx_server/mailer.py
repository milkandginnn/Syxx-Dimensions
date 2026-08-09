"""Outbound email — optional and best-effort.

If SMTP is not configured (``config.SMTP_HOST`` empty), every send is a logged
no-op. Mail is never required for the server to run, and a send failure never
raises into a request handler — callers get ``False`` and move on. Credentials
are read from config (environment) only; nothing is hardcoded.

Two consumers:
  * confirmation emails to a beta subscriber (fire-and-forget from the handler);
  * the operator's weekly CSV digest (from the background scheduler).
"""

import os
import smtplib
import ssl
import threading
import time
from email.message import EmailMessage

from . import config, logging_util

# A315: SMTP connection reuse. Maintain a module-level connection that is
# reused across sends to avoid the TCP+TLS+AUTH overhead on every email.
# The connection is lazily created and closed after _SMTP_IDLE_TIMEOUT seconds
# of inactivity or on any error (which triggers a fresh connect on next send).
_SMTP_IDLE_TIMEOUT = 120  # seconds before closing an idle connection
_smtp_conn = None  # type: smtplib.SMTP | smtplib.SMTP_SSL | None
_smtp_conn_lock = threading.Lock()
_smtp_conn_created_at = 0.0

# A314: maximum email size (body + attachments combined). Most SMTP servers
# reject messages over 10-25 MB. A monitor with hundreds of changes can
# generate an enormous email that SMTP rejects silently — the send returns
# False with no explanation. Cap at 10 MB to fail fast with a clear log.
_MAX_EMAIL_BYTES = 10 * 1024 * 1024  # 10 MiB


def enabled():
    """True when an SMTP host is configured. Used to avoid promising a confirmation
    email the server has no way to send."""
    return bool(config.SMTP_HOST)


def send(to, subject, body, html=None, attachments=None):
    """Send an email, optionally as multipart text+HTML, optionally with attachments.

    ``body`` is the plain-text part (always sent, and the fallback for clients that
    don't render HTML). ``html``, when given, is added as a rich alternative — pass a
    designed template here. ``attachments`` is a list of ``(filename, mime_type, bytes)``.
    Returns True on success, False on any failure (or when SMTP is unconfigured).
    Never raises — mail must not be able to break the caller.
    """
    if not enabled():
        logging_util.info("mail_skipped", reason="smtp_unconfigured", subject=subject)
        return False
    if not to:
        return False

    # A314: enforce email size limit before constructing the message.
    body_size = len(body.encode("utf-8", errors="replace")) if isinstance(body, str) else len(body)
    if html:
        body_size += len(html.encode("utf-8", errors="replace")) if isinstance(html, str) else len(html)
    for _fn, _mt, data in attachments or []:
        body_size += len(data) if data else 0
    if body_size > _MAX_EMAIL_BYTES:
        logging_util.error(
            "mail_rejected_oversized",
            subject=subject,
            size_bytes=body_size,
            max_bytes=_MAX_EMAIL_BYTES,
        )
        return False

    try:
        # A106: header assignment can raise ValueError for malformed addresses
        # (e.g. unvalidated Stripe metadata). Keep construction inside try so
        # the "never raises" contract holds.
        msg = EmailMessage()
        msg["From"] = config.MAIL_FROM
        msg["To"] = to
        msg["Subject"] = subject
        msg.set_content(body)  # text/plain fallback (always present)
        if html:
            msg.add_alternative(html, subtype="html")  # → multipart/alternative
        for filename, mime_type, data in attachments or []:
            maintype, _, subtype = mime_type.partition("/")
            msg.add_attachment(data, maintype=maintype, subtype=subtype or "octet-stream", filename=filename)
    except ValueError:
        # A188: log the exception type, not the raw ValueError message which may
        # contain the malformed address (PII).
        logging_util.error("mail_failed", subject=subject, err_type="ValueError", smtp_detail="invalid header")
        return False

    try:
        # A315: use connection reuse — get or create a persistent SMTP
        # connection instead of opening a new TCP+TLS+AUTH session per send.
        s = _get_smtp_connection()
        if s is None:
            return False  # connection failed, already logged
        s.send_message(msg)
        logging_util.incr("mail_sent")
        return True
    except Exception as e:  # any SMTP/DNS/TLS error — log and swallow
        # A315: on any error, close the pooled connection so the next send
        # gets a fresh one.
        _close_smtp_connection()
        # A188 Fix 1: log the exception type and SMTP code, NOT the raw string.
        # Several smtplib exceptions (SMTPRecipientsRefused, SMTPSenderRefused)
        # embed the recipient address in str(e), which re-introduces the PII
        # the call site deliberately withheld.
        err_type = type(e).__name__
        smtp_code = getattr(e, "smtp_code", None)
        smtp_err = getattr(e, "smtp_error", None)
        # Redact any address-like content from smtp_error before logging.
        if smtp_err:
            import re

            smtp_err_str = re.sub(r"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", "<redacted>", str(smtp_err))
        else:
            smtp_err_str = None
        logging_util.error(
            "mail_failed", subject=subject, err_type=err_type, smtp_code=smtp_code, smtp_detail=smtp_err_str
        )
        return False


def _get_smtp_connection():
    """A315: get or create a reusable SMTP connection.

    Returns an authenticated smtplib.SMTP or SMTP_SSL instance, or None on
    failure (logged). The connection is reused across sends and closed
    after _SMTP_IDLE_TIMEOUT seconds or on error.
    """
    global _smtp_conn, _smtp_conn_created_at

    with _smtp_conn_lock:
        # Check if we have a valid, non-expired connection.
        if _smtp_conn is not None:
            age = time.monotonic() - _smtp_conn_created_at
            if age < _SMTP_IDLE_TIMEOUT:
                try:
                    # NOOP to verify the connection is still alive.
                    _smtp_conn.noop()
                    return _smtp_conn
                except Exception:
                    # Connection is dead — close and recreate.
                    _close_smtp_connection_unlocked()
            else:
                _close_smtp_connection_unlocked()

        # Create a new connection.
        ctx = ssl.create_default_context()
        if config.SMTP_INSECURE_CERTS:
            if os.environ.get("SYXX_DEV_MODE") != "1":
                logging_util.error(
                    "mail_blocked_insecure_certs",
                    msg="SMTP_INSECURE_CERTS is set but SYXX_DEV_MODE!=1; "
                    "refusing to send mail without certificate validation.",
                )
                return None
            ctx.check_hostname = False
            ctx.verify_mode = ssl.CERT_NONE
        try:
            if config.SMTP_STARTTLS:
                s = smtplib.SMTP(config.SMTP_HOST, config.SMTP_PORT, timeout=config.SMTP_TIMEOUT)
                s.starttls(context=ctx)
            else:
                s = smtplib.SMTP_SSL(config.SMTP_HOST, config.SMTP_PORT, timeout=config.SMTP_TIMEOUT, context=ctx)
            if config.SMTP_USER:
                s.login(config.SMTP_USER, config.SMTP_PASSWORD)
            _smtp_conn = s
            _smtp_conn_created_at = time.monotonic()
            return _smtp_conn
        except Exception as e:
            logging_util.error(
                "mail_connection_failed",
                err_type=type(e).__name__,
                smtp_code=getattr(e, "smtp_code", None),
            )
            _smtp_conn = None
            return None


def _close_smtp_connection_unlocked():
    """Close the pooled SMTP connection (caller must hold _smtp_conn_lock)."""
    global _smtp_conn, _smtp_conn_created_at
    if _smtp_conn is not None:
        try:
            _smtp_conn.quit()
        except Exception:
            try:
                _smtp_conn.close()
            except Exception:  # noqa: S110 — best-effort close; connection is already dead
                pass
        _smtp_conn = None
        _smtp_conn_created_at = 0.0


def _close_smtp_connection():
    """Thread-safe wrapper to close the pooled SMTP connection."""
    with _smtp_conn_lock:
        _close_smtp_connection_unlocked()

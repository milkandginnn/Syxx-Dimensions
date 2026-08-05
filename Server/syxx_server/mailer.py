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
from email.message import EmailMessage

from . import config, logging_util


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
        ctx = ssl.create_default_context()
        if config.SMTP_INSECURE_CERTS:
            # A107: refuse to disable certificate validation in production.
            # An operator who needs this for a self-signed relay must set
            # SYXX_DEV_MODE=1 explicitly; silent MITM on credential-bearing
            # SMTP is not an acceptable production configuration.
            if os.environ.get("SYXX_DEV_MODE") != "1":
                logging_util.error(
                    "mail_blocked_insecure_certs",
                    subject=subject,
                    msg="SMTP_INSECURE_CERTS is set but SYXX_DEV_MODE!=1; "
                    "refusing to send mail without certificate validation. "
                    "Remove SMTP_INSECURE_CERTS or fix the relay's TLS.",
                )
                return False
            ctx.check_hostname = False
            ctx.verify_mode = ssl.CERT_NONE
        if config.SMTP_STARTTLS:
            with smtplib.SMTP(config.SMTP_HOST, config.SMTP_PORT, timeout=config.SMTP_TIMEOUT) as s:
                s.starttls(context=ctx)
                if config.SMTP_USER:
                    s.login(config.SMTP_USER, config.SMTP_PASSWORD)
                s.send_message(msg)
        else:
            with smtplib.SMTP_SSL(config.SMTP_HOST, config.SMTP_PORT, timeout=config.SMTP_TIMEOUT, context=ctx) as s:
                if config.SMTP_USER:
                    s.login(config.SMTP_USER, config.SMTP_PASSWORD)
                s.send_message(msg)
        logging_util.incr("mail_sent")
        return True
    except Exception as e:  # any SMTP/DNS/TLS error — log and swallow
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

"""Commerce preflight — say at STARTUP what will not work, instead of at the customer.

Why this exists
---------------
Licensing fails CLOSED (D040/A021): with no `SYXX_KEYGEN_PUBLIC_KEY` configured, every live key
validation is denied. That is the CORRECT behaviour — without the public key a forged Keygen
response cannot be told from a genuine one — but the failure is invisible from the server's side.
It surfaces as a paying customer being told their real key is invalid, and nothing in the log says
why. Fixing fail-closed made this MORE certain to bite, not less: the guard is right, the
deployment is unprovisioned.

The same shape applies to commerce: an unset SMTP host means a paid buyer's key email silently
no-ops (A047), and a missing Stripe secret means checkout raises on the first click.

So: report it once, loudly, at boot, where an operator can see it — never guess, never self-heal,
and never block startup. The DNS/analysis half of the product works perfectly well without any
commerce config, so a missing Stripe key must not take the daemon down.

This module reports; it does not decide. It reads config and returns findings.
"""

import os

from . import config, leak, mailer


# (env var, what breaks when it is blank, severity)
#   "blocker" — a customer-visible failure is CERTAIN if this is blank.
#   "degraded" — something is silently reduced; sales still complete.
def _keygen_key_error():
    """Return None if the Keygen public key is loadable, else a reason string.

    A206: a truthiness check (bool(config.KEYGEN_PUBLIC_KEY)) passes any non-empty
    string, including a 4-hex-byte typo. Ed25519PublicKey.from_public_bytes requires
    exactly 32 bytes of raw key material. A wrong-length or non-hex value here means
    every live key validation 402s — the exact failure the preflight exists to predict.
    """
    raw = config.KEYGEN_PUBLIC_KEY
    if not raw:
        return "not set"
    try:
        key_bytes = bytes.fromhex(raw)
    except ValueError:
        return "not valid hex (expected 64 hex chars / 32 bytes)"
    if len(key_bytes) != 32:
        return f"32 bytes of hex expected, got {len(key_bytes)} bytes"
    try:
        from cryptography.hazmat.primitives.asymmetric.ed25519 import (
            Ed25519PublicKey,  # type: ignore[unresolved-import]
        )

        Ed25519PublicKey.from_public_bytes(key_bytes)
    except Exception as e:
        return f"key not loadable: {e}"
    return None


def _keygen_finding():
    """Blocker if the Keygen public key is missing OR present-but-unloadable. None if it works.

    A206/A218: surfaces the specific parse error (wrong length, non-hex, unloadable) in the
    consequence string so an operator learns the exact provisioning mistake, not just the variable
    name. Mirrors _collector_finding() which already does this for the collector key.
    """
    why = _keygen_key_error()
    if why is None:
        return None
    return (
        "blocker",
        "SYXX_KEYGEN_PUBLIC_KEY",
        f"licensing FAILS CLOSED — every live key validation is denied (402), including valid keys ({why})",
    )


_CHECKS = [
    (
        "SYXX_KEYGEN_ADMIN_TOKEN",
        lambda: bool(config.KEYGEN_ADMIN_TOKEN),
        "a paid webhook cannot mint a license — sales record as pending-mint and no key is issued",
        "blocker",
    ),
    (
        "STRIPE_SECRET_KEY",
        lambda: bool(config.STRIPE_SECRET_KEY),
        "checkout cannot create a session — the buy button fails on the first click",
        "blocker",
    ),
    (
        "STRIPE_WEBHOOK_SECRET",
        lambda: bool(config.STRIPE_WEBHOOK_SECRET),
        "webhook signatures cannot be verified — paid events cannot be trusted or fulfilled",
        "blocker",
    ),
    (
        "SYXX_SMTP_HOST",
        lambda: mailer.enabled(),
        "a minted key CANNOT be emailed — the buyer pays and receives nothing until an operator "
        "resends manually (the sale is recorded minted-undelivered, never silently 'fulfilled')",
        "blocker",
    ),
    # Added 2026-07-16 after PRODUCTION LOGS proved this is a live failure mode that the first
    # version of this preflight missed:
    #   {"event": "handler_exception", "route": "/api/pubkey",
    #    "err": "SYXX_COLLECTOR_PRIVATE_KEY is not set. The collector refuses to start without a
    #            stable Ed25519 signing key..."}
    # The daemon had been up for TWO DAYS in that state, 500ing /api/pubkey on every call, and
    # nothing said so at boot — exactly the invisibility this module exists to end. The A022 guard
    # is behaving correctly (refusing an ephemeral key that would make attestations forgeable after
    # a restart); the deployment is unprovisioned.
]


def _collector_finding():
    """Blocker if the collector key is missing OR present-but-unloadable. None if it works.

    Added 2026-07-16 after PRODUCTION LOGS proved this is a live failure mode that the first
    version of this preflight missed:
      {"event": "handler_exception", "route": "/api/pubkey",
       "err": "SYXX_COLLECTOR_PRIVATE_KEY is not set. ..."}
    The daemon had been up for TWO DAYS in that state, 500ing /api/pubkey on every call, and
    nothing said so at boot — exactly the invisibility this module exists to end.

    WHY THIS PARSES THE KEY INSTEAD OF CHECKING len(str) > 0:
    the first version asked "is the variable non-empty". It is not the same question as "does
    the collector work", and the gap is not hypothetical — provisioning this on the live box
    produced a 32-byte seed (plausible, non-empty, base64) that the loader rejected with
    OpenSSL "not enough data" on EVERY request. A truthiness check prints commerce_ready for
    that host. A preflight that reports clean on a broken key is worse than no preflight: it
    is a diagnostic that has stopped diagnosing while still being trusted.
    """
    if os.environ.get("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY") == "1":
        return None  # documented dev/test escape; A022 warns at use.
    why = leak.collector_key_error(os.environ.get("SYXX_COLLECTOR_PRIVATE_KEY"))
    if why is None:
        return None
    return (
        "blocker",
        "SYXX_COLLECTOR_PRIVATE_KEY",
        f"the leak collector CANNOT SIGN ({why}) — /api/pubkey 500s and every leak attestation "
        "is unverifiable. SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY=1 is dev/test ONLY (an "
        "ephemeral key is forgeable and dies at restart)",
    )


def commerce_findings():
    """Return [(severity, var, consequence)] for every unconfigured commerce artifact."""
    findings = []
    for var, ok, consequence, severity in _CHECKS:
        if not ok():
            findings.append((severity, var, consequence))

    # A206/A218: keygen key check is now a dedicated function (like _collector_finding)
    # so the consequence string includes the specific parse error.
    keygen = _keygen_finding()
    if keygen:
        findings.append(keygen)

    collector = _collector_finding()
    if collector:
        findings.append(collector)

    # A317: an empty STRIPE_SKUS catalog means nothing is sellable even if all
    # Stripe keys are configured. Without this check, an operator with valid
    # keys but no catalog entries gets "commerce_ready" — a false positive.
    if not config.STRIPE_SKUS:
        findings.append(
            (
                "degraded",
                "STRIPE_SKUS",
                "no SKUs configured — the catalog is empty, so no buy button can work "
                "even though Stripe keys may be present",
            )
        )

    # A policy id per SKU. Blank is NOT a blocker: that SKU's sale is recorded pending rather
    # than dropped, so a missing id delays a key — it never loses a payment.
    for env in sorted({e["policy_env"] for sku, e in config.STRIPE_SKUS.items() if not config.keygen_policy_for(sku)}):
        findings.append(
            (
                "degraded",
                env,
                "that SKU's sale is recorded PENDING (never dropped) — no key is minted until the policy id is set",
            )
        )

    # A SKU with no price id cannot be sold at all: checkout refuses it rather than falling back to
    # the product's default price (which would charge the wrong tier). Unlike a missing policy this
    # is not a delay — the buy button 503s — but it is not a blocker for the DEPLOY either, since a
    # catalog can legitimately be provisioned ahead of Stripe. Say it once, at boot.
    for sku in sorted(s for s in config.STRIPE_SKUS if not config.stripe_price_for(s)):
        findings.append(
            (
                "degraded",
                "price:" + sku,
                f"SKU {sku} has no Stripe price id — it is NOT sellable and its buy button "
                "returns 503 (checkout refuses rather than charging a default price)",
            )
        )
    return findings


def log_commerce_readiness(log):
    """Log a one-shot readiness summary. Never raises; never blocks startup.

    The analysis half of Syxx works with zero commerce configuration, so a missing Stripe key must
    not take the daemon down — but an operator must not have to discover it from a customer.
    """
    try:
        findings = commerce_findings()
    except Exception as exc:  # noqa: BLE001 — boundary handler: any exception here must become a reported result, not a crash
        log.warn("preflight_failed", detail=str(exc))
        return []

    if not findings:
        log.info("commerce_ready", detail="Keygen + Stripe + SMTP configured")
        return findings

    blockers = [f for f in findings if f[0] == "blocker"]
    for severity, var, consequence in findings:
        (log.warn if severity == "blocker" else log.info)(
            "commerce_unconfigured", severity=severity, var=var, consequence=consequence
        )
    if blockers:
        log.warn(
            "commerce_not_sellable",
            blockers=",".join(v for _, v, _ in blockers),
            detail="Selling is NOT functional until these are set in /etc/syxx/syxx.env. "
            "DNS/analysis is unaffected and is serving normally.",
        )
    return findings

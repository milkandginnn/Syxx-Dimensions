"""Tests for the leak collector's Ed25519 attestation path (/api/leak/verify).

Regression guard for two cold-start defects that shipped because `leak.verify()`
had NO test: (1) `.sign()` ran before the collector key was created, and (2) the
public-key global was misnamed so `collector_public_key()` raised NameError. Both
were deterministic — every cold verify() 500'd — yet invisible to unit tests that
never exercised the signing path. This test drives issue -> observe -> verify and
INDEPENDENTLY verifies the emitted signature, so the path can't silently break again.

Skipped when `cryptography` isn't installed (leak signing genuinely requires it).
Run from Server/:  python3 -m unittest tests.test_leak -v
"""

import base64
import os
import unittest

# A214: use the shared temp DB from conftest.py (loaded by pytest before any test module).
# The old pattern (creating a per-module temp DB and setting SYXX_DB_PATH after import)
# was dead code: conftest.py already set the env var and config.DB_PATH was already bound.
import conftest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
# Dev/test escape (A022): allow an ephemeral collector key so the attestation path
# runs under test without a provisioned SYXX_COLLECTOR_PRIVATE_KEY. Production fails
# closed without this flag.
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ["SYXX_LEAK_ZONES"] = "leak.syxx.app"

from syxx_server import config, db, leak  # noqa: E402

# A214: force DB_PATH to the conftest-managed temp DB and re-init so tables exist.
config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


@unittest.skipUnless(leak._HAS_CRYPTO, "cryptography not installed")
class LeakAttestationTests(unittest.TestCase):
    def test_cold_verify_signs_and_signature_validates(self):
        from cryptography.exceptions import InvalidSignature
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

        subject = "device-abc123"
        issued = leak.issue_token(subject)
        leak.observe(issued["token"], "8.8.8.8", 1)
        leak.observe(issued["token"], "1.1.1.1", 1)

        # This is the call that used to 500 on a cold process.
        result, label = leak.verify(issued["subdomain"], subject)
        self.assertIsNotNone(result, "cold verify() returned no attestation")
        self.assertTrue(result["queriesObserved"])
        self.assertEqual(sorted(result["observedResolvers"]), ["1.1.1.1", "8.8.8.8"])

        # Independent verification: the emitted signature must validate...
        pub = Ed25519PublicKey.from_public_bytes(base64.b64decode(result["publicKey"]))
        payload = base64.b64decode(result["signedPayload"])
        sig = base64.urlsafe_b64decode(result["signature"] + "=" * (-len(result["signature"]) % 4))
        pub.verify(sig, payload)  # raises on failure

        # ...the SIGNED payload must bind the SAME facts the attestation DISPLAYS. Otherwise a
        # regression could sign an empty/forged resolver set while displaying the real one
        # ("lying attestation") and still validate. Assert field-equality, not just a valid sig.
        import json as _json

        signed_obj = _json.loads(payload)
        self.assertEqual(
            signed_obj["observedResolvers"],
            result["observedResolvers"],
            "signed payload resolvers must equal the displayed resolvers (lying attestation)",
        )
        self.assertEqual(signed_obj["queriesObserved"], result["queriesObserved"])
        self.assertEqual(signed_obj["token"], result["token"])
        self.assertEqual(signed_obj["zone"], result["zone"])

        # ...and a tampered payload must be rejected.
        tampered = payload.replace(b'"queriesObserved":true', b'"queriesObserved":fals')
        self.assertNotEqual(tampered, payload)
        with self.assertRaises(InvalidSignature):
            pub.verify(sig, tampered)

    def test_verify_rejects_wrong_subject(self):
        issued = leak.issue_token("owner-1")
        result, err = leak.verify(issued["subdomain"], "someone-else")
        self.assertIsNone(result)
        self.assertIn("does not belong", err)

    def test_collector_fails_closed_without_a_provisioned_key(self):
        """A022 failure case: with NO provisioned key and NO dev escape, the collector must
        raise (refuse to start) — never silently mint a forgeable ephemeral key. Goes RED if
        the fail-closed `else: raise` is removed (regressing to an ephemeral key)."""
        if not leak._HAS_CRYPTO:
            self.skipTest("cryptography not installed")
        saved_key = leak._COLLECTOR_PRIV_KEY
        saved_escape = os.environ.get("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY")
        saved_priv = os.environ.get("SYXX_COLLECTOR_PRIVATE_KEY")
        try:
            leak._COLLECTOR_PRIV_KEY = None  # force re-derivation
            os.environ.pop("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", None)
            os.environ.pop("SYXX_COLLECTOR_PRIVATE_KEY", None)
            with self.assertRaises(RuntimeError):
                leak._ensure_collector_key()
            self.assertIsNone(leak._COLLECTOR_PRIV_KEY, "fail-closed must not leave a silently-minted key behind")
        finally:
            if saved_escape is not None:
                os.environ["SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY"] = saved_escape
            if saved_priv is not None:
                os.environ["SYXX_COLLECTOR_PRIVATE_KEY"] = saved_priv
            leak._COLLECTOR_PRIV_KEY = saved_key  # restore for other tests


# A204: class must be defined BEFORE `if __name__ == "__main__"`, or
# `python3 tests/test_leak.py` silently drops it from unittest discovery.
@unittest.skipUnless(leak._HAS_CRYPTO, "cryptography not installed")
class LeakHonestyGuardTests(unittest.TestCase):
    """The two guards that could be DELETED against a green suite (audit 2026-07-16).

    Deleting the A024 block made the server emit a cryptographically signed `queriesObserved:false` on ZERO
    observations — a signed all-clear with no liveness proof, indistinguishable from a collector
    path that was blocked, censored or never exercised. That is the precise failure this product
    exists to prevent, and nothing failed.
    """

    def test_empty_collector_is_inconclusive_and_UNSIGNED(self):
        """A024: zero observations must NEVER be signed as clean."""
        subject = "device-empty-collector"
        issued = leak.issue_token(subject)
        # Deliberately observe NOTHING — the collector path never fired.
        result, label = leak.verify(issued["subdomain"], subject)

        self.assertIsNotNone(result, "an unexercised collector must still return an honest result")
        self.assertIsNone(result["queriesObserved"], "zero observations must be queriesObserved=None, never False")
        self.assertTrue(result["inconclusive"], "zero observations must be explicitly inconclusive")
        self.assertEqual(result["observedResolvers"], [])
        # The load-bearing assertion: no signature. A signed clean reads as trustworthy to a client.
        self.assertNotIn("signature", result, "a clean result on zero evidence must NOT carry an Ed25519 signature")
        self.assertNotIn("signedPayload", result)
        # And the token must NOT be consumed, so a late query can still be attested.
        self.assertIsNone(label, "an inconclusive result must not consume the token")

    def test_never_issued_token_writes_nothing(self):
        """A009: observing a token the server never issued must be a no-op.

        Otherwise anyone on the internet can drive unbounded DB writes and poison the passive-DNS
        index that feeds drift results, via `dig <random>.leak.syxx.app`.
        """
        leak.observe("neverissuedlabel", "203.0.113.9", 1)
        self.assertEqual(
            db.leak_observations("neverissuedlabel"), [], "a never-issued token must record no observations"
        )

    def test_a_real_observation_still_signs(self):
        """Mutation guard: the A024 refusal must be specific to the EMPTY case — a genuine
        observation still produces a signed attestation. Without this, 'never sign anything'
        would pass the test above."""
        subject = "device-real-observation"
        issued = leak.issue_token(subject)
        leak.observe(issued["token"], "8.8.8.8", 1)
        result, label = leak.verify(issued["subdomain"], subject)

        self.assertTrue(result["queriesObserved"])
        self.assertIn("signature", result, "a result backed by real evidence must be signed")


if __name__ == "__main__":
    unittest.main()

"""Tests for the commerce preflight.

The point of the preflight is that a stock deploy is UNSELLABLE and says so at boot rather than at
a customer. These tests exist because a diagnostic that silently stops diagnosing is worse than no
diagnostic — you would trust a clean startup log that means nothing.
"""
import os
import unittest
from unittest.mock import patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
from syxx_server import config, preflight  # noqa: E402


def _real_collector_key():
    """A base64 DER PKCS#8 Ed25519 key that ACTUALLY LOADS.

    The fixture here was "c2VlZA==" — base64 for the word "seed". It satisfied the old
    truthiness check, so 'a fully configured deploy reports nothing' passed while describing
    a host whose /api/pubkey 500s on every call. The fixture has to be a working key or the
    test is asserting the bug.
    """
    import base64
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
    from cryptography.hazmat.primitives.serialization import (
        Encoding, PrivateFormat, NoEncryption)
    der = Ed25519PrivateKey.generate().private_bytes(
        Encoding.DER, PrivateFormat.PKCS8, NoEncryption())
    return base64.b64encode(der).decode("ascii")


def _real_keygen_public_key_hex():
    """A 64-char hex (32-byte) Ed25519 public key that ACTUALLY LOADS.

    A206/A218: _configure_everything() was "deadbeef" (4 bytes). The preflight now parses
    and validates the Keygen public key, so the fixture must be a real 32-byte key or
    test_a_fully_configured_deploy_reports_nothing asserts the bug it claims to guard.
    """
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
    from cryptography.hazmat.primitives.serialization import (
        Encoding, PublicFormat)
    pub = Ed25519PrivateKey.generate().public_key().public_bytes(
        Encoding.Raw, PublicFormat.Raw)
    return pub.hex()


class _Log:
    def __init__(self):
        self.warns, self.infos = [], []
    def warn(self, event, **kw):
        self.warns.append((event, kw))
    def info(self, event, **kw):
        self.infos.append((event, kw))


class PreflightTests(unittest.TestCase):
    def setUp(self):
        self._orig = {k: getattr(config, k) for k in
                      ("KEYGEN_PUBLIC_KEY", "KEYGEN_ADMIN_TOKEN", "STRIPE_SECRET_KEY",
                       "STRIPE_WEBHOOK_SECRET", "SMTP_HOST")}

    def tearDown(self):
        for k, v in self._orig.items():
            setattr(config, k, v)

    def _blank_everything(self):
        config.KEYGEN_PUBLIC_KEY = ""
        config.KEYGEN_ADMIN_TOKEN = ""
        config.STRIPE_SECRET_KEY = ""
        config.STRIPE_WEBHOOK_SECRET = ""
        config.SMTP_HOST = ""

    def _configure_everything(self):
        # A206/A218: was "deadbeef" (4 bytes) — passed the old truthiness check while describing
        # a deploy that 402s every paying customer. Now a real 32-byte Ed25519 public key in hex.
        config.KEYGEN_PUBLIC_KEY = _real_keygen_public_key_hex()
        config.KEYGEN_ADMIN_TOKEN = "tok"
        config.STRIPE_SECRET_KEY = "sk_test_x"
        config.STRIPE_WEBHOOK_SECRET = "whsec_x"
        config.SMTP_HOST = "smtp.example.com"

    def _fully_configured_env(self, **extra):
        """Env for a deploy with nothing missing: every policy id plus the collector key (which is
        read from the environment, not from config)."""
        env = {e["policy_env"]: "pol_x" for e in config.STRIPE_SKUS.values()}
        # Price ids too: the shipped catalog has them blank until Stripe is provisioned, which the
        # preflight correctly reports. These tests are about the OTHER checks;
        # test_a_sku_with_no_price_id_is_reported covers the blank case.
        env.update({e["priceid_env"]: "price_" + sku for sku, e in config.STRIPE_SKUS.items()})
        env["SYXX_COLLECTOR_PRIVATE_KEY"] = _real_collector_key()
        env.update(extra)
        return env

    def test_the_collector_signing_key_is_a_blocker(self):
        """Regression from PRODUCTION: the daemon ran for two days with no collector key, 500ing
        /api/pubkey on every call, and nothing said so at boot. The first version of this preflight
        did not check it — the logs are what proved the gap."""
        self._blank_everything()
        env = {k: v for k, v in os.environ.items()
               if k not in ("SYXX_COLLECTOR_PRIVATE_KEY", "SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY")}
        with patch.dict(os.environ, env, clear=True):
            findings = preflight.commerce_findings()
        blockers = {var for sev, var, _ in findings if sev == "blocker"}
        self.assertIn("SYXX_COLLECTOR_PRIVATE_KEY", blockers)

        why = next(w for _, var, w in findings if var == "SYXX_COLLECTOR_PRIVATE_KEY")
        self.assertIn("unverifiable", why)

    def test_a_bare_32_byte_seed_is_a_blocker_not_a_ready_deploy(self):
        """FIELD REGRESSION, 2026-07-16, on the live host.

        Provisioning the collector key produced `base64(os.urandom(32))` — a bare Ed25519
        seed. It is non-empty, it is valid base64, and it is NOT what load_der_private_key()
        accepts: every /api/pubkey call died with OpenSSL "not enough data". The preflight of
        that hour asked only `bool(env)`, so it would have printed commerce_ready for that
        exact host while the collector was 100% broken.

        A diagnostic that reports clean on a broken key is worse than no diagnostic: the
        operator trusts the clean line and stops looking. Presence is not function.
        """
        import base64
        self._configure_everything()
        seed = base64.b64encode(b"\x01" * 32).decode("ascii")     # what bit us, exactly
        with patch.dict(os.environ, self._fully_configured_env(
                SYXX_COLLECTOR_PRIVATE_KEY=seed,
                SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY="")):
            findings = preflight.commerce_findings()

        blockers = {v for s, v, _ in findings if s == "blocker"}
        self.assertIn("SYXX_COLLECTOR_PRIVATE_KEY", blockers)

        # The message must name the mistake and hand over the fix. "invalid key" would be
        # true, useless, and would have cost the same three rounds it cost in the field.
        why = next(w for _, v, w in findings if v == "SYXX_COLLECTOR_PRIVATE_KEY")
        self.assertIn("seed", why)
        self.assertIn("PKCS8", why)

    def test_a_garbage_collector_key_is_a_blocker(self):
        """The other half of present-but-unusable: not even base64."""
        self._configure_everything()
        with patch.dict(os.environ, self._fully_configured_env(
                SYXX_COLLECTOR_PRIVATE_KEY="not a key at all!!",
                SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY="")):
            blockers = {v for s, v, _ in preflight.commerce_findings() if s == "blocker"}
        self.assertIn("SYXX_COLLECTOR_PRIVATE_KEY", blockers)

    def test_the_ephemeral_dev_escape_satisfies_the_collector_check(self):
        """Mutation guard: the check must accept the documented dev escape, or every local test
        run would scream about a blocker that is intentional there."""
        self._configure_everything()
        with patch.dict(os.environ, {"SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY": "1",
                                     "SYXX_COLLECTOR_PRIVATE_KEY": ""}):
            blockers = {v for s_, v, _ in preflight.commerce_findings() if s_ == "blocker"}
        self.assertNotIn("SYXX_COLLECTOR_PRIVATE_KEY", blockers)

    def test_a_stock_deploy_reports_the_keygen_public_key_as_a_blocker(self):
        # The single most likely reason a fresh deploy "rejects all valid keys". If the preflight
        # reports anything, it must report this.
        self._blank_everything()
        findings = preflight.commerce_findings()
        blockers = {var for sev, var, _ in findings if sev == "blocker"}

        self.assertIn("SYXX_KEYGEN_PUBLIC_KEY", blockers)
        self.assertIn("STRIPE_SECRET_KEY", blockers)
        self.assertIn("SYXX_SMTP_HOST", blockers)

    def test_the_keygen_finding_explains_the_402_rather_than_naming_a_variable(self):
        # An operator reading the log must learn WHY valid keys are rejected. "SYXX_KEYGEN_PUBLIC_KEY
        # is unset" alone does not connect to the symptom they are actually seeing.
        self._blank_everything()
        why = next(w for sev, var, w in preflight.commerce_findings()
                   if var == "SYXX_KEYGEN_PUBLIC_KEY")
        self.assertIn("denied", why.lower())
        self.assertIn("402", why)

    def test_a_fully_configured_deploy_reports_nothing(self):
        # Mutation guard: without this, "always report blockers" would satisfy every test above
        # while making the preflight pure noise that operators learn to ignore.
        self._configure_everything()
        with patch.dict(os.environ, self._fully_configured_env()):
            self.assertEqual(preflight.commerce_findings(), [])

    def test_a_wrong_length_keygen_pubkey_is_a_blocker(self):
        """A206/A218: a truncated or wrong-length hex pubkey is the single most likely
        provisioning slip (copy-paste truncated, hex of the seed instead of the public key, etc.).
        The preflight must reject it with a blocker, not ratify it as commerce_ready."""
        self._configure_everything()
        # 4 bytes of valid hex — passes bool() and hex parsing, but wrong length for Ed25519.
        config.KEYGEN_PUBLIC_KEY = "deadbeef"
        with patch.dict(os.environ, self._fully_configured_env()):
            findings = preflight.commerce_findings()
        blockers = {v for s, v, _ in findings if s == "blocker"}
        self.assertIn("SYXX_KEYGEN_PUBLIC_KEY", blockers)
        why = next(w for _, v, w in findings if v == "SYXX_KEYGEN_PUBLIC_KEY")
        self.assertIn("32 bytes", why)

    def test_a_non_hex_keygen_pubkey_is_a_blocker(self):
        """A206/A218: a value that is not valid hex at all (e.g. a base64 string, a file path)
        must be caught by the preflight, not passed through to licensing where it 402s silently."""
        self._configure_everything()
        config.KEYGEN_PUBLIC_KEY = "not-a-hex-key!"
        with patch.dict(os.environ, self._fully_configured_env()):
            findings = preflight.commerce_findings()
        blockers = {v for s, v, _ in findings if s == "blocker"}
        self.assertIn("SYXX_KEYGEN_PUBLIC_KEY", blockers)

    def test_a_missing_policy_is_degraded_not_a_blocker(self):
        # A blank policy delays a key; it never loses a payment. Ranking it as a blocker would cry
        # wolf and devalue the real blockers next to it.
        self._configure_everything()
        env = {e["policy_env"]: "" for e in config.STRIPE_SKUS.values()}
        env["SYXX_COLLECTOR_PRIVATE_KEY"] = _real_collector_key()
        with patch.dict(os.environ, env):
            findings = preflight.commerce_findings()
        self.assertTrue(findings)
        self.assertTrue(all(sev == "degraded" for sev, _, _ in findings),
                        "a missing policy id must not be reported as a blocker")

    def test_a_sku_with_no_price_id_is_reported(self):
        """The shipped state on 2026-07-16: SKUs exist in code, prices do not exist in Stripe yet.
        Checkout refuses them (it must not fall back to the product's default price), so the buy
        button 503s. Degraded, not a blocker — a catalog may legitimately be provisioned ahead of
        Stripe — but an operator must not have to discover it from a customer."""
        self._configure_everything()
        # Build the env BEFORE patching the table: in a multi-item `with`, the second expression is
        # evaluated after the first manager is entered, so _fully_configured_env() would see the
        # patched catalog and helpfully supply a price for the SKU that must not have one.
        env = self._fully_configured_env()
        table = dict(config.STRIPE_SKUS)
        table["syxx_mac_unpriced_sub"] = dict(table["syxx_mac_yearly_sub"], price_default="",
                                              priceid_env="SYXX_STRIPE_PRICEID_MAC_UNPRICED")
        with patch.object(config, "STRIPE_SKUS", table), patch.dict(os.environ, env):
            findings = preflight.commerce_findings()

        self.assertIn("price:syxx_mac_unpriced_sub", {v for _, v, _ in findings})
        self.assertTrue(all(s == "degraded" for s, _, _ in findings),
                        "an unpriced SKU delays a sale; it must not outrank a real blocker")

    def test_logging_warns_on_blockers_and_never_raises(self):
        self._blank_everything()
        log = _Log()
        preflight.log_commerce_readiness(log)

        self.assertTrue(any(e == "commerce_not_sellable" for e, _ in log.warns),
                        "an unsellable deploy must say so in one unmissable line")

    def test_logging_says_ready_when_configured(self):
        self._configure_everything()
        with patch.dict(os.environ, self._fully_configured_env()):
            log = _Log()
            preflight.log_commerce_readiness(log)
        self.assertTrue(any(e == "commerce_ready" for e, _ in log.infos))
        self.assertFalse(log.warns)

    def test_preflight_never_takes_the_daemon_down(self):
        # DNS/analysis works with zero commerce config. A preflight that raises would turn a
        # missing Stripe key into an outage of the part that does work.
        log = _Log()
        with patch.object(preflight, "commerce_findings", side_effect=RuntimeError("boom")):
            preflight.log_commerce_readiness(log)   # must not raise
        self.assertTrue(any(e == "preflight_failed" for e, _ in log.warns))


if __name__ == "__main__":
    unittest.main()

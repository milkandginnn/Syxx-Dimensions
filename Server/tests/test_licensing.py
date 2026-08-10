"""Tests for the server licensing path — Keygen response signature verification (A021).

Stdlib + cryptography, no network: the urllib layer is monkeypatched so the
validate-key HTTP call never leaves localhost. Run from the Server/ dir:

    python3 -m pytest tests/test_licensing.py -v

Or with unittest:

    python3 -m unittest tests.test_licensing -v
"""

import base64
import email.utils
import hashlib
import io
import json
import os
import time
import urllib.error
from unittest.mock import Mock, patch

import pytest

os.environ.setdefault("SYXX_KEYGEN_ACCOUNT_ID", "test-account-id")

# Ed25519 keypair generated for testing only — NOT a live Keygen key.
# The private key is a 32-byte Ed25519 seed; the public key is its derived
# 32-byte verify key.  Both are hex-encoded.
_TEST_HEX_PUBKEY = "1c4309b871c1444359aa1caaab50a5fc36894ddfa12783c6918b361e17626d72"
_TEST_HEX_PRIVKEY = "9b07c944244b4163d46b755fdb52e873351be5df5f2972f6ee6dadfcd552bd72"

os.environ["SYXX_KEYGEN_PUBLIC_KEY"] = _TEST_HEX_PUBKEY
# NOTE: SYXX_DEV_MODE is deliberately NOT set here. It used to be set at module import, which —
# because pytest imports every test module into ONE interpreter and there is no conftest to reset
# it — put the WHOLE suite in dev mode and made licensing's fail-closed branch (licensing.py:143)
# unreachable by any test. Tests that want dev mode now opt in explicitly with patch.dict.

from syxx_server import config, licensing  # noqa: E402


def _ed25519_sign(payload_bytes: bytes) -> str:
    """Sign bytes with the test Ed25519 private key, return base64 signature."""
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

    priv = Ed25519PrivateKey.from_private_bytes(bytes.fromhex(_TEST_HEX_PRIVKEY))
    return base64.b64encode(priv.sign(payload_bytes)).decode("ascii")


def _build_response(
    body_dict, sign: bool = True, status: int = 200, method: str = "post", url: str = None, account_id: str = None
) -> io.BytesIO:
    """Return a mock urllib response object.

    When *sign* is True, the response includes valid Keygen-Signature, Digest,
    and Date headers that pass _verify_validate_key_response.
    When *sign* is False, the headers are omitted entirely.
    """
    if url is None:
        aid = account_id or config.KEYGEN_ACCOUNT_ID
        url = f"https://api.keygen.sh/v1/accounts/{aid}/licenses/actions/validate-key"

    from urllib.parse import urlparse

    body_bytes = json.dumps(body_dict).encode("utf-8")
    body_bytes.decode("utf-8")

    # Build the digest
    digest_b64 = base64.b64encode(hashlib.sha256(body_bytes).digest()).decode("ascii")

    # A056: a genuine (non-replayed) Keygen response carries a CURRENT Date. Generate
    # it dynamically so this fixture stays fresh; a hardcoded literal goes stale and is
    # (correctly) rejected by the new freshness window. The stale-Date rejection path
    # has its own dedicated test below.
    date_str = email.utils.formatdate(usegmt=True)

    # Build the signing data for the signature
    parsed = urlparse(url)
    signing_data = (
        f"(request-target): {method} {parsed.path}\n"
        f"host: {parsed.hostname}\n"
        f"date: {date_str}\n"
        f"digest: sha-256={digest_b64}"
    )

    if sign:
        signature = _ed25519_sign(signing_data.encode("utf-8"))
        sig_header = (
            f'keyid="{account_id or config.KEYGEN_ACCOUNT_ID}", '
            f'algorithm="ed25519", '
            f'signature="{signature}", '
            f'headers="(request-target) host date digest"'
        )
    else:
        sig_header = ""

    headers = {
        "Keygen-Signature": sig_header,
        "Digest": f"sha-256={digest_b64}",
        "Date": date_str,
    }

    # Build a mock response that mimics urllib.response.addinfourl
    raw = io.BytesIO(body_bytes)
    raw.headers = headers
    raw.status = status
    raw.url = url
    return raw


@pytest.fixture
def _test_pubkey(monkeypatch):
    monkeypatch.setattr(config, "KEYGEN_PUBLIC_KEY", _TEST_HEX_PUBKEY)


def _valid_headers(
    body_str, method="post", url="https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key"
):
    """Helper: return (sig_header, digest_header, date_header) for a valid signature."""
    digest_b64 = base64.b64encode(hashlib.sha256(body_str.encode("utf-8")).digest()).decode("ascii")
    from urllib.parse import urlparse

    parsed = urlparse(url)
    # A056: use a current Date so the signed, freshly-dated response verifies; the
    # stale-Date replay path is covered by its own dedicated test below.
    date_str = email.utils.formatdate(usegmt=True)
    signing_data = (
        f"(request-target): {method} {parsed.path}\n"
        f"host: {parsed.hostname}\n"
        f"date: {date_str}\n"
        f"digest: sha-256={digest_b64}"
    )
    signature = _ed25519_sign(signing_data.encode("utf-8"))
    sig_header = (
        f'keyid="test", algorithm="ed25519", signature="{signature}", headers="(request-target) host date digest"'
    )
    return sig_header, f"sha-256={digest_b64}", date_str


def test_skip_when_no_key_configured_IN_DEV_MODE_ONLY(_test_pubkey):
    """DEV-MODE behaviour: with no pubkey AND SYXX_DEV_MODE=1, verification is skipped.

    This asserts the DEV escape hatch, not the production contract. The production contract
    (no pubkey => DENY) is test_fail_closed_when_no_pubkey_and_not_dev_mode below. Dev mode is
    opted into explicitly here; it used to leak from module import and silently disable the
    fail-closed branch for the entire suite.
    """
    config.KEYGEN_PUBLIC_KEY = ""
    with patch.dict(os.environ, {"SYXX_DEV_MODE": "1"}):
        result = licensing._verify_validate_key_response(
            '{"meta":{"valid":true}}',
            "some-sig",
            "sha-256=x",
            "date",
            "post",
            "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key",
        )
    assert result  # dev mode only


def test_fail_closed_when_no_pubkey_and_not_dev_mode(_test_pubkey):
    """D040/A021 PRODUCTION contract: no configured pubkey => DENY, never grant.

    Without this, shipping with SYXX_KEYGEN_PUBLIC_KEY unset silently grants entitlement to any
    forged response. This branch was unreachable while dev mode leaked from module import — the
    guard could be DELETED with a green 119/119 suite.
    """
    config.KEYGEN_PUBLIC_KEY = ""
    env = {k: v for k, v in os.environ.items() if k != "SYXX_DEV_MODE"}
    with patch.dict(os.environ, env, clear=True):
        result = licensing._verify_validate_key_response(
            '{"meta":{"valid":true}}',
            "some-sig",
            "sha-256=x",
            "date",
            "post",
            "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key",
        )
    assert not result, "no pubkey configured must DENY in production, not skip"


def test_valid_signature_passes(_test_pubkey):
    body_str = '{"meta":{"valid":true}}'
    sig, dgst, dt = _valid_headers(body_str)
    result = licensing._verify_validate_key_response(
        body_str, sig, dgst, dt, "post", "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key"
    )
    assert result


def test_forged_body_fails(_test_pubkey):
    """A021 Fix 1: a MITM that replaces the response body must be caught."""
    valid_body = '{"meta":{"valid":true}}'
    forged_body = '{"meta":{"valid":true,"extra":"forged"}}'

    sig, dgst, dt = _valid_headers(valid_body)
    # The forged body has a different SHA-256 digest, so the signature
    # over the original body's digest won't match.
    result = licensing._verify_validate_key_response(
        forged_body, sig, dgst, dt, "post", "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key"
    )
    assert not result


def test_missing_sig_header_fails(_test_pubkey):
    # A205: use a fresh date so the Date guard doesn't reject before signature check.
    fresh_date = email.utils.formatdate(usegmt=True)
    result = licensing._verify_validate_key_response(
        '{"meta":{"valid":true}}',
        "",
        "sha-256=x",
        fresh_date,
        "post",
        "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key",
    )
    assert not result


def test_missing_digest_header_fails(_test_pubkey):
    # A205: use a fresh date so the Date guard doesn't reject before signature check.
    fresh_date = email.utils.formatdate(usegmt=True)
    result = licensing._verify_validate_key_response(
        '{"meta":{"valid":true}}',
        'keyid="test", algorithm="ed25519", signature="abc"',
        "",
        fresh_date,
        "post",
        "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key",
    )
    assert not result


def test_garbage_signature_fails(_test_pubkey):
    # A205: use a fresh date so the Date guard doesn't reject before signature check.
    # This test must actually reach the Ed25519 verify path.
    fresh_date = email.utils.formatdate(usegmt=True)
    result = licensing._verify_validate_key_response(
        '{"meta":{"valid":true}}',
        'keyid="test", algorithm="ed25519", signature="AAAA"',
        "sha-256=x",
        fresh_date,
        "post",
        "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key",
    )
    assert not result


def test_wrong_request_target_fails(_test_pubkey):
    """If a MITM replays the response on a different path, the (request-target)
    component of the signing data won't match, so verification fails."""
    body_str = '{"meta":{"valid":true}}'
    sig, dgst, dt = _valid_headers(
        body_str, url=("https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key")
    )
    wrong_url = "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key/other"
    result = licensing._verify_validate_key_response(body_str, sig, dgst, dt, "post", wrong_url)
    assert not result


def test_wrong_host_fails(_test_pubkey):
    """If a MITM proxies through a different host, the host part of the
    signing data won't match."""
    body_str = '{"meta":{"valid":true}}'
    sig, dgst, dt = _valid_headers(
        body_str, url=("https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key")
    )
    wrong_url = "https://evil.example.com/v1/accounts/test/licenses/actions/validate-key"
    result = licensing._verify_validate_key_response(body_str, sig, dgst, dt, "post", wrong_url)
    assert not result


def test_replayed_old_date_fails(_test_pubkey):
    """A056: a response signed with a CONSISTENT but stale Date must be rejected by the
    clock-skew guard alone.

    The previous version of this test signed over `old_date` and then verified with
    `new_date`, so it died on SIGNATURE MISMATCH and never reached the freshness check —
    deleting the skew guard entirely left the suite green. Here the Date in the signing data
    and the Date header AGREE, so the signature is genuinely valid and the ONLY thing that can
    reject it is the skew window. Without that guard, a captured validly-signed response from
    when the key was active is replayable forever, defeating expiry.
    """
    body_str = '{"meta":{"valid":true}}'
    digest_b64 = base64.b64encode(hashlib.sha256(body_str.encode("utf-8")).digest()).decode("ascii")
    # One hour old: far outside the 300s skew window, but internally consistent.
    stale_date = email.utils.formatdate(time.time() - 3600, usegmt=True)
    signing_data = (
        "(request-target): post /v1/accounts/test/licenses/actions/validate-key\n"
        "host: api.keygen.sh\n"
        f"date: {stale_date}\n"
        f"digest: sha-256={digest_b64}"
    )
    sig = _ed25519_sign(signing_data.encode("utf-8"))
    sig_header = (
        f'keyid="test", algorithm="ed25519", signature="{sig}", headers="(request-target) host date digest"'
    )
    result = licensing._verify_validate_key_response(
        body_str,
        sig_header,
        f"sha-256={digest_b64}",
        stale_date,
        "post",
        "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key",
    )
    assert not result, "a validly-signed but stale response must be rejected as a replay"


def test_freshly_dated_response_passes(_test_pubkey):
    """Mutation guard for the test above: the skew check must reject only STALE dates, not
    every date. Without this, `return False` unconditionally would pass the replay test."""
    body_str = '{"meta":{"valid":true}}'
    sig, dgst, dt = _valid_headers(body_str)
    result = licensing._verify_validate_key_response(
        body_str, sig, dgst, dt, "post", "https://api.keygen.sh/v1/accounts/test/licenses/actions/validate-key"
    )
    assert result, "a correctly signed, freshly dated response must verify"


@pytest.fixture
def _keygen_mock(monkeypatch):
    monkeypatch.setattr(config, "KEYGEN_PUBLIC_KEY", _TEST_HEX_PUBKEY)
    # Register urlopen for restore; the tests reassign it directly via _install_mock.
    monkeypatch.setattr(licensing.urllib.request, "urlopen", licensing.urllib.request.urlopen)
    mock_sem = Mock()
    monkeypatch.setattr(licensing, "_keygen_sem", mock_sem)
    return mock_sem


def _install_mock(body_dict, sign=True, status=200, method="post", url=None):
    """Monkeypatch urllib.request.urlopen to return a controlled response."""
    mock_resp = _build_response(body_dict, sign=sign, status=status, method=method, url=url)
    licensing.urllib.request.urlopen = lambda req, **kw: mock_resp


def test_valid_key_with_signature_grants_pro(_keygen_mock):
    _keygen_mock.acquire.return_value = True
    _install_mock({"meta": {"valid": True}})
    result = licensing._validate_keygen("KEY-1234")
    assert result == "pro"


def test_forged_valid_key_without_signature_denies(_keygen_mock):
    """A021 Fix 1: response has meta.valid=true but no signature header → deny."""
    _keygen_mock.acquire.return_value = True
    _install_mock({"meta": {"valid": True}}, sign=False)
    result = licensing._validate_keygen("FORGED-KEY")
    assert result is None


def test_forged_body_with_stolen_signature_denies(_keygen_mock):
    """A225 Fix 1: a MITM captures a valid response (body A) and substitutes a forged
    body (body B) with body A's signature headers. The server computes body B's digest
    and compares it against body A's signature — they must not match.

    Previously this test was byte-for-byte identical to test_forged_valid_key_without_signature_denies
    (both used sign=False), so the stolen-signature scenario was never actually exercised
    at the integration layer."""
    _keygen_mock.acquire.return_value = True
    # Body A: the genuine response the MITM captured
    genuine_body = {"meta": {"valid": True}}
    genuine_resp = _build_response(genuine_body, sign=True)
    # Body B: the forged body the MITM wants to substitute
    forged_body_bytes = json.dumps({"meta": {"valid": True}, "data": {"type": "licenses", "id": "forged"}}).encode(
        "utf-8"
    )
    # Deliver body B with body A's signature headers — the server must reject
    # because body B's digest does not match body A's signature.
    forged_raw = io.BytesIO(forged_body_bytes)
    forged_raw.headers = genuine_resp.headers
    forged_raw.status = 200
    forged_raw.url = genuine_resp.url
    licensing.urllib.request.urlopen = lambda req, **kw: forged_raw
    result = licensing._validate_keygen("FORGED-BODY")
    assert result is None, "a stolen signature over a substituted body must be rejected"


def test_transient_http_error_still_transient(_keygen_mock):
    """Regression: HTTP 503 still returns __transient__, not None."""
    _keygen_mock.acquire.return_value = True

    def _raise_503(req, **kw):
        raise urllib.error.HTTPError(
            req.full_url or req.get_full_url(), code=503, msg="Service Unavailable", hdrs={}, fp=None
        )

    licensing.urllib.request.urlopen = _raise_503
    result = licensing._validate_keygen("KEY-ANY")
    assert result == "__transient__"


def test_keygen_401_returns_none_not_transient(_keygen_mock):
    """Regression: HTTP 401 (invalid key) returns None, not __transient__."""
    _keygen_mock.acquire.return_value = True

    def _raise_401(req, **kw):
        raise urllib.error.HTTPError(
            req.full_url or req.get_full_url(), code=401, msg="Unauthorized", hdrs={}, fp=None
        )

    licensing.urllib.request.urlopen = _raise_401
    result = licensing._validate_keygen("BAD-KEY")
    assert result is None


def test_keygen_404_returns_none(_keygen_mock):
    """Regression: HTTP 404 returns None."""
    _keygen_mock.acquire.return_value = True

    def _raise_404(req, **kw):
        raise urllib.error.HTTPError(
            req.full_url or req.get_full_url(), code=404, msg="Not Found", hdrs={}, fp=None
        )

    licensing.urllib.request.urlopen = _raise_404
    result = licensing._validate_keygen("NONEXISTENT-KEY")
    assert result is None


def test_network_timeout_still_transient(_keygen_mock):
    """Regression: network/timeout error returns __transient__."""
    _keygen_mock.acquire.return_value = True
    licensing.urllib.request.urlopen = lambda req, **kw: (_ for _ in ()).throw(Exception("Connection refused"))
    result = licensing._validate_keygen("KEY-ANY")
    assert result == "__transient__"


def test_semaphore_timeout_still_transient(_keygen_mock):
    _keygen_mock.acquire.return_value = False
    result = licensing._validate_keygen("KEY-ANY")
    assert result == "__transient__"


def test_skip_verification_when_no_pubkey_IN_DEV_MODE_ONLY(_keygen_mock):
    """DEV-MODE behaviour: empty KEYGEN_PUBLIC_KEY + SYXX_DEV_MODE=1 skips verification.

    The production counterpart is test_denies_when_no_pubkey_and_not_dev_mode below.
    """
    _keygen_mock.acquire.return_value = True
    config.KEYGEN_PUBLIC_KEY = ""
    _install_mock({"meta": {"valid": True}}, sign=False)
    with patch.dict(os.environ, {"SYXX_DEV_MODE": "1"}):
        result = licensing._validate_keygen("KEY-NOPUBKEY")
    assert result == "pro"


def test_denies_when_no_pubkey_and_not_dev_mode(_keygen_mock):
    """D040 end-to-end: a stock production deploy (no pubkey, no dev mode) must NOT grant pro
    on an unsigned response, however valid `meta.valid` claims to be."""
    _keygen_mock.acquire.return_value = True
    config.KEYGEN_PUBLIC_KEY = ""
    _install_mock({"meta": {"valid": True}}, sign=False)
    env = {k: v for k, v in os.environ.items() if k != "SYXX_DEV_MODE"}
    with patch.dict(os.environ, env, clear=True):
        result = licensing._validate_keygen("KEY-NOPUBKEY")
    assert result != "pro", "an unsigned response must never grant pro in production"


@pytest.fixture
def _offline_keys(monkeypatch):
    monkeypatch.setattr(config, "OFFLINE_LICENSE_KEYS", ["OFFLINE-ADMIN-KEY"])


def test_offline_key_returns_pro_directly(_offline_keys):
    ent, err = licensing.validate_license("OFFLINE-ADMIN-KEY")
    assert ent is not None
    assert ent.plan == "pro"
    assert ent.source == "offline"
    assert err is None


def test_short_key_returns_none(_offline_keys):
    ent, err = licensing.validate_license("short")
    assert ent is None
    assert err is None


def test_empty_key_returns_none(_offline_keys):
    ent, err = licensing.validate_license("")
    assert ent is None
    assert err is None


def test_none_key_returns_none(_offline_keys):
    ent, err = licensing.validate_license(None)
    assert ent is None
    assert err is None

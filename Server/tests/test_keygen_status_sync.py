"""Regression tests for S12 — the Keygen status sweep must query the STORED
license id, never the local subject hash.

The old check_keygen_license_status(subject) queried
/licenses/<subject>, where subject is "lic_" + sha256(key)[:16] — a local
hash, not a Keygen resource id. Keygen answered 404 for every real subject,
404 read as "invalid", and the daily sweep (_sync_api_keys) would have
revoked the API keys of EVERY Keygen-licensed subject on its first run.

The fix stores the Keygen license id at activation (_validate_keygen) in the
keygen_licenses table and queries that id. A subject with no stored id is
UNKNOWN (None), never invalid: the sweep must skip it, not revoke.

Run from Server/:  python3 -m unittest tests.test_keygen_status_sync -v
"""

import io
import json
import os
import tempfile
from unittest.mock import patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import config, db, licensing, scheduler  # noqa: E402


def _admin_response(status_value):
    """Mock urllib response for the admin GET /licenses/<id> endpoint."""
    body = json.dumps({"data": {"id": "kg-lic-1", "attributes": {"status": status_value}}}).encode("utf-8")
    raw = io.BytesIO(body)
    raw.headers = {}
    raw.status = 200
    return raw


@pytest.fixture
def _temp_db():
    tmp = tempfile.mkdtemp()
    orig_path = config.DB_PATH
    config.DB_PATH = os.path.join(tmp, "syxx-server.db")
    conn = db.connect()
    try:
        conn.executescript(db.SCHEMA)
        conn.commit()
    finally:
        conn.close()
    orig_admin = config.KEYGEN_ADMIN_TOKEN
    orig_account = config.KEYGEN_ACCOUNT_ID
    config.KEYGEN_ADMIN_TOKEN = "admin-test-token"
    config.KEYGEN_ACCOUNT_ID = "test-account-id"
    orig_urlopen = licensing.urllib.request.urlopen
    yield
    licensing.urllib.request.urlopen = orig_urlopen
    config.KEYGEN_ADMIN_TOKEN = orig_admin
    config.KEYGEN_ACCOUNT_ID = orig_account
    config.DB_PATH = orig_path


def test_happy_path_queries_stored_license_id(_temp_db):
    db.keygen_license_id_set("lic_0123456789abcdef", "kg-lic-1")
    seen = {}

    def fake_urlopen(req, **kw):
        seen["url"] = req.full_url
        return _admin_response("ACTIVE")

    licensing.urllib.request.urlopen = fake_urlopen
    result = licensing.check_keygen_license_status("lic_0123456789abcdef")
    assert result is True
    assert seen["url"].endswith("/licenses/kg-lic-1"), "must query the STORED Keygen id, got {}".format(
        seen.get("url")
    )
    assert "lic_0123456789abcdef" not in seen["url"], "the local subject hash must never be sent as a Keygen id"


def test_revoked_status_returns_false(_temp_db):
    db.keygen_license_id_set("lic_aaaa", "kg-lic-1")
    licensing.urllib.request.urlopen = lambda req, **kw: _admin_response("REVOKED")
    assert licensing.check_keygen_license_status("lic_aaaa") is False


def test_failure_case_no_stored_id_is_unknown_and_never_queries(_temp_db):
    """THE S12 catastrophe guard: a subject without a stored id must come
    back None (unknown), and no HTTP request may be made at all. The old
    code returned False here (via a 404 on the hash), which is exactly the
    state that revoked every key; this test goes RED on that code."""

    def must_not_be_called(req, **kw):
        raise AssertionError(f"HTTP query issued for a subject with no stored license id: {req.full_url}")

    licensing.urllib.request.urlopen = must_not_be_called
    assert licensing.check_keygen_license_status("lic_no_stored_id") is None


def test_transient_error_returns_none(_temp_db):
    import urllib.error

    db.keygen_license_id_set("lic_bbbb", "kg-lic-1")

    def raise_503(req, **kw):
        raise urllib.error.HTTPError(req.full_url, 503, "Service Unavailable", {}, None)

    licensing.urllib.request.urlopen = raise_503
    assert licensing.check_keygen_license_status("lic_bbbb") is None


def test_404_on_a_stored_id_is_invalid(_temp_db):
    """With a real stored id, a 404 means the license was deleted at Keygen;
    that IS a revocation signal and must stay False."""
    import urllib.error

    db.keygen_license_id_set("lic_cccc", "kg-lic-gone")

    def raise_404(req, **kw):
        raise urllib.error.HTTPError(req.full_url, 404, "Not Found", {}, None)

    licensing.urllib.request.urlopen = raise_404
    assert licensing.check_keygen_license_status("lic_cccc") is False


def _validate(key, body_dict):
    """_validate_keygen must persist data.id so the sweep has something to query.
    Dev mode + empty public key skips the Ed25519 layer (covered exhaustively in
    test_licensing.py); this targets only the id-storage side effect."""
    raw = io.BytesIO(json.dumps(body_dict).encode("utf-8"))
    raw.headers = {}
    raw.status = 200
    licensing.urllib.request.urlopen = lambda req, **kw: raw
    with patch.dict(os.environ, {"SYXX_DEV_MODE": "1"}):
        orig_pub = config.KEYGEN_PUBLIC_KEY
        config.KEYGEN_PUBLIC_KEY = ""
        try:
            return licensing._validate_keygen(key)
        finally:
            config.KEYGEN_PUBLIC_KEY = orig_pub


def test_valid_key_stores_the_license_id(_temp_db):
    result = _validate(
        "KEY-STORE-ME",
        {
            "meta": {"valid": True},
            "data": {"id": "kg-lic-777", "type": "licenses"},
        },
    )
    assert result == "pro"
    subject = licensing._subject_id("KEY-STORE-ME")
    assert db.keygen_license_id_get(subject) == "kg-lic-777"


def test_valid_key_without_data_id_still_validates_but_stores_nothing(_temp_db):
    result = _validate("KEY-NO-ID", {"meta": {"valid": True}})
    assert result == "pro"
    assert db.keygen_license_id_get(licensing._subject_id("KEY-NO-ID")) is None


def test_invalid_key_stores_nothing(_temp_db):
    result = _validate(
        "KEY-BAD",
        {
            "meta": {"valid": False},
            "data": {"id": "kg-lic-evil"},
        },
    )
    assert result is None
    assert db.keygen_license_id_get(licensing._subject_id("KEY-BAD")) is None


def test_sweep_keeps_keys_for_subjects_without_stored_ids(_temp_db):
    """End-to-end guard on the catastrophe: run the real _sync_api_keys with
    Keygen unreachable-by-assertion; every subject lacks a stored id, so no
    HTTP fires and NO key is revoked. Under the old code this sweep revoked
    both keys (404 -> False -> revoke), so this test goes RED there."""
    raw_a = db.api_key_create("lic_subject_a", label="a")
    raw_b = db.api_key_create("lic_subject_b", label="b")
    assert raw_a and raw_b

    def must_not_be_called(req, **kw):
        raise AssertionError("sweep issued an HTTP query with no stored id")

    licensing.urllib.request.urlopen = must_not_be_called
    scheduler._sync_api_keys()
    import hashlib

    for raw in (raw_a, raw_b):
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        assert db.api_key_lookup(key_hash) is not None, "sweep revoked a key it could not verify"

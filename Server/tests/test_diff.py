"""Tests for the content-diff half of /api/diff — script-injection detection + result folding.

The clean-vantage fetch (`warp.build_baseline`) is monkeypatched, so no network. Run from Server/:
    python3 -m pytest tests/test_diff.py -v
"""

import os

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import app, warp  # noqa: E402


@pytest.fixture
def _baseline(monkeypatch):
    def _patch(scripts, inline_hashes=None):
        monkeypatch.setattr(warp, "build_baseline", lambda url, **kwargs: {"scripts": scripts, "inlineScriptHashes": inline_hashes or []})

    yield _patch


def test_cross_host_injection_is_tampered(_baseline):
    _baseline(["https://cdn.example.com/app.js"])
    local = (
        '<script src="https://evil.tracker.net/inject.js"></script>'
        '<script src="https://cdn.example.com/app.js"></script>'
    )
    c = app._diff_content("https://example.com/", local, None, None)
    assert c["assessment"] == "injected"
    assert "evil.tracker.net" in c["injectedScriptHosts"]
    assert app._diff_result("match", c) == "tampered"


def test_same_host_churn_is_divergent_not_accused(_baseline):
    _baseline(["https://example.com/a.js"])
    local = '<script src="https://example.com/a.js"></script><script src="https://example.com/b.js"></script>'
    c = app._diff_content("https://example.com/", local, None, None)
    assert c["assessment"] == "divergent"
    assert c["injectedScriptHosts"] == []  # same host → never accused of injection
    assert app._diff_result("match", c) == "inconclusive"


def test_identical_structure_is_clean(_baseline):
    _baseline(["https://example.com/a.js"])
    local = '<script src="https://example.com/a.js"></script>'
    c = app._diff_content("https://example.com/", local, None, None)
    assert c["assessment"] == "match"
    assert c["match"]
    assert app._diff_result("match", c) == "clean"


def test_fetch_failure_is_inconclusive(_baseline, monkeypatch):
    def boom(url, **kwargs):
        raise app.fetcher.FetchError("blocked")

    monkeypatch.setattr(warp, "build_baseline", boom)
    c = app._diff_content("https://example.com/", "<html></html>", None, None)
    assert c["assessment"] == "inconclusive"
    assert app._diff_result("match", c) == "inconclusive"


def test_preparsed_scripts_supported(_baseline):
    _baseline(["https://example.com/a.js"])
    c = app._diff_content("https://example.com/", None, ["https://ads.x.com/t.js"], None)
    assert c["assessment"] == "injected"
    assert "ads.x.com" in c["injectedScriptHosts"]


def test_result_dns_divergence_alone_is_tampered():
    assert app._diff_result("divergent", None) == "tampered"


def test_result_partial_dns_stays_inconclusive():
    assert app._diff_result("partial", None) == "inconclusive"


def test_result_clean_dns_no_content_is_clean():
    assert app._diff_result("match", None) == "clean"

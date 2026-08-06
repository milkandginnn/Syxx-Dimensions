"""Tests for the content-diff half of /api/diff — script-injection detection + result folding.

The clean-vantage fetch (`warp.build_baseline`) is monkeypatched, so no network. Run from Server/:
    python3 -m unittest tests.test_diff -v
"""

import os
import unittest

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import app, warp  # noqa: E402


class DiffContentTests(unittest.TestCase):
    def setUp(self):
        self._orig = warp.build_baseline

    def tearDown(self):
        warp.build_baseline = self._orig

    def _baseline(self, scripts, inline_hashes=None):
        warp.build_baseline = lambda url: {"scripts": scripts, "inlineScriptHashes": inline_hashes or []}

    def test_cross_host_injection_is_tampered(self):
        self._baseline(["https://cdn.example.com/app.js"])
        local = (
            '<script src="https://evil.tracker.net/inject.js"></script>'
            '<script src="https://cdn.example.com/app.js"></script>'
        )
        c = app._diff_content("https://example.com/", local, None, None)
        self.assertEqual(c["assessment"], "injected")
        self.assertIn("evil.tracker.net", c["injectedScriptHosts"])
        self.assertEqual(app._diff_result("match", c), "tampered")

    def test_same_host_churn_is_divergent_not_accused(self):
        self._baseline(["https://example.com/a.js"])
        local = '<script src="https://example.com/a.js"></script><script src="https://example.com/b.js"></script>'
        c = app._diff_content("https://example.com/", local, None, None)
        self.assertEqual(c["assessment"], "divergent")
        self.assertEqual(c["injectedScriptHosts"], [])  # same host → never accused of injection
        self.assertEqual(app._diff_result("match", c), "inconclusive")

    def test_identical_structure_is_clean(self):
        self._baseline(["https://example.com/a.js"])
        local = '<script src="https://example.com/a.js"></script>'
        c = app._diff_content("https://example.com/", local, None, None)
        self.assertEqual(c["assessment"], "match")
        self.assertTrue(c["match"])
        self.assertEqual(app._diff_result("match", c), "clean")

    def test_fetch_failure_is_inconclusive(self):
        def boom(url):
            raise app.fetcher.FetchError("blocked")

        warp.build_baseline = boom
        c = app._diff_content("https://example.com/", "<html></html>", None, None)
        self.assertEqual(c["assessment"], "inconclusive")
        self.assertEqual(app._diff_result("match", c), "inconclusive")

    def test_preparsed_scripts_supported(self):
        self._baseline(["https://example.com/a.js"])
        c = app._diff_content("https://example.com/", None, ["https://ads.x.com/t.js"], None)
        self.assertEqual(c["assessment"], "injected")
        self.assertIn("ads.x.com", c["injectedScriptHosts"])

    def test_result_dns_divergence_alone_is_tampered(self):
        self.assertEqual(app._diff_result("divergent", None), "tampered")

    def test_result_partial_dns_stays_inconclusive(self):
        self.assertEqual(app._diff_result("partial", None), "inconclusive")

    def test_result_clean_dns_no_content_is_clean(self):
        self.assertEqual(app._diff_result("match", None), "clean")


if __name__ == "__main__":
    unittest.main()

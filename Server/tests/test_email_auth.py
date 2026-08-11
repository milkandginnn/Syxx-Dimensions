"""Tests for syxx_server.email_auth — SPF, DMARC, DKIM record checking.

Covers SPF policy extraction, DMARC tag parsing, DKIM selector logic,
and the overall conclusion algorithm. DNS queries are mocked.
"""

import os
from unittest.mock import MagicMock, patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import email_auth


# --- SPF parsing ------------------------------------------------------------


class TestExtractSpfAll:
    def test_hard_fail(self):
        assert email_auth._extract_spf_all("v=spf1 include:_spf.google.com -all") == "-all"

    def test_soft_fail(self):
        assert email_auth._extract_spf_all("v=spf1 ~all") == "~all"

    def test_pass_all(self):
        assert email_auth._extract_spf_all("v=spf1 +all") == "+all"

    def test_neutral_all(self):
        assert email_auth._extract_spf_all("v=spf1 ?all") == "?all"

    def test_bare_all(self):
        """C225: bare 'all' with no qualifier."""
        assert email_auth._extract_spf_all("v=spf1 all") == "all"

    def test_no_all_mechanism(self):
        assert email_auth._extract_spf_all("v=spf1 include:example.com") is None


class TestSpfResult:
    def test_hard_fail_is_pass(self):
        assert email_auth._spf_result("-all") == "pass"

    def test_plus_all_is_fail(self):
        assert email_auth._spf_result("+all") == "fail"

    def test_softfail(self):
        assert email_auth._spf_result("~all") == "softfail"

    def test_neutral(self):
        assert email_auth._spf_result("?all") == "neutral"

    def test_bare_all_is_permissive(self):
        assert email_auth._spf_result("all") == "permissive"

    def test_none_is_neutral(self):
        assert email_auth._spf_result(None) == "neutral"


# --- DMARC parsing ----------------------------------------------------------


class TestParseTags:
    def test_basic_tags(self):
        result = email_auth._parse_tags("v=DMARC1; p=reject; rua=mailto:dmarc@example.com")
        assert result["p"] == "reject"
        assert result["rua"] == "mailto:dmarc@example.com"

    def test_empty_string(self):
        assert email_auth._parse_tags("") == {}

    def test_no_equals(self):
        assert email_auth._parse_tags("v=DMARC1; nopair") == {"v": "DMARC1"}


class TestDmarcResult:
    def test_reject_with_rua(self):
        assert email_auth._dmarc_result("reject", "mailto:r@example.com") == "pass"

    def test_quarantine_with_rua(self):
        assert email_auth._dmarc_result("quarantine", "mailto:r@example.com") == "pass"

    def test_reject_without_rua(self):
        assert email_auth._dmarc_result("reject", None) == "partial"

    def test_none_with_rua(self):
        assert email_auth._dmarc_result("none", "mailto:r@example.com") == "neutral"

    def test_none_without_rua(self):
        assert email_auth._dmarc_result("none", None) == "fail"

    def test_no_policy(self):
        assert email_auth._dmarc_result(None, None) == "missing"


# --- DKIM -------------------------------------------------------------------


class TestDkimResult:
    def test_valid_key(self):
        assert email_auth._dkim_result("v=DKIM1; k=rsa; p=MIGfMA0GCSqG") == "pass"

    def test_revoked_key(self):
        """RFC 8301: empty p= means revoked."""
        assert email_auth._dkim_result("v=DKIM1; k=rsa; p=") == "fail"

    def test_no_p_tag(self):
        assert email_auth._dkim_result("v=DKIM1; k=rsa") == "fail"


# --- Overall ----------------------------------------------------------------


class TestOverall:
    def test_all_pass(self):
        assert email_auth._overall("pass", "pass", "pass") == "pass"

    def test_any_fail(self):
        assert email_auth._overall("fail", "pass", "pass") == "fail"
        assert email_auth._overall("pass", "fail", "pass") == "fail"
        assert email_auth._overall("pass", "pass", "fail") == "fail"

    def test_all_inconclusive(self):
        """A270: total measurement failure -> inconclusive, not partial."""
        assert email_auth._overall("inconclusive", "inconclusive", "inconclusive") == "inconclusive"

    def test_all_missing(self):
        assert email_auth._overall("missing", "missing", "missing") == "inconclusive"

    def test_mixed_inconclusive_and_pass(self):
        assert email_auth._overall("pass", "inconclusive", "pass") == "partial"

    def test_softfail_and_pass(self):
        assert email_auth._overall("softfail", "pass", "pass") == "pass"

    def test_neutral_and_pass(self):
        assert email_auth._overall("neutral", "pass", "pass") == "pass"

    def test_permissive_treated_as_determinate(self):
        """C225: bare 'all' = permissive is a valid determinate result."""
        assert email_auth._overall("permissive", "pass", "pass") == "pass"


# --- Integration with mocked DNS --------------------------------------------


def _mock_dns(records, rcode=0):
    """Build a mock resolver.query return value."""
    return {"rcode": rcode, "records": records, "ad": False}


class TestCheckSpf:
    @patch("syxx_server.email_auth.resolver")
    @patch("syxx_server.email_auth.config")
    def test_spf_found(self, mock_config, mock_resolver):
        mock_config.PUBLIC_RESOLVERS = ["8.8.8.8"]
        mock_resolver.TYPE_TXT = 16
        mock_resolver.DNSError = Exception
        mock_resolver.query.return_value = _mock_dns([{"type": "TXT", "value": "v=spf1 -all"}])
        result = email_auth._check_spf("example.com", "8.8.8.8", 5)
        assert result["conclusion"] == "pass"
        assert result["policy"] == "-all"

    @patch("syxx_server.email_auth.resolver")
    def test_spf_missing(self, mock_resolver):
        mock_resolver.TYPE_TXT = 16
        mock_resolver.DNSError = Exception
        mock_resolver.query.return_value = _mock_dns([{"type": "TXT", "value": "v=DKIM1; p=abc"}])
        result = email_auth._check_spf("example.com", "8.8.8.8", 5)
        assert result["conclusion"] == "missing"

    @patch("syxx_server.email_auth.resolver")
    def test_spf_dns_error(self, mock_resolver):
        mock_resolver.TYPE_TXT = 16
        mock_resolver.DNSError = Exception
        mock_resolver.query.side_effect = Exception("DNS timeout")
        result = email_auth._check_spf("example.com", "8.8.8.8", 5)
        assert result["conclusion"] == "inconclusive"


class TestCheckDmarc:
    @patch("syxx_server.email_auth.resolver")
    def test_dmarc_found(self, mock_resolver):
        mock_resolver.TYPE_TXT = 16
        mock_resolver.DNSError = Exception
        mock_resolver.query.return_value = _mock_dns([
            {"type": "TXT", "value": "v=DMARC1; p=reject; rua=mailto:d@example.com"}
        ])
        result = email_auth._check_dmarc("example.com", "8.8.8.8", 5)
        assert result["conclusion"] == "pass"
        assert result["policy"] == "reject"

    @patch("syxx_server.email_auth.resolver")
    def test_dmarc_missing(self, mock_resolver):
        mock_resolver.TYPE_TXT = 16
        mock_resolver.DNSError = Exception
        mock_resolver.query.return_value = _mock_dns([])
        result = email_auth._check_dmarc("example.com", "8.8.8.8", 5)
        assert result["conclusion"] == "missing"

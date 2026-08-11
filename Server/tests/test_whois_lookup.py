"""Tests for syxx_server.whois_lookup — WHOIS/RDAP lookup.

Covers TLD extraction, RDAP response parsing, WHOIS text parsing,
CR/LF injection prevention, and the lookup() entry point.
Network calls are mocked throughout.
"""

import os
from unittest.mock import MagicMock, patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import whois_lookup


# --- TLD extraction ---------------------------------------------------------


class TestExtractTld:
    def test_simple_tld(self):
        assert whois_lookup._extract_tld("example.com") == "com"

    def test_two_part_tld(self):
        """SM-035: multi-part TLDs extracted correctly."""
        assert whois_lookup._extract_tld("example.co.uk") == "co.uk"

    def test_co_jp(self):
        assert whois_lookup._extract_tld("example.co.jp") == "co.jp"

    def test_com_au(self):
        assert whois_lookup._extract_tld("example.com.au") == "com.au"

    def test_single_label(self):
        assert whois_lookup._extract_tld("localhost") == "localhost"

    def test_trailing_dot(self):
        assert whois_lookup._extract_tld("example.com.") == "com"

    def test_deep_subdomain(self):
        assert whois_lookup._extract_tld("a.b.c.example.com") == "com"


# --- RDAP parsing -----------------------------------------------------------


class TestParseRdap:
    def test_basic_rdap(self):
        data = {
            "events": [
                {"eventAction": "registration", "eventDate": "2020-01-15T00:00:00Z"},
                {"eventAction": "expiration", "eventDate": "2025-01-15T00:00:00Z"},
                {"eventAction": "last changed", "eventDate": "2024-06-01T00:00:00Z"},
            ],
            "status": ["client transfer prohibited"],
            "nameservers": [{"ldhName": "ns1.example.com"}, {"ldhName": "ns2.example.com"}],
            "entities": [
                {
                    "roles": ["registrar"],
                    "vcardArray": [None, [["fn", {}, "text", "Example Registrar Inc."]]],
                    "publicIds": [{"type": "IANA Registrar ID", "identifier": "1234"}],
                }
            ],
        }
        result = whois_lookup._parse_rdap(data)
        assert result["source"] == "rdap"
        assert result["created"] == "2020-01-15"
        assert result["expires"] == "2025-01-15"
        assert result["updated"] == "2024-06-01"
        assert result["registrar"] == "Example Registrar Inc."
        assert result["registrarId"] == "1234"
        assert "ns1.example.com" in result["nameservers"]

    def test_empty_events(self):
        result = whois_lookup._parse_rdap({"events": [], "status": [], "nameservers": [], "entities": []})
        assert result["source"] == "rdap"
        assert "created" not in result

    def test_no_entities(self):
        result = whois_lookup._parse_rdap({"events": [], "status": [], "nameservers": [], "entities": []})
        assert "registrar" not in result


# --- WHOIS text parsing -----------------------------------------------------


class TestParseWhoisText:
    def test_basic_whois(self):
        raw = (
            "   Domain Name: EXAMPLE.COM\r\n"
            "   Registrar: Example Registrar, Inc.\r\n"
            "   Creation Date: 2020-01-15T00:00:00Z\r\n"
            "   Registry Expiry Date: 2025-01-15T00:00:00Z\r\n"
            "   Updated Date: 2024-06-01T00:00:00Z\r\n"
            "   Name Server: NS1.EXAMPLE.COM\r\n"
            "   Name Server: NS2.EXAMPLE.COM\r\n"
            "   Domain Status: clientTransferProhibited https://icann.org/epp#clientTransferProhibited\r\n"
        )
        result = whois_lookup._parse_whois_text(raw, "example.com")
        assert result["source"] == "whois"
        assert result["registrar"] == "Example Registrar, Inc."
        assert result["created"] == "2020-01-15"
        assert "ns1.example.com" in result["nameservers"]
        assert "clientTransferProhibited" in result["status"]

    def test_not_found_response(self):
        """A170: negative WHOIS responses detected."""
        raw = "No match for \"NONEXISTENT.COM\".\n"
        result = whois_lookup._parse_whois_text(raw, "nonexistent.com")
        assert result["result"] == "not-found"

    def test_domain_not_found(self):
        raw = "Domain not found.\n"
        result = whois_lookup._parse_whois_text(raw, "gone.com")
        assert result["result"] == "not-found"

    def test_sm039_line_start_matching(self):
        """SM-039: patterns checked at line start, not substring."""
        raw = (
            "   Domain Name: NOMATCHFOR.COM\n"
            "   Registrar: Real Registrar\n"
        )
        result = whois_lookup._parse_whois_text(raw, "nomatchfor.com")
        # Should NOT be detected as "no match for" since it's in the domain name
        assert result.get("result") != "not-found"
        assert result["registrar"] == "Real Registrar"


# --- Redirect safety --------------------------------------------------------


class TestRedirectSafety:
    def test_non_https_rejected(self):
        assert whois_lookup._redirect_target_is_safe("http://evil.com") is False

    def test_empty_url(self):
        assert whois_lookup._redirect_target_is_safe("") is False

    def test_non_string(self):
        assert whois_lookup._redirect_target_is_safe(None) is False


# --- lookup() entry point ---------------------------------------------------


class TestLookup:
    def test_invalid_domain_raises(self):
        with pytest.raises(ValueError):
            whois_lookup.lookup("")

    def test_no_dot_raises(self):
        with pytest.raises(ValueError):
            whois_lookup.lookup("localhost")

    def test_cr_injection_rejected(self):
        """A170 / SD-99: CR/LF in domain rejected."""
        with pytest.raises(ValueError, match="CR/LF"):
            whois_lookup.lookup("example.com\r\nEVIL")

    def test_lf_injection_rejected(self):
        with pytest.raises(ValueError, match="CR/LF"):
            whois_lookup.lookup("example.com\nEVIL")

    @patch.object(whois_lookup, "_rdap_query", return_value=None)
    @patch.object(whois_lookup, "_whois_fallback", return_value=None)
    def test_both_fail_returns_no_data(self, mock_whois, mock_rdap):
        result = whois_lookup.lookup("example.com")
        assert result["source"] == "none"
        assert result["domain"] == "example.com"

    @patch.object(whois_lookup, "_rdap_query")
    def test_rdap_success(self, mock_rdap):
        mock_rdap.return_value = {"source": "rdap", "created": "2020-01-01", "registrar": "Test"}
        result = whois_lookup.lookup("example.com")
        assert result["source"] == "rdap"
        assert result["domain"] == "example.com"

    @patch.object(whois_lookup, "_rdap_query", return_value=None)
    @patch.object(whois_lookup, "_whois_fallback")
    def test_whois_fallback(self, mock_whois, mock_rdap):
        mock_whois.return_value = {"source": "whois", "registrar": "Fallback Registrar"}
        result = whois_lookup.lookup("example.com")
        assert result["source"] == "whois"
        assert result["domain"] == "example.com"

    @patch.object(whois_lookup, "_rdap_query", return_value=None)
    @patch.object(whois_lookup, "_whois_fallback")
    def test_whois_not_found_returned(self, mock_whois, mock_rdap):
        """A170: not-found is a valid conclusive answer."""
        mock_whois.return_value = {"source": "whois", "result": "not-found"}
        result = whois_lookup.lookup("nonexistent.com")
        assert result["result"] == "not-found"


# --- Two-part TLD set -------------------------------------------------------


class TestTwoPartTlds:
    def test_co_uk_present(self):
        assert "co.uk" in whois_lookup._TWO_PART_TLDS

    def test_com_au_present(self):
        assert "com.au" in whois_lookup._TWO_PART_TLDS

    def test_co_jp_present(self):
        assert "co.jp" in whois_lookup._TWO_PART_TLDS

    def test_com_not_present(self):
        assert "com" not in whois_lookup._TWO_PART_TLDS

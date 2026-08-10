"""Tests for syxx_server.subdomain_enum — CT log subdomain enumeration.

Covers subdomain extraction from crt.sh entries, name validation,
wildcard stripping, and the enumerate() entry point. Network calls
and DNS resolution are mocked.
"""

import os
from unittest.mock import MagicMock, patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import subdomain_enum


# --- Subdomain extraction ---------------------------------------------------


class TestExtractSubdomains:
    def test_basic_extraction(self):
        entries = [
            {"common_name": "www.example.com", "name_value": "www.example.com"},
            {"common_name": "mail.example.com", "name_value": "mail.example.com"},
        ]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert "www.example.com" in result
        assert "mail.example.com" in result

    def test_wildcard_stripped(self):
        entries = [{"common_name": "*.example.com", "name_value": "*.example.com"}]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert "example.com" in result
        assert "*.example.com" not in result

    def test_deduplication(self):
        entries = [
            {"common_name": "www.example.com", "name_value": "www.example.com"},
            {"common_name": "www.example.com", "name_value": "www.example.com"},
        ]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert result.count("www.example.com") == 1

    def test_san_newline_separated(self):
        entries = [{"common_name": "", "name_value": "a.example.com\nb.example.com"}]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert "a.example.com" in result
        assert "b.example.com" in result

    def test_unrelated_domain_excluded(self):
        entries = [{"common_name": "www.other.com", "name_value": "www.other.com"}]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert len(result) == 0

    def test_base_domain_included(self):
        entries = [{"common_name": "example.com", "name_value": "example.com"}]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert "example.com" in result

    def test_sorted_output(self):
        entries = [
            {"common_name": "z.example.com", "name_value": "z.example.com"},
            {"common_name": "a.example.com", "name_value": "a.example.com"},
        ]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert result == sorted(result)

    def test_empty_entries(self):
        result = subdomain_enum._extract_subdomains([], "example.com")
        assert result == []

    def test_max_subdomains_cap(self):
        """Entries beyond _MAX_SUBDOMAINS are truncated by enumerate(), not here."""
        # _extract_subdomains itself does not cap — enumerate() does
        entries = [{"common_name": f"s{i}.example.com", "name_value": f"s{i}.example.com"} for i in range(250)]
        result = subdomain_enum._extract_subdomains(entries, "example.com")
        assert len(result) == 250  # no cap at extraction level


# --- CrtshError -------------------------------------------------------------


class TestCrtshError:
    def test_is_exception(self):
        assert issubclass(subdomain_enum._CrtshError, Exception)

    def test_message(self):
        err = subdomain_enum._CrtshError("rate-limited")
        assert "rate-limited" in str(err)


# --- enumerate() entry point ------------------------------------------------


class TestEnumerate:
    @patch.object(subdomain_enum, "_resolve_subdomains")
    @patch.object(subdomain_enum, "_query_crtsh")
    def test_basic_enumerate(self, mock_crtsh, mock_resolve):
        mock_crtsh.return_value = [
            {"common_name": "www.example.com", "name_value": "www.example.com"},
            {"common_name": "mail.example.com", "name_value": "mail.example.com"},
        ]
        mock_resolve.return_value = (
            {"www.example.com": ["1.2.3.4"], "mail.example.com": ["5.6.7.8"]},
            False,
        )
        result = subdomain_enum.enumerate("example.com")
        assert result["domain"] == "example.com"
        assert result["total"] == 2
        assert result["source"] == "crt.sh"
        assert "subdomains" in result

    @patch.object(subdomain_enum, "_resolve_subdomains")
    @patch.object(subdomain_enum, "_query_crtsh")
    def test_crtsh_failure_truncated(self, mock_crtsh, mock_resolve):
        mock_crtsh.side_effect = subdomain_enum._CrtshError("503")
        mock_resolve.return_value = ({}, False)
        result = subdomain_enum.enumerate("example.com")
        assert result["truncated"] is True
        assert result["total"] == 0

    @patch.object(subdomain_enum, "_resolve_subdomains")
    @patch.object(subdomain_enum, "_query_crtsh")
    def test_no_resolve(self, mock_crtsh, mock_resolve):
        mock_crtsh.return_value = [
            {"common_name": "www.example.com", "name_value": "www.example.com"},
        ]
        result = subdomain_enum.enumerate("example.com", do_resolve=False)
        assert result["total"] == 1
        mock_resolve.assert_not_called()

    @patch.object(subdomain_enum, "_resolve_subdomains")
    @patch.object(subdomain_enum, "_query_crtsh")
    def test_truncation_at_max(self, mock_crtsh, mock_resolve):
        entries = [{"common_name": f"s{i}.example.com", "name_value": f"s{i}.example.com"} for i in range(250)]
        mock_crtsh.return_value = entries
        mock_resolve.return_value = ({}, False)
        result = subdomain_enum.enumerate("example.com")
        assert result["total"] <= subdomain_enum._MAX_SUBDOMAINS
        assert result["truncated"] is True

    @patch.object(subdomain_enum, "_resolve_subdomains")
    @patch.object(subdomain_enum, "_query_crtsh")
    def test_resolve_time_reported(self, mock_crtsh, mock_resolve):
        mock_crtsh.return_value = [{"common_name": "a.example.com", "name_value": "a.example.com"}]
        mock_resolve.return_value = ({"a.example.com": ["1.2.3.4"]}, False)
        result = subdomain_enum.enumerate("example.com")
        assert "resolveTime_ms" in result
        assert result["resolveTime_ms"] >= 0

    @patch.object(subdomain_enum, "_resolve_subdomains")
    @patch.object(subdomain_enum, "_query_crtsh")
    def test_budget_exhausted_sets_truncated(self, mock_crtsh, mock_resolve):
        mock_crtsh.return_value = [{"common_name": "a.example.com", "name_value": "a.example.com"}]
        mock_resolve.return_value = ({}, True)  # budget_exhausted = True
        result = subdomain_enum.enumerate("example.com")
        assert result["truncated"] is True

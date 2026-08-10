"""Tests for syxx_server.bulk_investigate — concurrent multi-domain investigation.

Covers deduplication, domain validation, truncation, timeout handling,
and the aggregate result structure. investigate.investigate() is mocked.
"""

import os
from unittest.mock import MagicMock, patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import bulk_investigate


def _mock_investigate(domain):
    """Return a valid investigation result for any domain."""
    return {
        "domain": domain,
        "consensus": "consistent",
        "consensusReason": "vantages agree",
        "ipv4": {"v1": {"status": "ok", "values": ["1.2.3.4"]}},
        "ipv6": {},
        "dnssec": {"status": "unknown", "observed": None},
    }, None


@pytest.fixture(autouse=True)
def _mock_investigate_module():
    with patch.object(bulk_investigate.investigate, "investigate", side_effect=_mock_investigate):
        with patch.object(bulk_investigate.investigate, "valid_domain", return_value=True):
            yield


class TestInvestigateAll:
    def test_basic_structure(self):
        result = bulk_investigate.investigate_all(["example.com"])
        assert "results" in result
        assert "total" in result
        assert "success" in result
        assert "failed" in result
        assert "truncated" in result

    def test_single_domain(self):
        result = bulk_investigate.investigate_all(["example.com"])
        assert result["total"] == 1
        assert result["success"] == 1

    def test_multiple_domains(self):
        result = bulk_investigate.investigate_all(["a.com", "b.com", "c.com"])
        assert result["total"] == 3
        assert result["success"] == 3

    def test_deduplication(self):
        """SM-019: duplicate domains are deduplicated."""
        result = bulk_investigate.investigate_all(["a.com", "a.com", "a.com"])
        assert result["total"] == 1

    def test_case_insensitive_dedup(self):
        result = bulk_investigate.investigate_all(["Example.com", "example.com"])
        assert result["total"] == 1

    def test_truncation(self):
        domains = [f"d{i}.com" for i in range(60)]
        result = bulk_investigate.investigate_all(domains, max_domains=10)
        assert result["truncated"] is True
        assert result["total"] <= 10

    def test_no_truncation_under_limit(self):
        result = bulk_investigate.investigate_all(["a.com", "b.com"], max_domains=10)
        assert result["truncated"] is False

    def test_empty_input(self):
        result = bulk_investigate.investigate_all([])
        assert result["total"] == 0
        assert result["success"] == 0


class TestInvalidDomains:
    def test_invalid_domain_reported(self):
        with patch.object(bulk_investigate.investigate, "valid_domain", return_value=False):
            result = bulk_investigate.investigate_all(["invalid"])
            assert result["total"] == 1
            assert result["failed"] == 1
            assert result["results"][0]["error"] == "invalid domain"

    def test_mixed_valid_and_invalid(self):
        def selective(d):
            return d != "bad"

        with patch.object(bulk_investigate.investigate, "valid_domain", side_effect=selective):
            result = bulk_investigate.investigate_all(["good.com", "bad"])
            assert result["total"] == 2
            assert result["success"] == 1
            assert result["failed"] == 1


class TestErrorHandling:
    def test_investigate_error_captured(self):
        def failing_investigate(domain):
            return None, "investigation failed"

        with patch.object(bulk_investigate.investigate, "investigate", side_effect=failing_investigate):
            result = bulk_investigate.investigate_all(["fail.com"])
            assert result["total"] == 1
            assert result["failed"] == 1
            assert "investigation failed" in result["results"][0]["error"]

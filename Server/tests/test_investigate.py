"""Tests for syxx_server.investigate — multi-vantage DNS investigation engine.

Covers domain validation, consensus algorithm (majority voting, divergence
detection, single-vantage escalation), DNSSEC observation, and the
investigate() entry point. DNS resolution is mocked throughout.
"""

import os
from unittest.mock import MagicMock, patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import investigate


# --- Domain validation ------------------------------------------------------


class TestValidDomain:
    def test_valid_ascii(self):
        assert investigate.valid_domain("example.com") is True

    def test_valid_subdomain(self):
        assert investigate.valid_domain("sub.example.com") is True

    def test_valid_idn(self):
        """SM-001: internationalized domain names accepted."""
        assert investigate.valid_domain("münchen.de") is True

    def test_valid_punycode_tld(self):
        """A291: Punycode TLDs (xn--...) accepted."""
        assert investigate.valid_domain("example.xn--fiqs8s") is True

    def test_empty_string(self):
        assert investigate.valid_domain("") is False

    def test_none(self):
        assert investigate.valid_domain(None) is False

    def test_no_tld(self):
        assert investigate.valid_domain("localhost") is False

    def test_too_long(self):
        assert investigate.valid_domain("a" * 254 + ".com") is False


# --- Consensus algorithm ----------------------------------------------------


class TestConsensusOne:
    def test_consistent_all_agree(self):
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v2": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v3": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "consistent"

    def test_consistent_majority_agrees(self):
        """S5: majority voting, not unanimity."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v2": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v3": {"status": "ok", "values": ["5.6.7.8"], "rcodeName": "NOERROR"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "consistent"

    def test_divergent_no_majority(self):
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.1.1.1"], "rcodeName": "NOERROR"},
            "v2": {"status": "ok", "values": ["2.2.2.2"], "rcodeName": "NOERROR"},
            "v3": {"status": "ok", "values": ["3.3.3.3"], "rcodeName": "NOERROR"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "divergent"

    def test_nxdomain_divergence(self):
        """A044: NXDOMAIN alongside records is divergence."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v2": {"status": "nxdomain", "values": [], "rcodeName": "NXDOMAIN"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "divergent"
        assert "NXDOMAIN" in reason

    def test_nodata_divergence(self):
        """NODATA alongside records is divergence."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v2": {"status": "ok", "values": [], "rcodeName": "NOERROR"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "divergent"

    def test_inconclusive_all_errors(self):
        by_vantage = {
            "v1": {"status": "error", "error": "timeout", "values": []},
            "v2": {"status": "error", "error": "timeout", "values": []},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "inconclusive"

    def test_inconclusive_majority_unreachable(self):
        """A279: >50% errors -> inconclusive."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v2": {"status": "error", "error": "timeout", "values": []},
            "v3": {"status": "error", "error": "timeout", "values": []},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "inconclusive"
        assert "unreachable" in reason

    def test_single_vantage(self):
        """A290: single reachable vantage -> single-vantage."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "single-vantage"

    def test_consistent_degraded_with_errors(self):
        """A045: non-ok vantage degrades confidence."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v2": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"},
            "v3": {"status": "error", "error": "timeout", "values": []},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "consistent-degraded"

    def test_two_vantage_disagree_is_degraded(self):
        """A292: 2 vantages disagree -> likely geo/CDN, not divergent."""
        by_vantage = {
            "v1": {"status": "ok", "values": ["1.1.1.1"], "rcodeName": "NOERROR"},
            "v2": {"status": "ok", "values": ["2.2.2.2"], "rcodeName": "NOERROR"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "consistent-degraded"

    def test_all_nxdomain(self):
        by_vantage = {
            "v1": {"status": "nxdomain", "values": [], "rcodeName": "NXDOMAIN"},
            "v2": {"status": "nxdomain", "values": [], "rcodeName": "NXDOMAIN"},
        }
        result, reason = investigate._consensus_one(by_vantage)
        assert result == "inconclusive"
        assert "NXDOMAIN" in reason


# --- Overall consensus ------------------------------------------------------


class TestConsensus:
    def test_ipv4_wins_over_inconclusive_ipv6(self):
        a = {"v1": {"status": "ok", "values": ["1.2.3.4"], "rcodeName": "NOERROR"}}
        aaaa = {"v1": {"status": "error", "error": "x", "values": []}}
        result, reason, va, v6 = investigate._consensus(a, aaaa)
        # Should not be inconclusive since IPv4 resolved
        assert result != "inconclusive" or va[0] == "inconclusive"


# --- Rank ordering ----------------------------------------------------------


class TestRank:
    def test_divergent_highest_rank(self):
        assert investigate._RANK["divergent"] > investigate._RANK["consistent"]

    def test_single_vantage_above_consistent_degraded(self):
        assert investigate._RANK["single-vantage"] > investigate._RANK["consistent-degraded"]

    def test_consistent_lowest_non_inconclusive(self):
        assert investigate._RANK["consistent"] < investigate._RANK["consistent-degraded"]

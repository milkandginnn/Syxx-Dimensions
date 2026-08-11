"""Unit tests for syxx_server.firewall_test — DNS filter detection module.

Covers _ip_is_sinkhole, _is_filtered, _query_resolver, _compute_assessment,
_query_clean_reference, _test_one_domain, and the top-level test() orchestrator.
All DNS queries are mocked — no network calls.
"""

import os
from unittest.mock import MagicMock, patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

from syxx_server import firewall_test  # noqa: E402


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_resolver_query():
    """Patch resolver.query so no real DNS queries are made."""
    with patch("syxx_server.firewall_test.resolver.query") as m:
        yield m


@pytest.fixture
def mock_ip_is_public():
    """Patch netutil.ip_is_public to control public-IP gating."""
    with patch("syxx_server.firewall_test._ip_is_public") as m:
        m.return_value = True
        yield m


def _make_response(rcode=0, records=None, qtype="A"):
    """Build a resolver.query-compatible response dict."""
    recs = records or []
    return {"rcode": rcode, "records": recs}


def _a_record(ip):
    return {"type": "A", "value": ip}


def _aaaa_record(ip):
    return {"type": "AAAA", "value": ip}


# ---------------------------------------------------------------------------
# _ip_is_sinkhole
# ---------------------------------------------------------------------------

class TestIpIsSinkhole:
    def test_known_sinkhole_0000(self):
        assert firewall_test._ip_is_sinkhole("0.0.0.0") is True

    def test_known_sinkhole_127001(self):
        assert firewall_test._ip_is_sinkhole("127.0.0.1") is True

    def test_known_sinkhole_ipv6_loopback(self):
        assert firewall_test._ip_is_sinkhole("::1") is True

    def test_zero_block_matches_any_0xyz(self):
        """SM-042: any address in 0.0.0.0/8 is a sinkhole."""
        assert firewall_test._ip_is_sinkhole("0.1.2.3") is True
        assert firewall_test._ip_is_sinkhole("0.255.255.255") is True

    def test_ten_block_is_sinkhole(self):
        """B321: 10.0.0.0/8 is ISP private range used for sinkholing."""
        assert firewall_test._ip_is_sinkhole("10.0.0.1") is True
        assert firewall_test._ip_is_sinkhole("10.200.100.50") is True

    def test_172_16_through_31_is_sinkhole(self):
        """B321: 172.16.0.0/12 range."""
        assert firewall_test._ip_is_sinkhole("172.16.0.1") is True
        assert firewall_test._ip_is_sinkhole("172.31.255.255") is True

    def test_172_outside_range_not_sinkhole(self):
        assert firewall_test._ip_is_sinkhole("172.15.0.1") is False
        assert firewall_test._ip_is_sinkhole("172.32.0.1") is False

    def test_public_ip_not_sinkhole(self):
        assert firewall_test._ip_is_sinkhole("8.8.8.8") is False
        assert firewall_test._ip_is_sinkhole("1.1.1.1") is False

    def test_rfc5737_test_nets_are_sinkholes(self):
        """B321: RFC 5737 documentation ranges used for DNS sinkholing."""
        assert firewall_test._ip_is_sinkhole("192.0.2.1") is True
        assert firewall_test._ip_is_sinkhole("198.51.100.1") is True
        assert firewall_test._ip_is_sinkhole("203.0.113.1") is True

    def test_systemd_resolved_stub(self):
        assert firewall_test._ip_is_sinkhole("127.0.0.53") is True


# ---------------------------------------------------------------------------
# _is_filtered
# ---------------------------------------------------------------------------

class TestIsFiltered:
    def test_nxdomain_on_user_resolved_on_clean(self):
        assert firewall_test._is_filtered("nxdomain", [], "resolved", ["93.184.216.34"]) is True

    def test_refused_on_user_resolved_on_clean(self):
        assert firewall_test._is_filtered("refused", [], "resolved", ["93.184.216.34"]) is True

    def test_nodata_on_user_resolved_on_clean(self):
        """A159: NODATA (Pi-hole/AdGuard mode) on user side is filtered."""
        assert firewall_test._is_filtered("nodata", [], "resolved", ["93.184.216.34"]) is True

    def test_sinkhole_ip_on_user_resolved_on_clean(self):
        assert firewall_test._is_filtered("resolved", ["0.0.0.0"], "resolved", ["93.184.216.34"]) is True

    def test_all_sinkhole_ips_filtered(self):
        assert firewall_test._is_filtered("resolved", ["0.0.0.0", "127.0.0.1"], "resolved", ["1.2.3.4"]) is True

    def test_mixed_sinkhole_and_real_ip_not_filtered(self):
        """If at least one address is not a sinkhole, it's not fully filtered."""
        assert firewall_test._is_filtered("resolved", ["0.0.0.0", "93.184.216.34"], "resolved", ["93.184.216.34"]) is False

    def test_clean_also_fails_cannot_judge(self):
        assert firewall_test._is_filtered("nxdomain", [], "nxdomain", []) is False

    def test_both_resolve_no_sinkhole_not_filtered(self):
        assert firewall_test._is_filtered("resolved", ["93.184.216.34"], "resolved", ["93.184.216.34"]) is False

    def test_user_error_clean_resolved_not_filtered(self):
        """A generic error on the user side is not classified as filtered."""
        assert firewall_test._is_filtered("error", [], "resolved", ["1.2.3.4"]) is False


# ---------------------------------------------------------------------------
# _query_resolver
# ---------------------------------------------------------------------------

class TestQueryResolver:
    def test_resolved_with_a_record(self, mock_resolver_query):
        mock_resolver_query.side_effect = [
            _make_response(0, [_a_record("93.184.216.34")]),   # A
            _make_response(0, []),                              # AAAA (no records)
        ]
        status, addrs = firewall_test._query_resolver("example.com", "1.1.1.1", 5)
        assert status == "resolved"
        assert "93.184.216.34" in addrs

    def test_nxdomain(self, mock_resolver_query):
        mock_resolver_query.return_value = _make_response(3)
        status, addrs = firewall_test._query_resolver("blocked.test", "1.1.1.1", 5)
        assert status == "nxdomain"
        assert addrs == []

    def test_refused(self, mock_resolver_query):
        mock_resolver_query.return_value = _make_response(5)
        status, addrs = firewall_test._query_resolver("blocked.test", "1.1.1.1", 5)
        assert status == "refused"
        assert addrs == []

    def test_exception_returns_error(self, mock_resolver_query):
        mock_resolver_query.side_effect = Exception("timeout")
        status, addrs = firewall_test._query_resolver("example.com", "1.1.1.1", 5)
        assert status == "error"
        assert addrs == []

    def test_noerror_with_no_records_returns_error(self):
        """NOERROR with zero A/AAAA records: last_status stays at initial 'error'
        because the rcode==0 path doesn't update last_status."""
        with patch("syxx_server.firewall_test.resolver.query") as mq:
            mq.return_value = _make_response(0, [])
            status, addrs = firewall_test._query_resolver("example.com", "1.1.1.1", 5)
        assert status == "error"
        assert addrs == []

    def test_aaaa_record_returned(self, mock_resolver_query):
        mock_resolver_query.side_effect = [
            _make_response(0, []),                                       # A: no records
            _make_response(0, [_aaaa_record("2606:2800:220:1:248:1893:25c8:1946")]),  # AAAA
        ]
        status, addrs = firewall_test._query_resolver("example.com", "1.1.1.1", 5)
        assert status == "resolved"
        assert "2606:2800:220:1:248:1893:25c8:1946" in addrs


# ---------------------------------------------------------------------------
# _compute_assessment
# ---------------------------------------------------------------------------

class TestComputeAssessment:
    def test_filtered_when_30pct_or_more(self):
        # 3 filtered out of 10 = 30% → filtered
        assert firewall_test._compute_assessment(10, 3, 0, 10, True, float("inf")) == "filtered"

    def test_open_when_no_filtering_and_clean_ok(self):
        assert firewall_test._compute_assessment(10, 0, 0, 10, True, float("inf")) == "open"

    def test_inconclusive_when_no_clean_reference(self):
        assert firewall_test._compute_assessment(10, 0, 0, 0, True, float("inf")) == "inconclusive"

    def test_inconclusive_when_too_many_inconclusive_measurements(self):
        """A159: >20% inconclusive → not 'open'."""
        # 3 inconclusive out of 10 = 30% > 20%
        assert firewall_test._compute_assessment(10, 0, 3, 10, True, float("inf")) == "inconclusive"

    def test_inconclusive_when_clean_domains_fail(self):
        # clean_domains_ok=False and 0 filtered
        assert firewall_test._compute_assessment(10, 0, 0, 10, False, float("inf")) == "inconclusive"

    def test_filtered_at_exact_boundary(self):
        # Exactly 30% → filtered (>= threshold)
        assert firewall_test._compute_assessment(10, 3, 1, 10, True, float("inf")) == "filtered"

    def test_not_filtered_below_threshold(self):
        # 2 filtered out of 10 = 20% < 30% → not automatically filtered
        result = firewall_test._compute_assessment(10, 2, 0, 10, True, float("inf"))
        assert result != "filtered"


# ---------------------------------------------------------------------------
# _query_clean_reference
# ---------------------------------------------------------------------------

class TestQueryCleanReference:
    def test_returns_resolved_when_any_vantage_succeeds(self):
        with patch.object(firewall_test, "_query_resolver") as mq:
            # First vantage fails, second resolves
            mq.side_effect = [
                ("nxdomain", []),
                ("resolved", ["93.184.216.34"]),
                ("error", []),
                ("error", []),
                ("error", []),
            ]
            status, addrs, had_ref = firewall_test._query_clean_reference("example.com", 5)
        assert status == "resolved"
        assert had_ref is True

    def test_no_vantage_resolves(self):
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("error", [])
            status, addrs, had_ref = firewall_test._query_clean_reference("example.com", 5)
        assert had_ref is False
        assert status == "error"


# ---------------------------------------------------------------------------
# _test_one_domain
# ---------------------------------------------------------------------------

class TestTestOneDomain:
    def test_filtered_domain_detected(self):
        entry = {"domain": "eicar.org", "category": "malware", "expected": "blocked"}
        with patch.object(firewall_test, "_query_resolver") as mq:
            # User resolver: NXDOMAIN; clean vantage: resolves
            mq.side_effect = [
                ("nxdomain", []),  # user query
                ("resolved", ["93.184.216.34"]),  # first clean vantage
                ("error", []),
                ("error", []),
                ("error", []),
                ("error", []),
            ]
            result, filtered, user_status, had_ref, clean_ok = firewall_test._test_one_domain(
                entry, "203.0.113.1", 5
            )
        assert filtered is True
        assert user_status == "nxdomain"
        assert had_ref is True

    def test_clean_domain_resolving_is_clean_ok(self):
        entry = {"domain": "example.com", "category": "clean", "expected": "resolve"}
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("resolved", ["93.184.216.34"])
            result, filtered, user_status, had_ref, clean_ok = firewall_test._test_one_domain(
                entry, "203.0.113.1", 5
            )
        assert clean_ok is True
        assert filtered is False

    def test_clean_domain_not_resolving_is_not_clean_ok(self):
        entry = {"domain": "example.com", "category": "clean", "expected": "resolve"}
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("nxdomain", [])
            result, filtered, user_status, had_ref, clean_ok = firewall_test._test_one_domain(
                entry, "203.0.113.1", 5
            )
        assert clean_ok is False


# ---------------------------------------------------------------------------
# test() — top-level orchestrator
# ---------------------------------------------------------------------------

class TestTest:
    def test_non_public_ip_returns_inconclusive(self, mock_ip_is_public):
        mock_ip_is_public.return_value = False
        result = firewall_test.test("127.0.0.1")
        assert result["assessment"] == "inconclusive"
        assert "error" in result
        assert result["results"] == []

    def test_all_filtered_returns_filtered_assessment(self, mock_ip_is_public):
        mock_ip_is_public.return_value = True
        with patch.object(firewall_test, "_query_resolver") as mq:
            # User: NXDOMAIN for everything; clean: resolves for everything
            def side_effect(domain, server, timeout):
                if server in firewall_test._CLEAN_VANTAGES:
                    return ("resolved", ["93.184.216.34"])
                return ("nxdomain", [])
            mq.side_effect = side_effect
            result = firewall_test.test("203.0.113.1")
        assert result["assessment"] == "filtered"
        assert result["summary"]["filtered"] > 0

    def test_all_open_returns_open_assessment(self, mock_ip_is_public):
        mock_ip_is_public.return_value = True
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("resolved", ["93.184.216.34"])
            result = firewall_test.test("203.0.113.1")
        assert result["assessment"] == "open"
        assert result["summary"]["filtered"] == 0

    def test_result_structure(self, mock_ip_is_public):
        mock_ip_is_public.return_value = True
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("resolved", ["93.184.216.34"])
            result = firewall_test.test("203.0.113.1")
        assert "resolver" in result
        assert "results" in result
        assert "summary" in result
        assert "assessment" in result
        assert result["resolver"] == "203.0.113.1"
        assert "tested" in result["summary"]
        assert "filtered" in result["summary"]
        assert "clean" in result["summary"]
        assert "inconclusive" in result["summary"]

    def test_summary_tested_matches_results_length(self, mock_ip_is_public):
        mock_ip_is_public.return_value = True
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("resolved", ["93.184.216.34"])
            result = firewall_test.test("203.0.113.1")
        assert result["summary"]["tested"] == len(result["results"])

    def test_each_result_has_required_keys(self, mock_ip_is_public):
        mock_ip_is_public.return_value = True
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("resolved", ["93.184.216.34"])
            result = firewall_test.test("203.0.113.1")
        for r in result["results"]:
            assert "domain" in r
            assert "category" in r
            assert "userConclusion" in r
            assert "cleanConclusion" in r
            assert "filtered" in r

    def test_deadline_truncated_flag_present(self, mock_ip_is_public):
        mock_ip_is_public.return_value = True
        with patch.object(firewall_test, "_query_resolver") as mq:
            mq.return_value = ("resolved", ["93.184.216.34"])
            result = firewall_test.test("203.0.113.1")
        assert "deadline_truncated" in result["summary"]

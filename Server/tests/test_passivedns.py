"""Unit tests for syxx_server/passivedns.py — passive DNS observation store.

Tests cover:
  - ingest(): recording, empty inputs, per-domain cardinality cap
  - query(): response shape, resolver field stripping, domain normalization
  - total_records(): count delegation
  - baseline(): drift detection, insufficient-data, truncation, established/new classification

All db functions are monkeypatched — no real SQLite interaction.
"""

import time
from unittest.mock import MagicMock

import pytest

from syxx_server import passivedns  # noqa: E402
from syxx_server import db as _db  # noqa: E402


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def passive_store():
    """In-memory passive DNS store replacing db.passive_* functions.

    Yields a dict with:
      - records: list of stored records
      - calls: list of (func_name, args) for verification
    """
    store = {"records": [], "calls": []}

    def fake_record(domain, rtype, value, source, resolver=None):
        norm = domain.lower().rstrip(".")
        now = int(time.time())
        # Upsert behavior: if exists, bump hits
        for r in store["records"]:
            if r["domain"] == norm and r["rtype"] == rtype and r["value"] == value:
                r["hits"] += 1
                r["last_seen"] = now
                store["calls"].append(("passive_record", (domain, rtype, value, source, resolver)))
                return
        store["records"].append({
            "domain": norm,
            "rtype": rtype,
            "value": value,
            "source": source,
            "resolver": resolver,
            "first_seen": now,
            "last_seen": now,
            "hits": 1,
        })
        store["calls"].append(("passive_record", (domain, rtype, value, source, resolver)))

    def fake_query(domain, limit=200, rtype=None):
        norm = domain.lower().rstrip(".")
        results = [r for r in store["records"] if r["domain"] == norm]
        if rtype:
            results = [r for r in results if r["rtype"] == rtype.upper()]
        store["calls"].append(("passive_query", (domain, limit, rtype)))
        return results[:limit]

    def fake_count():
        store["calls"].append(("passive_count", ()))
        return len(store["records"])

    def fake_domain_exists(domain, rtype, value):
        norm = domain.lower().rstrip(".")
        store["calls"].append(("passive_domain_exists", (domain, rtype, value)))
        return any(
            r["domain"] == norm and r["rtype"] == rtype and r["value"] == value
            for r in store["records"]
        )

    def fake_domain_count_capped(domain, cap):
        norm = domain.lower().rstrip(".")
        count = sum(1 for r in store["records"] if r["domain"] == norm)
        store["calls"].append(("passive_domain_count_capped", (domain, cap)))
        return min(count, cap)

    orig = {
        "passive_record": _db.passive_record,
        "passive_query": _db.passive_query,
        "passive_count": _db.passive_count,
        "passive_domain_exists": _db.passive_domain_exists,
        "passive_domain_count_capped": _db.passive_domain_count_capped,
    }
    _db.passive_record = fake_record
    _db.passive_query = fake_query
    _db.passive_count = fake_count
    _db.passive_domain_exists = fake_domain_exists
    _db.passive_domain_count_capped = fake_domain_count_capped

    yield store

    for k, v in orig.items():
        setattr(_db, k, v)


# ---------------------------------------------------------------------------
# ingest()
# ---------------------------------------------------------------------------

class TestIngest:
    def test_basic_ingest_records_observation(self, passive_store):
        passivedns.ingest("example.com", "A", "93.184.216.34", "investigation")
        assert len(passive_store["records"]) == 1
        r = passive_store["records"][0]
        assert r["domain"] == "example.com"
        assert r["rtype"] == "A"
        assert r["value"] == "93.184.216.34"
        assert r["source"] == "investigation"
        assert r["hits"] == 1

    def test_ingest_skips_empty_domain(self, passive_store):
        passivedns.ingest("", "A", "1.2.3.4", "test")
        assert len(passive_store["records"]) == 0

    def test_ingest_skips_empty_value(self, passive_store):
        passivedns.ingest("example.com", "A", "", "test")
        assert len(passive_store["records"]) == 0

    def test_ingest_with_resolver(self, passive_store):
        passivedns.ingest("example.com", "A", "1.2.3.4", "leak", resolver="8.8.8.8")
        assert passive_store["records"][0]["resolver"] == "8.8.8.8"

    def test_repeat_ingest_bumps_hits(self, passive_store):
        passivedns.ingest("example.com", "A", "1.2.3.4", "test")
        passivedns.ingest("example.com", "A", "1.2.3.4", "test")
        assert len(passive_store["records"]) == 1
        assert passive_store["records"][0]["hits"] == 2

    def test_different_values_create_separate_records(self, passive_store):
        passivedns.ingest("example.com", "A", "1.2.3.4", "test")
        passivedns.ingest("example.com", "A", "5.6.7.8", "test")
        assert len(passive_store["records"]) == 2

    def test_per_domain_cap_blocks_new_distinct_values(self, passive_store, monkeypatch):
        monkeypatch.setattr(passivedns, "_PASSIVE_PER_DOMAIN_MAX", 2)
        passivedns.ingest("capped.com", "A", "1.1.1.1", "test")
        passivedns.ingest("capped.com", "A", "2.2.2.2", "test")
        passivedns.ingest("capped.com", "A", "3.3.3.3", "test")  # should be dropped
        assert len(passive_store["records"]) == 2

    def test_per_domain_cap_allows_existing_value_update(self, passive_store, monkeypatch):
        monkeypatch.setattr(passivedns, "_PASSIVE_PER_DOMAIN_MAX", 1)
        passivedns.ingest("capped.com", "A", "1.1.1.1", "test")
        passivedns.ingest("capped.com", "A", "1.1.1.1", "test")  # existing, should upsert
        assert len(passive_store["records"]) == 1
        assert passive_store["records"][0]["hits"] == 2

    def test_trailing_dot_normalized(self, passive_store):
        passivedns.ingest("example.com.", "A", "1.2.3.4", "test")
        assert passive_store["records"][0]["domain"] == "example.com"


# ---------------------------------------------------------------------------
# query()
# ---------------------------------------------------------------------------

class TestQuery:
    def test_query_returns_domain_and_count(self, passive_store):
        passivedns.ingest("example.com", "A", "1.2.3.4", "test")
        result = passivedns.query("example.com")
        assert result["domain"] == "example.com"
        assert result["count"] == 1
        assert len(result["records"]) == 1

    def test_query_strips_resolver_from_response(self, passive_store):
        passivedns.ingest("example.com", "A", "1.2.3.4", "leak", resolver="8.8.8.8")
        result = passivedns.query("example.com")
        for r in result["records"]:
            assert "resolver" not in r

    def test_query_normalizes_domain_case(self, passive_store):
        passivedns.ingest("Example.COM", "A", "1.2.3.4", "test")
        result = passivedns.query("example.com")
        assert result["count"] == 1

    def test_query_strips_trailing_dot(self, passive_store):
        passivedns.ingest("example.com", "A", "1.2.3.4", "test")
        result = passivedns.query("example.com.")
        assert result["domain"] == "example.com"

    def test_query_empty_domain_returns_zero(self, passive_store):
        result = passivedns.query("nobody.example")
        assert result["count"] == 0
        assert result["records"] == []


# ---------------------------------------------------------------------------
# total_records()
# ---------------------------------------------------------------------------

class TestTotalRecords:
    def test_delegates_to_db_count(self, passive_store):
        passivedns.ingest("a.com", "A", "1.1.1.1", "test")
        passivedns.ingest("b.com", "A", "2.2.2.2", "test")
        assert passivedns.total_records() == 2


# ---------------------------------------------------------------------------
# baseline() — drift detection
# ---------------------------------------------------------------------------

class TestBaseline:
    def test_no_records_returns_insufficient_data(self, passive_store):
        result = passivedns.baseline("unknown.com")
        assert result["drift"] == "insufficient-data"
        assert result["recordCount"] == 0

    def test_insufficient_data_when_no_established_values(self, passive_store):
        """All records are brand-new (< 7 days old) → insufficient-data."""
        now = int(time.time())
        # Manually inject records that are too new
        passive_store["records"].append({
            "domain": "new.com", "rtype": "A", "value": "1.1.1.1",
            "source": "test", "resolver": None,
            "first_seen": now - 86400,  # 1 day ago
            "last_seen": now,
            "hits": 5,
        })
        result = passivedns.baseline("new.com", now=now)
        assert result["drift"] == "insufficient-data"

    def test_no_drift_when_all_established(self, passive_store):
        """All records are old and no new values appeared → drift = 'none'."""
        now = int(time.time())
        passive_store["records"].append({
            "domain": "stable.com", "rtype": "A", "value": "1.1.1.1",
            "source": "test", "resolver": None,
            "first_seen": now - 30 * 86400,  # 30 days ago
            "last_seen": now - 1 * 86400,     # seen yesterday
            "hits": 100,
        })
        result = passivedns.baseline("stable.com", now=now)
        assert result["drift"] == "none"
        assert result["established"] == ["1.1.1.1"]

    def test_drift_suspected_when_new_value_appears(self, passive_store):
        """An established value coexists with a brand-new value → drift = 'suspected'."""
        now = int(time.time())
        passive_store["records"].extend([
            {
                "domain": "drifting.com", "rtype": "A", "value": "1.1.1.1",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 1 * 86400,
                "hits": 100,
            },
            {
                "domain": "drifting.com", "rtype": "A", "value": "9.9.9.9",
                "source": "test", "resolver": None,
                "first_seen": now - 3600,  # 1 hour ago
                "last_seen": now - 600,    # 10 min ago
                "hits": 2,
            },
        ])
        result = passivedns.baseline("drifting.com", now=now)
        assert result["drift"] == "suspected"
        assert "9.9.9.9" in result["recentlyNew"]

    def test_domain_normalized_in_response(self, passive_store):
        result = passivedns.baseline("UPPER.COM.")
        assert result["domain"] == "upper.com"

    def test_rtype_filter_passed_through(self, passive_store):
        now = int(time.time())
        passive_store["records"].extend([
            {
                "domain": "dual.com", "rtype": "A", "value": "1.1.1.1",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 86400,
                "hits": 50,
            },
            {
                "domain": "dual.com", "rtype": "AAAA", "value": "::1",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 86400,
                "hits": 50,
            },
        ])
        result = passivedns.baseline("dual.com", rtype="A", now=now)
        assert result["type"] == "A"
        # Only A records should appear in values
        assert all(v["rtype"] == "A" for v in result["values"])

    def test_truncation_returns_insufficient_data(self, passive_store, monkeypatch):
        """When records exceed the baseline limit, drift must be 'insufficient-data'."""
        now = int(time.time())
        # Inject 502 records (limit is 500 + 1 for truncation detection)
        for i in range(502):
            passive_store["records"].append({
                "domain": "huge.com", "rtype": "A", "value": f"1.2.3.{i % 256}",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 86400,
                "hits": 1,
            })
        result = passivedns.baseline("huge.com", now=now)
        assert result["drift"] == "insufficient-data"
        assert result["truncated"] is True
        assert result["truncationReason"] == "server-limit"

    def test_observation_days_calculated(self, passive_store):
        now = int(time.time())
        passive_store["records"].append({
            "domain": "aged.com", "rtype": "A", "value": "1.1.1.1",
            "source": "test", "resolver": None,
            "first_seen": now - 14 * 86400,
            "last_seen": now - 86400,
            "hits": 10,
        })
        result = passivedns.baseline("aged.com", now=now)
        assert result["observationDays"] == pytest.approx(13.0, abs=0.5)

    def test_a_and_aaaa_compared_independently(self, passive_store):
        """A040: key by (rtype, value) so A and AAAA populations don't cross-contaminate.

        When both A and AAAA are established (old) and no new values appear,
        drift must be 'none' — the rtype key prevents the AAAA from being
        confused with a 'new' A value."""
        now = int(time.time())
        passive_store["records"].extend([
            {
                "domain": "dual.com", "rtype": "A", "value": "1.1.1.1",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 86400,
                "hits": 50,
            },
            {
                "domain": "dual.com", "rtype": "AAAA", "value": "2606:2800:220:1::",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 86400,
                "hits": 50,
            },
        ])
        result = passivedns.baseline("dual.com", now=now)
        # Both A and AAAA are established, neither is new → no drift.
        assert result["drift"] == "none"
        assert len(result["established"]) == 2

    def test_new_aaaa_alongside_established_a_is_drift(self, passive_store):
        """A brand-new AAAA value alongside an established A is a drift signal —
        the rtype key means they're compared independently, but a genuinely new
        observation is still flagged."""
        now = int(time.time())
        passive_store["records"].extend([
            {
                "domain": "new6.com", "rtype": "A", "value": "1.1.1.1",
                "source": "test", "resolver": None,
                "first_seen": now - 30 * 86400,
                "last_seen": now - 86400,
                "hits": 50,
            },
            {
                "domain": "new6.com", "rtype": "AAAA", "value": "2606:2800:220:1::",
                "source": "test", "resolver": None,
                "first_seen": now - 3600,  # brand new
                "last_seen": now - 600,
                "hits": 2,
            },
        ])
        result = passivedns.baseline("new6.com", now=now)
        assert result["drift"] == "suspected"
        assert "2606:2800:220:1::" in result["recentlyNew"]

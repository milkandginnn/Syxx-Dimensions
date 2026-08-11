"""Unit tests for the 5 repository (data store) modules.

Covers: kv_repo, leak_repo, license_repo, monitor_repo, beta_repo.
Each test gets a clean schema via the shared conftest temp DB.
"""

import hashlib
import time

import pytest

from syxx_server import db
from syxx_server import kv_repo, leak_repo, license_repo, monitor_repo, beta_repo


@pytest.fixture(autouse=True)
def _init_db():
    """Ensure the shared test DB has the schema, then clean all repo tables."""
    db._initialized = False
    db.init()
    yield
    # Truncate all repo tables so tests are isolated
    conn = db.connect()
    try:
        for table in (
            "kv", "leak_tokens", "leak_observations", "passive_dns",
            "licenses", "keygen_licenses", "api_keys",
            "monitors", "monitor_results", "warp_cache", "beta_signups",
        ):
            conn.execute(f"DELETE FROM {table}")
        conn.commit()
    finally:
        db._return_conn(conn)
    db.clear_pool()


# =========================================================================
# kv_repo tests
# =========================================================================

class TestKvRepo:
    def test_kv_set_and_get(self):
        kv_repo.kv_set("color", "blue")
        assert kv_repo.kv_get("color") == "blue"

    def test_kv_get_default(self):
        assert kv_repo.kv_get("missing") is None
        assert kv_repo.kv_get("missing", "fallback") == "fallback"

    def test_kv_set_overwrite_preserves_created_at(self):
        kv_repo.kv_set("k1", "v1")
        _, ts1 = kv_repo.kv_get_with_created_at("k1")
        time.sleep(1.1)
        kv_repo.kv_set("k1", "v2")
        val, ts2 = kv_repo.kv_get_with_created_at("k1")
        assert val == "v2"
        assert ts2 == ts1  # created_at preserved on update

    def test_kv_get_with_created_at_missing(self):
        val, ts = kv_repo.kv_get_with_created_at("nope")
        assert val is None
        assert ts is None

    def test_kv_insert_once_first_call(self):
        assert kv_repo.kv_insert_once("lock:1", "reserved") is True
        assert kv_repo.kv_get("lock:1") == "reserved"

    def test_kv_insert_once_second_call(self):
        kv_repo.kv_insert_once("lock:1", "reserved")
        assert kv_repo.kv_insert_once("lock:1", "other") is False
        assert kv_repo.kv_get("lock:1") == "reserved"  # unchanged

    def test_kv_find_by_value_prefix_match(self):
        kv_repo.kv_set("stripe_cust:abc", "cus_12345")
        result = kv_repo.kv_find_by_value_prefix("stripe_cust:", "cus_12345")
        assert result == "abc"

    def test_kv_find_by_value_prefix_no_match(self):
        kv_repo.kv_set("stripe_cust:abc", "cus_12345")
        assert kv_repo.kv_find_by_value_prefix("stripe_cust:", "cus_99999") is None

    def test_kv_find_by_value_prefix_empty_needle(self):
        kv_repo.kv_set("stripe_cust:abc", "cus_12345")
        assert kv_repo.kv_find_by_value_prefix("stripe_cust:", "") is None

    def test_kv_find_by_value_prefix_exact_not_substring(self):
        """Regression: must be exact equality, not substring match."""
        kv_repo.kv_set("lic:aaa", "abcdef")
        kv_repo.kv_set("lic:bbb", "abc")
        # Looking for "abc" must find lic:bbb, not lic:aaa
        result = kv_repo.kv_find_by_value_prefix("lic:", "abc")
        assert result == "bbb"

    def test_get_or_create_secret_persists(self):
        import os
        os.environ.pop("SYXX_SESSION_SECRET", None)
        s1 = kv_repo.get_or_create_secret()
        s2 = kv_repo.get_or_create_secret()
        assert s1 == s2
        assert len(s1) == 32


# =========================================================================
# leak_repo tests
# =========================================================================

class TestLeakRepo:
    def test_leak_token_create_and_get(self):
        leak_repo.leak_token_create("tok1", "sub1", "zone1", ttl=3600)
        row = leak_repo.leak_token_get("tok1")
        assert row is not None
        assert row["subject"] == "sub1"
        assert row["zone"] == "zone1"

    def test_leak_token_expired(self):
        leak_repo.leak_token_create("tok2", "sub1", "zone1", ttl=1)
        future = int(time.time()) + 100
        assert leak_repo.leak_token_get("tok2", now=future) is None

    def test_leak_token_not_found(self):
        assert leak_repo.leak_token_get("nonexistent") is None

    def test_leak_token_claim_and_release(self):
        leak_repo.leak_token_create("tok3", "sub1", "zone1", ttl=3600)
        assert leak_repo.leak_token_claim("tok3") is True
        # Second claim fails (already claimed)
        assert leak_repo.leak_token_claim("tok3") is False
        # Release allows re-claim
        leak_repo.leak_token_release_claim("tok3")
        assert leak_repo.leak_token_claim("tok3") is True

    def test_leak_token_consume_deletes_all(self):
        leak_repo.leak_token_create("tok4", "sub1", "zone1", ttl=3600)
        leak_repo.leak_observation_add("tok4", "1.2.3.4", 1)
        leak_repo.leak_token_claim("tok4")
        leak_repo.leak_token_consume("tok4")
        assert leak_repo.leak_token_get("tok4") is None
        assert leak_repo.leak_observations("tok4") == []

    def test_leak_observation_add_and_list(self):
        leak_repo.leak_token_create("tok5", "sub1", "zone1", ttl=3600)
        leak_repo.leak_observation_add("tok5", "10.0.0.1", 1)
        leak_repo.leak_observation_add("tok5", "10.0.0.2", 28)
        obs = leak_repo.leak_observations("tok5")
        assert len(obs) == 2
        assert obs[0]["resolver_ip"] == "10.0.0.1"
        assert obs[1]["qtype"] == 28

    def test_leak_observation_dedup(self):
        """Same (token, resolver_ip, qtype) should upsert, not duplicate."""
        leak_repo.leak_observation_add("tok6", "10.0.0.1", 1)
        leak_repo.leak_observation_add("tok6", "10.0.0.1", 1)
        obs = leak_repo.leak_observations("tok6")
        assert len(obs) == 1

    def test_passive_record_and_query(self):
        leak_repo.passive_record("Example.COM.", "A", "1.2.3.4", "dns")
        rows = leak_repo.passive_query("example.com")
        assert len(rows) == 1
        assert rows[0]["domain"] == "example.com"
        assert rows[0]["value"] == "1.2.3.4"

    def test_passive_record_hit_counter(self):
        leak_repo.passive_record("test.com", "A", "5.5.5.5", "dns")
        leak_repo.passive_record("test.com", "A", "5.5.5.5", "dns")
        leak_repo.passive_record("test.com", "A", "5.5.5.5", "dns")
        rows = leak_repo.passive_query("test.com")
        assert rows[0]["hits"] == 3

    def test_passive_query_rtype_filter(self):
        leak_repo.passive_record("multi.com", "A", "1.1.1.1", "dns")
        leak_repo.passive_record("multi.com", "AAAA", "::1", "dns")
        rows = leak_repo.passive_query("multi.com", rtype="AAAA")
        assert len(rows) == 1
        assert rows[0]["rtype"] == "AAAA"

    def test_passive_count(self):
        assert leak_repo.passive_count() == 0
        leak_repo.passive_record("a.com", "A", "1.1.1.1", "dns")
        leak_repo.passive_record("b.com", "A", "2.2.2.2", "dns")
        assert leak_repo.passive_count() == 2

    def test_passive_domain_exists(self):
        leak_repo.passive_record("exists.com", "A", "9.9.9.9", "dns")
        assert leak_repo.passive_domain_exists("exists.com", "A", "9.9.9.9") is True
        assert leak_repo.passive_domain_exists("exists.com", "A", "0.0.0.0") is False

    def test_passive_domain_count_capped(self):
        for i in range(10):
            leak_repo.passive_record("big.com", "TXT", f"v={i}", "dns")
        assert leak_repo.passive_domain_count_capped("big.com", 5) == 5
        assert leak_repo.passive_domain_count_capped("big.com", 100) == 10


# =========================================================================
# license_repo tests
# =========================================================================

class TestLicenseRepo:
    def test_license_upsert_and_lookup(self):
        license_repo.license_upsert("lic_test1", plan="pro", status="active")
        row = license_repo.license_lookup("lic_test1")
        assert row is not None
        assert row["plan"] == "pro"
        assert row["status"] == "active"

    def test_license_lookup_missing(self):
        assert license_repo.license_lookup("nonexistent") is None

    def test_license_upsert_overwrite(self):
        license_repo.license_upsert("lic_ow", plan="free", status="active")
        license_repo.license_upsert("lic_ow", plan="pro", status="expired")
        row = license_repo.license_lookup("lic_ow")
        assert row["plan"] == "pro"
        assert row["status"] == "expired"

    def test_license_lookup_rejects_unsigned_row(self):
        """A row inserted without HMAC should be rejected by lookup."""
        conn = db._write_conn()
        try:
            conn.execute(
                "INSERT INTO licenses(key,plan,status,created_at) VALUES(?,?,?,?)",
                ("lic_unsigned", "pro", "active", int(time.time())),
            )
            conn.commit()
        finally:
            db._return_conn(conn)
        assert license_repo.license_lookup("lic_unsigned") is None

    def test_keygen_license_id_set_and_get(self):
        license_repo.keygen_license_id_set("lic_sub1", "kg-uuid-123")
        assert license_repo.keygen_license_id_get("lic_sub1") == "kg-uuid-123"

    def test_keygen_license_id_get_missing(self):
        assert license_repo.keygen_license_id_get("nope") is None

    def test_keygen_subject_for_license_id(self):
        license_repo.keygen_license_id_set("lic_sub2", "kg-uuid-456")
        assert license_repo.keygen_subject_for_license_id("kg-uuid-456") == "lic_sub2"

    def test_keygen_subject_for_license_id_empty(self):
        assert license_repo.keygen_subject_for_license_id("") is None
        assert license_repo.keygen_subject_for_license_id(None) is None

    def test_api_key_create_and_lookup(self):
        raw = license_repo.api_key_create("lic_api1", label="test key")
        assert raw.startswith("syxx_")
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        row = license_repo.api_key_lookup(key_hash)
        assert row is not None
        assert row["subject"] == "lic_api1"
        assert row["label"] == "test key"

    def test_api_key_lookup_expired(self):
        raw = license_repo.api_key_create("lic_api2", ttl_days=0)
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        # ttl_days=0 means expires_at = now, which is already expired
        # (expires_at < int(time.time()) check)
        time.sleep(1)
        assert license_repo.api_key_lookup(key_hash) is None

    def test_api_key_touch(self):
        raw = license_repo.api_key_create("lic_api3")
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        row = license_repo.api_key_lookup(key_hash)
        assert row["use_count"] == 0
        license_repo.api_key_touch(row["id"])
        row2 = license_repo.api_key_lookup(key_hash)
        assert row2["use_count"] == 1
        assert row2["last_used"] is not None

    def test_api_key_list(self):
        license_repo.api_key_create("lic_list1", label="k1")
        license_repo.api_key_create("lic_list1", label="k2")
        rows = license_repo.api_key_list("lic_list1")
        assert len(rows) == 2
        labels = {r["label"] for r in rows}
        assert labels == {"k1", "k2"}

    def test_api_key_revoke(self):
        raw = license_repo.api_key_create("lic_rev1")
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        row = license_repo.api_key_lookup(key_hash)
        revoked = license_repo.api_key_revoke(row["id"], "lic_rev1")
        assert revoked == 1
        assert license_repo.api_key_lookup(key_hash) is None

    def test_api_key_revoke_wrong_subject(self):
        raw = license_repo.api_key_create("lic_rev2")
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        row = license_repo.api_key_lookup(key_hash)
        revoked = license_repo.api_key_revoke(row["id"], "wrong_subject")
        assert revoked == 0
        # Key still active
        assert license_repo.api_key_lookup(key_hash) is not None

    def test_api_key_revoke_all_for_subject(self):
        license_repo.api_key_create("lic_revall")
        license_repo.api_key_create("lic_revall")
        license_repo.api_key_create("lic_other")
        count = license_repo.api_key_revoke_all_for_subject("lic_revall")
        assert count == 2
        # Other subject's keys untouched
        other_rows = license_repo.api_key_list("lic_other")
        assert all(r["revoked"] == 0 for r in other_rows)

    def test_api_key_subjects_with_active_keys(self):
        license_repo.api_key_create("lic_active1")
        license_repo.api_key_create("lic_active2")
        subjects = license_repo.api_key_subjects_with_active_keys()
        assert "lic_active1" in subjects
        assert "lic_active2" in subjects


# =========================================================================
# monitor_repo tests
# =========================================================================

class TestMonitorRepo:
    def test_monitor_create_and_list(self):
        monitor_repo.monitor_create("mon1", "sub1", "example.com", "dns,tls")
        monitors = monitor_repo.monitor_list("sub1")
        assert len(monitors) == 1
        assert monitors[0]["id"] == "mon1"
        assert monitors[0]["domain"] == "example.com"
        assert monitors[0]["checks"] == "dns,tls"

    def test_monitor_list_empty(self):
        assert monitor_repo.monitor_list("nobody") == []

    def test_monitors_due(self):
        now = int(time.time())
        monitor_repo.monitor_create("mon_due", "sub1", "a.com", "dns", interval_hours=1)
        # next_run is now + 3600, so not due yet
        assert monitor_repo.monitors_due(now=now) == []
        # Advance time
        assert len(monitor_repo.monitors_due(now=now + 7200)) == 1

    def test_monitor_advance(self):
        now = int(time.time())
        monitor_repo.monitor_create("mon_adv", "sub1", "b.com", "dns", interval_hours=6)
        monitor_repo.monitor_advance("mon_adv", now=now)
        monitors = monitor_repo.monitor_list("sub1")
        m = monitors[0]
        assert m["last_run"] == now
        assert m["next_run"] == now + 6 * 3600

    def test_monitor_advance_nonexistent(self):
        # Should not raise
        monitor_repo.monitor_advance("nonexistent", now=int(time.time()))

    def test_monitor_result_save_and_query(self):
        monitor_repo.monitor_create("mon_res", "sub1", "c.com", "dns")
        monitor_repo.monitor_result_save("mon_res", "dns", "clean", changes=None, raw_json='{"ok":true}')
        monitor_repo.monitor_result_save("mon_res", "tls", "warning", changes="cert expiring")
        results = monitor_repo.monitor_results_for("mon_res")
        assert len(results) == 2

    def test_monitor_results_filter_by_check_type(self):
        monitor_repo.monitor_create("mon_filt", "sub1", "d.com", "dns,tls")
        monitor_repo.monitor_result_save("mon_filt", "dns", "clean")
        monitor_repo.monitor_result_save("mon_filt", "tls", "ok")
        monitor_repo.monitor_result_save("mon_filt", "dns", "dirty")
        dns_only = monitor_repo.monitor_results_for("mon_filt", check_type="dns")
        assert len(dns_only) == 2
        assert all(r["check_type"] == "dns" for r in dns_only)

    def test_monitor_results_limit(self):
        monitor_repo.monitor_create("mon_lim", "sub1", "e.com", "dns")
        for i in range(15):
            monitor_repo.monitor_result_save("mon_lim", "dns", f"result-{i}")
        results = monitor_repo.monitor_results_for("mon_lim", limit=5)
        assert len(results) == 5

    def test_monitor_delete(self):
        monitor_repo.monitor_create("mon_del", "sub1", "f.com", "dns")
        monitor_repo.monitor_result_save("mon_del", "dns", "ok")
        deleted = monitor_repo.monitor_delete("mon_del", "sub1")
        assert deleted == 1
        assert monitor_repo.monitor_list("sub1") == []
        assert monitor_repo.monitor_results_for("mon_del") == []

    def test_monitor_delete_wrong_subject(self):
        monitor_repo.monitor_create("mon_own", "sub1", "g.com", "dns")
        deleted = monitor_repo.monitor_delete("mon_own", "sub2")
        assert deleted == 0
        assert len(monitor_repo.monitor_list("sub1")) == 1

    def test_warp_cache_put_and_get(self):
        monitor_repo.warp_cache_put("https://example.com", "<html>cached</html>")
        payload = monitor_repo.warp_cache_get("https://example.com", ttl=3600)
        assert payload == "<html>cached</html>"

    def test_warp_cache_expired(self):
        monitor_repo.warp_cache_put("https://old.com", "stale")
        time.sleep(1.1)
        assert monitor_repo.warp_cache_get("https://old.com", ttl=0) is None

    def test_warp_cache_miss(self):
        assert monitor_repo.warp_cache_get("https://nope.com", ttl=3600) is None


# =========================================================================
# beta_repo tests
# =========================================================================

class TestBetaRepo:
    def test_beta_reserve_and_all(self):
        beta_repo.beta_reserve("alice@test.com", "nnyx,zzyn", ip="1.2.3.4")
        rows = beta_repo.beta_all()
        assert len(rows) == 1
        assert rows[0]["email"] == "alice@test.com"
        assert rows[0]["interests"] == "nnyx,zzyn"
        assert rows[0]["ip"] == "1.2.3.4"

    def test_beta_all_empty(self):
        assert beta_repo.beta_all() == []

    def test_beta_reserve_idempotent(self):
        beta_repo.beta_reserve("bob@test.com", "nnyx", ip="1.1.1.1")
        beta_repo.beta_reserve("bob@test.com", "nnyx,zzyn", ip="2.2.2.2")
        rows = beta_repo.beta_all()
        assert len(rows) == 1
        assert rows[0]["interests"] == "nnyx,zzyn"
        assert rows[0]["ip"] == "2.2.2.2"

    def test_beta_reserve_interests_too_long(self):
        with pytest.raises(ValueError, match="interests field too long"):
            beta_repo.beta_reserve("x@test.com", "a" * 501)

    def test_beta_all_ordered_by_created_at(self):
        beta_repo.beta_reserve("first@test.com", "nnyx")
        time.sleep(1.1)
        beta_repo.beta_reserve("second@test.com", "zzyn")
        rows = beta_repo.beta_all()
        assert rows[0]["email"] == "first@test.com"
        assert rows[1]["email"] == "second@test.com"

    def test_beta_reserve_no_ip(self):
        beta_repo.beta_reserve("anon@test.com", "nnyx")
        rows = beta_repo.beta_all()
        assert rows[0]["ip"] is None

    def test_beta_reserve_updated_at_changes(self):
        beta_repo.beta_reserve("upd@test.com", "nnyx")
        rows1 = beta_repo.beta_all()
        created = rows1[0]["created_at"]
        updated1 = rows1[0]["updated_at"]
        time.sleep(1.1)
        beta_repo.beta_reserve("upd@test.com", "nnyx,zzyn")
        rows2 = beta_repo.beta_all()
        # created_at should be updated by the upsert (both set to now)
        assert rows2[0]["updated_at"] >= updated1

    def test_beta_multiple_distinct_emails(self):
        beta_repo.beta_reserve("a@test.com", "nnyx")
        beta_repo.beta_reserve("b@test.com", "zzyn")
        beta_repo.beta_reserve("c@test.com", "nnyx,zzyn")
        rows = beta_repo.beta_all()
        assert len(rows) == 3
        emails = {r["email"] for r in rows}
        assert emails == {"a@test.com", "b@test.com", "c@test.com"}

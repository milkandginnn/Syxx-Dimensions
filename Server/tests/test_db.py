"""Unit tests for syxx_server/db.py — the SQLite persistence layer.

Covers: schema initialization, connection pool, audit logging, beta signups,
purge_expired, backup (including shutdown cancellation), _cap_rows,
_validate_env_secret, KV store, monitors, leak tokens, API keys, passive DNS,
trial anchors, and row-cap retention.

Uses the shared temp DB from conftest.py. No network calls, no auth prompts.
"""

import os
import sqlite3
import tempfile
import threading
import time
from unittest.mock import MagicMock, patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

from syxx_server import config, db  # noqa: E402


# ---------------------------------------------------------------------------
# Fixture: isolated temp DB per test (overrides the shared conftest DB path)
# ---------------------------------------------------------------------------

@pytest.fixture
def _temp_db():
    """Create a fresh temp DB with schema for each test."""
    tmp = tempfile.mkdtemp()
    orig_path = config.DB_PATH
    config.DB_PATH = os.path.join(tmp, "syxx-test.db")
    db.clear_pool()
    db._initialized = False
    conn = db.connect()
    try:
        conn.executescript(db.SCHEMA)
        conn.commit()
        db._migrate(conn)  # creates unique indexes needed by ON CONFLICT clauses
    finally:
        db._return_conn(conn)
    yield tmp
    config.DB_PATH = orig_path
    db.clear_pool()
    db._initialized = False


# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------

def _conn():
    """Get a connection from the pool."""
    return db.connect()


def _count(table):
    """Count rows in a table."""
    conn = _conn()
    try:
        row = conn.execute(f"SELECT COUNT(*) c FROM {table}").fetchone()
        return row["c"]
    finally:
        db._return_conn(conn)


# =========================================================================
# Schema initialization
# =========================================================================


class TestSchemaInit:
    def test_init_creates_all_tables(self, _temp_db):
        db.init()
        conn = _conn()
        try:
            tables = {
                r["name"]
                for r in conn.execute(
                    "SELECT name FROM sqlite_master WHERE type='table'"
                ).fetchall()
            }
        finally:
            db._return_conn(conn)
        expected = {
            "kv", "licenses", "keygen_licenses", "leak_tokens",
            "leak_observations", "passive_dns", "warp_cache", "audit",
            "beta_signups", "trial_anchors", "api_keys", "monitors",
            "monitor_results",
        }
        assert expected.issubset(tables)

    def test_init_is_idempotent(self, _temp_db):
        db.init()
        db.init()  # second call must not raise

    def test_init_sets_file_permissions(self, _temp_db):
        db.init()
        mode = os.stat(config.DB_PATH).st_mode & 0o777
        assert mode == 0o600


# =========================================================================
# Connection pool
# =========================================================================


class TestConnectionPool:
    def test_connect_returns_usable_connection(self, _temp_db):
        conn = db.connect()
        try:
            result = conn.execute("SELECT 1 AS v").fetchone()
            assert result["v"] == 1
        finally:
            db._return_conn(conn)

    def test_return_and_reuse(self, _temp_db):
        conn1 = db.connect()
        db._return_conn(conn1)
        conn2 = db.connect()
        # Returned connection should be usable (pool may or may not recycle the same object
        # depending on fixture ordering, but it must be functional)
        result = conn2.execute("SELECT 1 AS v").fetchone()
        assert result["v"] == 1
        db._return_conn(conn2)

    def test_clear_pool_closes_connections(self, _temp_db):
        conns = [db.connect() for _ in range(3)]
        for c in conns:
            db._return_conn(c)
        db.clear_pool()
        # After clearing, a new connect() should give a different connection
        new_conn = db.connect()
        assert new_conn not in conns
        db._return_conn(new_conn)

    def test_pool_respects_max_pooled(self, _temp_db):
        """Excess connections beyond _MAX_POOLED are closed, not pooled."""
        conns = [db.connect() for _ in range(db._MAX_POOLED + 2)]
        for c in conns:
            db._return_conn(c)
        pool = getattr(db._thread_local, "pool", [])
        assert len(pool) <= db._MAX_POOLED

# =========================================================================
# Audit logging
# =========================================================================


class TestAudit:
    def test_audit_inserts_row(self, _temp_db):
        db.audit("user-1", "/api/test", 200, "1.2.3.4")
        conn = _conn()
        try:
            row = conn.execute("SELECT * FROM audit WHERE subject='user-1'").fetchone()
        finally:
            db._return_conn(conn)
        assert row is not None
        assert row["route"] == "/api/test"
        assert row["status"] == 200
        assert row["ip"] == "1.2.3.4"

    def test_audit_multiple_entries(self, _temp_db):
        for i in range(5):
            db.audit(f"u-{i}", f"/route/{i}", 200 + i, "10.0.0.1")
        assert _count("audit") >= 5


# =========================================================================
# Beta signups
# =========================================================================


class TestBetaSignups:
    def test_beta_reserve_insert(self, _temp_db):
        db.beta_reserve("user@example.com", "macOS,Windows", "1.2.3.4")
        all_signups = db.beta_all()
        assert len(all_signups) >= 1
        assert any(s["email"] == "user@example.com" for s in all_signups)

    def test_beta_reserve_upsert(self, _temp_db):
        db.beta_reserve("user@example.com", "macOS", "1.2.3.4")
        db.beta_reserve("user@example.com", "macOS,Windows", "5.6.7.8")
        all_signups = db.beta_all()
        matches = [s for s in all_signups if s["email"] == "user@example.com"]
        assert len(matches) == 1
        assert matches[0]["interests"] == "macOS,Windows"

    def test_beta_reserve_interests_too_long(self, _temp_db):
        with pytest.raises(ValueError, match="too long"):
            db.beta_reserve("user@example.com", "x" * 501)

    def test_beta_all_ordered_by_created_at(self, _temp_db):
        db.beta_reserve("a@test.com", "linux")
        db.beta_reserve("b@test.com", "mac")
        all_signups = db.beta_all()
        timestamps = [s["created_at"] for s in all_signups]
        assert timestamps == sorted(timestamps)


# =========================================================================
# Purge expired
# =========================================================================


class TestPurgeExpired:
    def test_purge_removes_expired_leak_tokens(self, _temp_db):
        now = int(time.time())
        conn = _conn()
        try:
            conn.execute(
                "INSERT INTO leak_tokens VALUES(?,?,?,?,?,?)",
                ("tok-old", "sub", "leak.syxx.app", now - 7200, now - 3600, "ip"),
            )
            conn.execute(
                "INSERT INTO leak_tokens VALUES(?,?,?,?,?,?)",
                ("tok-new", "sub", "leak.syxx.app", now, now + 3600, "ip"),
            )
            conn.commit()
        finally:
            db._return_conn(conn)

        db.purge_expired(now)

        conn = _conn()
        try:
            tokens = {r["token"] for r in conn.execute("SELECT token FROM leak_tokens")}
        finally:
            db._return_conn(conn)
        assert "tok-new" in tokens
        assert "tok-old" not in tokens

    def test_purge_removes_orphan_observations(self, _temp_db):
        now = int(time.time())
        conn = _conn()
        try:
            conn.execute(
                "INSERT INTO leak_observations(token,resolver_ip,qtype,observed_at) VALUES(?,?,?,?)",
                ("tok-orphan", "8.8.8.8", 1, now),
            )
            conn.commit()
        finally:
            db._return_conn(conn)

        db.purge_expired(now)
        assert _count("leak_observations") == 0

    def test_purge_removes_expired_audit(self, _temp_db):
        now = int(time.time())
        old_ts = now - (config.AUDIT_RETENTION_DAYS + 1) * 86400
        conn = _conn()
        try:
            conn.execute(
                "INSERT INTO audit(ts,subject,route,status,ip) VALUES(?,?,?,?,?)",
                (old_ts, "old", "/old", 200, "1.2.3.4"),
            )
            conn.execute(
                "INSERT INTO audit(ts,subject,route,status,ip) VALUES(?,?,?,?,?)",
                (now, "new", "/new", 200, "1.2.3.4"),
            )
            conn.commit()
        finally:
            db._return_conn(conn)

        db.purge_expired(now)

        conn = _conn()
        try:
            subjects = {r["subject"] for r in conn.execute("SELECT subject FROM audit")}
        finally:
            db._return_conn(conn)
        assert "new" in subjects
        assert "old" not in subjects

    def test_purge_removes_leak_collector_passive_dns(self, _temp_db):
        """passive_dns rows with source='leak-collector' are always purged."""
        now = int(time.time())
        conn = _conn()
        try:
            conn.execute(
                "INSERT INTO passive_dns(domain,rtype,value,source,resolver,first_seen,last_seen,hits) "
                "VALUES(?,?,?,?,?,?,?,1)",
                ("leaked.com", "A", "1.2.3.4", "leak-collector", "8.8.8.8", now, now),
            )
            conn.execute(
                "INSERT INTO passive_dns(domain,rtype,value,source,resolver,first_seen,last_seen,hits) "
                "VALUES(?,?,?,?,?,?,?,1)",
                ("normal.com", "A", "5.6.7.8", "investigation", "8.8.4.4", now, now),
            )
            conn.commit()
        finally:
            db._return_conn(conn)

        db.purge_expired(now)

        conn = _conn()
        try:
            domains = {r["domain"] for r in conn.execute("SELECT domain FROM passive_dns")}
        finally:
            db._return_conn(conn)
        assert "normal.com" in domains
        assert "leaked.com" not in domains


# =========================================================================
# _cap_rows
# =========================================================================


class TestCapRows:
    def test_cap_rows_removes_oldest(self, _temp_db):
        conn = _conn()
        try:
            now = int(time.time())
            for i in range(10):
                conn.execute(
                    "INSERT INTO audit(ts,subject,route,status,ip) VALUES(?,?,?,?,?)",
                    (now - 100 + i, f"s-{i}", "/r", 200, "1.2.3.4"),
                )
            conn.commit()
            db._cap_rows(conn, "audit", "ts", 5)
            conn.commit()
            count = conn.execute("SELECT COUNT(*) c FROM audit").fetchone()["c"]
            assert count == 5
            # The 5 newest should remain
            oldest = conn.execute("SELECT MIN(ts) FROM audit").fetchone()[0]
            assert oldest >= now - 100 + 5
        finally:
            db._return_conn(conn)

    def test_cap_rows_zero_disables(self, _temp_db):
        conn = _conn()
        try:
            now = int(time.time())
            for i in range(5):
                conn.execute(
                    "INSERT INTO audit(ts,subject,route,status,ip) VALUES(?,?,?,?,?)",
                    (now + i, f"s-{i}", "/r", 200, "1.2.3.4"),
                )
            conn.commit()
            db._cap_rows(conn, "audit", "ts", 0)
            count = conn.execute("SELECT COUNT(*) c FROM audit").fetchone()["c"]
            assert count == 5
        finally:
            db._return_conn(conn)

    def test_cap_rows_rejects_unknown_table(self, _temp_db):
        conn = _conn()
        try:
            with pytest.raises(AssertionError, match="unexpected table"):
                db._cap_rows(conn, "users", "id", 100)
        finally:
            db._return_conn(conn)


# =========================================================================
# _validate_env_secret
# =========================================================================


class TestValidateEnvSecret:
    def test_valid_secret_accepted(self, _temp_db):
        import secrets
        good_secret = secrets.token_hex(32)
        result = db._validate_env_secret(good_secret)
        assert isinstance(result, bytes)
        assert len(result) == 64  # hex string → bytes

    def test_short_secret_rejected(self, _temp_db):
        with pytest.raises(RuntimeError, match="too short"):
            db._validate_env_secret("short")

    def test_low_entropy_secret_rejected(self, _temp_db):
        # 32 bytes but only 1 distinct byte value
        with pytest.raises(RuntimeError, match="too short|low-entropy"):
            db._validate_env_secret("a" * 32)


# =========================================================================
# Backup
# =========================================================================


class TestBackup:
    def test_backup_creates_restorable_file(self, _temp_db):
        # Seed some data
        conn = _conn()
        try:
            conn.execute("INSERT INTO kv(key,value,created_at) VALUES(?,?,?)", ("test-key", "test-val", 1))
            conn.commit()
        finally:
            db._return_conn(conn)

        bdir = os.path.join(_temp_db, "backups")
        out = db.backup(dest_dir=bdir, keep=0, stamp="20260811T000000Z")
        assert os.path.exists(out)

        # Verify the backup is restorable
        bconn = sqlite3.connect(out)
        try:
            row = bconn.execute("SELECT value FROM kv WHERE key='test-key'").fetchone()
            assert row[0] == "test-val"
        finally:
            bconn.close()

    def test_backup_no_partial_left_behind(self, _temp_db):
        bdir = os.path.join(_temp_db, "backups")
        db.backup(dest_dir=bdir, keep=0, stamp="20260811T000001Z")
        leftovers = [f for f in os.listdir(bdir) if f.endswith(".partial")]
        assert leftovers == []

    def test_backup_rotation(self, _temp_db):
        bdir = os.path.join(_temp_db, "backups")
        for i in range(5):
            db.backup(dest_dir=bdir, keep=3, stamp=f"20260811T00000{i}Z")
        kept = sorted(f for f in os.listdir(bdir) if f.endswith(".db"))
        assert len(kept) == 3

    def test_backup_cancelled_by_shutdown_event(self, _temp_db):
        """A075: backup must not start when shutdown is signaled."""
        shutdown = threading.Event()
        shutdown.set()
        result = db.backup(dest_dir=os.path.join(_temp_db, "backups"), shutdown_event=shutdown)
        assert result is None


# =========================================================================
# KV store (re-exported from kv_repo)
# =========================================================================


class TestKVStore:
    def test_kv_set_and_get(self, _temp_db):
        db.kv_set("my-key", "my-value")
        assert db.kv_get("my-key") == "my-value"

    def test_kv_get_default(self, _temp_db):
        assert db.kv_get("nonexistent", "default") == "default"

    def test_kv_set_upsert_preserves_created_at(self, _temp_db):
        db.kv_set("ts-key", "v1")
        conn = _conn()
        try:
            row1 = conn.execute("SELECT created_at FROM kv WHERE key='ts-key'").fetchone()
        finally:
            db._return_conn(conn)
        time.sleep(1)
        db.kv_set("ts-key", "v2")
        conn = _conn()
        try:
            row2 = conn.execute("SELECT created_at, value FROM kv WHERE key='ts-key'").fetchone()
        finally:
            db._return_conn(conn)
        assert row2["value"] == "v2"
        assert row2["created_at"] == row1["created_at"]

    def test_kv_insert_once_returns_true_first_time(self, _temp_db):
        assert db.kv_insert_once("once-key", "val") is True

    def test_kv_insert_once_returns_false_second_time(self, _temp_db):
        db.kv_insert_once("once-key", "val")
        assert db.kv_insert_once("once-key", "val2") is False

    def test_kv_get_with_created_at(self, _temp_db):
        db.kv_set("dated-key", "dated-val")
        value, created_at = db.kv_get_with_created_at("dated-key")
        assert value == "dated-val"
        assert created_at is not None

    def test_kv_get_with_created_at_missing(self, _temp_db):
        value, created_at = db.kv_get_with_created_at("missing-key")
        assert value is None
        assert created_at is None

    def test_kv_find_by_value_prefix(self, _temp_db):
        db.kv_set("billing:abc-123", "customer-xyz")
        result = db.kv_find_by_value_prefix("billing:", "customer-xyz")
        assert result == "abc-123"

    def test_kv_find_by_value_prefix_no_match(self, _temp_db):
        db.kv_set("billing:abc-123", "customer-xyz")
        result = db.kv_find_by_value_prefix("billing:", "other-customer")
        assert result is None

    def test_kv_find_by_value_prefix_empty_value(self, _temp_db):
        result = db.kv_find_by_value_prefix("billing:", "")
        assert result is None


# =========================================================================
# Monitors (re-exported from monitor_repo)
# =========================================================================


class TestMonitors:
    def test_monitor_create_and_list(self, _temp_db):
        db.monitor_create("mon-1", "user-1", "example.com", "dns", interval_hours=12)
        monitors = db.monitor_list("user-1")
        assert len(monitors) == 1
        assert monitors[0]["id"] == "mon-1"
        assert monitors[0]["domain"] == "example.com"
        assert monitors[0]["interval_hours"] == 12

    def test_monitor_advance_updates_next_run(self, _temp_db):
        db.monitor_create("mon-2", "user-1", "example.com", "dns", interval_hours=2)
        now = 1_700_000_000
        db.monitor_advance("mon-2", now=now)
        conn = _conn()
        try:
            row = conn.execute("SELECT last_run, next_run FROM monitors WHERE id='mon-2'").fetchone()
        finally:
            db._return_conn(conn)
        assert row["last_run"] == now
        assert row["next_run"] == now + 2 * 3600

    def test_monitor_advance_missing_id_no_error(self, _temp_db):
        # Should not raise
        db.monitor_advance("nonexistent-id", now=1_700_000_000)

    def test_monitors_due(self, _temp_db):
        db.monitor_create("mon-due", "user-1", "example.com", "dns", interval_hours=0)
        now = int(time.time()) + 3600
        due = db.monitors_due(now)
        assert any(m["id"] == "mon-due" for m in due)

    def test_monitor_delete(self, _temp_db):
        db.monitor_create("mon-del", "user-1", "example.com", "dns")
        deleted = db.monitor_delete("mon-del", "user-1")
        assert deleted == 1
        assert db.monitor_list("user-1") == []

    def test_monitor_delete_wrong_subject(self, _temp_db):
        db.monitor_create("mon-own", "user-1", "example.com", "dns")
        deleted = db.monitor_delete("mon-own", "user-2")
        assert deleted == 0

    def test_monitor_result_save_and_retrieve(self, _temp_db):
        db.monitor_create("mon-res", "user-1", "example.com", "dns")
        db.monitor_result_save("mon-res", "dns", "OK", changes=None, raw_json='{"status":"ok"}')
        results = db.monitor_results_for("mon-res")
        assert len(results) == 1
        assert results[0]["result"] == "OK"


# =========================================================================
# Leak tokens (re-exported from leak_repo)
# =========================================================================


class TestLeakTokens:
    def test_token_create_and_get(self, _temp_db):
        db.leak_token_create("test-token-123", "sub-1", "leak.syxx.app", ttl=3600)
        token = db.leak_token_get("test-token-123")
        assert token is not None
        assert token["subject"] == "sub-1"
        assert token["zone"] == "leak.syxx.app"

    def test_expired_token_returns_none(self, _temp_db):
        db.leak_token_create("expired-token", "sub-1", "leak.syxx.app", ttl=-10)
        assert db.leak_token_get("expired-token") is None

    def test_nonexistent_token_returns_none(self, _temp_db):
        assert db.leak_token_get("does-not-exist") is None

    def test_token_consume_deletes_everything(self, _temp_db):
        db.leak_token_create("consume-me", "sub-1", "leak.syxx.app", ttl=3600)
        db.leak_observation_add("consume-me", "8.8.8.8", 1)
        db.leak_token_consume("consume-me")
        assert db.leak_token_get("consume-me") is None
        assert db.leak_observations("consume-me") == []

    def test_observation_add_and_list(self, _temp_db):
        db.leak_token_create("obs-token", "sub-1", "leak.syxx.app", ttl=3600)
        db.leak_observation_add("obs-token", "8.8.8.8", 1)
        db.leak_observation_add("obs-token", "8.8.4.4", 28)
        obs = db.leak_observations("obs-token")
        assert len(obs) == 2
        ips = {o["resolver_ip"] for o in obs}
        assert ips == {"8.8.8.8", "8.8.4.4"}

    def test_observation_dedup(self, _temp_db):
        """Same (token, resolver_ip, qtype) should not create duplicates."""
        db.leak_token_create("dedup-token", "sub-1", "leak.syxx.app", ttl=3600)
        db.leak_observation_add("dedup-token", "8.8.8.8", 1)
        db.leak_observation_add("dedup-token", "8.8.8.8", 1)
        obs = db.leak_observations("dedup-token")
        assert len(obs) == 1


# =========================================================================
# API keys (re-exported from license_repo)
# =========================================================================


class TestAPIKeys:
    def test_api_key_create_and_lookup(self, _temp_db):
        import hashlib
        raw = db.api_key_create("user-1", label="test key")
        assert raw.startswith("syxx_")
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        found = db.api_key_lookup(key_hash)
        assert found is not None
        assert found["subject"] == "user-1"
        assert found["label"] == "test key"

    def test_api_key_list(self, _temp_db):
        db.api_key_create("user-list", label="k1")
        db.api_key_create("user-list", label="k2")
        keys = db.api_key_list("user-list")
        assert len(keys) >= 2
        labels = {k["label"] for k in keys}
        assert "k1" in labels
        assert "k2" in labels

    def test_api_key_revoke(self, _temp_db):
        raw = db.api_key_create("user-rev", label="revoke-me")
        keys = db.api_key_list("user-rev")
        key_id = keys[0]["id"]
        revoked = db.api_key_revoke(key_id, "user-rev")
        assert revoked == 1
        # After revocation, lookup should return None
        import hashlib
        key_hash = hashlib.sha256(raw.encode()).hexdigest()
        assert db.api_key_lookup(key_hash) is None

    def test_api_key_revoke_wrong_subject(self, _temp_db):
        db.api_key_create("user-a", label="key")
        keys = db.api_key_list("user-a")
        key_id = keys[0]["id"]
        revoked = db.api_key_revoke(key_id, "user-b")
        assert revoked == 0

    def test_api_key_touch(self, _temp_db):
        db.api_key_create("user-touch", label="touch-me")
        keys = db.api_key_list("user-touch")
        key_id = keys[0]["id"]
        db.api_key_touch(key_id)
        conn = _conn()
        try:
            row = conn.execute("SELECT use_count, last_used FROM api_keys WHERE id=?", (key_id,)).fetchone()
        finally:
            db._return_conn(conn)
        assert row["use_count"] == 1
        assert row["last_used"] is not None

    def test_api_key_revoke_all_for_subject(self, _temp_db):
        db.api_key_create("user-all", label="k1")
        db.api_key_create("user-all", label="k2")
        count = db.api_key_revoke_all_for_subject("user-all")
        assert count == 2


# =========================================================================
# Passive DNS (re-exported from leak_repo)
# =========================================================================


class TestPassiveDNS:
    def test_passive_record_and_query(self, _temp_db):
        db.passive_record("Example.COM", "A", "1.2.3.4", "investigation", "8.8.8.8")
        records = db.passive_query("example.com")
        assert len(records) >= 1
        assert records[0]["domain"] == "example.com"
        assert records[0]["value"] == "1.2.3.4"

    def test_passive_normalizes_domain(self, _temp_db):
        """Trailing dot and case should be normalized."""
        db.passive_record("Test.Com.", "A", "5.6.7.8", "test")
        records = db.passive_query("test.com")
        assert len(records) >= 1

    def test_passive_hit_counter(self, _temp_db):
        db.passive_record("hits.com", "A", "1.1.1.1", "test")
        db.passive_record("hits.com", "A", "1.1.1.1", "test")
        db.passive_record("hits.com", "A", "1.1.1.1", "test")
        records = db.passive_query("hits.com")
        assert records[0]["hits"] == 3

    def test_passive_query_with_rtype_filter(self, _temp_db):
        db.passive_record("multi.com", "A", "1.1.1.1", "test")
        db.passive_record("multi.com", "AAAA", "::1", "test")
        a_records = db.passive_query("multi.com", rtype="A")
        assert all(r["rtype"] == "A" for r in a_records)

    def test_passive_count(self, _temp_db):
        db.passive_record("count1.com", "A", "1.1.1.1", "test")
        db.passive_record("count2.com", "A", "2.2.2.2", "test")
        assert db.passive_count() >= 2

    def test_passive_domain_exists(self, _temp_db):
        db.passive_record("exists.com", "A", "3.3.3.3", "test")
        assert db.passive_domain_exists("exists.com", "A", "3.3.3.3") is True
        assert db.passive_domain_exists("exists.com", "A", "9.9.9.9") is False


# =========================================================================
# Trial anchors
# =========================================================================


class TestTrialAnchors:
    def test_get_or_create_new_anchor(self, _temp_db):
        now = int(time.time())
        first_launch, is_new = db.get_or_create_trial_anchor("fp-new", now)
        assert is_new is True
        assert first_launch == now

    def test_get_existing_anchor(self, _temp_db):
        now = int(time.time())
        db.get_or_create_trial_anchor("fp-existing", now)
        later = now + 86400
        first_launch, is_new = db.get_or_create_trial_anchor("fp-existing", later)
        assert is_new is False
        assert first_launch == now  # original timestamp preserved

    def test_anchor_row_cap(self, _temp_db):
        """When TRIAL_ANCHOR_MAX_ROWS is reached, new anchors return None."""
        orig = config.TRIAL_ANCHOR_MAX_ROWS
        try:
            config.TRIAL_ANCHOR_MAX_ROWS = 2
            db.get_or_create_trial_anchor("fp-a", 100)
            db.get_or_create_trial_anchor("fp-b", 200)
            result, is_new = db.get_or_create_trial_anchor("fp-c", 300)
            assert result is None
            assert is_new is False
        finally:
            config.TRIAL_ANCHOR_MAX_ROWS = orig


# =========================================================================
# Warp cache (re-exported from monitor_repo)
# =========================================================================


class TestWarpCache:
    def test_cache_put_and_get(self, _temp_db):
        db.warp_cache_put("https://example.com/page", '{"data": true}')
        result = db.warp_cache_get("https://example.com/page", ttl=3600)
        assert result == '{"data": true}'

    def test_cache_expired_returns_none(self, _temp_db):
        db.warp_cache_put("https://example.com/old", "stale")
        # Manipulate created_at to be old
        conn = _conn()
        try:
            conn.execute(
                "UPDATE warp_cache SET created_at = ? WHERE url = ?",
                (int(time.time()) - 7200, "https://example.com/old"),
            )
            conn.commit()
        finally:
            db._return_conn(conn)
        result = db.warp_cache_get("https://example.com/old", ttl=3600)
        assert result is None

    def test_cache_miss_returns_none(self, _temp_db):
        assert db.warp_cache_get("https://no-such-url.com", ttl=3600) is None

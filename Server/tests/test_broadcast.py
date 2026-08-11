"""Tests for syxx_server.broadcast — message-of-the-day system.

Covers JSON file reading, time-window filtering, severity validation,
URL safety, and field bounding.
"""

import json
import os
import tempfile

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import broadcast, config


@pytest.fixture
def broadcast_file(tmp_path):
    """Provide a temp broadcast file and restore config after test."""
    orig = config.BROADCAST_FILE
    path = tmp_path / "broadcast.json"
    config.BROADCAST_FILE = str(path)
    yield path
    config.BROADCAST_FILE = orig


def _write(path, data):
    path.write_text(json.dumps(data), encoding="utf-8")


class TestCurrent:
    def test_no_file_returns_none(self, broadcast_file):
        assert broadcast.current() is None

    def test_empty_file_returns_none(self, broadcast_file):
        broadcast_file.write_text("", encoding="utf-8")
        assert broadcast.current() is None

    def test_malformed_json_returns_none(self, broadcast_file):
        broadcast_file.write_text("{bad json", encoding="utf-8")
        assert broadcast.current() is None

    def test_missing_id_returns_none(self, broadcast_file):
        _write(broadcast_file, {"title": "no id"})
        assert broadcast.current() is None

    def test_valid_broadcast_returned(self, broadcast_file):
        _write(broadcast_file, {
            "id": "test-001",
            "title": "Test Alert",
            "body": "Something happened.",
            "severity": "warning",
        })
        result = broadcast.current()
        assert result is not None
        assert result["id"] == "test-001"
        assert result["title"] == "Test Alert"
        assert result["body"] == "Something happened."
        assert result["severity"] == "warning"

    def test_future_starts_at_returns_none(self, broadcast_file):
        _write(broadcast_file, {
            "id": "future",
            "title": "Not yet",
            "body": "Pending.",
            "startsAt": 9999999999,
        })
        assert broadcast.current() is None

    def test_past_ends_at_returns_none(self, broadcast_file):
        _write(broadcast_file, {
            "id": "expired",
            "title": "Expired",
            "body": "Done.",
            "endsAt": 1,
        })
        assert broadcast.current() is None

    def test_within_time_window(self, broadcast_file):
        now = 1700000000
        _write(broadcast_file, {
            "id": "windowed",
            "title": "Active",
            "body": "In range.",
            "startsAt": now - 100,
            "endsAt": now + 100,
        })
        result = broadcast.current(now=now)
        assert result is not None
        assert result["id"] == "windowed"


class TestSeverity:
    def test_valid_severities(self, broadcast_file):
        for sev in ("info", "warning", "critical"):
            _write(broadcast_file, {"id": f"sev-{sev}", "title": "T", "body": "B", "severity": sev})
            result = broadcast.current()
            assert result["severity"] == sev

    def test_invalid_severity_coerced_to_info(self, broadcast_file):
        _write(broadcast_file, {"id": "bad-sev", "title": "T", "body": "B", "severity": "urgent"})
        result = broadcast.current()
        assert result["severity"] == "info"


class TestURLSafety:
    def test_http_url_allowed(self, broadcast_file):
        _write(broadcast_file, {"id": "url-ok", "title": "T", "body": "B", "url": "https://syxx.app/status"})
        result = broadcast.current()
        assert result["url"] == "https://syxx.app/status"

    def test_javascript_url_blocked(self, broadcast_file):
        _write(broadcast_file, {"id": "url-js", "title": "T", "body": "B", "url": "javascript:alert(1)"})
        result = broadcast.current()
        assert result["url"] is None

    def test_data_url_blocked(self, broadcast_file):
        _write(broadcast_file, {"id": "url-data", "title": "T", "body": "B", "url": "data:text/html,<h1>xss</h1>"})
        result = broadcast.current()
        assert result["url"] is None

    def test_no_url_returns_none(self, broadcast_file):
        _write(broadcast_file, {"id": "no-url", "title": "T", "body": "B"})
        result = broadcast.current()
        assert result["url"] is None


class TestFieldBounding:
    def test_id_truncated(self, broadcast_file):
        _write(broadcast_file, {"id": "x" * 200, "title": "T", "body": "B"})
        result = broadcast.current()
        assert len(result["id"]) <= 128

    def test_title_truncated(self, broadcast_file):
        _write(broadcast_file, {"id": "long-title", "title": "x" * 300, "body": "B"})
        result = broadcast.current()
        assert len(result["title"]) <= 200

    def test_body_truncated(self, broadcast_file):
        _write(broadcast_file, {"id": "long-body", "title": "T", "body": "x" * 5000})
        result = broadcast.current()
        assert len(result["body"]) <= 4000


class TestVersionRange:
    def test_min_version_passthrough(self, broadcast_file):
        _write(broadcast_file, {"id": "ver", "title": "T", "body": "B", "minVersion": "2.0.0"})
        result = broadcast.current()
        assert result["minVersion"] == "2.0.0"

    def test_max_version_passthrough(self, broadcast_file):
        _write(broadcast_file, {"id": "ver2", "title": "T", "body": "B", "maxVersion": "3.0.0"})
        result = broadcast.current()
        assert result["maxVersion"] == "3.0.0"

    def test_non_string_version_returns_none(self, broadcast_file):
        _write(broadcast_file, {"id": "ver3", "title": "T", "body": "B", "minVersion": 123})
        result = broadcast.current()
        assert result["minVersion"] is None

"""Phase 3.2 — authenticated G-1 appcast foundations.

Covers:
  - dedicated update-signing key (never collector)
  - publish_package atomic per-platform metadata
  - build_appcast refuses incomplete packages
  - signature covers exact canonical bytes
  - failure cases: empty hash, zero size, disallowed URL, type mismatch, wrong key

Every refusal test is a mutation proof: the happy path is green first, then the
broken input turns red.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import sys
import tempfile

import pytest

# Ensure Server/ is on the path the same way other server tests do.
_SERVER_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if _SERVER_ROOT not in sys.path:
    sys.path.insert(0, _SERVER_ROOT)

# Force DEV update key BEFORE importing appcast/config consumers that cache keys.
os.environ["SYXX_ALLOW_DEV_UPDATE_KEY"] = "1"
os.environ.pop("SYXX_UPDATE_PRIVATE_KEY", None)
os.environ.pop("SYXX_UPDATE_PRIVATE_KEY_PATH", None)

from syxx_server import appcast, config  # noqa: E402


@pytest.fixture()
def package_dir(tmp_path, monkeypatch):
    d = tmp_path / "update-packages"
    d.mkdir()
    monkeypatch.setattr(config, "UPDATE_PACKAGE_DIR", str(d))
    monkeypatch.setattr(config, "ALLOW_DEV_UPDATE_KEY", True)
    monkeypatch.setattr(
        config,
        "UPDATE_DEV_PRIVATE_KEY_PATH",
        os.path.join(_SERVER_ROOT, "dev-keys", "update-signing.priv.b64"),
    )
    # Reset cached key so each test reloads under the patched config.
    appcast._UPDATE_PRIV_KEY = None
    appcast._UPDATE_PUB_KEY_RAW = None
    return d


def _write_pkg(tmp_path, name="Syxx.bin", content=b"syxx-package-bytes-v1"):
    p = tmp_path / name
    p.write_bytes(content)
    return str(p), hashlib.sha256(content).hexdigest(), len(content)


class TestPublishAndBuild:
    def test_publish_and_build_happy_mac(self, package_dir, tmp_path):
        path, sha, size = _write_pkg(tmp_path, "Syxx.dmg")
        url = "https://syxx.app/dist/Syxx-2.2.0.dmg"
        rec = appcast.publish_package(
            "mac", "2.2.0", path,
            release_notes="Phase 3.2 test",
            build="2026.8.1",
            download_url=url,
        )
        assert rec is not None
        assert rec["packages"][0]["sha256"] == sha
        assert rec["packages"][0]["size"] == size
        assert rec["packages"][0]["type"] == "dmg"
        assert (package_dir / "mac.json").is_file()

        manifest = appcast.build_appcast("mac")
        assert manifest is not None
        assert "signature" in manifest
        assert manifest["packages"][0]["sha256"] == sha
        assert manifest["packages"][0]["size"] == size
        assert manifest["version"] == "2.2.0"

        # Signature verifies over canonical bytes with the DEV public key.
        sig = base64.b64decode(manifest["signature"])
        unsigned = {k: v for k, v in manifest.items() if k != "signature"}
        payload = appcast._canon(unsigned)
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
        pub_b64 = open(
            os.path.join(_SERVER_ROOT, "dev-keys", "update-signing.pub.b64"),
            encoding="utf-8",
        ).read().strip()
        pub = Ed25519PublicKey.from_public_bytes(base64.b64decode(pub_b64))
        pub.verify(sig, payload)  # raises if wrong

    def test_publish_win_zip_and_deb_types(self, package_dir, tmp_path):
        path, _, _ = _write_pkg(tmp_path, "Syxx.zip")
        rec = appcast.publish_package(
            "win", "2.2.0", path,
            download_url="https://syxx.app/dist/Syxx-2.2.0.zip",
        )
        assert rec["packages"][0]["type"] == "zip"
        path2, _, _ = _write_pkg(tmp_path, "Syxx.deb")
        rec2 = appcast.publish_package(
            "deb", "2.2.0", path2,
            download_url="https://syxx.app/dist/Syxx-2.2.0.deb",
        )
        assert rec2["packages"][0]["type"] == "deb"

    def test_build_refuses_when_nothing_published(self, package_dir):
        assert appcast.build_appcast("mac") is None
        assert appcast.build_appcast("win") is None
        assert appcast.build_appcast("deb") is None

    def test_build_refuses_incomplete_empty_hash(self, package_dir, tmp_path):
        path, _, size = _write_pkg(tmp_path)
        # Manually write a corrupt record the validator must refuse to sign.
        bad = {
            "platform": "mac",
            "version": "2.2.0",
            "build": "",
            "minVersion": "1.0.0",
            "critical": False,
            "releaseNotes": "x",
            "packages": [{
                "url": "https://syxx.app/dist/Syxx.dmg",
                "sha256": "",  # empty — historical A264
                "size": size,
                "type": "dmg",
            }],
            "publishedAt": 1753747200,
        }
        (package_dir / "mac.json").write_text(json.dumps(bad), encoding="utf-8")
        assert appcast.build_appcast("mac") is None

    def test_build_refuses_zero_size(self, package_dir):
        bad = {
            "platform": "win",
            "version": "2.2.0",
            "build": "",
            "minVersion": "1.0.0",
            "critical": False,
            "releaseNotes": "x",
            "packages": [{
                "url": "https://syxx.app/dist/Syxx.zip",
                "sha256": "ab" * 32,
                "size": 0,
                "type": "zip",
            }],
            "publishedAt": 1753747200,
        }
        (package_dir / "win.json").write_text(json.dumps(bad), encoding="utf-8")
        assert appcast.build_appcast("win") is None

    def test_build_refuses_disallowed_url(self, package_dir):
        bad = {
            "platform": "mac",
            "version": "2.2.0",
            "build": "",
            "minVersion": "1.0.0",
            "critical": False,
            "releaseNotes": "x",
            "packages": [{
                "url": "https://evil.example/Syxx.dmg",
                "sha256": "ab" * 32,
                "size": 10,
                "type": "dmg",
            }],
            "publishedAt": 1753747200,
        }
        (package_dir / "mac.json").write_text(json.dumps(bad), encoding="utf-8")
        assert appcast.build_appcast("mac") is None

    def test_build_refuses_type_extension_mismatch(self, package_dir):
        bad = {
            "platform": "win",
            "version": "2.2.0",
            "build": "",
            "minVersion": "1.0.0",
            "critical": False,
            "releaseNotes": "x",
            "packages": [{
                "url": "https://syxx.app/dist/Syxx.deb",  # .deb but type zip
                "sha256": "ab" * 32,
                "size": 10,
                "type": "zip",
            }],
            "publishedAt": 1753747200,
        }
        (package_dir / "win.json").write_text(json.dumps(bad), encoding="utf-8")
        assert appcast.build_appcast("win") is None

    def test_publish_refuses_zero_size_file(self, package_dir, tmp_path):
        empty = tmp_path / "empty.dmg"
        empty.write_bytes(b"")
        assert appcast.publish_package(
            "mac", "2.2.0", str(empty),
            download_url="https://syxx.app/dist/Syxx.dmg",
        ) is None

    def test_publish_refuses_disallowed_url(self, package_dir, tmp_path):
        path, _, _ = _write_pkg(tmp_path, "Syxx.dmg")
        assert appcast.publish_package(
            "mac", "2.2.0", path,
            download_url="https://evil.example/Syxx.dmg",
        ) is None

    def test_publish_refuses_type_extension_mismatch(self, package_dir, tmp_path):
        path, _, _ = _write_pkg(tmp_path, "Syxx.dmg")
        # mac requires .dmg URL; giving .zip must refuse.
        assert appcast.publish_package(
            "mac", "2.2.0", path,
            download_url="https://syxx.app/dist/Syxx.zip",
        ) is None

    def test_publish_is_atomic_per_platform(self, package_dir, tmp_path):
        path, _, _ = _write_pkg(tmp_path, "a.dmg")
        appcast.publish_package(
            "mac", "1.0.0", path,
            download_url="https://syxx.app/dist/Syxx-1.0.0.dmg",
        )
        path2, sha2, _ = _write_pkg(tmp_path, "b.dmg", content=b"second-package")
        appcast.publish_package(
            "mac", "2.0.0", path2,
            download_url="https://syxx.app/dist/Syxx-2.0.0.dmg",
        )
        # Win untouched.
        assert not (package_dir / "win.json").exists()
        mac = json.loads((package_dir / "mac.json").read_text(encoding="utf-8"))
        assert mac["version"] == "2.0.0"
        assert mac["packages"][0]["sha256"] == sha2

    def test_altered_metadata_breaks_signature(self, package_dir, tmp_path):
        path, _, _ = _write_pkg(tmp_path, "Syxx.dmg")
        appcast.publish_package(
            "mac", "2.2.0", path,
            download_url="https://syxx.app/dist/Syxx-2.2.0.dmg",
        )
        manifest = appcast.build_appcast("mac")
        assert manifest is not None
        # Tamper after signing.
        manifest["version"] = "9.9.9"
        sig = base64.b64decode(manifest["signature"])
        unsigned = {k: v for k, v in manifest.items() if k != "signature"}
        payload = appcast._canon(unsigned)
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
        from cryptography.exceptions import InvalidSignature
        pub_b64 = open(
            os.path.join(_SERVER_ROOT, "dev-keys", "update-signing.pub.b64"),
            encoding="utf-8",
        ).read().strip()
        pub = Ed25519PublicKey.from_public_bytes(base64.b64decode(pub_b64))
        with pytest.raises(InvalidSignature):
            pub.verify(sig, payload)

    def test_never_uses_collector_key(self, package_dir, tmp_path, monkeypatch):
        """Update key path must not call leak._ensure_collector_key."""
        import syxx_server.leak as leak

        def boom():
            raise AssertionError("collector key must not be used for appcast")

        monkeypatch.setattr(leak, "_ensure_collector_key", boom)
        path, _, _ = _write_pkg(tmp_path, "Syxx.dmg")
        appcast._UPDATE_PRIV_KEY = None
        appcast._UPDATE_PUB_KEY_RAW = None
        rec = appcast.publish_package(
            "mac", "2.2.0", path,
            download_url="https://syxx.app/dist/Syxx.dmg",
        )
        assert rec is not None
        manifest = appcast.build_appcast("mac")
        assert manifest is not None
        assert manifest["signature"]

    def test_no_key_refuses_build(self, package_dir, tmp_path, monkeypatch):
        path, _, _ = _write_pkg(tmp_path, "Syxx.dmg")
        appcast.publish_package(
            "mac", "2.2.0", path,
            download_url="https://syxx.app/dist/Syxx.dmg",
        )
        appcast._UPDATE_PRIV_KEY = None
        appcast._UPDATE_PUB_KEY_RAW = None
        monkeypatch.setattr(config, "ALLOW_DEV_UPDATE_KEY", False)
        monkeypatch.setattr(config, "UPDATE_PRIVATE_KEY", "")
        monkeypatch.setattr(config, "UPDATE_PRIVATE_KEY_PATH", "")
        assert appcast.build_appcast("mac") is None

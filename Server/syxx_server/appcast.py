"""Custom auto-update appcast (G-1).

Sparkle-inspired but fully original: the server publishes a signed manifest
listing the latest version per platform, download URLs, and SHA-256 hashes.
Clients verify the Ed25519 signature against a key PINNED IN THE BINARY before
installing anything.

The manifest format:
{
  "platform": "mac" | "win" | "deb",
  "version": "2.1.0",
  "build": "2026.7.27",
  "minVersion": "1.0.0",
  "critical": false,
  "releaseNotes": "Bug fixes and improvements.",
  "packages": [
    {
      "url": "https://syxx.app/dist/Syxx-2.1.0.dmg",
      "sha256": "abc123...",
      "size": 12345678,
      "type": "dmg"
    }
  ],
  "publishedAt": 1722038400,
  "signature": "ed25519_sig..."
}

The signature covers the canonical JSON of everything except "signature" itself.
Clients verify using the dedicated UPDATE public key pinned in the binary —
never the collector identity, and never a key fetched from this origin.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import re
import tempfile
import time
from urllib.parse import urlparse

from . import config, logging_util

# Dedicated update-signing key. NEVER the collector / leak key.
_UPDATE_PRIV_KEY = None
_UPDATE_PUB_KEY_RAW = None

# Platform → required package type. Clients choose installer path from this field.
_PLATFORM_PACKAGE_TYPE = {
    "mac": "dmg",
    "win": "zip",
    "deb": "deb",
}

_ALLOWED_PACKAGE_TYPES = frozenset(_PLATFORM_PACKAGE_TYPE.values())

# Product download hosts — must stay aligned with client RemoteUrlPolicy.
_ALLOWED_HOST_SUFFIXES = ("syxx.app", "milkandgin.com")

_SEMVER_RE = re.compile(r"^\d+(\.\d+){0,3}([.-][0-9A-Za-z.-]+)?$")
_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")


def _ensure_key():
    """Lazy-load the dedicated update-signing key.

    Load order:
      1. SYXX_UPDATE_PRIVATE_KEY — PKCS8 DER, base64 (production)
      2. SYXX_UPDATE_PRIVATE_KEY_PATH — file containing PKCS8 DER base64 or raw 32-byte seed base64
      3. Server/dev-keys/update-signing.priv.b64 — ONLY when SYXX_ALLOW_DEV_UPDATE_KEY=1

    Never falls back to the collector identity.
    """
    global _UPDATE_PRIV_KEY, _UPDATE_PUB_KEY_RAW
    if _UPDATE_PRIV_KEY is not None:
        return

    try:
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey  # type: ignore[unresolved-import]
        from cryptography.hazmat.primitives.serialization import (  # type: ignore[unresolved-import]
            Encoding,
            PublicFormat,
            load_der_private_key,
        )
    except Exception as e:
        logging_util.error("appcast_crypto_import_failed", err=str(e))
        return

    raw_env = (config.UPDATE_PRIVATE_KEY or "").strip()
    path = (config.UPDATE_PRIVATE_KEY_PATH or "").strip()
    material = None
    source = None

    if raw_env:
        material = raw_env
        source = "env:SYXX_UPDATE_PRIVATE_KEY"
    elif path:
        try:
            with open(path, encoding="utf-8") as f:
                material = f.read().strip()
            source = f"path:{path}"
        except OSError as e:
            logging_util.error("appcast_key_path_unreadable", path=path, err=str(e))
            return
    elif config.ALLOW_DEV_UPDATE_KEY:
        dev_path = config.UPDATE_DEV_PRIVATE_KEY_PATH
        try:
            with open(dev_path, encoding="utf-8") as f:
                material = f.read().strip()
            source = f"dev-key:{dev_path}"
            logging_util.info(
                "appcast_using_dev_update_key",
                path=dev_path,
                msg="DEV-ONLY update key loaded. Not for production.",
            )
        except OSError as e:
            logging_util.error("appcast_dev_key_unreadable", path=dev_path, err=str(e))
            return
    else:
        logging_util.error(
            "appcast_no_signing_key",
            msg=(
                "No update-signing key configured. Set SYXX_UPDATE_PRIVATE_KEY or "
                "SYXX_UPDATE_PRIVATE_KEY_PATH. DEV fallback requires SYXX_ALLOW_DEV_UPDATE_KEY=1."
            ),
        )
        return

    try:
        der_or_seed = base64.b64decode(material)
    except Exception as e:
        logging_util.error("appcast_key_b64_invalid", source=source, err=str(e))
        return

    try:
        if len(der_or_seed) == 32:
            _UPDATE_PRIV_KEY = Ed25519PrivateKey.from_private_bytes(der_or_seed)
        else:
            loaded = load_der_private_key(der_or_seed, password=None)
            if not isinstance(loaded, Ed25519PrivateKey):
                logging_util.error(
                    "appcast_key_not_ed25519",
                    source=source,
                    actual=type(loaded).__name__,
                )
                _UPDATE_PRIV_KEY = None
                return
            _UPDATE_PRIV_KEY = loaded
    except Exception as e:
        logging_util.error("appcast_key_load_failed", source=source, err=str(e))
        _UPDATE_PRIV_KEY = None
        return

    pub = _UPDATE_PRIV_KEY.public_key()
    _UPDATE_PUB_KEY_RAW = pub.public_bytes(Encoding.Raw, PublicFormat.Raw)
    logging_util.info(
        "appcast_update_key_loaded",
        source=source,
        pub_prefix=base64.b64encode(_UPDATE_PUB_KEY_RAW[:8]).decode("ascii"),
    )


def update_public_key_base64():
    """Return the loaded update public key (raw 32 bytes, base64), or None."""
    _ensure_key()
    if _UPDATE_PUB_KEY_RAW is None:
        return None
    return base64.b64encode(_UPDATE_PUB_KEY_RAW).decode("ascii")


def _canon(obj):
    """Canonical JSON for signing (deterministic, compact).

    Must stay byte-identical to client AppcastCanonicalJSON / AppcastCanonicalJson
    and corpus/appcast-crossplatform/generate.py.
    """
    return json.dumps(obj, separators=(",", ":"), sort_keys=True).encode("utf-8")


def _sha256_file(path):
    """Compute lowercase hex SHA-256 of a file."""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


def _package_store_path(platform):
    return os.path.join(config.UPDATE_PACKAGE_DIR, f"{platform}.json")


def _is_allowlisted_https_url(url):
    """HTTPS, no credentials, default port, product host — mirrors client RemoteUrlPolicy."""
    if not isinstance(url, str) or not url.strip():
        return False
    try:
        parsed = urlparse(url.strip())
    except Exception:
        return False
    if parsed.scheme != "https":
        return False
    if parsed.username is not None or parsed.password is not None:
        return False
    if parsed.port is not None and parsed.port != 443:
        return False
    host = (parsed.hostname or "").lower().rstrip(".")
    if not host:
        return False
    for suffix in _ALLOWED_HOST_SUFFIXES:
        if host == suffix or host.endswith("." + suffix):
            return True
    return False


def _validate_package_record(record, platform):
    """Return None if installable; otherwise a short machine reason string."""
    if not isinstance(record, dict):
        return "not_an_object"
    if record.get("platform") != platform:
        return "platform_mismatch"
    version = record.get("version")
    if not isinstance(version, str) or not version.strip() or not _SEMVER_RE.match(version.strip()):
        return "invalid_version"
    build = record.get("build")
    if build is None or not isinstance(build, str):
        return "invalid_build"
    min_version = record.get("minVersion")
    if not isinstance(min_version, str) or not min_version.strip():
        return "invalid_min_version"
    notes = record.get("releaseNotes")
    if not isinstance(notes, str) or not notes.strip():
        return "invalid_release_notes"
    published = record.get("publishedAt")
    if not isinstance(published, int) or isinstance(published, bool) or published <= 0:
        return "invalid_published_at"
    critical = record.get("critical")
    if not isinstance(critical, bool):
        return "invalid_critical"
    packages = record.get("packages")
    if not isinstance(packages, list) or len(packages) != 1:
        return "invalid_packages"
    pkg = packages[0]
    if not isinstance(pkg, dict):
        return "invalid_package_entry"
    pkg_type = pkg.get("type")
    expected_type = _PLATFORM_PACKAGE_TYPE.get(platform)
    if pkg_type != expected_type or pkg_type not in _ALLOWED_PACKAGE_TYPES:
        return "invalid_package_type"
    url = pkg.get("url")
    if not _is_allowlisted_https_url(url):
        return "disallowed_url"
    # Extension must match authenticated type.
    path = urlparse(url).path.lower()
    if not path.endswith("." + pkg_type):
        return "type_extension_mismatch"
    sha = pkg.get("sha256")
    if not isinstance(sha, str) or not _SHA256_RE.match(sha.lower()):
        return "invalid_sha256"
    # Normalize: store/compare lowercase only.
    if sha != sha.lower():
        return "sha256_not_lowercase"
    size = pkg.get("size")
    if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
        return "invalid_size"
    return None


def _atomic_write_json(path, obj):
    """Write JSON atomically (temp file in same dir + os.replace)."""
    directory = os.path.dirname(path) or "."
    os.makedirs(directory, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".appcast-", suffix=".tmp", dir=directory)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(obj, f, indent=2, sort_keys=True)
            f.write("\n")
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
        try:
            os.chmod(path, 0o600)
        except OSError:
            pass
    except Exception:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def _load_published(platform):
    """Load and validate a published package record. Returns dict or None."""
    path = _package_store_path(platform)
    if not os.path.isfile(path):
        return None
    try:
        with open(path, encoding="utf-8") as f:
            record = json.load(f)
    except (OSError, json.JSONDecodeError) as e:
        logging_util.error("appcast_package_unreadable", platform=platform, err=str(e))
        return None
    reason = _validate_package_record(record, platform)
    if reason is not None:
        logging_util.error(
            "appcast_package_incomplete",
            platform=platform,
            reason=reason,
        )
        return None
    return record


def publish_package(
    platform,
    version,
    package_path,
    release_notes="",
    build="",
    min_version="1.0.0",
    download_url=None,
    critical=False,
):
    """Publish a new package version atomically per platform.

    Persists:
      - platform and package type
      - semantic version and build
      - allowlisted HTTPS download URL
      - full lowercase SHA-256
      - nonzero byte size
      - minimum compatible version
      - publication timestamp and approved release notes

    Returns the published record (without signature), or None on refusal.
    """
    platform = (platform or "").strip().lower()
    if platform not in _PLATFORM_PACKAGE_TYPE:
        logging_util.error("appcast_publish_unknown_platform", platform=platform)
        return None
    if not package_path or not os.path.isfile(package_path):
        logging_util.error("appcast_publish_missing_file", path=str(package_path))
        return None

    try:
        size = os.path.getsize(package_path)
    except OSError as e:
        logging_util.error("appcast_publish_stat_failed", err=str(e))
        return None
    if size <= 0:
        logging_util.error("appcast_publish_zero_size", path=package_path)
        return None

    sha256 = _sha256_file(package_path)
    pkg_type = _PLATFORM_PACKAGE_TYPE[platform]

    # Resolve download URL: explicit arg, then per-platform config, never a blank.
    url = (download_url or "").strip()
    if not url:
        url = {
            "mac": config.UPDATE_URL_MAC,
            "win": config.UPDATE_URL_WIN,
            "deb": config.UPDATE_URL_DEB,
        }.get(platform, "")
    url = (url or "").strip()
    if not _is_allowlisted_https_url(url):
        logging_util.error("appcast_publish_disallowed_url", url=url)
        return None
    if not urlparse(url).path.lower().endswith("." + pkg_type):
        logging_util.error(
            "appcast_publish_type_extension_mismatch",
            url=url,
            type=pkg_type,
        )
        return None

    notes = (release_notes or "").strip() or "Update available."
    version = (version or "").strip()
    if not version or not _SEMVER_RE.match(version):
        logging_util.error("appcast_publish_invalid_version", version=version)
        return None
    min_version = (min_version or "1.0.0").strip()
    if not min_version:
        logging_util.error("appcast_publish_invalid_min_version")
        return None
    if not isinstance(critical, bool):
        critical = False
    build = build if isinstance(build, str) else ""

    record = {
        "platform": platform,
        "version": version,
        "build": build,
        "minVersion": min_version,
        "critical": critical,
        "releaseNotes": notes,
        "packages": [
            {
                "url": url,
                "sha256": sha256,  # already lowercase hex from hashlib
                "size": int(size),
                "type": pkg_type,
            }
        ],
        "publishedAt": int(time.time()),
    }

    reason = _validate_package_record(record, platform)
    if reason is not None:
        logging_util.error("appcast_publish_validation_failed", reason=reason)
        return None

    store_path = _package_store_path(platform)
    try:
        _atomic_write_json(store_path, record)
    except OSError as e:
        logging_util.error("appcast_publish_write_failed", path=store_path, err=str(e))
        return None

    logging_util.info(
        "appcast_publish_package",
        platform=platform,
        version=version,
        sha256=sha256[:16] + "...",
        size=size,
        type=pkg_type,
        path=store_path,
    )
    return record


def build_appcast(platform):
    """Build a signed appcast manifest for the given platform.

    Refuses (returns None) when:
      - platform is unknown
      - no complete published package exists
      - any required package field is missing or invalid
      - the update-signing key is unavailable

    Signs the exact canonical bytes clients re-derive and verify.
    """
    platform = (platform or "").strip().lower()
    if platform not in _PLATFORM_PACKAGE_TYPE:
        return None

    record = _load_published(platform)
    if record is None:
        # Incomplete / missing packages must never be signed. Prefer 404 over a
        # signed lie with empty sha256/size (A264).
        return None

    _ensure_key()
    if _UPDATE_PRIV_KEY is None:
        logging_util.error("appcast_no_signing_key")
        return None

    # Manifest fields only — no signature yet. Canonical bytes of THIS object are signed.
    manifest = {
        "platform": record["platform"],
        "version": record["version"],
        "build": record["build"],
        "minVersion": record["minVersion"],
        "critical": record["critical"],
        "releaseNotes": record["releaseNotes"],
        "packages": [
            {
                "url": record["packages"][0]["url"],
                "sha256": record["packages"][0]["sha256"].lower(),
                "size": int(record["packages"][0]["size"]),
                "type": record["packages"][0]["type"],
            }
        ],
        "publishedAt": int(record["publishedAt"]),
    }

    # Final gate: refuse rather than sign anything incomplete.
    reason = _validate_package_record(manifest, platform)
    if reason is not None:
        logging_util.error("appcast_build_refused_incomplete", platform=platform, reason=reason)
        return None

    payload = _canon(manifest)
    sig = _UPDATE_PRIV_KEY.sign(payload)
    manifest["signature"] = base64.b64encode(sig).decode("ascii")
    return manifest

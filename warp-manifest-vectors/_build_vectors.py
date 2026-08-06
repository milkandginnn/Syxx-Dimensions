#!/usr/bin/env python3
"""Deterministic builder for the WARP_MANIFEST_V1 signed-manifest fixture corpus.

This is the single reproducible source of truth for `warp-manifest-vectors/`.
It writes, for every case:
  * the origin HTML bytes and each asset body under `inputs/`,
  * `envelope.json` = {manifest_b64, signature_ed25519, key_id},
  * `pinned_keys.json`, `requested_origin`, `now_ms`, optional `client_nonce`,
    optional `fetch_meta.json`,
  * `expected.json` (produced by the reference evaluator below, NOT hand-written).

Dependencies: Python stdlib + `cryptography` (Ed25519). The brotli case (014)
uses the `brotli` Python module if importable, else the `brotli` CLI.

Regenerate with:  python3 warp-manifest-vectors/_build_vectors.py
Never hand-edit a generated file; change this script and re-run.

Canonical inner-manifest serialization (spec §2), byte-for-byte:
    json.dumps(m, separators=(",", ":"), sort_keys=True, ensure_ascii=False).encode("utf-8")
The producer base64s those exact bytes into `manifest_b64` and signs the
base64-DECODED bytes with a detached Ed25519 signature. The client verifies over
the decoded bytes and MUST NOT re-serialize.
"""

from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
import subprocess
from pathlib import Path
from urllib.parse import urlsplit

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import (
    Ed25519PrivateKey,
    Ed25519PublicKey,
)
from cryptography.exceptions import InvalidSignature

ROOT = Path(__file__).resolve().parent
KEYS = ROOT / "keys"

# Set by main() from --force-reencode: re-encode gzip/br bodies even when the
# existing committed bytes decode to the expected plaintext (see keep_or_encode).
FORCE_REENCODE = False

# --- Corpus-wide constants -------------------------------------------------
BASE_ORIGIN = "https://example.test/"
NOW_MS = 1754179200000          # the clock every case is evaluated against
FRESH_SKEW_MS = 300_000         # future-dated tolerance (spec §4.5)
FRESHNESS_SECONDS = 300
# Spec Amendment A4: verifier-side ceiling on the declared freshness window. Clamped BEFORE the
# millisecond conversion so an absurd declared value can never overflow 64-bit arithmetic in the
# clients (Swift traps on unclamped *1000; C# silently wraps). Twin constants:
# WarpEngine.cs MaxFreshnessWindowSeconds / WarpEngine.swift maxFreshnessWindowSeconds.
MAX_FRESHNESS_WINDOW_SECONDS = 600
ASSET_CLASSES_IN_SCOPE = ["HTML", "JS", "CSS", "CSS_IMPORT"]

# Fixed 32-byte seeds so the committed keypairs are perfectly reproducible.
# TEST KEY ONLY -- these private keys are published on purpose; never trust them
# for anything real.
TEST_SEED = b"syxx-warp-manifest-TEST-KEY-seed"        # 32 bytes, trusted signer
UNTRUSTED_SEED = b"syxx-warp-manifest-UNTRUSTED-key"    # 32 bytes, untrusted signer
assert len(TEST_SEED) == 32 and len(UNTRUSTED_SEED) == 32


# --- Encoding helpers ------------------------------------------------------
def gz(data: bytes) -> bytes:
    # mtime=0 makes the gzip envelope byte-deterministic across runs/hosts.
    return gzip.compress(data, mtime=0)


def br(data: bytes) -> bytes:
    try:
        import brotli  # type: ignore

        return brotli.compress(data)
    except ImportError:
        return subprocess.run(
            ["brotli", "-q", "11", "-c"], input=data, capture_output=True, check=True
        ).stdout


def br_decompress(data: bytes) -> bytes:
    try:
        import brotli  # type: ignore

        return brotli.decompress(data)
    except ImportError:
        return subprocess.run(
            ["brotli", "-d", "-c"], input=data, capture_output=True, check=True
        ).stdout


def keep_or_encode(path: Path, plaintext: bytes, encoding: str, force: bool = False) -> bytes:
    """Idempotent compressed-body emission (README §10).

    zlib and brotli encoder output is build-dependent: a Linux zlib-ng build and
    a macOS zlib emit different, equally valid, streams for the same input. If
    `path` already holds bytes that DECODE to exactly `plaintext`, keep them
    unchanged so regeneration is byte-stable across hosts; otherwise (or under
    --force-reencode) encode fresh. A corrupt/undecodable existing file is
    always replaced.
    """
    if not force and path.exists():
        try:
            existing = path.read_bytes()
            decoded = gzip.decompress(existing) if encoding == "gzip" else br_decompress(existing)
            if decoded == plaintext:
                print(f"  kept existing {path.name} (decodes to expected bytes)")
                return existing
        except Exception:
            pass  # corrupt or undecodable existing file -> re-encode below
    return gz(plaintext) if encoding == "gzip" else br(plaintext)


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


# --- Filename mapping (documented in README; mirrored by every loader) -----
def asset_filename(url: str) -> str:
    """Map a declared asset URL to its on-disk `inputs/` filename.

    filename = "<host>__<path-with-leading-slash-stripped-and-'/'->'__'>"
    A root/empty path becomes "<host>__index".
    """
    parts = urlsplit(url)
    path = parts.path
    leaf = "index" if path in ("", "/") else path.lstrip("/").replace("/", "__")
    return f"{parts.netloc}__{leaf}"


# --- Canonical serialization + signing (spec §2) ---------------------------
def canonical_manifest_bytes(manifest: dict) -> bytes:
    return json.dumps(
        manifest, separators=(",", ":"), sort_keys=True, ensure_ascii=False
    ).encode("utf-8")


def key_id_of(pub: Ed25519PublicKey) -> str:
    raw = pub.public_bytes(
        serialization.Encoding.Raw, serialization.PublicFormat.Raw
    )
    return raw.hex()


def write_keys() -> tuple[Ed25519PrivateKey, str, Ed25519PrivateKey, str]:
    KEYS.mkdir(parents=True, exist_ok=True)
    out = {}
    for name, seed in (("test_signing_key", TEST_SEED), ("untrusted_key", UNTRUSTED_SEED)):
        priv = Ed25519PrivateKey.from_private_bytes(seed)
        pub = priv.public_key()
        kid = key_id_of(pub)
        (KEYS / f"{name}.pem").write_bytes(
            priv.private_bytes(
                serialization.Encoding.PEM,
                serialization.PrivateFormat.PKCS8,
                serialization.NoEncryption(),
            )
        )
        (KEYS / f"{name}.pub.pem").write_bytes(
            pub.public_bytes(
                serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo
            )
        )
        (KEYS / f"{name}.key_id").write_text(kid + "\n")
        out[name] = (priv, kid)
    (KEYS / "README.md").write_text(KEYS_README)
    tp, tk = out["test_signing_key"]
    up, uk = out["untrusted_key"]
    return tp, tk, up, uk


# --- Reference client evaluator (mirrors spec §4 order + §5 lattice) -------
def load_pinned_pubkeys() -> dict[str, Ed25519PublicKey]:
    """A client pins PUBLIC key material keyed by key_id. Build that registry
    from the committed test pubkeys (verification never touches a private key)."""
    registry: dict[str, Ed25519PublicKey] = {}
    for pem in KEYS.glob("*.pub.pem"):
        pub = serialization.load_pem_public_key(pem.read_bytes())
        registry[key_id_of(pub)] = pub
    return registry


def evaluate(case_dir: Path) -> dict:
    """Read the written case artifacts and produce the expected result.

    This is the reference client: the C#/Swift/Python test loaders MUST
    reproduce this exact sequence and land on the same conclusion/states.
    Verification uses only pinned PUBLIC key material.
    """
    envelope = json.loads((case_dir / "envelope.json").read_text())
    pinned = json.loads((case_dir / "pinned_keys.json").read_text())["trusted_key_ids"]
    registry = load_pinned_pubkeys()
    requested_origin = (case_dir / "requested_origin").read_text().strip()
    now_ms = int((case_dir / "now_ms").read_text().strip())
    client_nonce = None
    if (case_dir / "client_nonce").exists():
        client_nonce = (case_dir / "client_nonce").read_text().strip()
    fetch_meta = {}
    if (case_dir / "fetch_meta.json").exists():
        fetch_meta = json.loads((case_dir / "fetch_meta.json").read_text())

    result = {
        "conclusion": None,
        "signatureState": "notEvaluated",
        "manifestState": "notEvaluated",
        "freshnessState": "notEvaluated",
        "htmlState": "notEvaluated",
        "assets": [],
        "notes": "",
    }

    # Step 0 (spec Amendment A1, 2026-08-04): envelope structural integrity.
    # A structurally broken envelope is the ABSENCE of a signature, never
    # invalidSignature: transport corruption must not paint a page red.
    def structural_failure(note: str) -> dict:
        result["signatureState"] = "malformed"
        result["manifestState"] = "notEvaluated"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = note
        return result

    for field in ("manifest_b64", "signature_ed25519", "key_id"):
        if not isinstance(envelope.get(field), str):
            return structural_failure(
                f"envelope field '{field}' is missing or not a string (malformed envelope)."
            )

    # Step 1: base64-decode manifest_b64 -> B (undecodable = structural, A1)
    try:
        B = base64.b64decode(envelope["manifest_b64"], validate=True)
    except Exception:
        return structural_failure("manifest_b64 is not valid base64 (malformed envelope).")
    try:
        sig_bytes = base64.b64decode(envelope["signature_ed25519"], validate=True)
    except Exception:
        return structural_failure("signature_ed25519 is not valid base64 (malformed envelope).")

    # Step 2: resolve key_id in pinned set
    if envelope["key_id"] not in pinned:
        result["signatureState"] = "untrustedSigner"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "key_id not in the client's pinned set; never a match."
        return result

    # Step 3: Ed25519-verify signature over B, BEFORE any JSON parse.
    # Only InvalidSignature means "present but does not verify" (Amendment A2:
    # a decodable wrong-length signature also lands here — `cryptography`
    # raises InvalidSignature for it, as NSec/CryptoKit return false).
    pub = registry[envelope["key_id"]]  # pinned public key for this key_id
    try:
        pub.verify(sig_bytes, B)
    except InvalidSignature:
        result["signatureState"] = "invalidSignature"
        result["conclusion"] = "TAMPERING_DETECTED"
        result["notes"] = "signature present but does not verify over the decoded bytes."
        return result
    result["signatureState"] = "verified"

    # Step 4: parse the inner manifest
    try:
        manifest = json.loads(B.decode("utf-8"))
    except Exception:
        result["manifestState"] = "malformed"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "signature verified but decoded bytes are not parseable JSON."
        return result

    # Step 4a (spec Amendment A4, 2026-08-04): structural gate on the AUTHENTICATED manifest.
    # The root must be a JSON object, the version must be the one dialect this client speaks,
    # and `assets` must be present as an array of objects. Any failure -> malformed ->
    # INCONCLUSIVE (a corrupt asset list must never degrade to "nothing declared").
    def manifest_malformed(note: str) -> dict:
        result["manifestState"] = "malformed"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = note
        return result

    if not isinstance(manifest, dict):
        return manifest_malformed("signature verified but the manifest root is not a JSON object (Amendment A4).")
    if manifest.get("warp_manifest_version") != "WARP_MANIFEST_V1":
        return manifest_malformed(
            "signature verified but warp_manifest_version is not WARP_MANIFEST_V1 (unsupported dialect; Amendment A4)."
        )
    declared_assets = manifest.get("assets")
    if not isinstance(declared_assets, list) or any(not isinstance(a, dict) for a in declared_assets):
        return manifest_malformed(
            "signature verified but the manifest has no assets array of objects (Amendment A4)."
        )
    result["manifestState"] = "parsed"

    # Step 5: freshness / origin / nonce (spec order). Guards mirror the shipped
    # clients: a missing/non-integer timestamp is treated as expired (never a
    # crash, never a pass); a missing freshness_seconds is 0.
    ts = manifest.get("timestamp")
    freshness_seconds = manifest.get("freshness_seconds")
    if not isinstance(freshness_seconds, int):
        freshness_seconds = 0
    # Amendment A4 freshness ceiling: a signed manifest cannot extend its own replayable
    # lifetime past MAX_FRESHNESS_WINDOW_SECONDS (mirrors both client engines' clamp).
    freshness_seconds = min(freshness_seconds, MAX_FRESHNESS_WINDOW_SECONDS)
    if not isinstance(ts, int) or now_ms - ts > freshness_seconds * 1000:
        result["freshnessState"] = "expired"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "manifest older than freshness window."
        return result
    if ts > now_ms + FRESH_SKEW_MS:
        result["freshnessState"] = "futureDated"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "manifest timestamp is future-dated beyond the 300000 ms skew."
        return result
    if manifest.get("origin_url") != requested_origin:
        result["freshnessState"] = "originMismatch"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "origin_url does not equal the origin the client requested."
        return result
    if client_nonce is not None and manifest.get("nonce") != client_nonce:
        result["freshnessState"] = "replayNonceMismatch"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "client sent a nonce; manifest echoed a different one."
        return result
    result["freshnessState"] = "fresh"

    # Step 6: fetch each asset from the client vantage (= read inputs/), decode, hash.
    # Hash comparisons are case-insensitive on the DECLARED side (Amendment A5).
    # A missing inputs/origin.html or a missing html_sha256 is htmlState
    # fetchError (the page could not be fetched/verified), never a crash.
    html_path = case_dir / "inputs" / "origin.html"
    declared_html_sha = manifest.get("html_sha256")
    if not html_path.exists() or not isinstance(declared_html_sha, str):
        result["htmlState"] = "fetchError"
    else:
        html_bytes = html_path.read_bytes()
        result["htmlState"] = (
            "match" if sha256_hex(html_bytes) == declared_html_sha.lower() else "mismatch"
        )

    any_mismatch = result["htmlState"] == "mismatch"
    any_missing = result["htmlState"] == "fetchError"
    # declared_assets was validated by the Amendment A4 gate above: a list of dicts.
    for a in declared_assets:
        entry = {"url": a["url"], "class": a["class"], "state": None}
        if a.get("fetch_status") == "fetch_failed" or "sha256" not in a:
            entry["state"] = "fetchFailedDeclared"
            any_missing = True
            result["assets"].append(entry)
            continue
        fname = asset_filename(a["url"])
        fpath = case_dir / "inputs" / fname
        if not fpath.exists():
            entry["state"] = "fetchError"
            any_missing = True
            result["assets"].append(entry)
            continue
        raw = fpath.read_bytes()
        enc = fetch_meta.get(fname, "identity")
        if enc == "gzip":
            body = gzip.decompress(raw)
        elif enc == "br":
            body = br_decompress(raw)
        else:
            body = raw
        entry["state"] = "match" if sha256_hex(body) == a["sha256"].lower() else "mismatch"
        if entry["state"] == "mismatch":
            any_mismatch = True
        result["assets"].append(entry)

    # §5 precedence: confirmed tampering outranks missing data; missing never matches.
    if any_mismatch:
        result["conclusion"] = "TAMPERING_DETECTED"
        result["notes"] = "verified+fresh manifest, but a raw-byte hash mismatch was found."
    elif any_missing:
        result["conclusion"] = "INCONCLUSIVE"
        if result["htmlState"] == "fetchError":
            result["notes"] = "the origin page could not be fetched from the client vantage; missing data is never a clean pass."
        else:
            result["notes"] = "a declared asset could not be verified; missing data is never a clean pass."
    else:
        result["conclusion"] = "VERIFIED_MATCH"
        result["notes"] = "signature verified against a pinned key, fresh, HTML and every asset matched."
    return result


# --- Case authoring --------------------------------------------------------
class Asset:
    def __init__(self, url, klass, clean, *, client=None, encoding="identity",
                 fetch_failed=False, client_missing=False, bytes_override=None):
        self.url = url
        self.klass = klass
        self.clean = clean                       # server clean baseline bytes
        self.client = clean if client is None else client  # client-vantage bytes
        self.encoding = encoding                 # delivery encoding of client body
        self.fetch_failed = fetch_failed         # server declared fetch_failed
        self.client_missing = client_missing     # declared w/ sha256, but the CLIENT
                                                 # fetch fails: no inputs/ file (017/018)
        self.bytes_override = bytes_override     # wrong advisory `bytes` value (023)

    def manifest_entry(self):
        if self.fetch_failed:
            return {"url": self.url, "class": self.klass, "fetch_status": "fetch_failed"}
        return {
            "url": self.url,
            "class": self.klass,
            "sha256": sha256_hex(self.clean),
            "bytes": len(self.clean) if self.bytes_override is None else self.bytes_override,
        }


def build_case(
    priv: Ed25519PrivateKey,
    kid: str,
    name: str,
    *,
    html_clean: bytes,
    html_client: bytes | None,
    assets: list[Asset],
    origin_url: str = BASE_ORIGIN,
    requested_origin: str = BASE_ORIGIN,
    timestamp: int = NOW_MS - 60_000,
    now_ms: int = NOW_MS,
    manifest_nonce=None,
    client_nonce=None,
    pinned_kid: str | None = None,
    tamper_manifest_b64=None,
    override_manifest_bytes=None,
    override_signature=None,          # callable raw-sig-bytes -> bytes (020)
    override_signature_b64=None,      # replace the b64 signature string outright (026)
    hash_case: str = "lower",         # "upper" -> declared hex digests uppercased (022)
    omit_key_id: bool = False,        # drop key_id from the envelope (028)
    manifest_version: str = "WARP_MANIFEST_V1",  # non-V1 -> A4 version gate (029)
    omit_assets: bool = False,        # drop the assets key from the manifest (030)
    freshness_seconds: int = FRESHNESS_SECONDS,  # absurd value -> A4 ceiling (031)
    expected_conclusion: str,
):
    case_dir = ROOT / name
    inputs = case_dir / "inputs"
    inputs.mkdir(parents=True, exist_ok=True)

    # Build the inner manifest.
    manifest = {
        "warp_manifest_version": manifest_version,
        "origin_url": origin_url,
        "final_url": origin_url,
        "html_sha256": sha256_hex(html_clean),
        "asset_classes_in_scope": ASSET_CLASSES_IN_SCOPE,
        "assets": [a.manifest_entry() for a in assets],
        "timestamp": timestamp,
        "freshness_seconds": freshness_seconds,
        "redirect_policy": "none",
        "allowed_redirects": [],
        "nonce": manifest_nonce,
    }
    if omit_assets:
        del manifest["assets"]        # A4: assets is REQUIRED; its absence is malformed (030)
    if hash_case == "upper":
        # Amendment A5: declared hex is normally lowercase; case 022 pins that an
        # uppercase declaration still matches (compare case-insensitively).
        manifest["html_sha256"] = manifest["html_sha256"].upper()
        for e in manifest["assets"]:
            if "sha256" in e:
                e["sha256"] = e["sha256"].upper()

    if override_manifest_bytes is not None:
        B = override_manifest_bytes           # deliberately non-JSON (case 005)
    else:
        B = canonical_manifest_bytes(manifest)

    signature = priv.sign(B)
    if override_signature is not None:
        signature = override_signature(signature)   # e.g. truncate to 32 bytes (020)
    manifest_b64 = base64.b64encode(B).decode("ascii")
    if tamper_manifest_b64 is not None:
        manifest_b64 = tamper_manifest_b64(manifest_b64)
    signature_b64 = base64.b64encode(signature).decode("ascii")
    if override_signature_b64 is not None:
        signature_b64 = override_signature_b64      # e.g. not-base64 garbage (026)

    envelope = {
        "manifest_b64": manifest_b64,
        "signature_ed25519": signature_b64,
        "key_id": kid,
    }
    if omit_key_id:
        del envelope["key_id"]                      # structural hole (028)

    # Write inputs. html_client=None models an origin page the CLIENT cannot
    # fetch (019): the manifest still declares html_sha256 over html_clean, but
    # no inputs/origin.html exists.
    html_path = inputs / "origin.html"
    if html_client is None:
        html_path.unlink(missing_ok=True)
    else:
        html_path.write_bytes(html_client)
    fetch_meta = {}
    for a in assets:
        if a.fetch_failed:
            continue  # server couldn't fetch it; no client body to compare against
        fname = asset_filename(a.url)
        if a.client_missing:
            # declared with a real sha256, but the client-vantage fetch fails:
            # deliberately no inputs/ file (017/018).
            (inputs / fname).unlink(missing_ok=True)
            continue
        if a.encoding == "gzip":
            (inputs / fname).write_bytes(
                keep_or_encode(inputs / fname, a.client, "gzip", FORCE_REENCODE))
            fetch_meta[fname] = "gzip"
        elif a.encoding == "br":
            (inputs / fname).write_bytes(
                keep_or_encode(inputs / fname, a.client, "br", FORCE_REENCODE))
            fetch_meta[fname] = "br"
        else:
            (inputs / fname).write_bytes(a.client)

    # Write the rest of the case artifacts.
    (case_dir / "envelope.json").write_text(json.dumps(envelope, indent=2) + "\n")
    (case_dir / "pinned_keys.json").write_text(
        json.dumps({"trusted_key_ids": [pinned_kid if pinned_kid is not None else kid]}, indent=2)
        + "\n"
    )
    (case_dir / "requested_origin").write_text(requested_origin + "\n")
    (case_dir / "now_ms").write_text(str(now_ms) + "\n")
    if client_nonce is not None:
        (case_dir / "client_nonce").write_text(client_nonce + "\n")
    if fetch_meta:
        (case_dir / "fetch_meta.json").write_text(json.dumps(fetch_meta, indent=2) + "\n")

    # Produce expected.json from the reference evaluator over the WRITTEN files.
    expected = evaluate(case_dir)
    (case_dir / "expected.json").write_text(
        json.dumps(expected, indent=2, sort_keys=True) + "\n"
    )

    got = expected["conclusion"]
    ok = got == expected_conclusion
    print(f"  {name:24s} -> {got:18s} {'OK' if ok else 'MISMATCH(expected %s)' % expected_conclusion}")
    if not ok:
        raise SystemExit(f"case {name}: evaluator produced {got}, author intended {expected_conclusion}")
    return case_dir


# --- Shared page content ---------------------------------------------------
HTML_GOOD = (
    b"<!doctype html>\n<html><head>\n"
    b'<link rel="stylesheet" href="/styles.css">\n'
    b"</head><body>\n<h1>Syxx warp manifest test page</h1>\n"
    b'<script src="/app.js"></script>\n'
    b'<script src="https://cdn.example.test/vendor.js"></script>\n'
    b"</body></html>\n"
)
APP_JS = b"console.log('syxx warp app.js v1');\n"
STYLES_CSS = b"body{font-family:system-ui;margin:0}\n"
VENDOR_JS = b"window.__vendor__=function(){return 42};\n"


def std_assets():
    return [
        Asset("https://example.test/app.js", "JS", APP_JS),
        Asset("https://example.test/styles.css", "CSS", STYLES_CSS),
        Asset("https://cdn.example.test/vendor.js", "JS", VENDOR_JS),
    ]


def flip_one_char_in_value(b64: str) -> str:
    """Flip a base64 char so the DECODED bytes stay valid JSON but the signature
    no longer matches. We target a character deep inside the payload (a hash hex
    digit region) so JSON structure is preserved."""
    B = base64.b64decode(b64)
    text = B.decode("utf-8")
    # Flip one hex digit inside the html_sha256 value -> still valid JSON, bytes changed.
    marker = '"html_sha256":"'
    i = text.index(marker) + len(marker)
    orig = text[i]
    repl = "0" if orig != "0" else "1"
    text2 = text[:i] + repl + text[i + 1 :]
    return base64.b64encode(text2.encode("utf-8")).decode("ascii")


def main():
    global FORCE_REENCODE
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--force-reencode", action="store_true",
        help="re-encode the gzip/br compressed bodies even when the existing "
             "files decode to the expected plaintext (a diff after this is "
             "compressor variance, not semantic drift)")
    FORCE_REENCODE = parser.parse_args().force_reencode

    print("keys:")
    tp, tk, up, uk = write_keys()
    print(f"  trusted   key_id = {tk}")
    print(f"  untrusted key_id = {uk}")
    print("cases:")

    # 001 all good -> VERIFIED_MATCH
    build_case(tp, tk, "001_all_good",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               expected_conclusion="VERIFIED_MATCH")

    # 002 asset mismatch -> TAMPERING_DETECTED (client bytes differ from declared sha256)
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS,
                 client=b"console.log('TAMPERED app.js');\n")
    build_case(tp, tk, "002_asset_mismatch",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="TAMPERING_DETECTED")

    # 003 html mismatch -> TAMPERING_DETECTED
    build_case(tp, tk, "003_html_mismatch",
               html_clean=HTML_GOOD,
               html_client=HTML_GOOD.replace(b"test page", b"TAMPERED page"),
               assets=std_assets(),
               expected_conclusion="TAMPERING_DETECTED")

    # 004 invalid signature -> TAMPERING_DETECTED (flip a byte of manifest_b64 after signing)
    build_case(tp, tk, "004_invalid_signature",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               tamper_manifest_b64=flip_one_char_in_value,
               expected_conclusion="TAMPERING_DETECTED")

    # 005 malformed envelope -> INCONCLUSIVE (truncated/garbage manifest, signed correctly)
    garbage = b'{"warp_manifest_version":"WARP_MANIFEST_V1","origin_url":"https://exa'
    build_case(tp, tk, "005_malformed_envelope",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               override_manifest_bytes=garbage,
               expected_conclusion="INCONCLUSIVE")

    # 006 untrusted signer -> INCONCLUSIVE (valid sig by UNTRUSTED key, not pinned)
    build_case(up, uk, "006_untrusted_signer",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               pinned_kid=tk,  # client pins ONLY the trusted key, not uk
               expected_conclusion="INCONCLUSIVE")

    # 007 expired -> INCONCLUSIVE
    build_case(tp, tk, "007_expired",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               timestamp=NOW_MS - 600_000,
               expected_conclusion="INCONCLUSIVE")

    # 008 future dated -> INCONCLUSIVE
    build_case(tp, tk, "008_future_dated",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               timestamp=NOW_MS + 400_000,
               expected_conclusion="INCONCLUSIVE")

    # 009 nonce mismatch -> INCONCLUSIVE
    build_case(tp, tk, "009_nonce_mismatch",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               manifest_nonce="server-nonce-DIFFERENT",
               client_nonce="client-nonce-abc123",
               expected_conclusion="INCONCLUSIVE")

    # 010 origin mismatch -> INCONCLUSIVE
    build_case(tp, tk, "010_origin_mismatch",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               origin_url="https://not-example.test/",
               requested_origin=BASE_ORIGIN,
               expected_conclusion="INCONCLUSIVE")

    # 011 fetch_failed asset -> INCONCLUSIVE (declared fetch_failed, all fetchable match)
    a = std_assets()
    a.append(Asset("https://example.test/late.js", "JS", b"", fetch_failed=True))
    build_case(tp, tk, "011_fetch_failed_asset",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="INCONCLUSIVE")

    # 012 css @import closure -> VERIFIED_MATCH (HTML -> CSS -> @import nested CSS)
    main_css = b'@import url("/nested.css");\nbody{color:#111}\n'
    nested_css = b".nested{display:block}\n"
    html_css = (
        b"<!doctype html>\n<html><head>\n"
        b'<link rel="stylesheet" href="/main.css">\n'
        b"</head><body><h1>closure</h1></body></html>\n"
    )
    css_assets = [
        Asset("https://example.test/main.css", "CSS", main_css),
        Asset("https://example.test/nested.css", "CSS_IMPORT", nested_css),
    ]
    build_case(tp, tk, "012_css_import_closure",
               html_clean=html_css, html_client=html_css, assets=css_assets,
               expected_conclusion="VERIFIED_MATCH")

    # 013 gzip identity -> VERIFIED_MATCH (app.js delivered gzip, decodes to same bytes)
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS, encoding="gzip")
    build_case(tp, tk, "013_gzip_identity",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="VERIFIED_MATCH")

    # 014 br identity -> VERIFIED_MATCH (app.js delivered brotli, decodes to same bytes)
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS, encoding="br")
    build_case(tp, tk, "014_br_identity",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="VERIFIED_MATCH")

    # --- Batch 1 (2026-08-04): security holes, zero client-code change ------

    # 015 nonce stripped replay -> INCONCLUSIVE (Amendment A7: the client sent a
    # nonce; the manifest echoes null — a stripped echo is a replay signal).
    build_case(tp, tk, "015_nonce_stripped_replay",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               manifest_nonce=None, client_nonce="client-nonce-abc123",
               expected_conclusion="INCONCLUSIVE")

    # 016 mismatch outranks declared-missing -> TAMPERING_DETECTED (§5 precedence:
    # a confirmed byte mismatch must not be downgraded by a fetch_failed sibling).
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS,
                 client=b"console.log('TAMPERED app.js');\n")
    a.append(Asset("https://example.test/late.js", "JS", b"", fetch_failed=True))
    build_case(tp, tk, "016_mismatch_outranks_declared_missing",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="TAMPERING_DETECTED")

    # 017 client-side fetch error -> INCONCLUSIVE (declared with a real sha256,
    # but the CLIENT cannot fetch it; fetchError is never a skip and never a pass).
    a = std_assets()
    a[1] = Asset("https://example.test/styles.css", "CSS", STYLES_CSS, client_missing=True)
    build_case(tp, tk, "017_client_fetch_error_asset",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="INCONCLUSIVE")

    # 018 mismatch outranks client fetch error -> TAMPERING_DETECTED (§5
    # precedence on the client-side missing branch, distinct code path from 016).
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS,
                 client=b"console.log('TAMPERED app.js');\n")
    a[1] = Asset("https://example.test/styles.css", "CSS", STYLES_CSS, client_missing=True)
    build_case(tp, tk, "018_mismatch_outranks_client_fetch_error",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="TAMPERING_DETECTED")

    # 019 origin HTML unfetchable -> INCONCLUSIVE (all assets match, but the page
    # itself was never seen — asset greenness must not manufacture a match).
    build_case(tp, tk, "019_origin_html_unfetchable",
               html_clean=HTML_GOOD, html_client=None, assets=std_assets(),
               expected_conclusion="INCONCLUSIVE")

    # 020 signature wrong length -> TAMPERING_DETECTED (Amendment A2: a DECODABLE
    # signature that fails to verify — here 32 bytes instead of 64 — is
    # invalidSignature, never malformed; boundary against 026).
    build_case(tp, tk, "020_signature_wrong_length",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               override_signature=lambda sig: sig[:32],
               expected_conclusion="TAMPERING_DETECTED")

    # 021 expired AND tampered -> INCONCLUSIVE (Amendment A8: freshness failure
    # stops the sequence; bytes are never compared against a stale baseline).
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS,
                 client=b"console.log('TAMPERED app.js');\n")
    build_case(tp, tk, "021_expired_with_asset_mismatch",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               timestamp=NOW_MS - 600_000,
               expected_conclusion="INCONCLUSIVE")

    # 022 uppercase declared hex -> VERIFIED_MATCH (Amendment A5: compare
    # case-insensitively; casing must not manufacture a false mismatch).
    build_case(tp, tk, "022_declared_hash_uppercase_hex",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               hash_case="upper",
               expected_conclusion="VERIFIED_MATCH")

    # 023 wrong advisory bytes -> VERIFIED_MATCH (Amendment A6: `bytes` is
    # display-only; sha256 alone decides).
    a = std_assets()
    a[0] = Asset("https://example.test/app.js", "JS", APP_JS,
                 bytes_override=len(APP_JS) + 7)
    build_case(tp, tk, "023_bytes_field_is_advisory",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="VERIFIED_MATCH")

    # 024 empty asset body -> VERIFIED_MATCH (zero-length is a real body with a
    # real hash — e3b0c442... — not a missing one).
    a = std_assets()
    a.append(Asset("https://example.test/empty.js", "JS", b""))
    build_case(tp, tk, "024_empty_asset_body",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=a,
               expected_conclusion="VERIFIED_MATCH")

    # 025 manifest carries a nonce the client never sent -> VERIFIED_MATCH
    # (Amendment A7: no client nonce means no nonce check at all).
    build_case(tp, tk, "025_manifest_nonce_client_sent_none",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               manifest_nonce="server-issued-xyz",
               expected_conclusion="VERIFIED_MATCH")

    # --- Batch 2 (2026-08-04): envelope structural integrity (Amendment A1) --

    # 026 signature not base64 -> INCONCLUSIVE (undecodable signature is the
    # ABSENCE of a signature -> signatureState malformed, never invalidSignature;
    # transport corruption must not paint a page red. Boundary against 020.)
    build_case(tp, tk, "026_signature_not_base64",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               override_signature_b64="@@@not-base64@@@",
               expected_conclusion="INCONCLUSIVE")

    # 027 manifest_b64 not base64 -> INCONCLUSIVE (same structural tuple as 026;
    # the mirror-image field).
    build_case(tp, tk, "027_manifest_b64_not_base64",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               tamper_manifest_b64=lambda _b64: "%%%%",
               expected_conclusion="INCONCLUSIVE")

    # 028 envelope missing key_id -> INCONCLUSIVE (a missing key_id must NEVER
    # default to "the one pinned key" — that would be a trust-set bypass).
    build_case(tp, tk, "028_envelope_missing_key_id",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               omit_key_id=True,
               expected_conclusion="INCONCLUSIVE")

    # --- Batch 3 (2026-08-04): Amendment A4 — version gate, required assets, ----
    # --- verifier-side freshness ceiling                                     ----

    # 029 unsupported manifest version -> INCONCLUSIVE (validly signed by the
    # TRUSTED key, everything else clean/matching, but warp_manifest_version is
    # "WARP_MANIFEST_V2": signatureState verified, manifestState malformed — the
    # client never guesses forward-compatible semantics).
    build_case(tp, tk, "029_unsupported_manifest_version",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               manifest_version="WARP_MANIFEST_V2",
               expected_conclusion="INCONCLUSIVE")

    # 030 manifest missing assets key -> INCONCLUSIVE (A4: `assets` is REQUIRED;
    # a signed manifest that omits it must not degrade to "nothing declared, HTML
    # matched, VERIFIED_MATCH" — an absent declaration is missing data, not an
    # empty page).
    build_case(tp, tk, "030_manifest_missing_assets_key",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               omit_assets=True,
               expected_conclusion="INCONCLUSIVE")

    # 031 absurd freshness window -> VERIFIED_MATCH (A4 ceiling). The value
    # 18_446_744_073_709_552 is the SMALLEST integer whose *1000 overflows 64
    # bits (the intuitive UInt64.max//1000 = 18_446_744_073_709_551 still fits
    # when multiplied — off by 615 — and would NOT exercise the overflow path;
    # hence +1). Unclamped, Swift traps on the multiplication and C# wraps to an
    # arbitrary ~384 ms window (falsely expiring this 60 s-old manifest); with
    # the 600 s clamp applied BEFORE the multiplication both engines simply
    # conclude the matching manifest is fresh.
    build_case(tp, tk, "031_absurd_freshness_window",
               html_clean=HTML_GOOD, html_client=HTML_GOOD, assets=std_assets(),
               freshness_seconds=18_446_744_073_709_552,
               expected_conclusion="VERIFIED_MATCH")

    print("done.")


KEYS_README = """# `keys/` — TEST-ONLY Ed25519 keypairs

**These are TEST KEYS committed on purpose. Never trust them for anything real.**
They exist solely so the WARP_MANIFEST_V1 fixture corpus is reproducible and so
the untrusted-signer case has a concrete non-pinned key.

| file | meaning |
|------|---------|
| `test_signing_key.pem` | TEST signing private key (PKCS8, unencrypted). Signs every case except 006. |
| `test_signing_key.pub.pem` | its public key (SPKI PEM). |
| `test_signing_key.key_id` | `key_id` = lowercase hex of the raw 32-byte Ed25519 public key. This is the pinned key for every case. |
| `untrusted_key.pem` | TEST **untrusted** private key. Signs case 006 only. |
| `untrusted_key.pub.pem` | its public key. |
| `untrusted_key.key_id` | its `key_id`; deliberately NOT in any case's pinned set. |

Both keypairs are derived deterministically from fixed 32-byte seeds in
`_build_vectors.py`, so regenerating always yields the same `key_id`s.
"""


if __name__ == "__main__":
    main()

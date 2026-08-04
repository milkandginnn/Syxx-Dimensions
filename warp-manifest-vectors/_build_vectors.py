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

# --- Corpus-wide constants -------------------------------------------------
BASE_ORIGIN = "https://example.test/"
NOW_MS = 1754179200000          # the clock every case is evaluated against
FRESH_SKEW_MS = 300_000         # future-dated tolerance (spec §4.5)
FRESHNESS_SECONDS = 300
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

    # Step 1: base64-decode manifest_b64 -> B
    try:
        B = base64.b64decode(envelope["manifest_b64"], validate=True)
    except Exception:
        result["signatureState"] = "notEvaluated"
        result["manifestState"] = "malformed"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "manifest_b64 is not valid base64 (malformed)."
        return result

    # Step 2: resolve key_id in pinned set
    if envelope["key_id"] not in pinned:
        result["signatureState"] = "untrustedSigner"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "key_id not in the client's pinned set; never a match."
        return result

    # Step 3: Ed25519-verify signature over B, BEFORE any JSON parse
    pub = registry[envelope["key_id"]]  # pinned public key for this key_id
    try:
        pub.verify(base64.b64decode(envelope["signature_ed25519"], validate=True), B)
    except (InvalidSignature, Exception):
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
    result["manifestState"] = "parsed"

    # Step 5: freshness / origin / nonce (spec order)
    ts = manifest["timestamp"]
    if now_ms - ts > manifest["freshness_seconds"] * 1000:
        result["freshnessState"] = "expired"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "manifest older than freshness window."
        return result
    if ts > now_ms + FRESH_SKEW_MS:
        result["freshnessState"] = "futureDated"
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "manifest timestamp is future-dated beyond the 300000 ms skew."
        return result
    if manifest["origin_url"] != requested_origin:
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

    # Step 6: fetch each asset from the client vantage (= read inputs/), decode, hash
    html_bytes = (case_dir / "inputs" / "origin.html").read_bytes()
    result["htmlState"] = "match" if sha256_hex(html_bytes) == manifest["html_sha256"] else "mismatch"

    any_mismatch = result["htmlState"] == "mismatch"
    any_missing = False
    for a in manifest["assets"]:
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
        entry["state"] = "match" if sha256_hex(body) == a["sha256"] else "mismatch"
        if entry["state"] == "mismatch":
            any_mismatch = True
        result["assets"].append(entry)

    # §5 precedence: confirmed tampering outranks missing data; missing never matches.
    if any_mismatch:
        result["conclusion"] = "TAMPERING_DETECTED"
        result["notes"] = "verified+fresh manifest, but a raw-byte hash mismatch was found."
    elif any_missing:
        result["conclusion"] = "INCONCLUSIVE"
        result["notes"] = "a declared asset could not be verified; missing data is never a clean pass."
    else:
        result["conclusion"] = "VERIFIED_MATCH"
        result["notes"] = "signature verified against a pinned key, fresh, HTML and every asset matched."
    return result


# --- Case authoring --------------------------------------------------------
class Asset:
    def __init__(self, url, klass, clean, *, client=None, encoding="identity",
                 fetch_failed=False):
        self.url = url
        self.klass = klass
        self.clean = clean                       # server clean baseline bytes
        self.client = clean if client is None else client  # client-vantage bytes
        self.encoding = encoding                 # delivery encoding of client body
        self.fetch_failed = fetch_failed         # server declared fetch_failed

    def manifest_entry(self):
        if self.fetch_failed:
            return {"url": self.url, "class": self.klass, "fetch_status": "fetch_failed"}
        return {
            "url": self.url,
            "class": self.klass,
            "sha256": sha256_hex(self.clean),
            "bytes": len(self.clean),
        }


def build_case(
    priv: Ed25519PrivateKey,
    kid: str,
    name: str,
    *,
    html_clean: bytes,
    html_client: bytes,
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
    expected_conclusion: str,
):
    case_dir = ROOT / name
    inputs = case_dir / "inputs"
    inputs.mkdir(parents=True, exist_ok=True)

    # Build the inner manifest.
    manifest = {
        "warp_manifest_version": "WARP_MANIFEST_V1",
        "origin_url": origin_url,
        "final_url": origin_url,
        "html_sha256": sha256_hex(html_clean),
        "asset_classes_in_scope": ASSET_CLASSES_IN_SCOPE,
        "assets": [a.manifest_entry() for a in assets],
        "timestamp": timestamp,
        "freshness_seconds": FRESHNESS_SECONDS,
        "redirect_policy": "none",
        "allowed_redirects": [],
        "nonce": manifest_nonce,
    }

    if override_manifest_bytes is not None:
        B = override_manifest_bytes           # deliberately non-JSON (case 005)
    else:
        B = canonical_manifest_bytes(manifest)

    signature = priv.sign(B)
    manifest_b64 = base64.b64encode(B).decode("ascii")
    if tamper_manifest_b64 is not None:
        manifest_b64 = tamper_manifest_b64(manifest_b64)

    envelope = {
        "manifest_b64": manifest_b64,
        "signature_ed25519": base64.b64encode(signature).decode("ascii"),
        "key_id": kid,
    }

    # Write inputs.
    (inputs / "origin.html").write_bytes(html_client)
    fetch_meta = {}
    for a in assets:
        if a.fetch_failed:
            continue  # server couldn't fetch it; no client body to compare against
        fname = asset_filename(a.url)
        if a.encoding == "gzip":
            (inputs / fname).write_bytes(gz(a.client))
            fetch_meta[fname] = "gzip"
        elif a.encoding == "br":
            (inputs / fname).write_bytes(br(a.client))
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

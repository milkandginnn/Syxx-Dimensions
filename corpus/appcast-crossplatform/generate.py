#!/usr/bin/env python3
"""Generates the shared appcast signature-verification golden vectors.

The point of this file: the appcast is PRODUCED by the Python server
(`Server/syxx_server/appcast.py`) and CONSUMED by two independent client
implementations (macOS `SyxxKit/UpdateService.swift`, Windows
`Syxx.Kit/UpdateService.cs`). All three must agree on the exact canonical bytes
the Ed25519 signature covers. A one-byte divergence means one platform refuses
updates the other accepts, or worse, that a client verifies bytes that are not
the bytes it acts on.

So the canonical bytes here are produced by the SERVER'S OWN canonicalizer,
copied verbatim from Server/syxx_server/appcast.py:56-58, and both client test
suites assert byte-equality against them. This fixture is the only thing that
can catch that class of drift; a unit test written against each client's own
canonicalizer would be green on both while they disagree.

Regenerate:  python3 corpus/appcast-crossplatform/generate.py
"""

import base64
import hashlib
import json
import os

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "appcast-vectors.json")

# TEST-ONLY signing key, deterministic so the fixture is reproducible. This key is
# published in the repository on purpose: it must NEVER be a production update key.
TEST_SEED = bytes(range(32))
DECOY_SEED = bytes(range(100, 132))


def canon(obj):
    """Copied verbatim from Server/syxx_server/appcast.py:_canon."""
    return json.dumps(obj, separators=(",", ":"), sort_keys=True).encode("utf-8")


def base_manifest():
    return {
        "platform": "mac",
        "version": "2.1.0",
        "build": "",
        "minVersion": "1.0.0",
        "critical": False,
        "releaseNotes": "Update available.",
        "packages": [{
            "url": "https://syxx.app/dist/Syxx-2.1.0.dmg",
            "sha256": "9f" * 32,
            "size": 12345678,
            "type": "dmg",
        }],
        "publishedAt": 1753747200,
    }


def main():
    priv = Ed25519PrivateKey.from_private_bytes(TEST_SEED)
    pub_raw = priv.public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)
    pub_b64 = base64.b64encode(pub_raw).decode("ascii")

    decoy = Ed25519PrivateKey.from_private_bytes(DECOY_SEED)
    decoy_pub_b64 = base64.b64encode(
        decoy.public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)).decode("ascii")

    canonical_cases = []
    signed_cases = []

    def sign(unsigned):
        payload = canon(unsigned)
        return base64.b64encode(priv.sign(payload)).decode("ascii"), payload

    # ---- canonical-bytes vectors -------------------------------------------------
    # Each carries the RAW served text and the canonical bytes the signature covers.
    # `raw` is deliberately NOT the canonical form: sort order, whitespace and escaping
    # must all be normalised away by each client's canonicalizer.

    def add_canonical(name, unsigned, raw_text, why):
        payload = canon(unsigned)
        canonical_cases.append({
            "name": name,
            "why": why,
            "raw": raw_text,
            "canonicalUtf8Base64": base64.b64encode(payload).decode("ascii"),
            "canonicalSha256": hashlib.sha256(payload).hexdigest(),
        })

    m = base_manifest()
    sig, _ = sign(m)
    served = dict(m)
    served["signature"] = sig
    add_canonical(
        "server-shaped",
        m,
        json.dumps(served),
        "the manifest shape Server/syxx_server/appcast.py:105-120 actually emits",
    )

    # Keys deliberately out of order, pretty-printed, signature in the middle.
    reordered = json.dumps(
        {
            "version": m["version"],
            "signature": sig,
            "packages": m["packages"],
            "publishedAt": m["publishedAt"],
            "releaseNotes": m["releaseNotes"],
            "critical": m["critical"],
            "minVersion": m["minVersion"],
            "build": m["build"],
            "platform": m["platform"],
        },
        indent=2,
    )
    add_canonical(
        "unsorted-and-pretty",
        m,
        reordered,
        "same manifest served with reordered keys and indentation; canonical bytes must be identical",
    )

    # Non-ASCII: Python's json.dumps defaults to ensure_ascii=True, so the server signs
    # \uXXXX escapes. A client that emits raw UTF-8 here produces different bytes and
    # rejects every honest update. Includes an astral char (surrogate pair).
    uni = base_manifest()
    uni["releaseNotes"] = "Corrigé: réseau naïf é中文\U0001f512 done"
    sig_uni, _ = sign(uni)
    served_uni = dict(uni)
    served_uni["signature"] = sig_uni
    add_canonical(
        "non-ascii-release-notes",
        uni,
        json.dumps(served_uni, ensure_ascii=False),
        "server signs \\uXXXX escapes (ensure_ascii=True); raw served text is UTF-8",
    )

    # Escape-sensitive characters, including a forward slash (Python does NOT escape it),
    # control characters, and DEL (0x7f, which Python DOES escape).
    esc = base_manifest()
    esc["releaseNotes"] = (
        "quote:\" backslash:\\ slash:/ tab:\t nl:\n cr:\r bs:\b ff:\f "
        "ctl:\x01\x1f del:\x7f"
    )
    sig_esc, _ = sign(esc)
    served_esc = dict(esc)
    served_esc["signature"] = sig_esc
    add_canonical(
        "escape-characters",
        esc,
        json.dumps(served_esc),
        "quote/backslash/slash/control/DEL escaping must match the server byte for byte",
    )

    # Nested object with keys out of order: sort_keys is recursive.
    nested = base_manifest()
    nested_raw = dict(nested)
    nested_raw["signature"] = sign(nested)[0]
    nested_raw["packages"] = [{
        "type": "dmg",
        "size": 12345678,
        "url": "https://syxx.app/dist/Syxx-2.1.0.dmg",
        "sha256": "9f" * 32,
    }]
    add_canonical(
        "nested-keys-unsorted",
        nested,
        json.dumps(nested_raw),
        "sort_keys is recursive; package object keys must be sorted too",
    )

    # ---- signature-verification vectors ------------------------------------------

    def add_signed(name, raw_text, expect, why):
        signed_cases.append({
            "name": name,
            "why": why,
            "raw": raw_text,
            "expect": expect,
        })

    add_signed("valid", json.dumps(served), "verified",
               "signature over the exact canonical bytes")
    add_signed("valid-unsorted-served", reordered, "verified",
               "same signature, keys reordered on the wire; canonicalization must recover it")
    add_signed("valid-non-ascii", json.dumps(served_uni, ensure_ascii=False), "verified",
               "non-ASCII release notes; proves \\uXXXX parity end to end")

    # Case 2: a signature that is genuinely valid, but for DIFFERENT content.
    replay = base_manifest()
    replay["version"] = "9.9.9"
    replay["packages"] = [{
        "url": "https://syxx.app/dist/Syxx-9.9.9.dmg",
        "sha256": "aa" * 32,
        "size": 999,
        "type": "dmg",
    }]
    replay_served = dict(replay)
    replay_served["signature"] = sig  # signature from `server-shaped`, a real signature
    add_signed("replayed-signature-from-other-manifest",
               json.dumps(replay_served), "signatureMismatch",
               "a REAL signature lifted from another manifest; catches 'is there a signature' checks")

    # Case 6: version field altered after signing.
    altered = dict(served)
    altered["version"] = "9.9.9"
    add_signed("version-altered-after-signing", json.dumps(altered), "signatureMismatch",
               "one field mutated post-signing; catches implementations that verify nothing")

    # Also mutate the download URL and the artifact hash: the fields an attacker
    # actually wants to change.
    url_altered = json.loads(json.dumps(served))
    url_altered["packages"][0]["url"] = "https://evil.example/payload.dmg"
    add_signed("package-url-altered", json.dumps(url_altered), "signatureMismatch",
               "redirects the download; must not verify")

    hash_altered = json.loads(json.dumps(served))
    hash_altered["packages"][0]["sha256"] = "bb" * 32
    add_signed("package-hash-altered", json.dumps(hash_altered), "signatureMismatch",
               "swaps the artifact hash; must not verify")

    # Case 3: garbage signature, well-formed base64, correct length.
    garbage = dict(served)
    garbage["signature"] = base64.b64encode(b"\x00" * 64).decode("ascii")
    add_signed("garbage-signature-correct-length", json.dumps(garbage), "signatureMismatch",
               "well-formed base64, 64 bytes, all zeroes")

    garbage_short = dict(served)
    garbage_short["signature"] = base64.b64encode(b"short").decode("ascii")
    add_signed("garbage-signature-wrong-length", json.dumps(garbage_short), "signatureMalformed",
               "well-formed base64 but not 64 bytes")

    not_b64 = dict(served)
    not_b64["signature"] = "!!!not base64!!!"
    add_signed("signature-not-base64", json.dumps(not_b64), "signatureMalformed",
               "signature field is not decodable base64")

    # Case 4: signature empty / stripped.
    empty_sig = dict(served)
    empty_sig["signature"] = ""
    add_signed("signature-empty", json.dumps(empty_sig), "signatureMissing",
               "empty signature string")

    stripped = dict(m)  # no "signature" key at all
    add_signed("signature-field-absent", json.dumps(stripped), "signatureMissing",
               "signature field removed entirely")

    sig_wrong_type = dict(served)
    sig_wrong_type["signature"] = 12345
    add_signed("signature-not-a-string", json.dumps(sig_wrong_type), "signatureMissing",
               "signature present but not a JSON string")

    # Signed by a key that is not the pinned one.
    decoy_served = dict(m)
    decoy_served["signature"] = base64.b64encode(decoy.sign(canon(m))).decode("ascii")
    add_signed("signed-by-unpinned-key", json.dumps(decoy_served), "signatureMismatch",
               "a perfectly valid Ed25519 signature from a key that is not pinned")

    # Structurally hostile manifests.
    add_signed("not-json", "this is not json at all", "malformedManifest",
               "body is not JSON")
    add_signed("json-array-not-object", json.dumps([1, 2, 3]), "malformedManifest",
               "top-level JSON is not an object")
    add_signed("float-valued-field", '{"platform":"mac","publishedAt":1.5,"signature":"' + sig + '"}',
               "malformedManifest",
               "non-integer number: no cross-platform-stable canonical form, so refuse")

    doc = {
        "note": (
            "Generated by corpus/appcast-crossplatform/generate.py. Canonical bytes come from "
            "the SERVER'S canonicalizer (Server/syxx_server/appcast.py:_canon), so both clients "
            "are checked against the real producer rather than against each other's assumptions."
        ),
        "testPublicKeyBase64": pub_b64,
        "decoyPublicKeyBase64": decoy_pub_b64,
        "canonicalVectors": canonical_cases,
        "signatureVectors": signed_cases,
    }

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("wrote %s (%d canonical vectors, %d signature vectors)"
          % (OUT, len(canonical_cases), len(signed_cases)))


if __name__ == "__main__":
    main()

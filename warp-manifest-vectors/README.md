# `warp-manifest-vectors/` — WARP_MANIFEST_V1 signed-manifest fixture corpus

Cross-language **oracle** for the signed-manifest content-integrity protocol.
The C# and Swift client tests load this one corpus to prove they reach
**identical** conclusions from identical inputs. (A Python server-side loader is
planned but has not landed yet — as of 2026-08-04 the server does not load this
corpus; do not claim three-way arbitration until it does.)

The frozen contract is [`docs/warp-manifest-v1-spec.md`](../docs/warp-manifest-v1-spec.md).
If a case here and the spec ever disagree, **the spec wins** and the case is a
bug — regenerate with `_build_vectors.py` and reconcile. This directory is the
`warp-manifest-vectors/` named in §11 of that spec (it repurposes the retired
`warp-golden/` concatenation corpus).

> Terminology: the honesty rule bans the word "verdict". Everywhere here we use
> **result** (in code) / **conclusion** (in this doc). The three conclusions are
> `VERIFIED_MATCH`, `TAMPERING_DETECTED`, `INCONCLUSIVE`.

---

## 1. What a case proves

Each `NNN_name/` directory is one end-to-end scenario. The corpus models the
protocol's **two vantages** (spec §1):

- The **server** parsed a page once, hashed the raw bytes, and emitted a
  **signed manifest** — the authenticated *clean* baseline. Those hashes live
  inside `envelope.json` (base64 + signed).
- The **client** never parses. It fetches each declared URL from *its own*
  (suspect) vantage, hashes the raw bytes, and compares. The client's fetched
  bytes are the files under `inputs/`.

So `inputs/` = **the client-vantage bytes**; the sha256 values inside the signed
manifest = **the server clean baseline**. In the "good" cases they are equal; in
the tamper cases `inputs/` deliberately differs from the declared hash.

---

## 2. The transport envelope (spec §2)

`envelope.json`:

```json
{ "manifest_b64": "<base64 of the EXACT manifest bytes that were signed>",
  "signature_ed25519": "<base64 detached Ed25519 over the base64-DECODED manifest_b64 bytes>",
  "key_id": "<hex id of the signing pubkey>" }
```

**Canonical inner-manifest serialization — byte-for-byte:**

```python
B = json.dumps(manifest, separators=(",", ":"), sort_keys=True,
               ensure_ascii=False).encode("utf-8")
manifest_b64      = base64.b64encode(B)              # the transmitted bytes
signature_ed25519 = base64.b64encode(Ed25519_sign(private_key, B))   # detached, over B
```

- **The signed bytes are `B`** — the base64-**decoded** `manifest_b64`, i.e. the
  exact transmitted bytes. No JCS, no re-serialization. The client verifies over
  `B` and **MUST NOT re-serialize** the parsed object.
- **`key_id`** = lowercase hex of the **raw 32-byte Ed25519 public key**. (The
  production server uses `leak.collector_key_id()`; the corpus fixes the id to
  raw-pubkey-hex so all three languages compute it identically. See §9 note.)

The inner manifest, once verified and parsed (spec §3):

```json
{ "warp_manifest_version": "WARP_MANIFEST_V1",
  "origin_url": "https://example.test/",
  "final_url":  "https://example.test/",
  "html_sha256": "<lowercase hex over the raw HTML bytes>",
  "asset_classes_in_scope": ["HTML","JS","CSS","CSS_IMPORT"],
  "assets": [
    {"url":"https://example.test/app.js","class":"JS","sha256":"<hex>","bytes":36},
    {"url":"https://example.test/late.js","class":"JS","fetch_status":"fetch_failed"}
  ],
  "timestamp": 1754179140000,
  "freshness_seconds": 300,
  "redirect_policy": "none",
  "allowed_redirects": [],
  "nonce": null }
```

`timestamp` is **integer milliseconds** since epoch. An asset with
`fetch_status:"fetch_failed"` carries **no `sha256`** and is recorded, never
dropped — it forces `INCONCLUSIVE`.

---

## 3. Case directory layout

| File | Meaning |
|------|---------|
| `inputs/origin.html` | The client-vantage raw HTML bytes for the requested origin. |
| `inputs/<host>__<path>` | Each asset's client-vantage raw body (see §4 naming). For a `gzip`/`br` case this holds the **encoded** transport bytes. |
| `envelope.json` | `{manifest_b64, signature_ed25519, key_id}` — exactly as §2. |
| `pinned_keys.json` | `{"trusted_key_ids": [...]}` — key_ids the client treats as trusted **for this case**. |
| `requested_origin` | The origin URL the client asked for (text, one line). Compared against the manifest's `origin_url`. |
| `now_ms` | The clock value (integer ms, text) to evaluate freshness against. |
| `client_nonce` | Present **only** when the client sent a nonce; the nonce string. |
| `fetch_meta.json` | Present **only** when some asset body is transfer-encoded; maps `inputs` filename → `"gzip"`/`"br"`. Absent ⇒ every body is `identity`. |
| `expected.json` | The expected result: conclusion + all states (see §6). Produced by the reference evaluator, never hand-written. |

---

## 4. Asset URL → `inputs/` filename mapping

A filename cannot hold a full URL, so each declared asset `url` maps to an
`inputs/` filename by this rule (identical in every language):

```
filename = url.host + "__" + url.path            # leading "/" stripped
                                                  # remaining "/" -> "__"
                                                  # empty/"/" path -> "index"
```

Examples:

| Asset URL | `inputs/` filename |
|-----------|--------------------|
| `https://example.test/app.js` | `example.test__app.js` |
| `https://example.test/styles.css` | `example.test__styles.css` |
| `https://cdn.example.test/vendor.js` | `cdn.example.test__vendor.js` (cross-origin) |
| `https://example.test/a/b/c.js` | `example.test__a__b__c.js` |

The origin HTML is always `inputs/origin.html`. A `fetch_status:"fetch_failed"`
asset has **no** `inputs/` file (the server itself could not fetch it).

---

## 5. Transfer-encoding decode (spec §8)

Hashing is over **envelope-decoded raw bytes**. For a `gzip`/`br` asset the
`inputs/` file holds the *encoded* transport body; the loader must decode it
**before** hashing:

```
enc  = fetch_meta.get(filename, "identity")
body = gunzip(raw)            if enc == "gzip"
body = brotli_decompress(raw) if enc == "br"
body = raw                    otherwise
compare  sha256_hex(body) == declared_sha256
```

Cases `013`/`014` prove that gzip- and brotli-delivered bytes decode to the same
plaintext and therefore the same sha256 as the identity delivery. **Both sides
MUST support br** (spec §8) — deterministic.

---

## 6. `expected.json` — the result schema

```json
{ "conclusion": "VERIFIED_MATCH | TAMPERING_DETECTED | INCONCLUSIVE",
  "signatureState": "verified | invalidSignature | untrustedSigner | malformed | notEvaluated",
  "manifestState": "parsed | malformed | notEvaluated",
  "freshnessState": "fresh | expired | futureDated | originMismatch | replayNonceMismatch | notEvaluated",
  "htmlState": "match | mismatch | notEvaluated",
  "assets": [ { "url": "...", "class": "...", "state": "match | mismatch | fetchError | fetchFailedDeclared" } ],
  "notes": "human-readable rationale" }
```

`notEvaluated` means the mandatory sequence stopped before reaching that step
(e.g. an untrusted signer is never freshness-checked). `assets` is `[]` whenever
the manifest was never parsed (untrusted / invalid-signature / malformed).

`signatureState: malformed` (spec Amendment A1, 2026-08-04) means the transport
**envelope** was structurally broken — unparseable JSON, a missing
`manifest_b64`/`signature_ed25519`/`key_id` string field, or an undecodable
base64 field. That is the *absence* of a signature, never `invalidSignature`:
transport corruption is INCONCLUSIVE, not tampering. A **decodable** signature
that fails to verify (including wrong length) is `invalidSignature` →
TAMPERING_DETECTED (Amendment A2; cases 020 vs 026 pin the boundary).

---

## 7. The mandatory sequence + conclusion lattice (spec §4–§5)

Every loader MUST reproduce this exact order and land on the same conclusion.
`_build_vectors.py :: evaluate()` is the executable reference:

0. *(spec Amendment A1, 2026-08-04)* Envelope structural integrity: the envelope must be parseable
   JSON carrying `manifest_b64`, `signature_ed25519`, and `key_id` as strings, and **both** base64
   fields must decode. Any structural failure ⇒ `signatureState: malformed`,
   `manifestState: notEvaluated` → INCONCLUSIVE, stop.
1. base64-decode `manifest_b64` → `B` (structurally guaranteed by step 0; the decode itself happens
   here in the sequence).
2. Is `key_id` in `pinned_keys.json.trusted_key_ids`? **No ⇒ `untrustedSigner` → INCONCLUSIVE, stop.** (Never a match, even if the signature is cryptographically valid — the D011 rule.)
3. Ed25519-verify `signature_ed25519` over `B`, **before any JSON parse**. Fail — including a
   decoded signature of the wrong length (Amendment A2) ⇒ `invalidSignature` → **TAMPERING_DETECTED**, stop.
4. `json.parse(B)`. Unparseable ⇒ `malformed` → INCONCLUSIVE, stop.
5. Freshness, in order — any failure ⇒ INCONCLUSIVE, stop:
   - `now_ms - timestamp > freshness_seconds*1000` ⇒ `expired`
   - `timestamp > now_ms + 300000` (skew) ⇒ `futureDated`
   - `origin_url != requested_origin` ⇒ `originMismatch`
   - client sent a nonce and `nonce` ≠ it ⇒ `replayNonceMismatch` — **including when the manifest
     `nonce` is null** (a stripped echo is a replay signal; Amendment A7, cases 015/025). If the
     client sent no nonce, there is no nonce check at all.
6. For each asset: fetch from the client vantage (= read the `inputs/` file),
   decode (§5), hash, compare to declared `sha256`. Hash the HTML too.
   - **any** raw-byte mismatch (HTML or asset) ⇒ **TAMPERING_DETECTED**
   - else **any** `fetchFailedDeclared`/`fetchError` ⇒ INCONCLUSIVE
   - else ⇒ **VERIFIED_MATCH**

**Precedence (spec §5):** confirmed tampering (a real byte mismatch under a
verified+fresh manifest, or an invalid signature) outranks missing data; missing
data never upgrades to a match.

---

## 8. Case manifest

Base origin for every case: `https://example.test/`. Evaluation clock
`now_ms = 1754179200000` (fresh manifests are stamped 60 s earlier).

| Case | Conclusion | Proves |
|------|------------|--------|
| `001_all_good` | VERIFIED_MATCH | Trusted sig, fresh, HTML + all assets (incl. one cross-origin) match. |
| `002_asset_mismatch` | TAMPERING_DETECTED | One asset's client bytes differ from the declared sha256. |
| `003_html_mismatch` | TAMPERING_DETECTED | `html_sha256` differs from the client HTML bytes. |
| `004_invalid_signature` | TAMPERING_DETECTED | One byte of `manifest_b64` flipped **after** signing; decoded bytes still parse as JSON but the signature fails. |
| `005_malformed_envelope` | INCONCLUSIVE | Truncated (unparseable) manifest bytes, **correctly signed by the trusted key** — signature verifies, JSON parse fails ⇒ `malformed`. |
| `006_untrusted_signer` | INCONCLUSIVE | Cryptographically valid signature by the **untrusted** key whose `key_id` is not pinned ⇒ `untrustedSigner`; explicitly NOT a match. |
| `007_expired` | INCONCLUSIVE | `timestamp` older than the freshness window. |
| `008_future_dated` | INCONCLUSIVE | `timestamp` beyond `now_ms + 300000` skew. |
| `009_nonce_mismatch` | INCONCLUSIVE | Client sent a nonce; the manifest echoes a different one. |
| `010_origin_mismatch` | INCONCLUSIVE | `origin_url` ≠ the origin the client requested. |
| `011_fetch_failed_asset` | INCONCLUSIVE | A declared asset is `fetch_failed` (no sha256); every fetchable asset matches — missing data blocks a clean pass. |
| `012_css_import_closure` | VERIFIED_MATCH | HTML → CSS → `@import` nested CSS (class `CSS_IMPORT`) all declared and matching — transitive-closure coverage. |
| `013_gzip_identity` | VERIFIED_MATCH | An asset delivered `gzip` decodes to the same bytes ⇒ same sha256. |
| `014_br_identity` | VERIFIED_MATCH | An asset delivered `br` (brotli) decodes to the same bytes ⇒ same sha256. |
| `015_nonce_stripped_replay` | INCONCLUSIVE | Client sent a nonce; manifest echoes **null** ⇒ `replayNonceMismatch` (Amendment A7 — a stripped echo is a replay signal, not a pass). |
| `016_mismatch_outranks_declared_missing` | TAMPERING_DETECTED | One asset mismatches AND another is `fetch_failed` — §5 precedence: the confirmed mismatch is never downgraded by the missing sibling. |
| `017_client_fetch_error_asset` | INCONCLUSIVE | An asset declared with a real sha256 cannot be fetched by the CLIENT ⇒ `fetchError` — never a skip, never a pass. |
| `018_mismatch_outranks_client_fetch_error` | TAMPERING_DETECTED | §5 precedence on the client-side missing branch (distinct code path from 016). |
| `019_origin_html_unfetchable` | INCONCLUSIVE | No client-vantage HTML at all (`htmlState: fetchError`) while every asset matches — asset greenness cannot manufacture a page match. |
| `020_signature_wrong_length` | TAMPERING_DETECTED | Valid-base64 signature of 32 bytes (not 64) ⇒ `invalidSignature` (Amendment A2) — the decodable boundary against 026. |
| `021_expired_with_asset_mismatch` | INCONCLUSIVE | Expired manifest AND tampered bytes — freshness short-circuit (Amendment A8): content is never compared against a stale baseline. |
| `022_declared_hash_uppercase_hex` | VERIFIED_MATCH | Declared digests UPPERCASE ⇒ still a match (Amendment A5: compare case-insensitively). |
| `023_bytes_field_is_advisory` | VERIFIED_MATCH | Wrong `bytes` value with a correct sha256 ⇒ still a match (Amendment A6: `bytes` is display-only). |
| `024_empty_asset_body` | VERIFIED_MATCH | Zero-length asset body with the empty-input sha256 (`e3b0c442…`) — empty is a real body, not a missing one. |
| `025_manifest_nonce_client_sent_none` | VERIFIED_MATCH | Manifest carries a nonce the client never sent ⇒ no nonce check at all (Amendment A7). |
| `026_signature_not_base64` | INCONCLUSIVE | Undecodable `signature_ed25519` ⇒ `signatureState: malformed` (Amendment A1) — the absence of a signature, never `invalidSignature`. |
| `027_manifest_b64_not_base64` | INCONCLUSIVE | Undecodable `manifest_b64` ⇒ same structural tuple as 026 (Amendment A1). |
| `028_envelope_missing_key_id` | INCONCLUSIVE | Envelope lacks `key_id` ⇒ `malformed` (Amendment A1) — a missing key_id must never default to a pinned key (trust-set bypass). |

---

## 9. How each language loads the corpus

The loader is the same everywhere — reproduce §7 and assert your result equals
`expected.json`. Do **not** just re-hash `expected.json`; run your own client
logic (base64-decode → pin check → Ed25519 verify → parse → freshness → fetch &
compare) and check it lands on the same conclusion and states.

**Pinned key material.** The client pins **public** keys keyed by `key_id`. Load
`keys/test_signing_key.pub.pem` (and, only to exercise `006`, know that
`keys/untrusted_key`'s id is deliberately NOT pinned). Verification uses the
public key only — never a private key.

**Python (pytest, server).**
```python
env = json.loads((case/"envelope.json").read_text())
B   = base64.b64decode(env["manifest_b64"], validate=True)
pub = serialization.load_pem_public_key((ROOT/"keys/test_signing_key.pub.pem").read_bytes())
# step 2: env["key_id"] in pinned_keys.json  ->  else untrustedSigner
pub.verify(base64.b64decode(env["signature_ed25519"]), B)   # step 3, over B
manifest = json.loads(B)                                    # step 4
# steps 5-6 ... assert result == json.loads((case/"expected.json").read_text())
```

**C# (`dotnet test`, Windows client).**
```csharp
var env = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(Path.Combine(dir,"envelope.json")));
byte[] B = Convert.FromBase64String(env.manifest_b64);
// pinned pubkey from keys/test_signing_key.pub.pem (SPKI PEM)
bool ok = Ed25519.Verify(B, Convert.FromBase64String(env.signature_ed25519), pub); // over B, before parse
var manifest = JsonSerializer.Deserialize<Manifest>(Encoding.UTF8.GetString(B));
byte[] body = enc == "gzip" ? GUnzip(raw) : enc == "br" ? BrotliDecode(raw) : raw;
string h = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
// reproduce §7; assert conclusion/states == expected.json
```
(`System.Security.Cryptography.SHA256`, `GZipStream`, `BrotliStream`; Ed25519
via BouncyCastle or `NSec`.)

**Swift (`swift test`, macOS client).**
```swift
let env = try JSONDecoder().decode(Envelope.self, from: Data(contentsOf: dir/"envelope.json"))
let B   = Data(base64Encoded: env.manifest_b64)!
let pub = try Curve25519.Signing.PublicKey(rawRepresentation: pinnedRawKeyBytes) // from key_id / pub.pem
let ok  = pub.isValidSignature(Data(base64Encoded: env.signature_ed25519)!, for: B) // over B
let manifest = try JSONDecoder().decode(Manifest.self, from: B)
let body = enc == "gzip" ? gunzip(raw) : enc == "br" ? brotliDecode(raw) : raw
let h = SHA256.hash(data: body).map { String(format:"%02x",$0) }.joined()
// reproduce §7; assert conclusion/states == expected.json
```
(CryptoKit `Curve25519.Signing` + `SHA256`; gzip/br via the platform
`Compression` framework — brotli is native there as `COMPRESSION_BROTLI`,
available macOS 12+, so no libbrotli dependency is needed. Spec Amendment A10,
2026-08-04.)

> Note (§9, flagged to the lead): `key_id` here is raw-pubkey-hex, chosen so the
> id is trivially recomputable in all three languages. The production
> `leak.collector_key_id()` may use a different scheme (e.g. a hash of the SPKI).
> When the real pinning lands, either align `collector_key_id()` to raw-pubkey-hex
> or regenerate the corpus with the production scheme. The corpus does not depend
> on the *scheme*, only on `key_id` being a stable label matched against the
> pinned set.

---

## 10. Regenerating

```bash
python3 warp-manifest-vectors/_build_vectors.py   # stdlib + cryptography; brotli for 014
```

`_build_vectors.py` is the reproducible source of truth: it derives the TEST
keypairs from fixed seeds, writes every `inputs/` body, `envelope.json`, and the
rest, then runs the reference evaluator to emit each `expected.json` (asserting
the conclusion matches the case's declared intent). All hashes and signatures are
computed with real tools (`hashlib`, `cryptography` Ed25519, stdlib `gzip`, the
`brotli` module/CLI) — never hand-guessed. The `014_br_identity` regeneration
needs either the `brotli` Python module or the `brotli` CLI on `PATH`.

Never hand-edit a generated file; change the script and re-run. Regeneration is
idempotent: every uncompressed artifact is byte-identical on any host, and the
two compressed bodies (013/014) are kept as-is whenever they decode to the
declared bytes, because zlib and brotli encoder output is build-dependent (a
Linux zlib-ng build and a macOS zlib emit different, equally valid, streams for
the same input). `--force-reencode` re-encodes them; a diff after that is
compressor variance, not semantic drift — the decoded bytes, hashes, and
signatures are unchanged. CI runs the builder and fails on any `git diff`.

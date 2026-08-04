# WARP_MANIFEST_V1 — signed-manifest content-integrity protocol (frozen contract)

- **Status:** Frozen contract. Supersedes `docs/warp-assembly-v1-spec.md` (raw-concatenation, retired).
- **Why:** a 5-agent parity+security audit proved three hand-written HTML parsers / URL resolvers cannot
  hold byte-parity (11 divergence classes; even C# vs Swift disagreed) and that the prior response was
  unsigned (a MITM could forge the declared hash into a false match). This protocol moves ALL parsing +
  URL resolution to ONE trusted side (the server), so cross-language divergence is structurally
  impossible, and authenticates the reference with Ed25519.
- **Governing honesty rule (unchanged):** content bytes are compared RAW and never edited; the diff is a
  display overlay; the word "verdict" is banned (`result` in code, `conclusion` in UI/docs).

## 1. Roles
- **Server (only parser):** fetches the page, parses HTML once, resolves the transitive static-asset
  closure, hashes each asset's raw bytes, emits a signed manifest.
- **Client (no parser):** verifies the signature over the received bytes, checks freshness, fetches the
  declared URLs from ITS OWN vantage, hashes raw bytes, compares to the manifest, applies the lattice.
  The client never parses HTML and never resolves a URL. This preserves the two-vantage threat model:
  the manifest is the authenticated *clean* baseline; the client's fetch is the *suspect* vantage.

## 2. Wire — transport envelope
`GET /api/warp/manifest?url=<origin>&nonce=<optional>` (Pro-gated identically to existing warp routes):
```json
{ "manifest_b64": "<base64 of the EXACT manifest bytes that were signed>",
  "signature_ed25519": "<base64 detached Ed25519 over the base64-DECODED manifest_b64 bytes>",
  "key_id": "<hex id of the signing pubkey, matched against the client's pinned set>" }
```
- Sign the **exact transmitted bytes** (detached Ed25519, collector key via `leak.sign_content`). NO JCS,
  NO re-serialization. The producer serializes the manifest once (`json.dumps(..., separators=(",",":"),
  sort_keys=True, ensure_ascii=False)` → UTF-8), base64s it into `manifest_b64`, and signs those decoded
  bytes. The client verifies over the decoded bytes and MUST NOT re-serialize.

## 3. Wire — inner manifest (JSON inside `manifest_b64`, parsed ONLY after signature verify)
```json
{ "warp_manifest_version": "WARP_MANIFEST_V1",
  "origin_url": "<URL the client requested>",
  "final_url":  "<server's post-redirect URL that was parsed>",
  "html_sha256": "<lowercase hex over envelope-decoded raw HTML bytes>",
  "asset_classes_in_scope": ["HTML","JS","CSS","CSS_IMPORT"],
  "assets": [
    { "url":"<abs requested URL>", "final_url":"<abs post-redirect URL>",
      "class":"JS", "sha256":"<hex>", "bytes":1234 },
    { "url":"<abs>", "class":"CSS", "fetch_status":"fetch_failed" }
  ],
  "timestamp": 1754179200000,
  "freshness_seconds": 300,
  "redirect_policy": "same-origin",
  "allowed_redirects": [],
  "nonce": "<echo of client nonce, or null>" }
```
Rules:
- `timestamp` = **integer milliseconds** since epoch, truncated (same convention as `WitnessSigner`).
- `class` is an **opaque display string** to the client; only `asset_classes_in_scope` defines coverage.
- An asset with `fetch_status:"fetch_failed"` and **no `sha256`** is recorded, never dropped — it forces
  `INCONCLUSIVE` on the client, never a clean pass.
- The manifest MUST cover the full transitive closure (HTML + JS + CSS + CSS `@import`, deduped,
  depth-bounded). Initial-load only (no JS execution) — a deliberate, declared boundary.

## 4. Client verification order (MANDATORY sequence)
1. base64-decode `manifest_b64` → `B`.
2. Resolve `key_id` in the **pinned key set** (§7). Unknown key → `signatureState = untrustedSigner`.
3. Ed25519-verify `signature_ed25519` over `B` — **before any JSON parse**. Fail → `invalidSignature`.
4. Only now `json.parse(B)`. Unparseable → `malformed`.
5. Freshness: reject if `now_ms - timestamp > freshness_seconds*1000` (`expired`), or `timestamp` is
   future-dated beyond a 300 000 ms skew (`futureDated`); assert `origin_url` == the requested origin
   (`originMismatch`); if the client sent a nonce, assert `nonce` echoes it (`replayNonceMismatch`).
6. Fetch each asset's `url` from the client vantage under `redirect_policy` (§6); hash raw bytes; compare.

## 5. Conclusion lattice (fail-closed; one pure function)
- **TAMPERING_DETECTED** — signature is present but does not verify (`invalidSignature`), OR (signature
  verified AND fresh) the HTML hash mismatches, OR any fetched asset hash mismatches. Positive interference
  evidence.
- **INCONCLUSIVE** — `untrustedSigner` / `malformed` / signature unreachable; OR any freshness failure; OR
  (verified+fresh) a declared asset cannot be fetched though all fetched assets match. Missing/unverifiable
  data never becomes a clean pass, and an ambiguous failure is never labelled "tampering".
- **VERIFIED_MATCH** — signature verified against a pinned key, fresh, HTML matches, and every declared
  asset was fetched and matched.
- Precedence: confirmed tampering (a real byte mismatch under a verified manifest, or an invalid signature)
  outranks missing data; missing data never upgrades to match.

## 6. Redirect policy (part of the protocol, fail-closed)
`redirect_policy ∈ {none, same-origin, list}`. The server records each asset's requested `url` and its
post-redirect `final_url` (every hop SSRF-re-validated server-side). The client, fetching an asset:
- `none` → do not follow redirects; a redirect → asset `fetchError` → INCONCLUSIVE.
- `same-origin` → follow only same scheme+host+port hops; terminal URL must equal `final_url`.
- `list` → follow only hops whose targets are in `allowed_redirects`; terminal must equal `final_url`.
Any deviation (unexpected hop, host/scheme change, terminal ≠ `final_url`) → the asset is `fetchError`,
never hash-compared → INCONCLUSIVE. Fail closed.

## 7. Signing key + pinning (security-critical)
- Server signs with the **collector Ed25519 key** (`leak.sign_content`); `key_id = leak.collector_key_id()`.
- The client pins the trusted key(s) **out of band** via a new `WarpManifestPinning` mirroring
  `WitnessPinning` (env `SYXX_WARP_MANIFEST_KEYS` + a config/key-file). `/api/pubkey` is bootstrap/diagnostic
  ONLY — it is fetched over the same channel an attacker may control, so it is NEVER the trust root.
- A valid signature by a **non-pinned** key → `untrustedSigner` → INCONCLUSIVE, never match (the D011 rule
  already enforced for Witness/Glutton: valid-but-untrusted must not render green).
- **Owner deliverable:** distribute/bake the pinned manifest key into clients, exactly as Witness keys are.

## 8. Hashing (both sides identical, trivially)
- Hash SHA-256 over **envelope-decoded raw bytes** (un-gzip / br / deflate to plaintext; both sides MUST
  support br — deterministic). Never charset-decode, trim, or normalize the hashed bytes. Lowercase hex.
- `html_sha256` over the raw HTML; per-asset `sha256` over each raw asset body.

## 9. Diff overlay (owner UX; display only)
On an asset `state == mismatch`, the client fetches the server's clean copy of that ONE asset via
`GET /api/warp/raw?url=<abs>&raw=1` — a new byte-exact mode returning `application/octet-stream` +
`X-Syxx-Warp-Sha256` (the existing text mode utf-8-replaces bytes and must NOT be used for this). Render
side-by-side via the existing `TextDiff` engine. The overlay never feeds the lattice and never mutates bytes.

## 10. Conformance (server parser is the single ground truth)
Because the client no longer parses, server-vs-client parser divergence is impossible; the only residual is
server-parser-vs-browser **coverage** (does the manifest enumerate what a browser loads). Validate the
server enumerator against vendored **html5lib-tests** (HTML) and **web-platform-tests** `url/`+`fetch/`,
producing a measured conformance report + an **xfail ledger** (stdlib `html.parser`/`urljoin` cannot be
100% spec-conformant; the ledger is the honest statement of the coverage gap).

## 11. Retirement
Delete: the raw-concatenation assembler (server `warp.py` blob/seam/subtree machinery + client
`ContentWarp` assembler/scanner), `WARP_ASSEMBLY_V1` and its `warp-golden/` concatenation corpus, the
`/api/warp/assembly` route (one deprecation release), the legacy `build_baseline`/`comparePageContent`
paths, and the test that enshrined trusting an unsigned declared hash. Repurpose the corpus into a
signed-manifest fixture corpus (`warp-manifest-vectors/`). Reuse: SSRF fetcher, `WitnessSigner` Ed25519
verify + pinning discipline, `TextDiff`, `DimensionClient`, `ResolverConsensusStore`.

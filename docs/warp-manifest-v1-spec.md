# WARP_MANIFEST_V1 — signed-manifest content-integrity protocol (frozen contract)

- **Status:** Frozen contract. Supersedes `docs/warp-assembly-v1-spec.md` (raw-concatenation, retired).
  Amended 2026-08-04 — the original normative text below is unchanged; every amendment is recorded
  in the [Amendments — 2026-08-04](#amendments--2026-08-04) changelog section at the end, with inline
  "(amended 2026-08-04)" pointers at each affected section.
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

*(amended 2026-08-04 — see Amendment A6: `bytes` is advisory/display-only and MUST NOT participate
in comparison.)*

## 4. Client verification order (MANDATORY sequence)

*(amended 2026-08-04 — see Amendments A1 (new step 0: envelope structural integrity), A2 (decodable
signature that fails to verify is `invalidSignature`), A7 (nonce asymmetry made explicit), and A8
(freshness short-circuit is deliberate).)*

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

*(amended 2026-08-04 — see Amendments A3 (`malformed` added to the signature-state vocabulary, on the
INCONCLUSIVE side) and A9 (escalation-telemetry note on repeated soft failures).)*

## 6. Redirect policy (part of the protocol, fail-closed)

`redirect_policy ∈ {none, same-origin, list}`. The server records each asset's requested `url` and its
post-redirect `final_url` (every hop SSRF-re-validated server-side). The client, fetching an asset:

- `none` → do not follow redirects; a redirect → asset `fetchError` → INCONCLUSIVE.
- `same-origin` → follow only same scheme+host+port hops; terminal URL must equal `final_url`.
- `list` → follow only hops whose targets are in `allowed_redirects`; terminal must equal `final_url`.
Any deviation (unexpected hop, host/scheme change, terminal ≠ `final_url`) → the asset is `fetchError`,
never hash-compared → INCONCLUSIVE. Fail closed.

*(amended 2026-08-04 — see Amendment A11: corpus-coverage note for this section.)*

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

*(amended 2026-08-04 — see Amendments A5 (hex casing pinned: emit lowercase, compare
case-insensitively) and A10 (brotli availability on Apple platforms).)*

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

## Amendments — 2026-08-04

Changelog of every amendment applied 2026-08-04. The original normative text above is preserved
verbatim; where an amendment refines it, the amendment text below is normative. Amendment **A4 is
reserved** (deferred to a later wave; the number is kept so cross-references stay stable).

### A1 — §4 new step 0: envelope structural integrity

Before step 1, the client MUST check the transport envelope's structure. If the envelope is not
parseable JSON, or lacks any of `manifest_b64` / `signature_ed25519` / `key_id` as strings, or either
base64 field is undecodable, then: `signatureState = malformed`, `manifestState = notEvaluated`,
conclusion **INCONCLUSIVE**. Rationale: this is the **absence** of a signature, not a signature that
failed — §5 forbids labelling an ambiguous failure "tampering"; transport corruption must not paint a
page red. This aligns the reference evaluator to the two shipped engines (`WarpEngine.cs`,
`WarpEngine.swift`), the only direction that keeps the shipped clients conformant. Pinned by corpus
cases 026/027/028.

### A2 — §4.3 clarification: decodable-but-failing signature

A **decodable** signature that does not verify over `B` — including a signature of the wrong length
(e.g. 32 bytes instead of 64) — is `invalidSignature` → **TAMPERING_DETECTED**. Only an
*undecodable* signature field is `malformed` (A1). The boundary is pinned by corpus case 020
(decodable wrong-length → `invalidSignature`) vs 026 (undecodable → `malformed`).

### A3 — §5 vocabulary: `malformed` signature state

`malformed` is added to the signature-state vocabulary, sitting with `untrustedSigner` on the
INCONCLUSIVE side of the lattice. It was always a member of both shipped client enums; this
amendment makes it part of the contract and of the corpus `expected.json` schema.

### A5 — §8 hex casing

Declared hex digests (`html_sha256`, per-asset `sha256`, `key_id`) are **emitted lowercase** and
**compared case-insensitively** (the client lowercases the declared value before comparing). An
uppercase declared digest MUST NOT produce a false mismatch. Pinned by corpus case 022.

### A6 — §3 `bytes` is advisory

The per-asset `bytes` field is advisory/display-only. It MUST NOT participate in comparison: a wrong
`bytes` value with a correct `sha256` is still a match. Pinned by corpus case 023.

### A7 — §4.5 nonce asymmetry made explicit

If the client sent a nonce, the manifest `nonce` must equal it — **including when the manifest
`nonce` is null** (a stripped/absent echo of a sent nonce is `replayNonceMismatch`). If the client
sent no nonce, no nonce check is performed regardless of what the manifest carries. Pinned by corpus
cases 015 (sent nonce, null echo → `replayNonceMismatch`) and 025 (no nonce sent, manifest carries
one → no check, `fresh`).

### A8 — §4 freshness short-circuit is deliberate

A freshness failure (`expired` / `futureDated` / `originMismatch` / `replayNonceMismatch`) **stops
the sequence**: content is not fetched and not compared, and the conclusion is INCONCLUSIVE even if
the bytes would have mismatched. A content comparison against a baseline that is not authenticated
*in time* is not evidence. Pinned by corpus case 021.

### A9 — §5 escalation note (informative, not a lattice change)

A live adversary always prefers an outcome on the soft side of the lattice: `untrustedSigner`
(INCONCLUSIVE) over `invalidSignature` (TAMPERING_DETECTED). A1 widens the soft side (a garbled
signature is also soft). Therefore the operative detector for an active interception campaign is the
**frequency of `untrustedSigner` + `malformed` per origin** — repeated soft failures on one origin
deserve escalation telemetry. This is a note only; the lattice is unchanged.

### A10 — §8/§9: brotli on Apple platforms

Any implication that brotli is unavailable on Apple platforms is deleted. The platform `Compression`
framework provides `COMPRESSION_BROTLI` (macOS 12+; the Swift package targets macOS 14), so the
Swift client needs no new dependency to satisfy §8's "both sides MUST support br". Mirrored in
`warp-manifest-vectors/README.md` §9.

### A11 — §6 corpus-coverage note

Until the redirect batch lands in `warp-manifest-vectors/`, §6 (redirect policy) is covered only by
per-client injected tests (C# and Swift each carry their own), **not** cross-language arbitrated by
the shared corpus. Every current corpus case carries `redirect_policy: "none"` with no redirects,
and the production `same-origin` + per-asset `final_url` shape is not yet modelled. Deliberately
deferred; do not treat corpus greenness as §6 coverage.

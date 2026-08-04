# WARP_ASSEMBLY_V1 — the Warp raw-concatenation assembly contract

- **Status:** Frozen contract (Stream 0). Server and all clients MUST implement this identically.
- **Companion:** `docs/PROPOSAL-mirror-warp-reimagining-2026-08-03.md` (§4).
- **Governing rule (honesty protocol):** Warp **concatenates the available static web documents and
  never otherwise edits them.** Bytes are compared raw. No canonicalization, trimming, re-encoding, or
  attribute rewriting. The side-by-side diff is a **display overlay**, not an edit of the content.

The *only* thing that must be byte-identical between the Python server and the Swift/C# clients is the
**assembly recipe** below. If it is identical and no tampering occurred, the local and remote blobs hash
equal. Any divergence in this recipe would show as a false no-match, so it is pinned by golden vectors.

## 1. Inputs

Given a target page URL, each vantage independently fetches (through the SSRF-safe gateway):

1. The **initial-load HTML** — the response body of the target URL, as delivered, before any JavaScript
   executes. (We never render the DOM; we take the delivered bytes.)
2. The **external JavaScript files** referenced by that HTML: every `<script src=...>`, plus every
   `<link rel="modulepreload" href=...>` and `<link rel="preload" as="script" href=...>` (the
   modulepreload membership rule — both sides apply it identically).
3. The **initial-load CSS**: every `<link rel="stylesheet" href=...>`.

Assets injected later by JavaScript are out of scope (they are not part of the initial-load payload).

## 2. Fetch & envelope decoding

- Every fetch routes through the existing gateway (`fetcher.py` on the server; `validateURLForSSRF` +
  pinned request on clients): scheme allowlist, public-IP pinning, port allowlist 80/443, per-hop
  redirect re-validation, `FETCH_MAX_*` and aggregate-deadline caps, plus a per-run unique-host cap.
- **Envelope decoding only:** if a body arrives `Content-Encoding: gzip|br|deflate`, decompress it to
  recover the resource's actual bytes. This is decoding the transfer envelope, **not** editing content.
- **No charset decoding, no text normalization.** After envelope decompression, the file is an opaque
  **byte string**. It is never converted to text, trimmed, lowercased, or re-encoded.

## 3. File-set membership (both sides must agree)

Parse the initial-load HTML once to enumerate referenced assets. Resolve each `href`/`src` to an
absolute URL against the document's final (post-redirect) URL. Membership classes:

- `HTML` — the target document itself (always present, index 0).
- `JS` — `<script src>`, `<link rel=modulepreload>`, `<link rel="preload" as="script">`.
- `CSS` — `<link rel=stylesheet>`.

Cross-origin assets ARE included (they are part of what the page loads). A referenced asset that fails
to fetch on a vantage does not silently drop — see §6 fallback.

## 4. Ordering

The concatenation order is deterministic and identical on both sides:

1. The `HTML` document first.
2. Then all `JS` files **in document order** (the order their tags appear in the HTML source).
3. Then all `CSS` files **in document order**.

Both vantages parse the same delivered HTML, so document order is identical. (Ordering is fixed by
class then source position; it is never sorted by URL or by fetch-completion time.)

## 5. Seam & blob construction

The blob is the concatenation of, for each file in order:

```
seam(file) || raw_bytes(file)
```

where the **seam** is a fixed, deterministic header emitted as UTF-8 bytes:

```
"\n===== WARP <CLASS> <ABSOLUTE_URL> =====\n"
```

- `<CLASS>` ∈ `{HTML, JS, CSS}`. `<ABSOLUTE_URL>` is the resolved absolute URL (the target URL for the
  HTML entry). The seam contains **no timestamps, sizes, or per-request values** — only class + URL, so
  it is identical across vantages.
- The blob preimage begins with a version line: `"WARP_ASSEMBLY_V1\n"`. A future recipe change bumps
  this tag; a version mismatch between vantages yields **inconclusive**, never a false no-match. **The
  version line is present unconditionally — including the DEGRADED fallback blob (§6).**
- `blob = "WARP_ASSEMBLY_V1\n" || for each file: seam || raw_bytes`.
- `hash = SHA-256(blob)`, lowercase hex.

The client compares its own `hash` to the server's `hash` → **match / no-match**. The two blobs are
retained (raw) so the GUI can render the side-by-side diff overlay via `TextDiff`.

## 6. Fallback (degraded comparison)

If **either** vantage cannot obtain the full primary payload (the HTML itself, or any referenced CSS/JS
fails to fetch on one side so the file sets cannot match), switch **both** sides to the reduced payload:

- The DEGRADED blob **begins with the same `"WARP_ASSEMBLY_V1\n"` version line** (§5).
- The `HTML` entry is replaced by the concatenation of the document's **static subtrees** — the byte
  ranges of every `<template>` element's contents and every `<slot>` element's default content, in
  document order, each preceded by a seam `"\n===== WARP TEMPLATE <n> =====\n"` where **`n` is the
  0-based document-order index** across the combined template+slot sequence.
- Followed by the `JS` files, same recipe.
- CSS is omitted in the fallback.

The result is flagged **DEGRADED** and rendered with a visible degradation warning. Extracting a
subtree selects a byte range of the delivered document; it does not edit it, and both sides select the
same ranges by the same rule.

Never silently fail; never silently pass. If even the fallback cannot be assembled on a vantage, the
outcome is **inconclusive**.

## 7. Outcome shape (`WarpResult`, vantage-agnostic for Phase 2)

```
{
  assemblyVersion: "WARP_ASSEMBLY_V1",
  vantage: "client-local" | "server-clean" | "server-full",
  mode: "FULL" | "DEGRADED",
  files: [ { class, url, byteLen, sha256 } ],   // per-file, for localizing a diff
  blobSha256: "<hex>",
  blob: <raw bytes>,                            // retained for the overlay diff
  state: "MATCH" | "NO_MATCH" | "INCONCLUSIVE"  // set on the comparing side
}
```

Per-file `sha256` lets the diff overlay point at *which* file diverged without re-hashing. `state` is
computed on the client from `local.blobSha256 == remote.blobSha256` (with `DEGRADED`/`INCONCLUSIVE`
precedence).

## 8. Golden-vector corpus (`warp-golden/`)

Each case is a `NNN_name/` directory holding `inputs/` (the target HTML plus stub asset bodies),
`expected.blob` (the exact expected concatenated bytes), and `expected.sha256` (the expected hash as
**bare lowercase hex + trailing newline** — not `shasum -c` format, so loaders `read().strip()`). The
pytest, `swift test`, and `dotnet test` suites all load the same corpus and assert their assembler
reproduces `expected.blob` byte-for-byte and `expected.sha256`. CI fails on any drift. A committed
`_build_expected.py` (stdlib) regenerates every blob/hash deterministically. **Cross-origin asset bodies
cannot be named by full URL on disk**, so they are stored under a sanitized filename (e.g.
`cdn.example.com__x.js`) and mapped to their true absolute URL via the case/README manifest — the seam
always uses the true absolute URL, and loaders must resolve bodies via the manifest, not the filename.

Seed cases (minimum): single script; interleaved `<script>`/`<link>` in mixed document order;
`modulepreload` membership; a cross-origin script; gzip vs identity delivery of the same bytes; a
`<template>`/`<slot>` fallback; a missing-CSS fallback trigger; CRLF-containing and BOM-containing
bodies (must pass through **unchanged** — proof that we do not normalize).

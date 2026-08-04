# `warp-golden/` — WARP_ASSEMBLY_V1 golden-vector corpus

Cross-language oracle for the Warp raw-concatenation assembly contract. The
Python server, the Swift client, and the C# client each build the concatenated
blob **independently** from their own vantage's bytes; this corpus proves all
three produce the **byte-identical** blob and SHA-256 from the same inputs.

The frozen contract is `docs/warp-assembly-v1-spec.md` (§5 seam/blob, §4
ordering, §6 fallback). This directory is the `warp-golden/` referenced in §8 of
that spec. If a case here and the spec ever disagree, the spec wins and the case
is a bug — regenerate with `_build_expected.py` and reconcile.

## The recipe in one screen

```
version line   = b"WARP_ASSEMBLY_V1\n"
per-file seam  = b"\n===== WARP <CLASS> <ABSOLUTE_URL> =====\n"   # UTF-8
template seam  = b"\n===== WARP TEMPLATE <n> =====\n"             # fallback only, UTF-8
blob           = version_line  +  for each file in order: seam + raw_file_bytes
hash           = SHA-256(blob), lowercase hex
```

- `<CLASS>` is `HTML`, `JS`, or `CSS`. `<ABSOLUTE_URL>` is the resolved absolute
  URL (the target page URL for the `HTML` entry).
- **Order:** the `HTML` document first, then all `JS` in **document order**
  (order the tags appear in the HTML source), then all `CSS` in document order.
  Ordering is fixed by class then source position — never sorted by URL or by
  fetch-completion time.
- **`JS` membership:** `<script src>`, `<link rel=modulepreload>`, and
  `<link rel="preload" as="script">`. **`CSS` membership:** `<link
  rel=stylesheet>`. Cross-origin assets **are** included.
- Every seam begins with a leading `\n`, so a version line ending in `\n`
  produces one blank line before the first seam. This is intentional and part of
  the pinned bytes — do not "fix" it.
- **No normalization, ever.** After transfer-envelope decoding (gzip/br/deflate)
  the body is an opaque byte string: never charset-decoded, trimmed, lowercased,
  re-encoded, or CRLF/BOM-rewritten.

## Case layout

Each `NNN_name/` directory contains:

| File                | Meaning                                                             |
|---------------------|---------------------------------------------------------------------|
| `inputs/`           | The target `index.html` plus stub asset bodies, as raw bytes.       |
| `expected.blob`     | The exact expected concatenated bytes for the case.                 |
| `expected.sha256`   | Lowercase hex SHA-256 of `expected.blob`, followed by a newline.    |

`expected.sha256` holds **only** the 64-char hex digest + `\n` (no filename
column), so every language reads it with a trivial `read + strip`.

## Base URL and URL resolution

Unless a case says otherwise, the target page is delivered at
**`https://example.test/`**, so the `HTML` seam URL is `https://example.test/`
and relative asset hrefs resolve against that base (e.g. `app.js` →
`https://example.test/app.js`). Per-case bases and the exact seam URLs are in the
manifest below.

Cross-origin bodies are stored in `inputs/` under a filesystem-safe name (host
with `/` replaced by `__`), because a filename cannot contain a full URL. The
**seam** always carries the true absolute URL, not the on-disk filename — see
case `004`.

## Case manifest

| Case                         | Base URL                | What it pins                                                                                          |
|------------------------------|-------------------------|------------------------------------------------------------------------------------------------------|
| `001_single_script`          | `https://example.test/` | One `<script src>`. Minimal HTML→JS blob.                                                             |
| `002_interleaved`            | `https://example.test/` | `<script>`,`<link stylesheet>`,`<script>`,`<link stylesheet>` interleaved → JS-then-CSS grouping, document order **within** each class (`a.js,b.js` then `a.css,b.css`). |
| `003_modulepreload`          | `https://example.test/` | `modulepreload` + `preload as=script` both count as JS; JS order is `mod.js,main.js,pre.js` (document order across all three tag kinds). |
| `004_cross_origin`           | `https://example.test/` | Cross-origin `<script src="https://cdn.example.com/x.js">` is included; its seam URL is the absolute cross-origin URL. Body on disk: `inputs/cdn.example.com__x.js`. |
| `005_gzip_identity`          | `https://example.test/` | Gzip and identity delivery of the same bytes yield the **same** blob — envelope-decode before concatenation. `expected.blob` uses **decoded** bytes. `inputs/app.js.gz` is an illustrative gzip envelope (mtime pinned to 0) that decodes to `inputs/app.js`; loaders read `app.js`, not the `.gz`. |
| `006_template_slot_fallback` | `https://example.test/` | **DEGRADED** mode: `<template>`/`<slot>` static-subtree extraction + JS, `TEMPLATE <n>` seams (0-based), **CSS omitted**. Subtrees are the inner byte ranges of `<template>` (n=0) then `<slot>` (n=1) in document order. |
| `007_crlf_bom_passthrough`   | `https://example.test/` | A leading UTF-8 BOM (`EF BB BF`) and CRLF line endings in both the HTML and the asset body survive into `expected.blob` **unchanged** — proof of no normalization. |

## How each language loads the corpus

The loader is the same three steps everywhere:

1. Read `inputs/index.html` and each stub asset body as **raw bytes**.
2. Run the implementation's assembler with the per-case file set / mode
   (`FULL` for `001`–`005`/`007`, `DEGRADED` for `006`), the base URL above, and
   the documented seam URLs.
3. Assert the produced blob equals `expected.blob` byte-for-byte **and** its
   lowercase-hex SHA-256 equals the trimmed contents of `expected.sha256`.

**Python (pytest, server).** `blob = pathlib.Path(case/"expected.blob").read_bytes()`;
`want = (case/"expected.sha256").read_text().strip()`;
`assert hashlib.sha256(got).hexdigest() == want == hashlib.sha256(blob).hexdigest()`.

**Swift (`swift test`, macOS client).** Load with
`Data(contentsOf: caseDir.appendingPathComponent("expected.blob"))`; read the
digest with `String(contentsOf:).trimmingCharacters(in: .whitespacesAndNewlines)`;
compare `SHA256.hash(data:)` rendered as lowercase hex.

**C# (`dotnet test`, Windows client).** Load with
`File.ReadAllBytes(Path.Combine(caseDir, "expected.blob"))`; read the digest with
`File.ReadAllText(...).Trim()`; compare
`Convert.ToHexString(SHA256.HashData(got)).ToLowerInvariant()`.

Each suite asserts its **own** assembler reproduces `expected.blob`; loading the
blob only to re-hash it (without rebuilding it) would test nothing. CI fails on
any drift.

## Regenerating

```bash
python3 warp-golden/_build_expected.py    # stdlib only; rewrites inputs + expected.*
```

`_build_expected.py` is the reproducible source of truth for the bytes. It writes
every `inputs/` body, every `expected.blob`, and every `expected.sha256`, and
prints each case's digest. Regenerate after any deliberate, spec-approved recipe
change (which must also bump the `WARP_ASSEMBLY_V1` version tag) — never
hand-edit a generated `expected.blob` or `expected.sha256`.

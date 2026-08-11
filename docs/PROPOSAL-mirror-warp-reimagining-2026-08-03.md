# PROPOSAL: Reimagining the Mirror Module — Warp as the Functional Core

- **Date:** 2026-08-03
- **Status:** Design finalized (owner decisions locked, §10) — ready for build.
- **Author:** planning team (`agent-teams` planning run)
- **Scope:** Reimagine the Mirror module so it is no longer ~50% duplicative of Prophet, and make its
  real feature — **Warp** (two-vantage static-web-content integrity diffing) — fully functional.

> Framing (owner): **Mirror is the module; Warp is the feature.** This plan strips the Prophet echo
> and makes Warp the module's working payload.
>
> **Governing principle (owner, honesty protocol):** Warp **concatenates the available static web
> documents and never otherwise edits them.** Content is compared **raw, byte-for-byte**. No
> canonicalization, normalization, trimming, or re-encoding — altering the evidence before comparing or
> showing it would violate the honesty protocol and could silently erase a real injection. The
> side-by-side diff is a **display overlay** (presentation markup), not an edit of the content.

---

## 1. Problem statement

The Mirror view today is two columns, and both are broken in different ways:

- **Left — "DNS Resolver Matching" (`VantageComparer`)** re-implements Prophet's cross-resolver
  consensus with its *own* hard-coded resolver roster (kept "in lockstep by hand" with Prophet's — a
  documented drift hazard), its own tri-state, and near-identical row/conclusion UI. Prophet = symmetric
  peer consensus; Mirror = asymmetric local-vs-baseline: the *same* computation via two engines.
  This is the ~50% duplication.
- **Right — "Static Content Integrity" = Warp (`ContentWarp`)** is the module's real reason to exist,
  but is non-functional for most users: Pro-gated **and** server-gated, **entirely absent on Linux**,
  **no domain-wide discovery**, **no GUI diff**. This is the ~50% non-functional half.

**Why this tool is novel.** No consumer tool does exactly what Warp reaches for. SRI/CSP checks verify
a hash the page declares *about itself* (no independent vantage). Uptime/defacement monitors diff a
page against *its own past* (one vantage over time). DNS-tamper tools (Syxx's own Prophet) compare
*resolver answers*, not *delivered bytes*. Warp is a **two-vantage content-integrity comparator**:
fetch the same static content from the user's own (suspect) vantage and a clean off-path vantage,
**concatenate the raw documents identically on both sides** (never editing them), compare the hashes,
and overlay a human-readable diff. Comparing the *delivered bytes* from two vantages — not a
self-declared hash and not the page's own past — is the new idea.

---

## 2. Ground truth (from parallel code research)

| Warp step | Status today |
|---|---|
| 1. Identify static components on a **domain** | ❌ Absent — operates on one hand-supplied URL; no `<img>`/CSS discovery. |
| 2. Pull local + remote copies | 🟡 HTML + external/inline scripts only; **stylesheet bodies never fetched**. SSRF-safe fetchers exist (`fetcher.py`, `validateURLForSSRF`). |
| 3. Hash to compare | 🟡 SHA-256 on HTML/scripts; **not CSS**. Current code also *transforms* content before hashing (trim/charset) — the new raw model removes that (§4). |
| 4. Render a value in client | 🟡 Thin (counts + conclusion badge) on macOS/Windows; **Linux nothing**. |
| 5. Side-by-side diff in GUI | ❌ Absent in GUI; but `TextDiff.unified()` exists (CLI-only), `localHTML`/`remoteHTML` retained, `/api/warp/raw` is the server primitive built for it. |

**Reusable assets already present:** `TextDiff` engine (both platforms), `/api/warp/raw` endpoint,
`WarpResult.localHTML/remoteHTML` retention, existing two-column `MirrorView` layout, `DimensionClient`
shared HTTP contract, the tri-state conclusion pattern, and the honesty protocol
(`ProbeRunState { Idle, Running, Completed, Failed }`).

**Current-code divergences the raw model supersedes.** Today's code transforms content before hashing
and the transforms disagree across platforms, producing false mismatches. Under the raw-concatenation
model (§4) two of these simply **cease to exist**, and the third becomes a small assembly-agreement item:

1. **inline-script trim divergence** — server `str.strip()` (`warp.py:69`), Swift
   `.trimmingCharacters` (`ContentWarp.swift:593`), C# `.Trim()` (`ContentWarp.cs:552`). **Gone** — raw
   bytes are never trimmed.
2. **charset re-encode divergence** — server forces UTF-8-replace (`warp.py:137`) vs clients honoring
   declared charset (`ContentWarp.swift:351-368`). **Gone** — we concatenate raw bytes, never re-encode.
3. **modulepreload asymmetry** — clients treat `<link rel="modulepreload">` as a script
   (`ContentWarp.swift:604-607`, `ContentWarp.cs:559-568`); server only recognizes `rel="stylesheet"`
   (`warp.py:49-52`). Survives as an **assembly-set agreement** item: both sides must include the same
   files in the concatenation (§4.2).

---

## 3. Feature 1 — Unify DNS consensus (kill the duplication)

Create one **`ResolverConsensusEngine`** (Kit-level cross-platform twin) owning the single canonical
resolver roster + 12-vantage fan-out, exposing **both** a symmetric peer-consensus view (Prophet) and
an asymmetric local-vs-consensus delta (Mirror) from one query. Persist the last result + timestamp in
a **shared last-run store keyed by domain** that both modules read.

**Display split (owner-specified):**

- **Prophet keeps rendering all 12 vantages as individual rows** — it owns the detail.
- **Mirror renders no comparator rows.** The resolver-match is **not** Mirror's test — it is a
  supplementary **degradation factor** on Mirror's content conclusion, never a gate, and **Mirror never
  re-runs consensus itself.** It reads Prophet's last result + timestamp from the shared store:
  - **Prophet matched < 1 hr ago** → confirmation line, e.g. *"DNS query replies for your selected
    domain still match."* — no degradation.
  - **Prophet last ran > 1 hr ago, or never** → treat as a **Prophet-side failure**: DNS context is
    stale/unavailable, apply a mild degradation to Mirror's conclusion, and do **not** assert a match.
    (No silent re-run.)
  - **Prophet ran < 1 hr ago but replies mismatch** → degrade Mirror's content conclusion toward
    caution/inconclusive, e.g. *"Your DNS query replies for your selected server do NOT match."*
  (Exact copy catalogued/edited by owner — §7.)
- Mirror proceeds **straight to the diff feature** in all cases — the content diff is the real test.

Mirror-only pure-DNS extras (DoH cross-transport, authoritative delegation) fold into Prophet / the
shared engine. Net effect: duplicate roster, duplicate tri-state, and duplicate row UI all disappear.

**Refactor targets:** `Windows/dotNET/Syxx.Kit/ProphetConsensus.cs`, `.../VantageComparer.cs`,
`macOS/Sources/SyxxKit/VantageComparer.swift`, `macOS/Sources/SyxxApp/{ProphetView,AppModel}.swift`,
`.../ProbePlan.{cs,swift}`, plus the new `ResolverConsensusEngine` twin and shared last-run store.

---

## 4. Feature 2 — Make Warp functional (raw-concatenation content integrity)

### 4.1 The test — concatenate available static documents, compare raw, overlay a diff

Warp compares what the user's network delivers vs what a clean off-path vantage delivers, for a page's
**initial-load static payload**. On *each* vantage (client = local, server = clean):

1. Fetch the **initial-load HTML** (as delivered, before JS executes), the external **JavaScript
   files** it references, and the **initial-load CSS** (`<link rel=stylesheet>`).
2. **Un-gzip / decode the transfer encoding** to recover each file's actual bytes. This decodes the
   HTTP envelope; it is **not** editing content (§4.2).
3. **Concatenate the raw file bytes** in a fixed order with a fixed seam (§4.2). No per-file
   transformation of any kind.
4. `SHA-256` the concatenated blob.

The server delivers its hash (and its concatenated blob) to the client; the client compares
**local-hash vs remote-hash** → **match / no-match**, and renders the two blobs **side by side with a
diff overlay** so the user can see any deviation directly. Match/no-match is the machine signal; the
overlay diff is where the human judges *what* differs. **The diff is a display overlay — presentation
markup layered on the raw bytes — never an edit of the compared content.**

### 4.2 The assembly recipe — the ONLY cross-implementation contract

There is **no content canonicalization.** Files are hashed as raw bytes. The one thing that must be
byte-identical between the Python server and the Swift/C# clients is the **assembly recipe**:

- **File set:** initial-load HTML + external JS + initial-load CSS referenced by that HTML. Both sides
  must agree on membership — this is where the old `modulepreload` divergence matters: both sides make
  the same call on whether a `rel=modulepreload` link is an included JS file.
- **Order:** raw HTML first, then external JS in **document order**, then external CSS in document order.
  Both sides parse the *same* delivered HTML, so document order is identical.
- **Seam:** a fixed, deterministic header line before each file (the resource **URL only** — never a
  timestamp), so the diff reads clearly. If the server writes a seam line at a join, the client writes
  the identical bytes at the identical join.
- **Envelope decoding only:** un-gzip/brotli, nothing else. No charset re-encoding, no trimming, no
  attribute reordering — those alter content and defeat the test.

This is a *tiny* contract (which files, what order, what seam bytes) and it is the whole of what a
**golden-vector test** pins: given the same inputs, both engines must emit the **identical concatenated
blob**. There is nothing to canonicalize, so no shared "canonicalizer" library is needed.

### 4.3 Fallback — degraded comparison

If **either** vantage cannot obtain the full primary payload (e.g. the initial-load HTML or a
stylesheet will not come down on one side, so an apples-to-apples comparison is impossible), drop to a
**reduced payload**: the **static subtrees of the HTML — `<template>` and `<slot>` contents, if
present — plus the JavaScript files**, concatenated by the same raw recipe (same order, same seams, raw
bytes), hashed and compared, and rendered with a **degradation warning** so the user knows the
comparison is partial and weaker. Never silently fail; never silently pass. (Extracting a `<template>`
subtree selects a range of the delivered bytes; it does not edit them — both sides select the same
ranges by the same recipe.)

### 4.4 Reading the result — deviations are the user's call, not the machine's

Because nothing is normalized away, a benign per-request difference (a CSP nonce, a server-rendered
timestamp, a build ID embedded in the HTML) will make the hashes differ — and the overlay diff shows the
user exactly that line. This matches the original intent ("so they can *manually* see deviations"). We
may **annotate** a differing region non-destructively ("this line looks like a nonce/timestamp") to help
the user read it, but we **never mutate content or auto-suppress a difference.** For a genuine
legitimate-variance case (a CDN serving different regional builds at the same URL), the honest output is
**inconclusive**, never a forced pass. Honest-inconclusive over unearned "secure."

### 4.5 Discovery, fetch & SSRF

- Fetch the target document, then the JS and CSS **it declares** (document-order enumeration of
  `<script src>` and `<link rel=stylesheet>`; agree on `modulepreload` membership per §4.2). This closes
  today's biggest coverage gap — CSS bodies are currently enumerated by URL but never fetched
  (`warp.py:147-154` fetches only script bodies).
- **Un-gzip** on fetch; concatenate raw bytes (§4.1–4.2).
- **SSRF:** every fetch — target and each subresource — MUST route through the existing gateway (scheme
  allowlist, public-IP pinning `_PinnedHTTPSConnection` `fetcher.py:46-67`, port allowlist 80/443,
  per-hop redirect re-validation). No fetch path may bypass it. Reuse the existing `FETCH_MAX_*` /
  aggregate-deadline caps (`fetcher.py:198-200`); add a per-run unique-host cap.
- A bounded same-origin multi-page crawl is **out of scope** for this build (the test is per-page); the
  manifest/recipe is designed so it could be added later without changing the contract.

### 4.6 Cross-implementation parity — assembly only, no canonicalizer

The Python server and the Swift/C# clients each build the concatenated blob **independently** — each
must hash its own vantage's bytes, or the test measures nothing (a client that merely hashes the
server's bytes compares the clean vantage to itself and is blind to client-path tampering). The **only**
thing that must match across the three implementations is the **assembly recipe** (§4.2): file set,
order, seam bytes, envelope-decoding. Content bytes are never transformed, so the entire class of
"canonicalization drift" (whitespace/trim/charset/attribute-order) **does not exist** in this design.

Guard it with a **golden-vector test**: a corpus of `{inputs → expected concatenated blob + sha256}`
that the pytest, `swift test`, and `dotnet test` suites all load, asserting each engine emits the
**identical blob** (mirror the existing `witness-crossplatform` golden-vector pattern). Seed it with the
cases that matter here: `modulepreload` membership, document-order of interleaved `<script>`/`<link>`
tags, seam-line bytes, `<template>`/`<slot>` extraction for the fallback, and gzip vs identity delivery.
A small **assembly-spec version tag** in the blob preimage means an intentional recipe change surfaces
as inconclusive, never a false no-match. **No shared canonicalizer core is required** — there is nothing
to canonicalize.

---

## 5. Implementation plan — work streams (file ownership + dependency DAG)

| Stream | Owns (no file overlap) | Depends on |
|---|---|---|
| **0 · Assembly-recipe contract** (land FIRST) | `WARP_ASSEMBLY_V1` spec (file set + order + seam bytes + envelope-decode) + `warp-golden/` corpus (inputs → expected concatenated blob + `.sha256`), tri-language CI hookups (pytest + `swift test` + `dotnet test`), assembly-version tag; retire content-transform code paths (trim/charset); settle `modulepreload` membership | — |
| **A · Shared consensus engine** (de-dup) | new `ResolverConsensusEngine` (Kit + Swift twin) + shared last-run store; refactor `ProphetConsensus.cs`, `VantageComparer.cs/.swift`, `ProbePlan` | — |
| **B · Warp server pipeline** | `Server/syxx_server/warp.py` (declared-asset discovery, fetch HTML+JS+CSS, un-gzip, raw concatenation per recipe, SHA-256, `<template>`/`<slot>` fallback), `fetcher.py` (unique-host cap), `app.py` warp routes returning blob+hash, `openapi.yaml`/`api.html`, server tests | 0 |
| **C · Warp client engine** | `Syxx.Kit/ContentWarp.cs` + `SyxxKit/ContentWarp.swift`, `WarpResult` (raw blobs + hashes + match/degraded state), same raw-concatenation recipe, `TextDiff` reuse for the overlay | 0, B (contracts) |
| **D · Mirror UI + Warp GUI** | macOS `MirrorView.swift`; Windows `MirrorView.xaml(.cs)` + `AppViewModel`; Linux `MirrorViewModel.cs` + `MirrorView.axaml` — remove duplicate DNS column (consume shared summary), match/no-match + degradation state, **side-by-side diff overlay viewer**, **full Linux parity (Pro-gated normally once E lands)** | A, C (+ E for Linux Pro gate) |
| **E · Linux license activation (G16)** — IN SCOPE, blocks Linux Pro Warp | implement a **cross-platform secret store** (OS keyring / libsecret — no hand-rolled crypto) to replace the DPAPI-bound storage the Kit `LicenseManager` path relies on, and wire the activation API calls so `isFeatureUnlocked()` works on Linux (ROADMAP G16 / PR-10). Linux has `LicenseView.axaml(.cs)` but references the Windows Kit's DPAPI-bound `LicenseManager` | — (start immediately) |
| **F · Tests, docs, catalog, parity** | Mirror VM tests (none exist today), cross-platform assembly golden vectors, SSRF tests on new paths, `openapi`/`api.html` reconciliation, new output strings into `outputs-catalog-general-mode.csv` + `outputs-catalog-story-mode.csv`, this PROPOSAL/WORKLOG | trails A–D |

**Contracts to freeze first:** (1) `WARP_ASSEMBLY_V1` recipe (Stream 0, blocks B & C); (2) the
declared-asset manifest / fetch list (B→C); (3) the `WarpResult` raw-blob + match/degraded shape (C→D).
Streams 0, A, E parallelize immediately; B after 0; C after 0+B; D after A+C; F trails.

**Sequencing:** (1) assembly-recipe spec + golden-vector corpus + CI gate; (2) server raw pipeline
(HTML+JS+CSS fetch, un-gzip, concatenate, hash) + fallback; (3) client engine parity to the same recipe;
(4) diff overlay UI + match/degradation states; (5) Linux parity + G16. A (de-dup) and E (license) run
alongside.

---

## 6. Recommended build-team composition

Very Complex (4 platforms, server + client, new pipeline, + cross-platform secret storage) → **6 agents**,
Fullstack/Migration hybrid: `team-lead` + `team-implementer`(Server/Python = 0,B) +
`team-implementer`(Kit C#+Swift = A,C) + `team-implementer`(per-platform UI = D) +
`team-implementer`(Linux license/keystore = E, starts day one) + `team-reviewer` on the security-critical
seams (SSRF on new fetch paths, **assembly-recipe parity + envelope-decoding correctness**, honesty
compliance — no content edits, and the license secret-store). F trails.

**Lane note (`docs/CLAUDE.md`):** Warp spans all three governance lanes — Claude (golden-vector oracle /
design / Synthesis wiring), Cline (`Server/**`), Copilot (`SyxxApp`/`SyxxUI` GUI). Cross-lane build
scope needs owner direction before implementers touch another lane's files.

---

## 7. Cross-cutting requirements

- **The signal:** match / no-match on the raw concatenation of the available static documents; the
  overlay diff carries the detail for human judgement. Degraded fallback is always flagged. Content is
  never edited; the diff is a presentation overlay.
- **Honesty protocol:** every new panel respects `Idle/Running/Completed/Failed`; failed ≠ never-ran ≠
  match; never render "clean" when it didn't run; never mutate or auto-suppress evidence.
- **Output catalog:** all new user-facing Warp strings registered in both
  `outputs-catalog-general-mode.csv` and `outputs-catalog-story-mode.csv`
  (columns: `file:line,trigger,module,task,output,change`) so the owner can edit copy.

---

## 8. Top risks

1. **Assembly-recipe parity drift** — if server and client disagree on file set / order / seam bytes,
   identical content hashes differently and shows a false no-match. Small, fully testable: the
   golden-vector CI gate (Stream 0) must land first. (Far smaller than the retired canonicalization risk.)
2. **Benign per-request variance reads as no-match** — nonces/timestamps/build-IDs in the HTML. By
   design these are surfaced to the human via the overlay diff (not auto-suppressed), and legitimate
   whole-page variance reports inconclusive, never a forced pass.
3. **Discovery expands the SSRF surface** — every fetch through the existing gateway; per-run unique-host
   and total-fetch caps; no bypass path.

---

## 9. Phase 2 (next, owner-flagged) — fully remote execution: a server-side mirror of the client run

**Vision:** run the **entire** Warp check server-side — discovery, dual-fetch, raw concatenate, hash,
compare — and render it in a UI that **mirrors the client's**, so the user sees "what the world sees"
computed independently of their own device. A purely-local client run can be fooled if the client's own
resolver path is poisoned/hijacked; an independent full-remote run is a reference vantage that separates:

- **device/link tampering** — client-run deviates, server-run clean;
- **genuine origin content** — both agree;
- **whole-path / resolver poisoning of the client** — client-run reads "clean" against a locally-poisoned
  baseline, yet disagrees with the independent server-run.

**Design hooks to build into Phase 1 so Phase 2 is a new call site, not a rewrite** (freeze the §5
contracts vantage-agnostically now):

1. **Single pipeline, two call sites.** Structure the Warp pipeline (Streams B/C) so the same
   raw-concatenation recipe runs in-client and server-side — one recipe, invoked twice.
2. **Vantage-tagged result shape.** `WarpResult` carries vantage provenance for each copy
   (`client-local`, `server-clean`, `server-full`) so a result renders identically regardless of where
   it ran, and multiple vantages can be overlaid.
3. **Full-pipeline endpoint.** A server endpoint that runs the whole check for a domain and returns the
   complete vantage-tagged result (a superset of today's `/warp` + `/api/warp/raw`), shaped identically
   to the client engine's output — so the client renders a server-run with the same views.
4. **Three-vantage conclusion (future).** Reconcile client-local vs server-clean vs server-full to
   distinguish the three cases above. Phase-1 stays two-vantage; the model is left open.

Phase 2 is explicitly **not** in this build — but designing the frozen contracts (assembly recipe,
manifest, result shape) vantage-agnostically now keeps it cheap.

---

## 10. Owner decisions — all locked

- **Mirror DNS states** — ✅ resolver-match is a degradation factor on Mirror's conclusion, not a gate;
  Mirror never re-runs consensus; stale/absent Prophet (>1 hr) = Prophet-side failure (mild degradation,
  no match asserted); fresh mismatch degrades the conclusion. Owner finalizes exact copy in the catalogs.
- **Discovery breadth** — ✅ page + declared subresources now (HTML + JS + CSS, incl. CSS bodies), with a
  crawl-ready recipe; bounded same-origin crawl is later/opt-in.
- **Linux/Pro** — ✅ implement fully now — no locked/deferred state. Stream E (G16 cross-platform license
  activation) is an in-scope **blocking** stream so Linux Warp ships at true parity.
- **Content handling / "canonicalizer"** — ✅ RESOLVED: **no canonicalization.** Server and client
  **concatenate the available static documents raw and never otherwise edit them**; the only shared
  contract is the assembly recipe (§4.2), guarded by golden vectors. The diff is a display overlay, not
  an edit. Envelope decoding (un-gzip) is permitted because it recovers the actual file bytes.

# What this track did NOT cover — Task 7.4

A green result from this track's gates means what the gates measure and nothing more. This list is
the boundary, so the next reader is not misled by the green. Reachable from
`Linux/PARITY-LEDGER.md` (§ cross-platform-parity pointer) and filed with the track.

## Deliberately out of scope (spec)

- **GUI parity** (SwiftUI / WinUI / Avalonia) — separate track; a `gui-complete-macos` track ran
  concurrently and is its own record.
- **Python server parity** — single implementation, no twin.
- **Amending `docs/warp-manifest-v1-spec.md`** — verified untouched by this track (Task 7.3:
  no finding of F-01..F-26 implies a warp-manifest contract change, so no amendment proposal
  exists; the parity-vector spec's own Amendment A1 is a different, track-owned document).

## Inherently-live command surfaces — excluded by design, with reasons

The 13-entry list with per-item reasons is in [command-triage.md](./command-triage.md) (§ Task 4.4
draft): leak (both modes), witness create, witness verify --fetch-pubkey, key, route, lan,
sentinel (capture paths), cage (capture; its report generation is coverable only via
`forensics --analyze`), warp (replay nonce defeats any recorded manifest by construction), speed,
unbound (all actions), filter, firstrun. These surfaces have **no deterministic parity coverage**
and cannot gain it by fixtures; behavioral divergences there are found by the effect-surface
findings (F-14..F-26), not by vectors.

## In scope but not done by this track

- **Behavioral vectors (Tasks 4.2/4.3)** for the 7 fixture-able sub-actions — not yet authored.
  `witness verify` / `report` fixtures are **blocked on Windows/Linux by F-14** (no clock seam)
  until that code change lands.
- **v1 vector scope is 32 of 34 fields.** `resolverSubstituted` and `transportObservation` are
  rejected by both loaders (spec Amendment A1 — a scope decision, since 84e9bd7 gave C# both
  fields). The resolver-substituted Mirror cap is therefore covered by in-process twin tests only,
  not by the transcript gate. `drifts` is additionally unrepresentable in v1 (AnswerDrift carries a
  date; the format bans dates) — witness vectors use `configChanges` only.
- **The runtime matrix (windows-x64 / linux-x64 / linux-arm64 legs)** — everything verified in
  this track ran on one macOS host: Swift vs C# cross-language, both executing on arm64 macOS.
  Same-assembly-different-runtime divergence is exactly what only CI can show, and **GitHub
  Actions is refusing to start jobs (billing, C-06, owner-only)**, so every Phase 6 claim will be
  `fixed-unverified` until Actions runs again.
- **C# silent-skip audit (Task 5.3)** covered macOS/Tests exhaustively; the C# test tree got only
  the spot observation that MatrixGoldenCorpusTests already counts. A full C# sweep was not done.
- **Two known Swift test-coverage gaps, flagged and deliberately left**:
  `WitnessCrossPlatformGoldenVectorTests.swift:21-28` drives from a hardcoded three-vector list
  (a vector added to its corpus is silently not covered), and `PurpleScrollViewTests.swift:99-127`
  has no scanned-file count (a zero-file walk would pass). Both are the silent-skip class one
  level up; neither was in Phase 5's ownership.
- **D1/D2/D4/D5 capture divergences** are filed in cli-capture-report.md and gate-ledgered, but
  carry no individual F-rows; D3's functional item became F-15, and the flag-surface items became
  F-10..F-13.
- **Task 8.8** (the `-v` semantics conflict, B240 vs E088, plus the dead top-level `-V`) is filed
  with executed proof and remains **owner-gated** — resolving it changes a shipped flag's meaning.

## Verification honesty

Every "verified-by-real-run" in this track means: this macOS host, 2026-08-05, by the named seat
or the lead. Nothing in this track is `verified-by-CI` yet — see the runtime-matrix entry above.

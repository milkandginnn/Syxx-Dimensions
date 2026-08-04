# Syxx — Project State / Sync (canonical current picture)

**Updated:** 2026-07-12 · Branch `feat/beta-signup-and-server-hardening` · HEAD `f40dc58`

> **READ THIS FIRST.** Single source of truth for the reasoning-engine remediation. If anything you're about to do contradicts this doc, **stop and reconcile — do not freelance.** Fast is good; drift is not. Stay in your lane, build to the contracts, don't relitigate what's decided.

## Mission
Investigate & detect DNS tampering / hijacking / spoofing / censorship and produce evidence a user can **trust**. The reasoning engine turns Prophet / Mirror / Portal / Glutton / Cage / Witness evidence into one honest tri-state verdict.

**Explicit-path commits only** (`git diff --cached --name-only` before every commit; never `git add -A`). Need something in another lane? **Flag it — don't edit it.**

## DECIDED — do not relitigate
- **Design:** `REASONING-SYSTEM-DESIGN.md` = **v2.1, APPROVED** (2 audit blockers closed, O2/O3/O7 amendments folded).
- **Framework:** keep DST → complete as **TBM** (credal `Bel` headline; `BetP` for decisions only). **Cautious-conjunctive *within* a lineage cluster, Dempster *across* clusters** (= the existing group-merge minus the `maxWeightPerSource` cap — a *small* change). **M1** Shafer discounting for source reliability only; **M2** benign context conditions/refers, never discounts a reliable observation; **M3** completeness = separate dimension (never a `Bel` multiplier); **M4** decisive safety gate *outside* the belief number. Invariants **I1–I8** hold at every commit.
- **Contracts — build to these, don't invent parallel ones:** `STAGE5-CONTRACT.md` (engine↔UI DTO `Stage5Presentation`), `STAGE0-GOLDEN-VECTOR-CONTRACT.md` (fixture schema). Claude owns both; flag needs, don't fork them.
- **Wording:** `STAGE5-WORDING-PROPOSALS.md` **recommended set = APPROVED** (item 5 = Framing 3, "Corroboration"). New wording = proposal pending approval.
- **The fusion-rule swap is NOT open work** — deferred to the Phase-0 **bake-off RUN** (after Phase 1 lands); see `BAKEOFF-DESIGN.md`. Do not implement a fusion change yet. This pass, NE1 Δ1 lands as **inconclusive + referral**, NOT "REWRITE"-named.

## Landed (committed) — both platforms compile at HEAD `4fd5091`
- `bcf817e` **P1.0** — additive `Stage5Presentation` DTO twins + validator + byte-parity fixture.
- `f40dc58` **P0.1** — integer-% gate + away-from-zero rounding parity + deterministic DST fold.
- `daa97c4` **P0.2/P0.3** — Stage0 golden-vector parity harness (both platforms).
- `bdfc832` **Phase 1** — structural honesty gates (invalidated-dominates, critical-not-conclusive-forbids-clean, degraded discounting, `.positive`-blocks-clean, **I8** committed-threat-mass floor, min-corroboration referral). Real counts: macOS 398 / Windows 240.
- `d28c70a` **Phase 2 (P2.1/P2.2)** — M2 causal dampener + anti-cancellation: a benign marker only dampens a threat it can *causally* explain, and never cancels a confirmed one (forces referral). Closes the captive-portal-eats-a-DNSSEC-strip bug.
- `2c95d7b` **Phase 2 (P2.3)** — named M4 decision-policy region + `ValidatedDecisiveActs` scaffold (Phase 6 fills). Behavior-neutral.
- `5bec6f3` / `d262635` **P0.4 seam** — pluggable `Config.combinationRule`; `.cautious` dissolves the `maxWeightPerSource` cap (the NE1 Δ2 inversion cause). **Production stays `.dempster` — behavior unchanged.**
- `8afbf4d` **Stage-5 validator hardening (V1–V4)** — `isValid` fails closed on unsupported schemaVersion, clean-with-critical-missing, clean-with-findings, clean-with-active-conflict. Pre-emptive (stage5 still nil in production).
- `ecbe62b` **Stage-5 CX-1/CX-2 (macOS)** — `synthesize()` now populates a complete, validated `Stage5Presentation` (answer matrix + backing direct observations from `ComparisonResult`; determination mirroring the verdict with `.unresolved` mechanism; honesty signals; interim `profile.sections.v0` completeness); fails closed to nil on any §13 violation. **stage5 is NO LONGER nil in production.** `ConclusionsView` already renders it. Verified on the poisoned-local scenario (`validationErrors: []`).
- `55e765f` **Stage-5 CX-1/CX-2 (Windows)** — Syxx.Kit twin; byte-identical canonical object (3623 bytes AT THAT commit) to macOS, bit-identical DST doubles (full §11 parity). The object has since grown to **4749 bytes** as the fields below landed.
- `5e635e8` **Stage-5 provenance.unavailable** — untested/invalidated sections listed as `section.<name>` unavailable datapoints (§14 CX-1: never silently dropped). Both platforms, byte-parity re-verified.
- `3bf3048` **Stage-5 within-class (CX-3 partial)** — subordinate BetP distribution over threat hypotheses (normalized shares, deterministic order) when `state==tampered`; display-only, `mechanismClass` stays `.unresolved` (headline map still O5-gated).
- `bb915a9` **Stage-5 authoritative target plumbing** — `SectionInputs.targetDomain`/`targetQueryType`; empty-answer runs now produce a matrix row with typed statuses instead of no row; first-record fallback preserved; callers opt in where the domain is in scope.
- `4fd5091` **Stage-5 §14 parity test** — frozen 4749-byte populated-object fixture + cross-platform round-trip/semantic-anchor tests + 4 negative cases (bad schemaVersion, misspelled enum, bad within-class shares, dangling link). macOS 6/6 + Windows 6/6 pass; the manual byte-parity checks are now a permanent regression guard.

## In flight / next — THE WALL (do NOT fake past it)
Phases **1 and 2 are complete**; the P0.4 bake-off seam is laid. The **core-math** Phases 3+ are **gated on data/decisions that don't exist yet** (below). The **Stage-5 presentation track (§16, Claude's lane) is now essentially COMPLETE for everything un-gated + in-lane + sandbox-doable:** CX-1 answer matrix, CX-2 honesty, determination-state, provenance (direct + unavailable), within-class, target plumbing, and the §14 parity test all landed end-to-end on both platforms, byte-identical (4749-byte object). What remains is NOT un-gated Claude-lane work: **(a)** the finalized hypothesis→mechanism headline map + prioritized findings = Phase-4 **O5 sign-off** (below); **(b)** datapoint-level completeness (retire `profile.sections.v0`) = a contract/profile-definition decision, not a free edit; **(c)** CLI/GUI rendering of the new fields = **Copilot's** surfacing lane (see WS6-07 gating-panel directive); **(d)** the §15 real-run acceptance = a **real-machine** run (sandbox has no UDP:53). Also parked: the **witness-signing GUI failure** (CLI signs fine end-to-end; break is GUI-only, suspected Keychain-ACL/code-signature mismatch after rebuilds — needs the live app's error string).
- **Phase 3 (commit the fusion rule)** is BLOCKED on the bake-off running on a **real-capture corpus** (O1) + the **lineage map** (O4) — captured-data work in **Cline's** lane. The seam is ready; do **not** flip `combinationRule`'s default on synthetic data.
- **Phase 4 (mechanism-class partition)** needs **user sign-off** (O5 — it drives the headline number).
- **Phase 7 (calibration)** needs the labeled real corpus (O1).
- **Cline:** CL-1 signed GroundTruth, CL-2 probe robustness, **CL-3 real-capture corpus + runner** — the actual unblock for Phase 3+.
- **Copilot (GUI lane only):** wire approved copy → `Conclusion.stage5`; render absent/invalid `stage5` as unavailable. The engine gates on `isValid` (V1–V4) — do NOT reach into engine internals or edit `Synthesis/`.

## Standing rules (binding, all lanes)
1. **No stopping / no checkpoints / no interaction** — only pause = a `CHANGELOG.md` update, then continue. **Commit per step** so a crash loses nothing.
2. **Steps in the order written.** A phase completing is not a finish line.
3. **Batch tool calls.** Keep a live tiered TODO.
4. **Platform parity** (`SyxxKit` ⇔ `Syxx.Kit` twin) · **tri-state never bool** · **always `file:line`**.
5. **The bar is a real run with honest output, not green tests** — one real run per phase. **Only a lane's owner edits that lane's tests; never disable, re-flag, soften, or delete a test to turn CI green** — a failing honesty test is data, not an obstacle. The golden-vector corpus is engine-owned.
6. **Copy-gated:** ship only the approved recommended-set wording; unknown token → fail-closed generic message.
7. **Catalog all user-facing text** → `outputs-catalog.csv` (`file:line,trigger,module,task,output,change`), one row per string.
8. **Honesty spine:** nothing may read clean/secure for a tampered or unmeasured state, at *any* intermediate commit.
9. **Never edit `AGENTS.md`.** Flag — don't unilaterally change — shared contracts/schemas.

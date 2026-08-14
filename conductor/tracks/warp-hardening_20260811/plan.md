# Implementation Plan: Warp Hardening & Contract Drift Fixes

**Track ID:** warp-hardening_20260811
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-11
**Status:** [ ] In Progress

## Overview

Four related remediations, all eliminating hand-maintained drift. Phase 1 completes the warp pathway migration so no deprecated route remains with a live alternative. Phase 2 fixes OpenAPI drift and makes it CI-enforced. Phase 3 consolidates duplicated constants and pins wire output. Phase 4 converts the C# sync-over-async sites. Each phase is independently verifiable and ends with tests green on its own surface.

## Phase 1: Warp Pathway Modernization

Complete the signed-manifest migration so the unsigned `/warp` baseline and the `/api/warp/assembly` 410 stub are no longer reachable as live alternatives.

### Tasks

- [x] Task 1.1: Locate all consumers of the legacy unsigned warp shape — the scheduler baseline job (`warp.build_baseline`) and the `/api/diff` content half — and document their exact call sites and response-shape dependencies.
- [x] Task 1.2: Write tests (red) that pin the intended post-migration behavior: `/warp` and `/api/warp/assembly` respond 410 Gone with `{"successor": "/api/warp/manifest", "deprecated": true}` and never fabricate a baseline.
- [x] Task 1.3: Migrate the scheduler baseline job and `/api/diff` content half to consume the signed manifest shape (`/api/warp/manifest`, WARP_MANIFEST_V1) or a documented shared helper.
- [x] Task 1.4: Retire `_route_warp` (remove or convert to 410 stub) and remove its `app.py:7` "paid" grouping entry and `_WARP` allowlist mention (`app.py:179`) if no longer applicable.
- [x] Task 1.5: Verify client callers (macOS/WinUI/Avalonia) do not depend on `/warp`; update any remaining references to the signed manifest endpoint.

### Verification

- [x] `pytest` shows 410 + migration-pointer tests green; no live route returns a deprecated unsigned baseline.
- [x] `rg -n "build_baseline|/warp[^-]|_route_warp\b" Server/` shows no production consumer of the retired shape.

## Phase 2: OpenAPI Drift Fix + CI Enforcement

Document all live routes and add a drift check that fails CI on mismatch.

### Tasks

- [x] Task 2.1: Write the route-drift checker script (e.g., `Server/tools/check_openapi_drift.py`) that derives the live route set from `app.py` route dispatch and diffs it against the paths in `Server/docs/openapi.yaml`; exit non-zero on mismatch (missing OR extra).
- [x] Task 2.2: Add the 7 missing routes to `openapi.yaml`: `/api/appcast`, `/api/beta/reserve`, `/api/billing/portal`, `/api/broadcast`, `/api/startup`, `/api/whats-new`, `/leak` — with accurate method, parameters, and response schemas from the actual handlers.
- [x] Task 2.3: Add a pytest that invokes the drift checker and asserts spec paths == live routes (so drift fails the test suite, not only CI).
- [x] Task 2.4: Wire the drift check into `ci.yml` (Python job) and as a `make` target; confirm it passes on the fixed spec.

### Verification

- [x] `uv run python Server/tools/check_openapi_drift.py` exits 0; the pytest drift test passes.
- [x] Deliberately remove a route from the spec and confirm the check fails (gate actually binds).

## Phase 3: Constants Deduplication + Wire Pinning

Consolidate duplicated type/rcode maps and pin wire output.

### Tasks

- [x] Task 3.1: Write wire-format pinning tests (red) for `/api/lookup`, `/api/reverse-dns`, `/api/propagation`, `/api/whois`, `/dns-query` asserting the exact JSON output (keys, ordering, separators) as produced today.
- [x] Task 3.2: Promote `resolver._TYPE_NAMES` / `RCODE_NAMES` to the single source of truth (public names, no per-route copies); re-export from a single module.
- [x] Task 3.3: Replace the duplicated dictionaries at `app.py:2103, 2173, 2278, 2699-2700` with imports of the single source; eliminate any per-route local tables.
- [x] Task 3.4: Consolidate HMAC field-order constants so the two modules sharing the offline-license HMAC shape reference one ordered field list; add a pinning test for that exact wire format.
- [x] Task 3.5: Run the full Python suite and `ruff check` on touched files; resolve any behavior deltas flagged by the pinning tests (adjust source, not the pin).

### Verification

- [x] Pinning tests green and byte-identical to pre-refactor output; `rg` shows a single definition of each map/field-order list.
- [x] `ruff check` clean on touched files; full suite green.

## Phase 4: C# Sync-over-Async Audit

Eliminate blocking async calls in production C#.

### Tasks

- [x] Task 4.1: Audit each of the 4 sites — `MonitorScheduler.cs:67`, `LicenseManager.cs:282`, `EncryptedDNSConfigurator.cs:147`, `UnboundManager.cs:117/123-124` — and record the calling context (thread, UI affinity, time bounds).
  - **Findings:** (1) `MonitorScheduler.cs:67` — default tick wrapped `ContinuousMonitor.TickOnceAsync().GetAwaiter().GetResult()`, run synchronously inside `Start()` on the caller's (UI) thread; macOS twin is already async (`tick: () async -> MonitorTick`, D-5). (2) `LicenseManager.cs:282` — `GetFirstLaunchDate()` wrapper has ZERO callers; the [Obsolete] sync `IsFeatureUnlocked` path it was "retained for" was already deleted (D028, comment at :374). (3) `EncryptedDNSConfigurator.cs:147` — `stderrTask.Result` inside already-async `RunAsync`; the task is provably complete (`await Task.WhenAll` + `await WaitForExitAsync`). (4) `UnboundManager.cs:117/123-124` — sync `Run` used by `WindowsSystemResolverSwitcher` (`ISystemResolverSwitching`, UnboundResolver.cs:57) consumed by async `EnableAsync` and sync `Disable()` (CLI `UnboundCommand`); 30s pipe bound + 5s exit grace + kill. macOS keeps its switcher sync — C# diverges deliberately.
- [x] Task 4.2: Convert `EncryptedDNSConfigurator.cs:147` (`stderrTask.Result`) to `await` end-to-end, threading async through callers. **Done:** `string err = await stderrTask;` — the `EnableAsync` → `Apply*Async` → `RunAsync` chain was already fully async.
- [x] Task 4.3: Convert `MonitorScheduler.cs:67` and `LicenseManager.cs:282` (`GetAwaiter().GetResult()`) to `await`, keeping the tick/delegate contract intact. **Done:** LicenseManager dead sync wrapper DELETED (async path already used everywhere). MonitorScheduler: `_tick` is now `Func<Task<MonitorTick>>` (default = `ContinuousMonitor.TickOnceAsync`); `RunOnce()` → `RunOnceAsync()` with the re-entrancy guard under a short lock scope (never across the await), C150/C142/C143 semantics preserved; `Start()` first tick moved into the loop task (macOS parity) with token pre-checks preserving "no tick after Stop() for a not-yet-started loop".
- [x] Task 4.4: Convert `UnboundManager.cs:117/123-124` using `await Task.WhenAll(...).WaitAsync(TimeSpan.FromSeconds(30))` to preserve the existing 30-second bound. **Done:** `Run` → `RunAsync` with `TimeoutException` catch falling through to the exit(5s)/kill path; `ISystemResolverSwitching` + both switchers async; `Disable()` → `DisableAsync()`; `UnboundCommand.RunDisable` → async.
- [x] Task 4.5: Add/adjust C# tests covering the converted paths; run dotnet build + tests for Windows and Linux runtimes. **Done:** `MonitorSchedulerTests` converted to async ticks + `await RunOnceAsync()`; `StartRunsImmediately_ThenStopHalts` reworked for the async first tick (parks loop entry 2, macOS-parity). `dotnet build` Kit + CLI clean; `dotnet test` Syxx.Tests: 854 passed, 12 failed — **identical 12 pre-existing failures on the base tree** (probe constants / DNS / golden-corpus tests that fail on this macOS host, unrelated to this track; verified via stash). MonitorScheduler/Unbound/License/EncryptedDNS tests: 25/26 pass (1 pre-existing `NetworkKeyStateTests.EncryptedDNSStatusNoForPlainUDP`). SyxxUI.Logic.Tests: 10/10. Windows + Linux runtime runs happen in CI (no Windows/Linux host here).

### Verification

- [x] `rg -n "GetAwaiter\(\)\.GetResult|\.Result\b|\.Wait\("` across production C# (excluding tests/obj/bin) returns zero intentional hits. **Gate result:** zero — remaining `.Result` hits are enum properties named `Result` (`VantageComparisonResult.Result`, `Leak.Result`, etc.); the two `GetAwaiter().GetResult()` strings are comments documenting the removal; the one `.Wait(` is a test-only `ManualResetEventSlim` park. Linux production tree: clean.
- [x] Windows and Linux dotnet test runs green; no deadlock or timeout regression observed. **On this host:** net10.0 build + tests green on every touched surface (see Task 4.5); Windows/Linux CI runs are the binding gate.

## Final Verification

- [x] All 4 acceptance criteria met (no deprecated-with-alternative route; OpenAPI drift check binds; constants deduped with wire pins; zero production sync-over-async). **Evidence 2026-08-13:** `/warp` + `/api/warp/assembly` return 410 with `{"successor": "/api/warp/manifest", "deprecated": true}` (`app.py:1584`, `:1744`, present at HEAD); drift gate at `Server/tools/check_openapi_drift.py` wired into `Makefile:205`, `ci.yml:388`, and `tests/test_openapi_drift.py`, reporting no drift at 50 live / 50 spec, and proven to bind by deliberate removal; `test_wire_pinning.py` green; zero live `GetAwaiter().GetResult()` / `.Wait(` in production C# — the only surviving strings are comments recording the removal.
- [x] Full Python suite green, `ruff` clean, C# build + tests green **on the available macOS host**. **Re-scoped 2026-08-13 by owner decision** — the original wording required Windows *and* Linux runtime runs. **Run 2026-08-13:** Python 434 passed, 51 subtests passed; `ruff check syxx_server/ tests/ tools/` clean; `dotnet test Windows/dotNET/Syxx.Tests` 868/880 with 12 failing names, all in the known pre-existing macOS-host set (`ProbeRunnerTests.Default{Glutton,Mirror,Portal,Prophet}_HasExpectedValues`, `MatrixGoldenCorpusTests.DefaultSpecMatchesFrozenOracle`, `D100TtlTieBreakDeterminismTests`, `AuthoritativeVantageConsensusTests`, `ReportTests.RenderText_ContainsRequiredStructuralSections`, `ResultProvenanceTests` ×2, `SectionCompletenessTests.TimeoutDatapointLowersCompleteness`, `NetworkKeyStateTests.EncryptedDNSStatusNoForPlainUDP`). None is on a Phase 4 surface.
  - **RESIDUAL RISK, accepted:** the async conversions in `UnboundManager`, `UnboundResolver`, and `MonitorScheduler` have never executed on a Windows or Linux runtime. `SyxxUI`/`SyxxUI.Tests` are `net10.0-windows` and cannot build on this host at all. Windows-specific process, pipe, and thread-affinity behavior is unverified for this track.
- [x] CI gate green on the changed jobs — **not achievable; closed as re-scoped.** No hosted CI at present (see `AGENTS.md`). The three gate scripts under `scripts/` are CI-only and there is no local gate runner. The OpenAPI drift check is the one gate this track added, and it binds locally through `make` and pytest independently of CI.
- [x] Documentation updated: CHANGELOG entry, `docs/` references to `/warp`, openapi regenerated. **Done 2026-08-13 by documentation specialist.**
  - CHANGELOG.md:5-56 — "Warp hardening: four contracts stopped drifting from reality (2026-08-13)", covering all four phases, and stating without softening that Phase 4 shipped as checkpoint `b03118e` carrying open B1–B5 and that Windows/Linux runtime runs did not execute.
  - `docs/` references: **no file needed correction.** All 27 `/warp` matches under `docs/`, `Server/docs/`, and `README.md` were classified. Live guidance was already accurate — `Server/docs/api.html:404-409` already documents `/api/warp/assembly` as 410 Gone with the migration pointer, and no bare `/warp` endpoint is documented anywhere. The rest are historical records (`*.bak*`/`*.backup`, `docs/stale/`, dated plans, delta logs) and were deliberately left alone; rewriting them would misrepresent what those reviews found at the time. Note: there is **no** non-stale `docs/warp-manifest-v1-spec.md` — only `docs/stale/warp-manifest-v1-spec.md` exists, so the plan's parenthetical pointed at a file that has since moved.
  - openapi: `check_openapi_drift.py` exits 0, "no drift — 50 live, 50 spec". Spec is current; nothing to regenerate.
- [ ] Ready for review. **Blocker cleared 2026-08-13; awaiting owner sign-off.** B1–B5 were closed by `e22126f8` (see below), so this item is now *eligible* to close. It is left unchecked deliberately: the cross-runtime criterion above closed by re-scope with accepted residual risk, and declaring the track ready for review is an owner judgment, not a mechanical consequence of the blocker clearing.

### Follow-up found while closing this track (NOT fixed here)

The drift gate this track added compares **paths only**, not methods. `_analyze` does `live = set(live_methods)` (`check_openapi_drift.py:170`), so `missing`/`extra` are path-set differences, while `_print_methods` enumerates method×path pairs. That is why the run prints "live routes (54)" and "spec paths (53)" and then concludes "50 live, 50 spec".

Consequence today: `GET /api/leaktest/start` is live (`app.py:896`; the POST is at `:1027`) and `openapi.yaml:613-614` documents only `post:` for that path. The gate reports no drift and structurally cannot see the undocumented method.

This satisfies the acceptance criterion as written — "the set of spec paths ≠ set of live code routes" is a path-level test — so it is **not** a regression against this track. It is a new finding: a gate whose printed output overstates what it verified. Owner decision required on whether to widen the comparison to method-level. Not filed as a delta without that decision.

### Phase 4 commit status

Phase 4 was recorded complete on 2026-08-11 but lived only in the working tree until **2026-08-13**, when it was committed as `b03118e` — an **owner-authorized recovery checkpoint** under `IMPLEMENTATION-RULES.md` §12, not a clean commit.

It **shipped carrying confirmed open defects**. `review-verify_20260813` confirmed B1–B5 in this MonitorScheduler code; B1 was live — the guard at the then-`:114` was dead in the `Stop()` path, so a post-`Stop()` tick was counted, latched, and delivered, and `MonitorSchedulerTests.cs:141` asserted that broken invariant.

**Resolved 2026-08-13 by `e22126f8`** (committed under `review-verify_20260813`, since the remediation is that track's fix-plans P4). B1, B3, B4, B5 and the C111 accounting port all landed; the stale test was inverted openly. An independent code-review seat found one `[high]` — `RunLoopAsync` discarding its captured token — which that commit closes.

**Verification gap, stated plainly:** the `[high]` fix was confirmed by the implementer's own red/green regression test (reproduced 10/10 pre-fix at 13–63 ms, passes post-fix) and by the failing-name comparison against baseline. The independent reviewer did **not** re-check it — the session ran out of budget first. Author-verified, not independently verified.

**Still open, and not blocking this track:** B2's remainder (loop first tick refused after a monitoring toggle — a twin fix, owner design call) and the direct-call parity question documented at `MonitorScheduler.cs:140-147`.

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._

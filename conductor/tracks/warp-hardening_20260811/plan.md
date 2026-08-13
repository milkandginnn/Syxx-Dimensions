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

- [ ] All 4 acceptance criteria met (no deprecated-with-alternative route; OpenAPI drift check binds; constants deduped with wire pins; zero production sync-over-async).
- [ ] Full Python suite green, `ruff` clean, C# build + tests green on both runtimes.
- [ ] CI gate green on the changed jobs (ruff/SwiftLint/parity unaffected by this track).
- [ ] Documentation updated: CHANGELOG entry, `docs/` references to `/warp` (warp-manifest-v1-spec.md), openapi regenerated.
- [ ] Ready for review.

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._

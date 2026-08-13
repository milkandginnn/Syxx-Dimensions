# Specification: Warp Hardening & Contract Drift Fixes

**Track ID:** warp-hardening_20260811
**Type:** Refactor
**Created:** 2026-08-11
**Status:** Draft

## Summary

Modernize the `/api/warp` pathway so no server route is deprecated with a live alternative, eliminate OpenAPI drift (7 live routes missing from the spec), consolidate hand-maintained duplicated constants into a single source of truth, and audit/fix the four C# sync-over-async call sites.

## Context

A comprehensive 5-phase review of Syxx (`.full-review/`) surfaced four remediations that share a root cause: **hand-maintained contracts that drift from reality**. The unsigned `/warp` baseline is forgeable by a MITM into a false match and is superseded by the signed `/api/warp/manifest` (WARP_MANIFEST_V1); the scheduler baseline job and `/api/diff` content half still consume the legacy shape. The OpenAPI spec documents 36 of the live routes — 7 are missing with no spec↔route drift check in CI. Duplicated `type_map`/rcode/HMAC constants already caused real bugs (A155 int-vs-str, SEC-H1 HMAC field-order mismatch). Four C# sync-over-async sites (`GetAwaiter().GetResult()`, `.Result`, `.Wait()`) are latent deadlock/UI-freeze risks.

Per `conductor/product.md` and `conductor/workflow.md`: tests are required before implementation, gates must actually bind, all changes reviewed, and subagents used for implementation.

## Problem Description

1. **Deprecated warp pathway.** `_route_warp` (`app.py:1534`) is a "declared-but-unsigned hash" that a MITM can forge into a false match; `_route_warp_assembly` (`app.py:1691`) returns 410. Both remain live because the scheduler baseline job and `/api/diff` content half consume the unsigned shape. The pathway is half-migrated: `_route_warp_raw` and `_route_warp_manifest` are current, but the deprecated routes are not retired.
2. **OpenAPI drift.** `Server/docs/openapi.yaml` has 36 paths; 42 are live in code. Missing: `/api/appcast`, `/api/beta/reserve`, `/api/billing/portal`, `/api/broadcast`, `/api/startup`, `/api/whats-new`, `/leak`. No CI check compares spec paths to code routes.
3. **Duplicated constants.** `app.py:2103, 2173, 2278, 2699-2700` define overlapping `type_map`/rcode-name dictionaries; HMAC field orders exist in two modules. `resolver._TYPE_NAMES`/`RCODE_NAMES` already exist as candidates for the single source of truth.
4. **C# sync-over-async.** `MonitorScheduler.cs:67`, `LicenseManager.cs:282`, `EncryptedDNSConfigurator.cs:147`, `UnboundManager.cs:117/123-124` use blocking `.GetAwaiter().GetResult()`, `.Result`, or `.Wait()`.

## Acceptance Criteria

- [x] No server route marked `DEPRECATED` with a live alternative remains reachable; legacy unsigned warp consumers (scheduler baseline job, `/api/diff`) are migrated to the signed manifest or explicitly retired with 410 + migration pointer.
- [x] `openapi.yaml` documents every live route; a CI spec↔route drift check fails when the set of spec paths ≠ set of live code routes.
- [x] Duplicated `type_map`/rcode-name dictionaries are consolidated to a single source of truth, and wire-format pinning tests prove byte-identical output for `/api/lookup`, `/api/reverse-dns`, `/api/propagation`, `/api/whois`, `/dns-query`.
- [x] Zero `GetAwaiter().GetResult()`, `.Result`, or `.Wait()` in production C# (`Windows/dotNET` + `Linux/dotNET`, excluding tests); each of the 4 sites is converted to `await` end-to-end or, where conversion is impossible, annotated with an explicit documented rationale.
- [x] ~~Full Python suite green (`OTEL_TRACES_EXPORTER=none uv run pytest tests/ -q`), `ruff check` clean on touched files, dotnet build + C# tests green on Windows and Linux runtimes, CI gate green on the changed jobs.~~ **Amended 2026-08-13 by owner decision:** Full Python suite green, `ruff check` clean, dotnet build + C# tests green **on the available macOS host**. Windows and Linux runtime runs and the CI gate are deferred — no hosted CI at present, and `SyxxUI`/`SyxxUI.Tests` are `net10.0-windows` and cannot build here. Residual risk is recorded in `plan.md` under Final Verification: the `UnboundManager` / `UnboundResolver` / `MonitorScheduler` async conversions have never executed on a Windows or Linux runtime.

> **Sign-off is still blocked.** Phase 4 landed at `b03118e` as an owner-authorized recovery checkpoint carrying confirmed defects B1–B5, remediated in `review-verify_20260813` fix-plans P4. Meeting these criteria is not the same as being ready for review.

## Dependencies

- Existing code: `Server/syxx_server/app.py` (warp routes, type maps), `Server/syxx_server/warp.py`, `Server/syxx_server/resolver.py` (`_TYPE_NAMES`/`RCODE_NAMES`), `Server/docs/openapi.yaml`, `Server/syxx_server/db.py`, `Windows/dotNET/Syxx.Kit/*` (C# sites).
- The scheduler baseline job and `/api/diff` content half must be located and understood before retiring `/warp`.

## Out of Scope

- The SEC-H1 offline-license HMAC bug (separate decision: excise or fix — tracked independently).
- Behavioral cross-platform parity gap beyond the constants/wire-format pinning (BP-M5 full parity gate extension).
- Renaming or versioning the `/api/v1/` rewrite alias.
- Adding new warp features beyond completing the signed-manifest migration.

## Technical Notes

- `resolver._TYPE_NAMES`/`RCODE_NAMES` are the intended single source of truth; promote them and re-export, do not duplicate.
- Wire-format pinning tests must serialize the exact JSON the routes emit (same separators/ordering) to catch regressions.
- The spec↔route drift check belongs in CI (`ci.yml` Python job) and as a local `make` target; derive the route set from `app.py` route dispatch, not a hand-maintained list.
- C# conversion must preserve the time-bounded semantics of `UnboundManager`'s `Task.WhenAll(...).Wait(30s)` (e.g., `await Task.WhenAll(...).WaitAsync(TimeSpan.FromSeconds(30))`).
- Use subagents for implementation phases per workflow.md; verify every gate actually binds.

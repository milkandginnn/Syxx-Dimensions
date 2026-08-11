# plan.md — Fix Fingerprint Mismatch and C# Test Build Errors

**Track ID:** fix-fingerprint-mismatch_20260809
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-09
**Status:** [ ] Pending

## Phase 1: Fix fingerprint mismatch (D-1)

- [ ] Task 1.1: Fix `EscapeNonAscii` or `SerializeWithSortedKeys` so C# fingerprint matches Swift (`a7a24aae`)

## Phase 2: Fix C# test build errors (D-2)

- [ ] Task 2.1: Fix `DateTimeOffset` → `DateTime` conversion in `ResultProvenanceTests.cs:53`
- [ ] Task 2.2: Fix `DateTimeOffset` → `DateTime` conversion in `MatrixUntestedStateTests.cs:228`

## Phase 3: Verification

- [ ] Task 3.1: `bash scripts/parity-local-gate.sh` exits 0
- [ ] Task 3.2: `dotnet build Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release` — 0 errors
- [ ] Task 3.3: `dotnet build Windows/dotNET/Syxx/Syxx.csproj -c Release` — 0 errors
- [ ] Task 3.4: `Server/venv/bin/python -m pytest Server/tests/ -q` — 439 passed
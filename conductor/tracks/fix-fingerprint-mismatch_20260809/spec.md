# spec.md — Fix Fingerprint Mismatch and C# Test Build Errors

**Track ID:** fix-fingerprint-mismatch_20260809
**Type:** bug
**Status:** new

## Overview

The `parity-local-gate.sh` run after the D-1/D-5 fixes shows a transcript fingerprint mismatch between Swift and C#. The C# fingerprint changed from `a7a24aae` to `0c4fa509` while the Swift fingerprint remains `a7a24aae`. Additionally, the C# test project has 2 build errors.

## Defects

### D-1: Transcript fingerprint mismatch

`parity-local-gate.sh` reports:
```
FAIL transcript fingerprint mismatch — swift=a7a24aae... cs=0c4fa509...
```

The `EscapeNonAscii` post-processing added in D-1 is the likely cause. For ASCII-only corpus content it should be a no-op, but the fingerprint divergence indicates the output bytes are changing.

**Fix:** Investigate and fix the `EscapeNonAscii` method or the `SerializeWithSortedKeys` path so the C# fingerprint matches the Swift fingerprint (`a7a24aae`).

### D-2: C# test build errors

`Syxx.Tests` fails with 2 errors:
- `ResultProvenanceTests.cs:53` — `DateTimeOffset` → `DateTime` conversion
- `MatrixUntestedStateTests.cs:228` — `DateTimeOffset` → `DateTime` conversion

These are pre-existing build errors in the test project, not caused by our changes.

**Fix:** Fix the type conversion in both test files.

## Acceptance Criteria

- [ ] `bash scripts/parity-local-gate.sh` exits 0 — fingerprint matches
- [ ] `dotnet build Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release` — 0 errors
- [ ] `dotnet build Windows/dotNET/Syxx/Syxx.csproj -c Release` — 0 errors
- [ ] `swift build --package-path macOS` — 0 errors
- [ ] `Server/venv/bin/python -m pytest Server/tests/ -q` — 439 passed
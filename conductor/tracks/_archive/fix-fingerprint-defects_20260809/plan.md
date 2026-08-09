# plan.md — Fix Twin Parity Fingerprint Defects

**Track ID:** fix-fingerprint-defects_20260809
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-09
**Status:** [x] Complete

## Phase 1: Fix C# ParityTranscriptCommand.cs defects (D-1, D-2, D-5)

- [x] Task 1.1: Replace `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` with `JavaScriptEncoder.Default` (D-1)
- [x] Task 1.2: Fix `IsAlreadyEscaped` to count consecutive backslashes — odd count = escaped (D-2)
- [x] Task 1.3: Commit the two uncommitted improvements: `G17`→`"R"` and top-level `EscapeForwardSlashes` in `Write` (D-5)

## Phase 2: Fix cross-platform-parity-gate.py import (D-3)

- [x] Task 2.1: Move `import subprocess` from `main()` to the top of the file

## Phase 3: Fix shell script trailing newlines (D-4)

- [x] Task 3.1: Append trailing newline to `scripts/regenerate-parity-fingerprint.sh`
- [x] Task 3.2: Append trailing newline to `scripts/verify-parity-fingerprint.sh`

## Phase 4: Verification

- [x] Task 4.1: `bash scripts/verify-parity-fingerprint.sh` exits 0
- [x] Task 4.2: `python3 scripts/cross-platform-parity-gate.py` exits 0
- [x] Task 4.3: `Server/venv/bin/python -m pytest Server/tests/ -q` — 420 passed
- [x] Task 4.4: Verify all 6 acceptance criteria from spec.md
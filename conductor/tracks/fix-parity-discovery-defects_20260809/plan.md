# plan.md — Fix Parity-Discovery Defects

**Track ID:** fix-parity-discovery-defects_20260809
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-09
**Status:** [ ] Pending

## Phase 1: Fix JSON encoder (D-1)

- [ ] Task 1.1: Replace `JavaScriptEncoder.Default` with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
- [ ] Task 1.2: Add non-ASCII → `\uXXXX` post-processing step to `SerializeWithSortedKeys`

## Phase 2: Fix DimensionClient decompression (D-2)

- [ ] Task 2.1: Add `SocketsHttpHandler` with `AutomaticDecompression` to `_http`
- [ ] Task 2.2: Add `AutomaticDecompression` to `_hardenedHttp` in `CreateHardenedClient()`

## Phase 3: Fix LookalikeDetector NFC (D-3)

- [ ] Task 3.1: Add `.Normalize(NormalizationForm.FormC)` after `idn.GetUnicode(label)`

## Phase 4: Verification

- [ ] Task 4.1: `dotnet build Windows/dotNET/Syxx/Syxx.csproj -c Release` — 0 errors
- [ ] Task 4.2: `bash scripts/verify-parity-fingerprint.sh` exits 0
- [ ] Task 4.3: `python3 scripts/cross-platform-parity-gate.py` exits 0
- [ ] Task 4.4: `Server/venv/bin/python -m pytest Server/tests/ -q` — 420 passed
- [ ] Task 4.5: Verify all 7 acceptance criteria from spec.md
# Linux Parity Ledger

Durable artifact answering "is Linux at parity" with evidence.

> Append-only ledger tracking the Linux→macOS functional parity effort.

---

## §0 Findings (original plan survey)

| ID | Finding | Date |
|----|---------|------|
| 0.1 | `Linux/dotNET/Syxx.Linux/` was a decoy (166 lines, all stubs, never referenced) — **REMOVED** | 2026-08-05 |
| 0.2 | The real Linux CLI is `Windows/dotNET/Syxx/Syxx.csproj` with `win-x64;linux-x64;linux-arm64` RIDs | 2026-08-05 |
| 0.3 | `Syxx.Kit` already substantially Linux-aware (~40 IsLinux/IsMacOS hits) | 2026-08-05 |

---

## §7.0 Build pass results (committed 2026-08-05, sha 39b95ad)

| Item | State | Note |
|------|-------|------|
| G-01 SaveKey verify-after-store | **LANDED** | read-back + ordinal compare + throw before token write; orphan discarded |
| G-02a positive proof of absence | **LANDED** | File.Exists → guarded FileStream open; HasKey error-as-absence gone |
| G-02b store-before-mint | **PARTIAL** | closed for file-store + secret-tool; OPEN for libsecret P/Invoke (see G-16) |
| G-03 first-run manifest | **LANDED** | WarpManifest folded in both C# and Swift twins |
| G-04 CI gates | **LANDED** | package-pin-parity.py path fixed + self-test; sln config entries; CI DLL assertion |
| G-05 test seam | **LANDED** | KeyPath + store dir injectable; 22 tests; mutation-tested (5 guards, 9 flip) |

---

## Security review findings (2026-08-05, 4-seat review team)

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| S-1 | Medium | PassphraseAvailable() TOCTOU — double TryGetPassphrase() call | **OPEN** — queued for fix |
| S-2 | Medium | Cached derived key returned by reference across lock boundary | **OPEN** — queued for fix |
| S-3 | Medium | SYXX_WITNESS_PASSPHRASE env var readable via /proc/pid/environ | **OPEN** — doc fix queued |
| S-4 | Low | StringBuilder material never zeroed | **OPEN** — queued for fix |

---

## Open items within scope

| Item | Status | Blocker |
|------|--------|---------|
| G-16 libsecret P/Invoke | Fix coded, needs real-Linux verification | No libsecret on macOS |
| G-02b residual | **OPEN** | Must co-land with G-16 search binding |
| G-16b DNSPathConfirmation.swift | **OPEN** | Blocks all Swift compilation |
| G-17 macOS hasKey collapse | **OPEN** | Depends on G-16b |
| §7.4 parity gate | **RED** | tracer_outcome enum drift |

---

## Reserved items (routed to other streams)

| Item | Owner |
|------|-------|
| G-09 | Mirror stream |
| G-10, G-11, G-12, G-14 | 2nd Avalonia team |
| G-13 | Stage-5 wording |
| G-15 | All platforms reserved |

---

## §4.6 Parity gate baseline

| Command | State |
|---------|-------|
| `python3 scripts/cross-platform-parity-gate.py` | 🔴 RED (tracer_outcome drift) |
| `python3 scripts/package-pin-parity.py` | 🟢 GREEN (after G-04 fix) |
| `dotnet build Windows/Syxx.Linux.slnf -c Release` | ⚪ NOT RUN (no build available on macOS) |
| `dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release` | ⚪ NOT RUN |

---

## Appendix: Ledger entries

*Append new entries below in reverse chronological order.*

| Date | Entry |
|------|-------|
| 2026-08-05 | Ledger created from §7.0 build pass and security review results |

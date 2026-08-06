# CLI output capture — Task 3.4

**Date:** 2026-08-05 · **State of every claim below:** `verified-by-real-run` (this machine, both
binaries executed, outputs in [`cli-captures/`](./cli-captures/))

## Method

Both builds executed directly (no `swift run` / `dotnet run` wrapper noise):

- Swift: `macOS/.build/debug/syxx` (debug build, Build of product 'syxx' complete)
- C#: `dotnet Windows/dotNET/Syxx/bin/Release/net10.0/Syxx.dll`

Invocation set: `--help`, `--version`, and `<command> --help` for all 15 subcommands. Every
invocation guarded by an 8-second alarm because per-command `--help` was not known to be safe —
and for one command it is not (see D5).

## Results — top level

| Invocation | Swift exit | C# exit | Text |
|---|---|---|---|
| `--help` | 0 | 0 | 2 lines differ (cage description, cage example) |
| `--version` | 0 | 0 | **Diverges** — see D1 |

## Results — per-command `--help`

| Command | Swift exit | C# exit | Text |
|---|---|---|---|
| nslook | 0 | 2 | same |
| mirror | 0 | 0 | same |
| leak | 0 | 2 | same |
| witness | 0 | 0 | **differs** (D3) |
| key | 0 | 2 | same |
| route | 0 | 2 | same |
| lan | 0 | 2 | same |
| forensics | 0 | 2 | same |
| sentinel | 0 | 2 | same |
| cage | 0 | 2 | **differs** (D3) |
| warp | 0 | 0 | same |
| speed | 0 | 2 | same |
| unbound | 0 | 2 | **differs** (D3) |
| filter | 0 | 2 | same |
| firstrun | n/a — executed the scan | 2 | **differs** (D5) |

## Divergences

- **D1 — `--version` output.** Swift prints two lines: `Syxx 2.0.0` then
  `© J.J. Warne d/b/a MilkandGin. All rights reserved.` C# prints one: `syxx 2.0.0`
  (lowercase product name, no copyright line).
- **D2 — exit-code regime for `<command> --help`.** Swift exits 0 for every command. C# exits 2
  for 11 commands but 0 for `mirror`, `witness`, `warp` — so the two builds disagree with each
  other *and* C# is internally inconsistent.
- **D3 — advertised flag surface.**
  - `witness`: C# advertises `--gen-keypair … [--force]`; Swift has no `--force`.
  - `cage`: C# advertises `[--output <dir>]`; Swift has no `--output`. Privilege wording also
    differs (`requires sudo` vs `requires root/sudo on Linux and macOS, Administrator on Windows`).
  - `unbound`: Swift advertises the `verify-dnscrypt` action; C# does not have it.
- **D4 — top-level help wording.** `cage` description (`requires sudo` vs `requires root (sudo)`)
  and the cage example (`sudo syxx cage …` vs `syxx cage …`).
- **D5 — Swift `firstrun` does not parse `--help`; it executes.** `syxx firstrun --help` on the
  Swift build began the live five-minute scan (killed by the 8s alarm; exit 142 = SIGALRM). The C#
  build prints usage and exits 2. Asking for help must never start a network scan. This is the
  most severe finding of the capture.

## Known-but-not-retested here

The `-h` / `-v` short-flag asymmetry (C# accepts them, Swift does not) was found at spec pre-flight
and is Task 3.3's subject; the capture above used long flags only.

## Note on capture hygiene

The `firstrun --help` Swift invocation ran live scan startup for ≤8 seconds on this machine before
the alarm killed it. Recorded here because a capture that quietly triggered network activity should
say so.

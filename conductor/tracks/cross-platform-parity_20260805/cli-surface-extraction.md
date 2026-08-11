# CLI surface extraction and gate — Tasks 3.1 and 3.2

**Date:** 2026-08-05 · **Gate:** [`scripts/cli-surface-gate.py`](../../../scripts/cli-surface-gate.py)

**State of the claims below**

| Claim | State |
|---|---|
| The gate extracts both CLI surfaces and compares them at three levels | `verified-by-real-run` (run **a**) |
| The gate goes red on a deliberately introduced command/flag asymmetry | `verified-by-real-run` (run **b**) |
| The gate reports cannot-run, never a pass, on an empty or implausibly small extraction | `verified-by-real-run` (runs **c1**, **c2**) |
| The nine ledgered exceptions each still correspond to a real asymmetry | `verified-by-real-run` (run **a**, section [5]) |
| Nine NEW asymmetries are real, not extraction artifacts | `verified-by-real-run` — each confirmed by grep independently of the gate |
| The gate is wired into CI | `open` — Phase 6 Task 6.3. Nothing under `.github/` was touched here |

**Run (a) against the tree as-is exits 1.** That is the honest current state, not a gate
malfunction. The exception table matches every divergence it claims (section [5] passes), and the
gate additionally found nine asymmetries that no existing record documents. They are listed in full
below for the lead to file. **Nothing was added to the exception table for them** — an exception
written to make a gate green on the day it lands is how a gate stops meaning anything.

---

## What the gate measures

Three levels, kept apart, because `syxx --help`, `syxx nslook --help` and
`syxx witness --gen-keypair --force` are three different code paths. Pooling them would let a flag
missing at one level be covered by its twin at another.

| Level | What is read | From |
|---|---|---|
| **top** | command names, flag literals, unknown-command exit code | each dispatcher's own switch block |
| **`<global-flags>`** | the shared `--help`/`--version` handler every subcommand calls | `SyxxConfig.handleGlobalFlags` / `SyxxConfig.HandleGlobalFlags` |
| **per command** | ACCEPTED flags, ADVERTISED flags, action words | each command's implementation file |

Everything matches on emitted strings. No symbol name is looked up anywhere: commands come from the
dispatch case literals, flags from string literals, and a file is bound to the command it implements
by the tool-name literal it hands the global-flag handler (`"syxx nslook"`) — never by its filename
or its type name. `SyxxNSLook.swift` and `NslookCommand.cs` may be renamed freely.

### The context rules that make the known divergences visible

**ACCEPTED vs ADVERTISED.** A flag is ACCEPTED when something compares argv to it; it is ADVERTISED
when it appears inside a literal containing `Usage:`. The D3 divergences live in the gap between the
two: `witness --force` is accepted on **both** platforms and advertised on **Windows only**. A gate
that pooled them would report that divergence as absent and the Task 3.4 capture that found it as
mistaken.

**Dead cases.** Both dispatchers lowercase the token before the switch, so a `case` arm carrying an
uppercase letter cannot fire. The gate resolves the switch subject — following its local binding when
the switch reads a variable (`let subcommand = args[1].lowercased()`) rather than the expression
(`switch (args[0].ToLowerInvariant())`) — and excludes dead arms from the effective surface. This is
the F-08 addendum made mechanical rather than re-argued.

**Alias groups.** `-v` is accepted by both global-flag handlers, so a set comparison calls them
identical. The gate records which flags each handler tests *together in one condition*, so `-v`
grouping with `--verbose` on macOS and with `--version` on Windows is reported rather than passing.

---

## Extraction counts

All figures are the gate's own measured output (run **a**), not hand-sums.

| | macOS | Windows |
|---|---|---|
| source files scanned | 17 `.swift` under `macOS/Sources/SyxxCLI` | 29 `.cs` under `Windows/dotNET/Syxx` |
| top-level commands | 16 | 17 |
| top-level reachable flags | 3 (`--help`, `--version`, `-h`) | 4 (`--help`, `--version`, `-h`, `-v`) |
| top-level dead arms | 1 (`-V`) | 1 (`-V`) |
| unknown-command exit code | 3 (`SyxxCLI.swift:98`, via `CLIExitStatus.failure` = 3) | 3 (`Program.cs:113`) |
| global-flag handler flags | 7 | 5 |
| per-command accepted flags | 54 | 49 |
| per-command advertised flags | 36 | 39 |
| per-command action words | 9 | 8 |
| commands with usage text | 14 of 17 | 15 of 17 |

`guided` binds to no implementation file on either side — it is handled inline in `Program.cs`.
`parity-transcript` binds on both, via its `"parity-transcript: "` diagnostic prefix.

---

## Asymmetries — LEDGERED (9)

Each carries the record that documents it. A matched entry is reported LEDGERED and does not fail the
gate; an entry that matches nothing FAILS as a stale exception, because an exception outliving its
divergence is a standing permission to reintroduce the defect. All nine matched.

| Level | Dimension | Token | Side | Finding |
|---|---|---|---|---|
| top | topflag | `-v` | Windows only | F-08 |
| top | deadcase | `-V` | macOS | F-08 addendum |
| top | deadcase | `-V` | Windows | F-08 addendum |
| `<global-flags>` | globalflag | `--verbose` | macOS only | F-08 |
| `<global-flags>` | alias | `--version,-V,-v` | both, split differs | F-08 |
| cage | advertised | `--output` | Windows only | D3 |
| witness | advertised | `--force` | Windows only | D3 |
| unbound | action | `verify-dnscrypt` | macOS only | D3 |
| firstrun | usage | usage-text | Windows only | D5 |

`firstrun` is the one command whose per-flag ADVERTISED comparison is suppressed, because macOS has
no help text for it at all (D5 — `syxx firstrun --help` starts the scan rather than printing help).
The suppression is printed in the gate output, not silent: a comparison that quietly does not run is
the defect this gate exists to refuse.

---

## Asymmetries — NEW and unledgered (9). For the lead to file.

Nothing was added to the exception table for these. Each was confirmed against source by grep, after
and independently of the gate reporting it.

### N-1 · `guided` is a Windows-only top-level command

- Windows: `Windows/dotNET/Syxx/Program.cs:66` — `string.Equals(args[0], "guided", …)` enters the
  interactive launcher.
- macOS: absent. `grep -rn '"guided"' macOS/Sources/SyxxCLI/` returns nothing, so `syxx guided` on
  macOS falls to `default` and exits 3 "Unknown subcommand".

It is dispatched *before* the switch, so a switch-scoped extraction would have missed it entirely;
the gate reads `args[0]`/`args[1]` equality tests outside the switch for exactly this reason.

### N-2 · `--json` is a macOS-only global flag

- macOS: `macOS/Sources/SyxxKit/SyxxConfig.swift:72` — `isJsonOutput` tests `--json`.
- Windows: absent. `grep -rn -- '"--json"' Windows/dotNET/` returns nothing.

`SyxxCLI.swift:107` records that `--json` output for conclusion-bearing subcommands is unimplemented
(B137 Fix 3), so the accessor may be inert on macOS too. The flag is nonetheless accepted on one
platform and not the other, which is a surface divergence whatever the accessor currently does.

### N-3 · `cage`'s privileged-capture flags diverge in five places

| Flag | macOS | Windows |
|---|---|---|
| `--interface` | `SyxxCage.swift:84` | absent |
| `-i` | `SyxxCage.swift:84` | absent |
| `--owner-uid` | `SyxxCage.swift:95` | absent |
| `--owner-gid` | `SyxxCage.swift:104` | absent |
| `--out` | absent | `CageCommand.cs:71` |

Confirmed by `grep -n -- '"--interface"\|"-i"\|"--owner-uid"\|"--owner-gid"'` over
`CageCommand.cs` (nothing) and `grep -n -- '"--out"'` over `SyxxCage.swift` (nothing).

`--out` is deliberate and already explained at `CageCommand.cs:55-70` — D108's launcher invocations
ship with it, and `--output`/`-o` were added as aliases so a command line carries between platforms.
That explanation has no finding id, which is why the gate will not treat it as ledgered. The four
macOS-only flags have no such note anywhere. The same comment also records a divergence the gate
**cannot** see and does not claim to: on Windows `--output` takes a DIRECTORY, on macOS a FILE path.
The gate compares flag names, not value types.

### N-4 · `firstrun` accepts `--local` / `-l` on macOS only

- macOS: `macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift:38`.
- Windows: absent from `FirstRunCommand.cs`.

Both platforms exit 3, so the exit-code contract holds; the messages do not. macOS parses `--local`
and refuses it by name ("Error: --local is not yet supported. The scan uses the system resolver." —
B239), while Windows reports "Error: Unknown option '--local'". A user following macOS documentation
on Windows is told the flag does not exist, rather than that it is known and not yet supported.

---

## Resolved during this session — not a finding

`parity-transcript` read as macOS-only for `--vectors` and `--list-only` earlier in the session. The
C# twin landed at 22:03 (`Windows/dotNET/Syxx/Commands/ParityTranscriptCommand.cs:23-27`) and the two
surfaces now agree at 3 accepted flags each (`--out=`, `--vectors=`, `--list-only`). Nothing was
ledgered for it. Reading the landed twin also exposed a real extractor gap — item 4 below.

---

## Extractor corrections made in this pass

The first version of this gate reported three of its own exceptions as stale. All three were
extractor defects, not resolved divergences: the source divergences were still there.

1. **ADVERTISED read any prose literal.** `witness --force` and `cage --output` appear in macOS
   *error messages* ("Use --force to overwrite"), so both platforms looked to advertise them and the
   D3 divergences vanished. ADVERTISED is now restricted to literals containing `Usage:`, which
   covers the single-line usage strings and the multi-line help blocks alike.
2. **ACCEPTED read any flag-shaped literal.** `SyxxCage.swift:447` passes `["-n", "get", "default"]`
   to a *subprocess*; that was counted as a `syxx` flag. ACCEPTED now requires an argv-comparison
   context (`==` / `!=` / `.Equals` / `contains` / `hasPrefix` / `StartsWith`).
3. **Dead-case detection read only the switch expression.** C# switches on
   `args[0].ToLowerInvariant()`, but Swift binds first (`let subcommand = args[1].lowercased()`) and
   switches on the name — so macOS reported zero dead arms and its unreachable `-V` read as a live
   flag Windows was missing, inverting the finding. The gate now follows one local binding.
4. **`string.Equals(x, "lit", …)` was not recognised** — only the instance form
   `x.Equals("lit", …)`. Caught by the freshly landed C# `--list-only`, which uses the static form.
5. **Alias-group differences were reported once per member.** `-v` joining the Windows version group
   made `--version` and `-V` "differ" too, so one fact produced three failures. The groupings are now
   compared as partitions and reported once per connected component of regrouped flags.

A proper string-literal scanner also replaced the single-line regex, because Swift `"""` blocks and
C# `@"…"` verbatim blocks are exactly where both platforms keep their multi-line usage text — the
text the ADVERTISED surface is read from.

---

## Verification

### (a) Real run against the tree as-is — exit 1

```text
Syxx CLI surface parity gate — track cross-platform-parity_20260805, Tasks 3.1/3.2
  macOS   :  17 .swift under macOS/Sources/SyxxCLI  (dispatcher SyxxCLI.swift, globals SyxxConfig.swift)
  Windows :  29 .cs    under Windows/dotNET/Syxx  (dispatcher Program.cs, globals SyxxConfig.cs)

[1] Top-level dispatch surface
------------------------------
  macOS    switch subject 'subcommand' — lowercased via subcommand = args[1].lowercased() at macOS/Sources/SyxxCLI/SyxxCLI.swift:48
  Windows  switch subject '(args[0].ToLowerInvariant())' — lowercased via (args[0].ToLowerInvariant())

  token                  macOS                                  Windows
  cage                   macOS/Sources/SyxxCLI/SyxxCLI.swift:71 Windows/dotNET/Syxx/Program.cs:84
  filter                 macOS/Sources/SyxxCLI/SyxxCLI.swift:79 Windows/dotNET/Syxx/Program.cs:88
  firstrun               macOS/Sources/SyxxCLI/SyxxCLI.swift:81 Windows/dotNET/Syxx/Program.cs:89
  forensics              macOS/Sources/SyxxCLI/SyxxCLI.swift:67 Windows/dotNET/Syxx/Program.cs:82
  guided                 absent                                 Windows/dotNET/Syxx/Program.cs:66
  key                    macOS/Sources/SyxxCLI/SyxxCLI.swift:61 Windows/dotNET/Syxx/Program.cs:79
  lan                    macOS/Sources/SyxxCLI/SyxxCLI.swift:65 Windows/dotNET/Syxx/Program.cs:81
  leak                   macOS/Sources/SyxxCLI/SyxxCLI.swift:57 Windows/dotNET/Syxx/Program.cs:77
  mirror                 macOS/Sources/SyxxCLI/SyxxCLI.swift:55 Windows/dotNET/Syxx/Program.cs:76
  nslook                 macOS/Sources/SyxxCLI/SyxxCLI.swift:53 Windows/dotNET/Syxx/Program.cs:75
  parity-transcript      macOS/Sources/SyxxCLI/SyxxCLI.swift:84 Windows/dotNET/Syxx/Program.cs:91
  route                  macOS/Sources/SyxxCLI/SyxxCLI.swift:63 Windows/dotNET/Syxx/Program.cs:80
  sentinel               macOS/Sources/SyxxCLI/SyxxCLI.swift:69 Windows/dotNET/Syxx/Program.cs:83
  speed                  macOS/Sources/SyxxCLI/SyxxCLI.swift:75 Windows/dotNET/Syxx/Program.cs:86
  unbound                macOS/Sources/SyxxCLI/SyxxCLI.swift:77 Windows/dotNET/Syxx/Program.cs:87
  warp                   macOS/Sources/SyxxCLI/SyxxCLI.swift:73 Windows/dotNET/Syxx/Program.cs:85
  witness                macOS/Sources/SyxxCLI/SyxxCLI.swift:59 Windows/dotNET/Syxx/Program.cs:78
  --help                 macOS/Sources/SyxxCLI/SyxxCLI.swift:88 Windows/dotNET/Syxx/Program.cs:100
  --version              macOS/Sources/SyxxCLI/SyxxCLI.swift:93 Windows/dotNET/Syxx/Program.cs:94
  -h                     macOS/Sources/SyxxCLI/SyxxCLI.swift:88 Windows/dotNET/Syxx/Program.cs:101
  -v                     absent                                 Windows/dotNET/Syxx/Program.cs:95
  -V                     macOS/Sources/SyxxCLI/SyxxCLI.swift:93 DEAD Windows/dotNET/Syxx/Program.cs:96 DEAD

  macOS   : 16 commands, 3 reachable flags, 1 dead arm(s)
  Windows : 17 commands, 4 reachable flags, 1 dead arm(s)

[1b] Unknown-command exit code
------------------------------
  macOS   : 3  (macOS/Sources/SyxxCLI/SyxxCLI.swift:98)
  Windows : 3  (Windows/dotNET/Syxx/Program.cs:113)
  PASS      both exit 3 on an unknown subcommand

[2] Shared global-flag handler (every subcommand's --help / --version)
----------------------------------------------------------------------
  flag           macOS alias group                Windows alias group
  --help         --help, -h                       --help, -h
  --json         --json                           absent
  --verbose      --verbose, -v                    absent
  --version      --version, -V                    --version, -V, -v
  -V             --version, -V                    --version, -V, -v
  -h             --help, -h                       --help, -h
  -v             --verbose, -v                    --version, -V, -v

[3] Per-command surface (accepted flags / advertised flags / action words)
--------------------------------------------------------------------------
  command            macOS a/d/w    Windows a/d/w  usage    files
  cage               9/2/0          6/3/0          Y/Y      SyxxCage.swift | CageCommand.cs
  filter             0/0/0          0/0/0          Y/Y      SyxxFilter.swift | FilterCommand.cs
  firstrun           4/0/0          2/1/0          n/Y      FirstRunScan.swift | FirstRunCommand.cs
  forensics          5/6/0          5/6/0          Y/Y      SyxxForensics.swift | ForensicsCommand.cs
  guided             0/0/0          0/0/0          n/n      NONE | NONE
  key                0/0/0          0/0/0          Y/Y      SyxxKey.swift | KeyCommand.cs
  lan                0/0/0          0/0/0          Y/Y      SyxxLan.swift | LanCommand.cs
  leak               10/5/0         10/5/0         Y/Y      SyxxLeak.swift | LeakCommand.cs
  mirror             2/1/0          2/1/0          Y/Y      SyxxMirror.swift | MirrorCommand.cs
  nslook             2/2/0          2/2/0          Y/Y      SyxxNSLook.swift | NslookCommand.cs
  parity-transcript  3/0/0          3/0/0          n/n      ParityTranscript.swift | ParityTranscriptCommand.cs
  route              0/0/0          0/0/0          Y/Y      SyxxRoute.swift | RouteCommand.cs
  sentinel           4/4/0          4/4/0          Y/Y      SyxxSentinel.swift | SentinelCommand.cs
  speed              0/0/0          0/0/0          Y/Y      SyxxSpeed.swift | SpeedCommand.cs
  unbound            0/0/5          0/0/4          Y/Y      SyxxUnbound.swift | UnboundCommand.cs
  warp               1/1/0          1/1/0          Y/Y      SyxxWarp.swift | WarpCommand.cs
  witness            14/15/4        14/16/4        Y/Y      SyxxWitness.swift | WitnessCommand.cs

  macOS   : 54 accepted flags, 36 advertised flags, 9 action words, 14 of 17 commands with usage text
  Windows : 49 accepted flags, 39 advertised flags, 8 action words, 15 of 17 commands with usage text

  firstrun: advertised-flag comparison SUPPRESSED — only Windows has usage text (1 flag(s) advertised
  there, 0 on the other side). Reported once as the `usage` asymmetry below.

[4] Asymmetries against the exception table
-------------------------------------------
  9 LEDGERED, 9 unledgered

  LEDGERED  [F-08] top.topflag '-v' — Windows only
              Windows/dotNET/Syxx/Program.cs:95
              other side: absent
              `syxx -v` prints a version on Windows/Linux (E088) and is the verbose flag on macOS (B240).
              Two documented decisions in direct conflict; resolving it changes a shipped flag's meaning
              on at least one platform, so it is owner-gated (plan Task 8.8), not agent-fixable.
  LEDGERED  [F-08 addendum (2026-08-05)] top.deadcase '-V' — macOS only
              macOS/Sources/SyxxCLI/SyxxCLI.swift:93
              other side: n/a
              SyxxCLI.swift lowercases args[1] before the switch, so the `-V` arm cannot fire. Proven by
              execution: `syxx -V` exits 3 'Unknown subcommand'. macOS therefore has NO working short
              version flag at top level. Fix is part of Task 8.8.
  LEDGERED  [F-08 addendum (2026-08-05)] top.deadcase '-V' — Windows only
              Windows/dotNET/Syxx/Program.cs:96
              other side: n/a
              Program.cs lowercases args[0] the same way, so the `-V` arm is equally dead here. It is
              harmless on this platform only because the `-v` arm above it accepts the lowercased token.
              Removing `-v` without removing `-V` would silently drop both.
  LEDGERED  [F-08] <global-flags>.globalflag '--verbose' — macOS only
              macOS/Sources/SyxxKit/SyxxConfig.swift:67
              other side: absent
              C# has no verbose flag at all, which is what makes E088's cost invisible today: handing `-v`
              to --version costs nothing until Windows gains a verbose flag and cannot spell it the way
              macOS does.
  LEDGERED  [F-08] <global-flags>.alias '--version,-V,-v' — present on both, meaning differs
              macOS splits them {--version, -V} / {-v} at macOS/Sources/SyxxKit/SyxxConfig.swift:50
              Windows splits them {--version, -V, -v} at Windows/dotNET/Syxx.Kit/SyxxConfig.cs:33
              All three flags are accepted by both handlers, so a flat comparison calls them identical.
              They are not: `-v` is the flag that moved. macOS groups it with --verbose and keeps the
              version group to {--version, -V}; Windows folds it into the version group. `syxx nslook -v`
              prints a version string on one platform and enables verbose output on the other.
  LEDGERED  [D3 (cli-capture-report.md)] cage.advertised '--output' — Windows only
              Windows/dotNET/Syxx/Commands/CageCommand.cs:34
              other side: absent
              `cage` advertises [--output <dir>] on Windows only, though both platforms accept the flag.
              Captured 2026-08-05 by executing both builds' `cage --help`.
  LEDGERED  [D5 (cli-capture-report.md)] firstrun.usage 'usage-text' — Windows only
              Windows/dotNET/Syxx/Commands/FirstRunCommand.cs
              other side: no usage text in source
              macOS `firstrun` carries no usage literal because it never calls the global-flag handler:
              `syxx firstrun --help` does not print help, it starts the live five-minute scan (captured
              2026-08-05, killed at 8s by SIGALRM). Asking for help must never start a network scan. The
              advertised-flag comparison for `firstrun` is suppressed while this holds — there is no macOS
              help text to compare against.
  LEDGERED  [D3 (cli-capture-report.md)] unbound.action 'verify-dnscrypt' — macOS only
              macOS/Sources/SyxxCLI/Commands/SyxxUnbound.swift:23
              other side: absent
              The Unbound -> dnscrypt-proxy -> Anonymized-DNSCrypt chain proof exists only on macOS. `syxx
              unbound verify-dnscrypt` on Windows/Linux falls to the unknown-action branch.
  LEDGERED  [D3 (cli-capture-report.md)] witness.advertised '--force' — Windows only
              Windows/dotNET/Syxx/Commands/WitnessCommand.cs:79
              other side: absent
              Both platforms ACCEPT --force on --gen-keypair; only the Windows usage text mentions it. A
              macOS user reading the help has no way to learn the escape hatch from the 'private key
              already exists' refusal — which names --force in its error text.

  FAIL      unledgered asymmetry — top.command 'guided' (Windows)
            Windows : Windows/dotNET/Syxx/Program.cs:66
            macOS   : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — <global-flags>.globalflag '--json' (macOS)
            macOS   : macOS/Sources/SyxxKit/SyxxConfig.swift:72
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--interface' (macOS)
            macOS   : macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:84
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--owner-gid' (macOS)
            macOS   : macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:104
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--owner-uid' (macOS)
            macOS   : macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:95
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '-i' (macOS)
            macOS   : macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:84
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--out' (Windows)
            Windows : Windows/dotNET/Syxx/Commands/CageCommand.cs:71
            macOS   : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — firstrun.accepted '--local' (macOS)
            macOS   : macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift:38
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — firstrun.accepted '-l' (macOS)
            macOS   : macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift:38
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.

[5] Exception-table hygiene
---------------------------
  PASS      all 9 exception(s) still correspond to a real asymmetry

CLI SURFACE GATE FAILED — 9 unledgered asymmetry / stale exception(s). Build is RED.
```

### (b) Proven able to fail — exit 1, naming the injected tokens

A copy of `macOS/Sources/SyxxCLI` was made under the session scratchpad, and a fake command
`teleport` plus a fake top-level flag `--fabricate` were injected **into the copy only**. The gate was
then pointed at the copy with `--swift-cli-root`. **The real tree was never modified**, confirmed
three ways after the run:

```text
$ cmp macOS/Sources/SyxxCLI/SyxxCLI.swift $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift
macOS/Sources/SyxxCLI/SyxxCLI.swift $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift differ: char 2730, line 53

$ grep -rn 'teleport\|"--fabricate"' macOS/Sources/ Windows/dotNET/
(no matches)
```

and by run (a) above — executed from the same working tree — reporting neither token anywhere.

The failure list of run (b) differs from run (a) by exactly the two injected tokens and nothing else:

```text
$ diff <(grep -o 'unledgered asymmetry — .*' run_a.txt) \
       <(grep -o 'unledgered asymmetry — .*' run_b.txt)
0a1
> unledgered asymmetry — top.command 'teleport' (macOS)
1a3
> unledgered asymmetry — top.topflag '--fabricate' (macOS)
```

Full output of run (b):

```text
Syxx CLI surface parity gate — track cross-platform-parity_20260805, Tasks 3.1/3.2
  macOS   :  17 .swift under $SCRATCH/mutant/SyxxCLI  (dispatcher SyxxCLI.swift, globals SyxxConfig.swift)
  Windows :  29 .cs    under Windows/dotNET/Syxx  (dispatcher Program.cs, globals SyxxConfig.cs)

[1] Top-level dispatch surface
------------------------------
  macOS    switch subject 'subcommand' — lowercased via subcommand = args[1].lowercased() at $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:48
  Windows  switch subject '(args[0].ToLowerInvariant())' — lowercased via (args[0].ToLowerInvariant())

  token                  macOS                                  Windows
  cage                   $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:75 Windows/dotNET/Syxx/Program.cs:84
  filter                 $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:83 Windows/dotNET/Syxx/Program.cs:88
  firstrun               $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:85 Windows/dotNET/Syxx/Program.cs:89
  forensics              $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:71 Windows/dotNET/Syxx/Program.cs:82
  guided                 absent                                 Windows/dotNET/Syxx/Program.cs:66
  key                    $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:65 Windows/dotNET/Syxx/Program.cs:79
  lan                    $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:69 Windows/dotNET/Syxx/Program.cs:81
  leak                   $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:61 Windows/dotNET/Syxx/Program.cs:77
  mirror                 $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:59 Windows/dotNET/Syxx/Program.cs:76
  nslook                 $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:57 Windows/dotNET/Syxx/Program.cs:75
  parity-transcript      $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:88 Windows/dotNET/Syxx/Program.cs:91
  route                  $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:67 Windows/dotNET/Syxx/Program.cs:80
  sentinel               $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:73 Windows/dotNET/Syxx/Program.cs:83
  speed                  $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:79 Windows/dotNET/Syxx/Program.cs:86
  teleport               $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:53 absent
  unbound                $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:81 Windows/dotNET/Syxx/Program.cs:87
  warp                   $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:77 Windows/dotNET/Syxx/Program.cs:85
  witness                $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:63 Windows/dotNET/Syxx/Program.cs:78
  --fabricate            $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:55 absent
  --help                 $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:92 Windows/dotNET/Syxx/Program.cs:100
  --version              $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:97 Windows/dotNET/Syxx/Program.cs:94
  -h                     $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:92 Windows/dotNET/Syxx/Program.cs:101
  -v                     absent                                 Windows/dotNET/Syxx/Program.cs:95
  -V                     $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:97 DEAD Windows/dotNET/Syxx/Program.cs:96 DEAD

  macOS   : 17 commands, 4 reachable flags, 1 dead arm(s)
  Windows : 17 commands, 4 reachable flags, 1 dead arm(s)

[1b] Unknown-command exit code
------------------------------
  macOS   : 3  ($SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:102)
  Windows : 3  (Windows/dotNET/Syxx/Program.cs:113)
  PASS      both exit 3 on an unknown subcommand

[2] Shared global-flag handler (every subcommand's --help / --version)
----------------------------------------------------------------------
  flag           macOS alias group                Windows alias group
  --help         --help, -h                       --help, -h
  --json         --json                           absent
  --verbose      --verbose, -v                    absent
  --version      --version, -V                    --version, -V, -v
  -V             --version, -V                    --version, -V, -v
  -h             --help, -h                       --help, -h
  -v             --verbose, -v                    --version, -V, -v

[3] Per-command surface (accepted flags / advertised flags / action words)
--------------------------------------------------------------------------
  command            macOS a/d/w    Windows a/d/w  usage    files
  cage               9/2/0          6/3/0          Y/Y      SyxxCage.swift | CageCommand.cs
  filter             0/0/0          0/0/0          Y/Y      SyxxFilter.swift | FilterCommand.cs
  firstrun           4/0/0          2/1/0          n/Y      FirstRunScan.swift | FirstRunCommand.cs
  forensics          5/6/0          5/6/0          Y/Y      SyxxForensics.swift | ForensicsCommand.cs
  guided             0/0/0          0/0/0          n/n      NONE | NONE
  key                0/0/0          0/0/0          Y/Y      SyxxKey.swift | KeyCommand.cs
  lan                0/0/0          0/0/0          Y/Y      SyxxLan.swift | LanCommand.cs
  leak               10/5/0         10/5/0         Y/Y      SyxxLeak.swift | LeakCommand.cs
  mirror             2/1/0          2/1/0          Y/Y      SyxxMirror.swift | MirrorCommand.cs
  nslook             2/2/0          2/2/0          Y/Y      SyxxNSLook.swift | NslookCommand.cs
  parity-transcript  3/0/0          3/0/0          n/n      ParityTranscript.swift | ParityTranscriptCommand.cs
  route              0/0/0          0/0/0          Y/Y      SyxxRoute.swift | RouteCommand.cs
  sentinel           4/4/0          4/4/0          Y/Y      SyxxSentinel.swift | SentinelCommand.cs
  speed              0/0/0          0/0/0          Y/Y      SyxxSpeed.swift | SpeedCommand.cs
  teleport           0/0/0          0/0/0          n/n      NONE | NONE
  unbound            0/0/5          0/0/4          Y/Y      SyxxUnbound.swift | UnboundCommand.cs
  warp               1/1/0          1/1/0          Y/Y      SyxxWarp.swift | WarpCommand.cs
  witness            14/15/4        14/16/4        Y/Y      SyxxWitness.swift | WitnessCommand.cs

  macOS   : 54 accepted flags, 36 advertised flags, 9 action words, 14 of 18 commands with usage text
  Windows : 49 accepted flags, 39 advertised flags, 8 action words, 15 of 18 commands with usage text

  firstrun: advertised-flag comparison SUPPRESSED — only Windows has usage text (1 flag(s) advertised
  there, 0 on the other side). Reported once as the `usage` asymmetry below.

[4] Asymmetries against the exception table
-------------------------------------------
  9 LEDGERED, 11 unledgered

  LEDGERED  [F-08] top.topflag '-v' — Windows only
              Windows/dotNET/Syxx/Program.cs:95
              other side: absent
              `syxx -v` prints a version on Windows/Linux (E088) and is the verbose flag on macOS (B240).
              Two documented decisions in direct conflict; resolving it changes a shipped flag's meaning
              on at least one platform, so it is owner-gated (plan Task 8.8), not agent-fixable.
  LEDGERED  [F-08 addendum (2026-08-05)] top.deadcase '-V' — macOS only
              $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:97
              other side: n/a
              SyxxCLI.swift lowercases args[1] before the switch, so the `-V` arm cannot fire. Proven by
              execution: `syxx -V` exits 3 'Unknown subcommand'. macOS therefore has NO working short
              version flag at top level. Fix is part of Task 8.8.
  LEDGERED  [F-08 addendum (2026-08-05)] top.deadcase '-V' — Windows only
              Windows/dotNET/Syxx/Program.cs:96
              other side: n/a
              Program.cs lowercases args[0] the same way, so the `-V` arm is equally dead here. It is
              harmless on this platform only because the `-v` arm above it accepts the lowercased token.
              Removing `-v` without removing `-V` would silently drop both.
  LEDGERED  [F-08] <global-flags>.globalflag '--verbose' — macOS only
              macOS/Sources/SyxxKit/SyxxConfig.swift:67
              other side: absent
              C# has no verbose flag at all, which is what makes E088's cost invisible today: handing `-v`
              to --version costs nothing until Windows gains a verbose flag and cannot spell it the way
              macOS does.
  LEDGERED  [F-08] <global-flags>.alias '--version,-V,-v' — present on both, meaning differs
              macOS splits them {--version, -V} / {-v} at macOS/Sources/SyxxKit/SyxxConfig.swift:50
              Windows splits them {--version, -V, -v} at Windows/dotNET/Syxx.Kit/SyxxConfig.cs:33
              All three flags are accepted by both handlers, so a flat comparison calls them identical.
              They are not: `-v` is the flag that moved. macOS groups it with --verbose and keeps the
              version group to {--version, -V}; Windows folds it into the version group. `syxx nslook -v`
              prints a version string on one platform and enables verbose output on the other.
  LEDGERED  [D3 (cli-capture-report.md)] cage.advertised '--output' — Windows only
              Windows/dotNET/Syxx/Commands/CageCommand.cs:34
              other side: absent
              `cage` advertises [--output <dir>] on Windows only, though both platforms accept the flag.
              Captured 2026-08-05 by executing both builds' `cage --help`.
  LEDGERED  [D5 (cli-capture-report.md)] firstrun.usage 'usage-text' — Windows only
              Windows/dotNET/Syxx/Commands/FirstRunCommand.cs
              other side: no usage text in source
              macOS `firstrun` carries no usage literal because it never calls the global-flag handler:
              `syxx firstrun --help` does not print help, it starts the live five-minute scan (captured
              2026-08-05, killed at 8s by SIGALRM). Asking for help must never start a network scan. The
              advertised-flag comparison for `firstrun` is suppressed while this holds — there is no macOS
              help text to compare against.
  LEDGERED  [D3 (cli-capture-report.md)] unbound.action 'verify-dnscrypt' — macOS only
              $SCRATCH/mutant/SyxxCLI/Commands/SyxxUnbound.swift:23
              other side: absent
              The Unbound -> dnscrypt-proxy -> Anonymized-DNSCrypt chain proof exists only on macOS. `syxx
              unbound verify-dnscrypt` on Windows/Linux falls to the unknown-action branch.
  LEDGERED  [D3 (cli-capture-report.md)] witness.advertised '--force' — Windows only
              Windows/dotNET/Syxx/Commands/WitnessCommand.cs:79
              other side: absent
              Both platforms ACCEPT --force on --gen-keypair; only the Windows usage text mentions it. A
              macOS user reading the help has no way to learn the escape hatch from the 'private key
              already exists' refusal — which names --force in its error text.

  FAIL      unledgered asymmetry — top.command 'teleport' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:53
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — top.command 'guided' (Windows)
            Windows : Windows/dotNET/Syxx/Program.cs:66
            macOS   : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — top.topflag '--fabricate' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/SyxxCLI.swift:55
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — <global-flags>.globalflag '--json' (macOS)
            macOS   : macOS/Sources/SyxxKit/SyxxConfig.swift:72
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--interface' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/Commands/SyxxCage.swift:84
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--owner-gid' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/Commands/SyxxCage.swift:104
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--owner-uid' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/Commands/SyxxCage.swift:95
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '-i' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/Commands/SyxxCage.swift:84
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — cage.accepted '--out' (Windows)
            Windows : Windows/dotNET/Syxx/Commands/CageCommand.cs:71
            macOS   : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — firstrun.accepted '--local' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/Synthesis/FirstRunScan.swift:38
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.
  FAIL      unledgered asymmetry — firstrun.accepted '-l' (macOS)
            macOS   : $SCRATCH/mutant/SyxxCLI/Synthesis/FirstRunScan.swift:38
            Windows : absent
            Not in the exception table. Either the two surfaces are made to agree, or the divergence is filed and ledgered with its finding id.

[5] Exception-table hygiene
---------------------------
  PASS      all 9 exception(s) still correspond to a real asymmetry

CLI SURFACE GATE FAILED — 11 unledgered asymmetry / stale exception(s). Build is RED.
```

### (c) Anti-vacuity — exit 2, never 0

**c1 — extractor pointed at an empty directory:**

```text
$ python3 scripts/cli-surface-gate.py --swift-cli-root $SCRATCH/empty ; echo "EXIT=$?"
gate error: macOS SyxxCLI tree $SCRATCH/empty contains no .swift files
The gate could not run, so it reports CANNOT-RUN (exit 2), never a pass.
EXIT=2
```

**c2 — extractor pointed at a tree whose dispatch table was gutted** (12 of the 16 command arms
removed from a copy). This is the branch that matters. An empty directory is the easy case; a
*shrunken* extraction is the one that could plausibly be mistaken for a small, tidy surface.

```text
$ python3 scripts/cli-surface-gate.py --swift-cli-root $SCRATCH/thin/SyxxCLI ; echo "EXIT=$?"
gate error: macOS: extracted only 4 top-level command(s) (['firstrun', 'mirror', 'nslook', 'parity-transcript']); at least 15 are registered. The dispatch pattern no longer matches the source.
The gate could not run, so it reports CANNOT-RUN (exit 2), never a pass.
Syxx CLI surface parity gate — track cross-platform-parity_20260805, Tasks 3.1/3.2
  macOS   :  17 .swift under $SCRATCH/thin/SyxxCLI  (dispatcher SyxxCLI.swift, globals SyxxConfig.swift)
  Windows :  29 .cs    under Windows/dotNET/Syxx  (dispatcher Program.cs, globals SyxxConfig.cs)
EXIT=2
```

Note the exit code: 2, not 1. Four commands against sixteen would otherwise have produced twelve
"macOS is missing this command" failures — a red build for the wrong reason, and one a reader could
reasonably dismiss as noise. The gate says it could not run instead.

### Self-checks

```text
$ python3 -m py_compile scripts/cli-surface-gate.py
(no output — OK)

$ uv run --with ruff ruff check scripts/cli-surface-gate.py
All checks passed!
```

Two ruff findings were fixed rather than suppressed: `FURB188` (use `str.removesuffix`) and `EXE001`
(the file carries a shebang, so it is now mode 755, matching `scripts/cross-platform-parity-gate.py`).

---

## What this gate does NOT cover

Recorded so that a green run is not read as more than it is.

- **Flag semantics.** It compares names and grouping, not what a flag does with its value. The
  `cage --output` DIRECTORY-vs-FILE divergence at `CageCommand.cs:63-70` is invisible to it.
- **Positional arguments.** `syxx nslook <domain> [record_type] [@server]` and the `@resolver` idiom
  are not extracted. Only flags and action words are.
- **Exit codes other than the unknown-command one.** D2 (Swift exits 0 for every `<command> --help`
  while C# exits 2 for eleven of fifteen) is a runtime divergence among the global-flag handler's
  callers. It is not readable from the dispatch surface and is not asserted here.
- **Help text as prose.** D1 (`Syxx 2.0.0` + copyright line vs `syxx 2.0.0`) and D4 (cage wording)
  are text divergences. The gate reads flags out of usage text; it does not diff the text.
- **Alias grouping outside the shared handler.** Alias groups are computed only for the two
  `SyxxConfig` handlers, whose conditions are one flag-group per line. Per-command aliases
  (`--output`/`-o`) are compared as independent flags, so a per-command alias that regrouped would
  not be caught.
- **Reachability beyond the lowercasing rule.** An arm is reported dead only when the dispatch
  subject is lowercased. Any other way of making an arm unreachable is not modelled.

## Next

- Phase 6 Task 6.3 wires this into the `parity` job. It is **not** wired yet, and against the tree as
  it stands it would make that job red: N-1 through N-4 need filing, and then either fixing or
  ledgering with a finding id, before or as part of that wiring.
- Task 3.5 (catalog reconciliation) is untouched by this work.

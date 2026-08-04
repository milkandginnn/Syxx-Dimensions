# D278 Cross-Platform Parity Report

Date: 2026-08-01
Author: gui-cross-platform-continuity (Stage 3c, Workstream 1.0)
Commit under review: 69237bd (macOS Kit + App lanes)

## Overall verdict: FAIL → RESOLVED (fix landed 2026-08-01)

Items 1-3 and 5 **PASS**. Item 4 (startup unknown state renders identically on all three clients)
**FAILED** on Windows and Linux at the time of this report — both fabricated a resolver in their
run paths. The gap was then fixed (see "Parity fix landed" below), so the operational verdict for
the shipping tree is PASS with the Windows real-host build and real run still owed (host-gated).

## 1. Twin — PASS

- macOS: `macOS/Sources/SyxxKit/NetworkKeyState.swift:118-182` (`static func runProcess(_:_:timeout:)`).
- Windows: `Windows/dotNET/Syxx.Kit/NetworkKeyState.cs:302-329` (`internal static string? RunProcess(...)`), bounded wait at :318.
- Linux: inherits the Windows shape via `Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj:44`
  (`<ProjectReference Include="..\..\..\Windows\dotNET\Syxx.Kit\Syxx.Kit.csproj" />`).

## 2. Logic diff (macOS runProcess ↔ Windows RunProcess) — PASS

macOS `NetworkKeyState.swift`:
- `RunProcessState` sendable holder with `exited` + `drained` semaphores: :129-134.
- `terminationHandler` set BEFORE `run()`: :138 (handler) vs :155 (`try process.run()`).
- Concurrent pipe-drain thread started BEFORE `run()` (C112 read-handle constraint honored —
  read/close only on the drain thread): :147-152 vs :155.
- Bounded `exited.wait(timeout:)` whose return value IS the status: :162-169.
- SIGTERM → 0.5 s grace → SIGKILL escalation: :165-167.
- Final `waitUntilExit()` reap on both paths: :173.
- Timeout → nil (:178 bounded `drained.wait` +2.0 s → nil), kill → nil (:180), non-UTF-8 → nil (:181).

Windows `NetworkKeyState.cs`:
- Bounded `WaitForExit(TimeSpan.FromSeconds(timeoutSeconds))` returning bool = status: :318.
- `process.Kill(entireProcessTree: true)` on timeout, return null: :320-321.
- `ReadToEnd()` after confirmed exit (:323) — safe because the .NET framework drains the
  redirected pipe asynchronously; semantic twin of the macOS manual drain thread.
- `catch → return null`: :325-328. No semaphore handshake anywhere.

**No unsignalled-handshake path exists in either.** macOS has no `DispatchWorkItem` /
`timerWork` / `done.wait()` left in `runProcess` (verified by full-function read; only the two
bounded semaphores remain). Escalation note: Windows has no SIGTERM/SIGKILL distinction (.NET
`Kill` is the terminal kill); macOS keeps the two-stage escalation as the gold-standard shape —
platform-idiomatic, not a correctness divergence.

## 3. Linux inherits correct Kit path, unchanged — PASS (with git caveat)

- `SyxxUI.Avalonia.csproj:44` links Windows `Syxx.Kit`; the Linux client compiles the same
  `NetworkKeyState.cs` bytes. No `RunProcess`/semaphore shape exists in any Linux file (grep
  over `Linux/Ubuntu/SyxxUI.Avalonia/**` shows only `GetSystemDNSServers` callers).
- Cannot run git from this toolchain; the current file state is consistent with commit 69237bd
  being macOS-only (no ResolverState machine, no D278 markers in any Linux/Windows UI file).
  Git-verification of "no Linux files in 69237bd" is INCONCLUSIVE from this host.

## 4. Startup unknown state per client — FAIL on Windows and Linux

### macOS — PASS

`macOS/Sources/SyxxApp/AppModel.swift`:
- `checkStartup()` :859-900 fires the probe on `Task.detached` (:866) via the
  `systemResolverProbe` seam (:122-127), publishes through single-writer
  `applySystemResolverProbe` (:906-921).
- `ResolverState` enum `.checking/.measured/.unavailable`: :103-107; initial `.checking`: :319.
- `server` starts `""`: :134.
- `resetAll()` keeps the current value, never invents 8.8.8.8: :481-484.
- `runLookup()` gate with the two owner-approved strings: :939-944 (:940 "System resolver not yet
  measured. Prophet cannot audit an unknown resolver. Retry in a moment."; :943 "System resolver
  could not be determined. Check your network connection, then retry.").
- Consumers: `PortalView.swift:11-18` ("Checking system DNS servers…" at :14, consumed at :100);
  `DashboardView.swift:455-461` ("not determined" fallback); `WitnessView.swift:183` ("resolver
  not determined"); `PreviewSupport.swift:18-21` (DEBUG fixture sets `.measured` explicitly,
  single-writer). Server writers are only :484, :914, and the DEBUG fixture — no fabricated value.

### Windows — FIXED (post-report, owner approved "fix parity first")

- `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:393-411` (`RunProphetAsync`): removed the
  `?? "1.1.1.1"` ternary and the empty `if` + `// TODO` block. Now gates: when `Server` is
  whitespace AND no system resolver is measurable, sets `ProphetRunState = Failed` with the
  approved gate string and returns before `ProbePlan.RunProphetAsync`.
- `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:459-467` (`RunMirrorAsync`): removed the
  `local = "1.1.1.1"` fallback and `// TODO`. Now sets `MirrorRunState = Failed` with the approved
  gate string and returns before the probe.
- `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:728-737` (Witness seal): removed the
  `local = "1.1.1.1"` fallback. Now sets `WitnessTestimony = Failed` with the approved gate string
  and returns before the vantage comparison.
- `Windows/dotNET/SyxxUI/Views/ProphetView.xaml.cs:106` and `MirrorView.xaml.cs:87`: reason is
  `.TrimEnd('.')`-ed before the wrap so a refused run renders
  "The check could not complete: System resolver could not be determined. Check your network
  connection, then retry. This is not a clean result." (no double period).
- Static verification only; WinUI XAML compile is host-gated on this macOS host (the C#-compile
  of `Syxx.Kit` succeeded, `SyxxUI` failed in the Windows-only XamlCompiler before
  `AppViewModel.cs` was reached). A Windows-host build remains owed.

### Linux — FIXED (post-report, owner approved "fix parity first")

- `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ProphetViewModel.cs:33-41`: removed the `"8.8.8.8"`
  fallback and old warning. Gates when `ResolverIp` is whitespace AND no system resolver is
  measurable: sets `StatusLine` to the approved gate string, `IsRunning = false; HasResult = true`,
  returns before the probe.
- `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/MirrorViewModel.cs:39-48`: removed the `"8.8.8.8"`
  fallback and old warning. Gates on `servers.Count == 0`: sets `MatchLine` to the approved gate
  string, `MatchBrush = Amber`, returns before the probe.
- `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/WitnessViewModel.cs:108-116`: removed the `"8.8.8.8"`
  fallback. Gates on `servers.Count == 0`: sets `SignatureLine` to the approved gate string,
  `TestimonyState = Failed`, returns before `_runMirror`.
- Compiles clean on this macOS host (`dotnet build SyxxUI.Avalonia.csproj -c Debug`, 0 errors;
  warnings pre-existing). Linux runtime run not possible on this host.

## 5. Residual-pattern sweep (macOS Kit + App) — PASS, none launch-blocking

- `macOS/Sources/SyxxKit/LAN/CommandRunner.swift:144-180`: the only other `DispatchSemaphore` —
  `terminated` is signalled by a non-cancellable waitpid reaper thread (:158-166), all waits
  bounded (:168, :176-180), SIGTERM→0.25 s→SIGKILL (:170-173). Safe shape; not the D278
  cancellation deadlock.
- `macOS/Sources/SyxxApp/AppModel.swift:619-645` (`flushDNSCache`): `Process()` inside
  `Task.detached` (:621) — not main actor, user-triggered.
- `macOS/Sources/SyxxApp/AppModel.swift:1762-1800` (`locateCageBinary`/`findInPATH`): `private
  nonisolated static`, Cage user flow only — not launch.
- `checkStartup` main-actor entry fires `Task.detached` at :866 — no synchronous subprocess on
  the main actor at launch. No `DispatchWorkItem` + `wait()` pattern anywhere in the two trees.

## 6. Golden vector / suites / real run — INCONCLUSIVE

- Parity fixtures exist but cover determinative-core math only
  (`Windows/dotNET/Syxx.Tests/MatrixAllNegativeParityTests.cs:99`,
  `WitnessTestifyParityTests.cs:66`, `Linux/.../WitnessTestifyParityTests.cs:66` on
  `corpus/witness-testify-parity/`); none covers `runProcess` control flow. This is a
  control-flow change, not math, and Windows was the already-correct reference shape, so no new
  golden vector is strictly required for the Kit twin; a fixture would only be needed if the
  App-lane gate copy (:940/:943) needs cross-client pinning.
- Suites: macOS `swift test` (Kit 6/6 + App 8/8) reported green by implementers — NOT executed
  here. Windows `dotnet test` is host-gated — NOT runnable on this macOS host. Do not read either
  as PASS.
- Real run: resolving runs are (a) fresh bundle launch + sample scan, (b) guided-setup fresh
  HOME — NOT executed here.

## Remaining work

1. File the Windows/Linux startup-parity gap as a delta: `Windows/dotNET/SyxxUI/ViewModels/
   AppViewModel.cs:394` and `:450` (Windows), `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/
   ProphetViewModel.cs:34` / `MirrorViewModel.cs:39` / `WitnessViewModel.cs:108` (Linux)
   fabricate resolvers where macOS gates at `AppModel.swift:939-944`.
2. Bring Windows `RunProphetAsync`/`RunMirrorAsync` and the Linux Prophet/Mirror/Witness
   ViewModels to the macOS shape (gate on an unmeasured resolver with the owner-approved strings,
   never `"1.1.1.1"`/`"8.8.8.8"` fallback), per AGENTS.md "macOS is the gold standard; bring
   Windows and Linux to parity."

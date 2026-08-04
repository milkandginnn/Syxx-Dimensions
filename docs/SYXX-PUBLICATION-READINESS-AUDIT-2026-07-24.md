# Syxx Publication Readiness and Cross-Platform Code Health Audit

**Audit date:** 2026-07-24  
**Repository baseline:** `main` at `39579e9d077288ccf83054d86e43e65dfdaeafe6`, plus the current uncommitted working tree  
**Disposition:** **DO NOT PUBLISH OR SELL THIS BUILD**

## 1. Purpose and scope

This is an independent audit performed from the beginning under
`SYXX-FIX-JULY-23-prompt.md`. It does not amend `DELTAS.md`, `ROADMAP.md`,
`PROPOSALS.md`, any prior WS8 or WS9 report, or any product source.

The audit covers:

- the current macOS GUI in `macOS/Sources/SyxxApp`;
- the current Windows GUI in `Windows/SyxxUI`;
- the current Linux GUI in `Ubuntu/SyxxUI.Avalonia`;
- the macOS and C# shared reasoning and probe libraries;
- the Python server and public commerce surface;
- GUI actions, hangs, privilege boundaries, tri-state honesty, packaging,
  commerce, CI, and platform parity.

The retired Xcode template under `Syxx/Syxx`, files under `Syxx GUI Mockups`,
and deleted legacy GUI components were excluded from product findings.

Syxx was originally a macOS-only product. Windows and Linux ports began while
the owner was waiting for a Developer ID. They previously followed the macOS
determinative implementation, then fell behind when the former MFAE-based
engine was removed and the matrix replacement began. Windows/Linux findings
therefore describe port readiness. They are not prerequisites for a
macOS-only launch unless those platforms are advertised or distributed as part
of that launch.

The tree was already heavily modified before this audit. Findings describe the
current on-disk state and are not represented as findings against a clean
commit. No source or ledger file was changed by this audit.

## 2. Executive decision

The current macOS product is an **internal alpha**, not a release candidate.

This is not a case where publication is being held up by polish, a short list
of cosmetic defects, or only the missing Developer ID. Current macOS and server
production paths can:

- display reassuring or successful states before supporting evidence exists;
- audit a public resolver after local resolver discovery fails, then present
  that result as an assessment of the user's network;
- hang or remain blocked past advertised timeouts;
- leave elevated capture or DNS-management processes running;
- report encrypted DNS as enabled or disabled when the requested OS state was
  not established;
- package a stale macOS CLI after the current CLI fails to compile;
- fail to offer a working purchase or install path.

Windows and Linux have additional port defects documented below. Those defects
block release of the affected ports, but do not contribute to the macOS-only
publication decision.

### macOS-only launch-gate summary

| Launch gate | Result | Basis |
|---|---:|---|
| Evidence-first macOS conclusions | **FAIL** | Fresh Dashboard claims, result-presence pass logic, resolver substitution, and encrypted-DNS state can produce unsupported claims |
| Reproducible macOS build and test baseline | **FAIL** | The GUI compiles, but full macOS tests and the current CLI do not compile; the wrapper masks failure and packages a stale CLI |
| Critical macOS GUI workflows | **FAIL** | Run-all omits work and suppresses failures; privilege lifecycles and hang paths remain |
| Server safety and truthful readiness | **FAIL** | DoT accept-loop denial of service, DNS listener spin, and unknown-listener false readiness remain |
| Installable and signed macOS distribution | **BLOCKED** | The current bundle is invalid and ad hoc; a Developer ID is also still unavailable |
| Commerce from product to fulfillment | **FAIL** | Dead purchase/download links, unconfigured stock commerce, contradictory prices |

**No macOS launch gate passes in full.** Windows/Linux parity is deliberately
excluded from this table. Several components compile and many tests pass, but
those partial positives do not establish a publishable macOS product.

## 3. Highest-priority publication blockers

### PR-01: macOS first-render and scan-result states are not evidence-backed

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** macOS GUI

The current Dashboard renders protected or reassuring claims on a fresh launch,
before scans have established them:

- `macOS/Sources/SyxxApp/DashboardView.swift:42`
- `macOS/Sources/SyxxApp/DashboardView.swift:61-65`
- `macOS/Sources/SyxxApp/DashboardView.swift:96`
- `macOS/Sources/SyxxApp/DashboardView.swift:192`

The same view marks a warden as passed based only on the presence of a result
object at `DashboardView.swift:375-383`. Redirection, degraded,
inconclusive, unknown, or otherwise hostile result objects still satisfy that
condition. Relevant producers include:

- `macOS/Sources/SyxxApp/AppModel.swift:650-683`
- `macOS/Sources/SyxxApp/AppModel.swift:692-711`
- `macOS/Sources/SyxxApp/AppModel.swift:725-750`
- `macOS/Sources/SyxxApp/AppModel.swift:781-815`

This violates the Verification and Honesty Protocol on the product's primary
GUI surface.

### PR-02: macOS "Conduct All Scans" suppresses failures, omits Cage, and causes disorienting tab flicker

**Severity:** Critical  
**Classification:** Code defect, UX defect, and test defect  
**Affected:** macOS GUI

The primary button at `macOS/Sources/SyxxApp/DashboardView.swift:111-120` promises to *"Summon all six wardens to the roads"*. However, the implementation at `macOS/Sources/SyxxApp/AppModel.swift:403-443`:

- **Omits Warden**: Never executes Cage (the 6th warden for packet capture);
- **Discards Results**: Discards returned result strings via `_ = await runCompare()`, `_ = await runNetworkState()`, etc., failing to store or display scan evidence;
- **Suppresses Errors**: Ignores scan failures and unconditionally concludes with `"All scans complete."`;
- **Disorienting Tab Flicker**: Rapidly switches `selection` sidebar state (`.prophet` $\rightarrow$ `.mirror` $\rightarrow$ `.portal` $\rightarrow$ `.glutton` $\rightarrow$ `.witness` $\rightarrow$ `.dashboard`), creating a disorienting "fair ride" tab-flipping effect without showing intermediate results;
- **Flawed Test Gate**: The unit test at `macOS/Tests/SyxxAppTests/AppModelWitnessTests.swift:229-251` executes a single Witness run through a separate method, failing to test `runAllWardens()` or detect omitted wardens.

**Required UX Specification**:
- **Single-Pane Progress**: Remain on the Dashboard view without switching active tabs.
- **In-Place Status**: Update the primary button caption in-place to scroll active scan stages (*"Running Prophet..."* $\rightarrow$ *"Running Mirror..."* $\rightarrow$ *"Checking Network..."*).
- **Evidence Persistence**: Save all returned scan outcomes to state.
- **Navigation Gate**: Transition the primary action button to *"View Synthesis Results ➔"* upon completion, guiding the user to the Conclusions view only after evidence is collected.

### PR-03: timeouts can still hang and startup performs blocking work

**Severity:** High  
**Classification:** Code defect  
**Affected:** macOS GUI and shared core

Both timeout helpers cancel the losing task inside a Swift task group:

- `macOS/Sources/SyxxApp/AppModel.swift:493-505`
- `macOS/Sources/SyxxKit/ProbeRunner.swift:44-60`

Leaving the task-group scope waits for a child that ignores cancellation. A
structured reproduction with a nominal 100 ms timeout did not return until the
two-second child exited.

`NetworkKeyState.swift:101-121` also waits until its configured semaphore
deadline even when the child exits earlier. Startup calls this synchronous path
on the main actor at `AppModel.swift:608-613`, from
`ContentView.swift:78`. A production `syxx key` run took 16.07 seconds. Its
network failure was honestly reported as unknown, but the elapsed time confirms
the blocking behavior.

`AppModel.swift:446-460` adds an unbounded main-actor `waitUntilExit()` for DNS
cache flushing and reports success without checking the process exit status.

### PR-04: privilege-management paths are broken or unsafe

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** macOS and Windows

#### macOS

- The main Unbound GUI path calls the root-requiring manager directly without
  an escalation mechanism:
  `macOS/Sources/SyxxApp/AppModel.swift:236-270` and
  `macOS/Sources/SyxxKit/UnboundManager.swift:398-485`.
- A config is written before root ownership is restored, making re-enable fail
  from an unprivileged process:
  `UnboundManager.swift:663-679`.
- Stored resolver strings are replayed without IP validation, and disable
  removes protection even after DNS restoration fails:
  `UnboundManager.swift:295-320` and `:455-505`.
- Cage interpolates paths into a privileged shell string:
  `macOS/Sources/SyxxApp/AppModel.swift:1026-1032`.
- Stop terminates the `osascript` wrapper, not necessarily the elevated Cage
  child:
  `AppModel.swift:977-984` and `:1562-1569`.
- The privileged CLI opens and changes ownership of a user-announced pathname,
  leaving a symlink race:
  `macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:175-184`.
- A Cage executable found on `PATH` can later be run as root without signature
  or ownership verification:
  `AppModel.swift:1323-1360`.

#### Windows

- Every Unbound invocation constructs a new manager:
  `Windows/Syxx/Commands/UnboundCommand.cs:58-70`.
- Process state and original DNS state live only in that manager instance:
  `Windows/Syxx.Kit/UnboundManager.cs:89-105`, `:129-130`, and `:230-240`.
  A later disable loses the process and restore snapshot.
- Disable stops the resolver even if restoration fails:
  `UnboundManager.cs:152-166`.
- The Cage GUI stops its wrapper but cannot reliably terminate the privileged
  child, clears its handle, and can start another:
  `Windows/SyxxUI/Views/CageView.xaml.cs:118-185`.

No destructive or privileged exploit reproduction was attempted during this
read-only audit.

### PR-05: local resolver discovery failure becomes public-resolver evidence

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** macOS, Windows, Linux, signed Witness evidence

All three clients silently substitute Google or Cloudflare when the system
resolver is missing:

- macOS:
  `macOS/Sources/SyxxKit/Synthesis/FirstRunScan.swift:60-66`;
  `macOS/Sources/SyxxApp/AppModel.swift:657`, `:690`, `:888`, and `:1468`.
- Windows:
  `Windows/SyxxUI/ViewModels/AppViewModel.cs:234-242`, `:268-274`, and
  `:434-442`;
  `Windows/SyxxUI/Views/WitnessView.xaml.cs:105-107`.
- Linux:
  `Windows/Syxx.Kit/Synthesis/FirstRunScan.cs:52-58`;
  `Ubuntu/SyxxUI.Avalonia/ViewModels/ProphetViewModel.cs:31-39`;
  `MirrorViewModel.cs:37-44`;
  `WitnessViewModel.cs:106-110`.

The fallback may preserve the ability to query DNS, but it does not measure the
user's missing, blocked, or malicious local resolver. Presenting the public
resolver's result as the local-network assessment is false evidence. In Witness
paths it can be signed as the local vantage.

Required behavior is an explicit inconclusive local component. A separately
labeled public control query may still be useful, but it cannot replace the
missing local datum.

### PR-06: encrypted-DNS controls can report the opposite of OS state

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** macOS, Windows, Linux

#### macOS

- DoT is configured, then checked using a DoH endpoint:
  `macOS/Sources/SyxxApp/EncryptedDNSConfigurator.swift:38-41` and `:71-83`.
- Successful and failed disables collapse to the same failure state:
  `EncryptedDNSConfigurator.swift:88-97`.
- The toggle persists off before the OS operation, and disable failure is
  rendered as `Encryption is off`:
  `macOS/Sources/SyxxApp/SettingsView.swift:27-30` and `:135-145`.
- Dashboard labels any positive encrypted state as DoT:
  `DashboardView.swift:416-418`.

#### Windows

- Turning the setting off does not perform the matching OS disable but can
  display `Encryption is off`.
- The selected transport is ignored and the implementation always configures
  DoH:
  `Windows/Syxx.Kit/EncryptedDNSConfigurator.cs:39-42`.
- Registering templates is not equivalent to assigning them to active
  interfaces:
  `Windows/Syxx.Kit/EncryptedDNSConfigurator.cs:71-88`.

#### Linux

- Toggle-off performs no revert:
  `Ubuntu/SyxxUI.Avalonia/ViewModels/SettingsViewModel.cs:61-69`.
- The UI still describes the state as off:
  `SettingsViewModel.cs:81-89`.
- The shared configurator ignores the selected Linux transport and uses an
  invalid or incomplete per-link `resolvectl` flow:
  `Windows/Syxx.Kit/EncryptedDNSConfigurator.cs:39-43` and `:91-102`.
- Verification probes the DoH URL even for DoT:
  `EncryptedDNSConfigurator.cs:64-68`.
- Linux state parsing looks for fields that do not match the normal
  `resolvectl status` protocol grammar:
  `Windows/Syxx.Kit/NetworkKeyState.cs:250-269`.

These are security-state controls. Intent, attempted configuration, verified
state, failure, and unknown must remain distinct.

### PR-07: Windows can print CLEAN with zero evidence

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** Windows CLI and report output

The C# synthesis path can produce diagnosis `Clean` with conclusivity
`Untested`:

- `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs:383-390`

Absent score values become zero:

- `SynthesisEngine.cs:425`

The CLI special-cases `Inconclusive`, but not `Untested`, and can print CLEAN,
0%, and clean severity:

- `Windows/Syxx/Program.cs:293-296`

The report path always prints diagnosis and confidence:

- `Windows/Syxx.Kit/Synthesis/Report.cs:109-120`

Zero evidence is not clean evidence. This is a direct false-secure output path.

### PR-08: the known matrix-engine migration is not yet in platform parity

**Severity:** Critical for a cross-platform release  
**Classification:** Known in-progress port migration  
**Affected:** macOS, Windows, Linux, CI

This is not an independently discovered architectural issue. The owner removed
the former MFAE-based determinative engine from macOS three days before this
audit and is replacing it with the matrix engine. `DESIGN.md:111-121` and
`MATRIX-MIGRATION-PLAN.md` already identify the current transitional state.

The audit confirms that the transition is not complete enough for a
cross-platform release:

- macOS production uses the matrix engine:
  `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:29-70`.
- Windows production still enters residual code from the former MFAE-based
  engine:
  `Windows/Syxx.Kit/Synthesis/Synthesizer.cs:848-924`.
- The C# matrix integration explicitly states that production does not consume
  it:
  `Windows/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs:9-18`.
- Linux links `Windows/Syxx.Kit` and therefore inherits the Windows production
  behavior:
  `Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj:42-45`.
- Linux renders a confidence percentage even though the current matrix contract
  has no such calibrated number:
  `Windows/SyxxUI/Presentation/Stage5DisplayModel.cs:73-78` and
  `Ubuntu/SyxxUI.Avalonia/Views/ConclusionsView.axaml:85-91`.

The CI transcript-parity step is a placeholder that exits successfully without
comparing transcripts:

- `.github/workflows/ci.yml:145-172`

**Parity result: FAIL, known and in progress.** There is not yet a
behavior-identical macOS, Windows, and Linux production core, an active
transcript gate, or a real three-platform golden run. This blocks release of
the Windows and Linux ports. It does not block a macOS-only launch, and it
should not be counted as a newly uncovered defect.

### PR-09: the current Windows GUI is source-incompatible with the current core

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** Windows GUI

The current conclusion contract removed legacy `Verdict` and
`Hypothesis.Inconclusive` members:

- `Windows/Syxx.Kit/Synthesis/Conclusion.cs:13-31` and `:378-389`

The current WinUI source still references them:

- `Windows/SyxxUI/Views/ConclusionsView.xaml.cs:40`
- `Windows/SyxxUI/Views/DashboardView.xaml.cs:84-87`

A Windows-host WinUI build was not available during this macOS audit. The
cross-host build could not execute the Windows XAML compiler. The missing
members are nevertheless a source-level compile break, not a runtime
hypothesis.

### PR-10: the Linux flagship Mirror path enters Windows-only licensing

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** Linux

Linux Mirror Lite calls the plan with `isPro: false`:

- `Ubuntu/SyxxUI.Avalonia/ViewModels/MirrorViewModel.cs:46-48`

The plan still calls ContentWarp and license evaluation:

- `Windows/Syxx.Kit/ProbePlan.cs:135-147`

Ordinary trial evaluation reaches Windows DPAPI:

- `Windows/Syxx.Kit/LicenseManager.cs:518-599`

The shared library declares Windows cryptography imports at
`LicenseManager.cs:17-100`. Rebuilding with CA1416 promoted to an error produced
10 cross-platform failures at lines 212, 221, 291, 336, 356, 370, 532, 547,
570, and 580.

When that path fails, Linux discards the DNS comparison already performed and
replaces the whole result with a generic Mirror failure.

### PR-11: current Linux primary actions are visual no-ops

**Severity:** High  
**Classification:** Code defect  
**Affected:** Linux GUI

These current Avalonia controls are `Border` and `TextBlock` structures with no
command:

- Conduct All Scans:
  `Ubuntu/SyxxUI.Avalonia/Views/DashboardView.axaml:54-65`
- Flush DNS Cache: `DashboardView.axaml:69-83`
- Enable Unbound DNS: `DashboardView.axaml:84-98`
- Enable Encrypted DNS: `DashboardView.axaml:99-113`
- Begin the trial: `DashboardView.axaml:267-283`
- Start your story anew:
  `Ubuntu/SyxxUI.Avalonia/Views/MainWindow.axaml:91-100`

The Dashboard view model exposes no commands or session update API:

- `Ubuntu/SyxxUI.Avalonia/ViewModels/DashboardViewModel.cs:12-33`

Views are constructed independently, so a Conclusions scan does not update the
Dashboard's static `NOT RUN` state:

- `Ubuntu/SyxxUI.Avalonia/ViewModels/ShellViewModel.cs:34-47`

This confirms the user's concern that visible buttons or button-like controls
appear to do nothing. Cage is more honest: it explicitly says graphical
elevation is not wired rather than fabricating a capture.

### PR-17: Leak Detection broken by sub-domain label syntax and client JSON decoding crash

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** macOS GUI (`AppModel.swift`), `LeakDetector.swift`, `syxx_server/leak.py`

Leak Detection previously worked but broke due to three compounding flaws:

1. **RFC 1035 Label Formatting**: `Server/syxx_server/leak.py:147` generates tokens using `base64.b32encode(secrets.token_bytes(16))`. Base32 strings can randomly start with a digit (e.g. `3a...`). Strict upstream resolvers drop subdomains starting with digits per RFC 1035, preventing probes from reaching the collector.
2. **Client JSON Decoding Crash**: When a probe is dropped, the server returns `{"leaked": null}`. `LeakDetector.swift:328` typed `let leaked: Bool` (non-optional). `JSONDecoder` throws a `typeMismatch` exception on `null`, throwing an unhandled network error and masking the inconclusive result.
3. **AppModel Bypass**: `AppModel.swift:733` in `runLeak()` manually returned a local-only `.inconclusive` string without calling `runCollectorVerification()`.

**Code Fix Specification**:
- In `leak.py:147` & `LeakDetector.swift:114`, prepend `id-` to labels so subdomains always start with a letter (`id-b32token.leak.syxx.app`).
- In `LeakDetector.swift:328`, update `VerifyResponse` to `let leaked: Bool?`.
- In `AppModel.swift:733`, replace local short-circuit with a call to `await runCollectorVerification()`.

---

### PR-18: Witness snapshot creation and verification fail due to Swift Crypto trap and Keychain checks

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** macOS GUI (`AppModel.swift`), `WitnessSigner.swift`, `WitnessKeyManager.swift`

Witness testimony and verification fail due to two defects:

1. **Swift Crypto Precondition Trap**: `WitnessSigner.swift:105` passes raw base64-decoded bytes directly to `Curve25519.Signing.PrivateKey(rawRepresentation:)`. Swift Crypto has a strict 32-byte precondition that **traps/crashes** if byte count != 32. If key data is invalid or padded, the process traps instead of returning an error, leaving `witnessSignature = ""`.
2. **Spurious Private Key Guard on Public Verification**: `AppModel.swift:1499-1504` in `witnessVerify()` guards on `WitnessKeyManager.hasKey`. Verification requires only the *public* key (embedded in the `WitnessSnapshot` payload). Requiring the local private key breaks third-party testimony verification.

**Code Fix Specification**:
- In `WitnessSigner.swift:105`, assert `guard keyData.count == 32 else { throw WitnessSignerError.invalidPrivateKey }`.
- In `AppModel.swift:1499`, remove the `WitnessKeyManager.hasKey` check from `witnessVerify()` so signatures verify using the snapshot's embedded public key.

---

### PR-19: macOS GUI layout instability, window resizing, button spacing "jiggle", and logo clipping

**Severity:** High  
**Classification:** SwiftUI layout defect  
**Affected:** macOS GUI (`ContentView.swift`, `SidebarView.swift`, `DashboardView.swift`)

The GUI layout exhibits "jiggling" button spacing, window frame resizing, and logo clipping due to missing fixed-size layout bounds:

1. **Unbounded Sidebar Width & Window Resizing**: In `ContentView.swift:35-39` & `77`, the main `HStack` container lacks explicit `fixedSize()` or fixed sidebar frame bounds (`.frame(width: 236)`). Views with flexible intrinsic content sizes (like `WitnessView` or `ConclusionsView`) push the sidebar width when selected, causing the window to resize and buttons to shift position ("jiggle").
2. **Dynamic Logo Clipping**: `ContentView.swift:97-101` renders `Image("Syxx-logo").frame(width: 50, height: 50)`. Lacking `.fixedSize()` and explicit clipping bounds (`.clipped()`), large source images overflow the 50x50 frame, hanging off the container DOM.
3. **Flexible Button Padding**: `SidebarItem` buttons use flexible vertical padding without fixed height constraints (`.frame(height: 36)`), causing spacing between buttons to expand and contract depending on active view labels.

**Code Fix Specification**:
- Enforce explicit sidebar frame in `ContentView.swift`: `.frame(width: 236).frame(maxHeight: .infinity)`.
- Apply `.fixedSize()` to `SidebarItem` rows and explicit `.frame(height: 36)`.
- Add `.clipped()` to `Image("Syxx-logo")`.

---

### PR-20: Unbounded process execution, blocking `waitUntilExit()` on MainActor, and task group leaks

**Severity:** High  
**Classification:** Concurrency & process lifecycle defect  
**Affected:** macOS GUI (`AppModel.swift`), `NetworkKeyState.swift`, `ProbeRunner.swift`, `DNSCapture.swift`

1. **Blocking MainActor Calls**: `AppModel.swift:446-460` (`flushDNSCache()`) and `NetworkKeyState.swift:101-121` call `process.waitUntilExit()` synchronously on `@MainActor`, freezing the UI thread for 2+ seconds.
2. **TaskGroup Cancellation Leaks**: `ProbeRunner.swift:44-60` and `AppModel.swift:493-505` execute task groups with nominal timeouts, call `group.cancelAll()`, but exit without draining remaining tasks (`for try await _ in group {}`), leaving background network tasks executing past deadlines.
3. **Unbounded `lsof` Fork Storm**: `DNSCapture.swift:586-607` synchronously spawns `/usr/sbin/lsof` per captured packet missing process metadata, exhausting thread pools and CPU under active network traffic.

**Code Fix Specification**:
- Wrap all `Process` executions in `withCheckedThrowingContinuation` using `process.terminationHandler`.
- Drain task groups completely after cancellation.
- Gate `lsof` calls behind an actor-isolated cache with a concurrency semaphore cap.

---

### PR-21 [B192]: `wardenState()` returns "PASSED" for any non-nil result object (presence-only check)

**Severity:** Critical  
**Classification:** Code defect (Honesty Protocol §1 & §4)  
**Affected:** macOS GUI (`DashboardView.swift:375-385`, `:42`, `:61`, `:65`, `AppModel.swift:680-683`, `:741`)

- **Issue**: `wardenState(for:)` evaluates only presence (`model.prophetResult != nil ? "PASSED" : "NOT RUN"`). Inconclusive, degraded, or threat-detected result objects return `"PASSED"`. First-render UI contains unconditional reassurance literals ("The Vigil Holds", "The Ways are Warded") before scans run, and `StatusPill` color is hardcoded to `.ioSuccessGreen`.
- **Fix**: `wardenState()` must inspect result `.verdict` (`.clean` $\rightarrow$ "CLEAN", `.tampered` $\rightarrow$ "THREAT DETECTED", `.inconclusive` $\rightarrow$ "INCONCLUSIVE"). Remove unconditional reassurance text on first launch; derive status pill color dynamically from verified transport state.

---

### PR-22 [B193]: `runAllWardens()` omits Cage, discards output via `_ = await`, and hardcodes "All scans complete."

**Severity:** Critical  
**Classification:** Code & UX defect (Honesty Protocol §4)  
**Affected:** macOS GUI (`AppModel.swift:405-444`)

- **Issue**: Primary action `runAllWardens()` omits Cage (6th warden), discards returned string evidence via `_ = await runXxx()`, unconditionally sets `output = "All scans complete."` regardless of error status, and rapidly mutates `selection` sidebar state 6 times creating a disorienting "fair ride" tab-flipping effect.
- **Fix**: Execute all 6 wardens, store returned findings into state, remain on Dashboard with in-place progress captioning, and navigate to Conclusions view only upon completion.

---

### PR-23 [B194]: Windows synthesis outputs `Diagnosis = Clean, Conclusivity = Untested` (CLEAN 0%)

**Severity:** Critical  
**Classification:** Code defect (Honesty Protocol §1 & §5)  
**Affected:** Windows CLI & Kit (`SynthesisEngine.cs:383-391`, `:425`, `Program.cs:293-296`, `Report.cs:109-120`)

- **Issue**: When all sections are untested, `SynthesisEngine` sets `diagnosis = Hypothesis.Clean` and `conclusivity = Conclusivity.Untested`. `EvidenceConfidence` returns null and `?? 0` floors display confidence to 0%. The CLI guards only `Conclusivity.Inconclusive`, allowing `Untested` to fall through and print `"VERDICT: CLEAN / Confidence: 0%"`. Zero evidence is not clean evidence.
- **Fix**: `allUntested` must set `Conclusivity.Untested` and block downstream verdict rendering. The CLI and `Report.RenderText()` must intercept `Conclusivity.Untested` and print `"NO DATA — scans were not run"`.

---

### PR-24 [A136]: Authoritative DNS listener `_serve_dns` spins on persistent `OSError`

**Severity:** Critical  
**Classification:** Server availability defect  
**Affected:** Server (`dns_authoritative.py:191-196`)

- **Issue**: The `_serve_dns` receive loop immediately continues after every `recvfrom` `OSError` without sleep, back-off, or retry limit. A persistent socket error causes a tight loop burning 100% CPU on a core while reporting state `"listening"`.
- **Fix**: Add exponential back-off (min 0.1s, max 30s) on consecutive `OSError`s and transition state to `"down"` after N consecutive failures.

---

### PR-25 [A137]: Server readiness handler returns HTTP 200 when all listeners are in `"unknown"` state

**Severity:** Critical  
**Classification:** Server readiness defect  
**Affected:** Server (`listeners.py:28-31`, `app.py:764-782`)

- **Issue**: Known listener states are seeded to `"unknown"` at import. `down_listeners()` filters only `state == "down"`. The readiness endpoint returns HTTP 200 `{ ready: true }` when no listener is explicitly `"down"`, advertising full readiness on uninitialized cold-start servers.
- **Fix**: Fail-closed readiness: include `"unknown"` in `down_listeners()` trigger for HTTP 503, and auto-transition unstarted listeners to `"down"` after startup timeout.

---

### PR-26 [D113]: `bundle-app.sh` masks CLI build and copy failures with `|| true`

**Severity:** Critical  
**Classification:** Build & distribution defect  
**Affected:** macOS packaging (`bundle-app.sh:21-22`, `SyxxCage.swift:39`)

- **Issue**: `bundle-app.sh` appends `|| true` to `swift build --product syxx` and `cp`, masking CLI compilation errors (caused by `SyxxCage.swift:39` calling actor-isolated `flush()` from `deinit`). The script reports success and packages an older stale 1.0.0 CLI (`f8dfb393...`) inside `Syxx.app/Contents/Resources/syxx` while GUI reports version 2.0.0.
- **Fix**: Remove `|| true` so build fails loudly on CLI errors. Fix `SyxxCage.swift:39` `deinit` call using a nonisolated synchronous flush.

---

### PR-27 [D114]: Windows telemetry opt-out (`TelemetryEnabled`) stored in settings but never read

**Severity:** Critical  
**Classification:** Privacy defect  
**Affected:** Windows GUI (`App.xaml.cs:14-33`, `AppViewModel.cs:196-199`, `SettingsView.xaml.cs:67-70`)

- **Issue**: `TelemetryEnabled` is written in `SettingsView` but never read in any send path. `App.xaml.cs` constructs OpenTelemetry `TracerProvider` unconditionally in `App()`, and `DimensionService.FetchStartupAsync` runs unconditionally on startup.
- **Fix**: Check `SyxxSettings.TelemetryEnabled` before constructing the OTel exporter and before invoking `FetchStartupAsync`. Default to opt-in disabled (`false`).

---

### PR-28 [B195]: `witnessVerify()` guards on private key presence instead of embedded public key

**Severity:** Critical  
**Classification:** Code defect (Honesty Protocol §1)  
**Affected:** macOS GUI (`AppModel.swift:1499-1504`)

- **Issue**: `witnessVerify()` guards on `WitnessKeyManager.hasKey` (private key presence). Verification requires only the Ed25519 *public* key embedded in `WitnessSnapshot`. Requiring local private key breaks third-party verification and rotated key verification.
- **Fix**: Remove `WitnessKeyManager.hasKey` guard from `witnessVerify()`. Verify snapshots using `snap.publicKey`.

---

### PR-29 [C068 & C069]: `ProphetResult` hardcodes 1 resolver and `ProphetView` fabricates "every resolver agrees"

**Severity:** Critical  
**Classification:** Code defect (Honesty Protocol §4 & §5)  
**Affected:** macOS GUI (`AppModel.swift:680-683`, `ProphetView.swift:116`, `:121`, `:148`)

- **Issue**: `runLookup()` constructs `ProphetResult` hardcoding `resolversQueried: 1` and a single display server `[srv]`, ignoring `VantageComparer.publicResolvers` (Cloudflare, Google, Quad9). `ProphetView` unconditionally renders *"No tampering — every resolver agrees"* and *"1 / 1 agree"*. A sample of one cannot agree with anything; this is fabricated consensus output.
- **Fix**: Query multi-resolver oracles in `runLookup()` (local + Cloudflare + Google + Quad9), populate `resolverResponses` with real multi-vantage outcomes, and render agreement headers only when $\ge 2$ resolvers are queried and agree.

---

### PR-12: additional GUI hang and unbounded-growth paths remain

**Severity:** High  
**Classification:** Code defect or host-dependent source hypothesis  
**Affected:** macOS, Windows, Linux

- Windows Portal reads stdout and stderr to completion before applying its
  50-second exit timeout. A child that blocks or fills a pipe can make the
  timeout unreachable:
  `Windows/SyxxUI/Views/PortalView.xaml.cs:123-154`.
- Windows Cage repeatedly rebuilds a growing collection on a dispatcher timer
  and uses an unvirtualized list:
  `Windows/SyxxUI/Views/CageView.xaml.cs:162-300`.
- macOS Cage retains all rows and raw records and repeatedly reverses/copies the
  full array:
  `macOS/Sources/SyxxApp/AppModel.swift:1065-1080` and `:1194-1202`.
- macOS plain capture can launch an unbounded, timeout-free `lsof` per DNS
  packet:
  `macOS/Sources/SyxxKit/DNSCapture.swift:586-607`.
- Linux constructs Dashboard eagerly, and its constructor performs synchronous
  resolver discovery. On a slow `resolvectl`, first paint can block for the
  process timeout:
  `Ubuntu/SyxxUI.Avalonia/ViewModels/DashboardViewModel.cs:22-30` and
  `Windows/Syxx.Kit/NetworkKeyState.cs:184-191`, `:283-305`.

The macOS timeout failure was reproduced structurally. The Windows and Linux UI
occurrences require target-host traces, but their blocking mechanisms are
present in source.

### PR-13: server listener availability and readiness are not trustworthy

**Severity:** Critical  
**Classification:** Code defect  
**Affected:** production server

#### DoT accept-loop denial of service

`Server/syxx_server/dot_listener.py:37-46` wraps the listening socket with TLS.
The sole accept loop at `:146-151` can therefore perform a blocking TLS
handshake before the connection semaphore. One idle raw TCP client can prevent
the listener from accepting subsequent clients.

#### Authoritative DNS fatal-error spin

`Server/syxx_server/dns_authoritative.py:189-204` immediately continues after
every `recvfrom` `OSError`. A permanently invalid socket can produce a tight
CPU loop while listener state remains `listening`.

#### Unknown listeners reported ready

Known listener states default to `unknown`:

- `Server/syxx_server/listeners.py:25-31`

Only explicit `down` states are returned by `down_listeners()`. The readiness
handler returns HTTP 200 when none are explicitly down:

- `Server/syxx_server/app.py:764-782`

An all-unknown or never-started listener set can therefore report
`ready: true`. That is the opposite of a fail-closed readiness contract.

#### Deployment masks required failures

- Remote dependency installation masks a cryptography failure:
  `scripts/deploy.sh:137-155`.
- Initial backup failure is masked before the script reports that the backup
  was taken:
  `scripts/deploy.sh:508-509`.
- Certificate ACL setup creates a group but does not add the service account to
  it:
  `scripts/deploy.sh:189-204`, `:311`, and `:452-468`.

The full server unit suite passed, but it does not negate these source-confirmed
availability and deployment faults.

### PR-14: commercial and distribution paths are not operational

**Severity:** Critical  
**Classification:** Delivery and commerce blocker  
**Affected:** all platforms and server

The public page currently contains dead purchase and download actions:

- `Server/index.html:375`: Start Pro Trial only scrolls to downloads.
- `Server/index.html:384`, `:388`, `:395`, and `:396`: store and direct
  installer links use `href="#"`.

The server has a checkout route, but the public page does not call it.

Stock commerce preflight reported these missing blockers:

- `SYXX_KEYGEN_PUBLIC_KEY`
- `SYXX_KEYGEN_ADMIN_TOKEN`
- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- `SYXX_SMTP_HOST`
- `SYXX_COLLECTOR_PRIVATE_KEY`

It also reported missing monthly and yearly Keygen policy IDs for macOS,
Windows, and Debian. This proves the stock workspace configuration cannot
fulfill sales. It does not establish the state of an independently configured
production host.

Distribution is also incomplete:

- macOS has an app wrapper, but no valid signed/notarized deliverable or DMG.
- Windows is unpackaged and has no verified installer or Authenticode flow.
- Linux can produce raw self-contained folders, but there is no `.deb`, snap,
  desktop entry, PolicyKit helper, or required Tier-2 ship gate.

The product cannot presently complete the sequence from purchase, through
entitlement, to platform-native installation.

### PR-15: macOS bundle construction can silently ship stale, invalid content

**Severity:** Critical  
**Classification:** Build and delivery defect  
**Affected:** macOS

The app wrapper builds the GUI normally, but masks both failure to build the CLI
and failure to copy it:

- `macOS/bundle-app.sh:21-22`

The current CLI does not compile because
`macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:37-40` calls an actor-isolated
method synchronously from `deinit`. The bundle script still printed success and
copied the previous release CLI.

Evidence from the resulting release bundle:

- `.build/release/syxx` modification time: 2026-07-23 14:22:30
- bundled `Syxx.app/Contents/Resources/syxx` created: 2026-07-24 02:31
- both SHA-256:
  `f8dfb3930c56704827786a5466452ce0139999a040b572281592eedd9ec59aa2`
- embedded CLI reports `Syxx 1.0.0`
- app metadata reports `2.0.0`

The wrapper is therefore capable of calling a bundle successful while shipping
an older capture backend.

The resulting app also fails strict signature verification:

```text
macOS/Syxx.app: code has no resources but signature indicates they must be present
```

The executable is ad hoc/linker-signed, has no team identifier, does not bind
the Info.plist, and seals no resources.

### PR-16: commerce and status copy can claim unsupported state

**Severity:** High  
**Classification:** Code defect and specification ambiguity  
**Affected:** website, Linux, Windows

`Server/index.html:311` initially presents the passive DNS index as live.
`Server/index.html:743-749` tries to replace the status with fetched data, but
on failure silently leaves the original live claim visible.

Prices conflict:

- public website: `$2/month`;
- Linux License view: `$4.99/month` or `$29.99/year`;
- Linux Dashboard and shared CLI paths: `$2/month`.

The canonical commercial price is owner policy and cannot be inferred in this
audit. Publication requires one approved price across every surface.

Windows describes its telemetry choice as a once-per-launch platform and
version ping. The toggle is persisted at
`Windows/SyxxUI/Views/SettingsView.xaml.cs:67-70`, but the startup Dimension
request does not consult it:

- `Windows/SyxxUI/MainWindow.xaml.cs:52-63`
- `Windows/SyxxUI/ViewModels/AppViewModel.cs:195-215`
- `Windows/SyxxUI/Services/DimensionService.cs:107-126`
- `Windows/SyxxUI/App.xaml.cs:18-28`

The OpenTelemetry exporter is also configured without consulting the toggle.
The exact startup request data and whether any trace spans are emitted were not
measured on a Windows host, but the opt-out gate is absent from both setup
paths.

## 4. Platform health summary

### macOS

**Build status:** GUI debug and release products compile. Full package tests do
not compile. The bundle script reports success despite a failed CLI build and
creates an invalid, ad hoc bundle containing a stale CLI.

**Most important product risks:**

- first-render false reassurance;
- hostile and inconclusive result objects shown as passed;
- run-all omits Cage and suppresses failures;
- timeout and main-thread blocking paths;
- Unbound cannot complete its normal GUI privilege path;
- Cage child/process and privileged-path safety defects;
- local resolver substitution;
- encrypted-DNS state inversion;
- arbitrary collector URLs can receive the stored license key over HTTP:
  `macOS/Sources/SyxxApp/AppModel.swift:754-775` and
  `macOS/Sources/SyxxKit/LeakDetector.swift:342-365`;
- ContentWarp SSRF validation fails open and has a DNS-rebinding window:
  `macOS/Sources/SyxxKit/ContentWarp.swift:87`, `:118`, `:140`, and
  `:312-347`;
- RemoteContentView's documented resource sandbox is not implemented:
  `macOS/Sources/SyxxApp/RemoteContentView.swift:55-65` and `:103-113`.

**Positive evidence:**

- the current GUI target itself compiles in debug and release;
- a real captive-portal lookup failure remained unknown rather than clean;
- current Cage capture code requests the expected packet tap link type and uses
  captured length;
- zero-record Cage synthesis remains untested rather than clean;
- some ContentWarp baseline failures now remain inconclusive;
- the matrix engine does not invent affirmative clean-support indicators.

### Windows

**Build status:** Core tests execute, but six fail. A Windows-host WinUI build
was not available. Current GUI source references removed core members and
cannot be treated as buildable.

**Most important product risks:**

- empty evidence can print CLEAN;
- GUI and core API drift;
- known unfinished matrix cutover;
- silent public-resolver substitution;
- encrypted-DNS intent/state mismatch;
- Unbound state lost across invocations;
- Cage privileged children and quadratic UI growth;
- Portal timeout after blocking reads;
- Witness fabricates a default domain/resolver context on empty input:
  `Windows/SyxxUI/Views/WitnessView.xaml.cs:102-110`;
- telemetry opt-out is not applied to startup telemetry.

**Positive evidence:**

- inspected WinUI controls generally have handlers or commands; the dominant
  problem is incorrect behavior behind them, not disconnected XAML;
- inspected DPAPI and native-allocation cleanup did not show a double-free;
- P/Invoke allocations in the inspected paths have explicit cleanup.

### Linux

**Build status:** Avalonia debug and release builds pass. Self-contained
`linux-x64` and `linux-arm64` raw publishes pass. Seven Witness-only tests pass.
There was no real Ubuntu desktop run.

**Most important product risks:**

- public-resolver substitution;
- Mirror Lite reaches Windows DPAPI;
- known unfinished matrix cutover and unsupported confidence percentage;
- Glutton ignores measured encrypted-DNS state and can report Do53 based only
  on absence of a VPN:
  `Ubuntu/SyxxUI.Avalonia/ViewModels/GluttonViewModel.cs:34-48`;
- encrypted-DNS enable, disable, and parsing defects;
- Portal GUI advertises route tracing but has no route action:
  `Ubuntu/SyxxUI.Avalonia/Views/PortalView.axaml:8-13` and
  `PortalViewModel.cs:22-57`;
- shared traceroute launches Windows `tracert` on Linux:
  `Windows/Syxx.Kit/TracerouteRunner.cs:26-65`;
- primary Dashboard and trial controls do nothing;
- network-state failures collapse to confident negatives:
  `Windows/Syxx.Kit/NetworkKeyState.cs:330-421` and `:635-644`;
- no installable package or privilege integration.

**Positive evidence:**

- the current Avalonia project compiles in both configurations;
- raw x64 and arm64 publishes contain the expected native crypto library;
- Witness tests cover signing, verification, and mutation behavior;
- Cage explicitly identifies its unwired elevation instead of fabricating a
  capture;
- Linux license activation is visibly disabled instead of claiming that
  unsupported persistence works;
- severity colors reserve green for explicit clean.

### Server and website

**Build/test status:** 225 of 225 server tests passed in 39.783 seconds when
loopback access was allowed. The run emitted unclosed-socket resource warnings.

**Most important product risks:**

- DoT accept-loop handshake denial of service;
- authoritative DNS fatal-error busy loop;
- unknown listener states report ready;
- deployment masks cryptography and backup failures;
- service account certificate-group access is incomplete;
- purchase and download controls are dead;
- stock commerce preflight has blocking secrets and policy IDs missing;
- passive DNS status defaults to live before evidence.

**Positive evidence:**

- the full server suite passes in an environment with loopback access;
- the previously alleged SQL interpolation problem in
  `Server/syxx_server/db.py:498-509` is not externally injectable in its current
  call graph because callers supply only three fixed internal table constants.

## 5. Verification record

| Area | Command or check | Result |
|---|---|---|
| Server tests | `python3 -m unittest discover -s tests -v` from `Server` | 225 passed, 0 failed, 39.783 s; unclosed-socket warnings |
| Server commerce | stock `preflight.commerce_findings()` | Six blocking secrets/config values and all platform policy IDs missing |
| macOS full tests | `swift test` from `macOS` | Compile failure in production `SyxxCage.swift`; additional tests reference deleted pre-migration APIs |
| macOS GUI debug | `swift build -c debug --product syxx-gui` | Passed |
| macOS GUI release | `swift build -c release --product syxx-gui` | Passed with an OpenTelemetry deprecation warning |
| macOS wrapper debug/release | `bash bundle-app.sh debug` and `release` | Printed success, but masked CLI build failure and copied stale CLI |
| macOS signature | `codesign --verify --deep --strict --verbose=4 macOS/Syxx.app` | Failed |
| macOS real path | `.build/debug/syxx key` | Network failure remained unknown; elapsed 16.07 s |
| Windows core tests | `dotnet test Windows/Syxx.sln --no-restore --verbosity minimal -m:1` | 584 passed, 6 failed |
| Windows GUI cross-build | WinUI build on macOS | Could not execute Windows XAML compiler; source API mismatch independently confirmed |
| Linux GUI debug/release | `dotnet build ...SyxxUI.Avalonia.csproj` | Both passed |
| Linux tests | `dotnet test Ubuntu/SyxxUI.Avalonia.Tests/...` | 7 passed, all Witness-focused |
| Linux raw publish | self-contained Release for `linux-x64` and `linux-arm64` | Both passed |
| Linux API portability | rebuild shared Kit with `CA1416` as errors | Failed with 10 Windows-only reachability errors |
| Linux target runtime | real Ubuntu GUI run | Not performed; no Linux desktop/daemon host was available |
| Cross-platform transcript parity | CI and source inspection | FAIL: placeholder gate performs no comparison |

Passing unit counts must not be read as publication readiness. macOS cannot run
its full suite, Windows has live failures, Linux tests only one feature area,
and no determinative transcript has been proven identical on three real
platforms.

## 6. Current parity report

| Parity requirement | Result | Reason |
|---|---:|---|
| Same production determinative algorithm | **FAIL, KNOWN MIGRATION** | macOS matrix vs. Windows/Linux residual former engine |
| Same conclusion data contract | **FAIL** | WinUI references removed members; Linux still displays a pre-migration confidence value |
| Same missing-resolver behavior | **FAIL, uniformly unsafe** | All substitute public resolvers, with different defaults and disclosures |
| Same encrypted-DNS semantics | **FAIL** | Transport selection, enable, disable, and verification differ and are incorrect |
| Same capture capability | **FAIL** | macOS and Windows have unsafe elevation lifecycles; Linux GUI states elevation is absent |
| Same route capability | **FAIL** | Linux GUI has no trace action and shared wrapper invokes `tracert` |
| Same license/security storage | **FAIL** | Linux production path reaches Windows DPAPI; activation is unavailable |
| Same signed install experience | **FAIL** | None of the three has a verified complete native distribution path |
| Automated transcript comparison | **FAIL** | CI step is non-enforcing |
| Real three-platform golden run | **INCONCLUSIVE** | No Windows and Linux target-host production run was available |

## 7. What must be true before selling the macOS product

The following are release criteria, not implementation sequencing or an
amendment to the project's existing plans:

1. Every first-render, scan, verdict, confidence, color, and status surface must
   be traced to validated evidence. Missing or invalid evidence must remain
   unknown or inconclusive.
2. Local resolver discovery failure must never be replaced by public-resolver
   evidence. Public controls must be separately labeled.
3. The current macOS GUI, CLI, tests, and app wrapper must build from one clean
   source state. The wrapper must fail when a required product fails to build.
4. Every visible primary macOS action must have an interaction test with
   explicit failure cases.
5. macOS Unbound, encrypted DNS, Cage, and other elevated flows must have bounded,
   recoverable lifecycles with verified restoration and no wrapper/child leaks.
6. Advertised timeouts must return at the deadline even when child work ignores
   cooperative cancellation.
7. Server listeners must fail closed in readiness, resist idle-client
   starvation and fatal-socket spin, and deploy without masked required steps.
8. Commerce must complete from the live purchase control through payment,
   entitlement, activation, renewal/cancellation, and support handling using
   nonproduction test accounts.
9. macOS must have a valid signed and notarized installer that is installed and
   removed on clean machines. Obtaining the Developer ID is an external owner
   dependency; producing a valid bundle before signing is a repository task.
10. macOS privacy choices must be enforced by production send paths and
    verified with captured traffic.
11. Approved prices, output copy, legal terms, privacy disclosures, and support
    contacts must be consistent across website, GUI, CLI, checkout, and
    entitlement emails.
12. A macOS release candidate must complete real production-path runs on clean
    supported macOS machines and a production-like server. Unit tests alone do
    not satisfy this gate.

Before selling or advertising the Windows or Linux ports, each port separately
also requires:

1. The matrix engine and current conclusion contract in its production entry
   points.
2. Exact golden-transcript parity with macOS.
3. Target-host GUI, privilege, network, licensing, and failure-path runs.
4. A signed or platform-appropriate installable package and clean uninstall.

## 8. Publication assessment

Syxx is **not close enough to publication to accept payment or distribute a
macOS security-verdict product**. This conclusion is based on macOS and server
defects, not on the unfinished Windows and Linux ports. The largest macOS
launch gaps are foundational:

- evidence and verdict integrity;
- privilege and process lifecycle correctness;
- working primary GUI actions;
- a reproducible, valid, signed macOS distribution;
- end-to-end commerce.

The appropriate next milestone is not public beta or paid launch. It is a
macOS release candidate that can prove one honest end-to-end flow per
discipline with failure-path evidence. Windows and Linux can continue as
separate port workstreams and need not hold that milestone. Until the macOS
criteria above are satisfied, marketing the macOS product as a reliable DNS
tampering and privacy assessment tool would overstate what the current build
can establish.

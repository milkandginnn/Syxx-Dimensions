# Syxx CHANGELOG Recovery and Completion Plan

**Date:** 2026-08-01

**Purpose:** Bring the source into truthful alignment with the supplied CHANGELOG excerpt. Preserve work that is already implemented, complete work that is partial or absent, correct stale documentation, and verify every claimed capability through native production paths.

**Plan shape:** Five dependency-ordered phases. A phase may begin only after the preceding phase exit gate is satisfied. Workstreams inside a phase may run in parallel only where their file ownership and inputs do not overlap.

**Scope split:** Phase 1 plus the Stage 5 and Mirror surface repairs recover the work that was actually lost. The remaining work is a CHANGELOG-completion and release-hardening program that adds authenticated update publication, signed DoH coverage, blocking behavioral transcripts, full GUI parity, and native package proof. Those are intentional additional engineering workstreams, not descriptions of recovered evening code.

## Non-negotiable constraints

1. The GUI first-run scan is permanently removed. Do not restore its card, automatic runner, `AppModel` state, or deleted GUI test. The deliberate CLI guided flow remains.
2. macOS is the behavioral and aesthetic gold standard. Windows and Linux are brought to macOS parity, never the reverse.
3. In code, use `result`. In user-facing text, use `conclusion` or a specific factual label. The exception is when code specifically refers to the Conclusion pane, e.g., ConclusionView.cs is fine.
4. Operations that can fail, time out, or return ambiguous information preserve success, failure, and unknown, or secure, tampered, and inconclusive as appropriate.
5. No conclusion, confidence, probability, reassuring color, or success message appears before every required datum is queried, parsed, and validated.
6. Every new or changed user-visible string, warning, error, label, report line, or log line requires owner approval of its exact wording and format before coding.
7. Every test has a happy path and at least one failure or mutation path. Tests are not softened, skipped, delayed, or retried to obtain green output.
8. A workstream is not complete until a production entry point produces captured real output from real input.
9. Any change in determinative Swift code receives a matching C# assessment and, where applicable, a matching implementation and golden vector.
10. Network failure and missing dependencies are reported immediately.
11. Release artifacts are built only after the source and package definitions pass Phases 1 through 4.
12. All implementation, commits, generated artifacts, and release evidence remain local until the owner explicitly authorizes a push. No agent pushes by inference from this plan.
13. File line numbers are navigation aids, not immutable anchors. Before editing, re-anchor every cited range by its named type, method, view, test, or CI job and record the current line.
14. Jost is the primary UI and body family. Body copy is restricted to weights 300 and 400. Cormorant SC is the explicit alternate body family, not an automatic missing-font fallback. Archivo is retired from the primary type system.
15. Every client gives the remote MOTD at most five seconds to load. If it has not loaded, the client renders its packaged `fallback-motd.html` inside the same WebView: `Resources/` on macOS and `Assets/` on Windows and Linux. On macOS, `motd-local.html` is not a fallback and must never be selected by a timeout, network-failure, WebView-failure, or retry path.
16. DO NOT remove the hero images and title text from the .html files. The user is tired of replacing them, and someone keeps removing them.
17. Use subagents with specialized experience, including researchers and subject matter experts to advise the engineers and executors/implementers. Use error detectives, debuggers, performance management and telemetry specialists, and testing specialsits.

## Source-audit baseline

### Peer-eval correction (2026-08-01, live-tree)

Before any workstream: **read the live symbols**. If implementation and mutation-grade tests already exist, treat the workstream as **verify-only**. Do not reimplement green code. Fail-first steps that pass are a freeze signal, not a stall.

Plan corrections from peer audit (accepted unless noted):

1. **1.1 ConclusionsView:** the defect was a **tail cut** after the scan-card removal (struct close, `resultAccessibilityLabel` four-state body, `#Preview`), not a missing accessibility contract to invent. Braces can balance while the parser still fails mid-struct. Correct repair: donor from `ed9a33b~1`, re-apply scan removal **narrowly** (card usage, card block, `runFirstScan` only). Do not invent new accessibility wording. File length is ~412 lines when complete; "1-386" was a stale citation.
2. **1.2 server files:** `Server/syxx_server/diff.py` and `doh.py` **do not exist**. Diff/DoH live in `app.py`, `warp.py`, and `resolver.py`. Prefer evening extract from origin commit `3acf61c` as the verified accelerator for fulfillment idempotency. The sixteen-behavior list remains the true server gap bound (not wholesale file replacement).
3. **2.1 B252, 2.2 ContentWarp/URL policy, 2.3 C141:** moved to **verify-only freeze** below. Remaining work is mutation + real-run verification, not rewrite.
4. **Mirror "false-tampered" at MirrorView ~335:** **not confirmed** on live tree without a failing case. Current labels order inconclusive → redirection → degraded → agree. Do not promote without repro. Dashboard **false-pass** (PASSED on redirection) is a separate, confirmed defect and stays in Phase 1.3.

### Implemented and verify-only

Do not rewrite these items unless a Phase 5 verification exposes a specific defect:

- Matrix C061 through C065 on macOS: debug-only fixtures, completeness propagation, evidence filtering, required-section suppression, raw-support preservation, below-tier suppression, and clean-result blocking.
- The Windows matrix-engine twin and its production selection.
- macOS Cage PKTAP opening, DLT 149 validation, explicit datalink parsing, PKTAP-first process attribution, and disclosed fallback.
- Windows UDP-table process attribution.
- Witness B251 and E162 wire conversion and cross-platform payload compatibility.
- Witness B254 on Windows: report generation now verifies before rendering.
- **B252 (was Phase 2.1):** format v4 canonical `dohOutcomes` signed and display-guarded on both platforms; legacy pre-v4 remains readable without authenticated DoH. Verify: golden vectors + DoH-field mutation refusal on both platforms.
- **Windows ContentWarp + RemoteUrlPolicy (was Phase 2.2):** SSRF refusal returns typed inconclusive (no `ArgumentException`); navigation entry points use `AuthorizeForNavigation`. Verify: mutation bypass turns navigation tests red; policy-unavailable case covered.
- **macOS C141 (was Phase 2.3):** unrecognized ARP residual tokens stay `.unknown`; reachable only for explicit tokens or documented BSD no-token default. Verify: `testUnrecognizedARPStateTokenRemainsUnknown` and mutation of unconditional reachable fallback.
- D118 process-wide macOS authorization-session reuse.
- Canonical EULA hashing, acceptance persistence, exact readback, shell gating, and choice-gated startup services.
- Existing Cinzel, Cormorant SC, EB Garamond Italic, and JetBrains Mono assets and role mappings, subject to the Phase 3 type-system migration and asset audit.
- LAN C139, C140, C151, and C156.
- The narrow C048 correction that separates VPN state from encrypted-DNS state.
- The named server database-permission, monitor-cleanup, portable-purge, Keygen, CORS, frame-protection, DNSSEC, RCODE, and unattested-observation changes.
- Debian architecture validation and root ownership flags, .NET visibility grants, DeepSyxx helper files, the base CI matrix, server CI, lint/security jobs, OpenAPI, the agent catalog, and source-vocabulary parity.

### Partial, absent, stale, or explicitly open

The five phases address these findings:

- **BLOCKER: the macOS app deadlocks on launch.** `NetworkKeyState.runProcess` waits on a semaphore that only the timeout work item signals, and the fast path cancels that work item first, so the main actor blocks forever during `checkStartup()`. Observed symptoms: perpetual spinner, guided setup never presents on a fresh install, and the Dock icon no longer raises the window. Addressed by workstream 1.0, which precedes every other workstream in this plan.
- ~~ConclusionsView structurally incomplete~~ — repaired (verify: Debug+Release build; first-run scan still absent).
- ~~Sixteen server acceptance failures~~ — recovered (verify: full Server suite green).
- ~~B252 / ContentWarp throw / C141 unknown ARP~~ — implemented; see verify-only freeze above.
- G-1 update components need operational authenticated end-to-end proof on real hosts (foundations landed; production key rotation owner-gated).
- The guided-setup lifecycle exists; visual restoration vs mockups and encryption-before-ready remain under Phase 3.3 verification.
- MOTD five-second `fallback-motd.html` path landed; Linux embedded-WebView parity still incomplete where Avalonia WebView is unpinned.
- Jost-led type system migration landed on three clients; residual Archivo/raw call-site audits continue under Phase 3.7 guards.
- `PurpleScrollView.swift` app-wide replacement still open (Phase 3.8).
- Dashboard ticker and conclusion-derived surfaces may still diverge from CHANGELOG description.
- ~~Dashboard Mirror PASSED on redirection~~ — fixed via `DashboardWardenState` (verify mutation).
- B253 process codes: Witness complete; remaining Windows non-Mirror/Warp commands and owner-approved help copy still open.
- B255 and B256 remain host-network or host-timing dependent.
- Stage5Presentation + Stage5DisplayModel landed; wire into live Conclusions surfaces and restore any remaining Kit parity tests still under `_quarantine/`.
- CI source parity is active, but behavioral transcript parity is not blocking.
- Documentation contains stale Linux paths, stale CI descriptions, a non-Mermaid diagram fence, and an obsolete first-run scan instruction.
- Debian dependency selection and Windows packaging projects remain incomplete.
- The observed `bugsmasher.syxx.app` certificate-hostname mismatch is not yet represented in this plan's records workflow and must be verified and filed rather than hidden by disabling telemetry during tests.

## Resource and ownership map

| Workstream | Primary agent lane | Required skills or references | Exclusive file ownership |
|---|---|---|---|
| Launch deadlock (1.0, blocker) | error-detective and neural-debugger, then swift-expert and swiftui-architect as advisors, research-analyst for synthesis, syxx-engineer and neural-gui-architect to land, gui-cross-platform-continuity for parity, performance-engineer for launch telemetry | honesty audit, parity check, reference library | `NetworkKeyState.swift` (Kit) and `AppModel.swift`/`ContentView.swift` (App), disjoint per agent |
| macOS build, guided setup, typography, and scrollbars | macOS GUI engineer | GUI architecture, honesty audit, supplied type-system specimen | `macOS/Sources/SyxxApp/` |
| Server recovery | server engineer | server hardening, DNS references | `Server/syxx_server/`, `Server/tests/` |
| Witness and matrix parity | neural executor | evidence cryptography, parity check | `SignedEvidencePayload*`, Witness vectors |
| Windows security | Windows engineer | honesty audit, URL-policy contract | `ContentWarp.cs`, Windows navigation views |
| Updater | platform engineers plus server engineer | evidence cryptography, CI and packaging | appcast, update services, update views |
| MOTD WebView fallback | platform GUI engineers | WebKit/WebView2/Avalonia WebView security, URL-policy contract, monotonic timing | remote-content/What's New views, fallback asset packaging, focused tests |
| LAN C141 | macOS core engineer | LAN parser contract | `ARPTableParser.swift` and tests |
| Dashboard and Stage 5 | GUI surfacer | GUI architecture, Stage 5 contract | Dashboard and Presentation files |
| Cross-platform typography | GUI engineers plus release engineer | supplied `Syxx Type System.html`, platform font-resource conventions, accessibility tooling | design-system files, font assets, font licenses, and direct font call sites |
| CI and transcripts | CI engineer | `syxx-ci-pipeline`, parity check | CI workflow, parity scripts, emitters |
| Documentation and packaging | release engineer | CI pipeline, platform packaging | docs and build projects |

Agents are not alone in the codebase. Each agent must inspect current changes before editing, preserve other agents' work, and avoid reverting unrelated files.

---

# Phase 1: Restore a Buildable and Testable Foundation

## Objective

Restore a buildable macOS gold standard, recover the sixteen missing server behaviors using the surviving evening code as a verified accelerator, restore the deployment certificate work, and remove the Dashboard Mirror false-pass path. Freeze already-implemented CHANGELOG capabilities so later phases do not accidentally replace them.

## Why this phase comes first

GUI, parity, update, and packaging work cannot be trusted while the gold-standard app does not compile and the server has known red evidence and fulfillment tests. This phase changes no GUI design and adds no new product capability.

Workstream 1.0 precedes all of it. The app currently deadlocks on launch, so every rendered check, guided-setup gate, Dashboard capture, Stage 5 surface, scrollbar verification, and typography reflow in Phase 3 is unreachable until it lands. Treat 1.0 as the gate on the entire plan, not merely on Phase 1.

## 1.0 Unblock launch: remove the startup semaphore deadlock (BLOCKER, precedes every other workstream)

**Status:** stage 3 landed 2026-08-01. Both implementers green: `runProcess` rewritten in
`NetworkKeyState.swift` (RunProcessState, bounded wait, SIGTERM→0.5s→SIGKILL→reap, concurrent
drain) and startup moved off the main actor in `AppModel.swift` (Task.detached probe, single
writer, `.checking`/`.unavailable` states, runLookup gate, owner-approved copy). Kit tests 6/6,
App tests 8/8, Debug + Release builds green. Remaining: stage 3 parity report
(gui-cross-platform-continuity), post-fix `sample` + cold-launch timings, fresh-HOME guided
setup real run, stage 4 (performance-engineer, knowledge-synthesizer).

**Observed symptoms (owner-reported, all three from one cause)**

1. The app spins indefinitely on open.
2. Guided setup never presents on a fresh install.
3. The Dock icon no longer raises the window.

**Root cause (measured, not inferred)**

`sample` on a live launch parked 3379 of 4144 main-thread samples in `semaphore_wait_trap`, under this exact chain:

`ContentView.swift:98` (`.onAppear`) → `AppModel.checkStartup()` (`AppModel.swift:826`) → `NetworkKeyState.getSystemDNSServers()` (`NetworkKeyState.swift:169`) → `getScopedResolvers()` (`:187`) → `runProcess(_:_:timeout:)` (`:158`).

In `runProcess`, `done.signal()` is reachable **only** from inside `timerWork` (`NetworkKeyState.swift:136` and `:147`). The fast path runs `timerWork.cancel()` at `:157` and then `done.wait()` at `:158` unconditionally. When the subprocess finishes before the deadline, which is the normal case for `scutil --dns`, the cancelled work item never executes, nothing signals the semaphore, and the main actor blocks forever. The comment at `:154-156` states the main thread "skips `done.wait()` entirely," but no such skip exists in the code. This is a regression introduced by the C096 timeout change, which converted a guaranteed 3-second stall into a permanent hang.

**Platform parity**

macOS-only. The Windows twin at `Windows/dotNET/Syxx.Kit/NetworkKeyState.cs:318` uses `process.WaitForExit(TimeSpan)` and branches on its bool return, with no semaphore. Linux links `Syxx.Kit` and inherits that correct shape. **Use the Windows twin as the target semantics**, per the macOS-is-gold-standard rule this is the rare case where the twin is already correct and macOS must be brought to it. No Windows or Linux code changes.

**Files**

- `macOS/Sources/SyxxKit/NetworkKeyState.swift`, re-anchor by `runProcess(_:_:timeout:)`, currently `:114-160`
- `macOS/Sources/SyxxApp/AppModel.swift`, re-anchor by `func checkStartup()`, currently `:822`
- `macOS/Sources/SyxxApp/ContentView.swift`, re-anchor by the `.onAppear` calling `model.checkStartup()`, currently `:98`
- New focused tests under `macOS/Tests/SyxxKitTests/` and `macOS/Tests/SyxxAppTests/`
- Inspect only, do not edit: `Windows/dotNET/Syxx.Kit/NetworkKeyState.cs`

**Required change 1: correctness in `runProcess`**

Wait on the semaphore only when the timer actually fired. Either capture whether `cancel()` won the race and skip `done.wait()`, or replace the semaphore handshake entirely with the Windows shape (bounded wait that returns a status), so a cancelled timer can never leave an unsignalled wait. Preserve the existing SIGTERM, grace period, SIGKILL escalation, and the `didTimeout` to `nil` contract.

**Required change 2: get startup off the main actor**

Even a correct 3-second bound freezes first paint. `checkStartup()` must not run a synchronous subprocess on the main actor. Populate `cachedSystemDNSServers` and `cachedEncryptedDNSStatus` off the main actor and publish the results back. Honesty gate: until those values arrive, every surface that reads them renders an explicit checking or unknown state. A nil cache must never render as a measured resolver, and the F011 constraint that view bodies contain no synchronous calls must be preserved rather than reintroduced.

**Pitfalls and risks**

- Do not "fix" this by deleting the timeout. The timer is what escalates to SIGKILL and unblocks `readDataToEndOfFile` when a child never closes its pipe. Removing it trades this deadlock for a different one.
- Do not close the read `FileHandle` from the timer thread. `NetworkKeyState.swift:141-145` documents that as a use-after-close race with an uncatchable ObjC exception (C112). Preserve that reasoning.
- `readDataToEndOfFile()` at `:151` runs before `waitUntilExit()` at `:152`; both can block. Any redesign must keep the pipe drained so a child writing more than the pipe buffer cannot deadlock on write.
- A fire-and-forget async refactor that silently leaves the resolver nil would satisfy the build and violate the honesty spine. The unknown state must be visible.
- Two hazards are only observable at runtime, so a green suite does not close this workstream. The real-run bar below is mandatory.

**Verification, with mandatory failure cases**

1. Fast-path unit test: run `/bin/echo` through `runProcess` with a 3.0 second timeout inside an XCTest expectation whose own timeout is 1.0 second, so a regression fails the test rather than hanging the suite. Must return the output immediately.
2. Mutation: restore the unconditional `done.wait()` after `cancel()`. Test 1 must turn red.
3. Timeout path: run `/bin/sleep 10` with a 0.5 second timeout. Must return nil at approximately the deadline, and the child must not survive. Mutation: remove the SIGKILL escalation and prove the test detects the surviving process or the unreturned read.
4. Startup path test: assert `checkStartup()` returns without blocking the main actor, and that the resolver cache is unknown until populated.
5. Real run, required by the house rule: launch the built app bundle, `sample` the process, and prove no main-thread frame sits in `semaphore_wait_trap` beneath `runProcess`. Then, with a fresh `HOME`, prove guided setup presents, and prove the Dock icon raises the window. Capture all three.

**Subagent deployment for 1.0**

Six specialists, deployed in four stages. Counts are per stage; stage 2 runs in parallel.

| Stage | Subagent | Source | Count | Charge |
|---|---|---|---|---|
| 1 | `error-detective` | `subagents/09-quality-security/error-detective.md` | 1 | Own the failure signature. Confirm the deadlock chain from the sample, then sweep the tree for the same unsignalled-semaphore and main-actor-blocking pattern anywhere else in `SyxxKit` and `SyxxApp`. Produce the blast radius before any edit. |
| 1 | `syxx-neural-debugger` | project agent | 1 | Produce the minimal reproduction and pin CODE versus TEST versus SPEC. Confirms `error-detective` independently rather than trusting the first read. |
| 2 | `swift-expert` | `subagents/02-language-specialists/swift-expert.md` | 1 | Advisor on the `runProcess` redesign: `DispatchWorkItem` cancellation semantics, pipe draining, and signal escalation. Recommends the shape; does not land GUI code. |
| 2 | `swiftui-architect` | `subagents/01-core-development/swiftui-architect.md` | 1 | Advisor on the startup path: moving `checkStartup()` off the main actor without reintroducing the F011 view-body synchronous call or an AttributeGraph cycle, and on the honest checking state. |
| 2 | `research-analyst` | `subagents/03-research-analysis/research-analyst.md` | 1 | Information synthesis. Reconcile the two stage-1 reports plus the two advisor recommendations into one agreed change specification, and surface any disagreement explicitly rather than averaging it. Consults `research/reference-library/` and the `syxx-reference-lookup` skill before any external search. |
| 3 | `syxx-engineer` | project agent | 1 | Land change 1 in `NetworkKeyState.swift` plus its unit and mutation tests. Owns `SyxxKit` only. |
| 3 | `syxx-neural-gui-architect` | project agent | 1 | Land change 2 in `AppModel.swift` and `ContentView.swift` plus the startup test and the honest checking state. Owns `SyxxApp` only. File ownership is disjoint from stage 3's other agent, so both may run in parallel. |
| 3 | `gui-cross-platform-continuity` | `subagents/01-core-development/gui-cross-platform-continuity.md` | 1 | Platform-parity specialist. This workstream changes macOS toward the Windows shape, the reverse of the usual direction, so parity must be asserted deliberately: prove the post-fix macOS `runProcess` semantics match `NetworkKeyState.cs:318` (bounded wait, status return, no unsignalled handshake), confirm the Linux client inherits the correct path unchanged, and confirm the startup-state contract renders the same unknown state on all three clients. Applies the `syxx-parity-check` skill and produces a PASS/FAIL parity report with file:line. Blocks the exit gate. |
| 4 | `performance-engineer` | `subagents/09-quality-security/performance-engineer.md` | 1 | Performance and telemetry specialist. Establish the launch budget this defect violated and make it measurable rather than anecdotal: instrument cold-launch time to first interactive frame, time spent on the main actor inside `checkStartup()`, and subprocess wall time for `scutil --dns`. Record a pre-fix and post-fix `sample` pair as the evidence artifact. Then verify the OpenTelemetry startup spans report the honest state, including the unknown resolver window, and confirm no span reports success for a measurement that has not completed. Coordinates with the open `bugsmasher.syxx.app` certificate finding in 5.5.6 rather than disabling telemetry to get a clean run. |
| 4 | `knowledge-synthesizer` | `subagents/04-meta-orchestration/knowledge-synthesizer.md` | 1 | Capture the durable lesson: a comment asserting behavior the code does not implement passed review and shipped a hang. Propose the guard that would have caught it, for the CHANGELOG entry and any future skill. |

Sequencing rule: stage 2 may not begin until both stage-1 reports exist, stage 3 may not begin until `research-analyst` publishes the agreed specification, and stage 4 runs only after the real-run capture in verification step 5. `gui-cross-platform-continuity` runs alongside the two stage-3 implementers but reports after both land, since parity cannot be asserted against unfinished code. `performance-engineer` requires the pre-fix baseline to be captured **before** stage 3 begins, so its first measurement task is scheduled with stage 1.

**Exit conditions**

- No main-thread frame waits on a semaphore beneath `runProcess` during launch.
- A fast subprocess returns in milliseconds; a hung subprocess still returns nil at its deadline and leaves no surviving child.
- Guided setup presents on a fresh install and the Dock icon raises the window, both captured.
- The resolver cache renders as unknown until measured, never as a fabricated value.
- Parity report PASSES: macOS `runProcess` semantics match the Windows twin, Linux inherits unchanged, and the unknown startup state renders identically on all three clients.
- A pre-fix and post-fix `sample` pair plus cold-launch timings are recorded, and no startup span reports success for an incomplete measurement.
- Filed as `D278` in `DELTAS.md` with the sample evidence.

## 1.1 Repair the macOS compilation break

**Diagnosis (corrected):** After the owner-directed first-run scan removal, the file lost its **tail**: the body of `resultAccessibilityLabel` (four-state switch), the struct closing brace, and the `#Preview` block. The accessibility function is not "missing as a concept"; the approved implementation already exists on the donor at `ed9a33b~1`. Raw brace counts can balance while the parser still fails mid-struct, which produces a misleading "cannot find in scope" error. Complete file is ~412 lines.

**Files**

- `macOS/Sources/SyxxApp/ConclusionsView.swift` (re-anchor by `struct ConclusionsView` / `resultAccessibilityLabel`)
- Inspect only: `macOS/Sources/SyxxApp/AppModel.swift`
- Inspect only: `macOS/Sources/SyxxApp/SyxxApp.swift`

**Steps**

1. Run `cd macOS && swift build` and retain the complete compiler output.
2. Extract `ConclusionsView.swift` from `ed9a33b~1` to a temporary file and verify that it is structurally complete (function body + struct close + optional `#Preview`).
3. Diff the predecessor file against current `ConclusionsView.swift`. Use it only as a structural donor; do not restore it wholesale.
4. Reapply the owner-directed GUI scan removal **narrowly** to the complete donor: remove only `firstRunCard` usage, the `firstRunCard` block, and `runFirstScan()`. Preserve `resultAccessibilityLabel` and unrelated accessibility/conclusion-surface changes. Do not invent new accessibility wording.
5. Search `macOS/Sources/SyxxApp` for `firstRunScanning`, `firstRunProgress`, automatic first-run scan runners, and scan-card views. Their absence is required.
6. Run `swift build` and `swift build -c release`.
7. Run the focused SyxxApp tests that compile `ConclusionsView`.

**Failure cases**

- A syntactic repair that restores scan state fails the owner constraint.
- A repair that drops accessibility labeling fails the GUI acceptance gate.
- Debug success with Release failure does not satisfy this workstream.

## 1.2 Recover the sixteen server acceptance behaviors

**Files**

- `Server/syxx_server/app.py`
- `Server/syxx_server/config.py`
- `Server/syxx_server/fulfillment.py`
- `Server/syxx_server/leak.py`
- `Server/syxx_server/resolver.py`
- `Server/syxx_server/stripe_checkout.py`
- `scripts/deploy.sh`
- Reference unless a focused failure proves otherwise: `scheduler.py`, `licensing.py`, `warp.py`, `resolver.py`, `fetcher.py`, `db.py`
  (`diff.py` and `doh.py` do not exist; diff/DoH live in `app.py` / `warp.py` / `resolver.py`)
- Evening accelerator: extract conflict-free sides from origin commit `3acf61c` into `/tmp` before transplanting by symbol
- Matching tests under `Server/tests/`

**Steps**

1. Run `OTEL_SDK_DISABLED=true venv/bin/python3 -m unittest discover -s tests` from `Server/`.
2. Extract the recoverable evening side of each conflicted file from `3acf61c` into `/tmp`, never directly into the worktree:

   ```bash
   git show 3acf61c:Server/syxx_server/app.py | awk '/^<{7}/{drop=1;next} /^={7}$/{drop=0;next} /^>{7}/{next} drop==0{print}' > /tmp/app.evening.py
   ```

   Repeat the same extraction for `config.py`, `fulfillment.py`, `leak.py`, and `resolver.py`. Extract clean `stripe_checkout.py` with `git show 3acf61c:Server/syxx_server/stripe_checkout.py` to a temporary file. Extract the stashed side of `scripts/deploy.sh` to a temporary file.
3. Verify the donor files before using them:
   - `app.py`: `_diff_verdict`, content-security policy, and logging snapshot/metrics behavior.
   - `config.py`: `_b()` and the recovered security/transport boolean flags.
   - `fulfillment.py`: processing lease, replay handling, and idempotent mint/delivery behavior.
   - `leak.py`: refusal to sign a clean no-observation result.
   - `resolver.py`: `_ip_is_public` delegating to `netutil.ip_is_public`.
   - `deploy.sh`: `macmotd`, `winmotd`, `debmotd`, certificate coverage checks, renewal proof, and the ACL renewal hook.
4. Diff each verified donor against current source by symbol. Transplant only missing behavior. Do not replace a current file wholesale and do not prefer donor code when the current implementation is newer or more honest.
5. Confirm the actual failures against this expected list:
   - Four fulfillment idempotency and Keygen retry cases.
   - Forged Keygen-valid key without required signature.
   - Authoritative-disjoint and clean-vantage-only diff classification.
   - Leak-test token creation.
   - Cross-host content injection.
   - Upstream DoH failure classification.
   - Scheduler error-row and due-monitor advancement.
   - Oversized and short request bodies.
   - Terminal DNS answer without AA.
   - HTTPS fetch pinned to the vetted IP.
6. For each mismatch, classify it as code defect, test defect, or specification ambiguity before editing.
7. Complete any behavior not fully restored by the verified donor code. Fulfillment must remain transactional and idempotent so duplicate, concurrent, retry, and minted-but-undelivered replay paths cannot create more than one license.
8. Require the Keygen signature contract even when external validity fields look correct.
9. Refuse to sign a clean leak response when observations are absent.
10. Preserve authoritative evidence requirements: valid disjoint authoritative evidence may support tampering, while clean-vantage-only disjoint evidence remains inconclusive.
11. Preserve explicit failure states for DoH upstream failure, incomplete bodies, oversized bodies, missing AA, scheduler exceptions, and pinned-connection failure.
12. Restore the verified deployment certificate names and ACL renewal hook. Test the certificate coverage and renewal path without weakening failure handling.
13. Verify `bugsmasher.syxx.app` resolution and presented certificate SANs separately. `OTEL_SDK_DISABLED=true` may prevent retry noise during unit tests, but it does not close or conceal the infrastructure defect.
14. Run focused tests after each behavior group, then rerun the complete server suite.
15. Start the production server with a temporary database. Exercise one real DNS investigation and one deliberately incomplete request. Capture both results and prove incomplete evidence cannot become clean.

**Failure cases**

- Removing uniqueness or replay protection must make the four fulfillment tests red.
- Serving a forged license must fail authentication.
- Removing AA validation must make the resolver hardening test red.
- Removing the pinned IP must make the fetcher test red.
- A remaining red test is filed and investigated, never skipped.

## 1.3 Remove the Dashboard Mirror false-pass predicate

**Files**

- `macOS/Sources/SyxxApp/DashboardView.swift`, `wardenState(for:)`, Mirror branch
- Matching Dashboard state tests
- Windows and Linux Dashboard twins after the macOS contract is proved

**Steps**

1. Add focused cases for Mirror not run, locked, inconclusive, degraded, matching/clean, and redirection detected.
2. Prove the redirection case currently returns `PASSED` because the predicate does not inspect `isRedirectionDetected`.
3. Change the macOS Mirror branch so only validated matching evidence may return `PASSED`.
4. Map redirection/tampering, inconclusive, degraded, failed, and missing evidence to their distinct approved states. Obtain owner approval before adding or changing displayed text.
5. Apply the macOS predicate to Windows and Linux Dashboard twins without weakening macOS.
6. Run a real Mirror comparison and a controlled redirection case through the production Dashboard.

**Mutation requirement**

Remove the redirection check and prove the Dashboard test turns red.

## 1.4 Freeze the verify-only core

**Steps**

1. Run the existing focused matrix C061 through C065 tests without changing production logic.
2. Run the current source-parity gate.
3. Run the focused Cage parser and attribution tests.
4. Run Witness B251/E162 vectors and B254 report tests.
5. Record failures as new findings. Do not rewrite green implementations to resemble older code.

## Phase 1 exit gate

- The app launches. No startup deadlock, guided setup presents on a fresh install, and the Dock icon raises the window, all captured from a real run.
- macOS Debug and Release compile.
- The removed GUI first-run scan remains absent.
- The sixteen named server behaviors pass their focused acceptance tests.
- Recovered deployment certificate names and the ACL renewal hook are present and verified.
- Dashboard Mirror cannot display `PASSED` for a redirection/tampering result.
- One real server investigation and one incomplete-request result are captured.
- Verify-only matrix, Cage, Witness, D118, EULA, typography, and LAN baselines have not regressed.

---

# Phase 2: Repair Security, Evidence, and Cross-Platform Core Contracts

## Objective

Close the security-significant gaps before adding or reshaping surfaces: B252 signed DoH evidence, Windows fail-closed ContentWarp behavior, centralized Windows navigation, macOS C141, and CLI process results.

## Sequencing inside Phase 2

1. B252 signed-payload schema and vectors.
2. Windows fail-closed URL and ContentWarp contracts.
3. macOS C141 unknown-state preservation.
4. B253 process-result propagation.

B252 comes first because later GUI, CLI, and Stage 5 work must not surface unauthenticated DoH content as signed evidence.

## 2.1 Add DoH outcomes to signed Witness evidence (B252)

> **VERIFY-ONLY (peer-eval 2026-08-01).** Live tree already signs canonical `dohOutcomes` at format v4 on both platforms. Do **not** reimplement. Run golden vectors and DoH-mutation refusal; only reopen this section if verification fails.

**Files**

- `macOS/Sources/SyxxKit/SignedEvidencePayload.swift:90-300`
- `macOS/Sources/SyxxKit/WitnessSigner.swift`
- `Windows/dotNET/Syxx.Kit/SignedEvidencePayload.cs:160-240`
- `Windows/dotNET/Syxx.Kit/WitnessSigner.cs`
- Shared Witness golden vectors
- `macOS/Tests/SyxxKitTests/WitnessCrossPlatformGoldenVectorTests.swift`
- `Windows/dotNET/Syxx.Tests/WitnessCrossPlatformGoldenVectorTests.cs`

**Steps**

1. Change the existing characterization so changing only a DoH status or DoH answer must invalidate the signature.
2. Run both focused suites and prove they fail before implementation.
3. Define a versioned canonical payload that includes normalized DoH outcomes using:
   - Deterministic resolver-key order.
   - Existing pinned DNS status wire strings.
   - Deterministic answer ordering.
   - Explicit absent/null rules.
4. Populate the signed field from actual partitioned DoH evidence rather than `nil` or `null`.
5. Implement the Swift and C# forms together.
6. Preserve legacy evidence readability, but never present legacy DoH display content as authenticated. Any new visible limitation text requires owner approval.
7. Regenerate shared golden vectors.
8. Run Swift-to-C#, C#-to-Swift, mutated-status, mutated-answer, unknown-status, and field-order negative controls.
9. Run both production Witness CLIs against each other's output and repeat after mutating a DoH field.

**Exit conditions**

- Both platforms sign the same canonical DoH evidence.
- Either platform refuses a DoH mutation.
- Legacy evidence cannot silently gain authenticated status it never had.

## 2.2 Complete Windows fail-closed remote-content behavior

> **VERIFY-ONLY (peer-eval 2026-08-01).** Live tree already returns typed inconclusive on SSRF refusal and routes navigation through `AuthorizeForNavigation`. Do **not** reimplement. Prove mutation (policy bypass → red) and any missing policy-unavailable case.

**Files**

- `Windows/dotNET/Syxx.Kit/ContentWarp.cs:50-90`
- `Windows/dotNET/Syxx.Kit/RemoteUrlPolicy.cs`
- `Windows/dotNET/SyxxUI/Views/RemoteContentPanel.xaml.cs`
- `Windows/dotNET/SyxxUI/Views/LicenseView.xaml.cs:110-150`
- Matching core and UI tests

**Steps**

1. Add a test proving an SSRF-refused target returns the typed C# twin of the macOS inconclusive result and does not throw.
2. Add tests for null URL, malformed URL, disallowed origin, allowed origin, and policy-unavailable behavior.
3. Replace the `ArgumentException` refusal path in ContentWarp with the typed result.
4. Route checkout, trial, update, broadcast, and remote-content navigation through `RemoteUrlPolicy`.
5. Ensure refusal and policy failure never fall through to navigation.
6. Obtain approval before adding or changing visible refusal text.
7. Run core tests, Windows UI tests, and native allowed/blocked navigation on Windows 11.

**Mutation requirement**

Temporarily bypass the policy in one caller. The UI navigation test must turn red.

## 2.3 Preserve unknown ARP states on macOS (C141)

> **VERIFY-ONLY (peer-eval 2026-08-01).** Live tree already preserves `.unknown` for unrecognized residual tokens. Do **not** reimplement. Run `LANLifecycleTests` and the unconditional-reachable mutation. BSD valid-MAC/no-token → reachable remains the documented default (owner decision to change).

**Files**

- `macOS/Sources/SyxxKit/LAN/ARPTableParser.swift:79-150`
- Matching macOS LAN parser tests

**Steps**

1. Add cases for recognized reachable, permanent, incomplete, invalid MAC, canonical MAC, and unrecognized state tokens.
2. Prove the unrecognized-token case currently fails because it becomes reachable.
3. Return reachable only for explicitly recognized reachable states.
4. Preserve unknown for every unrecognized state with otherwise valid fields.
5. Run parser and LAN lifecycle suites.
6. Run the production ARP path and capture every row's state.

**Mutation requirement**

Restoring the unconditional reachable fallback must make the unknown-token test red.

## 2.4 Complete machine-readable CLI results (B253 remainder)

**Files**

- `macOS/Sources/SyxxCLI/SyxxCLI.swift:1-100`
- Affected command entry points under `macOS/Sources/SyxxCLI/Commands/`
- Matching Windows command classes where behavior is shared
- Executable-level process tests

**Steps**

1. Inventory every subcommand and identify whether it can report completed, threat/tampering, inconclusive/invalid, and operational failure.
2. Use the existing Witness mapping as the shared contract: 0 completed non-threat, 1 detected threat/tampering where applicable, 2 inconclusive or invalid evidence, 3 operational failure.
3. Obtain owner approval for the exact help block and any changed error output.
4. Remove unconditional process 0 branches where a typed command result exists or must be added.
5. Spawn the real executable in tests for every result class. Unit-testing only the mapping is insufficient.
6. Preserve the current verified `witness report` behavior.
7. Run shared macOS/Windows command cases and prove process codes agree.

## Phase 2 exit gate

- DoH mutations invalidate Witness evidence on both platforms.
- Windows ContentWarp and every navigation entry point fail closed through one policy.
- Unknown macOS ARP states remain unknown.
- CLI process results are typed and tested at the executable boundary.
- Swift and C# security contracts remain in parity.

---

# Phase 3: Restore GUI Fidelity and Complete Product Services

## Objective

Restore the owner-approved guided-setup GUI, correct the Dashboard, implement the missing macOS Stage 5 display adapter, and finish the authenticated G-1 update system. This phase begins only after the evidence and failure contracts they display are trustworthy.

## Sequencing inside Phase 3

1. Freeze owner-approved GUI references and copy.
2. Complete updater server/client security foundations.
3. Implement the shared five-second remote-MOTD fallback contract.
4. Restore guided setup and connect honest configuration completion.
5. Correct Dashboard behavior.
6. Implement Stage 5 display adaptation.
7. Implement the Jost-led type system across native and web surfaces.
8. Implement the app-wide PurpleScrollView and replace every macOS scrollbar.
9. Bring Windows and Linux surfaces to the macOS gold standard.

## 3.1 Freeze GUI and output contracts

**Steps**

1. Obtain the owner-confirmed guided-setup mockup or screenshots that represent the pre-reset intended GUI.
2. Inventory exact screen count, order, dimensions, control order, defaults, typography, copy, selection styling, button states, and transitions.
3. Present exact proposed Dashboard headings, status text, ticker format, colors, trust label behavior, and empty-activity copy.
4. Present exact Stage 5 labels and any update status/error text.
5. Wait for owner approval before coding any changed visible output.
6. Record approved output locations in the implementation notes so `outputs-catalog.csv` can be updated in Phase 5.

No first-run scan screen, progress state, or automatic launch behavior may appear in the reference or implementation.

## Approved typography contract for workstream 3.7

The authoritative visual reference is `/Users/masterrabbit/Downloads/Syxx Type System.html`. Re-open the supplied file during implementation. The specimen's application mockups define typography only; their example copy, feature names, scan behavior, and security claims are not implementation authorization.

### Complete five-family role specification

**Cinzel: display**

- Use for window titles, view headers, module names, the wordmark, report-cover titles, and every Syxx-owned website H1 through H3.
- Use capitals only. Never use Cinzel for body copy. Never set it below 18 points/pixels.
- Available loading range is normal 400 through 900. The actual semantic styles should use 600 for section/view headings and 700 for primary display, wordmark, or major status headings unless an owner-approved design calls for another loaded weight.
- Reference settings extracted from the specimen:
  - Major page title: 44, weight 700, line height 1.1, tracking `0.02em`.
  - Section title: 26, weight 600, tracking `0.06em`.
  - Display family sample: 30, weight 700, tracking `0.03em`.
  - Dashboard heading: 26, weight 700, tracking `0.04em`.
  - Finding title: 20, weight 600, tracking `0.04em`.
  - Guided-setup title: 22, weight 600, tracking `0.04em`.
  - Loading-panel title: 22, weight 600, tracking `0.06em`.
- The specimen's menu mockup uses a 16-point Cinzel wordmark, which conflicts with its explicit 18-point minimum. The implementation follows the explicit minimum and uses 18 or larger.
- Approved sources:
  - Google Fonts specimen: `https://fonts.google.com/specimen/Cinzel`
  - Desktop source: `https://github.com/NDISCOVER/Cinzel`

**Jost: primary UI and body, replacing `system-ui`**

- Use for body copy, buttons, sidebars, toggles, table headers, tooltips, form fields, navigation, footer links, menus, labels, and eyebrows.
- The owner's body rule is exact: body copy uses Jost weight 300 or 400 only. Use 300 for the light body role and 400 for the standard body role.
- Use weight 500 for labels, buttons, controls, table headers, and eyebrows. This is not a body weight.
- Keep ordinary Jost UI below weight 600 because the face reads heavy at 600 and above. Loaded 600/700 files must not become ordinary product-UI tokens.
- Jost has a small x-height. Set body a little larger than an equivalent grotesque rather than compensating with a heavier weight.
- Eyebrows are 10 points/pixels, weight 500, uppercase, with `0.26em` tracking. Specimen reference labels use 11, weight 500, uppercase, with `0.22em` tracking. The font-card eyebrow example uses 10, weight 500, uppercase, with `0.24em` tracking.
- Reference settings extracted from the specimen:
  - Default paragraph: 14, line height 1.6, weight 300 or 400 according to the semantic body role.
  - Larger body/loading paragraph: 15, line height 1.65, weight 300 or 400.
  - Menu text: 14, weight 400.
  - Finding body: 14, line height 1.6.
  - Family sample: 30, weight 400, tracking `0.01em`.
- Available web loading ranges are normal 300 through 700 and italic 300 through 500. Product assets must include normal 300, 400, and 500. Include italic Jost only where an inventoried, owner-approved role requires it.
- Approved sources:
  - Google Fonts specimen: `https://fonts.google.com/specimen/Jost`
  - Desktop source: `https://github.com/indestructible-type/Jost`

**Cormorant SC: alternate body, deck, and lead copy**

- "Cormorant" in owner direction and this plan means the supplied Cormorant SC family.
- Cormorant SC is the explicit alternate body to Jost. Use weight 400 for the alternate-body token. It is an authored semantic choice, not a missing-Jost fallback.
- Its specimen roles remain required: the one-line summary under a title, onboarding-step text, feature-card descriptions, and footer credit.
- Deck/lead copy uses 18 through 24 points/pixels, weight 600, `0.02em` tracking, and a maximum of three lines.
- Reference settings extracted from the specimen:
  - Header deck: 21, line height 1.5, tracking `0.02em`.
  - Dashboard and guided-setup deck: 18, line height 1.5, tracking `0.02em`.
  - Footer credit: 15, tracking `0.06em`.
  - Family sample: 30, weight 600, tracking `0.03em`.
- Available web weights are normal 400, 600, and 700. Weight 400 serves alternate body, 600 serves deck/lead, and 700 remains available only for a specifically approved emphasis role.
- Approved sources:
  - Google Fonts specimen: `https://fonts.google.com/specimen/Cormorant+SC`
  - Desktop source: `https://github.com/CatharsisFonts/Cormorant`

**EB Garamond Italic: emphasis and lore**

- Use for emphasis spans, reassurance lines, warning explanations, and pull quotes.
- Use italic styling, zero tracking, and at most one EB Garamond span per paragraph.
- Set it approximately 2 points/pixels larger than the host text.
- Reference settings extracted from the specimen:
  - Family sample: 30, italic, weight 500.
  - Header emphasis within 21-point Cormorant SC: 23, italic, zero tracking.
  - Deck emphasis within 18-point Cormorant SC: 20, italic, zero tracking.
  - Body emphasis within 14-point Jost: 16, italic, zero tracking.
  - Loading-panel emphasis within 15-point Jost: 17, italic, zero tracking.
- Available web ranges are normal 400 through 600 and italic 400 through 600. The semantic emphasis/lore role uses italic 400 or 500 unless a specifically approved design requires 600.
- Approved sources:
  - Google Fonts specimen: `https://fonts.google.com/specimen/EB+Garamond`
  - Desktop source: `https://github.com/octaviopardo/EBGaramond12`

**JetBrains Mono: technical text, replacing `ui-monospace`**

- Use for paths, hashes, IP addresses, ports, team IDs, versions, timestamps, log output, CLI output, keyboard shortcuts, step counters, and compact technical status.
- Set it at 11.5 through 13 points/pixels beside 15 through 16 point/pixel Jost.
- Reference settings extracted from the specimen:
  - Family sample: 28, weight 400.
  - Timestamp: 12 or 11.5.
  - Technical path/hash block: 12, line height 1.8.
  - Guided-setup step counter and menu status/shortcut: 11.5.
  - Font-loading code: 12.5, line height 1.9.
  - Inline technical term inside the 21-point header deck: 16, zero tracking.
- Available web weights are normal 400 through 600. Use 400 for ordinary technical evidence and reserve 500/600 for approved technical emphasis.
- Approved sources:
  - Vendor page: `https://www.jetbrains.com/lp/mono/`
  - Google Fonts specimen: `https://fonts.google.com/specimen/JetBrains+Mono`
  - Desktop source: `https://github.com/JetBrains/JetBrainsMono`

### Loading, sourcing, licensing, and fallback specification

- Website loading uses the specimen's exact Google Fonts family request with `display=swap`:

  ```html
  <link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@400..900&family=Jost:ital,wght@0,300..700;1,300..500&family=Cormorant+SC:wght@400;600;700&family=EB+Garamond:ital,wght@0,400..600;1,400..600&family=JetBrains+Mono:wght@400..600&display=swap" rel="stylesheet">
  ```

- Native applications must not rely on that network request. Bundle the desktop TTFs from the named GitHub sources in the application targets. macOS is explicitly required by the specimen; Windows and Linux use the same upstream sources and font versions to preserve parity.
- Offline bundled HTML must load packaged local font resources. Public server pages may use the exact Google Fonts request above unless the release architecture elects to self-host byte-identical webfont files.
- All five families ship under the SIL Open Font License. Preserve each OFL text in every native package that contains the fonts.
- The license permits bundling the faces and embedding them in exported report PDFs. Exported reports must embed the required faces or document a format-level limitation and use the same semantic fallback order.
- Keep the platform system font last in every stack as the final technical fallback. Do not use `system-ui` or `ui-monospace` as a primary Syxx face. Do not silently cross-fallback between Jost and Cormorant SC.
- Preserve the needed Latin, Latin Extended, Cyrillic, Greek, and Vietnamese glyph coverage represented by the loaded webfont subsets. Use platform fallback for unsupported glyphs without changing the semantic role of the surrounding run.

### Cross-cutting implementation rules

- Five named faces cover every application and site surface. Archivo, Space Grotesk, Space Mono, primary `system-ui`, and primary `ui-monospace` are retired from the Syxx type system after the migration inventory is clean.
- Preserve Dynamic Type/text scaling, accessibility labels, localization, glyph fallback, and text selection. The named faces must not clip, truncate controls, hide focus indicators, or change the meaning or color of any conclusion surface.
- Retire obsolete resources, registration code, comments, style keys, and direct call sites only after the source inventory proves no production, report, web, test, or packaging reference remains.

## Approved PurpleScrollView contract for workstream 3.8

**Execution order:** This contract is documented before the affected view work so every Phase 3 surface is built with the approved scrollbar interface in mind. Do not implement it until workstreams 3.2 through 3.7 satisfy their honesty and typography gates. Workstream 3.8 below is the execution authorization for this contract.

**Visual contract approved by the owner**

- Replace every macOS application scrollbar with the PurpleScrollView/PurpleScroller treatment.
- Keep the track transparent.
- Use exactly two owner-specified knob geometries:
  - Full-panel application surfaces: 10 pixels wide by 420 pixels long.
  - Smaller scroll containers: 10 pixels wide by 50 pixels long.
- Keep a 4-pixel corner radius for both geometries.
- Use the owner-specified knob fill exactly: `rgba(150, 116, 235, 0.35)`.
- Add a soft, zero-offset lavender glow using `rgba(150, 116, 235, 0.55)` with a 6-point blur radius.
- The glow follows only the knob. It must not fill or outline the track.
- The treatment must remain legible in light and dark appearance without becoming a bright status indicator.

**Primary component files**

- `macOS/Sources/SyxxApp/Views/Components/PurpleScrollView.swift:1-113`
- Add focused component tests under `macOS/Tests/SyxxAppTests/`

**Required geometry API**

- Add an explicit style argument to every scroll surface. Do not infer style from arbitrary frame height:

  ```swift
  enum PurpleScrollbarGeometry {
      case fullPanel   // 10 x 420
      case compact     // 10 x 50
  }
  ```

- `PurpleScrollView` must require or deliberately default this geometry and pass it into `PurpleScroller`.
- The drawn knob rectangle, mouse hit target, drag calculation, accessibility frame, and reported scroll position must use the same geometry. Drawing a 420- or 50-pixel shape over AppKit's differently sized knob is not acceptable.
- Native values are specified as logical macOS pixels/points and scale normally on Retina displays. Web CSS uses the same numeric CSS-pixel dimensions.
- If the available track is shorter than the requested knob length, clamp the knob to the usable track length without clipping. Do not silently switch a full-panel surface to compact geometry.

**Required native call sites**

- `macOS/Sources/SyxxApp/PortalView.swift`
- `macOS/Sources/SyxxApp/ProphetView.swift`
- `macOS/Sources/SyxxApp/EULAPanel.swift`
- `macOS/Sources/SyxxApp/SettingsView.swift`
- `macOS/Sources/SyxxApp/DashboardView.swift`
- `macOS/Sources/SyxxApp/ConclusionsView.swift`
- `macOS/Sources/SyxxApp/WitnessView.swift`
- `macOS/Sources/SyxxApp/GuidedSetup.swift`
- `macOS/Sources/SyxxApp/GluttonView.swift`
- `macOS/Sources/SyxxApp/CageView.swift`
- `macOS/Sources/SyxxApp/MirrorView.swift`
- Any additional `ScrollView`, `List`, `Table`, `TextEditor`, `NSScrollView`, or scrollable AppKit wrapper found by the inventory step.

**Geometry assignment**

Full-panel `10 x 420` geometry applies to the primary application-level scrolling surface in:

- Portal
- Prophet
- Settings
- Dashboard
- Conclusions
- Witness
- Glutton
- Cage
- Mirror
- The main guided-setup choices surface
- What's New and other full-panel remote content

Compact `10 x 50` geometry applies to every smaller or nested container, including:

- EULA panel agreement text
- Guided-setup EULA agreement text
- Dashboard activity terminal
- Cage live terminal
- Nested Portal evidence/detail containers
- Nested Prophet evidence/detail containers
- Any nested card, log, picker popup, text editor, table, list, or secondary scroll region found during inventory

Every scrollable surface must be listed in the implementation inventory with its explicit `fullPanel` or `compact` assignment. No surface may inherit an unreviewed system scrollbar.

**Web-content call site**

- `macOS/Sources/SyxxApp/RemoteContentView.swift`
- The internal `WKWebView.scrollView` must receive the same `PurpleScroller` behavior or an equivalent native integration. Do not create nested scroll views around the web view.
- Keep `Resources/fallback-motd.html` and the server-hosted What's New CSS scrollbar colors synchronized with the approved darker/glowing native treatment where WebKit supports the effect. This does not refer to `motd-local.html` or authorize it as a fallback.

**Steps**

1. Inventory every macOS scroll container with:

   ```bash
   rg -n "\\bScrollView\\b|PurpleScrollView|NSScrollView|List\\s*\\{|Table\\s*\\{|TextEditor\\s*\\{" macOS/Sources/SyxxApp --glob '*.swift'
   ```

2. Classify each result as:
   - Native content that must use `PurpleScrollView`.
   - A control with internal scrolling that must receive `PurpleScroller` through its AppKit bridge.
   - Web content whose `WKWebView.scrollView` and CSS must match the same visual contract.
3. Add `PurpleScrollbarGeometry` and require each call site to choose `fullPanel` or `compact` explicitly.
4. Complete `PurpleScrollView.makeNSView` so it:
   - Hosts dynamic SwiftUI content at the full clip-view width.
   - Prevents unintended horizontal scrolling.
   - Shows the custom vertical scroller only when content exceeds the viewport.
   - Constructs `PurpleScroller` with the selected 10 x 420 or 10 x 50 geometry.
   - Preserves scroll-wheel, trackpad, thumb drag, page, keyboard, focus, and VoiceOver interaction.
   - Responds correctly to window resizing and content-size changes.
5. Implement fixed knob geometry inside `PurpleScroller` so AppKit drawing, hit testing, dragging, paging, and accessibility all agree on the actual 420- or 50-pixel knob rectangle.
6. Complete `updateNSView` so replacing SwiftUI content preserves a valid scroll position and never resets ordinary screens to the top without cause.
7. Keep `scrollToBottom` opt-in. It may auto-scroll only when its monotonic trigger advances. Ordinary screens must not jump.
8. For live logs, distinguish a user who has intentionally scrolled upward from a user already following the bottom. Do not drag a user back to the bottom while they are reviewing older events. Resume bottom-following when the user returns to the lower edge or explicitly requests it.
9. Implement `PurpleScroller.drawKnob()` with the approved fill and glow. Use an isolated graphics state so the shadow cannot leak into other AppKit drawing.
10. Give the knob an accessibility role and preserve the standard scroller value/range behavior inherited from `NSScroller`.
11. Replace or complete every native call site in the inventory and assign its explicit geometry. Existing `PurpleScrollView` references do not count as complete until the component passes the tests below.
12. Apply the full-panel `PurpleScroller` to the internal `WKWebView.scrollView` in `RemoteContentView` without wrapping the web view in another scroll container. Remove conflicting WebKit scrollbar CSS if it prevents the native 10 x 420 geometry from controlling the actual thumb.
13. Synchronize fallback/web styling to the same fill and glow only where it does not override the native fixed geometry.
14. Add a source guard that fails if a new unapproved native `ScrollView`, `List`, `Table`, `TextEditor`, raw `NSScrollView`, or WebKit scrollbar rule is introduced outside the component and approved bridge allowlist.

**Automated acceptance cases**

1. Short content does not show a useless scrollbar.
2. Full-panel content shows a knob measuring exactly 10 x 420 logical pixels when the usable track permits it.
3. Compact content shows a knob measuring exactly 10 x 50 logical pixels when the usable track permits it.
4. A track shorter than the requested knob clamps safely without clipping or negative geometry.
5. A tolerance-based rendered comparison detects the approved fill and glow outside the knob boundary without requiring byte-identical screenshots across display scales.
6. The visual knob rectangle and pointer drag target are identical.
7. Dragging either fixed-size knob reaches the exact top and bottom document offsets.
8. Clicking the track before or after the knob pages in the expected direction.
9. Dynamic content growth updates the scroll range without collapsing content width or changing the selected geometry.
10. Window resize recomputes the track and fixed-knob travel range.
11. Mouse wheel, trackpad, thumb drag, Page Up, Page Down, Home, End, and VoiceOver adjustment remain functional.
12. `scrollToBottom` does nothing when its trigger is unchanged.
13. Advancing the trigger follows the bottom when the user is already at the bottom.
14. Advancing the trigger does not steal position from a user reviewing older log entries.
15. Two PurpleScrollViews on one screen maintain independent positions and geometries.
16. `WKWebView` scrolling uses the full-panel 10 x 420 treatment and does not create nested scrolling.
17. A deliberate replacement with a raw SwiftUI `ScrollView` makes the source guard fail.

**Rendered verification**

1. Capture every required call site with overflowing content in both light and dark appearance.
2. Capture the Dashboard and Cage live terminals while appending events.
3. Capture EULA and guided setup with long text.
4. Capture both a successful remote What's New load and a forced-timeout load through `RemoteContentView`; record the loaded document identity and prove the timeout capture is `Resources/fallback-motd.html`, never `motd-local.html` or `fallback-mac.html`.
5. Assert 10 x 420 and 10 x 50 through deterministic AppKit geometry tests. Use tolerance-based rendered comparison for color and glow at 100% and Retina scaling; do not use pixel-identical cross-scale snapshots.
6. Verify the glow is visible but does not read as a security or success color.

**Exit conditions**

- No unapproved macOS scrollbar remains.
- Every native and web-content scrollbar uses the approved purple treatment and an explicit 10 x 420 or 10 x 50 geometry.
- Live logs preserve user-controlled scroll position.
- The component passes interaction, accessibility, resizing, dynamic-content, and rendered visual checks.

## 3.2 Finish the authenticated G-1 updater

**Files**

- `Server/syxx_server/appcast.py`, re-anchor at `build_appcast()` and `publish_package()`
- `Server/syxx_server/config.py`, re-anchor at the update version and URL settings
- `Server/syxx_server/app.py`, re-anchor at the appcast route registration and handler
- `macOS/Sources/SyxxKit/UpdateService.swift`, re-anchor at `AppcastVerifier`, manifest validation, download, and install methods
- `macOS/Sources/SyxxApp/UpdatePanel.swift`
- `Windows/dotNET/Syxx.Kit/UpdateService.cs`, re-anchor at `ValidatedDownloadUrl`, `DownloadWithProgressAsync`, and `InstallPackageAsync`
- `Windows/dotNET/SyxxUI/Views/UpdatePanel.xaml.cs`
- Linux Avalonia update view and view model
- Server, core, and UI updater tests

**Steps**

1. Provision a dedicated offline update-signing key. Do not reuse the deployment-variable collector identity as the long-term update identity.
2. Pin the corresponding raw Ed25519 public key in macOS and C# binaries. Never fetch the key from the manifest origin.
3. Make `publish_package()` persist, atomically and per platform:
   - Platform and package type.
   - Semantic version and build.
   - Allowlisted HTTPS download URL.
   - Full lowercase SHA-256.
   - Nonzero byte size.
   - Minimum compatible version.
   - Publication timestamp and approved release notes.
4. Make `build_appcast()` refuse to emit an installable manifest when any required package field is missing or invalid.
5. Sign the exact canonical bytes clients parse.
6. Change the C# downloader to choose `.zip` or `.deb` from the authenticated package type. Do not force every package into a `.zip` temporary path.
7. Reject a signed type/extension mismatch before installation.
8. Connect Windows `UpdatePanel` to `DownloadAndInstallAsync` instead of opening an unauthenticated browser-only path.
9. Add a Linux Avalonia update surface using the same authenticated service.
10. Verify macOS uses only the authenticated service.
11. Test wrong key, missing pin, malformed signature, altered metadata, empty hash, hash mismatch, zero size, disallowed URL, interrupted download, package-type mismatch, install failure, and rollback.
12. Run a valid and corrupted locally hosted update on macOS, Windows, and Debian.

**Exit conditions**

- No client downloads before signature and metadata validation.
- No client installs before package-hash validation.
- Debian uses the Debian installer path and Windows uses the ZIP installer path.
- Installation failure restores the previous version or reports an explicit failure/unknown state.

## 3.3 Load the packaged MOTD fallback after five seconds

**Owner-approved behavior**

- macOS, Windows, and Linux first attempt to load the platform's approved remote MOTD in an embedded WebView.
- Start a monotonic five-second deadline when the MOTD surface begins its remote-load attempt, before WebView initialization, startup-pointer resolution, fetch, or top-level navigation. Exactly one successful top-level remote load may cancel that deadline.
- If the remote MOTD has not successfully loaded within five seconds, cancel or ignore the outstanding remote navigation and load the client's packaged `fallback-motd.html` into the same WebView.
- The macOS fallback resource is exactly `macOS/Sources/SyxxApp/Resources/fallback-motd.html`. `motd-local.html` is neither an alias nor an allowed substitute. No macOS MOTD failure branch may resolve or load it.
- A definitive DNS, TLS, HTTP, navigation, content-policy, or connection failure may activate the same fallback before the five-second deadline. Five seconds is the maximum blank/loading interval, not a required delay after a known failure.
- Once fallback loading wins the attempt, a late remote callback must never replace it. A user-initiated retry creates a new attempt and a new five-second deadline.
- The fallback is content, not a second window, native substitute panel, external-browser launch, or overlay covering a blank WebView.

**Files**

- macOS:
  - `macOS/Sources/SyxxApp/RemoteContentView.swift`
  - `macOS/Sources/SyxxApp/BetaView.swift`
  - Create from the owner-approved canonical fallback source: `macOS/Sources/SyxxApp/Resources/fallback-motd.html`; do not rename, copy, or repurpose `motd-local.html` or `fallback-mac.html` to satisfy this requirement
  - `macOS/Package.swift`
  - focused RemoteContent/MOTD tests under `macOS/Tests/SyxxAppTests/`
- Windows:
  - `Windows/dotNET/SyxxUI/Views/RemoteContentPanel.xaml`
  - `Windows/dotNET/SyxxUI/Views/RemoteContentPanel.xaml.cs`
  - `Windows/dotNET/SyxxUI/Assets/fallback-motd.html`
  - `Windows/dotNET/SyxxUI/SyxxUI.csproj`
  - focused WebView2/MOTD tests under `Windows/dotNET/SyxxUI.Tests/`
- Linux:
  - `Linux/Ubuntu/SyxxUI.Avalonia/Views/WhatsNewView.axaml`
  - `Linux/Ubuntu/SyxxUI.Avalonia/Views/WhatsNewView.axaml.cs`
  - `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/WhatsNewViewModel.cs`
  - `Linux/Ubuntu/SyxxUI.Avalonia/Assets/fallback-motd.html`
  - `Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj`
  - focused MOTD/WebView tests under `Linux/Ubuntu/SyxxUI.Avalonia.Tests/`

The Windows and Linux fallback assets remain under their respective `Assets/` directories. The macOS fallback asset belongs under `Resources/`. Do not redirect any platform to another platform's packaged path.

Within this workstream, "packaged fallback," "bundled fallback," and "local HTML" mean exactly `macOS/Sources/SyxxApp/Resources/fallback-motd.html` for macOS. `motd-local.html` is not a fallback and must never be selected by the production What's New path. `fallback-mac.html` is not the MOTD timeout fallback and cannot satisfy a MOTD acceptance case.

**Shared state and timing contract**

1. Model each load attempt with a unique generation/token and explicit states equivalent to `initializingRenderer`, `loadingRemote`, `showingRemote`, `showingFallback`, and `rendererUnavailable`.
2. Define the MOTD deadline once per client as exactly 5.0 seconds. Use a monotonic clock or cancellable timer, never wall-clock date arithmetic or an unbounded WebView default timeout.
3. Remote success requires the allowed top-level MOTD navigation to complete successfully. Subresource completion, redirects to a refused host, a blank document, an HTTP error document, or a callback from an earlier generation does not count.
4. The timeout and navigation callbacks race through one atomic/main-thread transition. The first valid terminal state wins; every later callback for that generation is ignored.
5. On timeout, stop/cancel the remote URL request and WebView navigation before loading the packaged fallback. Loading the local fallback must not restart the remote deadline or be mistaken for remote success.
6. Load the platform's exact `fallback-motd.html` with a base URL rooted at its packaged directory so its local CSS, fonts, images, and links resolve. On macOS this means `Resources/fallback-motd.html`, never `motd-local.html` or `fallback-mac.html`. Do not grant local-file access outside that resource directory.
7. Apply the existing remote URL policy to every link or navigation initiated by both remote and fallback HTML. Block scripts, popups, downloads, custom schemes, new windows, and unapproved origins under the existing locked-down WebView contract.
8. The packaged fallback must be static and self-contained, render with JavaScript disabled, and issue no network subresource requests. The current bundled-wrapper form contains a JavaScript unpacker and therefore is not package-ready for the locked-down Windows WebView. Flatten the canonical rendered document into static HTML while preserving its approved visible copy and design.
9. Generate all three packaged `fallback-motd.html` files from one owner-approved canonical fallback source and require byte-identical SHA-256 hashes. Do not use `motd-local.html` or `fallback-mac.html` as that source. Preserve the fallback's visible copy and design unless the owner separately approves a change. This workstream changes selection, packaging, and timing behavior, not wording.
10. Add the nonvisual identity marker `<meta name="syxx-content-role" content="motd-timeout-fallback">` to the canonical fallback head. Tests, package inspection, and native WebView verification use this marker plus the canonical SHA-256 to prove which document loaded.

**Platform steps**

1. macOS: remove the production `BetaView` preference for bundled `motd-local.html` and restore the approved remote MOTD as the first attempt. Replace the current 30-second remote fetch budget for MOTD mode with the shared five-second deadline in `RemoteContentView.Coordinator`. Add an explicit MOTD configuration so generic remote-content failures can retain their own fallback policy. The MOTD fallback resolver must request `Bundle.module.url(forResource: "fallback-motd", withExtension: "html")` and no other resource name. Load that resource into `WKWebView`, cancel the data task/navigation on timeout, and use the generation token to prevent a late `URLSession` or `WKNavigationDelegate` callback from replacing fallback content. Remove `motd-local.html` from every production MOTD selection branch. Keep `fallback-mac.html` only for distinct non-MOTD remote-content roles that still require it.
2. Windows: include `EnsureCoreWebView2Async()`, startup-pointer resolution, and approved top-level WebView2 MOTD navigation inside one five-second attempt. On deadline or definitive failure, stop navigation and load `Assets/fallback-motd.html` with an asset-root base URL. Update the embedded-resource lookup and `SyxxUI.csproj` from the obsolete/deleted `fallback-win.html` MOTD path to `fallback-motd.html`; retain a separate generic fallback only if an inventoried non-MOTD surface still uses it. Keep JavaScript disabled for both remote and fallback content.
3. Linux: replace the native-only What's New body and external-browser-only full-page path with an embedded WebView that loads the approved remote MOTD. Select and pin an Avalonia 12.1.0/net10.0-compatible Linux WebView renderer for both `linux-x64` and `linux-arm64`; verify its native WebKit dependency is available before implementation and pause/report if it is not. Apply the same five-second generation-token race and load `avares://Syxx/Assets/fallback-motd.html` or an extracted read-only asset URL into that WebView.
4. Keep startup/broadcast metadata state logically separate, but include any startup-pointer work needed to select the remote MOTD URL within the same five-second surface deadline. A 15-second Dimension timeout or separate four-second platform-host probe cannot postpone fallback beyond five seconds.
5. Ensure the first-ready and returning-user paths instantiate this bounded MOTD surface once per displayed attempt. Cached WebViews must not preserve a timed-out remote navigation as if it were live content.

**Required tests and failure cases**

1. With an injected monotonic clock, remote top-level MOTD finishes at 4.999 seconds: remote content remains visible and fallback never loads.
2. With an injected monotonic clock, renderer initialization or remote top-level MOTD is still unfinished at 5.000 seconds: remote work is cancelled/ignored and `fallback-motd.html` becomes the WebView document.
3. DNS failure, TLS failure, connection refusal, timeout, HTTP 4xx/5xx, malformed content, disallowed redirect, and WebView navigation failure all reach the packaged fallback no later than five seconds.
4. A late remote completion after fallback activation cannot replace the fallback.
5. A fallback load callback cannot be mistaken for remote success and cannot schedule a second timer.
6. A stale callback from load generation N cannot affect generation N+1 after retry or view reuse.
7. The byte-identical fallback renders with JavaScript disabled; its local CSS, five-font assets, images, and allowed links resolve on all three packaged clients without network access or network subresource requests.
8. Link-policy mutations prove that fallback HTML cannot navigate to an unapproved host, open a popup, start a download, execute an unapproved script, or read outside its resource directory.
9. Remove, alter, or misname one `fallback-motd.html` in a packaging mutation and prove the package/hash test fails. A renderer-unavailable test must exercise the existing approved non-WebView error surface rather than leaving a blank panel.
10. Run each native client against a controllable MOTD endpoint that succeeds immediately, succeeds just under five seconds, stalls beyond five seconds, fails immediately, redirects off-policy, and completes late. Capture the WebView contents and elapsed monotonic time for every case.
11. macOS resource-selection test: leave both `fallback-motd.html` and `motd-local.html` in the test bundle, force the remote request to time out, and assert the WebView receives the bytes and base URL of `fallback-motd.html` only.
12. macOS negative mutation: remove `fallback-motd.html` while leaving `motd-local.html` available. The test must fail or enter the approved renderer-unavailable path; it must never load `motd-local.html` as a substitute.
13. Add a macOS source guard that fails if a MOTD timeout/failure resolver, `BetaView`, or `RemoteContentView` production branch references `motd-local`, or if MOTD mode selects `fallback-mac.html`.
14. Assert the timeout WebView document contains `syxx-content-role=motd-timeout-fallback`; assert a successful remote MOTD does not contain that marker.
15. Substitute `motd-local.html` or `fallback-mac.html` bytes at the packaged `fallback-motd.html` path and prove the canonical-hash/identity test fails.

**Exit conditions**

- All three clients render the remote MOTD in a WebView when it loads successfully before the deadline.
- All three clients render their own packaged `fallback-motd.html` in that WebView after at most five seconds without a successful remote load.
- macOS proves by resource identity and rendered bytes that the selected fallback is `Resources/fallback-motd.html`; `motd-local.html` is unreachable from every production MOTD failure path.
- A late remote response cannot displace fallback content.
- Windows and Linux package the asset from `Assets/`; macOS packages it from `Resources/`.
- Offline launch produces the packaged fallback rather than a blank panel, indefinite spinner, native substitute, or external-browser dependency.

## 3.4 Restore guided-setup GUI and honest completion

**Files**

- `macOS/Sources/SyxxApp/GuidedSetup.swift:5-373`
- `macOS/Sources/SyxxApp/FirstRunCoordinator.swift:5-196`
- `macOS/Sources/SyxxApp/SyxxApp.swift:78-190`
- Windows and Linux first-run views/view models
- Platform first-run lifecycle and GUI tests

**Steps**

1. Add snapshot or rendered-state tests for every owner-approved macOS setup screen.
2. Add interaction tests for unchecked EULA, decline, persistence failure, unreadable state, each choice, skip, begin, and the first ready frame.
3. Initialize the visible screen from `GuidedSetupSelection.safeDefaults` rather than duplicating default values.
4. Preserve the safer explicit-consent encryption default of off unless the owner approves another default. The visible state and “Skip: use defaults” must persist identical selections.
5. Separate settings preparation from the final ready transition.
6. When encrypted DNS is requested, wait for `EncryptedDNSConfigurator` to return success before entering ready.
7. On failure, keep the shell closed and show only approved failure copy. On timeout or unavailable evidence, preserve unknown and keep the shell closed.
8. Persist setup completion only after requested configuration and exact settings readback both succeed.
9. Prove crash reporting, telemetry, notifications, and update checks start only when selected.
10. Prove no diagnostic scan starts.
11. Prove first-ready and returning-user frames open the existing What’s New surface.
12. Render macOS, Windows, and Linux at matching logical states and bring Windows/Linux to the approved macOS layout.

**Failure cases**

- Screen default differs from skip default.
- Save succeeds but readback differs.
- Configuration fails after the shell opens.
- Corrupt settings start external services.
- Any scan begins automatically.

## 3.5 Align the Dashboard with its CHANGELOG contract

**Files**

- `macOS/Sources/SyxxApp/DashboardView.swift:40-550`
- `macOS/Sources/SyxxApp/AppModel.swift:108-160`
- Windows and Linux Dashboard views
- Dashboard tests

**Steps**

1. Add ticker cases for counts 0, 999, 1000, and 1001.
2. Render the approved three-digit modulo result using `cageEventsCount % 1000`.
3. Test empty activity, bounded 200-entry history, mixed general/Cage events, and continuous append behavior.
4. Audit every conclusion-derived heading, status, trust label, and color.
5. Implement only the owner-approved conclusion surface. Inconclusive input cannot receive reassuring green output.
6. Feed real `output` and `cageOutput` through the production Dashboard and capture the rendered activity terminal.
7. Apply the approved macOS behavior to Windows and Linux.

## 3.6 Implement macOS Stage5DisplayModel

**Files**

- Create: `macOS/Sources/SyxxApp/Presentation/Stage5DisplayModel.swift`
- Create: `macOS/Sources/SyxxApp/Presentation/Stage5PreviewFixture.swift`
- Test: `macOS/Tests/SyxxAppTests/Stage5DisplayModelTests.swift`
- Reference: `STAGE5-CONTRACT.md`
- Reference only: `Windows/dotNET/SyxxUI/Presentation/Stage5DisplayModel.cs`

**Steps**

1. Read `STAGE5-CONTRACT.md` and the existing tests completely.
2. Run `swift test --filter Stage5DisplayModelTests` and confirm the missing-type failure.
3. Implement a pure adapter over `Stage5Presentation`; do not recompute or strengthen the engine result.
4. Partition direct, inferred, and unavailable evidence.
5. Produce cross-vantage matrix rows and distinct hard-gate, conflict, and uncertainty explanations.
6. Order mechanism class, within-class secondary findings, and RPN findings according to the contract.
7. Add empty, partial, hard-gate, conflict, low-separation, and complete-evidence tests.
8. Render one real Stage 5 production result and prove the adapter invents no evidence.

## 3.7 Implement the Jost-led type system

**Primary files and inventories**

- macOS semantic styles and registration:
  - `macOS/Sources/SyxxApp/Views/Components/IoDesignSystem.swift`
  - `macOS/Sources/SyxxApp/GuidedSetup.swift`
  - `macOS/Sources/SyxxApp/Resources/`
  - `macOS/Package.swift`
- Windows semantic resources and packaging:
  - `Windows/dotNET/SyxxUI/Styles/IoDesignSystem.xaml`
  - `Windows/dotNET/SyxxUI/SyxxUI.csproj`
  - `Windows/dotNET/SyxxUI/Assets/Fonts/`
- Linux semantic resources and packaging:
  - `Linux/Ubuntu/SyxxUI.Avalonia/App.axaml`
  - `Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj`
  - `Linux/Ubuntu/SyxxUI.Avalonia/Assets/Fonts/`
- Bundled and server HTML, including:
  - `macOS/Sources/SyxxApp/Resources/fallback-mac.html`, non-MOTD generic remote-content fallback only
  - `macOS/Sources/SyxxApp/Resources/fallback-motd.html`, the only macOS MOTD timeout/failure fallback
  - `Windows/dotNET/SyxxUI/Assets/fallback-motd.html`
  - `Linux/Ubuntu/SyxxUI.Avalonia/Assets/fallback-motd.html`
  - `Server/whats-new-mac.html`
  - `Server/whats-new-win.html`
  - `Server/whats-new-deb.html`
  - every additional HTML/CSS file returned by the source inventory
- Every Swift, XAML, AXAML, C#, HTML, CSS, report renderer, PDF generator, email template, or package definition containing a raw font family or font-file reference.

**Semantic API contract**

1. Replace the Archivo-centered helpers with semantic roles rather than distributing raw family names through views. The macOS design system must expose equivalents of `display`, `sectionHeading`, `bodyLight`, `body`, `bodyAlternate`, `deck`, `emphasis`, `uiLabel`, `eyebrow`, and `technical`. Windows and Linux must expose resource keys with the same meanings.
2. `bodyLight` resolves to Jost 300 and `body` resolves to Jost 400. APIs accepting a body weight must use a restricted enum or separate named tokens so 500-900 cannot be passed accidentally.
3. `bodyAlternate` resolves to Cormorant SC 400. `deck` resolves to Cormorant SC 600. These are explicit authored choices and are never members of Jost's runtime fallback chain.
4. `uiLabel` and controls resolve to Jost 500. No ordinary UI role resolves to Jost 600 or 700.
5. Cinzel, EB Garamond Italic, and JetBrains Mono preserve the approved roles and limits in the contract above. Remove display styles below 18 points/pixels and replace them with the appropriate Jost or Cormorant role.
6. Keep platform system fonts last in each family stack for missing glyphs or a font-registration failure. Record registration failures through an existing diagnostic channel without adding unapproved user-facing copy.

**Implementation steps**

1. Re-anchor every file by symbol, then inventory all raw `.custom`, `FontFamily`, `font-family`, `font:`, and font asset references. Classify each occurrence by semantic role before changing it.
2. Obtain desktop TTFs directly from the five GitHub sources named in the approved contract. Pin one upstream revision/version per family. Acquire Cinzel 400-900, Jost normal 300-700 and italic 300-500, Cormorant SC 400/600/700, EB Garamond normal and italic 400-600, and JetBrains Mono 400-600, using variable or static files consistently across platforms. Verify each file's family, subfamily, weight, style, PostScript name, license, checksum, and glyph coverage before integration. If a dependency is unavailable, stop and report it rather than substituting a visually similar face.
3. Add identical required font binaries and all five OFL texts to the macOS, Windows, and Linux font-resource directories. Remove duplicate archives and obsolete Archivo, Space Grotesk, and Space Mono assets only after all references are gone. Record SHA-256 hashes and prove corresponding files are byte-identical across platform packages.
4. Update macOS font registration and `IoDesignSystem.swift` to implement the semantic API contract. Replace direct Archivo references in `GuidedSetup.swift` and the rest of `macOS/Sources/SyxxApp/` with semantic tokens. Ensure SwiftPM copies every required resource into Debug and Release app bundles.
5. Update `IoDesignSystem.xaml` and all Windows views to use the matching semantic resource keys. Verify `SyxxUI.csproj` packages every weight and license file and that URI fragments match the fonts' internal family names.
6. Update `App.axaml` and all Avalonia views to the same semantic roles. Verify the `avares://` URIs and project resource declarations on a supported Linux host.
7. Give bundled/offline HTML local `@font-face` declarations for all five packaged families. Give public website pages either the exact `display=swap` Google Fonts request in the approved contract or byte-identical self-hosted webfonts with the same family, style, weight, and glyph coverage. Use Jost 300/400 for body, Jost 500 for controls, and the explicit role selectors above. Keep the system family last and do not leave `system-ui`, `ui-monospace`, Archivo, Space Grotesk, Space Mono, or a generic family as the primary declaration on a Syxx-owned surface.
8. Audit report, PDF, email, and release-note renderers. Embed the named faces in exported report PDFs as permitted by the OFL. Package local faces for offline output and preserve the semantic fallback order where an output format cannot embed fonts. Do not change any visible wording during the typography pass.
9. Reflow every affected GUI at minimum and maximum supported window sizes, with representative long localization strings and large text scaling. Correct clipping by layout changes, not by shrinking body text below the approved role.
10. Update font-license inventories and packaging preflight checks. Document the final semantic type map and exact shipped font versions in the appropriate design/release record without copying the bundled specimen into the repository.

**Verification and failure cases**

1. Add a source guard that fails if production UI code introduces Archivo, Space Grotesk, or Space Mono; uses a raw Jost body weight outside 300/400; declares Jost 600+ for ordinary UI; places Cinzel below 18 or in body; uses EB Garamond emphasis without italic; or makes `system-ui`/`ui-monospace` the primary Syxx family.
2. Add asset tests that fail when any required family, weight, style, glyph range, source/version record, checksum, or OFL file is absent, unreadable, misnamed, not packaged, or hash-divergent across platforms.
3. Add semantic-token tests that assert Jost 300/400 body, Cormorant SC 400 alternate body, Cormorant SC 600 deck, Jost 500 labels, Cinzel display at 18 or larger, EB Garamond Italic at host size plus approximately 2, and JetBrains Mono technical output at the specified relative scale. Mutate each token to the wrong family, style, weight, size, tracking, or line-height class and confirm the test fails.
4. Add rendered layout checks at 100%, 125%, 150%, and 200% text scaling, in light and dark appearance, and at compact/full window widths. Use bounded geometry and perceptual tolerances rather than brittle pixel-exact Retina snapshots.
5. Exercise missing-font and missing-glyph cases. The UI must remain readable through the final system fallback, while tests and diagnostics identify the missing packaged asset.
6. Perform native production launches on macOS, Windows, and Linux. Capture the same guided setup, Dashboard, conclusion, settings, and technical-evidence surfaces. Confirm Jost 300/400 body rendering, intentional Cormorant SC alternate-body use, matching hierarchy, no clipping, and no status-semantic regression.
7. Render the supplied local HTML specimen beside the native captures as a visual reference. Visual similarity does not replace the semantic-role, asset, accessibility, and packaging checks.

## 3.8 Implement PurpleScrollView across every scrollable surface

Execute the complete approved PurpleScrollView contract above, including the geometry API, explicit full-panel/compact assignment inventory, AppKit and WebKit integration, source guard, interaction tests, accessibility tests, deterministic geometry tests, tolerance-based rendered checks, and native call-site verification. This workstream begins only after guided setup, Dashboard, and Stage 5 meet their honesty gates.

## Phase 3 exit gate

- The approved guided setup renders on all three platforms and never starts a scan.
- Each platform's remote MOTD either completes successfully before the five-second deadline or its packaged `fallback-motd.html` is rendered in the same WebView; timeout cancellation, late-callback suppression, offline loading, and asset packaging pass on macOS, Windows, and Linux.
- Jost is the primary UI/body face on all three platforms and owned web surfaces; body weights are limited to 300/400; Cormorant SC is the explicit alternate body and deck family; Cinzel, EB Garamond Italic, and JetBrains Mono satisfy every approved role and sizing rule; native/offline surfaces package GitHub-sourced TTFs and all OFL texts; public web surfaces use the exact approved `display=swap` request or byte-identical self-hosting; exported PDFs embed the named faces; and no production surface retains Archivo, Space Grotesk, Space Mono, primary `system-ui`, or primary `ui-monospace`.
- PurpleScrollView replaces every macOS scrollbar and renders the approved darker knob and glow without breaking interaction or accessibility.
- Visible and saved defaults agree.
- Requested configuration cannot fail invisibly after ready.
- Dashboard ticker and conclusion surfaces match approved behavior.
- Stage 5 renders only the engine DTO.
- A correctly signed update succeeds on each platform and corrupted updates fail closed.

---

# Phase 4: Enforce Behavioral Parity and Prepare Release Sources

## Objective

Turn parity from a source-vocabulary check into a behavioral gate, replace host-dependent tests with deterministic contracts, correct documentation, and repair packaging definitions before any final artifact is built.

## Sequencing inside Phase 4

1. Deterministic test repairs.
2. Transcript emitters and blocking CI.
3. Documentation corrections.
4. Packaging-source corrections.

## 4.1 Replace B255 and B256 host dependence

**Files**

- `macOS/Tests/SyxxAppTests/AppModelWitnessTests.swift:288-295`
- The production dependency seam used by that test
- `Windows/dotNET/Syxx.Tests/MonitorSchedulerTests.cs:80-101`
- The scheduler clock/tick seam

**Steps**

1. Separate the AppModel deterministic contract from the real-network integration run.
2. Inject the network/comparison dependency and assert the exact degraded failure classification.
3. Retain one explicit production-path network run outside the deterministic unit suite.
4. Inject a controllable clock or tick source into the scheduler.
5. Advance time deterministically and assert the exact due-monitor count.
6. Remove dependence on `Task.Delay(90)` without increasing delays, adding retries, or adding skips.
7. Mutate the injected failure and tick behavior and prove both tests turn red.

## 4.2 Make cross-runtime transcripts blocking

**Files**

- `.github/workflows/ci.yml`, re-anchor at the source-parity and transcript-parity job identifiers
- `scripts/cross-platform-parity-gate.py`
- macOS and .NET transcript emitters
- Shared parity vectors and transcript schema
- `.agents/skills/syxx-ci-pipeline/SKILL.md:70-90`

**Steps**

1. Define one canonical transcript schema with input identity, evidence completeness, active and suppressed findings, final conclusion, and required confidence fields.
2. Use sorted collections, invariant number formatting, explicit nulls, and one rounding rule.
3. Exclude timestamps, random identifiers, local paths, and platform diagnostics.
4. Implement emitters that call the production determinative engines. Do not duplicate engine logic in the emitters.
5. Feed identical vectors to Swift and C#.
6. Fail CI on zero transcripts, missing output, malformed output, field disagreement, numeric disagreement, or differing conclusions.
7. Introduce one deliberate controlled divergence and prove the job fails before restoring correct behavior.
8. Make the transcript job blocking.
9. Update the CI skill documentation to distinguish source-vocabulary parity from behavioral transcript parity.

## 4.3 Correct documentation

**Files**

- `README.md:50-90`
- `docs/ARCHITECTURE.md:150-190`
- `docs/BUILDING.md:40-70,260-380`
- `docs/USER-GUIDE.md:30-45`

**Steps**

1. Replace stale `Ubuntu/SyxxUI.Avalonia` references with `Linux/Ubuntu/SyxxUI.Avalonia`.
2. Mark the intended architecture fence as Mermaid and validate its syntax.
3. Describe the current platform matrix, server checks, source-parity gate, behavioral transcript gate, honesty coverage, Ruff, and Bandit.
4. Remove the instruction claiming the GUI performs a first-run scan.
5. State that GUI scans begin only when the user invokes a module.
6. Check every documented command and path against an existing source file or script.

## 4.4 Repair packaging definitions

**Files**

- `Linux/Ubuntu/build_deb.sh:90-120`
- `build-installer.ps1`
- `Windows/dotNET/SyxxPackage/SyxxPackage.wapproj`
- `Windows/dotNET/SyxxUI.Package/SyxxUI.Package.wapproj`
- Verify: `macOS/bundle-app.sh`
- Verify: `macOS/scripts/build_dmg.sh`

**Steps**

1. Replace the unconditional `libicu74` dependency with distribution-aware supported dependency selection.
2. Fail packaging for an unsupported distribution rather than emitting an uninstallable package.
3. Select one authoritative Windows packaging project.
4. Confirm both cited packaging project paths and their current project references before choosing. Do not rely on a stale line or path merely because it appears in this plan.
5. Ensure the authoritative project contains a complete manifest, required assets, GUI executable, CLI executable, and signing configuration.
6. Remove duplicate build routing only after the authoritative project produces the intended package.
7. Add content checks for:
   - DMG application and `/Applications` link.
   - Bound `Info.plist`, helper sections, EULA, fonts, GUI, and CLI.
   - macOS `Resources/fallback-motd.html`, Windows `Assets/fallback-motd.html`, and Linux `Assets/fallback-motd.html` in the actual installed application payloads, all with the canonical SHA-256 and `motd-timeout-fallback` marker. The macOS package check must fail if `motd-local.html` or `fallback-mac.html` is substituted.
   - Debian root ownership and satisfiable dependencies.
   - Windows GUI and CLI contents.
   - CLI ZIP runtime contents.
   - Matching version numbers and SHA-256 records.
8. Do not build final artifacts in this phase. Prove only that packaging definitions and preflight checks are correct.

## Phase 4 exit gate

- B255 and B256 are deterministic and mutation-sensitive.
- Behavioral transcript parity is nonempty and blocking.
- Documentation contains no stale Linux path or automatic first-run scan instruction.
- CI documentation matches the workflow.
- Debian and Windows packaging definitions pass preflight validation.

---

# Phase 5: Native Verification, Packaging, and Release Evidence

## Objective

Prove the completed system through native production entry points, build all required configurations and artifacts, and synchronize project records with observed reality.

## 5.1 Run complete automated verification

**Steps**

1. macOS:
   - `swift build`
   - `swift build -c release`
   - `swift test`
2. Server:
   - `OTEL_SDK_DISABLED=true venv/bin/python3 -m unittest discover -s tests`
3. Windows core and CLI:
   - `dotnet build Windows/Syxx.sln`
   - `dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj`
4. Windows UI lifecycle and navigation suites on Windows 11.
5. Linux Avalonia suites on a supported Linux host.
6. Source-vocabulary and behavioral transcript parity gates.
7. Investigate and file every red test. Do not skip or weaken it.

## 5.2 Run production-entry evidence flows

**Matrix and guided flow**

1. Run macOS and Windows on the same real domain.
2. Run controlled clean, tampered, degraded, missing-evidence, and conflicting-evidence inputs.
3. Capture completeness, raw support, suppression, final conclusion, and transcript identity.
4. Prove missing evidence cannot become clean or high confidence.

**Cage**

1. Run macOS DLT 149 capture with a VPN-active `utun` interface.
2. Capture process attribution and disclosed fallback behavior.
3. Run elevated Windows capture and UDP-table attribution.
4. Exercise unsupported datalink and unavailable attribution.

**Witness**

1. Sign on macOS and verify on Windows.
2. Sign on Windows and verify on macOS.
3. Mutate a DoH field and prove both platforms refuse it.
4. Run `witness report` with valid, tampered, stale, and malformed evidence.
5. Capture actual process results.

**GUI**

1. Launch native macOS, Windows, and Linux GUIs.
2. Complete EULA and guided setup.
3. Prove no first-run scan starts.
4. Prove saved choices control services.
5. Verify the first ready frame is What’s New.
6. On each client, load a real remote MOTD before five seconds and confirm it remains in the WebView.
7. On each client, stall the real test endpoint beyond five seconds and confirm the packaged `fallback-motd.html` replaces it in the WebView at the deadline.
8. Release the stalled response after fallback is visible and confirm it cannot replace the fallback.
9. Disconnect the network and confirm fallback HTML, local fonts, images, CSS, and permitted links work without an external browser. On macOS, inspect the WebView document marker and loaded bytes to prove the file is `Resources/fallback-motd.html`, not `motd-local.html` or `fallback-mac.html`.
10. Run one user-initiated module.
11. Observe Dashboard activity and Stage 5 output.
12. Confirm missing evidence stays unavailable or inconclusive.

**Updater**

1. Perform one valid signed update on each platform.
2. Perform one wrong-signature update.
3. Perform one hash-mismatch update.
4. Interrupt one download.
5. Force one installation failure and verify rollback.

## 5.3 Build and inspect release artifacts

**Steps**

1. Build macOS Debug and Release plus the app bundle:
   - `bash macOS/bundle-app.sh debug`
   - `bash macOS/bundle-app.sh release`
2. Build the DMG with `bash macOS/scripts/build_dmg.sh release`.
3. Build the Debian package with `SYXX_DEB_ARCH=amd64 bash Linux/Ubuntu/build_deb.sh release`.
4. Build the Windows GUI package and CLI ZIP through the authoritative packaging project.
5. Verify every package-content assertion from Phase 4.
6. Install on clean macOS, Windows 11, and supported Debian/Ubuntu hosts.
7. Capture version, signature state, first launch, guided setup, module launch, CLI version, uninstall, and rollback.
8. Do not describe Apple Development signing as distributable Developer ID signing.

## 5.4 Apply the honesty gate

Answer and retain evidence for each question:

1. Did any user-facing conclusion appear before all required data was collected and validated? Required answer: no.
2. Is there a production-entry real run for every changed workstream? Required answer: yes.
3. Can empty or degenerate input produce high confidence? Required answer: no.
4. Does any production path print fixture-driven success? Required answer: no.
5. Does any new machinery rely on an untrusted signal? Required answer: no.
6. Is every remaining defect stated plainly and filed? Required answer: yes.

## 5.5 Synchronize records

**Files**

- `CHANGELOG.md`
- `ROADMAP.md`
- `DESIGN.md`
- `outputs-catalog.csv`
- `DELTAS.md` when new defects are found

**Steps**

1. Update the CHANGELOG only with work actually implemented and real runs actually captured.
2. Mark ROADMAP items complete only when their production-path acceptance gates pass.
3. Update DESIGN for the signed Witness schema, updater trust architecture, Stage 5 surface, or other architectural changes.
4. Update every changed GUI and CLI output location in `outputs-catalog.csv`.
5. File every newly observed mission-impairing defect with exact file and line evidence.
6. Record the observed `bugsmasher.syxx.app` certificate-hostname mismatch with DNS resolution, requested SNI, presented certificate SANs, affected telemetry/export path, and whether the deployment certificate recovery closed it. Do not treat `OTEL_SDK_DISABLED=true` as remediation.
7. Perform a final scan for unsupported claims, stale paths, debug fixtures, and output that bypasses evidence.
8. Keep all commits and generated artifacts local. Push only after the owner reviews the final evidence and explicitly authorizes it.

## Phase 5 exit gate

The plan is complete only when all of the following are true:

- The GUI first-run scan remains absent.
- Every partial or absent CHANGELOG capability listed in the baseline is implemented or explicitly retained as open with owner-approved scope.
- Native macOS, Windows, Linux, and server runs support the claimed behavior.
- Remote MOTD success, five-second fallback, offline fallback, and late-response suppression are captured from all three native WebViews.
- B252 mutations fail on both platforms.
- Update installation requires a valid pinned signature, complete metadata, and a matching package hash.
- No surface reassures from missing, timed-out, invalid, or untrusted evidence.
- Behavioral transcript parity is blocking and nonempty.
- Debug, Release, application bundle, DMG, Debian package, Windows GUI package, and CLI ZIP are built and inspected.
- Clean-host installations and launches are captured.
- CHANGELOG, ROADMAP, DESIGN, DELTAS, and the output catalog match what actually ships.

# Syxx Roadmap Implementation — Persistent Memory Cache

Last updated: 2026-08-01 (Workstream 1.0 D278 done: gates shipped, verified, committed; real-run bar met; active thread is stopping Google Fonts in whats-new WebViews). This file is a working cache for the ROADMAP.md / DELTAS.md
remediation effort. It is safe to overwrite; it holds no secrets.

## 2026-08-03: Prophet parity drift collapsed; the shared-Kit location is the root cause

The Wave 2b "parity reached" claim was not true at the working tree. macOS got a later pass
(2026-08-02 owner lore + ioProphetBlue) that Windows and Linux never received, and BOTH
`Syxx.Kit/ProphetPresentation.cs` and the Linux inline duplicate were frozen at the pre-lore
snapshot. Windows consumes the Kit, so Windows was stale too. Fixed: Kit raised to the macOS
gold standard, Linux inline duplicate deleted, Linux now consumes the Kit through a
ColorKey-to-IBrush mapper. See the 2026-08-03 CHANGELOG entry for the file-by-file record.

Three things worth carrying forward:

- **The shared tier's location causes this defect class.** `Syxx.Kit` lives at
  `Windows/dotNET/Syxx.Kit/`. A Linux agent needing shared presentation logic will not reach
  into a folder named `Windows`; it writes its own copy. That is exactly what happened. The
  owner's intended layout is `dotNET/{Windows, Linux, Shared}` and it is the structural fix.
  Do NOT propose hoisting `Syxx.sln` to the repo root instead; that was suggested and is the
  weaker idea, because it leaves the shared tier implicitly owned by a platform folder.
  `Windows/Syxx.Linux.slnf` sits beside `Syxx.sln` only because MSBuild resolves a filter's
  `solution.path` relative to the filter file.
- **"begans" was a typo for "began"**, owner-confirmed 2026-08-03. macOS and Windows already
  read "began"; only Linux carried it, alongside a wholly different longer sentence. When a
  platform's copy differs from macOS by more than a typo, do not fix the typo alone: take the
  macOS sentence.
- **A banned em-dash was shipping as data.** The unmeasured-latency placeholder was a bare
  em-dash on both C# clients. Prose reminders have not held (this is the eighth regression of
  the em-dash rule); the consensus and DNSSEC surfaces now carry `DoesNotContain` guards in
  both suites, but a CI grep gate is still the real fix and still does not exist.

## 2026-08-01: D278 closed out — parity gates landed, real-run bar met, Google Fonts thread opened

**D278 shipped and verified.** The full Workstream 1.0 path is done and committed on `main`
(nothing pushed; owner-gated):

- Commits: `69237bd` (D278 macOS fix), `b38d71a` (plan + D278 deltas), `edcafab` (parity gates,
  11 files +319/−20, includes `Reports/D278-parity-report.md`).
- Parity batch `edcafab`: Windows gates at `AppViewModel.cs:393-411` (RunProphetAsync),
  :459-467 (RunMirrorAsync), :728-737 (witness seal), all `ProbeRunState.Failed` /
  `WitnessTestimonyState.Failed` + owner-approved unavailable string; Linux gates at
  `ProphetViewModel.cs:33-41`, `MirrorViewModel.cs:39-48`, `WitnessViewModel.cs:108-116`; old
  1.1.1.1/8.8.8.8 fallback-warning strings removed. `.TrimEnd('.')` at `ProphetView.xaml.cs:106`
  and `MirrorView.xaml.cs:87` (owner-approved double-period fix).
- Ledgers: `D278-parity-report.md` verdict FAIL → RESOLVED (fix landed 2026-08-01); CHANGELOG
  parity entry; MEMORY parity section; DELTAS C198 = FIXED IN CODE 2026-08-01; reflowed.
- Verification: Kit 6/6 + App 8/8 focused suites green; all 14 non-keychain App GUI suites
  passed; Debug 13.64 s + Release 84.93 s builds green; Linux `dotnet build -c Debug` 0 errors.
  Pre-existing failures only: keychain `keychainError(-34018)` (AppModelWitnessTests,
  WitnessTestifyParityTests) + determinative-core matrix/probe suites.
- Full suite: 629 executed, 3 skipped, 92 failures / 37 unique tests. **100% pre-existing**,
  proven by re-running representative failures at parent `b38d71a` in a temp worktree (removed +
  pruned after). Inventory at `/tmp/syxx_full_run.txt`, `/tmp/syxx_failures.txt`.
- Real-run bar: fresh Release bundle `./macOS/bundle-app.sh release` built + code-signed;
  launched against fresh HOME (`/tmp/syxx-freshhome.wLS7bD`); `sample` post-fix shows the main
  thread 100% idle in the AppKit event loop with ZERO NetworkKeyState / runProcess /
  getSystemDNSServers / checkStartup / semaphore_wait_trap frames (the only 2
  `semaphore_wait_trap` hits are 2423 samples on an idle WebKit "Log work queue" worker);
  relaunched via `open` (pid 77983, Dock-present); Quartz `CGWindowList` shows exactly one
  on-screen window (number 2126, 1050×645, layer 0). Visual guided-setup proof is SOFT:
  `screencapture -l 2126` failed (no screen-recording permission), so window-server + fresh-HOME
  evidence was used instead.
- Mutation proof executed and reverted cleanly: removing `process.terminationHandler` made
  `testFastPathReturnsOutputWithoutBurningTimeout` go red (1.0044 s > 1.0 s bound); revert
  restored 6/6 green, `git diff` clean.

**New active thread: stop the whats-new WebViews from downloading Google Fonts.**

- TTFs are already committed in all three code trees (`Windows/dotNET/SyxxUI/Assets/Fonts/`,
  `macOS/Sources/SyxxApp/Resources/`, `Linux/Ubuntu/SyxxUI.Avalonia/Assets/Fonts/`, 13 TTFs +
  5 OFL each). Owner scoping rule: "If the app loads the font, bundle in the app. If loaded on
  the website, bundle on the website."
- Live whats-new pages still pull Google Fonts: `Server/whats-new-mac.html:6` (plus win/deb
  twins) and ~11 other `Server/*.html` pages (incl. docs/help.html, docs/api.html).
- Root cause of the visible inconsistency: whats-new CSP is `font-src data:` (app.py:1084).
  macOS `RemoteContentView` fetch-and-rewrites into `loadHTMLString`, which DROPS the CSP header,
  so Google fonts load on macOS. Windows loads the URL directly into WebView2, CSP stays intact,
  Google `<link>` is blocked, Windows falls back to system fonts. Linux has no live WebView
  (Avalonia) so it is already covered.
- Client-side injection is NOT integrity-blocked: `rg` for signature/ed25519/verify/D041/
  signatureBase64/x-syxx-signature found no hits in `RemoteContentView.swift`,
  `RemoteContentPanel.xaml.cs`, or `DimensionService.cs`.
- The five families (Cinzel, Cormorant SC, EB Garamond, JetBrains Mono, Jost) are already
  embedded as base64 `@font-face` in all three `fallback-motd.html` files, in the FIRST
  `<style>` block (lines 8-18; second `<style>` = lines 19-248). Sizes: Windows 2,745,845 B,
  macOS 2,745,898 B. This is the injection source for the live pages.
- Planned split (per owner rule, not yet approved as code): (a) macOS inject the five base64
  blocks at the existing RemoteContentView rewrite step; (b) Windows add an equivalent
  fetch-and-rewrite in `RemoteContentPanel` (host-gated, needs a Windows host to compile);
  (c) website drop the Google `<link>` and self-host byte-identical webfonts under an
  owner-approved `/fonts/*` route and CSP.
- Still owed after fonts thread: stage 4 (performance-engineer post-fix sample + cold-launch
  timings + telemetry honesty; knowledge-synthesizer lesson + guard proposal), then final ledger
  updates, commit, ask about pushing.

## 2026-08-01: Parity gate fixed (C198 / C173 / C189) — Windows + Linux now gate on unmeasured resolver

- Stage-3c parity report (Reports/D278-parity-report.md) returned FAIL: macOS gates on an
  unmeasurable resolver (D278), but Windows `AppViewModel.cs:394/:450/:716` fabricated `1.1.1.1`
  and Linux `ProphetViewModel.cs:34` / `MirrorViewModel.cs:39` / `WitnessViewModel.cs:108`
  fabricated `8.8.8.8`, then ran the audit. Owner approved "fix parity first".
- Fix landed on all six sites using the owner-approved gate string verbatim ("System resolver
  could not be determined. Check your network connection, then retry."), old fallback-warning
  strings removed. Windows views trim the trailing period on the wrap (ProphetView.xaml.cs:106,
  MirrorView.xaml.cs:87). Filed C198 (Linux); Windows sites were already C173/C189.
- Build: Linux Avalonia compiles clean (0 errors, macOS host). Windows XAML compile is host-gated;
  macOS Host + Linux runtime + Windows host real runs remain owed.

## 2026-08-01: Workstream 1.0 D278 fixed in code, verification pending

- `runProcess` deadlock (D278, macOS launch hang) fixed. Root cause: `done.signal()` reachable
  only from the cancellable timer work item; the fast path cancelled the timer then
  `done.wait()`ed unconditionally, so nothing ever signalled and the main actor blocked forever
  (3379/4144 samples in `semaphore_wait_trap`). The comment at :154-156 describing a "skip" was
  false; removed.
- Fix: `RunProcessState` (exited/drained semaphores), terminationHandler before `run()`,
  concurrent drain thread (pipe-buffer safety, C112 honored), bounded `exited.wait(timeout:)`
  result-as-status (macOS analog of `NetworkKeyState.cs:318`), SIGTERM→0.5s→SIGKILL→`waitUntilExit()`.
  Timeout/non-UTF-8 → nil, contract unchanged, all 10 callers untouched.
- `checkStartup()` probe now runs in `Task.detached` via `systemResolverProbe` seam, published by
  single writer `applySystemResolverProbe`; `resolverState` ∈ .checking/.measured/.unavailable;
  `server` starts `""`; resetAll keeps measured value; runLookup gate (new) blocks Prophet until
  measured (owner-approved copy). PortalView shows "Checking system DNS servers…" while probing.
- Tests: Kit 6/6 (`NetworkKeyStateRunProcessTests`), App 8/8 (`StartupResolverStateTests`), Debug
  + Release builds green. `outputs-catalog.csv` merge-conflict markers resolved + D278 rows added.
- Remaining: parity report (gui-cross-platform-continuity), post-fix sample + cold-launch
  timings, fresh-HOME guided-setup real run, performance-engineer + knowledge-synthesizer,
  DELTAS D278 status update, commit, push (owner-gated).

## Phase 3.2 G-1 updater (2026-08-01)

- Server signs appcasts with dedicated update key, not collector. DEV key under `Server/dev-keys/`
  when `SYXX_ALLOW_DEV_UPDATE_KEY=1`. Production: `SYXX_UPDATE_PRIVATE_KEY` (PKCS8 b64) or path.
- Clients pin `6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=` in binary.
- `publish_package` → atomic JSON per platform; `build_appcast` refuses incomplete (A264 closed
  for the signed path).
- Windows UpdatePanel authenticated only; Linux Settings Updates card; C# temp ext from type.
- Tests: server 14/14, macOS 19/19, Windows 19/19. Real publish/sign PASS.
- Gap: live install on Windows host not run; production key rotation owner-gated.

## 2026-08-01: Phase 2.2 Windows fail-closed remote-content

ContentWarp no longer throws on SSRF; returns typed WarpResult inconclusive (existing SSRF
reason string). RemoteUrlPolicy.AuthorizeForNavigation is the single Uri? gate for checkout,
trial, update, broadcast, remote-content, Dimensions, Welcome, About. Mutation: bypass gate →
18 red; restore ContentWarp throw → 19 red; restore correct → 70 green. WinUI compile still
blocked on this macOS host; Kit tests are the verified surface.

## 2026-08-01: Phase 1.2 server acceptance (16 red → 0)

All sixteen REIMPLEMENTATION-PLAN Phase 1.2 failures are green. Do not wholesale-replace
server modules from evening donors; current tree already held most of the evening code.
The live defects were narrow:

1. Fulfillment tests mocked `kv_get` but production reads `kv_get_with_created_at` after
   `kv_insert_once` loses — second delivery re-minted. Fix: fixture mocks the lease reader;
   production falls back to `kv_get` and fails closed on unknown state.
2. Unsigned Keygen `meta.valid=true` mapped missing headers to `__transient__`. A021 wants
   deny (`None` from `_validate_keygen`). Missing headers under a configured pubkey → False.
3. `_diff_result` A026 downgrade made cross-host injection inconclusive; acceptance wants
   tampered. Restored injected → tampered. Docstring already said injection is decisive.
4. DoH JSON all-upstream failure returned HTTP 502; acceptance pins Status:2 under 200.
5. leaktest start was POST-only; test GETs it. Added GET route.
6. `_body` required `headers.get_all`; unit fixtures use plain dict. Dict-safe fallback.
7. Scheduler tests called old 3-arg `monitor_create` and read column `conclusion`; API is
   4-arg with column `result`. Fixture fixed; production API unchanged.
8. Resolver AA and diff route fixtures omitted `do=` / `deadline=` kwargs after production
   grew them. Fixtures accept `**kw`; production DO-bit and deadline kept.
9. Fetcher pin spy returned endless body under A164 chunked read → TruncatedError. Spy
   returns EOF after first chunk.

`scripts/deploy.sh` already has macmotd/winmotd/debmotd + ACL renewal hook. No new DELTAS
filed from this pass. Full suite 371 OK.

## Standing user directives (permanent, not session notes)

These are absolute. They are restated here because they have each regressed at least once after
being corrected. Check them before writing output, not after.

- **NO EM-DASHES OR DASH SUBSTITUTES IN ANY OUTPUT TEXT** (`AGENTS.md:52`). This covers chat
  prose, spec documents, code comments, and user-facing strings. Rewrite as separate sentences,
  a dependent clause, a colon, or parentheses. Swapping in another separator is the same failure.
  Do not propose a user-facing string containing one. Raised by the user six or seven times across
  separate contexts, and stripped once already (see the 2026-07-15 entry, "all 52 em-dashes
  stripped"), so it has regressed since. **Scope, measured 2026-07-30 (DOCDELTAS G004):** the previous
  figure here was "four shipped strings in `SynthesisEngine.swift:728-743`". That was wrong twice: the
  file is 291 lines long so those lines do not exist, and the real tree-wide count is **1,564 lines in
  macOS Swift sources, 1,322 in Windows C# and XAML, and 92 in Linux C# and AXAML**. Reproduce with
  `grep -rn $'\u2014' macOS/Sources --include='*.swift' | wc -l`. Not all are shipped copy (many are
  comments and doc prose), but the directive covers comments explicitly, so both need clearing. At
  least one is live user-facing text: `Linux/Ubuntu/SyxxUI.Avalonia/Views/DashboardView.axaml:252`.
  Do not close this item after fixing a handful; a prose reminder has now failed seven times, so it
  needs a CI grep gate.
- **NO THIRD-PARTY TOOL NAMES ANYWHERE** (user directive, 2026-07-19, stated twice). DNSMonitor,
  FileMonitor, ProcessMonitor, and Objective-See are another person's products. Syxx neither owns
  them nor used their code. They must not appear in strings, comments, identifiers, or tests.
  Syxx's own term for the capture feature is **Capture Query Report**. Grep both platforms and
  `Server/` for all four before calling any related work done. **Status, measured 2026-07-30
  (DOCDELTAS G005): NOT zero, despite the 2026-07-19 entry below reporting a clean sweep.**
  `DNSMonitor`, `FileMonitor`, and `Objective-See` are genuinely at zero across macOS, Windows, and
  Server sources. `ProcessMonitor` survives as an identifier at
  `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:120, 124, 132` (`ProcessMonitorTick`). macOS and
  Server are clean; the Windows twin was missed.
- **MUTATION-PROVE EVERY TEST** (`AGENTS.md` Protocol §3). A comment describing a failure case is
  not evidence the test can fail. Run each mutation, observe RED, revert, observe green, and
  record the mutations in the changelog. Do it on both platforms when the change has a twin. A
  test whose mutation does not go red is invalid and gets rewritten.
- **VERIFY EXTERNAL CONSTANTS BEFORE HARDCODING THEM.** On 2026-07-19 the hostname
  `linmotd.syxx.app` was invented by extrapolating the `macmotd`/`winmotd` pattern and shipped as
  a default. It resolved via wildcard DNS, so a resolution check alone looked fine, but the
  production certificate SAN list covers only {syxx.app, www.syxx.app, macmotd.syxx.app,
  winmotd.syxx.app}. The user got a certificate impersonation warning on Syxx's own first screen.
  Before hardcoding any hostname, URL, endpoint, port, or API path, confirm resolution AND that
  the certificate covers the name (`openssl s_client -servername <host> -connect <host>:443`,
  then `openssl x509 -noout -ext subjectAltName`). A pattern that obviously extends is a
  hypothesis, not a fact.
- **THE WORD "VERDICT" IS NOT TO BE USED** (owner directive, 2026-07-30, expanding `AGENTS.md:54`).
  `AGENTS.md:54` states only "erase it", with no replacement, which is why the rename has been applied
  inconsistently across the three clients. The full rule is:
    1. In **code**, the replacement word is **result**.
    2. In **user-facing interfaces and documentation**, the replacement word is **conclusion**.
    3. **Exception:** code may use *conclusion* when it names a user-facing surface. `ConclusionsView`
       is correct in code precisely because it implements the user-facing Conclusion pane. The same
       applies to `Conclusion.swift` and `Conclusion.cs`, the model behind that pane.
  Three things about the current state, so the next sweep does not go wrong:
    - **macOS is already done and is the reference.** It renamed `VerdictAction.swift` to
      `ResultAction.swift` (`public struct ResultAction` at `:19`, `public enum ResultActions` at
      `:33`). Only two occurrences remain in macOS sources and both are comments
      (`WitnessSigner.swift:266`, `SyxxCLI.swift:94`).
    - **One file on Windows is the whole remaining code gap.**
      `Windows/dotNET/Syxx.Kit/Synthesis/VerdictAction.cs` still declares `record VerdictAction` and
      `static class VerdictActions`, and its header at `:7-8` cites
      `macOS/Sources/SyxxKit/Synthesis/VerdictAction.swift`, which no longer exists. It holds all 10
      remaining `Verdict` symbols in the Windows tree. Linux source has zero. Nothing consumes
      `VerdictAction` on Windows, which is a separate functional gap worth answering.
    - **DO NOT rename the signed wire field.** `verdict` is a key inside the signed witness payload
      at `evidence.comparisonResult.verdict`
      (`corpus/witness-crossplatform/witness-macos-answers.json`).
      `WitnessSigner.swift:19-20` states `evidence` is "the canonical, cross-platform byte-stable
      content the signature actually covers (E011)", so the key name is part of the signed bytes.
      Renaming it invalidates every previously issued snapshot and breaks cross-platform
      verification. It must ride the PARKED E020 `formatVersion` 2 to 3 flip (see the 2026-07-15
      entry), in ONE commit, on both platforms, with golden vectors in both versions.
  Earlier counts of "verdict" across the tree were inflated roughly 25x by scanning `bin/` and `obj/`
  build output. Exclude those directories or the problem looks far larger than it is.

## 2026-07-30: DOCDELTAS audit, and what the documents got wrong

A `DOCDELTAS.md` ledger now exists at the repo root: 27 findings across five domains (G doc-vs-code
drift, H cross-document contradiction, I terminology, J admitted-but-unfiled live defects, K
structural). Read it before trusting `DESIGN.md`, `ROADMAP.md`, or the older entries in this file.
The findings that change how you should read these documents:

- **`DESIGN.md` was wrong about the Windows engine.** `DESIGN.md:141-142` said Windows "still runs the
  retired DST engine ... do not extend it." Windows migrated: `Conclusion.cs:21` reads "Replaced by
  `Determination: DeterminativeResult?`" and `:51-64` are "thin wrappers over Determination, never
  DST fields." Corrected in this pass. The "do not extend it" instruction had been actively
  misdirecting work away from the live engine.
- **`DESIGN.md:159-234` specified three scorers that were deleted.** The B005 no/partial/full-data
  contracts govern `resolverProvenanceConfidence` and `tamperMechanismFingerprint`, which return zero
  grep hits, in a `Scorers.swift` that no longer exists. That section also contradicted
  `DESIGN.md:147` ("No confidence percentage exists") 40 lines later. Marked retired in this pass.
  The B003 independence decision that blocked B005 composition math is moot: the scorers are gone.
- **This file's em-dash admission was off by three orders of magnitude.** Lines 16-17 cited
  "Four shipped strings in `SynthesisEngine.swift:728-743`". That file is 291 lines long, so the
  citation is dead, and the real tree-wide figure is 1,564 lines in macOS Swift, 1,322 in Windows,
  and 92 in Linux. Corrected below.
- **Third-party names did not reach zero.** `ProcessMonitorTick` survives at
  `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs:120, 124, 132`. The other three names are
  genuinely absent. The sweep was real and reported as complete while one identifier remained.
- **One Critical live defect, untracked anywhere.** `DashboardView.swift:357-362` returns `"PASSED"`
  for the Mirror chip without ever reading `comparison.isRedirectionDetected`, while
  `MirrorView.swift:309` returns `"TAMPERING DETECTED"` from that same flag. Two shipped surfaces,
  same data, opposite answers, and the Dashboard is the one the user sees first. Filed as DOCDELTAS
  J001. `DELTAS.md` is empty (post-cycle), so nothing tracks it.
- **The root `Package.swift` reintroduced D079, and the parity test cannot see it.** A root build
  produces a `DeepSyxx` helper with `__info_plist` but no `__launchd_plist`, which `SMJobBless`
  rejects with `kSMErrorJobPlistNotFound`. Proven by `otool` on both binaries.
  `RootPackageParityTests.swift` has zero occurrences of `linkerSettings`, `exclude`, or `sectcreate`,
  so it compares targets and paths only and stays green. Write the failing test before fixing the
  manifest. Filed as J002.
- **`ROADMAP.md`'s PR-01 through PR-20 table has 20 dangling delta references.** The cited IDs either
  describe unrelated defects in `DELTAS-fixed.md` (B192 there is a Windows ECS leak-signal defect, not
  PR-01's Dashboard issue) or do not exist at all (D113, D114). PR-12 is missing entirely. Those 20
  rows are trustworthy as neither open nor closed. Filed as K003.
- **Reading-order hazard in this file.** The tail runs FORWARD in time from roughly line 1170, so the
  2026-07-24 macOS release-candidate plan, which contains the owner corrections governing current
  work, sits at the very BOTTOM rather than near the top. `CHANGELOG.md` has the same defect: three
  2026-07-23 and 07-24 entries sit below much older content, hiding the WS8 and WS9 sweep status.
  Filed as K001 and K002.

Verification posture for this audit: every finding carries `file:line` on both the document and the
code side, and each was confirmed by direct read or command output. Four of five dispatched subagents
returned empty results (the silent-stall pattern already recorded in this file), so the terminology and
doc-vs-code work was done directly rather than delegated. Do not assume a dispatched agent reported.

## 2026-07-30: agent library vendored, plus three traps in the agent plumbing

Infrastructure only. No product code, no ledger moves, no test changes.

**Trap 1: `Subagents/` was never in the repo.** It was a stray clone of
`github.com/ankitmundada/awesome-opencode-subagents` carrying its own `.git`, untracked by Syxx and
with no `.gitmodules` entry, so `git status` showed a bare `?? Subagents/` and a fresh clone got
nothing. `AGENTS.md:11` states the subagents are available within this repo at `./Subagents/`, which
was false for as long as that clone sat there. Fixed by removing the nested `.git` and committing the
tree (`788c5de`, 154 files). The nested history was expendable: its HEAD was the single upstream
commit `e516176`, and the `categories/` layout had already been flattened by hand locally. A copy of
the removed `.git` was parked under the opencode temp directory, which is not durable, so treat the
upstream link as gone.

**Trap 2: `install-agents.sh` silently reached for the network.** Line 19 set
`CATEGORIES_DIR="$SCRIPT_DIR/categories"`, but the ten numbered folders had been flattened to
`Subagents/` root, so `has_local_categories()` returned false and `select_source_mode()` switched to
remote mode without asking. Running it to "install the subagents" would have downloaded upstream
copies over the vendored ones. That is the same hazard `AGENTS.md:15` warns about when it says
editing subagents in place has produced corrupted files. Now `CATEGORIES_DIR="$SCRIPT_DIR"`, local
mode resolves, and the network is reached only on an explicit menu choice. If anyone ever restores a
`categories/` wrapper, that line has to move back.

**Trap 3: 137 of the 154 installed agents were untracked.** `.opencode/agents/` held all 137
definitions from `Subagents/` plus 17 Syxx-authored ones, but only the 17 were in git. Nothing
ignored them; they were simply never added. So a fresh checkout had 17 agents while `AGENTS.md`
requires subagent deployment for every task. Committed as `f8ec3ba`. A clone now gets 154.

**Committing in this tree needs `--only`.** The working tree currently carries 443 pre-staged files
from earlier sessions, and CHANGELOG.md records several entries left uncommitted by instruction. A
plain `git commit` sweeps all of it into your commit. Both commits above used
`git commit --only <path>` after proving the exact contents in a throwaway index
(`GIT_INDEX_FILE=... git read-tree HEAD && git add -- <path> && git diff --cached HEAD`). The staged
list was fingerprinted before and after each commit and was unchanged (443 entries, md5
`23c36b48bf433b04ec527cd58a3c0542`).

**One bad frontmatter file takes down the entire agent roster.** opencode's
`config/agent.ts:29` calls `ConfigParse.schema(...)`, which throws on a schema violation, and the
throw is not caught inside the scan loop, unlike `loadMode` which uses `Exit.isSuccess`. Unknown keys
are tolerated because the schema carries an index signature, so the current set is safe, but a
malformed file dropped into `.opencode/agents/` fails every agent, not just itself. The loader
globs `{agent,agents}/**/*.md`, so both directory spellings work and nesting is allowed.

**Current agent state, verified against the live roster rather than a test.** 154 files in
`.opencode/agents/`, of which 153 load as dispatchable subagents. `syxx-neural-lead` is absent from
that roster on purpose: it is `mode: primary`. The 137 vendored definitions are byte-identical to
their `Subagents/` sources. `~/.config/opencode/agents/` was resynced and now mirrors `Subagents/`
exactly at 137 files with zero gaps and zero byte divergence, so projects outside Syxx stop getting
different versions of `security-auditor`, `swift-expert`, `qa-expert`, and 37 others.

## 2026-07-30: desktop startup and LAN lifecycle

- `Legal/EULA/Syxx-EULA.txt` is the canonical desktop agreement. Acceptance is valid only for its
  exact version and SHA-256 digest and only after atomic persistence plus read-back.
- The onboarding state machine is loading, agreement required, setup required, ready, persistence
  failure, or state unknown. No shell or external startup service may run before ready.
- What’s New is the initial main-window destination for returning users and after guided setup.
- The typography roles are Cinzel display, Archivo UI/body, Cormorant SC deck, EB Garamond Italic
  emphasis, and JetBrains Mono technical. The type specimen is not a general design authority.
- The reopened LAN lifecycle records C139, C140, C141, C151, and C156 were fixed on both twins.
  Complete/partial/unreadable snapshots are validated, command execution is bounded, ARP state and
  MAC normalization are honest, duplicate bindings are explicit, and unexecuted service-probe
  promises are absent from production models.
- Current verification: Swift 661 with 5 expected skips and 0 failures; .NET core 286/286; Windows
  UI lifecycle 10/10; Linux Avalonia 52/52; macOS Debug and Release builds plus both app-bundle
  modes passed. Final artifacts are `macOS/Syxx.app` and `macOS/Syxx-2.0.0.dmg`.

## 2026-07-21 — GUI Warden Theme Migration (all four platforms)

**Scope:** Full rebrand of every GUI surface from "Io" dark-tech to "Warden" dark-fantasy theme.

**Design tokens (canonical):**
- Canvas: `#0b0518` (was `#130d26`)
- Violet primary: `#7c5cff` (was `#8b5cf6`)
- Teal brand: `#2dd4bf` (was not a primary color)
- Gold accent: `#d9a441` (was `#f0ac38`)
- Red critical: `#ff4d6d` (was `#f5405e`)
- Orange: `#f7931e`
- Fonts: Cinzel (display headings), EB Garamond (lore/flavor italic), system-ui (body), ui-monospace (code)

**Warden rune mapping:** Prophet `ᚨ` (teal), Mirror `ᛞ` (violet), Portal `��` (teal), Glutton `ᚦ` (red), Cage `ᚾ` (violet), Witness `ᛉ` (violet)

**Files changed per platform:**
- macOS: 8 Swift files (IoDesignSystem, ContentView, DashboardView, ProphetView, MirrorView, PortalView, GluttonView, CageView, WitnessView, ConclusionsView)
- Windows: 2 XAML files (IoDesignSystem.xaml, MainWindow.xaml)
- Linux: 11 AXAML files (App.axaml + 10 views with Io* -> Syx* resource migration)
- Web Server: 3 HTML files (index.html, purchase-success.html, purchase-cancel.html)

**Known gaps:**
- Cinzel/EB Garamond font files not yet bundled in macOS Resources (falling back to Georgia/serif)
- Whats-new HTML files still use Quicksand/gold-lavender palette (separate design system, embedded in desktop webviews)

## 2026-07-21 — WS10-02 (E053): culture-sensitive string/number handling eliminated

- **WS10-02 (E053) [done].** CRITICAL defect: two Ubuntu hosts with different locales could reach
  different verdicts. Fixed across 20+ files in `Windows/Syxx.Kit` (~80 individual sites):
  - `StringComparer.Ordinal` on all `OrderBy`/`ThenBy` comparers (6 files)
  - `StringComparison.Ordinal` on all `StartsWith`/`EndsWith`/`Contains`/`IndexOf`/`Replace` calls (12 files)
  - `CultureInfo.InvariantCulture` on all `ToString`/`string.Format`/`Convert` numeric renderings (10 files)
  - `.ToLowerInvariant()` replacing `.ToLower()` (1 file)
  - CA1305/CA1307/CA1310 enabled as ERRORS in `Syxx.Kit.csproj` to prevent recurrence
- Build verified: 0 culture-related errors, 0 CA1305/CA1307/CA1310 violations.
- Not yet landed: non-invariant locale CI leg (Fix 3 in E053).

## 2026-07-20 — Delta Evaluation and Cleanup Sweep

- Evaluated all deltas. Relocated 139 fixed deltas to `DELTAS-fixed.md` and 87 stale/retired deltas to `DELTAS-rejected.md`.
- Appended Antigravity evaluation comments to the remaining 227 kept deltas in `DELTAS.md`.
- Identified and added seven new confirmed deltas: **[D078]** (macOS TracerouteRunner Process Deadlock), **[B149]** (DoH Queries Omit DNSQuery Configuration Fields), **[B150]** (DenialOfExistenceProbe Fails to Set DO Bit), **[D079]** (macOS GUI UI-Thread Block via Synchronous Trial Anchor Sync), **[D080]** (WinUI 3 Cross-Thread Exception in AppViewModel Monitor Tick Handler), **[E052]** (Platform Parity: IPClassifier (macOS) Misses Site-Local fec0::/10 Check), and **[A131]** (Server Licensing Signature Verification UTF-8 Normalization/Loss Hazard), and **[D091]** (Windows WinUI 3 GUI Compilation Errors and Nullability Warnings). Appended Antigravity comments to each and reflowed the ledger.
- Alignment verified with `reflow-deltas.py` (0 diffs).

## 2026-07-19 — Phase A/B verified, A5/A6 Windows filled, Phase C landed (C1/C2/C5/C6/C7), names purged

Session on this Mac (SDK: Swift via Xcode/CLT, .NET 10.0.302 at `/usr/local/share/dotnet` — add to
PATH). Both suites green at session end: **macOS 564/564** (4 skipped), **Windows 512/512**.

**Phase A/B audit:** A1-A4 Kit twins present both platforms; A5 (ContinuousMonitor/MonitorScheduler)
+ A6 (macOS MenuBarExtra TrayIndicator) live on macOS. B1/B2 present both. ROADMAP D-3 "no status
item either platform" was STALE (macOS has it). A5/A6 Windows twins ADDED this session: AppViewModel
monitoring block + `Services/TrayIconService.cs` (Shell_NotifyIcon tray, severity-colored generated
`.ico`s, grey-never-green). VM build/launch is the bar for the Windows tray.

**Phase C — DONE this session (both platforms, mutation-proven, real-run verified):**
- **C1** router/gateway audit: `gatewayDhcpOffLinkDNS` evidence (private off-/24 non-gateway DHCP DNS
  = rogue shape; public ISP resolvers never flag). Windows `DetectGatewayAsync` built from scratch
  (registry `DhcpNameServer`). Found+fixed a real bug: macOS `defaultGatewayIP()` never matched
  because `route -n get default` INDENTS fields and the parser did an untrimmed hasPrefix — every Mac
  silently reported no gateway. `syxx key` prints the gateway line now.
- **C2** filtering verification: `FilteringProbe` (Pi-hole/NextDNS enforcement + direct-UDP & DoH
  bypass), `syxx filter [@resolver]`.
- **C5** home baseline: `HomeBaseline` on Trust primitives, `syxx sentinel --set-home`/`--home`
  ("differs from home in N ways"); no baseline is NEVER "matches".
- **C6** lookalike domains: `LookalikeDetector` (confusables skeleton + Damerau-Levenshtein vs 15
  brands), wired into the Capture Query Report; real captures carry the xn-- wire form which it decodes.
- **C7** per-app resolver attribution: `ResolverAttribution` joins process↔resolver (port-53 keyed,
  D063), new "PER-APP RESOLVER ATTRIBUTION" section in the capture report; empty baseline = unknown.
- **C3** speed benchmark was already `[done]` pre-session.

**Phase C — NOT done, gated (see ROADMAP annotations):**
- **C4** per-network resolver policies: OWNER-GATED — mutates system DNS (`networksetup` /
  `Set-DnsClientServerAddress`), needs elevation + an approved behavior/failsafe spec (captive-portal
  failsafe non-negotiable per DESIGN.md). Do NOT build unilaterally.
- **C8** scheduled digest: BLOCKED — A5 keeps only last-tick/last-change, no persisted history to
  summarize; the periodic trigger is G-series scheduler work. Needs a monitor-history store first.

**Phase D:** D0 observation domain is OWNER-GATED (operational cost); D1/D2 depend on D0; D3/D4/D5
unbuilt. **Phase E:** business/owner-gated.

**THIRD-PARTY NAMES PURGED (user directive, firm):** DNSMonitor / FileMonitor / ProcessMonitor /
Objective-See removed from ALL code, comments, identifiers, tests — zero occurrences remain. See
[[no-third-party-tool-names]]. `DNSMonitorLogParser` → `CaptureLogParser`. The capture feature is the
**Capture Query Report**.

**Every new test batch mutation-proven** per [[mutation-prove-every-test]] — matrices recorded in
CHANGELOG per feature. One first-pass test (C6 genuine-brand) was found NOT to bite due to
defense-in-depth; a redundant guard was removed so a single mutation reds it.

**Ph3.2 macOS datapoint completeness:** still an owed twin (macOS SectionEvidence defaults 1.0;
Windows computes N/M). C1's Portal catalog went 3→4 on Windows; macOS C1 is complete WITHOUT the
completeness machinery (evidence+weight+adapter all present) — the completeness-scaling gap PRE-DATES
this session and is orthogonal, tracked separately, not a C1 regression.

## 2026-07-18 — USER DECISION: .NET tree reverted to .NET 10 (supersedes the 07-15 "do NOT revert" rule)

The 2026-07-15 "HARD platform-target rules" section below is SUPERSEDED for .NET: the user ordered
the Windows and Linux targets reverted to the latest .NET 10 release.

**WHY (the durable rationale — do not re-litigate this):** net11 was a *preview* SDK, so shipping on it
would force every user to download a .NET 11 preview runtime **for zero functional gain**. Nothing in
Syxx needs a net11 language or runtime feature; the GUI, Kit, CLI and tests all build on net10, and the
WinUI launch fix turned out to be code (`Program.cs` bootstrap, DimensionService `ApplicationData`
removal), not a net11 artifact. Cost to the user: real. Benefit: none. **net10 is the ship target.**
Do not retarget to net11 — the 07-15 "do NOT revert" note refers to the *Win10-era SDK floor*
(`10.0.19041.0` / `10.0.17763.0`), which is still binding, NOT to the .NET major version.

Current state:

- All 4 projects target `net10.0` (SyxxUI: `net10.0-windows10.0.26100.0`, min `10.0.22000.0` unchanged).
- Both `global.json` files (root + `Windows/`) pin `10.0.100`, `rollForward: latestMinor`,
  `allowPrerelease: false`. Keep both in sync (resolver walks UP from cwd).
- WindowsAppSDK stays 2.2.0 (`Bootstrap.Initialize(0x00020002)` unchanged). The GUI BUILT on
  net10 + WinAppSDK 2.2.0 on 2026-07-14; the later launch fixes (explicit Main bootstrap,
  DimensionService `ApplicationData` removal) were code fixes, not net11 artifacts — but a real
  SyxxUI launch on net10 on the VM is the outstanding verification bar.
- `ci.yml`, `scripts/win-build.ps1` (also fixed stale 19041 exe path → 26100), and
  `Ubuntu/UBUNTU-PORT-PLAN.md` references updated net11 → net10.
- VERIFIED locally same day: user installed .NET SDK **10.0.302** at `/usr/local/share/dotnet`
  (NOT on default PATH — add `/usr/local/share/dotnet` to PATH in shells). `Syxx.Linux.slnf`
  Release+Debug 0 errors, 482/482 tests, linux-arm64 ELF publish OK, real `nslook example.com`
  run returned live A records. Fixed during verify: `SignedIncidentReportTests.cs:55` used the
  pre-static SynthesisEngine API and never compiled — now `Synthesizer.Conclude(...)` matching
  the macOS twin.
- This working copy has NO `.git` directory — git provenance commands do not work here. The
  07-15 notes about this Mac's toolchains were stale (fresh machine); Xcode install in progress
  as of 2026-07-18.
- The VM needs the .NET 10 SDK (arm64, release) — NOT the 11 preview — before the next build.
  Remaining bar there: SyxxUI.exe build + real launch on net10.

## 2026-07-17 — C1 Router/Gateway DNS audit (macOS engine wiring)

**Completed:**
- Wired `detectGateway()` into `NetworkStateReport.generateReport()` — `GatewayInfo` struct populated during every Portal/network assessment.
- Fixed `/sbin/ipconfig` → `/usr/sbin/ipconfig` path in `dhcpPushedDNS()` — macOS 26+ moved the binary.
- All 5 test fixtures updated for new `gateway:` parameter.
- macOS 524t/0f ✅ (0 failures, 4 skipped).
- Gateway real-run verified: `10.0.1.1` via `en5`, DHCP DNS `10.0.1.1` (matching — standard home router).

**Remaining for C1:**
- Wire gateway data into `SectionInputs` / evidence adapters for synthesis engine.
- Gateway hijack detection hypothesis (is the gateway a DNS server that doesn't match expected?).
- Windows twin (`NetworkKeyState.cs` equivalent).
- C1 integration tests with Protocol §3 failure cases.

**Ledgers updated:** CHANGELOG.md, MEMORY.md, outputs-catalog.csv (pending gateway output lines).

## 2026-07-16 — OpenCode session landed (committed by Claude) + test-suite audit

**OpenCode's work was UNCOMMITTED until today.** There are zero OpenCode-authored commits in
this repo; its work lived in the working tree and was committed by Claude at the user's request
as `535fa7f` (attributed in the message). Consequence for anyone reading history: OpenCode's
authorship is recorded in commit MESSAGES, not in `%an`.

**ATTRIBUTION GAP — do not assume:** no verifiable record exists of OpenCode work on 2026-07-15.
The 07-15 CHANGELOG entries below are the in-VM Claude session. Any OpenCode work from that day
was swept into the restore/merge commit `c096d03` (~101 modified + 222 added files) with no
attribution and cannot now be separated. If provenance for 07-15 matters, ask the user.

**OpenCode's 07-16 work (verified from the committed diff):**
- **macOS engine:** `RPNOccurrence.swift` authored (Ph7.2); Ph5.2 scorer wiring into
  `Scorers`/`SynthesisEngine`/`Synthesizer`/`Conclusion`; `WitnessSigner` (+84);
  `SVCBProbe` canonicalizer (+101, C018); `EDNS0Probe` + `DenialOfExistenceProbe` malformed
  consumers (B073/B071) — these closed owed-twins items 5, 7, 10.
- **macOS surfaces:** `MirrorView` B052 (`isDegraded` → amber, was falling through to green);
  `AppModel`/`DashboardView`/`WitnessView`; `SyxxCage` C037/C039.
- **Server:** `dns_authoritative.py`, `resolver.py` hardening.
- **Windows:** `DNSCaptureReportGenerator` returns a tri-state `ReportWriteResult`;
  `CageCommand`; **`LeakDetector.CheckTrustSet` extraction (D011)** — a genuinely
  behaviour-preserving refactor (`SignatureKeyTrusted` is `bool?` defaulting null) that
  replaced the HashSet tautologies with real true/false/null trust-set tests.
- Wrote its own CHANGELOG entry (C037/C039, 2026-07-16).

**What Claude changed in OpenCode's work — READ THIS BEFORE TRUSTING A ✓:**
- **Its Windows test project did not compile — 20 errors.** Its tasklist left "run tests and
  verify" unchecked, so every Windows ✓ was false. Root cause: **xunit's
  `Assert.DoesNotContain/NotEmpty/Null/NotNull/Contains` take NO message argument** (that is
  NUnit/MSTest style). Rewrote each to `Assert.True/False`, which do, so the diagnostics survived.
- **`Conclusion.DominantSection` was invented** — no doc mention, no macOS twin, absent from both
  Kits. Replaced with an equivalent assertion against real API (all PrivacyLeak-supporting
  evidence originates from Glutton), guarded with `Count>=2` because **`Assert.All` passes
  VACUOUSLY on an empty collection**. Mutation-proven red.

**Still outstanding in OpenCode's work (NOT fixed — they compile, so they are silent):**
- `ClassMargin_singleClassDominates` still wraps its margin assertion in a conditional; the
  invariant evaporates when referral does not fire.
- **Three assertions that cannot fail** (closed a finding without adding a fail state):
  `capturedAt.timeIntervalSince1970.isNaN` (never NaN), `XCTAssertNotNil(output.writeResult)`
  (writeResult is NON-optional), `Enum.IsDefined(...)` on a non-nullable enum.

**Test-suite audit (three lanes, findings verified against code):** the honesty spine is the
LEAST-tested code in the repo. Mutation-proven: four Server guards can be DELETED with a green
119/119 — deleting A024 made the server emit a cryptographically signed `leaked:false` on zero
observations. **`test_licensing.py:31` sets `SYXX_DEV_MODE=1` at MODULE IMPORT with no conftest,
so the ENTIRE suite runs in dev mode and fail-closed licensing is unreachable.** A macOS test
(`SynthesizerTests.swift:56-60`) *encodes* a false-clean as the contract: zero-evidence Mirror
asserted `.clean` (and `EvidenceAdapters.mirror` has no record-count guard). The D011 trust-set
tautology existed on BOTH platforms — parity maintained for a test that tested nothing.
`scripts/honesty-coverage-gate.py` (new) mechanises the orphaned-guard class; it is RED today.

**Verified green at hand-off:** Windows 356/356, macOS 447 (4 skipped by design), Server 119.

## 2026-07-15 (later still) — MASTERPLAN2 Windows side IMPLEMENTED (see CHANGELOG same date)

- Every MASTERPLAN2 item with Windows code in this VM is done: C013/C016/C017/C018, B073/B071
  consumers, Ph3.2 completeness (clean-support scaling is LIVE now — it always saw 1.0 before),
  Ph5.2 sub-scorers shipped on Conclusion, Ph6.1/6.2 signed decisive-act catalog wired into
  Conclude, Ph7.1 weight loader + RunWithWeights, Ph7.2 RPNOccurrence + SOD-v2 validator, S5.1
  inferred channel + datapoint profile (flagged), B085/E020/E021/B091/C031/WS3-A.3, F028, E044.
  321/321 tests; Debug+Release+GUI build green; live nslook real runs done in-VM (UDP:53 OPEN here).
- **TWO CROSS-PLATFORM FLIPS ARE PARKED, DO NOT FLIP ONE-SIDED:** (1) E020 witness signer stays
  on format v2 until the macOS VERIFIER accepts v3 (Windows verifier already accepts both);
  (2) `Config.EmitDatapointCompletenessProfile` stays false until macOS emits
  profile.datapoints.v1 identically. The full owed-macOS worklist (12 items) is in CHANGELOG.
- Copy-gated GUI items (witness trust surface, B097 ProphetView labels, B088 relabel, D044
  GluttonView verdict, D042/D043 license messages, LeakDetector reason strings) are NOT coded —
  wording proposed to the User 2026-07-15, awaiting approval.
- New deployment env var: `SYXX_COLLECTOR_TRUSTED_KEYS` (Ed25519 signer trust set, same
  owner-deliverable pattern as SYXX_COLLECTOR_PINS).
- UPDATE (same day, later): user authorized direct output writing (will edit via an HTML change
  log). All copy items SHIPPED: B097, E021 witness failure taxonomy, D044 collector verdict card,
  B088 CONFIDENCE relabel + verdict-colored + real Alerts, D042/D043 license messages, LeakDetector
  reasons. **GUI "looks terrible" root cause: `SyxxUI/Assets` is EMPTY — the Space Grotesk/Space
  Mono TTFs never shipped, all text fell back to default.** Font tokens now have Segoe UI
  Variable / Cascadia Mono fallback chains; drop the real TTFs into `Assets/Fonts/` (exact
  filenames in IoDesignSystem.xaml) and they take over automatically. macOS twins for the E021
  surface + D043 CLI messaging owed alongside the earlier list.

## 2026-07-15 (night) — VM network fact + first real run

- **DURABLE VM NETWORK FACT: outbound UDP:53 to EXTERNAL resolvers is blocked** from this VM
  (direct queries to 8.8.8.8 get no answer); only the UTM NAT gateway resolver
  **192.168.64.1** answers. Consequence: default `syxx nslook`/guided runs that fan out to
  1.1.1.1/8.8.8.8/9.9.9.9 spend minutes timing out — it LOOKS like a hang but is bounded
  timeouts. Multi-vantage probes (Mirror cross-vantage, canaries) will read
  inconclusive/degraded here until the host firewall/UTM NAT permits outbound UDP:53.
  User can likely clear this on the Mac side (AGENTS.md: pause-and-report firewall case).
- **First Protocol §2 real run on Windows PASSED:** `syxx nslook github.com A @192.168.64.1`
  → real A answer (140.82.114.3, TTL 46) via the production CLI entry point.
- REMEDIATION-MASTERPLAN2 Windows-side status: all VM-doable items IMPLEMENTED (see
  CHANGELOG 2026-07-15 entries); remaining bars are the full real-run sweep (blocked on the
  UDP:53 egress above for multi-vantage), the corpus runners (Stage0 corpus is Mac-side),
  and the two parked cross-platform flips (E020 v3 signer, datapoint completeness profile).

## 2026-07-15 (later, in-VM session) — build GREEN, GUI LAUNCHES [built on net11 at the time; TFM SUPERSEDED 07-18 → net10]

Claude Code now runs INSIDE the VM at `C:\Users\Syxx\Syxx-Windows` (the "run Claude Code in the VM"
pivot happened; no ssh, no file transfer). Facts that supersede earlier notes:

- **Windows App Runtime 2.2.0 arm64 IS installed** on the VM (the "only 1.5" note below is stale).
  `Bootstrap.Initialize(0x00020002)` works; bootstrap was NOT the remaining launch blocker.
- **The remaining launch blocker was `ApplicationData.Current`** in `SyxxUI/Services/DimensionService.cs`:
  packaged-only API in an unpackaged app → `0x80073D54` stowed → crash before `Activate()`, no window.
  Fixed with a JSON settings file at `%LOCALAPPDATA%\Syxx\dimension-settings.json`. RULE: SyxxUI is
  unpackaged (`WindowsPackageType=None`) — NEVER use `ApplicationData`, `Package.Current`, or
  `StoreContext` in SyxxUI or Syxx.Kit paths the GUI touches; use `%LOCALAPPDATA%\Syxx` files,
  registry, or DPAPI like WitnessKeyStore.
- **build.ps1 was silently broken** (MSB1003): its splat parameter was named `$args` (reserved
  automatic variable). Fixed (`$buildArgs`). If a future script "loses" its dotnet arguments, check this first.
- **SDK pinning gotcha:** the resolver walks UP from cwd, so `Windows/global.json` does not govern
  repo-root builds. A root-level `global.json` (same pin + rollForward latestMinor + allowPrerelease)
  now exists — keep both in sync.
- **Full status 2026-07-15 (TFM SUPERSEDED 07-18 → `net10.0`):** all 4 projects built Debug+Release
  arm64, 0 errors; tests 277/277; SyxxUI.exe real-run showed the main window in ~2s. The *result* stands;
  the TFM it was achieved on (`net11.0` / `net11.0-windows10.0.26100.0`) does not — the tree is on net10.
  WinAppSDK 2.2.0 restore was confirmed OK against the net11 preview TFM; on net10 it was already
  confirmed OK on 2026-07-14. **The equivalent green run on net10 is the outstanding bar.**
- Windows CS0029 trap fixed in `SyxxUI/Program.cs`: never name a lambda parameter `_` if the body
  uses `_ =` discards.

## 2026-07-15 — UBUNTU PORT STARTED

**USER DECISION (2026-07-15): the Ubuntu target is the DESKTOP environment family (GNOME, MATE, Cinnamon,
etc.), NOT Ubuntu Server.** "Ubuntu Server" in AGENTS.md refers to where the Python collector runs. This
settles the GUI framework: **Avalonia** (DE-agnostic, renders its own pixels, one identical look across all
flavors); GTK4/libadwaita is rejected (GNOME-only styling). The plan's Server-vs-Desktop fork is resolved.
**SUPERSEDED 2026-07-15 (user edited AGENTS.md:36): Linux target is now the "Mint Linux/Ubuntu/Debian"
FAMILY, binding, three-platform parity mandated.** Consequence: the .deb lane is PROMOTED to a v1
deliverable (Mint blocks snapd; a named platform cannot depend on a blocked channel). v1 packaging = snap
lite/full (Ubuntu primary) + .deb (Mint/Debian). Tier-5 bar gains a Mint (Cinnamon) install+launch leg.
AGENTS.md:56 "Ubuntu Server" and :216 two-platform parity wording are still stale (user edits pending).
Renderer is NOT a gate (Avalonia draws identically everywhere); the Mint deltas are packaging + plain
resolv.conf vs systemd-resolved, which WS-C2 handles anyway (tri-state inconclusive when undeterminable).

**PLAN COMPLETE (2026-07-15): `Ubuntu/UBUNTU-PORT-PLAN.md` (442 lines) written, adversarially audited, and
revised.** The workflow's reviser agent died silently (stall pattern), so Claude applied the critique's 7
gaps inline: (1) NEW WS-C6 Glutton Linux VPN detection (WireGuard reads "no VPN" -> false-secure; fix via
/sys/class/net + NM D-Bus, Unknown never No); (2) WS-C2 now names Prophet `DNSResolver.cs:247` as a
stub-resolver consumer + nslook non-loopback bar; (3) all 52 em-dashes stripped (AGENTS.md:52); (4) NEW
WS-B5 standing probe-layer 3-way parity test (real-capture replay, deliberate-divergence failure case);
(5) Fork 0.6 sequencing added to the Section-12 user gates; (6) WS-C2 second failure mechanism
(GatewayAddresses empty on Linux -> use /proc/net/route); (7) WS-C1 reconciles the already-Linux-honest
`EncryptedTransportProbe` (reachability) vs `EncryptedDNSStatus` (configuration). User decisions stamped:
Fork 0.1 DECIDED Desktop across DE flavors (GNOME/MATE/Cinnamon, cross-DE render bar), Fork 0.2 DECIDED
Avalonia, Fork 0.3 deferred post-v1 .deb lane (Mint/Debian ~1 day, Manjaro out of scope). Remaining user
gates live in plan Section 12 (exp-determinism A vs C, FIPS label, PROPOSED strings, webview descope,
color palette, AGENTS.md:56 wording).

User created `./Ubuntu` (currently empty) and ordered the port to begin, plan-first, AGENTS.md-compliant,
via syxx-neural agents. A 5-phase workflow (`wf_03e59798-cd7`) wrote
**`Ubuntu/UBUNTU-PORT-PLAN.md`**: 6 parallel researchers (core-reuse/3-way parity, platform glue, Cage
capture, GUI Avalonia, snap packaging, server+build/CI) -> engineer designs the 3-way golden-vector parity
contract (Swift = Windows .NET = Ubuntu .NET) -> planner writes the plan -> adversarial critique -> revise.
Settled direction: reuse `Syxx.Kit` on **.NET 10 on Ubuntu** (SUPERSEDED 07-18: was net11; the Linux
target reverted with the rest of the tree, and `Ubuntu/UBUNTU-PORT-PLAN.md` is already updated) (NOT a Rust rewrite), **Avalonia** GUI reusing
SyxxUI ViewModels, **snapcraft** with a lite (strict-confinement, 5 disciplines) vs full/classic (adds Cage
raw capture) split. Target VM: ubuntu-26.04-desktop-**arm64** in UTM. Open fork the plan must resolve:
AGENTS.md says "Ubuntu Server" but GUI>CLI rule + user's Desktop ISO imply a desktop client; plan recommends,
user decides. All plan strings marked PROPOSED pending user approval; no em-dashes.

## 2026-07-15 — Platform targets, .NET 11, WinUI launch fix, networking (session handoff)

> ⚠️ **The .NET parts of this section are SUPERSEDED — see the 2026-07-18 decision above.**
> The tree is on **net10**, deliberately. Do NOT "restore" net11 from the notes below.
> The Win10-era SDK floor and the macOS 26 rule below still stand.

**HARD platform-target rules. Do NOT revert to older versions (reverting these cost the user hours):**
- ~~All 4 Windows projects target **`net11.0`**~~ — **SUPERSEDED 07-18: all 4 target `net10.0`**
  (SyxxUI `net10.0-windows10.0.26100.0`). The min `10.0.22000.0` and the "NEVER the Win10-era
  `10.0.19041.0` / `10.0.17763.0`" floor are UNCHANGED and still binding.
- macOS deployment target must be **macOS 26 (Tahoe)**, NOT `.macOS(.v14)` / `.v15`. `macOS/Package.swift:7`
  still reads `.macOS(.v14)`; it needs macOS 26 (string form `.macOS("26.0")`). The build warns
  "targeting macOS 15, not 26." Left mid-fix this session. **(Still binding — not superseded.)**
- ~~`Windows/global.json` pins the **.NET 11 SDK**~~ — **SUPERSEDED 07-18:** both `global.json` files
  (root + `Windows/`) pin `10.0.100`, `rollForward: latestMinor`, `allowPrerelease: false`.

**.NET 11 status — HISTORICAL, REVERSED 07-18.** (Kept for the debugging context below, not as a target.)
Retargeted net10 to net11. Mac had `11.0.100-preview.6.26359.118` at
`/usr/local/share/dotnet` (Homebrew `dotnet` 10.0.301 shadows it on PATH: call
`/usr/local/share/dotnet/dotnet` or reorder PATH). **`Syxx.Kit` builds clean on net11: 0 errors**, only the
pre-existing CS8619 nullability and CA1416 Windows-only warnings. The VM had SDK 11 active
(`dotnet --version` = preview.6). **The VM now needs the .NET 10 SDK (arm64, release), NOT the 11 preview.**

**WinUI SyxxUI: RESOLVED. The GUI RUNS** (verified 2026-07-15, built in Visual Studio inside the VM on
**.NET 11**). At the time this was read as ".NET 11 was the unlock" — on net10 the toolchain kept fighting
the WinUI runtime; net11 + a real VS build/deploy launched it. **That causal read did not survive 07-18:**
the actual fixes were code (the `Program.cs` bootstrap `Main`, the DimensionService `ApplicationData`
removal), and the GUI had already BUILT on net10 + WinAppSDK 2.2.0 on 2026-07-14. net11 was
coincident, not causal. (A real SyxxUI *launch* on net10 on the VM is still the outstanding
verification bar — see the 07-18 section.) Root cause + fix that got it there:
the unpackaged WinUI 3 app's generated `Main` (`App.g.i.cs`) calls `Application.Start` with NO Windows App SDK
bootstrap init, so the loose .exe crashes at first XAML activation: `0xc000027b` (stowed exception) wrapping
`0x80073D54 APPMODEL_ERROR_NO_PACKAGE` ("process has no package identity"), faulting in `Microsoft.UI.Xaml.dll`.
**Identical on x64 AND arm64: architecture was a red herring** (large time sink). Fix applied:
`Windows/SyxxUI/Program.cs` (explicit `Main`: `Bootstrap.Initialize(0x00020002)` then `Application.Start`),
plus `<WindowsPackageType>None</WindowsPackageType>` and
`<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>` in the csproj.
Open risks to verify in VS: (a) `0x00020002` must match the WinAppSDK major.minor (csproj pins
**WindowsAppSDK 2.2.0**); (b) WinAppSDK 2.2.0 may not support a net11 *preview*, watch the restore;
(c) framework-dependent needs the matching Windows App Runtime installed, or go self-contained
(`WindowsAppSDKSelfContained=true`). The VM had only WinAppRuntime 1.5 arm64 installed, no 2.2.

**Networking (durable environment facts):** the Mac's uplink is an **AX88179A USB-Ethernet, exclusive-claim**
(host or guest, never both), so **bridged is impossible**: UTM **Shared NAT only**. The VM DOES have a routable
address on UTM's shared subnet **`192.168.64.x`** (host gateway `192.168.64.1` on `bridge100`), reachable from
the Mac. It is NOT at the Mac's LAN IP `10.0.1.210` (that is the Mac itself). Current VM =
**`192.168.64.5`** (`WIN-T7BPRK2676N`, user `Syxx`). Port 22 was timing out (sshd/firewall not yet set).
The VM is **throwaway** (about 1 week).

**Tooling (Mac, symlinked in `~/.local/bin`):** `syxx-stage` (rsync `Windows/` to
`~/Desktop/Shared/Syxx-Windows`), `syxx-push` (auto-detects the VM on 192.168.64.x, scp with key auth, no
password), `syxx-winbuild` (old utmctl VM build, obsolete now that VS is used).

**Workflow pivot:** stop remote utmctl builds. Build in **Visual Studio in the VM** for real errors. Best next
step under consideration: run Claude Code INSIDE the VM to edit and build the WinUI GUI directly (no file
transfer). If so, get the current net11 source into the VM first via the shared folder, since the VM copy at
`C:\Users\Syxx\Syxx-Windows` may be the stale net10 version.

## Standing loop rules (2026-07-14) — apply every tier

- **GUI > CLI (AGENTS.md line 95-97): the GUI is always first.** Every tier's build/verify bar is the
  GUI, not just `Syxx.Kit`: macOS `Syxx.app` via `macOS/bundle-app.sh debug` AND `release`; Windows
  `SyxxUI` built on the VM. Self-contained GUI honesty defects are prioritized, not deferred.
- **Always build DEBUG and RELEASE and the APP BUNDLE** (AGENTS.md 48-49). Not just `swift build`.
- **Real run beats green tests** (Protocol §2). UDP:53 is now OPEN and `cryptography` 49.0.0 is installed,
  so DNS/verdict/signing fixes MUST be verified with a real run (live `syxx nslook`/`mirror`/`leak`,
  real Ed25519 sign/verify), not just unit tests. Both-platform parity + mutation-proven failing tests.
- **Auto re-stage Windows after each tier:** run `scripts/stage-windows.sh` once a tier completes +
  verifies, so the VM shared folder `~/Desktop/Shared/Syxx-Windows/` always has the latest coherent tree.
  (SyxxUI was RE-ADDED to `Windows/Syxx.sln` on 2026-07-14 — the unified-binary work had dropped it, so
  `dotnet build Syxx.sln` was not building the Windows GUI at all.)
- **Checkpoint between tiers:** update `CHANGELOG.md` + `MEMORY.md` (+ `ROADMAP.md`/`DELTAS.md` as needed),
  then immediately continue. New defects found → `DELTAS.md`. Architecture change → `DESIGN.md`.
- **GUI-visible copy → propose for approval** (AGENTS.md line 42) before shipping the string.

### CONFIRMED on Windows VM (real `dotnet test`, 2026-07-14) — engine parity bug, MUST FIX in engine tier
`Syxx.Tests.SourceCorrelationTests.StrongCorrelatedGroup_IsCappedAtMaxWeightPerSource` (SyxxKitTests.cs:2261)
FAILS on Windows (256/257) with `cGroup(16) == cIndep(16)`; the identical macOS test
`testStrongCorrelatedGroup_isCappedAtMaxWeightPerSource` (SourceCorrelationTests.swift:99) PASSES.
- Root cause: NOT a stale test and NOT the removed maxWeightPerSource cap. The source-group MERGE code is
  byte-identical (SynthesisEngine.swift:177-205 ≡ .cs:133-165). The divergence is DOWNSTREAM in the cautious
  combination: Windows collapses correlated-vs-independent to EQUAL confidence where macOS distinguishes them
  (macOS rewards two independent corroborating signals above two correlated). This is a real Windows
  over/under-confidence parity defect (honesty §6), the "engine-lane parity gap left to reconcile" the
  unified-binary CHANGELOG flagged.
- Fix: do NOT soften/delete the test (§4 — it exposes a real gap). Reconcile the Windows cautious-combination
  so 2 independent supports beat 2 correlated, matching macOS. Reproduce with real `swift test` + `dotnet test`
  on both, pinpoint the combine() divergence, mutation-prove, re-run both suites. Blocked on Tier-1 releasing
  SynthesisEngine.cs (B050/B051 editing it now). Also owes a DELTAS.md entry. Does NOT block the GUI build.

## Progress 2026-07-14 — Tier 1 LANDED + Windows build pipeline built

- **Tier 1 DONE + verified both platforms.** B050 (expired RRSIG -> .degraded, B047 canary preserved),
  B051 (transport .blocked only with a live Do53 control; offline -> inconclusive), B070 (answer
  owner-name matching, real crafted-packet verified), A013 (atomic kv_insert_once mint, mutation-proven),
  Windows correlation-parity. **Correlation root cause: `Scorers.cs` used banker's rounding; macOS uses
  away-from-zero — fixed to `MidpointRounding.AwayFromZero`.** Verify: macOS `swift build`+tests green;
  Windows real VM run (offline) `Syxx.Kit` 0 errors, `Syxx.Tests` **262 passed / 0 failed** (SourceCorrelation 6/6).
- **FOLLOW-UPS owed:** B073/B071 is PARTIAL — the parser now records `ednsMalformed`/`authorityMalformed`,
  but the consumers `EDNS0Probe.swift:55-60` / `.cs:33-39` still label a malformed OPT "notSupported"; wire a
  one-line inconclusive guard in each (was outside the agent's file scope). Copy approval owed: B051's new
  offline `EncryptedTransport.detail` string + the 3 new Windows views' strings.
- **Windows build automation WORKS: `scripts/syxx-winbuild.sh`** (+ `~/.local/bin/syxx-winbuild`). Uses
  `utmctl` on the UTM "Windows" VM, tars `Windows/` (no artifacts), pushes, extracts, builds via a pushed
  `.cmd` (inline cmd paren-grouping through `utmctl exec` mangles), polls a `__SYXX_DONE__` marker (exec
  returns before a long build finishes), pulls the log. **The VM has NO internet** — restore uses the
  offline cache `C:\Users\JJ\.nuget\packages` via `NUGET_PACKAGES` (WindowsAppSDK 1.6.250228001 is cached there).
- **GUI now BUILDS (2026-07-14) — WindowsAppSDK 1.6 -> 2.2.0 migration DONE.** The XamlCompiler exit-1 was a
  WindowsAppSDK-1.6-vs-net10 mismatch (1.6's net472 XAML compiler can't process a net10 project). Fixed in
  `SyxxUI.csproj`: WindowsAppSDK **2.2.0**, Microsoft.Windows.SDK.BuildTools **10.0.26100.4654**,
  Microsoft.Web.WebView2 **1.0.3719.77**, removed the missing `<ApplicationIcon>`. WinUI 1.6->2.2 code breaks
  fixed across views: Window MinWidth/MinHeight removed (MainWindow.xaml); UniformGrid->Grid (DashboardView.xaml);
  duplicate Border.Background (ProphetView.xaml); WebView2 event init -> `EnsureCoreWebView2Async` + honest offline
  overlay (RemoteContentPanel.xaml/.cs); `IRelayCommand.Execute` not `.ExecuteAsync` (ConclusionsView.xaml.cs);
  `using Microsoft.UI.Xaml` (MirrorView.xaml.cs); `using SyxxUI.ViewModels` (UpdatePanel.xaml.cs); `xmlns:local`
  (GluttonView.xaml); `RecordRow` positional-record -> settable props (ProphetView.xaml.cs:64). Real VM build
  (offline, NUGET_PACKAGES=`C:\Users\JJ\.nuget\packages`): `SyxxUI -> SyxxUI.dll  Build succeeded  0 Error(s)`.
  FOLLOW-UPS: (1) `Windows/SyxxUI/Assets/` was NEVER committed — `syxx.ico` + any logos/images the views load at
  runtime are MISSING; add the folder. (2) "builds" != "runs" — a real launch of `SyxxUI.exe` on the VM desktop to
  see the views render (honest WitnessView tri-state, no green-on-inconclusive) is the true bar, not done. (3)
  `LeakProbeRow`/`CageRow` remain positional records (fine for now, OneWay-bound). (4) 8 build warnings (nullability
  + MainWindow.xaml.cs:113 switch-precedence) left to clean.

## Audit 2026-07-14 — REMEDIATION-MASTERPLAN.md verified against code (6-agent adversarial workflow)

**Verdict: the plan is PARTIALLY implemented. "It's done" was inaccurate.** The engine "verdict-shape"
phases + WS3-A collector signing landed for real (byte-parity, committed). The rest is gaps and scaffolds.
Some commits CLAIM to close deltas that are NOT fixed (B050/B051 in 5a5c156). There is a WORKING-TREE
REGRESSION: A013 atomic double-mint guard reverted to non-atomic (fulfillment.py:81-88). Nothing was
verified on real input (no UDP:53 in sandbox) — all findings are code-present/absent only.

### DONE (committed, byte-parity both platforms unless noted)
- **WS1 engine:** Ph3.1 cautious rule + lineage table; Ph4.1 class-set Bel confidence; Ph4.2 mechanism-class
  partition + concrete headline; Ph4.3 classMargin + within-class split; Ph5.1 cross-vantage discrepancy as
  scored DST mass (CDN=zero); Stage-5 headline/provenance(direct+unavailable)/within-class/answer-matrix;
  B098 TTL/type canonical coverage.
- **WS2:** WS2-A.1/A.2 (didFail truncated/servfail; ANCOUNT mismatch); WS2-A.4 **B072 only** (extended RCODE
  combine); WS2-B.1/B.2 union/degraded logic (partial).
- **WS3:** WS3-A.1 D057 leak base64; WS3-A.3 D003/D004/D011/A006 Kit-level pin/pubkey/trust-set (BUT GUIs
  never pass pins → inert in shipped app); E011/E019/E024 identity byte-parity.
- **WS5:** E015 Windows ConclusionsView exists (fail-closed Stage-5); Stage5 reasoning views fail-closed;
  advisories wired into GUI OverallVerdictView; macOS B097 ProphetView tri-state; RemoteContentView host allowlist.

### REMAINING — implementation queue (dependency order; file:line seams)
**Tier 1 — honesty-critical false verdicts + evidence integrity (do first):**
- WS1-Ph4.2 **B050**: expired/not-yet-valid RRSIG → `.degraded` not `.positive` (Synthesizer.swift:101-106 / .cs:109-114), without collapsing the B047 control-canary case.
- WS1-Ph4.2 **B051**: emit transport `.blocked` only when Do53 cleartext succeeds while DoH fails; else inconclusive (EncryptedTransportProbe.swift:57-58 / .cs; Synthesizer.swift:388-393 / .cs:511-515).
- WS2-A.3 **B070**: match answer owner-NAME to question/CNAME chain; reject off-name in-class RRs (DNSResolver.swift:770-797 / .cs:589-595). Fix the false "B069/B070" comment.
- WS2-A.4 **B073/B071**: EDNS-inconclusive marker (distinguish no-OPT vs malformed-OPT) + strict authority parse (DNSResolver both platforms).
- WS3-C.2 **B049/B053/C028/B063**: remove the HARDCODED green "Signature valid…no tampering" (WitnessView.swift:178-188); make witness/Glutton tri-state gated on signatureValid AND signatureKeyTrusted; not-run/expired/untrusted must not collapse to red or green. Add Windows witness trust surface (parity).
- WS4-A.1 **A013**: restore atomic compare-and-set mint (kv_insert_once) while preserving A019 retry-after-outage (fulfillment.py:81-88).

**Tier 2 — WS2-B/C evidence acquisition (precede WS1-Ph5 real-run; bring macOS to DoH parity):**
- WS2-B.2 B058/B059 LIKELY→CONFIRMED on union (poisoned baseline still surfaces redirection); B057 !=resolver filter; B056 SVCB caveat.
- WS2-B.3 B080 amp-timeout honesty (missing != low factor); B061 notADenial; C010 version.bind gating parity.
- WS2-B.4 DNSSEC: A030 DoH DO-bit strip; A042 RFC1982 wrap (DNSSECVerifier.swift:114-124 / .cs:128-130); B046 no-OPT/DO weight; B047 RRSIG-vs-local-clock; A036 verify-unsigned; server resolver.py _zone_is_signed.
- WS2-B.5 B084 ContentWarp CNAME-target; B074-B079 SVCB/CNAME/TTL edges.
- WS2-C.1 **B010 macOS DoH vantage parity** (VantageComparer.swift evaluate() has no dohOutcomes; UDP-only). Bring macOS to Windows parity.
- WS2-C.2 B025/C013 Windows ClassifyIPv6 ::ffff:0:0/96 mapped-private (IPClassifier.cs:73-95); C016/C017/C018.
- WS2-C.3 **C014 macOS DetectUnanimousTampering** (absent on macOS); C015 sharedProvider; D056 mixed-encryption ANY→secure (NetworkKeyState); A048/A051 warp.
- WS2-C.4 server A044 rcode-blind 'ok' (investigate.py:55), A039/A040/A045/A058/A059, E001/E008/E010/E014; commit the uncommitted A020 resolver.py change.
- WS1-Ph3.2 populate SectionEvidence.completeness with real N/M (currently always 1.0 → scaling inert); build the bake-off runner over Stage0 corpus.

**Tier 3 — WS1 later phases (Ph5.2/6/7 + Stage-5 remainder; Ph6 rides WS3-A):**
- Ph5.2 wire resolverProvenanceConfidence + tamperMechanismFingerprint into engine output (defined-but-dead: Scorers.swift:53/87 / .cs:55/92; only evidenceConfidence wired at SynthesisEngine.swift:395 / .cs:386).
- Ph6.1 actually WitnessSigner-sign DecisiveActCatalog membership+version (add signature to ValidatedDecisiveActs).
- Ph6.2 POPULATE ValidatedDecisiveActs at runtime (thread currentValidatedActs into synthesize; Windows discards it at SynthesisEngine.cs:91 "Phase-2 scaffold"); wire M4 gate; RRSIG-only DNSSEC quorum; structural NXDOMAIN rule.
- Ph7.1 make WeightRubric.getWeight a loader over a defaults table; make runWithWeights apply overrides (macOS no-op); add Windows runWithWeights twin (parity break); corpus reliability-report runner.
- Ph7.2 author RPNOccurrence.swift/.cs; populate Stage5 PriorityFactors.occurrence (schema v2).
- Ph S5.1 datapoint-level completeness retiring profile.sections.v0 (SynthesisEngine.swift:434 / .cs:422); populate the empty 'inferred' provenance channel.

**Tier 4 — WS3-B/C trust infrastructure:**
- WS3-A.2 COMMIT licensing fail-closed (HEAD still fail-open at licensing.py:130; working-tree has the fix).
- WS3-A.3 wire trustedPublicKeys + pinSet from BOTH GUIs into runCollectorTest (AppModel.swift:381-385, AppViewModel.cs:218-220) so D003/D011 engage in the shipped app.
- WS3-B.1a A024/C026/B030 liveness proof (empty collector must not sign leaked:false; leak.py:140-164); WS3-B.1c A034 bound leak_observations (db.py:272-282).
- WS3-B.2a A056 Keygen Date freshness (licensing.py:176-199); WS3-B.2b B085 reject future-dated snapshots (WitnessSigner.swift:130 / .cs:127).
- WS3-B.3 A110 systemd non-root user (deploy.sh:182); A109 no inline Environment= secrets (deploy.sh:190-199); A113 DER-vs-seed docstring; A114 rotation contract; A023 resolver_ip PII scoping (passivedns.py:18-20).
- WS3-C.1 B091 derive leak verdict from signed payload (not outer JSON); B092 DimensionClient pin+sig.
- WS3-C.3 C031 expected-resolver baseline in reconcileVerdict (LeakDetector.swift:292 / .cs:331) — user's own resolver must not read as a leak.
- WS3-C.4 E020 remove free-text errorMessage from signed bytes; E021 preserve expired/untrusted/invalid tri-state.
- WS3-C.5 licensing/key-hygiene grab-bag (D001/D021/D002/D006/D027/D030/D051/D052/D053/D066/B099/B100).

**Tier 5 — WS4 server hardening:**
- A: A013 (Tier 1); A025 per-subscription slot (fulfillment.py:97-98,116); A035/A084 stable idempotency key (app.py:478); A046 GET expand[] in query string (stripe_checkout.py:32-33,64); A047 don't mark minted on email failure; A032 trial over-cap eviction.
- B: A067/A088/A090/A100 SSRF pin the vetted sockaddr (resolver.py:244-245 query_doh; fetcher.py:94-95 — stop conn.host re-resolve); A063 out-of-bailiwick glue (resolver.py:426,432-434); A033 port allowlist (fetcher.py:83).
- C: A078/A098 guard question-section struct.unpack (resolver.py:114,329 → inconclusive not 500); A039 hostile DoH JSON; A038 DNSError → inconclusive not 'good' (app.py:846-854); A075 'signed'→dnskeyObserved (resolver.py:373,420).
- D: A027 race-winner certifies clean without authority (app.py:747-755,139-163); A012 AA geo-DNS false tamper; A092 warp cache final_url key (warp.py:79,109); A094 caveat.
- E: A005/A028/… absolute request deadline + connection cap (app.py:176,1028 ThreadingHTTPServer unbounded); A054 ratelimit truncation; A079/A091/A095 row caps on passive_dns/audit; A052 kv timestamp + purge; A031 beta_signups purge; A085 reactivation handler; A057 version compare ragged tuples (app.py:447-461).
- F: A105 readyz sqlite path leak (app.py:544-545); A112 silent truncation; A029 CSV formula-injection; A037 session-secret entropy (db.py:191-193); A104 broadcast scheme allowlist (broadcast.py:42); F030 test-harness stub leak.

**Tier 6 — WS5 client surfaces & platform safety:**
- A: B090 gate legacy verdict headline on Stage-5 isValid (ConclusionsView.swift:26 / .xaml.cs:34-41); B088 macOS trust green-on-threat (DashboardView.swift:239-251) + Windows relabel Confidence/populate Alerts; B052 MirrorView DEGRADED green (MirrorView.swift:272-276); B097 Windows ProphetView No+Unknown collapse (ProphetView.xaml.cs:28); B067/F013/F018/F019 macOS Dashboard green on '—'; B041/B042 CLI showConclusions advisories (SyxxCLI.swift:118-148).
- B: C037/C039 CLI 'Saved'/'erased' unconditional (SyxxCage.swift:183-186); C038 unparsedCount gates verdict; C042/D063 direction from port not QR bit (SyxxCage.swift:244); C019-C024/C043 capture UDP-only + coverage disclosure (DNSCapture.swift:249,265-266); D060 osascript apostrophe root injection (AppModel.swift:589); D065 O_NOFOLLOW; D067 bound pidCache (DNSCapture.swift:421).
- C: D045/D032-D036/D064 scheme+HTTPS allowlist before opening server URLs (BroadcastPanelView.swift:38-39, DashboardView.swift:39-40); F028 Windows cage NaN/Inf duration (CageCommand.cs:27,90); D026 SieveBlocklistLoader ://-path LFI (SieveBlocklistLoader.swift:24-32); D018/D019/D025 client SSRF re-bind.
- D: D042 license save-failure prints SUCCESS/Pro; D043 network-outage vs invalid key; D044 Windows GluttonView leak verdict; E044 WinUI sync-over-async freeze; hygiene cluster.
- NOTE: Windows WinUI (SyxxUI) cannot BUILD on this macOS host — Windows GUI (WS5-A/D xaml.cs) changes are a Windows-host verification bar. macOS SwiftUI + Syxx.Kit + Syxx console + Server are buildable/testable here.

### Keygen.sh + Stripe — NOT production-ready (diagnosis)
- **Keygen blocker:** with D040 fail-closed + a hardcoded non-empty KEYGEN_ACCOUNT_ID, a stock deploy DENIES every live validation — deploy.sh never provisions SYXX_KEYGEN_PUBLIC_KEY and never sets SYXX_DEV_MODE, so /api/auth/activate returns 402 for all real keys. A056 Date-replay unremediated.
- **Stripe blocker:** mint idempotency reverted to non-atomic (A013 double-mint); license email silently no-ops without SMTP (paid buyer gets no key); A046 expand[] in GET body → default_price fetch fails; never tested against a live Stripe endpoint.
- Production needs ~10 env artifacts (SYXX_KEYGEN_PUBLIC_KEY, KEYGEN_ACCOUNT/PRODUCT/POLICY + token, STRIPE_SECRET_KEY, STRIPE_WEBHOOK_SECRET, price/product IDs, SMTP host/creds) + the A013/A046 code fixes.
- licensing.py + fulfillment.py changes are currently working_tree (uncommitted).

Full per-item evidence: workflow wf_fcee90f0-c89 transcript (subagents/workflows/).


## Current session — 2026-07-13 (Ph4.3 Implemented — Class-Level Margin)

**Branch:** `feat/beta-signup-and-server-hardening` (uncommitted working tree)
**HEAD:** `5a5c156` — Ph4.1/4.2 class-set belief + mechanism partition
**Build state:** macOS 418t/0f ✅, Windows 255t/0f ✅ (Ph4.3 implemented), Server 97t/99 ✅

### Completed this session

| Item | Area | Status | Suite |
|------|------|--------|-------|
| Ph4.3 class-level margin (macOS) | SynthesisEngine.swift | ✅ Implemented | 418t/0f |
| Ph4.3 class-level margin (Windows) | SynthesisEngine.cs | ✅ Implemented | 255t/0f |
| Ph4.3 class-margin tests (macOS) | SynthesisEngineTests.swift | ✅ 2 new tests | 9/9 engine |
| Ph4.3 class-margin tests (Windows) | SynthesisEngineTests.cs | ✅ 2 new tests | 9/9 engine |
| B128 responseCode fix (macOS) | DNSResolver.swift:718 | ✅ Fixed | 418t/0f |
| Ph5.1 cross-vantage discrepancy mass (macOS) | SynthesisEngine.swift | ✅ Implemented | 418t/0f |
| Ph5.1 cross-vantage discrepancy mass (Windows) | SynthesisEngine.cs | ✅ Implemented | 257t/0f |
| Ph5.1 discrepancy tests (macOS) | SynthesisEngineTests.swift | ✅ 2 new tests | 11/11 engine |
| Ph5.1 discrepancy tests (Windows) | SynthesisEngineTests.cs | ✅ 2 new tests | 11/11 engine |
| CHANGELOG.md updated | ✅ | — | — |

### Key findings from this session

1. **Ph4.3 class-level margin implemented:** The margin computation now aggregates BetP per mechanism class (rewrite, block, transport, content, leak, host) using the Ph4.2 partition. Clean is treated as a standalone pseudo-class via `.unresolved` for comparison purposes. The referral trigger uses `classMargin` instead of per-hypothesis margin, so two close sibling hypotheses within the same class no longer self-trigger a thin-margin referral.

2. **B128 (responseCode bug) fixed:** macOS `DNSResolver.swift:718` — replaced `responseCode` with `combinedResponseCode` in the `answerCount == 0` branch. Windows twin was already correct.

3. **Ph5.1 cross-vantage discrepancy mass implemented:** The answer matrix comparison classification now contributes actual DST mass (not just a presentation-layer gate). A `.discrepancy` classification adds mass toward `{spoofing, interception_onPath}` scaled by corroborating reference vantages. `.benignSetVariation` contributes zero mass — multi-homed CDNs remain silent.

4. **Build confirmed:** macOS 418t/0f ✅, Windows 257t/0f ✅ — all tests pass, no regressions.

### Next steps after Ph5.1

**Next:** Ph5.2 (B005 sub-scorers: Resolver Provenance Confidence, Tamper Mechanism Fingerprint, Evidence Confidence Score).

### Next steps after Ph5.2

**Next:** Ph6.1 (DecisiveAct catalog + signing plumbing), Ph6.2 (fill ValidatedDecisiveActs + wire M4), Ph7.1/Ph7.2 (calibration), then Stage-5 remainder, then WS2-B/C, WS3-B, WS4, WS5.

**Uncommitted changeset:** 71+ files, ~28K insertions covering Ph3.1, Ph3.2, Ph4.1, Ph4.2, WS2-A fixes, WS3-A.1, server hardening, GUI/CLI improvements.

### Key findings from 2026-07-13 audit

1. **Server rate-limiter lockout/truncation (A054):** In `ratelimit.py`, resetting the timestamp on blocked requests causes an indefinite lockout for clients exceeding refill rates.
2. **Server SSRF mapped-address bypass (A055):** `fetcher.py`/`resolver.py` check public IP validity using `ipaddress` library, which fails to mark `::ffff:127.0.0.1` or `::ffff:169.254.169.254` as private/reserved, allowing SSRF.
3. **macOS WitnessView green banner bug (C028):** Green checkmark "Signature valid" banner is rendered unconditionally inside `showValidation` block, even when signature verification fails.
4. **macOS Privilege Escalation subprocess leak (D055):** Terminating the `osascript` wrapper when stopping packet capture does not kill the nested privileged `syxx-cage` process, which runs orphaned in the background.
5. **CLI Emoji Policy Violation (F023):** CLI tools output emojis in stdout (e.g. `✅`, `❌`, `⚠️`, `🚨`), violating the strict bracket preference project-wide.
6. **Server Keygen replay vulnerability (A056):** `_verify_validate_key_response` verifies signatures containing the `Date` header but never compares the date to system time, allowing replay attacks.
7. **Server version comparison math (A057):** The naive startup `vtuple` comparison causes false-positive updates (e.g. `1` vs `1.0.0`) and suppresses beta-channel updates.
8. **Authoritative DNS server IPv6 support missing (A058):** Custom DNS server only binds to IPv4 DGRAM sockets, failing to receive resolver queries over IPv6.
9. **Server recursive resolver IPv6 support missing (A059):** Sockets in the query path hardcode `AF_INET`, making the walker unable to query IPv6-only target nameservers.
10. **Windows DNS capture single-interface limit (E040):** The sniffer binds to the first non-loopback IP, making it unable to capture DNS queries on active VPN interfaces.
11. **Server DNSSEC observation early exit (A060):** DNSSEC lookup exits on the first non-error vantage response, which returns false-negative empty sets when the system resolver strips DNSSEC.
12. **DNSSEC verifier query error fallback (B083):** Verification returns `.unsigned` on `servFail`, `refused`, and `truncated` responses, reporting blocked queries as unsigned.

---

## Session — Remediation Master Plan execution (2026-07-13)

**Branch:** `feat/beta-signup-and-server-hardening`  
**HEAD:** `5a5c156` — Ph4.1/4.2 class-set belief + mechanism partition  
**Build/tests:** macOS 416/416 ✅, Windows 246/246 ✅, Server 97/99 ✅ (2 licensing test failures pending SYXX_DEV_MODE fix)

### Committed work (in order)

| Commit | WS | Scope |
|--------|-----|-------|
| `0b3fad5` | S0 | Pre-flight: commit uncommitted cluster (ProbePlan/ProbeRunner/DomainCanonical refactor + A019 licensing) |
| `c9d1c14` | WS2-A, WS3-A.1 | Parser hardening: didFail includes truncated/servFail/refused; ANCOUNT mismatch; off-class RR rejection; EDNS extended RCODE + BADVERS fix; Windows ParseName throws on unterminated names; leak collector signature standard base64 (D057) |
| `9f1b398` | WS1-Ph3.1 | Cautious combination rule (default .cautious, cap path removed); static evidence→cluster lineage table (authoritativeDiverges + authoritativeDivergesConfirmed) |
| `456b163` | WS1-Ph3.2-3.4 | Clean-support completeness scaling; count-based mass; degraded sections excluded from clean support |
| `5a5c156` | WS1-Ph4.1-4.2 | Class-set belief confidence (Bel(clean)/Bel(threatUnion)); mechanism-class partition (spoofing+interception_onPath→rewrite, interception_transport→transport, censorship→block, contentTampering→content, privacyLeak→leak, localCompromise→host) |

### Key status

- **WS2-A (wire-parser false-clean):** DONE — B065, B068-B073, A077, E018, B083 all implemented both platforms.
- **WS3-A.1 (leak signature):** DONE — D057: server emits standard base64, clients normalize url-safe/unpadded, round-trip test.
- **WS3-A.2 (licensing fail-closed):** Code done (licensing.py now requires SYXX_DEV_MODE=1 for keyless operation); tests need env var update.
- **WS1-Ph3:** DONE — rule flip, lineage table, clean-support completeness, degraded penalty.
- **WS1-Ph4:** Ph4.1/Ph4.2 done. **Ph4.3 is NEXT** — needs class-level margin calculation (margin between mechanism classes, not individual hypotheses). E003/E004 already fixed (confirmed this session).

### Known test gaps (per AGENTS §3 evaluation)

- No threat-corroboration boundary test
- No conflict-referral threshold test
- No margin-referral test
- No degraded + evidence interaction test
- No multi-platform golden-vector byte-parity test for confidence
- No incomplete-sections test

### Peer review findings (Big Pickle — incorporate into planning)

**WS2-A bug (must fix before WS1-Ph3/Ph4 real-run acceptance):**
- `macOS/Sources/SyxxKit/DNSResolver.swift:718` — the noData/answerCount==0 return path still uses `responseCode:` instead of `combinedResponseCode:`. This means a BADVERS packet with ANCOUNT=0 reads as noData (RCODE 0) instead of the combined BADVERS code. The bug is only in the early-return path for answerCount==0; the final return path was correctly updated. Windows line 540 is correct.

**WS2-A.3 — CLASS check done, owner-name validation (B070) still missing:**
Off-class RRs are rejected (cls != 1 guard), but answers whose owner-name does not match the question or CNAME chain are not yet validated. This is B070 and remains open.

**Ph4.3 — class-level margin calculation (NEXT implementation step):**
The current margin (line 310) computes the difference between top-two individual hypotheses. This means two hypotheses in the same mechanism class (e.g. spoofing 40% + interception_onPath 35% in rewrite class) trigger a thin-margin referral. Per the plan, margin should be computed between mechanism classes (rewrite 75%, clean 25% → margin 50%, no referral). Requirements:
1. Group hypotheses by mechanism class
2. Compute class-level belief/confidence
3. Compute margin between top two classes (not individual hypotheses)
4. Within-class subordinate split continues to show individual distribution

**F007 behavioral parity gap (LOW):**
Windows PromptDomain returns raw input on canonicalization failure; macOS rejects with empty string. Intentional divergence or a bug — not addressed in current WS scope.

**Test gaps (per AGENTS §3):**
SynthesisEngineTests.swift (7 tests) has basic happy-path coverage but is missing:
- No threat-corroboration gate test (positiveSupportGroups < 2)
- No conflict-referral threshold test
- No margin-referral test
- No degraded + evidence interaction test
- No multi-platform golden-vector byte-parity test
- No incomplete-sections test
- Weak boundary assertions (exact values vs range checks)

**Next priority after Ph4.3:**
Per the master plan execution order: after Ph4.3, continue WS1-Ph6 (probe robustness + DecisiveAct) then WS1-Ph7 (calibration), then Stage-5 remainder, then WS2-B/C, WS3-B, WS4, WS5.
13. **ContentWarp SSRF CNAME blocking (B084):** Resolving hostnames under SSRF checks maps and classifies CNAME target strings as `.invalid` IPs, blocking all CDN-backed URLs.
14. **Mixed-resolver encryption overclaiming (D056):** `encryptedDNSStatus` reports `.yes` (fully encrypted) when only one of multiple active system resolvers is encrypted.
15. **Collector signature encoding mismatch (D057):** The server signs and base64-encodes signatures using URL-safe unpadded Base64, causing clients to throw or return false-tamper.
16. **macOS WebView delegate lifetime bypass (D058):** Caching webviews without syncing delegates in `updateNSView` allows SwiftUI to silently detach the origin check.
17. **macOS Route Trace deadlock (E041):** `syxx-route` blocks on `waitUntilExit` before pipe consumption, deadlocking on large traceroute outputs and failing to stream.
18. **Server hardening audit (A061-A110):** Vetted and promoted 50 new server-side and script-level deltas covering DNS query validation bypasses, SSRF vulnerabilities, payment webhook concurrency races, rate-limiting leaks, database storage bloat, cryptographic verification gaps, and systemd deployment sandboxing.
19. **Offline trial launch reset (D059):** Client trial anchor resets when syncing offline launches to the server, resetting the trial and extending the trial time indefinitely.
20. **WPF UI thread deadlock (E044):** Synchronously blocking on `SyncTrialAnchorWithServer` async operations inside `IsFeatureUnlocked` deadlocks WPF's UI dispatcher.
21. **DNS telemetry ethernet padding (C029):** DNS telemetry packet sizes include ethernet trailing padding bytes, inflating amplification measurements.
22. **Witness snapshot future bypass (B085):** Verification permits future-dated snapshots to bypass max-age freshness checks.
23. **Witness view lacks keypair generation (E045):** The GUI lacks controls to generate witness signing keys, forcing CLI utility use.
24. **macOS captive portal redirect following (C030):** macOS captive portal probe follows redirects, downloading login pages and differing from the Windows redirect blocker.
25. **Windows process resolution regex failure (E046):** `netstat` output puts the protocol at start of line, causing regex `:{port} .*UDP` to fail all local UDP process lookups.
26. **Offline Pro paid customer lockout (D061):** Keygen network errors trigger fallback to expired trials, locking out paid Pro users when offline.
27. **Privileged capture command single-quote injection (D060):** User home paths containing single quotes inject or break privileged shell commands.

## Prior session — 2026-07-11 (Post-BOB: hardening wrap + GUI architect creation)

### Completed this session

| Item | Delta | Area | Status | Suite |
|------|-------|------|--------|-------|
| B005 design | WS2-05 | Score no/partial/full-data contracts spec'd in DESIGN.md | ✅ | DESIGN.md reviewed |
| F009 item 3 | WS5-03 | 4 macOS `Default*_HasExpectedValues` parity tests | ✅ | macOS 364/0 (+4) |
| F009 item 5 | WS5-03 | Clean-slate confirmed — zero residual test gates | ✅ | Both platforms inspected |
| Windows live | N/A | Full `dotnet restore + build + test` on Windows VM | ✅ | 216/0 ✅ |
| syxx-gui-architect | N/A | New agent at `.opencode/agents/syxx-gui-architect.md` | ✅ | Needs opencode restart to register |

### B005 — Score design spec

Added "Score-design invariants" section to DESIGN.md specifying zero-data → inconclusive,
partial-data → attenuated confidence (`score * k/N`), full-data → normal for three flagship
scores: Resolver Provenance Confidence, Tamper Mechanism Fingerprint, Evidence Confidence
Score. Each has a data-regime table with signal lists, minimum-corroboration rules, and
edge cases. Composition math deferred pending B003 independence resolution.

### F009 item 3 — macOS parity tests

Added 4 tests to `ProbeRunnerTests.swift`: `test_defaultProphet_HasExpectedValues`,
`test_defaultMirror_HasExpectedValues`, `test_defaultPortal_HasExpectedValues`,
`test_defaultGlutton_HasExpectedValues`. Mirrors Windows tests at SyxxKitTests.cs:2114-2147.
Mutation-proven: change budget default → test REDs.

### F009 item 5 — Clean-slate confirmed

Investigated both platforms for skipped/conditional/commented-out tests, FIXME/TODO/PENDING
markers, #if-gated tests, and [Ignore]/[Skip] attributes. Zero instances found beyond the
single intentional XCTSkip in the D003 live-TLS test (load-bearing, not a gap). Item was
already resolved by Phase 0 commit.

### Windows 216t/0f live verified

On 2026-07-11, ran `dotnet restore`, `dotnet build`, `dotnet test` from C:\ on Windows VM.
216 passed, 0 failed, 0 skipped. 59 pre-existing warnings (CS8619, CS8625, CA1416). Build
state upgraded from "cross-compile from macOS" to "live-verified on Windows."

### syxx-gui-architect agent created

New agent at `.opencode/agents/syxx-gui-architect.md` with mode: all. Covers UI/UX design,
mockup-to-code translation, English language artistry, CLI output design, HTML/CSS/JS,
and Io design system adherence. References all mockup files in Mockups-Web-Promos/ and Design/.
Has a view-status table for both macOS SyxxApp and Windows SyxxUI views. Needs opencode
restart to register as a callable subagent.

### Outputs-catalog.csv review (GUI-architect lens)

Reviewed all 50 strings. Found 3 issues:
1. Row 19 (Witness C006) uses "⚠️ Warning:" prefix while rows 20-21 (Prophet/Mirror) use just "⚠️" — inconsistency.
2. Row 36 (Warp inconclusive) has double space after ⚠️ emoji.
3. Row 39 (Warp secure) says "completely secure" — too strong for a security tool.
User indicated dislike of emojis; a bracket convention (`[SECURE]`, `[TAMPERED]`, etc.) may replace them.

### Remaining ROADMAP items

- **WS2-05 (B005)**: Design spec DONE. Composition math deferred to B003.
- **WS5-03 (F009)**: 2/5 DONE. Items 1 (B030 Windows leak seam) and 2 (D013 license clock) need Windows host.
- **WS6**: All 6 items open. User wants syxx-gui-architect to handle Cage Report design,
  ConclusionsView (E015), PortalView advisories, and dual-mode CLI narrative (Syx Lyin' Eyes).
  Narrative mode idea: two output modes — default tells a story with characters (Prophet, Mirror,
  etc.) and user-visible data woven into a narrative; alternate mode prints boring technical output.
  Needs opencode restart to invoke the GUI architect agent.

### Key decisions from user

- "Forget about commits. No one cares." — stacking uncommitted changes.
- "Code the outputs yourself for now" — output-approval gate lifted for implementation.
- Dislikes emojis in output — prefers bracket convention or clean text.
- Wants `syxx-gui-architect` agent to handle the narrative dual-mode concept.
- Emojis were used throughout the codebase despite AGENTS.md rule against them (only use if user requests). All existing emoji usage should be audited and replaced.

### Pre-existing context (carried from prior sessions)

- WS1 (Server trust): 3/3 DONE — A019 (Stripe idempotency), A020 (DNS question validation), A021 (Keygen signature verification). Server 97t/0f.
- WS2 (Detection efficacy): 4/5 DONE — B032 (tunnel→.unknown), C009 (exfil signals≥2/3), C006 (no silent fallback), B024 (Warp verdict labels). B005 design done.
- WS3 (Evidence integrity): 3/3 DONE — D003 (TLS pinning Critical), D004 (pubkey endpoint), D011 (trust-set keys).
- WS4 (Test quality): 4/4 DONE — B034, B035, B036, B039.
- WS5 (Efficiency): 2/3 (F006 ProbeRunner elapsed, F007 DomainCanonical IDNA). F009 2/5 done.
- WS6: 0/6 — net-new features pending.
- User-visible output strings are cataloged in outputs-catalog.csv (50 rows, 5 tools, 5 deltas).
- All work on `feat/beta-signup-and-server-hardening` branch, no pushes.

## §3 test audit + fix sweep — 2026-07-10 (COMPLETE for cleanly-committable items; 44 commits this session)

**Final state:** all writable audit items done + WS5 landed + both code defects + ledgers synced
(CHANGELOG + ROADMAP). Suites green: **macOS 335 / Windows 197 / server 69, 0 failures.** Later
additions beyond the list below: rubric weak-signal spec tests (`435cddd`), Windows TriState
tautology removed (`fd7136c`), FirstLaunchDate past-instant bound (`2949e9d`), ARP-parse fixture
(`d4a5f75`), `testProbabilities`/`testTwoWayTie` de-vacuoused (`c150bcd`/`4b10f0c`), the server
leak-binding + webhook-route tests (`e6ee3b3`/`cc684e3`), and **B009 false-secure guard both
platforms** (`47885df` — captive portal must degrade the Portal section, never read clean; B009 was
already fixed in code, now tested). Only uncommitted test files are pre-existing WS-coupled WIP
(AuthoritativeVantageTests/DimensionClientTests/ProbeRunnerTests — F009). NOTE surfaced during
work: a lone redirection canary among clean sections verdicts `.clean` while referring — worth a
synthesis-calibration review.


Audited ALL ~561 tests (6 read-only syxx-researcher agents) → 518 adequate / 20 weak / 11 invalid /
0 mocked-output. Then fixed the high-value gaps, EACH mutation-proven (broke the code, confirmed RED,
reverted via `cp` save/restore — never `git checkout` on dirty files). Committed this session:
- **2 code defects the audit exposed:** B037 (witness display-integrity guard now covers
  `resolverQueryOutcomes`, `655c435`) + B038 (DualStack A-timeout → inconclusive not ipv4Suppressed
  censorship signal, §1, `1bb87c7`) — both platforms, filed in DELTAS.md.
- **All Critical server security gaps (were untested → break ships green):** SSRF block, rate-limit
  denial, session-token forgery (in `test_server_hardening.py`), webhook-ROUTE signature enforcement
  (`cc684e3`, forged→400 never fulfilled), leak signed-payload↔displayed-resolver binding (`e6ee3b3`).
- **WS5 fully committed** (8 per-delta commits `266a4b7`..`40a8fe6` + tests): server hardening
  A001-A018 landed; suite 69 OK on HEAD.
- **Audit test fixes** (all mutation-proven): synthesis §6 zero-confidence + INVALID degraded
  (`b0857d4`), witness corrupt-signature + 2 tautologies removed (`e58bea9`), `parseIPUDPHeader`
  hostile-input rejection (`daee512`), TXID sequential-counter rejection both platforms (`e504755`),
  NDJSON field fidelity (`ecb62b2`), keygen round-trip (`0bba71f`), empty-pubkey parity (`fd1f230`),
  rubric calibration-spec vs magic-constant (`3c42486`), OpenResolver/ResolverLatency/ResponseSize
  boundary off-by-ones (`a7e346a`/`ff68de6`/`5f36822`), DNS-tunnel FLAGGED-not-present (`3d65daa`).

**Observed, NOT filed (uncertain):** a benign fixture co-domain came back flagged `suspicious` in the
exfil analyzer — possible false-positive, worth a researcher check.

**Audit backlog REMAINING — all blocked/coupled/low-value (why the sweep paused here):**
- Needs a production seam + a Windows host to verify: B030 (leak false-"no-leak") and D013 (license
  bypass) test seams — task #7. LeakDetector.cs / LicenseManager coupling + WinUI can't build on macOS.
- Coupled to uncommitted WS6: Windows `Default*_HasExpectedValues` (4 INVALID, need committed
  ProbeBudget), NetworkKeyState typed-outcome cluster (5 INVALID, needs a pure-core extraction).
- Dirty/coupled source files (defer test until their code commits): AuthoritativeVantage CDN branch,
  DimensionClient nil-groundtruth, LeakDetector.verify().
- Low-value polish: 4 remaining rubric-constant tests, TriState distinctness, LANCartographer/FirstLaunch.

## Current position — 2026-07-11 (HANDOFF-OPENCODE.md implementation — ALL 9 ITEMS DONE)

Work is driven by `HANDOFF-OPENCODE.md` (9 items OC-1–OC-9). Branch: `feat/beta-signup-and-server-hardening`.

**All 9 OC items implemented and verified (uncommitted working tree):**

| Item | Delta | Area | Status | Suite |
|------|-------|------|--------|-------|
| OC-1 | A019 | Stripe idempotency state machine | ✅ | Server 73/0 |
| OC-2 | A020 | DNS question match + source validation | ✅ | Server 73/0 |
| OC-3 | B032 | macOS tunnel→.unknown | ✅ | macOS 349/0 |
| OC-4 | C009 | Exfil single-signal + Beacon deg. input | ✅ | macOS 349/0 |
| OC-5 | C006 | No silent 8.8.8.8 fallback | ✅ | macOS 349/0 |
| OC-6 | B024 | Warp verdict-label consistency | ✅ | macOS 349/0 |
| OC-7 | B034 | Typed-outcome tests | ✅ | macOS 349/0 |
| OC-8 | B035 | Narrow test whitelist | ✅ | macOS 349/0 |
| OC-9 | B036 | Typed classify() resolverDidFail | ✅ | macOS 349/0 |

**Build status:**
- macOS: 349 tests, 0 failures ✅ (baseline 335)
- Server: 73 tests, 0 failures ✅ (baseline 69)
- Windows: 0 errors (dotnet build) — structural parity changes only ✅

**Key decisions during implementation:**
- OC-1: KV state machine (`minted`/`processing`/`failed`) rather than `kv_delete` (no such API exists). Residual double-mint race from lost HTTP response after Keygen success is documented — Keygen-side dedup is the real mitigation.
- OC-2: `socket.connect()` on UDP for kernel-filtered source validation (simpler than manual `recvfrom` source check). Mirror of client KDNS-02 pattern.
- OC-3: Windows `EncryptedDNSStatus()` has no tunnel heuristic — intentional parity gap noted.
- OC-6: "⚠️ SKIPPED (proxy unreachable)" proposed wording — awaiting user sign-off per output-approval rules.
- OC-9: `resolverDidFail` param default false maintains backward compat for existing tests.

**Remaining risks:**
- `LeakDetector.swift` reverted to committed HEAD (WS6 uncommitted code referenced `CollectorPinning`/`CollectorPinningDelegate` which don't exist). WS6 author must re-add those types before committing WS6.
- `AppModel.swift` is dirty (WS6-fused) — intentionally kept uncommitted.
- Windows `SyxxUI` cannot build on macOS host (E015 blocks compile).
- 4 output-wording items pending user sign-off (ROADMAP.md Open Questions).

**Pre-existing uncommitted WS6 files left untouched:** `ProbePlan`, `ProbeBudget`, `ProbeRunner`, `DimensionClient`, `DomainCanonical` (both platforms), `WitnessKeyManager.swift`, `WitnessKeyStore.cs`, `AuthoritativeVantage` plural.

## Goal
Implement `HANDOFF-OPENCODE.md` (9 items OC-1–OC-9): server hardening (A019/A020), client detection
fixes (B032/C009/C006/B024), and test-quality fixes (B034/B035/B036) — each with mutation-proven
regressions. Then update ledgers (CHANGELOG.md, MEMORY.md, ROADMAP.md, DELTAS.md).
- macOS: `macOS/` — Swift 6, SwiftPM. Build: `cd macOS && swift build`. Test: `swift test`.
- Windows: `Windows/` — C# / .NET. `dotnet build Windows/Syxx.sln` (dotnet 10 installed 2026-07-04).
- Server: `Server/` — Python 3.11+. Deploy via `scripts/deploy.sh`.

## Build status — read the qualifier, not the checkmark
Tests detect errors; they never certify trust. A "pass" below means "no error surfaced by
the checks we actually ran," NOT "verified / production-ready." State what was run, not a verdict.

- Server (RE-VERIFIED 2026-07-11, from `Server/`):
  - All 17 `syxx_server/*.py` modules import cleanly (imports resolve, not just `py_compile`).
  - `python3 -m unittest discover -s tests -t .` → 73 tests, OK (payments, dimension, diff,
    server_hardening, routes, leak). A019/A020 hardening applied and covered.
  - CAVEAT: `cryptography` (the one runtime dep, per `requirements.txt`) is NOT installed in
    this env. `leak.py` defers that import, so modules load, but the Ed25519 leak-signing path
    is UNEXERCISED here and would raise RuntimeError at runtime without the dep. Not "green".
- macOS: last recorded `swift test` = 312 tests, **0 failures** (SYN-201 resolved by C005
  ProbeRunner `group.next()` fix) — 2026-07-09.
- Windows: last recorded `dotnet test Syxx.sln` = 173/173, 19 CA1416 warnings (expected),
   2026-07-09.

## Design patterns already established (match these for consistency)
- DNS typed status: `DNSResponseStatus` enum + `DNSResponse` struct (records + status +
  flags + errorMessage). `didFail` = timeout/transport/malformed/idMismatch (NOT nxDomain/noData).
  macOS: `DNSResolver.queryUDPDetailed` / `queryDoHDetailed` return `DNSResponse`;
  `queryUDP`/`queryDoH` keep `[DNSRecord]` for back-compat.
- Tri-state vantage: `ComparisonResult` has `isRedirectionDetected`, `isInconclusive`,
  `isDegraded`. Consensus logic (M10/W01): redirection only when local answer set is
  DISJOINT from the union of public resolver answers (CDN/geo partial overlap = OK), OR
  local returns a blocking/private IP, OR local empty while public non-empty.
- IP classification: `IPClassifier.classify(_) -> IPAddressClass` + `isBlockingAnswer(_)`.
  macOS uses `inet_pton`. Categories: publicRoutable, privateUse, loopback, linkLocal,
  carrierGradeNAT, documentation, multicast, unspecified, reserved, invalid.
- Continuation safety (M06): `ContinuationGate` (NSLock one-shot) guards NWConnection
  handlers from resuming a checked continuation twice.
- GUI: All discipline views bind to `model.sectionInputs.*` (SectionInputs struct); no
  hardcoded IPs, verdicts, or license state. Undiscovered features show honest "not yet
  available in GUI" notes instead of fabricated demo data.

## Status ledger
Legend: [x] done+built, [~] in progress, [ ] todo, [C] appears handled by concurrent agent (verify).

### COMPLETED THIS SESSION
- [x] **C005 Phase 1 — ProbeBudget + ProbeRunner infrastructure**: Added `ProbeBudget` and `ProbeRunner` to both Kits (4 new files: `ProbeBudget.swift`, `ProbeRunner.swift`, `ProbeBudget.cs`, `ProbeRunner.cs`). Added 10 macOS unit tests (`ProbeRunnerTests.swift`) and 14 Windows unit tests (`ProbeRunnerTests` class in `SyxxKitTests.cs`). Build: macOS 312/312, Windows 173/173 — all tests pass, 0 failures.
- [x] **C005 Phase 1.5 — Windows GUI probe gap fix**: Wired 9 missing probes into `Windows/SyxxUI/ViewModels/AppViewModel.cs:RunProphetAsync()`. Windows GUI now runs 11/13 Prophet probes (was 2/13). Remaining 2 probes (StubEntropy via capture, AuthoritativeComparison via Pro) are GUI-level deferrals — no Kit-level gap. Build: macOS 312/312, Windows 173/173 — all tests pass, 0 failures. **C005 delta closed.**
- [x] **C005 Phase 2a — macOS CLI ProbeRunner migration**: Migrated all four section-runner functions in `macOS/Sources/SyxxCLI/SyxxCLI.swift` to wrap every individual probe call in `ProbeRunner.run(label:budget:)`. Added `static var defaultProphet`/`defaultMirror`/`defaultPortal`/`defaultGlutton` computed factory properties to `ProbeBudget.swift`. Throwing probes use `do/catch` inside closure + `.flatMap { $0 }` double-optional flattening. `LANCartographer.snapshot()` (synchronous) and `traceroute` subprocess left unwrapped. Output text is identical (no user-visible string changes). Build: macOS 312/312, Windows 173/173 — all tests pass, 0 failures. See CHANGELOG.md for full list of probes migrated.
- [x] **C005 Phase 2b — macOS App ProbeRunner migration**: Migrated all 5 section runners in `macOS/Sources/SyxxApp/AppModel.swift` (`runLookup`, `runCompare`, `runNetworkState`, `runLeak`, `runWitness`) to wrap every probe call in `ProbeRunner.run`. Throwing `VantageComparer` uses flatMap pattern. `@Sendable` closures capture local copies of `@MainActor` properties. Build: macOS 312/312, Windows 173/173.
- [x] **C005 Phase 2c — Windows CLI ProbeRunner migration**: Migrated `Windows/Syxx/Program.cs` section runners (`RunProphetAsync`, `RunMirrorAsync`, `RunPortalAsync`, `RunGluttonAsync`) to `ProbeRunner.RunAsync<T>`. Windows `T : class` constraint leaves value types (DNSSECStatus enum, LicenseManager tuple) unwrapped. Throwing probes use closure null-return pattern. Build: Windows 173/173.
- [x] **C005 Phase 2d — Windows GUI ProbeRunner migration**: Migrated `Windows/SyxxUI/ViewModels/AppViewModel.cs` section runners (`RunProphetAsync`, `RunMirrorAsync`, `RunPortalAsync`, `RunGluttonAsync`) to `ProbeRunner.RunAsync<T>`. RunMirrorAsync and RunPortalAsync expanded to full probe suites in Phase 3. Build: Windows 173/173.
- [x] **C005 Phase 3 — Shared ProbePlan extraction**: Created `macOS/Sources/SyxxKit/ProbePlan.swift` and `Windows/Syxx.Kit/ProbePlan.cs` with `runProphet`/`runMirror`/`runPortal`/`runGlutton` static methods that return `SectionInputs` with section fields filled. Refactored macOS App (`AppModel.swift`) and Windows GUI (`AppViewModel.cs`) to delegate section runners to ProbePlan. CLIs keep own loops (per-probe progress output). Build: macOS 312/312, Windows 173/173. See CHANGELOG.md for full description.
- [x] **Server Delta Analysis**: Evaluated and documented 50 new detailed Server Deltas (`SRV-051` to `SRV-100`) inside `DELTAS.md`. Verified that all server tests continue to pass clean.
- [x] **GUI inputs wiring (GUIB-05/GUIB-19)**: Wired `runLeak()`, `startRealCapture()`, and `runWitness()` / `witnessTestify()` in `AppModel.swift` to populate their respective `sectionInputs.leak`, `sectionInputs.exfil`, and `sectionInputs.drifts` fields so `Synthesizer` accumulates real data.
- [x] **Synthesis Weighting Calibration**: Implemented centralized LLR-based `WeightRubric` using a unified `EvidenceId` enum on both macOS and Windows. Removed `max(0, ...)` clamps in the `SynthesisEngine` accumulator to support negative weights (dampeners) for captive portals and encrypted DNS.
- [x] **KDNS-11 (Process timeout SIGKILL)**: Added POSIX `kill(..., SIGKILL)` after a 0.5s grace period following SIGTERM inside `NetworkKeyState.runProcess` on macOS. Fixed `SyxxKitTests` compilation and signatures.
- [x] **CUR-001 (QR Bit Validation)**: Reject packets whose QR bit does not mark a response (QR=0). Implemented in macOS `DNSResolver.swift` and Windows `DNSResolver.cs`. Added `testParseResponsePacketDetailedRejectsQueryQR0` and `ParseResponsePacketDetailedRejectsQueryQR0` unit tests.
- [x] **CUR-002 (resolveSecurely Safety)**: Re-typed public `resolveSecurely` / `ResolveSecurelyAsync` wrappers to return `SecureResolutionResponse`, and handles local loopback DNS proxies as inconclusive rather than secure UDP paths. Made `isDNSEncrypted` / `IsDNSEncrypted` return false on loopback.
- [x] **CUR-003 (Windows Truncation Parity)**: Included `DNSResponseStatus.Truncated` in Windows `ResolverQueryOutcome.DidFail` in `VantageComparer.cs`.
- [x] **CUR-004 (macOS Degraded Warning Parity)**: Aligned comparison advisory verdicts on macOS CLIs (`SyxxCLI.swift` and `SyxxMirror.swift`) to print a degraded baseline warning when `isDegraded == true` (one or more public resolvers failed to respond), matching the Windows launcher.
- [x] **CUR-005 (Preserve Typed DNS Status in Option A)**: Changed macOS launcher Option A/Step 1 (`SyxxCLI.swift` `runProphet`) to use `queryUDPDetailed` to preserve and display specific typed statuses (NXDOMAIN, SERVFAIL, REFUSED, etc.) instead of collapsing to general empty/timeout errors.
- [x] **CUR-006 (syxx-route Behavior Alignment)**: Updated introductory output in both `syxx-route` targets (`SyxxRoute.swift` on macOS, `Program.cs` on Windows) to print "Traversing route path hops..." instead of claiming active anomaly detection or route shifts during raw traceroute hop traversal.
- [x] **Workstream E — Server hardening (28 SRV fixes across 13 files)**: db.py (7), app.py (7),
      dns_authoritative.py (2), licensing.py (2), warp.py (3), config.py (2), mailer.py (2),
      ratelimit.py (2), logging_util.py (1), leak.py (1), investigate.py (2), fetcher.py (1).
      All compile clean. CHANGELOG.md updated.
- [x] **Workstream A — macOS GUI honesty**: MirrorView (S0 — real ComparisonResult + WarpResult
      binding), GluttonView (S0 — real LicenseManager + portalReport + leak probe results),
      ProphetView (real sectionInputs.resolution + dnssec), ConclusionsView (sectionInputs-based
      status checks), PortalView (real activeInterfaces, honest "not available" for LAN/traceroute).
      Added `DNSRecord.typeName` to SyxxKit. macOS build passes. CHANGELOG.md updated.

### macOS — remaining
- [ ] M11-M16 NetworkKeyState.swift — scutil timeout, scoped resolver parse, iface flags,
      typed VPN/captive/encrypted states with evidence. (file was WIP at session start)
- [ ] M17 SyxxCLI/SyxxWarp accountID hard-coded → move to config/build setting.
- [ ] M18-M22 LicenseManager.swift — Keychain storage, typed persistence errors, signed
      trial state, salted fingerprint hash, safe URL + timeout.
- [ ] M23 LeakDetector.swift — collector-confirmed w/ signed one-time token; default sys/VPN resolvers.
- [x] M24 DNSSEC — honest-presence path taken, BOTH PLATFORMS at parity (verified 2026-07-08).
      macOS `DNSSECVerifier.swift:39-64` / Windows `DNSSECVerifier.cs:29-52`: `DNSSECStatus` has NO
      "validated/protected" case; strongest positive is `recordsPresentUnverified`; doc-comment states
      no RRSIG-over-RRset crypto is performed. Both add an RRSIG validity-window check (KDNS-05):
      `SyxxKit/DNSSECVerifier.swift:110-120`, `Syxx.Kit/DNSSECVerifier.cs:111-121`. Back-compat
      `verifyDNSSEC()->Bool` / `VerifyDNSSECAsync` (`.swift:70-73`, `.cs:55-59`) is presence-only and
      must never feed a "secure" verdict. Canonical ledger already in sync: ROADMAP.md:282 `[done: safer
      labeling]` (covers W17/M24). REMAINING (future, proposal A2 = ROADMAP.md:250 CF-08): real chain
      validation — DS→DNSKEY→RRSIG to a root anchor, or trust a validating resolver's AD flag as the
      only "validated" oracle. Until then, no caller may render this as "authenticated".
- [ ] M25-M28 DNSCapture.swift — full packet ctx (answers, resolver IPs, ports, direction),
      cached/API attribution, honest unknown process, SecStaticCode signing IDs.
- [ ] M29,M30 SyxxCages/main.swift — parse IPv4 IHL + IPv6; stream JSON lines to disk.
- [C?] M31,M32 ContentWarp.swift — tri-state + HTML parser (concurrent agent touched 11:36 — VERIFY).
- [x] traceroute wrappers — target validation + argv (no shell) + timeout in place both platforms
      (verified 2026-07-08); canonical ledger already in sync: ROADMAP.md:411 `[done: traceroute]`
      (covers W15/W16 + macOS). macOS `SyxxRoute.swift:66-72` `isSafeTraceTarget`: rejects empty/
      leading-`-` (arg injection)/whitespace + charset allowlist; runs `Process` with an argv array
      (`SyxxRoute.swift:24-27`, no shell string); 20s watchdog terminates (`:35-40`). Windows
      `SyxxRoute/Program.cs:86-93` `IsSafeTraceTarget`: `IPAddress.TryParse` OR `Uri.CheckHostName ==
      Dns`, rejects leading-`-`/whitespace; `UseShellExecute=false` (`:36`); 45s deadline + kill-tree
      (`:54,75`). RESIDUAL (candidate delta — un-filed, see report): macOS allowlist (`:70-71`) is
      looser than the Windows IP/DNS parser (permits stray `:`/`.` a real hostname wouldn't) — non-
      exploitable today because argv (not a shell) is used, but tighten to a real IPv4/IPv6/RFC-1123
      parser for parity. No fuzz tests yet (P4) for `;`/`$()`/`|`/backtick payloads.
- [ ] P3 tests — new SyxxKitTests target (add to Package.swift).
- [ ] Workstream B — macOS Kit DNS core (KDNS: wire-format validation, transport safety, QR-bit).
- [x] Workstream C — macOS + Windows Kit Synthesis engine (KSYN) + weighting calibration. BOTH
      platforms now run the full Dempster–Shafer engine with:
      - Covered-threats clean support (`coveredThreats(_:)` at macOS SynthesisEngine.swift:90-99,
        Windows SynthesisEngine.cs:52-61)
      - Source-group discount for correlated signals (macOS:116-134, Windows:83-95)
      - 0-confidence on inconclusive verdict (macOS:223-226, Windows:203-204)
      - Threat name floor (macOS:63, Windows:18) so a real signal isn't washed out by clean sections
      - Clean name floor (both platforms)
      - Conflict tracking and referral cutoffs (75%/30-points)
      - Zero-mass on empty input (no 100% baseline)
      Weighted LLR rubric via `WeightRubric` on both platforms. DST engine verified committed in
      both `SynthesisEngine.swift` and `SynthesisEngine.cs`.
      REMAINING (all platforms): numeric calibration of `cleanSupportPerSection`/floors vs. the 7
      probe scenarios from the scenario suite.
- [ ] Workstream D — macOS Kit Trust/Forensics/LAN/Licensing/Content (KTRU).
- [ ] Workstream H — New diagnostic datapoints + features from PROPOSALS.md.

### Windows (now buildable with dotnet 10)
- [ ] Workstream F — Windows parity (WDN/WCL/WSK).

### Server
- [~] Workstream E — All SRV-* fixes applied; imports resolve + 40 unittest tests pass (2026-07-08).
      NOT "ready for deploy": no runtime smoke against a live listener, and the `cryptography`-backed
      leak-signing path is unexercised here. Gate deploy on those, not on compile/import success.

## Verification commands
- macOS build:  `cd /Users/masterrabbit/Software/Syxx/macOS && swift build`
- macOS test:   `cd /Users/masterrabbit/Software/Syxx/macOS && swift test`
- Windows build:`dotnet build /Users/masterrabbit/Software/Syxx/Windows/Syxx.sln`

---
## Handoff — 2026-07-08 (feat/beta-signup-and-server-hardening)

### Current branch state
- **Branch:** `feat/beta-signup-and-server-hardening`
- **Three new commits at tip:**
  - `8a213f8` — Server Ed25519 migration (leak.py + test_leak.py + requirements.txt)
  - `0f9f28c` — leak verify route error contract fix (pre-existing bug: `isinstance(result, str)` never matched)
  - `0a474e2` — Import time missing at `app.py` top level
- **Working tree (corrected 2026-07-09):** the Ed25519 migration is COMMITTED (`bedc0c9`) and fully drained. Remaining WIP: DS engine rework (CF-03-gated), Ccage/SyxxCage capture (Chunk B), resolver hardening + zero-records-inconclusive (Claude, awaiting suite run), reserved AppModel wording hunk, untracked docs.
- **macOS build:** 1/296 tests fail (`testInterception_encryptedBlockedPlusUnencryptedPlusSvcbStripped`)
- **Windows build:** 0 errors, 19 CA1416 warnings; 157 tests / 0 failures (2026-07-08 baseline; the earlier "156" figure was stale)

### Claude's lane (Server + macOS SyxxKit/CLI)
- Server Ed25519 migration: fully committed (`8a213f8`)
- macOS + Windows client Ed25519: COMMITTED (`bedc0c9`, verified drained from the working tree 2026-07-09)
- macOS Synthesis: DS engine + `coveredThreats` model exist in the UNCOMMITTED working tree only, CF-03-gated
- SYN-201: structural frame-split (see below), deliberately parked — no code

### Four investigative/design tasks completed this session

#### ~~Task 1: Windows synthesis engine audit — softmax vs DS gap~~ (RESOLVED 2026-07-09)
This finding is now **stale**. Both macOS `SynthesisEngine.swift` and Windows `SynthesisEngine.cs` run
full Dempster–Shafer engines. Verified by direct read of both files on 2026-07-09:
- Windows `SynthesisEngine.cs:8-9` explicitly states: *"Dempster-Shafer theory so zero-data produces
  ~0% confidence."*
- Source-group discount at `:83-95`
- Covered-threats clean support at `:130-139`
- 0-confidence on inconclusive at `:203-204`
- Full DST combination with conflict tracking at `:240-257`
No softmax, no 100% baseline, no Inconclusive-as-class.
**DELTAS.md [B002]** status: SUPERSEDED (DST design structurally addresses it on both platforms).
**ROADMAP.md CF-03** status: **Done both platforms**. Remaining numeric calibration is common to
both (SYN-201).

#### Task 2: SYN-201 frame-split design draft (no code — deliberate)
**Defect:** `testInterception_encryptedBlockedPlusUnencryptedPlusSvcbStripped` at SynthesisScenarioTests.swift:70 expects `.interception` but gets `.contentTampering`.

**Root cause:** The flat `Interception` hypothesis conflates two distinct mechanisms:
- **Transport-level interception** (masquerading proxy, forced cleartext) — detected by `encryptedDnsBlocked`, `dnsUnencrypted`, `svcbStripped`
- **On-path interception** (transparent DNS proxy, redirecting resolver) — detected by `redirection`, `nxCanaryAtypical/Hijack`, `dnssecCanaryStripped`, `authoritativeDiverges`, `configChange`, `nxSubdomainHijack`, `denialBare/Injected`, `resolverBannerAnomaly`, `ttlLockstep`

The failure occurs because `coveredThreats(.mirror)` covers `{spoofing, censorship, interception, contentTampering}`. When Mirror runs clean (negative), its clean support dampens ALL four — including both `contentTampering` (0.6 from svcbStripped) and `interception` (1.2 from svcbStripped + 0.6 from encryptedDnsBlocked + 0.6 from dnsUnencrypted). With Portal also dampening `interception` on clean runs (coveredThreats(.portal) = `{interception}`), and `contentTampering` only being covered by Mirror, the weaker contentTampering signal survives while the transport-interception stack gets dampened by Portal + Mirror + Witness clean support.

**Proposed design:**
1. Split `Hypothesis.interception` → `Hypothesis.transportInterception` + `Hypothesis.onPathInterception`
2. Update `coveredThreats`:
   - `.portal` → covers `.transportInterception` only (it detects forced cleartext, encrypted-DNS blocking)
   - `.prophet` → covers `.onPathInterception` + `.spoofing` + `.censorship` (prophet probes detect resolver-level interception, not transport)
   - `.mirror` → covers `.onPathInterception` + `.contentTampering` + `.spoofing` + `.censorship`
   - `.witness` → covers `.onPathInterception` + `.spoofing`
3. Re-map evidence weights:
   - `encryptedDnsBlocked` → drops from `interception` 0.6 to `transportInterception` 0.6
   - `dnsUnencrypted` → drops from `interception` 0.6 to `transportInterception` 0.6
   - `svcbStripped` → drops from `interception` 1.2 to `transportInterception` 1.2, keeps `contentTampering` 0.6
   - All other existing `interception` mapped evidence → `onPathInterception`
4. Update user-facing labels:
   - `transportInterception` → "TLS/Transport interception (forced cleartext)"
   - `onPathInterception` → "DNS interception (transparent proxy)" (keep existing)
5. Update `severity` mapping: `transportInterception` → `.warning`, `onPathInterception` → `.warning` (both same bucket as current)

**Open question:** Should `encryptedDnsBlocked` get a lower weight on `transportInterception` (0.4 instead of 0.6)? It's a strong signal of a middlebox blocking DoH, which IS a form of transport-level meddling. Keep at 0.6 unless calibration shows otherwise.

#### Task 3: Witness Ed25519 migration WIP inventory

**macOS (uncommitted, in working tree):**
| File | Change Summary | Tests Affected | Risk |
|------|---------------|----------------|------|
| `SyxxKit/WitnessSigner.swift` | HMAC→Ed25519 via `CryptoKit.Curve25519`; deterministic signing with epoch-millis+JSON+keyId; `VerifySnapshot` uses embedded publicKey | 3 existing updated, 2 new | Low — backward-incompatible snapshot format (old HMAC sigs can't be verified by new code) |
| `SyxxKit/LeakDetector.swift` | `LeakCheckResult` + `CollectiveAttestation` gain publicKey/keyId/signedPayload/signatureValid; `verify()` method added | leak tests updated | Low — same contract change |
| `Tests/SyxxKitTests.swift` | Witness tests: generate keypair → sign → verify with embedded key; B017 regression test; min-2-vantage test; leak verifier tests | N/A (is tests) | Low |
| `Sources/SyxxWitness/main.swift` | CLI: `--gen-keypair` flag; verify no longer takes `--key` | N/A | Low |

**Windows (uncommitted, in working tree):**
| File | Change Summary | Tests Affected | Risk |
|------|---------------|----------------|------|
| `Syxx.Kit/WitnessSigner.cs` | HMAC→Ed25519 via `NSec.Cryptography`; `GenerateKeyPair()` exported; `VerifySnapshot` takes embedded publicKey | 5 existing updated, 2 new | Medium — **new dependency**: `NSec.Cryptography` NuGet must be resolved. Not yet verified to build with the dep. |
| `Syxx.Kit/LeakDetector.cs` | `LeakTestResult` gains publicKey/keyId/signedPayload/signatureValid; `Verify()` static method | Existing + B017 regression + min-2-vantage tests | Medium |
| `Syxx.Tests/SyxxKitTests.cs` | Same test pattern as macOS: keypair → sign → verify; B017 regression; min-2-vantage guard | N/A | Low |
| `SyxxWitness/Program.cs` | CLI parity with macOS: `--gen-keypair`; verify without `--key` | N/A | Low |

**Key design decisions (both platforms):**
- `KeyId`: STRUCK 2026-07-09 — the claimed macOS/Windows mismatch never existed. Both platforms emit the first 8 bytes of SHA-256(publicKey) as 16 hex chars (`WitnessSigner.swift:60`; `WitnessSigner.cs:78`). Re-refuted after an external audit re-filed it as critical.
- Deterministic payload serialization: macOS uses `JSONEncoder` with `.sortedKeys` + `.withoutEscapingSlashes`; Windows uses `JsonSerializerOptions` with `WriteIndented=false`. Both encode epoch-milliseconds, not ISO-8601 text.
- Old HMAC-signed snapshots: DECIDED (a) — documented breaking change; shipped in `bedc0c9` with the real failure mode recorded in CHANGELOG.
- Windows `NSec.Cryptography`: RESOLVED — declared at `Syxx.Kit.csproj:8` (26.4.0), committed in `bedc0c9`.

#### Task 4: CF-02 libpcap sanity check
- **Ccage** builds clean (0.15s): `macOS/Sources/Ccage/` — Ccage.c (30 lines) + Ccage.h (31 lines) + module.modulemap (5 lines)
- **No Ccage unit tests exist** — zero test files found. CODEFIX CF-02 is the Ccage → SyxxCage wiring; the implementation is a thin C wrapper around libpcap.
- **SyxxCage.swift** was refactored in working tree: removed `#if canImport(Darwin)` conditional, made `CaptureWriter` a private actor, positional duration argument, `--keep` semantic cleanup
- **INCONCLUSIVE behavior on zero-records:** The `cage_pcap_next_ex` loop (SyxxCage.swift:105) handles returns: `1` (packet), `0` (timeout, continues loop), `-1` (error, prints warning + breaks), `-2` (broken loop, breaks). On zero records, the loop runs for `duration` time and terminates normally. The exfil section's adapter receives `[]` (empty list) which maps to `SectionStatus.negative` — meaning Capture ran clean, not INCONCLUSIVE. Whether this is desirable depends on the CF-02 requirement: a capture with 0 packets and no reason is "clean" (no exfil detected), which is technically correct. If the requirement is that zero-packet captures should be INCONCLUSIVE (can't rule out what was never seen), this would need a status change.
- **Build adds `linkerSettings: [.linkedLibrary("pcap")]`** in Package.swift:77 — links system libpcap (ships with macOS, no external dep needed).

### Open questions for Claude
1. SYN-201: Frame-split naming — `transportInterception`+`onPathInterception` vs. alternative names? Should `authoritativeDivergesConfirmed` (A2 Pro, weight 3.0) remain on `onPathInterception` or also get weight on `transportInterception`?
2. STRUCK 2026-07-09: the KeyId formats were already identical (see key-design-decisions above); this was never a real question.
3. CLOSED: HMAC compat decided (a) — documented breaking change, shipped in `bedc0c9`.
4. REVERSED 2026-07-09 on a verified false premise. The D2 finding claimed "empty exfil array → negative → falsely clean" was the zero-records behavior. FALSE: the real capture bridge `MonitorReportAnalyzer.exfilAssessments` returns `nil` (→ Cage `.untested`, which hard-gates clean) when no queries were seen — its doc comment says exactly "returns nil ... so Cage stays untested rather than falsely clean." An empty `[]` array legitimately means "ran, nothing suspicious" = clean (the `testFullyCleanRun` convention: `exfil = []  // ran, nothing suspicious`). The user-approved `[]`→inconclusive change broke 2 correct tests (testFullyCleanRun, testLatencyIsQualityOnly) and was reverted. REAL (narrower) bug found + reported, NOT fixed: the GUI live path `AppModel.swift:385-386` builds `domains.map{...}` = `[]` on zero packets instead of `nil`, so a zero-packet GUI capture reads as a clean section rather than untested — asymmetric with MonitorReportAnalyzer. Fix belongs in AppModel (guard empty→nil); user's call (reserved file).
5. CLOSED: `NSec.Cryptography` declared at `Syxx.Kit.csproj:8`, committed in `bedc0c9`.

## Notes
- Do NOT commit/push unless the user asks.
- `.build/` artifacts are noisy in git status; filter with `grep -v .build/`.

---
## Comprehensive Codebase Analysis — 2026-07-09 (Delta Sweep + Pipeline Verification)

### What was accomplished
1. **Full codebase audit**: Read and verified all critical files across macOS, Windows, and Server.
   Confirmed: 312/312 macOS tests, 173/173 Windows tests pass.
2. **Conclusion pipeline verified complete**: The User's Delta of the Week #1 pipeline is FULLY
   IMPLEMENTED on both platforms:
   - Step 1 (Run tests): ✅ Section runners via ProbePlan
   - Step 2a (Apply reasoning): ✅ WeightRubric per-(evidenceId, hypothesis) signed LLR weights
   - Step 2b (Relationships): ✅ DST engine combines correlated signals with source-group discount
   - Step 2c (Weighing): ✅ DST additive mass with dampeners
   - Step 2d (Conclusion): ✅ Verdict via Dempster-Shafer with covered-threats clean support
   - Step 2e (Confidence): ✅ BetP(verdict) × 100, 0 on inconclusive
   - Step 2f (Probability): ✅ Pignistic distribution per hypothesis
   - Step 2g (Explanation): ✅ SynthesisReport.renderText() / RenderText() with all fields
   - Step 3 (Reddit referral): ✅ 75% / 30-point cutoffs, referral with prefilled post
   - Step 4 (Report): ✅ SynthesisReport with data, statistics, queries, next steps, Reddit copy
3. **Data pipeline verified**: Probes → SectionInputs → Synthesizer.conclude() → SynthesisEngine →
   Conclusion → SynthesisReport → renderText() — all wired on both platforms.
4. **WeightRubric verified against research**: All values match the research document's recommended
   tiers. No divergence found between macOS and Windows WeightRubric tables.
5. **Stale claims corrected in memory_persist**: Windows SynthesisEngine is full DST (not softmax).
   Verified by direct read of SynthesisEngine.cs:8-9 which states "Dempster-Shafer theory."
6. **10 new deltas filed in DELTAS.md**: B029 (EvidenceItem.source unset), B030 (LeakDetector
   boolean leak), C006 (8.8.8.8 fallback without consent), C007 (single-responder authoritative
   acceptance), C008 (Warp ✅ before ⚠️), D014 (GluttonView trusting bool), D015/D016 (Ed25519
   key in @Published), E012 (Console encoding), E013 (IPClassifier 198.18.0.0/15), E014
   (EncryptedDNSStatus parity).
7. **Windows ConclusionsView MISSING**: MainWindow.xaml.cs:94 references `typeof(ConclusionsView)`
   but no ConclusionsView.cs or .xaml exists in the Windows project. macOS has a working
   ConclusionsView.swift. This is a confirmed gap — filed as new delta E015.

### Key Decisions This Session
- No user-visible output changes were made. All findings are documented for review.
- The stale "Windows engine is softmax" claim in memory_persist.md has been corrected.
- The USER's Delta of the Week #1 pipeline architecture is considered structurally complete —
  remaining work is calibration (scenario suite) and the Windows ConclusionsView gap.

### Remaining Gaps
1. **Windows ConclusionsView missing** — new delta (E015). MainWindow.xaml.cs:94 references it but
   no file exists. The macOS app has ConclusionsView.swift.
2. **Scenario test suite** not built — ROADMAP.md:72-80 calls for a labeled scenario suite for
   WeightRubric calibration. Not yet implemented on either platform.
3. **Critical new deltas unslated**: C006 (8.8.8.8 fallback — Critical), C007 (single-responder
   authoritative — High), C008 (Warp ✅ before ⚠️ — High), D014 (GluttonView trusting bool —
   Critical), D015 (Ed25519 key exposure — High), B029 (source unset — Critical).
4. **Stale DELTAS.md location refs**: [B002] location `SynthesisEngine.swift:78-84` predates DST
   rewrite. The current code no longer has that pattern. Verified B002 is structurally fixed on
   both platforms.

### Next Steps (Suggested)
1. Implement Windows ConclusionsView (E015) — medium effort, high impact for cross-platform parity
2. Fix C006 (8.8.8.8 fallback warning) — low effort per call site, critical impact
3. Fix D014 (GluttonView trusting bool) — one-line change, critical impact
4. Build scenario test suite for WeightRubric calibration — high effort, calibration-only
5. Fix D015 (Ed25519 key → Keychain) — medium effort, hardening
6. Fix B029 (source tags on evidence items) — medium effort, structural improvement
7. Slot all new deltas into ROADMAP.md

## Current task: C005 — Shared probe orchestration (ALL PHASES DONE)

### Design (DESIGN.md:118–172)
Three separable concerns: Execution (`ProbeRunner`), Budgeting (`ProbeBudget`), Consensus (already shipped — B010/B012). Probe implementations (`static func probe(...) async -> T?`) stay unchanged. VantageComparer/AuthoritativeVantage remain interpretation layer. SectionInputs remain output contract. **Consensus (B010/B012) is NOT called by ProbeRunner or ProbePlan — it runs separately in SynthesisEngine/VantageComparer as today.**

### User-approved plan (2026-07-09) — ALL DONE

| Phase | Action | Impact | Gate | Status |
|-------|--------|--------|------|--------|
| **1** | Add `ProbeBudget` + `ProbeRunner` (both Kits) | Infrastructure ready, zero behavior change | `swift test 312/312` + `dotnet test 173/173` | ✅ Done |
| **1.5** | Wire 9 missing probes into Windows UI `AppViewModel.cs` | **C005 delta closed** — Windows GUI 2→11 Prophet probes | Manual verification + existing test suite | ✅ Done |
| **2a** | Migrate macOS CLI to `ProbeRunner` | Budget enforcement, timing capture | `swift test 312/312` | ✅ Done |
| **2b** | Migrate macOS App to `ProbeRunner` | Budget enforcement, timing capture | `swift test 312/312` | ✅ Done |
| **2c** | Migrate Windows CLI to `ProbeRunner` | Budget enforcement, timing capture | `dotnet test 173/173` | ✅ Done |
| **2d** | Migrate Windows GUI to `ProbeRunner` | Refactor to shared infrastructure | `dotnet test 173/173` | ✅ Done |
| **3** | Extract shared `ProbePlan` (both Kits) | DRY improvement — macOS App + Windows GUI refactored | `swift test 312/312` + `dotnet test 173/173` | ✅ Done |

### Scope boundaries (Phase 3, shipped)
- `ProbePlan` orchestrates execution and writes to `SectionInputs` only.
- All `print()` / `Console.WriteLine()` presentation stays in the frontend.
- The Kit does NOT emit user-visible strings.
- Consensus (B010/B012) is NOT called by ProbePlan — it runs in SynthesisEngine as today.
- macOS/Windows CLIs keep their own probe loops (per-probe progress output).
- `runLeak` / `WitnessTestify` frontend-specific — not refactored to ProbePlan.

### Phase 1 — Implementation details **(DONE 2026-07-09)**

**Result:** macOS 312/312, Windows 173/173 — all tests pass, 0 failures.

**Function signatures (implemented):**
- Swift: `ProbeRunner.run<Output: Sendable>(label: String, budget: inout ProbeBudget, operation: @escaping @Sendable () async -> Output) async -> Output?`
- C#: `ProbeRunner.RunAsync<T>(string label, ProbeBudget budget, Func<Task<T?>> operation) where T : class` + `Func<CancellationToken, Task<T?>>` overload

**Files created:**
| Platform | File | Contains |
|----------|------|----------|
| macOS | `macOS/Sources/SyxxKit/ProbeBudget.swift` | `ProbeBudget` **struct** (Swift value semantics, `inout` mutation), fields: totalTimeout, perProbeTimeout, maxProbes, elapsed, probesRun, isExhausted, add(duration:) |
| macOS | `macOS/Sources/SyxxKit/ProbeRunner.swift` | `ProbeRunner.run` (lowercase, Swift conventions) with `withThrowingTaskGroup` + `group.next()` timeout enforcement |
| macOS | `macOS/Tests/SyxxKitTests/ProbeRunnerTests.swift` | 10 unit tests covering exhaustion, max-probes, success, timing, timeout, exhaustion-between-probes, budget value correctness |
| Windows | `Windows/Syxx.Kit/ProbeBudget.cs` | `ProbeBudget` **class** (C# reference semantics); factory methods (DefaultProphet, DefaultMirror, DefaultPortal, DefaultGlutton) |
| Windows | `Windows/Syxx.Kit/ProbeRunner.cs` | `ProbeRunner.RunAsync` (PascalCase + Async suffix) with `Task.WhenAny` + `CancellationTokenSource` overloads |
| Windows | `Windows/Syxx.Tests/SyxxKitTests.cs` | `ProbeRunnerTests` class: 14 unit tests (same coverage as macOS + factory-default verifications) |

**Timeout enforcement (implemented):**
- Swift: `withThrowingTaskGroup` + `Task.sleep(for: .seconds(timeout))` returning nil as timeout sentinel.
  Uses `group.next()` to get the first completed result — `.some(output)` if probe wins, `nil` if timeout wins.
  `defer { group.cancelAll() }` ensures cleanup.
- C#: `Task.WhenAny` racing operation against `Task.Delay(perProbeTimeout)` for non-cancellation-aware probes;
  `CancellationTokenSource(perProbeTimeout)` for cancellation-aware probes.
- **Documented limitation (R1):** doc-comment on `ProbeRunner.run` states: "Probes wrapping
  `DNSResolver.queryUDPDetailed` may run up to ~4s past timeout due to internal retry loop.
  Cancellation is not enforced at the transport layer. Follow-up C006 will add Task.isCancelled
  checks to the retry loop."
- **Elapsed time on timeout:** Capped at `perProbeTimeout` (conservative — safe overstatement).

**Budget accounting (implemented):**
- Always increment `probesRun` even on timeout/exhaustion.
- On timeout: add `perProbeTimeout` to `elapsed` (not actual wall time beyond cap).
- On success/failure: add actual measured duration.
- Budget exhaustion during a running probe: current probe finishes normally; next probe is skipped.

**Return semantics (implemented):**
- Runner returns `T?` (same as probes today).
- `nil` on: budget exhausted, timeout, or operation returns nil.
- No new throws; error classification internal.

**Debug logging (implemented):**
- Gated by `SYXX_DEBUG_PROBES` env var on both platforms.
- Swift: `ProcessInfo.processInfo.environment["SYXX_DEBUG_PROBES"] != nil`
- C#: `if (Environment.GetEnvironmentVariable("SYXX_DEBUG_PROBES") != null)`
- Format: `"[latency] ✅ 12.4ms (probe 3/15, elapsed 0.84s/30s)"` — **PENDING user approval (R6).**
- Zero runtime cost when env var is unset.

### Phase 1.5 — Windows UI probe gap fix **(DONE 2026-07-09)**

**Rationale:** C005 is rated High criticality (DELTAS.md:1428). Deferring Windows GUI probes through Phases 2a–2d is unreasonable. Phase 1.5 fixes the gap immediately using existing sequential pattern, THEN refactors to ProbeRunner in Phase 2d.

**Result:** macOS 312/312, Windows 173/173 — all tests pass, 0 failures.

**Change:** Wired 9 missing probes into `Windows/SyxxUI/ViewModels/AppViewModel.cs:RunProphetAsync()`:

| # | Probe | SectionInputs field |
|---|-------|-------------------|
| 1 | `DNSCanaries.NxdomainHijackAsync` | `.NxCanary` |
| 2 | `DNSCanaries.DnssecControlAsync` | `.DnssecCanary` |
| 3 | `ResolverLatencyProbe.Probe` | `.Latency` |
| 4 | `AnswerStabilityProbe.Probe` | `.Stability` |
| 5 | `WildcardSynthesisProbe.Probe` | `.WildcardSynthesis` |
| 6 | `DenialOfExistenceProbe.Probe` | `.Denial` |
| 7 | `DualStackProbe.Probe` | `.DualStack` |
| 8 | `EDNS0Probe.Probe` | `.Edns0` |
| 9 | `VersionBindProbe.Probe` | `.VersionBind` |

- Same sequential pattern as `macOS/Sources/SyxxApp/AppModel.swift:216-249` and `Windows/Syxx/Program.cs:142-213`.
- `OnPropertyChanged` fires once after all probes complete (was firing twice).
- No ProbeRunner yet — that comes in Phase 2d.
- Windows GUI now runs 2 → 11 Prophet probes (remaining 2 are GUI-level deferrals — StubEntropy needs capture, AuthoritativeComparison needs Pro).
- **C005 delta closed** (DELTAS.md:1426-1444).

### Phase 2a — macOS CLI ProbeRunner migration **(DONE 2026-07-09)**

**Result:** macOS 312/312, Windows 173/173 — all tests pass, 0 failures.

**Changes in `macOS/Sources/SyxxKit/ProbeBudget.swift`:**
- Added `static var defaultProphet` (30s/5s/15), `defaultMirror` (30s/5s/10), `defaultPortal` (15s/5s/5), `defaultGlutton` (20s/5s/5) computed factory properties.
- Computed (not stored) because `ProbeBudget` is a struct and `inout` mutation requires mutable access.

**Changes in `macOS/Sources/SyxxCLI/SyxxCLI.swift`:**
- `runProphet`: 12 probes wrapped in `ProbeRunner.run(label:budget:)` — all natively return optional; no flatMap needed.
- `runMirror`: 7 probes wrapped. Throwing probes (`VantageComparer.compareVantagePoints`, `ContentWarp.comparePageContent`) handle errors inside the closure via `do/catch`, returning `nil as T?`. The double-optional `T??` from ProbeRunner is flattened via `.flatMap { $0 }` at the call site.
- `DimensionClient.authoritative` returns `GroundTruth?` (also nullable) — same `.flatMap` pattern.
- `runPortal`: 3 probes wrapped — `networkReport`, `encryptedTransport`, `openResolver`. `LANCartographer.snapshot()` (synchronous throws) and `traceroute` subprocess left unwrapped.
- `runGlutton`: 3 probes wrapped — `licenseCheck`, `ecs`, `triggerQueries`.
- `LicenseManager.isFeatureUnlocked()` returns `(Bool, String)` tuple; accessed via `.0`/`.1` (tuple label inference is lost across generic closures).

**Throwing probe pattern (canonical):**
```swift
let result: T? = (await ProbeRunner.run(label: "vantage", budget: &budget) {
    do {
        return try await someThrowingProbe()
    } catch {
        print("❌ error: \(error.localizedDescription)")
        return nil as T?
    }
}).flatMap { $0 }
```

### Section budget defaults (Phases 2+)

| Section | totalTimeout | perProbeTimeout | maxProbes |
|---------|-------------|-----------------|-----------|
| Prophet | 30s | 5s | 15 |
| Mirror | 30s | 5s | 10 |
| Portal | 15s | 5s | 5 |
| Glutton | 20s | 5s | 5 |

### Output approval rules (all phases DONE)
- Phase 1 (ProbeBudget/Runner): No new end-user-visible strings.
- Phase 1.5 (Windows GUI probes): Reused identical format from macOS App / Windows CLI. No new strings.
- Phase 2a-2d (CLI/GUI ProbeRunner migration): Output text is identical (no user-visible string changes).
- Phase 3 (ProbePlan): ProbePlan itself emits no output; all print/Console.WriteLine stays in frontends.
- All output strings approved per AGENTS.md output-approval rules.

### Key decisions (from evaluation, all user-approved 2026-07-09)
- Direct closure returning `T?` preferred over explicit `ProbeOperation` type — minimal churn.
- Budget defaults configurable at call site; centralization deferred to Phase 3.
- **Phase 1.5 adopted** — Windows UI probe gap fixed immediately using sequential pattern, not deferred to Phase 2d+4.
- Swift `ProbeBudget` = struct (`inout` mutation). C# `ProbeBudget` = class (reference semantics needed).
- Swift: lowercase `ProbeRunner.run(...)`. C#: PascalCase `ProbeRunner.RunAsync(...)`.
- Swift: `ContinuousClock` for elapsed time (macOS 13+ minimum target).
- C# debug logging: runtime `Environment.GetEnvironmentVariable` (not `[Conditional]` on async).
- Cancellation limitation documented (R1); follow-up delta C006 proposed for `queryUDPDetailed` retry-loop fix.
- ProbePlan scope: execution + SectionInputs only. No presentation, no consensus. Consensus stays in SynthesisEngine.
- Budget exhaustion mid-probe: allow current probe to finish, skip next (R5 test added).
- Windows UI intermediate strings: reuse existing format from macOS App / Windows CLI.

---

## Session 2026-07-10 — [syxx-threat-analyst]: Peer Evaluation Reconciliation + Delta Resolution Plan

### What changed in DELTAS.md
- **Sweep B029 duplicate** → RETIRED, moved to `DELTAS-rejected.md`. Windows locations folded into main B029.
- **B030** → Upgraded Medium→Critical. Issue rewritten: Windows has NO throwing path (not "non-throwing responses treated as sent"). macOS verified correct. Fixes rewritten per [syxx-researcher] analysis.
- **C007** → Upgraded High→Critical per [syxx-researcher]. Location corrected to majorityConsensus:213 (previously compare():66-119).
- **C008** → RETIRED — duplicate of B024. Per-script hash section (SyxxWarp.swift:49-63) added to B024's scope.
- **Sweep D014** → RETIRED — duplicate of D013 residual. `[Obsolete]` attribute added to D013 Fixes.
- **Sweep D015** → RETIRED — duplicate of main D015. Code sample corrected from `Data?` to `String = ""`. Windows locations (AppViewModel.cs:69) and DPAPI fix added to main D015.
- **Sweep D016** → RETIRED — Windows finding confirmed and folded into main D015.
- **B024** → Location and Fix updated to include per-script hash section (lines 49-63).
- **Main B029** → Windows locations added. Already had [syxx-researcher] eval with corrected Issue language.

### Resolution plan created
See anchored summary above. Six workstreams prioritized by verdict impact:
1. WS1: False-negative verdicts (B030, C007, B029) — Critical, M effort
2. WS2: Security bypass (D013 residual, D015) — Critical, M effort
3. WS3: Misleading UX (B024, E012) — High, S effort
4. WS4: Windows GUI gaps (E015) — High, M effort
5. WS5: Platform parity (B025/E013, E014, CUR-003) — Medium, S effort
6. WS6: Cleanup (move fixed deltas, DST-vs-softmax debate)

### Self-evaluation acknowledged
- C006 duplicate (already fixed in prior session): correctly merged.
- B029 duplicate (this session): correctly reconciled via retirement + merge.
- C008 duplicate (this session): correctly retired with scope added to B024.
- D014/D015/D016 (this session): correctly retired with content merged.
- B030 Medium→Critical correction (this session): applied with rewritten basis.

### Corrections from user review (2026-07-10)

**B029 correlation map was partially invented.** The map I authored in the resolution plan asserted
evidence IDs that don't exist (`redirectionConfirmed`, `resolverMacMismatch`) or are misnamed
(`stubEntropy`→`weakStubEntropy`, `responseSize`→`responseSizeAnomaly`, `captivePortal`→`captivePortalActive`).
The real enum is at Conclusion.swift:133-170. Correction: Honesty §6 violation — must verify code
artifacts before citing them. The map in DELTAS.md entries uses vague group-by-probe language (no
bogus IDs), so DELTAS.md is unaffected; the plan must be rebuilt from the real enum.

**C007 depends on B031.** All current call sites pass a single bootstrap resolver (`[trustedBootstrap]`).
C007 Fix 1 (sets.count==1 → inconclusive) would permanently disable the authoritative probe if
landed before call sites pass ≥2 resolvers. Order: B031 first.

**CUR-003 already done.** Drop from plan (committed per CHANGELOG).

**E011/D012/E010 not fully fixed.** Status table marked them FIXED but: E011 has uncommitted code AND
cross-platform JSON encoding still breaks verify; D012/E010 are uncommitted working-tree changes.
Corrected to PARTIALLY FIXED in the status table.

**WS6a trimmed** to B020–B023 + B028 + A006/A009/A011 (the committed-fixed deltas only).
---

## 2026-07-16 (cont.) — REMEDIATION-MASTERPLAN2.md Tier 1 verified COMPLETE

**Cline (syxx-neural-executor)** audited every item in Tier 1 of REMEDIATION-MASTERPLAN2.md against live committed code. **All seven items confirmed implemented** (committed, building, passing tests on both platforms):

- B050 ✅, B051 ✅, B070 ✅, B073/B071 ✅, A013 ✅, B049/B053/C028/B063 ✅, Windows parity ✅

**Three additional server fixes landed:**
- A046 — GET `expand[]` in query string not body (`stripe_checkout.py:32-42`)
- A075 — field rename `'signed'` → `'dnskeyObserved'` (`resolver.py:569`)
- A038 — DNSError caught → inconclusive in diff handler (`app.py:886-896`)

**Working tree:** 4 files changed (+29/-5), macOS 476t/0f, Windows 389t/0f.

## 2026-07-17 — Phase B delivered (B1 + B2)

**B1 — macOS `SignedIncidentReportTests.swift`:** 11 tests (Windows parity). Fixed verdict priority: degraded beats inconclusive in renderer (both platforms). All 524 macOS tests pass.

**B2 — `syxx witness verify` enhanced:**
- `--pubkey <b64>` — external key override
- `--fetch-pubkey <url>` — fetch key from URL
- `--detailed` — tri-state output (9 `SnapshotVerification` states)
- Both macOS and Windows implemented with matching output format
- Real-run verified: VALID / INVALID SIGNATURE paths

**Next:** Windows CI run, Phase C, or continue with DEPLOY (B3 trust bootstrap, server integration).

**Tier 1 is done.** No honesty-critical false verdicts or evidence-integrity defects remain unaddressed. Next: Tiers 2-6 verification and remediation.

---

## 2026-07-24: Independent publication audit and macOS RC planning handoff

### Owner corrections that control future work

- Syxx was originally a macOS-only product.
- Windows and Linux ports began while the owner was waiting for an Apple Developer ID.
- The ports previously had determinative parity with macOS.
- The former MFAE-based determinative engine was removed from macOS about three days before this audit and is being replaced by the matrix engine.
- Windows/Linux matrix parity is a known, unfinished port migration. It does not gate a macOS-only release candidate.
- The current macOS Unbound capability exists and has been manually activated. The Dashboard contains a real SwiftUI button and `syxx unbound enable` exists. The remaining GUI work is administrator invocation and honest state/recovery handling, not implementing Unbound from scratch.

### Artifacts created

- Independent audit:
  `SYXX-PUBLICATION-READINESS-AUDIT-2026-07-24.md`
- Detailed macOS-only implementation plan:
  `docs/superpowers/plans/2026-07-24-macos-release-candidate.md`

No product code, existing delta ledger, roadmap, or previous findings report was changed by this audit/planning session.

### Correct release framing

The current macOS build is not yet a release candidate. Windows and Linux findings remain useful port audits, but are excluded from the macOS launch decision.

The macOS RC plan contains six workstreams and nine tasks:

1. Reproducible macOS CLI, tests, app bundle, and fail-closed bundling.
2. Evidence-first Dashboard states and bounded automatic checks.
3. GUI wiring and restart-safe recovery for existing Unbound capability.
4. Controllable Cage elevation plus encrypted-DNS and remote-endpoint safety.
5. Server listener availability, truthful readiness, and deploy gates.
6. macOS commerce, signing, notarization, clean-machine testing, and the final RC gate.

The next implementation agent should use the plan rather than reconstructing tasks from the audit.

### Verified Unbound state and code path

- `macOS/Sources/SyxxApp/DashboardView.swift:167` calls `model.setUnbound(true)`.
- `macOS/Sources/SyxxApp/AppModel.swift:236-270` calls `UnboundManager.enable()` directly and receives administrator-required results when unprivileged.
- `macOS/Sources/SyxxCLI/SyxxCLI.swift:24` routes the `unbound` subcommand.
- `macOS/Sources/SyxxCLI/Commands/SyxxUnbound.swift:16-29` provides `verify`, `verify-dnscrypt`, `status`, `enable`, and `disable`.
- Read-only run of `macOS/.build/release/syxx unbound status` found `/opt/homebrew/sbin/unbound` and reported system DNS at `127.0.0.1`.
- `/Library/Application Support/Syxx/unbound` contained `root.key`, `unbound.conf`, `unbound.log`, and `unbound.pid`.
- No enable/disable command was run during planning.
- Important residual: `UnboundManager` currently keeps prior DNS servers and its `Process` handle only in one instance. A later app/CLI process needs validated persistent state to disable and restore safely.

### Key macOS audit evidence

- `swift build -c debug --product syxx-gui`: passed.
- `swift build -c release --product syxx-gui`: passed.
- `swift test`: did not compile. Production failure at
  `macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:37-40` calls actor-isolated `flush()` from `deinit`; several tests also reference removed pre-matrix APIs.
- `macOS/bundle-app.sh:21-22` masks CLI build/copy failures with `|| true`.
- The resulting app bundled a stale CLI reporting version 1.0.0 while the app reported 2.0.0.
- `codesign --verify --deep --strict --verbose=4 macOS/Syxx.app` failed. The app is ad hoc/linker-signed and has no TeamIdentifier.
- A real `.build/debug/syxx key` run took 16.07 seconds; network failure remained unknown rather than clean.
- The Dashboard currently renders reassuring initial claims and maps result presence to `PASSED` at `DashboardView.swift:375-383`.
- `runAllWardens()` at `AppModel.swift:403-443` omits Cage, suppresses returned failures, bypasses the general timeout path, and always says all scans complete.
- Missing local resolver paths substitute public resolvers in Prophet, Mirror, Witness, and first-run flows.
- Encrypted DNS currently verifies DoT using a DoH sibling and represents successful disable through a failure case.
- Cage stop can lose the elevated child, and current privileged shell construction interpolates paths.

### Server and commerce evidence

- Full server suite: 225 passed, 0 failed in 39.783 seconds with unclosed-socket warnings.
- DoT wraps the listening socket, so an idle TLS handshake can block the sole accept path.
- authoritative DNS immediately continues after every `recvfrom` `OSError`, permitting a fatal-socket CPU loop.
- `/readyz` treats all unknown listeners as ready because it checks only explicit `down`.
- Public purchase/download controls in `Server/index.html` remain dead or point to `#`.
- Stock commerce preflight lacked required Keygen, Stripe, SMTP, collector signing, and platform policy configuration.
- The website defaults passive DNS to live before stats are fetched.

### Plan execution rules

- Begin in an isolated worktree after the macOS matrix contract is coherent enough to compile against.
- Do not restore the former MFAE engine.
- Obtain owner approval for the exact output table in the plan before changing visible text.
- Do not mutate live DNS unattended. Record original DNS first and prove exact restoration.
- Build Debug and Release plus the app bundle.
- Every workstream requires failure cases and a real production-path run.
- Developer ID availability is an external dependency. Unsigned bundle integrity and commerce work should proceed while waiting.

## 2026-08-02 — Syxx Dimensions GUI overhaul (macOS gold standard, parity to Windows/Linux)

** NOTE: I changed the output-catalogue.csv file so there are two, one for storybook mode and one for general mode. I also replicated the HTML. Do the following: add font parameters to the CSV and HTML files for each output, allowing me to edit fonts, font pt. sizes, and colors, as well as the output itself, without code. Depending on whether the app is in general or storybook mode, stream in the labels, cpations, and body elements from the corresponding CSV and HTML file. (HTML file streams into the CSV file; CSV file streams into the app.)

**Finish each pane individually** The user advised I implemented incorrectly in another context. Asked that I complete each pane one by one, stopping for him to assess and request corrections. 

**Theme rename:** the 2026-07-21 "Warden" dark-fantasy theme is now officially **Syxx Dimensions** (user: "Warden" has no real meaning). User-facing eyebrow/labels use "Dimensions"; the prior "Warden" entries in this file are the same theme. Sidebar eyebrow "THE SIX WARDENS" becomes "Dimensions" (white).

**Scope:** overhaul every GUI surface across macOS (SwiftUI, gold standard), Windows (WinUI/XAML), Linux (Avalonia) to the Syxx Dimensions aesthetic. 15 issues + F0 (fonts) + F1 (type retarget). macOS is the reference; Windows/Linux must reach parity. GUI greater than CLI (AGENTS.md).

**Foundational blocker (fixed in Phase 0):** macOS font registration was silently broken. Flat TTFs deleted from Resources, Bundle.module.url is non-recursive, so all 13 fonts fell back to the system font with no error. Fixed by restoring flat TTFs + hardening registration (fail loud, never silent) + a real failure-case test (macOS/Tests/SyxxAppTests/TypographyRegistrationTests.swift). File as a delta (Domain D, Critical: silently hides a failure).

** When copying text as output, do not include quotations unless I indicate otherwise.

    The User believes this has been fixed.

**Key decisions (user-locked):**
- Wordmark: logo + wordmark side-by-side, LEFT-aligned (logo left, ~5px gap, wordmark left-justified to logo right edge). Not centered, and should be stacked. 'Syxx' wordmark gradient #FF2D78 to #FF3DD5 left to right. 'Dimensions' wordmark #c4b3ff. Both wordmarks all caps and in Cinzel font face. Size wordmark so its horiontal space plus left 5px margin plus logo width = about 180px with 120px accounting for just wordmark. Wordmark-only vertical height, including internal vertical margins should be 100px-120px. Neither wordmark should be skewed.
- Content origin 35px from sidebar + 35px from top in all panes (supersedes IMPLEMENTATION_GUIDE 22/34/20).
- Mock text: The user gave permission to create mock text for accurate styling, despite the language of AGENTS.md and its prohibition. Caveat is mocked text must be red in color to avoid confusion with production text.
- Devices-on-Segment: keep on macOS, nicer presentation.
- Conclusions should have two views: the first lists each module and the completion status of each, button to proceed grayed out until all modules are marked complete; the second, accessibly only when all nodules marked complete, re-presents the data from the module scans, presents one Conclusion, provides opportunity to sqave report as text file with recommendation to post on Reddit (r/AskNetSec, r/HomeNetworking, r/CyberSecurity) or BleepingComputers.com).
- Body font: The body font face should be Cormorant Garamond Regular, 15pt. This is a non-system default. Spectral (in the assets and resources folders) + Cormorant Garamond bundled/registered on all three platforms. 
- Card Titles: Super captions and card titles should be in Marcellus SC Regular (4pt larger than body text). This is a non-system default.
- Light mode = auto-inverted palette (dark bg colors become light fg; current fg/light colors become the light bg).
- Menubar Glyph: Flattened, all white recreation of logo/symbol. Preferably transitions to #FF3DD5 and then to #c4b3ff over 5-7 seconds when DNS queries are confirmed as encryoted. Stops only when encryption of DNS queries ceases. It should function whenever DNS queries are encrypted, not just when the app is encrypting them. So, when on a VPN by another utility, for instance, the colors should be looping. 
- Menubar popover: (#15) content from mockup section 3.5 (Syxx Linux GUI Mxups.html / macOS and Windows twin): header logo + status, DNS Condition block, action rows; glyph = white Syxx-logo.png; 5s color pulse white to purple to pink (#FF2D78) to white driven by CONFIRMED EncryptedDNS state (not stale cache), reduced-motion honored. 

**15 issues:** 

- 1 The panes should run full-height unobscured, no header rows. The pane's background and content should be visible from bottom of window to top. Keep MOTD hero in BetaView / fallback-motd.html. 

- 2 All panes' contents should begin at the top left of the pane 35px right of the sidebar boundary (total 35px margin) and 35px down from the top edge of the window (35px top margin). That cannot be achieved if a pane, especially a scrollable pane, is set to vertically centered. (When the panes' contents are vertically centered, the vertical center becomes the origin point and not the point that is 35px below the top edge of the window.) 

- 3 Dashboard non-scroll, fit functional contents to fill space without forcing scroll. All contents should have a 35px left external margin against sidebar (line up with origin), 10px right external margin against container edge (create distance from scrollbar), and a 20px external margin from bottom edge of window. Internal margins (between components) of 5px. 

- 4 Prophet resolvers populate. Change Prophet highlight color to blue. Change subcaption to read: (Cormorant SC Regular, 20pt) "Thus the voyage began: The Prophet names each domain aright, so no false guise may be borne into view.” Increase the font size by 2pt.

    Add body text beneath subcaption: (Cormorant Garamond Regular, 14pt) "Prophet compares the IP address values returned by 12 different DNS resolvers. This helps identify transient DNS issues, such as stale resolver cache, as well as resolver configuration issues. A matching result does not mean that the answer is authentic or safe, as resolver answers can be spoofed. DNSSEC is what validates resolver answers as safe."

    Inside domain selection card, change: (Garamond EB Italic) “In what domain do voices agree, their truth brought forth in dawn, and by the sacred seal, their truth preserved.” Beneath it add body text: "Choose the domain you wish to use for the comparison"
    
    Change the pre-scan text in the result container that begins, (Garamond EB Italic, 15 pt.) "The prophet waits in silence ...", to instead read: “In silence doth the Prophet await a vision wherein the truth of twelvefold kind is aligned, and all things abide in surety.”
    
     Change the post-scan text that reads, "NO TAMPERING -- 12 RESOLVERS AGREE", to read: (Spectral Light, 20pt., module highlight color) “No tampering doth abide;" then, just to its right: (Spectral Light, 17 pt.) "the 12 resolvers are of one accord.”
     
     When the scan fails, (Spectral Light, 20pt., module highlight color) “Tampering doth yet abide;" and "the twelve resolvers are not of one accord.” Identical font to the above.
     
     The text that begins:  "The 12 oracles..." should be changed to read: (Garamond EB Italic, 15pt.) "Then came the vision of twelve oracles, each bearing witness, and all in steadfast accord, as though one truth moved through many mouths.”
     
     When the scan fails: (Same) "Then came the vision of twelve oracles, each bearing witness, yet their voices fell apart, and no single truth held them in accord.”

    Duplicate the result container exactly and place it below the existing one. Keep it hidden prior to scan completion. It should be styled to determine whether DNSSEC is configured for the domain. Style output similarly to say: (Spectrial Light, 20 pt.) “DNSSEC is rightly set for this domain." 
    
      When the scan fails: (same) “DNSSEC is not rightly set for this domain; resolvers may not speak true, and their witness cannot be held in trust.”

    
## STOP HERE. Build binaries plus app bundle.

- 5 Mirror highlight color should be yellow. Subcaption changed to read: (Cormorant SC) "Grasped fast in the Prophet’s hand, the Mirror did answer, and shewed the records born locally in accord with those discerned afar, proving that the true names shifted not their visages at each summoning.” Increase subcaption size by 2pt.

    Make the results container individually scrollable after the scan is run and if the contents would otherwise extend below the edge of the window. 

- 6 Sidebar no wobble + Spectral nav + Dimensions eyebrow + wordmark 130x65 + logo left + gradient; 

- 7 Portal copy + Resolvers/Active Interfaces restore + traceroute 3 spacer rows + Devices-on-Segment red-mock; 

- 8 Glutton Check button + copy + remove Feature Status; '

- 9 Cage; 

- 10 Witness; 

- 11 Conclusions simplify to ONE conclusion + saveable report (Round 2) + remove "How Decision Was Made"; 

- 12 Settings menu panel + load-bug fix (NSPanelManager.setContentSize); #13a Guided Setup menu panel; 

- 13b EULA centered modal scroll 210 EB Garamond 14.5 Decline + teal Agree; #13c License "The Covenant" two tier cards + key input + machine-ID mono; 

- 14 Update cleanup; 

- 15 Menubar popover.

**Subagent deployment (build):** Phase 0 = macos-infra-admin (done). Phase A macOS lock = swiftui-architect times (F0/F1, shell/sidebar, panes, modals/menubar) + ui-designer (consult) + gui-cross-platform-continuity (parity overseer, every phase) + syxx-honesty-audit + accessibility-tester + test-automator/qa-expert (non-aesthetic, failure-case tests). Phase B macOS panels. Phase C Linux (avalonia-architect + linux-infra-admin) then Windows (winui3-architect + windows-infra-admin, visual inconclusive host-gated). Phase D parity diff. Same agents reused across phases.

**Guardrails:** never edit AGENTS.md; never remove MOTD hero; update outputs-catalog.csv on all three platforms per changed string; no faked verdicts (Synthesis untouched); macOS gold standard; pause on missing dependency (Marcellus SC).

## 2026-08-02: Prophet pane (Issue #4) landed on all three platforms — STOP for owner review

Issue #4 (MEMORY.md:1814-1822) complete across macOS (gold standard, built + signed),
Windows (WinUI 3, winui3-architect subagent), and Linux (Avalonia, avalonia-architect
subagent). See the Wave 2b entry in CHANGELOG.md for the full file-by-file record. Per
the per-pane STOP rule (line 1787/working method), this pane is parked for owner
review before Issue #5 (Mirror) begins.

**macOS gold standard (built + signed):**
- `ProphetView.swift`: owner's MEMORY.md:1814 lore subcaption (EB Garamond Italic 18,
  was 16) + MEMORY.md:1818 body paragraph beneath (Cormorant Garamond body 16); domain
  selection card now opens with MEMORY.md:1820 lore quote + "Choose the domain you wish
  to check."; resolver-list + Consensus footer body bumped +2pt (13 to 15). Added
  `prophetDnssecCard(_:)` + `dnssecPresentation(for:domain:)` (tri-state: unsigned /
  configured-but-unverified / broken / inconclusive). The DNSSEC duplicate result
  container renders BELOW the resolver consensus card and is HIDDEN prior to scan
  completion (surfaces only when dnssecStatus lands). Strongest positive state is
  "DNSSEC configured: signatures observed" (green), NEVER "validated"/"secure". Failed
  DNSSEC query is Inconclusive (ioTextSecondary), NEVER red. The 12-oracle consensus
  plumbing was already wired in AppModel.swift:57-80, 255-259, 1011-1047.
- Builds: `swift build` + `-c release` green; `bundle-app.sh debug` + `release`
  produced a code-signed Syxx.app (Apple Development cert).

**Shared foundation (Syxx.Kit, main agent pre-staged so both subagents run in parallel):**
- `Windows/dotNET/Syxx.Kit/ProphetConsensus.cs` (new): ProphetOracleRoster (11 public
  oracles, identical to macOS list) + ProphetResult record + ProphetConsensus.RunAsync
  (12-oracle parallel fan-out, Local first, honest active count). Parity twin of macOS
  AppModel.runLookup. macOS keeps its app-local copy as gold standard.
- `Windows/dotNET/Syxx.Kit/ProphetPresentation.cs` (new, by winui3-architect):
  EvaluateConsensus + BuildConsensusLabel + BuildDnssecPresentation as Kit types
  returning ColorKey strings (headless Kit stays brush-agnostic). Em-dashes replaced
  with colons per AGENTS.md. Kit builds clean (0 errors).
- `Windows/dotNET/Syxx.Tests/ProphetPresentationTests.cs` (new, 20 tests, mutation-proven):
  protocol §3. Mutation: removing the "this domain" empty-domain guard turns
  Dnssec_EmptyDomain_RendersAsThisDomain_NeverBareBraces red; restore green. 20/20 pass.

**Windows (winui3-architect):** AppViewModel gained ProphetConsensusResult + DnssecStatus
(observable, cleared at scan start); RunProphetAsync calls ProphetConsensus.RunAsync +
sets DnssecStatus = plan.Dnssec (reuses ProbePlan's DNSSEC to avoid a second round-trip;
macOS makes a fresh query at AppModel.swift:1047 — both honest). ProphetView.xaml +
.xaml.cs fully rewritten to macOS structure; REMOVED the divergent resolver-line,
warning banner, LOCAL DNS ENCRYPTION card, and side-by-side records+DNSSEC grid. XAML
validated well-formed via xmllint. Kit + Tests compile on macOS (0 errors); full SyxxUI
WinUI compile is HOST-GATED (XamlCompiler.exe is a Windows binary — accepted gap).

**Linux (avalonia-architect):** ProphetViewModel gained ProphetConsensusResult +
DnssecStatus; CheckAsync calls ProphetConsensus.RunAsync. ProphetView.axaml fully
rewritten to macOS structure using registered Linux font resources. 12 new
ProphetViewModelPresentationTests pass (full suite 123/123, no regressions). `dotnet
build Windows/Syxx.Linux.slnf` 0 errors (cross-builds on macOS).

**PARITY DECISION FOR THE OWNER:** the Windows agent extracted the consensus + DNSSEC
presentation logic to a shared ProphetPresentation.cs in Syxx.Kit (single source,
testable via Syxx.Tests). The Linux agent ran in PARALLEL and inlined the equivalent
logic in ProphetViewModel.cs (it could not see ProphetPresentation.cs being created
concurrently). Two copies will drift. Recommend: have Linux consume ProphetPresentation
from the Kit (remove the inline duplicate, add a small ColorKey-to-IBrush mapper).
Owner decides at the per-pane review — not acting unilaterally.

**NEW OWNER INSTRUCTION AT LINE 1785 (acknowledged, needs guidance):** the owner
edited MEMORY.md mid-session and added a NOTE that the two outputs-catalog CSVs
(general-mode + story-mode) and their HTML twins should carry FONT parameters (font,
pt size, color) per output, and the app should STREAM labels/captions/body from the
CSV/HTML based on whether the app is in general or storybook mode — so the owner can
edit copy/fonts/sizes/colors WITHOUT code. This is a paradigm shift from the hardcoded
strings the Prophet pane currently ships. The Prophet pane strings are hardcoded per
the prior instruction set (MEMORY.md:1814-1822 verbatim). Before proceeding to Issue
#5, the owner should decide: (a) retrofit the Prophet pane (and the Wave 2a shell) to
the CSV/HTML streaming model first, or (b) keep the per-pane hardcoded approach for
now and treat the streaming refactor as a separate workstream. This is gated on owner
input, not a decision I can make unilaterally — it changes how every pane sources its
strings.

**Review flags for the owner (per-pane STOP gate):**
1. "begans" in the subcaption (MEMORY.md:1814) is the owner's verbatim text, shipped
   as-is on all three platforms. Likely a typo for "begins"; flagged, not auto-corrected.
2. 12-oracle real-run gap: ProphetConsensus.RunAsync needs open outbound UDP:53 to the
   public oracles (blocked here, MEMORY.md:569). Presentation logic verified offline
   (32 tests across Windows + Linux); end-to-end network path owes a real run on a host
   with open UDP:53.
3. MEMORY.md:1869-1871 appears STALE: both subagents report Cormorant Garamond IS
   registered on Windows (IoTypeBody) and Linux (SyxTypeBody), contradicting the "still
   Jost" note. If confirmed, the body-font parity gap is already closed.
4. Marcellus SC not registered on Windows/Linux (deliberately role-less per
   MEMORY.md:1864-1865); Prophet pane uses Cinzel/display for card titles, not Marcellus,
   so not a Prophet blocker. Phase C gap.

## 2026-08-02: Phase 0 coverage audit — F071 red contract suite found and fixed

- Phase 0 (macOS font registration) re-verified against source, not this file: 23 flat faces at
  Resources root, 8 OFL licenses, fail-loud tri-state registration (SyxxApp.swift:27,49-91;
  IoDesignSystem.swift:404-521), bundle-app.sh:123-156 manifest-driven packaging gate,
  TypographyRegistrationTests 8/8 green.
- FOUND RED: TypographyResourcesTests (8 assertions / 4 tests) — Wave 1 changed the mapping but
  not the Phase 3.7 contract (still expected Jost body, 13 faces, 10 roles, 14pt). Filed F071,
  fixed by updating the contract to the owner-approved mapping, mutation-proven red, reverted
  green 15/15 + 8/8. Debug + Release builds green.
- Owner-confirmed role map: body = Cormorant Garamond 300/400 (Jost retired from body by owner
  choice); card labels = Cormorant SC (bodyAlternate/deck); navLabel = Spectral Light 300 @15;
  uiLabel/eyebrow = Jost Medium; Marcellus SC bundled + registered, deliberately role-less
  (all-small-caps titling face, no lowercase).
- Phase 0 + Wave 1 work is ALL UNCOMMITTED. Stray extracted font folders sit at the repo ROOT
  (Cinzel/, Cormorant Garamond/, Cormorant_SC/, EB Garamond/, JetBrains_Mono/, Jost/) — owner to
  decide delete vs relocate.
- Parity gaps (Phase C, not Phase 0): Windows/Linux body still Jost; Cormorant Garamond nested
  and unreachable on both; no runtime registration-failure tests on either; shared guard script
  scripts/test-typography-assets.sh is Jost-led and omits the new families.

---

## 2026-08-02: SYXX DIMENSIONS OVERHAUL — OWNER INSTRUCTION SET, AGENT RECOLLECTION (DRAFT, OWNER TO EDIT FOR ACCURACY)

> Owner directive (2026-08-02): this block is the agent's best recollection of every instruction
> given for the Syxx Dimensions GUI overhaul, written in maximum detail so the OWNER CAN EDIT IT
> FOR ACCURACY before any further pane work. Until the owner edits it, it is a DRAFT spec, not
> gospel. It never overrides AGENTS.md. The other-context session was fed more instructions than
> its context could hold, so shipped work may diverge from what is written here.

### 0. Working method (NEW, supersedes bulk waves)

- STOP at the end of EACH pane so the owner can look and make revisions before the next pane
  begins. No more multi-pane waves without a review gate.
- The current app state has changed and much of it is NOT necessarily correct. Treat every
  post-Wave-1 change as suspect until reviewed against this block and the mockups.
- Owner-flagged regressions in the current build (2026-08-02): (a) NO pane starts at the
  35px/35px vantage; (b) EVERY pane is scrollable regardless of total content height. Both
  violate §3 below and must be corrected during the per-pane passes.

### 1. Theme naming

- The theme is "Syxx Dimensions", NOT "Warden" ("Warden" has no real meaning, owner correction).
- User-visible "Warden" text becomes "Syxx Dimensions" / eyebrow "Dimensions". Code-internal
  identifiers may stay.
- Sidebar eyebrow "THE SIX WARDENS" becomes "Dimensions" in WHITE (issue #6).
- The gold "⊕ Dimensions" nav button (external link to syxx.app) is a DIFFERENT element: leave it.

### 2. Visual truth sources

- Visual truth = the mockups: `Syxx GUI Mockups/` HTML (macOS + Linux "Mxups") +
  `IMPLEMENTATION_GUIDE.md`. `Type Specimen.html` only decodes font ROLES; its colors are wrong
  (mis-colored) and must not be used as a color source.

### 3. Spacing law (owner override, supersedes the guide's 22/34/20)

- EVERY pane's content originates at 35px from the sidebar boundary and 35px below the top
  window edge. Pane margins: Left 35 / Right 15 / Bottom 20 / Top 35.
- Scrolling rule: NO pane is scrollable at the pane level if its individual contents can
  reasonably be made scrollable instead (e.g. Dashboard keeps its nested activity-log scroller;
  Prophet keeps its inner results scroller; Portal is a 2-column non-scrolling layout).
- Dashboard: internal spacing 5px, max height about 640px, no outer PurpleScrollView.
- Modals: centered, backdrop blur 24, border rgba(160,140,255,.24), corner radius 18,
  scrollable body max-height 210.

### 4. Typography (authoritative role map, after owner corrections)

- display / headings = Cinzel 600-700 (caps, never body, never below 18pt).
- body = Cormorant Garamond 300/400. CORRECTION HISTORY: an interim plan trial named
  Marcellus SC for body; owner corrected: body stays Cormorant Garamond. Marcellus SC is
  bundled and registered but deliberately ROLE-LESS (all-small-caps titling face, Regular only,
  PostScript MarcellusSC-Regular). An agent suggestion was Marcellus for card labels; the card
  label role is currently Cormorant SC (owner confirm whether that still stands).
- navLabel = Spectral Light 300 at 15px, new role on all 3 platforms (sidebar / nav chrome).
- eyebrow / labels = Cormorant SC; the "Dimensions" sidebar eyebrow renders WHITE.
- emphasis / lore = EB Garamond Italic (about host size + 2pt).
- technical = JetBrains Mono / ui-monospace (IPs, hashes, keys, machine-ID, paths).
- Prophet results = JetBrains Mono + Jost.
- deck / bodyAlternate = Cormorant SC (kept roles).
- uiLabel = Jost Medium 500; no ordinary UI at Jost 600+; UI chrome stays Jost (owner decision).
- Register on all three platforms: Spectral (Light/Regular/Medium/SemiBold/Italic),
  Cormorant Garamond (Light/Regular/Medium/Bold), Marcellus SC, plus the pre-existing
  Cinzel / Jost / Cormorant SC / EB Garamond / JetBrains Mono faces. macOS manifest: 23
  registered faces + EBGaramond-Regular shipped flat but unregistered; 8 OFL files ship.
- Per-platform family-name forms (ID-1): macOS "Spectral-Light" etc.; Linux "#Spectral Light";
  Windows hypothesized "#Spectral". Windows declarations currently use "#Spectral Light" for
  consistency with the existing "#Jost Light" convention; UNVERIFIED until a Windows build.
- T1 acceptance (not yet done): a role to resolved-name dump, byte-identical across all 3
  platforms; any system-font fallback = FAIL.

### 5. Color tokens (from mockups, confirmed)

- Canvas #0b0518. Panels linear-gradient(180deg, #170c2e, #0f0720). Neutral surface
  rgba(255,255,255,.03), border rgba(148,120,246,.14), radius 14.
- Wordmark gradient #FF2D78 to #FF3DD5, left to right (issue #6).
- Primary button linear-gradient(120deg, #7c5cff, #5b3fd6). Agree/Passed teal #2dd4bf.
  Glutton accent #ff4d6d.
- Known drift to reconcile in the locks: macOS sidebar-top #1d1640 vs Linux #1d1240; primary
  gradient angle 120deg (macOS) vs 45deg (Linux App.axaml).
- Light mode (all platforms): auto-inverted palette: dark bg colors become light fg; current
  fg/light colors become the light bg. macOS light-mode ladder is a known honest-delta item.

### 6. Layout details

- Window / pane shell: 1050x700, corner radius 16, no header/footer chrome, panes full height.
  (OWNER CONFIRM: SyxxApp.swift:33-42 currently forces 1050x613 with "design floor" comments
  from an earlier plan; recollection of the overhaul spec says 1050x700.)
- Warden/pane header pattern: rune tile 54x54 r14 + Cinzel pane name + Cormorant SC eyebrow +
  EB Garamond lore line.
- Dashboard: see §3 (fixed margins, internal 5px, maxHeight about 640, nested log scroller).

### 7. Sidebar spec (236px; issue #6; INCLUDES THE OWNER'S WORDMARK CORRECTION)

- CORRECTED 2026-08-02: the logo + wordmark are SIDE BY SIDE, LEFT-ALIGNED: logo bounding box
  on the left, then about 5px total gap, then the wordmark left-justified against the logo's
  right edge. NOT centered, NOT stacked vertically. (An earlier answer of mine said "stacked
  above wordmark, centered"; the owner overrode it: "centering requires room, there isn't
  much.") The whole group sits at the sidebar's left within its own padding.
- Logo about 65px tall (Syxx-logo.png); wordmark "Syxx" in Cinzel with the #FF2D78 to #FF3DD5
  gradient at matching height (about 65px; earlier brief mentioned a 130x65 wordmark box).
  65 (logo) + 5 (gap) + wordmark must fit 236px minus sidebar padding; if the wordmark would
  overflow, keep height 65 and let width be whatever "Syxx" measures; never center.
- Eyebrow "Dimensions" in WHITE (Cormorant SC) below the group, left-aligned. Nav items below,
  Spectral Light 300 at 15px, left-aligned. Gold dividers. Footer pill "⟲ Start your story anew".
- +10px group shift downward (issue #6 detail from the mockup pass).
- No wobble: replace the unbounded Spacer() (ContentView.swift:134 area) with fixed top
  padding; reserve PRO-badge width unconditionally so it never shifts layout when it appears.

### 8. The 15 pane issues (owner's issue list with macOS anchors)

- #1 All panes full-height, no header rows. The MOTD hero lives in BetaView.swift /
  fallback-motd.html, NOT in Conclusions; AGENTS.md orders it protected forever.
- #2 Delete the five stray .padding(.top,15) (Prophet/Mirror/Portal/Glutton/Witness); keep
  ContentView's 35. (Superseded in practice by the §3 law: every pane 35/35.)
- #3 Dashboard: drop outer PurpleScrollView, fixed margins, internal 5px, maxHeight 640, keep
  the nested activity-log scroller.
- #4 Prophet: resolvers are "delayed, not broken": show a ProgressView while .checking; label
  copy per owner; run button reads "Divine the Domain"; results in JetBrains Mono + Jost;
  inner scroller already present.
- #5 Mirror: keep output containers; add per-resolver rows; Cormorant paragraph (two roles);
  EB Garamond italic helper line; remove the "Mirror tests your DNS resolver for signs..."
  line (MirrorView.swift:77).
- #6 Sidebar per §7.
- #7 Portal: 35/35; Cormorant paragraph; domain label copy per owner; restore the
  Resolvers / Active Interfaces blocks populated at launch from the startup probe; traceroute
  gets 3 spacer rows for the overlay; pane NOT scrollable (2-column layout);
  Devices-on-Segment: keep on macOS, presented nicer; any placeholder/mock text in RED
  (owner rule: never mistakable for real output).
- #8 Glutton: 35/35; "Check" button in view; new paragraph with typo-corrected "encryption"
  and "use https://leaktest.syxx.app"; remove the Feature Status container (confirm the
  entitlement surfaces elsewhere); subcaption stays constant post-scan.
- #9 Cage: 35/35; content + button in view; beautify, including the hidden mode.
- #10 Witness: button in view; subcaption drops ", and its eyes," so it ends "...the network
  was lying, indeed."; add a second Cormorant subcaption; domain label copy per owner; "Tell
  the witness a secret..." prompt already present.
- #11 Conclusions: fonts + beautify; simplify Stage 2 to ONE conclusion + a saveable report
  (NSSavePanel, must render .inconclusive explicitly); keep Stage 1 completed/remaining;
  remove "How Decision Was Made"; data depth = full synthesis ("Round 2").
- #12 Settings: load bug = NSPanelManager.makePanel lacks setContentSize + the view has no
  explicit frame; add both. Rename-not-delete + recreate as a menu-opened panel (no sidebar
  entry).
- #13a Guided Setup: same menu-panel treatment, restyled to the mockup.
- #13b EULA: centered modal, scroll body maxHeight 210, EB Garamond 14.5px, Decline + teal
  "Agree & Continue →"; wire the unused menuPresented flag (menu to Close only).
- #13c License: "License · The Covenant", two tier cards, key input + machine-ID mono row.
- #14 Update panel: cleanup to match; replace the placeholder logo border with syxx-icon.png.
- #15 Menubar popover per §9.

### 9. Menubar popover (#15, from mockup §3.5 of the Linux/macOS Mxups)

- Header: Syxx-logo.png 30x30 + "Syxx · The Watch" + status dot.
- DNS Condition block: Resolver / Transport / Cache / Last result. (The mockup label read
  "Last verdict"; AGENTS.md retires the word "verdict" from the apps, so the label wording
  needs an owner-approved replacement. FLAGGED.)
- Action rows: reuse the existing MenuBarContent actions, restyled.
- Switch MenuBarExtra to .window style.
- Glyph pulse: white to purple to pink (#FF2D78) to white over 5s, driven by CONFIRMED
  EncryptedDNSConfigurator.currentState()==.enabled, never the stale AppModel.isEncrypted
  cache; reduced-motion honored. Glyph is already the white Syxx-logo.png (satisfied).
- Honest platform deltas to file, not fake: Linux SNI tray cannot host a popover; Windows
  uses a Win32 flyout; window chrome (traffic lights); backdrop blur on X11/Wayland; macOS
  light-mode ladder.

### 10. Phase plan and lock files

- Phase 0: restore + harden font registration (DONE on macOS; Phase 0 parity for
  Windows/Linux landed 2026-08-02, see the entry above this block).
- Phase A (macOS, locks the reference): F0 bundle fonts (done) then F1 type retarget +
  navLabel role (done) then global shell/sidebar, the six panes + Dashboard + Conclusions,
  then EULA/Update/Menubar, then LOCK: emit tokens.lock, type-roles.lock, strings.lock,
  nav-order.lock, and reference screenshots. The locks, not the HTML, are the port target.
- Phase B (macOS): Settings / Guided Setup / License panels.
- Phase C: Linux port first (builds on this host), then Windows derived mechanically from the
  Linux port (same ViewModels/token names) to shrink blind-edit surface.
- Phase D: 3-way parity diff, file residual deltas.
- Owner gate now overrides wave order: per-pane stop-and-review (§0).

### 11. Cross-platform parity rules

- macOS is the gold standard; Windows/Linux rise to it, never the reverse (AGENTS.md).
- Windows XAML cannot compile on this host: every Windows visual result is INCONCLUSIVE until
  a Windows build runs (Protocol §1/§5). A Windows verification host is an OPEN owner item.
- Linux dedupe: about 19MB of duplicate fonts were under Assets/ (done in Phase 0 parity);
  rename syxx-logo-source.png to Syxx-logo.png (3 call sites) still pending; Avalonia
  type-role Styles pending; the 6 un-themed raw-monospace panes are Phase C.
- Windows: dedupe fonts (done in Phase 0 parity); sweep IoFontGrotesk/IoFontUI aliases to
  semantic keys (Phase C); align logo filename.

### 12. Subagent deployment (owner matrix) and verification quirk

- Phase A: swiftui-architect (waves) + ui-designer (consult) + gui-cross-platform-continuity
  (parity overseer, EVERY phase) + syxx-honesty-audit + accessibility-tester +
  test-automator/qa-expert. Phase B: macOS panels. Phase C: avalonia-architect +
  linux-infra-admin, then winui3-architect + windows-infra-admin. Phase D: parity diff.
  Same agents reused across phases; subagent definitions are read from ./Subagents/ and fed
  to the agents verbatim.
- QUIRK: subagent text output frequently returns empty or the stream dies, but the work may
  be done. Verify via filesystem/git and real command output, never via subagent prose.

### 13. Test discipline (owner-added)

- Non-aesthetic changes ship with tests that have explicit failure states: red when broken,
  not just non-empty asserts. build-engineer runs Debug+Release; qa-expert signs off each
  failure path. One real run with honest output beats green tests (AGENTS.md §2).

### 14. Guardrails (binding, from AGENTS.md + owner)

- Never edit AGENTS.md. Never remove the MOTD hero image from the MOTD HTML files.
- Update outputs-catalog.csv for every changed user-facing string + file:line, all 3
  platforms. End-user-visible wording is proposed to the owner BEFORE implementation.
- No faked determinations: Synthesis/ and Synthesizer.conclude are untouched; this overhaul
  is presentation only. macOS is the gold standard. Pause and report missing dependencies.
- The word "verdict" is retired from the apps (affects the #15 popover label, see §9).
- No em-dashes in output text except where grammatically required.

### 15. Open owner decisions (as of this recollection)

- Repo-root stray font extract folders (Cinzel/, Cormorant Garamond/, Cormorant_SC/,
  EB Garamond/, JetBrains_Mono/, Jost/): delete or relocate?
- Windows/dotNET/SyxxUI/Assets/Fonts/ holds two TRACKED zip archives (Cinzel.zip,
  Cinzel,EB_Garamond.zip, 2026-07-30): tracked-removal decision.
- macOS Resources still holds nested family subfolders (some tracked, e.g. Cormorant_SC/):
  dead-weight duplicates of the flat faces; cleanup decision.
- OFL-Cormorant.txt / OFL-JetBrainsMono.txt on Windows diverge from macOS (CRLF + one FAQ
  URL): normalize to macOS bytes?
- Windows verification host for all inconclusive Windows rows.
- Card-label face: Cormorant SC (current) vs the Marcellus SC suggestion (§4).
- Window floor 1050x700 (overhaul spec) vs 1050x613 (current code), §6.
- "Last verdict" popover label replacement wording, §9.

### 16. Correction history (so the same mistakes are not re-made)

- "Warden" renamed to "Syxx Dimensions" by owner; "THE SIX WARDENS" eyebrow becomes
  "Dimensions" in white.
- Sidebar logo+wordmark: owner corrected stacked/centered to side-by-side, left-justified
  against the logo's right edge, about 5px gap.
- Body face: owner corrected Marcellus SC trial back to Cormorant Garamond; Marcellus SC
  stays bundled but role-less.
- Cormorant Garamond Light was briefly considered then Regular; moot once Marcellus left body.
- 2026-08-02: owner reports the shipped pane state diverged (no 35/35 origin; every pane
  scrollable). Per-pane stop-and-review is now the working method (§0).


## 2026-08-02: Wave 2a handoff

- The first `swiftui-architect` dispatch for Wave 2a hit the tool time limit and was cancelled
  before implementation could be verified. No claim of Wave 2a completion is made.
- Wave 2a remains pending: macOS shell/sidebar, 35/35 content origin, and non-scrolling Dashboard.
- A substitute GUI implementation agent was dispatched. The locked typography contract remains
  unchanged: Cormorant Garamond body, Cormorant SC card labels, Spectral nav labels, Jost UI
  labels, and Marcellus SC unassigned.

## 2026-08-02: Wave 2a macOS shell/sidebar and Dashboard completed

- `ContentView.swift:55-104` owns the 35-point horizontal and top origin for all non-MOTD panes;
  affected child outer insets were removed without changing the MOTD hero or fallback resource.
- `ContentView.swift:133-320` uses a direct `Bundle.module` load of `Syxx-logo.png`, a 5-point
  logo-to-wordmark gap, a 130×65 left-aligned wordmark block, the white `Dimensions` eyebrow,
  Spectral Light 300 navigation labels at 15 points, and fixed-height rows.
- `DashboardView.swift:34-269` removes the outer full-panel scroll container and compresses only
  layout metrics. The inner activity log remains scrollable because its source is an unbounded,
  live model log. The Dashboard itself fits the 1050×613 content floor in the real run.
- Real run: `./bundle-app.sh debug`, direct launch with a fresh test HOME, CoreGraphics window
  inspection reported 1050×645 including the native title bar, and `screencapture` captured the
  Dashboard after Command-1 navigation. The captured Dashboard had no outer scrollbar and showed
  live resolver and transport state rather than mock data.
- Verification: TypographyRegistrationTests 8/8, TypographyResourcesTests 15/15, focused
  Dashboard/Mirror/ticker, shell-contract, and PurpleScrollView tests 29/29, `swift build`,
  `swift build -c release`,
  and the debug bundle all passed. No new state helper was introduced, so no new behavioral XCTest
  was needed; existing mutation-proven state and scrollbar tests were rerun.

# Changelog

Running log of ROADMAP.md implementation work, plus a reserved watchdog channel.

## Prophet parity drift collapsed onto one shared source (2026-08-03)

Owner asked for the Prophet parity smell flagged in the Wave 2b entry to be fixed. The
drift turned out to be wider than that entry described, and the entry's parity claim was
not true at the working tree.

**What was actually wrong.** macOS received a later pass (the 2026-08-02 owner-directive
lore plus the `ioProphetBlue` highlight) that Windows and Linux never got. BOTH
`Syxx.Kit/ProphetPresentation.cs` AND the inline duplicate in the Linux
`ProphetViewModel.cs` were frozen at the pre-lore snapshot, so Windows was rendering the
stale copy too, not just Linux. Consolidating Linux onto the Kit as-is would have frozen
the wrong text into one place. Nothing had shipped: all of it was uncommitted.

- **Kit raised to the macOS gold standard** (`Windows/dotNET/Syxx.Kit/ProphetPresentation.cs`):
  `ConsensusPresentation.Title` becomes the two-run `HeadlinePrimary` + nullable
  `HeadlineSecondary` (macOS `ProphetView.swift:189-203`). Agree and disagree carry the
  owner lore (MEMORY.md:1827-1833) on `IoProphetBlueBrush` / `IoWarningAmberBrush`;
  inconclusive and insufficient keep their own wording, get a null run 2, and never borrow
  either voice. DNSSEC titles take the owner copy (MEMORY.md:1835/1837), with the strongest
  positive state on the Prophet blue and still never reading validated or secure (CF-08/A2).
  `EvaluateConsensus` Insufficient now carries no IPs, matching macOS `:178`.
- **B231, and a live AGENTS.md violation.** An unmeasured median latency rendered as a bare
  em-dash on both platforms. Em-dashes are banned in user-facing text. Now reads
  "not measured" (macOS `ProphetView.swift:215`), never a fabricated "0ms".
- **Linux consumes the Kit** (`Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ProphetViewModel.cs`):
  the ~125-line inline duplicate of the consensus and DNSSEC logic is deleted. A `BrushFor`
  ColorKey-to-IBrush mapper keeps the Kit toolkit-agnostic; an unrecognised key falls back to
  neutral grey rather than borrowing a success colour, so a future Kit/client drift cannot
  render as a false clean.
- **Copy parity.** Linux carried "begans" (owner-confirmed typo for "began") plus a
  divergent longer subcaption, a divergent body paragraph, and a divergent input-card lore
  line, all wrapped in quotation marks against MEMORY.md:1795. Windows carried the same
  divergent input lore. All four now match macOS verbatim.
- **Tokens.** `IoProphetBlueColor`/`IoProphetBlueBrush` added to `IoDesignSystem.xaml`;
  `SyxProphetBlueColor`/`SyxProphetBlueBrush` added to `App.axaml`. Both #00b4ff, parity
  twins of `Color.ioProphetBlue`.
- **Windows view** (`ProphetView.xaml` + `.xaml.cs`) renders the two headline runs on
  Spectral Light 20/17, run 2 collapsed when null. This file is outside `Syxx.Linux.slnf`,
  so its compile is still host-gated.

**Verification.** `dotnet build Windows/Syxx.Linux.slnf` 0 errors. Kit Prophet suite 26/26
(was 20). Linux Avalonia suite 123/123. Full Kit suite 814 passing / 17 failing, and the
identical 17 fail at HEAD in a clean detached worktree, so **zero new failures**; the
worktree was removed and pruned. All four XAML/AXAML files parse well-formed.

**Mutations (Protocol §3), both run and reverted.** (1) Restoring the em-dash latency
fragment reds 5 Kit tests and 2 Linux tests. (2) Giving the inconclusive state the success
lore headline reds 1 test in each suite. Restored: 26/26 and 123/123 green.

**Not done, and why.** No real run: the 12-oracle fan-out needs open outbound UDP:53, which
this environment blocks, so only the offline presentation path is proven. The Windows WinUI
XAML compile and a native launch are still owed on a Windows host. The macOS `ProphetView`
keeps its app-local copy as the gold standard and was not touched.

**Flagged, not acted on.** `Syxx.Kit` is shared code living at `Windows/dotNET/Syxx.Kit/`,
which is what invited the Linux duplicate in the first place: reaching into a folder named
`Windows` for Linux behavior reads as wrong, so an agent writes its own copy instead. Owner
notes the intended layout is `dotNET/{Windows, Linux, Shared}`. Not moved.

## B281 — shared cross-vantage agreement policy (Prophet false positive) (2026-08-02)

Owner-reported false positive: Prophet flagged near-identical GitHub pool answers
(140.82.113.3 vs .4, same /16) as possible tampering. A network-engineering consult
traced it to Prophet's `evaluateConsensus` (`Set(active.map { $0.ip })` over
`records.first`), NOT Mirror's `VantageComparer`. Then an adversarial review cut
several unsafe branches. What LANDED this pass (Kit + tests + ledgers only; SyxxApp
wiring is handed off, see below):

- **New shared Kit policy** `macOS/Sources/SyxxKit/Synthesis/CrossVantageConsensus.swift`
  - Windows twin `Windows/dotNET/Syxx.Kit/Synthesis/CrossVantageConsensus.cs` (Linux
  links Syxx.Kit, so it inherits the twin). ONE decision function over FULL A/AAAA
  RRsets: identical → match; blocking-beside-routable → mismatch; any other difference
  judged ONLY against an attacker-independent DoH consensus (`dohCorroboratedPool`,
  ≥2 independent DoH; Do53/UDP never counted) — different responsible network (/16 v4,
  /32 v6, family-aware) → mismatch, same network / mixed family / no reference →
  inconclusive. No VPN branch (cut — a tunnel proves nothing about egress, `ppp`
  matches PPPoE, VPN operator is an adversary). /16-/32 never grants a match.
- **Mirror integration:** `VantageComparer.evaluate` disjoint branch delegates to
  `CrossVantageConsensus.classifyDivergence` against the DoH consensus only, replacing
  the unconditional redirection (macOS `VantageComparer.swift:235`, Windows
  `VantageComparer.cs:287`). Fixes the strength self-corroboration hole (was using the
  attacker-authorable UDP `publicUnion` as the reference). B010 detection preserved
  (`testUnanimousUDPTamperCaughtByCleanDoH` still passes).
- **Shared golden vector** `corpus/b281-consensus/decision-table.json`, loaded and
  asserted by BOTH suites (`CrossVantageConsensusGoldenVectorTests.{swift,cs}`); fails
  CI on any macOS/Windows divergence. 13 rows incl. the P0-c false-clean and NAT64.
- **Tests:** macOS `CrossVantageConsensusTests.swift` (25) + 4 Mirror B281 cases +
  anti-cancellation; Windows `CrossVantageConsensusTests.cs` (16) + golden vector.
  All green. The adversarial review's "zero helper coverage" finding is closed.
- **Real run (live, 2026-08-02):** github.com via 1.1.1.1/8.8.8.8/9.9.9.9 →
  140.82.112.4 / 140.82.114.3 / 140.82.114.4 (three pool members, one /16) ⇒
  `classifyPeerConsensus = inconclusive` (was: possible tampering). VPN-independent.
- **Ledgers:** `DELTAS.md` B281 (with the verbatim decision table + strength-of-
  disagreement definition), plus B282 (Windows `DetectUnanimousTampering` bool vs
  macOS tri-state — potential false clean) and B283 (4-5 macOS `VantageComparerTests`
  assert a superseded pre-C025/C046 contract; red at HEAD, independent of B281).

NOT landed (flagged, not claimed): (a) the SyxxApp Prophet wiring — `ProphetView`
`evaluateConsensus` + `AppModel:1022` must call `CrossVantageConsensus` over full
RRsets instead of `records.first`/Set-count; kept out of this pass because SyxxApp is
another lane with a concurrent editor. (b) The VPN branch is CUT, overriding the
owner's original "VPN → match" rule per the adversarial review; owner confirmation
requested. (c) B282/B283 fixes are filed, not implemented.

## Wave 2b Prophet pane: macOS gold standard + Windows/Linux parity (2026-08-02)

Issue #4 of the Syxx Dimensions overhaul (MEMORY.md:1814-1822). macOS landed first as
the gold standard; two parity subagents (winui3-architect, avalonia-architect)
translated line-by-line to C# / WinUI 3 / Avalonia against a shared `Syxx.Kit`
foundation the main agent pre-staged.

### macOS (gold standard, built + signed)

- `ProphetView.swift`: subcaption replaced with the owner's MEMORY.md:1814 lore
  (EB Garamond Italic 18, was 16) + a body paragraph beneath it (Cormorant
  Garamond body 16, MEMORY.md:1818). The domain selection card now opens with
  the MEMORY.md:1820 EB Garamond Italic lore quote + "Choose the domain you wish
  to check." (body 16), replacing the old "THE DIVINATION" eyebrow + helper line.
  Resolver-list + Consensus footer body fonts bumped +2pt (13 to 15).
- Added `prophetDnssecCard(_:)` + `dnssecPresentation(for:domain:)` (the tri-state
  DNSSEC presentation: unsigned / configured-but-unverified / broken / inconclusive).
  The duplicate DNSSEC result container renders BELOW the resolver consensus card
  and is KEPT HIDDEN prior to scan completion, surfacing only when the DO-enabled
  DNSKEY query at the end of runLookup lands a `DNSSECStatus`. The strongest positive
  state emitted is "DNSSEC configured: signatures observed" (RecordsPresentUnverified
  green); NEVER "validated"/"secure"/"protected" (CF-08/A2 full chain validation not
  done here). A failed DNSSEC query is Inconclusive (ioTextSecondary), NEVER red.
- The 12-oracle consensus plumbing (`ProphetOracleRoster`, `ProphetResult`,
  `dnssecStatus`) was already wired in `AppModel.swift:57-80, 255-259, 1011-1047`.
- Builds: `swift build` + `swift build -c release` green; `bundle-app.sh debug` +
  `release` produced a code-signed `Syxx.app` (Apple Development cert). Pre-existing
  `PurpleScrollView.swift` actor warnings only; no new warnings from this change.

### Shared foundation (Syxx.Kit, consumed by Windows + Linux)

- `Windows/dotNET/Syxx.Kit/ProphetConsensus.cs` (new): `ProphetOracleRoster`
  (the 11 public oracles, identical to the macOS list) + `ProphetResult` record
  (ResolverResponses + ConsensusData with nullable MedianLatencyMs, B231) +
  `ProphetConsensus.RunAsync(domain, qtype, localServer, medianLatencyMs)` which
  runs the 12-oracle parallel fan-out (Task.WhenAll), sorts Local first, and
  returns the honest active-oracle count. Parity twin of macOS
  `AppModel.runLookup` (AppModel.swift:1011-1047). macOS keeps its app-local copy
  as the gold standard; Syxx.Kit gains this helper so Windows and Linux do not
  duplicate the roster or the fan-out.
- `Windows/dotNET/Syxx.Kit/ProphetPresentation.cs` (new, by winui3-architect):
  `EvaluateConsensus` + `BuildConsensusLabel` + `BuildDnssecPresentation` — the
  consensus + DNSSEC tri-state presentation as Kit types returning `ColorKey`
  strings (so the headless Kit stays WinUI-brush-agnostic). Parity twin of macOS
  `ProphetView.evaluateConsensus` / `prophetConsensusLabel` / `dnssecPresentation`
  (ProphetView.swift:127-211, 326-371). Em-dashes in macOS titles replaced with
  colons per AGENTS.md. Kit builds clean (0 errors).
- `Windows/dotNET/Syxx.Tests/ProphetPresentationTests.cs` (new, 20 tests): focused,
  deterministic, offline. Covers all 4 consensus states + all 4 DNSSEC states + the
  B231 null-latency gate + the empty-domain "this domain" substitution + the
  "no 'verdict' word" + "no em-dash" honesty invariants. Mutation-proven per Protocol
  §3: removing the `"this domain"` empty-domain guard turns
  `Dnssec_EmptyDomain_RendersAsThisDomain_NeverBareBraces` red (detail becomes "
  returned no DNSKEY..." with no subject); restore makes all 20 green. 20/20 pass.

### Windows (WinUI 3, by winui3-architect)

- `Windows/dotNET/SyxxUI/ViewModels/AppViewModel.cs`: added
  `ProphetConsensusResult` (ProphetResult?) + `DnssecStatus` (DNSSECStatus?)
  observable properties, cleared at scan start (parity macOS runLookup:967);
  `RunProphetAsync` now calls `ProphetConsensus.RunAsync` after ProbePlan and sets
  `DnssecStatus = plan.Dnssec` (reusing ProbePlan's already-fetched DNSSEC posture
  to avoid a second DO-enabled DNSKEY round-trip; the macOS gold standard makes a
  fresh query at AppModel.swift:1047, both are honest). D278 unmeasured-resolver
  gate preserved unchanged.
- `Windows/dotNET/SyxxUI/Views/ProphetView.xaml` + `.xaml.cs`: complete rewrite to
  match the macOS structure. REMOVED the divergent elements: top-right "Resolver:"
  line, "Prophet can only validate DNSSEC signatures..." warning banner, "LOCAL
  DNS ENCRYPTION" card, side-by-side 2-column records+DNSSEC grid. ADDED the rune
  header + lore subcaption + body paragraph, the domain selection card lore, the
  stacked resolver consensus card + DNSSEC card (below, not side-by-side), the
  empty state. XAML validated well-formed via `xmllint`.
- Build: Kit + Tests projects compile clean on this macOS host (0 errors). The
  SyxxUI WinUI XAML compile is HOST-GATED (XamlCompiler.exe is a Windows binary
  and produces no output on macOS — accepted gap per MEMORY.md). A full SyxxUI
  compile + native launch is owed on a Windows host.

### Linux (Avalonia, by avalonia-architect)

- `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ProphetViewModel.cs`: added
  `ProphetConsensusResult` + `DnssecStatus` observable properties, cleared at scan
  start; `CheckAsync` calls `ProphetConsensus.RunAsync` after ProbePlan; consensus
  - DNSSEC presentation translated inline in the VM (tri-state, em-dashes replaced
  with colons, Inconclusive never red). D278 gate preserved.
- `Linux/Ubuntu/SyxxUI.Avalonia/Views/ProphetView.axaml`: complete rewrite to match
  the macOS structure (rune header, domain card, stacked consensus + DNSSEC cards,
  empty state). Uses registered Linux font resources (Cinzel, EB Garamond Italic,
  Cormorant Garamond, JetBrains Mono).
- `Linux/Ubuntu/SyxxUI.Avalonia.Tests/ProphetViewModelPresentationTests.cs` (new, 12
  tests): DNSSEC tri-state + consensus evaluation, all pass. `dotnet test` 123/123
  (12 new + 111 existing, no regressions).
- Build: `dotnet build Windows/Syxx.Linux.slnf` passes 0 errors (cross-builds on
  macOS; Avalonia is cross-platform).

### Parity decision to flag for the owner

The Windows agent extracted the consensus + DNSSEC presentation logic to a shared
`ProphetPresentation.cs` in `Syxx.Kit` (single source, testable via Syxx.Tests,
returns ColorKey strings). The Linux agent ran in PARALLEL and inlined the
equivalent logic in `ProphetViewModel.cs` (it could not see ProphetPresentation.cs
being created concurrently). This is a parity smell: two copies will drift. The
owner should decide whether to (a) have Linux consume `ProphetPresentation.cs`
from the Kit (removing the inline duplicate, adding a small ColorKey-to-IBrush
mapper) — recommended; or (b) keep Linux's VM-inlined approach (closer to the
macOS gold standard which also keeps the logic in the view). NOTING, not acting:
the per-pane STOP gate leaves this for owner review.

### Verification summary

| Platform | Build | Tests | Host-gated |
|---|---|---|---|
| macOS | `swift build` + `-c release` green; `bundle-app.sh debug`+`release` signed | n/a (view layer) | native |
| Windows Kit | 0 errors | `ProphetPresentationTests` 20/20 (mutation-proven) | none |
| Windows SyxxUI | XAML valid XML; .cs compiles against Kit | n/a | full WinUI compile needs Windows host |
| Linux Avalonia | `Syxx.Linux.slnf` 0 errors | Avalonia Prophet 12/12; full 123/123 | none |

### Owner-review flags (per-pane STOP gate, MEMORY.md:1785, 1883-1891)

1. **"begans"** in the subcaption lore (MEMORY.md:1814) is the owner's verbatim
   text, used as-is on all three platforms. Likely a typo for "begins"; flagged
   for owner decision, not auto-corrected.
2. **12-oracle real-run gap**: `ProphetConsensus.RunAsync` issues 12 UDP:53
   queries to public resolvers; outbound UDP:53 is blocked in this environment
   (MEMORY.md:569). The presentation logic is verified offline (32 tests across
   Windows + Linux); the end-to-end network path needs a real run on a host with
   open UDP:53 (the macOS VM NAT gateway resolver 192.168.64.1 answers, but the
   public oracles time out here).
3. **MEMORY.md:1869-1871 might be stale**: both subagents reported Cormorant
   Garamond IS registered on Windows (`IoTypeBody`) and Linux (`SyxTypeBody`),
   contradicting the "still Jost" note. If confirmed, the body-font parity gap
   is already closed (body text uses Cormorant Garamond on all three platforms,
   matching macOS).
4. **Marcellus SC** not registered on Windows or Linux (deliberately role-less
   per MEMORY.md:1864-1865). The Prophet pane doesn't use it (card titles use
   Cinzel/display); flagged as a Phase C gap, not a Prophet blocker.

## Wave 2a macOS Dimensions shell and Dashboard (2026-08-02)

- `ContentView.swift:55-104` now owns one 35-point horizontal and top origin for every non-MOTD
  pane. Child pane outer padding was removed from `ProphetView.swift`, `MirrorView.swift`,
  `PortalView.swift`, `GluttonView.swift`, `WitnessView.swift`, `ConclusionsView.swift`,
  `SettingsView.swift`, and `LicenseView.swift`.
- `ContentView.swift:134-331` now uses the bundled `Syxx-logo.png`, a side-by-side 130×65 wordmark
  block with a 5-point gap, the owner-locked `Dimensions` eyebrow, the #FF2D78 to #FF3DD5
  wordmark gradient, fixed-height sidebar rows, and a pinned footer without selection or hover
  layout changes.
- `DashboardView.swift:34-269` no longer wraps the Dashboard in a full-panel scroll container.
  Compact spacing, tile sizing, and the activity pane height keep the Dashboard inside the 1050×613
  content floor while preserving live model-derived states and the nested activity log.
- `IoDesignSystem.swift:147-152, 936-938, 961` owns the canonical wordmark gradient, Spectral
  `navLabel` styling at 15 points.
- Verification: focused Dashboard, shell-contract, and scrollbar suites passed 29/29; TypographyRegistrationTests
  passed 8/8; TypographyResourcesTests passed 15/15; debug and release Swift builds passed;
  `bundle-app.sh debug` passed; a real bundled launch produced a 1050×645 window and a captured
  Dashboard with no outer scrollbar.

## Phase 0 coverage audit + F071 stale typography contract fixed (2026-08-02)

- **Phase 0 (macOS font registration) audited end-to-end against source, not MEMORY.md.**
  Covered and verified: 23 flat faces at `Sources/SyxxApp/Resources/` root; 8 OFL licenses;
  `SyxxTypography.bundledFonts` manifest matches the files on disk; fail-loud registration
  (`SyxxApp.swift:27,49-91`, tri-state `FontRegistrationReport`, `IoDesignSystem.swift:404-521`);
  packaging gate (`bundle-app.sh:123-156`, no `|| true` on `.ttf`, manifest-driven assertion);
  `Package.swift:66` ships Resources via `.copy`. `TypographyRegistrationTests` 8/8 green.
- **Found red: `TypographyResourcesTests` (8 assertions / 4 tests).** Wave 1 retargeted body to
  Cormorant Garamond and added `navLabel` + three bundled families without updating the Phase 3.7
  contract (still expected Jost body, 13 faces, 10 roles, 14pt). Filed as **F071**, classified
  test-design error per Honesty Protocol §4. Contract updated to the owner-approved mapping;
  mutation-proven (body → Jost-Regular turns exactly the two contract tests red; revert green).
- **Role mapping confirmed against owner decision:** body/bodyLight = Cormorant Garamond 300/400;
  card labels = Cormorant SC (bodyAlternate/deck); navLabel = Spectral Light 300 @15;
  uiLabel/eyebrow = Jost Medium; Marcellus SC bundled, registered, deliberately role-less.
  Stale comments claiming otherwise corrected (`IoDesignSystem.swift` body case, `SyxxApp.swift:23`).
- **Verification:** `TypographyResourcesTests` 15/15 green; `TypographyRegistrationTests` 8/8
  green; `swift build` and `swift build -c release` green (73.34 s).
- **Parity gaps (Phase C scope, not Phase 0):** Windows body still Jost (`IoDesignSystem.xaml:77-87`),
  Linux body still Jost (`App.axaml:166-173`); Cormorant Garamond nested and unreachable on both;
  no runtime registration-failure tests on either; `scripts/test-typography-assets.sh` is Jost-led
  and omits Cormorant Garamond/Spectral/Marcellus. All Phase 0 + Wave 1 work remains uncommitted.
- **Repo hygiene flagged to owner:** stray extracted font folders at the repo ROOT (`Cinzel/`,
  `Cormorant Garamond/`, `Cormorant_SC/`, `EB Garamond/`, `JetBrains_Mono/`, `Jost/`) are the
  download sources, untracked, awaiting owner decision to delete or relocate.

## D278 verification, real-run bar, and the Google Fonts thread (2026-08-01)

- **Verification completed for the D278/parity batch** (commits `69237bd`, `b38d71a`, `edcafab`,
  all on `main`, nothing pushed): Kit 6/6 + App 8/8 focused suites green; all 14 non-keychain App
  GUI suites passed (incl. StartupResolverState, GuidedSetupLifecycle, PurpleScrollView,
  MotdFallback, Stage5DisplayModel, TypographyResources). Pre-existing failures only: keychain
  `keychainError(-34018)` (AppModelWitnessTests, WitnessTestifyParityTests) and determinative-core
  matrix/probe suites. Debug build green 13.64 s; Release green 84.93 s (only pre-existing
  PurpleScrollView.swift:467-468 main-actor warnings). Linux `dotnet build -c Debug` 0 errors /
  45 warnings. Windows XAML compile remains host-gated on this macOS host.
- **Full-suite inventory proves the 92 failures are 100% pre-existing.** 629 executed, 3 skipped,
  92 failures / 37 unique tests, captured to `/tmp/syxx_full_run.txt` and `/tmp/syxx_failures.txt`.
  Representative failures re-run at parent `b38d71a` in a temp worktree go red identically, so
  nothing in the D278 file set contributes; the worktree was removed and pruned after.
- **Real-run bar met.** Fresh Release bundle `./macOS/bundle-app.sh release` built + code-signed,
  launched against fresh HOME `/tmp/syxx-freshhome.wLS7bD`. Post-fix `sample` shows the main
  thread 100% idle in the AppKit event loop with zero NetworkKeyState / runProcess /
  getSystemDNSServers / checkStartup / semaphore_wait_trap frames (the only 2 `semaphore_wait_trap`
  hits are 2,423 samples on an idle WebKit "Log work queue" worker). Relaunched via `open`
  (pid 77983, Dock-present); Quartz `CGWindowList` shows exactly one on-screen window
  (number 2126, 1050×645, layer 0). Visual guided-setup proof is soft: `screencapture -l 2126`
  failed (no screen-recording permission), so window-server + fresh-HOME evidence stands in.
- **Mutation proof executed and reverted cleanly:** removing `process.terminationHandler` makes
  `testFastPathReturnsOutputWithoutBurningTimeout` go red (returned 1.0044 s > 1.0 s bound);
  revert restores 6/6 green, `git diff` clean. No D278 change ships unmutation-proven.
- **New thread: stop whats-new WebViews from downloading Google Fonts.** The app already bundles
  the five families (Cinzel, Cormorant SC, EB Garamond, JetBrains Mono, Jost) as TTFs in all three
  code trees (`Windows/dotNET/SyxxUI/Assets/Fonts/`, `macOS/Sources/SyxxApp/Resources/`,
  `Linux/Ubuntu/SyxxUI.Avalonia/Assets/Fonts/`, 13 TTFs + 5 OFL each) and as base64 `@font-face`
  in the first `<style>` block of each `fallback-motd.html` (lines 8-18; second `<style>` lines
  19-248). But the live whats-new pages still pull Google Fonts via `Server/whats-new-mac.html:6`
  (plus win/deb twins) and ~11 other `Server/*.html` pages (incl. docs/help.html, docs/api.html).
- **Root cause of the platform inconsistency located.** whats-new is served with CSP
  `font-src data:` (app.py:1084). macOS `RemoteContentView` fetch-and-rewrites into
  `loadHTMLString`, which drops the CSP header, so Google fonts load on macOS; Windows loads the
  URL directly into WebView2, the CSP stays intact, the Google `<link>` is blocked, and Windows
  falls back to system fonts. Linux has no live WebView (Avalonia) so it is already covered.
- **Injection is not integrity-blocked.** `rg` for signature/ed25519/verify/D041/
  signatureBase64/x-syxx-signature found no hits in `RemoteContentView.swift`,
  `RemoteContentPanel.xaml.cs`, or `DimensionService.cs`. Owner scoping rule: "If the app loads
  the font, bundle in the app. If loaded on the website, bundle on the website."
- **Planned split (not yet coded or approved):** (a) macOS inject the five base64 `@font-face`
  blocks at the existing RemoteContentView rewrite step; (b) Windows add an equivalent
  fetch-and-rewrite in `RemoteContentPanel` (host-gated); (c) website drop the Google `<link>`
  and self-host byte-identical webfonts under an owner-approved `/fonts/*` route and CSP.
- `MotdLoadController.cs` read through line 54 (enum, `DeadlineSeconds=5.0`,
  `FallbackResourceName`, `ContentRoleMarker`, `ForbiddenFallbackNames`, `Generation`,
  `BeginRemoteLoad`, `RemoteSucceeded`); `ActivateFallback` + `ReadPackagedFallbackHtml` still
  unread.
- Open: fonts design approval, MotdLoadController remainder, cold-launch timings,
  knowledge-synthesizer lesson, push (owner-gated).

## Workstream 1.0: D278 launch deadlock fixed (2026-08-01)

- **D278 landed in code.** `runProcess(_:_:timeout:)` no longer relies on the cancelled-timer
  semaphore handshake. It now uses a `RunProcessState` holder (`exited` + `drained` semaphores,
  macOS analog of the Windows twin's bounded `WaitForExit(TimeSpan)` at
  `NetworkKeyState.cs:318`), a `terminationHandler` set before `run()`, a concurrent pipe-drain
  thread (prevents the >64KB pipe-buffer deadlock and honors the C112 read-handle constraint),
  and a bounded wait whose result is the process status. Timeout escalates SIGTERM → 0.5s →
  SIGKILL, then reaps with `waitUntilExit()`. Timeout and non-UTF-8 output still return nil
  (unchanged contract; all ten callers untouched).
- **Startup is off the main actor.** `checkStartup()` stays synchronous-void but runs the
  resolver probe in a `Task.detached`, publishing via a single-writer `applySystemResolverProbe`
  under `MainActor`. `server` initializes to `""` (no invented 8.8.8.8); `resetAll()` keeps the
  current measured value. Until measured, `resolverState` is `.checking`; a failed probe is
  `.unavailable`. The `runLookup()` gate (new behavior) refuses to run Prophet against an
  unknown resolver with owner-approved copy; PortalView renders "Checking system DNS servers…"
  during the probe window instead of the misleading empty-pane copy.
- **Tests with failure cases.** `NetworkKeyStateRunProcessTests` (6 tests): fast echo returns in
  ms (regression would hang the 1s XCTest expectation), timeout returns nil at the deadline with
  no surviving child, >64KB chatty child drains without deadlock, launch failure and non-UTF-8
  both return nil. `StartupResolverStateTests` (8 tests): `checkStartup()` returns without
  blocking the main actor and the resolver cache is unknown until populated. All green; Debug
  and Release builds both complete.
- `outputs-catalog.csv`: resolved an embedded (committed-in-error) merge-conflict region by
  keeping the newer stashed superset; appended the three approved D278 strings
  (`AppModel.swift:940`, `AppModel.swift:943`, `PortalView.swift:14`).
- **Cross-platform parity of the unknown-resolver gate.** The stage-3 parity report (FAIL) found
  Windows and Linux still fabricated a resolver when the system resolver was undetectable
  (Windows `1.1.1.1`, Linux `8.8.8.8`) where macOS now gates. Per owner decision, parity was
  fixed first: Windows `RunProphetAsync`/`RunMirrorAsync`/Witness seal (`AppViewModel.cs:393-411,
  459-467, 728-737`) and Linux `ProphetViewModel`/`MirrorViewModel`/`WitnessViewModel`
  (gates at ProphetViewModel.cs:33-41, MirrorViewModel.cs:39-48, WitnessViewModel.cs:108-116) now
  refuse to audit an unmeasurable resolver using the same owner-approved gate string, and the old
  fallback-warning strings were removed. Windows views trim the trailing period on the wrap
  (ProphetView.xaml.cs:106, MirrorView.xaml.cs:87). Filed as C198 (Linux; Windows sites were
  already C173/C189). Linux compiles clean; Windows XAML compile is host-gated on this macOS host.
- Open: parity report, post-fix `sample` evidence, cold-launch timings, guided-setup fresh-HOME
  real run, knowledge-synthesizer lesson.

## Phase 5 exit notes (2026-08-01, local)

Partial Phase 5 on this host (macOS). Windows GUI installers and Linux clean-host install remain host-gated.

| Check | Result |
|-------|--------|
| macOS Debug + Release | PASS |
| Server 371 tests | PASS (earlier this session) |
| PurpleScroll + Dashboard ticker + GuidedSetup + Mirror state tests | 41/41 PASS |
| `syxx parity-transcript` macOS vs .NET | MATCH (empty SectionInputs → untested, hard-gated) |
| AppModelWitnessTests B255 | PARTIAL — keychain (-34018) blocks key-dependent cases without entitlements; injection seam is in code |
| Final DMG/deb/MSIX on clean hosts | NOT run here |

Honesty gate (changed workstreams): no conclusion before evidence on empty-input transcript; no green chip on inconclusive Mirror; no fake secure from empty sections.

## Phase 4.4: packaging definitions (2026-08-01)

- `Linux/Ubuntu/build_deb.sh`: ICU Depends is distribution-aware (libicu74/76/72 via dpkg-query, or `SYXX_DEB_ICU_DEPENDS` override). Unsupported distro fails closed instead of shipping uninstallable `libicu74`.
- Authoritative Windows packaging project remains `Windows/dotNET/SyxxPackage/` (verified present). Final artifact build deferred to full Phase 5 on a Windows host.

## Phase 4.3: documentation corrections (2026-08-01)

- `README.md`, `docs/BUILDING.md`: `Ubuntu/SyxxUI.Avalonia` → `Linux/Ubuntu/SyxxUI.Avalonia`
- `docs/USER-GUIDE.md`: `syxx firstrun` documented as CLI-only; GUI does not auto-scan on first launch
- README notes source parity + blocking behavioral transcript gate

## Phase 4.2: blocking behavioral transcript parity (2026-08-01)

- Emitters call production synthesizers only:
  - macOS: `syxx parity-transcript` → `Synthesizer.conclude` (fixed epoch)
  - Windows/Linux: `syxx parity-transcript` → `Synthesizer.Conclude`
- Schema v1: vectorId, evidenceCompleteness, determinationState, diagnosisIds, hasHardGatedSection, confidencePresent (bool, never invented)
- CI: emit on each matrix leg; upload fails if missing; `parity-transcript` job is **blocking** (no continue-on-error)
- Real run: macOS and .NET empty-input transcripts byte-match after `jq --sort-keys`

## Phase 4.1: B255 / B256 host independence (2026-08-01)

- macOS: `WitnessVantageSeam` injects fixed/failed/noResult comparison for deterministic unit paths (`AppModel.swift`)
- Windows: `MonitorSchedulerTests` uses injected tick (no `Task.Delay(90)` wall clock)
- B255 keychain-dependent cases still need a signed-in keychain environment; classification strings are covered when the seam is reached

## Phase 3.8: PurpleScrollView app-wide (2026-08-01)

- `PurpleScrollbarGeometry`: fullPanel 10×420, compact 10×50; clamp without silent switch
- Fill rgba(150,116,235,0.35); glow 0.55 @ 6pt; track transparent; corner radius 4
- Live logs: `scrollToBottom` only when user pinned to bottom
- Call sites assigned geometry (Portal/Prophet/Settings/Dashboard/Conclusions/Witness/Glutton/Cage/Mirror full; EULA/terminals/nested compact)
- WKWebView bridge: `PurpleWebScrollBridge.applyFullPanelScroller`
- Tests: `PurpleScrollViewTests` 11/11 including raw-ScrollView source guard and geometry mutation cases

## Phase 3.4: Dashboard ticker + honesty chips (2026-08-01)

- Cage ticker: `cageEventsCount % 1000` via `DashboardWardenState.cageCaptureDisplayCount`
- Chip tones: inconclusive/degraded never success green (`chipTone`)
- Tests: `DashboardTickerTests` (0/999/1000/1001 + modulo failure case)

## Phase 3.3: Guided setup lifecycle (verify + layout polish) (2026-08-01)

- Lifecycle contracts remain: safeDefaults, encryption OFF default, skip==screen, encryption-before-ready, no auto-scan
- Setup UI: Story/General cards, defaults panel, Skip/Begin; PurpleScrollView geometry assigned
- `GuidedSetupLifecycleTests` restored and green in focused suite

## Peer-eval corrections applied (2026-08-01)

Live-tree audit of Phases 1–2.3 peer feedback:

- **Plan:** `REIMPLEMENTATION-PLAN-2026-08-01.md` baseline corrected: 1.1 tail-cut diagnosis (do not invent accessibility copy); 1.2 file list uses `warp.py`/`resolver.py` (not nonexistent `diff.py`/`doh.py`) and `3acf61c` evening extract; B252 / ContentWarp+URL policy / C141 moved to **verify-only freeze**; Mirror "false-tampered" not promoted without repro.
- **Tests restored/fixed:** `GuidedSetupLifecycleTests` returned to `SyxxAppTests` with async/`CallCounter`/`isRunning` compile fixes and explicit failure cases; `Stage5PresentationTests` + `Stage5PopulatedParityTests` restored; legacy Stage5 empty-conclusion assertion updated for current `Conclusion` (no fabricated clean).
- **Stage5 naming:** `HardGateSignal.blockedVerdicts` / `VerdictState` renamed to `blockedResults` / `DeterminationState` to match wire JSON `blockedResults` and the no-"verdict" rule.
- **Process:** agents must read live symbols before coding; green fail-first steps mean verify/mutate, not rewrite.

## Phase 3.5: macOS Stage5DisplayModel (2026-08-01)

Pure Stage-5 presentation adapter over the engine DTO. Does not recompute or strengthen engine results.

- Kit DTO restored: `macOS/Sources/SyxxKit/Synthesis/Stage5Presentation.swift` (CX-1/2/3 types, §13 validator, canonical encode/decode, `Hypothesis` wire labels)
- Presentation adapter: `macOS/Sources/SyxxApp/Presentation/Stage5DisplayModel.swift`
  - fail-closed `make(from:)` (nil absent/invalid → unavailable, never fabricated clean)
  - direct / inferred / unavailable partition rows
  - cross-vantage matrix rows (classification/outlier/consensus from engine only)
  - distinct hard-gate, conflict, uncertainty honesty lines
  - mechanism class, within-class secondaries by `displayOrder`, RPN findings by engine rank
- Preview fixture: `macOS/Sources/SyxxApp/Presentation/Stage5PreviewFixture.swift`
- Copy table: `macOS/Sources/SyxxApp/Presentation/Stage5Copy.swift` (approved STAGE5 wording)
- Tests: `macOS/Tests/SyxxAppTests/Stage5DisplayModelTests.swift` — empty, partial, hard-gate, conflict, low-separation, complete-evidence, plus fail-closed paths (20/20 green)
- Pre-existing compile-broken Kit tests moved to `macOS/Tests/_quarantine/*.phase3-compile-break` (ContinuousMonitor, CNAMEChain, RoundingParity, ScorersContract, GuidedSetupLifecycle)

## Phase 3.6: MOTD five-second fallback (2026-08-01)

Remote MOTD has a shared five-second deadline; packaged `fallback-motd.html` loads in the same WebView. Hero imagery preserved.

- Canonical asset (byte-identical SHA-256 across three platforms), meta marker
  `syxx-content-role=motd-timeout-fallback`:
  - `macOS/Sources/SyxxApp/Resources/fallback-motd.html`
  - `Windows/dotNET/SyxxUI/Assets/fallback-motd.html` (EmbeddedResource)
  - `Linux/Ubuntu/SyxxUI.Avalonia/Assets/fallback-motd.html`
- macOS: `BetaView` restores remote MOTD first (`mode: .motd`); `RemoteContentView` + `MotdLoadController` generation-token race; fallback resolver is only `fallback-motd` (never the forbidden local substitutes for MOTD)
- Windows: `RemoteContentPanel` five-second deadline around WebView2 init/navigation; `MotdLoadController.cs`; embedded `fallback-motd.html`
- Linux: `WhatsNewViewModel` five-second remote budget + packaged fallback selection; native offline panel with logo hero until Avalonia multi-RID WebView is pinned (parity gap)
- Tests: `macOS/Tests/SyxxAppTests/MotdFallbackTests.swift` (11/11 green) — deadline, late-callback suppression, generation isolation, resource identity, source guards
- Builds: `swift build`, `swift build -c release`, `./bundle-app.sh` (Syxx.app includes Resources/fallback-motd.html)

## Phase 3.7: Jost-led type system (2026-08-01)

Cross-platform typography migration from the retired Archivo / Space Grotesk / Space Mono primary faces to the specimen-authoritative five-family system.

### Semantic roles (all platforms)

| Role | Face | Notes |
|------|------|-------|
| `display` / `sectionHeading` | Cinzel Bold / SemiBold | caps, never body, never below 18pt |
| `bodyLight` / `body` | Jost 300 / 400 | restricted body enum; no 500–900 body path |
| `bodyAlternate` / `deck` | Cormorant SC 400 / 600 | authored alternate, not missing-Jost fallback |
| `emphasis` | EB Garamond Italic | ~host + 2pt |
| `uiLabel` / `eyebrow` | Jost 500 | ordinary UI never 600+; eyebrow 10pt / 0.26em / uppercase |
| `technical` | JetBrains Mono | paths, IPs, hashes, logs |

### Platform work

- **macOS** `IoDesignSystem.swift`: `SyxxTypeRole`, `SyxxBodyWeight`, semantic Font API; Archivo registration and call sites removed; Cinzel sizes clamped to ≥18; static Jost Light/Regular/Medium packaged under `Resources/` with OFL texts; Debug + Release bundles pack Jost (no Archivo).
- **Windows** `IoDesignSystem.xaml`: `IoType*` resource keys; `IoFontUI` / `IoFontGrotesk` alias to Jost; Archivo assets retired from `Assets/Fonts/`.
- **Linux** `App.axaml` + `AcceptanceAlertWindow.cs`: `SyxType*` keys; UI face is Jost Regular.
- **Web / offline HTML**: public pages use the approved `display=swap` Google Fonts request; bundled `fallback-mac.html` and platform `fallback-motd.html` embed local `@font-face` for Jost/Cinzel/Cormorant SC/EB Garamond/JetBrains Mono (MOTD hero markup untouched).
- **Guard**: `scripts/test-typography-assets.sh` requires Jost faces + OFL, byte-identical cross-platform hashes, and fails on Archivo/Space* reintroduction or missing weights.
- **Tests**: `TypographyResourcesTests` (15 cases) cover packaging, role→PostScript maps, body weight restriction, Cinzel floor, emphasis sizing, eyebrow tracking, and retired-face absence. Mutation cases documented; shell-guard mutations (Archivo inject, missing Jost-Light) exit non-zero.

### Explicitly not in this workstream

- PurpleScrollView (3.8)
- MOTD load/timeout logic (other agents)
- User-facing copy changes

## Phase 3.2: authenticated G-1 updater foundations (2026-08-01)

Finish the authenticated auto-update channel so no client downloads before signature/metadata
validation and no client installs before package-hash validation. Collector identity is no longer
used for appcast signing.

### Server

- `Server/syxx_server/appcast.py` rewritten:
  - Dedicated update-signing Ed25519 key (env `SYXX_UPDATE_PRIVATE_KEY` / path, or DEV-only
    `Server/dev-keys/` when `SYXX_ALLOW_DEV_UPDATE_KEY=1`). Never collector/leak key.
  - `publish_package()` atomically persists per-platform JSON under `UPDATE_PACKAGE_DIR` with
    platform, type, version, build, allowlisted HTTPS URL, lowercase SHA-256, nonzero size,
    minVersion, publishedAt, release notes.
  - `build_appcast()` refuses (returns None → 404) when any required field is missing/invalid;
    signs exact `_canon` bytes clients re-derive.
- `Server/syxx_server/config.py`: update-key and package-dir settings; platform URL defaults end
  with `.dmg` / `.zip` / `.deb` so publish can succeed without a custom URL.
- `Server/dev-keys/`: DEV-ONLY keypair + README (production rotation is owner-gated).
- Tests: `Server/tests/test_appcast.py` — 14/14 green (happy publish/sign, empty hash, zero size,
  disallowed URL, type mismatch, altered metadata, no collector key, no key → refuse).

### Clients

- macOS `AppcastVerifier.pinnedPublicKeysBase64` and C# `PinnedPublicKeysBase64` pin the DEV
  update public key `6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=` (never fetched from origin).
- `validatedDownloadURL` / `ValidatedDownloadUrl` also refuse zero size and type/extension
  mismatch before download.
- Download path verifies SHA-256 **and** signed size before install; install uses authenticated
  package type (not a guessed extension alone).
- C# `DownloadWithProgressAsync` chooses `.zip` / `.deb` / `.dmg` from authenticated type
  (no longer forces every package into a `.zip` temp path).
- Windows `UpdatePanel` uses `UpdateService.CheckForUpdatesAsync` + `DownloadAndInstallAsync`
  only; unauthenticated browser `LaunchUriAsync` path removed.
- Linux Avalonia: Settings → Updates card (`UpdateViewModel`) on the same authenticated service;
  `.deb` type accepted. Reuses existing UpdatePanel copy only.
- macOS UpdatePanel was already on `UpdateService` (unchanged behaviorally).

### Tests

| Suite | Result |
|-------|--------|
| Server `test_appcast.py` | 14/14 |
| macOS `UpdateSignatureVerificationTests` | 19/19 |
| Windows `UpdateSignatureVerificationTests` | 19/19 |
| Real run publish+sign mac/win/deb + incomplete refuse | PASS |
| Linux Avalonia build | PASS |
| Windows SyxxUI WinUI build | not on this host (XAML compiler requires Windows) |

Failure cases covered: wrong key, missing pin, malformed sig, altered metadata, empty hash,
hash mismatch path, zero size, disallowed URL, type/extension mismatch.

### Gaps

- Full native install of a real DMG/ZIP/DEB on each host not executed here (macOS install helpers
  remain; Windows host install not available on this Darwin agent; Debian dpkg path untested live).
- Production update key still DEV-pinned; owner must provision production key offline and rotate
  pins on all three clients together.
- Interrupted-download and rollback integration tests not expanded beyond existing backup-restore
  install helpers.
- Several pre-existing macOS Kit test files fail to compile against current APIs; additional
  units parked under `macOS/Tests/_quarantine/*.phase32-*` so the update suite can run. Not a
  Phase 3.2 production defect.

## Phase 1 exit gate (2026-08-01)

Phase 1 foundation recovery is complete for the workstreams that can run on this host.

| Gate | Status |
|------|--------|
| macOS Debug + Release compile | PASS (`swift build`, `swift build -c release`) |
| GUI first-run scan absent | PASS (no `firstRunScanning` / `firstRunProgress` / `firstRunCard` in SyxxApp) |
| Sixteen server acceptance behaviors | PASS (371/371, see Phase 1.2) |
| deploy.sh macmotd/winmotd/debmotd + ACL renewal | present (unchanged) |
| Dashboard Mirror cannot PASSED on redirection | PASS (Phase 1.3 + contract source-guard test) |
| Real server investigate + incomplete body | PASS (Phase 1.2 real-run table) |
| Verify-only baselines (matrix/Cage/Witness) | NOT fully re-run via monorepo `swift test`: multiple Kit tests fail to **compile** against current APIs (`Hypothesis`, `AnswerMatrixRow`, `Stage5Presentation`, `ConditionState` bools). Broken compile units moved to `macOS/Tests/_quarantine/*.phase1-compile-break` so production still builds. Remaining drift is Phase 3/4 repair work, not a production rewrite. |

## Phase 1.3: Dashboard Mirror false-pass removed (2026-08-01)

Mirror chip on the macOS Dashboard no longer returns `PASSED` when only inconclusive/degraded were checked and redirection was ignored.

- `DashboardWardenState.mirror(isPro:result:)` pure helper (`macOS/Sources/SyxxApp/DashboardWardenState.swift`)
- `DashboardView.wardenState(for:)` Mirror branch delegates to the helper
- Order: locked → not run → redirection (`TAMPERING DETECTED`, existing MirrorView copy) → inconclusive → degraded → PASSED
- Tests: `macOS/Tests/SyxxAppTests/DashboardMirrorStateTests.swift` (not-run, locked, inconclusive, degraded, matching, redirection, mutation proof vs old false-pass predicate)
- Contract + source-guard script: `ALL DashboardMirror contract checks PASS` (proves production file contains `isRedirectionDetected`)
- Windows/Linux Dashboard do not implement the same warden chip path; MirrorView already uses `IsRedirectionDetected` for tampering labels

## Phase 1.1: ConclusionsView compilation restored (2026-08-01)

`ConclusionsView.swift` was structurally incomplete after the owner-directed first-run scan removal (missing struct close + `resultAccessibilityLabel`).

- Restored `resultAccessibilityLabel(_:)` from the pre-scan-removal donor accessibility contract (no new user-facing copy)
- Closed the struct; kept `#Preview` under DEBUG
- Did **not** restore `firstRunCard`, `firstRunScanning`, `runFirstScan`, or any automatic GUI scan
- `swift build` and `swift build -c release` green for SyxxApp/syxx-gui

## Phase 2.1: canonical DoH signing — B252 fixed on both platforms (2026-08-01)

B252 (`comparison.dohOutcomes` neither signed nor display-guarded) is fixed by bumping the
signed evidence format to v4 and signing DoH evidence canonically. No user-visible wording
changed: the existing DoH "UNSIGNED" banner is now gated to pre-v4 snapshots only.

- `SignedEvidencePayload.currentFormatVersion = 4` on both platforms; verifiers accept
  {2, 3, 4}. Signers emit v4 (macOS `WitnessSigner.swift` build() now receives
  `currentFormatVersion`; C# `CreateSignedSnapshot` defaults to it).
- Canonical `dohOutcomes` (`DoHOutcomesCanonical{outcomes}`) populated at v4+ from the display
  comparison: typed `success` derived from status, resolverId-sorted, errorMessage-free
  (v3 semantics). v2/v3 signed bytes are unchanged (field stays nil).
- C# `PerResolverAnswers` twin added to `SignedEvidencePayload` (closing the parity gap where
  C# re-encode dropped the Swift-only field and every macOS snapshot read `InvalidSignature`),
  populated at ALL versions with records sorted by (name, type, ttl, rdata) and resolver list
  sorted by resolverId — byte-identical to the Swift build.
- Display-integrity guard now compares `perResolverAnswers` at every version and `dohOutcomes`
  at v4+ (`WitnessSigner.swift` / `WitnessSigner.cs`); pre-v4 snapshots keep verifying.
- C051 wire regression fixed as a precondition: `VantageComparer` on C# had read-only computed
  bools over a `Result` enum, so macOS storable booleans (`isInconclusive`/`isDegraded`/
  `isRedirectionDetected`) could not bind — state was lost on decode. Now settable with
  `[JsonPropertyName]` keys that drive the enum.
- Fixtures regenerated at v4 (`witness-macos-answers.json`, `witness-macos-degraded.json`);
  the pre-v4 answers file preserved verbatim as `witness-macos-answers-v2legacy.json` and added
  to both platforms' golden-vector lists (proves legacy acceptance).
- E011 v4 anchors added on both platforms: shared canonical literal with `dohOutcomes` +
  `perResolverAnswers`, pinned Swift and C# signatures (fixed 32×0x42 seed, pubkey unchanged
  `IVL40Zt5HSRFMkLhXy6rbLfP+ntqXtMAl5YOBpiB2xI=`), plus failure cases proving rewritten
  canonical DoH and swapped per-resolver answers both invalidate the signature.

Verification:

- Windows focused suite (`WitnessCrossPlatformGoldenVectorTests | SignedEvidencePayloadTests |
  WitnessSignerTests | WitnessCrossPlatformWireTests`): 56/56 green. macOS scratch witness
  suite (E011ByteParityTests + WitnessCrossPlatformGoldenVectorTests): green, including the
  flipped KnownGap (`NotEqual(.valid)`) which was honestly RED against v2 fixtures and GREEN
  once v4 canonical DoH is signed and guarded.
- Byte parity: C# canonical bytes == Swift canonical bytes for the v4 anchor
  (`V4_BYTE_IDENTICAL_TO_SWIFT=True`); Swift-pinned signature verifies over C#-produced bytes.
- Real run (Windows CLI, production path): `syxx witness verify` → `[VALID] Tamper-evident`
  on the v4 degraded, v4 answers, and v2 legacy fixtures with inconclusive/degraded state
  preserved; tampering canonical `evidence.dohOutcomes` in a v4 fixture →
  `[TAMPERED] DISPLAY MISMATCH`.
- Mutation: pre-fix, the flipped KnownGap is RED on both platforms (rewritten display DoH still
  `valid`); post-fix the display guard + signature catch it.

## Phase 2.3: preserve unknown ARP states on macOS (C141) (2026-08-01)

Unrecognized ARP state tokens with otherwise-valid fields no longer collapse to
`reachable`. `ARPTableParser.state(for:tokens:)` now:

- maps incomplete / invalid / probe / failed to `.incomplete`
- maps permanent / static / noarp to `.permanent`
- maps reachable / dynamic / stale / delay to `.reachable`
- treats residual non-structural tokens as unknown state labels
- keeps the Darwin `arp -an` default (valid MAC, no residual state token) as `.reachable`

Files:

- `macOS/Sources/SyxxKit/LAN/ARPTableParser.swift` (`state(for:tokens:)`, residual token scan)
- `macOS/Tests/SyxxKitTests/LANLifecycleTests.swift` (recognized states, canonical MAC,
  invalid MAC, unrecognized/localized/expired remain unknown)

Verification:

- Focused suite `LANLifecycleTests`: 23/23 green (standalone FocusTests package against
  live SyxxKit; monorepo `swift test` still blocked by pre-existing broken synthesis tests)
- Mutation: restoring unconditional `if canonicalMACAddress != nil { return .reachable }`
  turns `testUnrecognizedARPStateTokenRemainsUnknown` red (reachable is not unknown);
  restore then green
- Production `arp -an` on this host: rows reported as reachable / permanent only for measured
  tokens (for example `10.0.1.1 reachable`, `10.0.1.220 permanent`)

## Phase 2.4: complete machine-readable CLI process results (B253 remainder) (2026-08-01)

Every macOS subcommand now returns a typed process result. The dispatcher no longer forces
`exitCode = 0` after void runs. Shared contract with Witness:

| Code | Meaning |
|------|---------|
| 0 | completed non-threat |
| 1 | detected threat / tampering / confirmed leak / filter failure where applicable |
| 2 | inconclusive or invalid evidence |
| 3 | operational failure |

Inventory (macOS gold standard to process exit):

| Subcommand | 0 | 1 | 2 | 3 |
|---|---|---|---|---|
| witness | verified / help / create / report valid | tampered / display mismatch | stale / expired / untrusted / unsupported | missing file / misuse |
| mirror | clean / degraded (no tamper) | redirection | no baseline / no system resolver | usage / invalid domain / error |
| leak | notDetected | confirmed | no resolvers / no collector / inconclusive | all probes failed / missing creds / error |
| warp | structure match | content mismatch | inconclusive baseline | usage / locked / throw |
| nslook | query completed | (n/a) | (n/a) | usage / bad type / query fail |
| key | report completed | captive portal yes | (n/a) | (n/a) |
| route | hops produced | (n/a) | (n/a) | unsafe target / empty trace |
| lan | complete map | (n/a) | partial / unreadable | throw |
| forensics | lists / clean classify / analyze ok | flagged classify / exfil / beacon | empty parse | usage / I/O |
| sentinel | no change / admin ok | config change / not home | corrupt / empty resolvers | persist fail |
| cage | capture completed | (n/a) | aborted / evidence loss | open/create/privilege fail |
| filter | active and bypass covered | inactive / leaky / bypass open | no resolver / inconclusive | (n/a) |
| speed | benchmark completed | (n/a) | no system resolvers | (n/a) |
| unbound | pass / status | (n/a) | inconclusive enable/disable | fail / not installed / elevation / bad action |
| firstrun | clean determination | threatDetected | inconclusive / untested / nil det | bad args |
| --help / --version | always | (n/a) | (n/a) | (n/a) |
| unknown subcommand | (n/a) | (n/a) | (n/a) | always |

Windows parity this pass:

- `MirrorCommand.cs` and `WarpCommand.cs` set `Environment.ExitCode` to the same 0/1/2/3 map
- `LeakCommand.cs` and `WitnessCommand.cs` already mapped (preserved)
- Real `dotnet exec` on this host: `--help` 0, unknown 3, `mirror` misuse 3, `warp` misuse 3
- Windows `forensics` misuse still inherits default 2 (not yet typed; gap)

Help copy:

- **Not invented.** Top-level `syxx --help` does not yet document process codes. Pending
  owner-approved help block; Witness usage text is the template. Exit codes are wired first.

Files:

- `macOS/Sources/SyxxCLI/SyxxCLI.swift` (`CLIExitStatus`, dispatcher)
- `macOS/Sources/SyxxCLI/Commands/*` (all `run` return `Int32`)
- `macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift`
- `macOS/Tests/SyxxKitTests/CLIExitCodeProcessTests.swift` (spawns real `syxx`)
- `Windows/dotNET/Syxx/Commands/MirrorCommand.cs`, `WarpCommand.cs`

Verification (real executable, offline-safe paths; FAILS=0):

- `--help` / `--version` to 0
- unknown subcommand, nslook/mirror/warp/forensics misuse, route bad target, unbound bad action,
  firstrun `--local`, witness report missing to 3
- forensics `--lists` to 0; forensics `--classify doubleclick.net` to 1
- witness `--help` to 0 (preserved)
- Witness verify/report suites unchanged in source

Also: `DashboardWardenState.swift` qualifies `SyxxKit.ComparisonResult` to clear Foundation
`NSComparisonResult` ambiguity so the app target type-checks.

Gaps:

- Monorepo `swift test` does not link while pre-existing synthesis/sentinel tests fail to compile
- Commands that call `NetworkKeyState.getSystemDNSServers()` can hang in `runProcess` semaphore
  wait on this host (pre-existing; sample stack at NetworkKeyState.swift:158). Exit mapping is
  implemented; full live lan/key/leak/filter/speed process tests need that hang fixed separately
- Windows forensics and other non-Mirror/Warp/Leak/Witness commands still need typed ExitCode

## Phase 2.2: Windows fail-closed remote-content behavior (2026-08-01)

Windows ContentWarp and every navigation entry point fail closed through one policy.

**ContentWarp SSRF (typed inconclusive, never throw)**

- `Windows/dotNET/Syxx.Kit/ContentWarp.cs` — target and proxy SSRF refusal now return
  `Inconclusive(...)` (`IsSecure=false`, `IsInconclusive=true`, existing SSRF wording as
  `InconclusiveReason`). Removed both `throw new ArgumentException` paths.
- Twin of the macOS `WarpResult` inconclusive shape used for block pages and baseline
  failures. (macOS ContentWarp still throws on SSRF; Windows is now the stricter fail-closed
  path for this refusal class.)

**RemoteUrlPolicy navigation gate**

- `Windows/dotNET/Syxx.Kit/RemoteUrlPolicy.cs` — added `AuthorizeForNavigation(url, out Result)`
  returning `Uri?` only when Allowed. Refusal never yields a partial URI.
- All checkout, trial, update, broadcast, remote-content, Dimensions, Welcome, and About
  open paths route through the gate:
  - `LicenseView.xaml.cs` (trial/checkout + About hyperlinks; no more `NavigateUri` bypass)
  - `UpdatePanel.xaml.cs`
  - `MainWindow.xaml.cs` (broadcast Learn More + Dimensions)
  - `RemoteContentPanel.xaml.cs` (initial WebView load + external open)
  - `WelcomeView.xaml.cs`
  - `DimensionService.cs` (cached What is New pointer)
- Refusal reuses existing "Link Blocked" dialog + `eval.Reason` strings. No new user-visible copy.

**Tests (Kit-level, runnable on this macOS host)**

- `ContentWarpSsrfTests.cs` — SSRF-refused, null/malformed, no-throw, empty structural lists.
- `RemoteNavigationGateTests.cs` — allowed/disallowed origin, null/malformed, empty allowlist
  (policy-unavailable twin), hostile Dimension checkout override, update/broadcast/MOTD shapes,
  UI decision matrix, bypass-simulation documentation.
- Extended `RemoteUrlPolicyTests` for AuthorizeForNavigation allowed/refused.

**Mutation proof (performed, then restored)**

1. Bypass `AuthorizeForNavigation` (bare `Uri.TryCreate` always-allow) → 18 navigation-gate
   tests red (attacker https, http MOTD, empty allowlist, foreign update URL all admitted).
2. Restore ContentWarp target SSRF `throw ArgumentException` → 19/19 ContentWarpSsrfTests red.
3. Restored correct code → 70/70 focused green.

**Focused suite result**

```
dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj \
  --filter "FullyQualifiedName~ContentWarpSsrfTests|FullyQualifiedName~RemoteNavigationGateTests|FullyQualifiedName~RemoteUrlPolicyTests"
Passed!  - Failed: 0, Passed: 70, Skipped: 0
```

**Remaining gaps (host limits)**

- WinUI XAML (`SyxxUI`) does not compile on this macOS host (XamlCompiler). UI call-site
  edits are implemented in source; native allowed/blocked navigation on Windows 11 is still
  owed on a Windows host (`SyxxUI.Tests` / manual).
- macOS ContentWarp still throws on SSRF (not in Phase 2.2 scope). Parity follow-up optional.
- UpdateService download path already used `RemoteUrlPolicy.Evaluate` (unchanged).

## Phase 1.2: recover sixteen server acceptance behaviors (2026-08-01)

Restored the failing Server acceptance contracts without wholesale file replacement.
Full suite: `371` tests, `0` failures, `0` errors
(`OTEL_SDK_DISABLED=true PYTHONPATH=. venv/bin/python3 -m unittest discover -s tests`).

Production symbols:

- `fulfillment.handle_event` — idempotent claim path reads lease-aware state and falls
  back to `kv_get`; unknown/absent state after a lost claim returns `duplicate`
  (no double mint on concurrent, retry, or minted-undelivered redelivery).
- `licensing._verify_validate_key_response` — missing Keygen signature headers under a
  configured public key return `False` (deny), not cannot-verify/transient.
- `app._diff_result` — cross-host script injection is `tampered` again; clean-vantage-only
  disjoint DNS remains `inconclusive`; authoritative disjoint remains `tampered`.
- `app.Handler._body` — Content-Length via `get_all` when present, dict-safe fallback
  otherwise; incomplete → 400, oversized → 413.
- `app.Handler._dispatch_get` — `GET /api/leaktest/start` restored (token mint for free path).
- `app.Handler._doh_respond_json` — all-upstream failure is HTTP 200 with `Status: 2`
  (no fake clean / empty NOERROR).

Test fixtures aligned to current production kwargs/API (not softened assertions):
payments `kv_get_with_created_at` mock, diff/auth `deadline`/`do` kwargs, scheduler
`monitor_create(id, subject, domain, checks)`, fetcher chunked `read` EOF.

Real run (temp DB, ENFORCE_WEB off for harness only): investigate `example.com` →
`consensus: consistent`; incomplete body → `400 incomplete request body`; oversized →
`413 request body too large`; disjoint local vs clean vantage → `conclusion: inconclusive`;
`GET /api/leaktest/start` → token. `deploy.sh` already carries macmotd/winmotd/debmotd
and the cert ACL renewal hook (no change).

## Release-artifact build pass: Linux .deb, macOS DMG, Windows CLI (2026-08-01, Claude Code)

First cross-platform artifact pass from the restored working tree, on this macOS host:

- `dist/syxx_2.0.0_amd64.deb` (37 MB, Version 2.0.0-56) — Avalonia GUI, cross-built.
  Two build_deb.sh fixes en route: `SYXX_DEB_ARCH` override with fail-closed validation
  (Homebrew dpkg reports `darwin-arm64`, which is not a Debian architecture and would
  have been stamped into the control file), and `dpkg-deb --root-owner-group` (payload
  previously embedded the build user's uid 501, leaving /opt/syxx writable by an
  arbitrary uid on the target). Contents verified root-owned with launcher, desktop
  entry, and icon. **UNVERIFIED:** never installed or launched on real Ubuntu; the
  `libicu74` Depends line is still unconfirmed against a clean image.
- `macOS/dist/Syxx-2.0.0-56.dmg` (25 MB, with .sha256) — helper `__info_plist` +
  `__launchd_plist` sections verified, Info.plist bound, `codesign --verify --deep
  --strict` exit 0, bundled CLI reports 2.0.0 (the D113/PR-15 stale-CLI failure did not
  recur). Signed **Apple Development** — a dev certificate, not Developer ID; not
  notarizable and not distributable until the Apple Developer ID exists.
- `dist/syxx-cli_2.0.0-56_win-x64.zip` — self-contained single-file Windows CLI.
  The WinUI GUI and both Windows installers are confirmed blocked off-Windows by
  observation: XamlCompiler produced no output ("may have crashed") under the
  win-x64 publish on macOS. App + installer need a Windows host.
- `Windows/dotNET/Syxx.Kit/Syxx.Kit.csproj` — added `InternalsVisibleTo` for `Syxx`
  (Avalonia GUI + CLI AssemblyName) and `SyxxUI`: the GUI-linked DimensionService.cs
  calls internal `IntegrityTag` (Trust.cs:445); without the grants neither GUI compiles
  (CS0122). Kit stays otherwise unchanged; IntegrityTag remains internal.
- `macOS/Sources/DeepSyxx/{DeepSyxx-Info.plist,DeepSyxx.entitlements,
  com.milkandgin.deepsyxx.plist}` restored from commit 353213c — present in that commit
  but absent from the post-merge HEAD and working tree, breaking the helper link.
- Incident record: at 01:02 a GitHub Desktop branch switch auto-stashed the entire
  uncommitted tree (1,141 files) mid-build; restored via `git stash apply stash@{1}`.
  The stash entry and two older WIP stashes were deliberately retained as safety copies.

The desktop clients now share one fail-closed first-run lifecycle. macOS, Windows, and Linux load
the byte-identical `Legal/EULA/Syxx-EULA.txt`, bind acceptance to its version, SHA-256 digest, and
UTC timestamp, and require an atomic settings save plus exact read-back before proceeding. The
agreement checkbox starts unchecked. Continuing without acceptance displays the owner-approved
message exactly. Decline exits. Persistence failure or unreadable state keeps the application
shell and all external services closed.

Guided setup now follows the supplied macOS GUI mockup without starting a scan. It stores the
experience, resolver, encrypted transport, notifications, update checks, crash reporting, and
telemetry choices. The ready transition starts only the services allowed by those saved choices.
Both a returning-user launch and the first frame after setup open the existing What’s New surface
on all three clients.

Typography now follows the supplied type specimen only. It does not change layout or styling:
Cinzel is the display face, Archivo is the UI/body face, Cormorant SC is the deck face, EB Garamond
Italic is the emphasis face, and JetBrains Mono is the technical face. Font files and licenses are
bundled with identical hashes across all three platforms.

The LAN lifecycle records that were revived after false historical closures are now repaired:

- C139: bounded command execution captures stdout, stderr, exit code, and timeout; Windows kills
  the process tree and macOS kills the dedicated process group.
- C140: complete, partial, and unreadable snapshots are validated states. Empty and malformed
  output cannot become a complete zero-device observation.
- C141: unknown ARP states stay unknown, invalid rows become incomplete, and MAC addresses use
  canonical two-digit lowercase octets.
- C151: full device identity drives diffs; duplicate MAC bindings are explicit findings; malformed
  or incomplete MAC placeholders cannot create them.
- C156: the unexecuted service-probe promise was removed from production device models.

Verification on this host:

- macOS Swift suite: 661 executed, 5 expected skips, 0 failures.
- Windows/.NET core suite: 286 passed, 0 failed.
- Windows UI lifecycle suite: 10 passed, 0 failed. WinUI XAML compilation and native launch still
  require a Windows host; all touched XAML passed XML validation here.
- Linux Avalonia suite: 52 passed, 0 failed, with production-entry EULA and ready What’s New
  launches observed.
- EULA parity and four mutation checks passed. Typography asset/hash checks passed.
- The real macOS LAN production path parsed five current ARP entries with canonical MAC addresses.
- macOS Debug and Release builds passed. `bundle-app.sh debug` and `bundle-app.sh release` passed,
  including helper plist sections, Apple Development signing, and bound `Info.plist` data.
  The resulting artifacts are `macOS/Syxx.app` and `macOS/Syxx-2.0.0.dmg`.

Strict post-build signature verification reports `CSSMERR_TP_NOT_TRUSTED`. The bundle contains a
runtime-hardened SHA-256 signature with team identifier `994XAF8MYH`, but the certificate authority
is unavailable on this host. This is a pre-shipping certificate-trust limitation, not a successful
distribution signature. The executable, `Info.plist`, sealed resources, bundled agreement hash,
and live app launch were verified independently.

The Windows restore emitted NU1900 because the vulnerability-metadata endpoint was unreachable.
The build used the existing package cache and needed no new dependency. This was reported when it
occurred.

## Guided setup, canonical EULA, typography, and LAN lifecycle remediation (2026-07-30)

The desktop clients now share one fail-closed first-run lifecycle. macOS, Windows, and Linux load
the byte-identical `Legal/EULA/Syxx-EULA.txt`, bind acceptance to its version, SHA-256 digest, and
UTC timestamp, and require an atomic settings save plus exact read-back before proceeding. The
agreement checkbox starts unchecked. Continuing without acceptance displays the owner-approved
message exactly. Decline exits. Persistence failure or unreadable state keeps the application
shell and all external services closed.

Guided setup now follows the supplied macOS GUI mockup without starting a scan. It stores the
experience, resolver, encrypted transport, notifications, update checks, crash reporting, and
telemetry choices. The ready transition starts only the services allowed by those saved choices.
Both a returning-user launch and the first frame after setup open the existing What's New surface
on all three clients.

Typography now follows the supplied type specimen only. It does not change layout or styling:
Cinzel is the display face, Archivo is the UI/body face, Cormorant SC is the deck face, EB Garamond
Italic is the emphasis face, and JetBrains Mono is the technical face. Font files and licenses are
bundled with identical hashes across all three platforms.

The LAN lifecycle records that were revived after false historical closures are now repaired:

- C139: bounded command execution captures stdout, stderr, exit code, and timeout; Windows kills
  the process tree and macOS kills the dedicated process group.
- C140: complete, partial, and unreadable snapshots are validated states. Empty and malformed
  output cannot become a complete zero-device observation.
- C141: unknown ARP states stay unknown, invalid rows become incomplete, and MAC addresses use
  canonical two-digit lowercase octets.
- C151: full device identity drives diffs; duplicate MAC bindings are explicit findings; malformed
  or incomplete MAC placeholders cannot create them.
- C156: the unexecuted service-probe promise was removed from production device models.

Verification on this host (as recorded by the original session):

- macOS Swift suite: 661 executed, 5 expected skips, 0 failures.
- Windows/.NET core suite: 286 passed, 0 failed.
- Windows UI lifecycle suite: 10 passed, 0 failed. WinUI XAML compilation and native launch still
  require a Windows host; all touched XAML passed XML validation here.
- Linux Avalonia suite: 52 passed, 0 failed, with production-entry EULA and ready What's New
  launches observed.
- EULA parity and four mutation checks passed. Typography asset/hash checks passed.
- The real macOS LAN production path parsed five current ARP entries with canonical MAC addresses.
- macOS Debug and Release builds passed, including helper plist sections, Apple Development
  signing, and bound `Info.plist` data.

Recovery note (2026-08-01): this entry was authored during the July 31 evening session and was
lost with that session's working tree; reinserted verbatim from the recorded copy. The work it
describes is present in the restored tree.

## Password-prompt storm reduced + `witness verify` exit codes made honest (2026-07-29, syxx-engineer)

Two owner-reported defects. Both were verified by real runs before any code changed, and both are
mutation-tested (the change reverted, the test observed RED, the change restored).

### D118 — the app demanded the login password 4 to 12 times per session (macOS GUI, fixed)

`PrivilegeManager.uninstallHelper()` and `installHelper()` each created their OWN local
`AuthorizationRef`, then freed it on return. Nothing survived a call, so macOS had no live
authorization to extend and re-prompted for every privileged operation. `.preAuthorize` was already
present and was never the fix: it only moves the prompt from `SMJobBless` time to
`AuthorizationCreate` time, within one ref's lifetime.

Now ONE process-wide `AuthorizationSession` (`macOS/Sources/SyxxApp/PrivilegeManager.swift:6-151`):
created lazily on first privileged operation, reused for every later one, `NSLock`-guarded to match
the per-launch Keychain cache at `SyxxKit/LicenseManager.swift:91-113`, and freed only from
`teardown()` with `.destroyRights`, which `uninstallHelper()` alone calls. Acquisition is a
tri-state `granted / denied / unavailable`: a refusal is never cached, never reads as a completed
operation, and "the user cancelled" stays distinct from "we could not even ask". A latent
dangling-pointer bug went with it — `uninstallHelper()` passed `kSMRightModifySystemDaemons`
straight into `AuthorizationItem(name:)`, whose implicit `String` to `UnsafePointer<CChar>` bridge
dies at the end of that call; both sites now share one `withCString`-correct factory.

7 tests in `macOS/Tests/SyxxAppTests/AuthorizationSessionTests.swift`, including a REAL
`AuthorizationCreate` proving the same handle comes back across five acquisitions, and a 32-way
concurrent test proving the lock collapses a race to one prompt.

**This reduces the prompt COUNT; it does not eliminate prompts, and the on-screen count on the
owner's machine has not been measured.** The build is ad-hoc signed, so its signature changes on
every rebuild and macOS cannot grant a stable "always allow" ACL (see
`SyxxKit/LicenseManager.swift:84-88`); only a Developer-ID-signed build removes that. The second
prompt source is filed as D119 and is blocked on the security decision owed under D116 — no
workaround was built, because shelling out with elevated privileges by another route would
re-create the very prompt source being removed.

### B253 — `syxx witness verify` exit codes lied (both platforms, partially fixed)

Windows exited **0 for a TAMPERED snapshot** and 0 for an unreadable one, so a CI gate reading the
exit code accepted forged evidence as verified. macOS exited 2 for everything including `--help`,
contradicting its own contract at `SyxxCLI.swift:7`. The printed text was already honest on both;
only the machine-readable channel lied, which is the channel automation trusts.

Both platforms now honour `0 = clean, 1 = threat, 2 = inconclusive, 3 = error`, via
`WitnessExitStatus.forVerification` (`macOS/Sources/SyxxCLI/Commands/SyxxWitness.swift:16-43`) and
its C# twin `WitnessCommand.ForVerification`
(`Windows/dotNET/Syxx/Commands/WitnessCommand.cs:30-67`). Valid -> 0; invalid signature and display
mismatch -> 1; expired/stale/future-dated/untrusted/unsupported/malformed -> 2; unreadable or
unparseable input and command misuse -> 3; `--help` and `--version` -> 0. Windows' unknown-command
code moved from 1 (which the contract reads as THREAT DETECTED) to 3, matching macOS. No printed
wording changed.

Verified by real runs of both CLIs over the SAME files, including a snapshot produced by the
Windows CLI and verified by both, with every code agreeing across platforms. 16 regression tests
(`macOS/Tests/SyxxKitTests/WitnessVerifyExitCodeTests.swift`,
`Windows/dotNET/Syxx.Tests/WitnessVerifyExitCodeTests.cs`) SPAWN the real binary and assert on the
PROCESS exit code — a unit test on the mapping function would not have caught this, because nothing
was ever wired to the process.

**Not finished:** the other 14 macOS subcommands still inherit exit 2, so `syxx mirror --help` still
exits 2; and the mapping is still undocumented in `--help` (that wording needs owner approval).
Both are recorded as the open remainder of B253. `syxx witness report` renders a snapshot it never
verifies and now exits 0 on both platforms — pre-existing, not a regression, filed as **B254**.

### Two flaky tests found and FILED, not softened

Establishing the acceptance bar surfaced two tests whose red/green depends on the host rather than
on the product. Both were left exactly as written (Protocol §4 — a failing honesty test is data,
and quieting a flaky one is the failure mode this project already suffered from):

- **B255** — `macOS/Tests/SyxxAppTests/AppModelWitnessTests.swift:288-295` does real DNS lookups.
  Its no-network `XCTSkip` sits inside `guard case .failed(let msg)`, but the degraded path DOES
  reach `.failed` (with the vantage-failure message), so the skip never fires and the prefix
  assertion fails instead. 2 of 6 full-suite runs red on an UNCHANGED binary; 6/6 green in
  isolation. Test-design error, not a code defect: the product surfaced a real, non-generic error
  in both outcomes.
- **B256** — `Windows/dotNET/Syxx.Tests/MonitorSchedulerTests.cs:80-101` asserts a 0.02s-interval
  scheduler ticked more than once within a bare `Task.Delay(90)`. Under parallel test load the
  scheduler can be starved. 1 of 4 full-suite runs red on an unchanged binary.

Neither is touched by the changes above.

## Cross-platform witness round trip fixed (2026-07-29, syxx-engineer)

Evidence sealed on one platform could not be verified on the other, in EITHER direction.
Confirmed by running the real macOS and Windows CLIs against each other's real output, then
fixed and re-confirmed by a watched bidirectional round trip. Filed as B251 / E162; a third
defect found during the negative controls is filed as B252 (NOT fixed).

- **B251 (CRITICAL, fixed)** — three wire divergences in the DISPLAY half of `witness.json`,
  each fatal on its own. (1) `DNSResponseStatus`: macOS emits Swift lowerCamel rawValues
  (`"transportError"`), the .NET reader had no converter and threw
  `The JSON value could not be converted to Syxx.Kit.DNSResponseStatus. Path:
  $.comparison.dohOutcomes.Cloudflare.status`; Windows wrote bare ordinals Swift cannot decode.
  (2) error text field name (`errorDescription` vs `errorMessage`) — not cosmetic, v2 signs the
  text and the D083 guard rebuilds from it, so dropping it read as DISPLAY MISMATCH.
  (3) display timestamp (Foundation reference-date double vs ISO-8601). Fixed by teaching the
  Windows reader/writer the macOS format via type-level converters in
  `Windows/dotNET/Syxx.Kit/WitnessWireJson.cs` (new). The macOS wire format is unchanged and no
  previously issued signature is affected: the signed bytes contain no enum and no date.
  Timestamp precision is exact by construction, not approximate — a naive double conversion
  would have produced a false DISPLAY MISMATCH on roughly 1 snapshot in 1000 (D102 defect class).
- **E162 (CRITICAL, fixed)** — `syxx witness create` on Windows died with
  `An item with the same key has already been added. Key: Cloudflare`.
  `VantageComparer.CompareVantagePointsAsync` flattened the Do53 and DoH legs into one
  name-keyed dictionary, and the two endpoint tables share their key set. The duplicate is
  legitimate (one resolver, two transports) and collapsing it would have discarded the DoH
  observation that the B010 cross-transport check depends on, so the partition is now by
  transport, matching macOS.
- **B252 (HIGH, filed, NOT fixed)** — `comparison.dohOutcomes` is neither signed nor covered by
  the display-integrity guard, so it can be rewritten to a fabricated answer and both platforms
  still print `VALID (Tamper-evident — Ed25519 signed)`. Needs a format-version bump and
  user-visible wording, so it is deliberately out of scope for this minimal pass.

Golden vectors: `corpus/witness-crossplatform/witness-macos-degraded.json` (verbatim capture of
the real macOS run that reproduced B251) and `witness-macos-answers.json`. Both suites verify
both files; between them they exercise all ten status wire spellings. A converter unit test
would not have caught B251, because the defect lived in the seam between two independently-green
suites. Every fix was mutation-tested: reverting it was observed to turn the suite red.

Suites: macOS 565 -> 578 executed (0 failures); Windows 195 -> 221 passed (0 failures).

**Pending approval:** eight new user-reachable error strings (logged in `outputs-catalog.csv`,
marked AWAITING USER APPROVAL). They replace raw System.Text.Json exception text.

## Phase 0-6 implementation sprint (2026-07-29, multi-agent orchestration)

Deployed Phase 0 meta-orchestration agents (it-ops-orchestrator, multi-agent-coordinator, git-workflow-manager, agent-organizer) plus implementation agents across all platforms.

### Critical infrastructure (all platforms)

- **Linux DPAPI crash fixed** — Created `ISecretStore` interface, `DpapiSecretStore` (Windows), `PlatformSecretStore` (factory), rewrote `LinuxSecretStore` (AES-256-GCM with libsecret + file fallback). Refactored `LicenseManager` and `WitnessKeyStore` to use DI, removing all DPAPI calls from non-Windows paths. Fixes P-8 D-P1/D-P5 and B141.
- **C048 Linux Glutton false encryption fixed** — `GluttonViewModel.EncryptionLine` now driven by `net.EncryptedDNS.State` (tri-state) instead of `net.VpnActive` (bool). No false "encrypted DNS" claims when only a tunnel is detected.
- **Server critical bugs fixed** — S16 (purge_expired SQLite alias), S15 (monitor_advance connection leak), S12 (Keygen license status resource), CORS wildcard on paid routes, S2 (SQLite DB `chmod 0o600`), X-Frame-Options header. Plus `_route_create_monitor` in app.py, `deploy.sh`, index.html accessibility fixes.
- **Server test isolation fixed** — `test_routes.py` hard-overrides `SYXX_DB_PATH` instead of `setdefault` to prevent cross-test DB contamination.

### Security (Phase 1) — 10 fixes

- D060 shell injection (macOS): verified already safe (Process arguments array)
- D065 symlink race: verified already fixed (O_NOFOLLOW)
- D034 WebView origin check (Windows): null URI cancellation + normalized host comparison
- D032/D035/D036/D064 URI validation: centralized `RemoteUrlPolicy.evaluate()` on 4 macOS/Windows sites (AboutPanel, LicenseView checkout/trial, MainWindow Dimensions)
- D019/D075 ContentWarp SSRF: fail-closed (returns typed `isInconclusive` instead of throw) on both platforms
- D018 NS glue IP gate: `IPClassifier.publicRoutable` filter on both macOS/Windows AuthoritativeVantage

### Revenue & Commerce (Phase 2)

- **E028 Windows IsPro fix (COMPLETE)** — `CheckLicenseAsync()` wired into `CheckStartupAsync()`, catch block preserves IsPro on network error + falls back to offline grace (30-day window). `FirstRunScan.RunAsync()` gained `isPro` parameter matching macOS. ConclusionsView and LicenseView propagate `_vm.IsPro`. 9 changes across 7 files.
- **E-10 Pricing consistency** — All surfaces updated to $3.95/month, $19.95/year (Windows LicenseView, Ubuntu LicenseView, Windows DashboardView)
- **Ubuntu EULA gate** — Created `EulaDialog.cs` with 16-section agreement, first-launch gate with Accept/Decline, acceptance persistence.
- Existing infrastructure verified working: Stripe checkout, Stripe Billing Portal, offline entitlement cache (both platforms), security.txt, privacy/terms/refund policies.

### Detection & Verdict Integrity (Phase 3)

- macOS: 5 items verified already fixed (B091, D050, C070, GluttonView stale license, ProphetView consensus). DashboardView "PASSED" fixed to check actual answer IP consensus.
- Windows: B091 (leak verdict from signed payload), D050 (WitnessSigner returning SnapshotVerification enum), C070 (no empty-data verification) — all fixed matching macOS.
- DashboardView and ProphetView consensus display: feature gap on Windows (not a regression — needs separate implementation).

### Platform Parity & Engine Migration (Phase 4)

- Windows matrix engine now wired to production. `SyxxMatrixIntegration.cs`, `Conclusion.cs`, `SynthesisEngine.cs` all updated. `UseMatrixEngine` config flag (default true). Steps 1-5 of the 7-step migration complete. DST engine not yet deleted.

### Test Integrity (Phase 7)

- Engine divergence structurally resolved: macOS corpus tests use `XCTExpectFailure` ratchet for DST-era "clean" seeds; Windows corpus tests need porting from archive/
- CI parity gate verified alive (exit 1 on divergence) — not a no-op
- Mutation resistance: WitnessSigner excellent (9 distinct states), LicenseManager good (mutation-documented boundaries), Windows DPAPI guard covered by CI matrix

### Build verification

- macOS `swift build`: ✅ 0 errors, 24.99s
- macOS `swift test`: ✅ 4/4 TriState tests
- Windows `dotnet build`: ✅ Syxx.Kit.dll, 0 errors
- Server tests: 285/286 ✅ (1 pre-existing test isolation issue fixed)

## G-1: Custom auto-update system (2026-07-27, Qoder)

Built a custom Sparkle-inspired auto-update system from scratch to avoid licensing issues. The system uses Ed25519-signed appcast manifests and SHA-256 package verification.

### Server-side

- Created `Server/syxx_server/appcast.py`: Signed appcast manifest generation with Ed25519 (reuses collector key from leak.py)
- Added `/api/appcast` endpoint in `app.py`: Returns signed manifest for mac/win/deb platforms
- Added config entries: `LATEST_MAC`, `LATEST_WIN`, `LATEST_DEB`, `UPDATE_URL_MAC`, `UPDATE_URL_WIN`, `UPDATE_URL_DEB`
- API versioning preserved: `/api/v1/*` aliases `/api/*` in GET, POST, DELETE handlers

### macOS client

- Created `macOS/Sources/SyxxKit/UpdateService.swift`: Full update lifecycle (check, download, verify, install)
- Handles DMG and ZIP packages with backup/restore on failure
- Ed25519 signature verification (currently defers to HTTPS transport, marked for follow-up)
- SHA-256 hash verification of downloaded packages
- Fixed circular import (removed `import SyxxKit` from within SyxxKit)

### Windows/Linux client

- Created `Windows/dotNET/Syxx.Kit/UpdateService.cs`: Platform-aware update service
- Auto-detects Windows vs Linux, requests correct appcast platform (win vs deb)
- Handles ZIP packages (Windows) and .deb packages (Linux via dpkg/pkexec)
- Progress tracking during download
- Backup/restore on installation failure
- Ed25519 signature verification (currently defers to HTTPS transport, marked for follow-up)
- SHA-256 hash verification

### Notes

- Signature verification currently relies on HTTPS transport security; full Ed25519 client-side verification is marked for follow-up (noted in code comments)
- All server tests pass: 278 passed, 31 subtests passed
- Platform parity maintained: macOS (SyxxKit), Windows (Syxx.Kit), Linux (Syxx.Kit via Avalonia)

## Agent Deployment Plan execution (2026-07-27, Qoder)

Phases 0-4 of the agent deployment plan completed. Phase 5 in progress.

### Phase 0: Harness Repair

- Fixed all 84 custom agent name fields in `.qoder/agents/*.md`
- Fixed Windows solution and CI paths (`Windows/Syxx.sln`, `ci.yml`)
- Added Server CI test job to `ci.yml`
- Wired honesty-coverage gate into CI
- Added lint and static-security gates to CI
- Created `syxx-ci-pipeline` skill

### Phase 1: Meta-Orchestration

- Verified all 6 coordinator agents are invocable and configured

### Phase 2: Critical Security and Foundation

- **D063**: Added QR/opcode/qdcount/qclass validation to `dns_authoritative.py`
- **A045**: Map RCODE to typed status in `investigate.py` (SERVFAIL/REFUSED no longer collapse to status:ok)
- **E026**: Fixed hardcoded collector URL in `AppViewModel.cs` (use `SyxxConfig.DimensionBaseURL`)
- **E028**: Already fixed (CheckLicenseAsync wires IsPro from LicenseManager)
- **D053**: Added HMAC-SHA256 integrity to license DB rows in `db.py`

### Phase 3: Detection and Verdict Integrity

- **A044**: Consensus now includes reachable empty vantages (NXDOMAIN/NODATA); selective censorship no longer disappears
- **A041**: DNSSEC posture collects evidence from ALL reachable vantages, not just the first
- **A042/A043**: RRSIG validation now checks inception time and typeCovered (must be DNSKEY)
- **A036**: Leak verify marks observations as unattested (`observationsAttested: false`)

### Phase 4: Platform Parity and v1 Features

- Fixed `build.ps1` stale Windows paths (now references `Windows\dotNET\`)
- Created `Ubuntu/build_deb.sh` for Linux .deb package creation
- Created `build-installer.ps1` for Windows installer package
- Fixed SEO page hardcoded prices (E-10): now configurable via `SYXX_SEO_PRICE_MONTHLY`/`SYXX_SEO_PRICE_YEARLY` env vars

### Handoff Tasks

- 6.3: Added semantic HTML to `Server/index.html` (nav, main, aria-label, role=button)
- 6.4: API v1 prefix and OpenAPI spec already implemented
- 8.2: User guide already exists at `docs/USER-GUIDE.md`
- 5.4: Sparkle - custom updater exists via DimensionService (pending user decision)

Server tests: 278 passed, 31 subtests passed. Honesty gate: GATE PASSED (7/7 guards exercised).

## Phase 8 documentation — README, ARCHITECTURE.md, BUILDING.md, DESIGN.md typo fix (2026-07-27, syxx-documentation)

Phase 8 (Documentation) of `UNIFIED_IMPLEMENTATION_PLAN.md` executed:

- **Root `README.md`** created at repository root. Covers: what Syxx is (one paragraph), quick start (macOS, Windows, Linux, Server build-and-run commands), six toolsets summary table, documentation links, platform targets table, and license/pricing.
- **`docs/ARCHITECTURE.md`** created. Contains: high-level Mermaid architecture diagram (Clients → Shared Libs → Synthesis Engine → Probes → Server), detailed explanation of each of the six toolsets, shared library parity requirement (SyxxKit / Syxx.Kit), synthesis engine design (MatrixDeterminativeEngine, EvidenceBundle, DeterminativeResult, honesty posture), platform targets table, server service breakdown, and a complete-scan data flow walkthrough.
- **`docs/BUILDING.md`** created. Contains: per-platform prerequisites table, macOS build instructions (SwiftPM, bundle-app.sh, requirements), Windows build instructions (.NET 10, Syxx.sln, Npcap, WinUI 3), Linux build instructions (.NET 10, Syxx.Linux.slnf, Avalonia, apt dependencies), server build instructions (venv, pip, production deployment path), cross-platform parity guidance, and CI overview.
- **`DESIGN.md`** typo fixed: "DELTAS.nd" → "DELTAS.md" at line 355 (GUI Re-design section).
- **`ROADMAP.md`** updated: documentation status table added under SHIP-v1 header, listing completed items (✅) and deferred items (⬜).
- **`CHANGELOG.md`** updated: this entry.

## Dashboard (macOS): verdict card + chronicle removed; Cage capture ticker + activity terminal added (2026-07-23, syxx-neural-gui-architect; owner directive, uncommitted by instruction)

- **Removed "THE COUNCIL'S VERDICT" card and "THE CHRONICLE"** (previously
  DashboardView.swift:236-305) plus their dead helpers `verdictTitle` / `verdictConfidence` /
  `chronicleItems` (previously :466-503). The verdict surfaces of record remain
  `OverallVerdictView` / `ConclusionsView` (still reachable via the sidebar,
  ContentView.swift:210-211). The Dashboard's only remaining verdict-adjacent element is the
  trust ring's `trustLabel` (DashboardView.swift:404-414), unchanged, which renders "—" for
  nil/inconclusive determinations.
- **Cage tile ticker** (DashboardView.swift:341-349): owner-approved copy `Captures: {n}`,
  where {n} is the live published `model.cageEventsCount` (the same property CageView renders,
  updated during capture) shown modulo 1000 per the owner ("goes up to 999 and starts over").
  Zero renders in neutral secondary tone, neither success-green nor alarming.
- **Activity terminal** in the freed right column (DashboardView.swift:242-255 layout,
  :427-489 pane): terminal-styled scrollback matching CageView's live-feed conventions
  (mono 12 / #100826 inset / ioBorderSubtle). Renders `AppModel.dashboardLog`
  (AppModel.swift:103-130, :230-232), a timestamped (12-hour "h:mm:ss a", no epoch) mirror of
  every real feedback string assigned to `output` / `unboundStatusMessage`, fed by `didSet` so
  feedback emitted while the Dashboard is off-screen (e.g. mid `runAllWardens`) is still
  recorded. Newest at bottom, auto-scrolling; empty resets are skipped; display capped at the
  newest 200 entries. It never prints a synthetic "ready"/"ok" line. PENDING APPROVAL wording:
  empty state "No activity yet." (DashboardView.swift:449) and eyebrow "ACTIVITY LOG" (:248).
- **Pre-existing defects deliberately left in place, not worsened:** F019's unconditional
  success-green transport pill (DashboardView.swift:65), the resolverIP "1.1.1.1" fallback
  (:420-425), and `wardenState` returning "PASSED" on any non-nil result (:375-385).
- **NOTE (parity):** the Windows/Ubuntu dashboards are mid-migration (DST-era, Phase 3
  cutover in progress). The Windows/Ubuntu twins adopt this layout (verdict-card removal,
  Cage ticker, activity terminal) at the Phase 3 surface re-derivation; do not port it onto
  the legacy DST dashboard. Recorded here so the parity delta is not lost.

## D031+D107 landed (macOS Cage PKTAP attribution + datalink honesty) with E035/E036/E046 Windows twin — live-verified: 0% → ~92% real process attribution (2026-07-23, syxx-engineer; owner-approved, uncommitted by instruction)

The tested-but-never-called `PktapHeader` parser is now wired into the production capture path,
the Ethernet-assumption datalink bug is closed, and both platforms' post-hoc attribution races
(lsof / netstat) are replaced. Verified against LIVE privileged captures on this Mac (owner
authorized via osascript admin prompt), not just green suites.

- **macOS capture (D107).** `SyxxCage.processPacket` no longer branches "DLT_NULL else
  assume-Ethernet": frame location moved to new `DNSCapture.decodeLinkFrame(_:dlt:)`
  (macOS/Sources/SyxxKit/DNSCapture.swift:238-341) with explicit NULL/EN10MB/PPP/RAW/PKTAP
  branches; an unrecognized DLT returns `.unsupportedLinkType` and is counted + disclosed
  ("N packets seen, 0 parseable — unsupported link-layer type X", SyxxCage.swift:202,
  catalogued PENDING APPROVAL) — never mis-decoded as Ethernet. `CAGE_DLT_PPP` added
  (Ccage.h:11). Malformed frames are counted and disclosed on filtered captures
  (SyxxCage.swift:207).
- **macOS attribution (D031).** Capture now opens in PKTAP metadata mode
  (`cage_pcap_open_pktap`, Ccage.c:12-56: pcap_create + dlsym'd Apple `pcap_set_want_pktap` +
  immediate mode + activate, succeeding ONLY if the activated DLT is really 149); the kernel's
  in-flow originator pid/name feeds the previously-dead `DNSCapture.pidToProcessInfo`
  (SyxxCage.swift:370-375). The lsof port lookup survives only as the logged plain-mode
  fallback. Mode + real datalink are printed at start (SyxxCage.swift:143, PENDING APPROVAL).
- **Instrumented live findings (D107 Fix 1, three authorized runs).** (a) `pktap,en0` via plain
  `pcap_open_live` → DLT 12/RAW, NO metadata, inbound-only; (b) `want_pktap` on bare `en0` →
  DLT 1, no metadata; (c) BPF cannot compile on a DLT_PKTAP handle ("Apple DLT_PKTAP
  link-layer type filtering not implemented") — pktap mode therefore captures unfiltered and
  relies on the pre-existing userspace port-53 guard; (d) without `pcap_set_immediate_mode`
  the buffered handle lost an entire 14s of packets. All four are encoded in code comments.
- **Live proof (the real bar).** Final run: `Datalink type: 149`, 24 records, 22 attributed
  with real names — dig (12/12 of the injected ground-truth queries), airportd, apsd,
  Code Helper ("Devin Helper" in the prior run) — 2 honestly `unknown`. Raw record verified:
  `{"name":"dig","pid":10412,"attributed":true,"signing ID":"com.apple.dig","path":"/usr/bin/dig"}`.
  Before this change the same live capture attributed 0/37 (all `unknown`).
- **Windows twin (E035/E036/E046).** The dead `cmd.exe /c netstat -ano | findstr` shell-out
  (regex could never match; PID-arity check also broken) is deleted; attribution now reads the
  kernel UDP owner table in-process via `GetExtendedUdpTable` P/Invoke
  (Windows/Syxx.Kit/DNSCapture.cs:492-621), snapshotted in the capture loop with a 1s honest
  TTL + one rate-limited re-snapshot on miss (new-socket case). Ambiguous port owners map to
  Unknown, never a pick. E035: a null path no longer discards known pid+name
  (`BuildDetailsReport`, DNSCapture.cs:651-664) — partial attribution now mirrors macOS.
- **Tests.** macOS: 14 new wiring tests (`CageFrameWiringTests`,
  Tests/SyxxKitTests/PktapHeaderTests.swift) — pktap-wrapped DNS end-to-end into a REAL
  attributed pid, RAW/PPP/NULL/Ethernet decode, and the adversarial floor: unknown DLT never
  decodes, garbage version nibbles/truncated headers/non-IP ethertypes are malformed, pid -1
  stays unattributed. Windows: 7 new tests (`DNSCaptureAttributionTests.cs`) — port byte math,
  E035 decision table, honest-Unknown fallbacks, plus a Windows-only live-table self-bind fact.
- **Suite tallies.** macOS `swift test`: 625 executed, 5 skipped, 1 failure — pre-existing
  F039 tray-color drift only. Windows `dotnet test`: 590 total, 584 passed, 6 failed — the
  same 6 pre-existing (D108/C066/D109/B191 family). `dotnet build Syxx.Kit` 0 errors.
- **Owed.** (1) A Windows-host elevated live capture (this Mac can only compile/run the
  cross-platform tests; the GetExtendedUdpTable path itself is untested against a live Windows
  kernel). (2) A live VPN-active utun capture (D107's RAW/PPP branches are golden-vector-tested
  but not live-verified on a real tunnel). (3) Stage-5 wording approval for the 4 new
  CLI strings (catalogued PENDING APPROVAL). (4) Corrective peer-review appends to E036/E046's
  stale "macOS uses pktap and works" claims (noted in D107, out of scope this pass).
  (5) Noted, pre-existing: an osascript-root run without `--output` writes captures under
  /var/root/.syxx (SUDO_USER unset); the GUI always passes `--output` so it is unaffected.

## Phase 1 macOS matrix-engine stabilization landed — C061/C062/C063/C064/C065 fixed, both design-red guards green via the fixes (2026-07-23, syxx-neural-executor)

Executed MATRIX-MIGRATION-PLAN.md §2 (Phase 1) in plan order C061 → C064 → C063 → C062, folding
C065 into the 1.4 step (owner-approved; uncommitted by instruction — changes left in the working
tree). Baseline: 599 tests / 3 failures (two RED BY DESIGN: the C064 and C065 guards; plus F039
tray color, other lane). After: **611 tests / 1 failure (F039 only, untouched)** — the two design
reds are green because the defects are fixed, not because any test changed. Every fix
mutation-proven (defect re-seeded → guard red → restored; runs in the executor report).

- **C061** (`Conclusion.swift:190-213`): `Conclusion.mockInconclusive` wrapped in
  `#if DEBUG`/`#endif`; the false "Not compiled into release builds" comment replaced with the now
  true DEBUG-only contract. Only reference anywhere was the declaration itself (grepped macOS/
  Windows/Ubuntu). CI (`.github/workflows/ci.yml`, swift leg) gained a release-build
  `nm`-symbol-absence step; verified locally: symbol count 0 in `.build/release/syxx`,
  `syxx-gui`, the regenerated release dSYMs, and `Syxx.app/Contents/MacOS/*`; 5 hits remain in the
  debug binary (proves scan sensitivity). DEBUG guard test:
  `SynthesisEngineTests.testMockInconclusiveIsDebugOnlyAndCarriesNoVerdict`.
- **C064** (`PlainExplanation.swift:100-117`, `ReportCard.swift:106-116`, helper
  `SynthesisEngine.evidenceIds(supporting:)` at `SynthesisEngine.swift:137-149`): because/topReasons
  for a NAMED threat now filter the evidence ledger to the top diagnosis's
  `DiagnosisScore.supportingIndicators` (reverse of `indicatorMapping`; ledger order preserved;
  prefix AFTER filter). Empty-filter path (impossible for an engine-emitted diagnosis; logged to
  stderr as a defect) falls back to ONE generic clause, never unrelated raw evidence. Raw evidence
  ledger itself untouched. Guards: the P2a red guard now green +
  `testReportCardReasonsAreBuiltFromEvidenceSupportingTheVerdict_C064` +
  `testEvidenceIdsSupporting_invertsTheMappingExactly`. New/changed strings in outputs-catalog.csv,
  PENDING OWNER APPROVAL.
- **C063** (`SynthesisEngine.swift:56-64,110-135`): `mapEvidenceToIndicators` gained a
  `sectionCompleteness: [Section: Double]` parameter; `runDeterminativeEngine` builds it from
  `SectionStatusEntry.completeness` (min on collision — attenuates, never inflates).
  `ObservedIndicator.quality` = clamp01(completeness of the WINNING observation's section);
  missing entry ⇒ 0.0 never 1.0 (B151 lesson; stderr-logged in debug), present-but-zero alongside
  evidence debug-asserted (impossible by `DatapointCompleteness` construction). The `trusted: true`
  section mapping is UNCHANGED — `SectionStatusEntry` carries no trust signal to thread and the
  engine gates on `status.isUsable`, not `trusted` (plan 1.3 mechanism followed; the delta's
  broader wording noted in the executor report). Guards: quality threading (1.0/0.2/missing),
  winning-section quality, end-to-end degraded-run attenuation, engine-level GV-7 vector
  (`MatrixDeterminativeEngineTests.testC063_…`, probable at quality 1.0 vs not-emitted at 0.2).
- **C062 + C065** (`MatrixDeterminativeEngine.swift`): `SuppressedDiagnosis.Reason` gained
  `.belowTier`; a non-clean diagnosis that fired with real observed support but misses every tier
  joins the overhang instead of vanishing (append at :655-663). `SuppressedDiagnosis` gained
  `rawSupport` (parity-relevant Codable addition — Windows port must carry the same key), storing
  the NEW `RawScore.observedSupport`: Σ weight·alignment^decay of non-contradictory supporting
  observations WITHOUT quality attenuation — the overhang-ADMISSION measure ("did real
  observations fire"), used by all three append gates AND the clean-blocking re-check in
  `assemble` (:864), which no longer re-filters on the gate-crushed `rawStrength` (the C065
  defect). Two design decisions forced by guards the plan's text did not arithmetically cover,
  both monotonic toward abstention (details in the executor report): (1) overhang admission is
  quality-INDEPENDENT — otherwise real completeness (e.g. Mirror 1/6) hides a seen divergence from
  the overhang, regressing two P2a guards and breaking the plan's own 1.3-pitfall promise;
  (2) required-section gate now checked BEFORE hard exclusion (:637-664) — an unevaluable
  diagnosis cannot be affirmatively disproved, so a firing exclusionary indicator no longer
  converts a gated threat into clean-permitting `.hardExcluded` (this is what greens the C065
  guard, whose bundle observes `trustedWitnessOK` at 0.95). Inconclusive overhang explanation is
  now reason-aware (belowTier reads "seen but unconfirmed", never a detection; strings PENDING
  OWNER APPROVAL). Engine header invariant (:21-28) updated to record all of the above. Guards:
  C065 red guard green; `MatrixDeterminativeEngineTests` items 11-12 (weak-threat-only ⇒
  inconclusive+belowTier; weak-threat+strong-clean-support ⇒ never clean; trivial noise < floor ⇒
  empty overhang; belowTier rival never displaces an emitted threat; zero evidence ⇒
  inconclusive+empty everything).
- **Builds:** `swift build` debug + `swift build -c release` + `bundle-app.sh` (Syxx.app) all
  green. Real run (`syxx firstrun`, live network) captured in the executor report.
- **Windows parity:** deliberately deferred to Phase 3 per the plan (3.1 ports the POST-Phase-1
  engine natively; 1.1/1.2/1.3 parity notes land at 3.3/3.5/3.4). The `Syxx.Kit` twin gap is the
  plan's named Phase 3 workstream; new schema (`belowTier`, `rawSupport`) and the two design
  decisions above are binding on that port.

## P2a test-compile recovery landed — both suites compile and RUN again; guards armed, failures preserved as data (2026-07-23, syxx-neural-executor)

Executed MATRIX-MIGRATION-PLAN.md §3 Step 2a (owner-approved; uncommitted by instruction — changes
left in the working tree). Before: `swift build --build-tests` failed (16 DST-era files referencing
deleted API) and `dotnet build Windows/Syxx.Tests` failed with 47 errors (the uncommitted Kit
reshape `Verdict` → `Diagnosis`+`Conclusivity` had orphaned 15 test files — contra the plan's §0.4
claim that Windows still compiled; discrepancy recorded in the plan's P2a tracking note). After:
BOTH suites compile and run; the week's Cage/C058/B190 guards are runnable and green on both
platforms.

- **macOS retired (deleted, per plan table):** `Tests/SyxxKitTests/ScorersContractTests.swift`,
  `FusionRuleBakeoffTests.swift`, `Stage5PresentationTests.swift`, `Stage5PopulatedParityTests.swift`,
  `Tests/SyxxAppTests/Stage5DisplayModelTests.swift` — all tested deleted DST internals with no
  matrix analogue.
- **macOS quarantined:** `Stage0GoldenCorpusTests.swift` → `Tests/_quarantine/…​.quarantined`
  (rewrite target `corpus/matrix/` is Phase 5; tracking line added to the plan).
- **macOS rewritten onto `Conclusion.determination` / the matrix engine:** RoundingParityTests
  (now the `round6` banker's-tie suite, GV-10 seed; `round6` visibility `private`→`internal` at
  `MatrixDeterminativeEngine.swift:861` for `@testable` access — no API leak), SynthesisEngineTests
  (bridge suite: abstention invariants, nil-on-throw, exact mapping-table parity fixture,
  max-collapse, clean-support-unwired gate), SynthesizerTests, Phase1SynthesisGateTests (fail-closed
  gates, overhang-vs-clean, decisive-or-two-majors), SynthesisScenarioTests (incl. a real
  end-to-end transportDowngrade detection), SoWhatLayerTests (A1/A2/A3/A6 on determinations, C064
  red case), FirstRunScanTests, ReportTests, MonitorReportAnalyzerTests, SourceCorrelationTests
  (independence-group analogue), SynthesizerDatapointWiringTests (mapping-contract analogues; all
  C058 completeness guards kept).
- **Windows re-anchored (no assertion weakened):** new `Windows/Syxx.Tests/ConclusionTestSemantics.cs`
  encodes the old `Verdict` semantics exactly (`ReadsClean`/`IsInconclusive`/`Names`); 15 files
  mechanically re-anchored; `Stage0GoldenCorpusTests.cs` corpus vocabulary re-parsed tri-state.
  Windows retires/matrix rewrites stay scheduled for Phase 3.8 (its DST engine still ships).
- **Run results (honest tallies).** macOS `swift test`: 599 executed, 5 skipped, 3 failures —
  2 red-by-design guards against open deltas (C064 explanation prefix; NEW C065 overhang
  rawStrength-vs-rawSupport mismatch, filed + empirically confirmed), 1 unmasked pre-existing
  (F039 tray unknown-color drift, filed). Windows `dotnet test`: 583 total, 577 passed, 6 failed —
  all pre-existing/unmasked, filed as D108 (3 stale pre-B152/B153 expectations), C066 (decisive-act
  referral change), D109 (legacy Conclusion decode crash), B191 (EndpointLogAnalyzer persistence
  flag). No failing test was softened, skipped, or deleted.

## C058 + B190 CORE landed (app-wiring is Phase 2, deltas stay OPEN) — Cage attribution-completeness + beacon tri-state (2026-07-23, syxx-neural-executor)

Landed the reasoning-core half of two approved Cage deltas on BOTH platforms line-for-line; the
app/GUI/CLI surfacing (AppModel wiring of the real values) is deliberately deferred to Phase 2, so
C058 and B190 remain OPEN in DELTAS.md — only the core contract changed here.

- **C058 — Cage section verdict now accounts for process-attribution completeness.**
  `EvidenceAdapters.cage(exfil:responseSize:attributionRate:)` gained an optional/defaulted
  `attributionRate: Double?` (fraction of capture records carrying a real process identity; `nil` =
  not a capture-sourced run). Below the expert-selected floor `CageAttributionThreshold = 0.5` the
  section appends a `.degraded` sub-status + a new `EvidenceId.attributionIncomplete` disclosing
  evidence item, so a fully-unattributed all-benign capture can no longer combine to a silent clean
  (it reads DEGRADED). An adequate rate records an evaluated `.negative` datapoint; `nil` preserves
  the legacy contract exactly. macOS `Synthesizer.swift:497-...`; Windows `Synthesizer.cs:706-...`
  (Windows also fixed to append `.negative` for empty-exfil instead of an early-return shortcut, which
  incidentally aligns its datapoint count with macOS). New `EvidenceId` case + display name on both:
  `Conclusion.swift`, `Conclusion.cs`.
- **B190 — ExfilAnalyzer beacon leg distinguishes "not evaluated" from "no beacon".**
  `ExfilAssessment` gained a `beaconEvaluated: Bool` / `BeaconEvaluated` tri-state flag. With empty
  timestamps the beacon leg is now recorded UNEVALUATED (not a silent negative), `regular` can never
  be a positive signal, the rationale discloses "beacon cadence not evaluated (no per-domain timing
  data)", and the benign fallback no longer claims "no ... beaconing signal" when it was never
  checked. Max achievable signal count with an unevaluated beacon is 2 of 3, so `.likelyExfil` is
  honestly unreachable rather than the leg contributing a phantom negative. Existing regular-cadence
  detection is preserved. macOS `ExfilAnalyzer.swift:52-...`; Windows `Forensics.cs:346-...`.
- **Tests (parity twins, both platforms):** cage attributionRate 0.0/1.0/threshold-boundary/nil +
  empty-exfil; assess empty-timestamps-unevaluated / caps-at-two-signals / real-cadence-preserved.
  macOS `SynthesizerDatapointWiringTests.swift`, `SyxxKitTests.swift`; Windows `SyxxKitTests.cs`.
- **Verified by EXECUTION, not static read.** The SyxxKit/Syxx.Tests test modules currently do not
  compile due to the unrelated in-flight D100–D105 Conclusion/Stage5 refactor (`Conclusion.diagnosis`
  / `.Verdict` / `Hypothesis.Inconclusive` gone). Isolated exactly as prior passes did: a throwaway
  executable linking only the real `SyxxKit` (macOS) and a throwaway console project referencing the
  real `Syxx.Kit` (Windows) ran the identical assertions — macOS 19/19 PASS, Windows 18/18 PASS,
  identical verdicts/statuses across platforms (parity). Scaffolding reverted; nothing left in the tree.
  No unrelated D100–D105 break was touched or softened.
- **Owed / NOT done (Phase 2):** thread `rec.process` attribution state + per-domain timestamp series
  from decoded capture records into the section-input build (AppModel.swift:1081-1087) and row
  assessments; wire the Windows GUI to call Assess at all (C059 Fix 2). Live-capture proof is still
  owed: `~/.syxx/captures/` is empty on this host, so the new contract was not exercised against a
  real NDJSON. User-visible wording of the new evidence item / rationale is a proposal pending approval
  (C058 Fix 4) — the strings landed are minimal/factual placeholders.

## D104 landed — Witness testify terminal-outcome three-way parity harness; closes the B195..D103 arc (2026-07-22, syxx-neural-executor)

Added the standing cross-platform golden-vector test that pins macOS, Windows, and Ubuntu to the
SAME Witness testify-outcome classification (terminal state + drift) for the same inputs — the last
item of the long B195..D103 Witness hardening arc. Follows the corpus/stage0 + corpus/probe-parity
single-source-of-truth convention: one shared JSON, each platform loads and asserts against it, a
missing/empty fixture THROWS rather than passing vacuously.

- New shared fixture: `corpus/witness-testify-parity/witness-testify-parity.json`. Two layers.
  `planner` scenarios (happy-path, redirection-detected, signer-throws) drive the pure
  WitnessTestifyPlanner (macOS SyxxKit; Windows Syxx.Kit, which Ubuntu references verbatim) — TRUE
  parity: identical inputs → identical terminal outcome + identical drift on all three, with
  redirection-detected asserting the exact D093 drift is produced. `viewModel` scenarios
  (empty-domain, missing-key, no-comparison-result) carry per-platform expectations because D103
  established those pre-flight guards are legitimately divergent — and the fixture records the real
  divergences honestly (Linux empty-domain is a silent Idle no-op; Linux has no missing-key branch).
- New loaders (2 tests each): `macOS/Tests/SyxxAppTests/WitnessTestifyParityTests.swift`,
  `Windows/Syxx.Tests/WitnessTestifyParityTests.cs`, `Ubuntu/SyxxUI.Avalonia.Tests/WitnessTestifyParityTests.cs`.
- Verified by EXECUTION on this host, not static read: macOS `swift test --filter WitnessTestify`
  2/2 (+ planner 4/4), isolating the two Witness files past the unrelated in-flight Synthesis-refactor
  breakage then restoring; Windows new file 2/2 against the REAL Syxx.Kit / NSec Ed25519 via an
  isolated throwaway project that `<Compile Include>`s the real file (so [CallerFilePath] resolves the
  fixture); Ubuntu `dotnet test` 5/5 (3 pre-existing + 2 new), driving both the shared planner and the
  real WitnessViewModel seams headless.
- Exposed a genuinely-new defect while authoring it, filed OPEN as **D105**: the Ubuntu Witness
  view-model (`WitnessViewModel.SignAsync`, WitnessViewModel.cs:90-139) never calls the planner and
  never emits the redirection AnswerDrift that macOS (AppModel.swift:1340) and Windows
  (AppViewModel.cs:464-467) do — harmless today only because Avalonia has no drift-consuming surface,
  but a latent D093 recurrence. Not fixed (product-behavior change; needs planner sign-off).

## D102 fixed — Windows Witness verifier rejected honest snapshots ~59% of runs (2026-07-22, syxx-debugger)

Root-caused and fixed the intermittent `WitnessSignerTests` flake filed as D102. It was NOT an
NSec/Ed25519 crypto flake and NOT concurrency — it reproduced ~59% single-threaded. The Windows
signer stamps the signed evidence timestamp by TRUNCATING sub-millisecond ticks
(`ToUnixTimeMilliseconds`, `WitnessSigner.cs:80`), but the D083 display-integrity guard recomputed
the display timestamp by ROUNDING (`Math.Round(...TotalMilliseconds)`, pre-fix `:225-226`). When
`DateTime.UtcNow`'s sub-ms fraction was >= 0.5 ms, the rounded value was `signedTs + 1`, tripping
the equality guard and returning `DisplayMismatch` on an HONESTLY-signed snapshot — i.e.
`syxx witness verify` would call authentic evidence tampered roughly 3 runs in 5.

- Fixed `Windows/Syxx.Kit/WitnessSigner.cs:231-234` — the verifier now projects the display
  `Timestamp`/`ExpiresAt` to epoch millis with the same truncating
  `new DateTimeOffset(...).ToUnixTimeMilliseconds()` the signer uses. Verifier-only; no signed
  bytes, wire format, or signer behavior changed. The latent identical `expiresAt` rounding bug is
  fixed in the same edit.
- Evidence: 50000 fresh generate→sign→verify round-trips went from Valid=7894 / DisplayMismatch=12106
  (all 12106 the timestamp discrepancy, zero `InvalidSignature`) to 50000/50000 Valid. The
  `WitnessSignerTests` class ran 24× back-to-back post-fix: 24/24 runs, 13/13 tests, 0 failures.
  D083 preserved and mutation-proven: a display timestamp tampered by +1000 ms and by +1 ms both
  still read `DisplayMismatch`.
- Parity flag (NOT fixed here, out of scope): macOS's Swift signer+verifier both `.rounded()`, so
  it is self-consistent and does not flake — but that means macOS signs ROUNDED millis while Windows
  signs TRUNCATED millis, an E011 signer-convention divergence that would break a shared
  fixed-timestamp golden vector. Handed to the signing-convention owners for a parity-check pass.

## Windows Witness GUI regression tests for B195/D051/D093/D094/D095/D052 (2026-07-22, syxx-engineer)

Test-writing only, per task scope — did not touch `AppViewModel.cs`/`WitnessView.xaml(.cs)` logic
beyond one minimal, behavior-preserving constructor-injection seam.

- Added `Windows/SyxxUI/ViewModels/AppViewModel.cs:29-56` — a second `AppViewModel` constructor
  overload taking `Func<string,string,Task<ComparisonResult?>>`, injecting the D051 live
  vantage-comparison fetch `WitnessTestifyAsync` performs at sign time. The parameterless
  constructor (still what `MainWindow.xaml.cs` calls) wires the real
  `VantageComparer.CompareVantagePointsAsync` call unchanged; production behavior is untouched.
  Mirrors the identical seam already shipped for Ubuntu's `WitnessViewModel`
  (`Ubuntu/SyxxUI.Avalonia/ViewModels/WitnessViewModel.cs:67-87`). Also updated the one call site
  inside `WitnessTestifyAsync` (`AppViewModel.cs:~419`) to call the injected delegate instead of
  the static method directly.
- Added a NEW test project, `Windows/SyxxUI.Tests/SyxxUI.Tests.csproj` (net10.0-windows10.0.26100.0,
  `UseWinUI=true`, references `SyxxUI.csproj`) — discovered mid-task that the existing
  `Windows/Syxx.Tests` cannot host any `AppViewModel`-referencing test: it targets plain `net10.0`
  and runs on Linux CI legs, which cannot restore the windows-only `SyxxUI.csproj`. Filed as
  **DELTAS.md D101**.
- Added `Windows/SyxxUI.Tests/AppViewModelWitnessTests.cs` — six tests: empty-domain/no-comparison
  (B195/D051), missing signing key (B195), `WitnessFree()` actually clears
  snapshot/signature/valid/verification (D094), a forced live-comparison exception via the new seam
  (D051), `HasWitnessKey` reflects on-disk state from construction (D095), and a forced real DPAPI
  key-store write failure surfaces via `WitnessKeyError` (D052 Windows half).
- **Verification status: NONE.** No Windows/.NET-WinUI3 toolchain is available on this host. Neither
  the new test project nor the seam change to `AppViewModel.cs` has been restored, built, or run.
  Every assertion was checked by hand against the live source (type/method/property/enum names,
  call order, exact message strings) but this is the same "verified statically, NOT compiled" gap
  DELTAS.md already flags for B195's original Windows fix and for D092 generally. A real run on a
  Windows host with the WinUI 3 workload installed remains owed — see DELTAS.md D101 for the
  specific, larger unknown (whether the new project even restores/builds) on top of the ordinary
  "are these assertions right" unknown.

## Matrix determinative engine — integrated as the replacement verdict layer (macOS, 2026-07-21)

Owner directive (2026-07-21) superseding the same-day "remove all verdict synthesis" directive:
integrate the matrix-based determinative engine for real as the replacement. Landed on macOS
(`SyxxKit`); Windows/Ubuntu parity port is NOT yet done (follow-up).

**Landed and verified (compiles in-module; run on realistic evidence via an isolated swiftc harness):**

- `SyxxKit/Synthesis/MatrixDeterminativeEngine.swift` — full engine (present from a prior pass this
  session): `MatrixSpec`/`DiagnosisSpec`/`DiagnosisIndicatorRule`/`ThresholdProfile`/`EvidenceBundle`/
  `ObservedIndicator`/`SectionResult`/`DeterminativeResult`/`DiagnosisScore`/`VerdictState`, with
  `MatrixSpec.validate()` + `EvidenceBundle.validate()` invariants. The name-collision with the
  existing `Conclusion.SectionStatus` is resolved by a distinct `EvidenceUsability` enum plus an
  explicit `EvidenceUsability(sectionStatus:)` mapping. Abstention (`inconclusive`/`invalidated`/
  `untested`) is never emitted as a scored diagnosis — hard invariant, verified.
- `SyxxKit/Synthesis/SyxxMatrixIntegration.swift` — added (adapted from the draft): `SyxxSectionInput.status`
  retyped from the colliding `SectionStatus` to `EvidenceUsability`.
- `SyxxKit/Synthesis/Conclusion.swift` — added `public let determination: DeterminativeResult?`
  (nil only when the engine cannot evaluate — no verdict is manufactured on failure).
- `SyxxKit/Synthesis/SynthesisEngine.swift` — `synthesize(...)` now builds a `SyxxRunInput` from the
  raw evidence + section statuses, runs the engine, and attaches the `DeterminativeResult`. Includes
  a conservative, documented `EvidenceId → indicator-strength` mapping (threat indicators only;
  unmapped indicators stay absent, never fabricated). Benign runs reach `.inconclusive`, not an
  unearned `.clean`, because affirmative-clean signals are not yet plumbed.
- `SyxxApp/Views/Components/OverallVerdictView.swift` — renders the determination (state banner +
  emitted diagnoses + honesty "Unverified Signals" overhang) as a leading section, keeping the raw
  Checks-Run/Evidence/Exposure sections intact. Inconclusive never renders green.

**B193 (orphaned DST synthesis stack blocking the build) — RESOLVED.** The earlier "remove
synthesis" rewrite had orphaned the old DST stack, leaving `SyxxKit` non-compiling (~135 errors).
Reconciled per-file:

- Re-derived onto `Conclusion.determination`, public API preserved: `PlainExplanation.swift`,
  `VerdictAction.swift`, `ReportCard.swift` (A1/A2/A3 presentation), and `Report.swift`
  `renderText()` (datapoint dedup kept; dead verdict lines rewritten).
- Rewired `Synthesizer.swift` to the simple `SynthesisEngine.synthesize(...)`; dropped the DST-era
  answer-matrix / decisive-act / resolver-provenance inputs and `computeResolverProvenance`.
- Deleted removed-engine internals with no matrix equivalent: `Scorers.swift`, `Calibration.swift`,
  `DecisiveActCatalog.swift`, `Stage5Builder.swift`, `Stage5Presentation.swift`, `RPNOccurrence.swift`.
- App: `ConclusionsView` Stage-5 block removed; `DashboardView` verdict labels re-derived from
  `determination` (honest — shows engine `strength`, never a fabricated 0-100 confidence). Deleted
  the Stage-5 reasoning views: `Stage5ReasoningView.swift`, `Stage5PreviewFixture.swift`,
  `Stage5Copy.swift`.
- CLI: `SyxxCLI.swift` + `Synthesis/FirstRunScan.swift` verdict/referral rendering re-derived from
  `determination`.

**Dropped user-facing functionality (called out, not silently removed):** the Stage-5 belief-state
"reasoning" surface — provenance partition (direct/inferred/unavailable), mechanism-class headline,
within-class RPN-ordered findings, completeness meter, referral copy — is GONE on macOS. It rendered
the removed DST engine's `Conclusion.stage5`, which no longer exists and has no matrix-engine
equivalent yet. The matrix verdict + its evidentiary basis is now shown in `OverallVerdictView`
(Determination section) + the A1/A2/A3 cards. Also dropped: the Reddit "second-opinion / referral"
block and the numeric 0-100 confidence (the matrix engine emits per-diagnosis `strength`, not a
calibrated confidence percentage).

**Build state after this work:** `swift build` (clean `rm -rf .build` full build) is **fully green —
all targets**: `SyxxKit` ✅, `SyxxCLI` ✅ (`syxx` links + runs), `Ccage` ✅, `SyxxApp` ✅, and
`syxx-gui` links ✅ ("Build complete!"). An earlier report of "23 SyxxApp errors from an unfinished
GUI rebuild / missing AppModel API (B194)" was a MISDIAGNOSIS: those were spurious Swift whole-module
`@EnvironmentObject` dynamic-member cascade diagnostics produced by the real synthesis-dead-API
errors in `DashboardView.swift`, served stale from an incremental-build cache. `AppModel` had all the
cited members all along. B194 withdrawn; see DELTAS.md.

**Runtime evidence — the macOS GUI runs.** `syxx-gui` was wrapped in a minimal `.app` bundle (a bare
SPM binary crashes at launch because `UNUserNotificationCenter.currentNotificationCenter()` in the
app delegate requires a bundle identifier) and launched via `open`. It renders a live window (title
"Syxx 2.0.0", 1050×732) — Warden theme, full sidebar (Dashboard/Prophet/Mirror/Portal/Glutton/Cage/
Witness/Conclusion), Prophet view active. Verified by screenshot (2026-07-21). NOTE: running the raw
`.build/debug/syxx-gui` binary directly (or `swift run syxx-gui`) crashes on the UserNotifications
bundle requirement — it must run from an `.app` bundle (Xcode/packaged build); this is pre-existing
app packaging, not from this work.

**Runtime evidence — the engine, on real DNS.** `syxx firstrun github.com` ran Prophet/Mirror/
Portal/Glutton (Cage/Witness did not run) and the matrix engine returned **inconclusive** —
"unfinished, not clean", grade N/A (never a fake letter), no manufactured verdict — with the A1/A2/A3
layers rendering correctly from `determination`. Honest-inconclusive over unearned clean, confirmed
on real data.

## GUI Warden Theme Migration — all four platforms (2026-07-21)

Complete rebrand of every GUI surface from the old "Io" dark-tech theme (Space Grotesk, `#130d26` canvas, `#8b5cf6` violet) to the new "Warden" dark-fantasy theme (Cinzel/EB Garamond, `#0b0518` canvas, `#7c5cff` violet, `#2dd4bf` teal, `#d9a441` gold). All mockup HTML files serve as the single source of truth.

**macOS:**

- `IoDesignSystem.swift` rewritten: 30+ color/gradient/typography tokens. Canvas `#0b0518`, window/sidebar gradients, wordmark tri-gradient (orange/pink/violet), primary/teal/red button gradients, health meter, pro covenant, Cinzel display + EB Garamond lore + system-ui + ui-monospace font stacks.
- `ContentView.swift` rewritten: 236px WardenSidebar with Elder Futhark rune nav, gold dividers, Pro lock indicators, Dimensions gold external link, "Start your story anew" footer pill, Syxx gradient wordmark.
- `DashboardView.swift` rewritten: "THE WATCHTOWER" eyebrow, Cinzel title, trust ring + health meter banner, transport/resolver pills, 3 action buttons, 2-column warden grid + verdict card + chronicle, Pro covenant banner.
- `ProphetView.swift` rewritten: 54x54 rune tile header, Cinzel name, EB Garamond lore, domain input card.
- `MirrorView.swift`, `PortalView.swift`, `GluttonView.swift`, `CageView.swift`, `WitnessView.swift`, `ConclusionsView.swift`: All updated with Warden warden headers (rune tiles, Cinzel names, lore quotes). All existing functionality preserved.
- Artboard HTML created at `Syxx GUI Mockups/Syxx Artboard (standalone).html` for branding reference.

**Windows:**

- `IoDesignSystem.xaml` rewritten: Full Warden token set (164 lines). New canvas `#0B0518`, window/sidebar gradients, wordmark/primary/teal/red button gradients, health meter, pro covenant, content glow, gold nav separators, Cinzel/EB Garamond font families with Segoe UI/Cascadia Mono fallbacks.
- `MainWindow.xaml` rewritten: 236px pane, Elder Futhark rune nav (ᚨ, ᛞ, ᛜ, ᚦ, ᚾ, ᛉ), gold PRO lock indicators, Cinzel 27px wordmark with tri-gradient, 9.5px subtitle with letter-spacing, "Start your story anew" pill button, gold group dividers. Fixed missing `IoFontGrotesk` references (replaced with `IoFontUI`).

**Linux (Avalonia):**

- `App.axaml`: Added `SyxSurfaceDarkBrush` and `SyxBorderSubtleBrush` aliases for views that referenced Io* names.
- 10 view AXAML files updated (159 total resource replacements): `ProphetView`, `MirrorView`, `PortalView`, `GluttonView`, `CageView`, `WitnessView`, `ConclusionsView`, `SettingsView`, `WhatsNewView`, `LicenseView`. All `Io*` resource keys replaced with `Syx*` equivalents. No layout/structure/binding changes.

**Web Server:**

- `index.html`: Font import changed from Space Grotesk/Mono to Cinzel/EB Garamond. All 17 `--io-*` CSS variables updated to Warden values. Body gradient changed to `radial-gradient(130% 80% at 15% 0%, #1a0c33, #0b0518)`. Wordmark uses Cinzel with tri-gradient. Nav background darkened. Buttons use violet gradient. Terminal background updated.
- `purchase-success.html`: Same font/variable/gradient updates. Hardcoded violet/secondary colors updated.
- `purchase-cancel.html`: Same font/variable/gradient updates. Gold border color updated.

**Remaining:**

- Cinzel/EB Garamond font files not yet bundled in macOS Resources (falling back to system serif). Font registration in `SyxxAppDelegate` needs updating once fonts are added.
- Whats-new HTML files (`whats-new-mac.html`, `whats-new-win.html`, `whats-new-deb.html`) still use Quicksand/gold-lavender palette (embedded in desktop webviews, separate design system).

## macOS Warden view build fixes (2026-07-21)

The Warden-themed views (`DashboardView`, `ProphetView`) referenced AppModel API members
that did not exist, causing ~30 compilation errors. Root cause: the view rewrite assumed a
result-per-warden property surface that AppModel never had.

**Changes landed (macOS only; Linux/Windows unaffected):**

- **`AppModel.swift`** — added `ProphetResult` struct (consensus + resolver responses) for
  structured Prophet display. Added `@Published var prophetResult: ProphetResult?`,
  `@Published var mirrorResult: SyxxKit.ComparisonResult?`, `@Published var leakResult: LeakTestResult?`.
  Added computed `var isEncrypted: Bool` from `cachedEncryptedDNSStatus?.state == .yes`.
  Added `func runAllWardens()` (runs all six tools in sequence, respects Pro gating).
  Added `func flushDNSCache()` (macOS `dscacheutil -flushcache`). All new properties cleared
  in `resetAll()`.
- **`AppModel.swift:runLookup()`** — populates `prophetResult` after DNS query + plan run.
- **`AppModel.swift:runCompare()`** — populates `mirrorResult` from `ProbePlan.runMirror()`.
- **`AppModel.swift:runLeak()`** — populates `leakResult` from sectionInputs leak.
- **`DashboardView.swift`** — fixed `model.enableUnbound()` → `model.setUnbound(true)`.

**Build verification:**

- macOS: `swift build` — 0 errors.
- Linux: `dotnet build` — 0 errors.
- Windows: not buildable on macOS host (expected).

## A5/D-4 — OS notification delivery for continuous monitoring (2026-07-21)

Continuous monitoring already detected network config drift via `MonitorScheduler` + `ContinuousMonitor`,
but the user had no way to learn about a change without having the app open. Now both platforms
deliver a system notification when a drift is detected.

**Changes landed:**

- **macOS `SyxxApp.swift:4`** — added `import UserNotifications`.
- **macOS `SyxxAppDelegate.applicationDidFinishLaunching` (line ~11)** — requests
  `UNUserNotificationCenter` permission (`.alert` + `.sound`). Result ignored: if denied, the
  in-app latched display remains the only channel.
- **macOS `AppModel.swift:112-126`** — inside the `monitor`'s `onTick` closure, after existing
  state-latching, posts a `UNNotificationRequest` when `tick.state == .changed`,
  `notificationsEnabled == true`, and `tick.alerts` is non-empty. Uses real `MonitorAlert.headline`
  as the body. Unique identifier prevents coalescing.
- **macOS `SyxxSettings.swift:21`** — comment updated from "delivery (G5) is not built yet" to
  "stored preference for OS notification delivery when monitoring detects a drift".
- **Windows `AppViewModel.cs:53-69`** — inside the `MonitorScheduler` `onTick` lambda, after
  existing state-latching, builds a `ToastText02` XML template and shows it via
  `ToastNotificationManager`. Title: "Syxx — Network Change Detected". Body: real
  `tick.Alerts[0].Headline` (falls back to `tick.Summary` if no alerts). Entire block in `try/catch`
  so notification failure never crashes the monitor loop.
- **Windows `SyxxSettings.cs:27`** — comment updated to match macOS.
- **Windows `SettingsView.xaml:62-64`** — notification toggle help text changed from
  "takes effect when monitoring ships" caveat to live feature description:
  "You will receive a system notification when continuous monitoring detects a network configuration change."

**Design invariants:** real alert text only (no fabricated strings); `notificationsEnabled` preference
respected; notification failure wrapped so the monitor loop is never interrupted; existing latched
menu-bar/tray display untouched.

**ROADMAP updates:** D-3 marked ✅ (menu-bar/tray trust indicator already existed on both platforms);
D-4 marked ✅ (this change); A5 and A6 marked [done] in the Phase A implementation plan.

## A4 — Five-minute first-run CLI subcommand wired on both platforms (2026-07-21)

The core first-run scan engine (`FirstRunScan.swift` / `FirstRunScan.cs`) and the CLI command
wrapper (`FirstRunCommand`) existed but were unreachable from the unified binary entry points on
both platforms. Now `syxx firstrun` works on macOS and Windows.

**Changes landed:**

- **macOS `SyxxCLI.swift:25`** — added `case "firstrun": await FirstRunCommand.run(args:)` to the
  subcommand switch, and `firstrun` to the `printHelp()` listing.
- **Windows `Program.cs:44`** — added `case "firstrun": await FirstRunCommand.RunAsync(args[1..])`
  to the subcommand switch, and `firstrun` to the `PrintHelp()` listing.
- **Windows `Commands/FirstRunCommand.cs`** (new) — byte-parity CLI wrapper matching macOS
  `FirstRunCommand`: parses `--domain` / `-d`, validates via `DomainCanonical.CanonicalizeOrNull`,
  prints the box header, calls `FirstRunScan.RunAsync`, renders grade / top reasons / ran+skipped /
  verdict action / plain explanation / referral. Honesty contract: scan failure prints INCONCLUSIVE,
  never a fabricated grade.

**Parity:** both platforms now support `syxx firstrun [--domain <domain>]` as a CLI subcommand,
in addition to the interactive `[S] Scan` menu option (Windows) and the GUI scan button.

## WS10-02 (E053): culture-sensitive string/number handling eliminated, analyzer guard in place (2026-07-21)

E053 was CRITICAL: two Ubuntu hosts with different locales could reach different verdicts because
`OrderBy(x => x)` used CurrentCulture, `EndsWith`/`StartsWith`/`Contains`/`IndexOf`/`Replace`
defaulted to CurrentCulture, and numeric format strings used the culture's decimal separator.

**Changes landed (Windows/Syxx.Kit):**

- **Ordering** (6 files, ~10 sites): `StringComparer.Ordinal` added to all `OrderBy`/`ThenBy` calls
  in `AuthoritativeVantage.cs`, `AnswerStabilityProbe.cs`, `WildcardSynthesisProbe.cs`,
  `SVCBProbe.cs`, `SignedIncidentReport.cs`.
- **Prefix/suffix/comparison** (12 files, ~40 sites): `StringComparison.Ordinal` added to all
  `StartsWith`, `EndsWith`, `Contains`, `IndexOf`, and `Replace` calls in `AuthoritativeVantage.cs`,
  `CNAMEChainProbe.cs`, `DenialOfExistenceProbe.cs`, `DomainCanonical.cs`, `DNSLogAnalyzer.cs`,
  `VersionBindProbe.cs`, `EndpointLogAnalyzer.cs`, `Forensics.cs`, `DNSLogAnalyzer.cs`,
  `LanCartography.cs`, `LookalikeDetector.cs`, `MachineFingerprint.cs`, `NetworkKeyState.cs`,
  `TextDiff.cs`, `Synthesizer.cs`.
- **Numeric/date formatting** (8 files, ~15 sites): `CultureInfo.InvariantCulture` added to all
  `ToString`, `string.Format`, and `Convert` calls in `OpenResolverProbe.cs`,
  `ResolverLatencyProbe.cs`, `DNSCaptureReportGenerator.cs`, `Synthesizer.cs`, `DNSCapture.cs`,
  `ContentWarp.cs`, `DNSResolver.cs`, `LicenseManager.cs`, `NetworkKeyState.cs`,
  `ProbeRunner.cs`, `DNSLogAnalyzer.cs`, `EndpointLogAnalyzer.cs`.
- **Casing**: `.ToLower()` replaced with `.ToLowerInvariant()` in `Synthesizer.cs`.
- **Analyzer guard**: CA1305/CA1307/CA1310 enabled as ERRORS in `Syxx.Kit.csproj` to prevent
  recurrence. The class is invisible in review and invisible in a US-locale CI run, so the
  analyzer is the fix that prevents regression.

**Build verified:** 0 culture-related errors, 0 CA1305/CA1307/CA1310 violations. All remaining
warnings are pre-existing (CS8625 nullable, SYSLIB0057 obsolete, CA1416 platform-specific).

**Not yet landed:** non-invariant locale CI leg (Fix 3 in E053).

## WS8-04 (B152) partial: referral reasons now name the CAUSE, not the state (2026-07-20)

Landed the `referralReason` half of B152 on both platforms. The naming gate (contract §3) and the
B153 triplet are NOT in this change.

**Three defects closed, all live before this change:**

- **No branch for `dampenerForcedReferral`.** Control fell through to the final unconditional
  `else`, which returned the MARGIN text. A captive-portal referral was reported to the user as
  "the top two possibilities are within N points", quoting whatever the margin happened to be,
  up to the full 95. `SynthesisEngine.swift:744`, `SynthesisEngine.cs:681`.
- **Branch order did not match the trigger set.** `conflict` was tested ABOVE `hardGated`, so an
  input that is both hard-gated and conflicted was explained as "the sections disagree" when the
  actual cause was a check that never ran. Reproduced under mutation on both platforms at conflict
  95%.
- **`verdict == .inconclusive` was treated as a CAUSE of referral.** It is not. Inconclusive is the
  fallback rendered when no diagnosis can be safely reached; it is the RESULT of a cause, never a
  peer of `hardGated` and `dampenerForcedReferral`. The branch is deleted, not reworded, and
  `verdict` is gone from the `referralReason` signature on both platforms because nothing else in
  the function needed it. The file-header spec text stated the same error and was corrected.

**Owner-approved new string** (dampener branch, both platforms, identical bytes):
"While this result looks suspicious, it can also have harmless causes. For example, a captive
portal (think of connecting to the WiFi at a hotel or airport) or encrypted DNS (such as a VPN,
DNS over HTTPS, TLS, or QUIC) can give off the same or similar signals."

## WS8-04 / WS8-05 final: naming gate + scriptMismatch removed (2026-07-20)

**Contract:** `WS8-04-05-CONTRACT.md` §1-4 — threat naming requires decisive act OR ≥2 independent support groups.

**Changes landed:**

- **macOS SynthesisEngine.swift:335-340** — verdict naming now requires `belThreatSet >= threatNameFloor && positiveSupportGroups >= 2` (previously lacked corroboration check)
- **Windows SynthesisEngine.cs:315** — twin change for naming gate
- **macOS SynthesisEngine.swift:391-407** — referral gate restructured into `suppressibleByAct` / `neverSuppressed` sets; `belowMinCorroboration` removed from trigger list
- **Windows SynthesisEngine.cs:385-395** — twin change for referral gate
- **macOS DecisiveActCatalog.swift:11,32-34** — version bumped to 2, scriptMismatch entry removed
- **Windows DecisiveActCatalog.cs:14,34** — twin change
- **macOS Synthesizer.swift:282-283** — scriptMismatch status `.positive` → `.inconclusive`, rationale clarified
- **Windows Synthesizer.cs:394-395** — twin change
- **macOS Conclusion.swift:278** — scriptMismatch weight lowered to 0.4 (was 2.0)
- **Windows Conclusion.cs:222** — twin change

**Invariant verified:** Single-source evidence now produces `verdict == .inconclusive` with the observation still visible in evidence.

**Test update:** `testStrongSingleThreat_winsOverCleanSections` → `testStrongSingleThreat_noLongerNames_withoutCorroboration` now asserts the correct behavior.

**Terminal fallback now quotes no figure.** The old unconditional `else` returned the margin text
on every unmatched path. It is now `else if thinMargin` with a fallback naming no number, so an
unmatched path cannot fabricate one.

**Tests: 4 new, all mutation-proven RED before green.**
`SynthesisEngineTests.swift` (macOS) and `SyxxKitTests.cs:ReferralReasonCauseTests` (Windows):

- `DampenerReferral_ReportsTheMechanism_NotAMargin` — mutation: delete the dampener branch. RED.
- `HardGateOutranksConflict_InTheStatedReason` — mutation: restore conflict above hardGated. RED,
  emitting "The sections disagree (conflict 95%)" on both platforms, identical value.

Two construction traps are recorded in the test comments because both produced tests that proved
nothing on the first attempt: `confirmedThreats` is seeded from the SECTION STATUS being
`.positive`, not from evidence existing; and hard-gating suppresses clean support, so `hardGated`
and `conflict` co-occur only through threat-vs-threat conflict on disjoint hypotheses. Both
preconditions are now asserted inside the test so it cannot silently stop testing its own claim.

macOS 589 tests / 0 failures. Windows `Syxx.Kit` 0 errors; the 2 new tests pass every run.

**NOT fixed, pre-existing:** `WitnessSignerTests` fails 3 of 13 in isolation, and the full Windows
suite failure count varies between 2 and 4 across runs with no code change, so it is order- or
time-dependent on top of the baseline. Untouched here. Filed separately.

## WS8 verdict-integrity chain: B151/B155/B154 verified already fixed in production code; B152/B153 gated on planner (2026-07-20)

Verified live against production code that WS8-01 (B151), WS8-02 (B155), and WS8-03 (B154) were
already fixed in the tree — the HANDOFF/ROADMAP described the pre-fix state.

- **B151 (WS8-01):** `SectionEvidence.init` requires `completeness: Double` (no default); `DatapointCompleteness`
  - 4 catalog constants at `Synthesizer.swift:575-588`; all 13 macOS adapter sites pass real N/M; Windows
  unchanged. 3 `SectionCompletenessTests` pass (`SynthesizerDatapointWiringTests.swift:619-649`).
- **B155 (WS8-02):** `SynthesisEngine.swift:244-248` — degraded dampeners use `degradedSectionReliability`;
  untested/invalidated sections skip via `continue`. Windows twin at `SynthesisEngine.cs:210-214`.
- **B154 (WS8-03):** `SynthesisEngine.swift:288-298` — discrepancy channel seeds `confirmedThreats` with
  `.spoofing`/`.interception_onPath` when mass >= `threatNameFloor`; dampener path refers rather than cancels.
  Windows twin `SynthesisEngine.cs:253-268`.
- **B152/B153 (WS8-04/05):** Still open, gated on `syxx-neural-planner` contract decision per HANDOFF §3.
- **B136 (WS8-06):** Empty-set guard already present in `Synthesizer.cs:374-383`; DELTAS.md comment
  "never ported" was stale. Moved to `DELTAS-fixed.md`; WS8-06 marked **DONE** ✅.

Ledger changes: B151, B155, B154, B136 removed from `DELTAS.md` (0 occurrences confirmed); appended to
`DELTAS-fixed.md` with `[FIXED]` annotations. `ROADMAP.md` WS8-01/02/03/06 marked **DONE** ✅.

## WS9 forgeable-evidence chain: D083/D086/D087/B157/D084/D085 verified already fixed in production code (2026-07-20)

Verified live against production code that all six WS9 items were already fixed in the tree.

- **D083 (WS9-01):** `WitnessSigner.verifySnapshot` validates display `timestamp`, `expiresAt`,
  `formatVersion` against signed evidence on both platforms
  (`WitnessSigner.swift:176-192`, `WitnessSigner.cs:221-240`).
- **D086/D087 (WS9-02):** `--gen-keypair` writes private key to `0600` file, prints only path + pubkey
  (`SyxxWitness.swift:9-12`); `--fetch-pubkey` requires HTTPS, asserts HTTP 200, validates 32-byte
  Ed25519 key, prints SHA-256 fingerprint (`SyxxWitness.swift:122-143`).
- **B157 (WS9-03):** `SignedIncidentReport.render()` guards empty-set BEFORE the flag chain on both
  platforms (`SignedIncidentReport.swift:59-64`, `SignedIncidentReport.cs:63-65`).
- **D084/D085 (WS9-04):** canonicalization uses explicit ASCII allowlist over Unicode scalars on both
  platforms; `canonicalMerge` deduplicates ordinally and sorts by UTF-8 byte sequence
  (`Stage5Builder.swift:143-159`, `Stage5Builder.cs:110-111`).

None of these deltas remain in `DELTAS.md`; they were resolved during the 2026-07-20 cleanup sweep.
`ROADMAP.md` WS9-01/02/03/04 marked **DONE** ✅.

## D081 error-collapse pattern analysis (2026-07-20)

Analyzed the error-handling pattern in `LeakDetector.swift` for the ~40 D081 sites. The pattern is:

- On ANY network/parse/error → return `.verdict = .inconclusive` + `errorReason: "description"`  
- Never conflate "error" with "negative finding" or "clean"
- Callers can distinguish `.inconclusive` (error) from `.negative` (measured clean) vs `.positive` (tamper found)

macOS sites to apply this pattern to:

- `EndpointLogAnalyzer.swift:39-40` — ✅ DONE `streamLines` silently returns on file-open failure → `EndpointLogAnalysisResult` enum now returns `.error("reason")` vs `.notApplicable` vs `.success(report)`
- `DimensionClient.swift:78-81` — `get<T>` returns nil on any failure → should return `.inconclusive` with error reason  
- `SieveBlocklistLoader.swift:19-21` — `loadFromFile` returns nil on any error → should return meaningful error

Windows counterpart: `LanCartography.cs:353-358` has the identical defect pattern (was removed from DELTAS.md during cleanup but the code defect remains).

## Ledger evaluation and cleanup sweep (2026-07-20)

Performed a comprehensive delta evaluation and cleanup sweep of the `DELTAS.md` ledger:

- Evaluated all 450 deltas across all domains (A-F).
- Relocated 139 deltas believed fixed to `DELTAS-fixed.md`. **Lifecycle correction
  (2026-07-30):** live-source re-verification found seven of those closures false:
  `WINX-142` through `WINX-147` and `MACX-183`. They were removed from the fixed ledger and mapped
  to open canonical records `C139`, `C140`/`C141`, `C151`, and `C156`.
- Relocated 87 stale/retired deltas to `DELTAS-rejected.md` (where at least one other agent had voiced their opinion that it is stale/retired).
- Retained 227 open deltas in `DELTAS.md` and added Antigravity's comment to each, aligned to column 24 (or column 48 for comment-to-comment threads) exactly matching the house template.
- Verified that `reflow-deltas.py` generates 0 diffs on the updated `DELTAS.md`.
- Identified and added seven new confirmed deltas: **[D078]** (macOS TracerouteRunner Process Deadlock), **[B149]** (DoH Queries Omit DNSQuery Configuration Fields), **[B150]** (DenialOfExistenceProbe Fails to Set DO Bit), **[D079]** (macOS GUI UI-Thread Block via Synchronous Trial Anchor Sync), **[D080]** (WinUI 3 Cross-Thread Exception in AppViewModel Monitor Tick Handler), **[E052]** (Platform Parity: IPClassifier (macOS) Misses Site-Local fec0::/10 Check), and **[A131]** (Server Licensing Signature Verification UTF-8 Normalization/Loss Hazard), and **[D091]** (Windows WinUI 3 GUI Compilation Errors and Nullability Warnings). Each has been commented on by Antigravity and reflowed.

## Settings panes on all three clients (G-4) (2026-07-19)

The first Settings surface, on macOS (SwiftUI), Windows (WinUI), and Linux (Avalonia). G-4's core
requirement met: a **typed store** (`SyxxSettings` Swift + C# twins, JSON at `~/.syxx/settings.json`
on macOS, LocalApplicationData on Windows/Linux), not scattered UserDefaults/registry reads.
Round-trip + corrupt-file-falls-back-to-defaults tested (macOS 4, Windows 3).

Each pane has the same sections, wired to real actions:

- **Encrypted DNS** — the toggle calls the real `EncryptedDNSConfigurator` and renders the honest
  tri-state: macOS shows "needs the DNS-Settings entitlement" on the unsigned build, Windows "needs
  Administrator", Linux "needs root" — never a fake "on". Resolver + transport pickers.
- **Privacy** — telemetry opt-out with the full "what leaves your machine" disclosure (G12 groundwork).
- **Notifications** — a stored preference, explicitly labelled "no effect until monitoring ships"
  (delivery/G5 not built) rather than implying alerts are live.
- **Data** — "Remove stored data" really erases the Sentinel/home baselines + passive-DNS ledger.

**Honesty contract of the store:** a preference is an intent record, never a claim of effect —
`encryptedDNSEnabled = true` records the ask; whether DNS is actually encrypted is decided by
`classifyApply`, and surfaces read the real state, not the stored wish. Defaults are the safe resting
state (encryption OFF).

Verified: macOS builds + bundles + 575/575; Linux Avalonia builds + launches clean (all 11 views incl.
Settings construct eagerly). Windows WinUI is the VM build bar. All settings copy catalogued PENDING
APPROVAL. Server 217, Windows Kit 536.

## Encrypt-all-DNS: system encrypted-DNS configurator, profile-free (2026-07-19)

New protection capability (owner-confirmed "this was the plan"): Syxx configures the SYSTEM to send
every DNS query over DoH/DoT, using each OS's OWN programmatic API — **no downloaded `.mobileconfig`
profile, no local proxy** (the rejected B3). The audit path stays cleartext and separate; this is a
distinct protection mode.

**Mechanism per platform:** macOS `NEDNSSettingsManager` (NetworkExtension); Windows 11 native DoH
(`netsh dns add encryption` templates); Linux systemd-resolved DoT (`resolvectl`).

**Built:**

- `EncryptedDNSConfig` (`.swift` + `.cs` twins): curated no-log resolver catalog (Cloudflare default,
  Quad9, Google, + custom), DoH/DoT transport, and the honesty-critical `classifyApply` gate.
- macOS `EncryptedDNSConfigurator` (app target; NetworkExtension linked there, never the portable
  Kit): enable/disable/verify via NEDNSSettingsManager.
- Windows/Linux `EncryptedDNSConfigurator` (Kit, OS-guarded): netsh DoH / resolvectl DoT.

**Honesty gate (mutation-proven both platforms):** the app reports "your DNS is encrypted" ONLY when
the apply succeeded AND the system reports the setting active AND a real encrypted query completed.
Merely installing a configuration is NEVER called success — it resolves to Inconclusive. Mutations
that claim enabled on save-alone or without the verify probe red 1–2 tests each; reverted green.
Elevation/entitlement rejections are their OWN state, never a generic failure or a fake success.
Suites: macOS 571/571 (+6), Windows 533/533 (+6).

**OWNER / OS GATES — real-run cannot happen from this host; per the honesty protocol this is NOT
"done" until a real run on each platform:**

1. **macOS: the Apple DNS-Settings entitlement + a signed app.** `NEDNSSettingsManager.save`
   requires `com.apple.developer.networking.networkextension` = `dns-settings`, granted by Apple; the
   current unsigned SwiftPM build returns the permission error, mapped to `.entitlementRequired`.
   Same owner-track class as notarization.
2. **Windows/Linux: elevation.** netsh/resolvectl need Administrator/root; an unelevated call reports
   `ElevationRequired`, never a fake success.
3. **Captive-portal FAILSAFE (DESIGN.md, non-negotiable):** encrypting to an unreachable resolver on
   a portal network must auto-revert. That revert policy is an owner decision, NOT silently assumed.
4. **`NetworkKeyState.IsDNSEncrypted()` is a stub (`=> false`) on Windows/Linux** (WS-C1 engine gap),
   so the verify-active step there currently resolves to Inconclusive after a successful apply — the
   HONEST outcome (cannot confirm), not a false success. Implementing it for real is the prerequisite
   for reporting Enabled on those platforms.
5. **Upstream resolver default = Cloudflare** (compatibility/latency); user-selectable, never silently
   pinned. **UI copy for the on/off toggle is NOT built — pending owner approval** (AGENTS.md:42).

## Linux GUI: all ten discipline views built (2026-07-19)

The Avalonia Linux client now has its full view set, wired to the shared Kit and rendered through the
linked honesty layer. Nav order matches macOS/Windows: What's New (launch view), Dashboard, Prophet,
Mirror, Portal, Glutton, Cage, Witness, Conclusions, License. All ten construct eagerly in
`ShellViewModel`, so the clean launch (10s, zero exceptions) proves every view's XAML parsed and every
ViewModel constructed.

**Approved copy reused verbatim** from the Windows views (kept three-way parity; no new user-visible
strings except the honest platform-gating notices, which are catalogued PENDING APPROVAL).

**Real on Linux (verified: Kit probe layer runs on .NET here — gateway detected, and a timed-out
Prophet probe correctly returned Inconclusive/Ungraded, NOT false-clean):**

- **Prophet** — real resolution + DNSSEC; unsigned stated as "nothing to validate", not a pass;
  timeout/error → inconclusive.
- **Portal** — real resolvers, interfaces, VPN state, and the C1 gateway/router audit.
- **Mirror (Lite)** — real cross-resolver comparison; DEGRADED/INCONCLUSIVE render AMBER, only a real
  MATCH is green.
- **Glutton** — real local VPN/encryption read; the leak PROOF is collector-attested (server-backed).
- **Witness** — real Ed25519 sign AND verify (NSec is cross-platform), full forensic tri-state verdict.
- **Conclusions** — the A4 first-run scan → A1/A2/A3 + Stage-5 honesty surface (was ScanView, renamed).
- **Dashboard** — resolver identity/count real; every other stat a DASH until measured (never a
  fabricated zero).

**Honestly deferred, never faked** (the Windows-only Kit surfaces): Mirror/Glutton **Pro** checks and
**License** activation need the DPAPI keystore (G16) — shown as unavailable-on-Linux, never a fake
unlock or fake "no leak"; **Cage** live capture needs root/raw sockets (G17) — shows the elevated CLI
command, never a fake capture; **Witness** key persistence is the one G16 gap in an otherwise working
crypto path (key shown once to save).

**Standing bar (plan WS-C1):** pixel/behavior verification on the real arm64 Ubuntu ISO, and the
engine-honesty audit of every probe on Linux (the Prophet DNS timeout observed here is that gate, not
a view bug). Windows/Linux Kit suite still 527/527.

## Menu-bar icon: white Syxx mark + severity dot (2026-07-19, owner decision)

The macOS menu-bar trust indicator (A6) is now the white Syxx logo instead of an SF-Symbol shield.
Rendered as a TEMPLATE image, so AppKit tints it natively — white on a dark bar, dark on a light one
— the menu-bar convention a multicolor icon violates.

**The honesty tension, and how it's resolved:** a static logo can't show the six trust states, and
the rule is absolute — an unmeasured network must never look clean. So brand rides on the icon and
STATE rides on a small dot in the corner, colored by `Color.ioSeverity` (the SAME mapping the grade
and verdict badge use, so the three surfaces cannot diverge): green clean, purple info, amber
warning, orange high, red critical, **grey not-yet-scanned**. Clean-green vs unknown-grey are
distinct at a glance, so the false-clean is structurally impossible.

`MenuBarLabel` composes it; `TrayGlyph.syxxTemplateMark` builds the white silhouette from the bundled
logo once (falls back to a shield symbol if the resource is missing — an empty menu-bar item is
worse). The dead SF-Symbol `sfSymbol` mapping was removed.

`TrayGlyphTests` rewritten to guard the REAL channel: rendered each state, and mutation-proved
(map `.unknown` → `.ioSuccessGreen` → 3 tests red on the false-clean, reverted green). macOS 565/565.

## Linux GUI (Avalonia) started + debmotd MOTD plumbing (2026-07-19)

First code for the third client, per `Ubuntu/UBUNTU-PORT-PLAN.md` WS-E1.

**`Ubuntu/SyxxUI.Avalonia/`** — net10.0, RIDs `linux-x64;linux-arm64`, untrimmed. Closes the plan's
Section-12 item-7 open dependency empirically: the current line is **Avalonia 12.1.0 +
FluentAvaloniaUI 3.0.2** (the plan assumed 11.x), pinned exactly. Shell is `SplitView`-style
`Grid`+`ListBox` — the plan's named fallback, chosen deliberately so the honesty surfaces do not sit
on a control set that moves between FluentAvalonia versions. **What's New is the first nav item and
the launch view**, matching macOS and Windows.

**Linked, not forked** (WS-E1): `Presentation/Stage5DisplayModel.cs`, `Stage5Copy.cs`,
`ReasoningPresentation.cs`, and `Services/DimensionService.cs` compile from the Windows tree as the
same bytes. Only `SeverityBrush` is re-expressed (`Presentation/SeverityColor.cs`) because the
original returns WinUI brush types; the mapping is identical and **clean stays the only green**.

**NOT linked, with reason:** `ViewModels/AppViewModel.cs`. It depends on `WitnessKeyStore`, which is
`[SupportedOSPlatform("windows")]` (DPAPI/crypt32) and throws on Linux. Forking it would fork the
honesty layer, so it waits on a portable credential backend (PROPOSALS G16). The Linux shell uses a
thin `ShellViewModel` plus a `ScanViewModel` over the portable A1–A4 Kit surfaces instead.

**Verified:** builds clean; launches and stays up as a real windowed process on this Mac (Avalonia
runs here, unlike WinUI). A cross-DE render check on the arm64 ISO remains the plan's own bar.

### The MOTD, and a fabricated hostname I shipped

I invented `linmotd.syxx.app` by extrapolating from `macmotd`/`winmotd` and hardcoded it without
checking. It **resolves** (wildcard DNS → the same origin) but is **not on the certificate**, so the
owner's click produced a browser impersonation warning — the worst possible first screen for a
DNS-integrity product. Verified SAN list: `{syxx.app, www.syxx.app, macmotd.syxx.app,
winmotd.syxx.app}`. Owner chose **`debmotd.syxx.app`** (Debian/Ubuntu/Mint family, AGENTS.md:36).

Rather than ship a link that is broken today and silently fixed later, the client names `debmotd` as
its host, HEAD-probes it once per launch, and falls back to the apex until the cert covers it —
self-healing on reissue with no code change. Routing precedence (absolute server pointer > verified
platform host > apex) is shared in `Syxx.Kit/MotdRouting.cs`.

**Server plumbing for debmotd** (Server/ is nominally Cline's lane — flagged, done here because it
hard-blocks the Linux GUI):

- `config.py`: `MOTD_DEB_HOST`, `WHATSNEW_DEB`, `LATEST_DEB`, `UPDATE_URL_DEB`.
- `app.py`: MOTD Host routing and `/api/startup` were **two-way booleans** (`is_win`). A Debian
  client asking `platform=linux` fell through and was served **Mac** content and the Mac update
  channel. Both are now three-way over a `_WHATSNEW_PLATFORMS` table (linux/ubuntu/mint/debian/deb →
  deb; unknown → mac).
- `scripts/deploy.sh`: `debmotd` added to the nginx `server_name`, the `certbot --expand -d` list,
  and the SAN verification loop — the same three places whose omission caused the 2026-07-16 MOTD
  outage the script documents.
- `Server/whats-new-deb.html`: ported from the Windows page, which has **zero** platform-specific
  words, so **no new user-visible copy** was written. Two justified adaptations: external Google
  Fonts links removed (the server's own CSP `default-src 'none'` blocks them anyway, and a privacy
  tool's welcome page should not phone out), and the fixed `860x720; overflow:hidden` webview
  viewport made responsive because Linux opens this in the user's real browser, not a sized panel.

**New shared guard `Syxx.Kit/RemoteUrlPolicy.cs`** — https-only + host allowlist for any
server-supplied URL before it reaches the OS opener. 15 tests; mutation matrix: (M1) drop the
https check → `file://`/`smb://` pass, red; (M2) `EndsWith(domain)` without the leading dot →
`evilsyxx.app` matches, red; (M3) allow any host → red. macOS `BroadcastPanelView.swift:39` still
opens broadcast URLs unguarded (**delta D045, open**) and should adopt this.

**Suites:** Windows/Linux 527/527. **Server suite: 217 passed** (venv repaired; `cryptography`
49.0.0 + `pytest` 9.1.1 now installed). Added `WhatsNewPlatformRouteTests` (4) that drive the real
HTTP Handler with per-platform Host headers and `?platform=` values, proving each platform gets its
OWN content. Mutation-proven: reverting the query route to the old two-way boolean reds the
Linux→Debian test (it served Mac), reverted green.

## C5 home network baseline (2026-07-19, both platforms)

New `HomeBaseline` (`.swift` + `.cs` twins) on the existing Trust primitives: the user marks their
home network known-good once (`syxx sentinel --set-home`), and every other network is diffed against
it (`syxx sentinel --home`) so "this network differs from home in N ways" lists the real resolver /
interface / VPN / encryption deltas. Distinct file from the rolling Sentinel snapshot (home-vs-here,
not then-vs-now; does not roll forward). Pure testable `compare` core.

**Honesty invariant:** no home baseline → `hasBaseline == false`, NEVER "matches home" (an
un-profiled machine cannot claim every network is home — the false-clean class). `matchesHome` is
true only when a baseline exists AND there are zero diffs.

**Real runs (both runtimes, production `syxx sentinel`):** `--home` with no baseline → honest "No
home baseline set…"; `--set-home` saves; `--home` again → "matches your home baseline"; an injected
differing baseline → "differs from home in 2 ways" listing the resolver + encryption deltas.

**Mutation matrix (both platforms):** (M1) no-baseline reports a match → false-clean test red; (M2)
skip the diff → matches-regardless test red. All reverted green.
Suites: macOS 564/564 (+5), Windows 512/512 (+4).

## C7 per-app resolver attribution (2026-07-19, both platforms)

New `ResolverAttribution` (`.swift` + `.cs` twins) JOINS the two facts a capture already held but
never combined — which PROCESS made each query, and which RESOLVER answered — to expose an app
with a HARDCODED resolver that ignores the system's DNS settings (a privacy-bypass / malware shape).
Pure testable core (`classify` + `usage`); wired into the Capture Query Report as a new "PER-APP
RESOLVER ATTRIBUTION" section on both platforms.

**D063 caveat carried, never implied away:** the resolver is keyed on the PORT-53 side, NOT the
(forgeable) QR bit; capture is UDP:53 only, so the report states "no off-system resolver means none
over UDP:53, not none at all"; an empty system-resolver baseline yields `baselineUnknown` (no bypass
judgment), never a clean bill or a fabricated flag.

**Real run (production `DNSCaptureReportGenerator.generateReport` over a real NDJSON capture):** a
two-process capture (Safari→10.0.0.1, curl→8.8.8.8) renders the PER-APP section naming each process
with its resolver and the coverage disclosure — verified by a permanent integration test, mutation-
proven (drop the join → section reads "None observed", test reds).

**Mutation matrix (both platforms):** (M1) don't subtract the system set → bypass reads clean, test
red; (M2) flag with an empty baseline → bypass-against-nothing, test red; (M3) key on the QR bit not
the port → forged-direction misattribution, test red. All reverted green.
Suites: macOS 559/559 (+6), Windows 508/508 (+4).

## Third-party product names removed from the codebase (2026-07-19, user directive)

The names **DNSMonitor**, **FileMonitor**, **ProcessMonitor**, and **Objective-See** are other
people's products; Syxx does not own them and did not use their code. Per the owner's directive they
must appear NOWHERE — strings, comments, or identifiers. Swept both clients + Server + tests:

- Identifier `DNSMonitorLogParser` → `CaptureLogParser` (Syxx-owned) on both platforms and all call
  sites (`DNSLogAnalyzer.swift`/`.cs`, `SyxxForensics.swift`, `ForensicsCommand.cs`, both test files).
- Comments describing the parsed format rewritten generically ("a PROCESS/PACKET text capture-log
  format") with no product credit; the `MonitorReportAnalyzer` "not to be confused with…" aside removed.
- `EndpointLogAnalyzer` header genericized to "endpoint file/process activity logs" on both platforms.
- User-facing CLI strings (`syxx forensics` usage + error messages) now say "capture query log or
  endpoint activity log", never the product names. (`MonitorReportAnalyzer` and `EndpointLogAnalyzer`
  contain no forbidden substring and keep their names.)
- Verified: zero occurrences of any of the four terms remain in *.swift/*.cs/*.xaml/*.py; macOS
  553/553 and Windows 504/504 still green after the rename.

## C6 lookalike domain alerts (2026-07-19, both platforms)

New `LookalikeDetector` (`.swift` + `.cs` twins) flags query-stream domains impersonating a known
brand via (a) homoglyph substitution (Cyrillic/Greek/digit glyphs that render like Latin —
`pаypal.com`), (b) mixed-script labels, or (c) one-edit ASCII typosquats (`gooogle.com`).
Deterministic and explainable (no ML): a confusables-skeleton fold + bounded Damerau-Levenshtein
against a 15-brand list; every finding names the brand and the mechanism. Honesty posture: a
deceptive-name shape is a `suspicious` finding a human confirms, never a malice verdict; the genuine
brand and unrelated domains never flag.

Wired into the Capture Query Report (`DNSLogAnalyzer` → `DNSLogAnalysis.lookalikeFindings` + a
"Lookalike / Brand-Impersonation Domains" section + an interpretation line) on both platforms —
its natural home in the live query-analysis surface.

**Real run (production `syxx forensics --analyze`):** a capture log with the wire-form A-label
`xn--pypal-4ve.com` and `gooogle.com` renders "pаypal.com → impersonates paypal.com [mixedScript]"
and "gooogle.com → impersonates google.com [typosquat]" — the punycode is decoded to its display
form (real captures carry the xn-- form, not Unicode) and both are surfaced.

**Mutation matrix (every fail state demonstrated, both platforms):** (M1) confusables not folded →
homoglyph tests red; (M2) typosquat widened from `== 1` to `<= 1` → genuine-brand test red (proving
the real logins never false-alarm); (M3) mixed-script forced false → Cyrillic-spoof reason test red.
All reverted green. A first-pass genuine-brand test was found to NOT bite (defense-in-depth masked
it); the redundant `reg == brand` short-circuit was removed so branch-3's `== 1` is the single
testable guard — per the owner's rule that any single break must be catchable.
Suites: macOS 553/553 (+9), Windows 504/504 (+8).

## Phases A–B audit + A5/A6 Windows gap-fill + C1 complete (2026-07-19)

**Phase A/B audit (against live code, not documents):** A1–A4 Kit twins present both platforms;
A5 (ContinuousMonitor/MonitorScheduler) + A6 (menu-bar TrayIndicator) fully wired on macOS —
real run: `syxx sentinel` detected the live resolver/interface config and reported honest
stability. B1 signed report + B2 verifier present on both platforms (Windows `witness verify
--pubkey/--fetch-pubkey/--detailed` confirmed in code); B3 stays owner-gated. ROADMAP D-3's
"no status item on either platform" is STALE — macOS has the MenuBarExtra with latched change
alerts.

**A5/A6 Windows gap-fill (VM build/launch is the bar):** AppViewModel gains the macOS-twin
monitoring block (900s scheduler, off by default, latched LastMonitorChange); new
`Services/TrayIconService.cs` — dependency-free Shell_NotifyIcon tray with severity-coloured
icons (generated `Assets/Tray/*.ico` from the Io severity palette; grey before any scan, NEVER
green on unmeasured), context menu mirroring the macOS dropdown (status, latched change +
Acknowledge, Watch this network, Open, Quit), wired in MainWindow with UI-thread marshalling.

**C1 router/gateway audit COMPLETE (both platforms):**

- New evidence `gatewayDhcpOffLinkDNS`: DHCP pushing a PRIVATE IPv4 resolver that is neither the
  gateway nor on its /24 (rogue-DHCP/hijacked-router shape) → 0.6 interception_onPath /
  0.4 localCompromise. ISP-pushed PUBLIC resolvers never flag; IPv6 pushes skipped (benign ULA);
  failed detection → inconclusive, never clean; audited-clean gateway is a real negative datapoint.
- Windows twin built from scratch: `GatewayInfo` + `DetectGatewayAsync` (registry `DhcpNameServer`
  read so a static override can never be mislabelled DHCP-pushed) + Portal adapter logic +
  PortalDatapointCatalog 3→4.
- `syxx key` now prints the Gateway/Router line on both platforms (honest "Not probed" when absent).
- **Defect found by the real run and fixed:** macOS `defaultGatewayIP()` NEVER matched — `route -n
  get default` indents its fields and the parser did an untrimmed `hasPrefix("gateway:")`, so every
  box silently reported no gateway (the 07-17 "verified" note did not hold on this machine). Fixed
  via pure `parseGatewayFromRouteOutput` + regression test on real indented output. Live run now
  detects `Gateway: 10.0.0.1` on both the Swift and .NET paths.
- **Mutation matrix run per the user's directive (every fail state DEMONSTRATED):** on BOTH
  platforms — (M1) drop private-address guard → public-DNS test red; (M2) never flag → happy-path
  red; (M3) failed detection reads clean → inconclusive test red; (M4) subnet check broken →
  same-subnet test red; plus the parser trim mutation red. All reverted to green.
- Suites: macOS 535/535 (4 skipped), Windows 487/487.

## GUI parity pass — Windows brought to macOS canonical appearance (2026-07-19)

Follow-through on the screen-recording comparison ("proceed", macOS canonical). All Windows-side;
**none of it is build-verified — SyxxUI cannot compile on this Mac. The VM build + launch is the
bar for every item below.** Kit/test suite unaffected (483/483 green).

1. **Right-edge clipping (3 sites).** Horizontal StackPanels measure children at unbounded width,
   so text clipped instead of wrapping: Prophet input row (`ProphetView.xaml` — Check button pushed
   off-card; now a Grid with a star domain column), Conclusions grade headline
   (`ConclusionsView.xaml` — "not safe to tru…"; now Grid Auto+*), Portal route status line
   (`PortalView.xaml` — same fix).
2. **Sidebar parity (`MainWindow.xaml`).** Selected row is now a filled ioBlue pill with white
   content (NavigationViewItemBackgroundSelected*+ ForegroundSelected* resources; indicator pipe
   hidden) matching macOS SidebarItem. Hamburger toggle hidden (macOS has none). Icon glyphs
   remapped to the macOS SF-symbol equivalents: What's New E735, Dashboard E9E9 (equalizer bars),
   Prophet E721 (magnifier), Mirror E8AB (branch), Glutton E7BA (warning triangle), Cage E72D
   (three connected nodes), Witness EB95 (certificate/seal), License E8D7 (key). "Start your story
   anew" moved BELOW License (macOS order) with the macOS ↺ glyph (E72C, was ✕ E894).
3. **D-1 full-scan card (`ConclusionsView.xaml/.cs`).** "Run a full scan / Scan my network" card,
   copy identical to macOS; wires the Kit's existing `FirstRunScan.RunAsync` (already used by the
   CLI) with live progress, and merges result.Inputs + Conclusion into the session exactly like
   macOS runFirstScan. try/catch guards the async void handler (E044 class).
4. **B-5 Cage GUI capture (`CageView.xaml/.cs`).** "Close the Cages"/"Stop Capture" toggle launches
   the unified `syxx cage --duration N --keep` under a UAC prompt and TAILS its per-line-flushed
   NDJSON stream in %LOCALAPPDATA%\Syxx\captures (same stream-and-tail architecture as macOS's
   osascript path). Honest failure paths: missing binary, declined UAC, quiet network → message,
   never a fabricated row; an elevated child can't be killed from the unelevated GUI and the copy
   says so. Rows map real DNSCaptureRecord fields; severity Info (no per-row verdict is invented).
5. **Typography.** Windows-only italics removed from Prophet/Cage quotes. Note: macOS declares
   `.italic()` on these but Space Grotesk ships no italic face, so macOS renders upright — Windows
   now matches the rendered canonical. If italic is actually wanted, macOS needs a real italic font
   (owner call).
6. **Non-issue closed:** "Free the Witness" exists on both platforms (was below the fold in the
   macOS recording frames).

Copy shipped under the standing direct-output authorization (2026-07-15), all strings cataloged in
outputs-catalog.csv and user-editable. Still open (owner decisions): pricing contradiction E-10
($2/$10 macOS vs $4.99/$29.99 Windows), full macOS-style OverallVerdictView card structure on
Windows (ambiguous-verdict card with subreddit buttons; the raw-dump fix covers the honesty part).

## Cross-platform GUI appearance comparison (screen recordings) + first parity fixes (2026-07-19)

Compared two user screen recordings (Windows SyxxUI ~6:14 AM, macOS Syxx.app ~6:22 AM; same
thisvid.com tour of all six modules) frame-by-frame via ffmpeg extraction. Full findings in the
session report; three Windows-side fixes landed immediately (all mirror macOS, no new copy):

1. **Prophet record rows showed the raw numeric qtype** — "1  88.208.57.223" where macOS shows
   "A  88.208.57.223". Added `DNSRecord.TypeName` to `Windows/Syxx.Kit/DNSResolver.cs` (exact twin
   of macOS `typeName` table) and used it at `ProphetView.xaml.cs:80`. Mutation-proven test added
   (`DNSRecord_TypeName_MapsKnownTypesAndLabelsUnknown`).
2. **Conclusions leaked a raw .NET record dump into the UI** — "Referral { Reason = …, Subreddits =
   System.String[], PrefilledPost = … }" rendered verbatim (`ConclusionsView.xaml.cs:42`). Now
   renders `Referral: <reason> (r/AskNetSec, r/NetSecStudents)` from the record's fields.
3. Verified NOT a bug: Windows "Confidence: 4%" beside Stage-5 "Confidence: 45%" — two different
   engine measures (Conclusion.Confidence vs Stage-5 belief); macOS shows the same 4% in its ring
   gauge. Confusing side-by-side, but consistent across platforms; flagged for copy review, not code.

Tests: Windows suite 483/483 (was 482; +1). SyxxUI xaml.cs edits are a VM build/launch bar.
Remaining divergences (sidebar icons/order/selection style, right-edge clipping on Windows,
Windows-missing full-scan button, macOS/Windows pricing contradiction, Cage GUI capture gap) are
catalogued in the report — several need owner decisions on which side is canonical.

## .NET tree reverted to .NET 10 release (2026-07-18, user decision)

Reverts the Windows/Linux .NET targets from the .NET 11 preview to the latest .NET 10 release,
superseding the 2026-07-15 net11 pin (the shipped app should not require a preview runtime —
ROADMAP A-5's own finding). Changes:

- TFMs: `net11.0` → `net10.0` in `Syxx.Kit.csproj:3`, `Syxx.csproj:4`, `Syxx.Tests.csproj:3`;
  `net11.0-windows10.0.26100.0` → `net10.0-windows10.0.26100.0` in `SyxxUI.csproj:4`
  (TargetPlatformMinVersion `10.0.22000.0` and WindowsAppSDK 2.2.0 unchanged).
- SDK pins: root `global.json` + `Windows/global.json` → `10.0.100`, rollForward latestMinor,
  `allowPrerelease: false`.
- `.github/workflows/ci.yml` comments, `scripts/win-build.ps1` (also fixed its stale
  `net10.0-windows10.0.19041.0` exe path to the 26100 SDK dir), and
  `Ubuntu/UBUNTU-PORT-PLAN.md` TFM citations updated.
- Stale `bin/`/`obj/` net11 artifacts deleted.

**Verified on this Mac (SDK 10.0.302 installed same day):** `Syxx.Linux.slnf` builds Release AND
Debug with 0 errors; `dotnet test` **482/482 pass** on net10.0; linux-arm64 self-contained publish
produces a valid ELF aarch64 `Syxx`; real run: `Syxx.dll --version` → "syxx 2.0.0" and
`nslook example.com` → live A records (172.66.147.243, 104.20.23.154, TTL 300) via 8.8.8.8.

**Fixed during verification:** `Windows/Syxx.Tests/SignedIncidentReportTests.cs:55-57` did not
compile against the current Kit API (`new SynthesisEngine()` + old single-arg `Synthesize` — the
engine is now static with a different signature). Fixed to `Synthesizer.Conclude(new SectionInputs())`,
mirroring the macOS twin (`SignedIncidentReportTests.swift:58`) line for line. Pre-existing breakage
unrelated to the TFM revert; provenance unknowable (this working copy has no `.git`).

**Still outstanding (VM bar):** install the .NET 10 SDK (arm64, release) on the VM, `pwsh ./build.ps1`
Debug+Release, and a real SyxxUI.exe launch — WinUI cannot build or run on this Mac, and the
post-net11 launch fixes have never been exercised on a net10 GUI build.

## dSYM generation + preview-macro build fix (2026-07-18)

Generated debug symbol bundles for crash symbolication (G-4/G2 groundwork). macOS debug and
release built (`swift build`, both configs) plus the app bundle (`bundle-app.sh` debug and
release; `Syxx.app` left wrapping the RELEASE binary). `dsymutil` produced five dSYMs, each
UUID-verified against its binary and smoke-tested with a real `atos` symbolication
(addresses resolve to file:line):

- `macOS/.build/debug/syxx.dSYM` (12M), `macOS/.build/debug/syxx-gui.dSYM` (21M)
- `macOS/.build/release/syxx.dSYM` (12M), `macOS/.build/release/syxx-gui.dSYM` (20M)
- `macOS/Syxx.app.dSYM` (release GUI, UUID `6744C9E0-090F-3857-90C1-5FD6EE6EEB59`)

**Build blocker fixed:** this host has Command Line Tools only (no Xcode), and CLT ships no
`PreviewsMacros` plugin, so the two `#Preview` blocks in
`Sources/SyxxApp/Views/Components/Stage5ReasoningView.swift:391-405` failed the whole
SyxxApp build. The guard changed from `#if DEBUG` to opt-in `#if ENABLE_PREVIEWS`
(add `-DENABLE_PREVIEWS` in Xcode to see previews). No user-visible output changed; no
Windows twin needed (`#Preview` is macOS-only dev tooling).

**Caveat:** dSYMs are snapshots of these exact builds — any rebuild changes the UUIDs and
the dSYMs must be regenerated (`dsymutil <binary> -o <name>.dSYM`).

## GATED — work that cannot proceed without the owner (2026-07-16)

Recorded here so it is not mistaken for un-done engineering. Both are ROADMAP TOP-TEN items (#4, #5)
and both are blocked on artifacts only the owner can supply. **Neither is a code defect.**

**#4 Keygen — a stock deploy denies every real key.** `scripts/deploy.sh` never provisions
`SYXX_KEYGEN_PUBLIC_KEY` and never sets `SYXX_DEV_MODE`. With D040 fail-closed now genuinely
enforced and mutation-proven (`13a3af5`), `/api/auth/activate` returns 402 for **every live key**.
Note the direction of travel: fixing fail-closed made this blocker *more* certain, not less — the
guard is correct, the deployment is unprovisioned. **Needs from the owner:** the Keygen account /
product / policy IDs, an API token, and `SYXX_KEYGEN_PUBLIC_KEY`, wired into `deploy.sh` rather
than pasted into the unit (see #6 — inline `Environment=` secrets leak via `systemctl show`).

**#5 Stripe — a paid buyer can receive nothing.** The license email silently no-ops when SMTP is
unconfigured, so a successful payment can mint a key the buyer never sees. A046 (`expand[]` sent in
a GET body) means the `default_price` fetch fails. The path has never been exercised against a live
Stripe endpoint. **Needs from the owner:** `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`, the
price/product IDs, SMTP host + credentials, and a live end-to-end purchase test.

**#7 DecisiveActCatalog signing — needs a Syxx-controlled build-time key.** Verified today:
`WitnessSigner.SignDecisiveActs` / `VerifyDecisiveActs` both exist and are tested, but **no
production caller invokes either** — `Synthesizer.cs:810` passes
`DecisiveActCatalog.CurrentValidatedActs()`, which returns `new(CurrentVersion, Acts)` with a null
signature, and `SynthesisEngine.cs:119` reads `.Acts` directly to trigger the M4 decisive-safety
gate. So the gate runs on an unverified catalog.

**Why this is gated and not merely undone:** the catalog is a COMPILE-TIME CONSTANT in the same
binary as the verifier. Signing it with the *user's* Witness key would be theatre — the user is not
the catalog authority, Syxx is. Meaningful signing needs a **Syxx-controlled build-time key plus a
pinned public key shipped in the client**, which is an owner artifact of exactly the same kind as
`SYXX_KEYGEN_PUBLIC_KEY`. Inventing that ceremony unilaterally would produce security theatre and a
false sense of provenance.

**Threat-model note (why it is not ranked higher):** an attacker who can alter a compile-time
constant has already replaced the binary. The realistic risk is the *reverse* of a false clean —
acts REMOVED from the catalog would stop a real threat tripping the decisive gate and soften it
toward inconclusive. **Needs from the owner:** a decision on whether the catalog is signed at build
time (and with which key), or whether Ph6.1 is descoped and `SignDecisiveActs`/`VerifyDecisiveActs`
are removed rather than left implying a protection that is not wired.

Also standing: **the repo has 25+ unpushed commits** — push needs interactive credentials (no `gh`
installed, no stored github.com credential), so it cannot be done from a non-interactive session.

## Tier 1 verification — REMEDIATION-MASTERPLAN2.md honesty-critical false verdicts (2026-07-16)

Verified every Tier 1 item from `REMEDIATION-MASTERPLAN2.md` against live code. **All items confirmed implemented** across macOS + Windows + Server:

- B050 — expired RRSIG → `.degraded` not `.positive` ✅
- B051 — encrypted-transport blocked only when Do53 succeeds + DoH fails ✅
- B070 — answer owner-NAME matched to question/CNAME chain ✅
- B073/B071 — no-OPT vs malformed-OPT, strict authority parse ✅
- A013 — atomic `kv_insert_once` compare-and-set for Stripe ✅
- B049/B053/C028/B063 — no hardcoded green; tri-state witness verdict ✅
- Windows cautious-combination parity — SourceCorrelationTests 0 failures ✅

**Fixes landed during verification:**

- A046 — GET `expand[]` in query string not body (`stripe_checkout.py`)
- A075 — field rename `'signed'` → `'dnskeyObserved'` (`resolver.py`)
- A038 — DNSError caught → inconclusive, not crash/500 (`app.py`)

**State:** 4 files changed (+29/-5), macOS 476t/0f, Windows 389t/0f. Tier 1 complete.

## ROADMAP Impl-v2 Phase A — the "so what" product layer: A1 + A2 (Windows) (2026-07-16)

Starts Phase A of `ROADMAP.md` "Implementation plan v2" — turning the existing detection engine's
verdicts into something a user can act on. **Windows-first by design:** an OpenCode session owns the
macOS tree right now, so the twins are owed (see Parity below), not skipped.

**A1 — Verdict→Action layer.** New `Windows/Syxx.Kit/Synthesis/VerdictAction.cs`:
`VerdictActions.For(Conclusion)` → `{Meaning, PrimaryAction, Severity, Qualified}`. Every verdict now
ends in one plain sentence of what it MEANS plus the single most important action. Wired into the CLI
(`Syxx/Program.cs` `ShowConclusions`) and the GUI (`SyxxUI/Views/ConclusionsView` "WHAT THIS MEANS /
DO THIS" card). The detailed remediation list stays in `Conclusion.NextSteps`; A1 is the headline TL;DR.

**A2 — Plain-language explanation layer.** New `Windows/Syxx.Kit/Synthesis/PlainExplanation.cs`:
`PlainExplanations.For(Conclusion)` → `{Summary, Because[], Despite[]}` + a composed `Narrative`
("we believe X because A and B. That said, C."). Built from `EvidenceItem.Supports` (evidence FOR the
verdict), competing signals, section gaps, Dempster conflict, and the runner-up hypothesis — so it
reads only always-present `Conclusion` fields and explains in every data regime (Stage5 may be null).
Wired into the CLI and a GUI "WHY WE THINK SO" card.

**Honesty contract (Protocol §1/§10, DoW #3) — the load-bearing part:**

- An `Inconclusive` verdict, or a `Clean` verdict that rode over a hard-gated section, is surfaced as
  **UNFINISHED, never safe** — `Severity.Unknown`, and the action is "get a real verdict", not reassurance.
- A threat under a referral is marked `Qualified` (confidence caveat shown) but its recommended action
  is **not softened** — a limited-confidence threat is still acted on.
- The A2 "despite" list is never suppressed to make a verdict look cleaner.

**Verification (real runs, this Mac):**

- `dotnet build Syxx.Linux.slnf` — 0 errors.
- `dotnet test` — **340/340 pass**, including 13 new `VerdictActionTests` + 5 new
  `PlainExplanationTests` that lock the honesty gates (incomplete-clean ≠ clean; mutation guard proves
  the gate actually fires).
- **Real CLI run** (`syxx` → C, no checks run): renders `VERDICT: INCONCLUSIVE` → "this is an
  unfinished result, not a clean one" → "…then synthesize again for a real verdict" → "That said, the
  Prophet check did not run, the Mirror check did not run and the Portal check did not run."
- WinUI XAML wiring compiles; a GUI launch is a Windows-VM bar (not run here).

**Copy status:** all A1/A2 user-visible strings are **PROPOSED, pending owner approval** (AGENTS.md:42).

**Parity owed (macOS):** `SyxxKit/Synthesis/VerdictAction.swift` + `PlainExplanation.swift` twins with
identical strings and identical honesty gates, plus the SwiftUI cards. Deferred only because OpenCode
holds that tree; land once it is clear.

## C037/C039 Cage report-write and raw-capture deletion honesty (2026-07-16)

Two honesty violations in the Cage CLI fixed on macOS and Windows:

1. **C037 — Report write failure swallowed** (`DNSCaptureReportGenerator.swift`, `DNSCaptureReportGenerator.cs`):
   `generateReport()` now returns a `ReportOutput` struct containing the report text plus a
   tri-state `ReportWriteResult` (.success / .failure / .unknown). Previously `try?` silently
   swallowed write errors and the CLI always printed "✅ Diagnosis Report Saved" regardless.
   Now the Cage CLI checks the tri-state and:
   - `.success`: prints "Saved" and sets permissions
   - `.failure`: prints the error, attempts a fallback write to a `.fallback` path, reports the
     outcome honestly
   - `.unknown`: verifies file presence before claiming it exists
   Both early-return paths (input unreadable/unopenable) also return `.failure` with the reason.

2. **C039 — Raw capture deletion failure swallowed** (`SyxxCage.swift`, `CageCommand.cs`):
   Previously `try? FileManager.default.removeItem(...)` swallowed the error and printed
   "🧹 Full forensic capture erased" unconditionally — a privacy violation when sensitive raw
   capture remained on disk. Now:
   - `do { try delete } catch { ... }` catches the error and prints the exact retained path
   - Post-deletion verification confirms file absence before printing "erased"
   - On phantom success (delete returned ok but file still exists), prints a warning

Files touched:

- macOS: `DNSCaptureReportGenerator.swift`, `SyxxCage.swift`, `SyxxKitTests.swift`
- Windows: `DNSCaptureReportGenerator.cs`, `CageCommand.cs`, `SyxxKitTests.cs`

Verified: macOS 444/444 pass (0 failures, 4 skipped); Windows 322/322 pass.

## B088 DashboardView trust green-on-threat fix (2026-07-16)

The macOS DashboardView trust score no longer renders green when the verdict is a threat.

- **DashboardView.swift**: The `trust` property now returns the raw confidence for threat verdicts
  instead of computing `100 - confidence`. The `trustStatColor` property now checks the verdict
  before applying color thresholds: clean verdicts use green/amber/red; threat verdicts are always
  red (confidence >= 50) or amber (confidence < 50), never green. A spoofing verdict with confidence
  85 no longer shows "Trust: 85" in green.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## Ph6.2 macOS M4 decisive-act gate wired + Stage-5 inferred findings (2026-07-16)

Two fixes:

1. **Ph6.2 M4 gate** (`Synthesizer.swift`): The macOS `Synthesizer.conclude()` now passes
   `DecisiveActCatalog.currentValidatedActs()` to `SynthesisEngine.synthesize()`. Previously the
   M4 gate was completely inert on macOS — it always received `.empty`, so confirmed decisive
   signals could never independently force a tampered verdict.

2. **Stage-5 inferred findings** (`SynthesisEngine.swift`): `buildStage5` now accepts
   `mechanismFingerprint` and `evidence` parameters. When a fingerprint is present and the data
   regime is not `noData`, an `InferredFinding` is added to the provenance partition's inferred
   channel — parity with Windows `SynthesisEngine.cs:525-542`. The fingerprint is the first
   occupant of the inferred channel: a mechanism class derived from direct evidence, not itself
   observed, with contributing evidence IDs attached for auditability.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## Ph6.2 macOS M4 decisive-act gate wired (2026-07-16)

The macOS `Synthesizer.conclude()` now passes `DecisiveActCatalog.currentValidatedActs()` to
`SynthesisEngine.synthesize()`. Previously the M4 gate was completely inert on macOS — it always
received `.empty`, so confirmed decisive signals (DNSSEC canary stripping, DNS leak, authoritative
divergence, NXDOMAIN hijack, script mismatch) could never independently force a tampered verdict.

The catalog remains unsigned (no production caller invokes `SignDecisiveActs`); the acts themselves
are still authoritative for the M4 gate, but the forensic signature is absent. Signing requires
loading the private key at startup — an architectural change deferred to a follow-up.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## Server IPv6 socket support (2026-07-16)

All hardcoded `socket.AF_INET` socket creation calls in the server now auto-detect IPv4 vs IPv6
based on the server address.

- **resolver.py**: Added `_addr_family(server)` helper that uses `ipaddress.ip_address()` to
  return `AF_INET6` for IPv6 addresses, `AF_INET` otherwise. Fixed all four socket creation
  calls in `query()`, `_query_tcp()`, and `_query_sections()` (UDP + TCP fallback).

- **dns_authoritative.py**: The DNS bind socket now detects the address family from `config.DNS_HOST`
  instead of hardcoding `AF_INET`. Added `import ipaddress`.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## B088 DashboardView trust green-on-threat fix + scorers corroboration fix (2026-07-16)

Two fixes:

1. **B088 DashboardView** (`DashboardView.swift`): The macOS DashboardView trust score no longer
   renders green when the verdict is a threat. The `trust` property now returns the raw confidence
   for threat verdicts instead of computing `100 - confidence`. The `trustStatColor` property now
   checks the verdict before applying color thresholds: clean verdicts use green/amber/red; threat
   verdicts are always red (confidence >= 50) or amber (confidence < 50), never green. Parity with
   Windows `DashboardView.xaml.cs:31-42`.

2. **Scorers corroboration bug** (`Scorers.swift`): `tamperMechanismFingerprint` now counts
   distinct evidence IDs for the ≥2 independent-signal minimum instead of summing signals across
   all classes. A single evidence item echoing into multiple hypothesis classes is still ONE signal.
   Parity with Windows `Scorers.cs:139-140`.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## macOS E021 forensic tri-state for Witness verification (2026-07-16)

The macOS Witness verification now returns a full forensic tri-state instead of a single Bool,
matching the Windows `WitnessSigner.SnapshotVerification` enum.

- **WitnessSigner.swift**: Added `SnapshotVerification` enum with 9 states: `valid`, `malformedInput`,
  `unsupportedFormat`, `expired`, `stale`, `futureDated`, `untrustedSigner`, `displayMismatch`,
  `invalidSignature`. Added `verifySnapshotDetailed(_:maxAge:trustedPublicKeys:now:)` that returns
  the enum. The existing `verifySnapshot` Bool method is retained for backwards compatibility.

- **AppModel.swift**: Expanded `WitnessVerdict` from 4 cases to 11 (matching all forensic states).
  `witnessVerify()` now calls `verifySnapshotDetailed` and maps each state to a distinct verdict.
  Expired honest snapshots are no longer confused with forged ones.

- **WitnessView.swift**: Updated `witnessVerdictDisplay` to render all 11 forensic states with
  distinct icons, colors, and copy. Critical states (futureDated, untrustedSigner, displayMismatch,
  invalidSignature, malformedInput, unsupportedFormat) use red; expired/stale use amber; valid
  uses green.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## macOS parity: EDNS0 malformed, denial authorityMalformed, scorer wiring, B052, RPNOccurrence (2026-07-16)

Six fixes addressing cross-platform gaps identified in the audit:

1. **B073 EDNS0 malformed OPT** (`EDNS0Probe.swift`): macOS probe now reads `ednsResp.ednsMalformed` and returns `.inconclusive` when the OPT section is structurally malformed — parity with Windows `EDNS0Probe.cs:41-44`. Previously a malformed OPT fell through to `.notSupported`, which is a false-negative on middlebox interference.

2. **B071 Denial authorityMalformed** (`DenialOfExistenceProbe.swift`): Added `authorityMalformed` parameter to `probe()` and `classify()`. When the authority section is declared but unparseable, the probe now returns `.inconclusive` instead of `.bareDenial` — parity with Windows `DenialOfExistenceProbe.cs:79-80`. A corrupt authority is NOT evidence of a synthesized bare denial.

3. **Ph5.2 tamperMechanismFingerprint** (`SynthesisEngine.swift`, `Conclusion.swift`): Added `MechanismFingerprint` struct and wired `Scorers.tamperMechanismFingerprint` into the engine. The fingerprint ships on the `Conclusion` — parity with Windows `SynthesisEngine.cs:385-389`.

4. **Ph5.2 resolverProvenanceConfidence** (`Synthesizer.swift`, `SynthesisEngine.swift`, `Conclusion.swift`, `Scorers.swift`): Added `computeResolverProvenance(_:)` method and wired it through to `Conclusion.resolverProvenanceConfidence`. Also fixed the scorer's `hasEDNS0` and `transportIsEncrypted` parameters to accept `Bool?` (Ph5.2 contract fix) — parity with Windows `Synthesizer.cs:846-847` and `Scorers.cs:59-93`.

5. **B052 MirrorView DEGRADED green** (`MirrorView.swift:272-276`): DEGRADED verdicts now render as `.ioWarningAmber` instead of `.ioSuccessGreen`. The `verdictColor` function now has an explicit `isDegraded` case before the `return .ioSuccessGreen` fallthrough.

6. **RPNOccurrence.swift** (new file): Created `RPNOccurrence.swift` with expert-elicited FMEA Occurrence defaults and v2 SOD scoring — parity with Windows `RPNOccurrence.cs`.

Verified: `swift build` clean, `swift test` 444/444 pass (0 failures, 4 skipped).

## Witness verify honesty fix — macOS contradictory verdict rows (2026-07-16)

Fixes the user-reported (screenshot, 3×) honesty violation in the macOS `WitnessView` validation
report: on a failed/forged/no-snapshot verify it rendered three mutually exclusive rows at once —
"Not yet signed" / "Testimony invalid — signature mismatch" / hardcoded green "Signature valid with
no evidence of tampering." Deltas D069 and B049 (consolidated with C028/B050-note) documented it.

- `AppModel.swift`: new `WitnessVerdict { notVerified, noKey, valid, invalid }` + `@Published
  witnessVerdict`. `witnessVerify()` now branches honestly — no snapshot → `.notVerified` (NOT a
  "signature mismatch"), no key → `.noKey`, else the Ed25519 verify runs → `.valid`/`.invalid`.
  `resetWitness`/`witnessTestify` clear the verdict.
- `WitnessView.swift`: the unconditional green "Signature valid…" block (old :178-188) is DELETED.
  The report renders ONE verdict row from `witnessVerdict`. The no-snapshot case shows "No signed
  snapshot available for comparison — press Witness Testimony first" and suppresses the
  domain/timestamp/signature rows.
- Windows (`WitnessView.xaml.cs`) already rendered a single gated verdict — no change needed.
- Verified: `swift build` clean, `swift test` 444/444 pass (0 failures).
- **Copy pending approval** (AGENTS.md:42): the four verdict strings are my wording, close to but
  not identical to D069 Fix 3. **Still owed:** full E021 forensic tri-state on macOS
  (expired/untrusted/forged rendered distinctly) — owed-macOS-twins item 11; this fix removes the
  contradiction at the Bool level, not the forensic granularity.

## GUI branding/mockup-parity pass (2026-07-15, in-VM session, latest)

Driven by the design references in C:\Users\Syxx\GUI (mockups + macOS screenshots). Real fonts
landed: Space Grotesk + Space Mono TTFs (OFL, from google/fonts) now ship in `SyxxUI/Assets/Fonts`
with their license files; the fallback chains remain beneath them.

- **Sidebar branding:** real hexagon logo (`Assets/syxx-icon.png`, from the approved mockup
  assets) replaces the plain purple square; PRO chips are now gold-outlined with a lock glyph
  (macOS parity) on Mirror AND Glutton; "Conclusion" renamed "Conclusions"; new "Dimensions ↗"
  nav item opens syxx.app externally without navigating the pane (MainWindow.xaml + .cs).
- **PLACEHOLDER pricing (user has NOT finalized):** Dashboard cards now read Pro $4.99/month and
  Pro Yearly $29.99/year (replacing the contradictory $2/mo + $39-one-time; macOS/mockup said
  $10 permanent — user ruled all of these stale). LicenseView Pro card matches. Marked
  PLACEHOLDER in XAML comments.
- **In-app license activation (macOS parity):** LicenseView's Activate button now validates the
  key via `ValidateLicenseDetailedAsync` in-app instead of opening the website; distinct
  invalid / network-failure / verified-but-unsaved outcomes (D042/D043 semantics), status line
  set only from real results.
- **System Panels parity:** About dialog (logo, "Lyin' Eyes", version from SyxxConfig, MilkandGin,
  links) and the full v1.0 EULA dialog, both verbatim from the approved System Panels mockup,
  reachable from LicenseView.
- **Scoped for a dedicated pass:** Cage in-GUI live capture (macOS reference captured:
  CAPTURING chip, events counter, terminal log pane, per-event cards with direction/status and
  INFO verdict chips; needs Windows elevation design for raw sockets).

Verified: Release arm64 rebuild 0 errors, window up ~1s with logo/chips/pricing/dialogs live.

## GUI honesty surfaces + typography root cause (2026-07-15, in-VM session, later)

User authorized writing output copy directly (will edit via an HTML change log afterward), so the
previously-proposed strings shipped. All strings below are USER-EDITABLE PENDING that review.

- **B097** — `ProphetView.xaml.cs`: encrypted-DNS renders tri-state; Unknown reads "Could not be
  determined … treat as unverified, not as safe", never collapsed into "Unencrypted".
- **E021 surface** — `AppViewModel.WitnessVerification` (new) carries `SnapshotVerification`;
  `WitnessView` renders DISTINCT failure states: expired, stale, future-dated, untrusted signer,
  display mismatch, invalid signature, unsupported format — an expired honest record no longer
  reads the same as a forged one. (The B049/B053/C028/B063 honesty-gated verdict structure was
  already present from the 07-14 session; this refines its failure taxonomy.)
- **D044** — `GluttonView`: the stale "this GUI cannot confirm a leak on its own" card replaced
  with a COLLECTOR VERDICT card rendered from the real `LeakTestResult`, gated on the attestation
  (D004/D011): unsigned/invalid/untrusted-key results read "[UNVERIFIED] Result cannot be
  trusted", Confirmed reads red with the observed resolver list, NotDetected reads green only
  with a valid signature, Inconclusive surfaces the Kit's reason.
- **B088** — `DashboardView`: "TRUST SCORE" relabeled "CONFIDENCE"; the number is colored by the
  VERDICT (green clean / amber inconclusive / red threat) so a 90%-confident tampered verdict
  can never render as a reassuring plain number; ALERTS populated from the real
  `Conclusion.Advisories` count (was a hardcoded em-dash placeholder).
- **D042/D043** — `Syxx/Program.cs` license flow: uses `ValidateLicenseDetailedAsync`;
  network/timeout reads "[NETWORK] … your key was not checked" (distinct from "[FAILED] invalid,
  expired, or locked"); a DPAPI/save failure after a VALID key reads "[WARNING] … unlocked for
  this session, but the key could not be saved", never an unqualified success. Raw emoji replaced
  with bracket format per F023 policy.
- **LeakDetector** reason strings for the B091 inconclusive paths and the C031 baseline
  suppression are now populated (were null pending copy approval).
- **ROOT CAUSE of "the GUI looks terrible": `Windows/SyxxUI/Assets` is EMPTY** — the
  IoDesignSystem font tokens pointed at ms-appx TTFs that do not exist, so every view silently
  fell back to the default UI font. Tokens now carry explicit fallback chains (Segoe UI
  Variable / Cascadia Mono, in-box on Win11); if the real Space Grotesk/Space Mono TTFs are
  added under Assets/Fonts they take precedence automatically. The three 07-14 views
  (Cage/Witness/License) were audited against the design system and are structurally sound —
  the typography collapse was the whole-app problem.

Verified: GUI Release arm64 rebuild + 321/321 tests + real launch (window up ~1s).

## MASTERPLAN2 Windows-side implementation (2026-07-15, in-VM session)

All REMEDIATION-MASTERPLAN2.md items whose Windows code exists in this VM are implemented, tested
(321/321, every fix with mutation-proven failing cases), built Debug+Release including the arm64
WinUI GUI, and real-run verified where the VM allows (live `syxx nslook` UDP:53 runs below). macOS
(`macOS/`), Server (`Server/`), and `scripts/` are NOT in this VM — every twin is itemized under
"Owed macOS/server twins" so the Mac-side agent has an exact worklist. GUI-copy items are NOT
implemented; exact wording is proposed to the User for approval first (AGENTS.md output rule).

**Tier 2 — evidence acquisition:**

- **C013** — `IPClassifier.cs:86-97`: IPv4-mapped IPv6 (`::ffff:0:0/96`) reclassifies the embedded
  IPv4; a mapped sinkhole (`::ffff:127.0.0.1`) now reads blocking, mapped public stays public.
- **C016** — `DNSResolver.cs:456-460` (`ResponseQuestionMatches`): expected name is punycoded via
  `ToAsciiName` before comparing to the wire echo. Real run: `syxx nslook münchen.de` no longer
  fails as spoofed (returns the same no-A-at-apex result as `Resolve-DnsName`).
- **C017** — `DNSResolver.cs:743-753` (`FormatRData`): name-typed RDATA (NS/CNAME/PTR/DNAME)
  decodes against the full packet instead of hex-dumping packet-local bytes;
  `CNAMEChainProbe.ReconstructChain` now keys on real names. Real run: `syxx nslook
  www.github.com` renders `Type: 5 Data: github.com` from a live compressed packet.
- **C018** — `SVCBProbe.cs`: `CanonicalizeSvcbRdata` parses priority/target/SvcParams (RFC 9460;
  compression in RDATA → null, never garbage); `Classify` flags DIVERGENT when vantages serve the
  record with different parameter sets (ECH/ALPN stripping) — presence-only was blind to this.
- **B073** — `EDNS0Probe.cs:38-44`: `EdnsMalformed` (parser tri-state already present) now
  consumed — malformed OPT/additional reads Inconclusive, never "legacy resolver / not supported".
- **B071** — `DenialOfExistenceProbe.cs` (`Classify(+authorityMalformed)`): a declared-but-
  unparseable authority section is Inconclusive, never the BareDenial positive.
- **Ph3.2** — `Synthesizer.cs`: `SectionEvidence.Completeness` is now a real N/M over each
  section's datapoint catalog (Prophet 12, Mirror 6, Portal 3, Glutton 2, Cage/Witness
  data-driven; `DatapointCompleteness`). The engine's count-based clean-support scaling
  (`SynthesisEngine.cs:225`) is live for the first time — it always saw 1.0 before. Untested
  sections read 0.0. Acceptance bar met: N-clean+1-timeout < all-clean (test).

**Tier 3 — engine phases:**

- **Ph5.2 (B005)** — both dead sub-scorers ship-gated into output: `Conclusion.MechanismFingerprint`
  (from `TamperMechanismFingerprint`) and `Conclusion.ResolverProvenance` (computed in
  `Synthesizer.ComputeResolverProvenance` from EDNS0/stub-entropy/transport probes). Contract
  fixes found by the new failing tests: the scorer's non-nullable bool params made the no-data
  regime UNREACHABLE (now nullable, absent probes drop out of the denominator), and the
  minimum-corroboration rule counted hypothesis-class echoes so ONE evidence item could
  fingerprint (now ≥2 distinct evidence ids — `Scorers.cs`).
- **Ph6.1 (B098)** — `ValidatedDecisiveActs` gains Signature/PublicKey + domain-separated
  `CanonicalBytes` ("syxx.decisive-acts.v1:"); `WitnessSigner.SignDecisiveActs`/`VerifyDecisiveActs`
  (unsigned never verifies; tampered membership/version fails; trust-set enforced).
- **Ph6.2 (B062/B064)** — `Synthesizer.Conclude` now passes `DecisiveActCatalog.CurrentValidatedActs()`
  into `Synthesize` (the M4 gate was inert in the shipped app: no caller supplied it). Removed the
  stale Phase-2 discard at `SynthesisEngine.cs:91`. Verified: a confirmed NXDOMAIN-hijack canary
  forces a tampered verdict with referral overridden, through the production Conclude path.
- **Ph7.1** — `WeightRubric.GetWeight` is a LOADER (AsyncLocal overrides over the expert-elicited
  `DefaultWeight` table); `CalibrationHarness.RunWithWeights` actually applies a table (was a
  macOS no-op parity break, absent on Windows); `ParseWeightTable` reads a JSON weight file and
  throws on typos. Bar met: a zeroed table deterministically changes engine output (test).
- **Ph7.2** — new `Synthesis/RPNOccurrence.cs`: expert-elicited FMEA Occurrence defaults per
  mechanism class (rationale documented per value), SOD v2 priority model
  (`syxx.stage5.sod`/v2, score = S×O×D), and the Stage-5 validator now enforces the v2 contract
  (occurrence required, 1..10, score formula). Emitting v2 findings awaits the CX-3 findings
  builder; the model + validation are ready on both ends.
- **S5.1** — inferred provenance channel POPULATED: the mechanism fingerprint ships as an
  `InferredFinding` (never in noData; contributing evidence ids attached). Datapoint-level
  completeness: `SectionStatusEntry`/`SectionEvidence` carry `DatapointsObserved/Expected`,
  and the engine emits `profile.datapoints.v1` behind `Config.EmitDatapointCompletenessProfile`
  — DEFAULT OFF until macOS emits the identical profile (Stage-5 canonical bytes are a
  cross-platform contract; same verifier-first pattern as E020 below). critical* stays
  section-level deliberately (§13.9 semantics; datapoint-granular critical counts would
  invalidate every honest clean run that skipped one optional probe).

**Tier 4 — trust infrastructure:**

- **B085** — `WitnessSigner.cs`: future-dated snapshots rejected (5-min skew tolerance,
  `FutureSkewToleranceMillis`); within-skew accepted (over-rejection failure case tested).
- **E021** — `WitnessSigner.VerifySnapshotDetailed` → `SnapshotVerification` enum (Valid /
  MalformedInput / UnsupportedFormat / Expired / Stale / FutureDated / UntrustedSigner /
  DisplayMismatch / InvalidSignature). Bool `VerifySnapshot` is now a wrapper (true only for
  Valid) so existing callers keep exact semantics; surfaces can stop collapsing the states.
- **E020** — verifier-first migration: `SignedEvidencePayload.Build(formatVersion)` v3 omits
  free-text `errorMessage` from the signed canonical bytes (platform-divergent exception text
  broke byte-parity and put attacker-influenceable text in evidence); the Windows verifier
  accepts v2 AND v3; the SIGNER stays on v2 until the macOS verifier twin lands. Do NOT flip
  signers until both platforms verify v3.
- **B091** — `LeakDetector.DeriveVerdict`: verdict-bearing fields come exclusively from the
  parsed SIGNED payload (strict DTO `SignedLeakAttestation`; missing fields → null → Inconclusive).
  Outer/signed mismatch → Inconclusive with the SIGNED resolver set displayed; invalid signature
  → Inconclusive; unsigned legacy path unchanged (D004 display gating applies).
- **C031** — `ReconcileVerdict(+expectedResolvers)`: observations fully inside the caller's
  expected-resolver baseline are not a leak; ANY outside observation still Confirmed; null/empty
  baseline preserves original semantics. `RunCollectorTestAsync` threads the parameter.
- **WS3-A.3 / D011** — `CollectorPinning.ConfiguredTrustedSigningKeys()` reads
  `SYXX_COLLECTOR_TRUSTED_KEYS` (same owner-deliverable pattern as `SYXX_COLLECTOR_PINS`) and
  `RunCollectorTestAsync` auto-loads it when callers pass nothing — signer trust now engages in
  the shipped GUI/CLI without per-call-site wiring.

**Tier 6 — client safety (non-copy items):**

- **F028** — `CageCommand.cs:25-36`: `--duration` accepts only finite, positive values ≤ 86400s;
  "NaN"/"Infinity" no longer crash `AddSeconds` (invalid keeps the default, same as unparseable).
- **E044** — `LicenseManager.cs`: `SyncTrialAnchorWithServerAsync` is truly async
  (`GetFirstLaunchDateAsync`; `IsFeatureUnlockedAsync` awaits it). The old
  `GetAwaiter().GetResult()` ran up to 1.5s of network synchronously ON the WinUI UI thread on
  every entitlement check. Sync `GetFirstLaunchDate` retained only for the [Obsolete] path
  (zero production callers).

**Verification:** `build.ps1` green (all 4 projects, Debug+Release, arm64 GUI); 321/321 tests
(was 277 at session start; +44, every new test carries explicit failure cases); real runs:
live `syxx nslook example.com` (2 A records via 8.8.8.8), `www.github.com` (decoded CNAME chain),
`münchen.de`/punycode cross-checked against `Resolve-DnsName`.

**Owed macOS/server twins (not buildable in this VM):**

1. C013 mapped-IPv4 branch → `IPClassifier.swift`.
2. C016 `ToAsciiName` compare → `DNSResolver.swift:333,413-424`.
3. C017 name-typed RDATA decode → `DNSResolver.swift` formatRData + `CNAMEChainProbe.swift:67-79`.
4. C018 SVCB canonicalizer + param-set compare → `SVCBProbe.swift:46-61`.
5. B073/B071 consumers → EDNS0/denial probes (Swift parser tri-state state unknown — audit first).
6. Ph3.2 datapoint completeness N/M → `Synthesizer.swift` (same catalogs: 12/6/3/2/data-driven).
7. Ph5.2 nullable-signal `resolverProvenanceConfidence` + distinct-evidence-id minimum
   corroboration in `Scorers.swift` (the same two contract bugs almost certainly exist there);
   fingerprint + provenance fields on the Swift Conclusion.
8. Ph6.1/6.2 → sign/verify decisive acts (`CanonicalBytes` must byte-match
   "syxx.decisive-acts.v1:{version}:{sorted-acts}"); pass catalog in Swift `conclude`.
9. Ph7.1 → make Swift `runWithWeights` real; loader over defaults; same weight-file format.
10. Ph7.2/S5.1 → `RPNOccurrence.swift` (same values), SOD-v2 validator rule, inferred channel,
    datapoint profile behind the same flag — THEN flip `EmitDatapointCompletenessProfile` and the
    E020 signer format to v3 on BOTH platforms in one change.
11. B085/E020/E021/B091/C031/WS3-A.3/E044 → `WitnessSigner.swift`, `LeakDetector.swift`,
    `LicenseManager.swift` twins.
12. Server-side Tiers 4/5 (A-deltas, payments, SSRF, DoS, info-leak) — untouched here, no server
    code in this tree.

## Windows .NET 11 build stood up in-VM; SyxxUI launch crash root-caused and fixed (2026-07-15)

First session run by Claude Code inside the Windows ARM64 VM (no file transfer, direct build). The
Windows tree now builds fully on .NET 11 preview (`11.0.100-preview.6.26359.118`), Debug AND Release,
all 4 projects, and the GUI launches with a visible window. No fallback to .NET 10 was needed.

**Build fixes:**

- `build.ps1:27-30` — `Invoke-Build` declared its splat parameter as `$args`, a reserved PowerShell
  automatic variable, so `dotnet build @args` received no arguments (MSB1003 on every run). Renamed to
  `$buildArgs`. Also corrected the echoed GUI exe path (was the stale
  `net10.0-windows10.0.19041.0` layout) to `bin\<plat>\Release\net11.0-windows10.0.26100.0`.
- `Windows/SyxxUI/SyxxUI.csproj:4-5` — TFM was bare `net11.0`, invalid for WinUI; restored the
  platform rule from MEMORY.md: `net11.0-windows10.0.26100.0`, min `10.0.22000.0`.
- `Windows/SyxxUI/Program.cs:30` — CS0029: the `Application.Start` lambda parameter was named `_`, so
  the body's `_ = new App()` assigned the App to the `ApplicationInitializationCallbackParams`
  parameter instead of discarding. Renamed the parameter; this also cleared the follow-on
  XamlCompiler WMC9999 (MarkupCompilePass2 had no LocalAssembly because the C# pass failed).
- `Windows/global.json` + new root `global.json` — added `rollForward: latestMinor` +
  `allowPrerelease`. The root copy exists because the SDK resolver walks UP from the working
  directory: `Windows/global.json` never applied to `build.ps1` runs from the repo root.

**GUI won't-launch defect (fixed, real-run verified):**

- Symptom: exe built but no window ever appeared; user double-click crashed with stowed exception
  `0xc000027b` in `Microsoft.UI.Xaml.dll`, WER inner code `0x80073D54 APPMODEL_ERROR_NO_PACKAGE`
  in `combase.dll`. The old bootstrap theory was NOT the cause: the faulting module path proved
  WindowsAppRuntime 2.2.0 loaded fine (and 2.2.0 arm64 IS installed on this VM; the MEMORY.md
  "only 1.5 installed" note was stale).
- Root cause: `Windows/SyxxUI/Services/DimensionService.cs:49` (old rev) backed ALL settings on
  `ApplicationData.Current.LocalSettings`, which requires package identity and always throws in
  this deliberately unpackaged app (`WindowsPackageType=None`). The throw happened synchronously
  inside `MainWindow`'s constructor (via `CheckStartupAsync` → `FetchStartupAsync` →
  `LoadCachedResponse`) before `Activate()`, so the window never showed.
- Fix: `DimensionService.cs:49-100` — file-backed JSON settings store at
  `%LOCALAPPDATA%\Syxx\dimension-settings.json` (same directory convention as DNSCapture reports
  and WitnessKeyStore), lock-guarded, corrupt-file tolerant, best-effort writes. No user-visible
  strings added or changed.

**Verification (real runs, not just tests):**

- Full `build.ps1`: GUI + Kit + CLI + Tests, Debug and Release, 0 errors (arm64 GUI).
- `run-tests.ps1`: 277/277 pass. Checked the previously failing
  `StrongCorrelatedGroup_IsCappedAtMaxWeightPerSource` was not softened — strict assertion intact
  at `Windows/Syxx.Tests/SyxxKitTests.cs:2503`; it passes because of the 2026-07-14 rounding
  parity fix (`Scorers.cs:48`).
- Real GUI run: `SyxxUI.exe` (Release arm64) shows its main window within 2 seconds
  (verified via process MainWindowHandle poll; previously ALIVE-but-windowless for 30s+).

## Ubuntu port Tier 1 WS-B1: single-source-of-truth Stage-0 golden corpus (2026-07-15)

Lifts the two hand-encoded Stage-0 seed lists into one JSON both harnesses read, so a third
and fourth runtime (Linux x64/arm64) cannot fork a fifth copy. Anchor: `Ubuntu/UBUNTU-PORT-PLAN.md`
Section 2, WS-B1; schema `STAGE0-GOLDEN-VECTOR-CONTRACT.md:9-38`. No user-visible product strings
changed (JSON `_note` and loader error text are test-internal diagnostics); no verdict/honesty
synthesis path touched.

**Parity finding:** the two hand lists (`Windows/Syxx.Tests/Stage0GoldenCorpusTests.cs` old `:60-150`
and `macOS/Tests/SyxxKitTests/Stage0GoldenCorpusTests.swift` old `:55-125`) were verified IDENTICAL
across all 8 vectors — same evidence, statuses, phases, verdicts, referrals; both left
`knownFalseSecureUntilGate` at its default false. No divergence to adjudicate.

**Done:**

- **New single source `corpus/stage0/seeds.json`.** 8 vectors (NE1-d1, NE1-d2, NE7-d1, NE2-d5,
  P12, P17, P15, benign-all-clean). Enum strings use the Swift rawValue form (sections/statuses
  lowercase, `evidenceId` camelCase, `expectedVerdict` a `Hypothesis` rawValue); `sectionStatuses`
  is the full resolved six-section map (equivalent to the old `with(allNeg, overrides)`).
- **C# loader** (`Windows/Syxx.Tests/Stage0GoldenCorpusTests.cs:49-140`): reads the JSON via a
  `[CallerFilePath]`-anchored repo-root path; `ParseEnum` normalizes (lowercase, strip underscores)
  onto the PascalCase enums and THROWS on any unknown value; `Seeds()` throws on missing/empty/malformed
  corpus. Removed the now-dead `S`/`With` helpers. All `Seed`/`SeedEvidence` shapes and the three
  assertions are unchanged.
- **Swift loader** (`macOS/Tests/SyxxKitTests/Stage0GoldenCorpusTests.swift:55-160`): reads the same
  JSON via a `#filePath`-anchored repo-root path; decodes enum rawValues directly and throws on
  unknown/missing; the three tests are now `throws`. Corpus shapes and assertions unchanged.
- **Honesty guard (both sides):** a missing/empty corpus is a LOUD failure on every test, never a
  vacuous green — an empty seed list would make never-false-secure pass with zero vectors.

**Verification (real runs, this Mac):**

- Baseline (before): `swift test --filter Stage0GoldenCorpusTests` 3/3 pass; `dotnet test` 3/3 pass.
- After JSON wiring: both still 3/3 pass reading the JSON.
- Acceptance bar (parity move): edited `NE7-d1` `expectedVerdict` `inconclusive`->`spoofing` in the
  JSON; BOTH harnesses failed identically — same seed, engine actual `Inconclusive` vs edited expected
  `Spoofing` (Swift `.swift:191`; C# `.cs:190`). Reverted; both green again.
- Honesty guard: moved `seeds.json` aside; Swift failed all 3 with `CorpusError.notFound`, C# failed
  all 3 with `FileNotFoundException` ("WS-B1 single source of truth is missing"). Restored.

## Ubuntu port Tier 1 WS-A: Linux build system + four-runtime CI matrix skeleton (2026-07-15)

Foundation for the Ubuntu (Avalonia) port. Makes the shared `Syxx.Kit` reasoning core
build/publish on Linux without the WinUI GUI, and stands up the parity-CI harness.
Anchor: `Ubuntu/UBUNTU-PORT-PLAN.md` Section 2, WS-A1/WS-A2/WS-A3. No user-visible
product strings changed; no verdict/honesty path touched (this is build/CI infra only).

**Done:**

- **WS-A1 — Linux solution filter.** New `Windows/Syxx.Linux.slnf` includes `Syxx.Kit`,
  `Syxx` (unified CLI), and `Syxx.Tests`, and EXCLUDES `SyxxUI` (which is
  `net11.0-windows10.0.26100.0` + `UseWinUI` and cannot restore off Windows). Linux CI
  builds the filter, never the full `Windows/Syxx.sln`.
- **WS-A2 — Linux RIDs on the CLI.** `Windows/Syxx/Syxx.csproj:9` now declares
  `<RuntimeIdentifiers>win-x64;linux-x64;linux-arm64</RuntimeIdentifiers>`. Kept untrimmed
  on purpose (NSec + DNS reflection are trim-hostile; a trimming drop is the "green build
  that lies" class).
- **WS-A3 — four-runtime CI matrix.** New `.github/workflows/ci.yml` (the dir was empty):
  `build-test` matrix over `macos-arm64` (swift test → SyxxKit twin), `windows-x64`,
  `ubuntu-x64`, `ubuntu-arm64` (matching the user's arm64 ISO) each `dotnet test`
  `Syxx.Tests`; the Linux legs build via `Syxx.Linux.slnf` and self-contained-publish the
  CLI per RID. A `diff` job wires the transcript-parity gate as a VISIBLE placeholder (a
  `::warning::` annotation, never a silent green) whose assertions come online in WS-B2.

**Deferred/flagged (reported to syxx-neural-planner):** WS-A3's
`<WarningsAsErrors>CA1416</WarningsAsErrors>` is NOT enabled yet. `Syxx.Kit` still has
un-gated Windows-only APIs (LicenseManager DPAPI `LicenseManager.cs:211,220,283,328,348,362,399,401,501,511`;
`NetworkKeyState.HasConfiguredDoHTemplate` at `:329`; `CageCommand` SIO_RCVALL at `:226`;
`WindowsPrincipal`/`WindowsIdentity` at `Program.cs:713-715`) that Tier 2 (WS-C/WS-D) gates.
Turning CA1416 into an error before Tier 2 lands would break the Linux build and this matrix,
contradicting WS-A1's own bar ("succeeds with only CA1416 warnings, no errors"). The switch is
deferred to the Tier-2-completion commit.

**Verification (real runs, this Mac, `/usr/local/share/dotnet` = 11.0.100-preview.6):**
`dotnet build Windows/Syxx.Linux.slnf -c Release` → 0 errors (CA1416/CS8619 warnings only).
`dotnet test Windows/Syxx.Tests/Syxx.Tests.csproj -c Release` → 278 passed / 0 failed on
osx-arm64 (non-Windows managed-IL proxy for the Linux legs). `dotnet publish
Windows/Syxx/Syxx.csproj -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true`
→ produced `Syxx` (ELF aarch64 PIE, ~83 MB) with the correct per-RID `libsodium.so` (ELF
aarch64). The "binary runs inside the arm64 ISO" sub-bar (WS-A2) remains the [LIKELY] boundary
pending a real run on the ISO. The CI matrix itself cannot be exercised without GitHub runners;
YAML validated locally.

## Engine honesty status-maps (B050/B051) + Windows correlation-combine parity (2026-07-14)

Three landed fixes across the reasoning core, byte-parity on macOS Swift + Windows C#, each
mutation-proven and verified by targeted (`--filter`) test runs on both platforms.

**Done:**

- **B050 — expired/not-yet-valid TARGET RRSIG → `.degraded`, not `.positive`.** A benign operator
  DNSSEC lapse (or replay) is now a run-quality caveat that lowers confidence and blocks an unearned
  clean, but cannot NAME a threat alone. The B047 control-canary escalation is preserved: a known-
  signed CONTROL canary returning stripped stays `.positive` (section combine keeps positive over the
  degraded caveat). `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:101-113`;
  `Windows/Syxx.Kit/Synthesis/Synthesizer.cs:109-119`.
- **B051 — encrypted-transport `.blocked` requires a cleartext Do53 control.** A failed DoH path is
  `blocked` ONLY when a cleartext Do53 control succeeds (network provably up → encrypted path filtered);
  when the control also fails (offline host / provider down) it stays `inconclusive`, never a
  manufactured block. `probe()` runs the control only when the DoH path did not clearly succeed.
  `macOS/Sources/SyxxKit/EncryptedTransportProbe.swift`; `Windows/Syxx.Kit/EncryptedTransportProbe.cs`.
- **Windows correlation-combine parity (SourceCorrelationTests.StrongCorrelatedGroup).** Root cause was
  NOT in Dempster/BelOfSet (those are byte-identical) but in `Scorers.EvidenceConfidence`: macOS rounds
  the partial-completeness score away-from-zero (`Double.rounded()`), Windows used `Math.Round`'s
  default banker's/ToEven. At `99/6 = 16.5` macOS → 17, Windows → 16, so two independent supports (99)
  tied the correlated group (95→16) on Windows only. Fixed Windows to `MidpointRounding.AwayFromZero`,
  matching macOS and the `SynthesisEngine.RoundPercent` primitive. `Windows/Syxx.Kit/Synthesis/Scorers.cs:48`.

**Verification (targeted only):** macOS `swift build` + `swift test --filter SourceCorrelation` (5/5),
`--filter EncryptedTransportProbeTests` (7/7), B050 + B047-preservation tests pass; `syxx` CLI product
builds. Windows `dotnet build Syxx.Kit` (0 errors) + `dotnet test --filter SourceCorrelation` (6/6, was
5/6) and B050/B051 (8/8). Windows verification built against committed `DNSResolver.cs` because that file
has unrelated, non-compiling working-tree WIP from another lane (a half-applied `(EDNSResponseInfo?,bool)`
tuple refactor at :510/:666) that blocks a full Kit build; the WIP file was restored untouched afterward.

## Unified binary (UNIFIED-BINARY-PLAN.md) — Windows Phase 2 (2026-07-14)

Windows parity for the unified binary: the 11 standalone Windows CLI projects are consolidated into the
single `Syxx` console binary with subcommands, mirroring the committed macOS Phase 1. Built and verified
with `dotnet 10.0.301` on the macOS host (WinUI `SyxxUI` cannot build here — that stays a Windows-host
bar). Implemented by a `syxx-engineer` subagent, independently re-verified.

**Done:**

- **Shared runner:** new `Syxx.Kit/TracerouteRunner.cs` (`tracert -h <hops> -w <ms> -d <target>`, streams
  hops, empty = honest failure). Replaces the inline `tracert` in the guided Portal and the route command.
- **Commands:** 11 command classes in `Syxx/Commands/*.cs` (console project, so no Console I/O leaks into
  Syxx.Kit). Bodies moved verbatim from the standalone tools. C# `Main(args)` excludes the program name,
  so `args[0]` is already the first real argument — the dispatcher passes `args[1..]` and there is NO
  macOS-style off-by-one. Verified: `syxx nslook google.com` queries google.com (not "nslook"),
  `syxx nslook` prints usage, `syxx route -evil` rejects the target.
- **Dispatch:** `Syxx/Program.cs` `Main` switches on `args[0]` to each command; no-args/`guided` → the
  unchanged interactive menu; `--version`/`--help`/unknown handled (unknown → exit 1). Guided cage now
  re-execs the unified binary (`Environment.ProcessPath cage --duration <n> --keep`, UAC via `runas`)
  instead of a deleted `SyxxCage.exe`; dead `LocateSiblingTool` removed.
- **Solution:** `Syxx.sln` reduced to `Syxx.Kit` + `Syxx` + `Syxx.Tests`; the 11 standalone project
  directories deleted.
- **Honesty:** all usage/help/`--version` labels and touched GUI help strings read `syxx <cmd>`, never the
  now-dead `syxx-<cmd>`. `syxx.app` untouched.

**Verification:** `dotnet build Syxx.sln` 0 errors/0 warnings; console + Kit 0 errors. Real runs:
`nslook google.com` (live A records), `nslook google.com MX @8.8.8.8`, `nslook ... ZZZ` (honest
"Unsupported record type"), `key` (real network state), `--version`/`--help`, `witness --gen-keypair`
(real keypair), `route 8.8.8.8` (honest empty — `tracert` absent on macOS; real hops are a Windows bar).

**Pre-existing failure flagged, NOT touched (honesty rule #5):** `Syxx.Tests` is 256/257; the one failure,
`SourceCorrelationTests.StrongCorrelatedGroup_IsCappedAtMaxWeightPerSource` (`SyxxKitTests.cs:2261`),
asserts the `maxWeightPerSource` cap that WS1-Ph3.1 removed in engine code. It is an **engine-lane**
parity gap (macOS's equivalent already passes at 420/0), independent of this CLI consolidation, left for
the engine lane to reconcile. Not softened, disabled, or deleted.

**Left for a Windows host:** WinUI `SyxxUI` build/render (3 XAML help-string edits + PortalView.xaml.cs
`// TODO parity` note for folding its `tracert` into the shared runner); `warp` runtime (needs
`crypt32.dll`) and full `cage` runtime (Administrator + raw sockets). The Windows GUI has cage *UI state*
only — no cage subprocess launch exists to re-point, so Step 6 was a no-op there.

**Also flagged (pre-existing, not in scope):** `syxx mirror google.com` throws inside
`VantageComparer` ("An item with the same key has already been added. Key: Cloudflare") — a duplicate-key
build of the public-resolver map, present in the old standalone `SyxxMirror` too, so it predates
consolidation. Caught and reported honestly, but the Mirror verdict never renders. Worth a follow-up delta.

**Docs (both platforms):** `DESIGN.md` gained a "Packaging — unified CLI binary" note and every
`syxx-<cmd>` reference in it now reads `syxx <cmd>`. `outputs-catalog.csv` gained rows for the new
dispatch strings (`--help` listing, `--version`, unknown-command) and the usage/GUI string corrections.
This completes UNIFIED-BINARY-PLAN.md's DESIGN.md + catalog steps. The remaining plan item is the
Windows-host verification of WinUI (`SyxxUI`) and the `warp`/`cage` runtime.

## Unified binary (UNIFIED-BINARY-PLAN.md) — macOS Phase 1 (2026-07-14)

Consolidating the 12 standalone CLI tools into a single `syxx` binary with subcommands. macOS Phase 1
now builds clean and every subcommand runs against real data. **macOS 420t/0f.** GUI (`syxx-gui`) links.

**State found on resume (Phase 1 partial):** `Package.swift` already reduced to `syxx` (SyxxCLI) +
`syxx-gui` (SyxxApp) + SyxxKit + Ccage; the 11 standalone tool sources deleted; command bodies moved to
`Sources/SyxxCLI/Commands/*.swift`; `CageHelpers.swift` created; dispatch in place. It built, but two
classes of defect made it non-functional and one honesty gap remained.

**Critical bug fixed — subcommand name consumed as the first positional arg.** Each moved command still
read the process-global `CommandLine.arguments`, so `syxx nslook google.com` parsed argv
`[syxx, nslook, google.com]` with `nslook` as the domain and `google.com` as the record type
(`Error: Unsupported record type 'google.com'`). Every positional-taking subcommand (nslook/mirror/route/
warp) was broken. Fix: each `SyxxXxxCLI.run()` now takes an explicit `argv`; the dispatcher passes
`["syxx-<sub>"] + args` so a command's own index-1 parsing is not thrown off by the `syxx <sub>` prefix.
Verified: `syxx nslook google.com` returns real A records; no-arg subcommands print usage; bad input
errors honestly.

**Cage capture wired to the unified binary (the plan's highest-risk change).**

- Guided flow (`SyxxCLI.swift:runCageCapture`): replaced the dead `locateSiblingTool("syxx-cage")` lookup
  (that binary no longer exists) with a self re-exec of the resolved executable path + the `cage`
  subcommand, under sudo when not already root.
- GUI (`AppModel.swift`): fixed a drop-value bug where `cage --duration --keep` was emitted with no
  duration value; now `cage --duration \(duration) --keep`. `locateCageBinary()` already resolves the
  unified `syxx`.
- Cage command (`Commands/SyxxCage.swift`): added `--output`, `--owner-uid`, `--owner-gid` parsing so the
  GUI's live-tail contract works. The GUI escalates via `osascript ... with administrator privileges`
  (not sudo, so `SUDO_UID/GID` are unset); the file is now chowned to the passed owner **at creation**,
  before the tail loop, so the non-root GUI can read the JSON stream live. `chownToSudoUser` generalized
  to `chownToOwner` (explicit uid/gid win, SUDO_* fallback).

**Honesty: dead binary names corrected.** Every `syxx-<cmd>` reference that a user could copy (usage
strings, `--help`/`--version` tool labels, GUI help text) now reads `syxx <cmd>` — the old hyphenated
binaries do not exist post-consolidation, so those strings were factually wrong. Left untouched:
`syxx.app` (domain) and the `var(--syxx-*)` CSS custom properties. `grep syxx- Sources/SyxxApp` returns
only CSS vars.

**Verified subcommands (real runs):** `nslook google.com` (live A records), `key` (real resolver/iface/
VPN state), `forensics --lists` (real blocklist counts), `sentinel` (real DNS config baseline),
`witness --gen-keypair` (real Ed25519 keys), `mirror`/`warp`/`nslook` no-arg usage.

**Step 2 (TracerouteRunner de-dup) — done.** Extracted the three copy-pasted traceroute wrappers
(`syxx route`, guided Portal, GUI `performTraceroute`) into one `SyxxKit/TracerouteRunner.trace(target:
maxHops:timeout:)`. Behavior preserved per caller (route 15-hop/20s, guided 15-hop/45s, GUI 30-hop/45s);
an empty result stays an honest failure, never a clean path. `/usr/sbin/traceroute` now appears only in
`TracerouteRunner.swift`. Verified: `syxx route 8.8.8.8` returns real hops.

**Step 8 (release build + app bundle) — done.** `bundle-app.sh` was stale: it built and embedded the
now-nonexistent `syxx-cage` product. Updated it to build `--product syxx` and copy the unified binary to
`Syxx.app/Contents/Resources/syxx`, which is exactly where `locateCageBinary()` looks. Verified: release
`syxx-gui` builds; `Syxx.app` contains `MacOS/Syxx` (GUI) + `Resources/syxx` (CLI, runs `Syxx 1.0.0`).

**macOS Phase 1 COMPLETE (Steps 1–8).** Not yet done: all of Phase 2 (Windows parity — needs a Windows
host to build/test). Cage `--output` live-tail could not be exercised end to end here (sandbox has no
sudo / UDP:53); the file-format contract (JSON records the GUI already parses) is confirmed by code, the
live sudo run is a real-machine bar.

## Delta analysis and audit sweep (2026-07-13)

Conducted a systematic source-grounded audit of the server, macOS, and Windows clients, focusing on Cage, Witness, MOTD/What's New, Glutton, and the rate limiter. Seventeen new deltas identified and documented in `DELTAS.md`:

- **A054 (Server Components)** — Rate limiter lockout and fractional token truncation in `ratelimit.py`.
- **A055 (Server Components)** — SSRF bypass in `fetcher.py` and `resolver.py` via IPv4-mapped IPv6 addresses (e.g. `::ffff:127.0.0.1`).
- **A056 (Server Components)** — Keygen signature validation date header replay vulnerability.
- **A057 (Server Components)** — Server-side update version comparison math bugs.
- **A058 (Server Components)** — Authoritative DNS server binds only to IPv4, lacking IPv6 query listening.
- **A059 (Server Components)** — Recursive resolver DNS client hardcodes IPv4 sockets, failing to query IPv6 target nameservers.
- **A060 (Server Components)** — Server DNSSEC observation early exit bug on system resolver empty answers.
- **B083 (Verdict & Evidence Integrity)** — DNSSEC verifier query error fallback treats failures as unsigned.
- **B084 (Verdict & Evidence Integrity)** — SSRF validation in ContentWarp blocks CNAME resolving target hosts.
- **C028 (Verdict & Evidence Integrity)** — macOS `WitnessView` displays unconditional green checkmark success banner even when signature validation fails.
- **D055 (Platform Integration & Resource Safety)** — macOS privilege escalation child process leak where `syxx-cage` survives termination of `osascript`.
- **D056 (Security & Hardening)** — Encrypted DNS status reports yes for mixed encrypted and unencrypted resolver configurations.
- **D057 (Security & Hardening)** — Collector signature Base64 encoding mismatch between client decoders and server encoder.
- **D058 (Security & Hardening)** — macOS RemoteContentView WebView delegate lifetime bypass due to missing updateNSView coordinator sync.
- **E040 (Platform Integration & Resource Safety)** — Windows DNS capture binds to a single interface IP, failing to capture DNS traffic on active VPN interfaces.
- **E041 (Platform Integration & Resource Safety)** — macOS route trace tool blocks thread before pipe read, introducing deadlock risks.
- **F023 (Miscellaneous)** — CLI tools violate Emoji Policy by printing raw emojis to stdout instead of using bracket or clean text formats.
- **A061 to A110 (Server Hardening & Audit Sweep 4)** — Vetted and promoted 50 new server-side and script-level deltas covering DNS query validation bypasses, SSRF vulnerabilities, payment webhook concurrency races, rate-limiting leaks, database storage bloat, cryptographic verification gaps, and systemd deployment sandboxing.
- **D059 (Security & Hardening)** — Offline-First Trial Launch Reset Vulnerability where client trial anchors reset when syncing offline first-launches to the server.
- **E044 (Platform Integration & Resource Safety)** — WPF UI thread deadlock via synchronous blocking on `SyncTrialAnchorWithServer` async operations inside `IsFeatureUnlocked`.
- **C029 (Verdict & Evidence Integrity)** — DNS telemetry ethernet padding includes trailing padding bytes in raw packet payload sizes, corrupting telemetry measurements.
- **B085 (Verdict & Evidence Integrity)** — Witness snapshot verification allows future-dated snapshots to bypass max-age freshness checks.
- **E045 (Witness View)** — Witness View lacks keypair generation on both platforms, forcing users to use CLI utilities.
- **C030 (Verdict & Evidence Integrity)** — macOS captive portal probe follows HTTP redirects, causing slow fetches and differing from Windows.
- **E046 (Platform Integration & Resource Safety)** — Windows process resolution netstat regex pattern `:port .*UDP` fails UDP local process attribution due to protocol position.
- **D061 (Security & Hardening)** — Paid Pro user offline lockout where client falls through to expired trial checks on Keygen network/timeout errors.
- **D060 (Security & Hardening)** — Privileged capture command on macOS is vulnerable to shell command injection/syntax errors via unescaped single quotes in paths.

## Remediation Master Plan execution — S0 (2026-07-13)

- **S0 pre-flight:** Committed the uncommitted working-tree cluster as `0b3fad5` (ProbePlan/ProbeRunner/ProbeBudget/DomainCanonical refactor + A019 licensing + tests). Phase-1 gates (`SynthesisEngine.swift:139-152`) and Phase-2 M2 dampener (`:236`) confirmed committed on both platforms. F023 id-collision renumbered — emoji variant → F031.
- **Build:** macOS `swift build` passes clean.

- **WS2-A.1 (B065/B083):** `didFail` now includes `.truncated`, `.servFail`, `.refused` on macOS (`DNSResolver.swift:183-184`) and Windows (`DNSSECVerifier.Failed`, `DNSResponse.DidFail` property, `transportFailed` in `ResolveSecurelyAsync`).
- **WS2-A.2 (B068/A077):** Answer-loop ANCOUNT mismatch detection — `parsedCount` tracking returns `.malformedResponse` when declared ANCOUNT exceeds records actually parsed, both platforms.
- **WS2-A.3 (B069/B070):** Off-class RR rejection — answer parser now reads CLASS field and skips non-IN (`cls != 1`) records on both platforms.
- **WS2-A.4 (B072/B073):** EDNS extended RCODE combined with base RCODE before routing (prevents BADVERS from reading as RCODE 0); EDNS info parsed once and reused. Windows parity with `ExtendedRCode`.
- **WS2-A.5 (E018/A077):** Windows `ParseName` now throws `FormatException` on unterminated/truncated names instead of returning a partial name.
- **WS3-A.1 (D057):** Leak collector signature encoding aligned to standard base64 (`Server/syxx_server/leak.py:158`); macOS and Windows `LeakDetector.verify` normalize url-safe/unpadded signatures before verification. Added `Server/tests/test_leak_signature.py` round-trip test (2 passed).

- **WS1-Ph4.1/4.2 (B086/B088/B050/B051):** Confidence switched from single-hypothesis pignistic to class-set belief (Bel({clean}) for clean, Bel(threatUnion) for threats). All-clean belClean is ~56%, triggering referral. Mechanism-class partition: spoofing+interception_onPath → rewrite, interception_transport → transport, censorship → block, contentTampering → content, privacyLeak → leak, localCompromise → host. Stage-5 now uses concrete mechanism class instead of .unresolved. macOS 416 pass; Windows 245 pass (1 golden-seed referral expectation parity gap).
- **WS1-Ph3.2/3/4 (I5/I3/B086):** Clean-support mass now scales by per-section `completeness` and is count-based (one bona-fide support per clean section). Degraded sections have evidence reliability penalized but do not supply clean support, so a degraded positive signal is never zeroed by clean-wash. Added `completeness` to `SectionStatusEntry` and `SectionEvidence` (additive, Codable-compatible) on both platforms.

## ENG — Reasoning-engine remediation: Phases 1–2 + P0.4 seam + validator hardening (2026-07-12)

Both platforms compile at HEAD `8afbf4d`. Production behavior changes ONLY where a real honesty bug was
fixed (the Phase-1 gates + the M2 dampener); everything else is behind a flag or pre-emptive.

- **Phase 1** (`bdfc832`) — structural honesty gates: invalidated-dominates, critical-not-conclusive-forbids-clean,
  degraded-source discounting, `.positive`-blocks-clean, I8 committed-threat-mass floor, min-corroboration referral.
  Verified macOS 398 / Windows 240 — but green tests are NOT the bar; the golden corpus is synthetic and
  real-capture validation (O1) is still unmet.
- **Phase 2** (`d28c70a`, `2c95d7b`) — M2 causal dampener: a benign marker (captive portal, encrypted DNS) may
  dampen a threat ONLY when it can causally explain it (new `BenignExplanation` table) AND no independent reliable
  source confirmed it; otherwise the ambiguity forces referral (anti-cancellation, R1). Closes the bug where a
  captive portal silently ate a confirmed DNSSEC strip's belief. Plus the named M4 decision-policy region + empty
  `ValidatedDecisiveActs` scaffold (Phase 6 fills).
- **P0.4 seam** (`5bec6f3`, `d262635`) — pluggable `Config.combinationRule`; `.cautious` dissolves the
  `maxWeightPerSource` cap (the NE1 Δ2 inversion cause). Production stays `.dempster`; the default is NOT flipped
  until the bake-off runs on a REAL corpus.
- **Stage-5 validator hardening** (`8afbf4d`) — `isValid` fails closed on unsupported schemaVersion,
  clean-with-critical-missing, clean-with-findings, clean-with-active-conflict (V1–V4). The GUI gates on `isValid`,
  so this closes those honesty holes at the source.

**Incident recovered:** a stray `git checkout` reverted the uncommitted macOS `SynthesisEngine.swift` mid-Phase-2;
re-applied from the known diff and committed. No commits lost.

**The wall:** Phase 3+ (fusion-rule commit, class partition, calibration) is gated on the real-capture corpus (O1),
the lineage map (O4), and user sign-off (O5). Not faked.

## WS6-01 — Cage Query Capture Report layout & content (2026-07-11)

New report template on both platforms. Full suites green:
**macOS 364t/0f ✅, Windows 216t/0f ✅**.

### macOS — `DNSCaptureReportGenerator.swift`

- **New72-char header** with centered "Syxx / Cage Query Capture" title and download line.
- **Metadata block**: Date, Duration, Interface, Output (relative), Report (relative).
- **Parse summary**: Records parsed, unparseable lines, file size, event rate.
- **Top-8 lists**: Domains, parent domains, processes, resolvers (was top-20).
- **Combined ANOMALOUS INDICATORS**: Long domains + encoded-looking domains as counts.
- **Actionable INTERPRETATION**: Summary sentence with top process stats, anomaly descriptions.
- **NEXT STEPS**: Review anomalies, run syxx-mirror, re-run with --keep.
- **INCONCLUSIVE variant**: Honest empty-capture report with TROUBLESHOOTING steps.
- **Minify recommendation**: `(consider --minify to reduce file size)` when file > 1 GB.
- **Relative paths**: `relativeCapturePath()` strips home directory prefix from output/report paths.
- **`interface` parameter**: Added with default `"unknown"`, passed from SyxxCage CLI.

### Windows — `DNSCaptureReportGenerator.cs`

- Identical layout and copy to macOS (platform parity).
- `RelativeCapturePath()` strips `UserProfile` prefix from paths.
- `iface` parameter added with default `"unknown"`.

### Tests

- Updated `testCaptureReportZeroRecordsIsInconclusive` on both platforms to match new format
  (`"Records parsed:"` instead of `"Parsed records:"`).

## Post-BOB session — 2026-07-11 (B005 design, F009 items 3/5, Windows live verified)

Branch `feat/beta-signup-and-server-hardening`. Full suites green:
**macOS 364t/0f ✅, Windows 216t/0f ✅ (live verified on VM), Server 97t/0f ✅**.

### B005 (WS2-05) — Score no/partial/full-data design spec

- **`DESIGN.md`** — new "Score-design invariants" section specifying zero-data → inconclusive,
  partial-data → attenuated, full-data → normal contracts for all three flagship scores:
  Resolver Provenance Confidence, Tamper Mechanism Fingerprint, Evidence Confidence Score.
  5 invariant rules, 3 data-regime tables with signal lists and edge cases (resolver unreachable,
  minimum corroboration rule, critical-section gating). Composition math deferred pending B003
  independence resolution.

### F009 item 3 — macOS `Default*_HasExpectedValues` parity tests

- **`macOS/Tests/SyxxKitTests/ProbeRunnerTests.swift`** — 4 new tests: `test_defaultProphet_HasExpectedValues`,
  `test_defaultMirror_HasExpectedValues`, `test_defaultPortal_HasExpectedValues`,
  `test_defaultGlutton_HasExpectedValues`. Mirrors Windows tests at `SyxxKitTests.cs:2114-2147`.
  macOS suite: 364t/0f (was 360, +4).

### F009 item 5 — Clean-slate confirmed

- Investigated both platforms for skipped/conditional/commented-out tests: zero instances found
  beyond the single intentional `XCTSkip` guard in the D003 live-TLS test. No `FIXME`/`TODO`/`PENDING`
  in any test file. Item was already resolved by Phase 0 commit. Marked DONE.

### Windows 216t/0f live verified

- On 2026-07-11, full `dotnet restore && dotnet build && dotnet test` pipeline executed live on
  Windows VM (C:\ drive, bypassing MSBuild MSB4248 network-drive bug). 216 tests passed, 0 failed,
  0 skipped. Build warnings (59) are all pre-existing nullability/hygiene warnings.

## HANDOFF-BOB.md implementation — 2026-07-11 (BOB-1 through BOB-3 + C009 Windows parity)

Branch `feat/beta-signup-and-server-hardening`. All fixes mutation-proven. (**Note:** macOS test count rose from 360 to 364 due to F009 item 3 parity tests added in the Post-BOB session.)

### BOB-1 (F006) — ProbeRunner real-elapsed accounting + DNS cooperative cancellation

- **`macOS/Sources/SyxxKit/ProbeRunner.swift:9`** — stale doc ref `(**C006**)` → `(**F006**)`.
- **`macOS/Sources/SyxxKit/ProbeRunner.swift:66-72`** — removed `perProbeTimeout` cap; budget now
  records real wall-clock elapsed on both success and timeout paths.
- **`macOS/Sources/SyxxKit/DNSResolver.swift:273`** — added `guard !Task.isCancelled` at top of UDP
  retry loop so a cancelled probe tears down at the deadline instead of running ~4s past it.
- **Windows** `Syxx.Kit/ProbeRunner.cs` — same real-elapsed accounting (Stopwatch); doc comments updated.
- **Windows** `Syxx.Kit/ProbeBudget.cs` — doc comments updated.
- **Tests:** `ProbeRunnerTests.swift` `test_run_timeout_accountsCorrectly` upper bound raised from
  `<= 0.015` to `<= 1.0`; `test_run_timeout_realElapsedNotCapped` added (mutation-proven: revert
  to cap → RED). Windows twins updated and added in `SyxxKitTests.cs`.

### BOB-2 (F007) — DomainCanonical post-IDNA length guard + accurate doc claim

- **`macOS/Sources/SyxxKit/DomainCanonical.swift:47-51`** — 253-byte guard now measures
  `ascii.utf8.count` (A-label after IDNA encoding), not `trimmed.utf8.count` (pre-IDNA UTF-8).
- **`macOS/Sources/SyxxKit/DomainCanonical.swift:1-11`** — doc claim "every domain that enters the
  system must pass through here" downgraded to "used at the Dimension client boundary" (C004), since
  `ProbePlan` and the CLI pass raw user input directly to DNS queries — the overreach claim was false.
- **`Windows/Syxx.Kit/LAN/DomainCanonical.cs:1-11,34-36`** — same post-IDNA guard and doc fix.
- **Tests (macOS):** `testDomainCanonical_rejectsNameTooLongAfterEncoding` (`"aä"×28`; UTF-8=111≤253,
  A-label=279>253 → rejected); `testDomainCanonical_acceptsNameBelowLimitAfterEncoding` (ASCII short).
- **Tests (Windows):** `DomainCanonicalTests` class — `RejectsName_TooLongAfterIDNAEncoding`,
  `AcceptsName_BelowLimitAfterIDNAEncoding`, `RejectsEmpty`, `StripsDot_AndLowercases`,
  `AreEquivalent_ReturnsFalse_OnMismatch`.

### BOB-3 (B039) — ReportTests cross-discipline target_answer + structural checks

- **`macOS/Tests/SyxxKitTests/ReportTests.swift`** — 4 new tests:
  1. `testTargetAnswer_crossDisciplineDiscrepancy_isConflicting` — Prophet≠Mirror `target_answer`
     → `isConflicting==true` + DISCREPANCY in `renderText()`. Mutation-proven: collapse to same IP
     → test REDs.
  2. `testEmptyInputs_producesNoDatapoints` — empty `SectionInputs()` → zero datapoints, no crash.
  3. `testRenderText_containsRequiredStructuralSections` — Verdict / FACTS / NEXT STEPS all present.
  4. `testRenderText_secondOpinionBlock_appearsIffReferralPresent` — SECOND OPINION iff `referral != nil`.
- **`Windows/Syxx.Tests/SyxxKitTests.cs`** — `ReportTests` class with the four matching tests.

### C009 Windows parity fix (found during BOB-3 suite run)

`Windows/Syxx.Tests/SyxxKitTests.cs:1072` had `ExfilFusesHighEntropyAndRegularCadenceIntoLikely`
asserting `ExfilSuspicion.LikelyExfil` with only 2 signals (highEntropy + regular, no blocklist).
After OC-4 (C009) raised the bar to ≥3 signals for `.LikelyExfil`, this test was stale — the
correct behavior is `.Suspicious` for 2 signals. The test was also missing the single-signal-only
parity from the macOS `testExfilBenignForSingleSignalOnly`. Fixed:

- Renamed to `ExfilSuspiciousForTwoSignalsWithoutBlocklist` with `Assert.Equal(Suspicious)`.
- Added `ExfilBenignForSingleSignalOnly` (entropy-only → benign; beacon-only → benign).

macOS was already correct (OC-4 updated it). Windows now at parity. **216/0**.

## D4 — Windows "Check for Updates" banner UI — 2026-07-11

Completed the last remaining gap from Claude Code's D1–D4 Workstream D client/GUI tasks
(HANDOFF-OPENCODE-2.md). All server Dimension endpoints (`/api/startup`, `/api/whats-new`,
`/api/broadcast`) were already live; macOS client was fully implemented (RemoteContentView,
BroadcastPanelView, DimensionService, UpdatePanel); Windows had data plumbing but no update UI.

- **New:** `Windows/SyxxUI/Views/UpdatePanel.xaml` + `.xaml.cs` — ContentDialog-hosted update
  panel matching macOS `UpdatePanel.swift`. Shows Syxx branding, current version, last-checked
  timestamp, status banner (idle/up-to-date/available/failed with IoDesignSystem colors), and
  Check for Updates / Download Update buttons. Manual check bypasses the 24-hour cache.
- **New:** `MainWindow.xaml` — update-available sidebar banner (amber "Update Available" button
  in pane footer, shown/hidden from code-behind based on startup response).
- **Modified:** `MainWindow.xaml.cs` — after `CheckStartupAsync()`, shows the UpdatePanel
  dialog + sidebar banner when `ViewModel.UpdateAvailable == true`. Added `ShowUpdateDialog()`
  helper (shared between auto-show and manual trigger via `CheckUpdateBtn_Click`).
- **Build:** Windows solution 0 errors, 0 warnings.

## A021 — Keygen response signature verification (Server) — 2026-07-11

- **Problem:** `_validate_keygen()` accepted `meta.valid` from Keygen's JSON response
  without cryptographic verification. A compromised CA or substituted trust anchor
  could forge paid-Pro access to server routes.
- **Fix (Fix 1):** Ed25519 response signature verification per the draft RFC http-signatures
  format. Added `config.KEYGEN_PUBLIC_KEY` (hex-encoded Ed25519 public key). When set,
  `_verify_validate_key_response()` reconstructs the signing data from `(request-target)`,
  `host`, `date`, and `digest` (SHA-256 of raw body), then verifies the `Keygen-Signature`
  header using the `cryptography` library. Missing/invalid signature → `None` (denial).
  Empty key → skip (backward compat for dev/test).
- **Files:** `Server/syxx_server/licensing.py` (new `_verify_validate_key_response()`,
  modified `_validate_keygen()`), `Server/syxx_server/config.py` (new `KEYGEN_PUBLIC_KEY`),
  `Server/tests/test_licensing.py` (22 new tests).
- **Tests:** 22 tests, all passing: valid signature works, forged body denied, missing/
  malformed signature denied, replayed date denied, wrong host/path denied, HTTP errors
  still transient, no-pubkey compat, offline-key path unchanged.
- **Build:** Server suite 97t/0f.

## D011 — Trust-set key population (macOS + Windows) — 2026-07-11

Threads `trustedPublicKeys` into `LeakDetector.verify()` so a valid signature from an untrusted key
resolves to an honest untrusted-key outcome, distinct from valid (signature + key trusted) and invalid
(signature wrong).

- **macOS** `LeakDetector.swift`: `signatureKeyTrusted: Bool?` on `LeakTestResult` (tri-state: `nil` =
  no trust set configured, `true` = key in trust set, `false` = key not in trust set).
  `trustedPublicKeys: Set<String>?` param on `runCollectorTest()`. Trust-check wiring after crypto
  verification succeeds.
- **Windows** `LeakDetector.cs`: `SignatureKeyTrusted: bool?` on `LeakTestResult`;
  `trustedPublicKeys: ISet<string>?` param on `RunCollectorTestAsync()`.
- **Tests:** 5 per platform (default is nil, round-trips, key in set → trusted, key not in set → not
  trusted, nil set ignores trust).
- **Build:** macOS 360t/0f, Windows 0 errors, 0 warnings.

## Verified done (prior sessions): B039, F006, F007, D003

- **B039:** macOS `ReportTests.swift` has all 8 tests (target_answer conflict/consensus, empty
  SectionInputs, structural sections, SECOND OPINION gating). Windows tests match. No code change
  was needed — tests were already present.
- **F006:** `ProbeRunner.swift` uses real wall-clock elapsed (not perProbeTimeout cap) for budget
  accounting. 8 ProbeRunnerTests. Windows parity already done (Stopwatch wall-clock).
- **F007:** `DomainCanonical.swift:48-51` and `DomainCanonical.cs:41` measure A-label length after
  IDNA encoding. CLI entry points on both platforms canonicalize all domain arguments. Invalid
  domains rejected at CLI boundary.
- **D003:** Full SPKI pinning infrastructure (CollectorPinningDelegate, CollectorPinning,
  CollectorTransportPinning) was confirmed present and working in the working tree. 4 pinning tests
  pass. macOS 354t/0f (now 360).

## WS3-03 / D003 — TLS (SPKI) pinning for the leak collector — 2026-07-11 (syxx-engineer)

Closes the forgeable-evidence hole where the leak collector transport trusted the system CA store
only, so a CA compromise / on-path TLS proxy could return forged leak verdicts with fake Ed25519
attestations. Now fail-closed on both platforms; a pin/PKI failure yields `inconclusive`, never a
trusted leak/no-leak and never an accepted attestation.

- **macOS** `Sources/SyxxKit/LeakDetector.swift`: new `CollectorTransportPinning` posture on
  `LeakTestResult`; `CollectorPinning` (config slot — `collector_spki_pins` UserDefaults or
  `SYXX_COLLECTOR_PINS` env, primary+backup; RFC-5280 DER SubjectPublicKeyInfo walker → SHA-256 →
  base64; pure `decide`); `CollectorPinningDelegate` evaluates `SecTrustEvaluateWithError` then the
  SPKI pin, `cancelAuthenticationChallenge` on miss. `runCollectorTest` builds a per-run pinned
  session and maps a cancelled challenge to `.inconclusive`/`.failed` + typed reason.
- **Windows** `Syxx.Kit/LeakDetector.cs`: mirror — `CollectorPinning` (`SYXX_COLLECTOR_PINS`;
  `PublicKey.ExportSubjectPublicKeyInfo()`; `Accept`); `HttpClientHandler.ServerCertificateCustom
  ValidationCallback` fail-closed; `AuthenticationException` mapped to the pinning reason.
  `InternalsVisibleTo Syxx.Tests` added for the internal helpers.
- **Parity:** the pin (`pin-sha256`) is byte-identical across openssl, .NET `ExportSubjectPublicKeyInfo`,
  and the Swift DER walker (EC P-256 and RSA-2048 both verified) — owner supplies ONE value for both.
- **Verified:** macOS `swift test` **354/0** including a LIVE self-signed-TLS handshake test that a
  mismatched pin fails closed to `inconclusive`; Windows D003 unit tests pass; both platforms
  mutation-proven (accept-all → guard REDs). Real collector (`leak.syxx.app`) was unreachable from
  this host (firewalled), so end-to-end match-proceeds against production is UNVERIFIED here.
- **User-visible strings:** the pin-failure reason and unconfigured caveat were APPROVED by the user
  (2026-07-11) and finalized (placeholder `[D003]` prefix removed). The real pin value remains an
  owner deliverable (unpinned + caveat until set). SyxxUI (WinUI) not buildable on this host —
  source-compatible optional-param change only, so unaffected but unverified.

### Bonus — DomainCanonical case-folding parity bug (found via the Windows red test)

`DomainCanonical.CanonicalizeOrNull("EXAMPLE.COM.")` returned `"EXAMPLE.COM"` instead of
`"example.com"`: `IdnMapping.GetAscii` / `toASCIIName` do NOT case-fold pure-ASCII labels, and the
documented "lowercase" rule was never applied. Both platforms had the latent bug (macOS was simply
untested for it). Fixed at the canonicalization boundary on both — `LAN/DomainCanonical.cs:41` and
`DomainCanonical.swift:52` now `.ToLowerInvariant()` / `.lowercased()` the encoded name (NOT inside
`toASCIIName`, so case-sensitive DNS callers like 0x20 encoding are unaffected). Added a macOS
`testDomainCanonical_lowercasesAndStripsTrailingDot` guard mirroring the Windows test. Both suites
green: **macOS 355/0, Windows 210/0**.

## HANDOFF-OPENCODE.md implementation — 2026-07-11 (OC-1 through OC-9)

Branch `feat/beta-signup-and-server-hardening`. All fixes below verified building; every code change
mutation-proven (break the fix → test REDs). Full suites green: **macOS 349, server 73, Windows 0
errors.**

**OC-1 (A019) — Stripe idempotency state machine** (Server):

- `fulfillment.py:73-95`: replaced `kv_insert_once` guard with explicit state machine (`minted`/
  `processing`/`failed`). Keygen failure sets `"failed"` so Stripe retries re-enter mint.
- `test_payments.py`: added outage-then-retry test + double-retry idempotency test.

**OC-2 (A020) — DNS oracle hardening** (Server):

- `resolver.py`: question-tuple validation (qname case-insensitive, qtype, qclass=1) in `_parse_response`
  and `_parse_sections`. Connected UDP (`sock.connect()`) so kernel discards off-source spoofed replies.
- TCP fallback (`_query_tcp`) also validates question.

**OC-3+OC-7 (B032/B034) — macOS tunnel→`.unknown` + typed-outcome tests**:

- `NetworkKeyState.swift`: extracted pure-core `encryptedDNSStatus(servers:scoped:)`; tunnel-only
  resolvers return `.unknown` (not `.yes`).
- `SyxxKitTests.swift`: 5 typed-outcome tests replacing vacuous constructor test.

**OC-4 (C009) — ExfilAnalyzer false-positive fix + BeaconAnalyzer degenerate fix**:

- `ExfilAnalyzer.swift`: changed to require ≥2 of {highEntropy, regular, flaggedBy} for `.suspicious`;
  ≥3 for `.likelyExfil`. Single signal → `.benign`.
- `BeaconAnalyzer.swift`: identical-timestamp series → `.insufficientData` (not `.highlyRegular`).
- `SyxxKitTests.swift`, `MonitorReportAnalyzerTests.swift`: regression fixtures + distinct timestamps.

**OC-5 (C006) — no silent 8.8.8.8 fallback** (macOS + Windows):

- `SyxxWitness.swift`, `SyxxCLI.swift` (2 sites): warning `⚠️ System resolver undetectable — falling
  back to 8.8.8.8` printed before fallback.
- `ContentWarp.swift` (macOS), `ContentWarp.cs` (Windows): no resolver → `return true` (conservative,
  assume public). Intentional public-vantage resolver lists unchanged.

**OC-6 (B024) — Warp verdict-label consistency** (macOS):

- `SyxxWarp.swift`: when overall verdict is inconclusive, per-check labels show "⚠️ SKIPPED (proxy
  unreachable)" instead of "✅ MATCH" / "🚨 MISMATCH". Proposed wording — awaiting user sign-off.

**OC-8 (B035) — narrow test whitelist**:

- `SynthesizerTests.swift`: replaced `contains()` whitelist with explicit `||` chain so `.clean`
  cannot be silently added to a future array.

**OC-9 (B036) — typed `classify()` for empty-set discrimination**:

- `AuthoritativeVantage.swift`: `resolverDidFail` parameter to `classify()` and `compare()`.
  Empty+empty with resolver failure → `.inconclusive` (not `.matches`).
- `ProbePlan.swift`, `SyxxCLI.swift`: call sites updated to pass `comparison.localOutcome.didFail`.
- `AuthoritativeVantageTests.swift`: regression test + classify-direct failure case.

**Other:** `LeakDetector.swift` reverted to committed version (WS6 uncommitted code referenced
`CollectorPinning`/`CollectorPinningDelegate` which don't exist in source tree).

## WS2–WS5 landings + §3 test-audit sweep — 2026-07-10 (Claude Fable 5)

Branch `feat/beta-signup-and-server-hardening`. All fixes below verified building; every test
mutation-proven (broke the code, confirmed RED, reverted). Full suites green: macOS 334, Windows
196, server 69.

**Fixes landed:**

- **WS2a — D013** (`1975b54`): Windows Glutton/Warp gate on server-validated `IsFeatureUnlockedAsync`,
  bool overload `[Obsolete]`.
- **WS2b — D015** (`149e81b` macOS Keychain, `334f009` Windows DPAPI): Ed25519 signing key off the
  observable view-model; only `hasWitnessKey` exposed.
- **WS3a — E011** (`4d22ba5`): canonical cross-platform `SignedEvidencePayload` — Ed25519 verifies
  across platforms; absorbs B026 (formatVersion), D005 (expiry), B027 (camelCase).
- **WS3b — A022** (`f2861b5`): collector fails closed without a provisioned key (no ephemeral).
- **Code defects from the audit** (both platforms): **B037** (`655c435`) witness display-integrity
  guard now covers `resolverQueryOutcomes`; **B038** (`1bb87c7`) DualStack A-timeout → inconclusive,
  not a censorship verdict (§1).
- **WS5 — server hardening A001–A018** (`266a4b7`..`40a8fe6`, 8 per-delta commits): rate limiter,
  SSRF/truncation fetcher, authoritative walker recursion cap + AA gate, licensing query-string/
  base64, Stripe idempotency + all-v1 sigs, trial-anchor retention, HTTP handler cluster.

**§3 test audit (561 tests → 518 adequate / 20 weak / 11 invalid / 0 mocked-output):** closed every
cleanly-committable failure-case gap, each mutation-proven. Highlights: all Critical server-security
properties now red-on-break (SSRF, rate-limit, session-forgery, webhook-route signature enforcement,
leak signed-payload↔display binding); synthesis §6 zero-confidence-on-empty; witness corrupt-signature;
hostile-input packet parsing; TXID sequential-counter rejection; NDJSON field fidelity; keygen
round-trip; rubric calibration-spec vs magic constants; threshold boundary off-by-ones; DNS-tunnel
FLAGGED-not-present; ARP-parse fixture. Deferred (need a Windows host / production seams / WS6):
filed as **F009**; a possible exfil false-positive filed as **C009**.

## WS1 ground-truth / false-clean cluster — 2026-07-10 (syxx-engineer)

Landed the cleanly-separable, standalone-buildable WS1 deltas as small commits;
verified each against a fresh worktree build + suite, plus a real Prophet run.

- **E013 / B025** (7eb2151) — IPClassifier now classifies the whole RFC 2544
  benchmarking /15 (198.18.0.0–198.19.255.255) as special-use/blocking, closing the
  198.19.x.x leak a transparent proxy/sinkhole could hide behind. Table tests both
  platforms.
- **B029 / B004** (2f080e0) — rebuilt the source-group correlation as a deliberate
  map (correlationGroup, from the live EvidenceId enum) and made a correlated group
  contribute ONE merged support capped at maxWeightPerSource; replaced Opencode's
  wrong blanket-per-section sources (which over-capped independent signals). macOS
  308 pass at the commit; Windows parity tests written + pass (187) in-tree.
- **B030** (0baf4a1) — Windows leak counts only *answered* dispatch statuses, so the
  E007 all-failed guard is reachable and a blackholed network reads Inconclusive, not
  a false NotDetected. Fix A only; the "sent"→"answered" copy (Fix B) is parked for
  user approval.

Verified but NOT committed (require the coupled probe-orchestration closure — the
new-signature AuthoritativeVantage callers AppModel/SyxxCLI/Program.cs and the
untracked ProbePlan/DimensionClient + uncommitted ProbeBudget/ProbeRunner C005 infra;
committing them standalone would break HEAD callers or bundle 8+ out-of-WS1 deltas):

- **B031 / C007** — multi-bootstrap consensus restored at the call sites (≥2 set minus
  the resolver-under-test) + majorityConsensus returns nil for a single responder.
  Present and unit-tested in the working tree (AuthoritativeVantageTests, incl. new
  single-responder→inconclusive + majorityConsensus tests); must land with the WS6
  probe-cluster commit.
- **B033** (new delta, filed this pass) — a half-applied D015 change (WitnessView.swift:37
  still binds the removed AppModel.witnessKey) leaves the working tree non-compiling for
  the macOS test bundle; documented in DELTAS.md. Out of WS1 scope.

## Deltas Analysis Sweep — macOS Client Codebase — 2026-07-09

Comprehensive code review of 15 macOS client source files for security defects,
efficacy issues, Honesty Protocol violations, and platform integration gaps.

### Verified Fixed (DELTAS.md statuses updated)

- **B003** (Confidence 100% on zero evidence) — Superseded by DST engine
- **B004** (Correlated signals summed as independent) — Partially fixed;
  source-group discount live for canaries only — B029 created for residual gap
- **B006** (Softmax assigns probability to unevidenced threats) — Superseded by DST engine
- **B008** (Clean verdict when all sections inconclusive) — Fixed by DST: clean support
  gated on `.negative` status
- **B009** (Captive portal `.negative` while intercepting) — Fixed: uses `.degraded` on macOS
- **B022** (Inconclusive verdict confidence meaningful) — Fixed: confidence = 0 for
  inconclusive

### New Deltas Recorded

- **B029** — EvidenceItem.source only set for canaries; source-group discount dead
  for all other correlated signals (Critical)
- **C006** — Silent 8.8.8.8 fallback without user consent in CLI (Critical;
  auto-enhanced per rule #1)
- **D014** — macOS CLI probe orchestration duplicates ProbePlan (Medium;
  future-drift risk)
- **D015** — Ed25519 private key in `@Published` property on AppModel (High;
  memory-exposure risk)

### Research Created

- `research/dns-interpretation-and-confidence-scoring.md` — comprehensive reference
  mapping each Syxx datapoint to what it can evidence alone and in combination,
  with thresholds, benchmarks, and recommended next steps per verdict

## C005 Phase 3 — Shared ProbePlan Extraction — 2026-07-09

Extracted standardised per-section probe orchestration into shared `ProbePlan` in both Kits,
replacing copy-pasted section runners across three frontends.

### Added files

| Platform | File | Purpose |
|----------|------|---------|
| macOS | `SyxxKit/ProbePlan.swift` | `ProbePlan.runProphet`, `runMirror`, `runPortal`, `runGlutton` |
| Windows | `Syxx.Kit/ProbePlan.cs` | `RunProphetAsync`, `RunMirrorAsync`, `RunPortalAsync`, `RunGluttonAsync` |

### macOS App refactoring (AppModel.swift)

- `runCompare()` → delegates to `ProbePlan.runMirror(domain:localResolver:isPro:targetURL:)`
- `runNetworkState()` → delegates to `ProbePlan.runPortal()`
- `runLookup()` → keeps user-selected server query for display; canonical Prophet suite delegated
  to `ProbePlan.runProphet(domain:resolver:)` against the audit (system) resolver

### Windows GUI refactoring (AppViewModel.cs)

- `RunProphetAsync()` → delegates to `ProbePlan.RunProphetAsync(Domain, resolver)`
- `RunMirrorAsync()` → delegates to `ProbePlan.RunMirrorAsync(Domain, local, isPro, targetURL)`
  (adds A2/A3 probes — previously only vantage + warp were run)
- `RunPortalAsync()` → delegates to `ProbePlan.RunPortalAsync()`

### Not refactored (per-probe progress printing required)

- macOS CLI (`SyxxCLI.swift`) — keeps own probe loop for per-probe print output
- Windows CLI (`Program.cs`) — keeps own probe loop for per-probe print output
- Both `runLeak` / `RunGluttonAsync` (App/Windows GUI) — per-probe result display needed
- Both `runWitness` / `WitnessTestify` — key-management flow is frontend-specific

### Scope boundaries

- `ProbePlan` orchestrates execution and writes to `SectionInputs` only.
- All `print()` / `Console.WriteLine()` presentation stays in the frontend.
- The Kit does NOT emit user-visible strings.

### Test results

- macOS: `swift test` — 312/312 passed, 0 failures.
- Windows: `dotnet test` — 173/173 passed, 0 failures.

## C005 Phases 2b–2d — ProbeRunner Migration (App, Windows CLI, Windows GUI) — 2026-07-09

Migrated the remaining three frontends to wrap every probe call in `ProbeRunner.run`, extending
the C005 Phase 2a (macOS CLI) pattern to all four frontends.

### Phase 2b — macOS App (`AppModel.swift`)

- `runLookup`: all 12 Prophet probes wrapped
- `runCompare`: 5 Mirror probes wrapped; throwing `VantageComparer` → flatMap pattern
- `runNetworkState`: 2 Portal probes wrapped + open-resolver conditional probe
- `runLeak`: 2 Glutton probes wrapped
- `runWitness`: 3 Witness probes wrapped (VantageComparer + synchronous WitnessSigner)

### Phase 2c — Windows CLI (`Program.cs`)

- `RunProphetAsync`: 12 Prophet probes wrapped (DNSSECStatus enum left unwrapped)
- `RunMirrorAsync`: 4 Mirror probes + VantageComparer/ContentWarp (throwing → closure null return) + GroundTruth
- `RunPortalAsync`: 2 Portal probes wrapped
- `RunGluttonAsync`: 2 Glutton probes wrapped (LicenseManager tuple left unwrapped)

### Phase 2d — Windows GUI (`AppViewModel.cs`)

- `RunProphetAsync`: 10 Prophet probes wrapped (DNSSECStatus enum left unwrapped)
- `RunMirrorAsync`: 2 Mirror probes wrapped (expanded to full suite in Phase 3)
- `RunPortalAsync`: 2 Portal probes wrapped (expanded to full suite in Phase 3)
- `RunGluttonAsync`: 2 Glutton probes wrapped

### Key patterns (all phases)

- Throwing probes: `do/catch` inside `ProbeRunner.run` closure → `return null`/`nil` → flatMap `T??`
- Synchronous throwing ops wrapped for budget accounting (timeout N/A for sync work)
- Platform-specific: Swift `@Sendable` cannot capture `@MainActor` properties — local captures
- Windows `ProbeRunner.RunAsync<T>` requires `T : class`; value types (DNSSECStatus enum, LicenseManager tuple) left unwrapped
- `DNSResponse` default on timeout: macOS `init(status:errorMessage:)`, Windows `new DNSResponse { Status = ..., ErrorMessage = ... }`

### Debug output

- Gated by `SYXX_DEBUG_PROBES` env var on both platforms (format pending user approval — R6)

### Test results

- macOS: `swift test` — 312/312 passed, 0 failures.
- Windows: `dotnet test` — 173/173 passed, 0 failures.

## C005 Phase 2a — macOS CLI ProbeRunner Migration — 2026-07-09

Migrated all four section-runner functions in `macOS/Sources/SyxxCLI/SyxxCLI.swift` to wrap
every individual probe call in `ProbeRunner.run(label:budget:)`, enforcing per-probe timeouts
and budget accounting from the CLI frontend.

### Changes

- `ProbeBudget.swift` — added `static var defaultProphet`/`defaultMirror`/`defaultPortal`/`defaultGlutton`
  computed factory properties (no stored lets — struct `inout` requires mutability).
- `SyxxCLI.swift:runProphet` — all 12 Prophet probes wrapped in `ProbeRunner.run`.
- `SyxxCLI.swift:runMirror` — 7 Mirror probes wrapped; throwing probes (`VantageComparer`,
  `ContentWarp`) handled via `do/catch` with `.flatMap { $0 }` double-optional flattening.
- `SyxxCLI.swift:runPortal` — 3 Portal probes (`networkReport`, `encryptedTransport`, `openResolver`)
  wrapped; synchronous `LANCartographer.snapshot()` and `traceroute` subprocess left unwrapped.
- `SyxxCLI.swift:runGlutton` — 3 Glutton probes (`licenseCheck`, `ecs`, `triggerQueries`) wrapped.
- `ProbeBudget` variables changed from `let` to `var` in all four section runners.

### Throwing probe pattern

- `VantageComparer.compareVantagePoints` and `ContentWarp.comparePageContent` are `throws`:
  the `do/catch` is inside the `ProbeRunner.run` closure; errors produce `return nil as T?`,
  yielding `T??` from `ProbeRunner.run`, flattened via `.flatMap { $0 }` at the call site.
- `DimensionClient.authoritative` returns `GroundTruth?` (also nullable): same `.flatMap` pattern.
- `LANCartographer` is synchronous — not ProbeRunner-wrappable; kept as-is.

### LicenseManager tuple access

- `isFeatureUnlocked()` returns `(Bool, String)` tuple (not a struct); accessed via `.0`/`.1`
  after ProbeRunner wrapping (Swift tuple label inference is lost across generic closures).

### Budget defaults

- Prophet: 30s total / 5s per-probe / 15 max probes
- Mirror: 30s / 5s / 10
- Portal: 15s / 5s / 5
- Glutton: 20s / 5s / 5

### Test results

- macOS: `swift test` — 312/312 passed, 0 failures.

## C005 Phase 1.5 — Windows GUI Probe Gap Fix — 2026-07-09

Wired 9 missing Prophet probes into `Windows/SyxxUI/ViewModels/AppViewModel.cs:RunProphetAsync()`.
Windows GUI now runs 2 → 11 Prophet probes, closing the High-criticality C005 delta.

### Change

- `AppViewModel.cs:RunProphetAsync()` — added calls to:
  1. `DNSCanaries.NxdomainHijackAsync`
  2. `DNSCanaries.DnssecControlAsync`
  3. `ResolverLatencyProbe.Probe`
  4. `AnswerStabilityProbe.Probe`
  5. `WildcardSynthesisProbe.Probe`
  6. `DenialOfExistenceProbe.Probe`
  7. `DualStackProbe.Probe`
  8. `EDNS0Probe.Probe`
  9. `VersionBindProbe.Probe`
- `OnPropertyChanged` fires once after all probes complete (was firing twice — once after Resolution, once after Dnssec).
- Sequential pattern matches `macOS/Sources/SyxxApp/AppModel.swift:216-249` and `Windows/Syxx/Program.cs:142-213`.
- No ProbeRunner yet — Phase 2d refactors this to shared infrastructure.

### Impact

- **C005 delta closed** (DELTAS.md:1426-1444 — 2/13 → 11/13 Prophet probes active in Windows GUI).
- Remaining 2 Prophet probes (`StubEntropyAudit`, `AuthoritativeComparison`) are GUI-level deferrals — they require capture-sourced or Pro-tier data respectively, no Kit-level gap.

### Test results

- Not adding per-probe unit tests (each probe is already unit-tested in Syxx.Kit).
- Windows build + test: 173/173 pass (no regression).
- macOS: 312/312 pass (no change).

## C005 Phase 1 — Shared Probe Orchestration Infrastructure — 2026-07-09

Added `ProbeBudget` and `ProbeRunner` to both Kits (zero behavioral change — probes still called
directly by frontends). This establishes the timeout-enforcement, budget-accounting, and debug-logging
infrastructure that all four frontends will migrate to in Phases 2a–2d.

### Added files

| Platform | File | Purpose |
|----------|------|---------|
| macOS | `SyxxKit/ProbeBudget.swift` | Budget struct: `totalTimeout`, `perProbeTimeout`, `maxProbes`, `elapsed`, `probesRun`, `isExhausted`, `add(duration:)` |
| macOS | `SyxxKit/ProbeRunner.swift` | `ProbeRunner.run(label:budget:operation:) -> Output?` with `withThrowingTaskGroup` timeout enforcement |
| Windows | `Syxx.Kit/ProbeBudget.cs` | Budget class (reference type) + factory methods `DefaultProphet`/`Mirror`/`Portal`/`Glutton` |
| Windows | `Syxx.Kit/ProbeRunner.cs` | `ProbeRunner.RunAsync<T>(label:budget:operation:) -> Task<T?>` with `Task.WhenAny` + `CancellationTokenSource` overloads |
| macOS | `Tests/SyxxKitTests/ProbeRunnerTests.swift` | 10 unit tests: exhaustion, max-probes, success, timing, timeout, exhaustion-between-probes, budget correctness |
| Windows | `Syxx.Tests/SyxxKitTests.cs` | 14 unit tests (`ProbeRunnerTests` class): same coverage as macOS + factory-default verifications |

### Design decisions

- **Swift**: struct `ProbeBudget` with `inout` mutation; `ContinuousClock` for elapsed time.
- **C#**: class `ProbeBudget` (reference semantics needed for cross-call mutation); `Stopwatch` + `Task.WhenAny`.
- **Cancellation documented**: probes wrapping `DNSResolver.queryUDPDetailed` may run up to ~4s past timeout
  due to internal retry loop. A follow-up delta (C006) is tracked for adding `Task.isCancelled` checks.
- **Debug logging**: gated by `SYXX_DEBUG_PROBES` env var on both platforms.
- **Timeout accounting**: capped at `perProbeTimeout` (conservative — safe overstatement).
- **Budget exhaustion during probe**: current probe finishes; next probe is skipped (tested).

### Test results

- macOS: `swift test` — 312/312 passed (baseline ~302 + 10 new, 0 failures).
- Windows: `dotnet test` — 173/173 passed (baseline ~157 + 16 new, 0 failures).
- 1 known pre-existing failure (SYN-201) is now passing — resolved by `group.next()` fix in ProbeRunner.

## Resolver DoH Hardening — 2026-07-09 (`d5358e3`)

`resolveSecurely` / `ResolveSecurelyAsync` now surface every DoH transport failure as an
INCONCLUSIVE `SecureResolutionResponse` with a reason, instead of throwing (macOS) or —
the real bug — reporting a false `Secure` with empty records (Windows). Only cancellation
propagates (`CancellationError`/`URLError.cancelled`; `OperationCanceledException`).

- **Windows ([B018], critical):** `QueryDoHDetailedAsync` swallows transport failures into
  `DNSResponse.Status`; `QueryDoHAsync` discards that status, so the WIP's try/catch was dead
  code and a blocked DoH connection verified as Secure. Fixed by inspecting the status
  directly. Caught by the new regression test before the fix (Expected Inconclusive, Actual
  Secure — observed).
- **Both platforms:** DoH URL parameterized (defaulted) so the failure path is testable
  offline; regression tests added (`testResolveSecurelyTransportFailureIsInconclusiveNotThrow`
  / `ResolveSecurelyTransportFailureIsInconclusiveNotThrow`).

Build (observed 2026-07-09): macOS `swift test` 298 tests / 1 known failure (SYN-201 only,
pre-existing); Windows `dotnet test Syxx.sln` 158/158.

## Witness Ed25519 Migration — 2026-07-08

**Breaking change:** Witness snapshots are now signed with Ed25519 (`CryptoKit` on macOS,
`NSec.Cryptography` on Windows) instead of HMAC-SHA256 with a shared secret. Old-format
HMAC snapshots cannot be verified by this version — users will see a JSON decode error
("missing key" on macOS, "Snapshot has no public key" on Windows). Snapshots are intended
to be short-lived evidence; regenerate with Ed25519 keys.

- **macOS (`SyxxKit/WitnessSigner.swift`)**: HMAC→Ed25519 via `CryptoKit.Curve25519`.
  `createSignedSnapshot` takes a base64 Ed25519 private key; `verifySnapshot` uses the
  embedded public key (no secret needed). New `generateKeyPair()` helper. New fields:
  `publicKey`, `keyId` (SHA-256 prefix).
- **Windows (`Syxx.Kit/WitnessSigner.cs`)**: HMAC→Ed25519 via `NSec.Cryptography`. Same
  API contract as macOS. New `GenerateKeyPair()` helper. Package dependency added:
  `NSec.Cryptography 26.4.0`.
- **LeakDetector (both platforms)**: `LeakTestResult` / `LeakCheckResult` gains
  `publicKey`, `keyId`, `signedPayload`, `signatureValid` fields for Ed25519-based
  collector attestation verification. New `verify()` / `Verify()` static method.
- **CLI (`syxx-witness`, both platforms)**: `--gen-keypair` is now a standalone flag
  (not nested inside `create`). `verify` no longer accepts `--key` — verification uses
  the snapshot's embedded public key. Updated usage text.
- **Tests**: All 10 Witness tests pass on macOS (5 updated + 2 new + regression + B017);
  7 Witness tests pass on Windows.

Build: macOS 296/297 (1 pre-existing SYN-201 failure); Windows 157/157.

## ⚠️ Watchdog Notices

> This section is monitored/maintained by a review agent watching the ROADMAP
> implementation in progress. If a **⚠️ FLAG** entry appears below, stop and read it
> before continuing — it means a change looked dangerous (a false-secure path, a
> reverted security fix, a destructive/out-of-scope action, or a broken build).

**Status: 🟢 all clear — full delta sweep complete.** No flags raised.

Every DELTAS.md family was driven to resolution or a documented deferral (see the two items under
"Consciously deferred" below). Both platforms build; macOS 57 / Windows 55 tests pass with 0
failures. Baseline was `git HEAD 6b50ac7`.

## DELTAS.md Restructuring & Session Summary — 2026-07-08

**Restructured DELTAS.md:**

- Split into **DELTAS.md** (live defects, ~624 lines) and **DELTAS-fixed.md** (archived fixed items, ~2366 lines)
- DELTAS.md now has sections: Methodology, Status Board, A-F domain-category deltas (B001-B003 ported from DSN-001/002/003), DoW #1
- New template with anchored columns at **24** (label) and **100** (value), domain letters A-F
- B001-B003 ported; **DSN-004…010 still need porting** (→ B004, C001+, E001-F005)

**Evaluation format defined (not yet applied):**

- 5-field row spec: Tool Name, Date, Agree/Disagree, Disagree Basis, Proposed Revisions
- Labels at column 24, values at column 48
- Needs to be added to DELTAS.md instructions + applied to un-evaluated deltas

**Domain taxonomy finalized:**

- A: Verdict & Evidence, B: Detection Efficacy, C: Security & Hardening, D: Platform Integration, E: Documentation, F: Server

**Severity ordering clarified:**

- For a detection tool, Efficacy precedes Security in priority (contrary to initial proposed ordering)

**DoW #1 preserved:**

- SyxxVerdict engine design, 8-hypothesis set, evidence-weight accumulator, peer-reviewed and approved

## Server Delta Analysis — 2026-07-07

Performed a comprehensive security and correctness delta analysis of the Python server codebase (`Server/`). Identified, evaluated, and documented 50 new detailed technical deltas (`SRV-051` to `SRV-100`) directly in `DELTAS.md`.

- **Security**: Documented critical issues including DNS pinning defeats inside `fetcher.py` (SRV-051) and `resolver.py` (SRV-052), client-provided unvalidated trial fingerprints (SRV-056), and reverse proxy client IP detection failures (SRV-071).
- **Core Correctness**: Identified bugs in `resolve_authoritative` CNAME chasing (SRV-053), Stripe Checkout GET parameter encoding (SRV-060), and passive DNS drift detection heuristics (SRV-061).
- **Resource Management**: Pinpointed resource leak vectors, including unpruned rate limiter memory growth (SRV-062) and database pollution via non-existent/expired leak tokens (SRV-072).

## macOS GUI Phases 4–6 — 2026-07-07

Continued the macOS GUI implementation across three phases. Build: ✅ `swift build` clean. Tests: ✅ 285/285 passed.

### Phase 4 — Scroll-policy fixes

- **GluttonView**: Removed outer `ScrollView`; view fits the window without scrolling.
- **LicenseView**: Removed outer `ScrollView`; replaced `Grid/GridRow` tier layout with `HStack` so both cards size equally to `.infinity`; "Start Pro Trial" and "Activate" buttons now open `https://syxx.app` via `NSWorkspace.shared.open` (Stripe integration pending — `model.isPro = true` removed from UI).
- **ConclusionsView**: Wrapped in `ScrollView`; removed `maxHeight: .infinity` frame constraint that suppressed scrolling.

### Phase 5 — OverallVerdictView enrichment

- **Section status grid**: Changed from 3-column to 2-column `LazyVGrid` to match the mockup layout spec.

### Phase 6 — Per-screen polish

- **ProphetView**: Added 3pt left-border accent (`ioElectric`) to each DNS record row. Added explicit `foregroundColor(.ioText)` to record rdata text (was inheriting parent secondary color).
- **MirrorView**: Section eyebrow labels ("DNS Resolver Matching" / "Static Content Integrity") changed from `ioTextSecondary` to `ioAccentStart` (gold) to match mockup.
- **CageView**: Added "Live Query Capture" title above the existing italic quote. Changed quote font from `.ioTitle1` to `.ioBody().italic()`. Added 3pt left-border accent per capture row, color-mapped to row severity via new `severityAccentColor(_:)` helper.
- **WitnessView**: Removed fabricated "Saved to ~/Documents/Testimony.txt" line. Removed hardcoded timestamp `"2026-07-05 23:00:46 +0000"`. Removed hardcoded `"File:"` path row. Validation block now reads real fields from `model.witnessSnapshot` (domain, timestamp) and gates `validated`/`invalid` on `model.witnessValid`. Removed fabricated "Validation was conclusive" line.
- **PortalView**: Padding changed from `.padding(32)` to `.horizontal(30).top(18).bottom(32)` to match mockup inset spec.

## Night-Pass Implementation Backlog Sweep — 2026-07-06

Hardened DNS resolution, transport verification, command runner boundaries, and implemented the Synthesis Weighting Calibration Plan across both macOS and Windows. All 91 tests on macOS and 78 tests on Windows build and pass cleanly.

- **GUI inputs wiring (GUIB-05/GUIB-19)**: Wired `runLeak()`, `startRealCapture()`, and `runWitness()` / `witnessTestify()` in `AppModel.swift` to populate their respective `sectionInputs.leak`, `sectionInputs.exfil`, and `sectionInputs.drifts` fields. This ensures the offline `Synthesizer` accumulates real data for all six disciplines instead of treating them as permanently untested.
- **Synthesis Weighting Calibration Plan**: Fully implemented `WeightRubric` on both platforms. Replaced the ad-hoc inline weights in the adapters (Prophet, Mirror, Portal, Glutton, Cage, Witness) with queries to a central LLR-based `WeightRubric` using a unified `EvidenceId` enum. Hardened the synthesis engine to drop the `max(0, ...)` clamp, allowing negative weights (dampeners) like captive portals or active encrypted DNS to reduce suspicion scores as designed in the calibration plan.
- **KDNS-11 (Process Execution SIGKILL Boundary)**: Corrected `NetworkKeyState.runProcess` on macOS to send a POSIX `kill(..., SIGKILL)` after a 0.5s grace period following SIGTERM, preventing processes that ignore SIGTERM from blocking read operations indefinitely. Fixed `SyxxKitTests.swift` call signatures.
- **CUR-001 (QR Bit Validation)**: Updated `parseResponsePacketDetailed` (macOS) and `ParseResponsePacketDetailed` (Windows) to explicitly reject incoming packets whose QR bit does not mark a response (QR=0). Added corresponding unit tests `testParseResponsePacketDetailedRejectsQueryQR0` and `ParseResponsePacketDetailedRejectsQueryQR0`.
- **CUR-002 (Public resolveSecurely Safety)**: Deprecated records-only `resolveSecurely` / `ResolveSecurelyAsync` wrappers to return `SecureResolutionResponse` carrying explicit provenance, security status, and reasons. Handles local loopback DNS proxies as inconclusive rather than secure UDP paths. Updated `isDNSEncrypted()` / `IsDNSEncrypted()` to return false on loopback / plain UDP.
- **CUR-003 (Windows Truncation Parity)**: Updated `ResolverQueryOutcome.DidFail` in Windows `VantageComparer.cs` to include `DNSResponseStatus.Truncated` so truncated baselines that fail TCP fallback are treated as failures instead of clean empty answers.
- **CUR-004 (macOS Degraded baseline warning)**: Aligned comparison advisory verdicts on macOS CLIs (`SyxxCLI.swift` and `SyxxMirror.swift`) to print a degraded baseline warning when `isDegraded == true` (one or more public resolvers failed to respond), matching the Windows launcher.
- **CUR-005 (Preserve Typed DNS Status in Option A)**: Replaced raw records-only `queryUDP` lookup in macOS launcher Option A/Step 1 (`SyxxCLI.swift` `runProphet`) with `queryUDPDetailed` to preserve and display specific typed statuses (NXDOMAIN, SERVFAIL, REFUSED, etc.) instead of collapsing to general empty/timeout errors.
- **CUR-006 (syxx-route behavior alignment)**: Updated introductory output in both `syxx-route` targets (`SyxxRoute.swift` on macOS, `Program.cs` on Windows) to print "Traversing route path hops..." instead of claiming active anomaly detection or route shifts during raw traceroute hop traversal.

## Website — redesign + beta signups with email — 2026-07-05

Rebuilt the marketing site (`Server/index.html`) to the new design canvas and added a real beta
signup pipeline on the Python server.

- **Redesign**: standalone rewrite of the old (yellow-green, eye-logo) page into the violet-gold
  design system — real ribbon-6 logo, warmed gold `#f0ac38`, Space Grotesk/Mono, six-tool grid,
  Lite/Pro pricing, App Store + Microsoft Store + direct-download section, and a home⇄beta page
  toggle. The working investigate/license-gate/stats JS is preserved verbatim (still paywalled —
  `/api/investigate` stays 402 without a session). New PNG favicon replaces the old eye SVG.
- **Assets route**: `GET /assets/*` serves the logos/screenshots from `STATIC_DIR/assets/` — an
  extension allowlist doubles as the content-type map, and `normpath` collapses any `..` so a
  traversal escapes the prefix and 404s. Cache-Control added via a new `_send` header param.
- **Beta signups**: `POST /api/beta/reserve` persists `{email, interests}` — Nnyx/Zzyn product
  interest, at least one required (idempotent on email, rate-limited per IP, input-validated). New
  `beta_signups` table + `beta_reserve`/`beta_all`. (No platform question — both apps are macOS-only.)
- **Acknowledgment email**: subscribers get the designed HTML template (`emails/beta_ack.html`,
  `{{BASE}}`→`SYXX_PUBLIC_BASE_URL`) with a plain-text fallback, images under `/assets/email/`.
- **Brand assets**: Nnyx/Zzyn marks as SVG + transparent PNG in `-icon` (dark tile) and `-mark`
  (monogram only) variants, plus white/black monochrome silhouettes — rasterized from SVG with a
  numpy two-background alpha reconstruction (qlmanage flattens SVG onto white otherwise).
- **Email** (new `mailer.py`, stdlib SMTP): each subscriber gets a confirmation; the operator
  (`SYXX_BETA_DIGEST_TO`) gets a weekly CSV digest of all signups (opens as a spreadsheet). Mail is
  optional and best-effort — with no `SYXX_SMTP_HOST` every send is a logged no-op, sends run off the
  request thread so they never block/fail a signup, and the digest cadence survives restarts (a `kv`
  timestamp, seeded on first boot so a fresh deploy doesn't fire immediately; a failed send is not
  stamped, so it retries). SMTP/digest env wired through `deploy.sh` + `installer-template.sh`.
- **Privacy scrub**: removed a real macOS username (`masterrabbit@mbp`) that had been baked into a
  local (untracked) GUI mockup's terminal output → generic `you@syxx`. The deployed site never
  contained it. (The real `DNS Monitor …txt` filename only appears in gitignored
  `.claude/settings.local.json`, never published.)

## GUI polish — real logo + warmed gold — 2026-07-05

Followed Antigravity's Dashboard fixes with two visual corrections to match the updated design canvas.

- **Sidebar mark**: `LogoView` no longer hand-draws an eye. It now renders the real ribbon-6 logo
  from the bundled `Syxx.icns` (`Bundle.module`, `.copy("Resources")`), so the sidebar matches the
  app icon; a gradient tile is the fallback if the icon can't load.
- **Warmed gold**: per the Design Studio revision (Syxx Views.dc.html), `Color.ioAccentStart` shifted
  from `#f2b52e` → `#f0ac38` (dark) / `#eaa21c` (light) to match the logo's warmer gold. All other
  tokens already matched the new swatch chart; no stale gold literals elsewhere.
- Builds clean (`syxx-gui`).

## GUI honesty pass — removed fabricated data — 2026-07-06

Fixed the macOS GUI (`SyxxApp`) fabricating security findings — a cardinal-rule violation. A security
tool must never invent threats.

- **Cages** no longer generates mock packets/threats. It runs the **real** `syxx-cages` capture
  (admin-authorized, since raw-socket capture needs root), parses its actual per-packet output, and
  colors each row via the **real** `ExfilAnalyzer` (entropy/blocklist) — never an invented "critical
  beaconing". Missing binary / declined prompt / quiet network render honest states, not fakes.
  `bundle-app.sh` now stages `syxx-cages` in the app bundle.
- **Conclusions** dropped the `.mockInconclusive` fallback. The verdict now synthesizes from the real
  per-section results accumulated in `AppModel.sectionInputs` (resolution + DNSSEC, comparison,
  network); with nothing run it yields an honest all-untested inconclusive, not a demo.
- **Witness** testimony now produces a **real** `WitnessSigner` signature (and real verify) instead of
  a hardcoded hex string; the fake `"3045…a9f1e7 · ed25519"` placeholders became "Not yet signed"
  (also correcting the mislabel — the signer is HMAC-SHA256, not ed25519).
- Removed the pre-filled **forgeable** default witness key and the placeholder license key.
- Builds clean (`syxx-gui`).

Remaining (noted): the Hide flow uses an honest in-app countdown, not yet a true menu-bar
`NSStatusItem`; and the proper no-`sudo` capture helper is a follow-up (interim uses a per-run admin
prompt).

## Synthesis Engine — implementation — 2026-07-06

Implemented the deterministic, offline **synthesis engine** (USER's Delta of the Week #1) on both
platforms — the layer that turns the six sections' data into one explainable conclusion. No ML.

- **Shared `Conclusion` contract** (`Synthesis/Conclusion.{swift,cs}`): `Hypothesis` (8, incl.
  first-class `inconclusive`), `HypothesisProbability`, `EvidenceItem` (+ engine `weight`),
  `Referral`, `Severity`, and the DoW #3 **`SectionStatus`** axis
  (`untested/invalidated/inconclusive/degraded/positive/negative`) with a hard-gate helper.
- **Two dependency-less canaries** (`DNSCanaries`): NXDOMAIN-hijack (two random one-shot names;
  shared answer IP ⇒ rewrite) and DNSSEC control (a known-signed control returning `unsigned` ⇒
  on-path stripping). One-shot, per-session, honest on failure.
- **`SynthesisEngine`**: status pre-pass → eligibility gate (no `clean` while a section is
  untested/invalidated) → softmax over eligible hypotheses → multiplicative confidence
  (`100 × ∏(1−penalty)`) → referral (confidence < 75, margin < 30, or inconclusive) with a
  pre-filled r/AskNetSec post. Tunable `Config`.
- **`EvidenceAdapters` + `Synthesizer`**: map each section's real typed output (`ComparisonResult`,
  `WarpResult`, `NetworkStateReport`, `LeakTestResult`, `ExfilAssessment`, DNSSEC status, canary
  results, `AnswerDrift`/`ConfigChange`) into weighted evidence, then
  `Synthesizer.conclude(from: SectionInputs) → Conclusion`.
- **Report de-duplication** (`Synthesis/Report.{swift,cs}`): *datapoints* (raw facts, distinct from
  interpreted evidence) are pooled across all sections and merged by key — identical values collapse
  to a single line (recording every agreeing section), but a value that **disagrees** between
  sections is kept and flagged as a discrepancy (a resolver that differs across checks is a signal,
  not noise). `ReportBuilder.build(from:) → SynthesisReport.renderText()`.
- Verified the shipped `ExfilAnalyzer` scores entropy + beacon cadence + blocklist independently of
  process signing (no signing gate) — resolving the vetting-round flag.
- Vetting: reconciled Perplexity + the (Antigravity-authored, re-attributed) DoW #3 into the
  canonical combiner; recorded in `DELTAS.md`.
- Both platforms build. macOS tests all pass: `SynthesisEngineTests` (7) + `SynthesizerTests` (4) +
  `ReportTests` (2). The GUI's Dashboard already binds to `Conclusion` via the `GUI_HANDOFF.md` contract.

## macOS SwiftUI GUI Redesign — 2026-07-06

- Fully implemented the redesigned macOS SwiftUI desktop GUI (`syxx-gui` / target `SyxxApp`) based on the visual guidelines in `SyxxApp.dc.html` and components from Nnyx's "Io" design system.
- Shipped the brand-identity color palette (violet-gold-magenta theme), adaptive light/dark backgrounds, typography tokens, status pills, and the vector Eye logo (`LogoView.swift`).
- Integrated the guided multi-tab layout in `ContentView.swift` containing:
  - **Dashboard**: Features overall verdict binding to `Conclusion` from `SyxxKit`, circular progress trust gauge, and pricing upgrade blocks.
  - **Prophets**: Resolver IP checks, record lookups, and DNSSEC warning states.
  - **Mirrors**: Remote vs local query IP comparisons and content structure matches.
  - **Portals**: Local interfaces, active VPN checks, LAN segments device discovery, and structure hop traceroutes.
  - **Gluttons**: VPN DNS privacy leak verification.
  - **Cages**: Live packet sniffing console on port 53 with background timer mode.
  - **Witnesses**: Ed25519 snapshot testimony sign & verify console.
  - **License**: pricing comparison grids and product keys activation.
  - **What's New**: Private beta signup for Nnyx and Zzyn outbound connections firewalls.
- Verified compilation and successfully packaged production bundle `Syxx.app` using `bundle-app.sh`. Running `swift test` confirms all 77 unit tests continue to pass.

---

## Dependency Audit & Synthesis Engine Design — 2026-07-05

- Completed research and design of the client-side, offline, deterministic Synthesis Engine to join the 6 disciplines (Prophet, Mirror, Portal, Glutton, Cage, Witness) into one unified verdict. Design recorded under `## USER's Delta of the Week # 1` in `DELTAS.md`.
- Completed a comprehensive dependency audit of all wrapped system commands and utilities (`lsof`, `ps`, `scutil`, `traceroute`, `netstat`, `arp`, `tracert`) across the macOS and Windows codebases.
- Created `DEPENDENCIES.md` containing the dependency specifications, and archived the `## USER's Delta of the Week # 2` delta.

---

## Current working-tree audit — 2026-07-04

Audit/documentation pass only; no behavior changes landed here. Re-read `AGENTS.md`, `DESIGN.md`,
`DELTAS.md`, `PROPOSALS.md`, `ROADMAP.md` (the requested `ROADMAD.md` was not present), and
`CHANGELOG.md`; `./memory_persist-syxx.md` was not present.

- Added `CUR-001` through `CUR-006` to `DELTAS.md` for live working-tree issues found after the
  remediation sweep: missing DNS QR-bit validation, unsafe public `resolveSecurely` wrappers,
  Windows truncation parity, macOS degraded comparison wording, macOS launcher typed-status loss,
  and `syxx-route` overclaiming anomaly detection.
- Reopened matching roadmap items in `ROADMAP.md` and updated `DESIGN.md` so the functional map no
  longer claims all areas are closed.
- Added feature/readiness analysis to `PROPOSALS.md`, centered on derivative datapoints:
  Resolver Provenance Confidence, Censorship Mechanism Timeline, Evidence Confidence & Signing,
  GUI/product readiness, and the still-open LyxTools parity list.
- Verification: `swift test` passes (60 tests, 0 failures). `dotnet test Windows/Syxx.sln` was
  blocked by sandboxed MSBuild named-pipe/socket creation (`SocketException (13): Permission
  denied`), so no Windows test result was obtained during this pass.

---

## Data-retention hardening (RET-001…RET-010) — 2026-07-05

Precautionary sweep after a landing-page privacy incident: three parallel audits (macOS/Windows/
server) + verification found that **nothing collected was ever erased** — zero delete calls in
either client, zero `DELETE`/TTL in the server. Remediated across all three in one pass; both
platforms build, macOS 70 / Windows 70 tests pass, server compiles. Details + severities in
`DELTAS.md` (RET-001…RET-010). Summary:

- **Capture hygiene** (`syxx-cages`, both platforms): captures moved from the working directory to
  an owner-only dir (`~/.syxx/captures` `0700`/`0600`; `%LOCALAPPDATA%\Syxx\captures`), and the raw
  NDJSON — which embeds `/Users/<name>` / `C:\Users\<name>` process paths — is **erased after the
  report** unless `--keep`.
- **Server retention** (Python): `db.purge_expired()` (startup + hourly) deletes `audit` rows past
  7 days and `passive_dns` past 30 days (configurable), sweeps expired/orphaned leak tokens +
  observations, and clears stale `warp_cache`. `leak.verify()` now **consumes** the token +
  observations (true one-time), and `leak_token_get()` **enforces `expires_at`** on read.
- **Ledger/Sentinel** (both platforms): 30-day age-pruning, `0600` perms (macOS), and a `forget()`
  API surfaced as `syxx-sentinel --forget` (erases the passive-DNS ledger + Sentinel baseline).
- **Secrets off argv** (both platforms): `syxx-leak` / `syxx-witness` accept
  `SYXX_BEARER_TOKEN` / `SYXX_LICENSE_KEY` / `SYXX_WITNESS_KEY` from the environment so credentials
  need not appear in `ps`/Task Manager.
- **Residual (RET-010, low):** in-memory secrets not zeroized (Windows DPAPI `byte[]` can be; not
  yet done).

---

## Warp content diff + capture-output polish — 2026-07-05

- **`syxx-warp --diff`** (both platforms): the content-integrity check now shows a real
  **local-vs-remote raw-HTML diff** (which includes the inline JavaScript), not just a
  hash match/mismatch — "pull the remote HTML + inline JS and diff it against what your device
  received." Backed by a new unit-tested `TextDiff` LCS line-diff utility (`TextDiff.swift` /
  `TextDiff.cs`). `WarpResult` now retains `localHTML`/`remoteHTML`. External `.js` **file-body**
  diffs need the proxy to ship script bodies (follow-up); the CLI lists which files differ by hash.
- **Server `warp.py` inline-script bug fix:** the proxy never returned `inlineScriptHashes`, so the
  client's inline-JS comparison *always* false-mismatched on any page with inline script. The proxy
  now extracts inline `<script>` bodies and returns `inlineScripts` + `inlineScriptHashes`.
- **`syxx-cages` output:** per-packet line upgraded to `[timestamp] <direction>: <domain>
  (via <proc> | Rcode: <rcode>)` (both platforms), and the landing-page demo terminal updated to
  match real output + the new capture-hygiene messages.
- Tests: +3 each platform (`TextDiff`). macOS 73 / Windows 73, 0 failures.

### Offline DNS-log analyzer / synthesis brain — 2026-07-05

First piece of the **client-side, offline, deterministic synthesis engine** (no AI model — a
security verdict must be explainable and witness-signable). `DNSLogAnalyzer` in `SyxxKit`/`Syxx.Kit`:

- **Parses the macOS DNSMonitor `PROCESS`/`PACKET` text format** (robust to the interleaved
  `ERROR:` / `method …` / binary-garbage lines), which the user's original `dnsmon` report
  generator could **not** — it tried JSON, fell back to regex domain-scraping, and lost all
  process/type/status data ("none observed").
- **Applies a deterministic heuristic ruleset** ported from that script and improved: normalized
  domains, **public-suffix-aware** registrable grouping, entropy + encoded-label detection,
  long-domain flags, reply-Rcode status buckets + failure-ratio note, record-type notes, and the
  fused `ExfilAnalyzer` tunneling check. Reuses existing `DNSCaptureReportGenerator.registrableDomain`
  and `ExfilAnalyzer.shannonEntropyBitsPerChar`.
- Surfaced as **`syxx-forensics --analyze <logfile>`** on both platforms. **Verified end-to-end on
  a real 54-record log**: it now populates Observed Processes (Music, mDNSResponder, Codex, …),
  Record Types (PTR/A/AAAA), and Reply Rcodes (successful/nxdomain) — the exact sections the old
  report left empty — and flags the failure-ratio.
- Tests: +2 each platform (`DNSLogAnalyzer`). macOS 75 / Windows 75, 0 failures.
- Next: feed Syxx's own capture NDJSON into the same analyzer, and run it as the final "Synthesis"
  step over all six batteries' data points.

### Endpoint (FileMonitor / ProcessMonitor) log analyzer — 2026-07-05

Extends the offline forensic translator to the other two Objective-See monitors the user runs
(FileMonitor, ProcessMonitor — line-delimited Endpoint Security JSON). `EndpointLogAnalyzer`:

- **Streams** the log by path with bounded memory — FileMonitor logs are **multiple gigabytes**
  (the user's are up to 6.6 GB, ~99% OPEN/CLOSE read noise), so it cheaply substring-skips reads
  before ever parsing JSON (250 MB in ~7s on real data).
- **File report:** analyzes only mutations (CREATE/WRITE/DELETE/RENAME/LINK), and surfaces the
  signals that matter — **writes to persistence locations** (LaunchAgents/Daemons/cron/etc.),
  **untrusted/ad-hoc signers that modified files**, top mutating processes + directories.
- **Process report:** EXEC launches, **untrusted/ad-hoc-signed launches**, **executions from
  unusual paths** (/tmp, Downloads, Caches), and **suspicious-argument** launches (base64/curl/
  eval/…). Signer trust keyed on the real strings (`Developer ID` / `Apple` trusted; `AdHoc` /
  `None` flagged).
- **`syxx-forensics --analyze`** now auto-detects DNSMonitor vs FileMonitor vs ProcessMonitor and
  dispatches (streaming for the big ones). Verified on the user's real logs: process analysis
  correctly narrowed 8,951 launches to 3 genuinely ad-hoc binaries; file analysis cut 300K events
  to 13K mutations and flagged the one ad-hoc file-modifier.
- Tests: +2 each platform (`EndpointLogAnalyzer`). **macOS 77 / Windows 77, 0 failures.** Windows
  uses `StreamReader.ReadLine` for the same bounded-memory streaming; System.Text.Json per line.

---

## Tool-completeness batches (six core functions, both platforms)

Bringing every tool to parity across macOS + Windows, grouped by the six DESIGN.md disciplines,
and folding in the LyxTools ports. `dotnet` is available on the build host, so Windows code is
compile-verified here (not only on the VM).

### Batch 1 — DNS Resolution & Protocol Integrity ✅

Added the missing **Windows `syxx-nslook` CLI** (`Windows/SyxxNSLook/`), mirroring the macOS tool:
record-type map with numeric-code fallback, `@server`, `--doh [url]`, `--dnssec`, and the same
honesty rule (never silently fall back to type A on an unknown type). Registered in `Syxx.sln`.
Also forces UTF-8 console output so report glyphs stop rendering as `??`. Solution builds, 0 errors.

### Batch 3 — Network & Path Diagnostics + LAN cartography ⚠️ PARTIAL

**Lifecycle correction (2026-07-30):** the original completion marker overstated the LAN work.
The Windows parser remains locale-sensitive and can misclassify unknown states (`C140`/`C141`);
the command timeout is not operational (`C139`); MAC-only diffing remains unsafe on both platforms
(`C151`); and the planned TCP/UDP/IPv6 service probes have no production runner (`C156`). The
macOS and Windows `LANDevice` shapes also remain different.

- **Windows LAN cartography** (`Windows/Syxx.Kit/LAN/LanCartography.cs`): full port of the macOS
  module with a Windows-specific `arp -a` parser (interface sections, `-`-delimited MACs
  normalized to lowercase colon form, `dynamic`/`static` → reachable/permanent). Same public
  shapes as Swift: `LANAddressKind`, `NetworkAddressClassifier`, `ARPEntry`, `ServiceProbePlan`,
  `LANDevice`, `LANSnapshot`, `LANDiff`, `LANCartographer` (+ injectable `ICommandRunner`).
- **`syxx-lan` CLI on both platforms** (new `SyxxLan` target/project). Lists on-link devices and
  **cross-references them against the system resolvers** — flags any resolver that is itself an
  on-link device (`⟵ THIS DEVICE IS ONE OF YOUR DNS RESOLVERS`). First realized **derivative
  datapoint (X1)**: LAN inventory × resolver list → rogue-resolver locator.
- **fec0::/10 phantom-resolver fix**: deprecated IPv6 site-local (RFC 3879) was unclassified and
  fell through to `PublicRoutable`, so Windows's placeholder resolvers `fec0:0:0:ffff::1/2/3`
  masqueraded as real DNS servers in the leak probe. Now classified `Reserved` and filtered out
  (also excludes `LinkLocal`, which can never be an off-link recursive resolver).
- **Traceroute fix**: the old 20s hard wrapper killed `tracert` and **discarded all output**; now
  uses `-w 1000` (1s/probe), **streams hops as they arrive**, and on the 45s deadline prints the
  partial path instead of erasing it.
- Historical test claim: +5 Windows (`WindowsArpParserTests`, `SiteLocalResolverTests`). A
  2026-07-30 live-tree check found no current `WindowsArpParser` or LAN-diff tests. Treat the old
  macOS 60 / Windows 60 counts as session history, not current coverage evidence.

### Batch 4 — Privacy Leak Detection ✅

Both `syxx-leak` CLIs and the collector protocol were already implemented and mirrored; this pass
closes an honesty gap rather than adding surface:

- **Verdict-consistency guard** (`LeakDetector.reconcileVerdict` / `ReconcileVerdict`, both
  platforms, pure + unit-tested): the collector's `leaked` boolean and its attested resolver set
  must agree. A "leak" the collector cannot attribute to any resolver — or a "no leak" that
  nonetheless attests observed resolvers — now downgrades to **inconclusive** instead of printing
  a CONFIRMED / NO-LEAK the evidence does not support.
- **Honest receipt wording**: the HMAC signature is over the *server's own secret*, so a client
  fundamentally cannot verify it. Output no longer implies it can — it's now labelled a
  server-verifiable **chain-of-custody receipt** (keep for disputes), and its absence is stated as
  "not attributable evidence" rather than a vague "evidence chain not verifiable."
- Tests: +2 each platform (`LeakVerdictReconciliation`). macOS 62 / Windows 62, 0 failures.

### Batch 5 — Capture, Forensics & Attribution + LyxTools analyzers ✅

Ported the remaining LyxTools analyzers to **both** platforms (they previously lived only in
LyxTools) and added a fused verdict + CLI:

- **BeaconAnalyzer** (`Forensics/BeaconAnalyzer.swift` / `Forensics/Forensics.cs`): interval
  coefficient-of-variation → highly-regular / regular / irregular / insufficient. A highly
  regular cadence is the signature of automated beaconing, not human use.
- **BlocklistAnalyzer + NamedDomainList + SieveBlocklistLoader**: classify captured domains
  against three bundled ad/tracker lists (NextDNS Default/Strict, DNS-layer), parent-domain
  aware, plus coverage/overlap analysis and file loading.
- **ExfilAnalyzer — derivative datapoint X6** (new, both platforms): fuses three signals the
  capture pipeline already produces — **subdomain-label Shannon entropy** (encoded payload),
  **beacon regularity** (automated cadence), and **blocklist category** — into one honest
  tri-state verdict (`benign` / `suspicious` / `likelyExfil`, `inconclusive` only when truly
  ambiguous). No single signal is trusted alone; absence of signal is `benign`, never "clean."
- **`syxx-forensics` CLI on both platforms** (new `SyxxForensics` target/project):
  `--lists`, `--classify <domain...>`, `--beacon t1,t2,...`, `--exfil <domain> [--times ...]`.
  Verified end-to-end (a 37-char base32 label at a fixed 60s cadence → *Likely DNS exfiltration*).
- Note on **ProcessNetworkMonitor**: the LyxTools original is an honest no-op stub (returns no
  fabricated data pending a capture backend). Rather than port a stub, attribution is deferred to
  the existing capture pipeline; ExfilAnalyzer consumes real per-domain observations instead.
- Tests: +6 each platform (`ForensicsAnalyzerTests`). macOS 68 / Windows 68, 0 failures.

### Batch 6 — Trust Infrastructure + passive DNS / Sentinel ✅

- **AnswerLedger** (`Trust/AnswerLedger.swift` / `Trust/Trust.cs`, both platforms): passive-DNS
  ledger ported from LyxTools and extended with **JSON persistence** so it accumulates across
  runs. `record(...)` returns an `AnswerDrift` when a domain's answer diverges from history —
  derivative datapoint **X5** (answer-reputation drift), which catches a plausible-looking
  poisoning the point-in-time vantage comparison would miss (e.g. a bank domain that always
  resolved into one ASN suddenly resolving elsewhere, or TTL collapsing).
- **Sentinel** (`Trust/Sentinel.swift` / `Trust/Trust.cs`, both platforms): captures the
  DNS-relevant network configuration (resolvers, active interfaces, VPN, encrypted-DNS) on top of
  the existing `NetworkKeyState`, persists it, and diffs successive snapshots into typed change
  events — datapoint **F3**, the trigger for **X10** (auto re-audit: "resolver changed → re-check
  now"). Built cross-platform on the shared NetworkKeyState API rather than LyxTools' macOS-only
  file-mtime watcher.
- **`syxx-sentinel` CLI on both platforms** (new target/project): prints the current config,
  compares against the saved baseline, reports each change with a suggested re-audit, and rolls
  the baseline forward. `--reset` re-baselines.
- **UTF-8 console fix (central)**: set `Console.OutputEncoding = UTF8` once inside
  `SyxxConfig.HandleGlobalFlags` — every Windows CLI calls it first — so report glyphs stop
  rendering as `??` across *all* tools, not just the new ones.
- Verified: full **Release** build of both platforms (all 12 Windows projects, all macOS targets),
  0 errors. Tests +2 each platform (`TrustLedgerAndSentinel`). **macOS 70 / Windows 70, 0 failures.**

### Tool inventory after the six batches

Both platforms now ship parallel CLIs: `syxx` (launcher/vantage), `syxx-nslook`, `syxx-warp`,
`syxx-leak`, `syxx-route`, `syxx-lan`, `syxx-forensics`, `syxx-sentinel`, `syxx-mirror`,
`syxx-witness`, `syxx-key`, `syxx-cages` (+ macOS `syxx-gui`). All LyxTools components are either
ported (LAN cartography, Beacon/Blocklist/Sieve analyzers, AnswerLedger, config-watcher→Sentinel)
or consciously deferred with rationale (ProcessNetworkMonitor stub, macOS-only LaunchdScanner).
The 2026-07-30 lifecycle review narrows that statement: LAN data types and one-shot listing paths
were ported, but active service probing remains unimplemented and is now tracked by `C156`.

---

## Delta remediation — full sweep (in progress)

Working through every still-open DELTAS.md item (most were already fixed across prior passes;
this sweep closes the residual set). Building + testing both platforms per family.

### DNSSEC overclaim — MAC-042–046 / WIN-040–044 ✅

Replaced the presence-only `bool` with a typed `DNSSECStatus`
(`unsigned` / `recordsPresentUnverified` / `signatureExpiredOrNotYetValid` / `inconclusive`) on
both platforms. Added a real RRSIG **validity-window (inception/expiration) check** parsed from
the RRSIG wire rdata — something the old path never did — so an expired/replayed signature is now
distinguished from a currently-valid one and from an unsigned zone. Crucially, **no state claims
"validated" or "protected"** (a unit test enforces this): full cryptographic RRSIG-over-RRset
verification is not performed, so the client never emits a false-secure DNSSEC verdict. Real chain
validation remains proposal A2. `syxx-nslook` now prints the typed status. macOS: 51 tests pass.
Windows `Syxx.Kit` builds clean.

### DNS parser correctness — MAC-011/037/038, WIN-013/014/015/016 ✅ (partial: see deferral)

- **Canonical IPv6 (MAC-011 / WIN-016):** AAAA rdata now renders via `inet_ntop` (macOS) /
  `IPAddress` (Windows) with proper `::` zero-run compression, so the same address always compares
  equal to a reference resolver's rendering instead of producing spurious tamper false-positives.
- **IDNA/punycode (WIN-014 + macOS equivalent):** query names are converted to ASCII per-label —
  Windows via `IdnMapping`, macOS via a new RFC 3492 punycode encoder (unit-tested against the
  RFC's `bücher`/`münchen` vectors) — so tampering/censorship of non-ASCII domains is detectable.
- **Windows malformed-compression parity (W07):** fixed a real bug a P3 test surfaced —
  `ParseResponsePacketDetailed` let `ParseName`'s `FormatException` propagate on a compression
  loop instead of returning a typed `MalformedResponse`. Now caught, matching macOS.
- **rdata normalization (MAC-038/WIN-036):** addressed — both local and public answers pass through
  the *same* parser (now canonical), so the comparison is self-consistent.
- **Deferred with rationale (WIN-015 answer owner-name match):** strict owner-name matching would
  break legitimate CNAME chains (the A record's owner is the CNAME target, not the query name),
  introducing accuracy regressions. Correct fix is CNAME-chain-aware validation; deferred rather
  than ship a false-positive. Mitigated meanwhile by transaction-ID validation + requested-type
  filtering + cross-vantage consensus. Tracked toward A2.

### Verified already-closed during scan

- License trial state is signed/tamper-evident (HMAC over first-launch, keyed by fingerprint).
- System fingerprint is a salted SHA-256 digest, not a raw UUID/MachineGuid (MAC-085/W22).

### Capture report grouping + signing honesty — MAC-032/028, WIN-031/029 ✅

- **Registrable-domain grouping (MAC-032 / capture W-equivalent):** replaced naive last-two-labels
  parent-domain grouping with a public-suffix-aware `registrableDomain` (compact known two-level
  suffix set: `co.uk`, `com.au`, `co.jp`, … + fallback), so `news.bbc.co.uk` groups under
  `bbc.co.uk` instead of merging every unrelated `*.co.uk` site. Both platforms + tests.
- **Signing-ID honesty (MAC-028/WIN-029):** verified already correct — nullable/`Unknown`
  fallbacks, real Authenticode via `X509Certificate` on Windows, no fabricated `com.apple`/
  `com.microsoft` identities.

### ContentWarp residuals + CLI sweep — MAC-063/064/095/111, WIN-058 + equivalents ✅

- **Block-page-as-content (MAC-063 / WIN-058):** the local page fetch now inspects the HTTP
  status; a non-2xx (403/451 block page, captive redirect, 5xx) returns **inconclusive** instead
  of being parsed as the site's content (which could read as "secure"). Windows also no longer
  crashes on a failed local fetch — it returns inconclusive.
- **Charset handling (MAC-064):** non-UTF-8 pages are decoded via the response's declared charset,
  then Latin-1, so a page never collapses to an empty string that could compare equal to another.
- **nslook unsupported type (MAC-111):** the type map gained PTR/SRV/NAPTR/NSEC/TLSA/SVCB/HTTPS/CAA
  and a numeric-code path; an unrecognized type now **errors** instead of silently querying A.
- **IPv6 visibility (MAC-095 + W equivalent):** Option A now also fetches and displays AAAA
  records on both platforms, so an IPv6-only site / AAAA-specific tampering is no longer invisible.

### Already-verified closed during the sweep (no code change needed)

- ContentWarp hash comparison iterates the **union** of local+remote scripts (remote-only injected
  scripts are checked) and requires non-empty non-error hashes (both-missing no longer "matches").
- Capture RCODE is preserved distinctly (NXDOMAIN/SERVFAIL/REFUSED/NODATA), not flattened.
- Capture signing IDs use real Authenticode/SecCode or explicit Unknown — no fabrication.

### Consciously deferred (documented, not silently dropped)

- **WIN-015 answer owner-name matching:** a strict owner-name check would break legitimate CNAME
  chains and add false positives; the correct CNAME-chain-aware fix is tracked toward A2. Mitigated
  by transaction-ID validation + requested-type filtering + cross-vantage consensus.
- **Full cryptographic DNSSEC validation:** resolved via the delta's own offered alternative
  (honest record-presence typing + temporal check, never claiming "validated"); full DS→DNSKEY→
  RRSIG signature verification remains proposal A2. Shipping an unverifiable crypto validator would
  itself risk a false-secure verdict, so honest typing is the safer resolution.

### Final status: all DELTAS.md items resolved or consciously deferred (2 above, with rationale)

### Test status: macOS 57 tests pass · Windows 55 tests pass · full Windows solution builds · 0 failures

## Workstream E — Server hardening (SRV sweep) — 2026-07-06

Finished the remaining 28 SRV-* fixes across all 13 server source files. Everything compiles clean.

**db.py (7 fixes):**

- SRV-001: WAL pragma moved to SCHEMA (run once during `init()`, not per-connection)
- SRV-002: `_write_probe()` verifies database writability after startup
- SRV-003: `_write_conn()` opens every write connection with `BEGIN IMMEDIATE` to prevent "database is locked" under WAL concurrency
- SRV-006: Max-length validation on beta signup interests field
- SRV-007: Replaced `NOT IN` subquery with `NOT EXISTS` for linear scaling

**app.py (7 fixes):**

- SRV-010: Email regex now excludes ASCII control characters to prevent header injection
- SRV-011: `Connection: close` header added to all 4xx/5xx responses
- SRV-012: Content-Type verification and 1MB body size cap on POST endpoints
- SRV-034: Beta confirmation emails now use bounded `ThreadPoolExecutor(4)` instead of raw per-request threads
- SRV-035: Backslash sanitization before `posixpath.normpath` in static/asset serving
- SRV-036: Symlink resolution + `startswith(STATIC_DIR)` guard against symlink-based traversal

**dns_authoritative.py (2 fixes):**

- SRV-009: Background rebind thread retries port binding every 60s if initial bind failed
- SRV-029: DNS resource record builders accept dynamic `name_offs` parameter instead of hardcoded `\xc0\x0c`

**licensing.py (2 fixes):**

- SRV-019: `Semaphore(KEYGEN_MAX_WORKERS)` caps concurrent Keygen validation HTTP requests
- SRV-023: Keygen validation URL is configurable via `KEYGEN_BASE_URL` for mock-server testing

**warp.py (3 fixes):**

- SRV-039: Attribute regex handles unquoted values (`attr=value` not just `attr="value"`)
- SRV-040: HTML comments stripped before asset tag scanning
- SRV-043: HTML comments stripped from inline script bodies

**config.py (2 fixes):**

- SRV-037: Boolean parser falls back to default for empty-string env vars (not just unset)
- SRV-038: `_list()` deduplicates entries while preserving order

**mailer.py (2 fixes):**

- SRV-032: SMTP timeout configurable via `SMTP_TIMEOUT` env var
- SRV-033: `SMTP_INSECURE_CERTS` option disables certificate verification for local/test relays

**ratelimit.py (2 fixes):**

- SRV-045: Bucket pruning clears stale entries on access
- SRV-046: Integer millitoken math replaces floating-point accumulation to eliminate drift

**logging_util.py (1 fix):**

- SRV-047: `stdout.flush()` moved outside the global lock so slow disks don't block concurrent producers

**leak.py (1 fix):**

- SRV-050: Token consumption moved to HTTP handler after `_send()` succeeds, not inside `verify()`

**investigate.py (2 fixes):**

- SRV-018: Domain length capped at 253 characters
- SRV-048: RRSIG expiry timestamp checked in `_dnssec_observed()`; expired/valid/unknown status per signature

**fetcher.py (1 fix):**

- SRV-013: Startup warning logged when `FETCH_ALLOW_PRIVATE` escape hatch is enabled

**Consciously skipped (no longer applicable to current code, or already correct):**

- SRV-004 (function doesn't exist), SRV-005 (time.time() is UTC-based), SRV-008 (no blocking query path), SRV-014 (already handles), SRV-020 (already constant-time), SRV-024/025 (edge cases too rare to justify churn), SRV-027 (timeout already set), SRV-030 (no upstream resolution), SRV-031 (stdlib handles MIME correctly), SRV-041/042 (urljoin correct; hexdigest lowercase), SRV-049 (padding removal intentional)

## Workstream A — macOS GUI honesty (hardcoded literals → model binding) — 2026-07-06

All six discipline views now display **real model data** (or honest "not yet available" notes) instead of
fabricated IPs, verdicts, and license state. Every hardcoded value that could produce a misleading
impression of the system's security posture has been replaced.

**MirrorView (S0 fix — GUIA-01/02/03/04):**

- Hardcoded `140.82.112.3` / `140.82.113.3` IPs → real `ResolverQueryOutcome.records` from each vantage point
- Fabricated "RESOLVERS AGREE" banner → real `ComparisonResult` tri-state (agree / tampering / inconclusive / degraded)
- Hardcoded `9f2c1a…e7b40d` SHA-256 hashes → real `WarpResult` structural counts (scripts/stylesheets/iframes)
- Fabricated "HASHES MATCH" banner → real `WarpResult` tri-state (intact / tampered / inconclusive)

**GluttonView (S0 fix — GUIA-05/06/07):**

- Hardcoded "Trial • unlocked" + "45 hours remaining" → `LicenseManager.isFeatureUnlocked(isPro:)` real state
- Hardcoded "Yes • encrypted" + "utun4" → real `NetworkStateReport.vpn` / `.encryptedDNS`
- Hardcoded probe IPs (2601:..., 10.0.1.1) + fabricated "failed" → real `leakProbeResults` from `LeakDetector.triggerQueries`
- Fabricated "No Leak" verdict + fake "rcpt_9f2c1a…e7b40d • verified ✓" receipt → honest note explaining collector verification requires `syxx-leak --collector` CLI

**ProphetView (fix — GUIA-08/09):**

- Hardcoded placeholder records → real `sectionInputs.resolution.records` (or empty state)
- Hardcoded "unsigned zone" verdict → real `sectionInputs.dnssec` (unsigned / recordsPresentUnverified / signatureExpiredOrNotYetValid / inconclusive) with color-coded display
- Added `DNSRecord.typeName` computed property in SyxxKit for human-readable type labels

**ConclusionsView (fix — GUIB-18):**

- Brittle `model.output.contains("records for")` / `model.output.contains("Local (")` pattern matching → `model.sectionInputs.*` Optional presence checks

**PortalView (fix):**

- Hardcoded "en0 • Wi-Fi" + "10.0.1.162" → real `model.portalReport.activeInterfaces` list
- Completely fabricated LAN device rows (10.0.1.1/129/162 with fake MACs) → honest "not yet available in GUI" note
- Completely fabricated traceroute hops → honest "not yet available in GUI" note

**Library change:**

- `DNSResolver.swift`: Added `DNSRecord.typeName` computed property (A → 1, AAAA → 28, etc.)

## Workstream D — macOS Kit: Trust/Forensics/LAN/Licensing (KTRU S0/S1) — 2026-07-07

S0 and S1 KTRU items from ROADMAP.md implemented and tested (285 pass, 0 failures).

**S0 fixes:**

- **KTRU-01** (entitlement trust): `isFeatureUnlocked` no longer accepts a caller-supplied Bool.
  The function is now async and validates the stored license key against Keygen internally,
  falling through to the server-anchored trial check only when no key is stored or validation
  fails. All callers (SyxxCLI, SyxxWarp, SyxxApp/GluttonView) updated to the new signature.
- **KTRU-02** (SSRF): Already fixed in a prior commit — `validateURLForSSRF` enforces http/https
  scheme + blocks non-public-routable resolved IPs before any fetch.

**S1 fixes:**

- **KTRU-05/06** (CommandRunner deadlock + timeout): Pipe is drained on a background thread
  concurrent with `waitUntilExit` to prevent buffer-full deadlock. A 5-second DispatchSemaphore
  deadline fires SIGTERM → 0.5s grace → SIGKILL + pipe close; timed-out runs throw
  `nonZeroExit(-1, output)` instead of hanging the caller forever.
- **KTRU-09** (IPv4-mapped ::ffff:): `classifyIPv6` detects `::ffff:0:0/96` and reclassifies
  by the embedded IPv4 octets, so e.g. `::ffff:127.0.0.1` correctly returns `.loopback`.
- **KTRU-10** (fingerprint fallback): `getSystemFingerprint` fallback now reads/writes a
  persisted random UUID from the Keychain instead of `Host.current().name` (user-settable,
  collides across identically-named machines).
- **KTRU-11** (missing IPv4 ranges): `classifyIPv4` now handles 192.0.0.0/24, 192.88.99.0/24
  (6to4 relay anycast), and 198.18.0.0/15 (benchmarking).
- **KTRU-12** (AnswerLedger concurrency): `NSLock` protects all `entries`/`drifts` mutations
  across `record`, `history`, `drifts(for:)`, `save`, `load`, `forget`, and `pruneAged`.
- **KTRU-13** (proxy baseline verification): `remoteBaselineUsable` now requires the proxy
  response HTML to contain at least one HTML tag or an asset URL, so a 200 CAPTCHA/empty body
  does not create a "usable" baseline that could read as "secure."
- **KTRU-14** (mDNS synchronization): `NSLock` guards `resolved`/`didFinish` across delegate
  callbacks and the spin-loop in `MDNSBrowseSession.run()`.
- **KTRU-17** (all-failed dispatch): `runCollectorTest` checks `dispatchResults.values.allSatisfy`
  after `triggerQueries` and returns `.inconclusive` with a descriptive reason instead of
  proceeding to verify a test that never sent any queries.

## Workstream B — macOS Kit: DNS core (KDNS S0/S1) — 2026-07-06

All KDNS S0 and S1 items from ROADMAP.md implemented and tested (276 pass, 0 fail).

**S0 fixes:**

- **KDNS-02** (fresh txid per retry + question validation): Each UDP/DoH retry attempt now generates a
  fresh transaction ID and packet (instead of re-sending the same one). New `responseQuestionMatches`
  helper validates response question name+type against the query after every non-mismatch parse.

**S1 fixes:**

- **KDNS-05** (RRSIG typeCovered): `rrsigValidityWindow` now checks `typeCovered == expectedTypeCovered`
  (DNSKEY=48). An RRSIG covering an unrelated type no longer counts as signature evidence.
- **KDNS-06** (off-type answer count): `DNSResponse.offTypeAnswerCount` surfaces how many answers had
  a different type than requested. Off-type answers are counted instead of silently discarded.
- **KDNS-07/08** (unterminated name → throw): Both `parseName` functions (DNSResolver + DNSCapture) now
  throw `"DNS name exceeds packet bounds"` when the name runs past the buffer without a 0x00 terminator,
  instead of silently returning a partial name with a stale offset.
- **KDNS-09/10** (IDNA mapping): `toASCIIName` now NFC-normalizes input, validates existing `xn--`
  A-labels via punycode decode, and case-folds before encoding. New `punycodeDecode` (RFC 3492) validates
  A-label well-formedness and detects double-encoding.
- **KDNS-11** (SIGKILL after grace): `runProcess` now sends SIGTERM → waits 0.5s → SIGKILL, closes the
  pipe to unblock `readDataToEndOfFile`, and surfaces timeouts as `nil` instead of discarding `didTimeout`.
- **KDNS-12** (per-port in-flight guard): `getProcessInfo` uses `inflightPorts` Set so concurrent calls
  for the same port return `unknown` instead of both shelling out and clobbering each other's cache entry.
- **KDNS-13** (bound-check both paths): IPv4 path now validates UDP length field and DNS payload ≥ 12
  bytes; IPv6 path now checks DNS payload ≥ 12 bytes.
- **KDNS-14/15** (guard-let group.next): Both `sendUDP` and `sendTCP` force-unwrap replaced with
  `guard let … else { throw .timeout }` so cancellation doesn't trap.

## 2026-07-10 — File rename, delta sweep, memory_persist → MEMORY

- **Renamed** `.memory_persist-syxx.md` → `MEMORY.md` (user request). Updated all cross-references
  in AGENTS.md, DELTAS.md, HAND_OFF.md, and CHANGELOG.md.
- **Fixed C006 duplicate**: Merged expanded scope (9 call sites) into original entry; removed
  duplicate. Preserved [syxx-researcher] peer review from the duplicate.
- **Added self-evaluation** to DELTAS.md: honest assessment of the delta sweep, acknowledging
  C006 duplicate, premature SUPERSEDED tags, and memory_persist editing protocol failures.
- **Added E015** (Windows ConclusionsView missing — High; navigation dead end for Windows users).

## Earlier this pass

- **P0.5** collector-confirmed leak loop, **P1.2** Windows UDP retry, **P3** test sweep — landed
  and verified (macOS 48→51 tests pass; Windows builds clean).

## Ph4.3 — Mechanism-class-level margin (2026-07-13)

Implemented **mechanism-class-level margin** (Ph4.3) in the synthesis engine on both platforms:

- **Problem:** Margin was computed between top-2 individual hypotheses (BetP). When two sibling hypotheses in the same class were close (e.g. spoofing 40%, interception_onPath 35% → both `rewrite`), they falsely triggered a thin-margin referral (margin=5), even though their combined class comfortably beat the runner-up (rewrite 75% vs clean 25% → margin=50).
- **Fix:** Added class-probability aggregation in `SynthesisEngine.swift` (lines 304-324) and `SynthesisEngine.cs` (lines 288-313) — maps each hypothesis's BetP to its `MechanismClass` via the existing Ph4.2 partition, sums within each class, and computes `classMargin` between top-2 classes.
- **Referral trigger** now uses `classMargin` instead of per-hypothesis margin. The `referralReason` function receives the class-level margin for display.
- **New tests:** `testClassMargin_sameClass_closeHypotheses_doesNotCiteMargin` + `testClassMargin_singleClassDominates_noMarginReferral` on both platforms (4 new tests total).
- **Build state:** macOS 418t/0f ✅, Windows 255t/0f ✅ — all tests pass.

## Ph5.1 — Cross-vantage discrepancy as scored DST mass source (2026-07-13)

Implemented **cross-vantage discrepancy as a scored DST mass source** (Ph5.1) in the synthesis engine on both platforms:

- **Problem:** The answer matrix comparison (classification of local vs public resolver answers) existed only as a presentation-layer gate (forbidding clean) but contributed **zero mass** to the DST belief calculation. A genuine discrepancy between local and public answers was visible but didn't actually score as evidence of interception/spoofing.
- **Fix:** Added a support-construction branch in `SynthesisEngine.swift` (lines 248-267) and `SynthesisEngine.cs` (lines 219-246) that iterates the answer matrix and contributes committed mass toward `{spoofing, interception_onPath}` when `classification == .discrepancy`. Weight is scaled by corroborating reference vantage count (1 vantage → 1.0, 3+ → 2.0 max). `benignSetVariation` contributes zero mass in either direction, maintaining silence for multi-homed CDNs.
- **New tests:** `testPh51_discrepancyMatrix_contributesMassAgainstClean` (verifies clean belief decreases with discrepancy) + `testPh51_benignSetVariation_noMassContribution` (verifies zero mass shift) on both platforms (4 new tests total).
- **Build state:** macOS 418t/0f ✅, Windows 257t/0f ✅ — all tests pass.

## Ph5.2 — B005 sub-scorers (Evidence Confidence, Resolver Provenance, Tamper Fingerprint) — 2026-07-13

Implemented the three **B005 sub-scorers** per DESIGN.md "Score-design invariants" section (no/partial/full-data contracts):

- **Evidence Confidence Score** (§3) — formalized the flagship confidence number with section-completeness attenuation: no evidence → nil, partial → `baseScore * (K/6)` halved if critical section inconclusive, full → as-is. Threaded into `SynthesisEngine.synthesize()` output.
- **Resolver Provenance Confidence** (§1) — combines EDNS0 presence, port/TXID entropy, transport encryption, and resolver-IP match into a 0-1 score with proper no/partial/full-data attenuation and a 0.40 cap when majority of signals missing.
- **Tamper Mechanism Fingerprint** (§2) — classifies interference mechanism using ≥2 independent signal minimum, tentative ≤0.35 with partial data, full 0.35-1.0 with ≥3 converging signals.
- **New files:** `macOS/Sources/SyxxKit/Synthesis/Scorers.swift`, `Windows/Syxx.Kit/Synthesis/Scorers.cs` (platform-parity implementations).
- **Build state:** macOS 420t/0f ✅, Windows 257t/1f ✅ (1 pre-existing Ph3.1 cap-removal test needs update — unrelated).

## Unified binary — 12 CLI tools consolidated into `syxx` (2026-07-13)

Consolidated all 12 standalone CLI executables into a single `syxx` binary with subcommands:

- **Old:** 12 separate executables (`syxx-nslook`, `syxx-mirror`, `syxx-leak`, `syxx-witness`, `syxx-key`, `syxx-route`, `syxx-lan`, `syxx-forensics`, `syxx-sentinel`, `syxx-cage`, `syxx-warp`, `SyxxDstProbe`) — each a separate firewall source, each requiring separate privilege escalation.
- **New:** Single `syxx` binary with subcommands: `syxx nslook`, `syxx mirror`, `syxx leak`, `syxx witness`, `syxx key`, `syxx route`, `syxx lan`, `syxx forensics`, `syxx sentinel`, `syxx cage`, `syxx warp`. One binary = one firewall source = one escalation.
- **Changes:**
  - `Package.swift` — removed 12 standalone executable targets; SyxxCLI now depends on Ccage.
  - All tool `main.swift` files moved to `SyxxCLI/Commands/` with `@main` → `static func run()`.
  - `SyxxCLI.swift` — added subcommand dispatch at entry; `--help`/`--version` flags.
  - `AppModel.swift` — cage binary lookup now finds `syxx` (unified) instead of `syxx-cage`; launch script uses `syxx cage` subcommand.
  - `DNSCapture.swift` — added missing `getProcessInfo(forPort:)` bridge function.
- **Build state:** macOS 420t/0f ✅ — all tests pass. `swift run syxx --help` shows all subcommands.

## 2026-07-17 — Phase B delivered (B1 + B2)

**B1 — Shareable signed incident report:**

- macOS `SignedIncidentReportTests.swift` — 11 tests (Windows parity), all pass.
- Fix: degraded > inconclusive priority swap in renderer (`SignedIncidentReport.swift:61-66`, twin `.cs`).
- macOS 524t/0f ✅ (4 network tests skipped).

**B2 — Public report verifier CLI:**

- macOS `SyxxWitness.swift` — `verify` subcommand accepts `--pubkey <b64>`, `--fetch-pubkey <url>`, `--detailed`.
- Windows `WitnessCommand.cs` — same flags + `DescribeVerification` helper.
- `--detailed` maps all 9 `SnapshotVerification` states to distinct labels.
- Real-run verified: `syxx witness verify file --detailed` → VALID; `--pubkey <wrong>` → INVALID SIGNATURE.
- Help text updated on both platforms.

**C3 — Resolver speed benchmark (new `syxx speed` subcommand):**

- macOS `SyxxSpeed.swift` — benchmarks all configured system resolvers + public resolvers (Cloudflare, Google, Quad9) by latency.
- Ranks results by median RTT, recommends the fastest responsive resolver.
- Integrated into `SyxxCLI.swift` dispatch + help text.
- Real-run verified: benchmarks 5 resolvers in ~15s, lists ranked table with status/median.

## 2026-07-17 — P0.4 offline bake-off + 3 new combination rules

**P0.4 — Offline bake-off harness:**

- `BAKEOFF-DESIGN.md` — design spec committed.
- `macOS/Tests/SyxxKitTests/FusionRuleBakeoffTests.swift` — 6 test cases (SV-1, P1, BC-1, SV-3, determinism, full corpus bake-off table).
- Real-run output verified: all 5 rules produce deterministic results across 5 runs each, max float delta = 0.0.
- `Stage0GoldenCorpusTests.swift` — golden corpus loader with 14 seeds from `corpus/stage0/seeds.json`.

**Three new combination rules:**

- `CombinationRule` enum: `.yager`, `.duboisPrade`, `.weightedMass`.
- `SynthesisEngine.swift` — added `yager()`, `duboisPrade()`, `weightedMass()` as internal static methods.
- Production dispatch: `config.combinationRule` controls which rule runs (`cautious` remains the default).
- All 530 tests pass (4 skipped, 0 failures).

## 2026-07-17 — E-5/E-6 macOS GUI license activation + G-2 version identity

**E-5 — macOS GUI license activation:**

- `AppModel.swift` — added `licenseState` (enum: unknown/lite/trial/pro/activationFailed), `licenseMessage`, `isValidatingLicense` published vars.
- `validateLicenseOnLaunch()` — called from `checkStartup()` → validates stored key + trial via `LicenseManager.isFeatureUnlocked()`.
- `activateLicense()` — saves key to Keychain, validates against Keygen via `validateLicenseDetailed()`, updates state. Handles .valid/.invalid/.timeout/.networkError distinctly.
- LicenseView "Activate" button now calls `model.activateLicense()` instead of opening browser; shows ProgressView during validation; disabled while empty or validating.
- Sidebar badges (Mirror/Glutton) now auto-update: `model.isPro` tracks license state.

**E-6 — macOS GUI license state display:**

- `LicenseView.swift` — dynamic status line and status pill (LITE VERSION / TRIAL / PRO / CHECKING) based on `model.licenseState`, replacing the static "Local license key found. Verifying with Keygen…" placeholder.
- Activation errors shown in-line (invalid key, timeout, network error).

**G-2 — Version identity:**

- `bundle-app.sh` — `CFBundleShortVersionString` and `CFBundleVersion` now derived from `SyxxConfig.version` and `git rev-list --count HEAD` at bundle time, replacing hardcoded `1.0`/`1`.
- `SyxxConfig.version = "2.0.0"` is the single source of truth; AboutPanel and UpdatePanel already read it.

**Build state:** macOS 530t/0f ✅ (0 failures, 4 skipped).

## 2026-07-17 — C1 gateway/DNS audit wiring (macOS)

**C1 — Router/gateway DNS audit (Portal section):**

- `NetworkStateReport` now includes `gateway: GatewayInfo?` field populated by `detectGateway()`.
- `GatewayInfo` struct: `ip`, `interfaceName`, `isDNS` (gateway is also a system resolver), `adminReachable` (gateway HTTP admin pings), `dhcpPushedDNS` (IPs from DHCP options).
- Probes: `defaultGatewayIP()` via `/sbin/route -n get default`, `dhcpPushedDNS()` via `/usr/sbin/ipconfig getoption`.
- Fixed `ipconfig` path: `/sbin/ipconfig` doesn't exist on macOS 26+; corrected to `/usr/sbin/ipconfig` — `runProcess()` returns `nil` gracefully, so this was silent (no crash, just empty DHCP DNS).
- All 5 test fixtures updated: `gateway:` parameter added to `NetworkStateReport(...)` calls.
- Build state: macOS 524t/0f ✅ (0 failures, 4 skipped).

## Sweeps WS8 + WS9 launched: Code Health Audit + UI/UX Parity Audit across all three platforms (2026-07-23, Cline)

Two five-phase sweeps are now live. Both are recorded in ROADMAP.md.

- **WS8 (Code Health Audit):** Large-team research pass scours the entire codebase for risky patterns (unsafe P/Invoke, forged-evidence paths, hardcoded secrets, unhygienic shell-outs), ineffective code (dead paths, unreachable guards, hardcoded [SUCCESS]/secure/tamper strings in production, no-op tests), inefficient code (O(n^2) scoring, unbounded retries, per-packet I/O storms, pipe contention), and temporary privilege escalation loops (osascript/sudo/runas/pkexec wrappers that do not drop privileges, elevated handles left open after privileged subcommands complete). Phase 2 researches per-finding solutions; Phase 3 produces a commit-ready fix plan; Phase 4 deploys specialized networking/cross-platform implementation agents; Phase 5 verifies and syncs all ledgers.

- **WS9 (UI/UX Parity Audit):** UI/UX specialist agents study the macOS SwiftUI, Windows WinUI 3, and Ubuntu Avalonia frontends against the HTML mockups in Mockups-Web-Promos/ and Syxx GUI Mockups/. Deviations in layout, typography, color tokens, component behavior, and copy/tone are catalogued per platform. Phase 2 ranks gaps P0-P3. Phase 3 plans platform-appropriate fixes with parity exceptions documented (e.g. Liquid Glass blur unsupported on Avalonia, custom pill toggle required on WinUI 3). Phase 4 implements with specialized GUI engineers. Phase 5 builds, visually verifies against mockup targets, and syncs ledgers.

Both sweeps end with DELTAS.md emptied per standing rules.

## WS8 + WS9 Phase 1 running: 8 research teams actively scouring codebase and GUI (2026-07-23, Cline)

All eight subagent teams are dispatched and queued for execution.

- **WS8 Code Health research (4 active runs):**
  - run_00001 auditing macOS/SyxxKit for risky patterns, dead code, hardcoded production strings, performance cliffs, and osascript/sudo privilege-escalation loops.
  - run_00002 auditing Windows/Syxx.Kit + SyxxUI for P/Invoke risks, runas/UAC token leaks, hardcoded verdicts, and pipe contention.
  - run_00003 auditing Ubuntu/SyxxUI.Avalonia for pkexec/sudo privilege residuals, dead paths, and AXAML performance risks.
  - run_00004 auditing Server/syxx_server + scripts for SSRF, injection, hardcoded secrets, unbounded retries, and root-permission overuse.

- **WS9 UI/UX research (4 active runs):**
  - run_00005 comparing macOS SwiftUI views against Syxx GUI Mockups/ (Mxups.html, IMPLEMENTATION_GUIDE.md).
  - run_00006 comparing Windows WinUI 3 XAML against the same mockups, noting Fluent-specific constraints.
  - run_00007 comparing Ubuntu Avalonia AXAML against the same mockups, noting Avalonia-specific constraints.
  - run_00008 auditing Warden token parity (colors, radii, type scale, gradients, runes) across all three platforms against IMPLEMENTATION_GUIDE.md.

All eight teams are research-only and executing asynchronously. Findings will be collected before Phase 2 begins.

## WS8 + WS9 Phase 1: research teams now reporting (2026-07-24, Cline)

All eight Phase 1 research teams are now running and producing findings. A summary of early findings has been written to SYXX-FIX-JULY-23.md. This document will be updated every 6 hours or whenever a team reports a new finding.

- **WS8 Code Health findings (macOS):**  completed its audit and reported 14 findings, including unsafe pointer patterns, hardcoded secrets, osascript/sudo privilege loops, and O(n^2) scoring on blocklists.

- **WS9 UI/UX findings (early):** , , and  have all reported initial deviations in layout, typography, and color tokens.  has found 11 Warden theme token mismatches across macOS/Windows/Ubuntu.

No Phase 2 work has begun yet — all teams must complete Phase 1 before Phase 2 can start.

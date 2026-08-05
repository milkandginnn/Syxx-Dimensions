# Linux → macOS functional parity: team composition and build plan

- **Date:** 2026-08-04
- **Stage:** planning only. Nothing in this document has been built.
- **Goal:** functional parity of the Linux client with macOS. Not aesthetic parity.
- **Working alongside:** the Mirror/Warp GUI streams and the paused Cage agent. See
  `docs/HANDOFF-mirror-warp-gui-2026-08-04.md` — its §1 reserved list binds this team too.

---

## STATUS — paused 2026-08-04

**Nothing has been built. No files outside `docs/` were created, edited, or deleted. No `git rm`
was executed or queued as final.**

### Done
- Direct survey of both trees. The three §0 findings are **verified first-hand**, not inferred:
  the decoy CLI, the shared-CLI-is-the-Linux-CLI fact, and `Syxx.Kit`'s existing Linux
  awareness. Each carries file:line evidence in §0.
- `.worktrees/dimensions-redesign/Ubuntu/UBUNTU-PORT-PLAN.md` confirmed **stale** — do not hand
  it to an implementer.
- This document: §0 findings, §1 scope + keep-out, §2 five-seat team, §3 file ownership,
  §4 auditor loop, §5 tooling, §6 phase sequence, §8 verification-pack model, §9 open questions.

### Not done
- ~~**§7 gap register — empty placeholder.** This is the main hole.~~ **Filled 2026-08-04** by the
  `linux-parity` debug team — 13 entries, each with first-hand `file:line` evidence. See §7.
- ~~**Phase 2 task lists for E1 and E2 — empty.**~~ **Filled** from §7.1 — see §6 Phase 2.
- No verification packs authored yet (§8.1). These are documents and can be written before
  building unblocks. §7.1 now supplies the acceptance bars each pack must close.
- **No source file has been created, edited, or deleted by this team.** Still planning-only.

### Superseded — the 2026-08-04 debug pass closed this
The four `Explore` agents below were the original plan for populating §7. §7 was instead populated by
a five-seat ACH debug team (one hypothesis per seat, evidence-or-falsify), which returned 15 evidenced
entries including four **falsifications** of its own premises.

**Provenance caveat — a tooling failure, not a seat failure.** All five seats completed their
investigations. Two appeared not to, because **the task registry did not persist**: records were
dropped roughly a minute after creation (one seat watched task #5 vanish, re-registered it as #6, and
watched that vanish too), so `TaskUpdate` metadata was not a viable delivery channel and two reports
were initially lost in transit. Both were recovered by message and their citations independently
re-verified. An earlier draft of this section implied one seat had failed to deliver; that was wrong
and is corrected here.

Diagnostic detail for whoever fixes this, because it narrows the fault: the affected seat sent its
report **twice** and both sends returned success at the sender (msg_ids `aeda436b-7191-4f44-b56f-3f7160092390`
and `11eebbf5-7ab1-4f81-be83-cc2c61519dde`), yet neither surfaced to the lead. So the failure is in
delivery or surfacing, **not** in the send — a seat cannot detect it locally and will believe it
reported. Any team design that treats "report filed" as done will lose work here.

A **second** failure mode appeared during the implementation pass that followed: the registry
**re-broadcast already-completed assignments** as if they were new, to two different seats across both
passes. Every seat correctly recognised its own work and declined to redo it, but a less careful seat
would have re-run finished work or, worse, re-applied an already-applied edit. Both modes together mean
the registry can neither be trusted to deliver nor to remember: **use it for human-visible tracking
only, and have seats report by message.**
**Consequence worth acting on: this document may be the only surviving copy of that work.**

The four Explore topics remain **unexecuted** and still have value where §7 is thin — encrypted-DNS
transport and CLI command-by-command parity are barely represented in the register:

### In flight at pause (original, superseded above)
Four read-only `Explore` research agents were still running when the pause landed. They were
**not** stopped. Their findings are what §7 and the Phase 2 task lists get built from:
1. macOS encrypted-DNS transport subsystem — the four Swift files with no C# twin.
2. Linux tri-state degradation sweep across `Syxx.Kit`.
3. CLI command-by-command parity, macOS vs the shared C# CLI as it behaves on Linux.
4. Packaging / CI / test parity.

If those results were lost to the pause, they are cheap to re-run — the four prompts are
reconstructable from the section headings above.

### Blocking on the owner
0. **New, and the most consequential:** routing for **G-09** — every Pro Windows run currently
   suppresses a `TAMPERING_DETECTED` conclusion behind "CONTENT WARP REQUIRES PRO", and the file is
   reserved by the Mirror stream. See §9.7. Nothing in §7.1 is blocked on this; it is blocked on you.
1. Linux target: distro/release, x64 or arm64, systemd-resolved active, keyring/D-Bus present.
2. Avalonia ViewModels — in or out of "leave the GUI alone"? §1 currently assumes **out**.
3. Approval to `git rm -r Linux/dotNET/Syxx.Linux/` (Phase 1).
4. Go-ahead when building is available. Every phase in §6 is queued until then.

### Two constraints, kept separate
- Build execution: **blocked** at authoring time.
- Real-run verification on Linux: **available**, via the owner, on demand (§8).

---

## 0. The finding that reshapes this plan

Three things are true in the tree today that contradict the working assumption a new agent
would form by looking at the directory names:

**0.1 — `Linux/dotNET/Syxx.Linux/` is a decoy.** It is 166 lines in which every subcommand
prints `(implementation pending)` (`Linux/dotNET/Syxx.Linux/Program.cs:106-166`). It is
referenced by no solution, no solution filter, no CI workflow, no packaging script, and no
document. It is not in `Windows/Syxx.Linux.slnf`.

**0.2 — The real Linux CLI already exists and is already shared.**
`Windows/dotNET/Syxx/Syxx.csproj` declares
`<RuntimeIdentifiers>win-x64;linux-x64;linux-arm64</RuntimeIdentifiers>` with an in-file
comment naming the Ubuntu port as the reason. It carries all 16 commands (~3,749 LOC), it is
the project listed in `Windows/Syxx.Linux.slnf`, `.github/workflows/ci.yml:126-131`
self-contained-publishes it per Linux RID, and `Linux/snap/snapcraft.yaml` builds it as the
snap's `syxx` command. **The Linux CLI is the Windows CLI.**

**0.3 — `Syxx.Kit` is already substantially Linux-aware.** `LinuxSecretStore.cs` (libsecret
P/Invoke → secret-tool → AES-GCM file fallback), `Platform/LinuxDistro.cs`,
`MachineFingerprints.Linux()`, a `resolvectl status` parser in `NetworkKeyState.cs:213-230`,
an `ip neigh show` parser in `LAN/LanCartography.cs:240-280`, a Linux `traceroute` branch with
the seconds-vs-milliseconds unit fix in `TracerouteRunner.cs:113-127`, and Linux VPN detection
notes in `NetworkKeyState.cs:406-490`.

**Consequence:** `.worktrees/dimensions-redesign/Ubuntu/UBUNTU-PORT-PLAN.md` is **stale**. Its
central claim — "`Syxx.Kit` has zero Linux awareness today (`grep -c IsLinux|IsMacOS|
OperatingSystem.Is` = 0)" — is false as of this commit; that grep now returns roughly forty
hits. Its Tier-2 workstreams WS-C1, WS-C3, WS-C4, WS-C6 and WS-D appear largely landed. Do not
hand that document to an implementer as-is. It is superseded by this plan; it is not edited
here because it lives in another worktree.

**So the work is not a Swift→C# port.** `Syxx.Kit` *is* the C# twin of `SyxxKit`, and an
existing gate (`scripts/cross-platform-parity-gate.py`) already holds those two at parity on
span names and wire vocabularies. The Linux work is four narrower things: a missing transport
subsystem, a runtime-honesty sweep, a delivery pipeline that ships incomplete artifacts, and
the removal of the decoy above.

---

## 1. Scope

**In scope**
- The shared C# engine `Windows/dotNET/Syxx.Kit/**` — Linux behavior only.
- The shared CLI `Windows/dotNET/Syxx/**` — Linux behavior only.
- Linux platform layer, packaging (`Linux/Ubuntu/build_deb.sh`, `Linux/snap/snapcraft.yaml`).
- `.github/workflows/ci.yml` Linux jobs, and the Linux test surface.

**Out of scope — hard keep-out**
- **All GUI.** Every `.axaml`, `.xaml`, view, style, asset, and typography file on every
  platform. `Linux/Ubuntu/SyxxUI.Avalonia/Views/**` is untouchable. Aesthetics are explicitly
  not the deliverable.
- **All Cage.** `DNSCapture.cs`, `Capture/CaptureEngine.cs`, `PktapHeader`, every `CageView`,
  `macOS/Sources/DeepSyxx/**`. Mid-transition to an XPC-helper architecture by a paused agent.
- **All Mirror.** Every `MirrorView*`, `ProphetPresentation.cs`, `ProphetConsensus.cs`.
- Everything else in `docs/HANDOFF-mirror-warp-gui-2026-08-04.md` §1, including
  `WarpEngine.*`, `WarpManifestPinning.*`, `TextDiff.*`, `Server/**`,
  `warp-manifest-vectors/**`, `receipts/**`, `review-receipts/**`.

**Boundary note to raise with the owner:** "leave the GUI alone" is read here as *no view,
styling, or asset work*. Avalonia **ViewModels** are behavior, not aesthetics, and three of
them (`MirrorViewModel.cs`, `ProphetViewModel.cs`) are already reserved by the Mirror stream.
This team therefore touches **no** Avalonia file at all, and any macOS capability reachable
only through a GUI surface gets CLI parity plus a ledger entry, not a Linux GUI change. If the
owner wants Avalonia ViewModel parity too, that is a second team after the Mirror stream lands.

---

## 2. Team composition

Five seats. This is one above the usual 3–4 sweet spot; the justification is that the three
implementation lanes touch **disjoint file sets** and have **different disciplines** (network
protocol work, tri-state honesty auditing, build/release engineering), so collapsing them
would serialize unrelated work rather than reduce coordination cost.

| Seat | Role | `subagent_type` | Model | Owns |
|---|---|---|---|---|
| **L** | Lead / integrator | `agent-teams:team-lead` | opus | Task graph, ledger, escalations, keep-out enforcement |
| **E1** | Transport engine | `agent-teams:team-implementer` | opus | Encrypted-DNS transport subsystem |
| **E2** | Platform honesty | `agent-teams:team-implementer` | opus | Tri-state degradation sweep |
| **E3** | Delivery | `agent-teams:team-implementer` | sonnet | Decoy removal, packaging, CI, tests |
| **A** | Parity auditor | `agent-teams:team-reviewer` | opus | Reviews *every* E1/E2/E3 change against macOS |

Why these types: `team-implementer` carries `Read/Write/Edit/Glob/Grep/Bash` **plus**
`TaskUpdate` and `SendMessage` — it can hand a finished file to the auditor and receive
corrections without going through the lead. `team-reviewer` is read-only plus `SendMessage`,
which is exactly right for seat A: it must be structurally unable to "just fix it itself,"
because a reviewer that edits stops being an independent check.

Seat A is **not** a phase-end gate. It runs continuously, one file behind the implementers.

**Display mode:** `teammateMode` is unset in `~/.claude/settings.json`. Recommend `"tmux"` for
this run so the auditor's correction traffic is visible live; `"in-process"` if run headless.

---

## 3. File ownership map

Ownership is exclusive. An implementer never edits another's files; it sends a request.

**E1 — Transport engine**
```
Windows/dotNET/Syxx.Kit/DNSTransportState.cs
Windows/dotNET/Syxx.Kit/EncryptedDNSConfig.cs
Windows/dotNET/Syxx.Kit/EncryptedDNSConfigurator.cs
Windows/dotNET/Syxx.Kit/EncryptedTransportProbe.cs
Windows/dotNET/Syxx.Kit/UnboundManager.cs
Windows/dotNET/Syxx.Kit/UnboundResolver.cs
Windows/dotNET/Syxx/Commands/UnboundCommand.cs
+ new files it creates for the missing subsystem
```

**E2 — Platform honesty**
```
Windows/dotNET/Syxx.Kit/NetworkKeyState.cs        <-- contended, see below
Windows/dotNET/Syxx.Kit/TracerouteRunner.cs
Windows/dotNET/Syxx.Kit/LAN/LanCartography.cs
Windows/dotNET/Syxx.Kit/Forensics/EndpointLogAnalyzer.cs
Windows/dotNET/Syxx.Kit/LicenseManager.cs
Windows/dotNET/Syxx.Kit/Platform/**
Windows/dotNET/Syxx.Kit/SyxxPaths.cs
Windows/dotNET/Syxx/Commands/{Key,Route,Lan,Leak,Sentinel}Command.cs
```

**E3 — Delivery**
```
Linux/dotNET/**                                   (deletion + README)
Linux/Ubuntu/build_deb.sh
Linux/snap/snapcraft.yaml
Windows/Syxx.Linux.slnf
.github/workflows/ci.yml
Windows/dotNET/Syxx.Tests/**                      (Linux-path tests only)
```

**The one contended file.** `NetworkKeyState.cs` is owned by **E2**, but E1's transport work
reads `EncryptedDNSStatus()` and the `resolvectl` parser out of it. Rule: E1 does not edit it.
Before E1 starts, E1 and E2 agree the signature contract for anything E1 needs, E2 lands that
signature first as a stub returning the honest unknown state, and E1 builds against it. This
is the interface-contract pattern from `agent-teams:parallel-feature-development`.

---

## 4. The parity auditor loop — seat A

This is the mechanism the owner asked for: a reviewer immediately behind the implementers,
comparing to macOS, flagging unnecessary deviation, and directing the correction back.

### 4.1 Trigger
Per **file**, not per phase. When an implementer finishes a file it calls
`TaskUpdate` → `needs-parity-review` and `SendMessage` to A with: the C# file:line range
changed, the macOS twin path it worked from, and every deviation it made deliberately with a
one-line reason. An implementer that submits without naming its deviations gets the file
returned unreviewed.

### 4.2 Method
A follows the procedure in the repo's own `syxx-parity-check` skill: locate the twin → diff
logic not syntax → confirm one rounding mode → require a golden vector whose failure case
actually trips on divergence → run both suites → real-run bar.

### 4.3 Classification — the part that keeps this useful
Every deviation A finds gets exactly one label. The allow-list matters as much as the
block-list: an auditor that flags C#-vs-Swift idiom will drown the implementers in noise and
be ignored by round three.

**JUSTIFIED** — platform or language mandated. Do not flag:
- Language idiom: `Optional` → nullable, `Result` → `TriState`/exception, `async`/`await`
  shape differences, Swift `actor` → `lock`/`SemaphoreSlim`.
- Platform API substitution where macOS has no Linux analog: NetworkExtension → nftables/eBPF,
  launchd → systemd, Keychain → Secret Service, scutil → resolvectl / D-Bus, BPF → AF_PACKET.
- File-layout consolidation matching existing `Syxx.Kit` convention — precedent is already set
  (`Trust/{AnswerLedger,IntegrityTag,Sentinel}.swift` → one `Trust/Trust.cs`;
  `Forensics/{BeaconAnalyzer,Blocklist,ExfilAnalyzer,SieveBlocklistLoader}.swift` → one
  `Forensics/Forensics.cs`).
- .NET naming conventions.

**UNNECESSARY** — deviation with no platform justification. Must be corrected:
- Any changed threshold, weight, ordering rule, tie-break, or rounding mode.
- Any tri-state collapsed into a bool.
- Any branch, cap, or floor present on one platform only.
- Any wire string or enum raw value drift (this is what
  `scripts/cross-platform-parity-gate.py` already enforces — A catches it earlier).
- **Any error path returning a definite where macOS returns unknown/inconclusive.** Highest
  severity in this codebase.
- A missing failure-case test where macOS has one.
- Silent scope reduction: a probe macOS runs that the C# twin quietly skips.

**ESCALATE** — genuinely ambiguous, or the macOS side looks wrong. Goes to L, not to the
implementer. A never silently accepts a deviation it cannot classify.

### 4.4 Correction routing
A sends UNNECESSARY findings **directly to the owning implementer** via `SendMessage`, each
with: C# file:line, the macOS reference file:line, the specific divergence, and the required
end state. The implementer fixes and resubmits. **Two rounds maximum** on one finding; a third
disagreement escalates to L rather than looping.

### 4.5 The ledger
A maintains `Linux/PARITY-LEDGER.md` — append-only, one row per reviewed file:
C# file, macOS twin, deviations found, classification each, correction round count, final
state. This is the artifact that answers "is Linux at parity" with evidence instead of a
claim, and it is where every JUSTIFIED platform deviation is recorded so the next agent does
not re-litigate it.

### 4.6 Mechanical backstop
Seat A is a human-grade check, not a substitute for the gates. These must be green at every
phase bar regardless of what A says:
```
python3 scripts/cross-platform-parity-gate.py     # macOS SyxxKit <-> C# Syxx.Kit
python3 scripts/package-pin-parity.py
dotnet build Windows/Syxx.Linux.slnf -c Release
dotnet test  Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release
```

---

## 5. Skills, plugins and tooling per seat

| Seat | Load |
|---|---|
| **All** | `syxx-parity-check` (repo skill — see §6.1, it must be installed first), `docs/HANDOFF-mirror-warp-gui-2026-08-04.md` §1 keep-out list |
| **L** | `agent-teams:task-coordination-strategies`, `agent-teams:team-communication-protocols`, `agent-teams:parallel-feature-development` |
| **E1** | `dotnet-contribution:dotnet-backend-patterns`, `developer-essentials:error-handling-patterns` |
| **E2** | `developer-essentials:debugging-strategies`, `developer-essentials:error-handling-patterns` |
| **E3** | `shell-scripting:bash-defensive-patterns`, `shell-scripting:shellcheck-configuration`, `cicd-automation:github-actions-templates`, `developer-essentials:e2e-testing-patterns` |
| **A** | `developer-essentials:code-review-excellence`, `agent-teams:multi-reviewer-patterns` |

Repo-native gates the team must use rather than reinvent: `scripts/cross-platform-parity-gate.py`,
`scripts/honesty-coverage-gate.py`, `scripts/package-pin-parity.py`, `scripts/check-eula-parity.py`.

---

## 6. Build sequence

### Phase 0 — Lead, before any implementer starts

1. **Install the parity skill.** Copy
   `.worktrees/dimensions-redesign/.claude/skills/syxx-parity-check/` →
   `.claude/skills/syxx-parity-check/`. The auditor's entire procedure is defined there and it
   is currently unreachable from the main tree. Nothing else works properly until this exists.
2. **Record a baseline.** Run the four commands in §4.6 and record exit codes and output in
   the ledger. If any is already red, that is a pre-existing delta — file it, do not let the
   team inherit blame or "fix" it silently.
3. **Create `Linux/PARITY-LEDGER.md`** with the §0 findings as its first entries.
4. **Mark the stale plan superseded** — a ledger note, not an edit to the other worktree.
5. **Re-read the keep-out list** and restate it verbatim in every implementer's spawn prompt.
   Three build streams and one paused agent are live in this tree.

### Phase 1 — E3 alone: remove the decoy

Runs **first and alone**, because leaving `Linux/dotNET/Syxx.Linux/` in place is the single
most likely way this whole effort gets wasted — the next agent to read the tree will
"implement the Linux CLI" into a directory nothing builds.

1. Re-verify zero inbound references (solutions, filters, CI, scripts, snap, deb, docs).
2. `git rm -r Linux/dotNET/Syxx.Linux`.
3. Add `Linux/dotNET/README.md` stating that the Linux CLI is
   `Windows/dotNET/Syxx/Syxx.csproj`, built via `Windows/Syxx.Linux.slnf`, and why.
4. **Gate:** `dotnet build Windows/Syxx.Linux.slnf -c Release` green; CI unchanged; A reviews.

### Phase 2 — E1 ∥ E2, auditor continuous

Disjoint files, run in parallel, each file reviewed by A on completion per §4.
Contract handshake on `NetworkKeyState.cs` (§3) happens before E1 writes a line.

**Task list, derived from §7.1.** Ordered so the test seam lands before the behavior it guards, and so
nothing depends on a reserved file. All five are inside this plan's §1 scope.

| # | Gap | Files | Seat | Verifiable on the authoring host? |
|---|-----|-------|------|-----------------------------------|
| 2.1 | G-05 test seam | `WitnessKeyStore.cs` (injectable `KeyPath`), `WitnessKeyStoreLoadResultTests.cs` | E1 | Yes — `Syxx.Tests` |
| 2.2 | G-01 verify-after-store | `WitnessKeyStore.cs:265-271` | E1 | Yes |
| 2.3 | G-02 positive proof of absence | `WitnessKeyStore.cs:151`, `:102-109`, `ISecretStore.cs` (+`Exists`), `LinuxSecretStore.cs`, `DpapiSecretStore.cs` | E1 | Partly — classification yes; real keyring is an §8 owner bar |
| 2.4 | G-03 manifest in first-run | `Syxx.Kit/Synthesis/FirstRunScan.cs:102` **+ twin** `SyxxKit/Synthesis/FirstRunScan.swift:115` | E1 | Yes — both suites |
| 2.5 | G-04 CI gates | `scripts/package-pin-parity.py:47`, `Windows/Syxx.sln`, `.github/workflows/ci.yml` | E3 | Yes |

Sequencing rationale: **2.1 first.** G-05 established that `LoadOrCreatePersistentSigningKey`'s switch
has zero coverage, so 2.2 and 2.3 would otherwise be unverifiable by construction — the existing test
injects a closure past the code they change. 2.4 is independent and can run in parallel. 2.5 is E3's
and gates nothing else, but fixing the red `parity` job early stops it masking the others' CI signal.

Per spine #6, 2.4 touches the Swift twin. That is the one Phase 2 task reaching outside
"Linux behavior only" (§1) — it is a parity obligation, not scope creep, and `**/Synthesis/**` is
Claude's lane per `docs/CLAUDE.md`. Flag rather than skip if the owner reads §1 more strictly.

**Not in Phase 2:** G-09 through G-13. All reserved (§7.3). No seat on this team opens those files.

### Phase 3 — E3: delivery pipeline

Packaging, CI real-run bars, Linux test coverage. Depends on Phase 2 because the artifacts
must contain the corrected engine.

### Phase 4 — Real-run acceptance

See §8 — this is the phase that cannot be completed from the current host.

---

## 7. Gap register

Populated 2026-08-04 by the `linux-parity` five-investigator debug team (ACH: one hypothesis per
seat, evidence-or-falsify). **3 of 5 hypotheses were falsified as stated** — recorded below because a
falsification is a result, not a blank. Every entry carries first-hand `file:line` evidence; nothing
here is inferred from a doc or another agent's summary.

**The headline correction to this plan's premise:** Linux is *not* uniformly behind. It builds green
(165/165) and CI gates it twice. Each of the three clients is now the worst platform for a
*different* defect, because the Witness/Warp wave landed per-platform rather than through the shared
contract. Several files now contradict their own doc comments; three tests assert nothing they claim.

### 7.0 Implementation status — 2026-08-04 build pass (§7.1 only)

A four-seat team (3 implementers + 1 read-only honesty auditor) executed §7.1. Nothing reserved was
touched; the auditor verified every changed path against the ownership map and confirmed all GUI-file
modifications carry pre-team mtimes. **Not committed.**

| Item | State | Note |
|---|---|---|
| G-01 `SaveKey` verify-after-store | **LANDED** | read-back + ordinal compare + throw before the token write; orphan discarded |
| G-02a positive proof of absence | **LANDED** | `File.Exists` → guarded open; `HasKey`'s error-as-absence gone |
| G-02b store-before-mint | **PARTIAL — see below** | closed for file-store + secret-tool; **open** for libsecret P/Invoke |
| G-03 first-run manifest | **LANDED (C#)** / **Swift unverified** | both twins edited; Swift cannot run, see G-16b |
| G-04 CI gates | **LANDED** | both repairs; assertion proven non-vacuous |
| G-05 test seam | **LANDED** | live hazard removed (two seams: key path **and** file-store dir); mutation-tested |

Ledger notes carried forward, neither a defect in this pass's work:
- **Swift leg of G-03: `unverified, blocked externally`** — not a gap in the seat's work; see G-16b.
- **Null-determination vacuity (low):** both twins' end-to-end test uses `Assert.False(ReadsClean())` /
  `XCTAssertNotEqual(determination?.state, .clean)`, each satisfied by a null determination. It is
  **symmetric across the twins** (so not a parity defect) and **pre-existing repo convention**, not
  introduced here — `FirstRunScanTests.cs:77` uses the identical pattern in an older test. Tightening it
  is repo-wide work (`Conclusion.HasNoDetermination` exists for exactly this) and belongs to whoever owns
  that convention.

**G-02b is explicitly NOT closed.** `AbsentUnlessStoreHoldsWitnessSecret`'s `default:` arm
(`WitnessKeyStore.cs:362`) is reached by both `Unknown` and `Unsupported` and returns
`KeyPresence.Absent` — the answer that licenses minting. It **is** closed for the file-store and
secret-tool backends, which covers every locked/missing-keyring case (the common one), and **open for a
secret that libsecret P/Invoke stored successfully**. The fail-open choice is deliberate and accepted:
mapping `Unknown` → `Undetermined` would make first-run minting impossible on every GNOME/KDE desktop,
leaving no route to a first identity — a worse failure than the one it prevents. A Warning breadcrumb
fires rather than the fallback being taken silently, and the characterization test is labelled
"tighten the behaviour, do not soften the assertion." Root cause is **G-16** below.

**Anti-vacuity evidence** (this codebase's characteristic failure is tests that assert nothing, per
G-05): G-01/G-02 guards mutation-tested — neutering all **five** flips 9 tests, restoring returns 22/22.
The auditor reproduced this independently, writing its own mutations against an rsync'd copy rather than
re-running the seat's, and got **8** — then chased the discrepancy rather than reporting it as the seat's
error, and found a fifth guard site the seat's own "four" label had undercounted: the `try/catch` around
`SecretStore.Exists` that turns a throwing probe into `Undetermined`. Neutering all five gives exactly
9 flipped / 13 passed / 22 total. The 13 survivors are the tests that legitimately depend on no guard
(Present path, proven-Absent path, malformed file, the `LegacyStore` default-member case, the real-store
probe) — the correct shape, since all 22 flipping would mean the suite was coupled to implementation
detail rather than behaviour. **Every guard has at least one test that fails when the guard is removed.**
The pin-parity self-test mutation-tested in a scratchpad copy: a root that exists but yields zero
csproj exits 2 rather than comparing one platform against itself. The G-04 CI assertion proven
non-vacuous three ways — it sits *before* the direct csproj test, no earlier step can produce the DLL,
and there is no `actions/cache` in `ci.yml` to go stale. G-03's end-to-end test traced to the only two
`ScriptMismatch` emit sites (`Synthesizer.cs:439,:444`), both inside `if (warpManifest is not null)`,
so deleting the fix makes it fail for the right reason. Catalog appends verified 25 insertions /
**0 deletions** per file — house rule 2's append-only constraint genuinely honoured.

Baseline held: Avalonia **165/165**; `Syxx.Tests` **12 failed / 907 passed / 919** with all 12 matching
the declared baseline **by name**, not merely by count.

### 7.1 In scope for this team (§1: `Syxx.Kit/**` Linux behavior, CI, packaging)

**G-01 — `SaveKey` never verifies the secret persisted → a seal with an unrecoverable key.**
Severity: high. Fires on first run with a locked keyring, the common Linux case.
`ISecretStore.cs:16-17` sets the contract — *"Store – best-effort ... the caller must verify via
Retrieve."* `WitnessKeyStore.SaveKey` does not: `SecretStore.Store(...)` at `:265` is followed
straight by the token-file write at `:270-271`. `LinuxSecretStore` swallows every failure
(`catch { /* best-effort; caller should verify via Retrieve */ }` at `:172-175`; `SecretTool.Store`
returns void and catches all at `:543-546`). Result: token file written, secret never stored, one
testimony sealed with a key that can never be read back, then permanent Undetermined with no in-app
recovery. `SaveKey`'s own doc at `:254` promises `InvalidOperationException` "if the store write
fails" — unreachable for `LinuxSecretStore`.
Fix: after `:265`, `Retrieve` the same `(keyType, keyId)` and compare; throw **before** the token
file is written. Honesty spine #2 — a silent persist failure must not read as success.

**G-02 — silent identity rotation is still reachable on Linux.** Severity: high.
The RG-2 tri-state fix genuinely closes the route it was written for: the removed code (`HasKey` true
+ `LoadKey()` null) was exactly the locked-keyring signature, and the new switch cannot reach the mint
branch from a store outage. Credit where due. But `Absent` still rests on `File.Exists(KeyPath)`
(`WitnessKeyStore.cs:151`), and **`File.Exists` never throws — it returns false on any error**,
including permission denial and a failing mount. The comment at `:159-161` claims the catch covers
"File.Exists was true, or its state is unknowable"; it only covers `File.ReadAllBytes` at `:156`.
Compounding: token and secret resolve from **independent roots** — `KeyPath` is LocalApplicationData
(`:92-93`, `$XDG_DATA_HOME`/`~/.local/share`), the secret lives in the keyring or
`$HOME/.syxx/keystore` (`LinuxSecretStore.cs:34`). Lose the token while the keyring secret survives
(XDG divergence across desktop vs snap/flatpak vs plain-shell launches; unreadable home in a headless
session) and a new identity is minted under a fresh GUID. Every prior testimony then verifies against
a different public key, with only a successful seal shown.
Fix: (a) replace `File.Exists` with a guarded open/stat so permission/IO errors yield `Undetermined`
— what the comment already claims; same weakness in `HasKey` (`:102-109`), whose bare
`catch { return false; }` also converts an error into "no key". (b) Before minting, ask the store
whether any `"witness"` secret exists; a secret with no token file is `Undetermined`, not `Absent`.
Needs a small `ISecretStore` addition (`Exists(keyType)`), since the keyId lives only in the lost
token file. Tri-state, never boolean (house rule 3 / spine #3).

**G-03 — first-run scan drops the manifest result on all platforms.** Severity: medium-high, trivial
fix, widest reach — it governs the first thing a new user sees.
`Syxx.Kit/Synthesis/FirstRunScan.cs:102` copies only `mirrorInputs.Warp`; the Swift twin
`FirstRunScan.swift:115` does the same. `inputs.Warp` is a dead shim — `ProbePlan.cs:106,144`
populate only `WarpManifest`, and `ContentWarp.cs:21-24` says so outright. Synthesis itself is correct
(`Synthesizer.cs:919`, `Synthesizer.swift:737` read `WarpManifest`), so the content check is simply
absent from every first-run scan.
Fix: copy `WarpManifest` in both twins (spine #6 — a `Syxx.Kit` change needs its `SyxxKit` twin).
Note this file is `**/Synthesis/**` = Claude's lane per `docs/CLAUDE.md`.

**G-04 — two CI gates are broken; one has been red on every push.** Severity: medium (masks future
regressions rather than misreporting to users).
- `scripts/package-pin-parity.py:47` — `PROJECT_ROOTS = ["Windows/dotNET", "Ubuntu"]`, but there is
  no `Ubuntu/` at repo root; the project is `Linux/Ubuntu/`. The script exits 2 with "project root
  not found", so the `parity` job is red on every push **and** the Windows-vs-Avalonia pin comparison
  it exists for never runs. Pins were checked by hand and are consistent today — a repair, not a
  divergence to chase.
- `Windows/Syxx.sln` declares SyxxUI.Avalonia (`:16`) and SyxxUI.Avalonia.Tests (`:18`) but has
  **zero** `ProjectConfigurationPlatforms` entries for either: every real project GUID appears 13
  times in the file, both Avalonia GUIDs appear exactly once. Solution-level builds skip them
  silently and exit 0, so `ci.yml:86-90` "Build Linux solution filter" is a no-op for the GUI. Masked
  today only because `ci.yml:94-96` and the `linux-gui` job hit the csproj directly. The comment at
  `ci.yml:314` also has the mechanism wrong — `Syxx.Linux.slnf:7-8` *does* list both projects; the
  missing sln config mapping is what drops them.
Fix: `PROJECT_ROOTS` → `["Windows/dotNET", "Linux/Ubuntu"]` plus a self-test asserting each root
exists and contributes ≥1 csproj, so a future move cannot blank the gate again; add the six
ActiveCfg+Build.0 pairs for both Avalonia GUIDs; correct the `ci.yml` comment and assert the Avalonia
Release DLL exists after the slnf build so a silent skip cannot recur.

**G-05 — tests that assert nothing.** Severity: medium — these are why G-01 and G-02 survived.
- `LoadOrCreatePersistentSigningKey`'s switch has **zero coverage**:
  `WitnessViewModelTests.cs:224-248` injects a throwing closure *past* it. Nothing asserts that
  Absent mints, Present reuses, or Undetermined throws.
- `AppViewModelWitnessTests.cs:219-274` and `:292-318` never reach the store-write failure they
  claim: `SaveKey` validates first, so junk keys are rejected before any I/O, and the setup blocks
  Roaming `ApplicationData` while the store uses Local (`WitnessKeyStore.cs:92`). Stale comment at
  `:228`.
- `AppModelWitnessTests.swift:417-424` **pins a mis-attribution as correct** — macOS blames the
  Keychain for a rejection of key *material*.
- `WitnessViewModelTests.cs:261` leaks a static `SetSecretStore` (cross-test contamination).
- Live hazard: `WitnessKeyStoreLoadResultTests.cs:30-44` writes the developer's real
  `~/.local/share/Syxx/witness.key` and restores it in `Dispose`.
- **`SelfVerify` has zero tests on the one platform that has the affordance.**
  `grep -rn "SelfVerify\|_lastSnapshot\|LastSnapshot" Linux/Ubuntu/SyxxUI.Avalonia.Tests/` → no
  matches. Not the happy path, not the null-snapshot prompt, not the stale case (G-12).
- **The external-then-self contamination of G-15 is untested everywhere, and the test structure hides
  it:** `AppViewModelWitnessTests.cs:380+` uses two *separate* view-models named `sealer` and
  `verifier`; `AppModelWitnessTests.swift:499+` builds a fresh `AppModel()` per case. The defect
  requires **one** instance doing both, which no test does.
- **Windows and macOS have no severity-tier test at all.** Their mappings live in the view layer
  (`WitnessView.xaml.cs:245-265`, `WitnessView.swift:14-53`); grepping `witnessResultDisplay` /
  `IoCriticalRedBrush` / `ioWarningAmber` across `macOS/Tests/` and `Windows/dotNET/SyxxUI.Tests/`
  returns nothing. So both collapses in G-06's caveat are unguarded. Linux is the **only** platform
  that tests the mapping — `WitnessViewModelTests.cs:335-345` is a `[Theory]` over all nine members
  pinning exact ARGB, and `:355-368` asserts `lines.Count == lines.Distinct().Count()`, a genuine
  E021 non-collapse assertion. Weakest runtime behaviour, strongest coverage — the two point opposite
  ways here.
- The non-empty assertion at `WitnessViewModelTests.cs:352`
  (`Assert.False(string.IsNullOrWhiteSpace(vm.VerifyLine))`) is **harmless in place**, because `:351`
  pins the exact hex per outcome and `:355-368` pins distinctness. But the gap it leaves is real: **no
  test anywhere asserts the actual text of any branch.** Distinct-and-correctly-coloured is proven;
  correctly-*worded* is not — two same-tier messages could be swapped and every Linux test stays green.
Fix: make `KeyPath` injectable (test-only seam) and drive all three presence states against a temp
directory — including "missing token file + populated store must NOT mint". Repoint the Windows test
at Local `ApplicationData` with a valid key. Add: `SelfVerify` coverage including the G-12 stale case
(seal A → fail on B → assert no green line, or one naming A); a G-14 assertion that the external
conclusion is reachable from `Idle`; a G-15 single-instance seal→paste→self-verify assertion on both
non-Linux platforms; exact-text assertions per branch; and any tier test at all on Windows/macOS.
Per spine #5, none of these are softened to go green — where a test currently pins wrong behaviour
(G-06 caveat 3), it is the behaviour being corrected, not the bar being lowered.

### 7.2 Falsified hypotheses — recorded so they are not re-litigated (one carries a caveat that does need work)

**G-06 — nothing non-positive is ever rendered green, on any platform.** The dangerous half of the
collapse hypothesis is **falsified, high confidence.** Green is reachable only from
`SnapshotVerification.Valid` on all three: Linux `ApplyVerification`
(`WitnessViewModel.cs:416-437`, brushes `:431-436`, constants `:180-182`), Windows `UpdateValidation`
(`WitnessView.xaml.cs:196-273`, switch `:245-265`), macOS `witnessResultDisplay`
(`WitnessView.swift:14-53`) via `applyWitnessVerification` (`AppModel.swift:2061-2092`). All nine enum
members (`WitnessSigner.cs:441-460`) map to distinct messages in each GUI switch; Windows and macOS
wording is verbatim-identical to each other. Every INCONCLUSIVE-class state lands amber or red.

**Caveat — three collapses do exist, all *inside* the non-green tiers, and all outside the switch the
hypothesis suspected.** E021 is therefore violated, but never in the dangerous direction. These need
work; they are listed here rather than in §7.1 because two of the three sit in reserved files:
1. **macOS carries a second, divergent mapping.** `AppModel.swift:1356-1380` — `runWitness()`, the
   Run-All-Wardens text report — gives `futureDated`, `untrustedSigner`, `displayMismatch`,
   `malformedInput` and `unsupportedFormat` the same `⚠️` as `expired`/`stale`; only
   `invalidSignature` gets `❌`. **Five of six critical states demoted to the caution tier in
   user-visible output.** It also calls `verifySnapshot` rather than `verifySnapshotDetailed`
   (`:1354`) and bypasses `applyWitnessVerification` entirely — so it sits outside the "single source"
   that switch exists to be. Fix: route it through the shared switch and use the detailed verifier.
2. **Windows' default arm reuses the MalformedInput wording verbatim** (`WitnessView.xaml.cs:263-264`
   vs `:259-260`) — an unknown outcome is indistinguishable from an unparseable snapshot. Linux keeps
   a distinct "Could not verify (inconclusive)." (`:429`). Fix: give the default arm its own wording.
3. **Empty paste is not distinguished from malformed bytes.** Linux gives an amber prompt
   (`:322-324`); Windows and macOS both render **red** "the snapshot is malformed" to a user who
   simply clicked before pasting. "You submitted nothing" and "these bytes are not a testimony" are
   forensically different. **Both non-Linux suites pin the collapse as correct** —
   `AppViewModelWitnessTests.cs:433-442` and the empty-paste half of
   `AppModelWitnessTests.swift testExternalVerifyMalformedJsonIsHonestNotCrash` — so fixing it means
   amending tests, which under spine #5 is legitimate only because the test encodes the wrong
   behaviour, not to make a red suite green.

Also a genuine state-space divergence, not just wording: **Linux has no no-local-key state at all**,
where Windows has amber (`:240`) and macOS has `.noKey`. That is a direct consequence of the auto-mint
in G-10, and `WitnessTestifyParityTests.cs:29-30` already records the missing branch.

**G-07 — Linux builds and is gated.** Hypothesis **falsified.**
`dotnet build` on `SyxxUI.Avalonia` succeeds (7 warnings: 1× NU1510, 6× AVLN5001
`TextBox.Watermark` obsolete, all pre-existing); `dotnet test` on the suite is 165/165, 0 failed,
including in CI's exact `-c Release --nologo` form. All five new Kit symbols resolve with matching
signatures. AXAML compiles with CompiledBindings, so a missing command member would be an AVLN
error, not a runtime-silent failure. CI gates Linux twice: `build-test` ubuntu-x64 + ubuntu-arm64
(`ci.yml:94-96`) and the dedicated `linux-gui` job (`ci.yml:317-336`). The working-tree `ci.yml` diff
adds only the warp-corpus drift gate (`:367-374`) — nothing Linux changed this wave.

**G-08 — BYOK accept/reject sets are identical across platforms.** Strong hypothesis **falsified.**
Every save path routes through the platform's single validator, shared with the signer: Linux
`WitnessViewModel.cs:369` → `SaveKey` → `ValidatePrivateKey` (`WitnessKeyStore.cs:257`); Windows
`AppViewModel.cs:718,742`; macOS `AppModel.swift:1892` → `WitnessKeyManager.swift:120`. Sign time
uses the same validator (`WitnessSigner.cs:138`, `WitnessSigner.swift:177`). No unvalidated-persist
path, no save-vs-sign rule drift; the decoders' whitespace quirks agree and are test-pinned on both
sides.

### 7.3 Out of scope here — reserved, and routed elsewhere

These are **real and evidenced**, but every one lands inside `docs/HANDOFF-mirror-warp-gui-2026-08-04.md`
§1 and this plan's own §1 hard keep-out. They are recorded so they are not lost, and explicitly **not
queued for any seat on this team**. §0.1.0 of the handoff additionally gates all Windows/Linux GUI
translation on owner approval of the macOS work.

**G-09 — Windows discards a tampering conclusion behind "CONTENT WARP REQUIRES PRO".** Severity:
**highest of everything found** — it fires on every Pro Windows run and suppresses the one output the
product exists to produce. `SyxxUI/Views/MirrorView.xaml.cs:183` reads the dead
`SectionInputs.Warp`, so `:214-219` falls to `MirrorView.xaml:133-140` "CONTENT WARP REQUIRES PRO"
for a paying user whose run completed, discarding `TAMPERING_DETECTED` without trace.
`ConclusionsView.xaml.cs:128-137` repeats the stale read, so the Conclusions Warp card never appears.
The model layer is already correct (`AppViewModel.cs:529`) — this is strictly the view surface.
macOS was fixed in the working tree (`MirrorView.swift:13,220-239`); Windows was not.
Route to: the **Mirror stream**, which owns `MirrorView.xaml{,.cs}` per handoff §1.1 and is about to
rewrite them wholesale. The mapping to reuse already exists at `MirrorView.swift:404-429`
(`manifestLabel`/`manifestColor`/`manifestDetail`) — VERIFIED_MATCH green, INCONCLUSIVE **amber never
green**, TAMPERING_DETECTED red; `WarpProGate` reserved for `!IsPro` only, plus a distinct
"did not complete" state for Pro-with-null-manifest. `ConclusionsView.xaml.cs` is *not* on any
reserved list, so that half could be split out if the owner wants partial credit sooner.
Corrects `MEMORY2.md:81` — "both GUIs' dead Warp panel wiring fixed" is true only at the model layer
and for the macOS view.

**G-10 — Linux seals with the wrong identity when a key is pasted but not saved.** Severity: high;
fires on every BYOK attempt, the documented flow. `ImportKeyText` occurs only at
`WitnessViewModel.cs:357` and `:387`, both inside `ImportKey`; `SignAsync` (`:211-308`) never reads
it. Windows (`WitnessView.xaml.cs:46`) and macOS (`WitnessView.swift:118`) treat the field as
authoritative on Testify — macOS proceeds only `if model.saveWitnessKey(witnessKeyInput)`.
Compounding, Linux **auto-mints** (`:157-173`) where Windows (`AppViewModel.cs:776`) and macOS
(`AppModelWitnessTests.swift:116`) refuse to seal without a key. Net: paste your key, press Witness
Testimony, receive a valid-looking seal from a machine-generated identity, no warning.
Owner has chosen **strict parity** as the intended fix (refuse to seal with no saved key; refuse
while unsaved text sits in the box; add a Linux "Generate Keypair" affordance, which becomes
*required* once auto-mint is removed). Deliberate behavior change: Linux users currently get an
identity for free and would be asked to create one — which is the point, it makes the identity a
choice.
Route to: the second team contemplated in §1's boundary note (Avalonia ViewModels, after the Mirror
stream lands), or to Copilot's lane per `docs/CLAUDE.md`.

**G-11 — Linux displays the pasted Ed25519 private key in cleartext.** Severity: high (secret
exposure), one-attribute fix. `WitnessView.axaml:55` is a plain `TextBox` with no `PasswordChar`;
Windows uses `PasswordBox` (`WitnessView.xaml:45`), macOS `SecureField` (`WitnessView.swift:109`).
`WitnessViewModel.cs:38-39` asserts "the private key never appears on screen" — the new box falsifies
its own file's stated invariant, so the doc comment must be corrected in the same change.
Route to: same as G-10. Flagged as the cheapest high-severity item in the whole register — a single
attribute — should the owner want it lifted out of the reservation early.

**G-12 — a green "Verified" can sit beside a seal failure, by two routes.** Severity: medium-high.
Violates spine #4 (nothing shown as measured that is not current).

*First, a falsified sub-claim, recorded because it was the obvious suspect:* `SignAsync` **does** clear
`VerifyLine` at `:216`, and both the RG-2 catch (`:261-265`) and the generic key catch (`:266-271`) sit
below it — so no uncleared catch strands a green line. Two smaller things survive that: `:216` clears
`VerifyLine` but **not** `VerifyBrush` (harmless today, since the only consumer is the `Foreground` of
an empty `TextBlock` at `axaml:32-33`, but a latent trap for any future element bound to the brush
without gating on text emptiness); and one return precedes all clearing — `:214`
`if (IsRunning || string.IsNullOrWhiteSpace(Domain)) return;` makes an empty-domain press a silent
no-op that clears nothing (deliberate, documented as D103 at `WitnessTestifyParityTests.cs:28-32`).

**Route A — no stale snapshot needed.** Any failed seal leaves `HasResult` true (`:101` counts
`Failed` as having a result, per B195), so the panel stays visible with the failure text. Paste a
valid foreign testimony → "Verify Testimony" → `VerifyExternal` (`:317-344`) writes
`VerifyLine`/`VerifyBrush` and touches **neither** `SignatureLine` **nor** `TestimonyState`, so green
*"Verified: the seal is valid and the testimony is intact."* lands two rows under the failure message
inside the same bordered block. Reverse the order and a red foreign finding paints over your own good
seal, reading as a tamper accusation against it.

**Route B — stale `_lastSnapshot`.** Assigned at exactly one site (`:287`, after a successful seal) and
**never cleared** — no null assignment exists anywhere in the file, and Linux has no Free/reset command
at all, unlike Windows `WitnessFree` (`AppViewModel.cs:927-944`, nulls at `:944`) and macOS
`witnessFree` (`AppModel.swift:2094-2108`, nulls at `:2104`). Seal domain A → change Domain to B →
seal fails (`:249`) → press "Re-verify last seal" (`axaml:35`, still visible) → green *"Verified…"*
about A, with the domain box showing B.

**Compounding both: Linux's verification output has zero attribution.** `SelfVerify` never consults
`Domain`, and no `ApplyVerification` message carries a domain, timestamp, or signature fragment — so
nothing on screen says *which* testimony was judged. Windows prints the snapshot's domain and timestamp
(`WitnessView.xaml.cs:204-206`), macOS prints both (`WitnessView.swift:274-282`). Linux prints neither.

**Fix:** clear `_lastSnapshot` **and** `VerifyBrush` at `:216` (or disable the `axaml:35` button when
`TestimonyState == Failed`, so the affordance cannot outlive its subject); give the self-verify and
external paths **separate sinks** — have `ApplyVerification` *return* `(message, brush)` so each caller
writes its own property pair, which keeps wording and tier single-sourced (the real E021 guarantee)
while stopping the two conclusions from overwriting each other; and prefix every conclusion with the
snapshot's `TargetDomain` + `Timestamp`, which `_lastSnapshot` already carries. Route to: same as G-10.

**G-14 — a Linux tampering finding can be rendered NOWHERE AT ALL.** Severity: **high** — this is the
honesty spine failing outright, and it is the most serious Linux-side defect in the register.
The only sink for `VerifyLine`/`VerifyBrush` is `axaml:32-33`, inside the panel gated at `axaml:24`
`IsVisible="{Binding HasResult}"`. `HasResult` is `Signed or Failed` (`:101`), so it is **false until
something has been sealed** — and `VerifyExternal` never sets `TestimonyState`. The external-verify
Border (`axaml:67-80`) contains **no output element of its own**: header, description, `TextBox`,
`Button`, nothing more. Its comment at `axaml:65-66` states it "Renders into the same
VerifyLine/VerifyBrush the self-verify path uses" — which is precisely the defect.

So: launch, go to Witness, seal nothing, paste a testimony with one altered signature byte, press
"Verify Testimony" → `ApplyVerification` sets red *"INVALID signature: the seal does not verify
(tampering)."* into a collapsed control. **No output.** A user who opens the app solely to check
someone else's testimony — the whole point of the external pane — presses the button and sees nothing.
Verified: the state write, the gate, and the absence of any second sink. *Inferred (no GUI executed):*
that the collapsed panel renders nothing rather than something unexpected.
**Fix:** give the external Border its own output `TextBlock` gated on **its own** text being non-empty.
The correct pattern is already in this same file at `axaml:61`, where `ImportStatusLine` uses
`StringConverters.IsNotNullOrEmpty` rather than unrelated lifecycle state. Nothing forensic should be
reachable only after an unrelated action. Route to: same as G-10.

**G-15 — Windows and macOS present a FOREIGN testimony as this machine's own.** Severity: high.
**This inverts the parity direction this plan assumed — here Linux is the reference implementation and
the other two are the defects.** The external path overwrites the *same* field self-verify reads:
Windows `AppViewModel.cs:918` `WitnessSnapshot = parsed;` inside `WitnessVerifyExternal`, while
`WitnessVerify` reads `WitnessSnapshot` at `:864-867`; macOS `AppModel.swift:2051`
`witnessSnapshot = snap` inside `witnessVerifyExternal`, while `witnessVerify` guards on it at `:2001`.
Sequence: seal locally → "Verify pasted testimony" → "Verify Testimony" → the self-verify button
re-verifies the **foreign** snapshot and the report presents it as this machine's testimony. Linux
cannot do this: `SelfVerify` reads the private `_lastSnapshot`, which only a real seal writes (`:287`).

Both panels then fabricate a composite that never existed, contaminating **opposite halves**:
- macOS `AppModel.swift:2052` also sets `witnessSignature = snap.signature` (foreign), and
  `WitnessView.swift:224-233` renders "Creating signed Witness Testimony for *your* domain via *your*
  resolver" with the foreign signature shown at `:243`. The comment at `:2049-2050` shows the shared
  write is deliberate — so this is a design oversight, not a slip.
- Windows keeps your signature but `WitnessView.xaml.cs:173` takes the resolver from
  `_vm.WitnessSnapshot?.Comparison?.LocalServer` — now the **foreign** resolver — into "Creating
  signed Witness Testimony for {Domain} via {resolver}" at `:177`, beside your signature. This
  contradicts its own comment at `:160`: *"never fabricate a domain or resolver."* Spine #4 violation,
  self-documented.

To their credit both clear on re-seal — Windows `ShowValidation=false` (`:762`) + `WitnessSnapshot=null`
(`:765`), macOS more thoroughly at `:1936-1941` including `witnessResult` — and macOS explicitly nulls
the stale self-snapshot on a malformed paste (`:2043-2044`). Only Linux lacks any reset.
**Fix:** give the external path its **own** snapshot field on both platforms; reserve
`WitnessSnapshot`/`witnessSnapshot` for seals this machine made. That also fixes both fabricated
composites, which read the shared field only because it was overloaded. Route to: reserved —
`WitnessView.xaml{,.cs}` is handoff §1.3, `AppViewModel.cs` is §1.5 additive-only, macOS
`WitnessView.swift` is §1.3.

**G-13 — copy and catalog drift.** Severity: low. Invalid-key copy is not equivalent: Linux names the
cause and says nothing was saved; Windows and macOS collapse invalid-material and store-write failure
into one message; only Linux prompts on empty input. Catalog: Linux's external-verify button reads
"Verify Testimony" (`WitnessView.axaml:77`) but is catalogued with the Windows string; BYOK header
case differs; Windows/macOS save-failure copy is uncatalogued. Per house rule 2, reconcile by
**appending** only. Route to: Stage-5 wording is Claude's lane, but the strings live in reserved GUI
files — sequence after G-09/G-10 land.

### 7.3a Q-01 — RESOLVED 2026-08-04: confirmed, but conditional and fail-open by default

**Question was:** could an auto-minted identity fail its own trust set, so self-verify of a just-sealed
snapshot returns `UntrustedSigner` — a red critical line directly under "Sealed."?

**Answer: the mechanism is real and every link is now read on the C# side, but it CANNOT fire on a
default machine.** All four links verified:
1. C# **does** auto-load a trust set — `WitnessSigner.cs:240`,
   `trustedPublicKeys ??= WitnessPinning.ConfiguredTrustedWitnessKeys().ToList();` (B211; its comment
   notes that without this "the trust check is inert in every production caller"). The macOS comment at
   `AppModel.swift:2013-2018` was accurate.
2. Self-verify callers **pass no trust argument** — Windows `AppViewModel.cs:867` (self) and `:919`
   (external); Linux `WitnessViewModel.cs:305`, `:343`, `:408`.
3. **The qualifier that decides it — `WitnessSigner.cs:345` is fail-open.** The check requires
   `trustedPublicKeys.Count > 0`, and the comment at `:342-344` states "Empty/unconfigured set = no
   restriction (fail-open, until Phase 1b)". The set is populated **only by the user** — env
   `SYXX_WITNESS_TRUSTED_KEYS` (`:402`) falling back to
   `LocalApplicationData/Syxx/witness_trusted_keys.txt` (`:422`) — and a repo-wide grep found **no
   production writer on any platform**; the only writes are macOS test setup
   (`AppModelWitnessTests.swift:461,:510`). So on a fresh machine the set is empty, the check is
   skipped, and an auto-minted identity does not accuse itself.
4. **When a set IS configured, it fires exactly as feared.** Auto-mint at
   `WitnessViewModel.cs:157-173` never adds the new public key to any trust set; `:289` writes
   "Sealed. Signature: …" and `:305` immediately self-verifies; `ApplyVerification` maps
   `UntrustedSigner` (`:424`) through the `_ =>` default (`:435`) to `CriticalBrush` red `#FF4D6D`
   (`:182`). Windows is the same at `WitnessView.xaml.cs:253-254`.

**Severity is therefore inherited from G-01/G-02, which is the useful conclusion.** The realistic
trigger is not a bare fresh install but a **chain**: a user who has configured a trust set containing
their own key, whose key is then misreported as `Absent` by the `File.Exists`/`HasKey` weakness in
G-01/G-02, gets a new identity minted whose key is not in their set → red `UntrustedSigner` on a
perfectly good local seal. **Fixing G-01/G-02 removes the common path into Q-01.** Filed as resolved,
not as a separate work item.

*Standing design concern, needs an owner and touches reserved files:* "the signer is not in your trust
set" is a **trust-posture statement, not tamper evidence**, yet it renders in the same critical-red tier
as `InvalidSignature` and `DisplayMismatch`. That reads as an accusation. It belongs to the G-14
render-gating conversation, not here. (Minor, verified benign: the trust comparison is an exact base64
match with no normalisation (`:345`), but the splitter at `:409-411` handles comma/space/newline/tab, so
a trailing newline in the key file is harmless.)

### 7.3b Found during the build pass — owner decisions, not seat work

**G-16 — the libsecret P/Invoke call passes attribute VALUES into the NAME slots.** Severity: high.
**This is the root cause of the G-02b residual**, and it is pre-existing — no seat introduced it.
`LinuxSecretStore`'s delegate is
`(schema, collection, label, password, cancellable, out error, attrK1, attrV1, attrK2, attrV2, terminator)`,
and both `Store` and `Retrieve` pass `keyTypePtr, keyIdPtr` into **attrK1/attrV1**. libsecret varargs are
`(name, value, …, NULL)`, so the attribute **name** becomes the literal string `"witness"` and its
**value** becomes the keyId. The schema names declared at `:390-391` (`syxx-key-type`, `syxx-key-id`) are
never passed by that call, while `SecretTool` (`:527-529`) uses them correctly.

Two consequences: the two keyring backends **cannot see each other's secrets** (P/Invoke round-trips with
itself, so nothing looks broken in isolation), and libsecret may reject attributes absent from the
declared schema. And critically — `SecretTool.Exists` searches `syxx-key-type`, so it can *never* see a
P/Invoke-written secret, which forces `LinuxSecretStore.Exists` to answer `Unknown`, which drives the arm
that licenses minting. **Fixing G-16 is upstream of closing G-02b.**

**Deliberately NOT fixed in the build pass, for three reasons that should survive into whoever picks it
up:** (1) unverifiable from a macOS host — there is no libsecret here, and a keyring fix landed blind is
how a confident wrong answer ships; (2) **migration hazard** — correcting the names makes every
already-stored P/Invoke secret unfindable by the corrected `Retrieve`, and with G-01/G-02 in place that
user lands on `Undetermined` and **cannot mint, i.e. is locked out of signing entirely**, so a legacy
read path must exist before the write path changes; (3) it changes how secrets are persisted, which is an
owner call. Note also `secret_password_search_sync` was initially proposed as the G-02b fix and is
aimed at the wrong layer — a search by `syxx-key-type` finds nothing while `Store` files under `"witness"`.

*Provenance note worth keeping:* the auditor initially cleared these schemas as matching, having read the
schema **declaration** and credited it to the **call site**. That is the same class of error as the
vacuous tests it was hunting — the artifact exists, so it is assumed to be in force. When a varargs or
P/Invoke call is load-bearing, read the call against its own signature and count arguments into slots.

**G-16b — `macOS/Sources/SyxxKit` does not compile in-tree, blocking ALL Swift verification.**
`macOS/Sources/SyxxKit/DNSPathConfirmation.swift:90` — `type 'DNSPathSnapshot' does not conform to
protocol 'Equatable'`. The file is **untracked** (`??`, no git history, mtime 11:59 pre-team) — another
stream's in-flight work. Not a seat regression, and deliberately not fixed: editing another agent's
untracked work is the destroy-work-that-exists-nowhere-else case. **Consequence: no Swift test can run at
any scope**, which is why G-03's Swift leg is `unverified` rather than verified, and why the house-rule-5
`swift build --package-path macOS --target <Target>` workaround no longer helps — this is a *different*
failure from the `DeepSyxx-Info.plist` link error. Needs claiming or fixing before anyone touches SyxxKit.

**G-17 — macOS `hasKey` collapses `.undetermined` → `false`.** Severity: medium. Same defect just fixed
on the .NET side by G-02a. `macOS/Sources/SyxxKit/WitnessKeyManager.swift:42-47` maps `.undetermined` to
`false` while its own doc two lines above states "false now genuinely means confirmed absent" — so an
unreachable Keychain reads as "no key". macOS has no load-or-create path, so it does not rotate an
identity, but it does produce the misleading "A signing key is required" instead of "store unreachable".
Spine #3 (tri-state, never boolean) and spine #6 (parity) both point at fixing it.

**Housekeeping, low priority:** ~50 orphaned `license_*` blobs sit in `~/.syxx/keystore` from
LicenseManager tests — the same orphan pattern G-01 now guards against in `SaveKey`, pre-existing and a
different owner's.

### 7.4 Adjacent, different owner — currently red

`scripts/cross-platform-parity-gate.py` fails: "wire vocabulary partially drifted
(tracer_outcome)". `macOS/Sources/SyxxKit/Synthesis/CrossVantageConsensus.swift:38` `Outcome` is
{insufficient, match, mismatch, noData} against canon {failure, inconclusive, success, unclassified,
untested}. Committed and unmodified this wave; macOS↔Windows Kit, not a Linux gap. Route to whoever
owns `SyxxTracer.Outcome`. Recorded because it is red *now* and will be mistaken for this team's
breakage.

---

## 8. Real-run verification — the owner is the execution surface

The owner **can run Linux**. This host cannot build right now. Those are two different
constraints and the plan depends on keeping them separate:

- **Build/test execution is blocked at authoring time.** No phase in §6 executes until the
  owner says building is available. Everything below is queued, not run.
- **Real-run verification is available on demand, via the owner.** Every acceptance bar that
  needs real Linux behavior — `resolvectl` on a systemd-resolved box, `ip neigh` on a
  populated table, WireGuard `wg0` detection, libsecret against a live keyring, `.deb`
  install, snap strict confinement — **is closable**, on the owner's machine.

### 8.1 The verification pack — a required deliverable, not a courtesy

Because the owner runs the commands and this team reads the results, the team cannot verify by
watching. It must therefore ship, per phase, a **verification pack**: one copy-pasteable
block the owner runs, plus a pre-declared expectation table.

Rules that make this trustworthy rather than theatre:

1. **Expectations are written down BEFORE the owner runs anything.** For each command: the
   output that means pass, the output that means fail, and — critically — the output that
   means *inconclusive*. A three-way expectation, matching the product's own tri-state rule.
2. **Every bar names its false-clean.** For each check, state the specific wrong output that
   would look like success. Example shape: for the Linux VPN probe, `Not detected` while a
   tunnel is up is the failure being hunted; `Unknown` is an acceptable pass, `No` is not.
   Without this, a Linux box that silently answers "fine" reads as a green bar.
3. **Raw output goes in the ledger verbatim.** Not a summary, not "confirmed working." The
   owner pastes back stdout/stderr and exit code; the auditor reads it and records it.
4. **No bar is closed on inference.** If the owner's paste does not clearly discriminate
   pass from inconclusive, the bar stays open and the pack gets a better command. The team
   does not close a bar because the output "looks right."
5. **Packs are self-contained and cheap.** One block, no multi-step interactive setup, no
   assumption about what is installed beyond what the pack itself checks first.

### 8.2 Division of labour with CI

CI is not a substitute for the owner's box; it is the part that must never regress silently.

- **CI's job — mechanical, every push.** `.github/workflows/ci.yml` already runs both
  `ubuntu-latest` (linux-x64) and `ubuntu-24.04-arm` (linux-arm64), but today those runners
  only prove the code *compiles and publishes*. E3 adds a job that **executes the Linux CLI
  and asserts on its output**, so every bar CI *can* hold becomes a required status instead of
  a prose claim.
- **The owner's box — what a stock runner cannot present.** A live VPN tunnel, a desktop
  keyring with a real D-Bus session, systemd-resolved in a desktop configuration, a populated
  neighbour table, `.deb` install on a desktop image, snap confinement.

### 8.3 Ledger states

Three states only, per bar: **verified** (real output pasted and recorded), **CI-enforced**
(a required status holds it), **unverified**. An unverified bar is never reported as parity —
the same rule this product applies to its own users.

---

## 9. Decisions needed from the owner

1. **Avalonia ViewModels in or out?** §1 reads "leave the GUI alone" as excluding them
   entirely. Confirm.
2. **`teammateMode`** — tmux or in-process.
3. **Deletion of `Linux/dotNET/Syxx.Linux/`** — this is a `git rm`, so confirm before Phase 1.
4. **Linux target details** — distro, release, and desktop environment; arch (x64 or arm64);
   whether systemd-resolved is active; whether a keyring/D-Bus session is present. These
   determine which verification packs are meaningful and which bars CI can hold instead.
5. Any macOS capability the owner considers *not* required on Linux, so it can be marked
   deliberately-out rather than sitting in the ledger as a gap forever.
6. **Build availability** — every phase in §6 is queued pending the owner's go-ahead. The
   verification packs of §8.1 can be authored and reviewed before then, since they are
   documents rather than builds.

Added 2026-08-04 with the §7 gap register:

7. **G-09 routing — the one that matters most.** The highest-severity defect found anywhere in this
   pass is Windows-only and sits in a reserved file: every Pro Windows run shows
   "CONTENT WARP REQUIRES PRO" and discards a `TAMPERING_DETECTED` conclusion
   (`MirrorView.xaml.cs:183`). Handoff §1.1 reserves that file for the Mirror stream's imminent
   wholesale rewrite, and §0.1.0 gates Windows/Linux GUI translation on your approval of the macOS
   work. Three options: (a) hand G-09 to the Mirror stream as a rewrite requirement — cleanest, but
   it ships whenever that stream ships; (b) split out `ConclusionsView.xaml.cs`, which is on **no**
   reserved list, for partial credit now; (c) lift the reservation for this one fix. Recommendation:
   (a) plus (b). Until one is chosen, a completed tampering finding keeps being suppressed on Windows.
8. **G-11 — lift early?** Cheapest high-severity item in the register: the Linux import box shows the
   pasted private key in cleartext, and the fix is a single `PasswordChar` attribute. It is reserved
   only because §1 puts *all* `.axaml` out of scope, not because any stream owns it. Say the word and
   it can be lifted out with the doc-comment correction that must accompany it.
9. **Second team for Avalonia ViewModels?** §1's boundary note anticipated this; §7.3 now gives it a
   concrete backlog (G-10, G-11, G-12, **G-14**, and the Linux half of G-13). Confirm whether that team
   is wanted after the Mirror stream lands, and whether it is Copilot's lane per `docs/CLAUDE.md`.
   Note **G-14 is arguably as serious as G-09** and is the cheapest of the two to fix: a Linux user who
   opens the app purely to check someone else's testimony currently gets **no output at all**, and the
   remedy is one `TextBlock` using a pattern already present ten lines away in the same file.
10. **Parity direction needs restating in §1.** G-15 establishes that on foreign-snapshot handling
    **Linux is the reference implementation and Windows/macOS are the defects** — they let an external
    paste overwrite the field self-verify reads, then render a composite of your domain with a foreign
    signature (macOS) or your signature with a foreign resolver (Windows). This plan is framed
    throughout as "Linux → macOS parity". That framing is wrong for at least this axis and should not be
    applied mechanically: two of the fixes in §7.3 move macOS and Windows *toward* Linux's design.
    Worth deciding whether this plan's title and §1 goal statement get amended, or whether G-15 is
    simply routed out as a non-parity defect.
11. **Strict-parity behavior change (G-10) confirmed?** Removing Linux's silent auto-mint means
    first-run users are asked to create an identity instead of receiving one. Already chosen as the
    intended fix; recorded here because it is user-visible and needs to survive review, and because
    it makes the Linux "Generate Keypair" affordance **required** rather than optional.

---

## House rules inherited (bind every seat)

1. The word "verdict" is banned. `result` in code, `conclusion` in UI/docs. Sole exception is
   the pre-existing signed wire field `evidence.comparisonResult.verdict` — never renamed,
   never introduced anywhere new.
2. Every new user-facing string is **appended** to both output catalogs
   (`outputs-catalog-general-mode.csv`, `outputs-catalog-story-mode.csv`). Append only —
   never sort, dedupe, reformat, or edit existing rows.
3. Honesty protocol: `Idle / Running / Completed / Failed`. Failed ≠ never-ran ≠ match. Never
   render a clean or positive state for something that did not run.
4. Run .NET tests from the test project directory, not the repo root.
5. `swift build`/`swift test` at package scope currently fails on the `DeepSyxx-Info.plist`
   link step. That is the Cage agent's lane and is expected — do not fix it. Scope builds:
   `swift build --package-path macOS --target <Target>`.

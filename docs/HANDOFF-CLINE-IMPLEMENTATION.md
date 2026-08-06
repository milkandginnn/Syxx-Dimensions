# HANDOFF — Cline — Delta implementation (2026-07-20)

From: Claude. Basis: a 19-agent source audit followed by a per-delta verification pass in
which **every open delta was checked against the live tree**, not against its status tag.
460 verdicts. The evaluation comments are already in `DELTAS.md`, dated 2026-07-20, one per
open delta.

**Read this file before `DELTAS.md`.** Four things about the ledger will actively mislead you
if you go in cold, and they are listed under "Traps" below.

---

## 1. What state the ledgers are in

| File | Contents |
|---|---|
| `DELTAS.md` | 357 open deltas. 293 pre-existing (all verified LIVE / PARTIAL / UNVERIFIABLE) + 64 filed by this sweep. Every one carries a dated Claude evaluation. |
| `DELTAS-fixed.md` | +163 deltas relocated this pass, each verified absent from the live tree. |
| `DELTAS-rejected.md` | +2 (A129 misfiled, A076 non-reproducing), each with a stated `Rejection reason`. |

Verification tally across the 460:

| | FIXED | LIVE | PARTIAL | UNVERIFIABLE |
|---|---|---|---|---|
| A Server | 69 | 49 | 12 | 1 |
| B Verdict & Evidence | 45 | 55 | 18 | 0 |
| C Detection Efficacy | 15 | 30 | 6 | 1 |
| D Security & Hardening | 12 | 52 | 12 | 0 |
| E Platform Integration | 16 | 27 | 4 | 0 |
| F Miscellaneous | 10 | 20 | 5 | 1 |

Everything still in `DELTAS.md` is work. Nothing in it was left there for lack of checking.

---

## 2. Traps — read these first

**2.1 ~92 ledger `Location:` fields cite directories that no longer exist.**
`macOS/Sources/SyxxWitness|SyxxLeak|SyxxWarp|SyxxRoute|SyxxMirror|SyxxNSLook/` and
`Windows/SyxxCage|SyxxWitness|SyxxLeak|SyxxWarp|SyxxRoute/`. The code consolidated into
`macOS/Sources/SyxxCLI/Commands/` and `Windows/Syxx/Commands/`. **A dead path does not mean
the defect is gone.** B024 was closed twice against `macOS/Sources/SyxxWarp/SyxxWarp.swift`
and the fix was lost in the move — it is live today at
`macOS/Sources/SyxxCLI/Commands/SyxxWarp.swift:34,37,40,42`. When you hit a dead path, find
the moved code.

**2.2 `DELTAS-fixed.md` is not a reliable record of landed fixes.**
Lines 597-700 hold ~174 adjudications where `AGREE` means *the defect is real*, misfiled as
fixes. The `MACX/WINX` block is headed `[partial]` and states its own tail was "not
individually re-verified". Confirmed still-live entries sitting in that file: B024, GUIA-30,
GUIB-24, KTRU-29, WINX-154, MACX-196, D010, D011, D062, D065, D066, D077.

**2.3 `DELTAS-rejected.md` is wrong in the more dangerous direction.**
D014(a), D018, D021, D026, D027 are rejected with the defects fully live. B085, B089, B091,
B092, B094, B099 are verbatim copies of open entries, still tagged `CONFIRMED`, all three
adjudicators recorded as Agree, and **none carries the `Rejection reason` its own file header
requires** — they were bulk-moved, not adjudicated. B085 and B091 have fixes in the tree
commented `// B085:` and `// B091:`; you cannot have rejected a delta you then fixed.
A rejected delta gets no re-audit, so this is the highest-risk file.

**2.4 Two entries are double-assigned with conflicting verdicts.**
`D014(a)` LIVE / `D014(b)` FIXED, and `D015(a)` PARTIAL / `D015(b)` FIXED. They were
deliberately not relocated. Split them before touching either.

---

## 3. Do these first — verdict-integrity chain

These five compose into a single silent-erasure path. Fixing any one alone leaves the chain
intact, so schedule them together.

1. **B151** — section completeness hardcoded `1.0` on macOS, real `N/M` on Windows.
   Measured: identical evidence yields **CLEAN 56% on macOS, INCONCLUSIVE 7% on Windows**.
   *Start here.* Port `Syxx.Tests/SyxxKitTests.cs:3084-3119` (`SectionCompletenessTests`) to
   Swift **first** — it fails immediately against the unfixed code, which is your mutation
   proof, and establishes failing-test-as-data before the fix lands.
2. **B154 + B155** — the captive-portal dampener applies at reliability 1.0 while the evidence
   it suppresses is discounted to 0.5, and the anti-cancellation guard keys on section status
   so the answer-matrix discrepancy channel is unprotected. A *confirmed* cross-vantage
   discrepancy is normalised to zero.
3. **B152** — `!decisiveActTriggered &&` short-circuits every referral condition including
   `hardGated`, so nothing surfaces when the above fires.
4. **B153** — `scriptMismatch` is decisive on a heuristic HTML diff whose catalog comment
   describes an SRI check that does not exist. Ad rotation produces an unqualified tampering
   verdict with referral suppressed.
5. **B136** — Windows Mirror renders a clean section from an empty comparison. macOS fixed
   this on 2026-07-16 (ROADMAP TOP-TEN #1); the port never landed. Ubuntu links the Windows
   assembly, so this is a live Linux defect too.

**Design decisions inside this set — do not invent the rule.** Whether a decisive act may
suppress the referral gate at all (B152), and whether `scriptMismatch` belongs in the catalog
(B153), are contract questions. Route to `syxx-neural-planner` before implementing.

---

## 4. Then — forgeable evidence

- **D083** — the witness display-integrity guard omits `timestamp`, `expiresAt` and
  `formatVersion`, and *sources* the timestamp from the signed payload rather than comparing
  against it. Edit the JSON date on a genuine snapshot and it still prints VALID.
- **D086 / D087** — `--gen-keypair` prints the Ed25519 private key to stdout; `--fetch-pubkey`
  accepts a trust anchor over plain HTTP with the HTTP status discarded. AGENTS.md §3 names the
  second verbatim as the D011 required failure case.
- **B157** — `SignedIncidentReport` prints `MATCHES — Answers match reference` for a comparison
  with zero records on both sides. Same defect `EvidenceAdapters.mirror` was patched for; the
  fix never reached the signed report.
- **D084 / D085** — canonicalization and base64 strictness diverge, so genuine cross-platform
  evidence fails verification *as tampered*.

---

## 5. Then — Linux

Ubuntu project-references `Windows/Syxx.Kit`, so every Windows-API-only path is a live Linux
defect. **E061 is a sequencing prerequisite**: the C# `ScopedResolver` has no transport member,
so the model literally cannot represent an encrypted resolver. Land E061 before the WS-C1
encrypted-DNS work or you will waste a cycle.

Then **E053** (culture-sensitive comparison and formatting, ~40 sites — `InvariantGlobalization`
is unset, so this is live not latent), **C053 / C054** (Linux persistence markers absent; nine
PowerShell markers unreachable from a case mismatch).

---

## 6. The one review rule worth more than most individual fixes

**D081** collects ~40 sites of one pattern: a bare `catch`/`guard` yielding a value the caller
cannot distinguish from a real measurement. `LanCartography.cs:357` returns a snapshot with
`isPartial` defaulting to **false** while the timeout path two lines below sets it **true** —
the honest branch was written and the catch bypasses it.

Adopt the rule rather than only the 40 fixes: *no bare `catch` or `guard` may yield a value a
caller cannot distinguish from a real measurement.*

---

## 7. Not for this handoff

- **Copy and wording** (~20 items) — needs the user's approval per AGENTS.md:42, not a code
  change. Belongs in `PROPOSALS.md`.
- **Spec decisions** — CDN dampener (already filed as **C015**), beacon minimum-n, decisive-act
  referral suppression, Ubuntu palette, IPv6 LAN scope. Route to design.
- **Corpus work** — the Stage-0 gaps (NE5-d1, NE7-d5, four missing mechanism classes, absent
  `capture` provenance field) are **Cline's lane already** per `HANDOFF-CLINE.md:33` and
  `CL-IMPLEMENTATION-PLAN.md:258-270`. Named here so it is not done twice.
- **The 127 cross-file duplicate IDs** — deltas were historically *copied* between ledgers, not
  moved, so one ID can carry three contradictory statuses. Deciding which copy is authoritative
  is a judgment call per entry. Flagged, not touched.

---

## 8. Status corrections owed on existing entries

| Delta | Action | Why |
|---|---|---|
| **B083** | close — move to `DELTAS-fixed.md` | Fixed in the tree on both platforms, still sitting open |
| **A091** | close | Row-cap fix landed at `db.py:599`; the residual is filed as **E058** |
| **B024** | do **not** move to fixed | Marked resolved-pending-move; the fix was lost in a refactor |
| **B016** | reopen | Closed as "superseded by DST"; the nondeterminism reproduces under the shipped DST engine at `Scorers.swift:112` |
| **E022** | re-rate Medium → Critical | Predates the B070 off-name filter; the macOS silent label-drop now defeats a live security control |
| **B035** | reopen | The applied fix rewrote `contains([...])` as an `\|\|` chain; the whitelist is byte-for-byte identical |
| **E013** | reopen and rescope | Marked fixed; live on Windows **and has since spread to macOS**, so its stated basis ("macOS differs") is now false |
| **E017(a)** | rescope | Parity was reached by *deleting* the working half — neither platform now emits the TTL-anomaly evidence item |
| **F020** | re-rate and re-domain to C | Filed Low; it is the structural cause of the C005 divergence class |
| **A065, A058, D079, D073** | re-rate | Verified live but tagged Critical on bases the evidence does not support — see their evaluation comments |

---

## 9. Three verdicts that need a real run before you act on them

Source analysis could not settle these, and the verifying agent said so rather than guessing:

- **D080** — whether a live XAML binding exists to trigger the cross-thread crash
- **D070** — whether the blank panel is the user's actual symptom
- **D058** — whether SwiftUI re-evaluates the representable

Also unexecuted: no Windows or Avalonia build ran during this audit (macOS host), so **every
Windows and Ubuntu verdict is a source read**. `E011` byte-identity in particular is correct on
both sides by inspection but has no cross-platform fixture and was never executed.

### Note from User on first run

At first run, the app should load the license agreement and give the end user an opportunity to explicitly accept or decline. The software should not be visible and you should not be able to interact with the software, directly or indrectly, until and unless the "agree" button is clicked. If the decline link is clicked, all aspects of the app should immediatly close. If the agree button is clicked, the next presentation should be of the guided setup screen.

Options for the guided setup screen should include: (1) Story Mode or Non-Story Mode; (2) Preferred resolver; (3) information as to whether DNS traffic is currently encrypted (at the time the screen loads); (4) if DNS queries are unencrypted, whether the user wants their DNS queries encrypted by DNS over HTTPS/3; (5) whether the user wants continuous monitoring of DNS trust conditions on device; and (6) if so, whether the user wants notifications of changes to trust conditions on device. The Software begins loading only after the guided setup screen has been completed and the user clicks Begin," OR after the user selects "Skip -- use defaults".

If the user chose continuous monitoring,the menu bar icon should display and the popover should display below it upon click (macOS). Equivalent behavior for Windows and Linux installations. The main window should display with the entry point being the NMOTD.

---

## 10. The bar

Per AGENTS.md: one real run with honest output beats a green suite. Several defects in this
handoff exist *because* a green suite stood over a broken path —
`Syxx.Tests/SyxxKitTests.cs:15` excludes the network-touching functions, so
`VantageComparer.CompareVantagePointsAsync` throws on every invocation on Windows and Ubuntu
with zero test coverage; the server's paywall can be deleted entirely and the suite stays
217/217 green.

Do not close anything on a passing test alone.

---

## 11. Per-delta disposition roster

Derived from the per-delta verification. Every open ID appears exactly once.

### 11.1 Implement as filed — 290

Verified LIVE at the cited location, or filed this pass with the location verified. No revision needed; the delta text matches the tree.

**A** A025 A026 A030 A031 A032 A035 A036 A040 A041 A042 A043 A044 A045 A048 A050 A051 A053 A060 A066 A077 A081 A084 A085 A093 A094 A099 A101 A106 A107 A111 A114 A115 A116 A117 A118 A119 A120 A121 A122 A123 A124 A125 A126 A127 A128 A130 A131 A132 A133 A134 A135  **B** B007 B016 B024 B041 B042 B043 B047 B053 B054 B055 B056 B057 B058 B062 B064 B067 B074 B075 B076 B077 B078 B079 B080 B081 B082 B084 B086 B087 B089 B090 B092 B093 B094 B095 B096 B099 B100 B129 B130 B131 B132 B133 B134 B135 B136 B137 B138 B139 B140 B141 B146 B148 B149 B150 B151 B152 B153 B154 B155 B156 B157 B158 B159 B160 B161 B162 B163 B164 B165 B166 B167 B168 B169 B170 B171 B172 B173 B174 B175 B176 B177 B178 B179 B180 B181 B182 B183 B184 B185 B186 B187 B188 B189  **C** C001 C010 C015 C019 C020 C021 C022 C023 C024 C027 C029 C030 C031 C032 C034 C035 C036 C038 C040 C041 C042 C043 C044 C045 C046 C047 C048 C049 C050 C051 C052 C053 C054 C055  **D** D010 D018 D019 D020 D021 D022 D023 D024 D025 D026 D027 D028 D029 D030 D031 D033 D034 D035 D036 D037 D038 D039 D041 D042 D043 D045 D051 D052 D053 D054 D055 D056 D059 D060 D061 D062 D063 D064 D065 D066 D067 D068 D072 D074 D075 D076 D077 D078 D081 D082 D083 D084 D085 D086 D087 D088  **E** E008 E012 E013 E016 E019 E022 E024 E026 E027 E030 E031 E033 E035 E036 E037 E038 E039 E040 E041 E045 E046 E047 E048 E049 E050 E051 E052 E053 E054 E055 E056 E057 E058 E059 E060 E061  **F** F003 F009 F016 F018 F019 F021 F022 F023 F025 F026 F027 F029 F030 F031 F032 F033 F034 F035 F036 F037

### 11.2 Implement with revisions — 59

Partially remediated, or the delta is real but its stated basis, severity or proposed fix is wrong. Read the 2026-07-20 evaluation comment before starting — it names what changed.

- **A012** — revise — scope to the outstanding half
- **A027** — revise — scope to the outstanding half
- **A028** — revise — scope to the outstanding half
- **A034** — revise — scope to the outstanding half
- **A049** — revise — scope to the outstanding half
- **A058** — revise — stated basis/severity or applied fix wrong
- **A065** — revise — stated basis/severity or applied fix wrong
- **A072** — revise — scope to the outstanding half
- **A073** — revise — scope to the outstanding half
- **A080** — revise — scope to the outstanding half
- **A083** — revise — scope to the outstanding half
- **A087** — revise — scope to the outstanding half
- **A095** — revise — scope to the outstanding half
- **A097** — revise — scope to the outstanding half
- **B006** — revise — scope to the outstanding half
- **B010** — revise — scope to the outstanding half
- **B014** — revise — scope to the outstanding half
- **B020** — revise — scope to the outstanding half
- **B026** — revise — scope to the outstanding half
- **B034** — revise — scope to the outstanding half
- **B035** — revise — stated basis/severity or applied fix wrong
- **B039** — revise — scope to the outstanding half
- **B040** — revise — scope to the outstanding half
- **B045** — revise — scope to the outstanding half
- **B061** — revise — scope to the outstanding half
- **B065** — revise — scope to the outstanding half
- **B066** — revise — scope to the outstanding half
- **B085** — revise — scope to the outstanding half
- **B088** — revise — scope to the outstanding half
- **B091** — revise — scope to the outstanding half
- **B098** — revise — scope to the outstanding half
- **B145** — revise — scope to the outstanding half
- **B147** — revise — scope to the outstanding half
- **C006** — revise — scope to the outstanding half
- **C017** — revise — scope to the outstanding half
- **C025** — revise — scope to the outstanding half
- **C026** — revise — scope to the outstanding half
- **C033** — revise — scope to the outstanding half
- **D001** — revise — scope to the outstanding half
- **D003** — revise — scope to the outstanding half
- **D004** — revise — scope to the outstanding half
- **D005** — revise — scope to the outstanding half
- **D011** — revise — scope to the outstanding half
- **D017** — revise — scope to the outstanding half
- **D032** — revise — scope to the outstanding half
- **D050** — revise — scope to the outstanding half
- **D071** — revise — scope to the outstanding half
- **D073** — revise — stated basis/severity or applied fix wrong
- **D079** — revise — stated basis/severity or applied fix wrong
- **E014** — revise — scope to the outstanding half
- **E017** — revise — scope to the outstanding half
- **E020** — revise — scope to the outstanding half
- **E028** — revise — scope to the outstanding half
- **F006** — revise — scope to the outstanding half
- **F007** — revise — scope to the outstanding half
- **F013** — revise — scope to the outstanding half
- **F020** — revise — stated basis/severity or applied fix wrong
- **F024** — revise — scope to the outstanding half
- **F028** — revise — scope to the outstanding half

### 11.3 Needs a real run before disposition — 6

Source analysis could not settle these. Do not schedule on the source verdict alone.

- **A055**
- **C008**
- **D058**
- **D070**
- **D080**
- **F011**

### 11.4 Split before touching — 2

Double-assigned IDs whose sub-entries have conflicting verdicts.

- **D014** — (a) LIVE, (b) FIXED
- **D015** — (a) PARTIAL, (b) FIXED

### 11.5 Already rejected this pass — 2

- **A129** — misfiled; the cited code is verbatim its own Code Sample. In `DELTAS-rejected.md`.
- **A076** — non-reproducing on the deployed interpreter. In `DELTAS-rejected.md`.

**Totals:** 290 as-filed · 59 with revisions · 6 run-first · 2 to split · 2 rejected · 163 already moved to `DELTAS-fixed.md`.

---

## 12. Operating instructions

### 12.1 Sequencing — do not reorder

Work top to bottom. The order is load-bearing, not preference.

1. **WS8-01 (B151)** alone, first. Every other item in WS8 is tuned against belief values this fix
   changes. Landing them first means re-verifying them afterwards.
2. **WS8-02 → WS8-03 → WS8-06.** The rest of the erasure chain, minus the gated pair.
3. **WS8-04 / WS8-05** only after `syxx-neural-planner` returns the contract decision. If it has not
   returned, skip past them and keep going. Do not implement a rule you inferred.
4. **WS10-01 (E061)** before any `UBUNTU-PORT-PLAN` WS-C1 work. It is a data-model prerequisite: the
   C# type cannot represent an encrypted resolver, so WS-C1 cannot be fixed correctly first.
5. **WS9**, then **WS10-02/03**, then **WS11**.
6. Everything else from the §11 roster, highest criticality first within each domain.

Within a single delta, follow the `Implementation:` steps in `ROADMAP.md` in the order given. Where a
step says to remove a default parameter or delete a fallback *before* fixing call sites, that ordering
is deliberate: it makes the compiler enumerate the work instead of leaving a site silently missed.

### 12.2 Do not stop

Per AGENTS.md: **no checkpoints.** Finish the assigned work, not a phase of it. Do not stop to report
progress, to ask which item is next, or to confirm an obvious decision. The roster in §11 is the
decision; priority is already assigned and does not need re-litigating.

**The only permitted mid-task stops are to update `CHANGELOG.md`, `ROADMAP.md` or `MEMORY.md` — and
you resume immediately after writing, in the same run.**

### 12.3 Ledger updates as you go, not at the end

After each delta lands:

- **`CHANGELOG.md`** — record what actually landed, with `file:line`. Never log what did not land. If
  half a delta landed, say which half.
- **`ROADMAP.md`** — mark the WS item `**DONE**` only after its stated verification passed, including
  the mutation proof. If only part landed, mark `**PARTIAL**` and name the outstanding half in the
  same edit. A `DONE` that later proves false is the specific failure that produced WS8 and the whole
  "Ledger integrity" section: five items in this roadmap were marked DONE while live in the tree.
- **`DELTAS.md` → `DELTAS-fixed.md`** — move the delta only when you have verified by reading the code
  AND running it that the fix is present. Add your evaluation comment in the house format (labels at
  column 24, text at column 48) recording what you did and how you proved it.
- **`outputs-catalog.csv`** — any new or changed user-visible string, with filename and line numbers.

Then continue. Do not wait for acknowledgement.

### 12.4 The four things that DO require stopping

Exceptions in AGENTS.md, not discretion:

1. **A network connection failure.** Report it. It is often a firewall rule the user can clear, not a
   dead end. Do not work around it silently.
2. **A missing dependency or toolchain.** Report it and name what you need. Relevant here: `dotnet
   build` needs a Windows host; the Avalonia build and a non-invariant-locale Linux box are required
   for WS10. Do not mark a Windows or Linux item done from a source read alone.
3. **A user-visible string change.** CLI output, report text, UI labels, error messages, verdict
   wording. Propose the exact wording and wait for approval before writing code. Affected here:
   WS10-03's "none observed" gating, B182's blocklist naming, B177's likelihood copy.
4. **A genuine contradiction between this handoff and the code.** If a delta's premise is false in the
   tree, say so and stop on that item. Do not implement a fix for a defect that is not there. Two
   deltas in this sweep were rejected for exactly that reason.

Everything else: decide and proceed.

### 12.5 Batch your permissions

Plan 25 to 30 commands ahead and request them as one batch, so the run continues unattended. Do not
trickle single-command requests.

### 12.6 The bar for "done"

- **One real run with honest output beats a green suite.** Several defects here exist *because* a
  green suite stood over a broken path. `Syxx.Tests/SyxxKitTests.cs:15` excludes the network-touching
  functions, so `VantageComparer.CompareVantagePointsAsync` throws on every invocation on Windows and
  Ubuntu with zero coverage. The server's paywall can be deleted entirely and its suite stays
  217/217 green.
- **Every test needs a failure case.** Write the mutation, run it, confirm RED, then fix. Where
  `ROADMAP.md` names the mutation, use that one.
- **A failing test is data.** If one fails, do not soften, skip or widen it. File a delta recording
  what the test expected, what the code produced, and whether it is a code defect, a test defect or a
  spec ambiguity. Then judge continue-versus-deviate and state which you chose.
- **Platform parity is a deliverable.** A `SyxxKit` change needs its `Syxx.Kit` twin, and Ubuntu links
  the Windows assembly, so a Windows fix is usually the Linux fix. If you can only land one side, land
  it and file the owed side as an explicit parity delta. Never leave a silent divergence.
- Run `syxx-parity-check` before closing any determinative-core item, and `syxx-honesty-audit` on any
  surface whose wording changed.

### 12.7 Commits

Explicit paths only. Stage named files, then `git diff --cached --name-only` before committing. The
repo-wide staging shortcut is blocked by a PreToolUse hook, so do not reach for it. Never edit
`AGENTS.md`. Stay in lane: `Server/**`, trust/GroundTruth consumers, probes and the labeled corpus are
yours; flag cross-lane needs rather than reaching into `SyxxApp/**` or `SyxxUI/**`.

### 12.8 Display a TODO

Keep a self-updating TODO visible for the whole run: assigned items, current item, completed items.
It is how the run is followed without interrupting it.

### 12.9 Dispatch a `syxx-neural-*` subagent — do not implement the core by hand

Most of this work sits in the determinative core, and the `syxx-neural-*` agents exist precisely for
it: they carry the Verification & Honesty Protocol **into the code** rather than treating it as review
guidance. They enforce, by construction, the things these deltas are about — no verdict before
evidence is queried, parsed, validated and classified; missing, timed-out, invalid or untrusted inputs
propagating as inconclusive rather than defaulting clean; golden vectors for expected FAILURE as well
as expected success; a real run on real or adversarial input before "done". Implementing WS8 by hand
means re-deriving all of that from this document, and the failure mode of re-deriving it is exactly
what produced the defects.

They pin `claude-opus-4-8`. Definitions live in `.claude/agents/`.

**Which agent for which work:**

| Work | Agent | Why |
|---|---|---|
| **WS8 — all six** (`Synthesis/**`, `SynthesisEngine`, `Scorers`, `Synthesizer`, `Conclusion`, `DecisiveActCatalog`) | **`syxx-neural-executor`** | Implements an approved reasoning design into code, tests, adapters and parity fixtures. It is the only agent chartered to touch the verdict core. |
| **WS8-04 / WS8-05 contract decisions** (may a decisive act suppress referral; does `scriptMismatch` belong in the catalog) | **`syxx-neural-planner`** first | These are score-contract decisions, not patches. The planner decides and records in `REASONING-SYSTEM-DESIGN.md` §12; the executor then implements. Do not let the executor invent the rule. |
| **WS8-03's dampener-floor fraction** | **`syxx-neural-planner`** | A score-contract constant. Needs deciding before implementation, not during. |
| **WS9-01 / WS9-03 / WS9-04** (`SignedEvidencePayload`, `WitnessSigner`, `Stage5Builder`, canonical bytes) | **`syxx-neural-executor`** | Signed-evidence and canonicalization are determinative-core material — byte-identity and parity fixtures are its charter. |
| **WS9-02** (CLI key handling, `SyxxWitness.swift`) | **`syxx-engineer`** | CLI surface and key storage, outside the reasoning core. |
| **WS10 — all** (`NetworkKeyState`, `EndpointLogAnalyzer`, culture-sensitivity sweep, Linux paths) | **`syxx-engineer`** | Platform APIs, probes and transports. Not verdict logic. |
| **WS11 — all** (error-collapse class, dead code) | **`syxx-engineer`** | General production code across subsystems. |
| **Any delta whose premise you cannot confirm in the tree** | **`syxx-neural-researcher`** | Investigates and files, rather than guessing. Two deltas in this sweep were rejected because their premise was false; do not implement a third. |
| **Any user-visible wording change** | **`syxx-neural-surfacer`** (Stage-5 DTO) or **`syxx-gui-architect`** (everything else) | And it still needs owner approval before the code is written — see §12.4. |

**How to dispatch well.** Give the agent the ROADMAP item verbatim (it already carries Files,
Implementation, Verification, Threat-defended and Gating), plus the delta's own entry from `DELTAS.md`
including its 2026-07-20 evaluation comment. Do not paraphrase the file:line detail — it was verified
against the live tree on 2026-07-20 and the ledger's older citations have drifted, in ~92 cases to
directories that no longer exist.

**Two constraints to pass through on every dispatch:**

1. **Read-only on the ledgers unless the agent is the one landing the change.** A previous fleet was
   told this and wrote to `DELTAS.md` anyway, consuming an ID range and forcing a renumber. If you fan
   out more than one agent, only one may write, and it writes sequentially.
2. **Scratchpad isolation.** Give each agent its own subdirectory. Two agents in this audit collided
   on a shared scratch path and one lost a reproduction mid-run.

**Where a neural agent is not warranted:** single-line mechanical edits with a named file, line and
replacement — the `StringComparison.Ordinal` additions in WS10-02, for instance. Dispatching for those
costs more than it returns. Use judgement, but bias toward dispatching for anything touching
`Synthesis/**`.

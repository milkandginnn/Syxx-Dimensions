# Phase 0 — Baseline & Carry-Forward

**Baseline:** HEAD `ecda22c9`, branch `main`. Working tree: 202 files changed (+9,401 / −40,123).

## Task 0.2 — the finding that reframes the track

**12 of 13 cited paths are dirty.** The report reviewed the **working tree, not HEAD**:

```
M Server/syxx_server/app.py                    M Windows/dotNET/Syxx.Kit/Trust/MonitorScheduler.cs
M Server/syxx_server/dnssec_validator.py       M Windows/dotNET/Syxx.Kit/UnboundManager.cs
M Server/syxx_server/fulfillment.py            M Windows/dotNET/Syxx.Kit/UnboundResolver.cs
M Server/syxx_server/investigate.py            M Windows/dotNET/Syxx.Tests/MonitorSchedulerTests.cs
M Server/syxx_server/lookalike.py              M docs/USER-GUIDE.md
M Server/syxx_server/whois_lookup.py           M scripts/deploy.sh
```

Only `Server/README.md` is clean.

**Consequences.** Every verification reads the working tree and must say so. Fix plans must state whether they apply to committed or uncommitted code. And any of these findings can vanish or change if the working tree is committed, reverted, or stashed before the fixes land — the same pre-revert-tree trap that made Phase 0 of `review-fixes_20260812` necessary, one level up.

## Task 0.3 — carry-forward table

Prior evidence from `review-fixes_20260812/triage.md` (T) and this session's reported findings (R), both against this same working tree.

| ID | Finding | Prior | Conclusion carried forward | Re-verify? |
|---|---|---|---|---|
| B1 | MonitorScheduler `:114` guard dead in Stop() path | R | CONFIRMED — `_cts` nulled by `Stop()`; post-stop tick counted, latched, delivered | No — extend only to the test coupling |
| B5 | `_inFlight` released before state commit | R | CONFIRMED — interleaving leaves stale `LastTick` | No |
| C1 | `VantageComparer.swift:320` missing `!didFail` | T (N20) | CONFIRMED latent — no post-fix input found yielding a false clean | No |
| C2 | `VantageComparer.cs` twin, both sites | T (N20) | CONFIRMED — C# filters `Records.Count > 0` only; its `DidFail` covers the same five statuses | No |
| D1 | Four stale `VantageComparerTests` | T (cause 1) | CONFIRMED stale — three contradict their own name or doc comment | No |
| D2 | `MonitorSchedulerTests` first-tick assertions deleted | R | CONFIRMED — doc still promises the behavior | No |
| E2 | `UnboundManager.cs:122` blocking `WaitForExit` in async | R | CONFIRMED | No |
| F1 | `app.py:1344` dead expression | R | CONFIRMED — introduced by the %→f-string refactor | No |
| F2 | `fulfillment.py:450` dead expression | R | CONFIRMED — and the subscription id is discarded, so reinstatement is customer-keyed | No |
| F3 | `test_licensing.py:65` dead expression | R | CONFIRMED | No |
| G1 | `lookalike.py:15` homoglyph `1→l` dropped | R | CONFIRMED — map is single-replacement, so the fix is structural | No |
| H1 | `deploy.sh:218` `\|\| true` + duplication | R | CONFIRMED | No |
| J2 | `AGENTS.md:7` self-edit authorization | R | CONFIRMED — `CLAUDE.md` symlinks to it | No |
| J3 | `AGENTS.md:2` contradictory agent counts | R | CONFIRMED — 164 vs 203 in one file | No |
| K2 | `docs/USER-GUIDE.md` banned-term uses | R | CONFIRMED — three lines | No |

**15 carried forward, 22 to verify.** Higher than the 12 estimated in the spec — the `/code-review` pass overlapped the report more than expected.

Not carried forward, and needing fresh work: the whole A cluster (7), B2/B3/B4 (3), E1/E3/E4 (3), F4, G2, I1–I3 (3), L1–L3 (3), J1, K1.

## Remaining Phase 0 tasks

- Task 0.4 — check the report's own dedup claims (five collapses asserted)
- Task 0.5 — spot-check the four self-refuted findings

## Task 0.4 — the report's dedup claims

Four of five collapses upheld. **One rejected, one finding disproved outright.**

| Collapse | Result |
|---|---|
| C142 guard: correctness HIGH + maintainability MEDIUM | **Agree** — same three lines, one construct, one fix. The test coupling is confirmed: `MonitorSchedulerTests.cs:141` asserts `afterStop == 2` with a comment naming the parked in-flight tick, so fixing `:114` makes it 1. Code and test move together |
| `lookalike.py:15` | **Agree on the merge, finding is factually wrong** — see below |
| `app.py:1344` | **Agree** — bare expression, one fix, nothing behavioral lost |
| `fulfillment.py:450` | **Disagree — hides a second defect.** Split 1 → 2 |
| `investigate.py` `.format()` | **Agree on merge, wrong label** — four error-path strings are not a measurable cost; relabel from performance to maintainability |

### G1 disproved by execution

The adjudicator ran both map variants. Pre-diff and post-diff both yield `HOMOGLYPH_MAP["1"] == 'i'`, identical length. The removed `"1": "l"` was **shadowed** by `"1": "i"` on the next line — in a dict literal the later key wins — and the sole consumer is a plain `.get(ch)`. The diff removed dead code and changed nothing.

Both claims in the finding were wrong: not introduced by this diff, and coverage was not halved. A real issue survives but is different and pre-existing: the map is 1:1 and cannot express one-to-many substitution, so `1` has only ever permuted to `i`.

**This was also reported through `/code-review` earlier today.** Withdrawn and re-reported with the correction.

### F2 split 1 → 2

The dead expression at `:450` is cosmetic, LOW. But the comment at `:441-442` claims the branch only fires when the subscription reports `active`, and the `invoice.paid` path at `:449-455` performs no such check — `sub_status` is computed at `:447` and used only in the `elif` at `:456`. Unearned license reinstatement, separate fix, separate blast radius. The deleted binding was already unused pre-diff, so it was the last trace of a guard that was never wired.

Total findings 37 → 38.

### Three-way scheduler split upheld, with an overlap the report missed

C142, the Stop→Start race, and the TOCTOU are genuinely distinct — each has a failure mode the others' fixes do not reach. But fixing C142 also repairs the stale-commit half of the race; only the skipped baseline survives a token fix. Sequencing should account for that.

## Task 0.5 — the four self-refutations

All four upheld. One carries a caveat that matters.

- **`MonitorScheduler.cs:82`** — upheld. The comment is correct; C# does forbid `await` in a `lock` (CS1996). The original finding was the wrong thing.
- **`MonitorScheduler.cs:136`** — upheld **on the doc-accuracy claim only**. Two things it misses: the unchanged clause "Runs one tick immediately" is now an outcome `Start()` no longer guarantees at return, and — more importantly — **the second report raises `:141` as a separate MEDIUM** about the dropped synchronous first tick and stale consumer state. That is an API-contract change, not a doc claim. **The refutation must not be allowed to retire it.**
- **`investigate.py:1722-1740`** — upheld. File is 319 lines; the relocation to `:149/:158/:168/:171` is exact.
- **`UnboundManager.cs:117-131`** — upheld from the diff. The 30s drain, 5s grace, kill, and output-dropping all pre-date this change; the diff only swapped the sync wait for an async one.
- **Python floor** — both halves confirmed, and stronger than argued: all four `datetime.UTC` sites were `datetime.timezone.utc` before this diff, so the 3.11+ floor is newly introduced, not pre-existing. README still claims 3.8+.

## Scope limit declared

The original per-dimension finding texts are not in the track's read scope, so collapses 1, 3 and 5 rest on the cited code admitting only one defect — not on comparing reviewer prose.

---

# Phase 0 Handoff

**Objective:** dedupe the ultra review, verify each cited defect, plan fixes. No fixes land in this track.

**Completed:** coordinator installed copy-first with provenance · baseline captured · carry-forward table built · the report's dedup claims adjudicated · its four self-refutations spot-checked.

**Files/components affected:** track documents only. No source changed — verify with `git diff --stat` over source paths.

**Verified facts:**
- HEAD `ecda22c9`, branch `main`. **12 of 13 cited paths are dirty** — the report reviewed the working tree, not HEAD.
- 15 of 38 findings carry forward with prior evidence; 23 need fresh work.
- G1 disproved by execution and withdrawn; F2 split 1 → 2.
- The three MonitorScheduler bugs are genuinely distinct, but fixing C142 also repairs the stale-commit half of the race.
- `.claude/` is gitignored, so `multi-agent-coordinator` and `swift-expert` exist on disk but are **untracked** — they will not survive a clean checkout.

**Decisions made:** DNSSEC anchor in scope at highest priority · trust prior evidence where cited · coordinator plus four advisory specialists · marketplace findings planned separately · every spawned agent reads `AGENTS.md` and `rules/*` as step 0 and confirms it in its output header.

**Validation performed:** none beyond reading — no builds, no suites. Correct for a verification phase; Phase 1 Task 1.3 is the first task requiring execution.

**Remaining work:** Task 0.6 assignment packets · Task 0.7 this handoff · then Phases 1–5.

**Risks / compatibility notes:**
- The dirty tree is the dominant risk. Committing, reverting, or stashing it before fixes land invalidates findings — the same trap that made Phase 0 of `review-fixes_20260812` necessary, recurring one level up.
- This track and `review-fixes_20260812` both touch `VantageComparer`. Whichever runs second must check the other's state.
- The `:136` refutation must not be allowed to retire the separate `:141` MEDIUM.

**Next recommended action:** Phase 1, Task 1.1 — verify the DNSSEC root trust anchor. If confirmed, it likely warrants its own track and would reorder everything after it.

---

# Phase 3 — Cluster L (macOS test quality)

**Baseline correction:** macOS files are **clean**, not dirty. The Phase 0 note said 12 of 13 cited paths were dirty; that held for the Server and Windows paths I queried, but the macOS test files are tracked and unmodified. Briefing the specialists otherwise was an over-generalization. Changes no finding in this cluster.

| ID | State | Detail |
|---|---|---|
| L1 | Confirmed as fact, **hygiene not defect** | The discarded `SecItemDelete` status is real, but the UUID-per-run probe service means a leaked item can never satisfy a later probe, and every product query pins an exact service so none can reach one. `kSecMatchLimitAll` appears nowhere under `macOS/`. Cost is one ~5-byte item per abnormally terminated run. Record as latent; not worth a fix |
| L2 | **Confirmed, and broader than reported.** Medium | See below |
| L3 | Confirmed, **cited lines wrong** | The guard is at `AppModelWitnessTests.swift:42-73`, not `:186-211` — those lines are an unrelated test body. Parity file is `:129-157`, cited as `:135-157` |

## L2 — the asymmetry is worse than "strict vs lenient"

Swift's `ExpectedCase` has no optional stored properties, so any missing or retyped key fails loudly. The C# twin reads keys individually off a `JsonDocument`, and three diverge:

- **`manifestState` uses `TryGetProperty`, so the assertion is silently skipped when the key is absent.** This is exactly the pattern the Swift comment says `ExpectedCase` was written to eliminate — quietly turning a lookup into nil and skipping the assertion that key guards. A case omitting it goes red on macOS and **green on Windows, one real assertion lighter than it looks**.
- `assets[].class` and `notes` are never read in C# at all — contract-only, no assertion lost either side.

All 14 corpus cases conform today, so the finding is latent. Fix is one line at `WarpEngineTests.cs:119-120`; the cleaner move is deserializing `expected.json` into a record with required members so each runtime states its schema contract once.

**Two further twin deltas found while comparing:** the case-directory filter is `^\d\d\d_` in Swift but only checks first-char-digit and `n[3] == '_'` in C#, so `0a1_foo` would be collected by one and skipped by the other. And Swift silently `continue`s past any brotli case, so `014_br_identity` is never exercised on macOS while C# decodes it — the already-recorded parity gap, noted only because it is the same silent-skip family sitting four lines from the L2 decode.

## L3 — extraction is safe, but not by inheritance

Take a value type, not a base class. Forgetting `super` in `setUp` fails safe — no backup, so no restore. Forgetting it in `tearDown` does **not**: the restore is lost and whatever key the test wrote stays in the developer's keychain. That asymmetry is the argument against a shared base class. Per-instance state is required; lifting the backup to `static` turns a per-test restore into last-writer-wins over a real key.

A third site shares the pattern at `SyxxKitTests/WitnessKeyValidationTests.swift:52,57`, but it is in a different test target from the support type, so extraction cannot cover it without relocating.

---

# Phase 2 — Clusters B and E (Windows)

All six confirmed. **Three severities are wrong in the source report — two up, one down.**

| ID | Result | Reachable? |
|---|---|---|
| B2 skipped baseline | confirmed | Yes — plain monitoring toggle off/on |
| B2 stale commit | confirmed | Yes — same path; repaired by B1's fix |
| B3 `State` TOCTOU | confirmed — **permanent wedge, not transient** | Yes, narrow window |
| B4 deadlock | confirmed shape — **not reachable** | **No** |
| E1 dropped output | confirmed — **escalates to a silent DNS reset** | Yes, rare |
| E3 `catch(Exception)` | confirmed as written — latent, no token passed today | Not today |
| E4 shape divergence | confirmed — **the material divergence runs the other way** | n/a |

## E1 is not "lost diagnostics" — recommend LOW → higher

`WaitForExit(5000)` is the **timed** overload, which does not wait for redirected-stream EOF. If the process exits inside those 5 s with the drain still pending, both outputs return `""` and `ExitCode` is typically **0**.

Trace it forward: `SnapshotAsync` sees `code == 0`, so the D099 guard does not fire; the empty-string branch then returns "**genuinely no DNS servers configured**". That empty-but-non-null list is not `null`, so `EnableAsync`'s refuse-to-switch guard does not trip either. On the later `DisableAsync` the empty list is restored, `SetServersAsync` takes `-ResetServerAddresses`, and **the user's static DNS is silently reset to DHCP** — precisely the outcome the five D099 comments exist to prevent.

A "could not read" is laundered into a confident "no servers". Realistic trigger: PowerShell spawns a helper inheriting the stdout handle, so the drain outlives the direct child's exit.

Also answered: the abandoned read tasks can fault unobserved when `using var p` disposes the pipes, but no crash today — the repo sets no `ThrowUnobservedTaskExceptions`, so .NET swallows them. Enabling that switch would turn this into a crash.

## B3 is a permanent wedge — recommend MED → higher

A `Stop()` landing between the token check and the `State = Running` assignment runs to completion, then the loop thread overwrites `State` with `Running`. Result: `State == Running`, `_cts == null`, `_loopTask == null`, no live loop. Nothing ever writes `State` back to `Idle`, so **the wedge lasts the process lifetime**: `IsRunning` reports true, the toggle shows monitoring on, and no tick runs again. Silent failure of a safety feature.

**Do not fix with `_runLock`** — `_onTick` runs under it, so folding lifecycle in converts B4 from conditional to certain. Needs a separate lifecycle lock.

## B4 is not reachable — recommend MED → lower

Every consumer was enumerated: `new MonitorScheduler(` appears at six sites, and **only `AppViewModel.cs:108` passes `onTick`**. That handler marshals through `TryEnqueue` (fire-and-forget); the headless fallback runs inline but blocks on nothing. **Nothing in the repo calls `StopAsync()` at all.** The handler is `Action<MonitorTick>`, so it cannot even await — it would have to block explicitly.

A latent trap, not a live bug. The lock-hoist is still worth doing: it also removes disk I/O (`SyxxSettingsStore.Load()`) and toast construction from under the lock.

## E4 — the material divergence is nullability, and it runs toward macOS

The filed async-vs-sync shape difference is real but deliberate and correct per platform. The consequential one is different: C# `SnapshotAsync` returns `IReadOnlyList<string>?` where `null` means "could not read" and empty means "genuinely none" — the D099 tri-state. **Swift's `snapshot() -> [String]` is non-optional and cannot express "could not read" at all**, its consumers guard nothing, and `disable()` restores unconditionally.

**macOS has no D099 safe-restore-point guard, because its protocol shape makes one impossible.** C# is ahead on safety here. Closing the gap means changing Swift's return type, not C#'s.

## B1/B2 overlap and fix sequencing

Confirmed: B1's fix repairs the stale-commit half of B2 — a token captured at entry is the old, cancelled one, so the commit is discarded. It does **not** touch the skipped baseline, whose cause is `_inFlight` being instance-scoped and outliving `Stop()`.

1. **B1 + `MonitorSchedulerTests.cs:141` in one commit.** After the fix `TickCount` is 1; the test burns its full 30 s timeout then fails. Assertion becomes 1 and the comment documenting the wrong invariant is rewritten.
2. **B3** — introduces the lifecycle lock step 4 also needs.
3. **B5** — after B1, since it widens the window `_inFlight` is held.
4. **B2's remaining half** — a design call, not a one-liner. `Start()` must not clear `_inFlight` (two overlapping ticks would stomp the snapshot file). Preferred: the loop's first tick retries with backoff rather than treating null as a completed baseline.
5. **B4's lock-hoist** — independent, land anywhere.

Also: macOS records `skippedTickCount` and `lastTickAttemptedAt` (C111) so a skipped baseline is visible; **the C# port dropped that accounting** and returns null silently. Port it.

---

# Phase 3 — Clusters F/G/I/J/K

**Baseline correction:** `tools/` is dirty too — `capabilities.py` and `test_adapters.py` both modified. Load-bearing for J1.

| ID | Result |
|---|---|
| F4 | Confirmed. All four `.format()` calls and three sibling f-strings are in the **same function**, all added by this diff. Diff touches 11 added lines, not 9 — two are continuations |
| G2 | Confirmed, medium. Both in-repo contrasts hold |
| I1 | Confirmed at `:370`, **misfiled at `:255`** |
| I2 | **REFUTED — drop it** |
| I3 | Confirmed, low |
| J1 | Confirmed and **understated — escalate** |
| K1 | Confirmed, documentation-only |

## I2 refuted

`netutil.ip_is_public` is **live** at `netutil.py:26`, reached from `whois_lookup` via `fetcher._resolve_public` → `fetcher._ip_is_public`. The same delegation appears in `resolver.py` and `firewall_test.py`. The docstring is accurate and the body comment already spells out the indirection. Nothing to fix.

## J1 — not a docs cleanup, an uncommitted revert

`git show HEAD:tools/adapters/capabilities.py` carries the full tier ladder — fable/opus/sonnet/haiku/inherit each mapped to its own Anthropic model. The working tree replaces all five with one free-tier model in a **single hunk**, and `test_adapters.py:637` was edited in the same dirty state to follow the code.

Four signals it is undocumented, one contradicting it outright:
- The `MODEL_ALIASES` header explains codex, copilot and gemini by name; says nothing about opencode.
- One commit in that file's history, so no explanatory message — and the change is not committed at all.
- No changelog entry, comment, or doc anywhere mentions the model.
- **`docs/harnesses.md:63-64` still documents the OpenCode column as the tier ladder** — matching HEAD, contradicting the working tree.

Consequence: **every OpenCode subagent, including the security-auditor and code-reviewer roles, is silently pinned to one free-tier model.** Cursor's all-`inherit` collapse is not the same defect — it is documented and hands choice back to the user rather than pinning.

Owner decision: revert to the ladder, or keep the pin deliberately and land it properly with a header comment, a docs update, all five rows asserted, and mirrors regenerated.

## G2 — the harm is deferred, not immediate

An immediate duplicate is harmless (zero rows matched). The real damage: `license.renewed` only records a note and re-issues nothing, so **once a customer re-subscribes and mints fresh keys, re-posting the old captured signed body revokes those too** — silent customer-facing denial of service, answered 200.

Resist escalating beyond medium: the attack needs possession of a previously-valid signed body, and TLS covers the wire.

Fix: the payload exposes no event id (`data.get("id")` is the *license* id), so key the guard on the already-computed HMAC, mirroring `fulfillment.py:417`.

**Undetermined, network denied:** which headers Keygen actually sends. The code consumes a bare hex HMAC, not the keyId/algorithm form the validate-key path parses, so a Date header may not exist. The freshness half of the fix is an open question; the idempotency half stands alone.

## I1 split

`:370` calls `_pdk` **three times per DNSKEY**, each allocating a fresh copy — worth the one-line walrus fix. `:255` is the last line of `_kt` and copies nothing; the real per-RRSIG redundancy is `_psig` at `:265`, but hoisting it means restructuring the key × signature loops inside signature verification, where a subtle change alters which pairs get tried. **Not worth the churn.**

## K1 — plus a third floor

Confirmed: README claims 3.8+ while `StrEnum` and four `datetime.UTC` sites (all added by this diff) require 3.11+. README is **clean**, so the claim predates the change; the code moved. Runtime and lint already agree on 3.12, and ruff's `UP` rules actively push toward these constructs.

Adjacent, outside K1: **`Server/uv.lock:3` declares `requires-python = ">=3.14"`** — a third floor disagreeing with both CI's 3.12 and ruff's py312 target, while `pyproject.toml` declares none at all.

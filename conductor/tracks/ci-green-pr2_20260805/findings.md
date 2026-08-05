# Findings — ci-green-pr2_20260805

## C-01 · SelfVerify false-green on domain switch (Task 1.1 conclusion)

**Product defect, not a stale test.** Required written conclusion, with evidence:

- `WitnessViewModel.cs:293` — `_lastSnapshot` is written only on a successful seal, and **no
  failure branch clears or re-scopes it**.
- `WitnessViewModel.cs:438` (pre-fix) — `SelfVerify` guarded only on nullity, then verified the
  retained snapshot with **no check that it belongs to the domain currently shown**.
- Sequence: seal `a.com` → switch `Domain` to `b.com` → seal fails ("Nothing to witness") →
  self-verify replays `a.com`'s valid seal → "Verified: the seal is valid…" in green, rendered
  beside `b.com`. The user reads it as a claim about `b.com`. G-12 Route B false-confidence class.
- The test (`WitnessViewModelTests.cs:485-513`) encodes exactly this and was RIGHT.

**Fix (1.2):** `SelfVerify` now also requires `snap.TargetDomain` to equal the current domain
(ordinal, case-insensitive); on mismatch it renders a caution naming both domains —
`SelfVerifyDomainMismatchFormat`, deliberately containing no form of "Verified". Nullity guard,
same-domain re-verify, and `ApplyVerification` severity matrix untouched.

**Evidence:** CI run 3 = red on unfixed code. Local post-fix: all 4 `SelfVerify` tests pass,
including the happy path.

**Cross-platform note, filed not fixed:** the T-6 test comment records that Windows and macOS
*share* a snapshot field between verify paths (DELTAS.md G-15). Whether their self-verify has the
same missing domain guard is a parity question for cross-platform-parity_20260805 Phase 4 —
checking it is out of this track's scope, but the question is now written down.

---

## C-02 · VerifyExternal tests observed the wrong panel; the type assert masked it

The six `VerifyExternal_*` tests asserted `VerifyBrush`/`VerifyLine` (self-verify panel) while the
command writes `ExternalVerifyBrush`/`ExternalVerifyLine` — the G-14 separation the T-6 test pins
as intended design. Untouched, `VerifyBrush` stayed `Brushes.Gray`, an immutable brush, so CI
reported a brush *type* error where the truth was a wrong-*property* error. Corrected as a declared
harness fix; every severity-color and wording assertion preserved. All 10 verify tests green.

## C-03 · Claude Code Review workflow — accepted exception, owner action required

The workflow fails with `"anthropic_api_key": ""` — the `ANTHROPIC_API_KEY` repository secret is
not configured. No code change can fix this; it needs the owner to add the secret in GitHub repo
settings (or disable the workflow). Recorded as this track's one accepted exception, pending owner
acknowledgment.

## C-04 · Root-cause ledger for the run-3 reds (all fixed this session)

| Red | Cause | Fix |
|---|---|---|
| macos legs ×2 | runner Swift 5.10 vs tools 6.0 | `runs-on: macos-15` |
| Avalonia + ubuntu legs | C-01 false-green + C-02 + FileFallback | product fix + harness fix + verified fallthrough |
| FileFallback ×4 | `Store()` trusted secret-tool on existence | round-trip verify, fall through |
| keyring leg | empty `--unlock` password → no login collection | throwaway non-empty password |
| Server pytest | committed corpus builder ≠ committed corpus | committed the reconciled builder |
| `.deb` smoke ×2 | `libcoreclrtraceptprovider.so` needs liblttng | optional provider dropped from payload |
| snap | `dotnet-sdk/latest` has no .NET 10 | pin `10.0/stable` track |
| windows-x64 | 3 real C# breaks, first Windows build ever | Suspending removed, ResolverRow renamed, Evaluation→Result |
| markdownlint | 50 real violations incl. records misparsed | fixed content-preservingly to zero |
| Validate | plugin sources behind committed artifacts | sources committed, validate green locally |

---

## C-05 · Closing state: what this track fixed, and what it uncovered that it cannot finish

### Fixed and verified (green on CI unless noted)

| # | Red | Root cause | Evidence |
|---|---|---|---|
| 1 | SelfVerify false-green | product: `_lastSnapshot` never re-scoped; no domain check | C-01; 4 tests green |
| 2 | 7 Witness tests | tests read self-verify panel, not external; exact-type assert masked it | C-02; 10 green |
| 3 | keyring leg | empty `--unlock` password → no login collection | **green on CI run 5** |
| 4 | markdownlint | 50 real violations | **green on CI run 5**; 0 locally |
| 5 | `.deb` build ×2 | payload/committed-input gaps | **green on CI runs 4–5** |
| 6 | Warp corpus drift | committed builder ≠ committed corpus | gate passes |
| 7 | doc-gardener | 11 dead links | `make garden` 0 errors |
| 8 | Server lint | 475 → 98 (ruff fix/format; 418 tests still pass) | partial, see below |
| 9 | 3 Windows C# breaks | Suspending, ResolverRow, Evaluation→Result | superseded by newly-visible errors |
| 10 | `.deb` smoke CA | container has no CA bundle | fix pushed, unverified |

### Uncovered, NOT finishable inside this track

Each is a first-ever compile of code on a platform that had never built it. They are not CI
plumbing; they are ports.

1. **Windows GUI has never compiled on Windows.** Fixing the first three C# errors revealed the
   next layer (`Loaded` unresolved, `SolidColorBrush`/`Color` unresolved, CS0411 in AppViewModel).
   Expect several rounds. `docs/CLAUDE.md` already records that WinUI cannot build on macOS, so
   every round costs a CI run.
2. **macOS SyxxApp has never compiled under Swift 6.** Moving the runner to macos-15 (required —
   macos-14 ships Swift 5.10 and cannot parse a tools-6.0 package) exposed real strict-concurrency
   errors in `SyxxApp.swift:279` (`NSImage` static), `PurpleScrollView.swift:269` (main-actor
   mutation) and a test type error. `SyxxKit` itself builds clean in debug and release.
3. **FileFallback ×4 on Linux only.** Not reproducible on this Mac — CI has libsecret and
   secret-tool present with no Secret Service behind them. Two fixes landed (round-trip
   verification on both backends); a third guess would be unevidenced, so the store now names its
   failure on stderr and the next run will say why.
4. **Server lint, 98 remaining.** Not mechanical: `S310` urlopen audits inside the SSRF fetcher,
   `B904` raise-from, `E402` deliberate late imports, `S608`. Each needs a per-site decision — fix
   or justified `noqa` — and a sweep would be exactly the softening this track forbids.
5. **snap build.** Pinning `dotnet-sdk/10.0/stable` did not clear it; cause not yet read.
6. **claude-review** — C-03, repository secret, owner-only.

### The pattern worth keeping

Every root cause found in the first three runs was the same: **a fix that existed only in a working
tree while CI built HEAD.** The syntax error at `LinuxSecretStore.cs:766`, the ~50 referenced-but-
uncommitted source files, the corpus builder, the plugin sources, three Kit fixes. `ci.yml` now
carries a gate asserting every packaging input is committed; the same idea generalised — assert
that what CI builds is what a clean checkout produces — would have caught all of them.

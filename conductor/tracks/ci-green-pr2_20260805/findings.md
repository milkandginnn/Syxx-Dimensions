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

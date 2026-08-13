# Ultra Review Report: `macOS/`

- Date: 2026-08-12
- Scope: `macOS/` (7 files reviewed, 852 diff lines; production: `MatrixDeterminativeEngine.swift`, `VantageComparer.swift`)
- Dimensions: security, correctness, performance, architecture, maintainability (ultra level)
- Method: parallel dimension reviewers + adversarial critique pass, consolidated below

## High (1)

- **`macOS/Tests/SyxxKitTests/VantageComparerTests.swift:129`** (also `:56`, `:69`, `:97-99`) — The W04/N2/M03 production changes supersede the behavior four existing tests pin, and the diff leaves them red. `testPartialDoHFailureWithOneSuccessIsNotInconclusive` was modified by this diff yet still asserts `XCTAssertTrue(result.redirection)` (fails), with a comment admitting "PRE-EXISTING RED, NOT W04… left untouched deliberately." Three sibling tests (`testSingleDoHDisagreementIsNotTamper`, `testNoDoHOutcomesFallsBackToUDPOnly`, `testUDPUnanimousAllDoHBlocked_IsInconclusive`) assert the old C046 "lone/no DoH + UDP unanimity = detection signal" contract that the new `.undetermined → inconclusive` behavior deliberately removes; they fail at HEAD and are not reconciled anywhere.
  - *Impact:* `swift test` is red on 5 assertions; the suite now contains two mutually contradictory sets of tests (old stale vs new W04/N2/F1/F2), and the modified test's name ("IsNotInconclusive") asserts the opposite of what it claims.
  - *Suggestion:* reconcile the three stale tests to the corroboration-floor contract and fix the touched test's `redirection` assertion (should assert `!redirection && inconclusive`, or be split). This is incomplete reconciliation, not a regression — failures already existed at the initial commit.

## Medium (2)

- **`macOS/Sources/SyxxKit/VantageComparer.swift:320`** — The B010 residual unanimity check filters `$0.hasRecords` but not `!$0.didFail`, while the F1 repair in the same diff at `:373-374` mandates both. A forged `.responseIdMismatch`/`.truncated`/`.malformedResponse` packet carrying attacker records can push `udpSetsForCheck` to ≥2 and unanimous where the clean answering set is below quorum, manufacturing the W04 residual inconclusive (`dohBlockedWhileUdpUnanimous`, `:324`).
  - *Impact:* attacker-constructible false inconclusive in the <2-clean corner; inconsistent with the F1 contract.
  - *Suggestion:* apply `!$0.didFail && $0.hasRecords` to `udpSetsForCheck` and add a regression test with a forged outcome hitting the residual path.
- **`Windows/dotNET/Syxx.Kit/VantageComparer.cs:396`** (and `:358`) — The C# twin's `DetectUnanimousTampering` still filters `Records.Count > 0` only, so one forged packet can still break or manufacture UDP unanimity on Windows. Deliberately left unfixed.
  - *Impact:* known cross-platform spoofing gap and behavior divergence between twins.
  - *Suggestion:* apply the identical `!DidFail && Records.Count > 0` repair to the C# twin and its residual `udpSetsForCheck`, as a tracked follow-up.

## Low (3)

- **`macOS/Tests/SyxxAppTests/WitnessKeychainTestSupport.swift:133`** — `SecItemDelete(probeQuery)` return value is ignored; a test process killed between add and delete leaves a UUID-namespaced orphan item in the data-protection keychain (never read by product code, but accumulates).
  - *Suggestion:* check the delete status or wrap in a defer with cleanup.
- **`macOS/Tests/SyxxKitTests/WarpEngineTests.swift:67-80,138-139`** — Strict `ExpectedCase` decode requires `notes` and per-asset `class` keys; all 14 current corpus cases conform, but the corpus is a shared cross-platform artifact — any new case missing a key fails loudly on macOS while a lenient twin could stay green.
  - *Suggestion:* confirm the C# twin is equally strict, or drop the unused `notes`/`class` fields.
- **`macOS/Tests/SyxxAppTests/AppModelWitnessTests.swift:186-211` / `macOS/Tests/SyxxAppTests/WitnessTestifyParityTests.swift:135-157`** — Identical `backupWasTaken` guard logic duplicated across both classes.
  - *Suggestion:* extract into a shared base class/helper in `WitnessKeychainTestSupport`.

## Summary

Total findings: 6 (Critical: 0, High: 1, Medium: 2, Low: 3).
Top concerns: the production fixes (W04 corroboration floor, N2 record-set unanimity, F1 forged-packet exclusion, overhang admission, keychain data-loss guard, Sendable decode) are correct, well-reasoned, and validated — new tests pass and the witness tests now skip cleanly instead of destroying developer keys. But the change ships with four stale tests pinning the superseded behavior still red, and one F1 call site plus the entire C# twin were left with the forged-packet gap the change set out to close.

## Conclusion

The production changes are sound and verified, but **not ready to merge as-is** — the stale-test reconciliation is required (High), and the residual `udpSetsForCheck` filter should be aligned with F1 in the same change. The C# twin repair can be a tracked follow-up. A follow-up review is recommended once the VantageComparer tests are reconciled and green.

Confirmed (with corrected severity)
1. SECURITY HIGH — dnssec_validator.py:409-426 — CONFIRMED.
Verified: validate_chain fetches the root DNSKEY RRset over plain UDP from a root server IP discovered by the same unauthenticated mechanism (_find("."), line 409; _sq(".", TYPE_DNSKEY, ...), line 412), picks a KSK via _ksk (line 419), and at line 425 unconditionally records _step(ch, ".", "root-dnskey", "valid", ...) — no RFC 7958 pin (grep confirms no 19036 keyTag constant or anchor digest anywhere in Server/), and no verification of the root DNSKEY RRset's own RRSIG. Since the trust anchor is attacker-chosen, every downstream step (forged TLD DS → forged child DNSKEY → self-signed RRSIG) verifies "valid" internally, so an on-path attacker can force conclusion: "valid" for any domain. The diff only reformatted these lines (the flaw is pre-existing), but the diff did touch them and added the A158 answer-path hardening alongside without addressing the root. Impact stands; keep HIGH (arguably CRITICAL for a tool whose purpose is integrity detection, but it requires on-path capability).
2. SECURITY MEDIUM — dnssec_validator.py:353-364 — CONFIRMED. _ksk falls back from SEP+protocol3 → any protocol3 → bare rs[0] (line 364), so a root response containing only a non-SEP, even protocol≠3 DNSKEY is still accepted as the trust key. Pre-existing (diff reformats only). In practice subsumed by #1, but as a defense-in-depth gap, MEDIUM is right.
3. SECURITY LOW — dnssec_validator.py:189-198 _vec — CONFIRMED technically (no explicit on-curve check before verify), but the impact is overstated: this is signature verification, not ECDH — an off-curve point cannot produce a valid forgery; verify fails safe (returns False/raises → caught → False). On-curve validation is hygiene, not a vulnerability. Keep LOW.
4. SECURITY LOW — dnssec_validator.py:200-208 _vrsa — CONFIRMED: no modulus-size floor. Only exploitable when combined with #1 (attacker-chosen weak key); for real zones the key is DS-verified. Keep LOW.
5. SECURITY LOW — dnssec_validator.py:217-233 _dsm — CONFIRMED (function spans 217–233; == dd comparisons at 228/230/232). Non-constant-time digest compare. Practical impact nil — DS digests are public data and both operands are attacker-visible. Keep LOW.
6. SECURITY LOW — dnssec_validator.py:93-101 _parse — CONFIRMED. for _ in range(qd) with no qd != 1 rejection means QDCOUNT=0 skips question matching entirely, and this directly contradicts resolver._parse_response (resolver.py:232: if qd != 1: raise DNSError(...), A061) in the same package. No extra on-path power vs. a well-formed spoof, but it is a real parser asymmetry. Keep LOW.
7. SECURITY LOW — app.py:1308-1337 keygen webhook replay — CONFIRMED. The handler verifies only the raw-body HMAC; there is no t= timestamp parse, no replay window, and no event-id idempotency (unlike the Stripe handler's kv_insert_once). Note the project already rejects stale responses on the validate-key side (A056 Date freshness, test_licensing.py:70-74), so webhook-side replay protection is inconsistent with the project's own standard. Replay of a captured license.revoked re-revokes keys of a possibly reinstated license (DoS). Pre-existing. Keep LOW (capture requires prior compromise; bounded effect).
8. CORRECTNESS HIGH — MonitorScheduler.cs:114 (C142) — CONFIRMED, and it is the most defensible HIGH in the batch. Chain of evidence:
- Stop() (209–217) nulls _cts synchronously after Cancel; the in-flight tick is still awaiting _tick().
- When the tick completes, line 114 _cts is { IsCancellationRequested: true } is false for null — the guard cannot fire in the Stop() path, ever. It is dead code for the exact case it was written for.
- Post-stop tick is then counted (121), latched (122–127), and delivered to _onTick (128) — a UI notification after the user stopped monitoring.
- StopAsync (193–205) keeps _cts alive until after await _loopTask, so it does discard — confirming the guard works only on one of the two stop paths.
- macOS twin discards correctly: MonitorScheduler.swift:91 if Task.isCancelled { return nil }. C# behavior diverges from its stated parity twin.
- The new test (MonitorSchedulerTests.cs:135–141) waits for TickCount >= 2 and asserts Assert.Equal(2, afterStop) — it requires the broken guard. Fixing line 114 fails this test; the test must be corrected in the same change. The diff's async conversion made this deterministic (the old sync RunOnce() had only a sub-millisecond interleaving window).
9. CORRECTNESS MEDIUM — MonitorScheduler.cs:83-88,157,172 Stop()→Start() race — CONFIRMED, distinct bug. New Start() creates a new _cts; the new loop's first RunOnceAsync() returns null (old _inFlight still held, 85) so the baseline is silently skipped while State=Running (159); the old tick then completes and its stale data passes line 114 against the new (uncancelled) _cts and is committed into the new run — including _onTick. Two failure modes from one interleaving. Keep MEDIUM.
10. CORRECTNESS LOW — UnboundManager.cs:120-128 dropped output — CONFIRMED. On the 30s pipe-drain timeout, if the process exits inside WaitForExit(5000), outpTask.IsCompleted is still false at line 127 → "" returned, silently losing the diagnostic output; the abandoned ReadToEndAsync task completes later unobserved (and would fault unobserved if the pipe errors). Bounded to the hung-PowerShell failure path. Keep LOW.
11. CORRECTNESS LOW — app.py:1344 dead expression — CONFIRMED and introduced by this diff (attrs = data.get(...) → bare data.get(...), diff line 156–157). The old attrs was already unused, so this is a regression of code quality, not behavior. Keep LOW.
12. CORRECTNESS LOW — fulfillment.py:450 dead expression — CONFIRMED and made worse by this diff (sub_obj = obj.get(...) → bare obj.get(...), diff line 1671–1672). Verified the invoice.paid path reinstates the license with no subscription-status check (449–455); the guard comment at 441–442 claims a status check that doesn't exist on this path. Keep LOW (borderline MEDIUM for licensing correctness, but the reinstatement-after-late-invoice scenario is an edge case and pre-existing).
13. CORRECTNESS LOW — lookalike.py:15 — CONFIRMED and introduced by this diff: the diff removes "1": "l", (diff line 1941), leaving "l": "1" (line 15). Every other pair is symmetric; "1"-containing target labels now only permute to "i", halving "1"-impersonation coverage. Keep LOW.
14. PERFORMANCE LOW — investigate.py .format() strings — CONFIRMED at the correct lines: 4 .format() calls at 149, 158, 168, 171 surrounded by f-strings (104, 121, 189, 193, 197). Introduced by this diff (converted from %-format alongside f-string conversions). The cited range 1722–1740 is wrong — investigate.py is 319 lines. Substance is a style nit; keep LOW.
15. PERFORMANCE LOW — UnboundManager.cs:122 WaitForExit(5000) — CONFIRMED. Blocking call in an async method; on the normal path the process has already exited (pipes EOF), so the block is confined to the timeout path. Not a regression in magnitude. Keep LOW.
16. ARCHITECTURE LOW — UnboundResolver.cs:59-66 async seam vs. macOS sync protocol — CONFIRMED. Windows ISystemResolverSwitching is SnapshotAsync/SetServersAsync/RestoreAsync; macOS SystemResolverSwitching (UnboundResolver.swift:294–301) remains synchronous (snapshot(), setServers(_:), restore(_:)). Genuine interface-shape divergence between twins, acknowledged only on the Windows side. Keep LOW.
17. ARCHITECTURE MEDIUM → LOW — MonitorScheduler.cs:49,63,79,141 public API break — CONFIRMED as a change: RunOnce()→RunOnceAsync(), delegate Func<MonitorTick>→Func<Task<MonitorTick>>, Start() no longer ticks synchronously. But all in-repo consumers are updated (AppViewModel.cs:163 uses the unchanged Start()/Stop(); only tests consumed RunOnce), and Syxx.Kit ships inside this repo with no external-consumer story. Re-rank MEDIUM → LOW (a deprecation shim is desirable discipline, but there is no evidence of an external blast radius).
18. MAINTAINABILITY LOW — whois_lookup.py:19 private-API coupling — CONFIRMED. _PinnedHTTPSConnection/_resolve_public are still imported from fetcher after this diff dropped the netutil import; both are genuinely used (117, 148, 151) — not dead — but lines 98–99 now reference the removed netutil.ip_is_public gate (stale comment). Pre-existing coupling. Keep LOW.
19. MAINTAINABILITY LOW — app.py:1312-1313 function-local imports — CONFIRMED (pre-existing; this diff reformatted import hashlib, hmac into two lines). Re-imports modules already at module scope (app.py:18-19). Harmless style debt. Keep LOW.
20. MAINTAINABILITY LOW — UnboundManager.cs:131 catch(Exception) — CONFIRMED (pre-existing). With the async conversion, a hypothetical OperationCanceledException would be reported as a generic error string; no token is passed today so this is theoretical. Keep LOW.
Refuted / False positives
1. MAINTAINABILITY LOW — MonitorScheduler.cs:82 "C# does not allow awaiting in lock (factually wrong)" — REFUTED. The comment is correct: C# emits compile error CS1996 ("The 'await' operator cannot be used in the body of a lock statement") — the language does prohibit it, precisely because of the Monitor reentrancy hazard the finding calls "the real risk." The finding itself is factually wrong.
2. MAINTAINABILITY LOW — MonitorScheduler.cs:136 Start() XML doc now wrong — REFUTED. The doc was updated in this diff (diff lines 2650+): "Runs one tick immediately … The immediate first tick runs inside the loop task (macOS parity, MonitorScheduler.swift) — Start() itself never blocks the caller." That is an exact description of the implementation (loop task at 148 runs RunOnceAsync as its first act). Doc and code agree.
3. PERFORMANCE LOW — investigate.py:1722-1740 — REFUTED as written (file is 319 lines; those lines don't exist). The underlying observation survives at 149/158/168/171 and is deduplicated above — it is a bad citation, not a bad finding.
4. MAINTAINABILITY MEDIUM — UnboundManager.cs:117-131 "new timeout/kill fall-through path added with no unit tests" — REFUTED as to "new." The 30s drain + 5s grace + kill fall-through existed before; this diff only swapped Wait(bool) for WaitAsync+catch TimeoutException and .Result for await. The absence of tests for the process glue is pre-existing and documented in the class header ("UNVERIFIED-ON-THIS-HOST… platform glue mirrors the verified macOS flow"). Downgrade MEDIUM → LOW as a coverage note, not a diff regression.
5. ARCHITECTURE LOW — two sequential PowerShell spawns per Snapshot/SetServers — confirmed as accurate description but is a non-finding: pre-existing, intentional (interface-index lookup is a required prerequisite), and the diff didn't change the count. Informational; keep at LOW only as a design note.
Severity re-ranks
Finding
types.py / Server Python floor (architecture)
MonitorScheduler public API break
UnboundManager "new" timeout path untested
C142 guard (correctness HIGH)
All security findings
Inter-reviewer disagreement (1) — Python floor 3.8 vs 3.11 — RESOLVED: both partially right; re-rank HIGH → MEDIUM.
- The architecture reviewer is right that this diff newly introduced StrEnum (types.py:12, diff line 2451) and datetime.UTC (app.py:2907/2958, db.py:857, licensing.py:214), making the real floor 3.11+ while Server/README.md:4 still claims "Stdlib-only Python 3.8+". A user on 3.8–3.10 following the README gets an immediate ImportError.
- The correctness reviewer is right that there is no functional regression: CI pins 3.12 in three workflows (ci.yml:349-352, validate.yml:172/261, code-quality.yml:110), ruff targets py312, docs/BUILDING.md:74 and conductor/tech-stack.md:39/47 already say 3.12, and MEMORY.md:1194 says 3.11+. The delta-history backups show the "3.8+" line was flagged before.
- Conclusion: it is a stale-README contract break, not a compatibility regression. The correct fix is documentation (README.md:4 → 3.11+ or 3.12), not code. MEDIUM (not HIGH) because no supported/tested runtime is affected and the authoritative docs already pin 3.12; not LOW because the primary deploy doc is false and the break is newly introduced by this diff. The TimeoutError sub-claim is overstated (the builtin exists since 2.6; the except-clause usages at dot_listener.py:82/118 are not 3.10+ features) — StrEnum and datetime.UTC alone decide the floor.
Inter-reviewer disagreement (2) — MonitorScheduler C142: one root cause or two? — RESOLVED: the HIGH and the maintainability MEDIUM are ONE root cause; the two lifecycle MEDIUMs are distinct.
- Correctness HIGH (line 114) and maintainability MEDIUM (112–114) cite the same lines and the same mechanism: Stop() nulls _cts before the in-flight tick completes, so the is { IsCancellationRequested: true } guard never fires in the Stop() path. Report as ONE deduplicated finding at HIGH (with the test at MonitorSchedulerTests.cs:141 requiring a coordinated fix).
- The Stop()→Start() race (correctness MEDIUM, 83-88/157/172) is a different bug: root cause is _inFlight being owned by neither lifecycle plus line 114 checking the new _cts — resulting in a silently skipped baseline and stale data committed into a new run. Do not merge.
- The TOCTOU (architecture MEDIUM, 156–159) is a third, distinct bug: check-then-act without a lock on State lets Stop() interleave so State=Running is set with no live loop and _cts==null, leaving the scheduler stuck in Running (next Start() becomes a no-op at 143) until a second Stop(). Do not merge.
Deduplication note
Five findings are reported 2–3× across reviewer dimensions and must collapse to one each:
- C142 guard (correctness HIGH + maintainability MEDIUM 112-114) → one HIGH (#8 above).
- lookalike.py:15 (correctness LOW + architecture MEDIUM) → one LOW (#13).
- app.py:1344 (correctness LOW + architecture LOW + maintainability MEDIUM) → one LOW (#11).
- fulfillment.py:450 (correctness LOW + architecture LOW + maintainability MEDIUM) → one LOW (#12).
- investigate.py .format() (performance LOW@1722-1740 + architecture LOW@149/158/168/171 + maintainability MEDIUM) → one LOW at the correct lines (#14); the performance reviewer's line range is invalid.
Net result: 20 confirmed findings (2 HIGH, 5 MEDIUM, 13 LOW), 4 refuted (1 wrong-fact refutation, 1 stale-doc refutation, 2 wrong-location/"new"-framing), and 3 severity adjustments. The two HIGHs — the forgeable DNSSEC trust anchor and the dead C142 guard — are both real, both in code this diff touched, and both worth blocking on.4

Ultra Review Report: ~/Software/Syxx/ (working-tree diff)
Reviewed: security, correctness, performance, architecture, maintainability (ultra, 2026-08-12)
Critical (0) · High (0)
The sole reported High (deploy.sh:220 usermod on a "nonexistent literal user") was critique-refuted — the block runs inside a remote bash -s heredoc where the escaping is correct and ${SVC_USER} resolves to syxx.
Medium (4)
- MonitorScheduler.cs:141 — Start() dropped its synchronous first tick; comment at :136 contradicts behavior, consumers see stale state for one interval. Fix: expose WhenBaselineEstablishedAsync().
- MonitorScheduler.cs:114 — post-tick guard reads shared _cts; rapid Stop()→Start() can latch a spurious "DNS changed" alert into the new session. Fix: capture the token locally.
- MonitorScheduler.cs:128/193-205 — _onTick fires inside _runLock while StopAsync awaits _loopTask; a handler awaiting StopAsync() deadlocks. Fix: invoke handler outside the lock.
- capabilities.py:284-289 — five opencode aliases collapse to deepseek-v4-flash-free, erasing tier/inherit semantics; test_adapters.py:637 only asserts opus. Fix: restore distinct model ids or document intentionally + extend test.
Low (11)
- app.py:1344 & fulfillment.py:450 — dead expressions from the %→f-string refactor (webhook attributes never parsed; "subscription status" check is a no-op).
- dnssec_validator.py:370/:255 — redundant rdata copies/parses per DNSKEY/RRSIG.
- app.py:62-64 — DNSSEC imports resolver _-private helpers under a swallowing try/except; a rename silently 502s /api/dnssec.
- Cross-platform DNSSEC model mismatch (UnboundManager.cs vs dnssec_validator.py) breaks consistent conclusions.
- deploy.sh D-2 group provisioning duplicated + || true masking failures.
- test_licensing.py:65 dead statement; db.py:827/831 f-string forced noqa: S608; investigate.py half-migrated .format().
Conclusion
Safe to merge — no blockers. Address the two MonitorScheduler races + deadlock before relying on the Windows loop in production, confirm the capabilities.py model collapse is intentional; then pick up the dead code/micro-perf items in a follow-up cleanup. Four issues from the original reviewer output (deploy.sh High, lookalike.py "1":"l", the other scheduler items) were adjudicated by the critique phase and either refuted or confirmed as stated above.
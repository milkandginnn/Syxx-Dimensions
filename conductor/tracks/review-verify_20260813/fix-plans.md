# Fix Plans — review-verify_20260813

One plan per confirmed defect. **No code is written in this track.** Each plan names its cross-runtime twin or states there is none.

Three findings are closed no-change (A3, A5, I2). Two need an owner decision before any implementer touches them (A1's trust model, J1's model ladder).

## Sequencing rules that override finding order

1. **Anchor before plumbing.** The `/api/dnssec` name-form blocker must not be repaired before A1. Fixing it alone converts a permanently-undetermined endpoint into one reporting `valid` under active attack. This is the single most important ordering constraint in the track.
2. **B1 ships with its test.** `MonitorSchedulerTests.cs:141` asserts the broken guard. One commit, or CI goes red for the wrong reason.
3. **B3 before B5 before B2's remainder.** B3 introduces the lifecycle lock the others need; B5 widens the window B2 depends on.
4. **A1's three parts are one fix.** Anchor pin, root RRSIG check, and DS RRSIG verification at each hop. Without the third the anchor is decorative — trust still never propagates.

---

## P1 — DNSSEC trust chain (A1 + A2 + the blocker) · **owner decision required**

**Approach.** This is a trust-model change, not a patch. Before implementation it needs a design decision on anchor provisioning and rotation, which is outside an implementer's authority.

1. Vendor the IANA root anchor (`root-anchors.xml`) as repository data; pin its digests. **Take whatever IANA currently publishes — do not hardcode a key tag from memory or from a review note. 19036 is KSK-2010, retired in 2019; pinning it would itself be a defect.**
2. Before recording the root step, match a root DNSKEY against a pinned anchor digest, then verify the root DNSKEY RRset's own RRSIG using that anchor-matched key. No match → undetermined, never `valid`.
3. Verify each DS RRset's RRSIG against the parent's already-validated DNSKEY before consuming it.
4. Delete `_ksk`'s tier-3 `rs[0]` fallback (A2). Anchor selection becomes `(keyTag, algorithm, digest)` match. **A "pick whatever is first" fallback behind a trust anchor reintroduces the same defect class.**
5. Fail closed at every gate.
6. **Only then** repair the name-form blocker so the endpoint functions.

**Files.** `Server/syxx_server/dnssec_validator.py`, `Server/syxx_server/resolver.py` (name form), plus a new anchor data file.

**Tests.** Red first, using the `_FakeSock` pattern from `test_doh.py:63`, not `dns_sim.py` — that is a pure classifier with no sockets. Cases: hostile root key rejected; anchor-matched root accepted; DS RRSIG forged at each hop rejected; anchor rotation with two valid anchors; anchor file missing or unparseable → undetermined, never valid.

**Blast radius.** `/api/dnssec` is a free-tier unauthenticated GET. Behavior changes from "always undetermined" to a real result, so this is the first time the endpoint returns anything meaningful. Every consumer must handle a fail-closed undetermined.

**Rollback.** Anchor file removal returns the endpoint to undetermined — safe, not silent.

**Twin: none.** Both clients deliberately refuse to claim validation and emit `recordsPresentUnverified`. Do **not** port chain validation to them as part of this fix; that is proposal A2 on both platforms and a separate decision.

## P2 — RSASHA256 never verifies (A4) · **High**

`_vrsa` feeds raw RFC 3110 key material to `load_der_public_key`. Parse RFC 3110 properly (1-byte exponent length, or `0x00` plus 2-byte length), build via `RSAPublicNumbers(e, n).public_key()`, then apply the modulus floor — reject n < 1024 bits and zero exponent length. **Narrow the bare `except Exception` to `InvalidSignature`** so a parse failure surfaces as undetermined rather than masquerading as a cryptographic failure.

Independent of P1 and safe to land first — it removes a false *tamper* claim rather than creating a false clean. Tests: a real alg-8 RRSIG verifies; a malformed key yields undetermined, not `False`.

**Twin: none.**

## P3 — QDCOUNT hardening not carried over (A6) · **Medium**

Three lines mirroring `resolver.py:227-231`, placed before the question loop in `dnssec_validator._parse`. Reject `qd != 1` outright.

**Twin: `resolver.py` already has it** — this is a within-package parity gap, and the fix is to carry A061 across.

## P4 — MonitorScheduler lifecycle (B1–B5) · Windows

| Step | Fix | Note |
|---|---|---|
| 1 | **B1 + test** — capture the token (or a generation counter) at `RunOnceAsync` entry; test the captured value, not the `_cts` field. Update `MonitorSchedulerTests.cs:141` to expect 1 and rewrite the comment documenting the wrong invariant | One commit. Also repairs B2's stale-commit half |
| 2 | **B3** — add a dedicated `_lifecycleLock`; take it in `Start`/`Stop`/`StopAsync` and around the check-then-assign pair; re-check the token and generation before assigning `State`. **Do not reuse `_runLock`** — `_onTick` runs under it, which would make B4 certain | Permanent wedge; highest of the five |
| 3 | **B5** — move the `_inFlight` release to after the state commit | After B1 |
| 4 | **B2 remainder** — the loop's first tick retries with short backoff rather than treating null as a completed baseline. `Start()` must **not** clear `_inFlight`; two overlapping ticks would stomp the snapshot file | Design call, not a one-liner |
| 5 | **B4** — hoist `_onTick` out of the lock | Independent; also removes disk I/O and toast construction from under the lock |

**Twins.** B1: Swift tests the running task's own cancellation, which survives `stop()` — restore those semantics. B2's skipped baseline exists on macOS too, but macOS records `skippedTickCount` and `lastTickAttemptedAt` (C111) so the miss is visible; **port that accounting to C#**, which currently returns null silently. B3, B4: C#-only. B5: C#-only.

## P5 — Silent DNS reset (E1) · **recommend raising from Low**

Replace the `IsCompleted ? await : ""` pattern with a bounded second drain after exit, and **make "not drained" distinguishable from "empty"** — return `code = -1` when either read task is still pending, routing `SnapshotAsync` to its existing "could not read" branch. Attach a faulted-continuation to both tasks so abandoned reads are observed.

This is the fix that matters most in cluster E: without it, a slow drain launders "could not read" into "no servers configured", bypasses both D099 guards, and silently resets the user's static DNS to DHCP.

**Twin: none** on the mechanism — but see P8, which is the macOS side of the same safety property.

## P6 — Windows process glue (E3, E4-shape) · Low

E3: insert `catch (OperationCanceledException) { throw; }` before the general handler. One line, no behavior change today, closes the trap before a token is threaded through.

E4's filed async-versus-sync shape divergence needs **no fix** — the async form is correct for its platform and deliberate.

## P7 — Keygen webhook replay (G2) · Medium

The payload exposes no event id, so key the guard on the already-computed HMAC, mirroring `fulfillment.py:417`'s compare-and-set. Duplicate returns 200 with a duplicate conclusion.

**Open question, undetermined under network denial:** which headers Keygen actually sends. The handler consumes a bare hex HMAC, not the keyId/algorithm form the validate-key path parses, so a freshness window may not be expressible. **The idempotency half stands alone and should not wait on it.**

**Twin: `fulfillment.py`** already has the pattern.

## P8 — macOS cannot express "could not read" (E4-nullability) · parity

Swift's `snapshot() -> [String]` is non-optional, its consumers guard nothing, and `disable()` restores unconditionally — so **macOS has no D099 safe-restore-point guard, because its protocol shape makes one impossible.** Change the return to `[String]?` and add the guard.

Direction is the opposite of the filed finding: **C# is ahead on safety here.**

## P9 — Corpus schema asymmetry (L2) · Medium

One line at `WarpEngineTests.cs:119-120`: replace `TryGetProperty` with `GetProperty` so a missing `manifestState` fails instead of silently skipping the assertion. Cleaner: deserialize `expected.json` into a record with required members, mirroring Swift's `ExpectedCase`.

Also tighten the C# case-directory filter to `^\d{3}_` for exact parity.

**Twin: Swift is already strict** — this is C# catching up.

## P10 — Test-quality and cleanup · Low

- **L3** — extract the backup guard as a **value type, not a base class**. Forgetting `super` in `setUp` fails safe; forgetting it in `tearDown` loses the restore and leaves a test key in a developer's keychain. Per-instance state required; never `static`. Correct locations are `AppModelWitnessTests.swift:42-73` and `WitnessTestifyParityTests.swift:129-157`. A third site in a different test target cannot be covered without relocating the support type.
- **L1** — assert the `SecItemDelete` status so a leak is observable. Hygiene; optional.
- **F1, F3, I3** — delete the dead expressions and the function-local re-imports.
- **F2** — delete the dead expression **and** file the reinstatement guard separately (P11).
- **F4** — convert four `.format()` calls to f-strings. Consistency only; the performance framing was rejected.
- **I1** — the one-line walrus at `:370` only. The `_psig` hoisting is **not** worth the churn: it restructures the key × signature loops inside signature verification, where a subtle change alters which pairs get tried.
- **A7** — log the swallowed import and correct the misleading 501; promote or explicitly re-export the seven private resolver helpers.
- **K1** — `Server/README.md:4` to 3.12+. Documentation only. **Adjacent:** `uv.lock` declares `>=3.14`, a third floor disagreeing with CI's 3.12 and ruff's py312.
- **K2** — substitute the banned term at three sites in the user guide.
- **D2** — restore a first-tick assertion.

## P11 — Unearned license reinstatement (F2b) · owner ranks

The comment claims an active-subscription guard the `invoice.paid` path does not perform, so a license is reinstated on any paid invoice. Either wire the check up or correct the comment. The deleted binding was the last trace of a guard never implemented.

## P12 — Model tier ladder (J1) · **owner decision**

Not a fix — a decision. The working tree collapses five model aliases to one free-tier model in a single uncommitted hunk, while `docs/harnesses.md:63-64` still documents the ladder. **Every OpenCode subagent, including the security-auditor and code-reviewer roles, is currently pinned to one free-tier model.**

Either revert to the HEAD ladder, or keep the pin deliberately and land it properly: header comment, docs update, all five rows asserted in tests, mirrors regenerated.

## P13 — AGENTS.md governance (J2, J3) · marketplace

Scope the self-edit authorization to explicit owner-approved changes or remove it; the file governs every agent in this repo and `CLAUDE.md` symlinks to it. Reconcile the contradictory agent counts against `ls plugins/*/agents/*.md`. Add the missing `rules/BUDGET-RULES.md` entry and the `rules/` path prefix.

## P14 — Forged-packet remaining sites (C1, C2) · already tracked

Recorded as N20 in `review-fixes_20260812`. Not re-planned here. **That track owns them; check its state before touching `VantageComparer`.**

## Out of scope, routed to parity

The C# `Classify` takes no queried-name parameter, so the Windows DNSSEC stripping canary can be defeated by appending a valid-window DNSKEY+RRSIG pair from any unrelated signed zone — the attack Swift's C059 comment describes defending against. Found while establishing cluster A's cross-runtime status; belongs to whoever owns parity.

## Owner decisions outstanding

| # | Decision | Recommendation |
|---|---|---|
| 1 | A1 anchor provisioning and rotation | Vendor IANA's current anchor; fail closed when absent |
| 2 | Whether A1 splits into its own security track | Yes — trust-model change with coupled sequencing |
| 3 | J1 model ladder: revert or keep the pin | Revert; the docs already describe the ladder |
| 4 | F2b: wire the guard or correct the comment | Wire it; the comment describes the intended behavior |
| 5 | E1 and B3 severity raises, B4 lower | Accept all three; the evidence is specific |
| 6 | Whether the clients ever gain chain validation | Separate proposal on both platforms; not this track |

## Before implementation

**A1's design needs a planning specialist** under `SUBAGENT-RULES.md` §4.1 — anchor provisioning, rotation, and fail-closed semantics are architecture decisions, not an implementer's call. Every other plan here is implementer-ready.

# Workflow

## Testing policy — evidence-gated, not test-first

Tests need not precede the code. **A claim needs evidence before it counts.** The test may be
written after the implementation; what may not happen is an assertion entering a document, a commit
message, or a UI string before something has executed it.

What counts as evidence, strongest to weakest:

1. A shared golden vector both engines execute (`corpus/`, `warp-manifest-vectors/`,
   `Server/tests/conformance/`)
2. A blocking CI gate that fails the build on divergence
3. A test that runs in one runtime
4. A local run whose output was inspected
5. Reading the code — **not evidence.** It produces candidates to verify, never conclusions.

### Two rules that make the above real

**A harness must prove how many cases it ran.** A suite that silently skips is worse than no suite,
because green reads as coverage. `WarpEngineTests.swift` currently `return nil`s on brotli cases and
the caller `continue`s — no skip marker, no counter — so a green run says nothing about how many of
the 31 cases executed. Every corpus-driven harness asserts `evaluated == case_count`.

**A gate must be able to fail.** Before adding one, ask what makes it go red. `cross-platform-parity-gate.py`
exists because the job it replaced could only pass: it emitted a warning and `exit 0` on every
reachable path, and rendered as a green check while measuring nothing. Gates fail on empty input and
on incomplete input, not just on mismatch.

## Verification checkpoints — per claim, five states

Every assertion carries exactly one state. **"Fixed" is deliberately not available.**

| State | Means | What it is worth |
|---|---|---|
| `open` | The defect stands. Nothing has landed. | — |
| `fixed-unverified` | A change is in the tree. **Nothing has executed it.** | A claim — the author's reading of what the code will do |
| `verified-by-CI` | A CI job ran this exact assertion and went green | Evidence, re-checked on every push |
| `verified-by-real-run` | Executed on a machine, output inspected, by a named party | Evidence for one machine at one moment |
| `deliberately-out` | Decided not to do; reason recorded | A decision, not a gap |

Verification is per claim, not per phase or per task. A task may close with several claims at
different states, and that is the honest outcome — better than one summary state that averages them.

## Commit strategy

Descriptive sentences. **No conventional-commit prefixes** (`feat:`, `fix:`). Consistent across the
whole log; match it.

```
Linux parity: build pass, security fixes, libsecret search binding, test gaps
Fix three plan errors, one of which would have misdirected a seat
G-16: the libsecret backend has never stored anything on Linux
```

**Correcting commits are first-class.** When a prior commit or document overstated something, the
correction is its own commit and says what was overstated:

```
Correct the G-02 ledger line: my wording erred toward reassurance
Second-pass parity survey; correct pass 1's overstated premise claim
```

This is the mechanism that keeps the ledger trustworthy. Do not fold a correction silently into an
unrelated commit.

## Code review

Required for non-trivial changes. Parity-affecting, crypto, and conclusion-lattice changes are
always non-trivial.

Reviews split by dimension rather than by file — security, correctness, parity, honesty-of-claims —
because the failure modes are independent and a single reviewer tends to find only the first kind.

## Task lifecycle

1. **Scope** — state what the task claims it will make true.
2. **Implement.**
3. **Evidence** — attach the strongest available from the ladder above. If it is level 4 or 5, the
   claim is `fixed-unverified`; say so rather than rounding up.
4. **Record** — one row per claim with its state, in the relevant ledger.
5. **Close** — a task may close with `open` and `fixed-unverified` claims remaining. What may not
   happen is closing with those claims described as done.

## Reading a failing suite

**Check failure names, never counts.** "~12 pre-existing failures" was treated as baseline noise;
naming them individually revealed at least four real and unrelated defects — stale assertions
expecting the wrong case, mocks using RFC 5737 addresses that a later SSRF guard rejects, and a
timeout divergence. Treat "known baseline" as an unaudited list, not a cleared one.

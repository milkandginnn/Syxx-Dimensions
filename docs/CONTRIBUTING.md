# Contributing to Syxx

Syxx detects DNS tampering, hijacking, spoofing, and censorship, and produces evidence a user can
actually trust. Almost every rule below follows from that second clause. A detector that is merely
usually right is worse than useless in this domain, because a user acts on its reassurance.

## Read these first, in this order

| Document | Why |
|----------|-----|
| [`AGENTS.md`](AGENTS.md) | **Binding.** The authoritative rules. **Never edit it.** A PreToolUse hook blocks edits, and only the owner may change it. |
| [`PROJECT-STATE.md`](PROJECT-STATE.md) | Canonical current state. If what you are about to do contradicts it, stop and reconcile rather than freelance. |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | How evidence flows from probe to conclusion. |
| [`DELTAS.md`](DELTAS.md), [`ROADMAP.md`](ROADMAP.md), [`DESIGN.md`](DESIGN.md) | Open defects, status, and the functional map. |

Where a document and the code disagree, **the code is the truth and the document is a defect.** Fix
the document; do not fix reality to match the document.

## The honesty spine

Six rules bind every change, at **every intermediate commit**, not merely at the end of a branch.

### 1. No conclusion before evidence

Never emit or render a conclusion, confidence, or reassuring label before its inputs have been
queried, parsed, and validated. Code that renders a conclusion before its evidence exists is
incorrect by definition, whatever the tests say.

### 2. Missing data is not clean data

Missing, invalid, degraded, or timed-out becomes `inconclusive` or `unknown`. Never a silent clean,
secure, or success. A timeout is not a negative result. An empty packet capture is not a clean
packet capture.

### 3. Tri-state, never boolean

Preserve secure/tampered/inconclusive and success/failure/unknown end to end. Any operation that can
fail, time out, or return something ambiguous must return a tri-state type, never a bare `Bool`.

This is the single most common defect class in `DELTAS.md`, and it recurs because it is easy to
backslide under time pressure. A `Bool` silently merges "we checked and it was fine" with "we could
not check", and once merged those two facts can never be separated again downstream.

Note the corollary in `docs/ARCHITECTURE.md`: `inconclusive` (ran, valid data, did not resolve) is
**usable evidence** and does not hard-gate, while `untested` (never ran) and `invalidated` (ran,
data corrupt) are **not usable** and do hard-gate. Collapsing those three into one state is a
structural misclassification. Get this right before touching anything in `Synthesis/`.

### 4. No fabricated value shown as measured

Every user-visible number must trace to live evidence, not a mock, a default, or a placeholder. No
hardcoded success, secure, or tamper strings in a production path.

Syxx once shipped a suite-green build that printed "Success... 0 queries captured... 31%
inconclusive" in one output. Every test passed. That is the failure this rule exists to prevent, and
`Conclusion.mockInconclusive` is `#if DEBUG`-gated with a CI symbol-absence check precisely because
a fabricated fixture must be structurally unreachable from a release binary.

### 5. The bar is a real run with honest output, not green tests

One real run per phase, per platform. **Never edit, disable, soften, re-flag, or delete a test to go
green.** A failing honesty test is data.

A real run means executing from a production entry point, on real input, capturing the output, and
verifying it against the specification. Unit tests with stubbed dependencies do not count.

You should not expect the app to pass every test. Until it works correctly all the time, it should
be failing some. If it is passing everything, the tests are probably wrong.

### 6. Platform parity

**macOS is the gold standard.** A `SyxxKit` change needs its `Syxx.Kit` twin. Trust-material changes
touch `Server/` too.

Parity is reached by bringing Windows and Linux **up to** macOS. Changing macOS to match Windows or
Linux is a regression, not a fix. Ship order does not alter this: Linux ships first and macOS ships
last, and macOS remains the reference implementation throughout.

## Lanes: the three-way split

Work is divided into three lanes. **Stay in your lane.** If you need something in another lane,
**flag it; do not edit it.**

| Lane | Owns | Paths |
|------|------|-------|
| **Claude** | The reasoning engine, plus design, planning, and Stage-5 wording | `**/Synthesis/**`, `Conclusion.*`, the `Report.*` extractor, the golden-vector oracle |
| **Cline** | Server, trust and GroundTruth consumers, probes, the labeled corpus | `Server/**`, probe implementations, capture corpus |
| **Copilot** | GUI and CLI **app** surfacing | `SyxxApp/**`, `SyxxUI/**`, CLI report layout |

The boundary that matters most: the app lane renders what the engine produced and **must not reach
into engine internals**. The engine gates on its own validator; a surfacing change that bypasses
that gate defeats it. Equally, the engine lane does not silently restyle user-facing copy.

**Only a lane's owner edits that lane's tests.** The golden-vector corpus is engine-owned.

## Every test must have a proven failure case

A test that cannot fail is not a test. It is a comment that costs CI time.

For every test you write, state both paths explicitly:

- **Happy path:** input, expected correct output.
- **Failure path:** the specific input variant or code defect that **must** make this test fail.

**The litmus test: if you can break the code and the test still passes, the test is broken.** Do not
merely reason about this. Actually break the code, watch the test fail, then restore it. A failure
case you have not observed is a hypothesis.

Worked examples from this codebase:

| Test | Proven failure case |
|------|--------------------|
| Signing | Corrupt one byte of the signature; verification must fail. |
| Cross-platform DTO byte-identity | Rename a field on one platform only; the parity test must detect the mismatch. |
| Public-key pinning | Serve the wrong pubkey; verification must fail **and** raise a tamper signal. |
| C061 fixture absence | Remove the `#if DEBUG` guard around `Conclusion.mockInconclusive`; the release symbol-absence step must fail. |
| Honesty-coverage gate | Delete a registered security guard; the gate must fail even though the suite stays green. |

A test asserting on a hardcoded value, or whose assertion is always true, is invalid and must be
rewritten or removed.

**When a test fails, file it, do not fix it away.** Add a delta recording what the test expected,
what the code produced, and whether the cause is a code defect, a test-design error, or a spec
ambiguity. Then decide whether to continue the workstream or deviate to fix the underlying code.
Failed tests are findings.

## Commits

### Explicit paths only

```bash
# Stage named paths, one at a time.
git add path/to/the/file.swift path/to/the/twin.cs

# ALWAYS review what is actually staged, before every commit.
git diff --cached --name-only

git commit -m "..."
```

**Never `git add -A`. Never `git add .`.** A PreToolUse hook (`.claude/hooks/lane-guard.sh`) blocks
it, but the rule matters independently of the hook: this is a multi-lane repository where several
agents have files open simultaneously, and a wildcard stage silently commits another lane's
in-flight work under your message. If `git diff --cached --name-only` lists a file you did not
intend, unstage it before committing.

### Commit granularity

Commit per step, so a crash loses nothing. Every intermediate commit must independently satisfy the
honesty spine. "It is fixed by the end of the branch" is not sufficient; nothing may read clean,
secure, or success for a tampered or unmeasured state at any commit in the history.

### Pre-commit gate

Before claiming a task complete, answer these six inline in the commit message or agent log:

1. **Evidence first.** Did any user-facing conclusion render before all required data was collected
   and validated? If yes, not done.
2. **Real run.** Can you show a real run (logs, CLI output, traces) proving the feature works on
   real input, not just tests?
3. **Math check.** Do any confidence scores give high confidence on empty or degenerate input? If
   yes, the math is wrong.
4. **Mocks.** Does any production path print fake success, secure, or tamper messages? If yes,
   remove them.
5. **Scope.** Does this change add machinery on top of untrustworthy signals? If yes, narrow the
   scope or flag it in `DELTAS.md`.
6. **Honesty.** If something is broken, did you say so plainly in your report and in `DELTAS.md`?

The work is done only if the answer is **no** to 1, 3, and 4, and **yes** to 2, 5, and 6.

## Keep the ledgers in sync

| Event | Goes in |
|-------|---------|
| New defect found | `DELTAS.md` (never buried in a commit message) |
| Item closed | Status updated in `ROADMAP.md` |
| Architecture changed | `DESIGN.md` |
| Work performed | `CHANGELOG.md` |
| New user-facing string | `outputs-catalog.csv`, one row: `file:line,trigger,module,task,output,change` |

Anchor every change to an open delta ID or a ROADMAP workstream.

A wrongly green roadmap row is the same defect class as a wrongly green conclusion. Do not mark
something done because a file now exists; mark it done because its contents were verified. "Created"
is not "correct".

## Citing code

**Always cite `file:line`.** This applies in `DELTAS.md`, `ROADMAP.md`, `CHANGELOG.md`, `DESIGN.md`,
code review, and agent reports.

- Insufficient: "The synthesis engine has a bug in the comparison logic."
- Sufficient: "`SynthesisEngine.swift:78-84`: the comparison logic does not handle a nil
  `authoritativeSet`."

## Style rules

These are binding, not preferences.

### No em-dashes

Do not use em-dashes to join clauses or fragments. Use a comma, a colon, a semicolon, parentheses,
or two sentences. This applies to code comments, documentation, commit messages, and every
user-facing string.

### Do not use the word "verdict"

| Context | Use |
|---------|-----|
| User-facing text | **Conclusion** |
| Code identifiers | **Result** or **State**: `DeterminativeResult`, `DeterminativeState`, `LeakResult` |

A rename is in flight: `DeterminativeVerdict` is becoming `DeterminativeState`, and `LeakVerdict` is
becoming `LeakResult`. Write new code against the new names.

**The wire format is exempt and is not changing.** The lowercase JSON values `"clean"`,
`"threatDetected"`, `"inconclusive"`, `"untested"`, and the `"verdict"` JSON key inside the signed
collector payload, are canonical bytes covered by Ed25519 signatures and frozen parity fixtures.
Renaming them would invalidate existing evidence. Document the wire format as it actually is.

### User-facing output requires owner approval

Before implementing anything that produces user-visible output (CLI messages, report text, UI
labels, error strings), assess what strings the change will produce, propose the exact wording, and
wait for approval. Ship only approved copy; an unknown token must fail closed to a generic message.

## Building and testing

See [`docs/BUILDING.md`](docs/BUILDING.md) for per-platform commands. Every command there carries an
explicit marker for what evidence backs it, and commands that genuinely cannot be run in a given
environment are marked UNVERIFIED rather than presented as tested. Please preserve that convention.

Build **both** Debug and Release, and build the app bundle, not just the binaries.

## Anti-Syxx failure modes

If you notice any of these, you are in a Syxx-like failure mode. Stop adding features, narrow to one
honest flow, record it in `DELTAS.md` as a foundational defect, and say plainly what does and does
not work:

- A conclusion rendered before its evidence.
- Broken math presented as "confidence".
- Mocked output treated as real.
- Passing tests written against the wrong specification.
- Premature aggregation over signals that are not yet trustworthy.
- Scope explosion across disciplines and platforms.

## Reporting

Describe capabilities and limitations accurately. Never minimize a defect, inflate confidence, or
soften broken behavior. Report problems proactively rather than only when asked.

Prefer "This does not work", "This is structurally wrong", or "This layer is not trustworthy yet"
over "it is mainly a UX issue" or "a minimal patch fixes it", unless that is literally and narrowly
true. If you are uncertain, say "I don't know" and list what would have to be measured to find out.

The job is to be correct, not comforting.

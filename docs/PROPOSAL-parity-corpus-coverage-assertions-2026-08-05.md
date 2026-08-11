# Proposal: coverage-integrity assertions for the parity-vector corpus

**Date:** 2026-08-05
**Track:** `conductor/tracks/cross-platform-parity_20260805` (Phase 6 — Task 6.5, and the case-count
half of Task 6.2)
**Status:** proposal. Nothing here is implemented; every snippet is a diff to be applied by whoever
owns the file. **Revised 2026-08-06** after review by `ci-wiring-planner` — one factual correction
and two scope changes, all recorded in §8. Read §8 first if you have read an earlier revision.
**Scope boundary:** this document does **not** plan the switch of the CI emit steps to
`--vectors=` — that is `docs/PROPOSAL-parity-transcript-corpus-ci-2026-08-05.md`. Everything below
assumes that switch has landed and the four legs emit corpus envelopes. Where the two proposals
touch the same step, this one says so and defers.

---

## 1. The hole, stated precisely

The engine-side coverage guard is done and it is real. Both emitters count `evaluated` per case
actually concluded and refuse to write when it disagrees with the case count:

- `macOS/Sources/SyxxCLI/Commands/ParityTranscript.swift:109-119` — counter at `:113`, guard at
  `:116`.
- `Windows/dotNET/Syxx/Commands/ParityTranscriptCommand.cs:166-195` — counter at `:185`, guard at
  `:188`, and the `continue` at `:182` that makes the counter capable of disagreeing.

The envelope is `{schemaVersion, caseCount, evaluated, records}`
(`ParityTranscript.swift:121-126`, `ParityTranscriptCommand.cs:197-203`).

**Both numbers come from the same list.** `caseCount` is `cases.count` /
`cases.Count`, and `cases` is whatever `ParityVectorLoader.load` returned. A case file the loader
never saw is absent from `caseCount`, absent from `evaluated`, and absent from `records`. The three
numbers agree perfectly about a corpus that shrank. Both engines shrink identically, so the
`parity-transcript` diff at `.github/workflows/ci.yml:493-520` is clean. This is the F-27 defect
class (commit `9d2550b`) moved up one level: not "the two loaders disagree", but "the two loaders
agree about the wrong thing".

### 1.1 Every path where a file on disk fails to become a case

I read both loaders end to end. Each has exactly **two** `continue` statements; every other
rejection throws and names the file. The two skips are the entire silent surface.

| # | Swift | C# | Silent on both? |
|---|---|---|---|
| S1 | `ParityVectorLoader.swift:190` — `if name.hasPrefix(".") { continue }`, inside the `isDirectory` branch opened at `:184` | `ParityVectorLoader.cs:192` — `if (name.StartsWith(".", StringComparison.Ordinal)) continue;`, inside `if (Directory.Exists(entry))` at `:185` | Yes, but only hidden **directories**, which cannot be cases under a file-per-case format |
| S2 | `ParityVectorLoader.swift:195` — `guard entry.pathExtension == "json" else { continue }` | `ParityVectorLoader.cs:197` — `if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;` | **Yes — this is the hole** |

Everything else is loud, and deliberately so:

- non-hidden directory entry → throws (`swift:191-193`, `cs:193-195`)
- `.json` name that is not `NNN_<id>` → throws (`swift:196-200`, `cs:198-203`)
- zero candidates → throws (`swift:204`, `cs:207-212`)
- duplicate `NNN` → throws (`swift:207-213`, `cs:214-225`)
- unreadable file, invalid JSON, unknown/missing top-level key, bad `schemaVersion`, `vectorId`
  mismatch, unusable `pinnedUnixSeconds`, unknown `sectionInputs` key, undecodable value, unknown
  nested member → all throw with the case named (`swift:235-273, 312-379, 485-547`;
  `cs:252-366, 413-487`).

So **S2 is the only way a file sitting in `corpus/parity-vectors/` becomes zero cases on both
engines without a word.** Concretely: rename `005_glutton-ecs-forwarded.json` to
`005_glutton-ecs-forwarded.json.orig` — a merge artifact, a `.rej`, an editor backup, a
`sed -i ''` on macOS that leaves a sibling — and `pathExtension` is `orig`, `EndsWith(".json")` is
false, both loaders step over it, `caseCount` is 10 on all four legs, the guard passes, the diff is
clean, and the ECS case is gone.

### 1.2 The trap in the obvious fix

The brief proposed asserting `caseCount == number of corpus/parity-vectors/*.json files`. **That
assertion does not close S2**, because the shell glob `*.json` is the same predicate the loaders
skip on. The renamed file is invisible to `ls corpus/parity-vectors/*.json` for exactly the reason
it is invisible to the loaders. The count would be 10 on both sides and the gate would agree that
everything is fine.

The count has to be taken **without the loaders' filter**. That is the difference between a check
and a second copy of the bug. Section 3 does it as a stray sweep over every directory entry.

### 1.3 The failure mode no on-disk count catches

If the file is *deleted* rather than renamed, the disk count drops with `caseCount` and every
count-based assertion still agrees. Nothing that derives its expectation from the corpus directory
can see a deletion. The expectation has to live **outside** the directory and be version
controlled. Section 4 does that with a committed manifest.

### 1.4 One residue worth naming, and why it is not in scope here

Swift's `assertNoDroppedMembers` cannot round-trip `nxCanary`, `dnssecCanary`, `leak` or `exfil`
(`ParityVectorLoader.swift:475-479`, the `default: return nil` at `:479`), so an unknown member
nested inside those four is still silently dropped by Swift's synthesized decoders — while C#'s
`Walker.Done()` refuses unknown members at every depth (`ParityVectorLoader.cs:86-89`). That
asymmetry is real, it is already recorded under F-27, and it fails **loud**: C# throws, the C#
emitter exits 3 (`ParityTranscriptCommand.cs:148-152`), the transcript is not written, and
`if-no-files-found: error` at `ci.yml:265` plus the four-transcript guard at `ci.yml:502-506` turn
the build red. It is a divergence, not a silent-coverage hole, so it is out of scope for this
document.

---

## 2. Assertion A1 — envelope self-consistency, checked by the gate

**Where:** inside the `parity-transcript` job's diff script, at the marked insertion comment
immediately before the final `rc` check — the contract agreed with `ci-wiring-planner` on
2026-08-06 (see §8). At that point `${corpus[@]}` holds the four **corpus** artifact paths and
`rc` is the failure accumulator. **Set `rc=1`; never `exit`** — an early exit stops the later
assertions from reporting, and a gate that stops at the first finding makes the operator re-run CI
to discover the second.

**Scope — this is load-bearing.** `ci-wiring-planner` is keeping the single-vector transcript as a
second artifact after the corpus switch (it is the only cross-runtime comparison of the
`--vectors`-absent dispatch, and it loses no engine coverage because corpus case
`001_empty-section-inputs.json` pins the same epoch `1752268477` as `ParityTranscript.swift:15`).
Artifacts are therefore split by directory: `corpus.<runtime>.json` → `corpus-transcripts/`,
`transcript.<runtime>.json` → `transcripts/`, each with an explicit `pattern:` on
`download-artifact`. **A1 must read the corpus group only.** Pointed at `transcripts/` it would
report four malformed envelopes on a correctly working gate, because a single-vector record has no
`caseCount` key by design (`ParityTranscript.swift:55-68`).

**What it asserts:** for each of the four corpus envelopes, `evaluated == caseCount`,
`records | length == caseCount`, and `caseCount > 0`.

**Honest framing.** Today this cannot go red on any live path, and the proposal is weaker if it
pretends otherwise. Any envelope that exists at all already satisfies it, because the emitters
refuse to write when it does not (`ParityTranscript.swift:116`,
`ParityTranscriptCommand.cs:188`). A1 buys three things and no more:

1. It stops trusting the thing being tested. The emitters' refusal is source that a future edit can
   remove; A1 lives in the gate, so removing the refusal produces a red build rather than a
   quieter one.
2. It catches a **truncated or partially-uploaded artifact** — the download at `ci.yml:487-491`
   has no integrity check, and a half-written `records` array is exactly what A1 reads.
3. It catches a **corpus artifact that is not a corpus envelope** — if a leg's emit step is left
   un-switched during the Task 6.1 rollout, it uploads a bare record under the corpus artifact
   name, and A1 names that leg instead of the diff blaming the engines. This branch supersedes
   `ci-wiring-planner`'s `Not a corpus transcript` check; only one of the two ships (§8).

Three of jq. Worth having; not the assertion that closes the hole.

Written as a fragment for the marked insertion point, not a standalone step:

```bash
          # --- Corpus coverage integrity, part 1 of 2: each envelope must agree with itself.
          # The emitters already refuse to write an envelope whose `evaluated` disagrees with its
          # `caseCount` (ParityTranscript.swift:116, ParityTranscriptCommand.cs:188). This asserts
          # it again HERE because a guard that lives only in the thing being measured is a guard a
          # future edit can delete with a green build. It also catches the two states the emitters
          # cannot speak for: a truncated artifact, and a leg that uploaded a single-vector record
          # under the corpus artifact name (no caseCount key at all — ParityTranscript.swift:55-68).
          # Iterates ${corpus[@]} — the CORPUS group only. Pointed at the single-vector group it
          # would report four malformed envelopes on a correctly working gate.
          for f in "${corpus[@]}"; do
            case_count=$(jq -r 'if has("caseCount") then .caseCount else "absent" end' "$f")
            evaluated=$(jq  -r 'if has("evaluated") then .evaluated else "absent" end' "$f")
            records=$(jq    -r 'if (.records|type) == "array" then (.records|length) else "absent" end' "$f")

            if [ "$case_count" = absent ] || [ "$evaluated" = absent ] || [ "$records" = absent ]; then
              echo "::error title=Corpus envelope malformed::$f has no caseCount/evaluated/records envelope (read: caseCount=$case_count evaluated=$evaluated records=$records). Either the artifact is truncated, or this leg emitted the single-vector transcript shape under the corpus artifact name and its emit step was not switched to --vectors=corpus/parity-vectors."
              rc=1
              continue
            fi
            if [ "$case_count" -eq 0 ]; then
              echo "::error title=Corpus empty::$f reports caseCount=0. Zero cases is a failure, not a vacuous pass."
              rc=1
              continue
            fi
            if [ "$evaluated" -ne "$case_count" ] || [ "$records" -ne "$case_count" ]; then
              echo "::error title=Corpus coverage shortfall::$f reports evaluated=$evaluated records=$records against caseCount=$case_count. The emitter is required to refuse to write this file (ParityTranscript.swift:116 / ParityTranscriptCommand.cs:188); an envelope that reaches this gate in this state means that refusal was removed, bypassed, or the artifact is damaged."
              rc=1
            fi
          done
```

`jq -r 'if has("caseCount") ...'` rather than `.caseCount // "absent"`: jq's `//` treats `false`
as absent but treats `0` as present, and mixing the two conventions in one gate is how a `0`
becomes an `"absent"` message that sends the reader to the wrong file.

---

## 3. Assertion A2a — the stray sweep, taken without the loaders' filter

**Where:** the **`parity`** job (`ci.yml:453-475`), as its own step — *not* the `parity-transcript`
insertion point that A1 uses. Settled with `ci-wiring-planner` on 2026-08-06 (§8); an earlier
revision of this document put it beside the count check in `parity-transcript`.

The reason is decoupling, and it is worth stating precisely because the obvious reason is wrong.
The obvious argument is "`parity` takes no `needs:` (`ci.yml:445-448`) so its answer is never
withheld when a build leg fails" — but `ci-wiring-planner` is proposing `if: always()` on
`parity-transcript`, which would keep the sweep reporting there too. So the honest argument is
narrower: staying put would make this assertion's reporting **conditional on their open question 2
being approved**. Coupling a corpus-integrity check to an unresolved question in another proposal
is the weaker arrangement even when the likely resolution is favourable. Moving it removes the
dependency entirely.

Three secondary benefits: the sweep needs nothing but the repository — no build, no artifacts, no
downloads — so it starts immediately rather than after the four-leg matrix; `parity` is
`ubuntu-latest` with the bash default, so no `shell:` key is needed; and it leaves
`ci-wiring-planner`'s script owning everything artifact-dependent, with A1's fragment as the only
surface the two proposals share.

**What it asserts:** every entry in `corpus/parity-vectors/` is a regular file matching
`[0-9][0-9][0-9]_*.json`. This is the assertion that closes S2. It does not ask the loaders
anything; it looks at the directory the way the loaders refuse to.

**Deliberately *not* asserted here: the count itself.** An earlier draft of this section also
compared `caseCount` against the on-disk file count. That check now belongs to `ci-wiring-planner`,
whose script computes `$on_disk` using the loader's own `NNN_<vectorId>.json` rule
(`ParityVectorLoader.cs:235-250`) and compares it to each envelope. Shipping mine as well would be
two spellings of one question. The division agreed on 2026-08-06 (§8):

- **Their count asks:** did the four legs read *this checkout's* corpus? (Catches an emitter
  pointed at the wrong directory — a typo in `--vectors=`, a stale working copy.)
- **My sweep asks:** does the directory hold anything the loaders pass over? (Catches S2 — the
  renamed case that both engines and every `*.json`-shaped count agree is simply not there.)

Neither subsumes the other, and the two messages name different questions so a red build says
which one failed.

**The corpus directory is clean today**, which is what makes A2a a one-liner rather than an
allowlist: `corpus/parity-vectors/` holds exactly the 11 case files, no `README.md`, no dotfiles,
and `git ls-files corpus/parity-vectors/` returns the same 11. Compare `warp-manifest-vectors/`,
which does carry a `README.md` (cited at `ci.yml:409`) — if the parity corpus ever grows one, A2a
must gain an explicit allowlist entry for it, and that edit is the point: a human decides what is
allowed to sit next to the cases.

**Extract it to `scripts/`, do not inline it.** The house pattern already exists — `parity` invokes
`python3 scripts/cross-platform-parity-gate.py --github` (`ci.yml:468`) and
`python3 scripts/package-pin-parity.py --github` (`ci.yml:475`), and `build-test` invokes
`./scripts/honesty-coverage-gate.py` (`ci.yml:422`). Three reasons, the third of which is decisive
this month:

1. It matches the three gates already sitting in the job this step joins.
2. Logic in a file can be unit-tested and read; logic in a YAML string cannot.
3. **Inline bash in a `run:` block can only ever execute by pushing** — and per §6.0 pushing
   currently executes nothing. The same logic as `scripts/parity-corpus-stray-sweep.py --github`
   runs on a laptop today, against a real corpus and against a sabotaged copy, which is what makes
   this assertion's red-proof available now instead of at the end of the month.

So the recommendation is a script invoked as a one-liner. The shell below is the specification for
what that script does, not the step body:

```yaml
      # --- Corpus stray sweep. Looks at the corpus directory WITHOUT the loaders' filter.
      #
      # Both loaders skip a directory entry on its extension alone, silently, with no counter and
      # no message: ParityVectorLoader.swift:195 (`entry.pathExtension == "json"`) and
      # ParityVectorLoader.cs:197 (`name.EndsWith(".json")`). Those are the ONLY two silent skips
      # in either loader — everything else throws and names the file. So a case renamed
      # 005_glutton-ecs-forwarded.json -> .json.orig by a merge, a .rej, an editor backup, or a
      # macOS `sed -i ''` becomes zero cases on BOTH engines, caseCount drops to 10 on all four
      # legs, the emitters' evaluated==caseCount guard passes, and the transcript diff is clean.
      #
      # Deliberately NOT `ls corpus/parity-vectors/*.json | wc -l`, and deliberately not the
      # caseCount-vs-on-disk comparison in the parity-transcript job either: both of those use the
      # loaders' own filename rule, so both are blind to the exact rename this exists to catch.
      # That comparison answers a different question (did the legs read THIS checkout's corpus?)
      # and is load-bearing on its own — do not remove it as a duplicate of this.
      #
      # Lives here rather than in parity-transcript so it depends on nothing but the checkout.
      - name: Parity corpus — stray sweep
        run: |
          set -uo pipefail
          corpus_dir=corpus/parity-vectors

          if [ ! -d "$corpus_dir" ]; then
            echo "::error title=Corpus missing::$corpus_dir does not exist in this checkout. Refusing to report coverage of a corpus that is not there."
            exit 1
          fi

          strays=$(find "$corpus_dir" -mindepth 1 -maxdepth 1 \
                     \( ! -type f -o ! -name '[0-9][0-9][0-9]_*.json' \) -print)
          if [ -n "$strays" ]; then
            echo "::error title=Corpus holds entries that are not cases::$corpus_dir must hold PARITY_VECTOR_V1 case files (NNN_<vectorId>.json) and nothing else. Both loaders skip anything else SILENTLY on its extension (ParityVectorLoader.swift:195, ParityVectorLoader.cs:197), so an entry listed below is either a case that has stopped counting on both engines or clutter that makes every count untrustworthy:"
            echo "$strays"
            exit 1
          fi

          echo "corpus stray sweep clean: every entry in $corpus_dir is a case file"
```

As an extracted script the step collapses to the house one-liner, and the `::error` emission moves
behind `--github` exactly as `cross-platform-parity-gate.py` does it:

```yaml
      - name: Parity corpus — stray sweep
        run: python3 scripts/parity-corpus-stray-sweep.py --github
```

Its exit codes should follow the `parity` job's existing convention (`ci.yml:450-452`): `0` only
after having actually examined a directory, `1` on strays found, `2` on cannot-run (corpus absent
or unreadable). Never `0` without having looked at something.

### 3.1 What A2a catches that A1 structurally cannot

A1 compares two numbers that both descend from `ParityVectorLoader.load`. Anything the loader
never enumerated is missing from both, so A1 is blind to **every** skip path by construction. A2a
is the first assertion in the chain that does not ask the loader. Specifically:

- a case file renamed out of the `.json` extension (S2, `swift:195` / `cs:197`) — the live one
- a case file whose extension changed case (`.JSON`; `pathExtension` is `"JSON" != "json"`,
  `EndsWith(".json", Ordinal)` is false — both skip)
- a build artifact, backup, or merge leftover deposited in the corpus directory, which makes any
  future count argument unsound even when it costs no case today

An emitter invoked against the *wrong directory* is also invisible to A1, but that one is caught by
`ci-wiring-planner`'s `$on_disk` comparison rather than by this sweep — see the division in §3
above.

And what it still does not catch: **deletion.** That is §4.

---

## 4. `--list-only`, the committed manifest, and the Task 6.5 drift gate

These are one mechanism, so they are one section.

### 4.1 Position on the cross-platform `--list-only` byte diff

**Diffing the two CLIs' `--list-only` stdout against each other is redundant once the corpus
envelope diff lands, and I recommend not wiring it.**

The reasoning: `--list-only` prints `caseCount=<n>` then the ids in case order
(`ParityTranscript.swift:103-107`, `ParityTranscriptCommand.cs:154-164`). Every one of those facts
is already in the envelope — `caseCount` at `swift:123`/`cs:200`, and `records[].vectorId` in case
order because the loop at `swift:111-114` / `cs:168-186` preserves the loader's order. The existing
diff at `ci.yml:493-520` compares the full envelope across all four legs with `jq --sort-keys`, so
cross-platform agreement on case count, case identity, and case *order* is already asserted there.
A `--list-only` peer diff is a strictly weaker projection of a comparison the gate already makes,
and it costs artifact plumbing (two more uploads, or a job that builds both CLIs) to add nothing.

The one property it uniquely carries is the deliberate bare `\n`
(`ParityTranscriptCommand.cs:156-161`, with the reason at `:136-139`) — and note that a
`git diff`- or `diff -u`-based check will **not** prove it. There is no `.gitattributes` in this
repository, GitHub-hosted Windows runners set `core.autocrlf=true`, and both `git diff` and
`diff -u` normalise or ignore the difference. Asserting the line-ending property needs `cmp`
against an LF-pinned fixture, and it is a CLI-surface assertion belonging to Phase 3 / Task 6.3,
not a corpus-coverage one. Recommend it be handed to whoever owns the Phase 3 CLI gate.

**What `--list-only` is genuinely good for is being compared against a committed expectation
instead of against its twin.** That is the rest of this section.

### 4.2 The manifest

Add one version-controlled file holding exactly the bytes `--list-only` prints:

```
corpus/parity-vectors.manifest
```

```
caseCount=11
empty-section-inputs
prophet-dnssec-unsigned
mirror-authoritative-matches
portal-encrypted-transport-working
glutton-ecs-forwarded
cage-attribution-and-size
witness-config-changes
prophet-resolution-success
unfetchable-resolution-timeout
unverifiable-dnssec-inconclusive
degenerate-present-but-uninformative
```

**It must live beside the corpus directory, not inside it** — a `corpus/parity-vectors/MANIFEST`
would be the first thing A2a's stray sweep rejects. This interaction between the two proposals is
the reason for the path.

**Why a manifest is the only thing that catches a deletion.** Delete
`011_degenerate-present-but-uninformative.json`: A1 passes (10/10/10), A2 passes (10 files, no
strays), the four-way envelope diff passes (all four legs say 10), and coverage has silently
shrunk by a case. Every assertion so far derives its expectation from the corpus, and the corpus is
what changed. The manifest is the only expectation a deletion does not get to edit for free — it
has to be edited deliberately, in the same commit, where a reviewer sees a line disappear.

Because the manifest is the `--list-only` bytes verbatim, it also pins case **order** and case
**identity**, not just the count — so a case renamed `011_x.json` → `011_y.json`, or two cases
swapping sort keys, is caught too.

### 4.3 The Task 6.5 drift gate

Task 6.5 asks for "a corpus regeneration drift gate for any new corpus, modelled on the existing
`warp-manifest-vectors` one". The model is `ci.yml:412-415`:

```yaml
      - name: Warp manifest corpus — regeneration drift gate
        run: |
          Server/venv/bin/python warp-manifest-vectors/_build_vectors.py
          git diff --exit-code -- warp-manifest-vectors/
```

Its mechanism is *idempotent regeneration*: run the builder, and any diff is drift.
**`corpus/parity-vectors/` has no builder.** There is no `_build_vectors.py`, no `generate.py`, no
`README.md` — the 11 cases are hand-authored, and `find corpus -maxdepth 2` turns up generators
only under `corpus/appcast-crossplatform/`. Copying the warp gate verbatim would mean writing a
generator for a hand-authored corpus purely to have something to re-run, which inverts the
relationship: the cases would become outputs of a script nobody reads.

**The adaptation:** the corpus has exactly one derivable artifact — the manifest — and the CLI is
its generator. Same mechanism, one regenerable file:

```yaml
      # --- Task 6.5: corpus regeneration drift gate, modelled on the warp-manifest-vectors gate
      # at ci.yml:412-415. That gate re-runs an idempotent BUILDER and treats any diff as drift.
      # corpus/parity-vectors/ is hand-authored and has no builder, so the regenerable artifact is
      # its manifest and the generator is the CLI's own --list-only. Any diff means one of:
      # a case was added, removed, renamed or reordered without the manifest being regenerated, or
      # the manifest was hand-edited to describe a corpus that does not exist.
      - name: Parity corpus — manifest regeneration drift gate
        if: matrix.kind == 'dotnet' && runner.os == 'Linux'
        shell: bash
        run: |
          dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release -- \
            parity-transcript --vectors=corpus/parity-vectors --list-only \
            > corpus/parity-vectors.manifest
          git diff --exit-code -- corpus/parity-vectors.manifest
```

`shell: bash` is **required**, not stylistic. `grep -n "shell:" .github/workflows/ci.yml` returns
nothing today and there is no `defaults:` block, so every `run:` uses the runner default — `pwsh`
(PowerShell 7) on `windows-latest`. The step above is pinned to the Linux .NET leg so it would work
either way, but a future copy onto the Windows leg would break on `\` line continuations, which
pwsh does not honour (its continuation character is a backtick). The step's redirect is safe under
pwsh, which writes UTF-8 without a BOM; **UTF-16LE is a Windows PowerShell 5.1 behaviour**, so it
is a reason never to reach for `shell: powershell`, not a hazard of the current default.
*(Corrected 2026-08-06 — an earlier revision of this document attributed the UTF-16LE redirect to
pwsh. `ci-wiring-planner` caught it; the mechanism is real but belongs to 5.1.)* The same caution
applies to A1 and A2 if they are ever moved out of the ubuntu-only `parity-transcript` job.

One leg is enough for this gate: it asserts manifest-vs-CLI, and §5 asserts corpus-vs-manifest on
every leg through the test suites.

---

## 5. Per-case test parameterization — yes, and the Swift side is the gap

**Recommendation: yes, add a corpus-parameterized test to both suites.** The argument is not
symmetry, it is that today the canonical corpus is exercised by exactly one automated thing in the
entire repository:

```
$ grep -rn "parity-vectors" --include=*.swift --include=*.cs --include=*.yml .
Windows/dotNET/Syxx.Tests/ParityVectorLoaderTests.cs:447    (CanonicalCorpusLoadsWhole)
```

Nothing else. No CI step reads it (`ci.yml:251-258` is still single-vector), and no Swift test
reads it — `macOS/Tests/SyxxKitTests/ParityVectorLoaderTests.swift:16` says so outright: *"Fixtures
are written to a fresh temp directory per test. Nothing here reads or writes `corpus/`."* All 23
Swift loader tests are synthetic-fixture tests. So on macOS, the 11 cases the whole track is built
around are read by no test at any point; the first thing that will ever read them there is the CLI
inside a CI job.

### 5.1 What the existing loader tests already cover

Both suites are thorough on **rejection** and prove the message names the case, per the discipline
at `ParityVectorLoaderTests.swift:7-14`. Swift covers unrecognised `schemaVersion` (`:144`),
`vectorId` mismatch (`:156`), Swift-only fields refused (`:179`, `:189`), unknown `sectionInputs`
key (`:199`), unknown nested member (`:215` — the F-27 pin), unknown top-level key (`:238`),
invalid JSON (`:250`), the three `pinnedUnixSeconds` refusals (`:259`, `:269`, `:279`), empty
directory (`:292`), directory of only non-case files (`:298`), unreadable directory (`:304`),
duplicate sort prefix (`:315`), misnamed `.json` (`:339`), undecodable known field (`:353`), and
the hand-decoded field round-trips (`:367`, `:401`). C# mirrors these and adds the
`UnrepresentedMembers` pin and the platform-only refusals.

The gap is not rejection. It is that **no Swift test asserts anything about the real corpus**, and
that neither suite asserts the corpus against an expectation that survives the corpus changing.
Note also `testDirectoryOfOnlyNonCaseFilesIsStillEmpty`
(`ParityVectorLoaderTests.swift:298-302`) writes a `README.md` and asserts the loader reports "no
case files" — that test documents S2 working as designed for a *whole* directory, and is precisely
why S2 is silent for a *single* file among ten valid ones.

C#'s `CanonicalCorpusLoadsWhole` (`ParityVectorLoaderTests.cs:445-473`) is the right shape already
— it loads the real corpus, asserts `cases.Count` against `Directory.GetFiles(corpus, "*.json")`,
asserts the file list in ordinal order, and counts cases that reach `Synthesizer.Conclude`. Its one
weakness is §1.2: `GetFiles(corpus, "*.json")` at `:451` and `:459` is the loaders' own predicate,
so it inherits the S2 blind spot exactly.

### 5.2 The house pattern being followed

Task 5.2's Warp counter (`macOS/Tests/SyxxKitTests/WarpEngineTests.swift:175, 207, 210-213`):
count cases that actually reached the engine, print the count, assert non-zero, assert equal to the
discovered count. `MatrixGoldenCorpusTests.swift:142, 162, 166-167` and its C# twin
`MatrixGoldenCorpusTests.cs:139, 156, 160` are the same shape. Both sketches below are that
pattern plus the two expectations the Warp suite does not need: the stray sweep, and the manifest.

### 5.3 Swift sketch — `macOS/Tests/SyxxKitTests/ParityCorpusCoverageTests.swift` (new file)

New file rather than an addition to `ParityVectorLoaderTests.swift`, so the "nothing here reads
`corpus/`" invariant at `:16` stays true of that file.

```swift
import XCTest
@testable import SyxxKit

/// Coverage integrity for the shared corpus at `corpus/parity-vectors/` (track
/// cross-platform-parity_20260805, Task 6.5). Twin of
/// Windows/dotNET/Syxx.Tests/ParityCorpusCoverageTests.cs.
///
/// This is the only Swift test that reads the real corpus. ParityVectorLoaderTests is
/// synthetic-fixture-only by design (see its header), which left the 11 canonical cases read by
/// nothing on this platform: the C# suite has CanonicalCorpusLoadsWhole, macOS had no counterpart,
/// so a case that stopped loading on Swift would first be noticed by a CI job.
///
/// Three expectations, because each catches something the others cannot:
///  1. Every entry in the corpus directory IS a case file. Both loaders skip on extension alone
///     and silently (ParityVectorLoader.swift:195, ParityVectorLoader.cs:197), so a case renamed
///     `.json.orig` costs a case with no message and no count change anywhere downstream. A glob
///     for `*.json` would share that blind spot, so the sweep looks at every entry.
///  2. The case ids equal the committed manifest, in order. A DELETED case is consistent with
///     every count derived from the corpus; only an expectation stored outside the corpus can see
///     it.
///  3. Every case reaches Synthesizer.conclude, counted (Task 5.2 pattern, WarpEngineTests.swift:210).
final class ParityCorpusCoverageTests: XCTestCase {

    private static func repoRoot() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
    }

    private static func corpusDir() -> URL {
        repoRoot().appendingPathComponent("corpus").appendingPathComponent("parity-vectors")
    }

    private static func manifestURL() -> URL {
        repoRoot().appendingPathComponent("corpus").appendingPathComponent("parity-vectors.manifest")
    }

    /// Expectation 1 — no entry in the corpus directory is anything but a case file.
    func testCorpusDirectoryHoldsOnlyCaseFiles() throws {
        let dir = Self.corpusDir()
        let entries = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        XCTAssertFalse(entries.isEmpty, "\(dir.path) is empty — refusing to pass vacuously")

        let strays = entries.filter { ParityVectorLoader.parseCaseFilename($0) == nil }.sorted()
        XCTAssertTrue(strays.isEmpty,
                      """
                      \(dir.path) holds entries that are not PARITY_VECTOR_V1 cases: \
                      \(strays.joined(separator: ", ")). Both loaders skip a non-.json entry \
                      silently (ParityVectorLoader.swift:195, ParityVectorLoader.cs:197), so a \
                      case renamed out of its extension stops counting on both engines with no \
                      message — caseCount, evaluated and the cross-platform diff all agree about \
                      the smaller corpus.
                      """)
    }

    /// Expectation 2 — the corpus is exactly what the committed manifest says it is.
    func testCorpusMatchesTheCommittedManifest() throws {
        let text = try String(contentsOf: Self.manifestURL(), encoding: .utf8)
        var lines = text.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
        if lines.last == "" { lines.removeLast() }
        guard let header = lines.first, header.hasPrefix("caseCount=") ,
              let declared = Int(header.dropFirst("caseCount=".count)) else {
            return XCTFail("manifest does not start with caseCount=<n>: \(lines.first ?? "<empty>")")
        }
        let expectedIds = Array(lines.dropFirst())
        XCTAssertEqual(declared, expectedIds.count, "manifest header disagrees with its own id list")

        let cases = try ParityVectorLoader.load(directory: Self.corpusDir())
        XCTAssertEqual(cases.map(\.vectorId), expectedIds,
                       """
                       the corpus and corpus/parity-vectors.manifest disagree. A case added, \
                       removed, renamed or reordered without regenerating the manifest is a \
                       coverage change nothing else can see: a deletion leaves caseCount, \
                       evaluated and all four transcripts in perfect agreement about the smaller \
                       corpus. Regenerate with: swift run --package-path macOS syxx \
                       parity-transcript --vectors=corpus/parity-vectors --list-only \
                       > corpus/parity-vectors.manifest
                       """)
    }

    /// Expectation 3 — every case reaches the engine, counted rather than assumed.
    func testEveryCaseReachesTheEngine() throws {
        let cases = try ParityVectorLoader.load(directory: Self.corpusDir())
        XCTAssertFalse(cases.isEmpty, "empty corpus — an empty run must not read as coverage")

        var evaluated = 0
        for c in cases {
            let conclusion = Synthesizer.conclude(from: c.inputs, now: c.pinnedNow)
            XCTAssertEqual(conclusion.sectionStatuses.count, 6, "[\(c.caseFile)] section count")
            evaluated += 1
        }
        print("[parity corpus] evaluated \(evaluated) of \(cases.count) cases in \(Self.corpusDir().path)")
        XCTAssertGreaterThan(evaluated, 0, "no case was evaluated — an empty run must not read as coverage")
        XCTAssertEqual(evaluated, cases.count,
                       "every discovered case must reach the engine; \(evaluated) of \(cases.count) ran")
    }
}
```

**One source change this needs:** `ParityVectorLoader.parseCaseFilename` is `static` with default
(internal) access at `ParityVectorLoader.swift:223`, so `@testable import SyxxKit` reaches it — no
change required. Reusing it rather than re-implementing the pattern is deliberate: a stray sweep
with its own copy of the filename rule can drift from the loader's, and then the test and the
loader disagree about what a case is.

### 5.4 C# sketch — `Windows/dotNET/Syxx.Tests/ParityCorpusCoverageTests.cs` (new file)

C# already has `CanonicalCorpusLoadsWhole`. Rather than duplicate it, this adds the two
expectations it lacks and **fixes its S2 blind spot**; `CanonicalCorpusLoadsWhole` itself can stay
as-is (it is the engine-reachability half) or have its `GetFiles(..., "*.json")` calls at `:451`
and `:459` swapped for the sweep below.

```csharp
using System.Runtime.CompilerServices;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Coverage integrity for corpus/parity-vectors (track cross-platform-parity_20260805, Task 6.5).
/// Twin of macOS/Tests/SyxxKitTests/ParityCorpusCoverageTests.swift.
/// </summary>
/// <remarks>
/// <para>Complements <see cref="ParityVectorLoaderTests.CanonicalCorpusLoadsWhole"/>, which counts
/// the corpus with <c>Directory.GetFiles(corpus, "*.json")</c> — the SAME predicate both loaders
/// skip on (ParityVectorLoader.cs:197, ParityVectorLoader.swift:195). A case renamed
/// <c>005_x.json.orig</c> is invisible to that glob for exactly the reason it is invisible to the
/// loaders, so the assertion agrees with the loader about a corpus that shrank.</para>
/// </remarks>
public sealed class ParityCorpusCoverageTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    private static string CorpusDir() => Path.Combine(RepoRoot(), "corpus", "parity-vectors");
    private static string ManifestPath() => Path.Combine(RepoRoot(), "corpus", "parity-vectors.manifest");

    /// <summary>Every entry in the corpus directory must BE a case file.</summary>
    [Fact]
    public void CorpusDirectoryHoldsOnlyCaseFiles()
    {
        var dir = CorpusDir();
        var entries = Directory.GetFileSystemEntries(dir);
        Assert.NotEmpty(entries);

        var strays = entries
            .Where(e => !File.Exists(e)
                        || !ParityVectorLoader.TryParseCaseFilename(Path.GetFileName(e), out _, out _))
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"{dir} holds entries that are not PARITY_VECTOR_V1 cases: {string.Join(", ", strays)}. " +
            "Both loaders skip a non-.json entry silently (ParityVectorLoader.cs:197, " +
            "ParityVectorLoader.swift:195), so a case renamed out of its extension stops counting " +
            "on both engines with no message.");
    }

    /// <summary>The corpus must be exactly what the committed manifest says it is.</summary>
    [Fact]
    public void CorpusMatchesTheCommittedManifest()
    {
        var lines = File.ReadAllLines(ManifestPath());
        Assert.StartsWith("caseCount=", lines[0], StringComparison.Ordinal);
        var declared = int.Parse(lines[0]["caseCount=".Length..], CultureInfo.InvariantCulture);
        var expectedIds = lines.Skip(1).Where(l => l.Length > 0).ToList();
        Assert.Equal(declared, expectedIds.Count);

        var cases = ParityVectorLoader.Load(CorpusDir());
        Assert.Equal(expectedIds, cases.Select(c => c.VectorId));
    }

    /// <summary>Every case must reach the engine, counted rather than assumed.</summary>
    [Fact]
    public void EveryCaseReachesTheEngine()
    {
        var cases = ParityVectorLoader.Load(CorpusDir());
        Assert.NotEmpty(cases);

        var evaluated = 0;
        foreach (var c in cases)
        {
            var conclusion = Synthesizer.Conclude(c.Inputs, now: c.PinnedNow);
            Assert.Equal(6, conclusion.SectionStatuses.Count);
            evaluated++;
        }
        Assert.Equal(cases.Count, evaluated);
    }
}
```

`TryParseCaseFilename` is `internal static` at `ParityVectorLoader.cs:235`, and
`Windows/dotNET/Syxx.Kit/Syxx.Kit.csproj:19` already carries
`<InternalsVisibleTo Include="Syxx.Tests" />`, so the test reaches it with no source change.
Reusing it rather than re-implementing the filename rule matters for the same reason as on the
Swift side: a stray sweep carrying its own copy of the rule can drift from the loader's, and then
the test and the loader disagree about what a case is.

### 5.5 Why this belongs in the test suites and not only in CI

Both suites already run on every leg — `swift test` at `ci.yml:167-169`, `dotnet test` at
`ci.yml:140-142` — so this costs no new CI step, no artifact plumbing, and no `shell: bash`
exposure. More to the point, it makes the corpus's integrity a property that fails on a developer's
machine before it fails in CI, which the CLI-only path cannot do: `parity-transcript` is a hidden
subcommand (`ParityTranscript.swift:5`, `ParityTranscriptCommand.cs:11`) that nobody runs by hand.

---

## 6. Red-proof protocol

`plan.md:221` requires that a deliberate divergence turns the build red **and that the failure
names the diverging case**. Each assertion below has a specific sabotage, an expected message, and
— where it matters — a control run proving the hole is real without it. Do these on a scratch
branch; none of them is a commit to `main`.

### 6.0 Two proofs run later; everything else runs now

Hosted CI is off until roughly 2026-08-27 — a deliberate spending decision by the owner
(2026-08-06), not a fault to diagnose. **Nothing here is blocked on it.** Design, implementation,
and all but two of the proofs below proceed locally; this subsection exists only so the two
exceptions are recorded rather than quietly ticked.

**Runs now, locally — which is nearly all of it:** A1's hand-edited envelope, the A2a rename
against the sweep, the manifest and drift-gate proofs in A3, and A4's truncation under
`swift test` / `dotnet test`. Do these as the work lands; none needs a runner.

**Runs later, when runners return — exactly two:** the A2a **control** (a green build with a case
renamed away) and the four-leg agreement run. Both assert something about *a green build*, and a
green build is the one thing a laptop cannot produce. Running the checks locally proves the checks
work; it cannot prove that without them the corpus silently shrinks while everything else still
passes. Those are different claims, and only the second is what the control is for.
`ci-wiring-planner` reached this framing independently and put it better than my first draft.

Annotate those two `blocked-on-CI` with the reason: never ticked, never omitted, and never softened
to "verified locally" when what ran locally was a different thing. That is bookkeeping on two
lines, not a headline and not a reason to pause — a gate recorded green on a proof that never ran
is the defect this track exists to refuse, and this section is where it would enter.

**This is the strongest argument for §3's script extraction.** Inline bash in a `run:` block can
only execute by pushing, and pushing currently executes nothing. The same logic in `scripts/` runs
against hand-placed fixtures today. That is why extraction moved from a stylistic preference to the
recommendation.

### A1 — envelope self-consistency

Not reachable by touching the corpus: the emitters refuse to write a shortfall envelope, so no CI
run can produce the input A1 rejects. Two-part proof:

1. **Script-level (do this one — it is runnable today, per §6.0).** Copy a real envelope, hand-edit
   it, run the fragment's body against it with `corpus=(...)` bound by hand:
   ```bash
   mkdir -p /tmp/rp/corpus-transcripts
   swift run --package-path macOS syxx parity-transcript \
     --vectors=corpus/parity-vectors --out=/tmp/rp/corpus-transcripts/corpus.macos-arm64.json
   jq '.evaluated = 10 | .records = (.records[0:10])' \
     /tmp/rp/corpus-transcripts/corpus.macos-arm64.json \
     > /tmp/rp/corpus-transcripts/corpus.sabotage.json
   ```
   Expected: `::error title=Corpus coverage shortfall::/tmp/rp/corpus-transcripts/corpus.sabotage.json
   reports evaluated=10 records=10 against caseCount=11`, and `rc` left at 1.
2. **Emitter-level (record as not performed unless the owner wants it).** Deleting the guard at
   `ParityTranscript.swift:116` and adding a `continue` to the loop is a two-edit sabotage of
   shipping source. That it takes two edits *is* the evidence for A1's framing in §2: it is
   defense in depth against a future edit, not a live gate.

Also worth running once: truncate a transcript to 100 bytes and confirm the `absent` branch fires
rather than jq exploding into an unreadable stack.

### A2a — stray sweep (the load-bearing one)

```bash
git checkout -b scratch/redproof-corpus-rename
git mv corpus/parity-vectors/005_glutton-ecs-forwarded.json \
       corpus/parity-vectors/005_glutton-ecs-forwarded.json.orig
git commit -m "red-proof: rename one case out of its extension"
```

**Control run first, with the A2a fragment removed or commented out.** Expected: fully green. All
four legs emit `caseCount=10, evaluated=10`, A1 passes, `ci-wiring-planner`'s `$on_disk` comparison
also passes (10 files match the loader rule, because the renamed one no longer does), the four-way
diff at `ci.yml:493-520` passes, and the ECS case is gone. Capture that green run — it is the
direct evidence that the hole in §1 is real and not hypothetical, and it is the strongest artifact
this whole proposal can produce.

**This control run is the one blocked by §6.0.** It needs CI to actually execute steps. The
local substitute — running both fragments' bodies against locally-emitted envelopes — proves the
fragments work but *cannot* demonstrate the green-build-with-a-missing-case, because locally there
is no build to be green. Record it as `blocked-on-CI` until runners recover.

**Then with A2 in place.** Expected:
```
::error title=Corpus holds entries that are not cases::corpus/parity-vectors must hold
PARITY_VECTOR_V1 case files (NNN_<vectorId>.json) and nothing else. [...]
corpus/parity-vectors/005_glutton-ecs-forwarded.json.orig
```
exit 1, and the offending file is named — satisfying `plan.md:221`.

Also run the Swift and C# `CorpusDirectoryHoldsOnlyCaseFiles` tests on this branch: both must fail
naming `005_glutton-ecs-forwarded.json.orig`.

### A2b — count agreement (now `ci-wiring-planner`'s check; red-proof retained here for reference)

This assertion moved to their script per §3. Their red-proof, not mine — recorded here only because
the sabotage is non-obvious. It cannot be reached by editing the corpus (A2a fires first on
anything that changes the entry set, and a legitimate add/remove moves both numbers together).
Sabotage the *emitter's* input instead: run one leg with `--vectors=` pointed at a copy holding 10
of the 11 cases, and feed its envelope to the step. Expected:
`::error title=Corpus coverage mismatch::... reports caseCount=10 but
corpus/parity-vectors holds 11 case files`.

### A3 — manifest / drift gate

```bash
git checkout -b scratch/redproof-corpus-deletion
git rm corpus/parity-vectors/011_degenerate-present-but-uninformative.json
git commit -m "red-proof: delete one case"
```

Expected, and this is the point of the manifest:

- A1 passes — 10/10/10 on all four legs.
- A2a passes — 10 files, no strays.
- A2b passes — 10 on disk, `caseCount=10`.
- The four-way envelope diff passes — all four legs agree.
- **`CorpusMatchesTheCommittedManifest` fails on both platforms**, naming
  `degenerate-present-but-uninformative` as the missing id.
- **The drift gate fails**, because regenerating the manifest produces a file one line shorter and
  `git diff --exit-code` is non-zero.

Second sabotage for the drift gate specifically — hand-edit the manifest to add a case that does
not exist:
```bash
printf 'phantom-case\n' >> corpus/parity-vectors.manifest
sed -i'' -e 's/^caseCount=11$/caseCount=12/' corpus/parity-vectors.manifest
```
Expected: the manifest tests fail (`cases.map(\.vectorId)` has 11 entries, the manifest declares
12), and the drift gate's `git diff --exit-code` fails after regeneration removes both edits. This
is the check that the manifest cannot be quietly widened to make a red test green.

### A4 — the per-case tests' own counter

Follow the Task 5.2 protocol exactly (`plan.md:196-201`): make one case unreadable in place and
confirm the suite goes red naming it, rather than passing with a smaller count.
```bash
: > corpus/parity-vectors/008_prophet-resolution-success.json     # truncate to zero bytes
```
Expected: `ParityVectorLoader.load` throws `invalidJSON` naming
`008_prophet-resolution-success.json`, and both `testEveryCaseReachesTheEngine` /
`EveryCaseReachesTheEngine` fail with that message rather than reporting `evaluated 10 of 10`.
Restore with `git checkout -- corpus/parity-vectors/008_prophet-resolution-success.json` and
confirm the file is byte-identical afterwards.

### Recording

Each proof above should land in `plan.md` Phase 6's verification block in the house format used by
Task 5.2 and Task 2.5 — `(verified-by-real-run: <what was perturbed> → <what the gate printed>;
<how the corpus was restored>)`. The A2a **control** run deserves its own line: a recorded green
build on a branch with a case renamed away is the clearest possible statement of what the gate is
for.

---

## 7. Open questions for the owner

1. **Manifest path and name.** I propose `corpus/parity-vectors.manifest` — beside the directory,
   not in it, because A2a's stray sweep rejects anything inside that is not a case file. If you
   would rather keep the corpus self-describing, the alternative is
   `corpus/parity-vectors/README.md`-style in-directory placement plus an explicit allowlist entry
   in A2a. I recommend against it: the allowlist is the thing a future stray hides behind.

2. **Should A1/A2 fail the build, or annotate?** Both are written as `exit 1`. The
   `parity-transcript` job is already blocking (`ci.yml:477-478`), so this is consistent — but it
   means a stray `.DS_Store` committed by accident turns the build red. That is the intended
   behaviour and I recommend keeping it; flagging in case you want A2a to warn for one release
   cycle first.

3. ~~**Does the `parity` job want the stray sweep?**~~ **Resolved 2026-08-06** — both planners
   agree it moves to `parity`, and §3 now specifies it there. Recorded rather than deleted because
   the reasoning changed twice: first because dropping the count check removed the only reason to
   keep it beside `parity-transcript`, then because `ci-wiring-planner` pointed out that the
   obvious justification ("`parity` takes no `needs:`") is not the operative one — their proposed
   `if: always()` would keep it reporting either way. The real argument is decoupling: staying put
   would make this assertion's reporting conditional on an unresolved question in another
   proposal. Still the owner's to overrule, but it is no longer an open disagreement between the
   two documents.

4. **Ordering against the CI-wiring proposal.** A1 and A2a are inert until the emit steps switch to
   `--vectors=` (`ci.yml:251-258`); until then A1's `absent` branch fires on all four legs, which
   is correct behaviour but reads as a regression. They must land in the same PR as
   `docs/PROPOSAL-parity-transcript-corpus-ci-2026-08-05.md`'s change, or immediately after it —
   agreed with `ci-wiring-planner` and recorded in both documents. §5's tests and §4.3's drift gate
   have no such dependency and can land first.

5. **How should the two deferred proofs be recorded — a `blocked-on-CI` annotation, or a full open
   checkbox?** Not a question about whether to proceed: everything in this document is
   implementable and all but two proofs are demonstrable now (§6.0). It is narrower — when the
   other proofs pass locally and Task 6.5 is otherwise done, does the box get ticked with two
   annotated lines beside it, or stay open? I lean ticked-with-annotations, because leaving it open
   would misreport finished work as unstarted; what must not happen is the two lines quietly
   disappearing. Bookkeeping preference, yours to set.

---

## 8. Reconciliation with the CI-wiring proposal (2026-08-06)

Recorded because both documents were written in parallel and a reader of either alone would
otherwise ship a duplicated or mis-scoped gate. Agreed with `ci-wiring-planner`:

| Question | Owner | Job | Where |
|---|---|---|---|
| Does each corpus envelope agree with itself? | this doc (A1) | `parity-transcript` | §2 |
| Is a leg emitting a non-corpus shape? | this doc (A1's `absent` branch) | `parity-transcript` | §2 — supersedes their `Not a corpus transcript` **echo**; their `continue` stays, because it is control flow and losing it would compare a string against a count |
| Did the legs read *this checkout's* corpus? | `ci-wiring-planner` (`$on_disk`) | `parity-transcript` | their doc; removed from mine, and **load-bearing — not a duplicate of my sweep** |
| Does the directory hold anything the loaders pass over? | this doc (A2a) | **`parity`** | §3 |
| Artifact naming and download split | `ci-wiring-planner` | `build-test` / `parity-transcript` | their doc; A1 depends on it |

**Three corrections their review produced, applied above:**

1. **The UTF-16LE claim was wrong.** I attributed it to the current default shell. pwsh
   (PowerShell 7) writes UTF-8 without a BOM; UTF-16LE redirects are Windows PowerShell 5.1
   (`shell: powershell`). The operative pwsh hazard is that `\` is not a line continuation.
   Corrected in §4.3 with the correction noted in place rather than silently rewritten.
2. **A1 was mis-scoped.** It globbed `transcripts/transcript.*.json`. Because the single-vector
   transcript is being *kept* as a second artifact, that glob would have reported four malformed
   envelopes on a correctly working gate. A1 now iterates `${corpus[@]}` only.
3. **A2b was a duplicate.** Dropped in favour of their `$on_disk` comparison; the stray sweep,
   which asks a different question, stays.

**On the CI observation** (§6.0): the emit steps have never executed on a hosted runner — eight
consecutive `failure` runs on this branch with `steps=0` on every job. Both of us investigated it
as a defect. **It is not one.** Hosted CI is off on a deliberate owner spending decision
(2026-08-06), until roughly 2026-08-27, and the owner has stated plainly that nothing is blocked on
it. Recorded here because an earlier revision of this document called it a blocker and asked that
restoring runners be treated as a prerequisite for closing Task 6.5 — that framing was wrong twice
over: it implied stalled work where there is none, and it pointed an investigation at the owner's
own budget. §6.0 is rewritten; the two genuinely runner-only proofs keep their `blocked-on-CI`
annotation as quiet bookkeeping.

**Settled in the second exchange (2026-08-06):**

4. **The stray sweep moves to `parity`**, on decoupling grounds rather than the reporting grounds I
   first gave — see open question 3 and §3. Both planners agree; their doc says so too.
5. **Dropping the count check was correct and must not be undone.** Their §3.2 now marks the
   `caseCount`-vs-`$on_disk` block as load-bearing for both documents specifically so a later
   tidy-up does not delete it as a duplicate of my sweep. It is not a duplicate: their check skips
   a stray by construction (`ParityVectorLoader.cs:197` ignores non-`.json`; `:198-203` makes a
   malformed `.json` a hard error), so a `README.md` or `.DS_Store` is invisible to it and visible
   only to the sweep.
6. **A1 supersedes only their message, not their control flow.** Their `absent` branch keeps its
   `continue` — `"absent"` must never reach a numeric comparison — and loses only the `echo` when
   A1 lands. Recorded because "supersedes" read as license to delete the whole branch.
7. **The two runner-only proofs are annotated, not blocking.** Reached independently on both sides
   before the owner directive landed, and narrowed by it: the annotation convention stands, the
   "prerequisite" framing does not. Their sharper reason for why a laptop cannot substitute — a
   control asserts the gate goes red *while the build is green* — is now quoted in §6.0.
8. **The stray sweep is extracted to `scripts/`, not inlined** (§3). Promoted from stylistic
   preference to recommendation for one reason: inline `run:` bash can only execute by pushing, and
   pushing currently executes nothing, so extraction is what makes this assertion's red-proof
   available this month rather than at the end of it. `ci-wiring-planner` made the same move in
   their §3.4.

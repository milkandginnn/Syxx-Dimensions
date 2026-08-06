# PROPOSAL — Wire the parity-transcript CI job to the corpus (2026-08-05)

Planning-seat proposal for track `cross-platform-parity_20260805`, Phase 6 Tasks 6.1 and 6.2
(`conductor/tracks/cross-platform-parity_20260805/plan.md:209-211`). **No source edited, no YAML
edited, no builds run, nothing committed.** Every change below is a proposed diff.

The engine side is done. Both CLIs take `--vectors=<dir>`, share one record builder with the
single-vector path, count `evaluated` per case actually concluded, and refuse to write when
`evaluated != caseCount` (`macOS/Sources/SyxxCLI/Commands/ParityTranscript.swift:94-128`, guard at
`:116`; `Windows/dotNET/Syxx/Commands/ParityTranscriptCommand.cs:141-205`, guard at `:188`). What is
missing is the wiring: **CI has never passed `--vectors` to either emitter**
(`.github/workflows/ci.yml:250-258`), so the job named `transcript-diff (blocking)` compares one
empty-`SectionInputs` vector across four runtimes while an 11-case corpus sits committed in
`corpus/parity-vectors/`.

---

## 0. What the job does today, and what it has actually demonstrated

Two facts, both established from run records rather than from reading the YAML, because they change
what the rest of this proposal has to be responsible for.

**Fact 1 — the emit steps have never executed in CI.** In the most recent run whose jobs executed
any steps at all (run `30879528119`, `main`, 2026-08-04), every `build-test` leg failed before
reaching the emitter:

| leg | step that failed | steps 18-19 (`Emit parity transcript`) | step 20 (`Upload parity transcript`) |
|---|---|---|---|
| macos-arm64 | 11 `Test the macOS twin (SyxxKit)` | skipped | **failure** |
| ubuntu-x64 | 5 `Test Ubuntu witness parity` | skipped | **failure** |
| windows-x64 | 7 `Build Windows GUI (SyxxUI, x64)` | skipped | **failure** |

`transcript-diff (blocking)` reported **skipped**. The upload step failed because
`if: always()` (`ci.yml:260`) runs it even when the emit was skipped, and `if-no-files-found: error`
(`ci.yml:265`) then turns "the leg was already red" into a second, misleading error pointing at the
transcript. So no transcript artifact has ever been produced by this workflow, and no line of the
diff script at `ci.yml:493-520` has ever run against real input.

**Fact 2 — hosted CI is off, and the reason is known.** The eight most recent runs on
`feature/portal-path-integrity` (latest `31067746003`, 2026-08-06T03:11Z) all completed in ~12s with
**zero steps recorded on every job** — jobs that never started, not jobs that ran and failed. That is
a spending limit, confirmed by the owner on 2026-08-06, not a defect in this workflow; hosted runners
are expected back around 2026-08-27. Nothing here is blocked on it. Local verification is the program
of record, which is why §3.4's script extraction moves from "worth considering" to the recommended
shape, and why §4 is ordered local-first with the two runner-only proofs annotated rather than
pending.

Consequence for this proposal: I cannot appeal to "the existing relative paths work in CI today,"
because they have never been observed to. Nothing about the current emit/upload pair is
verified-by-real-run. The design below therefore removes the assumption instead of inheriting it.

---

## 1. Task 6.1 — the four emit steps in corpus mode

### 1.1 The working-directory question, and why the answer is "don't depend on it"

Both loaders resolve the vector directory against the **process** working directory:

- C#: `Directory.GetFileSystemEntries(directory)` — `Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:171`
- Swift: `FileManager.default.contentsOfDirectory(...)` on `URL(fileURLWithPath: directory)` —
  `macOS/Sources/SyxxKit/Synthesis/ParityVectorLoader.swift:170`, called from
  `ParityTranscript.swift:97`

`--out` resolves the same way (`ParityTranscript.swift:80` `write(toFile:)`,
`ParityTranscriptCommand.cs:114` `File.WriteAllText`).

For the swift leg, `--package-path macOS` tells SwiftPM where the package is; it does not relocate
the launched binary's working directory, which is the step's — `${{ github.workspace }}`.

For the three dotnet legs the answer is genuinely unsettled, and the mechanism is worth writing down
because it is not guessable from the command line. `dotnet run` takes the child's working directory
from the MSBuild property `RunWorkingDirectory`, which defaults to `$(StartWorkingDirectory)`
(`/usr/local/share/dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets:1408`).
`StartWorkingDirectory` comes from a `launchSettings.json` profile, and
`Windows/dotNET/Syxx/` has no `Properties/launchSettings.json` — so the property evaluates empty, and
what the SDK's run command does with an empty value (inherit the caller's directory, or fall back to
the project directory) is SDK-internal behaviour this repo has never exercised: the test harness
sidesteps `dotnet run` entirely, invoking `dotnet <path-to-Syxx.dll>` with absolute paths
(`Windows/dotNET/Syxx.Tests/ParityVectorLoaderTests.cs:703-741`).

A gate whose correctness turns on an undocumented default is a gate that will one day pass while
reading the wrong directory. **Both arguments become absolute, rooted at `${{ github.workspace }}`.**
The alternative — `-p:RunWorkingDirectory=${{ github.workspace }}` on the dotnet legs, keeping the
arguments relative — pins the same thing through the same uncertain machinery; absolute paths pin it
in the emitter, where the resolution actually happens.

The same change applies to the *existing* single-vector steps (§2). Their relative `--out` has never
been shown to land beside the `upload-artifact` step's relative `path:` (Fact 1), so it is an
untested assumption in exactly the same place.

### 1.2 Matrix — one new key per leg

Artifact names must be disjoint from the single-vector ones (§2.2), so the corpus transcripts get
their own prefix, keeping the existing convention of naming the **runtime**, not the leg.

```diff
--- a/.github/workflows/ci.yml
+++ b/.github/workflows/ci.yml
@@ -53,19 +53,23 @@ jobs:
           - name: macos-arm64
             runs-on: macos-15
             kind: swift
             transcript: transcript.macos-arm64.json
+            corpus-transcript: corpus.macos-arm64.json
           - name: windows-x64
             runs-on: windows-latest
             kind: dotnet
             rid: win-x64
             transcript: transcript.net-win-x64.json
+            corpus-transcript: corpus.net-win-x64.json
           - name: ubuntu-x64
             runs-on: ubuntu-latest
             kind: dotnet
             rid: linux-x64
             transcript: transcript.net-linux-x64.json
+            corpus-transcript: corpus.net-linux-x64.json
           - name: ubuntu-arm64
             runs-on: ubuntu-24.04-arm
             kind: dotnet
             rid: linux-arm64
             transcript: transcript.net-linux-arm64.json
+            corpus-transcript: corpus.net-linux-arm64.json
```

### 1.3 Emit + upload steps

`run: >` (folded scalar) rather than `run: |` with backslash continuations: the windows-x64 leg's
default shell is pwsh, where `\` is not a line continuation. The folded form collapses to one line
before any shell sees it, matches the existing house pattern at `ci.yml:148-151`, and needs no
`shell:` override. No path here contains a space (`/home/runner/work/Syxx-Dimensions/…`,
`D:\a\Syxx-Dimensions\…`), and .NET accepts mixed separators on Windows, so the workspace root
interpolates safely unquoted.

That is not a stylistic preference, and it is load-bearing for anyone editing these steps later.
`shell:` appears **zero** times in `.github/workflows/ci.yml` and the workflow declares no
`defaults:` block (verified 2026-08-05), so every `run:` in it uses the runner default — bash on the
macOS and Linux legs, pwsh on windows-x64. The current emit steps survive that only because they are
bare `swift run` / `dotnet run` invocations containing no shell syntax at all. The moment one gains
a `$(...)`, a `[ ]` test, a pipe into `jq`, or an output redirect, the windows-x64 leg needs an
explicit `shell: bash` — and a redirect is the worst of them, because it fails as a *content*
difference rather than a syntax error: `>` under Windows PowerShell 5.1 (`shell: powershell`) writes
UTF-16LE, and the resulting transcript diverges from the other three legs on encoding while every
record in it is correct. Credit to the coverage-planner seat for pinning the `shell:` absence.
The proposed steps below keep the no-shell-syntax property deliberately; the guard rail is written
into the YAML comment so the next editor sees it in place.

```diff
@@ -250,17 +250,45 @@
-      # Phase 4.2: production-engine behavioral transcript (empty SectionInputs vector).
+      # Phase 4.2: production-engine behavioral transcript (empty SectionInputs vector).
+      # Kept alongside the corpus transcript below on purpose — see §2 of
+      # docs/PROPOSAL-parity-transcript-corpus-ci-2026-08-05.md. This is the ONLY thing in CI that
+      # compares the `--vectors`-absent dispatch across runtimes; the per-platform test suites
+      # exercise it, but never against each other.
       - name: Emit parity transcript (${{ matrix.transcript }})
         if: matrix.kind == 'swift'
-        run: |
-          swift run --package-path macOS syxx parity-transcript --out=${{ matrix.transcript }}
+        run: >
+          swift run --package-path macOS syxx parity-transcript
+          --out=${{ github.workspace }}/${{ matrix.transcript }}
       - name: Emit parity transcript (${{ matrix.transcript }})
         if: matrix.kind == 'dotnet'
-        run: |
-          dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release -- parity-transcript --out=${{ matrix.transcript }}
+        run: >
+          dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release --
+          parity-transcript
+          --out=${{ github.workspace }}/${{ matrix.transcript }}
+
+      # Phase 6.1: the corpus transcript — one record per case in corpus/parity-vectors/ (11 cases
+      # as of 2026-08-05), inside the {schemaVersion, caseCount, evaluated, records} envelope
+      # (ParityTranscript.swift:121-126, ParityTranscriptCommand.cs:197-203).
+      #
+      # `--vectors` is the whole difference: without it the emitters take the single-vector path
+      # (ParityTranscript.swift:30-36, ParityTranscriptCommand.cs:57) and the job downstream measures
+      # one vector while its name claims a corpus. That is what it has been doing since Phase 4.2.
+      #
+      # Both arguments are absolute because both loaders resolve relative paths against the PROCESS
+      # working directory (ParityVectorLoader.cs:171, ParityVectorLoader.swift:170) and `dotnet run`
+      # does not promise that directory is the step's: RunWorkingDirectory defaults to
+      # $(StartWorkingDirectory) (Microsoft.NET.Sdk.targets:1408), which is populated from a
+      # launchSettings profile, and Windows/dotNET/Syxx has none. A wrong directory does not fail
+      # quietly — the loader throws (ParityVectorLoader.cs:175) — but it would fail identically on
+      # all four legs, and four identical failures is not what this gate is for.
+      #
+      # KEEP THESE STEPS FREE OF SHELL SYNTAX. `shell:` appears nowhere in this workflow and there is
+      # no defaults: block, so windows-x64 runs pwsh while the other three run bash. A `$(...)`, a
+      # `[ ]`, a pipe, or a redirect added here needs an explicit `shell: bash`; a redirect most of
+      # all, because under Windows PowerShell it writes UTF-16LE and the leg then diverges on
+      # encoding while every record in its transcript is correct — a content failure, not a syntax
+      # error. The folded `>` scalar exists to keep these one shell-agnostic line.
+      - name: Emit corpus parity transcript (${{ matrix.corpus-transcript }})
+        if: matrix.kind == 'swift'
+        run: >
+          swift run --package-path macOS syxx parity-transcript
+          --vectors=${{ github.workspace }}/corpus/parity-vectors
+          --out=${{ github.workspace }}/${{ matrix.corpus-transcript }}
+      - name: Emit corpus parity transcript (${{ matrix.corpus-transcript }})
+        if: matrix.kind == 'dotnet'
+        run: >
+          dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release --
+          parity-transcript
+          --vectors=${{ github.workspace }}/corpus/parity-vectors
+          --out=${{ github.workspace }}/${{ matrix.corpus-transcript }}
+
       - name: Upload parity transcript (${{ matrix.transcript }})
-        if: always()
+        # Was `always()`, which ran this step even when the emit above was skipped by an earlier
+        # failure, so `if-no-files-found: error` reported a missing transcript instead of the real
+        # cause — observed on all four legs of run 30879528119 (2026-08-04). The emit is the step
+        # immediately before this one, so `always()` bought no artifact that success() does not.
+        # Absence of the artifact is still caught, in the place that can say what it means: the
+        # `parity-transcript` job's own guards (§3).
+        if: success()
         uses: actions/upload-artifact@v4
         with:
           name: ${{ matrix.transcript }}
           path: ${{ matrix.transcript }}
           if-no-files-found: error
+      - name: Upload corpus parity transcript (${{ matrix.corpus-transcript }})
+        if: success()
+        uses: actions/upload-artifact@v4
+        with:
+          name: ${{ matrix.corpus-transcript }}
+          path: ${{ matrix.corpus-transcript }}
+          if-no-files-found: error
```

`path:` stays relative: `upload-artifact` resolves it against `${{ github.workspace }}`, which is
where the absolute `--out` wrote it.

Cost: one extra CLI invocation per leg against an already-built project (the swift leg's debug build
is warm from the preceding `swift run`; the dotnet legs' Release output is warm from `ci.yml:88`
/ `:120`). Artifacts are a few KB.

---

## 2. The single-vector transcript: keep it

**Recommendation: keep it, as a second artifact, diffed as its own group.**

### 2.1 Why not retire it, when the corpus contains the same vector

It does contain the same vector, and this is worth being precise about rather than hand-waving:
`corpus/parity-vectors/001_empty-section-inputs.json` is `{"schemaVersion":1, "vectorId":
"empty-section-inputs", "pinnedUnixSeconds":1752268477, "sectionInputs":{}}` — an empty
`SectionInputs` at exactly the epoch the single-vector path pins (`ParityTranscript.swift:15`,
`ParityTranscriptCommand.cs:31-32`). Both paths call the same record builder
(`ParityTranscript.swift:41`, `ParityTranscriptCommand.cs:64`). So record 001 of the corpus
transcript is byte-identical to the single-vector transcript's body, and **no engine coverage is lost
by retiring it.**

What *is* lost is cross-runtime coverage of a different thing: the `--vectors`-absent branch itself —
argument parsing (`ParityTranscript.swift:22-32`, `ParityTranscriptCommand.cs:41-57`) and the bare
(un-enveloped) write. Each platform's own suite exercises that path (`plan.md:36`, `:88`), but a
per-platform test proves the path works *there*; it cannot notice that macOS and Windows disagree
about what it emits. This job is the only place those two are ever compared.

That is the same argument the workflow already makes in three other places about paths nothing
exercises: `ci.yml:153-157` (no Linux artifact ever left a run), `ci.yml:171-192` (C061 — the release
binary nobody inspected), `ci.yml:344-350` (the Avalonia GUI nothing compiled). Retiring a comparison
because a *different* comparison covers the engine underneath it is how those gaps opened.

### 2.2 Non-regression, and the collision that must not be handled by accident

Task 2.4's constraint was that the single-vector path keeps working while the corpus path is built —
honoured literally here: the emitters' flag-absent branch is untouched (`ParityTranscript.swift:19-21`
and `ParityTranscriptCommand.cs:51-53` both record that intent), the step keeps its name and artifact
name, and only its `--out` becomes absolute, which changes where the bytes land, not what they are.

The collision to avoid is in the diff job, and it is silent if the naming is careless. The current
download uses `merge-multiple: true` with no `pattern:` (`ci.yml:487-491`), which flattens **every
artifact in the run** into `transcripts/` — including `syxx-cli-${{ matrix.rid }}` (`ci.yml:158-164`)
and `Syxx-macos-arm64` (`ci.yml:337-342`) — and the script then globs `transcripts/transcript.*.json`
(`ci.yml:497`). A corpus artifact named `transcript.corpus.macos-arm64.json` would land in that glob,
and the script would diff an envelope against a bare record and fail with a difference that has
nothing to do with the engines.

Two changes prevent it: the `corpus.` prefix chosen in §1.2, and an explicit `pattern:` on each
download so the diff job stops pulling a self-contained CLI publish and an `.app` bundle it never
looks at.

---

## 3. Task 6.2 — the diff job, envelope-aware, with every guard kept

### 3.1 What the diff means now

The comparison is a whole-file `jq --sort-keys` diff. Applied to the envelope, that **already covers
every record**: `records` is a JSON array, both engines build it in the loader's sorted case order
(`ParityVectorLoader.cs:227-230`; the Swift twin sorts to match — `ParityTranscript.swift:112`
iterates the loaded order), and `jq --sort-keys` normalises the one known serialisation difference
(Swift writes `.sortedKeys`, `ParityTranscript.swift:73`; C# writes insertion order,
`ParityTranscriptCommand.cs:99-103`). Array order is *not* normalised by `--sort-keys`, which is
correct: two engines that agree on every record but disagree on case order have diverged, and the
diff should say so.

So a per-record comparison adds **no detection power at all**. What it adds is the failure message
the track requires: `plan.md:221` says the failure must *name the diverging case*, and plain `diff`
emits no context lines, so a one-field divergence prints

```
21c21
<     "determinationState": "clean"
---
>     "determinationState": "threatDetected"
```

with the `vectorId` line nowhere in the output. §3.3 therefore keeps the envelope diff as the
authority — it cannot miss anything — and adds a per-case pass that runs **only after** the envelope
diff has already failed, purely to name the cases. It can never turn a failure into a pass.

### 3.2 The guards, and one hole in how they are reached

Preserved verbatim in meaning:

1. **Zero transcripts found → fail** (`ci.yml:498-501`). Kept, per group.
2. **Fewer than four → fail, naming what was found** (`ci.yml:502-506`). Kept, per group.
3. **No vacuous pass**: the job never exits 0 without having compared something.

Guard 3 has a hole that is not in the script: `needs: build-test` (`ci.yml:481`) means one red leg
makes this job **skipped**, which is what happened on 2026-08-04 (Fact 1). A blocking gate that
disappears rather than fails is the same shape as the no-op this job was rescued from
(`ci.yml:438-452`), and GitHub treats a skipped required check as satisfied. Adding `if: always()`
at job level makes the missing artifact reach guard 2, which is already written to say the right
thing. The build stays red either way — `build-test` is red on its own — but the gate now answers
instead of abstaining, which is the same principle `ci.yml:445-448` states for the `parity` job.

**New (Task 6.2's case-count guard).** Whole-envelope identity is not corpus coverage. Point all four
legs at a two-case directory and the four transcripts are still identical: the diff passes, the gate
is green, and the corpus is 9 cases short. Nothing in the current script can see that, because the
only thing it compares transcripts to is each other. The guard cross-checks each transcript's
`caseCount` against the corpus in the checkout the gate itself is running from, and against the other
legs. Its scope is deliberately narrow — cross-leg agreement, and agreement with the repo — because
within-transcript coverage integrity belongs to the sibling proposal (§5).

**This block is now load-bearing for both proposals and must not be deleted as a duplicate.** The
coverage seat's own count check asked the same question in a different spelling and has been dropped
in favour of this one (§5), so it is the only place either document asks whether the legs read *this
checkout's* corpus. An earlier revision of §5 told a reader to delete this block if the sibling
proposal subsumed it; that instruction is withdrawn, and the settled ownership is recorded in §5.

The on-disk count must use the loader's definition of a case file, not `*.json`: a name is a case only
if it matches `NNN_<vectorId>.json` with three ASCII digits
(`ParityVectorLoader.cs:235-250`), non-matching `.json` entries are a hard error rather than a skip
(`:198-203`), and non-`.json` entries are ignored (`:197`).

### 3.3 Proposed job

```diff
@@ -477,45 +477,140 @@
   # Phase 4.2: behavioral transcript parity is BLOCKING. Emitters call production
-  # Synthesizer.conclude / Synthesizer.Conclude on a shared empty-input vector.
+  # Synthesizer.conclude / Synthesizer.Conclude over corpus/parity-vectors/ (Phase 6.1), plus the
+  # single-vector path as its own group (§2 of the proposal doc).
   parity-transcript:
     name: transcript-diff (blocking)
     needs: build-test
+    # `needs:` without `always()` makes a red build leg SKIP this job rather than fail it — observed
+    # on run 30879528119 (2026-08-04), where all four legs were red, no transcript artifact existed,
+    # and this gate reported "skipped". A gate that vanishes when its inputs vanish is the no-op this
+    # job was rescued from (see the `parity` job comment above). With always(), a missing artifact
+    # reaches the "fewer than four" guard below, which already knows how to say what happened.
+    if: always()
     runs-on: ubuntu-latest
     steps:
       - name: Checkout
         uses: actions/checkout@v4

-      - name: Download parity transcripts
+      # `pattern:` matters: without it, merge-multiple flattens EVERY artifact in the run into this
+      # directory — the self-contained CLI publishes and the macOS .app included — and any future
+      # artifact whose filename happens to match the glob below silently becomes an input to the
+      # parity comparison.
+      - name: Download corpus parity transcripts
         uses: actions/download-artifact@v4
         with:
+          pattern: corpus.*.json
+          path: corpus-transcripts
+          merge-multiple: true
+
+      - name: Download single-vector parity transcripts
+        uses: actions/download-artifact@v4
+        with:
+          pattern: transcript.*.json
           path: transcripts
           merge-multiple: true

-      - name: Diff conclusion transcripts (blocking)
+      - name: Diff conclusion transcripts (blocking)
         run: |
-          echo "Four-runtime transcript-parity diff."
+          set -uo pipefail
           shopt -s nullglob
-          found=(transcripts/transcript.*.json)
-          if [ ${#found[@]} -eq 0 ]; then
-            echo "::error title=Transcript parity gate empty::No conclusion transcripts were produced. Emitters must write transcript.*.json from the production synthesizer."
-            exit 1
-          fi
           expected=4
-          if [ ${#found[@]} -lt $expected ]; then
-            echo "::error title=Transcript parity gate incomplete::Expected $expected transcripts (one per matrix leg: macos-arm64, windows-x64, ubuntu-x64, ubuntu-arm64) but found ${#found[@]}: ${found[*]}."
-            exit 1
-          fi
-          echo "Transcripts found: ${found[*]}"
-          first="${found[0]}"
-          all_match=true
-          for f in "${found[@]:1}"; do
-            if ! diff <(jq --sort-keys . "$first") <(jq --sort-keys . "$f") > /tmp/transcript-diff.txt 2>&1; then
-              echo "::error title=Transcript parity failure::$first vs $f:"
-              cat /tmp/transcript-diff.txt
-              all_match=false
-            fi
-          done
-          if [ "$all_match" != true ]; then
-            exit 1
-          fi
-          echo "All transcripts match."
+          rc=0
+
+          # ---- group comparison. Identical guards for both groups; the group NAME is in every
+          # message so a failure says which comparison broke, not just that one did.
+          compare_group() {
+            local label="$1"; shift
+            local found=("$@")
+            if [ ${#found[@]} -eq 0 ]; then
+              echo "::error title=Transcript parity gate empty ($label)::No $label transcripts were produced. Emitters must write them from the production synthesizer; a gate with no input has measured nothing."
+              return 1
+            fi
+            if [ ${#found[@]} -lt $expected ]; then
+              echo "::error title=Transcript parity gate incomplete ($label)::Expected $expected $label transcripts (one per matrix leg: macos-arm64, windows-x64, ubuntu-x64, ubuntu-arm64) but found ${#found[@]}: ${found[*]}."
+              return 1
+            fi
+            echo "$label transcripts found: ${found[*]}"
+            local first="${found[0]}" f group_rc=0
+            for f in "${found[@]:1}"; do
+              # Whole-file, key-normalised. For the corpus group this covers every record: `records`
+              # is an array in loader-sorted case order on both engines, and --sort-keys does not
+              # reorder arrays, so a case-ORDER divergence is a difference too — correctly.
+              if ! diff -u <(jq --sort-keys . "$first") <(jq --sort-keys . "$f") > /tmp/diff.txt 2>&1; then
+                echo "::error title=Transcript parity failure ($label)::$first vs $f:"
+                cat /tmp/diff.txt
+                group_rc=1
+              fi
+            done
+            return $group_rc
+          }
+
+          # ---- which case diverged. Runs ONLY after the envelope diff above has already failed:
+          # it adds no detection, only a name. plan.md:221 requires the failure to name the diverging
+          # case, and `diff` prints no context lines, so a one-field divergence otherwise prints
+          # `"determinationState": "clean"` with the vectorId nowhere in the output.
+          name_diverging_cases() {
+            local first="$1" f="$2" id
+            local ids_a ids_b
+            ids_a=$(jq -r '.records[].vectorId' "$first")
+            ids_b=$(jq -r '.records[].vectorId' "$f")
+            if [ "$ids_a" != "$ids_b" ]; then
+              echo "::error title=Corpus case sets differ::$first and $f do not contain the same cases in the same order."
+              diff <(printf '%s\n' "$ids_a") <(printf '%s\n' "$ids_b") || true
+              return
+            fi
+            while IFS= read -r id; do
+              a=$(jq --sort-keys -c --arg id "$id" '.records[] | select(.vectorId == $id)' "$first")
+              b=$(jq --sort-keys -c --arg id "$id" '.records[] | select(.vectorId == $id)' "$f")
+              if [ "$a" != "$b" ]; then
+                echo "::error title=Parity divergence in case $id::$(basename "$first") $a"
+                echo "::error title=Parity divergence in case $id::$(basename "$f") $b"
+              fi
+            done <<< "$ids_a"
+          }
+
+          corpus=(corpus-transcripts/corpus.*.json)
+          single=(transcripts/transcript.*.json)
+
+          compare_group "corpus" "${corpus[@]}" || {
+            rc=1
+            if [ ${#corpus[@]} -ge 2 ]; then
+              for f in "${corpus[@]:1}"; do name_diverging_cases "${corpus[0]}" "$f"; done
+            fi
+          }
+          compare_group "single-vector" "${single[@]}" || rc=1
+
+          # ---- case-count guard (Task 6.2). Four IDENTICAL transcripts over a two-case directory
+          # pass every check above: identity between transcripts is not coverage of the corpus,
+          # because the only thing they are compared against is each other. This is the one check
+          # that compares them to the repo the gate is running from.
+          #
+          # The count uses the loader's definition of a case file — NNN_<vectorId>.json, three ASCII
+          # digits (ParityVectorLoader.cs:235-250) — not *.json, so it cannot drift from what the
+          # emitters actually load.
+          on_disk=$(find corpus/parity-vectors -maxdepth 1 -type f \
+                      -regex '.*/[0-9][0-9][0-9]_[^/]*\.json' | wc -l | tr -d ' ')
+          if [ "$on_disk" -eq 0 ]; then
+            echo "::error title=Corpus missing::corpus/parity-vectors contains no NNN_<vectorId>.json case files in this checkout. The gate cannot state what the transcripts should have covered."
+            rc=1
+          fi
+          for f in "${corpus[@]}"; do
+            cc=$(jq -r '.caseCount // "absent"' "$f")
+            # The `absent` case means a leg emitted the single-vector shape, which has no caseCount
+            # key at all (ParityTranscript.swift:55-68). The coverage proposal's A1 owns naming this
+            # condition and says which leg was left un-switched; when it lands, DELETE THE ECHO AND
+            # KEEP THE `continue`, which is control flow — "absent" must never reach the numeric
+            # comparison below. Until then the echo stays, because a silent rc=1 is worse than a
+            # duplicated message.
+            if [ "$cc" = "absent" ]; then
+              echo "::error title=Not a corpus transcript::$f has no caseCount — it is a single-vector transcript in the corpus group, which means a leg emitted without --vectors."
+              rc=1
+              continue
+            fi
+            if [ "$cc" != "$on_disk" ]; then
+              echo "::error title=Corpus coverage shortfall::$f reports caseCount=$cc but corpus/parity-vectors holds $on_disk case files. The leg read a different corpus than this checkout contains."
+              rc=1
+            fi
+          done
+
+          # <<< coverage assertions land here — see
+          # docs/PROPOSAL-parity-corpus-coverage-assertions-2026-08-05.md >>>
+
+          if [ $rc -ne 0 ]; then exit 1; fi
+          echo "All transcripts match; corpus group covers $on_disk cases on all $expected legs."
```

### 3.4 Extract the script — recommended, and load-bearing while runners are off

The workflow's other two gates are files (`scripts/cross-platform-parity-gate.py`,
`scripts/package-pin-parity.py` — `ci.yml:468`, `:475`), which is why they can be run and red-proofed
on a laptop. Inline bash in a `run:` block can only be executed by pushing a commit, and per Fact 2
pushing currently executes nothing. Extracting it to
`scripts/parity-transcript-diff.sh <corpus-dir> <single-dir> <expected-legs>` reduces the step to

```yaml
      - name: Diff conclusion transcripts (blocking)
        run: scripts/parity-transcript-diff.sh corpus-transcripts transcripts 4
```

and turns most of §4 into local commands, runnable today against two directories of hand-placed
transcripts. It is a larger diff than Task 6.1 strictly needs, which is why it was originally raised
as an open question rather than assumed; with hosted runners unavailable it is the difference between
a gate whose behaviour can be demonstrated now and one whose behaviour cannot be demonstrated at all
until the runners return. Recommended.

---

## 4. Red-proof protocol

`plan.md:299` — *every gate added has been demonstrated going red at least once*; `plan.md:221` — the
failure must name the diverging case. Four gates ship here, so four demonstrations, each with the
output that proves it.

**Run them locally.** With §3.4 adopted, experiments 1-3 need no runner at all: emit two transcripts
with the local toolchains, place them in a `corpus-transcripts/` and a `transcripts/` directory, and
run `scripts/parity-transcript-diff.sh` over them. Every guard except the four-leg count is exercised
that way, and the four-leg count is exercised by hand-placing four files. This is the primary path
while hosted runners are off, not a fallback.

Two things genuinely need a matrix and get a quiet `blocked-on-CI` annotation until runners return
(§4's recording rules): the four-leg green run, and experiment 1's **control** — a *green build* with
the corpus silently shortened, which a laptop cannot produce because locally there is no build to be
green. Everything else below is demonstrable today.

**Scratch branch** (for the matrix experiments, when runners return):
`scratch/parity-corpus-redproof-2026-08-05`, pushed, never merged, deleted after the outputs are
captured. The local variants need no branch — they run against a dirty working tree.

**1. Case-count guard — the one the diff cannot do.** Add `corpus/parity-vectors-subset/` with copies
of cases 001 and 002 only, and point all four `--vectors=` at it. Every leg emits the same 2-case
envelope, so the envelope diff **passes**. Expected: exit 1 from the count guard alone —

```
::error title=Corpus coverage shortfall::corpus-transcripts/corpus.macos-arm64.json reports caseCount=2 but corpus/parity-vectors holds 11 case files. The leg read a different corpus than this checkout contains.
```

This is the demonstration that matters: *the diff was green and the gate still failed.*

**2. Divergence naming a single case.** One temporary line in the C# record builder
(`ParityTranscriptCommand.cs:91`), scoped to one `vectorId` so exactly one case diverges and the
message can be checked for precision:

```csharp
["hasHardGatedSection"] = c.VectorId == "glutton-ecs-forwarded"
    ? !conclusion.HasHardGatedSection : conclusion.HasHardGatedSection,   // RED-PROOF, revert
```

Expected: the three dotnet legs diverge from macos-arm64; the envelope diff fails; and
`name_diverging_cases` prints `::error title=Parity divergence in case glutton-ecs-forwarded::` with
both records. Verify no *other* case is named — that is what proves the message is precise rather
than merely present.

**3. Both anti-vacuity guards.** Two pushes, or one push with two steps:
*(a)* `if: matrix.name != 'ubuntu-arm64'` on the corpus upload → expected
`Transcript parity gate incomplete (corpus)::Expected 4 … but found 3: …`;
*(b)* the download `pattern:` temporarily set to `corpus-nothing.*.json` → expected
`Transcript parity gate empty (corpus)`. Guard (a) doubles as the proof that `if: always()` on the
job works: with a leg deliberately red, confirm the check reports **failure**, not **skipped**.

**4. Emitter refusal still reachable.** Already red-proofed at the CLI level (`plan.md:85`, both
sides exit 3 with no file written). Confirm once through CI that the refusal fails the *step*: on the
scratch branch, point one leg's `--vectors` at a directory containing one non-conforming filename
(e.g. `corpus/parity-vectors-bad/12_x.json`, refused at `ParityVectorLoader.cs:200-203`). Expected:
non-zero exit from the emit step, the upload skipped by `if: success()`, and the diff job red on the
"fewer than four" guard rather than skipped.

**Recording, per this track's conventions:**

- **`plan.md`** — annotate Tasks 6.1 and 6.2 in the established form, e.g.
  `(verified-by-real-run 2026-08-06: all-legs-subset corpus → envelope diff green, count guard red
  naming caseCount=2 vs 11 on disk; single-case divergence in glutton-ecs-forwarded named by the
  gate; 3-of-4 and 0-found both red)` — matching `plan.md:122`, `:141`, `:198`, `:200`. Tick the
  Phase 6 Verification boxes at `:220-222` only once experiments 1 and 2 have real output.
- **`ci-latest-failures.log`** — replace with `gh run view <id> --log-failed` for the red-proof run,
  which is how the file was produced (it currently holds the 2026-08-05T17:35Z Avalonia failures).
  Consider keeping the red-proof output beside it rather than overwriting.
- **`findings.md`** — only if an experiment surfaces a *defect* rather than a demonstration (an
  `F-NN` entry with State / Found by / Severity / Evidence table, per its header). A gate behaving as
  designed is a plan annotation, not a finding.

**What to write for the two runner-only proofs.** They are annotated **`blocked-on-CI`** with the
reason — never ticked, never quietly omitted, and never softened to "verified locally" when what ran
locally was a different thing. That annotation is bookkeeping on two lines of a track file, not a
status: the rest of Phase 6 proceeds on local evidence, and this proposal is implementable today.

The reason those two cannot be closed locally is worth stating precisely, because "run it on the
laptop" sounds like it should cover them. The strongest demonstration in §4 is a *control*:
experiment 1 asserts the gate goes red **while the diff is green**, and a green build is exactly what
a laptop cannot produce, because locally there is no build to be green. Running the script locally
proves the script works; it cannot prove that without it the corpus silently shrinks and everything
else still passes. Those are different claims, and only the second justifies the gate existing.
So `plan.md:220-222` — "all four legs green with equal case counts" — waits for four legs, while
everything it does not name goes green on local runs. The coverage seat reached the same conclusion
independently, which is worth the owner knowing: convergence from two directions beats either of us
asserting it.

**How the boxes get marked**, using one convention across both proposals so a reader of either lands
in the same place. When the local proofs pass and the task is otherwise done, **tick the box and keep
the two annotated lines beside it** — leaving it open misreports finished work as unstarted, which is
the more misleading of the two errors available here. The failure mode to guard against is not a
ticked box; it is the two lines quietly disappearing from a later tidy-up, at which point the record
claims four-leg evidence nobody has. `plan.md:220-222` is the one exception, because that box names
the four legs in its own text: it stays open until they run.

---

## 5. Interaction with the sibling coverage-assertions proposal

`docs/PROPOSAL-parity-corpus-coverage-assertions-2026-08-05.md` is being written in parallel by
another seat; this document does not specify its content and its author should not be constrained by
the sketch below.

**Insertion point:** the marked line in §3.3, immediately before the final `rc` check —

```bash
          # <<< coverage assertions land here — see
          # docs/PROPOSAL-parity-corpus-coverage-assertions-2026-08-05.md >>>
```

At that line, every corpus transcript is downloaded and validated as a corpus transcript, `$on_disk`
holds the case-file count from this checkout, `${corpus[@]}` holds the four artifact paths, and `rc`
is the accumulator — set `rc=1` to fail; do not `exit` early, or later assertions stop reporting.

**Only A1's fragment lands here.** As settled below, the coverage seat's stray sweep now lives in the
`parity` job as a standalone step with its own `exit 1`, not in this script. The two forms are not
interchangeable: an `exit` pasted into this script at the insertion point would stop every later
assertion — including the diff conclusion at the end — from reporting. If a future assertion arrives
without saying which form it is, it is the `rc=1` form, because everything in this script shares one
accumulator by design.

**Boundary.** This proposal claims only what needs all four legs at once, or a comparison against the
repository: cross-leg `caseCount` agreement, `caseCount` against the corpus on disk, and the diff
itself. Assertions **within** a single transcript — `evaluated == caseCount`, `records | length`
against both, per-case identifiers against the corpus directory, `schemaVersion` — are theirs.

**Ownership, settled with the coverage seat 2026-08-05.** Both overlaps are resolved, in opposite
directions, and each resolution is recorded here because both documents are now instructions to
whoever implements them:

| condition | owner | consequence |
|---|---|---|
| a leg emitted the single-vector shape (no `caseCount`) | **theirs** — A1's malformed-envelope branch, which names the un-switched leg | delete the `echo` in §3.3's `absent` branch when A1 lands; **keep the `continue`** — it is control flow, not a message |
| did the legs read *this checkout's* corpus? | **mine** — the `caseCount`-vs-`$on_disk` block in §3.3 | their equivalent count check is dropped; §3.3's block is now the only place either document asks this, and must not be deleted as a duplicate |
| does the corpus directory hold anything the loaders pass over? | **theirs** — the stray sweep, now `scripts/parity-corpus-stray-sweep.py --github` in the **`parity`** job | genuinely unanswerable by any count using the loaders' filename rule, including mine; the two coexist in different jobs, and it exits on the `parity` job's own convention (`ci.yml:450-452`: 0 only after examining a directory, 1 on strays, 2 on cannot-run) rather than this script's `rc=1` |

An earlier revision of this section said "theirs supersedes" as a blanket rule and told a reader to
delete the §3.3 count block if their design subsumed it. That is withdrawn — it would now delete the
only surviving check for that question. Recorded rather than rewritten silently, since the earlier
wording has been read.

**Placement of the stray sweep — settled: it moved to `parity`.** It had been placed in
`parity-transcript` only so it could sit beside the count check that has now been dropped, and it
needs nothing but the repository: no build, no artifacts, no downloads.

The operative reason is **decoupling**, and the first version of the argument was wrong in a way
worth recording. The original rationale was that moving it means its answer is never withheld when a
build leg fails — which my proposed `if: always()` (§3.2) falsifies, since the sweep would keep
reporting where it was. The surviving reason is that leaving it there makes a corpus-integrity
assertion's reporting conditional on §6 question 2 being approved, in a different proposal. Secondary
benefits: `parity` takes no `needs:` (`ci.yml:445-448`) so it starts immediately rather than after the
four-leg matrix; it is ubuntu-latest with the bash default, so no shell key; and it leaves this script
owning everything artifact-dependent, with A1's fragment as the only surface the two documents share.
Recorded rather than tidied away because a rationale that was falsified once is the kind of thing that
gets re-litigated later.

**Scope — corpus group only.** The insertion point is inside the corpus half of the script on
purpose. Envelope assertions must never be applied to `transcripts/transcript.*.json`: a
single-vector transcript is a bare record with no `caseCount`, `evaluated`, or `records` key at all
(`ParityTranscript.swift:55-68`), so an envelope check pointed at that group reports four malformed
envelopes on a correctly working gate. §2 keeps that group in the run, which makes the directory
split — `corpus-transcripts/` versus `transcripts/` — the thing that keeps the two sets of
assertions from meeting.

**Sequencing.** Their assertions are inert until this switch lands, and their malformed-envelope
branch fires on any leg still emitting the single-vector shape — by design, since it names an
un-switched leg rather than letting the diff blame the engines. The two changes therefore want the
same PR, or theirs immediately after this one. Landing theirs first turns the gate red on all four
legs for a reason that is correct but reads as a regression.

If their proposal is not approved, the §3.3 block stands alone and Task 6.2's case-count guard is
satisfied; the two are additive, not co-dependent.

---

## 6. Open questions for the owner

1. **Extract the diff script (§3.4)?** Recommended, and consistent with how the other two gates in
   this workflow are built, but it is scope beyond Task 6.1. It is also what makes §4's red-proofs
   runnable now rather than after 2026-08-27: inline bash in a `run:` block can only be executed by
   pushing, and pushing currently executes nothing. This is the one question whose answer changes what
   can be verified this month.
2. **`if: always()` on `parity-transcript` (§3.2).** This makes a currently-skipping check start
   reporting failure whenever any `build-test` leg is red. Correct in principle; it will also make a
   second red check appear on every PR that has a red leg, which some readers will experience as
   noise. Confirm that is wanted before it lands.
3. **Retiring the single-vector transcript later.** §2 recommends keeping it. If runner time becomes
   the constraint, the case to retire it is stronger than it looks, because corpus case 001 is that
   exact vector at that exact epoch — the loss is coverage of the flag-absent dispatch, not of the
   engine. Worth a decision when Phase 6 closes rather than now.
4. **Two proofs wait for runners; nothing else does.** Hosted CI is off on a spending limit until
   roughly 2026-08-27 (Fact 2), so `plan.md:220-222` ("all four legs green with equal case counts")
   and experiment 1's green-build control carry a `blocked-on-CI` annotation. Everything else in this
   proposal — the YAML, the script, and every guard's red-proof — is implementable and demonstrable
   locally now. No action needed from the owner here; it is recorded so the two unticked boxes are
   not read later as forgotten work.
5. **`corpus.<runtime>.json` naming.** Chosen to mirror the existing `transcript.<runtime>.json`
   convention and to keep the two download patterns disjoint. If the owner prefers grouping by leg
   (`parity/<leg>/{single,corpus}.json` via per-leg artifact directories), say so before this lands —
   changing artifact names afterwards silently breaks anything downstream that globs them.

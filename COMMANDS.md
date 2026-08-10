# Corrected Claude Command Order

## Step 0 — Establish local ground truth
Run from the repository root before agent work and after every parity change:

```zsh
cd /Users/masterrabbit/Software/Syxx
syxx-parity
```

Portable alternative:
```zsh
cd /Users/masterrabbit/Software/Syxx
bash scripts/parity-local-gate.sh
```

Success should report:
`PASS parity-local-gate: 11 cases, two runtimes, zero divergence`

If the gate reports a divergence, use this manual transcript comparison pipeline to retain and diff normalized JSON outputs:

```zsh
cd /Users/masterrabbit/Software/Syxx

syxx_parity_dir="$(mktemp -d /tmp/syxx-parity.XXXXXX)" && \
dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release -- \
  parity-transcript \
  --vectors=corpus/parity-vectors \
  --out="$syxx_parity_dir/t.net.json" && \
swift run --package-path macOS -c release syxx \
  parity-transcript \
  --vectors=corpus/parity-vectors \
  --out="$syxx_parity_dir/t.swift.json" && \
diff \
  <(jq --sort-keys . "$syxx_parity_dir/t.net.json") \
  <(jq --sort-keys . "$syxx_parity_dir/t.swift.json") && \
echo "IDENTICAL — full parity corpus matched" && \
echo "Transcripts saved in: $syxx_parity_dir"
```

---

## Step 1 — Check the existing Conductor track
Check the durable track status in Claude Code:

```text
/conductor:status twin-parity-fingerprint_20260809 --detailed
```

> [!NOTE]
> Do not run `/conductor:setup`; Conductor is already initialized. Do not create another copy of the existing track. Run `/conductor:setup` only in a fresh checkout where `conductor/product.md` does not exist.

For genuinely new parity features requiring a new track:

```text
/conductor:new-track feature <new-track-name>
```

---

## Step 2 — Discover candidate parity divergences
Run an automated reviewer team (automatically collects findings and cleans up):

```text
/agent-teams:team-review . --reviewers="transfer-encoding,unicode-equality-and-idn,time-precision-and-rounding,filesystem-case-sensitivity,trust-stores,collection-ordering"
```

> [!NOTE]
> Do not use `team-spawn custom --dimensions`; `--dimensions` is unsupported. Reviewer findings do not establish parity until both engines execute corresponding vectors.

If long-lived research requiring follow-up assignments beyond a single review is needed:

```text
/agent-teams:team-spawn research --name parity-discovery --members 3
/agent-teams:team-status parity-discovery
/agent-teams:team-delegate parity-discovery
/agent-teams:team-delegate parity-discovery --assign <task-id>=<member-name>
```

---

## Step 3 — Certify new or changed vectors
After reviewing intentional changes under `corpus/parity-vectors`:

```zsh
cd /Users/masterrabbit/Software/Syxx

swift run --package-path macOS syxx parity-transcript \
  --vectors=corpus/parity-vectors \
  --list-only > corpus/parity-vectors.manifest

bash scripts/regenerate-parity-fingerprint.sh

syxx-parity
```

A vector is certified only when:
- It appears in the manifest.
- Both engines evaluate it.
- The evaluated count equals the case count.
- Swift and C# produce identical normalized transcripts and fingerprints.

> [!WARNING]
> Do not regenerate the fingerprint merely to silence an unexplained mismatch.

---

## Step 4 — Implement only genuinely new infrastructure & fix findings
The corpus-driven emitter and evaluated-case assertion already exist. Do not repeat that work.

For implementing code fixes or new independently decomposable parity features across runtimes, spawn implementation agents (`team-implementer`) with file-editing permissions:

```text
/agent-teams:team-feature "<feature description and acceptance criteria>" --team-size 4 --plan-first
```

Approve exclusive file ownership before implementation begins.

---

## Step 5 — Test and review
Generate tests only for identified coverage gaps:

```text
/unit-testing:test-generate "Add mutation-resistant parity-transcript tests for corpus coverage, loader rejection, fingerprint mismatch, and evaluated == caseCount"
```

Run the real verification:

```zsh
cd /Users/masterrabbit/Software/Syxx

swift test --package-path macOS -c release --filter Parity

dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~Parity'

python3 scripts/cross-platform-parity-gate.py

syxx-parity
```

Then run the Claude reviews:

```text
/security-review
/simplify
/code-review ultra
```

If `/simplify` changes anything, rerun all applicable tests and `syxx-parity`.

---

## Step 6 — Close out
Check the existing track:

```text
/conductor:status twin-parity-fingerprint_20260809 --detailed
```

Only if a manually managed research team remains active:

```text
/agent-teams:team-shutdown parity-discovery
```

Record the verified result in `Linux/PARITY-LEDGER.md` using the ledger’s five-state vocabulary.

---

## Step 7 — Verify the CI runtime matrix
In Claude Code:

```text
/loop 10m check the parity and parity-transcript jobs on this branch; report failures with job and step names
```

Or directly from the terminal:

```zsh
gh pr checks --watch
```

Cancel the loop after every relevant CI job reaches a terminal state.
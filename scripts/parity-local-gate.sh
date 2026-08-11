#!/usr/bin/env bash
# Local two-runtime parity gate over corpus/parity-vectors (track cross-platform-parity_20260805,
# Phase 6 — local stand-in while hosted runners are unavailable, owner ruling 2026-08-06).
#
# This is the runnable subset of the two Phase 6 proposals
# (docs/PROPOSAL-parity-transcript-corpus-ci-2026-08-05.md,
# docs/PROPOSAL-parity-corpus-coverage-assertions-2026-08-05.md) for one host with both toolchains:
# it proves the two runtimes on THIS machine read the same corpus and conclude identically. What it
# cannot prove is the four-leg matrix (four OS/arch legs) — that remains a CI-only demonstration.
#
# Assertions accumulate into rc rather than exiting early, so one failure never hides another
# (settled integration contract, CI proposal §5).
set -uo pipefail

resolve_script_dir() {
  local source="${BASH_SOURCE[0]}"
  while [ -h "$source" ]; do
    local dir
    dir="$(cd -P "$(dirname "$source")" && pwd)"
    source="$(readlink "$source")"
    case "$source" in
      /*) ;;
      *) source="$dir/$source" ;;
    esac
  done
  cd -P "$(dirname "$source")" && pwd
}

REPO="$(cd "$(resolve_script_dir)/.." && pwd)"
CORPUS="$REPO/corpus/parity-vectors"
MANIFEST="$REPO/corpus/parity-vectors.manifest"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
rc=0

# Verify corpus fingerprint
if ! bash "$REPO/scripts/verify-parity-fingerprint.sh"; then
    echo "FATAL: corpus fingerprint mismatch — refusing to run on a tampered corpus"
    exit 2
fi

# --- Assertion 1: stray sweep. Every entry in the corpus directory must BE a case file.
# Deliberately NOT a *.json glob: both loaders skip a non-.json entry silently
# (ParityVectorLoader.swift:195, ParityVectorLoader.cs:197), so a case renamed .json.orig shrinks
# the corpus with no message anywhere — and a *.json count shares that exact blind spot.
strays="$(find "$CORPUS" -mindepth 1 \( ! -type f -o ! -name '[0-9][0-9][0-9]_*.json' \) | sort)"
if [ -n "$strays" ]; then
  echo "FAIL stray-sweep: entries that are not PARITY_VECTOR_V1 cases:" >&2
  echo "$strays" >&2
  rc=1
else
  echo "ok   stray-sweep"
fi

# --- Manifest sanity. An empty manifest must not let anything below pass vacuously.
declared="$(head -1 "$MANIFEST" | sed -n 's/^caseCount=//p')"
if [ -z "$declared" ] || [ "$declared" -le 0 ]; then
  echo "FAIL manifest: $MANIFEST does not declare a positive caseCount" >&2
  exit 1
fi

# --- Assertions 2+3: each runtime's --list-only must equal the committed manifest byte-for-byte.
# The manifest is the one expectation stored OUTSIDE the corpus, so it is the only thing here a
# case DELETION cannot silently update (coverage proposal §4.2).
(cd "$REPO/macOS" && swift run syxx parity-transcript \
    --vectors="$CORPUS" --list-only >"$WORK/list.swift.txt" 2>"$WORK/swift.stderr") \
  || { echo "FAIL swift --list-only exited nonzero:" >&2; cat "$WORK/swift.stderr" >&2; rc=1; }

dotnet build "$REPO/Windows/dotNET/Syxx/Syxx.csproj" --nologo -v q >"$WORK/csbuild.log" 2>&1 \
  || { echo "FAIL dotnet build:" >&2; tail -5 "$WORK/csbuild.log" >&2; exit 1; }
DLL="$(ls -t "$REPO"/Windows/dotNET/Syxx/bin/Debug/net*/Syxx.dll | head -1)"
dotnet "$DLL" parity-transcript --vectors="$CORPUS" --list-only >"$WORK/list.cs.txt" \
  || { echo "FAIL C# --list-only exited nonzero" >&2; rc=1; }

for leg in swift cs; do
  if ! cmp -s "$MANIFEST" "$WORK/list.$leg.txt"; then
    echo "FAIL manifest ($leg): the corpus and $MANIFEST disagree —" >&2
    diff "$MANIFEST" "$WORK/list.$leg.txt" >&2 || true
    echo "regenerate with: swift run --package-path macOS syxx parity-transcript" \
         "--vectors=corpus/parity-vectors --list-only > corpus/parity-vectors.manifest" >&2
    rc=1
  else
    echo "ok   manifest agreement ($leg)"
  fi
done

# --- Assertion 4: full corpus transcripts, compared whole. jq --sort-keys normalises the one known
# serialisation difference (Swift .sortedKeys vs C# insertion order) and does NOT reorder arrays —
# correct, since case-order disagreement is a real divergence (CI proposal §3.1).
(cd "$REPO/macOS" && swift run syxx parity-transcript \
    --vectors="$CORPUS" --out="$WORK/corpus.swift.json" >/dev/null 2>&1) \
  || { echo "FAIL swift corpus emission exited nonzero" >&2; rc=1; }
dotnet "$DLL" parity-transcript --vectors="$CORPUS" --out="$WORK/corpus.cs.json" \
  || { echo "FAIL C# corpus emission exited nonzero" >&2; rc=1; }

if [ -f "$WORK/corpus.swift.json" ] && [ -f "$WORK/corpus.cs.json" ]; then
  if diff <(jq --sort-keys . "$WORK/corpus.swift.json") \
          <(jq --sort-keys . "$WORK/corpus.cs.json") >"$WORK/transcript.diff"; then
    echo "ok   transcript diff (swift vs cs, jq --sort-keys)"
  else


    echo "FAIL transcript diff — the two runtimes disagree:" >&2
    cat "$WORK/transcript.diff" >&2
    # Name the diverging cases, since plain diff omits the vectorId context (CI proposal §3.1).
    for i in $(seq 0 $((declared - 1))); do
      a="$(jq --sort-keys ".records[$i]" "$WORK/corpus.swift.json")"
      b="$(jq --sort-keys ".records[$i]" "$WORK/corpus.cs.json")"
      [ "$a" = "$b" ] || echo "  diverging case: $(jq -r ".records[$i].vectorId" "$WORK/corpus.swift.json")" >&2
    done
    rc=1
  fi

  # --- Assertion 5: envelope self-consistency, read by the gate rather than trusted, and pinned to
  # the manifest's count so both emitters agreeing on a shrunken corpus still fails here.
  for leg in swift cs; do
    read -r cc ev nr <<<"$(jq -r '"\(.caseCount) \(.evaluated) \(.records|length)"' "$WORK/corpus.$leg.json")"
    if [ "$cc" != "$declared" ] || [ "$ev" != "$declared" ] || [ "$nr" != "$declared" ]; then
      echo "FAIL envelope ($leg): caseCount=$cc evaluated=$ev records=$nr, manifest declares $declared" >&2
      rc=1
    else
      echo "ok   envelope ($leg): caseCount=$cc evaluated=$ev records=$nr == manifest $declared"
    fi
  done

  # --- Assertion 5a: transcript fingerprints must match (proves both twins produced the same output)
  swift_fp="$(jq -r '.fingerprint // empty' "$WORK/corpus.swift.json")"
  cs_fp="$(jq -r '.fingerprint // empty' "$WORK/corpus.cs.json")"
  if [ -z "$swift_fp" ] || [ -z "$cs_fp" ]; then
    echo "FAIL transcript fingerprint missing — one or both platforms did not emit a fingerprint" >&2
    rc=1
  elif [ "$swift_fp" != "$cs_fp" ]; then
    echo "FAIL transcript fingerprint mismatch — swift=$swift_fp cs=$cs_fp" >&2
    rc=1
  else
    echo "ok   transcript fingerprint match ($swift_fp)"
  fi
fi

if [ "$rc" -eq 0 ]; then
  echo "PASS parity-local-gate: $declared cases, two runtimes, zero divergence"
else
  echo "FAIL parity-local-gate" >&2
fi
exit "$rc"

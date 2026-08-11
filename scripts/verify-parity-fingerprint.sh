#!/usr/bin/env bash
# verify-parity-fingerprint.sh — verify the parity corpus fingerprint
# Usage: bash scripts/verify-parity-fingerprint.sh
# Exit: 0 = match, 2 = mismatch (cannot run), 1 = error
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
STORED="$REPO_ROOT/corpus/parity-fingerprint.sha256"

if [ ! -f "$STORED" ]; then
    echo "FATAL: corpus/parity-fingerprint.sha256 not found — run scripts/regenerate-parity-fingerprint.sh first"
    exit 2
fi

# Recompute fingerprint the same way as regenerate
CORPUS_DIR="$REPO_ROOT/corpus/parity-vectors"
MANIFEST="$REPO_ROOT/corpus/parity-vectors.manifest"

FILES=()
while IFS= read -r -d '' f; do
    FILES+=("$f")
done < <(LC_ALL=C find "$CORPUS_DIR" -type f -name '*.json' -print0 | LC_ALL=C sort -z)
FILES+=("$MANIFEST")

TMP=$(mktemp)
for f in "${FILES[@]}"; do
    sha256sum "$f" | cut -d' ' -f1 >> "$TMP"
done

COMPUTED=$(LC_ALL=C sort "$TMP" | sha256sum | cut -d' ' -f1)
rm -f "$TMP"

STORED_FP=$(cat "$STORED")

if [ "$COMPUTED" != "$STORED_FP" ]; then
    echo "FATAL: corpus fingerprint mismatch — corpus has been modified since fingerprint was generated"
    echo "  Stored:    $STORED_FP"
    echo "  Computed:  $COMPUTED"
    echo "  Run scripts/regenerate-parity-fingerprint.sh to update the fingerprint if the changes are intentional."
    exit 2
fi

echo "Fingerprint verified: $COMPUTED"
exit 0

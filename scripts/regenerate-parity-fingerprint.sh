#!/usr/bin/env bash
# regenerate-parity-fingerprint.sh — compute SHA-256 fingerprint of the parity corpus
# Usage: bash scripts/regenerate-parity-fingerprint.sh
# Output: corpus/parity-fingerprint.sha256 (one-line hex fingerprint)
set -euo pipefail
IFS=$'\n\t'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CORPUS_DIR="$REPO_ROOT/corpus/parity-vectors"
MANIFEST="$REPO_ROOT/corpus/parity-vectors.manifest"
OUTPUT="$REPO_ROOT/corpus/parity-fingerprint.sha256"

# Collect all files in deterministic order
FILES=()
while IFS= read -r -d '' f; do
    FILES+=("$f")
done < <(LC_ALL=C find "$CORPUS_DIR" -type f -name '*.json' -print0 | LC_ALL=C sort -z)

# Add the manifest
FILES+=("$MANIFEST")

# Compute SHA-256 of each file and concatenate for final fingerprint
TMP=$(mktemp)
for f in "${FILES[@]}"; do
    sha256sum "$f" | cut -d' ' -f1 >> "$TMP"
done

# Final fingerprint: SHA-256 of the concatenated per-file hashes
FINGERPRINT=$(LC_ALL=C sort "$TMP" | sha256sum | cut -d' ' -f1)
rm -f "$TMP"

echo "$FINGERPRINT" > "$OUTPUT"
echo "Fingerprint written to $OUTPUT: $FINGERPRINT"
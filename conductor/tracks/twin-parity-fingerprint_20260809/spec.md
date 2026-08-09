# spec.md — Twin Parity Fingerprint

**Track ID:** twin-parity-fingerprint_20260809
**Type:** feature
**Status:** new

## Overview

Add cryptographic fingerprinting to the cross-platform parity system at two levels:

1. **Corpus fingerprint** — a SHA-256 hash of the entire parity corpus (vectors + manifest)
   committed to the repo. Parity gates verify the fingerprint before running, refusing to
   operate on a tampered or incomplete corpus.

2. **Transcript fingerprint** — each parity transcript (Swift/C# JSON output) includes a
   content-addressable fingerprint proving which input vector produced which output. Enables
   independent verification that a transcript is genuine and complete.

## Functional Requirements

### FR-1: Corpus Fingerprint

- Compute SHA-256 over all files in `corpus/parity-vectors/` + `corpus/parity-vectors.manifest`
  in deterministic order (sorted by filename)
- Store in `corpus/parity-fingerprint.sha256`
- Fingerprint file is committed to git alongside the corpus
- Parity gates (`cross-platform-parity-gate.py`, `parity-local-gate.sh`) verify fingerprint
  before running — exit 2 if mismatch (tampered corpus)
- Regenerate fingerprint when corpus changes (new vectors, manifest update)

### FR-2: Transcript Fingerprint

- Each transcript JSON includes a `fingerprint` field at the top level
- Fingerprint = SHA-256 of: `caseCount || vector_fingerprints || records_fingerprint`
- Each vector fingerprint = SHA-256 of the vector's JSON input (canonical serialization)
- Records fingerprint = SHA-256 of all `records[].conclusion` sorted by `caseFile`
- Implemented in both the Swift and C# transcript emitters
- Python `parity-local-gate.sh` verifies transcript fingerprints match across platforms

### FR-3: Fingerprint Verification

- `cross-platform-parity-gate.py`: verify corpus fingerprint before running
- `parity-local-gate.sh`: verify corpus fingerprint + compare transcript fingerprints
- `parity-transcript` CI job: verify transcript fingerprints
- Verification failure is exit 2 (cannot run) for corpus mismatch, exit 1 (gate failed)
  for transcript mismatch

### FR-4: Fingerprint Regeneration

- Script: `scripts/regenerate-parity-fingerprint.sh` — recomputes corpus fingerprint
- Script: `scripts/verify-parity-fingerprint.sh` — verifies corpus fingerprint
- Both scripts are idempotent and deterministic

## Acceptance Criteria

- [ ] `corpus/parity-fingerprint.sha256` exists and is committed
- [ ] `cross-platform-parity-gate.py` verifies corpus fingerprint before running
- [ ] `parity-local-gate.sh` verifies corpus fingerprint + transcript fingerprints
- [ ] Swift transcript emitter includes `fingerprint` in output
- [ ] C# transcript emitter includes `fingerprint` in output
- [ ] Fingerprint mismatch causes exit 2 (corpus) or exit 1 (transcript)
- [ ] `scripts/regenerate-parity-fingerprint.sh` produces deterministic output
- [ ] `scripts/verify-parity-fingerprint.sh` exits 0 on matching fingerprint
- [ ] `python3 scripts/cross-platform-parity-gate.py` exits 0
- [ ] All existing tests pass

## Out of Scope

- Fingerprinting behavioral vectors (separate corpus)
- Fingerprinting witness cross-platform vectors
- Signing fingerprints (Ed25519) — this is a separate cryptographic trust feature
- Fingerprinting individual source files
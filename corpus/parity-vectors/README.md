# Parity Vectors — Cross-Platform Corpus

11 vectors covering all 6 Syxx sections plus the fail-closed boundaries (unfetchable,
unverifiable, degenerate). Each vector is a single JSON file following the
`docs/parity-vector-v1-spec.md` contract.

## Index

| # | Vector | What it covers |
|---|--------|---------------|
| 001 | `empty-section-inputs` | All six sections present, all empty — the null case |
| 002 | `prophet-dnssec-unsigned` | Prophet with unsigned DNSSEC |
| 003 | `mirror-authoritative-matches` | Mirror authoritative resolution matches |
| 004 | `portal-encrypted-transport-working` | Portal encrypted transport |
| 005 | `glutton-ecs-forwarded` | Glutton ECS forwarding |
| 006 | `cage-attribution-and-size` | Cage attribution and response size |
| 007 | `witness-config-changes` | Witness configuration changes |
| 008 | `prophet-resolution-success` | Prophet resolution success path |
| 009 | `unfetchable-resolution-timeout` | Fail-closed: resolution timeout |
| 010 | `unverifiable-dnssec-inconclusive` | Fail-closed: DNSSEC inconclusive |
| 011 | `degenerate-present-but-uninformative` | Fail-closed: present but uninformative |

## Fingerprint

### Corpus fingerprint

`corpus/parity-fingerprint.sha256` is a SHA-256 hash computed over all vector files
plus `corpus/parity-vectors.manifest` in deterministic order (sorted by filename,
`LC_ALL=C`). It is committed alongside the corpus and verified by every parity gate
before running.

**Regenerate** (after adding or modifying vectors):
```bash
bash scripts/regenerate-parity-fingerprint.sh
```

**Verify** (checks the fingerprint matches the current corpus):
```bash
bash scripts/verify-parity-fingerprint.sh
```
Exit 0 on match, 2 on mismatch.

**How it is used:**
- `scripts/cross-platform-parity-gate.py` — verifies fingerprint before any comparison;
  exits 2 on mismatch (tampered corpus, cannot run)
- `scripts/parity-local-gate.sh` — same; exits 2 before any transcript work
- CI `parity` job — runs `cross-platform-parity-gate.py` which includes the check

### Transcript fingerprint

Each parity transcript JSON (emitted by both the Swift and C# `parity-transcript`
commands) carries a top-level `fingerprint` field:

```json
{
  "fingerprint": "a7a24aaebee0fa6d86dd2240059915eca13e28568b26ebf5ef1c9cba1919791f",
  "caseCount": 11,
  "evaluated": 11,
  "records": [...]
}
```

The fingerprint is computed as:
```
SHA-256(caseCount || vector_fingerprints || records_fingerprint)
```

Where:
- `vector_fingerprints` = SHA-256 of each vector's canonical JSON input, concatenated in order
- `records_fingerprint` = SHA-256 of all `records[].conclusion` sorted by `caseFile`

Both the Swift and C# emitters produce identical fingerprints for the same input.
The CI `parity-transcript` job verifies all four runtime transcripts carry the same
fingerprint, and `parity-local-gate.sh` assertion 5a compares them.

## Manifest

`corpus/parity-vectors.manifest` declares `caseCount=<N>` on its first line followed by
the sorted vector IDs. It is the one expectation stored OUTSIDE the corpus — a case
deletion cannot silently update it. Both loaders' `--list-only` output must match the
manifest byte-for-byte.

Regenerate the manifest after adding or removing vectors:
```bash
swift run --package-path macOS syxx parity-transcript --vectors=corpus/parity-vectors --list-only > corpus/parity-vectors.manifest
```

## Format

Each vector file follows `docs/parity-vector-v1-spec.md`. Key rules:
- One JSON file per case, named `NNN_description.json`
- `vectorId` + `SectionInputs` payload
- Malformed vectors must cause exit non-zero with the case named — never silently skipped
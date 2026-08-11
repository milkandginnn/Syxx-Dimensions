---
name: delta-clean
description: Remove remediated deltas from the active catalog
metadata:
  user-invocable: true
  disable-model-invocation: true
  argument-hint: '[--schedule YYYY-MM-DD HH-MM-SS] [--repeat]'
---

# Delta Clean

Move verified deltas from the active catalog to an archived ledger. Rewrite DELTAS.md to contain only open and deferred deltas.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. Verify `DELTAS.md` exists. If missing, error: "No delta catalog found."

3. Verify `.deltas/implementation-log.json` exists. If missing, proceed with only `DELTAS.md` as source (clean any delta with a verified status).

4. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

## Phase 1: Identify Eligible Deltas

Read `DELTAS.md`. Identify deltas eligible for cleaning:

| Status | Eligible? |
|---|---|
| `open` | No — remains in catalog |
| `fixed-unverified` | No — must be verified first |
| `verified-by-CI` | **Yes** |
| `verified-by-real-run` | **Yes** |
| `deliberately-out` | **Yes** |
| `deferred` | No — remains in catalog |

## Phase 2: Archive

Create `.deltas/archive/deltas-archived-YYYYMMDD.md` containing all eligible deltas, preserving their full descriptions, IDs, fix history, and verification evidence.

## Phase 3: Rewrite Catalog

Rewrite `DELTAS.md` to contain only:
- Header
- Open deltas (status: `open`)
- Deferred deltas (status: `deferred`)
- Fixed-but-unverified deltas (status: `fixed-unverified`)

Regenerate sequential delta IDs. Maintain a cross-reference table mapping old IDs to new IDs.

## Phase 4: Summary

Record the cleaning operation in `.deltas/clean-log.json`:

```json
{
  "timestamp": "ISO_TIMESTAMP",
  "cleaned_count": 15,
  "archived_to": ".deltas/archive/deltas-archived-20260809.md",
  "remaining_count": 27,
  "id_mappings": { "D042": "D027", "D015": "D028" }
}
```

## Output

- Updated `DELTAS.md` (open + deferred + fixed-unverified only)
- `.deltas/archive/deltas-archived-YYYYMMDD.md` (verified deltas)
- `.deltas/clean-log.json`

## Contract

- Cleaning is non-destructive. Archived deltas are preserved, never deleted.
- Only verified deltas are eligible for cleaning.
- `fixed-unverified` deltas are NOT cleaned — they must pass `/delta-review` first.
- ID regeneration is deterministic. Two runs produce the same new IDs.

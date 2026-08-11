---
description: "Remove remediated deltas from the active catalog"
argument-hint: "[--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Clean

Move verified deltas from the active catalog to an archived ledger. Rewrite DELTAS.md to contain only open and deferred deltas.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--help`: Display help information and exit without executing
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. If `--help` is set, display the following and exit:

---

### `/delta-remediation` — Delta Remediation Suite

A thorough, multi-agent debugging and defect remediation workflow for large codebases. The suite discovers defects through parallel specialized agents, scores them by severity and impact, records human decisions about what to fix, generates phased implementation plans, executes fixes with strict file ownership, verifies the fixes through independent review, and archives completed work.

### Command Pipeline (intended order)

```
1. /delta-analysis    — Discover defects (reads code, writes DELTAS.md)
2. /delta-evaluation  — Score and classify deltas (reads DELTAS.md, writes .deltas/evaluation.json)
3. /delta-decide      — Human decides what to fix (reads evaluation, writes .deltas/decisions.json)
4. /delta-plan        — Generate implementation plan (reads decisions, writes .deltas/plan.md)
5. /delta-implement   — Execute fixes phase by phase (reads plan, modifies code, updates DELTAS.md)
6. /delta-review      — Verify fixes through independent review (reads code + DELTAS.md, writes .deltas/review-report.md)
7. /delta-clean       — Archive verified deltas (rewrites DELTAS.md, archives to .deltas/archive/)
```

### `/delta-clean` — Archive Verified Deltas

Move verified deltas from the active catalog to an archived ledger. Rewrite `DELTAS.md` to contain only open, deferred, and fixed-but-unverified deltas. Regenerate sequential delta IDs. This is the final step in the remediation cycle — it shrinks the catalog back to a manageable size.

**Prerequisite:** `DELTAS.md` must exist.

**Flags and arguments:**

| Flag | Description | Default | Example |
|---|---|---|---|
| `--help` | Display this help and exit | — | `/delta-clean --help` |
| `--schedule <datetime>` | Run at a future time | Now | `/delta-clean --schedule 2026-08-15 09-00-00` |
| `--repeat` | Recurring schedule | Once | `/delta-clean --schedule 2026-08-15 09-00-00 --repeat` |

**Eligible for archiving:**

| Status | Eligible? |
|---|---|
| `open` | No — remains in catalog |
| `fixed-unverified` | No — must pass `/delta-review` first |
| `verified-by-CI` | **Yes** |
| `verified-by-real-run` | **Yes** |
| `deliberately-out` | **Yes** |
| `deferred` | No — remains in catalog |

**Output:**
- Updated `DELTAS.md` (open + deferred + fixed-unverified only, IDs regenerated)
- `.deltas/archive/deltas-archived-YYYYMMDD.md` (verified deltas preserved)
- `.deltas/clean-log.json` (cleaning operation record with old→new ID mappings)

---

3. Verify `DELTAS.md` exists. If missing, error: "No delta catalog found."

4. Verify `.deltas/implementation-log.json` exists. If missing, proceed with only `DELTAS.md` as source (clean any delta with a verified status).

5. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

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
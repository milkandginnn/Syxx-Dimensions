---
description: "Verify that fixed deltas were remediated correctly and completely"
argument-hint: "[--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Review

Deploy a multi-reviewer team to audit implemented fixes. Verify correctness, check for regressions, and confirm completeness. Produce a consolidated review report with pass/fail per delta.

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

### `/delta-review` — Verify Fixes

Deploy a multi-reviewer team to independently audit every `fixed-unverified` delta. Reviewers have NOT seen the implementation plan — they form independent judgments from the code diff and delta descriptions. Each delta receives a verdict: PASS (promote to verified), PASS-WITH-NOTES (promote with concerns), FAIL (revert to open), or INCONCLUSIVE (needs manual review).

**Prerequisite:** `DELTAS.md` must contain `fixed-unverified` deltas, and `.deltas/implementation-log.json` must exist (run `/delta-implement` first).

**Flags and arguments:**

| Flag | Description | Default | Example |
|---|---|---|---|
| `--help` | Display this help and exit | — | `/delta-review --help` |
| `--schedule <datetime>` | Run at a future time | Now | `/delta-review --schedule 2026-08-15 09-00-00` |
| `--repeat` | Recurring schedule | Once | `/delta-review --schedule 2026-08-15 09-00-00 --repeat` |

**Reviewers (3 agents, deployed in parallel):**

| Reviewer | Dimension | Checks |
|---|---|---|
| Code Reviewer | Correctness | Does the fix close the defect? Is the logic sound? |
| Code Reviewer | Regression | Did the fix break anything? Are tests still passing? |
| Code Reviewer | Completeness | Are all selected deltas genuinely fixed? Any partial fixes? |

**Verdicts:**

| Verdict | Condition | Promotion |
|---|---|---|
| PASS | All reviewers agree: closed, no regressions, minimal fix | → `verified-by-CI` or `verified-by-real-run` |
| PASS-WITH-NOTES | Closed but fix not minimal or minor concerns | → `verified-by-CI` with notes |
| FAIL | Not closed, or fix introduced regression | → Revert to `open` |
| INCONCLUSIVE | Reviewers disagree | → Stay `fixed-unverified` |

**Output:** `.deltas/review-report.md`

---

3. Verify `DELTAS.md` exists and contains `fixed-unverified` deltas. If none, error: "No unverified fixes to review."

4. Verify `.deltas/implementation-log.json` exists. If missing, error: "No implementation log. Run /delta-implement first."

5. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

## Phase 1: Scope Resolution

1. Read `.deltas/implementation-log.json` to identify which deltas were fixed and in which commits.
2. Collect the git diff for the implementation commits.
3. Identify only deltas with status `fixed-unverified`.

## Phase 2: Multi-Reviewer Deployment

Deploy reviewers in parallel, one per dimension:

| Reviewer | Dimension | Checks |
|---|---|---|
| `comprehensive-review__code-reviewer` | Correctness | Does the fix close the defect? Is the logic sound? |
| `comprehensive-review__code-reviewer` | Regression | Did the fix break anything? Are existing tests still passing? |
| `comprehensive-review__code-reviewer` | Completeness | Are all selected deltas genuinely fixed? Any partial fixes? |

Each reviewer receives:
- The full git diff of implementation commits
- The delta descriptions from DELTAS.md
- The relevant source files

**CRITICAL: Reviewers have NOT seen the implementation plan. They form independent judgments from the code and delta descriptions.**

## Phase 3: Per-Delta Assessment

For each `fixed-unverified` delta, each reviewer answers:

1. **Is the defect actually closed?** (Yes/No/Partial)
2. **Did the fix introduce new issues?** (Yes/No — with evidence)
3. **Is the fix minimal and correct?** (Yes/No — the fix should be the smallest change that resolves the defect)

## Phase 4: Consolidation

Merge reviewer assessments into a per-delta verdict:

| Verdict | Condition |
|---|---|
| **PASS** | All reviewers agree: defect closed, no regressions, fix is minimal |
| **PASS-WITH-NOTES** | Defect closed, but fix is not minimal or has minor concerns |
| **FAIL** | Defect not closed, or fix introduced a regression |
| **INCONCLUSIVE** | Reviewers disagree or cannot determine |

## Phase 5: Promotion

Based on the consolidated verdict:
- **PASS** → Promote delta to `verified-by-CI` (if CI ran) or `verified-by-real-run`
- **PASS-WITH-NOTES** → Promote to `verified-by-CI` with notes
- **FAIL** → Revert to `open` with review findings
- **INCONCLUSIVE** → Leave as `fixed-unverified`, request manual review

## Output

`.deltas/review-report.md`:

```markdown
# Delta Review Report

**Reviewed:** ISO_TIMESTAMP
**Reviewers:** 3 agents (correctness, regression, completeness)

## Summary
- Reviewed: 8 deltas
- PASS: 6
- PASS-WITH-NOTES: 1
- FAIL: 1
- INCONCLUSIVE: 0

## Per-Delta Results

### D042 — PASS
- **Correctness:** Defect closed. Empty HashSet replaced with ConfiguredPins().
- **Regression:** No regressions. All existing tests pass.
- **Completeness:** Fix is minimal (one line). No partial fix.
- **Promotion:** fixed-unverified → verified-by-CI

### D023 — FAIL
- **Correctness:** Defect NOT closed. Encoder changed to Default, but Default over-escapes 6 HTML characters. The fix introduced a new divergence.
- **Regression:** Fingerprint mismatch detected. Parity-local-gate fails.
- **Completeness:** Fix is incomplete — needs UnsafeRelaxedJsonEscaping + non-ASCII post-processing.
- **Promotion:** Reverted to open
```

## Contract

- Review is independent of implementation. Reviewers have not seen the plan.
- Reviewers form their own judgments from code and delta descriptions.
- A FAIL verdict reverts the delta to `open`. The fix must be re-planned and re-implemented.
- Promotion is gated on unanimous or majority agreement among reviewers.
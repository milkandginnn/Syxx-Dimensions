---
description: Verify that fixed deltas were remediated correctly and completely
argument-hint: "[--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Review

Deploy multi-reviewer team (correctness, regression, completeness). Assess each `fixed-unverified` delta. Promote to `verified-by-CI` or revert to `open`. Output `.deltas/review-report.md`.

## Pre-flight

Verify `DELTAS.md` has `fixed-unverified` deltas. Verify `.deltas/implementation-log.json` exists.

## Contract

Reviewers are independent — they have not seen the plan. FAIL reverts to `open`.
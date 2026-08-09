---
description: Score and classify discovered deltas for remediation planning
argument-hint: "[--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Evaluation

Read `DELTAS.md` and assign each delta a composite score (severity × exploitability × fix complexity × blast radius). Group into tiers: MUST FIX (≥0.70), SHOULD FIX (0.40–0.69), COULD FIX (0.15–0.39), WON'T FIX (<0.15). Detect dependency chains. Output `.deltas/evaluation.json`.

## Pre-flight

Verify `DELTAS.md` exists. Filter by `--domain`, `--platform`, `--branch` if specified.

## Contract

Evaluation is mechanical and reproducible. Two runs produce the same scores.
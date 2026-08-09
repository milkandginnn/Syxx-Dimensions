---
description: Generate an implementation plan from decided deltas
argument-hint: "[--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Plan

Group deltas by file ownership and dependency order. Produce phased plan where no two phases touch the same file. Estimate effort. Output `.deltas/plan.md`.

## Pre-flight

Verify `.deltas/decisions.json` exists. Filter by flags.

## Contract

Plan must be executable by parallel agents. Each phase has a verification gate.
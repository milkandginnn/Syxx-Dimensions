---
description: Discover defects through multi-agent codebase analysis
argument-hint: "[--target-number N] [--deep] [--deepest] [--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Analysis

Deploy a team of specialized agents to audit the codebase for defects across predefined dimensions (security, correctness, parity, performance, observability). Each agent produces structured findings with file:line citations, severity ratings, and concrete failure scenarios. Findings are deduplicated and merged into `DELTAS.md`.

## Pre-flight

Parse arguments: `--target-number N` (stop after N deltas), `--deep` (cross-file tracing), `--deepest` (line-by-line), `--domain <letter>` (filter by DELTAS.md domain), `--platform <name>` (filter by platform), `--branch <name>` (filter by branch), `--schedule` (cron), `--repeat` (recurring).

## Phases

1. **Scope Resolution**: Determine files to analyze from branch/platform flags.
2. **Dimension Assignment**: Deploy agents in parallel — security, correctness, parity, performance, observability.
3. **Collection**: Deduplicate findings, assign unique IDs, write to `DELTAS.md`.
4. **Target Check**: If `--target-number` set, repeat until reached.

## Contract

Analysis is read-only. Findings are evidence-cited. No code is modified.
---
name: delta-analysis
description: Discover defects through multi-agent codebase analysis
metadata:
  user-invocable: true
  disable-model-invocation: true
  argument-hint: >-
    [--target-number N] [--deep] [--deepest] [--domain <letter>] [--platform
    <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]
---

# Delta Analysis

Deploy a team of specialized agents to audit the codebase for defects across predefined dimensions. Each agent produces structured findings with file:line citations, severity ratings, and concrete failure scenarios. Findings are deduplicated and merged into a consolidated delta catalog.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--target-number N`: Stop after N deltas found (default: unlimited)
   - `--deep`: Deeper analysis with cross-file tracing
   - `--deepest`: Line-by-line analysis of the entire codebase
   - `--domain <letter>`: Filter to specific DELTAS.md domain (e.g., S=Security, C=Correctness, P=Parity)
   - `--platform <name>`: Filter to specific platform (macOS, Windows, Linux, Server)
   - `--branch <name>`: Analyze only within a specific branch
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule (requires --schedule)

2. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

3. Check if `DELTAS.md` exists. If not, initialize with header.

## Phase 1: Scope Resolution

Determine the codebase scope:
- If no `--branch` flag: current working tree
- If `--branch <name>`: diff against that branch
- If `--platform <name>`: limit to platform-specific directories

## Phase 2: Dimension Assignment

Deploy agents in parallel, one per analysis dimension:

| Dimension | Agent | Focus |
|---|---|---|
| Security (S) | `security-scanning__security-auditor` | OWASP, injection, auth, crypto, secrets |
| Correctness (C) | `comprehensive-review__code-reviewer` | Logic errors, race conditions, null handling |
| Parity (P) | `comprehensive-review__code-reviewer` | Cross-platform divergence, Swift/C# mismatch |
| Performance (R) | `application-performance__performance-engineer` | N+1, memory leaks, I/O bottlenecks |
| Observability (O) | `application-performance__observability-engineer` | Missing spans, telemetry gaps |

Each agent receives the scope and dimension-specific checklist. Agents are read-only.

## Phase 3: Collection and Deduplication

1. Wait for all agents to complete.
2. Collect findings from each agent.
3. Deduplicate: merge findings that reference the same file:line and defect type.
4. Assign unique delta IDs (e.g., `D001`, `S015`, `C042`).
5. Write to `DELTAS.md`.

## Phase 4: Target Check

If `--target-number N` is set and fewer than N deltas were found:
- Deploy additional agents with different heuristic approaches
- Repeat until target reached or no new deltas found

## Output

`DELTAS.md` is written/updated with each delta in format:

```
## D{NNN} — {severity} — {domain} — {file}:{line}
**Description:** {one-line defect statement}
**Failure scenario:** {concrete inputs/state → wrong outcome}
**Evidence:** {code citation}
**Platform:** {macOS|Windows|Linux|Server|All}
**Status:** open
```

## Contract

- Analysis is read-only. No code is modified.
- Findings are evidence-cited, never speculative.
- If a finding cannot be substantiated with file:line evidence, it is dropped.
- The `--deepest` flag guarantees every line of every file is read.

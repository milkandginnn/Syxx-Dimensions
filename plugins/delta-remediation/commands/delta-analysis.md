---
description: "Discover defects through multi-agent codebase analysis"
argument-hint: "[--target-number N] [--deep] [--deepest] [--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Analysis

Deploy a team of specialized agents to audit the codebase for defects across predefined dimensions. Each agent produces structured findings with file:line citations, severity ratings, and concrete failure scenarios. Findings are deduplicated and merged into a consolidated delta catalog.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--help`: Display help information and exit without executing
   - `--target-number N`: Stop after N deltas found (default: unlimited)
   - `--deep`: Deeper analysis with cross-file tracing
   - `--deepest`: Line-by-line analysis of the entire codebase
   - `--domain <letter>`: Filter to specific DELTAS.md domain (e.g., S=Security, C=Correctness, P=Parity)
   - `--platform <name>`: Filter to specific platform (macOS, Windows, Linux, Server)
   - `--branch <name>`: Analyze only within a specific branch
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule (requires --schedule)

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

### `/delta-analysis` — Discover Defects

Deploy a team of specialized agents to audit the codebase for defects across five dimensions: Security, Correctness, Parity, Performance, and Observability. Each agent produces structured findings with file:line citations, severity ratings, and concrete failure scenarios. Findings are deduplicated and merged into a consolidated delta catalog (`DELTAS.md`).

**Flags and arguments:**

| Flag | Description | Default | Example |
|---|---|---|---|
| `--help` | Display this help and exit | — | `/delta-analysis --help` |
| `--target-number N` | Stop after finding N deltas | Unlimited | `/delta-analysis --target-number 50` |
| `--deep` | Deeper analysis with cross-file tracing | Off | `/delta-analysis --deep` |
| `--deepest` | Line-by-line analysis of every file | Off | `/delta-analysis --deepest` |
| `--domain <letter>` | Scan only one domain | All domains | `/delta-analysis --domain S` |
| `--platform <name>` | Scan only one platform | All platforms | `/delta-analysis --platform macOS` |
| `--branch <name>` | Diff against a specific branch | Working tree | `/delta-analysis --branch main` |
| `--schedule <datetime>` | Run at a future time | Now | `/delta-analysis --schedule 2026-08-15 09-00-00` |
| `--repeat` | Recurring schedule | Once | `/delta-analysis --schedule 2026-08-15 09-00-00 --repeat` |

**Domains:** A=Server, B=Detection Efficacy, C=Verdict & Evidence Integrity, D=Platform Integration & Parity, E=Security & Hardening, F=Miscellaneous

**Depth levels:**
- *(none)* — Standard scan: one agent per dimension, focused on high-impact areas
- `--deep` — Cross-file tracing: agents trace data flows across module boundaries
- `--deepest` — Exhaustive: every source file read line-by-line

**Output:** `DELTAS.md` (created if missing, appended if exists)

---

3. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

4. Check if `DELTAS.md` exists. If not, initialize with header.

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
---
description: Execute the remediation plan, fixing deltas phase by phase
argument-hint: "[--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat] [--phase N]"
---

# Delta Implement

Execute `.deltas/plan.md` phase by phase. Deploy implementer agents with strict file ownership. Mark deltas `fixed-unverified`. Run verification gates. Output `.deltas/implementation-log.json`.

## Pre-flight

Verify `.deltas/plan.md` exists. Resume from last incomplete phase if log exists.

## Contract

No delta is marked `fixed`. Verification gates are mandatory. User approval required between phases.
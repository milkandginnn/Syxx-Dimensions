---
description: Make explicit decisions about which deltas to remediate
argument-hint: "[--auto] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Decide

Present evaluated deltas for human decision. Options: FIX NOW, FIX NEXT, DEFER, CLOSE (with reason), or AUTO. Record decisions in `.deltas/decisions.json`. Produce scoped worklist.

## Pre-flight

Verify `.deltas/evaluation.json` exists. If `--auto`, apply model decisions (MUST→NOW, SHOULD→NEXT, COULD→DEFER, WON'T→CLOSE).

## Contract

Decisions are human-made unless `--auto`. Agent recommends but does not decide.
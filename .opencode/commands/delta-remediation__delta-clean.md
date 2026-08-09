---
description: Remove remediated deltas from the active catalog
argument-hint: "[--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Clean

Move verified deltas to `.deltas/archive/`. Rewrite `DELTAS.md` with only open, deferred, and fixed-unverified deltas. Regenerate IDs. Output `.deltas/clean-log.json`.

## Pre-flight

Verify `DELTAS.md` exists.

## Contract

Cleaning is non-destructive. Only verified deltas are eligible. Archived deltas are preserved.
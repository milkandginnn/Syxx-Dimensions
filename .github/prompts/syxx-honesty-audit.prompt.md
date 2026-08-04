---
description: Audit a Syxx verdict surface (CLI report, GUI view, or synthesis output) against the Verification & Honesty Protocol before it ships.
mode: agent
---

Audit the verdict-bearing code I point you at (or the current diff) against Syxx's
honesty spine. Run **every** item; report each as **PASS / FAIL / INCONCLUSIVE**
with a `file:line` anchor and the adversarial condition that would trigger a
failure. Never soften a reasoning defect into a UX nit.

1. **No verdict before evidence.** Trace back from the rendered verdict/confidence
   to where its inputs are queried, parsed, and validated. If any path can render
   before its evidence exists → FAIL.
2. **Missing data is not clean data.** For each input, confirm missing / invalid /
   contradictory / degraded / timed-out / `nil` forces inconclusive/unavailable —
   never a silent clean/secure/success. Check the no-data and partial-data regimes.
3. **Tri-state preserved end to end.** No `Bool` collapses a
   secure/tampered/inconclusive (or success/failure/unknown) distinction between
   synthesis and the surface. Flag any dampener/negative weight that can zero a
   confirmed tampering signal.
4. **No fabricated/hardcoded value shown as measured.** Every user-visible
   number/label traces to live evidence — not a placeholder, default, or mock.
   (Historical failure: "Success: packet capture started… 0 queries captured…
   Verdict: 31% inconclusive.")
5. **Colour/label matches state.** No green/success styling for a degraded/
   inconclusive/unmeasured verdict; `isPro`/gating actually fires.
6. **Three honesty lines distinct** (if present): hard-gate vs conflict vs
   uncertainty — never collapsed into a bare "inconclusive." Time anchor
   (`generatedAt`) present on rendered reports.
7. **Platform parity.** The macOS surface and its `Syxx.Kit`/WinUI twin render the
   same verdict/confidence for identical evidence.

**Output:** a checklist table (item → PASS/FAIL/INCONCLUSIVE → `file:line` →
trigger). For each FAIL, a concrete fix direction and, if asked, a delta in
`DELTAS.md` house format (implementation is the engine/executor lane; wording
changes are proposals pending approval). The bar is a real run showing honest
output — not green tests; if you couldn't run the surface, mark items INCONCLUSIVE
and say what run resolves them.

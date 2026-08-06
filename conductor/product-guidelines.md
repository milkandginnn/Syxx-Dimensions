# Product Guidelines

## Voice — one register, everywhere

The same voice governs engineering docs, commit messages, and user-facing strings. It is not two
styles for two audiences; a non-specialist deserves the same accuracy an engineer gets, expressed in
words they already know.

**The register:**

- **State the mechanism, not just the outcome.** "The `.deb` shipped only the GUI" is worth more
  than "packaging was broken."
- **Refuse reassuring words that outrun the evidence.** `Linux/PARITY-LEDGER.md` deliberately makes
  "fixed" unavailable as a state, because it was the word that let packaging defects survive: a fix
  was real, nothing had executed it, and no one could tell the difference.
- **Name things individually; do not count them.** "~12 pre-existing failures" hid four distinct
  real defects. Naming them surfaced that three had unrelated root causes.
- **Say what was not measured.** An unmeasured check must never render like a passed one.
- **Correct in place, plainly.** When a claim was overstated, the correction is its own commit and
  says so. Several commits in this repo exist for exactly that; they are the standard, not an
  embarrassment.
- **Plain words over jargon**, given the primary user. But never reach for a softer word that
  changes what is true.

## User-facing strings

Exact UI copy is **not** authored from this document. It is catalogued in:

- `outputs-catalog-general-mode.csv`
- `outputs-catalog-story-mode.csv`

Both are **append-only and never reordered** — row order is referenced elsewhere. To change a
string, append; do not edit in place unless the change is a correction to an inaccuracy, and say so
in the commit.

## Design principles

1. **The conclusion is the product.** Everything else is evidence-gathering in service of one honest
   statement.
2. **Safe by default.** A user who tunes nothing gets the same strength of check as one who tunes
   everything.
3. **Evidence is exportable.** A result the user cannot hand to someone else, or re-check
   themselves, is weaker than one they can.
4. **Degrade, do not guess.** When a measurement is unavailable, lower confidence and say why. Never
   substitute an inference for the measurement and present it at the same strength.
5. **One trusted implementation beats three agreeing ones.** Where byte-parity across languages is
   required and hard, move the work to a single authority and sign the result — the reasoning behind
   the Warp signed-manifest design.

## Banned language

One word is banned outright across prose, code, docs, and UI. It is named once, in `docs/CLAUDE.md`;
this document deliberately does not repeat it, because restating a ban is how the word stays alive in
the tree.

Use **result** for code and identifiers; **conclusion** for UI, modules, and documentation
(`Conclusion.swift` / `Conclusion.cs` are correct, and the synthesis pane is "Conclusions").

One pre-existing signed wire key under `evidence.comparisonResult.*` still carries the banned segment.
It cannot be renamed without breaking existing signatures, so leave it exactly as it is — and never
introduce the segment anywhere new.

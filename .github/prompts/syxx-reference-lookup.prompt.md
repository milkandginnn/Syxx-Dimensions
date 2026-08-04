---
description: Ground an answer in the Syxx reference library (extracted paper text) before reaching for external web search.
mode: agent
---

Answer my question by grounding it in the project's reference papers first, not
guesswork or the open web. The papers are pre-extracted to plain text under
`research/reference-library/` (Copilot reads text, not PDF bytes), with a manifest
at `research/reference-library/INDEX.md`.

Procedure:

1. **Scan the manifest** — read `research/reference-library/INDEX.md` for titles,
   page/word counts, and the library filename per paper.
2. **Grep for the concept**, e.g.
   `grep -ril "dempster\|dnssec\|beacon\|rpn\|captive portal" research/reference-library`.
   Match several papers; don't stop at the first.
3. **Read the matching `.txt`** and pull the load-bearing passage. Each file opens
   with a `# Source:` header naming the original PDF.
4. **Cite the original PDF**, not the extracted `.txt` (e.g.
   "ndss2019_04B-4_Klein_paper.pdf — DNS Cache-Based User Tracking"), with section/
   page when possible.
5. **Flag extraction damage** — tables, math, and multi-column layout can be
   garbled; if a passage is broken, say so rather than quoting a mangled line.

Output: the answer with each nontrivial claim tied to a library paper. If the
library does **not** cover the question, say so explicitly — then it's fair to use
web search or `syxx-neural-researcher`. Never launder an unsupported claim as
cited; "the library doesn't address this" is a valid answer.

The library is rebuilt by `python3 scripts/build-reference-library.py --source
~/Downloads` (the `.webm` talk is not text-extracted).

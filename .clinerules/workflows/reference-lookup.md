# Reference lookup

Ground an answer in the Syxx reference library before reaching for external web
search. The papers are pre-extracted to plain text under
`research/reference-library/` (Cline reads text, not PDF bytes); the manifest is
`research/reference-library/INDEX.md`.

Steps:

1. Read `research/reference-library/INDEX.md` — titles, page/word counts, library
   filenames.
2. Grep the folder for the concept, e.g.
   `grep -ril "dempster\|dnssec\|beacon\|quorum\|captive portal" research/reference-library`.
   Match several papers; don't stop at the first.
3. Open the matching `.txt`; pull the load-bearing passage. Each file opens with a
   `# Source:` header naming the original PDF.
4. Cite the original PDF (e.g. "ndss2019_04B-4_Klein_paper.pdf — DNS Cache-Based
   User Tracking"), with section/page when possible — not the `.txt`.
5. Flag extraction damage (tables/math/multi-column can be garbled); don't quote a
   mangled line.

If the library doesn't cover the question, say so explicitly, then use web search
or the research mode. Never launder an unsupported claim as cited.

Rebuild with `python3 scripts/build-reference-library.py --source ~/Downloads`
(the `.webm` talk is not text-extracted).

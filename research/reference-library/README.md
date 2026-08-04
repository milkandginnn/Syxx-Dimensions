# Syxx reference library

Machine-extracted **plain text** of the project's reference papers, so text-only
agents can actually read them. Big Pickle (OpenCode Zen) and opencode's
`read`/`grep` tools consume text, not PDF bytes — a `.pdf` in `~/Downloads` is
invisible to them, but the `.txt` here is fully searchable.

## How agents should use it

1. **Grep the library before external search.** `INDEX.md` lists every paper with
   a best-guess title, page count, and word count. Grep the folder for a concept
   (`grep -ril "dempster\|dnssec strip\|beacon" research/reference-library`), then
   open the matching `.txt`.
2. **Cite the original.** Each `.txt` starts with a `# Source:` header naming the
   original PDF. Quote the paper by that filename, not the extracted `.txt`.
3. **Trust but verify the text.** Extraction is mechanical — column breaks,
   tables, figures, and math may be garbled. If a passage looks broken, note it;
   don't build a load-bearing claim on a mangled line.

The `syxx-reference-lookup` skill (`.opencode/skills/`) encodes this so any agent
picks it up automatically.

## Refreshing / adding papers

Populated by `scripts/build-reference-library.py`. To re-extract or add sources:

```bash
# add the new filename to the SOURCES list in the script, then:
python3 scripts/build-reference-library.py --source ~/Downloads
python3 scripts/build-reference-library.py --list      # dry run / check what's found
```

The script tries PyMuPDF, then the `pdftotext` CLI (Poppler), then pdfminer.six —
install any one if extraction is skipped. It's idempotent and regenerates
`INDEX.md`.

## Curator notes (this batch)

- **`p213-sherry.webm`** is a ~207 MB talk **video** — not text-extractable here
  (no speech-to-text engine in the extraction environment). Transcribe it
  separately (e.g. Whisper) if you want it searchable.
- **`ieee-xplore-full-text-pdf.txt`** and **`ieee-xplore-full-text-pdfyui.txt`**
  are single **abstract/first pages** of papers whose fuller versions are also
  here (`detecting_network_scanning_*` and `summary_of_dns_over_https_abuse*`) —
  prefer the full versions.
- **`summary_of_dns_over_https_abuse.txt`** and **`…-2.txt`** are duplicates of the
  same document.
- **`export.csv`** is the source bibliography/metadata export, copied verbatim.

## In-repo vs. external

This library lives inside the repo, so opencode and any IDE can read it with no
extra configuration. If you'd rather keep the bulk out of git, move the folder
outside the project and register it as an opencode **reference** (see
`.opencode/opencode.jsonc.sample`); agents get the same access via the
`external_directory` permission.

#!/usr/bin/env python3
"""
build-reference-library.py — make Syxx's reference PDFs usable by text-only
agents (Big Pickle via OpenCode Zen, opencode's read/grep tools, most IDEs).

Big Pickle and opencode do NOT ingest raw PDF bytes. This script extracts each
source PDF to a plain-text file under `research/reference-library/`, copies
tabular/data sources as-is, records media (e.g. .webm) it can't transcribe, and
regenerates `INDEX.md` — a manifest the agents grep first (see the
`syxx-reference-lookup` skill and the library README).

Run it on YOUR machine (the files live in ~/Downloads, which the assistant's
sandbox can't reach):

    python3 scripts/build-reference-library.py            # extract everything
    python3 scripts/build-reference-library.py --list     # dry run, just list
    python3 scripts/build-reference-library.py --source ~/Downloads --dest research/reference-library

Text extraction tries, in order: PyMuPDF (`pip install pymupdf`), pdfminer.six
(`pip install pdfminer.six`), then the `pdftotext` CLI (Poppler:
`brew install poppler`). Install any one of them if extraction is skipped.

Idempotent: re-running refreshes text and the index. Safe to run after adding
files to SOURCES.
"""

from __future__ import annotations

import argparse
import csv
import datetime as _dt
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

# ---- Source files (edit this list to add/remove references) ----------------
# Paths may use ~ and are resolved against --source only when given as bare
# filenames; absolute paths are used as-is.
SOURCES = [
    "computers-14-00375.pdf",
    "s10922-025-09911-6.pdf",
    "3811822.pdf",
    "3759425.3763377.pdf",
    "preprint-fmea-classification-first_en_v0-16.pdf",
    "December-2025-Vol-14-No-2_JD_2504.pdf",
    "s41598-025-11929-4.pdf",
    "IEEE Xplore Full-Text PDFyui.pdf",
    "IEEE Xplore Full-Text PDF.pdf",
    "Detecting_Network_Scanning_Through_Monitoring_and_Manipulation_of_DNS_Traffic.pdf",
    "Summary_of_DNS_Over_HTTPS_Abuse-2.pdf",
    "Summary_of_DNS_Over_HTTPS_Abuse.pdf",
    "woot25-chen.pdf",
    "2605.26563v2.pdf",
    "How_to_be_unhackable.pdf",
    "s42400-019-0038-7.pdf",
    "futureinternet-17-00172-v2.pdf",
    "s41597-026-06738-x.pdf",
    "sensors-25-01153-v3.pdf",
    "ssrn-5289824.pdf",
    "2501.13004v1.pdf",
    "CJDS-22-1-16.pdf",
    "3719027.3765205.pdf",
    "NETFALCON_AI-Based_Network_Traffic_Analyzer_with_T.pdf",
    "export.csv",
    "1629607.1629610.pdf",
    "tesi.pdf",
    "ndss2019_04B-4_Klein_paper.pdf",
    "DNS_Poisoning_of_Operating_System_Caches_Attacks_and_Mitigations.pdf",
    "3478472.3478481.pdf",
    "p213-sherry.webm",
]

MEDIA_EXTS = {".webm", ".mp4", ".mov", ".mp3", ".wav", ".m4a"}
DATA_EXTS = {".csv", ".tsv", ".json"}


def slugify(name: str) -> str:
    stem = Path(name).stem
    stem = re.sub(r"[^A-Za-z0-9._-]+", "-", stem).strip("-").lower()
    return stem or "file"


# ---- PDF text extraction (three fallbacks) ---------------------------------
def extract_pymupdf(path: Path) -> tuple[str, int, str] | None:
    try:
        import fitz  # PyMuPDF
    except Exception:
        return None
    doc = fitz.open(path)
    title = (doc.metadata or {}).get("title") or ""
    text = "\n".join(page.get_text("text") for page in doc)
    n = doc.page_count
    doc.close()
    return text, n, title.strip()


def extract_pdfminer(path: Path) -> tuple[str, int, str] | None:
    try:
        from pdfminer.high_level import extract_text
    except Exception:
        return None
    text = extract_text(str(path)) or ""
    n = text.count("\f") + 1
    return text, n, ""


def extract_pdftotext(path: Path) -> tuple[str, int, str] | None:
    if not shutil.which("pdftotext"):
        return None
    try:
        out = subprocess.run(
            ["pdftotext", "-enc", "UTF-8", str(path), "-"],
            capture_output=True, text=True, check=True,
        )
    except subprocess.CalledProcessError:
        return None
    text = out.stdout
    n = max(text.count("\f"), 1)  # pdftotext writes a form-feed after every page
    return text, n, ""


def extract_pdf(path: Path):
    # Order favors speed+reliability: PyMuPDF if present, else the pdftotext CLI
    # (Poppler — fast, clean), else pdfminer.six (pure-Python, slower).
    for fn in (extract_pymupdf, extract_pdftotext, extract_pdfminer):
        res = fn(path)
        if res is not None:
            return res
    return None


_TITLE_NOISE = re.compile(
    r"^(received\b|accepted\b|downloaded\b|digital object identifier|doi\b|"
    r"www\.|https?:|ieee\b|volume\b|vol\.|issn\b|arxiv:|preprint\b|©|copyright)",
    re.I,
)


def first_title(text: str, fallback: str) -> str:
    """Best-guess title: first substantial line that isn't journal/masthead noise."""
    for line in text.splitlines():
        s = line.strip()
        if len(s) < 8 or s.isdigit():
            continue
        if _TITLE_NOISE.search(s):
            continue
        # skip lines that are mostly digits/punctuation (dates, page furniture)
        letters = sum(c.isalpha() for c in s)
        if letters < max(6, len(s) // 2):
            continue
        return s[:160].strip()
    return fallback


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--source", default="~/Downloads", help="dir holding the source files (default: ~/Downloads)")
    ap.add_argument("--dest", default=None, help="output dir (default: <repo>/research/reference-library)")
    ap.add_argument("--list", action="store_true", help="dry run: list what would be processed, extract nothing")
    args = ap.parse_args()

    repo = Path(__file__).resolve().parent.parent
    source_dir = Path(os.path.expanduser(args.source))
    dest = Path(os.path.expanduser(args.dest)) if args.dest else repo / "research" / "reference-library"
    dest.mkdir(parents=True, exist_ok=True)

    rows = []
    missing, extracted, skipped = [], 0, []

    for name in SOURCES:
        p = Path(os.path.expanduser(name))
        src = p if p.is_absolute() else source_dir / name
        ext = src.suffix.lower()
        exists = src.exists()
        slug = slugify(name)

        if args.list:
            kind = "pdf" if ext == ".pdf" else ("data" if ext in DATA_EXTS else ("media" if ext in MEDIA_EXTS else "other"))
            print(f"[{'ok ' if exists else 'MISS'}] {kind:5} {name}")
            if not exists:
                missing.append(name)
            continue

        if not exists:
            missing.append(name)
            rows.append((name, "— (source not found)", "missing", "", ""))
            continue

        size_kb = f"{src.stat().st_size // 1024} KB"

        if ext in MEDIA_EXTS:
            skipped.append(name)
            rows.append((name, "(video/audio — not text-extractable)", "media", "", size_kb))
            continue

        if ext in DATA_EXTS:
            shutil.copy2(src, dest / src.name)
            note = ""
            if ext == ".csv":
                try:
                    with open(src, newline="", encoding="utf-8", errors="replace") as f:
                        n = sum(1 for _ in csv.reader(f))
                    note = f"{n} rows"
                except Exception:
                    pass
            rows.append((src.name, "(data file, copied verbatim)", "data", note, size_kb))
            continue

        if ext != ".pdf":
            shutil.copy2(src, dest / src.name)
            rows.append((src.name, "(copied verbatim)", "other", "", size_kb))
            continue

        res = extract_pdf(src)
        if res is None:
            skipped.append(name)
            rows.append((name, "(extraction failed — install pymupdf/pdfminer.six/poppler)", "pdf!", "", size_kb))
            continue

        text, pages, meta_title = res
        title = meta_title or first_title(text, Path(name).stem)
        words = len(text.split())
        out = dest / f"{slug}.txt"
        header = (
            f"# Source: {name}\n"
            f"# Title (best guess): {title}\n"
            f"# Pages: {pages} · Words: {words}\n"
            f"# Extracted: {_dt.date.today().isoformat()} by build-reference-library.py\n"
            f"# NOTE: machine-extracted text; layout/figures/tables may be imperfect.\n"
            f"{'-' * 72}\n\n"
        )
        out.write_text(header + text, encoding="utf-8")
        extracted += 1
        rows.append((f"{slug}.txt", title, "pdf", f"{pages}p · {words}w", size_kb))

    if args.list:
        print(f"\n{len(SOURCES)} sources listed · {len(missing)} missing from {source_dir}")
        return 0

    # ---- Regenerate INDEX.md -----------------------------------------------
    idx = dest / "INDEX.md"
    lines = [
        "# Syxx reference library — index",
        "",
        f"_Generated {_dt.datetime.now().isoformat(timespec='seconds')} by `scripts/build-reference-library.py`._",
        "",
        "Machine-extracted text of the Syxx reference papers, for agents (Big Pickle / "
        "opencode) that read text, not PDF bytes. **Grep this folder before reaching for "
        "external search.** Open the matching `.txt`; cite the original PDF filename in the "
        "`# Source:` header when you quote it.",
        "",
        "| # | Library file | Title (best guess) | Kind | Size/len |",
        "|---|---|---|---|---|",
    ]
    for i, (fname, title, kind, extent, size) in enumerate(rows, 1):
        t = title.replace("|", "\\|")
        lines.append(f"| {i} | `{fname}` | {t} | {kind} | {extent or size} |")
    lines += [
        "",
        f"**{extracted}** PDFs extracted · **{len(skipped)}** skipped (media/failed) · "
        f"**{len(missing)}** sources missing.",
    ]
    if missing:
        lines += ["", "> Missing from source dir (re-run with `--source` pointing at the right folder):", ""]
        lines += [f"> - `{m}`" for m in missing]
    idx.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"Done. {extracted} extracted, {len(skipped)} skipped, {len(missing)} missing.")
    print(f"Library: {dest}")
    print(f"Index:   {idx}")
    if skipped or missing:
        print("Some items need attention — see INDEX.md.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

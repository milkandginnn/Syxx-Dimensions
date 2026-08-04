#!/usr/bin/env python3
"""Regenerate outputs-catalog.html FROM outputs-catalog.csv.

The catalog is the governance record of every user-facing string. The CSV is the source of truth;
the HTML is a rendered view for reading/filtering. They had DRIFTED (CSV ahead of HTML, title stuck
at a stale count) because two files were hand-maintained in parallel — "add to the CSV" and "add to
the HTML" were separate steps and one got skipped. This makes the HTML a pure function of the CSV:
edit the CSV, run this, and they cannot diverge.

It preserves the existing HTML's <style>/<script>/chrome byte-for-byte (splitting on the <tbody>…
</tbody> boundary) and rebuilds only the rows and the entry count. So the look never changes; only
the data is refreshed.

Usage:  python3 scripts/build-outputs-catalog.py
"""
import csv
import html
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[1]
CSV = ROOT / "outputs-catalog.csv"
HTML = ROOT / "outputs-catalog.html"

_PILL = {"new": "c-new", "modified": "c-mod", "existing": "c-exist"}


def _row(fields):
    file_line, trigger, module, task, output, change = (fields + [""] * 6)[:6]
    cls = _PILL.get(change.strip().lower(), "c-exist")
    cells = "".join("<td>%s</td>" % html.escape(v) for v in
                    (file_line, trigger, module, task, output))
    pill = '<td><span class="pill %s">%s</span></td>' % (cls, html.escape(change))
    return "<tr>%s%s</tr>" % (cells, pill)


def main():
    with CSV.open(newline="", encoding="utf-8") as f:
        rows = list(csv.reader(f))
    if not rows:
        raise SystemExit("outputs-catalog.csv is empty")
    header, data = rows[0], rows[1:]
    if header[:6] != ["file:line", "trigger", "module", "task", "output", "change"]:
        raise SystemExit("unexpected CSV header: %r" % (header,))

    doc = HTML.read_text(encoding="utf-8")
    # Preserve everything up to <tbody> and from </tbody> onward — the style, script, and chrome are
    # kept exactly; only the rows between are regenerated.
    m_open = re.search(r"<tbody>", doc)
    m_close = re.search(r"</tbody>", doc)
    if not (m_open and m_close):
        raise SystemExit("could not find <tbody>…</tbody> in outputs-catalog.html")
    head = doc[:m_open.end()]
    tail = doc[m_close.start():]

    body = "\n".join(_row(r) for r in data)
    # Refresh the entry count wherever it appears (the <title> and the live #count are set by JS, but
    # the title is static — keep it honest).
    head = re.sub(r"outputs-catalog \(\d+ entries\)",
                  "outputs-catalog (%d entries)" % len(data), head)

    HTML.write_text(head + "\n" + body + "\n" + tail, encoding="utf-8")
    print("outputs-catalog.html rebuilt from CSV: %d entries" % len(data))


if __name__ == "__main__":
    main()

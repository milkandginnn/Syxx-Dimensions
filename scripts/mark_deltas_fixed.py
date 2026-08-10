#!/usr/bin/env python3
"""Mark implemented deltas as ALREADY FIXED in DELTAS.md.

Run after all Phase 1-8 remediation agents complete.
"""

import re
import sys
from pathlib import Path

DELTAS_PATH = Path(__file__).resolve().parent.parent / "DELTAS.md"

# All delta IDs targeted by Phases 1-8 of REMEDIATION_PLAN.md
FIXED_DELTAS = {
    # Phase 1: Critical
    "A281", "A282", "A309",
    "C207", "C208", "C211", "C212",
    "D300", "D301",
    "E205", "E210", "E211", "E223",
    "F079",
    # Phase 2: Server Python (A273-A318, excluding Phase 1)
    "A273", "A274", "A275", "A276", "A277", "A278", "A279", "A280",
    "A283", "A284", "A285", "A286", "A287", "A288", "A289", "A290",
    "A291", "A292", "A293", "A294", "A295", "A296", "A297", "A298",
    "A299", "A300", "A301", "A302", "A303", "A304", "A305", "A306",
    "A307", "A308",
    "A310", "A311", "A312", "A313", "A314", "A315", "A316", "A317", "A318",
    # Phase 3: Detection Efficacy (B271-B325)
    "B271", "B272", "B273", "B277", "B279",
    "B270", "B274", "B275", "B276", "B278",
    "B280", "B281", "B282", "B283",
    "B284", "B285", "B286", "B287", "B288", "B289",
    "B290", "B291", "B292", "B293", "B294", "B295",
    "B296", "B297", "B298", "B299",
    "B300", "B301", "B302", "B303", "B304", "B305", "B306",
    "B307", "B308", "B309",
    "B310", "B311", "B312", "B313", "B314", "B315", "B316", "B317", "B318", "B319",
    "B320", "B321", "B322", "B323", "B324", "B325",
    # Phase 4: Verdict & Evidence Integrity (C197-C225)
    "C197", "C198", "C199",
    "C200", "C201", "C202", "C203", "C204", "C205", "C206",
    "C209", "C210",
    "C213", "C214", "C215", "C216", "C217", "C218", "C219",
    "C220", "C221", "C222", "C223", "C224", "C225",
    # Phase 5: macOS Swift (D302-D409, excluding Phase 1 D300/D301)
    "D302", "D303", "D304", "D305",
    "D322", "D323", "D324", "D325", "D326", "D327", "D328", "D329",
    "D330", "D331", "D332",
    "D401", "D402", "D403", "D404", "D405", "D406", "D407", "D408", "D409",
    # Phase 6: Windows C# (D001, D274, D297-D299, D306-D321)
    "D001", "D274", "D297", "D298", "D299",
    "D306", "D307", "D308", "D309",
    "D310", "D311", "D312", "D313", "D314", "D315", "D316", "D317", "D318", "D319",
    "D320", "D321",
    # Phase 7: Linux C# (D275, D281-D291, D333-D350)
    "D275", "D281", "D282", "D283", "D284", "D285", "D286", "D287", "D288", "D289",
    "D290", "D291",
    "D333", "D334", "D335", "D336", "D337", "D338", "D339",
    "D340", "D341", "D342", "D343", "D344", "D345", "D346", "D347", "D348", "D349",
    "D350",
    # Phase 8: Security & Hardening (E206-E229, excluding Phase 1)
    "E206", "E207",
    "E212", "E213", "E214", "E215", "E216", "E217", "E218", "E219",
    "E220", "E221", "E222",
    "E224", "E225", "E226", "E227", "E228", "E229",
}

STATUS_LINE = "- **Status:**           **ALREADY FIXED** — remediated 2026-08-07 by delta remediation sweep."


def mark_delta(text: str, delta_id: str) -> tuple[str, bool]:
    """Add ALREADY FIXED marker above a delta entry. Returns (new_text, was_modified)."""
    pattern = re.compile(
        r"^(\-\-\-\n\n)(\-\s+\*\*\[" + re.escape(delta_id) + r"\]\*\*)",
        re.MULTILINE,
    )
    already_fixed_pattern = re.compile(
        r"\*\*\[ALREADY FIXED\]\*\*.*?\*\*\[" + re.escape(delta_id) + r"\]\*\*",
        re.DOTALL,
    )

    if already_fixed_pattern.search(text):
        return text, False

    match = pattern.search(text)
    if not match:
        return text, False

    header = f"**[ALREADY FIXED]**\n{STATUS_LINE}\n\n"
    new_text = text[: match.start(2)] + header + text[match.start(2):]
    return new_text, True


def main():
    text = DELTAS_PATH.read_text()
    marked = 0
    skipped = 0
    not_found = []

    for delta_id in sorted(FIXED_DELTAS):
        new_text, was_modified = mark_delta(text, delta_id)
        if was_modified:
            text = new_text
            marked += 1
        else:
            if f"[{delta_id}]" in text:
                skipped += 1
            else:
                not_found.append(delta_id)

    DELTAS_PATH.write_text(text)
    print(f"Marked {marked} deltas as ALREADY FIXED")
    print(f"Skipped {skipped} already marked")
    print(f"Not found in DELTAS.md: {len(not_found)} ({', '.join(not_found[:20])}{'...' if len(not_found) > 20 else ''})")


if __name__ == "__main__":
    main()

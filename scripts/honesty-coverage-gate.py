#!/usr/bin/env python3
"""Honesty-spine coverage gate (Server lane).

WHY THIS EXISTS
---------------
The 2026-07-16 test audit found that the suite could report a green 119/119 while
FOUR security guards were deleted outright — including the one that refuses to
Ed25519-sign a `leaked:false` on zero collector observations (A024). The defect
class was never "the assertion is weak"; it was "no test executes this branch at
all". Plain branch coverage catches that class deterministically and cheaply.

WHAT IT DOES
------------
Runs the Server suite under coverage.py and asserts that every guard in the
registry below was actually EXECUTED by a test. A guard that no test reaches is
a guard that can be deleted with a green suite — i.e. it is unprotected.

WHY ANCHORS, NOT LINE NUMBERS
-----------------------------
Line numbers drift with every edit and would rot this gate into noise. Each guard
is located by matching distinctive source text in the file, then that resolved
line is checked against the coverage data. If an anchor no longer matches, the
gate FAILS LOUDLY (anchor rot is a real failure — never a silent pass).

HONESTY NOTE
------------
This gate is EXPECTED TO FAIL on first run. It reports the audit's findings
mechanically. Do not "fix" it by removing guards from the registry or by adding a
baseline of accepted failures — that is the softening this whole exercise exists
to prevent. Fix it by writing the missing tests.

Usage:  ./venv/bin/python ../scripts/honesty-coverage-gate.py     (run from Server/)
Exit:   0 = every registered guard is covered; 1 = at least one is unprotected.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# The honesty spine. Each entry: (module, delta-id, anchor text, what it protects)
# Anchor text must be distinctive enough to match exactly one line in the module.
# ---------------------------------------------------------------------------
GUARDS = [
    (
        "syxx_server/leak.py", "A024", "if not obs:",
        "Refuses to sign a clean `leaked:false` on ZERO observations — a signed "
        "all-clear with no liveness proof is indistinguishable from a blocked collector.",
    ),
    (
        "syxx_server/leak.py", "A009", "if rec is None:",
        "Never-issued token writes nothing — otherwise anyone can drive unbounded DB "
        "writes and poison the passive-DNS index that feeds drift conclusions.",
    ),
    (
        # Two-element anchor: the fail-closed RETURN is the guard, not the outer `if`.
        # The outer `if not config.KEYGEN_PUBLIC_KEY:` takes both arcs across the suite,
        # which made an earlier version of this gate report a false PASS.
        "syxx_server/licensing.py", "D040/A021",
        ("if not config.KEYGEN_PUBLIC_KEY:", 'if os.environ.get("SYXX_DEV_MODE") != "1":'),
        "Fail CLOSED when no Keygen public key is configured — without this, a forged "
        "response grants entitlement.",
    ),
    (
        "syxx_server/licensing.py", "A056", "if abs(time.time() - resp_epoch) > _KEYGEN_DATE_SKEW_SECONDS:",
        "Rejects a validly-signed response replayed outside the clock-skew window.",
    ),
    (
        "syxx_server/passivedns.py", "A023", 'r.pop("resolver", None)',
        "Strips the observing resolver IP at the /api/passive-dns boundary — the index is "
        "cross-readable, and for a leak observation that IP is submitter PII.",
    ),
    (
        "syxx_server/dns_authoritative.py", "AUTH-ZONE", "if zone is None:",
        "Answers REFUSED without AA for a zone we do not own — an out-of-zone authoritative "
        "answer is the server lying about names it has no authority over.",
    ),
    (
        "syxx_server/dns_authoritative.py", "AUTH-LEN", "if len(data) < 12:",
        "Drops a runt packet before unpacking attacker-controlled UDP — a crash here is a DoS "
        "on the open internet.",
    ),
]


def resolve_anchor(root: Path, module: str, anchor) -> list[int]:
    """Locate a guard line.

    `anchor` is either a single string (must match exactly one line) or a tuple of
    strings that must appear on CONSECUTIVE lines — the guard is the LAST of them.
    The tuple form disambiguates repeated idioms (e.g. two identical SYXX_DEV_MODE
    checks) without pinning a line number that would drift.
    """
    src = [l.strip() for l in (root / module).read_text(errors="ignore").splitlines()]
    if isinstance(anchor, str):
        return [i for i, line in enumerate(src, 1) if line.startswith(anchor)]

    hits = []
    for i in range(len(src) - len(anchor) + 1):
        if all(src[i + j].startswith(anchor[j]) for j in range(len(anchor))):
            hits.append(i + len(anchor))  # 1-indexed line of the LAST anchor element
    return hits


def main() -> int:
    root = Path(__file__).resolve().parent.parent / "Server"
    py = root / "venv" / "bin" / "python"
    if not py.exists():
        print(f"FAIL: no venv python at {py}", file=sys.stderr)
        return 1

    print("Running Server suite under coverage…")
    run = subprocess.run(
        [str(py), "-m", "coverage", "run", "--branch", "-m", "pytest", "tests/", "-q"],
        cwd=root, capture_output=True, text=True,
    )
    if run.returncode != 0:
        print("FAIL: the suite itself is not green — fix that before gating coverage.\n")
        print(run.stdout[-2000:])
        return 1

    cov_json = root / ".coverage-honesty.json"
    subprocess.run([str(py), "-m", "coverage", "json", "-o", str(cov_json), "-q"],
                   cwd=root, capture_output=True, text=True)
    data = json.loads(cov_json.read_text())
    files = data.get("files", {})

    unprotected, rotted = [], []
    print(f"\nChecking {len(GUARDS)} honesty guards:\n")

    for module, delta, anchor, protects in GUARDS:
        lines = resolve_anchor(root, module, anchor)
        if len(lines) != 1:
            rotted.append((module, delta, anchor, len(lines)))
            print(f"  [ANCHOR ROT] {delta:12s} {module}: anchor matched {len(lines)} lines, expected 1")
            continue
        line = lines[0]

        entry = files.get(module) or files.get(f"./{module}")
        if entry is None:
            rotted.append((module, delta, anchor, 0))
            print(f"  [NO COV DATA] {delta:12s} {module}")
            continue

        # A guard is a BRANCH. Line-execution is not protection: `if not obs:` runs on
        # every call, but the guard only FIRES when the condition is true. If either arc
        # out of the guard line was never taken, the guard's behaviour is unexercised —
        # which is precisely how four guards survived deletion against a green suite.
        executed = line in set(entry.get("executed_lines", []))
        missing_arcs = [b for b in entry.get("missing_branches", []) if b and b[0] == line]

        # NOTE ON WORDING — this gate can prove a guard is UNPROTECTED, but it can NEVER
        # prove one is protected. Coverage proves EXECUTION, not ASSERTION: a test can take
        # both arcs and assert nothing about the outcome, in which case deleting the guard
        # still leaves the suite green. So the passing state is "EXERCISED", not "protected".
        # Only mutation testing proves protection. Do not let a green run here be read as
        # "the honesty spine is safe" — it means only "no guard is provably orphaned".
        if not executed:
            state, ok = "NEVER RUN  ", False
        elif missing_arcs:
            state, ok = "UNPROTECTED", False
        else:
            state, ok = "EXERCISED  ", True

        detail = ""
        if missing_arcs:
            detail = f"  (branch never taken: {'; '.join(f'{a[0]}->{a[1]}' for a in missing_arcs)})"
        print(f"  [{state}] {delta:12s} {module}:{line}  {anchor}{detail}")
        if not ok:
            unprotected.append((module, delta, line, protects))

    print()
    if rotted:
        print("ANCHOR ROT — a guard could not be located. This is a FAILURE, not a pass:")
        for module, delta, anchor, n in rotted:
            print(f"  {delta}: {module} anchor {anchor!r} matched {n} lines")
        print()

    if unprotected:
        # Precise wording: the guard LINE may well execute on every call. What no test does
        # is take the arc where the guard FIRES. An unfired guard is an unprotected guard.
        print(f"GATE FAILED — {len(unprotected)} honesty guard(s) never actually fire in any test.")
        print("Each of these can be deleted with a green suite:\n")
        for module, delta, line, protects in unprotected:
            print(f"  {delta} @ {module}:{line}")
            print(f"      protects: {protects}\n")
        print("Fix by writing the missing tests — NOT by editing this registry.")

    # A222: count delta-tagged guards in the package so the pass line cannot be misread
    # as full coverage. "7 registered / 78 delta-tagged guards" makes the gap explicit.
    import re as _re
    delta_tag_pattern = _re.compile(r'#\s*(?:A\d{3}|D\d{3}|SD-\d{2,3}|SRV-\d{3})')
    total_tagged = 0
    pkg_dir = root / "syxx_server"
    if pkg_dir.is_dir():
        for py_file in pkg_dir.glob("*.py"):
            for line in py_file.read_text(errors="ignore").splitlines():
                if delta_tag_pattern.search(line):
                    total_tagged += 1
    ratio_note = f" ({len(GUARDS)} registered / {total_tagged} delta-tagged guards in package)"

    if unprotected or rotted:
        return 1
    print(f"GATE PASSED — every registered honesty guard is exercised by a test.{ratio_note}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

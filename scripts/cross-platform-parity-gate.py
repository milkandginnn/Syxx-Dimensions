#!/usr/bin/env python3
"""Cross-platform parity gate (UNIFIED_IMPLEMENTATION_PLAN.md Phase 7.1 QA-3).

WHY THIS EXISTS
---------------
The `diff` job in .github/workflows/ci.yml was a NO-OP. Its only reachable path
emitted `::warning title=Transcript parity gate inactive` and `exit 0`, because the
transcript emitter it depends on (WS-B2) has never shipped, so `transcripts/*.json`
is always empty. A gate that cannot fail is not a gate: it renders as a passing
check on every PR while measuring nothing. That is precisely the defect class this
product exists to refuse — an unmeasured check presented as a passed one.

This gate replaces the placeholder with assertions that run against source that
EXISTS TODAY and that fail the build on divergence:

  1. SPAN NAMES  — the OTel span names created by macOS `SyxxKit` must be the same
     set as the ActivitySource span names created by Windows `Syxx.Kit`. A span
     present on one platform and absent on the other means one platform's operation
     is invisible in telemetry while the other's is observable, and no dashboard,
     alert or SLO built on the visible one covers the invisible one.

  2. WIRE VOCABULARIES — the shared enum RAW/WIRE values (not the C#/Swift symbol
     names) must be byte-identical across platforms, and must exactly match the
     canon declared in VOCABULARIES below. The four determinative states
     ("clean" / "threatDetected" / "inconclusive" / "untested") are checked here.
     A single character of drift ("threat_detected", "Inconclusive") silently
     breaks report interchange, signed-report verification and every server-side
     query, with both platforms' test suites green.

WHY IT MATCHES ON STRINGS, NOT SYMBOLS
--------------------------------------
The C#/Swift type and member names are mid-flight (`DeterminativeResult` ->
`DeterminativeState`, `LeakResult` -> `LeakResult`). Symbol names are an internal
concern and may be renamed freely. The WIRE values may not: they are the contract.
So this gate never looks up a vocabulary by its type name. It looks for *any* enum
(Swift) or *any* switch block (C#) whose emitted string set is exactly the canon.
A rename therefore cannot break the gate, and cannot sneak a wire change past it.

EXIT CODES
----------
  0  every assertion held
  1  divergence (build must go red)
  2  the gate could not run (bad paths, unreadable source) — NEVER silently 0

USAGE
-----
  scripts/cross-platform-parity-gate.py [--repo-root PATH] [--github]

`--github` additionally emits GitHub Actions `::error` annotations. It does not
change the exit code: the exit code is the gate.
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import Counter
from pathlib import Path

# ---------------------------------------------------------------------------
# Scope: the two implementations of the shared reasoning core.
# ---------------------------------------------------------------------------
SWIFT_ROOT = "macOS/Sources/SyxxKit"
CSHARP_ROOT = "Windows/dotNET/Syxx.Kit"

# ---------------------------------------------------------------------------
# The shared wire vocabularies. Each entry is a canon that BOTH platforms must
# reproduce exactly. Keys are gate-local labels, not symbol names.
# ---------------------------------------------------------------------------
VOCABULARIES: dict[str, dict] = {
    "determinative_state": {
        "why": (
            "the four run-level determinative states. This is the product's top-line "
            "answer; drift here means a macOS report and a Windows report cannot be "
            "compared, aggregated or verified against each other."
        ),
        "values": {"clean", "threatDetected", "inconclusive", "untested"},
    },
    "tri_state": {
        "why": "TriState — success/failure/inconclusive. The honesty spine's third state.",
        "values": {"success", "failure", "inconclusive"},
    },
    "tracer_outcome": {
        "why": (
            "SyxxTracer.Outcome — the telemetry outcome vocabulary written to the "
            "`syxx.outcome` span attribute. Cross-platform collector queries key on it."
        ),
        "values": {"success", "failure", "inconclusive", "untested", "unclassified"},
    },
    "section_status": {
        "why": "per-section terminal status; `untested`/`invalidated` are what block a clean conclusion.",
        "values": {"untested", "invalidated", "inconclusive", "degraded", "positive", "negative"},
    },
    "section": {
        "why": "the six toolset section ids that key every report and every server row.",
        "values": {"prophet", "mirror", "portal", "glutton", "cage", "witness"},
    },
    "diagnosis_confidence": {
        "why": "the emitted-diagnosis confidence tiers.",
        "values": {"probable", "conclusive"},
    },
}

# ---------------------------------------------------------------------------
# Signed-payload JSON wire keys.
#
# These are the keys that appear in Ed25519-SIGNED JSON. A rename does not merely
# break parsing: it changes the bytes that were signed, so a report signed by one
# platform fails verification on the other, and the failure surfaces as "signature
# invalid" (looks like tampering) rather than "schema drift". That is the worst
# possible misattribution for this product.
#
# This assertion is the reason the whole gate matches on WIRE values. The Swift
# symbol for the conclusion field is `result`, mapped to the wire key "conclusion"
# (SignedEvidencePayload.swift:88-92); the C# property is `Result` carrying
# [JsonPropertyName("conclusion")] (SignedEvidencePayload.cs:217-220). The symbols
# already differ ACROSS platforms and differ FROM the wire key on both. A gate
# asserting on symbol names would report a false divergence here, and would sail
# straight past a real change to the signed bytes.
# ---------------------------------------------------------------------------
SIGNED_PAYLOAD_FILES = ("SignedEvidencePayload", "DimensionClient")
REQUIRED_SIGNED_PAYLOAD_KEYS = {"conclusion"}


# ---------------------------------------------------------------------------
# Comment stripping. Naive regex stripping corrupts string literals containing
# `//` (e.g. "https://dns.google") and would make the gate lie about what the
# source says, so both strippers are escape-aware scanners that preserve string
# literals byte-for-byte and blank out only real comments.
# ---------------------------------------------------------------------------
def strip_swift_comments(src: str) -> str:
    out: list[str] = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            if src.startswith('"""', i):  # multiline string literal
                out.append('"""')
                i += 3
                while i < n and not src.startswith('"""', i):
                    out.append(src[i])
                    i += 1
                out.append(src[i : i + 3])
                i += 3
                continue
            out.append(c)
            i += 1
            while i < n:
                if src[i] == "\\" and i + 1 < n:
                    out.append(src[i : i + 2])
                    i += 2
                    continue
                out.append(src[i])
                if src[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if src.startswith("//", i):
            while i < n and src[i] != "\n":
                out.append(" ")
                i += 1
            continue
        if src.startswith("/*", i):
            depth = 1  # Swift block comments nest
            out.append("  ")
            i += 2
            while i < n and depth:
                if src.startswith("/*", i):
                    depth += 1
                    out.append("  ")
                    i += 2
                elif src.startswith("*/", i):
                    depth -= 1
                    out.append("  ")
                    i += 2
                else:
                    out.append("\n" if src[i] == "\n" else " ")
                    i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def strip_csharp_comments(src: str) -> str:
    out: list[str] = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == "@" and i + 1 < n and src[i + 1] == '"':  # verbatim string
            out.append('@"')
            i += 2
            while i < n:
                if src.startswith('""', i):
                    out.append('""')
                    i += 2
                    continue
                out.append(src[i])
                if src[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == '"':
            out.append(c)
            i += 1
            while i < n:
                if src[i] == "\\" and i + 1 < n:
                    out.append(src[i : i + 2])
                    i += 2
                    continue
                out.append(src[i])
                if src[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == "'":  # char literal
            out.append(c)
            i += 1
            while i < n:
                if src[i] == "\\" and i + 1 < n:
                    out.append(src[i : i + 2])
                    i += 2
                    continue
                out.append(src[i])
                if src[i] == "'":
                    i += 1
                    break
                i += 1
            continue
        if src.startswith("//", i):
            while i < n and src[i] != "\n":
                out.append(" ")
                i += 1
            continue
        if src.startswith("/*", i):
            out.append("  ")
            i += 2
            while i < n and not src.startswith("*/", i):
                out.append("\n" if src[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def match_braces(src: str, open_idx: int) -> int:
    """Index just past the `}` matching the `{` at open_idx. -1 if unbalanced."""
    depth = 0
    for i in range(open_idx, len(src)):
        if src[i] == "{":
            depth += 1
        elif src[i] == "}":
            depth -= 1
            if depth == 0:
                return i + 1
    return -1


# ---------------------------------------------------------------------------
# Span names
# ---------------------------------------------------------------------------
# A span name literal looks like `dns.query.udp` / `syxx.synthesis.synthesize`.
SPAN_NAME_SHAPE = re.compile(r"^[a-z][a-z0-9_]*(?:\.[a-z0-9_*]+)+$")

# Interpolated span names — `$"probe.{label}"` (C#) and `"probe.\(label)"` (Swift)
# — name a family, not one span. Normalising both to `probe.*` compares the family,
# which is the parity-relevant fact; comparing the raw literals would report a
# false divergence purely because the two languages spell interpolation differently.
SWIFT_INTERP = re.compile(r"\\\([^)]*\)")
CSHARP_INTERP = re.compile(r"\{[^}]*\}")

SWIFT_SPAN_CALL = re.compile(r"\b(?:SyxxTracer\s*\.\s*)?(?:span|spanSync)\s*\(\s*\"((?:[^\"\\]|\\.)*)\"")
CSHARP_SPAN_CALL = re.compile(
    r"\b(?:SyxxTracer\s*\.\s*)?(?:StartSpan|EmitUntested)\s*\(\s*\$?\"((?:[^\"\\]|\\.)*)\""
)


def collect_swift_spans(files: dict[Path, str]) -> dict[str, list[str]]:
    found: dict[str, list[str]] = {}
    for path, src in files.items():
        for m in SWIFT_SPAN_CALL.finditer(src):
            name = SWIFT_INTERP.sub("*", m.group(1))
            if SPAN_NAME_SHAPE.match(name):
                found.setdefault(name, []).append(f"{path}:{src.count(chr(10), 0, m.start()) + 1}")
    return found


def collect_csharp_spans(files: dict[Path, str]) -> dict[str, list[str]]:
    found: dict[str, list[str]] = {}
    for path, src in files.items():
        for m in CSHARP_SPAN_CALL.finditer(src):
            name = CSHARP_INTERP.sub("*", m.group(1))
            if SPAN_NAME_SHAPE.match(name):
                found.setdefault(name, []).append(f"{path}:{src.count(chr(10), 0, m.start()) + 1}")
    return found


# ---------------------------------------------------------------------------
# Wire vocabularies
# ---------------------------------------------------------------------------
SWIFT_ENUM_DECL = re.compile(r"\benum\s+(\w+)\s*:\s*([^{}\n]*?)\{")
SWIFT_CASE_LINE = re.compile(r"^\s*(?:indirect\s+)?case\s+(.+?)\s*$", re.MULTILINE)
SWIFT_CASE_MEMBER = re.compile(r"^(\w+)\s*(?:=\s*\"((?:[^\"\\]|\\.)*)\")?$")


def collect_swift_vocabularies(files: dict[Path, str]) -> list[tuple[str, str, frozenset[str]]]:
    """Every `enum X: String` and the exact set of raw values it produces.

    Returns (location, enum_name, raw_value_set). Enums whose cases carry
    associated values or are otherwise unparseable are skipped: an enum this
    parser cannot read is not silently treated as matching anything.
    """
    out: list[tuple[str, str, frozenset[str]]] = []
    for path, src in files.items():
        for m in SWIFT_ENUM_DECL.finditer(src):
            name, conformances = m.group(1), m.group(2)
            if not re.search(r"\bString\b", conformances):
                continue
            open_idx = src.index("{", m.start())
            close_idx = match_braces(src, open_idx)
            if close_idx < 0:
                continue
            body = src[open_idx + 1 : close_idx - 1]
            # Only depth-0 case declarations: a nested enum must not leak into its parent.
            depth, kept = 0, []
            for line in body.splitlines():
                if depth == 0:
                    kept.append(line)
                depth += line.count("{") - line.count("}")
            flat = "\n".join(kept)
            values: set[str] = set()
            ok = True
            for cm in SWIFT_CASE_LINE.finditer(flat):
                for member in cm.group(1).split(","):
                    member = member.strip().rstrip(",")
                    if not member:
                        continue
                    mm = SWIFT_CASE_MEMBER.match(member)
                    if not mm:
                        ok = False
                        break
                    values.add(mm.group(2) if mm.group(2) is not None else mm.group(1))
                if not ok:
                    break
            if ok and values:
                line_no = src.count("\n", 0, m.start()) + 1
                out.append((f"{path}:{line_no}", name, frozenset(values)))
    return out


# `Member => "wire"` (forward) and `"wire" => Type.Member` (JSON reader direction).
CS_ARM_FWD = re.compile(r"(?:(\w+)\s*\.\s*)?(\w+)\s*=>\s*\$?\"((?:[^\"\\]|\\.)*)\"")
CS_ARM_REV = re.compile(r"\"((?:[^\"\\]|\\.)*)\"\s*=>\s*(\w+)\s*\.\s*(\w+)")


def collect_csharp_vocabularies(files: dict[Path, str]) -> list[tuple[str, str, frozenset[str]]]:
    """Every `switch` block that maps an enum to/from string literals.

    Grouped by the enum type that qualifies the majority of the block's arms, so
    a discard arm (`_ => "untested"`) is attributed to the same vocabulary rather
    than dropped — dropping it would invent a divergence that does not exist.
    """
    out: list[tuple[str, str, frozenset[str]]] = []
    for path, src in files.items():
        for sm in re.finditer(r"\bswitch\b", src):
            open_idx = src.find("{", sm.end())
            if open_idx < 0:
                continue
            close_idx = match_braces(src, open_idx)
            if close_idx < 0:
                continue
            block = src[open_idx : close_idx]
            qualifiers: Counter[str] = Counter()
            values: set[str] = set()
            for am in CS_ARM_FWD.finditer(block):
                if am.group(1):
                    qualifiers[am.group(1)] += 1
                values.add(am.group(3))
            for am in CS_ARM_REV.finditer(block):
                qualifiers[am.group(2)] += 1
                values.add(am.group(1))
            if not qualifiers or not values:
                continue
            line_no = src.count("\n", 0, sm.start()) + 1
            out.append((f"{path}:{line_no}", qualifiers.most_common(1)[0][0], frozenset(values)))
    return out


# Swift `case result = "conclusion"` / bare `case conclusion` inside a CodingKeys enum.
SWIFT_CODING_KEYS = re.compile(r"\benum\s+CodingKeys\b[^{]*\{")
# C# `[JsonPropertyName("conclusion")]`
CSHARP_JSON_KEY = re.compile(r"JsonPropertyName\s*\(\s*\"((?:[^\"\\\\]|\\\\.)*)\"\s*\)")


def collect_swift_wire_keys(files: dict[Path, str]) -> dict[str, list[str]]:
    """JSON wire keys declared by Swift CodingKeys enums, keyed to their location."""
    found: dict[str, list[str]] = {}
    for path, src in files.items():
        for m in SWIFT_CODING_KEYS.finditer(src):
            open_idx = src.index("{", m.start())
            close_idx = match_braces(src, open_idx)
            if close_idx < 0:
                continue
            body = src[open_idx + 1 : close_idx - 1]
            for cm in SWIFT_CASE_LINE.finditer(body):
                for member in cm.group(1).split(","):
                    member = member.strip().rstrip(",")
                    if not member:
                        continue
                    mm = SWIFT_CASE_MEMBER.match(member)
                    if not mm:
                        continue
                    # An explicit raw value IS the wire key; a bare case uses its own name.
                    key = mm.group(2) if mm.group(2) is not None else mm.group(1)
                    line_no = src.count("\n", 0, m.start()) + 1
                    found.setdefault(key, []).append(f"{path}:{line_no}")
    return found


def collect_csharp_wire_keys(files: dict[Path, str]) -> dict[str, list[str]]:
    """JSON wire keys declared by C# [JsonPropertyName] attributes."""
    found: dict[str, list[str]] = {}
    for path, src in files.items():
        for m in CSHARP_JSON_KEY.finditer(src):
            found.setdefault(m.group(1), []).append(
                f"{path}:{src.count(chr(10), 0, m.start()) + 1}"
            )
    return found


def best_candidate(
    candidates: list[tuple[str, str, frozenset[str]]], canon: frozenset[str]
) -> tuple[str, str, frozenset[str]] | None:
    """The closest near-miss, for a failure message that names the actual drift."""
    scored = [
        (len(canon & v) / len(canon | v), c)
        for c in candidates
        for v in (c[2],)
        if canon & v
    ]
    if not scored:
        return None
    return max(scored, key=lambda t: t[0])[1]


# ---------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------
class Report:
    def __init__(self, github: bool) -> None:
        self.github = github
        self.failures = 0

    def ok(self, msg: str) -> None:
        print(f"  PASS  {msg}")

    def fail(self, title: str, msg: str) -> None:
        self.failures += 1
        print(f"  FAIL  {title}")
        for line in msg.splitlines():
            print(f"        {line}")
        if self.github:
            flat = msg.replace("\n", "%0A")
            print(f"::error title=Parity gate: {title}::{flat}")


def read_sources(root: Path, suffix: str, stripper) -> dict[Path, str]:
    files: dict[Path, str] = {}
    for p in sorted(root.rglob(f"*{suffix}")):
        try:
            files[p] = stripper(p.read_text(encoding="utf-8"))
        except OSError as exc:  # unreadable source is inconclusive, not clean
            print(f"gate error: cannot read {p}: {exc}", file=sys.stderr)
            raise SystemExit(2)
    return files


def rel(paths: list[str], root: Path) -> str:
    return ", ".join(sorted({p.replace(str(root) + "/", "") for p in paths}))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo-root", default=None, help="repo root (default: parent of scripts/)")
    ap.add_argument("--github", action="store_true", help="also emit ::error annotations")
    args = ap.parse_args()

    repo = Path(args.repo_root).resolve() if args.repo_root else Path(__file__).resolve().parent.parent
    swift_root = repo / SWIFT_ROOT
    cs_root = repo / CSHARP_ROOT

    for label, root in (("macOS SyxxKit", swift_root), ("Windows Syxx.Kit", cs_root)):
        if not root.is_dir():
            print(f"gate error: {label} source root not found: {root}", file=sys.stderr)
            print("The gate cannot run, so it reports UNKNOWN (exit 2), never a pass.", file=sys.stderr)
            return 2

    swift_files = read_sources(swift_root, ".swift", strip_swift_comments)
    cs_files = read_sources(cs_root, ".cs", strip_csharp_comments)
    if not swift_files or not cs_files:
        print("gate error: one of the source trees produced zero files.", file=sys.stderr)
        return 2

    print("Cross-platform parity gate (QA-3)")
    print(f"  macOS   : {len(swift_files)} .swift under {SWIFT_ROOT}")
    print(f"  Windows : {len(cs_files)} .cs under {CSHARP_ROOT}")
    print()

    r = Report(args.github)

    # -- 1. OTel span names -------------------------------------------------
    print("[1] OTel span names (SyxxKit vs Syxx.Kit)")
    swift_spans = collect_swift_spans(swift_files)
    cs_spans = collect_csharp_spans(cs_files)
    if not swift_spans or not cs_spans:
        r.fail(
            "no span names found",
            "One platform yielded zero span names. Either instrumentation was removed or the\n"
            "gate's call-site pattern no longer matches. Both are failures, not passes.\n"
            f"macOS: {sorted(swift_spans)}\nWindows: {sorted(cs_spans)}",
        )
    else:
        only_mac = sorted(set(swift_spans) - set(cs_spans))
        only_win = sorted(set(cs_spans) - set(swift_spans))
        if only_mac or only_win:
            lines = ["OTel span names diverge between the macOS and Windows reasoning cores."]
            for name in only_mac:
                lines.append(f"  macOS ONLY   {name!r}   ({rel(swift_spans[name], repo)})")
            for name in only_win:
                lines.append(f"  Windows ONLY {name!r}   ({rel(cs_spans[name], repo)})")
            lines.append(
                "An operation traced on one platform and untraced on the other is invisible to "
                "every dashboard, alert and SLO built on the traced one."
            )
            r.fail("OTel span-name divergence", "\n".join(lines))
        else:
            r.ok(f"{len(swift_spans)} span names identical on both platforms: {', '.join(sorted(swift_spans))}")
    print()

    # -- 2. Shared wire vocabularies ---------------------------------------
    print("[2] Shared wire/raw string vocabularies (byte-identical across platforms)")
    swift_vocab = collect_swift_vocabularies(swift_files)
    cs_vocab = collect_csharp_vocabularies(cs_files)

    for label, spec in VOCABULARIES.items():
        canon = frozenset(spec["values"])
        swift_hits = [c for c in swift_vocab if c[2] == canon]
        cs_hits = [c for c in cs_vocab if c[2] == canon]

        if swift_hits and cs_hits:
            # A type can own SEVERAL string-mapping blocks — typically a writer
            # (`Member => "wire"`) and a reader (`"wire" => Member`). Accepting the
            # vocabulary because ANY ONE of them matches the canon is not enough: a
            # mutation that renames the value in the WRITER alone still round-trips
            # through the untouched READER as far as this gate can see, so the gate
            # would pass while the platform actually serialises a value its own
            # deserialiser rejects. (Observed: mutating only
            # `DeterminativeState.ThreatDetected => "threatDetected"` left the reader
            # switch matching, and an earlier revision of this gate passed.)
            #
            # So every WIRE block owned by the same type must reproduce the canon
            # exactly. A block counts as a wire block when it shares at least one value
            # with the canon. Blocks that share NOTHING are display/prose mappings over
            # the same enum (Report.cs `"CLEAN"`, PlainExplanation.cs
            # `"the {name} check did not run"`); those are free to say anything and are
            # deliberately not held to the wire contract.
            stragglers: list[str] = []
            for plat, hits, cands in (
                ("macOS", swift_hits, swift_vocab),
                ("Windows", cs_hits, cs_vocab),
            ):
                owners = {h[1] for h in hits}
                for loc, name, vals in cands:
                    if name in owners and vals != canon and (vals & canon):
                        stragglers.append(
                            f"  {plat} {name} @ {rel([loc], repo)} is a wire mapping for this "
                            f"vocabulary but does not reproduce it:\n"
                            f"      missing here : {sorted(canon - vals)}\n"
                            f"      extra here   : {sorted(vals - canon)}"
                        )
            if stragglers:
                r.fail(
                    f"wire vocabulary partially drifted ({label})",
                    f"Vocabulary {label!r} — {spec['why']}\n"
                    f"Canon: {sorted(canon)}\n"
                    "At least one mapping reproduces the canon, but another mapping owned by the "
                    "same type does not. That is a half-applied rename: one direction of the "
                    "serialisation contract was changed and the other was not, so the platform "
                    "writes a value it cannot itself read back.\n"
                    + "\n".join(stragglers),
                )
                continue
            r.ok(
                f"{label}: {{{', '.join(sorted(canon))}}} "
                f"[macOS {swift_hits[0][1]} @ {rel([swift_hits[0][0]], repo)}; "
                f"Windows {cs_hits[0][1]} @ {rel([cs_hits[0][0]], repo)}]"
            )
            continue

        lines = [f"Vocabulary {label!r} — {spec['why']}", f"Canon: {sorted(canon)}"]
        for plat, hits, cands in (
            ("macOS SyxxKit", swift_hits, swift_vocab),
            ("Windows Syxx.Kit", cs_hits, cs_vocab),
        ):
            if hits:
                lines.append(f"  {plat}: OK ({hits[0][1]} @ {rel([hits[0][0]], repo)})")
                continue
            near = best_candidate(cands, canon)
            if near is None:
                lines.append(f"  {plat}: NOT FOUND — no enum/switch emits this vocabulary at all.")
            else:
                loc, name, vals = near
                lines.append(f"  {plat}: DRIFTED — closest is {name} @ {rel([loc], repo)}")
                lines.append(f"      missing here : {sorted(canon - vals)}")
                lines.append(f"      extra here   : {sorted(vals - canon)}")
        lines.append(
            "Wire values are the cross-platform contract. Symbol names may be renamed freely; "
            "these strings may not."
        )
        r.fail(f"wire vocabulary divergence ({label})", "\n".join(lines))
    print()

    # -- 3. Signed-payload JSON wire keys -----------------------------------
    print("[3] Signed-payload JSON wire keys (the bytes that get Ed25519-signed)")
    # Scoped PER CONTRACT FILE, never pooled across them. Pooling let a rename in
    # SignedEvidencePayload.cs pass because DimensionClient.cs still declared the same
    # key somewhere in the union — the gate would confirm "the key exists on Windows"
    # while the specific signed payload had already stopped emitting it.
    swift_payload_files = {
        tag: {p: src for p, src in swift_files.items() if tag in p.name}
        for tag in SIGNED_PAYLOAD_FILES
    }
    cs_payload_files = {
        tag: {p: src for p, src in cs_files.items() if tag in p.name}
        for tag in SIGNED_PAYLOAD_FILES
    }

    if not any(swift_payload_files.values()) or not any(cs_payload_files.values()):
        # The gate could not locate the contract it was asked to police. That is
        # UNKNOWN, not OK: silently skipping would recreate the very no-op this
        # gate replaced.
        r.fail(
            "signed-payload sources not found",
            "Expected files matching "
            f"{list(SIGNED_PAYLOAD_FILES)} under both source roots.\n"
            f"macOS matched  : {sorted(p.name for d in swift_payload_files.values() for p in d)}\n"
            f"Windows matched: {sorted(p.name for d in cs_payload_files.values() for p in d)}\n"
            "The gate cannot verify the signed wire contract, so it reports failure, "
            "never a pass.",
        )
    else:
      for tag in SIGNED_PAYLOAD_FILES:
        mac_src, win_src = swift_payload_files[tag], cs_payload_files[tag]
        if not mac_src or not win_src:
            r.fail(
                f"signed-payload source missing ({tag})",
                f"{tag} exists on one platform but not the other — the signed contract has no twin.\n"
                f"  macOS   : {sorted(p.name for p in mac_src) or 'ABSENT'}\n"
                f"  Windows : {sorted(p.name for p in win_src) or 'ABSENT'}",
            )
            continue
        swift_keys = collect_swift_wire_keys(mac_src)
        cs_keys = collect_csharp_wire_keys(win_src)
        for key in sorted(REQUIRED_SIGNED_PAYLOAD_KEYS):
            in_mac, in_win = key in swift_keys, key in cs_keys
            if in_mac and in_win:
                r.ok(
                    f"{tag}: signed key {key!r} present on both "
                    f"[macOS {rel(swift_keys[key], repo)}; Windows {rel(cs_keys[key], repo)}]"
                )
                continue
            lines = [f"Contract file: {tag}",
                f"The Ed25519-signed JSON key {key!r} is missing on "
                + ("Windows." if in_mac else "macOS." if in_win else "BOTH platforms."),
                f"  macOS   : {'present at ' + rel(swift_keys[key], repo) if in_mac else 'ABSENT'}",
                f"  Windows : {'present at ' + rel(cs_keys[key], repo) if in_win else 'ABSENT'}",
                "Renaming a signed key changes the signed bytes, so a report signed by one "
                "platform fails verification on the other and surfaces to the user as "
                "'signature invalid' — indistinguishable from tampering.",
                f"  macOS keys seen  : {sorted(swift_keys)}",
                f"  Windows keys seen: {sorted(cs_keys)}",
            ]
            r.fail(f"signed-payload wire key missing ({tag}.{key})", "\n".join(lines))
    print()

    # -- conclusion ------------------------------------------------------------
    if r.failures:
        print(f"PARITY GATE FAILED — {r.failures} divergence(s). Build is RED.")
        return 1
    print("PARITY GATE PASSED — span names and wire vocabularies agree across platforms.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""CLI surface parity gate (track cross-platform-parity_20260805, Tasks 3.1 and 3.2).

WHY THIS EXISTS
---------------
`syxx` ships as two independent implementations of one advertised command-line
contract: a hand-rolled Swift dispatcher (macOS) and a hand-rolled C# dispatcher
(Windows and Linux). Nothing mechanical has ever held the two argument surfaces
together. Task 3.4 executed both binaries and found five divergence classes by
hand (cli-capture-report.md, D1-D5) — a flag advertised on one platform and
absent on the other, an action word that exists only on macOS, and a top-level
short flag that means "version" on one platform and "verbose" on the other
(findings.md F-08). Every one of those shipped green.

This gate extracts both surfaces FROM SOURCE and compares them, so a sixth
divergence cannot be introduced silently. It is the CLI analogue of
`cross-platform-parity-gate.py`, which polices span names and wire vocabularies.

THE THREE LEVELS, KEPT APART
----------------------------
`syxx --help`, `syxx nslook --help`, and `syxx witness --gen-keypair --force` are
answered by three different code paths. Pooling them would let a flag missing at
one level be covered by its twin at another, so each is measured separately.

  TOP LEVEL       command names, flag literals, and the unknown-command exit
                  code, read out of each dispatcher's own switch block.

  GLOBAL FLAGS    the shared `--help`/`--version` handler every subcommand calls
                  (SyxxConfig.handleGlobalFlags / SyxxConfig.HandleGlobalFlags).
                  One handler per platform, so it is compared once rather than
                  fifteen times.

  PER COMMAND     for each subcommand, three separate surfaces:
                    ACCEPTED   — a flag literal the parser actually tests argv
                                 against: an operand of ==/!=/.Equals, or an
                                 argument to contains/hasPrefix/StartsWith.
                    ADVERTISED — a flag token inside the command's usage text,
                                 i.e. what `<command> --help` shows the user.
                    ACTIONS    — action words (`verify`, `create`, `status`),
                                 from `case`/`==`/`.Equals` operands.

ACCEPTED and ADVERTISED are kept apart because the D3 divergences live in the gap
between them. `witness --force` is ACCEPTED on both platforms; only the Windows
usage text mentions it. A gate that pooled the two would report that divergence
as absent and the capture that found it as mistaken.

Both distinctions are drawn on context, not on symbols. A flag-shaped literal is
only ACCEPTED when something compares argv to it, so the `"-n"` in
`p.arguments = ["-n", "get", "default"]` (SyxxCage.swift:447 — an argument to a
subprocess, not to `syxx`) is not mistaken for a flag this CLI answers. A flag is
only ADVERTISED when it appears in a literal containing `Usage:`, so the
`--force` in "Use --force to overwrite" — an error message, not help text — does
not silently satisfy the check that macOS advertises it.

WHY IT MATCHES ON STRINGS, NOT SYMBOLS
--------------------------------------
`NslookCLI` / `NslookCommand`, `SyxxWitnessCLI` / `WitnessCommand` — the type
names already differ across platforms, and renaming them is nobody's business but
the platform's. What may not change silently is the text a user types. So nothing
here is looked up by symbol: commands come from the dispatch case literals, flags
from string literals, and a source file is bound to the command it implements by
the tool-name literal it passes to the global-flag handler ("syxx nslook") —
never by its filename or its type name.

DEAD CASES
----------
Both dispatchers lowercase the incoming token before the switch
(SyxxCLI.swift:48, Program.cs:73). A `case` literal containing an uppercase
letter can therefore never match. The gate resolves the switch subject — through
its local binding when the switch reads a variable rather than the expression —
and reports every such literal as DEAD, excluding it from the effective surface.
This is what makes the F-08 addendum mechanical: macOS's top-level `-V` is
present in the switch and cannot fire, so macOS has no working short version flag
at top level while Windows does.

ALIAS GROUPS
------------
A flag present on both platforms can still mean different things. `-v` is
accepted by both global-flag handlers; on Windows it is OR'd with `--version` and
on macOS with `--verbose`. A set comparison calls that identical. So the gate also
records which flags each handler tests TOGETHER in one condition, and reports a
flag whose alias group differs. That is the subcommand half of F-08, which a flat
comparison cannot see.

LEDGERED EXCEPTIONS
-------------------
The asymmetries already found and filed are listed in LEDGERED_EXCEPTIONS below,
each carrying the finding or divergence id that documents it. A matched entry is
reported LEDGERED and does not fail the build.

An entry that matches NOTHING in the extracted surface is a STALE exception and
FAILS. An exception that outlives its divergence is worse than no exception: it is
a standing permission to reintroduce the defect, and it makes the ledger read as
describing a tree it no longer describes.

EXIT CODES
----------
  0  the two surfaces agree, modulo the ledgered exceptions
  1  an unledgered asymmetry, or a stale exception (build must go red)
  2  the gate could not run — bad paths, no dispatch switch, an exit code it
     could not resolve, or an extraction too small to be plausible. NEVER 0.

USAGE
-----
  scripts/cli-surface-gate.py [--repo-root PATH]
                              [--swift-cli-root PATH] [--csharp-cli-root PATH]
                              [--github]

`--swift-cli-root` / `--csharp-cli-root` point the extractor at a copy of a tree.
They exist so the gate can be shown going red against a mutated copy without the
real tree being touched. `--github` adds `::error` annotations; it does not change
the exit code — the exit code is the gate.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

# ---------------------------------------------------------------------------
# Scope: the two CLI implementations, and the two global-flag handlers the
# subcommands share. The handlers live outside the CLI trees, so they are read
# from the repo root and are NOT redirected by --swift-cli-root: a mutation demo
# points at a copied dispatcher, and the shared handler stays honest.
# ---------------------------------------------------------------------------
SWIFT_CLI_ROOT = "macOS/Sources/SyxxCLI"
SWIFT_DISPATCH = "SyxxCLI.swift"          # relative to the Swift CLI root
SWIFT_GLOBAL_FLAGS = "macOS/Sources/SyxxKit/SyxxConfig.swift"

CSHARP_CLI_ROOT = "Windows/dotNET/Syxx"
CSHARP_DISPATCH = "Program.cs"            # relative to the C# CLI root
CSHARP_GLOBAL_FLAGS = "Windows/dotNET/Syxx.Kit/SyxxConfig.cs"

GLOBAL_LEVEL = "<global-flags>"

# Anti-vacuity floors. Both dispatchers register 15 user-facing subcommands plus
# the CI-only `parity-transcript`. An extraction that finds fewer than 15 has
# stopped matching the source, and must say so rather than report a clean surface.
MIN_COMMANDS = 15

MAC, WIN, BOTH = "macOS", "Windows", "both"


# ---------------------------------------------------------------------------
# Ledgered exceptions — asymmetries that are already filed.
#
# `finding` is the record that documents the divergence. `why` says what the
# asymmetry costs, so a reader of the gate output does not have to open the
# ledger to judge whether the entry is still acceptable.
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class Ledgered:
    level: str        # "top" | "<global-flags>" | a command name
    dimension: str    # command | topflag | deadcase | globalflag | alias
    token: str        # | accepted | advertised | action | usage
    side: str         # the platform that HAS it (or "both" for an alias split)
    finding: str
    why: str

    @property
    def key(self) -> tuple[str, str, str, str]:
        return (self.level, self.dimension, self.token, self.side)


LEDGERED_EXCEPTIONS: tuple[Ledgered, ...] = (
    Ledgered(
        level="top",
        dimension="topflag",
        token="-v",
        side=WIN,
        finding="F-08",
        why=(
            "`syxx -v` prints a version on Windows/Linux (E088) and is the verbose flag on "
            "macOS (B240). Two documented decisions in direct conflict; resolving it changes a "
            "shipped flag's meaning on at least one platform, so it is owner-gated "
            "(plan Task 8.8), not agent-fixable."
        ),
    ),
    Ledgered(
        level="top",
        dimension="deadcase",
        token="-V",
        side=MAC,
        finding="F-08 addendum (2026-08-05)",
        why=(
            "SyxxCLI.swift lowercases args[1] before the switch, so the `-V` arm cannot fire. "
            "Proven by execution: `syxx -V` exits 3 'Unknown subcommand'. macOS therefore has NO "
            "working short version flag at top level. Fix is part of Task 8.8."
        ),
    ),
    Ledgered(
        level="top",
        dimension="deadcase",
        token="-V",
        side=WIN,
        finding="F-08 addendum (2026-08-05)",
        why=(
            "Program.cs lowercases args[0] the same way, so the `-V` arm is equally dead here. It "
            "is harmless on this platform only because the `-v` arm above it accepts the "
            "lowercased token. Removing `-v` without removing `-V` would silently drop both."
        ),
    ),
    Ledgered(
        level=GLOBAL_LEVEL,
        dimension="globalflag",
        token="--verbose",
        side=MAC,
        finding="F-08",
        why=(
            "C# has no verbose flag at all, which is what makes E088's cost invisible today: "
            "handing `-v` to --version costs nothing until Windows gains a verbose flag and "
            "cannot spell it the way macOS does."
        ),
    ),
    Ledgered(
        level=GLOBAL_LEVEL,
        dimension="alias",
        token="--version,-V,-v",
        side=BOTH,
        finding="F-08",
        why=(
            "All three flags are accepted by both handlers, so a flat comparison calls them "
            "identical. They are not: `-v` is the flag that moved. macOS groups it with --verbose "
            "and keeps the version group to {--version, -V}; Windows folds it into the version "
            "group. `syxx nslook -v` prints a version string on one platform and enables verbose "
            "output on the other."
        ),
    ),
    Ledgered(
        level="witness",
        dimension="advertised",
        token="--force",
        side=WIN,
        finding="D3 (cli-capture-report.md)",
        why=(
            "Both platforms ACCEPT --force on --gen-keypair; only the Windows usage text mentions "
            "it. A macOS user reading the help has no way to learn the escape hatch from the "
            "'private key already exists' refusal — which names --force in its error text."
        ),
    ),
    Ledgered(
        level="cage",
        dimension="advertised",
        token="--output",
        side=WIN,
        finding="D3 (cli-capture-report.md)",
        why=(
            "`cage` advertises [--output <dir>] on Windows only, though both platforms accept the "
            "flag. Captured 2026-08-05 by executing both builds' `cage --help`."
        ),
    ),
    Ledgered(
        level="unbound",
        dimension="action",
        token="verify-dnscrypt",
        side=MAC,
        finding="D3 (cli-capture-report.md)",
        why=(
            "The Unbound -> dnscrypt-proxy -> Anonymized-DNSCrypt chain proof exists only on "
            "macOS. `syxx unbound verify-dnscrypt` on Windows/Linux falls to the unknown-action "
            "branch."
        ),
    ),
    Ledgered(
        level="firstrun",
        dimension="usage",
        token="usage-text",
        side=WIN,
        finding="D5 (cli-capture-report.md)",
        why=(
            "macOS `firstrun` carries no usage literal because it never calls the global-flag "
            "handler: `syxx firstrun --help` does not print help, it starts the live five-minute "
            "scan (captured 2026-08-05, killed at 8s by SIGALRM). Asking for help must never "
            "start a network scan. The advertised-flag comparison for `firstrun` is suppressed "
            "while this holds — there is no macOS help text to compare against."
        ),
    ),
    Ledgered(
        level="top",
        dimension="command",
        token="guided",
        side=WIN,
        finding="F-10",
        why=(
            "`guided` is dispatched as an explicit command token on Windows (Program.cs:66); "
            "macOS enters guided mode only on zero args, so `syxx guided` exits 3 there. Filed "
            "2026-08-05; remediation is a shipped-surface change, owner-gated."
        ),
    ),
    Ledgered(
        level=GLOBAL_LEVEL,
        dimension="globalflag",
        token="--json",
        side=MAC,
        finding="F-11",
        why=(
            "Machine-readable output exists only on macOS (SyxxConfig.swift:72). High-severity "
            "output-contract asymmetry; adding a C# twin is a feature, not a flag alias, so it is "
            "filed rather than fixed in this track."
        ),
    ),
    Ledgered(
        level="cage",
        dimension="accepted",
        token="--interface",
        side=MAC,
        finding="F-12",
        why=(
            "Interface selection exists only where libpcap capture does. User-visible edge of the "
            "capture-mechanism split (command-triage.md difference 6)."
        ),
    ),
    Ledgered(
        level="cage",
        dimension="accepted",
        token="-i",
        side=MAC,
        finding="F-12",
        why="Short alias of --interface; same capture-mechanism family.",
    ),
    Ledgered(
        level="cage",
        dimension="accepted",
        token="--owner-uid",
        side=MAC,
        finding="F-12",
        why=(
            "Output-ownership control for sudo-created files is a macOS-only concept in cage "
            "(SyxxCage.swift:95); the raw-socket Windows path never drops privileges this way."
        ),
    ),
    Ledgered(
        level="cage",
        dimension="accepted",
        token="--owner-gid",
        side=MAC,
        finding="F-12",
        why="Companion of --owner-uid (SyxxCage.swift:104); same rationale.",
    ),
    Ledgered(
        level="cage",
        dimension="accepted",
        token="--out",
        side=WIN,
        finding="F-12",
        why=(
            "Windows-only legacy alias for --output (CageCommand.cs:71, D108 note at :55-70). "
            "Carried in the ledger so the alias cannot silently spread; the D108 comment alone "
            "carries no finding id."
        ),
    ),
    Ledgered(
        level="firstrun",
        dimension="accepted",
        token="--local",
        side=MAC,
        finding="F-13",
        why=(
            "macOS reserves the flag and refuses with 'not yet supported' (FirstRunScan.swift:38); "
            "Windows calls it an unknown option. Same exit code, different contract and different "
            "message bytes."
        ),
    ),
    Ledgered(
        level="firstrun",
        dimension="accepted",
        token="-l",
        side=MAC,
        finding="F-13",
        why="Short alias of --local; same divergence.",
    ),
)


# ---------------------------------------------------------------------------
# Comment stripping.
#
# Naive regex stripping corrupts string literals containing `//` (there are URLs
# and `https://` prose in both trees) and would make the gate lie about what the
# source says. Both strippers are escape-aware scanners that preserve string
# literals byte-for-byte and blank out only real comments. They are also
# LENGTH-PRESERVING, so a byte offset in the stripped text is the same offset in
# the original and line numbers need no correction.
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
# String-literal scanning.
#
# A regex cannot do this correctly: Swift `"""` blocks and C# `@"..."` verbatim
# blocks are where BOTH platforms keep their multi-line usage text, and a
# single-line regex mis-lexes them — which is precisely the text this gate has to
# read to know what each command advertises.
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class Lit:
    body: str
    start: int  # offset of the opening quote


def swift_literals(text: str) -> list[Lit]:
    out: list[Lit] = []
    i, n = 0, len(text)
    while i < n:
        if text.startswith('"""', i):
            end = text.find('"""', i + 3)
            if end < 0:
                break
            out.append(Lit(text[i + 3 : end], i))
            i = end + 3
            continue
        if text[i] == '"':
            j, buf = i + 1, []
            while j < n and text[j] not in '"\n':
                if text[j] == "\\" and j + 1 < n:
                    buf.append(text[j : j + 2])
                    j += 2
                    continue
                buf.append(text[j])
                j += 1
            if j < n and text[j] == '"':
                out.append(Lit("".join(buf), i))
                i = j + 1
                continue
        i += 1
    return out


def csharp_literals(text: str) -> list[Lit]:
    out: list[Lit] = []
    i, n = 0, len(text)
    while i < n:
        if text[i] in '@$"':
            k, verbatim = i, False
            while k < n and text[k] in "@$":
                verbatim = verbatim or text[k] == "@"
                k += 1
            if k < n and text[k] == '"':
                j, buf = k + 1, []
                if verbatim:
                    while j < n:
                        if text.startswith('""', j):
                            buf.append('"')
                            j += 2
                            continue
                        if text[j] == '"':
                            break
                        buf.append(text[j])
                        j += 1
                else:
                    while j < n and text[j] not in '"\n':
                        if text[j] == "\\" and j + 1 < n:
                            buf.append(text[j : j + 2])
                            j += 2
                            continue
                        buf.append(text[j])
                        j += 1
                if j < n and text[j] == '"':
                    out.append(Lit("".join(buf), i))
                    i = j + 1
                    continue
        i += 1
    return out


# ---------------------------------------------------------------------------
# Shapes and contexts
# ---------------------------------------------------------------------------
LONG_FLAG_TOKEN = re.compile(r"(?<![\w-])(--[A-Za-z][A-Za-z0-9-]*=?)")
SHORT_FLAG_TOKEN = re.compile(r"(?<![\w-])(-[A-Za-z])(?![\w-])")
EXACT_LONG_FLAG = re.compile(r"^--[A-Za-z][A-Za-z0-9-]*=?$")
EXACT_SHORT_FLAG = re.compile(r"^-[A-Za-z]$")
ACTION_WORD_SHAPE = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")
LOWERCASING = re.compile(r"lowercased\(\)|ToLowerInvariant\(\)|ToLower\(\)")

# A literal is only a flag this CLI ANSWERS when something tests argv against it.
# Membership and prefix tests count (`args.contains("--force")`,
# `$0.hasPrefix("--out=")`); a bare literal in an array does not, which is what
# keeps a subprocess's `["-n", "get", "default"]` out of the surface.
_MEMBERSHIP = r"contains|Contains|hasPrefix|StartsWith|hasSuffix|EndsWith"
_EQUALITY = (
    r"(?:==|!=)\s*$"
    # `cmd.Equals("create", ...)` and the static `string.Equals(a, "--list-only", ...)`
    # — the literal is the first argument in one form and the second in the other,
    # and both trees use both.
    r"|\.(?:Equals|equals)\s*\(\s*$"
    r'|\.(?:Equals|equals)\s*\(\s*[^,;{}"]{1,80},\s*$'
    r"|\bcase\s+$"
    r'|\bcase\s+(?:"(?:[^"\\]|\\.)*"\s*,\s*)+$'
)
EQUALITY_PREFIX = re.compile(_EQUALITY)
ACCEPT_PREFIX = re.compile(_EQUALITY + rf"|\.(?:{_MEMBERSHIP})\s*\(\s*$")
EQUALITY_SUFFIX = re.compile(r"^\s*(?:==|!=)")

# `Usage:` marks help text. Every usage string in both trees starts a line with
# it, including the multi-line `"""` / `@"..."` blocks, so one test identifies the
# help surface without the gate knowing which function prints it.
USAGE_MARKER = re.compile(r"\bUsage:")


def line_of(text: str, idx: int) -> int:
    return text.count("\n", 0, idx) + 1


def norm_flag(token: str) -> str:
    """`--out=` and `--out` name one flag with two value idioms."""
    return token.removesuffix("=")


def in_context(text: str, lit: Lit, prefix: re.Pattern[str]) -> bool:
    before = text[max(0, lit.start - 160) : lit.start]
    after = text[lit.start + len(lit.body) + 2 : lit.start + len(lit.body) + 12]
    return bool(prefix.search(before) or EQUALITY_SUFFIX.match(after))


def case_arm_literals(block: str) -> list[tuple[str, int]]:
    """Every string literal that is a `case` operand, with its offset in `block`.

    Reads from each `case` keyword to the `:` that ends the arm, so Swift's
    comma-separated form (`case "--version", "-V":`) and C#'s stacked form
    (`case "-v": case "-V":`) both yield all of their literals.
    """
    single = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
    found: list[tuple[str, int]] = []
    for m in re.finditer(r"\bcase\b", block):
        colon = block.find(":", m.end())
        while colon >= 0 and block[m.end() : colon].count('"') % 2 == 1:
            colon = block.find(":", colon + 1)
        if colon < 0:
            continue
        for lm in single.finditer(block[m.end() : colon]):
            found.append((lm.group(1), m.end() + lm.start()))
    return found


# ---------------------------------------------------------------------------
# Source loading
# ---------------------------------------------------------------------------
class CannotRun(Exception):
    """The gate could not measure something it was asked to measure."""


@dataclass
class Source:
    path: Path
    raw: str
    text: str                       # comments blanked, length-preserved
    lits: list[Lit] = field(default_factory=list)

    def at(self, idx: int) -> str:
        return f"{self.path}:{line_of(self.text, idx)}"


def load(path: Path, stripper, lexer) -> Source:
    try:
        raw = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise CannotRun(f"cannot read {path}: {exc}") from exc
    text = stripper(raw)
    return Source(path, raw, text, lexer(text))


def read_tree(root: Path, suffix: str, stripper, lexer, label: str) -> list[Source]:
    if not root.is_dir():
        raise CannotRun(f"{label} source root not found: {root}")
    out = [load(p, stripper, lexer) for p in sorted(root.rglob(f"*{suffix}"))]
    if not out:
        raise CannotRun(f"{label} tree {root} contains no {suffix} files")
    return out


# ---------------------------------------------------------------------------
# Top-level dispatch surface (Task 3.1a)
# ---------------------------------------------------------------------------
@dataclass
class TopSurface:
    platform: str
    commands: dict[str, str] = field(default_factory=dict)   # name -> file:line
    flags: dict[str, str] = field(default_factory=dict)      # reachable flags only
    dead: dict[str, str] = field(default_factory=dict)       # unreachable case arms
    lowercased: bool = False
    subject: str = ""
    lowercase_evidence: str = ""
    unknown_exit: int | None = None
    unknown_exit_at: str = ""


def find_dispatch_block(src: Source, platform: str) -> tuple[str, int, str]:
    """The switch block that answers `syxx <token>`, plus its subject expression.

    Identified by CONTENT, not by position or by the name of the variable it
    switches on: the top-level dispatcher is the switch whose arms include both
    `--help` and `--version`. Both files contain other string switches (the
    interactive menu), and either dispatcher could be reordered or extracted into
    a helper without this needing to change.
    """
    for m in re.finditer(r"\bswitch\b", src.text):
        open_idx = src.text.find("{", m.end())
        if open_idx < 0:
            continue
        close_idx = match_braces(src.text, open_idx)
        if close_idx < 0:
            continue
        block = src.text[open_idx:close_idx]
        arms = {lit for lit, _ in case_arm_literals(block)}
        if "--help" in arms and "--version" in arms:
            return block, open_idx, src.text[m.end() : open_idx]
    raise CannotRun(
        f"{platform}: no top-level dispatch switch found in {src.path}. Expected a switch whose "
        "arms include both '--help' and '--version'. The dispatcher moved, or the gate's locator "
        "is stale — either way the surface was not measured."
    )


def resolve_lowercasing(src: Source, subject: str) -> tuple[bool, str]:
    """Does the switch see a lowercased token? Follows one local binding if needed.

    C# switches on the expression (`args[0].ToLowerInvariant()`); Swift binds it
    first (`let subcommand = args[1].lowercased()`) and switches on the name. Only
    following the binding makes the two comparable — without it macOS reports zero
    dead arms and its unreachable `-V` reads as a live flag Windows is missing.
    """
    if LOWERCASING.search(subject):
        return True, subject.strip()
    m = re.fullmatch(r"[\s()]*([A-Za-z_]\w*)[\s()]*", subject)
    if not m:
        return False, ""
    ident = m.group(1)
    bind = re.search(
        rf"\b(?:let|var|string\??|readonly)\s+{re.escape(ident)}\s*=\s*([^\n;{{]*)", src.text
    )
    if bind and LOWERCASING.search(bind.group(1)):
        return True, f"{ident} = {bind.group(1).strip()} at {src.at(bind.start())}"
    return False, ""


def resolve_swift_int_enum(sources: list[Source]) -> dict[tuple[str, str], int]:
    """`enum CLIExitStatus: Int32 { case failure = 3 }` -> {("CLIExitStatus","failure"): 3}.

    Swift assigns the unknown-command exit code through a named enum case, so the
    literal is one hop away. The VALUE is still a literal — this resolves the hop
    without the gate ever asserting on the enum's name.
    """
    table: dict[tuple[str, str], int] = {}
    decl = re.compile(r"\benum\s+(\w+)\s*:\s*([^{}\n]*?)\{")
    for src in sources:
        for m in decl.finditer(src.text):
            if not re.search(r"\bInt\d*\b", m.group(2)):
                continue
            open_idx = src.text.index("{", m.start())
            close_idx = match_braces(src.text, open_idx)
            if close_idx < 0:
                continue
            body = src.text[open_idx + 1 : close_idx - 1]
            for cm in re.finditer(r"\bcase\s+(\w+)\s*=\s*(-?\d+)", body):
                table[(m.group(1), cm.group(1))] = int(cm.group(2))
    return table


def extract_top_surface(
    platform: str,
    dispatcher: Source,
    exit_assign: re.Pattern[str],
    enum_table: dict[tuple[str, str], int] | None,
) -> TopSurface:
    block, block_at, subject = find_dispatch_block(dispatcher, platform)
    surf = TopSurface(platform=platform, subject=subject.strip())
    surf.lowercased, surf.lowercase_evidence = resolve_lowercasing(dispatcher, subject)

    for lit, off in case_arm_literals(block):
        loc = f"{dispatcher.path}:{line_of(dispatcher.text, block_at + off)}"
        # A lowercased subject can never equal a literal carrying an uppercase
        # letter: the arm is present in source and unreachable at runtime.
        if surf.lowercased and lit != lit.lower():
            surf.dead[lit] = loc
        elif lit.startswith("-"):
            surf.flags[norm_flag(lit)] = loc
        else:
            surf.commands[lit] = loc

    # Commands dispatched OUTSIDE the switch — C# answers `guided` with an
    # `args[0]` equality test before the switch is reached, and a switch-scoped
    # extraction would miss it entirely.
    for m in re.finditer(
        r'args\s*\[\s*[01]\s*\][^;{}\n]*?"((?:[^"\\\n]|\\.)*)"'
        r'|"((?:[^"\\\n]|\\.)*)"[^;{}\n]*?args\s*\[\s*[01]\s*\]',
        dispatcher.text,
    ):
        lit = m.group(1) if m.group(1) is not None else m.group(2)
        if lit and not lit.startswith("-") and ACTION_WORD_SHAPE.match(lit):
            surf.commands.setdefault(lit, f"{dispatcher.path}:{line_of(dispatcher.text, m.start())}")

    default_at = block.rfind("default")
    if default_at < 0:
        raise CannotRun(
            f"{platform}: dispatch switch has no `default` arm — unknown-command behaviour is "
            "unmeasured"
        )
    am = exit_assign.search(block[default_at:])
    if not am:
        raise CannotRun(
            f"{platform}: could not read the unknown-command exit code from the `default` arm "
            f"({dispatcher.path}:{line_of(dispatcher.text, block_at + default_at)}). The gate "
            "reports CANNOT-RUN rather than assuming the contract holds."
        )
    surf.unknown_exit_at = (
        f"{dispatcher.path}:{line_of(dispatcher.text, block_at + default_at + am.start())}"
    )
    if am.group(1):
        surf.unknown_exit = int(am.group(1))
    else:
        if enum_table is None:
            raise CannotRun(
                f"{platform}: unknown-command exit code is not an integer literal and no enum "
                "table was supplied"
            )
        key = (am.group(2), am.group(3))
        if key not in enum_table:
            raise CannotRun(
                f"{platform}: unknown-command exit code is {key[0]}.{key[1]}, which no Int-backed "
                "enum in the tree defines. The code was not measured."
            )
        surf.unknown_exit = enum_table[key]
    return surf


# ---------------------------------------------------------------------------
# Shared global-flag handler
# ---------------------------------------------------------------------------
@dataclass
class GlobalSurface:
    flags: dict[str, str] = field(default_factory=dict)
    groups: dict[str, frozenset[str]] = field(default_factory=dict)


def extract_global_flags(src: Source) -> GlobalSurface:
    """Flags the shared `--help`/`--version` handler accepts, and their alias groups.

    An alias group is the set of flags tested together in one condition. Both
    handlers write each condition on one line, so co-occurrence on a line is the
    grouping signal. It is a proxy for "means the same thing", and it is the only
    reason `-v` — accepted by both — does not read as agreement.
    """
    surf = GlobalSurface()
    by_line: dict[int, set[str]] = {}
    for lit in src.lits:
        if not (EXACT_LONG_FLAG.match(lit.body) or EXACT_SHORT_FLAG.match(lit.body)):
            continue
        if not in_context(src.text, lit, ACCEPT_PREFIX):
            continue
        flag = norm_flag(lit.body)
        line = line_of(src.text, lit.start)
        surf.flags.setdefault(flag, f"{src.path}:{line}")
        by_line.setdefault(line, set()).add(flag)
    for flag in surf.flags:
        group: set[str] = set()
        for members in by_line.values():
            if flag in members:
                group |= members
        surf.groups[flag] = frozenset(group)
    return surf


# ---------------------------------------------------------------------------
# Per-command surface (Task 3.1b)
# ---------------------------------------------------------------------------
@dataclass
class CommandSurface:
    accepted: dict[str, str] = field(default_factory=dict)
    advertised: dict[str, str] = field(default_factory=dict)
    actions: dict[str, str] = field(default_factory=dict)
    spellings: dict[str, set[str]] = field(default_factory=dict)
    has_usage: bool = False
    files: list[Path] = field(default_factory=list)


def bind_files_to_commands(
    tree: list[Source], dispatcher: Source, commands: list[str]
) -> dict[str, list[Source]]:
    """Which source file implements which subcommand — decided on strings only.

    Three tiers, strongest first. Nothing here reads a filename or a type name, so
    `SyxxNSLook.swift` -> `NslookCommand.cs` -> anything else is free.

      1. the tool-name literal the file hands the shared global-flag handler
         ("syxx nslook"), which is also the text that command prints for --help
      2. a `Usage:` line naming the command — reads RAW text, because the only
         place macOS documents `firstrun`'s flags is a doc comment, and firstrun
         is precisely the command that never calls the global-flag handler (D5)
      3. a diagnostic literal prefixed with the command name ("parity-transcript:
         write failed"), which is all the transcript emitters carry
    """
    bound: dict[str, list[Source]] = {c: [] for c in commands}
    for src in tree:
        if src.path == dispatcher.path:
            continue
        for cmd in commands:
            esc = re.escape(cmd)
            if (
                re.search(rf'"syxx[ -]{esc}"', src.text)
                or re.search(rf"Usage:\s*`?(?:\w+ +)*syxx[ -]{esc}\b", src.raw)
                or re.search(rf'"{esc}:\s', src.text)
            ):
                bound[cmd].append(src)
    return bound


def extract_command_surface(files: list[Source]) -> CommandSurface:
    surf = CommandSurface(files=[f.path for f in files])
    for src in files:
        for lit in src.lits:
            body, at = lit.body, src.at(lit.start)
            is_flag = bool(EXACT_LONG_FLAG.match(body) or EXACT_SHORT_FLAG.match(body))

            if is_flag and in_context(src.text, lit, ACCEPT_PREFIX):
                key = norm_flag(body)
                surf.accepted.setdefault(key, at)
                surf.spellings.setdefault(key, set()).add(body)
                continue

            if not is_flag and USAGE_MARKER.search(body):
                # Help text. Every flag named here is part of the surface the user
                # is shown, whether or not the parser still honours it.
                surf.has_usage = True
                for fm in LONG_FLAG_TOKEN.finditer(body):
                    key = norm_flag(fm.group(1))
                    surf.advertised.setdefault(key, at)
                    surf.spellings.setdefault(key, set()).add(fm.group(1))
                for fm in SHORT_FLAG_TOKEN.finditer(body):
                    surf.advertised.setdefault(fm.group(1), at)
                continue

            if (
                not is_flag
                and len(body) > 1
                and ACTION_WORD_SHAPE.match(body)
                and in_context(src.text, lit, EQUALITY_PREFIX)
            ):
                surf.actions.setdefault(body, at)
    return surf


# ---------------------------------------------------------------------------
# Comparison and reporting (Task 3.2)
# ---------------------------------------------------------------------------
@dataclass
class Asymmetry:
    level: str
    dimension: str
    token: str
    side: str
    evidence: str
    other: str = "absent"

    @property
    def key(self) -> tuple[str, str, str, str]:
        return (self.level, self.dimension, self.token, self.side)


def diff_maps(level: str, dim: str, mac: dict[str, str], win: dict[str, str]) -> list[Asymmetry]:
    out = [Asymmetry(level, dim, t, MAC, mac[t]) for t in sorted(set(mac) - set(win))]
    out += [Asymmetry(level, dim, t, WIN, win[t]) for t in sorted(set(win) - set(mac))]
    return out


class Report:
    def __init__(self, github: bool) -> None:
        self.github = github
        self.failures: list[str] = []

    def head(self, title: str) -> None:
        print(f"\n{title}")
        print("-" * len(title))

    def ok(self, text: str) -> None:
        print(f"  PASS      {text}")

    def fail(self, title: str, detail: str) -> None:
        self.failures.append(title)
        print(f"  FAIL      {title}")
        for line in detail.splitlines():
            print(f"            {line}")
        if self.github:
            print(f"::error title=CLI surface gate: {title}::{detail.replace(chr(10), '%0A')}")


def rel(loc: str, repo: Path) -> str:
    return loc.replace(str(repo) + "/", "")


def wrap(text: str, width: int) -> list[str]:
    lines, cur = [], ""
    for w in text.split():
        if cur and len(cur) + 1 + len(w) > width:
            lines.append(cur)
            cur = w
        else:
            cur = f"{cur} {w}" if cur else w
    if cur:
        lines.append(cur)
    return lines


# ---------------------------------------------------------------------------
def run(args: argparse.Namespace) -> int:
    repo = Path(args.repo_root).resolve() if args.repo_root else Path(__file__).resolve().parent.parent
    swift_root = Path(args.swift_cli_root).resolve() if args.swift_cli_root else repo / SWIFT_CLI_ROOT
    cs_root = Path(args.csharp_cli_root).resolve() if args.csharp_cli_root else repo / CSHARP_CLI_ROOT

    swift_tree = read_tree(swift_root, ".swift", strip_swift_comments, swift_literals, "macOS SyxxCLI")
    cs_tree = read_tree(cs_root, ".cs", strip_csharp_comments, csharp_literals, "Windows Syxx")

    for p, label in (
        (repo / SWIFT_GLOBAL_FLAGS, "macOS SyxxConfig"),
        (repo / CSHARP_GLOBAL_FLAGS, "Windows SyxxConfig"),
    ):
        if not p.is_file():
            raise CannotRun(f"{label} not found at {p}; subcommand global flags are unmeasured")
    swift_globals = load(repo / SWIFT_GLOBAL_FLAGS, strip_swift_comments, swift_literals)
    cs_globals = load(repo / CSHARP_GLOBAL_FLAGS, strip_csharp_comments, csharp_literals)

    swift_dispatch = next((s for s in swift_tree if s.path.name == SWIFT_DISPATCH), None)
    cs_dispatch = next((s for s in cs_tree if s.path.name == CSHARP_DISPATCH), None)
    if swift_dispatch is None:
        raise CannotRun(f"macOS dispatcher {SWIFT_DISPATCH} not found under {swift_root}")
    if cs_dispatch is None:
        raise CannotRun(f"Windows dispatcher {CSHARP_DISPATCH} not found under {cs_root}")

    print("Syxx CLI surface parity gate — track cross-platform-parity_20260805, Tasks 3.1/3.2")
    print(f"  macOS   : {len(swift_tree):>3} .swift under {rel(str(swift_root), repo)}  "
          f"(dispatcher {swift_dispatch.path.name}, globals {Path(SWIFT_GLOBAL_FLAGS).name})")
    print(f"  Windows : {len(cs_tree):>3} .cs    under {rel(str(cs_root), repo)}  "
          f"(dispatcher {cs_dispatch.path.name}, globals {Path(CSHARP_GLOBAL_FLAGS).name})")

    mac_top = extract_top_surface(
        MAC, swift_dispatch,
        re.compile(r"exitCode\s*=\s*(?:(-?\d+)|(\w+)\s*\.\s*(\w+)\s*\.\s*rawValue)"),
        resolve_swift_int_enum(swift_tree),
    )
    win_top = extract_top_surface(
        WIN, cs_dispatch, re.compile(r"ExitCode\s*=\s*(-?\d+)"), None
    )

    # Anti-vacuity, checked BEFORE any comparison: two empty surfaces match
    # perfectly, and reporting that as a pass is the exact defect this gate and
    # its sibling exist to refuse.
    for surf in (mac_top, win_top):
        if len(surf.commands) < MIN_COMMANDS:
            raise CannotRun(
                f"{surf.platform}: extracted only {len(surf.commands)} top-level command(s) "
                f"({sorted(surf.commands) or 'none'}); at least {MIN_COMMANDS} are registered. "
                "The dispatch pattern no longer matches the source."
            )
        if not surf.flags:
            raise CannotRun(
                f"{surf.platform}: extracted zero top-level flags. A dispatcher that answers no "
                "flags is not plausible; the extraction is broken."
            )

    mac_glob = extract_global_flags(swift_globals)
    win_glob = extract_global_flags(cs_globals)
    for plat, g, src in ((MAC, mac_glob, swift_globals), (WIN, win_glob, cs_globals)):
        if not g.flags:
            raise CannotRun(
                f"{plat}: extracted zero flags from the shared global-flag handler ({src.path}). "
                "Every subcommand's --help/--version goes through it; zero is not plausible."
            )

    all_commands = sorted(set(mac_top.commands) | set(win_top.commands))
    mac_files = bind_files_to_commands(swift_tree, swift_dispatch, all_commands)
    win_files = bind_files_to_commands(cs_tree, cs_dispatch, all_commands)
    mac_cmds = {c: extract_command_surface(mac_files[c]) for c in all_commands}
    win_cmds = {c: extract_command_surface(win_files[c]) for c in all_commands}

    for plat, cmds in ((MAC, mac_cmds), (WIN, win_cmds)):
        total = sum(len(s.accepted) + len(s.advertised) for s in cmds.values())
        if total == 0:
            raise CannotRun(
                f"{plat}: extracted zero per-command flags across {len(all_commands)} commands. "
                "Either every command lost its argument parsing, or the literal scan is broken."
            )
        if not any(s.has_usage for s in cmds.values()):
            raise CannotRun(
                f"{plat}: no command yielded any usage text. The advertised-flag surface — the "
                "half of the contract users actually read — was not measured."
            )

    r = Report(args.github)
    asymmetries: list[Asymmetry] = []

    # -- [1] Top level -----------------------------------------------------
    r.head("[1] Top-level dispatch surface")
    for surf in (mac_top, win_top):
        note = f"lowercased via {surf.lowercase_evidence}" if surf.lowercased else "NOT lowercased"
        print(f"  {surf.platform:<8} switch subject {surf.subject!r} — {rel(note, repo)}")
    print()
    print(f"  {'token':<22} {'macOS':<38} {'Windows'}")
    for token in sorted(set(mac_top.commands) | set(win_top.commands)):
        print(f"  {token:<22} {rel(mac_top.commands.get(token, 'absent'), repo):<38} "
              f"{rel(win_top.commands.get(token, 'absent'), repo)}")
    for token in sorted(set(mac_top.flags) | set(win_top.flags)):
        print(f"  {token:<22} {rel(mac_top.flags.get(token, 'absent'), repo):<38} "
              f"{rel(win_top.flags.get(token, 'absent'), repo)}")
    for token in sorted(set(mac_top.dead) | set(win_top.dead)):
        m = rel(mac_top.dead[token], repo) + " DEAD" if token in mac_top.dead else "absent"
        w = rel(win_top.dead[token], repo) + " DEAD" if token in win_top.dead else "absent"
        print(f"  {token:<22} {m:<38} {w}")
    print()
    for surf in (mac_top, win_top):
        print(f"  {surf.platform:<8}: {len(surf.commands)} commands, {len(surf.flags)} reachable "
              f"flags, {len(surf.dead)} dead arm(s)")

    asymmetries += diff_maps("top", "command", mac_top.commands, win_top.commands)
    asymmetries += diff_maps("top", "topflag", mac_top.flags, win_top.flags)
    for plat, dead in ((MAC, mac_top.dead), (WIN, win_top.dead)):
        for token, loc in sorted(dead.items()):
            asymmetries.append(Asymmetry("top", "deadcase", token, plat, loc, "n/a"))

    # -- [1b] Unknown-command exit code ------------------------------------
    r.head("[1b] Unknown-command exit code")
    print(f"  macOS   : {mac_top.unknown_exit}  ({rel(mac_top.unknown_exit_at, repo)})")
    print(f"  Windows : {win_top.unknown_exit}  ({rel(win_top.unknown_exit_at, repo)})")
    if mac_top.unknown_exit != win_top.unknown_exit:
        r.fail(
            "unknown-command exit code diverges",
            f"macOS exits {mac_top.unknown_exit} at {rel(mac_top.unknown_exit_at, repo)};\n"
            f"Windows exits {win_top.unknown_exit} at {rel(win_top.unknown_exit_at, repo)}.\n"
            "Under the shared contract (0 clean / 1 threat / 2 inconclusive / 3 operational "
            "failure) a typo'd subcommand must read the same on both, or a script that branches "
            "on the exit status draws a different conclusion about the network per platform.",
        )
    else:
        r.ok(f"both exit {mac_top.unknown_exit} on an unknown subcommand")

    # -- [2] Shared global flags -------------------------------------------
    r.head("[2] Shared global-flag handler (every subcommand's --help / --version)")
    print(f"  {'flag':<14} {'macOS alias group':<32} {'Windows alias group'}")
    for token in sorted(set(mac_glob.flags) | set(win_glob.flags)):
        m = ", ".join(sorted(mac_glob.groups[token])) if token in mac_glob.flags else "absent"
        w = ", ".join(sorted(win_glob.groups[token])) if token in win_glob.flags else "absent"
        print(f"  {token:<14} {m:<32} {w}")
    asymmetries += diff_maps(GLOBAL_LEVEL, "globalflag", mac_glob.flags, win_glob.flags)
    # `-v` joining the Windows version group also makes --version and -V "differ",
    # so a naive per-flag comparison reports one fact three times. Compare the
    # groupings as PARTITIONS over the flags both handlers accept (a flag only one
    # handler has is already reported by the flat diff above), then emit one row
    # per connected set of flags that were regrouped.
    common = set(mac_glob.flags) & set(win_glob.flags)
    regrouped = sorted(
        f for f in common
        if (mac_glob.groups[f] & common) != (win_glob.groups[f] & common)
    )
    components: list[set[str]] = []
    for f in regrouped:
        related = (mac_glob.groups[f] | win_glob.groups[f]) & set(regrouped)
        merged = {f} | related
        rest = []
        for comp in components:
            if comp & merged:
                merged |= comp
            else:
                rest.append(comp)
        components = rest + [merged]
    def blocks(groups: dict[str, frozenset[str]], keys: list[str]) -> str:
        seen: list[str] = []
        for f in keys:
            block = f"{{{', '.join(sorted(groups[f] & common))}}}"
            if block not in seen:
                seen.append(block)
        return " / ".join(seen)

    for comp in components:
        keys = sorted(comp)
        asymmetries.append(Asymmetry(
            GLOBAL_LEVEL, "alias", ",".join(keys), BOTH,
            f"macOS splits them {blocks(mac_glob.groups, keys)} "
            f"at {rel(mac_glob.flags[keys[0]], repo)}",
            f"Windows splits them {blocks(win_glob.groups, keys)} "
            f"at {rel(win_glob.flags[keys[0]], repo)}",
        ))

    # -- [3] Per command ---------------------------------------------------
    r.head("[3] Per-command surface (accepted flags / advertised flags / action words)")
    print(f"  {'command':<18} {'macOS a/d/w':<14} {'Windows a/d/w':<14} usage    files")
    suppressed: list[str] = []
    for cmd in all_commands:
        m, w = mac_cmds[cmd], win_cmds[cmd]
        mf = ", ".join(sorted(p.name for p in m.files)) or "NONE"
        wf = ", ".join(sorted(p.name for p in w.files)) or "NONE"
        usage = f"{'Y' if m.has_usage else 'n'}/{'Y' if w.has_usage else 'n'}"
        print(f"  {cmd:<18} {f'{len(m.accepted)}/{len(m.advertised)}/{len(m.actions)}':<14} "
              f"{f'{len(w.accepted)}/{len(w.advertised)}/{len(w.actions)}':<14} "
              f"{usage:<8} {mf} | {wf}")

        asymmetries += diff_maps(cmd, "accepted", m.accepted, w.accepted)
        asymmetries += diff_maps(cmd, "action", m.actions, w.actions)
        if m.has_usage == w.has_usage:
            asymmetries += diff_maps(cmd, "advertised", m.advertised, w.advertised)
        else:
            # One platform prints no help for this command at all. Listing each
            # flag the other advertises would be N restatements of ONE fact, and
            # would churn every time a flag is added. Report the fact once — and
            # say out loud that the per-flag comparison was suppressed, because a
            # comparison that quietly does not run is the defect this gate exists
            # to refuse.
            side = MAC if m.has_usage else WIN
            have, lack = (m, w) if m.has_usage else (w, m)
            asymmetries.append(Asymmetry(
                cmd, "usage", "usage-text", side,
                str(have.files[0]) if have.files else "present",
                "no usage text in source",
            ))
            suppressed.append(
                f"{cmd}: advertised-flag comparison SUPPRESSED — only {side} has usage text "
                f"({len(have.advertised)} flag(s) advertised there, {len(lack.advertised)} on the "
                "other side). Reported once as the `usage` asymmetry below."
            )
    print()
    for plat, cmds in ((MAC, mac_cmds), (WIN, win_cmds)):
        print(f"  {plat:<8}: {sum(len(s.accepted) for s in cmds.values())} accepted flags, "
              f"{sum(len(s.advertised) for s in cmds.values())} advertised flags, "
              f"{sum(len(s.actions) for s in cmds.values())} action words, "
              f"{sum(1 for s in cmds.values() if s.has_usage)} of {len(cmds)} commands with usage text")
    for line in suppressed:
        print()
        for out in wrap(line, 100):
            print(f"  {out}")

    # -- [4] Asymmetries against the ledger --------------------------------
    r.head("[4] Asymmetries against the exception table")
    ledger = {e.key: e for e in LEDGERED_EXCEPTIONS}
    matched: set[tuple[str, str, str, str]] = set()
    ledgered_rows: list[tuple[Asymmetry, Ledgered]] = []
    unledgered: list[Asymmetry] = []
    for a in asymmetries:
        if a.key in ledger:
            matched.add(a.key)
            ledgered_rows.append((a, ledger[a.key]))
        else:
            unledgered.append(a)

    print(f"  {len(ledgered_rows)} LEDGERED, {len(unledgered)} unledgered\n")
    for a, e in ledgered_rows:
        scope = " — present on both, meaning differs" if a.side == BOTH else f" — {a.side} only"
        print(f"  LEDGERED  [{e.finding}] {a.level}.{a.dimension} {a.token!r}{scope}")
        print(f"              {rel(a.evidence, repo)}")
        print(f"              {'' if a.side == BOTH else 'other side: '}{rel(a.other, repo)}")
        for line in wrap(e.why, 92):
            print(f"              {line}")
    if unledgered:
        print()
    for a in unledgered:
        if a.side == BOTH:
            # Present on both, but not meaning the same thing — the evidence
            # strings already name their own platform.
            body = f"{rel(a.evidence, repo)}\n{rel(a.other, repo)}\n"
        else:
            other = WIN if a.side == MAC else MAC
            body = f"{a.side:<8}: {rel(a.evidence, repo)}\n{other:<8}: {rel(a.other, repo)}\n"
        r.fail(
            f"unledgered asymmetry — {a.level}.{a.dimension} {a.token!r} ({a.side})",
            body + "Not in the exception table. Either the two surfaces are made to agree, or the "
            "divergence is filed and ledgered with its finding id.",
        )

    # -- [5] Stale exceptions ---------------------------------------------
    r.head("[5] Exception-table hygiene")
    stale = [e for e in LEDGERED_EXCEPTIONS if e.key not in matched]
    for e in stale:
        r.fail(
            f"stale exception — {e.level}.{e.dimension} {e.token!r} ({e.side})",
            f"The table excuses this asymmetry under {e.finding}, but the extraction no longer "
            "finds it. Either the divergence was resolved (delete the entry) or the extraction "
            "stopped seeing it (fix the extraction).\n"
            "An exception that outlives its divergence is a standing permission to reintroduce "
            "the defect.",
        )
    if not stale:
        r.ok(f"all {len(LEDGERED_EXCEPTIONS)} exception(s) still correspond to a real asymmetry")

    print()
    if r.failures:
        print(f"CLI SURFACE GATE FAILED — {len(r.failures)} unledgered asymmetry / stale "
              "exception(s). Build is RED.")
        return 1
    print(f"CLI SURFACE GATE PASSED — {len(all_commands)} commands compared at three levels; "
          f"{len(ledgered_rows)} known asymmetry/asymmetries LEDGERED, none new.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--repo-root", default=None, help="repo root (default: parent of scripts/)")
    ap.add_argument("--swift-cli-root", default=None, help=f"override {SWIFT_CLI_ROOT}")
    ap.add_argument("--csharp-cli-root", default=None, help=f"override {CSHARP_CLI_ROOT}")
    ap.add_argument("--github", action="store_true", help="also emit ::error annotations")
    args = ap.parse_args()
    try:
        return run(args)
    except CannotRun as exc:
        print(f"gate error: {exc}", file=sys.stderr)
        print("The gate could not run, so it reports CANNOT-RUN (exit 2), never a pass.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())

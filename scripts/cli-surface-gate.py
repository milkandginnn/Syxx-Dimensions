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

WHAT IT COMPARES, AND AT WHICH LEVEL
------------------------------------
The two levels behave differently and must not be pooled. `syxx --help` is
answered by the top-level dispatcher; `syxx nslook --help` is answered by the
subcommand's own global-flag handler, which is a different code path with a
different flag set. Pooling them would let a flag missing at one level be
covered by its twin at the other.

  TOP LEVEL      command names, flag literals, and the unknown-command exit
                 code, read out of each dispatcher's own switch block.

  PER COMMAND    for each subcommand, three separate surfaces:
                   ACCEPTED   — a string literal that IS a flag ("--force").
                                Whatever the comparison idiom around it
                                (`contains`, `==`, `.Equals`, `hasPrefix`),
                                a whole-literal flag is a flag the parser reads.
                   ADVERTISED — a flag token embedded in a longer literal, i.e.
                                usage/help prose the user is shown.
                   ACTIONS    — action words (`verify`, `create`, `status`),
                                taken from `case`/`==`/`.Equals` operands.

ACCEPTED and ADVERTISED are kept apart because the D3 divergences live in the
gap between them. `witness --force` is accepted on BOTH platforms; only the
Windows usage text mentions it. A gate that pooled the two would report that
divergence as absent, and the capture that found it as mistaken.

WHY IT MATCHES ON STRINGS, NOT SYMBOLS
--------------------------------------
`NslookCLI` / `NslookCommand`, `SyxxWitnessCLI` / `WitnessCommand` — the type
names differ across platforms already, and renaming them is nobody's business
but the platform's. What may not change silently is the text a user types.
So nothing here is looked up by symbol: commands come from the dispatch case
literals, flags from string literals, and a source file is bound to the command
it implements by the tool-name literal it passes to the global-flag handler
("syxx nslook") — never by its filename or its type name.

DEAD CASES
----------
Both dispatchers lowercase the incoming token before the switch
(SyxxCLI.swift:48, Program.cs:73). A `case` literal containing an uppercase
letter can therefore never match. The gate detects the lowercasing from source
and reports every such literal as DEAD, excluding it from the effective surface.
This is what makes the F-08 addendum mechanical: Swift's top-level `-V` is
advertised in the switch and cannot fire, so macOS has no working short version
flag at top level while Windows does.

LEDGERED EXCEPTIONS
-------------------
The asymmetries already found and filed are listed in LEDGERED_EXCEPTIONS below,
each carrying the finding or divergence id that documents it. A matched entry is
reported LEDGERED and does not fail the build.

An entry that matches NOTHING in the extracted surface is a STALE exception and
FAILS. An exception that outlives its divergence is worse than no exception: it
is a standing permission to reintroduce the defect, and it makes the ledger read
as describing a tree it no longer describes.

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
real tree being touched. `--github` adds `::error` annotations; it does not
change the exit code — the exit code is the gate.
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

# ---------------------------------------------------------------------------
# Scope: the two CLI implementations, and the two global-flag handlers that the
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

# Anti-vacuity floors. Both dispatchers register 15 user-facing subcommands plus
# the CI-only `parity-transcript`. An extraction that finds fewer than 15 has
# stopped matching the source, and must report that rather than a clean surface.
MIN_COMMANDS = 15


# ---------------------------------------------------------------------------
# Ledgered exceptions — asymmetries that are already filed.
#
# `finding` is the record that documents the divergence. `why` says what the
# asymmetry costs, so a reader of the gate output does not have to open the
# ledger to know whether the entry is still acceptable.
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class Ledgered:
    level: str        # "top" | command name
    dimension: str    # "command" | "topflag" | "deadcase" | "accepted" | "advertised" | "action"
    token: str
    side: str         # "macOS" | "Windows" — the platform that HAS it (or where it is dead)
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
        side="Windows",
        finding="F-08",
        why=(
            "`syxx -v` prints a version on Windows/Linux (E088) and is the verbose flag on "
            "macOS (B240). Two documented decisions in direct conflict; resolving it changes "
            "a shipped flag's meaning on at least one platform, so it is owner-gated "
            "(plan Task 8.8), not agent-fixable."
        ),
    ),
    Ledgered(
        level="top",
        dimension="deadcase",
        token="-V",
        side="macOS",
        finding="F-08 addendum (2026-08-05)",
        why=(
            "SyxxCLI.swift lowercases args[1] before the switch, so the `-V` arm cannot fire. "
            "Proven by execution: `syxx -V` exits 3 'Unknown subcommand'. macOS therefore has "
            "NO working short version flag at top level. Fix is part of Task 8.8."
        ),
    ),
    Ledgered(
        level="top",
        dimension="deadcase",
        token="-V",
        side="Windows",
        finding="F-08 addendum (2026-08-05)",
        why=(
            "Program.cs lowercases args[0] the same way, so the `-V` arm is equally dead here. "
            "It is harmless on this platform only because the `-v` arm above it accepts the "
            "lowercased token. Removing `-v` without removing `-V` would silently drop both."
        ),
    ),
    Ledgered(
        level="witness",
        dimension="advertised",
        token="--force",
        side="Windows",
        finding="D3 (cli-capture-report.md)",
        why=(
            "Both platforms ACCEPT --force on --gen-keypair; only the Windows usage text "
            "mentions it. A macOS user reading the help has no way to learn the escape hatch "
            "from the 'private key already exists' refusal, which names --force in its error."
        ),
    ),
    Ledgered(
        level="cage",
        dimension="advertised",
        token="--output",
        side="Windows",
        finding="D3 (cli-capture-report.md)",
        why=(
            "`cage` advertises [--output <dir>] on Windows only. Captured 2026-08-05 by "
            "executing both builds' `cage --help`."
        ),
    ),
    Ledgered(
        level="unbound",
        dimension="action",
        token="verify-dnscrypt",
        side="macOS",
        finding="D3 (cli-capture-report.md)",
        why=(
            "The Unbound -> dnscrypt-proxy -> Anonymized-DNSCrypt chain proof exists only on "
            "macOS. `syxx unbound verify-dnscrypt` on Windows/Linux falls to the unknown-action "
            "branch."
        ),
    ),
)


# ---------------------------------------------------------------------------
# Comment stripping.
#
# Naive regex stripping corrupts string literals containing `//` (there are URLs
# and `https://` prose in both trees) and would make the gate lie about what the
# source says. Both strippers are escape-aware scanners that preserve string
# literals byte-for-byte and blank out only real comments. They are also
# LENGTH-PRESERVING, so a byte offset in the stripped text is the same byte
# offset in the original and line numbers need no correction.
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
# Lexing helpers
# ---------------------------------------------------------------------------
STRING_LITERAL = re.compile(r'"((?:[^"\\\n]|\\.)*)"')

# A long flag as the user types it. The optional trailing `=` records the
# `--out=VALUE` idiom, which both platforms use for the transcript emitter.
LONG_FLAG_TOKEN = re.compile(r"(?<![\w-])(--[A-Za-z][A-Za-z0-9-]*=?)")
SHORT_FLAG_TOKEN = re.compile(r"(?<![\w-])(-[A-Za-z])(?![\w-])")

EXACT_LONG_FLAG = re.compile(r"^--[A-Za-z][A-Za-z0-9-]*=?$")
EXACT_SHORT_FLAG = re.compile(r"^-[A-Za-z]$")
ACTION_WORD_SHAPE = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")

# Operands of a dispatch comparison, in every idiom the two trees use:
#   Swift  `case "verify":`  `case "--help", "-h":`  `cmd == "create"`
#   C#     `case "verify":`  `cmd.Equals("create", ...)`  `args[i] == "--force"`
COMPARISON_OPERAND = re.compile(
    r"""(?:
          ==\s*"((?:[^"\\\n]|\\.)*)"        # x == "lit"
        | "((?:[^"\\\n]|\\.)*)"\s*==        # "lit" == x
        | \.[Ee]quals\(\s*"((?:[^"\\\n]|\\.)*)"   # x.Equals("lit", ...)
        )""",
    re.VERBOSE,
)


def line_of(src: str, idx: int) -> int:
    return src.count("\n", 0, idx) + 1


def norm_flag(token: str) -> str:
    """`--out=` and `--out` name the same flag with different value idioms."""
    return token[:-1] if token.endswith("=") else token


def case_arm_literals(block: str) -> list[tuple[str, int]]:
    """Every string literal that is a `case` operand, with its offset in `block`.

    Reads from each `case` keyword to the `:` that ends the arm, so Swift's
    comma-separated form (`case "--version", "-V":`) and C#'s stacked form
    (`case "-v": case "-V":`) both yield all of their literals.
    """
    found: list[tuple[str, int]] = []
    for m in re.finditer(r"\bcase\b", block):
        colon = block.find(":", m.end())
        if colon < 0:
            continue
        span = block[m.end() : colon]
        # A `:` inside a literal would truncate the arm; re-extend past it.
        while span.count('"') % 2 == 1:
            colon = block.find(":", colon + 1)
            if colon < 0:
                break
            span = block[m.end() : colon]
        if colon < 0:
            continue
        for lm in STRING_LITERAL.finditer(span):
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
    text: str  # comments blanked, length-preserved

    def at(self, idx: int) -> str:
        return f"{self.path}:{line_of(self.text, idx)}"


def read_tree(root: Path, suffix: str, stripper, label: str) -> list[Source]:
    if not root.is_dir():
        raise CannotRun(f"{label} source root not found: {root}")
    out: list[Source] = []
    for p in sorted(root.rglob(f"*{suffix}")):
        try:
            raw = p.read_text(encoding="utf-8")
        except OSError as exc:
            raise CannotRun(f"cannot read {p}: {exc}") from exc
        out.append(Source(p, raw, stripper(raw)))
    if not out:
        raise CannotRun(f"{label} tree {root} contains no {suffix} files")
    return out


# ---------------------------------------------------------------------------
# Top-level dispatch surface (Task 3.1a)
# ---------------------------------------------------------------------------
@dataclass
class TopSurface:
    platform: str
    commands: dict[str, str] = field(default_factory=dict)      # name -> file:line
    flags: dict[str, str] = field(default_factory=dict)         # effective (reachable) flags
    dead: dict[str, str] = field(default_factory=dict)          # unreachable case literals
    lowercased: bool = False
    unknown_exit: int | None = None
    unknown_exit_at: str = ""
    subject: str = ""


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
        f"{platform}: no top-level dispatch switch found in {src.path}. "
        "Expected a switch whose arms include both '--help' and '--version'. "
        "The dispatcher moved, or the gate's locator is stale — either way the "
        "surface was not measured."
    )


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
            name, conformances = m.group(1), m.group(2)
            if not re.search(r"\bInt\d*\b", conformances):
                continue
            open_idx = src.text.index("{", m.start())
            close_idx = match_braces(src.text, open_idx)
            if close_idx < 0:
                continue
            body = src.text[open_idx + 1 : close_idx - 1]
            for cm in re.finditer(r"\bcase\s+(\w+)\s*=\s*(-?\d+)", body):
                table[(name, cm.group(1))] = int(cm.group(2))
    return table


def extract_top_surface(
    platform: str,
    dispatcher: Source,
    tree: list[Source],
    exit_assign: re.Pattern[str],
    enum_table: dict[tuple[str, str], int] | None,
) -> TopSurface:
    block, block_at, subject = find_dispatch_block(dispatcher, platform)
    surf = TopSurface(platform=platform, subject=subject.strip())
    surf.lowercased = bool(re.search(r"lowercased\(\)|ToLowerInvariant\(\)|ToLower\(\)", subject))

    for lit, off in case_arm_literals(block):
        loc = f"{dispatcher.path}:{line_of(dispatcher.text, block_at + off)}"
        # A lowercased subject can never equal a literal carrying an uppercase
        # letter. The arm is present in source and unreachable at runtime.
        if surf.lowercased and lit != lit.lower():
            surf.dead[lit] = loc
            continue
        if lit.startswith("-"):
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
        if not lit or lit.startswith("-") or not ACTION_WORD_SHAPE.match(lit):
            continue
        surf.commands.setdefault(lit, f"{dispatcher.path}:{line_of(dispatcher.text, m.start())}")

    # Unknown-command exit code: the `default` arm of the dispatch switch.
    default_at = block.rfind("default")
    if default_at < 0:
        raise CannotRun(f"{platform}: dispatch switch has no `default` arm — unknown-command behaviour is unmeasured")
    tail = block[default_at:]
    am = exit_assign.search(tail)
    if not am:
        raise CannotRun(
            f"{platform}: could not read the unknown-command exit code from the `default` arm "
            f"({dispatcher.path}:{line_of(dispatcher.text, block_at + default_at)}). "
            "The gate reports UNKNOWN rather than assuming the contract holds."
        )
    surf.unknown_exit_at = f"{dispatcher.path}:{line_of(dispatcher.text, block_at + default_at + am.start())}"
    if am.lastindex and am.group(1) and am.group(1).lstrip("-").isdigit():
        surf.unknown_exit = int(am.group(1))
    else:
        if enum_table is None:
            raise CannotRun(f"{platform}: unknown-command exit code is not an integer literal and no enum table was supplied")
        key = (am.group(2), am.group(3))
        if key not in enum_table:
            raise CannotRun(
                f"{platform}: unknown-command exit code is {key[0]}.{key[1]}, which no "
                "Int-backed enum in the tree defines. The code was not measured."
            )
        surf.unknown_exit = enum_table[key]
    return surf


# ---------------------------------------------------------------------------
# Per-command surface (Task 3.1b)
# ---------------------------------------------------------------------------
@dataclass
class CommandSurface:
    accepted: dict[str, str] = field(default_factory=dict)     # flag -> file:line
    advertised: dict[str, str] = field(default_factory=dict)
    actions: dict[str, str] = field(default_factory=dict)
    spellings: dict[str, set[str]] = field(default_factory=dict)  # flag -> raw forms seen
    files: list[str] = field(default_factory=list)


def bind_files_to_commands(
    tree: list[Source], dispatcher: Source, commands: list[str]
) -> dict[str, list[Source]]:
    """Which source file implements which subcommand — decided on strings only.

    Three tiers, strongest first. Nothing here reads a filename or a type name,
    so `SyxxNSLook.swift` -> `NslookCommand.cs` -> anything else is free.

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
            tier1 = re.search(rf'"syxx[ -]{re.escape(cmd)}"', src.text)
            tier2 = re.search(rf"Usage:\s*`?syxx[ -]{re.escape(cmd)}\b", src.raw)
            tier3 = re.search(rf'"{re.escape(cmd)}:\s', src.text)
            if tier1 or tier2 or tier3:
                bound[cmd].append(src)
    return bound


def extract_command_surface(cmd: str, files: list[Source]) -> CommandSurface:
    surf = CommandSurface(files=[str(f.path) for f in files])
    for src in files:
        for lm in STRING_LITERAL.finditer(src.text):
            body, at = lm.group(1), src.at(lm.start())
            if EXACT_LONG_FLAG.match(body) or EXACT_SHORT_FLAG.match(body):
                # The whole literal is a flag: something compares argv to it.
                key = norm_flag(body)
                surf.accepted.setdefault(key, at)
                surf.spellings.setdefault(key, set()).add(body)
                continue
            # Embedded in prose: this is the surface the user is shown.
            for fm in LONG_FLAG_TOKEN.finditer(body):
                key = norm_flag(fm.group(1))
                surf.advertised.setdefault(key, at)
                surf.spellings.setdefault(key, set()).add(fm.group(1))
            for fm in SHORT_FLAG_TOKEN.finditer(body):
                surf.advertised.setdefault(fm.group(1), at)

        # Action words: operands of a dispatch comparison, in any idiom.
        for lit, off in case_arm_literals(src.text):
            if ACTION_WORD_SHAPE.match(lit):
                surf.actions.setdefault(lit, src.at(off))
        for m in COMPARISON_OPERAND.finditer(src.text):
            lit = next(g for g in m.groups() if g is not None)
            if ACTION_WORD_SHAPE.match(lit) and len(lit) > 1:
                surf.actions.setdefault(lit, src.at(m.start()))
    return surf


# ---------------------------------------------------------------------------
# Comparison and reporting (Task 3.2)
# ---------------------------------------------------------------------------
@dataclass
class Asymmetry:
    level: str
    dimension: str
    token: str
    side: str        # the platform that HAS it
    evidence: str
    other: str = "absent"

    @property
    def key(self) -> tuple[str, str, str, str]:
        return (self.level, self.dimension, self.token, self.side)


MAC, WIN = "macOS", "Windows"


def diff_maps(
    level: str, dimension: str, mac: dict[str, str], win: dict[str, str]
) -> list[Asymmetry]:
    out = [
        Asymmetry(level, dimension, t, MAC, mac[t])
        for t in sorted(set(mac) - set(win))
    ]
    out += [
        Asymmetry(level, dimension, t, WIN, win[t])
        for t in sorted(set(win) - set(mac))
    ]
    return out


class Report:
    def __init__(self, github: bool) -> None:
        self.github = github
        self.failures: list[str] = []

    def head(self, title: str) -> None:
        print(f"\n{title}")
        print("-" * len(title))

    def row(self, tag: str, text: str) -> None:
        print(f"  {tag:<9} {text}")

    def fail(self, title: str, detail: str) -> None:
        self.failures.append(title)
        print(f"  FAIL      {title}")
        for line in detail.splitlines():
            print(f"            {line}")
        if self.github:
            print(f"::error title=CLI surface gate: {title}::{detail.replace(chr(10), '%0A')}")


def rel(loc: str, repo: Path) -> str:
    return loc.replace(str(repo) + "/", "")


# ---------------------------------------------------------------------------
def run(args: argparse.Namespace) -> int:
    repo = Path(args.repo_root).resolve() if args.repo_root else Path(__file__).resolve().parent.parent
    swift_root = Path(args.swift_cli_root).resolve() if args.swift_cli_root else repo / SWIFT_CLI_ROOT
    cs_root = Path(args.csharp_cli_root).resolve() if args.csharp_cli_root else repo / CSHARP_CLI_ROOT

    swift_tree = read_tree(swift_root, ".swift", strip_swift_comments, "macOS SyxxCLI")
    cs_tree = read_tree(cs_root, ".cs", strip_csharp_comments, "Windows Syxx")

    # The shared global-flag handlers are part of the per-command surface: they
    # are where `<command> --help` and `<command> --version` are actually parsed.
    for extra, stripper, label in (
        (repo / SWIFT_GLOBAL_FLAGS, strip_swift_comments, "macOS SyxxConfig"),
        (repo / CSHARP_GLOBAL_FLAGS, strip_csharp_comments, "Windows SyxxConfig"),
    ):
        if not extra.is_file():
            raise CannotRun(f"{label} not found at {extra}; subcommand global flags are unmeasured")

    swift_globals = Source(
        repo / SWIFT_GLOBAL_FLAGS,
        (repo / SWIFT_GLOBAL_FLAGS).read_text(encoding="utf-8"),
        strip_swift_comments((repo / SWIFT_GLOBAL_FLAGS).read_text(encoding="utf-8")),
    )
    cs_globals = Source(
        repo / CSHARP_GLOBAL_FLAGS,
        (repo / CSHARP_GLOBAL_FLAGS).read_text(encoding="utf-8"),
        strip_csharp_comments((repo / CSHARP_GLOBAL_FLAGS).read_text(encoding="utf-8")),
    )

    swift_dispatch = next((s for s in swift_tree if s.path.name == SWIFT_DISPATCH), None)
    cs_dispatch = next((s for s in cs_tree if s.path.name == CSHARP_DISPATCH), None)
    if swift_dispatch is None:
        raise CannotRun(f"macOS dispatcher {SWIFT_DISPATCH} not found under {swift_root}")
    if cs_dispatch is None:
        raise CannotRun(f"Windows dispatcher {CSHARP_DISPATCH} not found under {cs_root}")

    print("Syxx CLI surface parity gate — track cross-platform-parity_20260805, Tasks 3.1/3.2")
    print(f"  macOS   : {len(swift_tree):>3} .swift under {rel(str(swift_root), repo)}  "
          f"(dispatcher {swift_dispatch.path.name})")
    print(f"  Windows : {len(cs_tree):>3} .cs    under {rel(str(cs_root), repo)}  "
          f"(dispatcher {cs_dispatch.path.name})")

    # -- Extraction --------------------------------------------------------
    mac_top = extract_top_surface(
        MAC,
        swift_dispatch,
        swift_tree,
        re.compile(r"exitCode\s*=\s*(?:(-?\d+)|(\w+)\s*\.\s*(\w+)\s*\.\s*rawValue)"),
        resolve_swift_int_enum(swift_tree),
    )
    win_top = extract_top_surface(
        WIN,
        cs_dispatch,
        cs_tree,
        re.compile(r"ExitCode\s*=\s*(-?\d+)"),
        None,
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

    all_commands = sorted(set(mac_top.commands) | set(win_top.commands))
    mac_files = bind_files_to_commands(swift_tree + [swift_globals], swift_dispatch, all_commands)
    win_files = bind_files_to_commands(cs_tree + [cs_globals], cs_dispatch, all_commands)

    mac_cmds = {c: extract_command_surface(c, mac_files[c]) for c in all_commands}
    win_cmds = {c: extract_command_surface(c, win_files[c]) for c in all_commands}

    mac_flag_total = sum(len(s.accepted) + len(s.advertised) for s in mac_cmds.values())
    win_flag_total = sum(len(s.accepted) + len(s.advertised) for s in win_cmds.values())
    for plat, total in ((MAC, mac_flag_total), (WIN, win_flag_total)):
        if total == 0:
            raise CannotRun(
                f"{plat}: extracted zero per-command flags across {len(all_commands)} commands. "
                "Either every command lost its argument parsing, or the literal scan is broken."
            )

    r = Report(args.github)
    asymmetries: list[Asymmetry] = []

    # -- [1] Top level -----------------------------------------------------
    r.head("[1] Top-level dispatch surface")
    print(f"  macOS   subject {mac_top.subject!r}"
          f"{'  (lowercased -> uppercase arms are dead)' if mac_top.lowercased else ''}")
    print(f"  Windows subject {win_top.subject!r}"
          f"{'  (lowercased -> uppercase arms are dead)' if win_top.lowercased else ''}")
    print()
    print(f"  {'token':<22} {'macOS':<34} {'Windows'}")
    for token in sorted(set(mac_top.commands) | set(win_top.commands)):
        a = rel(mac_top.commands.get(token, "absent"), repo)
        b = rel(win_top.commands.get(token, "absent"), repo)
        print(f"  {token:<22} {a:<34} {b}")
    for token in sorted(set(mac_top.flags) | set(win_top.flags)):
        a = rel(mac_top.flags.get(token, "absent"), repo)
        b = rel(win_top.flags.get(token, "absent"), repo)
        print(f"  {token:<22} {a:<34} {b}")
    print()
    print(f"  macOS  : {len(mac_top.commands)} commands, {len(mac_top.flags)} reachable flags, "
          f"{len(mac_top.dead)} dead arm(s)")
    print(f"  Windows: {len(win_top.commands)} commands, {len(win_top.flags)} reachable flags, "
          f"{len(win_top.dead)} dead arm(s)")

    asymmetries += diff_maps("top", "command", mac_top.commands, win_top.commands)
    asymmetries += diff_maps("top", "topflag", mac_top.flags, win_top.flags)
    for plat, dead in ((MAC, mac_top.dead), (WIN, win_top.dead)):
        for token, loc in sorted(dead.items()):
            asymmetries.append(Asymmetry("top", "deadcase", token, plat, loc, "unreachable"))

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
        r.row("PASS", f"both exit {mac_top.unknown_exit} on an unknown subcommand")

    # -- [2] Per command ---------------------------------------------------
    r.head("[2] Per-command surface (accepted flags / advertised flags / action words)")
    print(f"  {'command':<18} {'macOS acc/adv/act':<20} {'Windows acc/adv/act':<20} files")
    for cmd in all_commands:
        m, w = mac_cmds[cmd], win_cmds[cmd]
        mc = f"{len(m.accepted)}/{len(m.advertised)}/{len(m.actions)}"
        wc = f"{len(w.accepted)}/{len(w.advertised)}/{len(w.actions)}"
        mf = ", ".join(sorted(Path(f).name for f in m.files)) or "NONE"
        wf = ", ".join(sorted(Path(f).name for f in w.files)) or "NONE"
        print(f"  {cmd:<18} {mc:<20} {wc:<20} {mf} | {wf}")
        asymmetries += diff_maps(cmd, "accepted", m.accepted, w.accepted)
        asymmetries += diff_maps(cmd, "advertised", m.advertised, w.advertised)
        asymmetries += diff_maps(cmd, "action", m.actions, w.actions)

    # -- [3] Asymmetries against the ledger --------------------------------
    r.head("[3] Asymmetries")
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

    if ledgered_rows:
        print(f"  {len(ledgered_rows)} LEDGERED (documented, not a failure):")
        for a, e in ledgered_rows:
            other = "unreachable" if a.dimension == "deadcase" else "absent"
            print(f"    LEDGERED  [{e.finding}] {a.level}.{a.dimension} {a.token!r} "
                  f"— {a.side} only ({rel(a.evidence, repo)}); other side: {other}")
            for line in _wrap(e.why, 92):
                print(f"                {line}")
    else:
        print("  no ledgered asymmetry matched — see the stale-exception check below")

    if unledgered:
        print()
        for a in unledgered:
            other_plat = WIN if a.side == MAC else MAC
            detail = (
                f"{a.level}.{a.dimension} {a.token!r}\n"
                f"  {a.side:<8}: {rel(a.evidence, repo)}\n"
                f"  {other_plat:<8}: {a.other}\n"
                "Not in the exception table. Either the two surfaces must be made to agree, or "
                "the divergence must be filed and ledgered with its finding id."
            )
            r.fail(f"unledgered asymmetry — {a.level}.{a.dimension} {a.token!r} ({a.side} only)", detail)

    # -- [4] Stale exceptions ---------------------------------------------
    r.head("[4] Exception-table hygiene")
    stale = [e for e in LEDGERED_EXCEPTIONS if e.key not in matched]
    if stale:
        for e in stale:
            r.fail(
                f"stale exception — {e.level}.{e.dimension} {e.token!r} ({e.side})",
                f"The table excuses this asymmetry under {e.finding}, but the extraction no longer "
                "finds it. Either the divergence was resolved (delete the entry) or the extraction "
                "stopped seeing it (fix the extraction).\n"
                "An exception that outlives its divergence is a standing permission to "
                "reintroduce the defect.",
            )
    else:
        r.row("PASS", f"all {len(LEDGERED_EXCEPTIONS)} exception(s) still correspond to a real asymmetry")

    # -- conclusion --------------------------------------------------------
    print()
    if r.failures:
        print(f"CLI SURFACE GATE FAILED — {len(r.failures)} unledgered asymmetry/stale exception(s). Build is RED.")
        return 1
    print(
        f"CLI SURFACE GATE PASSED — {len(all_commands)} commands compared at two levels; "
        f"{len(ledgered_rows)} known asymmetry/asymmetries LEDGERED, none new."
    )
    return 0


def _wrap(text: str, width: int) -> list[str]:
    words, lines, cur = text.split(), [], ""
    for w in words:
        if cur and len(cur) + 1 + len(w) > width:
            lines.append(cur)
            cur = w
        else:
            cur = f"{cur} {w}" if cur else w
    if cur:
        lines.append(cur)
    return lines


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
        print(
            "The gate could not run, so it reports CANNOT-RUN (exit 2), never a pass.",
            file=sys.stderr,
        )
        return 2


if __name__ == "__main__":
    sys.exit(main())

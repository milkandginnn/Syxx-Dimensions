#!/usr/bin/env python3
"""NuGet pin-parity gate (UNIFIED_IMPLEMENTATION_PLAN.md Phase 0.3b).

WHY THIS EXISTS
---------------
0.3b was logged as "CommunityToolkit.Mvvm 8.3.2 (Windows) vs 8.4.2 (Ubuntu)". That is
not a one-off typo, it is a defect CLASS: the Windows GUI, the Avalonia (Linux) GUI and
the CLI all ProjectReference the same shared libraries, so every package pinned at two
different versions means the platforms link different code behind an identical source
tree. Nothing in the build says so.

It had already recurred and gone unnoticed: Syxx.Telemetry pinned OpenTelemetry 1.9.0
while SyxxUI pinned 1.16.0. Because Syxx.Telemetry is the ONLY OTel path the Avalonia
client has, the first-ship platform resolved 1.9.0 — which `dotnet restore` flags as
carrying known advisories GHSA-g94r-2vxg-569j and GHSA-4625-4j76-fww9 — while Windows
resolved a patched 1.16.0.

This gate asserts every package id is pinned to exactly ONE version across all the
.NET projects that participate in the shared-core graph. Cheap, deterministic, and it
needs no SDK and no network.

RELATION TO CENTRAL PACKAGE MANAGEMENT
--------------------------------------
The structurally stronger fix is Central Package Management: one Directory.Packages.props
with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`, and every
csproj carrying a version-less <PackageReference>. That makes divergence UNREPRESENTABLE
rather than merely detected, and is the recommended follow-up. It also rewrites every
csproj in the tree and must be validated by a real restore on each platform, so it is a
larger change than this gate. This gate is the interim enforcement and stays useful
afterwards (it would then simply always pass).

EXIT CODES
----------
  0  every package id has a single version
  1  a package id is pinned at two or more versions
  2  the gate could not run — NEVER silently 0
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

# Project roots that share the reasoning core by ProjectReference.
PROJECT_ROOTS = ["Windows/dotNET", "Ubuntu"]

PACKAGE_REF = re.compile(
    r"<PackageReference\s+Include=\"([^\"]+)\"[^>]*?\bVersion=\"([^\"]+)\"", re.IGNORECASE
)

# Package ids allowed to differ, each with a stated reason. Empty by design: an
# exemption must be argued in code review, not defaulted into.
EXEMPT: dict[str, str] = {}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--repo-root", default=None)
    ap.add_argument("--github", action="store_true")
    args = ap.parse_args()

    repo = Path(args.repo_root).resolve() if args.repo_root else Path(__file__).resolve().parent.parent

    csprojs: list[Path] = []
    for root in PROJECT_ROOTS:
        d = repo / root
        if not d.is_dir():
            print(f"gate error: project root not found: {d}", file=sys.stderr)
            print("The gate cannot run, so it reports UNKNOWN (exit 2), never a pass.", file=sys.stderr)
            return 2
        csprojs.extend(sorted(d.rglob("*.csproj")))

    if not csprojs:
        print("gate error: zero .csproj files found; refusing to report a pass.", file=sys.stderr)
        return 2

    pins: dict[str, dict[str, list[str]]] = {}
    for p in csprojs:
        try:
            text = p.read_text(encoding="utf-8")
        except OSError as exc:
            print(f"gate error: cannot read {p}: {exc}", file=sys.stderr)
            return 2
        for pkg, version in PACKAGE_REF.findall(text):
            pins.setdefault(pkg, {}).setdefault(version, []).append(str(p.relative_to(repo)))

    print("NuGet pin-parity gate (0.3b)")
    print(f"  scanned {len(csprojs)} .csproj under {', '.join(PROJECT_ROOTS)}")
    print(f"  {len(pins)} distinct package ids")
    print()

    failures = 0
    for pkg in sorted(pins):
        versions = pins[pkg]
        if len(versions) == 1:
            continue
        if pkg in EXEMPT:
            print(f"  EXEMPT  {pkg}: {sorted(versions)} — {EXEMPT[pkg]}")
            continue
        failures += 1
        lines = [f"Package {pkg!r} is pinned at {len(versions)} different versions:"]
        for version in sorted(versions):
            for proj in sorted(versions[version]):
                lines.append(f"    {version}  {proj}")
        lines.append(
            "These projects share the reasoning core by ProjectReference, so a split pin means "
            "the platforms link different code behind identical source."
        )
        print(f"  FAIL  {pkg}")
        for line in lines[1:]:
            print(f"        {line}")
        if args.github:
            print(f"::error title=NuGet pin split: {pkg}::" + "%0A".join(lines))

    if failures:
        print()
        print(f"PIN-PARITY GATE FAILED — {failures} split package pin(s). Build is RED.")
        return 1
    print("PIN-PARITY GATE PASSED — every package id has a single version across all projects.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

# Linux/dotNET

## The Linux CLI is the Windows CLI

The Linux command-line interface is built from `Windows/dotNET/Syxx/Syxx.csproj`, which
declares `<RuntimeIdentifiers>win-x64;linux-x64;linux-arm64</RuntimeIdentifiers>`.

Build via: `Windows/Syxx.Linux.slnf`

CI publishes self-contained Linux binaries per RID in `.github/workflows/ci.yml`.

## Why the `Syxx.Linux` directory was removed

A previous `Linux/dotNET/Syxx.Linux/` directory contained 166 lines where every
subcommand printed `(implementation pending)`. It was:

- Referenced by no solution file
- Referenced by no solution filter
- Referenced by no CI workflow
- Referenced by no packaging script
- Referenced by no documentation

It was a placeholder that was never connected to the build system. The real Linux CLI
has always been the shared `Syxx.csproj` — which carries all 16 commands (~3,749 LOC)
and is the project listed in `Windows/Syxx.Linux.slnf` and `Linux/snap/snapcraft.yaml`.

Removed 2026-08-05 as part of the Linux→macOS functional parity effort.
See `docs/PLAN-linux-parity-team-2026-08-04 - saved.md` §0.1 for the full analysis.

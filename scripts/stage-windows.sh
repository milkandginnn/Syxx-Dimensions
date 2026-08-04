#!/bin/bash
# stage-windows.sh — sync the Windows solution + context docs to the VM shared folder
# so the Windows GUI (SyxxUI) can be built there. Run after each remediation tier lands.
#
# Excludes bin/obj/.vs build artifacts and uses --delete so removed projects/files do
# not linger in the staged tree. Idempotent; safe to re-run.
set -e

SRC="${SRC:-/Users/masterrabbit/Software/Syxx}"
DST="${DST:-/Users/masterrabbit/Desktop/Shared/Syxx-Windows}"

if [ ! -d "$DST" ]; then
  echo "ERROR: shared folder not found: $DST" >&2
  echo "Create it (VM shared folder) or set DST=... before running." >&2
  exit 1
fi

echo ">>> syncing Windows/ source (excluding build artifacts) -> $DST/Windows/"
rsync -a --delete \
  --exclude='bin/' --exclude='obj/' --exclude='.vs/' --exclude='.DS_Store' \
  "$SRC/Windows/" "$DST/Windows/"

echo ">>> refreshing context docs"
for f in global.json AGENTS.md CHANGELOG.md DELTAS.md DELTAS-fixed.md DELTAS-rejected.md DESIGN.md \
         MEMORY.md PROPOSALS.md ROADMAP.md outputs-catalog.csv \
         REMEDIATION-MASTERPLAN.md REMEDIATION-MASTERPLAN2.md REMEDIATION-MASTERPLAN-complete.md \
         REMPLANERRORLOG.md UNIFIED-BINARY-PLAN.md OPENCODE-BUILD-PROMPTS.md \
         ENCRYPTED-TRANSPORTS-PLAN.md ENCRYPTED-TRANSPORTS-A-C-PLAN.md \
         ENCRYPTED-TRANSPORTS-B-ENGINE-PLAN.md ENCRYPTED-TRANSPORTS-D-SERVER-PLAN.md \
         ENCRYPTED-TRANSPORTS-E-F-PLAN.md \
         HANDOFF.md HANDOFF-BOB.md HANDOFF-OPENCODE.md HANDOFF-OPENCODE-2.md; do
  [ -f "$SRC/$f" ] && cp "$SRC/$f" "$DST/" 2>/dev/null && echo "    refreshed $f"
done

echo ">>> refreshing build helper (build.ps1)"
[ -f "$SRC/scripts/win-build.ps1" ] && cp "$SRC/scripts/win-build.ps1" "$DST/build.ps1" && echo "    refreshed build.ps1"

echo ">>> verifying staged solution builds the GUI"
if grep -q 'SyxxUI\\SyxxUI.csproj\|SyxxUI/SyxxUI.csproj' "$DST/Windows/Syxx.sln" 2>/dev/null \
   && [ -f "$DST/Windows/SyxxUI/SyxxUI.csproj" ]; then
  echo "    OK  SyxxUI is in the staged solution (GUI will build on the VM)"
else
  echo "    WARN  SyxxUI missing from staged solution — GUI will NOT build" >&2
fi

echo ">>> staged $(find "$DST/Windows" -type f | wc -l | tr -d ' ') Windows source files to $DST"
echo "    VM build:  dotnet build Windows/Syxx.sln -c Release   (or: pwsh ./run-tests.ps1)"

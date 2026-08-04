#!/usr/bin/env bash
# syxx-winbuild — build + test the Syxx Windows app inside the UTM "Windows" VM, end to end,
# entirely from the Mac terminal. No SSH: uses the UTM guest agent (utmctl file/exec).
#
# It: starts the VM if needed -> packages the Windows/ solution tree (no build artifacts) ->
# pushes it -> extracts in the guest -> builds the GUI (SyxxUI) + core (Syxx.Kit) in Release and
# runs the tests, logging everything -> pulls the log back to your terminal -> shuts the VM down.
#
# Usage:
#   syxx-winbuild                     # full cycle, then shut the VM down
#   SYXX_WIN_KEEP=1 syxx-winbuild     # leave the VM running afterward
#   SYXX_WIN_PLATFORM=arm64 syxx-winbuild   # if the VM is Windows-on-ARM
#   SYXX_WIN_DEST='C:\Users\Foo' syxx-winbuild
set -uo pipefail

UTMCTL="/Applications/UTM.app/Contents/MacOS/utmctl"
VM="${SYXX_WIN_VM:-Windows}"
SYXX_ROOT="${SYXX_ROOT:-/Users/masterrabbit/Software/Syxx}"
SRC="$SYXX_ROOT/Windows"
DEST="${SYXX_WIN_DEST:-C:\\Users\\Synn}"
PLATFORM="${SYXX_WIN_PLATFORM:-x64}"
NUGET_CACHE="${SYXX_WIN_NUGET:-C:\\Users\\JJ\\.nuget\\packages}"   # offline package cache (the VM has no internet)

TGZ_NAME="syxx-build.tgz"
G_TGZ="$DEST\\$TGZ_NAME"
G_DIR="$DEST\\Windows"
G_LOG="$DEST\\build.log"

hdr(){ printf '\033[1;36m>>> %s\033[0m\n' "$*"; }
err(){ printf '\033[1;31m!!! %s\033[0m\n' "$*" >&2; }

[ -x "$UTMCTL" ] || { err "utmctl not found at $UTMCTL (is UTM installed?)"; exit 1; }
[ -d "$SRC" ]    || { err "Windows source not found: $SRC"; exit 1; }

# 1) Ensure the VM is running.
if [ "$("$UTMCTL" status "$VM" 2>/dev/null)" != "started" ]; then
  hdr "Starting VM '$VM' ..."
  "$UTMCTL" start "$VM" || { err "could not start VM '$VM'"; exit 1; }
fi

# 2) Wait for the guest agent (poll ip-address, up to ~5 min for a cold boot).
hdr "Waiting for guest agent ..."
ok=0
for _ in $(seq 1 60); do
  if "$UTMCTL" ip-address "$VM" >/dev/null 2>&1; then ok=1; break; fi
  sleep 5
done
[ "$ok" = 1 ] || { err "guest agent never responded (is the VM fully booted?)"; exit 1; }

# 3) Package the Windows/ tree (source only — no bin/obj/.vs).
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
hdr "Packaging $SRC (source only) ..."
COPYFILE_DISABLE=1 tar -czf "$TMP/$TGZ_NAME" -C "$SYXX_ROOT" \
  --exclude='Windows/*/bin' --exclude='Windows/*/obj' \
  --exclude='Windows/.vs' --exclude='.DS_Store' --exclude='._*' Windows

# 4) Push the archive and extract it in the guest.
hdr "Uploading to guest ($G_TGZ) ..."
"$UTMCTL" exec "$VM" --cmd cmd.exe /c "if not exist $DEST mkdir $DEST" >/dev/null 2>&1
"$UTMCTL" file push "$VM" "$G_TGZ" < "$TMP/$TGZ_NAME" || { err "file push failed"; exit 1; }
hdr "Extracting in guest ..."
"$UTMCTL" exec "$VM" --cmd cmd.exe /c "cd /d $DEST & rmdir /s /q Windows 2>nul & tar -xzf $TGZ_NAME" >/dev/null 2>&1

# 5) Build GUI + core (Release) + run tests, logging everything to a guest file.
#    '&' (not '&&') runs every step so the log shows all results even if the GUI fails.
hdr "Building GUI + core + tests in guest (Release, $PLATFORM; offline cache $NUGET_CACHE) ..."
# Build via a pushed .cmd file — inline cmd grouping through utmctl exec mangles parens.
# NUGET_PACKAGES points at an existing offline cache because the VM has no internet.
# The .cmd writes __SYXX_DONE__ last; utmctl exec often returns before a long build
# finishes, so the fetch step polls the log for that marker.
BCMD="$TMP/build.cmd"
{
  printf '@echo off\r\n'
  printf 'set NUGET_PACKAGES=%s\r\n' "$NUGET_CACHE"
  printf 'cd /d %s\r\n' "$G_DIR"
  printf 'echo ==== SyxxUI GUI (Release/%s) ====> %s\r\n' "$PLATFORM" "$G_LOG"
  printf 'dotnet build SyxxUI\\SyxxUI.csproj -c Release -p:Platform=%s >> %s 2>&1\r\n' "$PLATFORM" "$G_LOG"
  printf 'echo.>> %s\r\n' "$G_LOG"
  printf 'echo ==== Syxx.Kit (Release) ====>> %s\r\n' "$G_LOG"
  printf 'dotnet build Syxx.Kit\\Syxx.Kit.csproj -c Release >> %s 2>&1\r\n' "$G_LOG"
  printf 'echo.>> %s\r\n' "$G_LOG"
  printf 'echo ==== Syxx.Tests ====>> %s\r\n' "$G_LOG"
  printf 'dotnet test Syxx.Tests\\Syxx.Tests.csproj >> %s 2>&1\r\n' "$G_LOG"
  printf 'echo __SYXX_DONE__>> %s\r\n' "$G_LOG"
} > "$BCMD"
"$UTMCTL" exec "$VM" --cmd cmd.exe /c "del $G_LOG 2>nul" >/dev/null 2>&1
"$UTMCTL" file push "$VM" "$DEST\\build.cmd" < "$BCMD"
"$UTMCTL" exec "$VM" --cmd cmd.exe /c "$DEST\\build.cmd" >/dev/null 2>&1 &

# 6) Pull the log back and show it.
hdr "Building in guest — polling for completion (up to ~8 min) ..."
LOG_LOCAL="$TMP/build.log"
done=0
for i in $(seq 1 50); do
  sleep 10
  "$UTMCTL" file pull "$VM" "$G_LOG" > "$LOG_LOCAL" 2>/dev/null || true
  if grep -q "__SYXX_DONE__" "$LOG_LOCAL" 2>/dev/null; then done=1; break; fi
done
echo "------------------------------------------------------------------------"
sed 's/__SYXX_DONE__//' "$LOG_LOCAL" 2>/dev/null
echo "------------------------------------------------------------------------"
rc=0
if [ "$done" != 1 ]; then
  err "Build did not finish within the timeout (or log unreadable). NOT clean."; rc=1
elif grep -qiE "Build FAILED|: error |error MSB|error CS|error NU|failed with|Failed: +[1-9]" "$LOG_LOCAL" 2>/dev/null; then
  err "Build/test reported errors (see log above)."; rc=1
else
  hdr "Build + tests clean (0 errors in the log)."
fi

# 7) Shut the VM down (unless asked to keep it up).
if [ "${SYXX_WIN_KEEP:-0}" != 1 ]; then
  hdr "Shutting down VM '$VM' ..."
  "$UTMCTL" stop "$VM" >/dev/null 2>&1
else
  hdr "Leaving VM '$VM' running (SYXX_WIN_KEEP=1)."
fi
exit $rc

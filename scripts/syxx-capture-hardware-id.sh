#!/bin/sh
# Syxx — privileged hardware-id capture (Linux).
#
# WHAT
#   Reads the machine's DMI UUID (root-only, 0400), salts+hashes it, and writes ONLY THE HASH to a
#   root-owned, world-readable file the unprivileged app can read. Run once at install/upgrade.
#
# WHY THIS SHAPE — the alternatives are all worse:
#   * setuid helper   — a permanently-privileged binary on every user's box, forever, so that we can
#                       read 16 bytes once. Any bug in it is a root bug. Not worth it.
#   * root daemon     — a long-lived privileged process for a value that never changes.
#   * pkexec/polkit   — an authentication prompt, at every launch, to answer "which machine is this".
#   * install-time    — the packager (.deb postinst / snap / rpm %post) ALREADY runs as root, once.
#                       Capture there and the app needs no privilege at all, ever. <- this
#
# THE SECURITY DECISION, stated plainly because getting it wrong is silent:
#   We write the HASH, never the raw UUID. /sys/class/dmi/id/product_uuid is mode 0400 ON PURPOSE —
#   the kernel hides it because it is a stable, cross-correlatable hardware identifier. Copying it
#   into a world-readable file would DEFEAT that protection and hand every process on the box a
#   permanent hardware id it currently cannot obtain. That would be a real privacy regression
#   shipped in the name of licensing. The salted hash is useless to anyone but Syxx.
#
# THE DUAL-BOOT PROPERTY (the whole point):
#   The file is per-install, but its VALUE is per-hardware. Install Ubuntu and Arch on one box and
#   each gets its own /var/lib/syxx/hardware-id — containing the SAME hash, because the DMI UUID is
#   the same. So one machine, one activation, and the family logic can do its job. This is exactly
#   what /etc/machine-id CANNOT do: that is regenerated per install, so it would read as two
#   machines and burn two activations.
#
# FAILURE IS NOT FATAL:
#   VMs and containers often have no readable/real DMI UUID. Then we write NOTHING and exit 0 — the
#   app falls back to an explicitly InstallScoped fingerprint and simply does not node-lock. A
#   licensing helper must never fail a package install.
#
# Usage:  sudo scripts/syxx-capture-hardware-id.sh [output-path]
set -eu

OUT="${1:-/var/lib/syxx/hardware-id}"
DMI="${SYXX_DMI_PATH:-/sys/class/dmi/id/product_uuid}"
# MUST match MachineFingerprints.Salt in Windows/Syxx.Kit/Platform/MachineFingerprint.cs.
# MachineFingerprintTests.TheHelperScriptAgreesWithTheKitHash pins the two together.
SALT="com.milkandgin.syxx.fingerprint.v1:"

log() { echo "syxx-hardware-id: $*" >&2; }

sha256_hex() {
    # Portable across coreutils (Linux) and BSD/macOS, so the algorithm can be tested off-Linux.
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum | cut -d' ' -f1
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 | cut -d' ' -f1
    else
        log "no sha256sum/shasum available; skipping (app will use an install-scoped fingerprint)"
        return 1
    fi
}

if [ ! -r "$DMI" ]; then
    # Almost always: not root (0400), or a container with no DMI. Not an error.
    log "cannot read $DMI (root-only, or absent) — skipping; the app will fall back to an install-scoped id"
    exit 0
fi

RAW="$(tr -d '\n\r' < "$DMI" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')"

# Placeholder UUIDs: kernels print all-zero / all-f on hardware and VMs with no real UUID. Every
# such box would share ONE fingerprint — worse than having none, because it looks legitimate.
NORM="$(printf '%s' "$RAW" | tr -d '-' | tr 'A-F' 'a-f')"
case "$NORM" in
    "" ) log "empty DMI UUID — skipping"; exit 0 ;;
    *[!0]* ) : ;;                                  # contains a non-'0' -> not the all-zero placeholder
    * ) log "placeholder (all-zero) DMI UUID — skipping"; exit 0 ;;
esac
case "$NORM" in
    *[!f]* ) : ;;                                  # contains a non-'f' -> not the all-f placeholder
    * ) log "placeholder (all-f) DMI UUID — skipping"; exit 0 ;;
esac

HASH="$(printf '%s' "${SALT}${RAW}" | sha256_hex)" || exit 0
if [ -z "$HASH" ]; then
    log "hashing produced nothing — skipping"
    exit 0
fi

DIR="$(dirname "$OUT")"
mkdir -p "$DIR"

# Write atomically: a half-written fingerprint read mid-install would be a WRONG machine identity,
# not a missing one. umask 022 => 0644, so the unprivileged app can read it and only root can write
# it — a user-writable file here would let anyone forge their own machine identity.
TMP="$(mktemp "${OUT}.XXXXXX")"
umask 022
printf '%s\n' "$HASH" > "$TMP"
chmod 0644 "$TMP"
mv -f "$TMP" "$OUT"

log "captured hardware id -> $OUT (hash only; the raw DMI UUID was not stored)"
exit 0

#!/bin/bash
# Build a distributable Syxx .deb package for Debian/Mint/Ubuntu.
# Usage: ./build_deb.sh [release|debug]
# Output: dist/syxx_<version>_amd64.deb (or arm64)
#
# P-09/P-17 — WHAT THIS SCRIPT USED TO SHIP, AND WHY NOBODY NOTICED
#   It published ONLY the Avalonia GUI and wrote a launcher that read
#       exec /opt/syxx/Syxx "$@"
#   Both projects set <AssemblyName>Syxx</AssemblyName> — Windows/dotNET/Syxx/Syxx.csproj (the
#   CLI) and Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj (the GUI) — so the payload
#   looked right. It was not. On a .deb install `/usr/bin/syxx` resolved to the GUI, and the
#   GUI's own Cage pane told the user to run `sudo syxx cage --duration 30`, which launched the
#   GUI as root with an argument it ignores. The snap had the mirror-image defect: its part
#   pointed at Windows/dotNET/Syxx, so it shipped the CLI and no GUI.
#
#   macOS solved this and GUARDS it. macOS/bundle-app.sh:22-33 builds `--product syxx`, copies
#   it into Contents/Resources/, and fails the build if it is not there, because otherwise you
#   ship "a distributable .app where Cage cannot start a real capture." This script now does the
#   same thing, for the same reason.
#
#   Both binaries now ship: the CLI as `syxx`, the GUI as `syxx-gui`, exactly as macOS does.
#   That makes every "run `sudo syxx cage`" string already in the product TRUE.
#
# set -u and -o pipefail are deliberate additions: the version derivation and the ICU probe are
# both pipelines whose first stage failing must not read as an empty-but-successful answer.
set -euo pipefail

# E057: resolve to repo root (script lives at Linux/Ubuntu/ inside the repo).
# Old code: cd "$(dirname "$0")" left ROOT at Linux/Ubuntu/, making every
# subsequent relative path (Windows/..., Design/..., Ubuntu/...) wrong.
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

CONFIG="${1:-release}"
ROOT="${REPO_ROOT}"
DIST="${ROOT}/dist"

# Derive version from SyxxConfig.cs
SYXX_VERSION=$(grep 'public const string Version' "${ROOT}/Windows/dotNET/Syxx.Kit/SyxxConfig.cs" | sed 's/.*"\(.*\)".*/\1/')
# E057: never build an unversioned package — fail closed if the grep misses.
if [ -z "${SYXX_VERSION}" ]; then
    echo "❌ Could not extract version from SyxxConfig.cs" >&2
    exit 1
fi
SYXX_BUILD=$(git rev-list --count HEAD 2>/dev/null || echo 1)
# SYXX_DEB_ARCH overrides for cross-builds (e.g. macOS host, where a Homebrew
# dpkg reports "darwin-arm64" — not a Debian architecture). The control file
# must only ever carry a real Debian arch that matches the published RID.
ARCH="${SYXX_DEB_ARCH:-$(dpkg --print-architecture 2>/dev/null || echo amd64)}"
case "$ARCH" in
    amd64|arm64) ;;
    *)
        echo "❌ '$ARCH' is not a supported Debian architecture (amd64|arm64)." >&2
        echo "   Set SYXX_DEB_ARCH=amd64 or SYXX_DEB_ARCH=arm64 for cross-builds." >&2
        exit 1
        ;;
esac
DEB_NAME="syxx_${SYXX_VERSION}_${ARCH}.deb"

echo "=== Building Syxx ${SYXX_VERSION} (build ${SYXX_BUILD}) for ${ARCH} ==="

# Determine RID based on architecture
if [ "$ARCH" = "arm64" ]; then
    RID="linux-arm64"
else
    RID="linux-x64"
fi

# Step 1: Build BOTH halves of the product.
#
# Separate output trees are mandatory, not tidiness: both publishes emit an apphost literally
# named `Syxx`, so a shared -o would have one silently overwrite the other and the resulting
# package would again contain only one program.
#
# Untrimmed on purpose, matching the note in both csprojs: NSec and the DNS reflection paths are
# trim-hostile, and a trimming drop is the "green build that lies" class. Do NOT add
# PublishTrimmed here to shrink the package.
CLI_PUBLISH="${DIST}/publish-cli"
GUI_PUBLISH="${DIST}/publish-gui"
rm -rf "${CLI_PUBLISH}" "${GUI_PUBLISH}"

echo "--- Building the syxx CLI for ${RID} ---"
dotnet publish "${ROOT}/Windows/dotNET/Syxx/Syxx.csproj" \
    -c "$CONFIG" -r "$RID" --self-contained true -o "${CLI_PUBLISH}"

echo "--- Building the Avalonia GUI for ${RID} ---"
dotnet publish "${ROOT}/Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj" \
    -c "$CONFIG" -r "$RID" --self-contained true -o "${GUI_PUBLISH}"

# Step 2: Create .deb package structure
echo "--- Creating .deb package structure ---"
PKG_DIR="${DIST}/pkg"
rm -rf "${PKG_DIR}"
mkdir -p "${PKG_DIR}/DEBIAN"
mkdir -p "${PKG_DIR}/opt/syxx/cli"
mkdir -p "${PKG_DIR}/opt/syxx/gui"
mkdir -p "${PKG_DIR}/usr/bin"
mkdir -p "${PKG_DIR}/usr/lib/syxx"
mkdir -p "${PKG_DIR}/usr/share/applications"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/512x512/apps"
mkdir -p "${PKG_DIR}/usr/share/polkit-1/actions"

cp -R "${CLI_PUBLISH}/." "${PKG_DIR}/opt/syxx/cli/"
cp -R "${GUI_PUBLISH}/." "${PKG_DIR}/opt/syxx/gui/"

# P-11: drop the LTTng tracepoint provider from both payloads. It is dlopen'd only when LTTng
# tracing is explicitly enabled, and it links liblttng-ust, which ubuntu:24.04 does not ship —
# the install-smoke ldd gate rightly refuses it. Removing the optional provider is honest;
# adding a distro dependency for tracing nobody uses is not.
rm -f "${PKG_DIR}/opt/syxx/cli/libcoreclrtraceptprovider.so" \
      "${PKG_DIR}/opt/syxx/gui/libcoreclrtraceptprovider.so"

STAGED_CLI="${PKG_DIR}/opt/syxx/cli/Syxx"
STAGED_GUI="${PKG_DIR}/opt/syxx/gui/Syxx"

# --- P-09/P-17 fail-closed staging guard (twin of macOS/bundle-app.sh:29-33, D073) -----------
# A silent publish failure, an <AssemblyName> change, or a csproj regression that drops one of
# the two projects would otherwise produce a package that installs cleanly and is missing half
# the product. That is precisely how the CLI/GUI split survived: the build was always green.
is_elf() {
    # First four bytes must be 7f 45 4c 46. Catches a wrong-RID publish, which leaves a
    # plausibly-sized apphost that cannot execute on the target at all.
    [ "$(od -An -tx1 -N4 "$1" 2>/dev/null | tr -d ' \n')" = "7f454c46" ]
}

assert_shipped_binary() {
    # assert_shipped_binary <staged-path> <installed-path> <what-breaks-without-it>
    local staged="$1" installed="$2" consequence="$3"
    if [ ! -f "$staged" ]; then
        echo "❌ ERROR: staged binary is missing: $staged" >&2
        echo "   It would install to $installed. $consequence" >&2
        echo "   Check that the corresponding 'dotnet publish' above succeeded and still emits" >&2
        echo "   an apphost named 'Syxx' (<AssemblyName> in the csproj)." >&2
        exit 1
    fi
    if [ ! -s "$staged" ]; then
        echo "❌ ERROR: staged binary is empty: $staged" >&2
        exit 1
    fi
    if ! is_elf "$staged"; then
        echo "❌ ERROR: staged binary is not an ELF executable: $staged" >&2
        echo "   The publish produced a host for the wrong platform. Expected RID ${RID}." >&2
        exit 1
    fi
    # dotnet sets the exec bit when publishing on Linux; a macOS cross-publish does not always.
    # Set it deliberately, then assert, so a chmod failure is also fatal.
    chmod 0755 "$staged"
    if [ ! -x "$staged" ]; then
        echo "❌ ERROR: staged binary is not executable after chmod: $staged" >&2
        exit 1
    fi
}

assert_shipped_binary "$STAGED_CLI" "/opt/syxx/cli/Syxx" \
    "Without it /usr/bin/syxx does not exist, and every 'sudo syxx cage' instruction the GUI prints is a lie."
assert_shipped_binary "$STAGED_GUI" "/opt/syxx/gui/Syxx" \
    "Without it the desktop entry launches nothing and the package has no graphical client at all."

# Existence is not enough. The defect this guard exists to stop is "one half of the product
# published twice", and a file check cannot see that.
#
# The two native apphosts are LEGITIMATELY byte-identical — the .NET apphost is a generic
# launcher patched with the name of the managed assembly it loads, and both projects set
# <AssemblyName>Syxx</AssemblyName>, so both get patched with "Syxx.dll" and come out identical
# (verified: 78256 bytes each). Do not compare them; that check fails on a correct package. The
# product is in the managed assembly beside the apphost.
if cmp -s "${PKG_DIR}/opt/syxx/cli/Syxx.dll" "${PKG_DIR}/opt/syxx/gui/Syxx.dll"; then
    echo "❌ ERROR: opt/syxx/cli/Syxx.dll and opt/syxx/gui/Syxx.dll are byte-identical." >&2
    echo "   One publish overwrote the other and this package ships one program twice." >&2
    exit 1
fi
# Positive identification, so the guard cannot pass on two copies of the wrong half.
if [ ! -f "${PKG_DIR}/opt/syxx/gui/Avalonia.dll" ]; then
    echo "❌ ERROR: opt/syxx/gui contains no Avalonia.dll — that tree is not the GUI." >&2
    exit 1
fi
if [ -f "${PKG_DIR}/opt/syxx/cli/Avalonia.dll" ]; then
    echo "❌ ERROR: opt/syxx/cli contains Avalonia.dll — the GUI was published into the CLI's directory." >&2
    exit 1
fi
echo "Shipped binaries verified: opt/syxx/cli (CLI) + opt/syxx/gui (Avalonia GUI), distinct assemblies"

# Create launchers. TWO of them, and the names are load-bearing: `syxx` is the CLI because that
# is the name every printed instruction, every doc, and the polkit action all use.
cat > "${PKG_DIR}/usr/bin/syxx" <<'LAUNCHER'
#!/bin/bash
# The unified Syxx CLI. `sudo syxx cage`, `syxx mirror`, `syxx witness` all land here.
exec /opt/syxx/cli/Syxx "$@"
LAUNCHER
chmod 755 "${PKG_DIR}/usr/bin/syxx"

cat > "${PKG_DIR}/usr/bin/syxx-gui" <<'LAUNCHER'
#!/bin/bash
# The Avalonia desktop client. Deliberately NOT named `syxx`: it ignores CLI arguments, so
# resolving `sudo syxx cage --duration 30` to this program launches a GUI as root that silently
# drops the request (P-09).
exec /opt/syxx/gui/Syxx "$@"
LAUNCHER
chmod 755 "${PKG_DIR}/usr/bin/syxx-gui"

# The privileged hardware-id capture helper, invoked once by DEBIAN/postinst (P-03). It must be
# INSIDE the package: postinst runs on the user's machine, where the repo does not exist.
HWID_HELPER_SRC="${ROOT}/scripts/syxx-capture-hardware-id.sh"
if [ ! -f "${HWID_HELPER_SRC}" ]; then
    echo "❌ ERROR: ${HWID_HELPER_SRC} not found." >&2
    echo "   Without it postinst cannot capture a hardware id, every Linux licence silently falls" >&2
    echo "   back to a non-node-lockable install-scoped fingerprint, and each reinstall burns" >&2
    echo "   another activation. Refusing to build a package with that defect baked in." >&2
    exit 1
fi
cp "${HWID_HELPER_SRC}" "${PKG_DIR}/usr/lib/syxx/syxx-capture-hardware-id.sh"
chmod 0755 "${PKG_DIR}/usr/lib/syxx/syxx-capture-hardware-id.sh"

# The polkit action (P-02). pkexec is already load-bearing on Linux — Syxx.Kit/UpdateService.cs:409
# shells `pkexec dpkg -i` — but no action file was ever shipped, so every escalation fell back to
# the generic "run `/usr/bin/syxx' as the super user" dialog.
POLKIT_SRC="${ROOT}/Linux/Ubuntu/polkit/app.syxx.policy"
if [ ! -f "${POLKIT_SRC}" ]; then
    echo "❌ ERROR: ${POLKIT_SRC} not found — refusing to ship a package that declares no polkit action." >&2
    exit 1
fi
cp "${POLKIT_SRC}" "${PKG_DIR}/usr/share/polkit-1/actions/app.syxx.policy"
chmod 0644 "${PKG_DIR}/usr/share/polkit-1/actions/app.syxx.policy"

# Maintainer scripts (P-10). There were none at all before this.
#
# THERE IS DELIBERATELY NO prerm, and that is a decision rather than an omission. A prerm exists
# to stop something before dpkg unlinks the files under it: a daemon, an init script, an
# update-alternatives registration, an ldconfig-visible library. This package installs none of
# those. Its only state outside the file list is /var/lib/syxx/hardware-id, which postrm handles
# on purge — Debian Policy §6.8 puts it there, not in prerm. A no-op prerm would be one more
# script dpkg has to run and one more place a `set -e` slip can wedge an uninstall.
for script in postinst postrm; do
    src="${ROOT}/Linux/Ubuntu/debian/${script}"
    if [ ! -f "${src}" ]; then
        echo "❌ ERROR: maintainer script ${src} not found." >&2
        echo "   Without postinst the hardware id is never captured (P-03) and the icon/desktop" >&2
        echo "   caches are never refreshed. Refusing to build." >&2
        exit 1
    fi
    cp "${src}" "${PKG_DIR}/DEBIAN/${script}"
    # dpkg refuses to run a maintainer script that is not executable.
    chmod 0755 "${PKG_DIR}/DEBIAN/${script}"
done

# Create .desktop file. Exec points at the GUI launcher, not at `syxx`.
cat > "${PKG_DIR}/usr/share/applications/syxx.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Syxx
Comment=DNS tampering detection toolkit
Exec=/usr/bin/syxx-gui
Icon=syxx
Terminal=false
Categories=Network;Security;
Keywords=DNS;Security;Tampering;
DESKTOP

# E057: icon resolved from repo root; missing icon is fatal (a .desktop with
# Icon=syxx and no icon file produces a broken launcher — never ship silently).
if [ ! -f "${ROOT}/Design/logos/syxx-logo-source.png" ]; then
    echo "❌ Icon not found at ${ROOT}/Design/logos/syxx-logo-source.png — refusing to build a package with a broken launcher entry." >&2
    exit 1
fi
cp "${ROOT}/Design/logos/syxx-logo-source.png" "${PKG_DIR}/usr/share/icons/hicolor/512x512/apps/syxx.png"

# ICU dependency is distribution-aware. Unconditional libicu74 produced uninstallable
# packages on hosts that ship libicu72/libicu76. Fail closed on unsupported distros.
resolve_icu_depends() {
    local ver=""
    if command -v dpkg-query >/dev/null 2>&1; then
        ver=$(dpkg-query -W -f='${Version}\n' libicu74 2>/dev/null | head -1 || true)
        if [ -n "$ver" ]; then echo "libc6, libicu74"; return 0; fi
        ver=$(dpkg-query -W -f='${Version}\n' libicu76 2>/dev/null | head -1 || true)
        if [ -n "$ver" ]; then echo "libc6, libicu76"; return 0; fi
        ver=$(dpkg-query -W -f='${Version}\n' libicu72 2>/dev/null | head -1 || true)
        if [ -n "$ver" ]; then echo "libc6, libicu72"; return 0; fi
    fi
    # Cross-build on non-Debian hosts: allow override, else fail closed.
    if [ -n "${SYXX_DEB_ICU_DEPENDS:-}" ]; then
        echo "${SYXX_DEB_ICU_DEPENDS}"
        return 0
    fi
    echo "❌ No supported libicu package detected (tried 74/76/72)." >&2
    echo "   Set SYXX_DEB_ICU_DEPENDS='libc6, libicuNN' for a known target, or build on the target distro." >&2
    exit 1
}
ICU_DEPENDS="$(resolve_icu_depends)"

# --- P-11: the rest of Depends, audited against what the product actually loads --------------
#
# libsecret-1-0     Syxx.Kit/LinuxSecretStore.cs:380 does NativeLibrary.Load("libsecret-1.so.0").
#                   Absent => LibSecretNative.IsAvailable is false for every call.
# libsecret-tools   provides `secret-tool`, and this is the package that actually matters. Read
#                   LinuxSecretStore.cs:123-135: the P/Invoke backend passes the keyType VALUE in
#                   an attribute-NAME slot, so secret_password_store_sync returns FALSE without
#                   even setting a GError and EVERY write through it fails silently. secret-tool
#                   is therefore the only backend that can genuinely store into the keyring, and
#                   the only one that can answer Exists(keyType) at all. Without it the file
#                   store at ~/.syxx/keystore is the sole backend — the degraded path P-11 names.
#                   It is a Depends, not a Recommends, for exactly that reason.
# pkexec            UpdateService.cs:409 execs /usr/bin/pkexec, and the shipped polkit action is
#                   useless without it. The alternation is not cosmetic: `policykit-1` is a
#                   TRANSITIONAL package on Debian 12 / Ubuntu 24.04 and is GONE in Debian 13, so
#                   depending on it alone yields an uninstallable package there; while Ubuntu
#                   22.04 has no `pkexec` package and ships the binary inside `policykit-1`.
#                   First alternative wins where it exists, so this resolves correctly on both.
# libfontconfig1    SkiaSharp (Avalonia's renderer) links fontconfig; without it the GUI aborts
#   libstdc++6      during startup before drawing a single frame.
#   libgcc-s1
# libx11-6          Avalonia's X11 backend dlopens these. Same failure mode: no window, no error
#   libice6         the user can act on.
#   libsm6
# iproute2          LAN/LanCartography.cs:715 runs `ip neigh show`. It catches and falls back to
#                   `arp -a`, but net-tools is NOT installed by default on modern Ubuntu, so with
#                   neither present the neighbour table is simply unreadable on every run. Priority
#                   is 'important' on Debian so this costs nothing in practice; declaring it is
#                   what makes the container leg in install-smoke-deb prove it.
#
# NOT listed, and each of these was checked rather than assumed:
#   libglib2.0-0t64 / libgobject-2.0  LinuxSecretStore.cs:1162,1175 dlopen them, but libsecret-1-0
#                   depends on both, so they arrive with it. A second declaration would only be a
#                   name that has to be kept in step across suites.
#   bind9-host      A grep for a shelled `host` matches only IndependenceGroup: "host" in
#                   Synthesis/SyxxMatrixIntegration.cs and a <paramref name="host"/> in
#                   DimensionClient.cs. The product does not shell `host`. Do not re-add this.
#   dpkg            UpdateService.cs:399/409 shells it, but dpkg is Essential: yes. Depending on an
#                   Essential package is noise Debian Policy §3.5 tells you not to write.
#
# Recommends rather than Depends: a Secret Service provider (the keyring daemon itself),
# traceroute, and systemd-resolved. All three are real functional gaps when absent, but all three
# degrade to an honest inconclusive rather than to a wrong answer — NetworkKeyState.cs:340 falls
# back to /etc/resolv.conf when `resolvectl status` cannot run, and EncryptedDNSConfigurator.cs:105
# reports the failure rather than claiming a configuration it did not make. None is present on a
# server install where the CLI alone is perfectly usable.
DEB_DEPENDS="${ICU_DEPENDS}, libsecret-1-0, libsecret-tools, pkexec | policykit-1, libfontconfig1, libstdc++6, libgcc-s1, libx11-6, libice6, libsm6, iproute2"
DEB_RECOMMENDS="gnome-keyring, traceroute, systemd-resolved"
DEB_SUGGESTS="unbound, unbound-anchor"

# Create control file
cat > "${PKG_DIR}/DEBIAN/control" <<CONTROL
Package: syxx
Version: ${SYXX_VERSION}-${SYXX_BUILD}
Section: net
Priority: optional
Architecture: ${ARCH}
Depends: ${DEB_DEPENDS}
Recommends: ${DEB_RECOMMENDS}
Suggests: ${DEB_SUGGESTS}
Maintainer: Syxx <support@syxx.app>
Description: DNS tampering detection toolkit
  Syxx investigates and detects DNS tampering, hijacking, spoofing,
  and censorship. It produces evidence a user can actually trust.
  .
  Ships two programs: the syxx command-line investigator and the
  syxx-gui desktop client.
Homepage: https://syxx.app
CONTROL


# Step 3: Build .deb
echo "--- Building .deb package ---"
mkdir -p "${DIST}"
# --root-owner-group: payload must install root-owned; without it the archive
# embeds the build user's uid/gid (e.g. 501:20 on macOS), leaving /opt/syxx
# writable by an arbitrary uid on the target system.
dpkg-deb --root-owner-group --build "${PKG_DIR}" "${DIST}/${DEB_NAME}"

# Step 4: Clean up
rm -rf "${PKG_DIR}" "${CLI_PUBLISH}" "${GUI_PUBLISH}"

# Step 5: Report
echo ""
echo "=== Done ==="
echo "Package: ${DIST}/${DEB_NAME}"
echo "Size: $(du -h "${DIST}/${DEB_NAME}" | cut -f1)"
echo "Contains: /usr/bin/syxx (CLI), /usr/bin/syxx-gui (Avalonia GUI)"
echo ""
echo "To install: sudo apt install ./${DEB_NAME}"
echo "  (apt, not 'dpkg -i': dpkg does not resolve Depends, and a missing"
echo "   libsecret-tools silently downgrades key storage to the file store.)"
echo "To sign (requires GPG key):"
echo "  dpkg-sig --sign builder ${DEB_NAME}"

#!/bin/bash
# Build a distributable Syxx .deb package for Debian/Mint/Ubuntu.
# Usage: ./build_deb.sh [release|debug]
# Output: dist/syxx_<version>_amd64.deb (or arm64)
set -e

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

# Step 1: Build the Avalonia app
echo "--- Building Avalonia app for ${RID} ---"
# E057: path corrected — ROOT is now the repo root, so the project is at Linux/Ubuntu/...
cd "${ROOT}/Linux/Ubuntu/SyxxUI.Avalonia"
dotnet publish -c "$CONFIG" -r "$RID" --self-contained true -o "${ROOT}/dist/publish"
cd "$ROOT"

# Step 2: Create .deb package structure
echo "--- Creating .deb package structure ---"
PKG_DIR="${DIST}/pkg"
rm -rf "${PKG_DIR}"
mkdir -p "${PKG_DIR}/DEBIAN"
mkdir -p "${PKG_DIR}/opt/syxx"
mkdir -p "${PKG_DIR}/usr/bin"
mkdir -p "${PKG_DIR}/usr/share/applications"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/512x512/apps"

# Copy published app
cp -R "${DIST}/publish/"* "${PKG_DIR}/opt/syxx/"

# Create launcher script
cat > "${PKG_DIR}/usr/bin/syxx" <<'LAUNCHER'
#!/bin/bash
exec /opt/syxx/Syxx "$@"
LAUNCHER
chmod 755 "${PKG_DIR}/usr/bin/syxx"

# Create .desktop file
cat > "${PKG_DIR}/usr/share/applications/syxx.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Syxx
Comment=DNS tampering detection toolkit
Exec=/usr/bin/syxx
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

# Create control file
cat > "${PKG_DIR}/DEBIAN/control" <<CONTROL
Package: syxx
Version: ${SYXX_VERSION}-${SYXX_BUILD}
Section: net
Priority: optional
Architecture: ${ARCH}
Depends: ${ICU_DEPENDS}
Maintainer: Syxx <support@syxx.app>
Description: DNS tampering detection toolkit
  Syxx investigates and detects DNS tampering, hijacking, spoofing,
  and censorship. It produces evidence a user can actually trust.
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
rm -rf "${PKG_DIR}" "${DIST}/publish"

# Step 5: Report
echo ""
echo "=== Done ==="
echo "Package: ${DIST}/${DEB_NAME}"
echo "Size: $(du -h "${DIST}/${DEB_NAME}" | cut -f1)"
echo ""
echo "To install: sudo dpkg -i ${DEB_NAME}"
echo "To sign (requires GPG key):"
echo "  dpkg-sig --sign builder ${DEB_NAME}"

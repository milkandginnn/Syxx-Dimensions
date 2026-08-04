#!/bin/bash
# Build a distributable Syxx DMG from the app bundle.
# Usage: ./build_dmg.sh [release|debug]
# Output: dist/Syxx-<version>-<build>.dmg
set -e
cd "$(dirname "$0")/.."

CONFIG="${1:-release}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "$SCRIPT_DIR")"
DIST="${ROOT}/dist"

# Derive version from SyxxConfig.swift
SYXX_VERSION=$(grep 'public static let version' Sources/SyxxKit/SyxxConfig.swift | sed 's/.*"\(.*\)".*/\1/')
SYXX_BUILD=$(git rev-list --count HEAD 2>/dev/null || echo 1)
DMG_NAME="Syxx-${SYXX_VERSION}-${SYXX_BUILD}.dmg"
APP_NAME="Syxx.app"

echo "=== Building Syxx ${SYXX_VERSION} (build ${SYXX_BUILD}) ==="

# Step 1: Build the app bundle
echo "--- Building app bundle ---"
bash "${SCRIPT_DIR}/../bundle-app.sh" "$CONFIG"

# Step 2: Create staging directory
STAGING="${DIST}/staging"
rm -rf "${STAGING}"
mkdir -p "${STAGING}"

cp -R "${APP_NAME}" "${STAGING}/"

# Create a symlink to /Applications for drag-to-install
ln -s /Applications "${STAGING}/Applications"

# Step 3: Create DMG
mkdir -p "${DIST}"
DMG_PATH="${DIST}/${DMG_NAME}"
rm -f "${DMG_PATH}"

echo "--- Creating DMG ---"
hdiutil create -volname "Syxx" \
    -srcfolder "${STAGING}" \
    -ov -format UDZO \
    "${DMG_PATH}" \
    -imagekey zlib-level=9

# Step 4: Clean up staging
rm -rf "${STAGING}"

# Step 4a: Record SHA-256 checksum (D085: canonical release artifact fingerprint)
SHASUM_PATH="${DIST}/Syxx-${SYXX_VERSION}-${SYXX_BUILD}.sha256"
shasum -a 256 "${DMG_PATH}" | sed "s|${DIST}/||" > "${SHASUM_PATH}"

# Step 5: Report
echo ""
echo "=== Done ==="
echo "DMG: ${DMG_PATH}"
echo "SHA: ${SHASUM_PATH}"
echo "Size: $(du -h "${DMG_PATH}" | cut -f1)"
echo ""
echo "To notarize (requires Apple Developer ID):"
echo "  xcrun notarytool submit '${DMG_PATH}' --apple-id \$APPLE_ID --team-id \$TEAM_ID --password \$APP_PASSWORD --wait"
echo "  xcrun stapler staple '${DMG_PATH}'"
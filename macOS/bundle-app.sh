#!/bin/bash
# Wrap the SwiftPM `syxx-gui` binary into a distributable Syxx.app so Finder and
# the Dock show the Syxx icon (CFBundleIconFile). SwiftPM alone only emits a bare
# executable; this assembles the .app around it, including the SwiftPM resource
# bundle so Bundle.module keeps resolving at runtime.
set -e
cd "$(dirname "$0")"
CONFIG="${1:-release}"

swift build -c "$CONFIG" --product syxx-gui
swift build -c "$CONFIG" --product DeepSyxx
BINDIR="$(swift build -c "$CONFIG" --product syxx-gui --show-bin-path)"

APP="Syxx.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Library/LaunchServices"

cp "$BINDIR/syxx-gui" "$APP/Contents/MacOS/Syxx"
cp Sources/SyxxApp/Resources/Syxx.icns "$APP/Contents/Resources/Syxx.icns"
# Bundle the unified `syxx` CLI so the GUI's Cage can re-exec it (with the `cage` subcommand) for
# real, admin-authorized packet capture. locateCageBinary() looks for `syxx` in Resources first.
swift build -c "$CONFIG" --product syxx
cp "$BINDIR/syxx" "$APP/Contents/Resources/syxx"

# D073: post-build assertion — the bundled CLI must exist and be executable. A silent build
# failure (or a Package.swift regression that removes the syxx product) would produce a
# distributable .app where Cage cannot start a real capture. Fail the bundle rather than
# shipping a broken package.
if [ ! -x "$APP/Contents/Resources/syxx" ]; then
  echo "ERROR: bundled CLI binary is missing or not executable: $APP/Contents/Resources/syxx"
  echo "The GUI's Cage feature requires this binary. Check that 'swift build --product syxx' succeeded."
  exit 1
fi

# Bundle DeepSyxx privileged helper for SMJobBless installation.
# NOTE: the launchd plist is deliberately NOT copied here as a sidecar file. SMJobBless
# reads it only from the helper's embedded __TEXT,__launchd_plist section (created by the
# linker flags in Package.swift:79-96). A sidecar copy is never read and can silently drift
# out of sync with the embedded one. See D079.
cp "$BINDIR/DeepSyxx" "$APP/Contents/Library/LaunchServices/com.milkandgin.deepsyxx"

# --- Assert the SMJobBless-required Mach-O sections are actually embedded (D079) ---
# Without __TEXT,__launchd_plist, SMJobBless fails at install time with
# kSMErrorJobPlistNotFound (CFErrorDomainLaunchd 8) and the helper can never run. This
# class of defect is otherwise silent: the build succeeds and the app ships broken.
HELPER="$APP/Contents/Library/LaunchServices/com.milkandgin.deepsyxx"
HELPER_LABEL="com.milkandgin.deepsyxx"

extract_macho_section() {
  # extract_macho_section <binary> <sectname> -> raw section bytes on stdout
  /usr/bin/python3 - "$1" "$2" <<'PY'
import subprocess, sys
binary, sect = sys.argv[1], sys.argv[2]
out = subprocess.run(["/usr/bin/otool", "-l", binary],
                     capture_output=True, text=True, check=True).stdout.splitlines()
off = size = None
for i, line in enumerate(out):
    if line.strip() == "sectname " + sect:
        for f in out[i:i + 12]:
            parts = f.split()
            if len(parts) == 2 and parts[0] == "size":
                size = int(parts[1], 16)
            elif len(parts) == 2 and parts[0] == "offset":
                off = int(parts[1])
        break
if off is None or size is None:
    sys.exit(1)
with open(binary, "rb") as fh:
    fh.seek(off)
    sys.stdout.buffer.write(fh.read(size))
PY
}

for SECT in __info_plist __launchd_plist; do
  if ! otool -l "$HELPER" | grep -q "sectname $SECT"; then
    echo "ERROR: helper is missing the __TEXT,$SECT section."
    echo "SMJobBless will fail (kSMErrorJobPlistNotFound). Check the DeepSyxx linkerSettings in Package.swift."
    exit 1
  fi
done

EMBEDDED_LAUNCHD="$(mktemp -t deepsyxx-launchd)"
trap 'rm -f "$EMBEDDED_LAUNCHD"' EXIT
if ! extract_macho_section "$HELPER" __launchd_plist > "$EMBEDDED_LAUNCHD"; then
  echo "ERROR: could not read the embedded __TEXT,__launchd_plist section from the helper."
  exit 1
fi
if ! plutil -lint "$EMBEDDED_LAUNCHD" >/dev/null 2>&1; then
  echo "ERROR: the embedded __TEXT,__launchd_plist section is not a valid property list (truncated or corrupt)."
  exit 1
fi
EMBEDDED_LABEL="$(plutil -extract Label raw -o - "$EMBEDDED_LAUNCHD" 2>/dev/null || true)"
if [ "$EMBEDDED_LABEL" != "$HELPER_LABEL" ]; then
  echo "ERROR: embedded launchd plist Label is '$EMBEDDED_LABEL', expected '$HELPER_LABEL'."
  echo "The Label must match the helper filename in Contents/Library/LaunchServices/."
  exit 1
fi
if ! plutil -extract MachServices xml1 -o - "$EMBEDDED_LAUNCHD" 2>/dev/null | grep -q "<key>$HELPER_LABEL</key>"; then
  echo "ERROR: embedded launchd plist does not declare MachServices['$HELPER_LABEL']."
  echo "The app connects with NSXPCConnection(machServiceName: \"$HELPER_LABEL\"); without this key nothing claims it."
  exit 1
fi
if ! plutil -extract ProgramArguments.0 raw -o - "$EMBEDDED_LAUNCHD" 2>/dev/null \
     | grep -qx "/Library/PrivilegedHelperTools/$HELPER_LABEL"; then
  echo "ERROR: embedded launchd plist ProgramArguments[0] must be /Library/PrivilegedHelperTools/$HELPER_LABEL."
  exit 1
fi
# Both ends of the XPC connection hard-code the Mach service name in Swift. If either
# drifts from the embedded plist the bless succeeds and the connection then silently
# never resolves, which is far harder to diagnose than a build failure.
if ! grep -q "static let machServiceName = \"$HELPER_LABEL\"" Sources/DeepSyxx/main.swift; then
  echo "ERROR: DeepSyxx/main.swift does not declare machServiceName = \"$HELPER_LABEL\"."
  echo "The daemon would listen on a Mach service the launchd plist does not declare."
  exit 1
fi
if ! grep -q "NSXPCConnection(machServiceName: \"$HELPER_LABEL\"" Sources/SyxxApp/PrivilegeManager.swift; then
  echo "ERROR: SyxxApp/PrivilegeManager.swift does not connect to machServiceName \"$HELPER_LABEL\"."
  echo "The app would connect to a Mach service the helper never claims."
  exit 1
fi
echo "Helper sections verified: __info_plist + __launchd_plist (Label=$EMBEDDED_LABEL, MachServices OK)"

# --- Typography ---
# A face that fails to copy is INVISIBLE at runtime: Font.custom() falls back to the system
# font and reports nothing. The old `2>/dev/null || true` on the .ttf copy swallowed exactly
# that, so a zero-font bundle built "successfully". It now fails the build instead.
# (.otf keeps its || true: the project ships no OpenType faces, so an empty glob is expected.)
cp Sources/SyxxApp/Resources/*.otf "$APP/Contents/Resources/" 2>/dev/null || true
cp Sources/SyxxApp/Resources/*.ttf "$APP/Contents/Resources/"

# Assert every face declared in SyxxTypography.bundledFonts actually landed in the bundle.
# Driven off the manifest, so adding a face to IoDesignSystem.swift without shipping the
# .ttf breaks the build rather than the rendering.
TYPO_SRC="Sources/SyxxApp/Views/Components/IoDesignSystem.swift"
DECLARED_FACES=$(sed -n '/static let bundledFonts/,/^    \]/p' "$TYPO_SRC" \
                 | sed -n 's/.*resourceName: "\([^"]*\)".*/\1/p')
DECLARED_COUNT=$(printf '%s\n' "$DECLARED_FACES" | grep -c . || true)
if [ "$DECLARED_COUNT" -eq 0 ]; then
  echo "ERROR: could not read SyxxTypography.bundledFonts from $TYPO_SRC."
  echo "The font-packaging gate cannot verify anything; refusing to ship an unverified bundle."
  exit 1
fi

MISSING_FACES=""
for face in $DECLARED_FACES; do
  [ -f "$APP/Contents/Resources/$face.ttf" ] || MISSING_FACES="$MISSING_FACES $face"
done
if [ -n "$MISSING_FACES" ]; then
  echo "ERROR: declared faces missing from $APP/Contents/Resources:$MISSING_FACES"
  echo "Each would silently render as the system font. Confirm the flat .ttf files sit at"
  echo "Sources/SyxxApp/Resources/ root — Bundle.module lookups do NOT recurse into subfolders."
  exit 1
fi

COPIED_FONTS=$(ls -1 "$APP/Contents/Resources/"*.ttf 2>/dev/null | wc -l | tr -d ' ')
echo "Fonts verified: $COPIED_FONTS .ttf in Contents/Resources, all $DECLARED_COUNT declared faces present"
# SwiftPM resource bundle goes in Resources/ so Bundle.module (which searches
# Bundle.main.resourceURL) resolves inside the wrapped app and never traps.
for b in "$BINDIR"/*.bundle; do [ -e "$b" ] && cp -R "$b" "$APP/Contents/Resources/"; done

# Derive version from SyxxConfig.swift so Info.plist stays in sync with SyxxConfig.version (G-2).
SYXX_VERSION=$(grep 'public static let version' Sources/SyxxKit/SyxxConfig.swift | sed 's/.*"\(.*\)".*/\1/')
SYXX_BUILD=$(git rev-list --count HEAD 2>/dev/null || echo 1)

# --- Generate Info.plist BEFORE code signing (CRITICAL for SMJobBless) ---
# SMJobBless reads SMPrivilegedExecutables from the app's Info.plist and validates
# it against the code signature. If Info.plist is written AFTER signing, the plist
# is "not bound" to the signature, SMJobBless cannot trust its contents, and
# installation fails with CFErrorDomainLaunchd error 8.
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Syxx</string>
  <key>CFBundleDisplayName</key><string>Syxx</string>
  <key>CFBundleIdentifier</key><string>com.milkandgin.syxx</string>
  <key>CFBundleExecutable</key><string>Syxx</string>
  <key>CFBundleIconFile</key><string>Syxx</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${SYXX_VERSION}</string>
  <key>CFBundleVersion</key><string>${SYXX_BUILD}</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>ATSApplicationFontsPath</key>
  <string>.</string>
  <key>SMPrivilegedExecutables</key>
  <dict>
    <key>com.milkandgin.deepsyxx</key>
    <string>identifier "com.milkandgin.deepsyxx" and anchor apple generic and certificate leaf[subject.OU] = "994XAF8MYH"</string>
  </dict>
</dict>
</plist>
PLIST

# --- Code signing (required for SMJobBless) ---
# Use Developer ID if available, otherwise fall back to Apple Development
SIGNING_IDENTITY="${CODESIGN_IDENTITY:-}"
if [ -z "$SIGNING_IDENTITY" ]; then
  # Try Developer ID first (distribution), then Apple Development (dev builds)
  if security find-identity -v -p codesigning | grep -q "Developer ID Application"; then
    SIGNING_IDENTITY="Developer ID Application"
  elif security find-identity -v -p codesigning | grep -q "Apple Development"; then
    SIGNING_IDENTITY="Apple Development"
  else
    echo "WARNING: No signing identity found. SMJobBless will not work without a Developer ID or Apple Development certificate."
    echo "Create one at https://developer.apple.com/account/resources/certificates/list"
    SIGNING_IDENTITY="-" # ad-hoc fallback (won't work for SMJobBless)
  fi
fi

# Sign the DeepSyxx helper with the correct identifier, embedded Info.plist, and entitlements
codesign --force --sign "$SIGNING_IDENTITY" \
  --identifier "com.milkandgin.deepsyxx" \
  --options runtime \
  --entitlements Sources/DeepSyxx/DeepSyxx.entitlements \
  "$APP/Contents/Library/LaunchServices/com.milkandgin.deepsyxx"

# Sign the syxx CLI (bundled in Resources)
codesign --force --sign "$SIGNING_IDENTITY" \
  --identifier "com.milkandgin.syxx.cli" \
  --options runtime \
  "$APP/Contents/Resources/syxx"

# Sign the main app with the correct identifier.
# The Info.plist (with SMPrivilegedExecutables) is already on disk, so it will be
# included in the sealed resources and bound to the code signature.
codesign --force --sign "$SIGNING_IDENTITY" \
  --identifier "com.milkandgin.syxx" \
  --options runtime \
  --entitlements Sources/SyxxApp/Syxx.entitlements \
  "$APP"

echo "Code signed with: $SIGNING_IDENTITY"

# Verify Info.plist is now bound to the signature
BINDSTATUS=$(codesign -d --verbose=2 "$APP" 2>&1 | grep "Info.plist" | head -1)
echo "Info.plist signature status: $BINDSTATUS"
if echo "$BINDSTATUS" | grep -q "not bound"; then
  echo "ERROR: Info.plist is still not bound to the code signature!"
  echo "SMJobBless will fail. Check that Info.plist is generated before signing."
  exit 1
fi

touch "$APP"   # nudge Finder/LaunchServices to pick up the new icon
echo "built $APP  (open with: open $APP)"

# D085: DMG creation removed from here. scripts/build_dmg.sh is the single packager —
# it produces a canonical dist/Syxx-<version>-<build>.dmg with the /Applications symlink
# and a SHA-256 checksum. Running both produced two non-identical DMGs per build.
echo "To create a distributable DMG: bash scripts/build_dmg.sh $CONFIG"

#!/bin/sh
# Syxx Dimensions typography asset + source guard (Phase A manifest, Phase 0 parity).
# Happy path: all 24 bundled faces + 8 OFL texts present on all three platforms, byte-identical
# to the macOS gold standard, and semantic role declarations matching the owner-approved map:
#   body/bodyLight = Cormorant Garamond 300/400 (Jost RETIRED from body)
#   navLabel = Spectral Light 300 · uiLabel/eyebrow = Jost Medium
#   bodyAlternate/deck = Cormorant SC · emphasis = EB Garamond Italic
#   technical = JetBrains Mono · Marcellus SC bundled, deliberately role-less
# Failure cases: missing face/license, byte drift between platforms, nested font folders on
# Windows/Linux (unreachable at runtime), retired Archivo/Space* primary, Jost back in a body
# role, Cinzel-as-body, or system-ui/ui-monospace as primary Syxx face.

set -eu

repo_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
failed=0

required_font_files='
Cinzel-Regular.ttf
Cinzel-SemiBold.ttf
Cinzel-Bold.ttf
Jost-Light.ttf
Jost-Regular.ttf
Jost-Medium.ttf
CormorantGaramond-Light.ttf
CormorantGaramond-Regular.ttf
CormorantGaramond-Medium.ttf
CormorantGaramond-Bold.ttf
CormorantSC-Regular.ttf
CormorantSC-SemiBold.ttf
CormorantSC-Bold.ttf
EBGaramond-Italic.ttf
EBGaramond-Regular.ttf
JetBrainsMono-Regular.ttf
JetBrainsMono-Medium.ttf
JetBrainsMono-SemiBold.ttf
Spectral-Light.ttf
Spectral-Regular.ttf
Spectral-Medium.ttf
Spectral-SemiBold.ttf
Spectral-Italic.ttf
MarcellusSC-Regular.ttf
'

required_license_files='
OFL-Cinzel.txt
OFL-Jost.txt
OFL-Cormorant.txt
OFL-CormorantGaramond.txt
OFL-EBGaramond.txt
OFL-JetBrainsMono.txt
OFL-Spectral.txt
OFL-MarcellusSC.txt
'

assert_file() {
    if [ ! -f "$1" ]; then
        echo "FAIL missing required typography asset: $1" >&2
        failed=1
        return 1
    fi
}

assert_contains() {
    if ! grep -Fq "$2" "$1"; then
        echo "FAIL missing typography declaration '$2' in $1" >&2
        failed=1
        return 1
    fi
}

assert_not_contains() {
    if grep -Eq "$2" "$1"; then
        echo "FAIL retired/forbidden typography declaration '$2' remains in $1" >&2
        failed=1
        return 1
    fi
}

# ── Byte-identical faces across platforms (macOS is the gold standard) ─────
for face in $required_font_files; do
    mac="$repo_root/macOS/Sources/SyxxApp/Resources/$face"
    win="$repo_root/Windows/dotNET/SyxxUI/Assets/Fonts/$face"
    lin="$repo_root/Linux/Ubuntu/SyxxUI.Avalonia/Assets/Fonts/$face"
    assert_file "$mac" || true
    assert_file "$win" || true
    assert_file "$lin" || true
    if [ -f "$mac" ] && [ -f "$win" ] && [ -f "$lin" ]; then
        h_mac=$(shasum -a 256 "$mac" | awk '{print $1}')
        h_win=$(shasum -a 256 "$win" | awk '{print $1}')
        h_lin=$(shasum -a 256 "$lin" | awk '{print $1}')
        if [ "$h_mac" != "$h_win" ] || [ "$h_mac" != "$h_lin" ]; then
            echo "FAIL $face hash divergence mac=$h_mac win=$h_win lin=$h_lin" >&2
            failed=1
        fi
    fi
done

for asset_root in \
    "$repo_root/macOS/Sources/SyxxApp/Resources" \
    "$repo_root/Windows/dotNET/SyxxUI/Assets/Fonts" \
    "$repo_root/Linux/Ubuntu/SyxxUI.Avalonia/Assets/Fonts"
do
    for filename in $required_font_files; do
        assert_file "$asset_root/$filename" || true
    done
    for filename in $required_license_files; do
        assert_file "$asset_root/$filename" || true
    done
    # Failure case: Archivo still present as an active root asset.
    for retired in Archivo-Regular.ttf Archivo-Medium.ttf Archivo-SemiBold.ttf Archivo-Bold.ttf \
                   SpaceGrotesk-Regular.otf SpaceGrotesk-Variable.ttf SpaceMono-Regular.ttf; do
        if [ -f "$asset_root/$retired" ]; then
            echo "FAIL retired face still in active font root: $asset_root/$retired" >&2
            failed=1
        fi
    done
done

# ── Flat-layout invariant on the .NET clients (the non-recursive-lookup regression) ──
# A face inside a family subfolder is unreachable at runtime: the avares/ms-appx URIs asserted
# below address the flat root, and nothing recurses into subfolders. Raw Google Fonts staging
# extracts must never survive inside the asset roots. (macOS nested leftovers are a separate,
# owner-visible cleanup note; the Swift root-placement invariant already gates what macOS
# actually resolves.)
for asset_root in \
    "$repo_root/Windows/dotNET/SyxxUI/Assets/Fonts" \
    "$repo_root/Linux/Ubuntu/SyxxUI.Avalonia/Assets/Fonts"
do
    if [ -d "$asset_root" ]; then
        nested=$(find "$asset_root" -mindepth 2 -name '*.ttf' 2>/dev/null)
        if [ -n "$nested" ]; then
            echo "FAIL nested .ttf files are unreachable at runtime:" >&2
            echo "$nested" >&2
            failed=1
        fi
    fi
done

swift_tokens="$repo_root/macOS/Sources/SyxxApp/Views/Components/IoDesignSystem.swift"
swift_registration="$repo_root/macOS/Sources/SyxxApp/SyxxApp.swift"
windows_tokens="$repo_root/Windows/dotNET/SyxxUI/Styles/IoDesignSystem.xaml"
linux_tokens="$repo_root/Linux/Ubuntu/SyxxUI.Avalonia/App.axaml"
swift_package="$repo_root/macOS/Package.swift"
windows_project="$repo_root/Windows/dotNET/SyxxUI/SyxxUI.csproj"
linux_project="$repo_root/Linux/Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj"

# Semantic role API must exist on macOS.
for declaration in \
    'enum SyxxTypeRole' \
    'enum SyxxBodyWeight' \
    'case bodyLight' \
    'case bodyAlternate' \
    'Jost-Light' \
    'Jost-Regular' \
    'Jost-Medium' \
    'Cinzel-SemiBold' \
    'CormorantGaramond-Regular' \
    'CormorantSC-SemiBold' \
    'EBGaramond-Italic' \
    'JetBrainsMono-Regular' \
    'Spectral-Light' \
    'MarcellusSC-Regular' \
    'case navLabel' \
    'cinzelMinimumSize' \
    'uiLabelPostScriptName'
do
    assert_contains "$swift_tokens" "$declaration" || true
done

assert_contains "$swift_registration" 'SyxxTypography.registerBundledFonts' || true
# Phase 0 structure: the registration LOOP (font.resourceName) lives in the design system;
# SyxxApp.swift calls the fail-loud entrypoint. (Pre-Phase-0 the loop lived in SyxxApp.swift
# with its CFError discarded — the stale assertion encoding that layout was itself a miss.)
assert_contains "$swift_registration" 'registerTypographyOrComplainLoudly' || true
assert_contains "$swift_tokens" 'font.resourceName' || true

# Retired primaries must not appear as production faces in the design system.
assert_not_contains "$swift_tokens" 'Archivo-Regular|Archivo-Medium|Archivo-SemiBold|Archivo-Bold' || true
assert_not_contains "$swift_tokens" 'SpaceGrotesk|SpaceMono|"system-ui"|ui-monospace' || true
assert_not_contains "$swift_registration" 'Archivo-|SpaceGrotesk|SpaceMono' || true

# Body weight restriction: no Jost-SemiBold/Bold as ordinary UI tokens in the design system.
if grep -E 'uiLabelPostScriptName.*"Jost-(SemiBold|Bold|Black)"' "$swift_tokens" >/dev/null 2>&1; then
    echo "FAIL ordinary UI resolves to Jost 600+" >&2
    failed=1
fi

# Source guard: production SyxxApp Swift must not introduce Archivo/Space* as custom faces.
if grep -R --include='*.swift' -nE '\.custom\("(Archivo|SpaceGrotesk|SpaceMono)' \
    "$repo_root/macOS/Sources/SyxxApp" >/dev/null 2>&1; then
    echo "FAIL production SyxxApp still references Archivo/Space* via .custom(" >&2
    grep -R --include='*.swift' -nE '\.custom\("(Archivo|SpaceGrotesk|SpaceMono)' \
        "$repo_root/macOS/Sources/SyxxApp" >&2 || true
    failed=1
fi

# Windows semantic keys (owner-approved map: body = Cormorant Garamond, navLabel = Spectral Light)
for declaration in \
    'IoTypeDisplay' \
    'IoTypeBodyLight' \
    'IoTypeBody' \
    'IoTypeBodyAlternate' \
    'IoTypeDeck' \
    'IoTypeEmphasis' \
    'IoTypeNavLabel' \
    'IoTypeUiLabel' \
    'IoTypeEyebrow' \
    'IoTypeTechnical' \
    'Assets/Fonts/CormorantGaramond-Regular.ttf#Cormorant Garamond' \
    'Assets/Fonts/CormorantGaramond-Light.ttf#Cormorant Garamond Light' \
    'Assets/Fonts/Spectral-Light.ttf#Spectral Light' \
    'Assets/Fonts/Jost-Medium.ttf#Jost Medium' \
    'Assets/Fonts/Cinzel-SemiBold.ttf#Cinzel' \
    'Assets/Fonts/CormorantSC-SemiBold.ttf#Cormorant SC' \
    'Assets/Fonts/EBGaramond-Italic.ttf#EB Garamond' \
    'Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono'
do
    assert_contains "$windows_tokens" "$declaration" || true
done
# Body roles must never regress to Jost (owner decision: Jost retired from body).
if grep -E 'x:Key="IoTypeBody(Light)?">[^<]*Jost' "$windows_tokens" >/dev/null 2>&1; then
    echo "FAIL Windows body role resolves to Jost (owner retired Jost from body)" >&2
    failed=1
fi
assert_not_contains "$windows_tokens" 'Archivo|Space Grotesk|Space Mono|Cascadia Mono' || true

# Linux semantic keys (owner-approved map: body = Cormorant Garamond, navLabel = Spectral Light)
for declaration in \
    'SyxTypeDisplay' \
    'SyxTypeBodyLight' \
    'SyxTypeBody' \
    'SyxTypeBodyAlternate' \
    'SyxTypeDeck' \
    'SyxTypeEmphasis' \
    'SyxTypeNavLabel' \
    'SyxTypeUiLabel' \
    'SyxTypeEyebrow' \
    'SyxTypeTechnical' \
    'avares://Syxx/Assets/Fonts/CormorantGaramond-Regular.ttf#Cormorant Garamond' \
    'avares://Syxx/Assets/Fonts/CormorantGaramond-Light.ttf#Cormorant Garamond Light' \
    'avares://Syxx/Assets/Fonts/Spectral-Light.ttf#Spectral Light' \
    'avares://Syxx/Assets/Fonts/Jost-Medium.ttf#Jost Medium' \
    'avares://Syxx/Assets/Fonts/Cinzel-SemiBold.ttf#Cinzel' \
    'avares://Syxx/Assets/Fonts/CormorantSC-SemiBold.ttf#Cormorant SC' \
    'avares://Syxx/Assets/Fonts/EBGaramond-Italic.ttf#EB Garamond' \
    'avares://Syxx/Assets/Fonts/JetBrainsMono-Regular.ttf#JetBrains Mono'
do
    assert_contains "$linux_tokens" "$declaration" || true
done
# Body roles must never regress to Jost (owner decision: Jost retired from body).
if grep -E 'x:Key="SyxTypeBody(Light)?">[^<]*Jost' "$linux_tokens" >/dev/null 2>&1; then
    echo "FAIL Linux body role resolves to Jost (owner retired Jost from body)" >&2
    failed=1
fi
assert_not_contains "$linux_tokens" 'Archivo|Space Grotesk|Space Mono|Ubuntu Mono' || true

# Linux AcceptanceAlert must not hardcode Archivo
if grep -n 'Archivo' "$repo_root/Linux/Ubuntu/SyxxUI.Avalonia/Views/AcceptanceAlertWindow.cs" >/dev/null 2>&1; then
    echo "FAIL AcceptanceAlertWindow still references Archivo" >&2
    failed=1
fi

assert_contains "$swift_package" 'resources: [.copy("Resources")]' || true
assert_contains "$windows_project" '<Content Include="Assets\**"' || true
assert_contains "$linux_project" '<AvaloniaResource Include="Assets\**"' || true

if [ "$failed" -ne 0 ]; then
    echo "Typography asset/source guard FAILED." >&2
    exit 1
fi

echo "Typography assets and semantic declarations match the Syxx Dimensions type system (24 faces, 8 OFL, byte-identical across macOS/Windows/Linux)."

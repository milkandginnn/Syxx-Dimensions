import SwiftUI
import AppKit
import CoreText
import OSLog
import SyxxKit

// MARK: - Warden Color Palette (dark fantasy "security console")
//
// Tokens lifted from the canonical artboard (`Syxx Artboard.dc.html` +
// `WardenSidebar.dc.html`) so a UI coder can reproduce it 1:1.
// Each token resolves per the current Aqua/Dark Aqua appearance.

private extension NSColor {
    convenience init(ioRGB rgb: UInt) {
        self.init(srgbRed: CGFloat((rgb >> 16) & 0xFF) / 255.0,
                  green: CGFloat((rgb >> 8) & 0xFF) / 255.0,
                  blue: CGFloat(rgb & 0xFF) / 255.0,
                  alpha: 1)
    }
}

/// A color that resolves to `light` in Aqua and `dark` in Dark Aqua.
func ioDynamic(light: UInt, dark: UInt) -> Color {
    Color(nsColor: NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
            ? NSColor(ioRGB: dark) : NSColor(ioRGB: light)
    })
}

extension Color {
    // ── Brand (violet) ──────────────────────────────────────────────
    static let ioBlue        = ioDynamic(light: 0x6a3de8, dark: 0x7c5cff)   // Violet primary
    /// Prophet highlight (owner directive 2026-08-02): rgba(0, 180, 255, X) = #00b4ff.
    /// Applied to the AGREE consensus container chrome + conclusion lines and the DNSSEC
    /// "rightly set" container. NOT applied to disagree / inconclusive / insufficient.
    static let ioProphetBlue = ioDynamic(light: 0x00b4ff, dark: 0x00b4ff)   // Prophet highlight
    /// Mirror highlight (owner directive 2026-08-02): "Make the highlight color yellow.
    /// Apply to the domain picker container." Applied to the Mirror domain picker container
    /// chrome (border + tint) and its Marcellus SC card title. Deliberately distinct from
    /// `ioAccentStart` (#d9a441 gold) and `ioWarningAmber` (#f7931e orange) so the module
    /// colour can never be mistaken for the semantic warning state.
    ///
    /// Light mode resolves darker (#8a6a00) because this token also tints TEXT: pure #ffd400
    /// on the pale canvas (#eee7fb) is ~1.4:1 and unreadable, while #8a6a00 is ~6.4:1. This
    /// follows the palette's own darken-for-light convention (see ioSuccessGreen / ioText).
    static let ioMirrorYellow = ioDynamic(light: 0x8a6a00, dark: 0xffd400)  // Mirror highlight
    static let ioDeepBlue    = ioDynamic(light: 0x5228b0, dark: 0x5b3fd6)   // Violet deep
    static let ioElectric    = ioDynamic(light: 0x8a6ef0, dark: 0xa78bff)   // Bright violet (links)
    static let ioAccentStart = ioDynamic(light: 0xd9a441, dark: 0xd9a441)   // Gold
    static let ioAccentEnd   = ioDynamic(light: 0x5228b0, dark: 0x5b3fd6)   // Violet gradient end
    /// Pale wordmark violet (#c4b3ff), companion to the ioWordmark gradient.
    static let ioVioletPale  = Color(red: 196/255, green: 179/255, blue: 255/255)

    // ── Semantic / severity ──────────────────────────────────────────
    static let ioCriticalRed  = ioDynamic(light: 0xc41e3a, dark: 0xff4d6d)  // Red critical
    static let ioWarningAmber = ioDynamic(light: 0xc87a10, dark: 0xf7931e)  // Orange warning
    static let ioSuccessGreen = ioDynamic(light: 0x1e9e8f, dark: 0x2dd4bf)  // Teal healthy
    static let ioHighOrange   = ioDynamic(light: 0xc87a10, dark: 0xf7931e)  // High (alias)
    static let ioInfoPurple   = ioDynamic(light: 0x7c5cff, dark: 0xa78bff)  // Violet info

    // ── Canvas / surfaces ────────────────────────────────────────────
    static let ioBackgroundDark  = ioDynamic(light: 0xe6ddf7, dark: 0x0b0518)  // Canvas
    static let ioSurfaceDark     = ioDynamic(light: 0xeee7fb, dark: 0x170c2e)  // Window base
    static let ioSurfaceElevated = ioDynamic(light: 0xdcd0f5, dark: 0x1d1640)  // Card / sidebar
    static let ioBorderSubtle    = ioDynamic(light: 0xc8b9ee, dark: 0x382c66)  // Hairline border

    // ── Text ─────────────────────────────────────────────────────────
    static let ioText          = ioDynamic(light: 0x2a134d, dark: 0xefeafe)  // Primary headings
    static let ioTextSecondary = ioDynamic(light: 0x6a4fa0, dark: 0xc9c1e6)  // Body copy
    static let ioTextMuted     = ioDynamic(light: 0x9a8fc0, dark: 0x9a8fc0)  // Captions
    static let ioTextFaint     = ioDynamic(light: 0x7d7399, dark: 0x6f6597)  // Timestamps
}

// MARK: - Warden Canvas (background radial gradient)

struct IoCanvasBackground: View {
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        if scheme == .light {
            Color.ioBackgroundDark
        } else {
            ZStack {
                Color(red: 11/255, green: 5/255, blue: 24/255)               // #0b0518
                RadialGradient(
                    colors: [Color(red: 26/255, green: 12/255, blue: 51/255), // #1a0c33
                             Color(red: 11/255, green: 5/255, blue: 24/255).opacity(0)],
                    center: UnitPoint(x: 0.15, y: 0),
                    startRadius: 0,
                    endRadius: 900
                )
            }
        }
    }
}

extension View {
    func ioCanvas() -> some View {
        background(IoCanvasBackground())
    }
}

// E123: IoContentGlow removed — defined but never instantiated anywhere in the app.

// MARK: - Brand Gradients

extension LinearGradient {
    static let ioBrand = LinearGradient(
        colors: [.ioAccentStart, .ioAccentEnd],
        startPoint: .leading, endPoint: .trailing
    )
    /// Teal to violet health meter
    static let ioHealthMeter = LinearGradient(
        colors: [Color(red: 45/255, green: 212/255, blue: 191/255),  // #2dd4bf
                 Color(red: 124/255, green: 92/255, blue: 255/255)], // #7c5cff
        startPoint: .leading, endPoint: .trailing
    )
    /// Primary button gradient
    static let ioPrimaryButton = LinearGradient(
        colors: [Color(red: 124/255, green: 92/255, blue: 255/255),  // #7c5cff
                 Color(red: 91/255, green: 63/255, blue: 214/255)],  // #5b3fd6
        // UnitPoint has no angle-based initializer; these are the computed points for 120°/300°
        // (x = 0.5 + 0.5·cos θ, y = 0.5 + 0.5·sin θ), preserving the intended diagonal gradient.
        startPoint: UnitPoint(x: 0.25, y: 0.933),
        endPoint: UnitPoint(x: 0.75, y: 0.067)
    )
    /// Prophet primary-button gradient. Owner directive 2026-08-02: the Prophet module colour
    /// is rgba(0, 180, 255, X) with a VARIABLE alpha channel, so the fill is one hue (#00b4ff)
    /// ramped in opacity rather than two hues. Same 120°/300° diagonal as `ioPrimaryButton`.
    static let ioProphetButton = LinearGradient(
        colors: [Color.ioProphetBlue.opacity(0.95),
                 Color.ioProphetBlue.opacity(0.62)],
        startPoint: UnitPoint(x: 0.25, y: 0.933),
        endPoint: UnitPoint(x: 0.75, y: 0.067)
    )
    /// Teal button gradient
    static let ioTealButton = LinearGradient(
        colors: [Color(red: 45/255, green: 212/255, blue: 191/255),  // #2dd4bf
                 Color(red: 20/255, green: 168/255, blue: 154/255)], // #14a89a
        // UnitPoint has no angle-based initializer; these are the computed points for 120°/300°
        // (x = 0.5 + 0.5·cos θ, y = 0.5 + 0.5·sin θ), preserving the intended diagonal gradient.
        startPoint: UnitPoint(x: 0.25, y: 0.933),
        endPoint: UnitPoint(x: 0.75, y: 0.067)
    )
    /// Red button gradient
    static let ioRedButton = LinearGradient(
        colors: [Color(red: 255/255, green: 77/255, blue: 109/255),  // #ff4d6d
                 Color(red: 214/255, green: 42/255, blue: 76/255)],  // #d62a4c
        // UnitPoint has no angle-based initializer; these are the computed points for 120°/300°
        // (x = 0.5 + 0.5·cos θ, y = 0.5 + 0.5·sin θ), preserving the intended diagonal gradient.
        startPoint: UnitPoint(x: 0.25, y: 0.933),
        endPoint: UnitPoint(x: 0.75, y: 0.067)
    )
    /// Active nav item gradient
    static let ioActiveNav = LinearGradient(
        colors: [Color(red: 124/255, green: 92/255, blue: 255/255),  // #7c5cff
                 Color(red: 106/255, green: 73/255, blue: 239/255)], // #6a49ef
        startPoint: .leading, endPoint: .trailing
    )
    /// Sidebar fill gradient
    static let ioSidebarFill = LinearGradient(
        colors: [Color(red: 29/255, green: 18/255, blue: 64/255),   // #1d1240
                 Color(red: 22/255, green: 12/255, blue: 48/255)],   // #160c30
        startPoint: .top, endPoint: .bottom
    )
    /// Window fill gradient
    static let ioWindowFill = LinearGradient(
        colors: [Color(red: 23/255, green: 12/255, blue: 46/255),   // #170c2e
                 Color(red: 15/255, green: 7/255, blue: 32/255)],    // #0f0720
        startPoint: .top, endPoint: .bottom
    )
    /// Dimensions wordmark gradient, left to right (#FF2D78 → #FF3DD5).
    static let ioWordmark = LinearGradient(
        colors: [Color(red: 255/255, green: 45/255, blue: 120/255),  // #ff2d78
                 Color(red: 255/255, green: 61/255, blue: 213/255)], // #ff3dd5
        startPoint: .leading, endPoint: .trailing
    )
    /// Pro covenant banner gradient
    static let ioProCovenant = LinearGradient(
        colors: [Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.16),
                 Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.06)],
        // UnitPoint has no angle-based initializer; these are the computed points for 120°/300°
        // (x = 0.5 + 0.5·cos θ, y = 0.5 + 0.5·sin θ), preserving the intended diagonal gradient.
        startPoint: UnitPoint(x: 0.25, y: 0.933),
        endPoint: UnitPoint(x: 0.75, y: 0.067)
    )
}

extension AngularGradient {
    static let ioBrandRing = AngularGradient(
        colors: [.ioAccentStart, .ioAccentEnd, .ioAccentStart],
        center: .center
    )
}

// MARK: - Warden Typography (Syxx Dimensions type system — Phase A/F1)
//
// Semantic roles (authoritative specimen: Syxx Type System.html):
//   display / sectionHeading → Cinzel (caps, never body, never below 18pt)
//   bodyLight → Cormorant Garamond 300 · body → Cormorant Garamond 400
//   navLabel → Spectral 300 (sidebar / navigation chrome)
//   bodyAlternate → Cormorant SC 400 · deck → Cormorant SC 600
//   emphasis → EB Garamond Italic (~host + 2pt)
//   uiLabel / eyebrow → Jost 500 (no ordinary UI at 600+)
//   technical → JetBrains Mono
//
// Phase A retarget: body/bodyLight moved off Jost onto Cormorant Garamond. Jost is retained
// for uiLabel/eyebrow and for the Prophet result surfaces that call it directly, so nothing
// that reads as UI chrome changed face.
//
// Fonts must be registered in SyxxAppDelegate at launch. PostScript names:
//   Cinzel-Regular, Cinzel-SemiBold, Cinzel-Bold
//   Jost-Light, Jost-Regular, Jost-Medium
//   CormorantGaramond-Light, -Regular, -Medium, -Bold
//   CormorantSC-Regular, CormorantSC-SemiBold, CormorantSC-Bold
//   Spectral-Light, -Regular, -Medium, -SemiBold, -Italic
//   EBGaramond-Italic
//   JetBrainsMono-Regular, JetBrainsMono-Medium, JetBrainsMono-SemiBold
//   MarcellusSC-Regular (bundled, not yet wired to a role)

struct SyxxBundledFont: Equatable {
    let resourceName: String
    let postScriptName: String
}

/// Restricted body weights — only 300 (light) and 400 (regular) may be used for body copy.
enum SyxxBodyWeight: Equatable, CaseIterable {
    case light    // Cormorant Garamond 300
    case regular  // Cormorant Garamond 400

    var postScriptName: String {
        switch self {
        case .light: return "CormorantGaramond-Light"
        case .regular: return "CormorantGaramond-Regular"
        }
    }

    var numericWeight: Int {
        switch self {
        case .light: return 300
        case .regular: return 400
        }
    }
}

/// Canonical semantic type roles for every Syxx surface.
enum SyxxTypeRole: String, Equatable, CaseIterable {
    case display
    case sectionHeading
    case bodyLight
    case body
    case bodyAlternate
    case deck
    case emphasis
    /// Sidebar / navigation chrome. Spectral Light 300 — a text face at nav sizes, which
    /// reads as considered rather than as a control label.
    case navLabel
    /// Super captions and card titles (owner directive 2026-08-02): Marcellus SC Regular at
    /// body+2pt. This is Marcellus SC's first wired role; it was bundled role-less in Phase A.
    case cardTitle
    case uiLabel
    case eyebrow
    case technical
}

enum SyxxTypography {
    /// Cinzel must never render below this point size.
    static let cinzelMinimumSize: CGFloat = 18

    /// Product-required static faces (flat under Resources/).
    static let bundledFonts: [SyxxBundledFont] = [
        .init(resourceName: "Cinzel-Regular", postScriptName: "Cinzel-Regular"),
        .init(resourceName: "Cinzel-SemiBold", postScriptName: "Cinzel-SemiBold"),
        .init(resourceName: "Cinzel-Bold", postScriptName: "Cinzel-Bold"),
        .init(resourceName: "Jost-Light", postScriptName: "Jost-Light"),
        .init(resourceName: "Jost-Regular", postScriptName: "Jost-Regular"),
        .init(resourceName: "Jost-Medium", postScriptName: "Jost-Medium"),
        .init(resourceName: "CormorantSC-Regular", postScriptName: "CormorantSC-Regular"),
        .init(resourceName: "CormorantSC-SemiBold", postScriptName: "CormorantSC-SemiBold"),
        .init(resourceName: "CormorantSC-Bold", postScriptName: "CormorantSC-Bold"),
        .init(resourceName: "EBGaramond-Italic", postScriptName: "EBGaramond-Italic"),
        .init(resourceName: "JetBrainsMono-Regular", postScriptName: "JetBrainsMono-Regular"),
        .init(resourceName: "JetBrainsMono-Medium", postScriptName: "JetBrainsMono-Medium"),
        .init(resourceName: "JetBrainsMono-SemiBold", postScriptName: "JetBrainsMono-SemiBold"),
        // Dimensions Phase A (F0). Spectral splits its family per weight, so the PostScript
        // names are Spectral-Light / -Medium / -SemiBold rather than a single "Spectral"
        // family with weight variations. Verified with:
        //   fc-scan --format "%{postscriptname}\n" Spectral-*.ttf
        .init(resourceName: "Spectral-Light", postScriptName: "Spectral-Light"),
        .init(resourceName: "Spectral-Regular", postScriptName: "Spectral-Regular"),
        .init(resourceName: "Spectral-Medium", postScriptName: "Spectral-Medium"),
        .init(resourceName: "Spectral-SemiBold", postScriptName: "Spectral-SemiBold"),
        .init(resourceName: "Spectral-Italic", postScriptName: "Spectral-Italic"),
        // Cormorant Garamond — the body face as of Phase A. Distinct from Cormorant SC
        // (small caps) above; both ship because they serve different roles.
        .init(resourceName: "CormorantGaramond-Light", postScriptName: "CormorantGaramond-Light"),
        .init(resourceName: "CormorantGaramond-Regular", postScriptName: "CormorantGaramond-Regular"),
        .init(resourceName: "CormorantGaramond-Medium", postScriptName: "CormorantGaramond-Medium"),
        .init(resourceName: "CormorantGaramond-Bold", postScriptName: "CormorantGaramond-Bold"),
        // Marcellus SC — bundled and registered, but NOT yet wired to a role. See the
        // `body` case in postScriptName(for:) for why.
        .init(resourceName: "MarcellusSC-Regular", postScriptName: "MarcellusSC-Regular")
    ]

    /// License texts that must ship beside the faces.
    static let bundledLicenseFiles: [String] = [
        "OFL-Cinzel.txt",
        "OFL-Jost.txt",
        "OFL-Cormorant.txt",
        // Byte-identical to OFL-Cormorant.txt: Cormorant SC and Cormorant Garamond are the
        // same upstream project (github.com/CatharsisFonts/Cormorant). Shipped under both
        // names so a licence audit finds each family covered by name.
        "OFL-CormorantGaramond.txt",
        "OFL-EBGaramond.txt",
        "OFL-JetBrainsMono.txt",
        "OFL-Spectral.txt",
        "OFL-MarcellusSC.txt"
    ]

    static func cinzelPostScriptName(for weight: Font.Weight) -> String {
        if weight == .bold || weight == .heavy || weight == .black {
            return "Cinzel-Bold"
        }
        if weight == .semibold || weight == .medium {
            return "Cinzel-SemiBold"
        }
        return "Cinzel-Regular"
    }

    /// Ordinary UI never resolves above Jost Medium (500).
    static let uiLabelPostScriptName = "Jost-Medium"

    /// Navigation chrome (sidebar items, rail labels). Spectral Light 300.
    static let navLabelPostScriptName = "Spectral-Light"

    /// Numeric weight `navLabel` represents, for any surface that needs to state it.
    static let navLabelNumericWeight = 300

    /// Cormorant Garamond runs a noticeably smaller x-height than Jost at the same point
    /// size. Every body size that was tuned against Jost is scaled by this factor so the
    /// Phase A face change does not silently shrink existing copy.
    static let cormorantOpticalScale: CGFloat = 1.107

    /// Converts a point size tuned for Jost body copy into its Cormorant Garamond equivalent,
    /// snapped to the nearest half point. A literal 12pt caption in Cormorant reads about a
    /// point smaller than the same caption did in Jost; this keeps the optical size.
    static func bodySize(matchingJost size: CGFloat) -> CGFloat {
        (size * cormorantOpticalScale * 2).rounded() / 2
    }

    /// Default body point size. Owner directive 2026-08-02: body text was too small, +2pt,
    /// so the Jost-era 14pt becomes 16pt before optical matching (15.5pt → 17.5pt resolved).
    static let bodyDefaultSize: CGFloat = bodySize(matchingJost: 16)

    static func jetBrainsMonoPostScriptName(for weight: Font.Weight) -> String {
        if weight == .bold || weight == .semibold || weight == .heavy || weight == .black {
            return "JetBrainsMono-SemiBold"
        }
        if weight == .medium {
            return "JetBrainsMono-Medium"
        }
        return "JetBrainsMono-Regular"
    }

    /// Resolve the PostScript face for a semantic role (tests assert these mappings).
    static func postScriptName(for role: SyxxTypeRole) -> String {
        switch role {
        case .display: return "Cinzel-Bold"
        case .sectionHeading: return "Cinzel-SemiBold"
        case .bodyLight: return SyxxBodyWeight.light.postScriptName
        // Body is Cormorant Garamond Regular — the owner's chosen body face for Syxx
        // Dimensions (Jost was retired from body on owner review; Cormorant SC is reserved
        // for card labels, i.e. bodyAlternate/deck). Marcellus SC was considered for body
        // and rejected: it is an all-small-caps titling face with no lowercase, so pointing
        // running copy at it would set every paragraph in caps. MarcellusSC-Regular stays
        // bundled and registered but is deliberately not wired to any role.
        case .body: return SyxxBodyWeight.regular.postScriptName
        case .bodyAlternate: return "CormorantSC-Regular"
        case .deck: return "CormorantSC-SemiBold"
        case .emphasis: return "EBGaramond-Italic"
        case .navLabel: return navLabelPostScriptName
        case .cardTitle: return "MarcellusSC-Regular"
        case .uiLabel, .eyebrow: return uiLabelPostScriptName
        case .technical: return "JetBrainsMono-Regular"
        }
    }

    /// Default point size for each role (specimen reference).
    static func defaultSize(for role: SyxxTypeRole) -> CGFloat {
        switch role {
        case .display: return 28
        case .sectionHeading: return 22
        case .bodyLight, .body: return bodyDefaultSize
        case .bodyAlternate: return 14
        case .deck: return 18
        case .emphasis: return 16
        case .navLabel: return 15
        // Super captions / card titles: body + 2pt (owner directive 2026-08-02).
        case .cardTitle: return bodyDefaultSize + 2
        case .uiLabel: return 13
        case .eyebrow: return 10
        case .technical: return 12
        }
    }

    /// Clamp Cinzel sizes to the specimen minimum.
    static func clampedDisplaySize(_ size: CGFloat) -> CGFloat {
        max(size, cinzelMinimumSize)
    }

    /// Emphasis is approximately 2 points larger than the host run.
    static func emphasisSize(hostSize: CGFloat) -> CGFloat {
        hostSize + 2
    }

    /// Eyebrow tracking in ems (specimen: 0.26em at 10pt / weight 500 / uppercase).
    static let eyebrowTrackingEm: CGFloat = 0.26

    static func bundledResourceURL(named resourceName: String) -> URL? {
        if let url = Bundle.module.url(forResource: resourceName, withExtension: "ttf") {
            return url
        }
        // License texts and nested copies under family folders.
        if let url = Bundle.module.url(forResource: resourceName, withExtension: nil) {
            return url
        }
        return Bundle.module.url(forResource: resourceName, withExtension: "txt")
    }

    static func bundledLicenseURL(named fileName: String) -> URL? {
        let stem = (fileName as NSString).deletingPathExtension
        let ext = (fileName as NSString).pathExtension
        return Bundle.module.url(forResource: stem, withExtension: ext.isEmpty ? nil : ext)
    }

    // MARK: - Font registration (Phase 0 — fail loud, never fall back silently)

    /// Why a declared face did not end up usable.
    enum FontRegistrationFailureReason: Equatable {
        /// `Bundle.module` could not locate the `.ttf`. This is the failure mode that
        /// shipped silently after the flat faces were moved into family subfolders:
        /// `url(forResource:withExtension:)` does NOT recurse.
        case resourceMissing
        /// `CTFontManagerRegisterFontsForURL` reported an error AND the face is still
        /// unresolvable. (An "already registered" error with a resolvable face is not a failure.)
        case registrationFailed(code: Int, message: String)
        /// The file registered without error, but the declared PostScript name still does
        /// not resolve — i.e. the manifest name disagrees with the font's own name table.
        case postScriptNameUnresolved
    }

    struct FontRegistrationFailure: Equatable {
        let resourceName: String
        let postScriptName: String
        let reason: FontRegistrationFailureReason

        var diagnostic: String {
            switch reason {
            case .resourceMissing:
                return "\(resourceName).ttf — not found in Bundle.module "
                     + "(url(forResource:withExtension:) is non-recursive; the face must sit at "
                     + "the Resources root, not inside a family subfolder)"
            case let .registrationFailed(code, message):
                return "\(resourceName).ttf — CTFontManagerRegisterFontsForURL failed "
                     + "(code \(code): \(message)); '\(postScriptName)' does not resolve"
            case .postScriptNameUnresolved:
                return "\(resourceName).ttf — registered, but PostScript name "
                     + "'\(postScriptName)' does not resolve (manifest disagrees with name table)"
            }
        }
    }

    /// Tri-state, never a Bool. A partial registration is neither success nor failure: some
    /// text renders in the design face and some silently falls back to the system font, which
    /// is precisely the ambiguous state a boolean would erase.
    enum FontRegistrationOutcome: Equatable {
        case allRegistered
        case partiallyRegistered
        case noneRegistered
    }

    struct FontRegistrationReport: Equatable {
        /// PostScript names proven resolvable by `NSFont(name:size:)` after registration.
        let registered: [String]
        let failures: [FontRegistrationFailure]

        var outcome: FontRegistrationOutcome {
            if failures.isEmpty && !registered.isEmpty { return .allRegistered }
            if registered.isEmpty { return .noneRegistered }
            return .partiallyRegistered
        }
    }

    /// Registers every face in `bundledFonts` and **verifies the outcome by resolution**,
    /// not by the return value of the CoreText call.
    ///
    /// Verifying by `NSFont(name:size:)` is deliberate: `CTFontManagerRegisterFontsForURL`
    /// returns `false` with `kCTFontManagerErrorAlreadyRegistered` on a second call in the
    /// same process (which happens in tests and on re-entry), so its return value alone
    /// answers neither "is this face usable?" nor "did typography survive?".
    @discardableResult
    static func registerBundledFonts() -> FontRegistrationReport {
        register(bundledFonts)
    }

    /// Manifest-injectable core of `registerBundledFonts()`. Tests drive this with a
    /// deliberately-broken manifest to prove the detector can still detect — a registration
    /// check that cannot go red is worth nothing.
    static func register(_ fonts: [SyxxBundledFont]) -> FontRegistrationReport {
        var registered: [String] = []
        var failures: [FontRegistrationFailure] = []

        for font in fonts {
            guard let url = bundledResourceURL(named: font.resourceName) else {
                failures.append(.init(resourceName: font.resourceName,
                                      postScriptName: font.postScriptName,
                                      reason: .resourceMissing))
                continue
            }

            var cfError: Unmanaged<CFError>?
            let didRegister = CTFontManagerRegisterFontsForURL(url as CFURL, .process, &cfError)
            let error = cfError?.takeRetainedValue()

            // Resolution is the only honest proof the face is usable.
            if NSFont(name: font.postScriptName, size: 12) != nil {
                registered.append(font.postScriptName)
                continue
            }

            // "Already registered" means the FILE is in place; it says nothing about the name.
            // If the face still will not resolve after that, the manifest's PostScript name is
            // the fault, not CoreText — reporting code 105 here would blame the wrong layer and
            // read like a benign duplicate while the face is in fact unusable.
            let alreadyRegistered = error.map {
                CFErrorGetCode($0) == CTFontManagerError.alreadyRegistered.rawValue
            } ?? false

            if !didRegister, let error, !alreadyRegistered {
                failures.append(.init(resourceName: font.resourceName,
                                      postScriptName: font.postScriptName,
                                      reason: .registrationFailed(
                                          code: CFErrorGetCode(error),
                                          message: CFErrorCopyDescription(error) as String? ?? "unknown")))
            } else {
                failures.append(.init(resourceName: font.resourceName,
                                      postScriptName: font.postScriptName,
                                      reason: .postScriptNameUnresolved))
            }
        }

        return FontRegistrationReport(registered: registered, failures: failures)
    }
}

extension Font {
    // ── Semantic role API (Phase 3.7) ──

    static func ioRole(_ role: SyxxTypeRole, size: CGFloat? = nil) -> Font {
        let resolved = size ?? SyxxTypography.defaultSize(for: role)
        switch role {
        case .display:
            return .custom(SyxxTypography.postScriptName(for: .display),
                           size: SyxxTypography.clampedDisplaySize(resolved))
        case .sectionHeading:
            return .custom(SyxxTypography.postScriptName(for: .sectionHeading),
                           size: SyxxTypography.clampedDisplaySize(resolved))
        case .bodyLight:
            return .custom(SyxxBodyWeight.light.postScriptName, size: resolved)
        case .body:
            return .custom(SyxxBodyWeight.regular.postScriptName, size: resolved)
        case .bodyAlternate:
            return .custom("CormorantSC-Regular", size: resolved)
        case .deck:
            return .custom("CormorantSC-SemiBold", size: max(resolved, 18))
        case .emphasis:
            return .custom("EBGaramond-Italic", size: resolved)
        case .navLabel:
            return .custom(SyxxTypography.navLabelPostScriptName, size: resolved)
        case .cardTitle:
            return .custom("MarcellusSC-Regular", size: resolved)
        case .uiLabel:
            return .custom(SyxxTypography.uiLabelPostScriptName, size: resolved)
        case .eyebrow:
            return .custom(SyxxTypography.uiLabelPostScriptName, size: resolved)
        case .technical:
            return .custom("JetBrainsMono-Regular", size: resolved)
        }
    }

    static func display(_ size: CGFloat = 28) -> Font { .ioRole(.display, size: size) }
    static func sectionHeading(_ size: CGFloat = 22) -> Font { .ioRole(.sectionHeading, size: size) }
    static func bodyLight(_ size: CGFloat = SyxxTypography.bodyDefaultSize) -> Font {
        .ioRole(.bodyLight, size: size)
    }
    static func body(_ size: CGFloat = SyxxTypography.bodyDefaultSize) -> Font {
        .ioRole(.body, size: size)
    }
    static func bodyAlternate(_ size: CGFloat = 14) -> Font { .ioRole(.bodyAlternate, size: size) }
    static func deck(_ size: CGFloat = 18) -> Font { .ioRole(.deck, size: size) }
    static func emphasis(hostSize: CGFloat) -> Font {
        .ioRole(.emphasis, size: SyxxTypography.emphasisSize(hostSize: hostSize))
    }
    /// Sidebar / navigation chrome (Spectral Light 300).
    static func navLabel(_ size: CGFloat = 15) -> Font { .ioRole(.navLabel, size: size) }
    /// Super captions and card titles (Marcellus SC Regular, body+2pt by default).
    static func cardTitle(_ size: CGFloat = SyxxTypography.defaultSize(for: .cardTitle)) -> Font {
        .ioRole(.cardTitle, size: size)
    }
    static func uiLabel(_ size: CGFloat = 13) -> Font { .ioRole(.uiLabel, size: size) }
    static func eyebrow(_ size: CGFloat = 10) -> Font { .ioRole(.eyebrow, size: size) }
    static func technical(_ size: CGFloat = 12) -> Font { .ioRole(.technical, size: size) }

    // ── Compatibility helpers (map onto semantic roles) ──

    static func ioHero() -> Font { .display(34) }
    static func ioTitle1() -> Font { .display(28) }
    static func ioTitle2() -> Font { .sectionHeading(22) }
    static func ioTitle3() -> Font { .sectionHeading(18) }
    /// Was Cinzel 16 (below min). Section headings clamp to 18.
    static func ioTitle4() -> Font { .sectionHeading(18) }
    static func ioTitle5() -> Font { .sectionHeading(20) }
    /// Compact titles below Cinzel minimum use Jost UI labels.
    static func ioTitle6() -> Font { .uiLabel(16) }

    /// EB Garamond Italic emphasis / lore.
    static func ioLore(_ size: CGFloat = 15) -> Font {
        .custom("EBGaramond-Italic", size: size)
    }

    /// Explicit alternate body (Cormorant SC 400) — not a Jost fallback.
    static func ioLoreRegular(_ size: CGFloat = 14) -> Font {
        .bodyAlternate(size)
    }

    /// Deck / lead (Cormorant SC 600). Weight argument retained for call-site compatibility;
    /// ordinary deck is SemiBold. Bold maps to CormorantSC-Bold (700) only when explicitly requested.
    static func ioDeck(_ size: CGFloat = 18, weight: Font.Weight = .semibold) -> Font {
        let clamped = max(size, 18)
        if weight == .bold || weight == .heavy || weight == .black {
            return .custom("CormorantSC-Bold", size: clamped)
        }
        if weight == .regular || weight == .light || weight == .thin || weight == .ultraLight {
            return .custom("CormorantSC-Regular", size: clamped)
        }
        return .custom("CormorantSC-SemiBold", size: clamped)
    }

    /// Body / UI. Body weights are restricted to light (300) and regular (400).
    /// Medium and above resolve to uiLabel (Jost 500) — never 600+.
    /// Body sizes are optically matched to their Jost-era values (see `bodySize(matchingJost:)`).
    /// The uiLabel/eyebrow branches keep their literal sizes — those are still Jost.
    static func ioBody(_ weight: Font.Weight = .regular) -> Font {
        if weight == .light || weight == .thin || weight == .ultraLight {
            return .bodyLight(SyxxTypography.bodySize(matchingJost: 14))
        }
        if weight == .regular {
            return .body(SyxxTypography.bodySize(matchingJost: 14))
        }
        return .uiLabel(14)
    }

    /// Restricted body API — cannot accidentally pass 500–900.
    static func ioBody(_ bodyWeight: SyxxBodyWeight,
                       size: CGFloat = SyxxTypography.bodyDefaultSize) -> Font {
        switch bodyWeight {
        case .light: return .bodyLight(size)
        case .regular: return .body(size)
        }
    }

    static func ioCaption(_ weight: Font.Weight = .regular) -> Font {
        if weight == .light || weight == .thin || weight == .ultraLight {
            return .bodyLight(SyxxTypography.bodySize(matchingJost: 12))
        }
        if weight == .regular {
            return .body(SyxxTypography.bodySize(matchingJost: 12))
        }
        return .uiLabel(12)
    }

    static func ioSmall(_ weight: Font.Weight = .regular) -> Font {
        if weight == .regular || weight == .light || weight == .thin || weight == .ultraLight {
            let size = SyxxTypography.bodySize(matchingJost: 11)
            return weight == .regular ? .body(size) : .bodyLight(size)
        }
        return .uiLabel(11)
    }

    static func ioMicro(_ weight: Font.Weight = .regular) -> Font {
        if weight == .regular {
            return .body(SyxxTypography.bodySize(matchingJost: 10))
        }
        if weight == .light || weight == .thin || weight == .ultraLight {
            return .bodyLight(SyxxTypography.bodySize(matchingJost: 10))
        }
        return .eyebrow(10)
    }

    static func ioMono(_ size: CGFloat) -> Font {
        .custom("JetBrainsMono-Regular", size: size)
    }
    static func ioMonoBold(_ size: CGFloat) -> Font {
        .custom("JetBrainsMono-SemiBold", size: size)
    }
}

extension View {
    func ioTitleHeading() -> some View {
        self.tracking(-0.4)
            .shadow(color: .black.opacity(0.45), radius: 1.5, x: 0, y: 1)
    }
}

// MARK: - Warden Card Modifier

struct IoCard: ViewModifier {
    var padding: CGFloat = 18
    var cornerRadius: CGFloat = 14

    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(Color(white: 1, opacity: 0.03))
            .cornerRadius(cornerRadius)
            .overlay(RoundedRectangle(cornerRadius: cornerRadius).stroke(Color(white: 1, opacity: 0.05).opacity(0.25), lineWidth: 1))
    }
}

extension View {
    func ioCard(padding: CGFloat = 18, cornerRadius: CGFloat = 14) -> some View {
        modifier(IoCard(padding: padding, cornerRadius: cornerRadius))
    }
}

// E123: IoButton / IoButtonStyle / .ioButton() removed — defined but never called.
// All buttons use IoFilledButtonStyle / IoOutlineButtonStyle (separate implementations below).

// MARK: - Io Button Styles (readable, mockup-matched)

struct IoFilledButtonStyle: ButtonStyle {
    var color: Color = .ioBlue
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.ioBody(.semibold))
            .foregroundColor(isEnabled ? .white : .ioTextMuted)
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(isEnabled ? color : Color(white: 1, opacity: 0.03))
            )
            .shadow(color: (isEnabled ? color : .clear).opacity(0.4), radius: 8, y: 4)
            .brightness(configuration.isPressed ? -0.06 : 0)
            .opacity(configuration.isPressed ? 0.9 : 1)
    }
}

struct IoOutlineButtonStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.ioBody(.medium))
            .foregroundColor(isEnabled ? .ioText : .ioTextMuted)
            .padding(.horizontal, 14)
            .padding(.vertical, 7)
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(Color(white: 1, opacity: 0.03))
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(white: 1, opacity: 0.14), lineWidth: 1))
            )
            .brightness(configuration.isPressed ? -0.04 : 0)
            .opacity(configuration.isPressed ? 0.9 : 1)
    }
}

// MARK: - Warden Severity Badge

struct IoSeverityBadge: View {
    let severity: Severity

    var body: some View {
        HStack(spacing: 4) {
            Circle()
                .fill(color)
                .frame(width: 7, height: 7)
            Text(severity.rawValue.uppercased())
                .font(.uiLabel(11))
                .foregroundColor(color)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(color.opacity(0.10))
        .cornerRadius(999)
        .overlay(Capsule().stroke(color.opacity(0.30), lineWidth: 1))
    }

    private var color: Color {
        switch severity {
        case .clean:    return .ioSuccessGreen
        case .info:     return .ioInfoPurple
        case .warning:  return .ioWarningAmber
        case .high:     return .ioHighOrange
        case .critical: return .ioCriticalRed
        case .unknown:  return .ioTextMuted
        }
    }
}

// MARK: - Warden Status Pill

struct StatusPill: View {
    let label: String
    let color: Color
    var glow: Bool = false

    var body: some View {
        HStack(spacing: 6) {
            Circle()
                .fill(color)
                .frame(width: 7, height: 7)
                .shadow(color: glow ? color : .clear, radius: glow ? 6 : 0)
            Text(label)
                .font(.uiLabel(11))
                .foregroundColor(color)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .background(color.opacity(0.10))
        .cornerRadius(999)
        .overlay(Capsule().stroke(color.opacity(0.30), lineWidth: 1))
    }
}

// E123: IoStatCard removed — defined but never instantiated anywhere in the app.

// MARK: - Warden Progress Ring (SVG-equivalent)

struct IoProgressRing: View {
    let progress: Double // 0.0 to 1.0
    var lineWidth: CGFloat = 10
    var size: CGFloat = 70
    // E123: label/sublabel are now required — the previous defaults ("WARDED", "4 of 6")
    // were hardcoded filler that would mislead if a future caller omitted them.
    var label: String
    var sublabel: String

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color(white: 1, opacity: 0.08), lineWidth: lineWidth)

            Circle()
                .trim(from: 0.0, to: progress)
                .stroke(
                    LinearGradient(
                        colors: [Color(red: 45/255, green: 212/255, blue: 191/255),
                                 Color(red: 124/255, green: 92/255, blue: 255/255)],
                        startPoint: .topLeading, endPoint: .bottomTrailing
                    ),
                    style: StrokeStyle(lineWidth: lineWidth, lineCap: .round)
                )
                .rotationEffect(.degrees(-90))
                .shadow(color: .ioBlue.opacity(0.5), radius: 6)
                .animation(.easeInOut(duration: 0.8), value: progress)

            // WS9-FIX: Add gradient overlay to match mockup specification
            Circle()
                .fill(
                    LinearGradient(
                        colors: [Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.15),
                                 Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.08)],
                        startPoint: .topLeading, endPoint: .bottomTrailing
                    )
                )
                .blur(radius: 8)

            VStack(spacing: 0) {
                // Ring center label is below Cinzel's 18pt floor — use Jost UI label.
                Text(label)
                    .font(.uiLabel(max(10, size * 0.14)))
                    .foregroundColor(.ioText)
                    .tracking(1)
                Text(sublabel)
                    .font(.ioMono(size * 0.12))
                    .foregroundColor(.ioTextMuted)
            }
        }
        .frame(width: size, height: size)
    }
}

// MARK: - Warden Glow Effect

struct IoGlow: ViewModifier {
    let color: Color
    let radius: CGFloat
    @Environment(\.colorScheme) private var scheme

    func body(content: Content) -> some View {
        if scheme == .light {
            content
        } else {
            content
                .shadow(color: color.opacity(0.30), radius: radius, x: 0, y: 0)
                .shadow(color: color.opacity(0.20), radius: radius * 1.5, x: 0, y: 0)
        }
    }
}

extension View {
    func ioGlow(color: Color, radius: CGFloat = 8) -> some View {
        modifier(IoGlow(color: color, radius: radius))
    }
}

// MARK: - Warden Pulse Animation

struct IoPulse: ViewModifier {
    @State private var isPulsing = false
    let color: Color

    func body(content: Content) -> some View {
        content
            .overlay(
                Circle()
                    .stroke(color.opacity(0.5), lineWidth: 2)
                    .scaleEffect(isPulsing ? 1.3 : 1.0)
                    .opacity(isPulsing ? 0 : 1)
            )
            .onAppear {
                withAnimation(.easeOut(duration: 1.2).repeatForever(autoreverses: false)) {
                    isPulsing = true
                }
            }
    }
}

extension View {
    func ioPulse(color: Color) -> some View {
        modifier(IoPulse(color: color))
    }
}

// E123: IoToggle removed — defined but never instantiated anywhere in the app.

// MARK: - Warden Sidebar Item

struct SidebarItem: View {
    let label: String
    let rune: String
    let selected: Bool
    let onClick: () -> Void
    var isGold: Bool = false
    var isLocked: Bool = false
    var showExternal: Bool = false
    var shortcut: KeyEquivalent? = nil
    /// Per-module selection tint. `nil` keeps the shared violet selection. A module that owns a
    /// highlight colour passes it here so the SELECTED row reads in that module's colour, the
    /// same way each pane tints its own rune tile and primary button (Prophet #00b4ff). The
    /// alpha ramp is the same variable-alpha treatment the pane chrome uses.
    var accent: Color? = nil

    @State private var isHovering = false

    /// Selected-row fill: the module accent ramped in opacity, or the shared violet pair.
    private var selectionFill: LinearGradient {
        if let accent {
            return LinearGradient(colors: [accent.opacity(0.92), accent.opacity(0.55)],
                                  startPoint: .leading, endPoint: .trailing)
        }
        return LinearGradient(colors: [Color(red: 124/255, green: 92/255, blue: 255/255),
                                       Color(red: 106/255, green: 73/255, blue: 239/255)],
                              startPoint: .leading, endPoint: .trailing)
    }

    /// Selected-row glow, matched to `selectionFill`.
    private var selectionGlow: Color {
        accent ?? Color(red: 124/255, green: 92/255, blue: 255/255)
    }

    var body: some View {
        Button(action: onClick) {
            HStack(spacing: 13) {
                Text(rune)
                    // rune.unicodeScalars.first!.value is already numeric (UInt32) — no radix parse
                    // needed; 0x16A0 is the start of the Runic Unicode block (real glyphs get 17pt).
                    .font(.system(size: rune.count == 1 && rune.unicodeScalars.first!.value > 0x16A0 ? 17 : 15))
                    .frame(width: 22)
                    .foregroundColor(selected ? .white : (isGold ? Color(red: 224/255, green: 180/255, blue: 90/255) : Color(red: 155/255, green: 138/255, blue: 201/255)))

                Text(label)
                    .font(.navLabel(15))
                    .foregroundColor(selected ? .white : (isGold ? Color(red: 224/255, green: 180/255, blue: 90/255) : Color(red: 179/255, green: 167/255, blue: 214/255)))

                Spacer()

                if isLocked {
                    HStack(spacing: 5) {
                        Image(systemName: "lock.fill")
                            .font(.system(size: 10))
                        Text("PRO")
                            .font(.eyebrow(10))
                    }
                    .foregroundColor(Color(red: 224/255, green: 180/255, blue: 90/255))
                    .shadow(color: Color(red: 224/255, green: 180/255, blue: 90/255).opacity(0.55), radius: 5)
                }

                if showExternal {
                    Text("↗")
                        .font(.system(size: 12))
                        .foregroundColor(Color(red: 111/255, green: 101/255, blue: 151/255))
                }
            }
            .padding(.horizontal, 13)
            .padding(.vertical, 8)
            .frame(height: 36)
            .background(
                RoundedRectangle(cornerRadius: 9)
                    .fill(selected ? AnyShapeStyle(selectionFill)
                                   : AnyShapeStyle(isHovering ? Color(white: 1, opacity: 0.04) : Color.clear))
            )
            .shadow(color: selected ? selectionGlow.opacity(0.4) : .clear,
                    radius: selected ? 8 : 0, y: selected ? 8 : 0)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .keyboardShortcut(shortcut.map { KeyboardShortcut($0, modifiers: .command) })
        .onHover { hovering in isHovering = hovering }
        // VoiceOver reads the tool name, not the decorative rune's Unicode code point.
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(isLocked ? "\(label), requires Pro" : label)
        .accessibilityAddTraits(selected ? [.isSelected] : [])
    }
}

// MARK: - Severity -> colour, ONE source

extension Color {
    static func ioSeverity(_ s: Severity) -> Color {
        switch s {
        case .clean:    return .ioSuccessGreen
        case .info:     return .ioInfoPurple
        case .warning:  return .ioWarningAmber
        case .high:     return .ioHighOrange
        case .critical: return .ioCriticalRed
        case .unknown:  return .ioTextMuted
        }
    }
}

// MARK: - Warden Gold Divider

struct WardenGoldDivider: View {
    var body: some View {
        Rectangle()
            .fill(Color(red: 217/255, green: 164/255, blue: 65/255).opacity(0.32))
            .frame(height: 1)
            .padding(.horizontal, 8)
    }
}

// MARK: - Design System Typography Tokens

extension Font {
    /// Cinzel display — always clamped to the 18pt minimum.
    static func ioDisplay(size: CGFloat, weight: Font.Weight = .semibold) -> Font {
        .custom(SyxxTypography.cinzelPostScriptName(for: weight),
                size: SyxxTypography.clampedDisplaySize(size))
    }

    /// EB Garamond Italic for emphasis, or Cormorant SC alternate body when non-italic.
    static func ioLore(size: CGFloat, isItalic: Bool = true) -> Font {
        if isItalic {
            return .custom("EBGaramond-Italic", size: size)
        }
        return .bodyAlternate(size)
    }

    /// Jost UI labels / controls (weight 500). Replaces the retired grotesk UI role.
    static func ioGrotesk(size: CGFloat, weight: Font.Weight = .medium) -> Font {
        // Ordinary UI never uses Jost 600+. Medium and above → Medium (500).
        if weight == .light || weight == .thin || weight == .ultraLight {
            return .bodyLight(size)
        }
        if weight == .regular {
            return .body(size)
        }
        return .uiLabel(size)
    }

    /// JetBrains Mono for technical evidence (IPs, hashes, timestamps).
    static func ioMono(size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .custom(SyxxTypography.jetBrainsMonoPostScriptName(for: weight), size: size)
    }
}

/// View helper for specimen eyebrows: 10pt Jost 500, uppercase, 0.26em tracking.
struct IoEyebrow: ViewModifier {
    func body(content: Content) -> some View {
        content
            .font(.eyebrow(10))
            .textCase(.uppercase)
            .tracking(10 * SyxxTypography.eyebrowTrackingEm)
    }
}

extension View {
    func ioEyebrow() -> some View {
        modifier(IoEyebrow())
    }
}

#if DEBUG
#Preview {
    VStack(alignment: .leading, spacing: 16) {
        StatusPill(label: "READY", color: .ioSuccessGreen)
        IoSeverityBadge(severity: .warning)
        IoProgressRing(progress: 0.5, lineWidth: 10, size: 96, label: "3 OF 6", sublabel: "preview")
        SidebarItem(label: "Prophet", rune: "\u{16A8}", selected: true, onClick: {})
        SidebarItem(label: "Mirror", rune: "\u{16DE}", selected: false, onClick: {}, isLocked: true)
        WardenGoldDivider()
    }
    .padding(24)
    .syxxPreviewCanvas()
}
#endif

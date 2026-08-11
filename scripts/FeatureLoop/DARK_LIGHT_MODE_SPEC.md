# Feature 2: Dark/Light Mode — Implementation Spec

**Version:** 1.0.0
**Created:** 2026-08-06
**Status:** Spec Complete (spec agent)

---

## 1. Current State Summary

### macOS (SwiftUI)
- **Color system is already dual-mode.** `IoDesignSystem.swift` uses `ioDynamic(light:dark:)` for every semantic token — canvas, surfaces, text, module highlights, and severity colors all have light and dark hex values defined.
- **Theme is locked to dark.** `ContentView.swift:113` sets `@State private var localDarkMode = true` and applies `.preferredColorScheme(localDarkMode ? .dark : .light)` at the root. The sidebar receives `$localDarkMode` as a binding but never exposes a toggle.
- **Persistence:** Settings use `SyxxSettingsStore` (JSON file at `~/.syxx/settings.json`). No `themeMode` field exists yet.
- **GuidedSetup** exists at `GuidedSetup.swift` with a `SetupChoices` view (experience mode, encryption, telemetry). No appearance/theme option.
- **SettingsView** exists with cards for Experience Mode, Encrypted DNS, Privacy, Notifications, Data. No Appearance card.

### Linux (Avalonia UI / C#)
- **Dark-only.** `App.axaml` sets `RequestedThemeVariant="Dark"` and `PreferSystemTheme="False"`.
- All color tokens in `App.axaml` resources are hardcoded dark hex values (e.g., `SyxCanvasColor=#0b0518`, `SyxTextColor=#efeafe`).
- `SettingsView.axaml` exists but has no theme section.
- No `ThemeManager` or dynamic theme switching infrastructure.

### Windows (WPF / C#)
- **Dark-only.** `Styles/IoDesignSystem.xaml` defines all brushes with hardcoded dark hex values.
- ~69 direct `{StaticResource IoSurfaceDarkBrush}` and `{StaticResource IoBackgroundDarkBrush}` references across all view XAML files.
- `SettingsView.xaml` exists with cards for Encrypted DNS, Privacy, Notifications, Data. No theme section.
- `FirstRunView.xaml` exists (onboarding flow).

---

## 2. Design Decisions

### 2.1 Theme Preference Model
A single enum persisted in the existing settings store:
```
enum ThemeMode: String {
    case system   // follow OS appearance (DEFAULT)
    case dark     // force dark
    case light    // force light
}
```

### 2.2 System Theme Detection (per platform)
| Platform | API |
|----------|-----|
| macOS | `@Environment(\.colorScheme)` — SwiftUI already provides this. When `themeMode == .system`, remove `.preferredColorScheme()` override entirely so the OS appearance flows through naturally. |
| Linux | Avalonia's `Application.Current!.RequestedThemeVariant` can be set to `ThemeVariant.Default` to follow the system (FluentAvalonia must set `PreferSystemTheme="True"`). |
| Windows | `Application.Current.RequestedTheme` or `SystemEvents.UserPreferenceChanged` to detect OS theme changes. |

### 2.3 Persistence
| Platform | Mechanism |
|----------|-----------|
| macOS | Add `themeMode: String` to `SyxxSettings` struct, persisted via `SyxxSettingsStore` JSON. Default: `"system"`. |
| Linux | Add `ThemeMode` property to the existing settings JSON file. Same schema as macOS. |
| Windows | Same JSON settings file approach via `Syxx.Kit` shared settings model. |

### 2.4 Light Mode Color Palette

macOS tokens already define light values via `ioDynamic`. For Linux and Windows, light-mode resource dictionaries must be added.

| Token | Dark Hex | Light Hex | Notes |
|-------|----------|-----------|-------|
| Canvas | `#0b0518` | `#e6ddf7` | Pale violet canvas |
| Window/Surface | `#170c2e` | `#eee7fb` | Card backgrounds |
| Surface Elevated | `#1d1640` | `#dcd0f5` | Sidebar, cards |
| Border Subtle | `#382c66` | `#c8b9ee` | Hairlines |
| Text Primary | `#efeafe` | `#2a134d` | Deep violet |
| Text Secondary | `#c9c1e6` | `#6a4fa0` | Body copy |
| Text Muted | `#9a8fc0` | `#9a8fc0` | Same (mid-grey) |
| Text Faint | `#6f6597` | `#7d7399` | Timestamps |
| Blue/Violet | `#7c5cff` | `#6a3de8` | Darker for contrast |
| Prophet Blue | `#00b4ff` | `#00b4ff` | Same (works on both) |
| Mirror Yellow | `#ffd400` | `#8a6a00` | **Darker in light** |
| Glutton Pink | `#ff6eb4` | `#b0286e` | **Darker in light** |
| Cage Cyan | `#00e5ff` | `#0a8a8a` | **Darker in light** |
| Witness Red | `#ff3355` | `#b01e38` | **Darker in light** |
| Success Green | `#2dd4bf` | `#1e9e8f` | Darker in light |
| Critical Red | `#ff4d6d` | `#c41e3a` | Darker in light |
| Warning Amber | `#f7931e` | `#c87a10` | Darker in light |
| Gold | `#d9a441` | `#d9a441` | Same |

**Module highlight inversion rule:** In light mode, module highlights are slightly *darker* than their dark-mode counterparts to maintain contrast ratio ≥4.5:1 against the pale canvas.

### 2.5 Gradient Inversion
- Sidebar: `(#dcd0f5, #d0c0f0)` instead of `(#1d1240, #160c30)`
- Window fill: `(#eee7fb, #e0d4f5)` instead of `(#170c2e, #0f0720)`
- Radial canvas glow: removed in light mode

---

## 3. Implementation Plan by Platform

### 3.1 macOS (Phase 1 — implement first)

#### Files to change:
1. **`SettingsView.swift`** — Add APPEARANCE card with 3-option Picker (System / Dark / Light)
2. **`ContentView.swift`** — Replace hardcoded `localDarkMode = true` with computed binding from settings
3. **`GuidedSetup.swift`** — Add APPEARANCE section after Experience Mode
4. **`SyxxSettings.swift`** — Add `var themeMode: String = "system"`
5. **`FirstRunCoordinator.swift`** — Wire `themeMode` through `applySelection()`

#### Key implementation:
```swift
var resolvedScheme: ColorScheme? {
    switch settings.themeMode {
    case "dark": return .dark
    case "light": return .light
    default: return nil  // follow system
    }
}
```

### 3.2 Linux / Avalonia (Phase 2)

#### Files to change:
1. **`App.axaml`** — Add light-mode color resources
2. **`App.axaml.cs`** — Add `ThemeManager` class
3. **`SettingsView.axaml`** — Add APPEARANCE card
4. **`SettingsViewModel.cs`** — Add `ThemeMode` property
5. **All view AXAML files** — Replace hardcoded dark brush refs with theme-aware aliases

#### Approach: Resource dictionary swap with `Themes/DarkTheme.axaml` and `Themes/LightTheme.axaml`

### 3.3 Windows / WPF (Phase 3)

#### Files to change:
1. **`IoDesignSystem.xaml`** — Add semantic brush aliases
2. **Create `IoDesignSystem.Light.xaml`** — Light-mode resource dictionary
3. **Create `Services/ThemeManager.cs`** — Theme swap + OS detection
4. **`SettingsView.xaml`** — Add APPEARANCE card
5. **ALL view XAML files (~10 files, ~69 references)** — `StaticResource IoSurfaceDarkBrush` → `DynamicResource IoSurfaceBrush`
6. **`FirstRunView.xaml`** — Add appearance option

---

## 4. SettingsView UI (all platforms)

```
┌─────────────────────────────────────────────┐
│ APPEARANCE                                  │
│ Choose how Syxx looks.                      │
│                                             │
│ ┌─────────────────────────────────────┐     │
│ │  ◉ System  ○ Dark  ○ Light         │     │
│ └─────────────────────────────────────┘     │
│                                             │
│ System follows your OS appearance setting.  │
└─────────────────────────────────────────────┘
```

---

## 5. Acceptance Criteria

- AC-01: On macOS, defaults to 'system' theme mode on first launch
- AC-02: On macOS, SettingsView has APPEARANCE card as first card with 3-option segmented control
- AC-03: On macOS, toggling appearance picker immediately re-themes without restart
- AC-04: On macOS, GuidedSetup includes Appearance option defaulting to System
- AC-05: On macOS, themeMode persists across app restarts via SyxxSettingsStore
- AC-06: On macOS, 'system' mode responds to OS appearance changes in real time
- AC-07: Light mode uses existing ioDynamic light values (canvas #e6ddf7, surface #eee7fb, etc.)
- AC-08: Light mode module highlights are darker: Mirror Yellow #8a6a00, Glutton Pink #b0286e, Cage Cyan #0a8a8a, Witness Red #b01e38
- AC-09: Light mode suppresses radial canvas gradient
- AC-10: All text levels meet WCAG AA contrast (≥4.5:1) in both modes
- AC-11: On Linux, defaults to 'system' — no more hardcoded RequestedThemeVariant="Dark"
- AC-12: On Linux, SettingsView includes APPEARANCE section
- AC-13: On Linux, light mode resources match macOS light hex values
- AC-14: On Linux, theme changes take effect immediately at runtime
- AC-15: On Windows, defaults to 'system' theme mode
- AC-16: On Windows, SettingsView includes APPEARANCE section
- AC-17: On Windows, all ~69 hardcoded dark brush refs replaced with DynamicResource semantic aliases
- AC-18: On Windows, light mode resource dictionary matches macOS light hex values
- AC-19: On Windows, FirstRunView includes appearance selection
- AC-20: On all platforms, themeMode values are 'system'/'dark'/'light' — consistent JSON schema
- AC-21: On all platforms, button gradients render with sufficient contrast in both modes
- AC-22: On all platforms, gold dividers, wordmark gradient, active nav visible in both modes
- AC-23: On all platforms, no hardcoded 'Dark' brush key references remain after refactor

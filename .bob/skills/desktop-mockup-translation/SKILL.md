---
name: desktop-mockup-translation
description: Translate pixel-perfect design mockups to production code across SwiftUI, WinUI 3, Avalonia UI, and Qt 6 with consistent design tokens, color mapping, typography, and cross-platform parity. Use when implementing a view from a mockup, ensuring visual fidelity across desktop platforms, or porting a design between platforms.
---

# Desktop Mockup Translation

Translate pixel-perfect design mockups to production code across SwiftUI (macOS), WinUI 3 (Windows), Avalonia UI (Linux), and Qt 6 (C++) with consistent design tokens, color mapping, typography, and cross-platform parity.

## When to Use This Skill

- Implementing a desktop view from a Figma/HTML/Sketch mockup
- Ensuring visual fidelity across all desktop platforms
- Porting a design from one platform to another
- Mapping design tokens to platform-native equivalents
- Debugging visual discrepancies between platforms

## Translation Process

### Step 1: Decompose the Mockup

Break the mockup into a layout tree:

```
1. Outer container: [dimensions, padding, background]
2. Section A: [layout type, children, spacing]
3. Section B: [layout type, children, spacing]
4. Leaf elements: [text, icons, shapes, controls]
```

### Step 2: Map Design Tokens

**Colors:**

| Token | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Accent | `Color.ioAccent` | `{StaticResource IoAccentBrush}` | `{StaticResource IoAccentBrush}` | `QColor(...)` |
| Background | `Color.ioSurfaceDark` | `{StaticResource SyxSurfaceFillBrush}` | `{StaticResource SyxSurfaceFillBrush}` | palette window color |
| Border | `Color.ioBorderSubtle` | `{StaticResource SyxBorderBrush}` | `{StaticResource SyxBorderBrush}` | `QColor(48, 54, 61)` |
| Text primary | `Color.ioText` | `{StaticResource SyxTextBrush}` | `{StaticResource SyxTextBrush}` | palette text color |
| Text muted | `Color.ioTextMuted` | `{StaticResource SyxTextMutedBrush}` | `{StaticResource SyxTextMutedBrush}` | palette midlight text |

**Typography:**

| Role | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Display | `.font(.custom("Cinzel-Bold", size: 28))` | `FontFamily="SyxDisplayFont" FontSize="28"` | `FontFamily="{StaticResource SyxDisplayFont}" FontSize="28"` | `QFont("family", 28, QFont::Bold)` |
| Heading | `.font(.system(size: 18, weight: .semibold))` | `FontSize="18" FontWeight="SemiBold"` | `FontSize="18" FontWeight="SemiBold"` | `QFont("family", 18, QFont::DemiBold)` |
| Body | `.font(.system(size: 14))` | `FontSize="14"` | `FontSize="14"` | `QFont("family", 14)` |
| Caption | `.font(.system(size: 10, weight: .bold)).tracking(2)` | `FontSize="10" FontWeight="Bold" CharacterSpacing="200"` | `FontSize="10" FontWeight="Bold" LetterSpacing="2"` | `QFont("family", 10, QFont::Bold)` + `setLetterSpacing` |
| Monospace | `.font(.system(.body, design: .monospace))` | `FontFamily="Cascadia Code"` | `FontFamily="Ubuntu Mono"` | `QFont("monospace", 14)` |

### Step 3: Map Visual Properties

| Mockup Element | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Padding | `.padding(.horizontal, N)` | `Padding="N"` | `Padding="N"` | `setContentsMargins(N,N,N,N)` |
| Margin | `.padding(.bottom, N)` | `Margin="0,0,0,N"` | `Margin="0,0,0,N"` | `layout->setSpacing(N)` |
| Rounded corners | `.clipShape(RoundedRectangle(cornerRadius: 14))` | `CornerRadius="14"` | `CornerRadius="14"` | `border-radius: 14px` in QSS |
| Shadow | `.shadow(color: .black.opacity(0.3), radius: 8, y: 4)` | `<ThemeShadow/>` | `BoxShadow="0 4 8 #4D000000"` | `QGraphicsDropShadowEffect` |
| Gradient | `LinearGradient(colors: [...], startPoint: .top, endPoint: .bottom)` | `<LinearGradientBrush>` | `<LinearGradientBrush>` | `QLinearGradient` |
| Glass morphism | `.background(.ultraThinMaterial)` | `<BackdropType>Acrylic</BackdropType>` | `TransparencyLevelHint="Acrylic"` | Semi-transparent `QWidget` + blur |
| Opacity | `.opacity(0.18)` | `Opacity="0.18"` | `Opacity="0.18"` | `setWindowOpacity(0.18)` or QSS `opacity` |
| Border | `.overlay(RoundedRectangle(...).stroke(...))` | `BorderBrush + BorderThickness` | `BorderBrush + BorderThickness` | QSS `border: 1px solid #color` |

### Step 4: Preserve Proportions

Use relative sizing so the layout adapts to different window sizes:

- **SwiftUI:** `.frame(maxWidth: .infinity)`, `GeometryReader` for proportional layouts
- **WinUI 3:** `Grid` with `Star` sizing, `HorizontalAlignment="Stretch"`
- **Avalonia:** `Grid` with `*` sizing, `HorizontalAlignment="Stretch"`
- **Qt 6:** `QSizePolicy::Expanding`, stretch factors in `QGridLayout`

### Step 5: Verify

After implementing, verify by:
1. Building and running on each platform
2. Comparing the rendered output to the mockup
3. Checking responsive behavior at different window sizes
4. Verifying accessibility labels and keyboard navigation

## Cross-Platform Parity Checklist

- [ ] Same view name across all platforms
- [ ] Same layout hierarchy and spacing tokens
- [ ] Same color tokens (mapped to platform equivalents)
- [ ] Same typography roles (display, heading, body, caption, mono)
- [ ] Same interactive behavior (hover, focus, click)
- [ ] Same accessibility labels and hints
- [ ] Same error states and empty states
- [ ] Same dark/light theme support

## Common Translation Issues

| Issue | Cause | Fix |
|---|---|---|
| Color looks different | Different gamma/color space handling | Use platform-specific color tokens, not raw hex |
| Font rendering differs | Different font hinting/rasterization | Accept minor rendering differences; match type role, not pixel-perfect glyphs |
| Spacing looks off | Different default padding/margin | Use explicit spacing tokens; never rely on platform defaults |
| Shadow looks heavier | Different shadow implementations | Adjust radius/opacity per platform; SwiftUI shadows are softer than WinUI |
| Glass morphism absent | Not supported on all platforms | Provide fallback: semi-transparent background on Qt |

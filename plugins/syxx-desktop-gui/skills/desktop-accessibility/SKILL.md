---
name: desktop-accessibility
description: Implement WCAG 2.2 compliant desktop applications with VoiceOver (macOS), Narrator (Windows), Orca (Linux), and keyboard navigation across SwiftUI, WinUI 3, Avalonia UI, and Qt 6. Use when auditing desktop app accessibility, implementing screen reader support, or ensuring keyboard navigation across all desktop platforms.
---

# Desktop Accessibility

Implement WCAG 2.2 compliant desktop applications with VoiceOver (macOS), Narrator (Windows), Orca (Linux), and keyboard navigation across SwiftUI, WinUI 3, Avalonia UI, and Qt 6.

## When to Use This Skill

- Auditing desktop app accessibility for WCAG compliance
- Implementing screen reader support across platforms
- Ensuring keyboard navigation works on all desktop targets
- Adding accessibility labels and hints to desktop views
- Testing with assistive technology (VoiceOver, Narrator, Orca)

## Reference

See `references/details.md` for detailed platform-specific reference material.

## Accessibility API Mapping

### Labels & Hints

| Concept | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Accessible name | `.accessibilityLabel("Name")` | `AutomationProperties.Name="Name"` | `AutomationProperties.Name="Name"` | `accessible.setName("Name")` |
| Help text | `.accessibilityHint("Description")` | `AutomationProperties.HelpText="Desc"` | `AutomationProperties.HelpText="Desc"` | `accessible.setDescription("Desc")` |
| Live region | `.accessibilityAddTraits(.updatesFrequently)` | `AutomationProperties.LiveSetting="Polite"` | `AutomationProperties.LiveSetting="Polite"` | `QAccessible::updateAccessibility()` |
| Hidden from AT | `.accessibilityHidden(true)` | `AutomationProperties.AccessibilityView="Raw"` | N/A (set `IsVisible=false` or custom) | `accessible.setRole(QAccessible::NoRole)` |

### Roles & Traits

| Concept | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Button | `.accessibilityAddTraits(.isButton)` | `Button` control (inherent) | `Button` control (inherent) | `QAccessible::Button` |
| Heading | `.accessibilityAddTraits(.isHeader)` | `AutomationProperties.HeadingLevel="1"` | Custom `AutomationPeer` | `QAccessible::Heading` |
| Toggle | `.accessibilityAddTraits(.isToggle)` | `ToggleSwitch` (inherent) | `ToggleButton` (inherent) | `QAccessible::CheckBox` |
| Link | `.accessibilityAddTraits(.isLink)` | `HyperlinkButton` (inherent) | `Button` + custom role | `QAccessible::Link` |
| Image | `.accessibilityLabel("Alt text")` | `AutomationProperties.Name` on `Image` | `AutomationProperties.Name` on `Image` | `QAccessible::Graphic` + description |

### Keyboard Navigation

| Concept | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Focus ring | `.focusable()` | `UseSystemFocusVisuals="True"` | `FocusManager` | `QStyle::PE_FrameFocusRect` |
| Tab order | `.accessibilitySortPriority(N)` | `KeyboardNavigation.TabIndex="N"` | `TabNavigation` / `TabIndex` | `setTabOrder(prev, next)` |
| Keyboard shortcut | `.keyboardShortcut("cmd+s")` | `KeyTip` / `KeyboardAccelerator` | `KeyBinding` | `QShortcut` + `QKeySequence` |
| Focus trap | `.accessibilityElement(children: .contain)` | `KeyboardNavigation.TabNavigation="Cycle"` | Custom focus scope | `setFocusPolicy(Qt::TabFocus)` |

### Focus Management

| Pattern | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Move focus | `@FocusState` + `.focused()` | `FocusManager.TryMoveFocus()` | `FocusManager.SetFocusedElement()` | `setFocus()` |
| Initial focus | `.onAppear { focusField = true }` | `Loaded` event + `Focus()` | `AttachedToVisualTree` + `Focus()` | `showEvent` + `setFocus()` |
| Return focus after modal | `@FocusState` restoration | `FocusManager.GetFocusedElement()` save/restore | `FocusManager.GetFocusedElement()` save/restore | `focusWidget()` save/restore |

## Color Contrast

### WCAG 2.2 Requirements

- **Normal text** (< 18pt / 14pt bold): minimum 4.5:1 contrast ratio
- **Large text** (≥ 18pt / 14pt bold): minimum 3:1 contrast ratio
- **UI components & graphical objects**: minimum 3:1 contrast ratio

### Testing Per Platform

| Platform | Tool | Command |
|---|---|---|
| macOS | Accessibility Inspector | Xcode → Open Developer Tool → Accessibility Inspector |
| Windows | Accessibility Insights for Windows | `ms-accessibility-insights` |
| Linux | Accerciser | `accerciser` |
| Qt | GammaRay | `gammaray` |

## Screen Reader Testing

### VoiceOver (macOS)

1. Enable: `⌘F5` or System Settings → Accessibility → VoiceOver
2. Navigate: `Ctrl+Option+Arrow keys`
3. Interact: `Ctrl+Option+Space` to activate
4. Rotor: `Ctrl+Option+U` for headings, links, etc.
5. Verify: every control has a label, every state change is announced

### Narrator (Windows)

1. Enable: `Win+Ctrl+Enter`
2. Navigate: `Caps Lock+Arrow keys`
3. Interact: `Caps Lock+Enter` to activate
4. Scan mode: `Caps Lock+Space` for document navigation
5. Verify: `AutomationProperties` are correct, live regions announce updates

### Orca (Linux)

1. Enable: `Super+Alt+S` or `orca --setup`
2. Navigate: `NumPad` keys for structural navigation
3. Interact: `Orca+Enter` to activate
4. Flat review: `Orca+KP_Enter` for by-character navigation
5. Verify: ATK/AT-SPI roles are correct, `AutomationProperties` are announced

## Common Accessibility Patterns

### Announcing State Changes

```swift
// SwiftUI — announce dynamic content updates
Text(statusMessage)
    .accessibilityAddTraits(.updatesFrequently)
```

```xml
<!-- WinUI 3 — live region for dynamic content -->
<TextBlock AutomationProperties.LiveSetting="Polite"
           Text="{x:Bind StatusMessage}" />
```

```xml
<!-- Avalonia — live region -->
<TextBlock AutomationProperties.LiveSetting="Polite"
           Text="{Binding StatusMessage}" />
```

### Accessible Custom Controls

```swift
// SwiftUI — custom toggle with accessibility
Toggle("Feature", isOn: $enabled)
    .toggleStyle(IoToggleStyle())
    .accessibilityLabel("Enable feature")
    .accessibilityHint("Turns the feature on or off")
    .accessibilityAddTraits(.isToggle)
```

```xml
<!-- WinUI 3 — custom toggle with AutomationProperties -->
<ToggleSwitch AutomationProperties.Name="Enable feature"
              AutomationProperties.HelpText="Turns the feature on or off"
              IsOn="{x:Bind Enabled, Mode=TwoWay}" />
```

### Accessible Data Tables

- Provide column headers via `accessibilityHeader` (SwiftUI) or `AutomationProperties.HeadingLevel` (WinUI/Avalonia)
- Announce row/column position when navigating
- Support sort indicators via accessibility values

## Accessibility Audit Checklist

- [ ] Every interactive element has an accessible name
- [ ] Every interactive element has a help text / hint
- [ ] Color contrast meets WCAG 2.2 AA (4.5:1 text, 3:1 UI)
- [ ] All functionality is keyboard-accessible (no mouse-only interactions)
- [ ] Tab order follows visual layout (top-left to bottom-right)
- [ ] Focus indicators are visible and meet contrast requirements
- [ ] Dynamic content changes are announced via live regions
- [ ] Modals trap focus correctly and restore focus on close
- [ ] Images have alt text or are marked as decorative
- [ ] Form validation errors are announced, not just visually displayed

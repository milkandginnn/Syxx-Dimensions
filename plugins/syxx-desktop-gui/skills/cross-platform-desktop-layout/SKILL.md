---
name: cross-platform-desktop-layout
description: Build responsive desktop layouts that work across SwiftUI, WinUI 3, Avalonia UI, and Qt 6 with consistent spacing, margin ownership, and platform-native idioms. Use when creating or fixing desktop GUI layouts, ensuring cross-platform parity, or implementing responsive desktop views.
---

# Cross-Platform Desktop Layout

Build responsive desktop layouts across SwiftUI (macOS), WinUI 3 (Windows), Avalonia UI (Linux), and Qt 6 (C++) with consistent spacing, margin ownership, and platform-native idioms.

## When to Use This Skill

- Creating new desktop GUI views that must work across multiple platforms
- Fixing layout parity issues between platform implementations
- Implementing responsive desktop layouts that adapt to window resizing
- Establishing margin ownership and spacing conventions
- Porting a view from one platform to another

## Spacing Token Grid

All desktop platforms use the same spacing tokens. Round off-grid measurements to the nearest token:

```
4 · 8 · 12 · 16 · 24 · 32 · 48 px
```

## Margin Ownership

**The outermost container owns edge margins.** Individual views own internal spacing only.

| Anti-Pattern | Correct Pattern |
|---|---|
| Child view adds `.padding(.horizontal, 35)` | Parent provides edge padding; child adds internal spacing only |
| Both parent and child add horizontal padding | Remove the child's root horizontal padding |
| Fixed-width containers that overflow on resize | Use `maxWidth: .infinity` / `GridLength("*")` / star-sized columns |

## Layout Tree Decomposition

Before writing any layout code, decompose the design into a tree:

```
DashboardView
├── Sidebar (236px fixed width)
│   ├── Logo + Wordmark
│   ├── Navigation Items
│   └── Footer Pill
├── Detail Area (flex)
│   ├── Header (eyebrow + title + status)
│   ├── Content Cards (2-column grid)
│   └── Action Bar
```

## Platform Layout Mapping

### Containers

| Concept | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Vertical stack | `VStack(spacing: N)` | `StackPanel Orientation="Vertical" Spacing="N"` | `StackPanel Orientation="Vertical" Spacing="N"` | `QVBoxLayout` + `addSpacing(N)` |
| Horizontal stack | `HStack(spacing: N)` | `StackPanel Orientation="Horizontal" Spacing="N"` | `StackPanel Orientation="Horizontal" Spacing="N"` | `QHBoxLayout` + `addSpacing(N)` |
| Z-layering | `ZStack { ... }` | `Grid` + `Canvas.ZIndex` | `Panel` + `ZIndex` | `QStackedLayout` |
| Grid | `Grid { ... }` | `Grid` with `RowDefinitions`/`ColumnDefinitions` | `Grid` with `RowDefinitions`/`ColumnDefinitions` | `QGridLayout` |
| Scroll | `ScrollView { ... }` | `ScrollViewer` | `ScrollViewer` | `QScrollArea` |
| Split view | `NavigationSplitView` | `NavigationView` | `SplitView` | `QSplitter` |

### Sizing

| Concept | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Fixed width | `.frame(width: 236)` | `Width="236"` | `Width="236"` | `setFixedWidth(236)` |
| Flexible | `.frame(maxWidth: .infinity)` | `HorizontalAlignment="Stretch"` | `HorizontalAlignment="Stretch"` | `sizePolicy.setHorizontalPolicy(QSizePolicy::Expanding)` |
| Star-sized | N/A (use `Grid` with size classes) | `Width="2*"` | `Width="2*"` | `QGridLayout` stretch factors |
| Min/max | `.frame(minWidth: 400, maxWidth: 800)` | `MinWidth="400" MaxWidth="800"` | `MinWidth="400" MaxWidth="800"` | `setMinimumWidth(400)` + `setMaximumWidth(800)` |

### Spacing & Padding

| Concept | SwiftUI | WinUI 3 | Avalonia | Qt 6 |
|---|---|---|---|---|
| Padding | `.padding(N)` or `.padding(.horizontal, N)` | `Padding="N"` | `Padding="N"` | `setContentsMargins(N,N,N,N)` |
| Margin | `.padding(.bottom, N)` | `Margin="0,0,0,N"` | `Margin="0,0,0,N"` | `layout->setSpacing(N)` |
| Gap between children | `VStack(spacing: N)` | `StackPanel Spacing="N"` | `StackPanel Spacing="N"` | `layout->addSpacing(N)` |

## Responsive Desktop Patterns

### Window-Size Adaptation

```
SwiftUI:     @Environment(\.horizontalSizeClass) + GeometryReader
WinUI 3:     VisualStateManager + AdaptiveTrigger
Avalonia:    Styles with :width Selector
Qt 6:        QResizeEvent override + QSizePolicy
```

### Sidebar + Detail Pattern

```
SwiftUI:     NavigationSplitView(columnVisibility:) with width constraints
WinUI 3:     NavigationView with PaneDisplayMode
Avalonia:    SplitView with IsPaneOpen binding
Qt 6:        QSplitter with stretch factors
```

### Card Grid Pattern

```
SwiftUI:     LazyVGrid with adaptive columns
WinUI 3:     ItemsRepeater with UniformGrid layout
Avalonia:    UniformGrid or ItemsControl with WrapPanel
Qt 6:        QGridLayout with dynamic column count
```

## Common Pitfalls

1. **Double margins** — both parent and child add horizontal padding. Parent owns edge margins.
2. **Fixed-width overflow** — use flexible sizing for content areas; reserve fixed widths for sidebars only.
3. **Platform-specific scroll** — SwiftUI `ScrollView` clips by default; WinUI `ScrollViewer` needs `HorizontalScrollBarVisibility`; Avalonia `ScrollViewer` needs `AllowAutoHide`.
4. **Grid star-sizing** — SwiftUI doesn't have star sizing; use `GeometryReader` or proportion-based `Grid` rows.
5. **Thread safety** — UI updates must dispatch to the main thread: `@MainActor` (SwiftUI), `DispatcherQueue.TryEnqueue()` (WinUI), `Dispatcher.UIThread.Post()` (Avalonia), `QMetaObject::invokeMethod` (Qt).

## Best Practices

- **Design for 1024×768 minimum** — desktop windows can be resized small
- **Test with narrow windows** — ensure content reflows rather than clips
- **Use consistent spacing tokens** — never hardcode arbitrary pixel values
- **Verify margin ownership** — trace the layout tree and confirm each view only owns its internal spacing
- **Build on each platform** — layout bugs are often invisible until rendered

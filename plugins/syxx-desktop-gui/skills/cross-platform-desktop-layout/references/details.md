# Cross-Platform Desktop Layout — Detailed Patterns

## SwiftUI Layout Deep Dive

### Grid with Adaptive Columns

```swift
Grid {
    GridRow {
        CardView()
        CardView()
    }
    GridRow {
        CardView()
        CardView()
    }
}
```

### GeometryReader for Responsive Widths

```swift
GeometryReader { geo in
    let columns = geo.size.width > 800 ? 3 : (geo.size.width > 500 ? 2 : 1)
    LazyVGrid(columns: Array(repeating: GridItem(.flexible()), count: columns)) {
        ForEach(items) { item in
            CardView(item: item)
        }
    }
}
```

### PreferenceKey for Frame Tracking

```swift
struct FramePreferenceKey: PreferenceKey {
    static var defaultValue: CGRect = .zero
    static func reduce(value: inout CGRect, nextValue: () -> CGRect) {
        value = nextValue()
    }
}

// Attach to detect size changes (prevents jiggling)
.overlay(
    GeometryReader { geo in
        Color.clear.preference(
            key: FramePreferenceKey.self,
            value: geo.frame(in: .global)
        )
    }
)
.onPreferenceChange(FramePreferenceKey.self) { frame in
    // Handle frame change
}
```

## WinUI 3 Layout Deep Dive

### Star-Sized Grid

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="236"/>  <!-- Fixed sidebar -->
        <ColumnDefinition Width="*"/>     <!-- Flexible content -->
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>    <!-- Header -->
        <RowDefinition Height="*"/>       <!-- Content -->
        <RowDefinition Height="Auto"/>    <!-- Footer -->
    </Grid.RowDefinitions>
</Grid>
```

### Responsive with VisualStateManager

```xml
<VisualStateManager.VisualStateGroups>
    <VisualStateGroup>
        <VisualState x:Name="WideState">
            <VisualState.StateTriggers>
                <AdaptiveTrigger MinWindowWidth="800"/>
            </VisualState.StateTriggers>
            <VisualState.Setters>
                <Setter Target="ContentGrid.Visibility" Value="Visible"/>
            </VisualState.Setters>
        </VisualState>
        <VisualState x:Name="NarrowState">
            <VisualState.StateTriggers>
                <AdaptiveTrigger MinWindowWidth="0"/>
            </VisualState.StateTriggers>
            <VisualState.Setters>
                <Setter Target="ContentGrid.Visibility" Value="Collapsed"/>
            </VisualState.Setters>
        </VisualState>
    </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

## Avalonia Layout Deep Dive

### DockPanel Pattern

```xml
<DockPanel>
    <Border DockPanel.Dock="Left" Width="236" Background="{StaticResource SyxSurfaceFillBrush}">
        <!-- Sidebar -->
    </Border>
    <Border DockPanel.Dock="Bottom" Height="48">
        <!-- Footer -->
    </Border>
    <ScrollViewer>
        <!-- Main content fills remaining space -->
    </ScrollViewer>
</DockPanel>
```

### Responsive Column Count

```xml
<ItemsControl Items="{Binding Cards}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <WrapPanel Orientation="Horizontal" ItemWidth="300"/>
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>
```

## Qt 6 Layout Deep Dive

### Splitter Layout

```cpp
auto splitter = new QSplitter(Qt::Horizontal);
auto sidebar = new QWidget;
auto content = new QWidget;
splitter->addWidget(sidebar);
splitter->addWidget(content);
splitter->setStretchFactor(0, 0);  // Sidebar: fixed
splitter->setStretchFactor(1, 1);  // Content: stretch
splitter->setSizes({236, 800});
```

### Responsive Grid

```cpp
auto grid = new QGridLayout;
int col = 0, row = 0;
const int maxCols = 3;
for (auto* card : cards) {
    grid->addWidget(card, row, col);
    col++;
    if (col >= maxCols) { col = 0; row++; }
}
```

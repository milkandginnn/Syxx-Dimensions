# Desktop Mockup Translation — Detailed Reference

## SwiftUI Mockup Translation

### Glass Morphism

```swift
// Background blur + tinted overlay
.background(.ultraThinMaterial)
.background(Color(red: 10/255, green: 6/255, blue: 20/255).opacity(0.35))
```

### Custom Toggle

```swift
struct IoToggleStyle: ToggleStyle {
    func makeBody(configuration: Configuration) -> some View {
        let isOn = configuration.isOn
        HStack(spacing: 8) {
            configuration.label
            Spacer()
            RoundedRectangle(cornerRadius: 13)
                .fill(isOn ? Color.ioAccent : Color.ioSurfaceDark)
                .overlay(alignment: isOn ? .trailing : .leading) {
                    Circle()
                        .fill(Color.white)
                        .padding(3)
                }
                .frame(width: 44, height: 26)
                .shadow(color: isOn ? Color.ioAccent.opacity(0.4) : .clear, radius: 8)
                .onTapGesture { configuration.isOn.toggle() }
        }
    }
}
```

### Section Title with Eyebrow

```swift
VStack(alignment: .leading, spacing: 6) {
    Text("THE SANCTUM")
        .font(.system(size: 10.5, weight: .bold))
        .tracking(2.8)
        .foregroundStyle(Color(red: 138/255, green: 110/255, blue: 240/255))
    Text("Settings")
        .font(.custom("Cinzel-Bold", size: 28))
        .foregroundStyle(Color.ioText)
}
```

## WinUI 3 Mockup Translation

### Mica Backdrop

```xml
<Window>
    <Window.SystemBackdrop>
        <MicaBackdrop/>
    </Window.SystemBackdrop>
</Window>
```

### Custom NavigationView Sidebar

```xml
<NavigationView PaneDisplayMode="Left"
                IsPaneToggleButtonVisible="False"
                OpenPaneLength="236"
                PaneBackground="#FF1D1240">
    <NavigationView.MenuItems>
        <NavigationViewItem Content="Dashboard" Icon="Home"/>
        <NavigationViewItem Content="Prophet" Icon="Globe"/>
    </NavigationView.MenuItems>
</NavigationView>
```

### Card with Border

```xml
<Border CornerRadius="14"
        Background="{StaticResource SyxSurfaceFillBrush}"
        BorderBrush="{StaticResource SyxBorderBrush}"
        BorderThickness="1"
        Padding="18">
    <StackPanel Spacing="10">
        <TextBlock Text="CARD TITLE"
                   FontSize="10" FontWeight="Bold"
                   CharacterSpacing="200"
                   Foreground="{StaticResource SyxTextMutedBrush}"/>
    </StackPanel>
</Border>
```

## Avalonia Mockup Translation

### Card with Border

```xml
<Border CornerRadius="14"
        Background="{StaticResource SyxSurfaceFillBrush}"
        BorderBrush="{StaticResource SyxBorderBrush}"
        BorderThickness="1"
        Padding="18">
    <StackPanel Spacing="10">
        <TextBlock Text="CARD TITLE"
                   FontSize="10" FontWeight="Bold"
                   LetterSpacing="2"
                   Foreground="{StaticResource SyxTextMutedBrush}"/>
    </StackPanel>
</Border>
```

### Toggle with Custom Style

```xml
<Styles>
    <Style Selector="ToggleSwitch">
        <Setter Property="OnContent" Value="On"/>
        <Setter Property="OffContent" Value="Off"/>
        <Setter Property="Foreground" Value="{StaticResource SyxTextBrush}"/>
    </Style>
    <Style Selector="ToggleSwitch:on">
        <Setter Property="Background" Value="{StaticResource IoAccentBrush}"/>
    </Style>
</Styles>
```

## Qt 6 Mockup Translation

### Dark Theme Palette

```cpp
QPalette darkPalette;
darkPalette.setColor(QPalette::Window, QColor(13, 17, 23));       // #0D1117
darkPalette.setColor(QPalette::WindowText, QColor(230, 237, 243)); // #E6EDF3
darkPalette.setColor(QPalette::Base, QColor(22, 27, 34));          // #161B22
darkPalette.setColor(QPalette::AlternateBase, QColor(48, 54, 61)); // #30363D
darkPalette.setColor(QPalette::Text, QColor(230, 237, 243));       // #E6EDF3
darkPalette.setColor(QPalette::Button, QColor(22, 27, 34));        // #161B22
darkPalette.setColor(QPalette::Highlight, QColor(79, 227, 217));   // #4FE3D9
qApp->setPalette(darkPalette);
```

### Card with QSS

```css
QFrame[class="card"] {
    background-color: #161B22;
    border: 1px solid #30363D;
    border-radius: 14px;
    padding: 18px;
}
```

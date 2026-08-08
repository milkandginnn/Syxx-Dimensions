using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace SyxxUI.Avalonia.Controls;

/// <summary>
/// Module header: rune tile + eyebrow + name + content slot.
/// Parity twin of macOS IoDesignSystem.ModuleHeader (SwiftUI).
/// </summary>
public class ModuleHeader : UserControl
{
    public static readonly StyledProperty<string> RuneProperty =
        AvaloniaProperty.Register<ModuleHeader, string>(nameof(Rune));

    public static readonly StyledProperty<string> EyebrowProperty =
        AvaloniaProperty.Register<ModuleHeader, string>(nameof(Eyebrow));

    public static new readonly StyledProperty<string> NameProperty =
        AvaloniaProperty.Register<ModuleHeader, string>(nameof(Name));

    public static readonly StyledProperty<IBrush> AccentBrushProperty =
        AvaloniaProperty.Register<ModuleHeader, IBrush>(nameof(AccentBrush));

    public string Rune
    {
        get => GetValue(RuneProperty);
        set => SetValue(RuneProperty, value);
    }

    public string Eyebrow
    {
        get => GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public new string Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public IBrush AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private readonly Border _runeTile;
    private readonly TextBlock _runeGlyph;
    private readonly TextBlock _eyebrowBlock;
    private readonly TextBlock _nameBlock;
    private readonly StackPanel _contentSlot;

    public ModuleHeader()
    {
        // D344: fonts and brushes are resolved lazily on the Loaded event rather than at construction
        // time. At construction, App.Current?.Resources may not yet be loaded (resource dictionaries
        // are loaded after the control tree is constructed), causing FontFamily.Default to be used
        // instead of the intended display font. Deferring to Loaded ensures resources are available.
        _runeGlyph = new TextBlock
        {
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _runeTile = new Border
        {
            Width = 54, Height = 54,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Top,
            Child = _runeGlyph,
        };

        _eyebrowBlock = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            LetterSpacing = 3,
        };

        _nameBlock = new TextBlock
        {
            FontSize = 26,
        };

        _contentSlot = new StackPanel { Spacing = 4 };

        var rightStack = new StackPanel
        {
            Spacing = 4,
            MaxWidth = 560,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _eyebrowBlock, _nameBlock, _contentSlot },
        };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 15,
            Children = { _runeTile, rightStack },
        };

        // D344: resolve fonts and brushes after resources are loaded
        Loaded += OnLoaded;
    }

    /// <summary>
    /// D344: resolve fonts and brushes from the application resource dictionary on the Loaded event,
    /// when resource dictionaries are guaranteed to be available. Falls back to safe defaults for
    /// designer/test contexts where App.Current may be null.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var resources = App.Current?.Resources;

        _runeGlyph.FontFamily = (FontFamily?)resources?["SyxDisplayFont"] ?? FontFamily.Default;
        _eyebrowBlock.FontFamily = (FontFamily?)resources?["SyxTypeEyebrow"] ?? FontFamily.Default;
        _nameBlock.FontFamily = (FontFamily?)resources?["SyxDisplayFont"] ?? FontFamily.Default;
        _nameBlock.Foreground = (IBrush?)resources?["SyxTextBrush"] ?? Brushes.White;

        // Re-apply accent brush if it was set before Loaded
        if (AccentBrush is not null)
        {
            _runeGlyph.Foreground = AccentBrush;
            _eyebrowBlock.Foreground = AccentBrush;
            UpdateRuneTile();
        }

        // Re-apply text properties that may have been set before Loaded
        _runeGlyph.Text = Rune;
        _eyebrowBlock.Text = Eyebrow;
        _nameBlock.Text = Name;
    }

    /// <summary>Add content items (lore, body paragraphs) below the name.</summary>
    public void AddContent(Control child) => _contentSlot.Children.Add(child);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RuneProperty)
            _runeGlyph.Text = Rune;

        if (change.Property == EyebrowProperty)
            _eyebrowBlock.Text = Eyebrow;

        if (change.Property == NameProperty)
            _nameBlock.Text = Name;

        if (change.Property == AccentBrushProperty)
        {
            _runeGlyph.Foreground = AccentBrush;
            _eyebrowBlock.Foreground = AccentBrush;
            UpdateRuneTile();
        }
    }

    private void UpdateRuneTile()
    {
        if (AccentBrush is ISolidColorBrush scb)
        {
            _runeTile.Background = new SolidColorBrush(Color.FromArgb(26, scb.Color.R, scb.Color.G, scb.Color.B));
            var glow = Color.FromArgb(89, scb.Color.R, scb.Color.G, scb.Color.B);
            _runeTile.BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 0, Blur = 22, Color = glow });
        }
    }
}

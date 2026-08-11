using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace SyxxUI.Presentation;

/// <summary>
/// Module header: rune tile + eyebrow + name + content slot.
/// Parity twin of macOS IoDesignSystem.ModuleHeader (SwiftUI) and
/// Linux SyxxUI.Avalonia.Controls.ModuleHeader (Avalonia).
/// </summary>
public sealed class ModuleHeader : UserControl
{
    public static readonly DependencyProperty RuneProperty =
        DependencyProperty.Register(nameof(Rune), typeof(string), typeof(ModuleHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty EyebrowProperty =
        DependencyProperty.Register(nameof(Eyebrow), typeof(string), typeof(ModuleHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register(nameof(ModuleHeader.Name), typeof(string), typeof(ModuleHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(ModuleHeader), new PropertyMetadata(null));

    public string Rune
    {
        get => (string)GetValue(RuneProperty);
        set => SetValue(RuneProperty, value);
    }

    public string Eyebrow
    {
        get => (string)GetValue(EyebrowProperty);
        set => SetValue(EyebrowProperty, value);
    }

    public new string Name
    {
        get => (string)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private readonly Border _runeTile;
    private readonly TextBlock _runeGlyph;
    private readonly TextBlock _eyebrowBlock;
    private readonly TextBlock _nameBlock;
    private readonly StackPanel _contentSlot;

    public ModuleHeader()
    {
        _runeGlyph = new TextBlock
        {
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _runeTile = new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(14),
            VerticalAlignment = VerticalAlignment.Top,
            Child = _runeGlyph,
        };

        _eyebrowBlock = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            CharacterSpacing = 260,
        };

        _nameBlock = new TextBlock
        {
            FontSize = 26,
            Foreground = (Brush)App.Current.Resources["IoTextBrush"],
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
    }

    /// <summary>Add content items (lore, body paragraphs) below the name.</summary>
    public void AddContent(FrameworkElement child) => _contentSlot.Children.Add(child);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);

        if (args.Property == RuneProperty)
            _runeGlyph.Text = Rune;

        if (args.Property == EyebrowProperty)
            _eyebrowBlock.Text = Eyebrow;

        if (args.Property == NameProperty)
            _nameBlock.Text = Name;

        if (args.Property == AccentBrushProperty)
        {
            _runeGlyph.Foreground = AccentBrush;
            _eyebrowBlock.Foreground = AccentBrush;
            UpdateRuneTile();
        }
    }

    private void UpdateRuneTile()
    {
        if (AccentBrush is SolidColorBrush scb)
        {
            _runeTile.Background = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(26, scb.Color.R, scb.Color.G, scb.Color.B));
            _runeTile.BorderBrush = null;
        }
    }
}

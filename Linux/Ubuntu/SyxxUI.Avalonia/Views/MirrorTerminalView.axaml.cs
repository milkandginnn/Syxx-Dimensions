using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SyxxUI.Shared.Mirror;

namespace SyxxUI.Avalonia.Views;

/// <summary>
/// GNOME Terminal-style pane for displaying Mirror bundle content.
/// Flat header bar (no macOS traffic lights), dark background, monospace font,
/// green prompt line, and scrollable content with line numbers.
/// </summary>
public partial class MirrorTerminalView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<MirrorTerminalView, string>(nameof(Title), "Terminal");

    public static readonly StyledProperty<string> DomainProperty =
        AvaloniaProperty.Register<MirrorTerminalView, string>(nameof(Domain), "");

    public static readonly StyledProperty<MirrorBundle?> BundleProperty =
        AvaloniaProperty.Register<MirrorTerminalView, MirrorBundle?>(nameof(Bundle));

    public static readonly StyledProperty<string?> WarningProperty =
        AvaloniaProperty.Register<MirrorTerminalView, string?>(nameof(Warning));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Domain
    {
        get => GetValue(DomainProperty);
        set => SetValue(DomainProperty, value);
    }

    public MirrorBundle? Bundle
    {
        get => GetValue(BundleProperty);
        set => SetValue(BundleProperty, value);
    }

    public string? Warning
    {
        get => GetValue(WarningProperty);
        set => SetValue(WarningProperty, value);
    }

    // L2-020: nullable field references — FindControl may return null if the AXAML template
    // changes. All access sites guard against null before using these fields.
    private TextBlock? _titleLabel;
    private Border? _warningBanner;
    private TextBlock? _warningText;
    private TextBlock? _promptCommand;
    private StackPanel? _bundleContent;
    private StackPanel? _placeholderPanel;

    public MirrorTerminalView()
    {
        InitializeComponent();
        _titleLabel = this.FindControl<TextBlock>("TitleLabel");
        _warningBanner = this.FindControl<Border>("WarningBanner");
        _warningText = this.FindControl<TextBlock>("WarningText");
        _promptCommand = this.FindControl<TextBlock>("PromptCommand");
        _bundleContent = this.FindControl<StackPanel>("BundleContent");
        _placeholderPanel = this.FindControl<StackPanel>("PlaceholderPanel");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
            UpdateTitle();
        else if (change.Property == DomainProperty)
            UpdatePrompt();
        else if (change.Property == BundleProperty)
            UpdateContent();
        else if (change.Property == WarningProperty)
            UpdateWarning();
    }

    private void UpdateTitle()
    {
        if (_titleLabel != null)
            _titleLabel.Text = Title;
    }

    private void UpdatePrompt()
    {
        if (_promptCommand != null)
            _promptCommand.Text = $" curl {Domain}";
    }

    private void UpdateWarning()
    {
        if (_warningBanner == null) return;

        if (!string.IsNullOrEmpty(Warning))
        {
            _warningBanner.IsVisible = true;
            _warningText.Text = Warning;
        }
        else
        {
            _warningBanner.IsVisible = false;
        }
    }

    private void UpdateContent()
    {
        if (_bundleContent == null || _placeholderPanel == null) return;

        _bundleContent.Children.Clear();

        var bundle = Bundle;
        if (bundle == null)
        {
            _placeholderPanel.IsVisible = true;
            return;
        }

        _placeholderPanel.IsVisible = false;

        // Summary line
        var summary = new TextBlock
        {
            Text = bundle.SummaryLine,
            FontFamily = GetMonoFont(),
            FontSize = 10,
            Foreground = GetBrush("SyxTextMutedBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        _bundleContent.Children.Add(summary);

        // HTML section
        if (!string.IsNullOrEmpty(bundle.HtmlContent))
        {
            AddSectionHeader("HTML");
            AddCodeBlock(bundle.HtmlContent);
        }

        // CSS section
        if (!string.IsNullOrEmpty(bundle.CssContent))
        {
            AddSectionHeader("CSS");
            AddCodeBlock(bundle.CssContent);
        }

        // JS section
        if (!string.IsNullOrEmpty(bundle.JsContent))
        {
            AddSectionHeader("JavaScript");
            AddCodeBlock(bundle.JsContent);
        }

        // SHA-256 hash
        if (!string.IsNullOrEmpty(bundle.Metadata.ServerHash))
        {
            var hashPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 8, 0, 0)
            };
            hashPanel.Children.Add(new TextBlock
            {
                Text = "SHA-256:",
                FontFamily = GetMonoFont(),
                FontSize = 10,
                Foreground = GetBrush("SyxTextMutedBrush")
            });
            hashPanel.Children.Add(new TextBlock
            {
                Text = bundle.Metadata.ServerHash.Length > 16
                    ? bundle.Metadata.ServerHash[..16] + "…"
                    : bundle.Metadata.ServerHash,
                FontFamily = GetMonoFont(),
                FontSize = 10,
                Foreground = GetBrush("SyxTextSecondaryBrush")
            });
            _bundleContent.Children.Add(hashPanel);
        }
    }

    private void AddSectionHeader(string name)
    {
        string dashes = new('─', Math.Max(1, 40 - name.Length));
        var header = new TextBlock
        {
            Text = $"── {name} {dashes}",
            FontFamily = GetMonoFont(),
            FontSize = 10,
            FontWeight = FontWeight.Medium,
            Foreground = GetBrush("SyxTextMutedBrush"),
            Margin = new Thickness(0, 10, 0, 4)
        };
        _bundleContent.Children.Add(header);
    }

    private void AddCodeBlock(string text)
    {
        string[] lines = text.Split('\n');
        int maxLines = Math.Min(lines.Length, 500);

        for (int i = 0; i < maxLines; i++)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 1, 0, 0)
            };

            // Line number
            row.Children.Add(new TextBlock
            {
                Text = (i + 1).ToString(),
                FontFamily = GetMonoFont(),
                FontSize = 10,
                Foreground = GetBrush("SyxTextFaintBrush"),
                Width = 36,
                TextAlignment = TextAlignment.Right
            });

            // Line content
            row.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(lines[i]) ? " " : lines[i],
                FontFamily = GetMonoFont(),
                FontSize = 12,
                Foreground = GetBrush("SyxTextBrush")
            });

            _bundleContent.Children.Add(row);
        }

        // Truncation notice
        if (lines.Length > 500)
        {
            _bundleContent.Children.Add(new TextBlock
            {
                Text = $"… ({lines.Length - 500} more lines truncated)",
                FontFamily = GetMonoFont(),
                FontSize = 10,
                Foreground = GetBrush("SyxTextFaintBrush"),
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
    }

    private FontFamily GetMonoFont()
    {
        return (FontFamily?)(App.Current?.Resources["SyxMonoFont"]) ?? FontFamily.Default;
    }

    private IBrush GetBrush(string key)
    {
        return (IBrush?)(App.Current?.Resources[key]) ?? Brushes.Gray;
    }
}

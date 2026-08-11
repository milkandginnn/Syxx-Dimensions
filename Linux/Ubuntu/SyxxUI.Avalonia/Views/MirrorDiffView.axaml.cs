using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SyxxUI.Shared.Mirror;

namespace SyxxUI.Avalonia.Views;

/// <summary>
/// Unified diff display component for Mirror comparisons.
/// Shows line-by-line differences with color-coded additions/removals,
/// expandable sections (HTML / CSS / JavaScript), and line numbers.
/// </summary>
public partial class MirrorDiffView : UserControl
{
    public static readonly StyledProperty<DiffResult?> DiffResultProperty =
        AvaloniaProperty.Register<MirrorDiffView, DiffResult?>(nameof(DiffResult));

    public DiffResult? DiffResult
    {
        get => GetValue(DiffResultProperty);
        set => SetValue(DiffResultProperty, value);
    }

    private readonly HashSet<DiffSection> _expandedSections = new() { DiffSection.Html };

    // L2-020: nullable field references — FindControl may return null if the AXAML template changes.
    // Tab controls
    private TextBlock? _htmlChevron;
    private TextBlock? _cssChevron;
    private TextBlock? _jsChevron;
    private TextBlock? _htmlChangeCount;
    private TextBlock? _cssChangeCount;
    private TextBlock? _jsChangeCount;
    private TextBlock? _additionsCount;
    private TextBlock? _removalsCount;
    private StackPanel? _diffContent;

    // Brushes
    private static readonly SolidColorBrush AddedColor = new(Color.Parse("#2dd4bf"));
    private static readonly SolidColorBrush RemovedColor = new(Color.Parse("#ff4d6d"));
    private static readonly SolidColorBrush ContextColor = new(Color.Parse("#8f83b8"));
    private static readonly SolidColorBrush AddedBg = new(Color.FromArgb(15, 0x2d, 0xd4, 0xbf));
    private static readonly SolidColorBrush RemovedBg = new(Color.FromArgb(15, 0xff, 0x4d, 0x6d));

    public MirrorDiffView()
    {
        InitializeComponent();
        _htmlChevron = this.FindControl<TextBlock>("HtmlChevron");
        _cssChevron = this.FindControl<TextBlock>("CssChevron");
        _jsChevron = this.FindControl<TextBlock>("JsChevron");
        _htmlChangeCount = this.FindControl<TextBlock>("HtmlChangeCount");
        _cssChangeCount = this.FindControl<TextBlock>("CssChangeCount");
        _jsChangeCount = this.FindControl<TextBlock>("JsChangeCount");
        _additionsCount = this.FindControl<TextBlock>("AdditionsCount");
        _removalsCount = this.FindControl<TextBlock>("RemovalsCount");
        _diffContent = this.FindControl<StackPanel>("DiffContent");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DiffResultProperty)
            Rebuild();
    }

    private void OnHtmlTabClick(object? sender, RoutedEventArgs e) => ToggleSection(DiffSection.Html);
    private void OnCssTabClick(object? sender, RoutedEventArgs e) => ToggleSection(DiffSection.Css);
    private void OnJsTabClick(object? sender, RoutedEventArgs e) => ToggleSection(DiffSection.JavaScript);

    private void ToggleSection(DiffSection section)
    {
        if (!_expandedSections.Remove(section))
            _expandedSections.Add(section);
        Rebuild();
    }

    /// <summary>
    /// D337: maximum diff lines to render before truncating. StackPanel rendering creates one UI
    /// element per line; beyond this threshold, memory and layout time degrade noticeably.
    /// A future migration to ItemsRepeater with VirtualizingStackPanel would remove this cap.
    /// </summary>
    private const int MaxRenderedLines = 2_000;

    /// <summary>
    /// Rebuild the diff display. D337: includes a line-count guard that truncates rendering for
    /// very large diffs to prevent UI thread stalls. Sections exceeding MaxRenderedLines show a
    /// truncation notice with the total count.
    /// </summary>
    private void Rebuild()
    {
        var diff = DiffResult;
        if (diff == null || _diffContent == null) return;

        _diffContent.Children.Clear();

        // Null-safe guards for all FindControl results (D338)
        if (_additionsCount != null) _additionsCount.Text = diff.TotalAdditions.ToString();
        if (_removalsCount != null) _removalsCount.Text = diff.TotalRemovals.ToString();

        // Update tab change counts and chevrons
        UpdateTab(DiffSection.Html, _htmlChevron, _htmlChangeCount, diff.HtmlDiff);
        UpdateTab(DiffSection.Css, _cssChevron, _cssChangeCount, diff.CssDiff);
        UpdateTab(DiffSection.JavaScript, _jsChevron, _jsChangeCount, diff.JsDiff);

        // Render expanded sections
        foreach (var section in new[] { DiffSection.Html, DiffSection.Css, DiffSection.JavaScript })
        {
            if (!_expandedSections.Contains(section)) continue;
            var lines = diff.LinesFor(section);
            if (lines.Count == 0)
            {
                _diffContent.Children.Add(new TextBlock
                {
                    Text = $"No differences in {SectionName(section)}",
                    FontFamily = GetMonoFont(),
                    FontSize = 11,
                    Foreground = ContextColor,
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }
            else if (lines.Count > MaxRenderedLines)
            {
                // D337: truncate large diffs to prevent UI stalls. Show a notice with total count.
                RenderDiffLines(lines.Take(MaxRenderedLines).ToList());
                _diffContent.Children.Add(new TextBlock
                {
                    Text = $"… ({lines.Count - MaxRenderedLines} more lines truncated — {lines.Count} total in {SectionName(section)})",
                    FontFamily = GetMonoFont(),
                    FontSize = 10,
                    Foreground = ContextColor,
                    Margin = new Thickness(0, 4, 0, 8)
                });
            }
            else
            {
                RenderDiffLines(lines);
            }
        }

        // No changes message
        if (!diff.HasChanges)
        {
            var noChanges = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 16)
            };
            noChanges.Children.Add(new TextBlock
            {
                Text = "✓",
                Foreground = AddedColor,
                FontSize = 14
            });
            noChanges.Children.Add(new TextBlock
            {
                Text = "No differences detected — remote and local content are identical.",
                FontFamily = GetMonoFont(),
                FontSize = 12,
                Foreground = AddedColor
            });
            _diffContent.Children.Add(noChanges);
        }
    }

    /// <summary>
    /// D338: null-safe UpdateTab — chevron and changeCount may be null if FindControl failed.
    /// </summary>
    private void UpdateTab(DiffSection section, TextBlock? chevron, TextBlock? changeCount, IReadOnlyList<DiffLine> lines)
    {
        bool expanded = _expandedSections.Contains(section);
        if (chevron != null) chevron.Text = expanded ? "▾" : "▸";

        if (changeCount == null) return;

        int changes = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != DiffLineKind.Context) changes++;
        }

        if (changes > 0)
        {
            changeCount.IsVisible = true;
            changeCount.Text = $"({changes})";
            changeCount.Foreground = RemovedColor;
        }
        else
        {
            changeCount.IsVisible = false;
        }
    }

    private void RenderDiffLines(IReadOnlyList<DiffLine> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                Margin = new Thickness(0, 1, 0, 0),
                Background = BackgroundFor(line.Kind)
            };

            // Old line number
            row.Children.Add(new TextBlock
            {
                Text = line.OldLineNumber?.ToString() ?? "",
                FontFamily = GetMonoFont(),
                FontSize = 10,
                Foreground = ThemeBrush("SyxTextFaintBrush", "#6f6597"),
                Width = 36,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            });

            // New line number
            row.Children.Add(new TextBlock
            {
                Text = line.NewLineNumber?.ToString() ?? "",
                FontFamily = GetMonoFont(),
                FontSize = 10,
                Foreground = ThemeBrush("SyxTextFaintBrush", "#6f6597"),
                Width = 36,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 8, 0)
            });

            // Kind indicator
            row.Children.Add(new TextBlock
            {
                Text = PrefixFor(line.Kind),
                FontFamily = GetMonoFont(),
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = ColorFor(line.Kind),
                Width = 14
            });

            // Content
            row.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(line.Text) ? " " : line.Text,
                FontFamily = GetMonoFont(),
                FontSize = 12,
                Foreground = ColorFor(line.Kind)
            });

            _diffContent.Children.Add(row);
        }
    }

    private static string PrefixFor(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Context => " ",
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "−",
        _ => " "
    };

    private static IBrush ColorFor(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Context => ContextColor,
        DiffLineKind.Added => AddedColor,
        DiffLineKind.Removed => RemovedColor,
        _ => ContextColor
    };

    private static IBrush? BackgroundFor(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => AddedBg,
        DiffLineKind.Removed => RemovedBg,
        _ => null
    };

    private static string SectionName(DiffSection section) => section switch
    {
        DiffSection.Html => "HTML",
        DiffSection.Css => "CSS",
        DiffSection.JavaScript => "JavaScript",
        _ => section.ToString()
    };

    private FontFamily GetMonoFont()
    {
        return (FontFamily?)(App.Current?.Resources["SyxMonoFont"]) ?? FontFamily.Default;
    }

    /// <summary>
    /// Resolve a theme-aware brush from the application resource dictionary.
    /// Falls back to <paramref name="fallback"/> when resources are not yet loaded (designer / tests).
    /// </summary>
    private static IBrush ThemeBrush(string resourceKey, string fallback)
    {
        if (App.Current?.Resources.TryGetResource(resourceKey, null, out var res) == true && res is IBrush brush)
            return brush;
        return new SolidColorBrush(Color.Parse(fallback));
    }
}

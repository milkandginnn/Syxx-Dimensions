using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SyxxUI.Mirror;

/// <summary>
/// Unified diff display component for Mirror comparisons.
///
/// Shows line-by-line differences with:
/// - Green (#2dd4bf) for added lines
/// - Red (#ff4d6d) for removed lines
/// - Context lines in muted color
/// - Line numbers
/// - Expandable sections: HTML | CSS | JavaScript
/// </summary>
public sealed partial class MirrorDiffView : UserControl
{
    private static readonly Color AddedColor = Color.FromArgb(0xFF, 0x2D, 0xD4, 0xBF);    // #2dd4bf
    private static readonly Color RemovedColor = Color.FromArgb(0xFF, 0xFF, 0x4D, 0x6D);  // #ff4d6d
    private static readonly Color AddedBgColor = Color.FromArgb(0x0F, 0x2D, 0xD4, 0xBF);  // 6% opacity
    private static readonly Color RemovedBgColor = Color.FromArgb(0x0F, 0xFF, 0x4D, 0x6D); // 6% opacity

    private DiffResult? _diffResult;
    private readonly HashSet<DiffSection> _expandedSections = new() { DiffSection.Html };

    public MirrorDiffView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Populate the diff view with a diff result.
    /// </summary>
    public void Populate(DiffResult diffResult)
    {
        _diffResult = diffResult;

        // Update summary counts
        AdditionsCount.Text = diffResult.TotalAdditions.ToString();
        RemovalsCount.Text = diffResult.TotalRemovals.ToString();

        // Update tab counts
        UpdateTabCount(HtmlCount, diffResult.HtmlDiff);
        UpdateTabCount(CssCount, diffResult.CssDiff);
        UpdateTabCount(JsCount, diffResult.JsDiff);

        // Render expanded sections
        RenderContent();
    }

    private void UpdateTabCount(TextBlock countText, List<DiffLine> lines)
    {
        int changes = 0;
        foreach (var line in lines)
        {
            if (line.Kind != DiffLineKind.Context) changes++;
        }
        countText.Text = changes > 0 ? $"({changes})" : "";
        countText.Foreground = changes > 0
            ? new SolidColorBrush(RemovedColor)
            : (Brush)Application.Current.Resources["IoTextFaintBrush"];
    }

    private void RenderContent()
    {
        DiffContentPanel.Children.Clear();

        if (_diffResult is null) return;

        bool anyExpanded = false;

        if (_expandedSections.Contains(DiffSection.Html))
        {
            RenderSection("HTML", _diffResult.HtmlDiff);
            anyExpanded = true;
        }

        if (_expandedSections.Contains(DiffSection.Css))
        {
            RenderSection("CSS", _diffResult.CssDiff);
            anyExpanded = true;
        }

        if (_expandedSections.Contains(DiffSection.JavaScript))
        {
            RenderSection("JavaScript", _diffResult.JsDiff);
            anyExpanded = true;
        }

        if (!_diffResult.HasChanges)
        {
            // No changes view
            var noChanges = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 16)
            };
            noChanges.Children.Add(new TextBlock
            {
                Text = "✓",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["IoSuccessGreenBrush"]
            });
            noChanges.Children.Add(new TextBlock
            {
                Text = "No differences detected — remote and local content are identical.",
                FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["IoSuccessGreenBrush"]
            });
            DiffContentPanel.Children.Add(noChanges);
        }
        else if (!anyExpanded)
        {
            var hint = new TextBlock
            {
                Text = "Click a section tab above to expand its diff.",
                FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
                Margin = new Thickness(0, 16, 0, 16)
            };
            DiffContentPanel.Children.Add(hint);
        }

        // Update chevrons
        HtmlChevron.Text = _expandedSections.Contains(DiffSection.Html) ? "▼" : "▶";
        CssChevron.Text = _expandedSections.Contains(DiffSection.Css) ? "▼" : "▶";
        JsChevron.Text = _expandedSections.Contains(DiffSection.JavaScript) ? "▼" : "▶";
    }

    private void RenderSection(string name, List<DiffLine> lines)
    {
        if (lines.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = $"No differences in {name}",
                FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
                Margin = new Thickness(0, 8, 0, 8)
            };
            DiffContentPanel.Children.Add(empty);
            return;
        }

        foreach (var line in lines)
        {
            DiffContentPanel.Children.Add(CreateDiffLineRow(line));
        }
    }

    private UIElement CreateDiffLineRow(DiffLine line)
    {
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Background color based on line kind
        Color bgColor = line.Kind switch
        {
            DiffLineKind.Added => AddedBgColor,
            DiffLineKind.Removed => RemovedBgColor,
            _ => Colors.Transparent
        };
        if (bgColor != Colors.Transparent)
        {
            row.Background = new SolidColorBrush(bgColor);
        }

        // Foreground color
        Color fgColor = line.Kind switch
        {
            DiffLineKind.Added => AddedColor,
            DiffLineKind.Removed => RemovedColor,
            _ => ((Color)Application.Current.Resources["IoTextFaintColor"])
        };
        var fgBrush = new SolidColorBrush(fgColor);
        var monoFont = (FontFamily)Application.Current.Resources["IoFontMono"];

        // Old line number
        var oldNum = new TextBlock
        {
            Text = line.OldLineNumber?.ToString() ?? "",
            FontFamily = monoFont,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(oldNum, 0);
        row.Children.Add(oldNum);

        // New line number
        var newNum = new TextBlock
        {
            Text = line.NewLineNumber?.ToString() ?? "",
            FontFamily = monoFont,
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(newNum, 2);
        row.Children.Add(newNum);

        // Kind indicator (+ / − / space)
        string prefix = line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "−",
            _ => " "
        };
        var kindIndicator = new TextBlock
        {
            Text = prefix,
            FontFamily = monoFont,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = fgBrush,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(kindIndicator, 4);
        row.Children.Add(kindIndicator);

        // Content text
        var content = new TextBlock
        {
            Text = string.IsNullOrEmpty(line.Text) ? " " : line.Text,
            FontFamily = monoFont,
            FontSize = 12,
            Foreground = fgBrush,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(content, 5);
        row.Children.Add(content);

        return row;
    }

    private void ToggleSection(DiffSection section)
    {
        if (_expandedSections.Contains(section))
            _expandedSections.Remove(section);
        else
            _expandedSections.Add(section);
        RenderContent();
    }

    private void HtmlTab_Click(object sender, RoutedEventArgs e) => ToggleSection(DiffSection.Html);
    private void CssTab_Click(object sender, RoutedEventArgs e) => ToggleSection(DiffSection.Css);
    private void JsTab_Click(object sender, RoutedEventArgs e) => ToggleSection(DiffSection.JavaScript);
}

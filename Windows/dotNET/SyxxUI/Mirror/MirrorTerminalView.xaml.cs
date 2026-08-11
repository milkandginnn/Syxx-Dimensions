using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyxxUI.Shared.Mirror;

namespace SyxxUI.Mirror;

/// <summary>
/// Windows Terminal / PowerShell-style pane for displaying Mirror bundle content.
///
/// Renders:
/// - Title bar tab with close button (Windows Terminal chrome)
/// - Dark background (#0c0c0c Windows Terminal dark)
/// - Monospace font (Cascadia Code / Consolas 12pt via JetBrains Mono)
/// - Prompt: PS C:\Users\user> in gold (#e5c07b)
/// - Scrollable content with line numbers
/// </summary>
public sealed partial class MirrorTerminalView : UserControl
{
    /// <summary>The maximum number of lines to display per section before truncation.</summary>
    private const int MaxLinesPerSection = 500;

    public MirrorTerminalView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the close button is clicked.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Populate the terminal pane with bundle data.
    /// </summary>
    /// <param name="title">Title shown in the tab (e.g. "Remote (Server)" or "Local (Your PC)").</param>
    /// <param name="domain">The domain that was fetched (shown in the prompt line).</param>
    /// <param name="bundle">The bundle to display. If null, shows a placeholder.</param>
    /// <param name="warning">Optional warning to display (G-02 fallback).</param>
    /// <param name="showCloseButton">Whether to show the close button in the tab bar.</param>
    public void Populate(string title, string domain, MirrorBundle? bundle,
        string? warning = null, bool showCloseButton = false)
    {
        TitleText.Text = title;
        CloseBtn.Visibility = showCloseButton ? Visibility.Visible : Visibility.Collapsed;

        // Warning banner
        if (!string.IsNullOrEmpty(warning))
        {
            WarningBanner.Visibility = Visibility.Visible;
            WarningText.Text = warning;
        }
        else
        {
            WarningBanner.Visibility = Visibility.Collapsed;
        }

        // Prompt command
        PromptCommandText.Text = $"Invoke-SyxxMirror -Domain {(string.IsNullOrEmpty(domain) ? "<domain>" : domain)}";

        // Clear previous sections
        SectionsPanel.Children.Clear();

        if (bundle is not null)
        {
            // Summary line
            SummaryText.Text = bundle.SummaryLine;
            SummaryText.Visibility = Visibility.Visible;
            PlaceholderText.Visibility = Visibility.Collapsed;

            // HTML section
            if (!string.IsNullOrEmpty(bundle.HtmlContent))
            {
                AddSection("HTML", bundle.HtmlContent);
            }

            // CSS section
            if (!string.IsNullOrEmpty(bundle.CssContent))
            {
                AddSection("CSS", bundle.CssContent);
            }

            // JavaScript section
            if (!string.IsNullOrEmpty(bundle.JsContent))
            {
                AddSection("JavaScript", bundle.JsContent);
            }

            // SHA-256 hash
            if (!string.IsNullOrEmpty(bundle.Metadata.ServerHash))
            {
                var hashPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
                hashPanel.Children.Add(new TextBlock
                {
                    Text = "SHA-256:",
                    FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"]
                });
                hashPanel.Children.Add(new TextBlock
                {
                    Text = bundle.Metadata.ServerHash[..Math.Min(16, bundle.Metadata.ServerHash.Length)] + "…",
                    FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                    FontSize = 10,
                    Foreground = (Brush)Application.Current.Resources["IoTextSecondaryBrush"]
                });
                SectionsPanel.Children.Add(hashPanel);
            }
        }
        else
        {
            SummaryText.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddSection(string name, string content)
    {
        // Section header: ── HTML ────────────────────────────
        string dashes = new('─', Math.Max(1, 40 - name.Length));
        var header = new TextBlock
        {
            Text = $"── {name} {dashes}",
            FontFamily = (FontFamily)Application.Current.Resources["IoFontMonoBold"],
            FontSize = 10,
            Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
            Margin = new Thickness(0, 10, 0, 4)
        };
        SectionsPanel.Children.Add(header);

        // Code block with line numbers
        var lines = content.Split('\n');
        int displayCount = Math.Min(lines.Length, MaxLinesPerSection);

        for (int i = 0; i < displayCount; i++)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lineNum = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(lineNum, 0);
            row.Children.Add(lineNum);

            var codeLine = new TextBlock
            {
                Text = string.IsNullOrEmpty(lines[i]) ? " " : lines[i],
                FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["IoTextBrush"],
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(codeLine, 2);
            row.Children.Add(codeLine);

            SectionsPanel.Children.Add(row);
        }

        // Truncation notice
        if (lines.Length > MaxLinesPerSection)
        {
            var truncNotice = new TextBlock
            {
                Text = $"… ({lines.Length - MaxLinesPerSection} more lines truncated)",
                FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["IoTextFaintBrush"],
                Margin = new Thickness(0, 2, 0, 0)
            };
            SectionsPanel.Children.Add(truncNotice);
        }
    }
}

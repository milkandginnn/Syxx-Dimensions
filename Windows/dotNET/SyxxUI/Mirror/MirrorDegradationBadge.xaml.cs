using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SyxxUI.Mirror;

/// <summary>
/// Badge displaying the degradation score with color-coded classification.
///
/// Color coding (matching macOS MirrorDegradationBadge):
/// - Identical: green (IoSuccessGreen #2dd4bf)
/// - Minor: green
/// - Moderate: amber (IoWarningAmber #f7931e)
/// - Significant: orange (IoHighOrange #ff7a1a)
/// - Critical: red (IoCriticalRed #ff4d6d)
/// </summary>
public sealed partial class MirrorDegradationBadge : UserControl
{
    private static readonly Color GreenColor = Color.FromArgb(0xFF, 0x2D, 0xD4, 0xBF);
    private static readonly Color AmberColor = Color.FromArgb(0xFF, 0xF7, 0x93, 0x1E);
    private static readonly Color OrangeColor = Color.FromArgb(0xFF, 0xFF, 0x7A, 0x1A);
    private static readonly Color RedColor = Color.FromArgb(0xFF, 0xFF, 0x4D, 0x6D);

    public MirrorDegradationBadge()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Update the badge with a degradation score.
    /// </summary>
    public void Populate(DegradationScore score)
    {
        Color fg = GetForegroundColor(score.Bucket);
        var fgBrush = new SolidColorBrush(fg);

        // Background: foreground at 10% opacity
        Color bgColor = Color.FromArgb(0x19, fg.R, fg.G, fg.B);
        BadgeBorder.Background = new SolidColorBrush(bgColor);

        // Border: foreground at 30% opacity
        Color borderColor = Color.FromArgb(0x4C, fg.R, fg.G, fg.B);
        BadgeBorder.BorderBrush = new SolidColorBrush(borderColor);

        // Ring background: foreground at 35% opacity
        Color ringBgColor = Color.FromArgb(0x59, fg.R, fg.G, fg.B);
        BgRing.Stroke = new SolidColorBrush(ringBgColor);

        // Percentage text
        PercentageText.Text = $"{score.Percentage}%";
        PercentageText.Foreground = fgBrush;

        // Classification label
        ClassificationText.Text = DegradationScore.LabelFor(score.Bucket);
        ClassificationText.Foreground = fgBrush;
    }

    private static Color GetForegroundColor(DegradationScore.Classification bucket) => bucket switch
    {
        DegradationScore.Classification.Identical => GreenColor,
        DegradationScore.Classification.MinorDifferences => GreenColor,
        DegradationScore.Classification.ModerateDifferences => AmberColor,
        DegradationScore.Classification.SignificantDifferences => OrangeColor,
        DegradationScore.Classification.CriticalDifferences => RedColor,
        _ => GreenColor
    };
}

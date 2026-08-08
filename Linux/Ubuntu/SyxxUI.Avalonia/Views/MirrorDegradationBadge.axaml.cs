using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using SyxxUI.Avalonia.Mirror;

namespace SyxxUI.Avalonia.Views;

/// <summary>
/// Badge displaying the degradation score with color-coded classification.
///
/// Color coding:
/// - Identical: green (#2dd4bf)
/// - Minor: green (#2dd4bf)
/// - Moderate: amber (#F2B33D)
/// - Significant: orange (#f7931e)
/// - Critical: red (#ff4d6d)
/// </summary>
public partial class MirrorDegradationBadge : UserControl
{
    public static readonly StyledProperty<DegradationScore?> ScoreProperty =
        AvaloniaProperty.Register<MirrorDegradationBadge, DegradationScore?>(nameof(Score));

    public DegradationScore? Score
    {
        get => GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }

    private Border _badgeBorder = null!;
    private Border _scoreCircle = null!;
    private TextBlock _scorePercent = null!;
    private TextBlock _classificationLabel = null!;

    public MirrorDegradationBadge()
    {
        InitializeComponent();
        _badgeBorder = this.FindControl<Border>("BadgeBorder")!;
        _scoreCircle = this.FindControl<Border>("ScoreCircle")!;
        _scorePercent = this.FindControl<TextBlock>("ScorePercent")!;
        _classificationLabel = this.FindControl<TextBlock>("ClassificationLabel")!;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScoreProperty)
            UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var score = Score;
        if (score == null || _scorePercent == null) return;

        _scorePercent.Text = $"{score.Percentage}%";
        _classificationLabel.Text = DegradationScore.Label(score.Bucket);

        var fg = ForegroundFor(score.Bucket);
        var bg = BackgroundFor(score.Bucket);

        _scorePercent.Foreground = fg;
        _classificationLabel.Foreground = fg;
        _scoreCircle.BorderBrush = bg;
        _badgeBorder.Background = new SolidColorBrush(Color.FromArgb(26, fg.Color.R, fg.Color.G, fg.Color.B));
        _badgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(77, fg.Color.R, fg.Color.G, fg.Color.B));
    }

    private static SolidColorBrush ForegroundFor(DegradationScore.Classification c) => c switch
    {
        DegradationScore.Classification.Identical => new(Color.Parse("#2dd4bf")),
        DegradationScore.Classification.MinorDifferences => new(Color.Parse("#2dd4bf")),
        DegradationScore.Classification.ModerateDifferences => new(Color.Parse("#F2B33D")),
        DegradationScore.Classification.SignificantDifferences => new(Color.Parse("#f7931e")),
        DegradationScore.Classification.CriticalDifferences => new(Color.Parse("#ff4d6d")),
        _ => new(Color.Parse("#2dd4bf"))
    };

    private static SolidColorBrush BackgroundFor(DegradationScore.Classification c)
    {
        var fg = ForegroundFor(c);
        return new SolidColorBrush(Color.FromArgb(89, fg.Color.R, fg.Color.G, fg.Color.B));
    }
}

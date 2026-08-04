using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Presentation;   // linked from Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs
using SyxxUI.Avalonia.Presentation;

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// The A4 five-minute first-run scan, surfaced on Linux. One button → the no-configuration
/// disciplines → a real graded result, rendered through the SHARED honesty layer
/// (<see cref="ReportCards"/> / <see cref="ResultActions"/>), never re-derived here.
///
/// HONESTY CONTRACT (the reason this view exists at all):
///  • Nothing is rendered before the scan completes — no pre-populated grade, no placeholder result.
///  • The grade, headline, and reasons come from <c>ReportCards.For</c>; this view does not invent them.
///  • Colour comes from <see cref="SeverityColor"/>: clean is the ONLY green. Inconclusive is amber
///    and Unavailable is neutral, so an unmeasured network can never look safe.
///  • The Stage-5 block renders only when the engine's DTO is present AND §13-valid; otherwise the
///    view says so plainly rather than showing a fabricated preview.
/// </summary>
public partial class ConclusionsViewModel : ObservableObject
{
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _progressText = "";

    // Report card (A3)
    [ObservableProperty] private string _gradeLetter = "";
    [ObservableProperty] private string _gradeLabel = "";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private IBrush _severityBrush = Brushes.Gray;
    public System.Collections.ObjectModel.ObservableCollection<string> TopReasons { get; } = new();

    // Result → action (A1) and plain explanation (A2)
    [ObservableProperty] private string _meaning = "";
    [ObservableProperty] private string _primaryAction = "";
    [ObservableProperty] private string _narrative = "";

    // Stage-5 honesty surface
    [ObservableProperty] private bool _hasStage5;
    [ObservableProperty] private string _stage5Headline = "";
    [ObservableProperty] private string _confidenceText = "";
    [ObservableProperty] private string _completenessText = "";
    [ObservableProperty] private string _stage5Unavailable = "";
    public System.Collections.ObjectModel.ObservableCollection<string> HonestyLines { get; } = new();

    // Coverage: which checks actually ran, and which did not.
    public System.Collections.ObjectModel.ObservableCollection<string> Ran { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<string> Skipped { get; } = new();

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        HasResult = false;
        TopReasons.Clear(); HonestyLines.Clear(); Ran.Clear(); Skipped.Clear();
        ProgressText = "Starting…";

        var progress = new Progress<string>(s => ProgressText = s);
        FirstRunResult result;
        try
        {
            result = await FirstRunScan.RunAsync(progress: progress);
        }
        catch (Exception ex)
        {
            // A crashed scan is an unmeasured network, not a clean one.
            // Defect-2 parity (macOS SyxxApp/ErrorPresenter.swift, linked here as
            // Presentation/ErrorPresenter.cs): the full technical detail goes to the diagnostic
            // sink; the screen gets readable text, or the bare failure when the exception carried no
            // cause we can honestly state.
            ProgressText = ErrorPresenter.Compose(
                "The scan could not complete.", ErrorPresenter.UserDetail(ex, "conclusions.scan"));
            IsScanning = false;
            return;
        }

        var card = result.Card;
        GradeLetter = card.Grade == Grade.Ungraded ? "—" : card.Grade.ToString();
        GradeLabel = card.GradeLabel;
        Headline = card.Headline;
        foreach (var r in card.TopReasons) TopReasons.Add(r);

        // Severity → colour through the shared mapper. Ungraded/inconclusive can never be green.
        // Passed straight through: SeverityColor now takes the Kit severity, so Info/Warning/High
        // keep their distinct tones instead of being flattened into one amber.
        SeverityBrush = SeverityColor.For(card.Severity);

        var action = ResultActions.For(result.Conclusion);
        Meaning = action.Meaning;
        PrimaryAction = action.PrimaryAction;
        Narrative = PlainExplanations.For(result.Conclusion).Narrative;

        // Matrix engine: the retired DST Stage5 pipeline is gone.
        // The Determination carries the structured reasoning the app surfaces.
        var det = result.Conclusion.Determination;
        if (det is null)
        {
            HasStage5 = false;
            Stage5Unavailable = "Detailed reasoning is unavailable for this run. That is a gap in the "
                              + "evidence, not a clean result.";
        }
        else
        {
            HasStage5 = true;
            Stage5Headline = det.Explanation;
            ConfidenceText = det.State.ToString();
            CompletenessText = "";
            foreach (var d in det.Diagnoses)
                HonestyLines.Add($"{d.DisplayName}: {d.Confidence}");
            foreach (var s in det.Suppressed)
                HonestyLines.Add($"({s.Reason}) {s.DisplayName}");
        }

        foreach (var r in result.Ran) Ran.Add(r);
        foreach (var s in result.Skipped) Skipped.Add(s);

        ProgressText = "";
        HasResult = true;
        IsScanning = false;
    }
}

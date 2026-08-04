using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyxxUI.Presentation;
using SyxxUI.ViewModels;
using Syxx.Kit;

namespace SyxxUI.Views;

public sealed partial class ConclusionsView : Page
{
    private AppViewModel? _vm;

    public ConclusionsView()
    {
        InitializeComponent();
    }

    // E155: subscribe to PropertyChanged so the pane refreshes when data changes while visible.
    private System.ComponentModel.PropertyChangedEventHandler? _pcHandler;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null)
        {
            _pcHandler = (_, _) => Refresh();
            _vm.PropertyChanged += _pcHandler;
        }
        Refresh();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_vm is not null && _pcHandler is not null)
            _vm.PropertyChanged -= _pcHandler;
        base.OnNavigatedFrom(e);
    }

    private void Refresh()
    {
        if (_vm is null) return;

        var conclusion = _vm.Conclusion;
        if (conclusion is not null)
        {
            ResultPanel.Visibility = Visibility.Visible;
            EvidencePanel.Visibility = Visibility.Collapsed;
            SynthesizeBtn.Visibility = Visibility.Collapsed;

            // Show the matrix engine's result state. No confidence score exists — the DST-era
            // synthesized confidence was removed (owner directive 2026-07-21).
            ResultText.Text = $"Conclusion: {conclusion.Determination?.State.ToString() ?? "Unknown"}";

            // A3 — the glanceable report card. Ungraded renders "—" in neutral, never a letter.
            RenderReportCard(ReportCards.For(conclusion));

            // A1 — the "so what": plain meaning + primary action, honesty-gated in the Kit.
            var va = ResultActions.For(conclusion);
            ActionMeaningText.Text = va.Meaning;
            ActionStepText.Text = va.PrimaryAction;
            ActionCard.Visibility = Visibility.Visible;

            // A2 — plain-language "why we think so" over the DST synthesis.
            ExplanationText.Text = PlainExplanations.For(conclusion).Narrative;
            ExplanationCard.Visibility = Visibility.Visible;

            RenderStage5(conclusion.Determination);
        }
        else
        {
            ReportCardBorder.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            EvidencePanel.Visibility = Visibility.Visible;
            SynthesizeBtn.Visibility = Visibility.Visible;

            // Collapse all component cards and clear their text before population pass.
            // This ensures stale data from a previous run doesn't remain visible after Reset.
            ProphetCard.Visibility = Visibility.Collapsed;
            ProphetDetail.Text = "";
            MirrorCard.Visibility = Visibility.Collapsed;
            MirrorDetail.Text = "";
            WarpCard.Visibility = Visibility.Collapsed;
            WarpDetail.Text = "";
            PortalCard.Visibility = Visibility.Collapsed;
            PortalDetail.Text = "";
            GluttonCard.Visibility = Visibility.Collapsed;
            GluttonDetail.Text = "";
            CageCard.Visibility = Visibility.Collapsed;
            CageDetail.Text = "";
            WitnessCard.Visibility = Visibility.Collapsed;
            WitnessDetail.Text = "";
            ActionCard.Visibility = Visibility.Collapsed;
            ExplanationCard.Visibility = Visibility.Collapsed;
            Stage5Card.Visibility = Visibility.Collapsed;

            var inputs = _vm.SectionInputs;
            bool anyData = false;

            // Prophet
            if (inputs.Resolution is not null)
            {
                ProphetCard.Visibility = Visibility.Visible;
                anyData = true;
                var detail = $"Records: {inputs.Resolution.Records.Count}  |  Status: {inputs.Resolution.Status}";
                if (inputs.Dnssec is not null)
                    detail += $"\nDNSSEC: {DNSSECVerifier.Describe(inputs.Dnssec.Value)}";
                ProphetDetail.Text = detail;
            }

            // Mirror comparison
            if (inputs.Comparison is not null)
            {
                MirrorCard.Visibility = Visibility.Visible;
                anyData = true;
                var result = inputs.Comparison.IsRedirectionDetected ? "TAMPERING DETECTED"
                            : inputs.Comparison.IsInconclusive ? "INCONCLUSIVE"
                            : inputs.Comparison.IsDegraded ? "RESOLVERS AGREE (DEGRADED)"
                            : "RESOLVERS AGREE";
                MirrorDetail.Text = $"Conclusion: {result}";
            }

            // Mirror warp
            if (inputs.Warp is not null)
            {
                WarpCard.Visibility = Visibility.Visible;
                anyData = true;
                var result = inputs.Warp.IsInconclusive ? "INCONCLUSIVE"
                            : inputs.Warp.IsSecure ? "CONTENT INTACT"
                            : "TAMPERING DETECTED";
                WarpDetail.Text = $"Conclusion: {result}";
            }

            // Portal
            if (inputs.Network is not null)
            {
                PortalCard.Visibility = Visibility.Visible;
                anyData = true;
                var vpnLabel = inputs.Network.Vpn.State == ConditionState.Yes
                    ? $"Active ({string.Join(", ", inputs.Network.Vpn.TunnelInterfaces)})"
                    : inputs.Network.Vpn.State == ConditionState.No ? "Not detected" : "Unknown";
                var dnsLabel = inputs.Network.EncryptedDNS.State == ConditionState.Yes ? "Encrypted"
                            : inputs.Network.EncryptedDNS.State == ConditionState.No ? "Unencrypted"
                            : "Unknown";
                PortalDetail.Text = $"VPN: {vpnLabel}\nEncrypted DNS: {dnsLabel}\nDNS Servers: {string.Join(", ", inputs.Network.DnsServers)}";
            }

            // Glutton
            if (inputs.Leak is not null)
            {
                GluttonCard.Visibility = Visibility.Visible;
                anyData = true;
                var detail = $"Conclusion: {inputs.Leak.Result.ToString().ToUpperInvariant()}";
                if (inputs.Leak.ObservedResolvers.Count > 0)
                    detail += $"\nObserved Resolvers: {string.Join(", ", inputs.Leak.ObservedResolvers)}";
                // Defect-2 parity (macOS AppModel.swift:858): ErrorReason may have been built from an
                // exception message in the core (Syxx.Kit/LeakDetector.cs:207,271). Append it only
                // when it is genuine human-readable text — the CONCLUSION line above already states
                // the outcome, so dropping a crash-reporter fragment loses nothing the user needs.
                if (ErrorPresenter.DisplayReason(inputs.Leak.ErrorReason) is { } leakNote)
                    detail += $"\nNote: {leakNote}";
                GluttonDetail.Text = detail;
            }

            // Cage
            if (inputs.Exfil is not null && inputs.Exfil.Count > 0)
            {
                CageCard.Visibility = Visibility.Visible;
                anyData = true;
                var suspicious = inputs.Exfil.Count(a =>
                    a.Suspicion == ExfilSuspicion.Suspicious || a.Suspicion == ExfilSuspicion.LikelyExfil);
                var detail = $"Domains Captured: {inputs.Exfil.Count}";
                if (suspicious > 0)
                    detail += $"\nSuspicious: {suspicious} domain(s) flagged";
                CageDetail.Text = detail;
            }

            // Witness
            if (inputs.Drifts is not null && inputs.Drifts.Count > 0)
            {
                WitnessCard.Visibility = Visibility.Visible;
                anyData = true;
                var detail = $"Drifts Detected: {inputs.Drifts.Count}";
                foreach (var drift in inputs.Drifts.Take(3))
                    detail += $"\n{drift.Domain}: {drift.PreviousAnswer} \u2192 {drift.NewAnswer}";
                if (inputs.Drifts.Count > 3)
                    detail += $"\n... and {inputs.Drifts.Count - 3} more";
                WitnessDetail.Text = detail;
            }

            EmptyStateText.Visibility = anyData ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void SynthesizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        // GenerateSynthesis is a synchronous [RelayCommand] (void), so its generated
        // command is IRelayCommand. Use Execute, not ExecuteAsync (which lives on
        // IAsyncRelayCommand).
        _vm.GenerateSynthesisCommand.Execute(null);
        Refresh();
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ResetAllCommand.Execute(null);
        Refresh();
    }

    // A4/D-1 first-run scan (macOS ConclusionsView.runFirstScan twin). Kit-level orchestration;
    // the result's evidence is merged into the session so the component cards and any later
    // manual synthesis reflect it. try/catch because an unhandled exception in an async void
    // handler kills the process.
    private bool _scanRunning;
    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _scanRunning) return;
        _scanRunning = true;
        ScanBtn.Visibility = Visibility.Collapsed;
        ScanProgressRow.Visibility = Visibility.Visible;
        ScanProgressRing.IsActive = true;
        ScanProgressText.Text = "Scanning…";
        try
        {
            // Progress<string> is created on the UI thread, so reports marshal back to it.
            var progress = new System.Progress<string>(msg => ScanProgressText.Text = msg);
            var result = await FirstRunScan.RunAsync(_vm.Domain, isPro: _vm.IsPro, progress: progress);
            _vm.SectionInputs = result.Inputs;
            _vm.Conclusion = result.Conclusion;
        }
        catch (System.Exception ex)
        {
            // A failed scan is reported, never a silent reset to the idle button (no fake clean).
            // Defect-2 parity (macOS ErrorPresenter.swift): the raw exception message is routed to
            // the diagnostic sink, and only a sentence a person can read reaches this label. When
            // the exception carries no readable cause the user sees the bare failure — an honest
            // "this failed and we do not yet know why" rather than a crash-reporter fragment.
            // The base sentence is the EXISTING approved Windows string; only the detail is now
            // conditional. (Windows says "Scan failed" where Ubuntu says "The scan could not
            // complete" — an unresolved wording divergence, flagged for the owner, not silently
            // changed here.)
            ScanProgressText.Text = ErrorPresenter.Compose(
                "Scan failed.", ErrorPresenter.UserDetail(ex, "conclusions.scan"));
            ScanProgressRing.IsActive = false;
            _scanRunning = false;
            return;
        }
        ScanProgressRing.IsActive = false;
        ScanProgressRow.Visibility = Visibility.Collapsed;
        ScanBtn.Visibility = Visibility.Visible;
        _scanRunning = false;
        Refresh();
    }

    // MARK: - Stage-5 reasoning surface (parity twin of macOS Stage5ReasoningView)

    /// A3 — render the report card. The grade colour follows the SEVERITY of the real result, and
    /// an Ungraded run renders a neutral "—": never a green letter on an unmeasured network.
    private void RenderReportCard(ReportCard card)
    {
        GradeLetterText.Text = card.GradeLabel;
        GradeHeadlineText.Text = card.Headline;

        // Route through the single Severity→colour source (SyxxUI.Presentation.SeverityBrush), the
        // twin of macOS Color.ioSeverity, so the two clients never grade the same result a
        // different colour and no fourth independent mapping can drift: info→purple, high→orange
        // (not red), critical→red. Unknown/Ungraded stays neutral secondary — never green.
        GradeLetterText.Foreground = SyxxUI.Presentation.SeverityBrush.Of(card.Severity);

        GradeReasonsPanel.Children.Clear();
        foreach (var reason in card.TopReasons)
        {
            GradeReasonsPanel.Children.Add(new TextBlock
            {
                Text = "• " + reason,
                FontFamily = (FontFamily)Application.Current.Resources["IoFontGrotesk"],
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["IoTextSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        }
        ReportCardBorder.Visibility = Visibility.Visible;
    }

    /// Renders the engine's reasoning from `DeterminativeResult`.
    ///
    /// MIGRATION NOTE. This previously consumed `Stage5Presentation` — the DST-era DTO carrying a
    /// cross-vantage answer matrix, a direct/inferred/unavailable provenance partition and
    /// pre-composed honesty lines. The matrix cutover retired that engine and its DTO to
    /// `archive/dst-engine-windows/`, but this view was never re-derived, so `ConclusionsView`
    /// held 17 references to types that are no longer in the build and **SyxxUI did not compile**.
    /// (Worth stating plainly because a handoff recorded that failure as "XAML compiler needs
    /// Windows": these are C# type-resolution errors and they fail on a Windows host too.)
    ///
    /// The replacement mirrors the two surfaces that already migrated and build —
    /// `Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs` and macOS
    /// `ConclusionsView.swift` — so the three platforms render the same fields from the same
    /// source. The answer matrix and provenance partition are NOT reproduced: nothing in
    /// `DeterminativeResult` supplies them, and inventing a substitute would be fabricating
    /// structure the engine never produced.
    ///
    /// HONESTY. A null determination is not a result — it means the engine could not evaluate.
    /// It must never render as clean, and it must not be silently blank either: an empty panel
    /// reads as "nothing wrong". It says so explicitly instead.
    private void RenderStage5(DeterminativeResult? det)
    {
        Stage5Card.Visibility = Visibility.Visible;
        Stage5Body.Children.Clear();

        if (det is null)
        {
            Stage5Body.Children.Add(Text(
                "Detailed reasoning is unavailable for this run.",
                "IoFontGrotesk", 15, "IoTextBrush", bold: true));
            Stage5Body.Children.Add(Text(
                "That is a gap in the evidence, not a clean result.",
                "IoFontGrotesk", 12, "IoWarningAmberBrush"));
            return;
        }

        // Headline: the engine's own explanation, never re-derived here.
        Stage5Body.Children.Add(Text(det.Explanation, "IoFontGrotesk", 15, "IoTextBrush", bold: true));

        // State, in the engine's vocabulary. Four values — Untested (nothing ran) and Inconclusive
        // (ran, valid, unresolved) are categorically different and must not be collapsed.
        Stage5Body.Children.Add(Text($"State: {det.State.RawValue()}", "IoFontMono", 12,
            det.State switch
            {
                DeterminativeState.Clean          => "IoSuccessGreenBrush",
                DeterminativeState.ThreatDetected => "IoCriticalRedBrush",
                // Inconclusive and Untested are NOT green and NOT alarming: neither is a finding.
                _                                    => "IoTextSecondaryBrush",
            }));

        if (det.Diagnoses.Count > 0)
        {
            Stage5Body.Children.Add(Text("WHAT THE EVIDENCE SUPPORTS", "IoFontMono", 11,
                "IoTextSecondaryBrush", bold: true));
            foreach (var d in det.Diagnoses)
                // E157: use RawValue() (camelCase) instead of ToString() (PascalCase CLR identifier).
                Stage5Body.Children.Add(Text($"{d.DisplayName}: {d.Confidence.RawValue()}",
                    "IoFontGrotesk", 12, "IoTextBrush"));
        }

        // Suppressed diagnoses are the honesty overhang: signals that fired but could not be
        // confirmed. Hiding them is how an unverifiable threat becomes a reassuring silence.
        if (det.Suppressed.Count > 0)
        {
            Stage5Body.Children.Add(Text("SIGNALS WE COULD NOT CONFIRM", "IoFontMono", 11,
                "IoTextSecondaryBrush", bold: true));
            foreach (var s in det.Suppressed)
                // E157: human-readable reason, not a CLR enum identifier like "MissingRequiredSection".
                Stage5Body.Children.Add(Text($"({DescribeSuppression(s.Reason)}) {s.DisplayName}",
                    "IoFontGrotesk", 12, "IoWarningAmberBrush"));
        }
    }

    /// Helper to create a styled TextBlock. Resolves font and brush from application resources.
    private static TextBlock Text(string s, string fontKey, double size, string brushKey, bool bold = false)
    {
        var tb = new TextBlock
        {
            Text = s,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
        };

        if (Application.Current.Resources.TryGetValue(fontKey, out var fontObj) && fontObj is FontFamily font)
            tb.FontFamily = font;

        if (Application.Current.Resources.TryGetValue(brushKey, out var brushObj) && brushObj is Brush brush)
            tb.Foreground = brush;

        if (bold)
            tb.FontWeight = FontWeights.Bold;

        return tb;
    }

    /// E157: human-readable suppression reason instead of a CLR enum identifier.
    /// Mirrors the JSON converter vocabulary (camelCase) but in plain English for the screen.
    private static string DescribeSuppression(SuppressedDiagnosis.SuppressionReason reason) => reason switch
    {
        SuppressedDiagnosis.SuppressionReason.MissingRequiredSection => "required check did not run",
        SuppressedDiagnosis.SuppressionReason.InvalidatedRequiredSection => "required check was invalidated",
        SuppressedDiagnosis.SuppressionReason.HardExcluded => "excluded by rule",
        SuppressedDiagnosis.SuppressionReason.BelowTier => "below the confidence threshold",
        _ => reason.ToString(),
    };
}

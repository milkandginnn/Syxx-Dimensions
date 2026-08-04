using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyxxUI.ViewModels;
using Syxx.Kit;
using System.Collections.ObjectModel;

namespace SyxxUI.Views;

// Parity twin of macOS ProphetView.swift (gold standard). The consensus evaluation and DNSSEC
// presentation logic live in Syxx.Kit/ProphetPresentation.cs so they are testable offline from
// Syxx.Tests without a WinUI host. This code-behind is a thin adapter: it reads the VM's
// ProphetConsensusResult + DnssecStatus, calls ProphetPresentation to build the labels/colours,
// and pushes them into the XAML elements.
//
// HONESTY:
// - The strongest DNSSEC state rendered is RecordsPresentUnverified, coloured IoSuccessGreen but
//   labelled "configured: signatures observed" / "unverified". NEVER "validated" or "secure".
// - A failed DNSSEC query renders as Inconclusive (IoTextSecondaryBrush), NEVER red (E144).
// - Consensus is computed from the actual returned IPs via ProphetPresentation.EvaluateConsensus.
// - No mocked success strings in the production path.
// - No em-dashes in user-facing strings (AGENTS.md rule).
public sealed partial class ProphetView : Page
{
    // 12-oracle resolver list shown in the consensus card (replaces the old raw-records list).
    public ObservableCollection<ResolverRow> ResolverRows { get; } = [];

    private AppViewModel? _vm;

    // E155: subscribe to PropertyChanged so the pane refreshes when data changes while visible.
    private System.ComponentModel.PropertyChangedEventHandler? _pcHandler;

    public ProphetView()
    {
        InitializeComponent();
    }

    private static SolidColorBrush SolidBrush(string key) => (SolidColorBrush)Application.Current.Resources[key];

    // Create a semi-transparent copy of a brush for card backgrounds/borders.
    // Mirrors macOS pres.color.opacity(0.05) / pres.color.opacity(0.24).
    private static SolidColorBrush WithAlpha(SolidColorBrush source, double opacity)
    {
        var c = source.Color;
        return new SolidColorBrush(Color.FromArgb((byte)(c.A * opacity), c.R, c.G, c.B));
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null)
        {
            _pcHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(AppViewModel.ProphetConsensusResult))
                    RefreshConsensus();
                else if (args.PropertyName == nameof(AppViewModel.DnssecStatus))
                    RefreshDnssec();
                else if (args.PropertyName == nameof(AppViewModel.IsRunning))
                    UpdateProgressRing();
            };
            _vm.PropertyChanged += _pcHandler;
        }

        // Sync the domain box with the VM's current domain (parity with macOS binding to model.domain).
        if (_vm is not null && !string.IsNullOrEmpty(_vm.Domain))
            DomainBox.Text = _vm.Domain;

        // Initial button state: disabled when domain is empty/whitespace (E108 parity).
        CheckBtn.IsEnabled = !string.IsNullOrWhiteSpace(DomainBox.Text);

        RefreshConsensus();
        RefreshDnssec();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_vm is not null && _pcHandler is not null)
            _vm.PropertyChanged -= _pcHandler;
        base.OnNavigatedFrom(e);
    }

    // E108 parity: disable the button on empty/whitespace domain to prevent clobbering.
    private void DomainBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckBtn.IsEnabled = !string.IsNullOrWhiteSpace(DomainBox.Text);
    }

    private async void CheckBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Domain = DomainBox.Text;
        _vm.Server = ResolverBox.Text;
        // E146: the record-type picker was removed (the Kit does not support qtype selection yet);
        // RecordType stays at its default "A" until ProbePlan.RunProphetAsync threads a type through.
        CheckBtn.IsEnabled = false;
        CheckProgress.IsActive = true;
        CheckProgress.Visibility = Visibility.Visible;
        try
        {
            await _vm.RunProphetCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            // The VM already sets ProphetRunState = Failed + ProphetRunMessage; surface it in the
            // empty state text so the user sees the failure instead of the idle lore.
            var reason = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "prophet.check") ?? ex.GetType().Name;
            EmptyStateText.Text = $"The check could not complete: {reason}. This is not a clean result.";
        }
        finally
        {
            CheckBtn.IsEnabled = !string.IsNullOrWhiteSpace(DomainBox.Text);
            CheckProgress.IsActive = false;
            CheckProgress.Visibility = Visibility.Collapsed;
        }
        RefreshConsensus();
        RefreshDnssec();
    }

    private void UpdateProgressRing()
    {
        if (_vm is null) return;
        CheckProgress.IsActive = _vm.IsRunning;
        CheckProgress.Visibility = _vm.IsRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    // Build the resolver consensus card from ProphetConsensusResult. Mirrors macOS
    // prophetResultCard (ProphetView.swift:216-276) + the evaluateConsensus/label helpers.
    private void RefreshConsensus()
    {
        ResolverRows.Clear();

        var result = _vm?.ProphetConsensusResult;
        if (result is null)
        {
            // Empty state: show the placeholder (or the error text if the scan failed).
            ResolverCard.Visibility = Visibility.Collapsed;
            DnssecCard.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;

            // E132: a probe that failed must not render as one that never ran.
            if (_vm is not null && _vm.ProphetRunState == AppViewModel.ProbeRunState.Failed)
            {
                var reason = (SyxxUI.Presentation.ErrorPresenter.DisplayReason(_vm.ProphetRunMessage) ?? "the lookup could not complete").TrimEnd('.');
                EmptyStateText.Text = $"The check could not complete: {reason}. This is not a clean result.";
            }
            else
            {
                EmptyStateText.Text = "The Prophet waits in silence. Name a city, and it shall divine.";
            }
            return;
        }

        // Result card: evaluate consensus from the actual returned IPs and build the presentation.
        EmptyState.Visibility = Visibility.Collapsed;
        ResolverCard.Visibility = Visibility.Visible;

        var consensus = ProphetPresentation.EvaluateConsensus(result.ResolverResponses);
        var domain = string.IsNullOrWhiteSpace(_vm?.Domain) ? "this domain" : _vm!.Domain;
        var pres = ProphetPresentation.BuildConsensusLabel(consensus, domain, result.Consensus.MedianLatencyMs);

        var colorBrush = SolidBrush(pres.ColorKey);

        // Card background + border from the consensus colour (macOS: pres.color.opacity(0.05/0.24)).
        ResolverCard.Background = WithAlpha(colorBrush, 0.05);
        ResolverCard.BorderBrush = WithAlpha(colorBrush, 0.24);

        // Dot + two-run headline. Run 2 is present ONLY for the lore agree/disagree states; an
        // unmeasured or under-measured run renders run 1 alone and never borrows either voice.
        ConsensusDot.Fill = colorBrush;
        ConsensusHeadlinePrimary.Text = pres.HeadlinePrimary;
        ConsensusHeadlinePrimary.Foreground = colorBrush;
        ConsensusHeadlineSecondary.Text = pres.HeadlineSecondary ?? "";
        ConsensusHeadlineSecondary.Foreground = colorBrush;
        ConsensusHeadlineSecondary.Visibility =
            pres.HeadlineSecondary is null ? Visibility.Collapsed : Visibility.Visible;

        // Detail (lore 14).
        ConsensusDetail.Text = pres.Detail;

        // Resolver list (12 rows: resolver name left / IP right).
        foreach (var resp in result.ResolverResponses)
            ResolverRows.Add(new(resp.Resolver, resp.Ip));

        // Consensus footer (left label / right agreement fraction).
        AgreementFraction.Text = pres.AgreementFraction;
        AgreementFraction.Foreground = colorBrush;

        // DNSSEC card only renders when BOTH ProphetConsensusResult and DnssecStatus are non-null
        // (parity with macOS: the DNSSEC card is inside the `if let result = prophetResult` block).
        RefreshDnssec();
    }

    // Build the DNSSEC result card from DnssecStatus. Mirrors macOS prophetDnssecCard
    // (ProphetView.swift:281-324) + dnssecPresentation (ProphetView.swift:339-371).
    private void RefreshDnssec()
    {
        // The DNSSEC card only shows when ProphetConsensusResult is non-null (macOS nests it
        // inside the `if let result = prophetResult` block) AND DnssecStatus is non-null.
        if (_vm?.ProphetConsensusResult is null || _vm?.DnssecStatus is null)
        {
            DnssecCard.Visibility = Visibility.Collapsed;
            return;
        }

        var status = _vm!.DnssecStatus!.Value;
        var domain = string.IsNullOrWhiteSpace(_vm?.Domain) ? "" : _vm!.Domain;
        var pres = ProphetPresentation.BuildDnssecPresentation(status, domain);

        var colorBrush = SolidBrush(pres.ColorKey);

        DnssecCard.Visibility = Visibility.Visible;
        DnssecCard.Background = WithAlpha(colorBrush, 0.05);
        DnssecCard.BorderBrush = WithAlpha(colorBrush, 0.24);

        DnssecDot.Fill = colorBrush;
        DnssecTitle.Text = pres.Title;
        DnssecTitle.Foreground = colorBrush;

        DnssecDetail.Text = pres.Detail;

        // DNSSEC posture body: the typed status description from DNSSECVerifier.Describe, never a
        // fabricated "secure". Mono 13, ioTextSecondary.
        DnssecDescription.Text = DNSSECVerifier.Describe(status);

        // DNSSEC footer (left label / right short status tag).
        DnssecStatusLabel.Text = pres.StatusLabel;
        DnssecStatusLabel.Foreground = colorBrush;
    }
}

// 12-oracle resolver row for the consensus card list (replaces the old RecordRow).
public sealed class ResolverRow
{
    public string Resolver { get; set; } = "";
    public string Ip { get; set; } = "";
    public ResolverRow(string resolver, string ip)
    {
        Resolver = resolver;
        Ip = ip;
    }
}
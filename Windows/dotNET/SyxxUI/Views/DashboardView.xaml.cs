using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Syxx.Kit;
using SyxxUI.ViewModels;

namespace SyxxUI.Views;

public sealed partial class DashboardView : Page
{
    private static readonly HashSet<string> _dashboardProps = new()
    {
        nameof(AppViewModel.Conclusion), nameof(AppViewModel.SectionInputs), nameof(AppViewModel.CageEventsCount),
    };

    private AppViewModel? _vm;

    public DashboardView()
    {
        InitializeComponent();
    }

    // E155: subscribe to PropertyChanged so the pane refreshes when data changes while visible
    // (e.g. a probe completes on another pane). Unsubscribe in OnNavigatedFrom to prevent leaks.
    private System.ComponentModel.PropertyChangedEventHandler? _pcHandler;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null)
        {
            _pcHandler = (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) || _dashboardProps.Contains(e.PropertyName))
                    Refresh();
            };
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

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    /// Faint tint of a design-system colour, for the IoStatCard-style borders (macOS uses 0.35).
    private static Brush BorderTint(string colorKey)
    {
        var c = (Color)Application.Current.Resources[colorKey];
        return new SolidColorBrush(Color.FromArgb((byte)(0.35 * 255), c.R, c.G, c.B));
    }

    private void Refresh()
    {
        if (_vm is null) return;

        var servers = NetworkKeyState.GetSystemDNSServers();
        ResolverText.Text = servers.FirstOrDefault() ?? "—";
        ResolversText.Text = servers.Count.ToString();
        ResolversCard.BorderBrush = BorderTint("IoBlueColor");

        var conclusion = _vm.Conclusion;

        // The matrix engine deliberately produces NO confidence percentage — it was removed by
        // owner directive, and macOS renders none. This card previously read `conclusion.Confidence`,
        // a field that no longer exists; printing a percentage here would be inventing a number the
        // engine never computed. Show the result state instead, in the engine's own vocabulary.
        var det = conclusion?.Determination;
        ResultText.Text = det is null ? "—" : det.State switch
        {
            DeterminativeState.Clean          => "Clean",
            DeterminativeState.ThreatDetected => "Threat",
            DeterminativeState.Inconclusive   => "Unresolved",
            DeterminativeState.Untested       => "Not tested",
            _                                    => "—",
        };
        ResultCard.BorderBrush = ResultBorder(det);

        // B088: Alerts from the real advisory list, never a dead placeholder. Red border when any.
        int alerts = conclusion?.Advisories.Count ?? 0;
        AlertsText.Text = conclusion is null ? "—" : alerts.ToString();
        AlertsCard.BorderBrush = BorderTint(alerts > 0 ? "IoCriticalRedColor" : "IoTextSecondaryColor");

        var leakResult = _vm.SectionInputs.Leak?.Result;
        LeakText.Text = leakResult switch
        {
            LeakResult.Confirmed => "Yes",
            LeakResult.NotDetected => "No",
            _ => "—",
        };
        LeakCard.BorderBrush = BorderTint(leakResult switch
        {
            LeakResult.Confirmed => "IoHighOrangeColor",
            LeakResult.NotDetected => "IoSuccessGreenColor",
            _ => "IoTextSecondaryColor",
        });

        QueriesText.Text = _vm.CageEventsCount > 0 ? _vm.CageEventsCount.ToString() : "—";
        // A count is a fact, not a conclusion, so it should not be graded green.
        // Use blue when there's data, neutral when empty.
        QueriesCard.BorderBrush = BorderTint(_vm.CageEventsCount > 0 ? "IoBlueColor" : "IoTextSecondaryColor");
    }

    /// Border colour for the result card. Replaces the DST-era `ConfidenceBorder`, which keyed on
    /// `Conclusion.Confidence`/`Result` — both removed by the matrix cutover.
    ///
    /// CLEAN IS THE ONLY GREEN. Threat is red; Inconclusive (ran, unresolved) and Untested (never
    /// ran) are amber and neutral respectively. Neither may render green: an unmeasured or
    /// unresolved network is not a safe one, and colour is the fastest thing a user reads.
    private static Brush ResultBorder(DeterminativeResult? det)
    {
        if (det is null) return BorderTint("IoTextSecondaryColor");
        return det.State switch
        {
            DeterminativeState.Clean          => BorderTint("IoSuccessGreenColor"),
            DeterminativeState.ThreatDetected => BorderTint("IoCriticalRedColor"),
            DeterminativeState.Inconclusive   => BorderTint("IoWarningAmberColor"),
            // Untested: nothing was measured. Neutral — not green, not alarming.
            _                                    => BorderTint("IoTextSecondaryColor"),
        };
    }
}

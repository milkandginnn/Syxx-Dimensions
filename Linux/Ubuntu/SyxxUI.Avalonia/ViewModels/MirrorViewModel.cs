using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Avalonia.Mirror;
using SyxxUI.Presentation;   // linked from Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Mirror — is something interfering with what I asked for? The cross-resolver comparison (Mirror
/// Lite, <c>isPro:false</c>) runs for real on Linux: it resolves a name locally and across public
/// vantages and reports the tri-state. The Pro content-integrity check (server-fetched page hashes)
/// is server-backed and needs a licence, which is not activatable on Linux yet (G16) — so it is shown
/// as deferred, never faked as passing.
///
/// HONESTY: DEGRADED and INCONCLUSIVE render AMBER, never green. Only a real MATCH is green.
/// </summary>
public partial class MirrorViewModel : ObservableObject
{
    [ObservableProperty] private string _domain = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _matchLine = "";
    [ObservableProperty] private IBrush _matchBrush = Brushes.Gray;

    // View Details — expandable content integrity comparison
    [ObservableProperty] private bool _detailsExpanded;
    [ObservableProperty] private bool _hasComparisonResult;
    [ObservableProperty] private DegradationScore? _degradationScore;
    [ObservableProperty] private MirrorBundle? _remoteBundle;
    [ObservableProperty] private MirrorBundle? _localBundle;
    [ObservableProperty] private DiffResult? _comparisonDiffResult;
    [ObservableProperty] private bool _isPartialResult;
    [ObservableProperty] private string? _partialReason;

    public string DetailsToggleLabel => DetailsExpanded ? "▾ Hide Details" : "▸ View Details";

    public string ProNote =>
        "Static-content integrity (comparing page hashes against a clean server-fetched copy) is a Pro, "
        + "server-backed check. Licence activation is not yet available on the Linux client, so this "
        + "part is unavailable here — it is not being run or assumed to pass.";

    [RelayCommand]
    private void ToggleDetails()
    {
        DetailsExpanded = !DetailsExpanded;
    }

    partial void OnDetailsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(DetailsToggleLabel));
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(Domain)) return;
        IsRunning = true; HasResult = false;
        MatchLine = "Comparing across resolvers…"; MatchBrush = Brushes.Gray;

        var servers = NetworkKeyState.GetSystemDNSServers();
        if (servers.Count == 0)
        {
            // D278 parity (macOS AppModel.runMirror): never fabricate a resolver. A refused run
            // renders amber (failed/unavailable), never as a completed or clean result — same state
            // discipline as the catch branch below.
            MatchLine = "System resolver could not be determined. Check your network connection, then retry.";
            MatchBrush = Amber;
            IsRunning = false; HasResult = true;
            return;
        }

        string local = servers[0];

        SectionInputs r;
        try { r = await ProbePlan.RunMirrorAsync(Domain.Trim(), local, isPro: false); }
        // Defect-2 parity: raw detail to the diagnostic sink, readable text (or the bare failure,
        // with no invented cause) to the screen. The brush stays amber either way — a comparison
        // that could not run is inconclusive, never clean.
        catch (System.Exception ex)
        {
            MatchLine = ErrorPresenter.Compose(
                "Mirror could not complete.", ErrorPresenter.UserDetail(ex, "mirror.run"));
            MatchBrush = Amber; IsRunning = false; HasResult = true; return;
        }

        var c = r.Comparison;
        if (c is null)
        {
            MatchLine = "No comparison was produced — inconclusive, not clean.";
            MatchBrush = Amber;
        }
        else if (c.IsRedirectionDetected)
        {
            MatchLine = "DIVERGES — the local resolver's answer differs from the public consensus (a redirection/tampering signal).";
            MatchBrush = new SolidColorBrush(Color.Parse("#FF5C7A"));
        }
        else if (c.IsInconclusive)
        {
            MatchLine = "INCONCLUSIVE — not enough agreeing evidence to judge. This is not a clean result.";
            MatchBrush = Amber;
        }
        else if (c.IsDegraded)
        {
            MatchLine = "DEGRADED — some vantages could not be reached, so confidence is reduced.";
            MatchBrush = Amber;
        }
        else
        {
            MatchLine = "MATCHES — the local answer agrees with the public resolver consensus.";
            MatchBrush = new SolidColorBrush(Color.Parse("#3FD08A"));
        }

        HasResult = true; IsRunning = false;
    }

    private static IBrush Amber => new SolidColorBrush(Color.Parse("#F2B33D"));
}

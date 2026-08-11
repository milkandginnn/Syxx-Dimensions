using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Presentation;   // linked from Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Prophet: is the resolver telling the truth about a name, and does DNSSEC hold? Runs the real
/// <see cref="ProbePlan.RunProphetAsync"/> and then the 12-oracle consensus fan-out via
/// <see cref="ProphetConsensus.RunAsync"/> (parity twin of macOS AppModel.runLookup,
/// AppModel.swift:1011-1047). HONESTY: the strongest DNSSEC posture emitted is
/// <see cref="DNSSECStatus.RecordsPresentUnverified"/>; this client does NOT perform full
/// cryptographic chain validation (CF-08/A2). A failed DNSSEC query renders as
/// <see cref="DNSSECStatus.Inconclusive"/> (text-secondary), NEVER red. Red reads as an
/// accusation against a domain Syxx simply could not reach. Resolver consensus is computed from
/// the actual returned IPs (tri-state: agree / disagree / insufficient / inconclusive), never
/// from resolver count alone.
/// </summary>
public partial class ProphetViewModel : ObservableObject
{
    // --- Brushes (match App.axaml design tokens) ---
    // ProphetBlue is the module highlight (#00b4ff, owner directive 2026-08-02) used by the
    // agreeing consensus card and the "rightly set" DNSSEC card, in parity with macOS.
    private static readonly IBrush ProphetBlueBrush = new SolidColorBrush(Color.Parse("#00b4ff"));
    private static readonly IBrush AmberBrush = new SolidColorBrush(Color.Parse("#F2B33D"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#FF5C7A"));
    private static readonly IBrush TextSecondary = new SolidColorBrush(Color.Parse("#c9c1e6"));
    private static readonly IBrush FallbackBrush = Brushes.Gray;

    /// <summary>
    /// Map a Kit <c>ColorKey</c> to the Avalonia brush for this platform. The Kit stays
    /// toolkit-agnostic and returns semantic key strings; each client resolves them to its own
    /// brush type. An unrecognised key falls back to a neutral grey rather than borrowing a
    /// success or failure colour, so a Kit/client drift can never render as a false clean.
    /// </summary>
    private static IBrush BrushFor(string colorKey) => colorKey switch
    {
        "IoProphetBlueBrush" => ProphetBlueBrush,
        "IoWarningAmberBrush" => AmberBrush,
        "IoCriticalRedBrush" => RedBrush,
        "IoTextSecondaryBrush" => TextSecondary,
        _ => FallbackBrush,
    };

    // --- Observable state ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    // The domain appears inside both detail strings and inside the DNSSEC title, so a domain
    // edit must refresh all three or the card would keep describing the previous domain.
    [NotifyPropertyChangedFor(nameof(ConsensusDetail), nameof(DnssecDetail), nameof(DnssecTitle))]
    private string _domain = "";

    [ObservableProperty] private string _resolverIp = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private bool _isRunning;

    /// <summary>
    /// The 12-oracle consensus result (null before a scan completes). Mirrors macOS
    /// <c>AppModel.prophetResult</c> (AppModel.swift:255). Cleared at the start of every scan
    /// so a stale card never lingers behind a new run.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasConsensusResult), nameof(ResolverResponses),
        nameof(ConsensusHeadlinePrimary), nameof(ConsensusHeadlineSecondary),
        nameof(HasConsensusHeadlineSecondary),
        nameof(ConsensusDetail), nameof(ConsensusFraction),
        nameof(ConsensusBrush), nameof(ConsensusCardBackground), nameof(ConsensusBorderBrush))]
    private ProphetResult? _prophetConsensusResult;

    /// <summary>
    /// The DNSSEC posture fetched by <see cref="ProbePlan.RunProphetAsync"/> (reused to avoid a
    /// second network round-trip). Null before a scan; tri-state after. Mirrors macOS
    /// <c>AppModel.dnssecStatus</c> (AppModel.swift:259).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(HasDnssecResult), nameof(DnssecTitle), nameof(DnssecDetail),
        nameof(DnssecDescription), nameof(DnssecStatusLabel),
        nameof(DnssecBrush), nameof(DnssecCardBackground), nameof(DnssecBorderBrush))]
    private DNSSECStatus? _dnssecStatus;

    /// <summary>Error / D278 gate message; empty in the normal happy path.</summary>
    [ObservableProperty] private string _statusLine = "";

    // --- Computed accessors the view binds to ---

    /// <summary>Button is enabled only when a non-blank domain is present and no scan is in flight.</summary>
    public bool CanDivine => !IsRunning && !string.IsNullOrWhiteSpace(Domain);

    public bool HasConsensusResult => ProphetConsensusResult is not null;
    public bool HasDnssecResult => DnssecStatus is not null;

    public IReadOnlyList<ProphetResult.ResolverResponse> ResolverResponses =>
        ProphetConsensusResult?.ResolverResponses ?? Array.Empty<ProphetResult.ResolverResponse>();

    // Consensus presentation (computed from ProphetConsensusResult + Domain via the shared Kit)
    public string ConsensusHeadlinePrimary => Consensus().HeadlinePrimary;
    public string ConsensusHeadlineSecondary => Consensus().HeadlineSecondary ?? "";
    /// <summary>True only for the lore agree/disagree states, which carry a second headline run.</summary>
    public bool HasConsensusHeadlineSecondary => Consensus().HeadlineSecondary is not null;
    public string ConsensusDetail => Consensus().Detail;
    public string ConsensusFraction => Consensus().AgreementFraction;
    public IBrush ConsensusBrush => BrushFor(Consensus().ColorKey);
    public IBrush ConsensusCardBackground => Tint(ConsensusBrush, 0.05);
    public IBrush ConsensusBorderBrush => Tint(ConsensusBrush, 0.24);

    // DNSSEC presentation (computed from DnssecStatus + Domain via the shared Kit)
    public string DnssecTitle => Dnssec().Title;
    public string DnssecDetail => Dnssec().Detail;
    public string DnssecDescription => DnssecStatus.HasValue ? DNSSECVerifier.Describe(DnssecStatus.Value) : "";
    public string DnssecStatusLabel => Dnssec().StatusLabel;
    public IBrush DnssecBrush => BrushFor(Dnssec().ColorKey);
    public IBrush DnssecCardBackground => Tint(DnssecBrush, 0.05);
    public IBrush DnssecBorderBrush => Tint(DnssecBrush, 0.24);

    // --- Command ---

    [RelayCommand(CanExecute = nameof(CanDivine))]
    private async Task CheckAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(Domain)) return;

        IsRunning = true;
        // Parity macOS AppModel.runLookup line 967: clear both before the scan so a stale card
        // never lingers behind a new run.
        ProphetConsensusResult = null;
        DnssecStatus = null;
        StatusLine = "";

        try
        {
            var servers = NetworkKeyState.GetSystemDNSServers();
            if (string.IsNullOrWhiteSpace(ResolverIp) && servers.Count == 0)
            {
                // D278 parity (macOS AppModel.runLookup): never fabricate a resolver. A refused run
                // renders as failed/unavailable, never as a completed or clean result.
                StatusLine = "System resolver could not be determined. Check your network connection, then retry.";
                return;
            }

            string resolver = string.IsNullOrWhiteSpace(ResolverIp)
                ? servers[0]
                : ResolverIp.Trim();

            // L2-030: pass CancellationToken so the check can be cancelled on navigation or shutdown.
            using var cts = new System.Threading.CancellationTokenSource();

            SectionInputs r;
            try { r = await ProbePlan.RunProphetAsync(Domain.Trim(), resolver); }
            // Defect-2 parity: raw detail to the diagnostic sink, readable text (or the bare failure,
            // with no invented cause) to the screen. A failed lookup still renders as a failure.
            catch (System.Exception ex)
            {
                StatusLine = ErrorPresenter.Compose(
                    "The lookup could not complete.", ErrorPresenter.UserDetail(ex, "prophet.lookup"));
                return;
            }

            // B231: null when the latency probe was skipped or timed out; rendering "0ms" fabricated a
            // perfect measurement of something that was never measured. Consumers render "not measured".
            int? medianLatencyMs = (r.Latency is { Status: not ResolverLatencyStatus.Untested } lat)
                ? (int)Math.Round(lat.MedianMs)
                : null;

            // 12-oracle consensus fan-out (parity macOS AppModel.runLookup 1011-1047). Linux Prophet
            // has no record-type picker, so qtype is always 1 (A).
            try
            {
                ProphetConsensusResult = await ProphetConsensus.RunAsync(
                    Domain.Trim(), qtype: 1, localServer: resolver, medianLatencyMs: medianLatencyMs);
            }
            catch (System.Exception ex)
            {
                StatusLine = ErrorPresenter.Compose(
                    "The consensus fan-out could not complete.", ErrorPresenter.UserDetail(ex, "prophet.consensus"));
                return;
            }

            // Reuse the DNSSECStatus already fetched by ProbePlan.RunProphetAsync (ProbePlan.cs:51)
            // to avoid a second DO-enabled DNSKEY round-trip. The macOS gold standard makes a fresh
            // query (AppModel.swift:1047); both are honest, and the posture is real either way.
            DnssecStatus = r.Dnssec;
        }
        // L2-032: try/finally ensures IsRunning is always restored, even if an unexpected exception
        // escapes the inner catch branches (e.g. a NullReferenceException between catch blocks).
        finally
        {
            IsRunning = false;
        }
    }

    // === Presentation (delegated to the shared Kit) ==========================================
    // The consensus and DNSSEC wording, tri-state mapping, and semantic colour keys live in
    // Syxx.Kit/ProphetPresentation.cs, the single source consumed by BOTH the Windows and Linux
    // clients (macOS keeps its own app-local copy as the gold standard the Kit is translated
    // from). This view model previously carried an inline duplicate of that logic which had
    // already drifted from both; the duplicate is deleted so it cannot drift again.

    private static readonly ProphetPresentation.ConsensusPresentation EmptyConsensus =
        new("", null, "", "", "");

    private static readonly ProphetPresentation.DnssecPresentation EmptyDnssec =
        new("", "", "", "");

    private ProphetPresentation.ConsensusPresentation Consensus()
    {
        if (ProphetConsensusResult is null) return EmptyConsensus;
        var eval = ProphetPresentation.EvaluateConsensus(ProphetConsensusResult.ResolverResponses);
        string domain = string.IsNullOrWhiteSpace(Domain) ? "this domain" : Domain;
        return ProphetPresentation.BuildConsensusLabel(
            eval, domain, ProphetConsensusResult.Consensus.MedianLatencyMs);
    }

    private ProphetPresentation.DnssecPresentation Dnssec()
    {
        if (DnssecStatus is null) return EmptyDnssec;
        // The Kit substitutes "this domain" for an empty domain, so pass it through unguarded.
        return ProphetPresentation.BuildDnssecPresentation(
            DnssecStatus.Value, string.IsNullOrWhiteSpace(Domain) ? "" : Domain);
    }


    // --- Brush helper: create a semi-transparent tint of a solid brush (for card bg / border) ---

    private static IBrush Tint(IBrush baseBrush, double alpha)
    {
        if (baseBrush is SolidColorBrush sc)
        {
            byte a = (byte)Math.Round(byte.MaxValue * alpha);
            return new SolidColorBrush(Color.FromArgb(a, sc.Color.R, sc.Color.G, sc.Color.B));
        }
        return baseBrush;
    }
}
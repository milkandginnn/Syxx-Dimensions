using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sentry;
using Syxx.Kit;
using SyxxUI.Services;
using System.Collections.ObjectModel;

namespace SyxxUI.ViewModels;

/// <summary>
/// Root view-model for the Syxx Windows GUI — mirrors AppModel.swift.
/// Uses CommunityToolkit.MVVM <see cref="ObservableObject"/> for property-change notifications.
/// </summary>
public sealed partial class AppViewModel : ObservableObject
{
    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Shared inputs (bound by multiple screens)
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private string _recordType = "A";
    [ObservableProperty] private string _server = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPro;
    [ObservableProperty] private string _licenseKey = string.Empty;

    /// <summary>
    /// Correlation ID: a fresh UUID v4 generated at the start of every user-initiated scan.
    /// Flows through all spans and breadcrumbs so a single user action can be traced
    /// end-to-end in Sentry and OTel. Null when no scan is in progress.
    /// Parity twin of macOS AppModel.scanCorrelationId.
    /// </summary>
    [ObservableProperty] private string? _scanCorrelationId;

    public ObservableCollection<string> RecordTypes { get; } =
        new(["A", "AAAA", "MX", "NS", "TXT", "CNAME", "SOA", "PTR", "SRV", "CAA"]);

    public AppViewModel() : this(
        (domain, local) => VantageComparer.CompareVantagePointsAsync(domain: domain, localServer: local),
        FailClosedSettings())
    {
    }

    public AppViewModel(SyxxSettings settings) : this(
        (domain, local) => VantageComparer.CompareVantagePointsAsync(domain: domain, localServer: local),
        settings)
    {
    }

    /// <summary>
    /// Test seam (Windows/SyxxUI.Tests/AppViewModelWitnessTests.cs): mirrors the identical
    /// constructor-injection fix already landed for Ubuntu's WitnessViewModel (see DELTAS.md B195
    /// follow-up, Ubuntu/SyxxUI.Avalonia/ViewModels/WitnessViewModel.cs:67-87). The live
    /// vantage-comparison fetch <see cref="WitnessTestifyAsync"/> performs at sign time (D051's
    /// re-probe-instead-of-stale-cache fix) is injected via a delegate so a unit test can force a
    /// transport exception or an unresolved/empty-domain result deterministically, without a real
    /// network resolve. The parameterless constructor above wires the real
    /// <see cref="VantageComparer.CompareVantagePointsAsync"/> call unchanged, so production
    /// behavior is untouched.
    /// </summary>
    public AppViewModel(Func<string, string, Task<ComparisonResult?>> compareVantagePoints)
        : this(compareVantagePoints, FailClosedSettings())
    {
    }

    private AppViewModel(
        Func<string, string, Task<ComparisonResult?>> compareVantagePoints,
        SyxxSettings settings)
    {
        _compareVantagePoints = compareVantagePoints;
        _settings = settings;
        // Windows GUI defect: HasWitnessKey previously started false and was only ever assigned
        // inside SaveWitnessKey(), so a key persisted in DPAPI storage during a PREVIOUS run (or
        // saved and never re-typed this session) still read as "no signing key on this machine" —
        // a genuinely valid testimony could display as unverifiable. Must reflect on-disk reality
        // from the moment this view-model exists, not just after an explicit re-save.
        RefreshWitnessKeyStatus();
    }

    private readonly Func<string, string, Task<ComparisonResult?>> _compareVantagePoints;
    private readonly SyxxSettings _settings;

    private static SyxxSettings FailClosedSettings() => new()
    {
        NotificationsEnabled = false,
        TelemetryEnabled = false,
        UpdateCheckEnabled = false,
        CrashReportingEnabled = false,
    };

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: A5 — continuous monitoring (macOS AppModel twin)
    // ──────────────────────────────────────────────────────────────────────────

    // Off by default: monitoring is a subscription capability the user opts into, and a
    // background loop should never start unasked. LastMonitorChange latches until acknowledged
    // so "your DNS changed" is not lost by not having the window open when it happened.
    [ObservableProperty] private bool _monitoringOn;
    // E151: LastMonitorTick was written on every scheduler tick and read by nothing — removed.
    [ObservableProperty] private MonitorTick? _lastMonitorChange;

    /// Raised on every scheduler tick. The scheduler ticks on a background thread; marshalling to
    /// the UI thread is handled internally by the onTick callback so subscribers receive the event
    /// on the UI thread.
    public event System.Action<MonitorTick>? MonitorTicked;

    private MonitorScheduler? _monitor;
    private MonitorScheduler Monitor => _monitor ??= new MonitorScheduler(
        intervalSeconds: 900,
        onTick: tick =>
        {
            // FIX (MonitorScheduler cross-thread): MonitorScheduler ticks on a background thread
            // but [ObservableProperty] setters must raise PropertyChanged on the UI thread for
            // WinUI data binding. Marshal the entire tick handler through the DispatcherQueue.
            // If no DispatcherQueue is available (headless/test), fall back to direct assignment.
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ?? Microsoft.UI.Xaml.Window.Current?.DispatcherQueue;
            if (dispatcher is not null)
            {
                dispatcher.TryEnqueue(() => ProcessMonitorTick(tick));
            }
            else
            {
                ProcessMonitorTick(tick);
            }
        });

    /// <summary>
    /// Processes a monitor tick on the UI thread. Extracted so the dispatcher marshalling in
    /// the onTick lambda is testable without accessing WinUI DispatcherQueue.
    /// </summary>
    private void ProcessMonitorTick(MonitorTick tick)
    {
        // E151: LastMonitorTick was written and never read — removed.
        if (tick.State == MonitorState.Changed) LastMonitorChange = tick;
        MonitorTicked?.Invoke(tick);

        // A5/D-4: OS toast notification delivery when a drift is detected.
        // Respects the user's NotificationsEnabled preference. Notification failure
        // must never crash the monitor loop.
        // Load settings fresh on each tick to pick up mid-session changes (cheap operation).
        if (ReadyRuntimePolicy.ShouldDeliverMonitorNotification(
                tick.State,
                SyxxSettingsStore.Load().NotificationsEnabled))
        {
            try
            {
                var toastXml = Windows.UI.Notifications.ToastNotificationManager
                    .GetTemplateContent(Windows.UI.Notifications.ToastTemplateType.ToastText02);
                var textNodes = toastXml.GetElementsByTagName("text");
                textNodes[0]!.AppendChild(toastXml.CreateTextNode("Syxx \u2014 Network Change Detected"));
                textNodes[1]!.AppendChild(toastXml.CreateTextNode(tick.Alerts.Count > 0 ? tick.Alerts[0].Headline : tick.Summary));
                var toast = new Windows.UI.Notifications.ToastNotification(toastXml);
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch { /* notification failure must not crash the monitor loop */ }
        }
    }

    public void SetMonitoring(bool on)
    {
        MonitoringOn = on;
        if (on) Monitor.Start(); else Monitor.Stop();
    }

    public void AcknowledgeMonitorChange()
    {
        _monitor?.AcknowledgeChange();
        LastMonitorChange = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Section inputs — accumulated by synthesis engine
    // ──────────────────────────────────────────────────────────────────────────

    // SectionInputs is a mutable class; mutations must notify via OnPropertyChanged.
    private SectionInputs _sectionInputs = new();
    public SectionInputs SectionInputs
    {
        get => _sectionInputs;
        set => SetProperty(ref _sectionInputs, value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: E132 — Probe run state (per-module lifecycle tracking)
    // ──────────────────────────────────────────────────────────────────────────

    /// E132: honest per-module lifecycle state, generalising B195 (Witness-only) to all panes.
    /// A probe that failed must be distinguishable from one that never ran — previously every
    /// null result rendered the idle empty-state copy, which is a false statement about what
    /// happened (Honesty Protocol §2/§3).
    public enum ProbeRunState { Idle, Running, Completed, Failed }

    [ObservableProperty] private ProbeRunState _prophetRunState = ProbeRunState.Idle;
    [ObservableProperty] private string _prophetRunMessage = string.Empty;

    // 12-oracle consensus result for the Prophet pane (parity twin of macOS
    // AppModel.prophetResult, AppModel.swift:255-259). Null before a scan; populated after
    // ProphetConsensus.RunAsync lands. The ProphetView result card renders only when non-null.
    [ObservableProperty] private ProphetResult? _prophetConsensusResult;

    // DNSSEC posture for the duplicate result card (parity twin of macOS AppModel.dnssecStatus,
    // AppModel.swift:258). A SEPARATE top-level VM property (not just SectionInputs.Dnssec) so the
    // ProphetView can clear it at scan start and set it only when the fresh DNSKEY query lands,
    // mirroring macOS runLookup line 967: dnssecStatus = nil. Null before a scan; populated after.
    // Tri-state via DNSSECStatus, never a Bool, never a mocked "secure".
    [ObservableProperty] private DNSSECStatus? _dnssecStatus;

    [ObservableProperty] private ProbeRunState _mirrorRunState = ProbeRunState.Idle;
    [ObservableProperty] private string _mirrorRunMessage = string.Empty;
    [ObservableProperty] private ProbeRunState _portalRunState = ProbeRunState.Idle;
    [ObservableProperty] private string _portalRunMessage = string.Empty;
    [ObservableProperty] private ProbeRunState _gluttonRunState = ProbeRunState.Idle;
    [ObservableProperty] private string _gluttonRunMessage = string.Empty;

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Portal screen
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private NetworkStateReport? _portalReport;

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Glutton screen
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _leakTestSubdomain = string.Empty;
    public ObservableCollection<LeakProbeRow> LeakProbeResults { get; } = [];

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Cage (capture) screen
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _cageCapturing;
    // E151: CageHidden and CageRemain were written only by ResetAll and read by nothing — removed.
    [ObservableProperty] private int _cageDuration = 30;
    [ObservableProperty] private int _cageEventsCount;
    public ObservableCollection<CageRow> CageRows { get; } = [];

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Witness screen
    // ──────────────────────────────────────────────────────────────────────────

    // The Ed25519 signing key is NEVER an observable/public property — it would be reachable via
    // reflection or a crash dump. Only a bool is exposed; the key lives DPAPI-encrypted at rest
    // in WitnessKeyStore and is loaded transiently for signing/verify. (D015)
    [ObservableProperty] private bool _hasWitnessKey;
    /// D052 (Windows half): the PRESENTABLE cause of a failed SaveWitnessKey() DPAPI write,
    /// previously discarded by an empty catch block. Rendered in WitnessView (KeyErrorText) as
    /// "Could not save the signing key: {reason}" — owner-approved wording, 2026-07-22.
    ///
    /// Defect-2 parity: this is now the output of <see cref="SyxxUI.Presentation.ErrorPresenter"/>,
    /// not a raw <c>ex.Message</c>, so crash-reporter material can no longer reach the screen. It is
    /// therefore EMPTY on a failure whose exception carried no human-readable cause — which is why
    /// emptiness is no longer the "no error" sentinel. Ask
    /// <see cref="WitnessKeySaveFailed"/> whether the save failed; ask this only for the detail.
    [ObservableProperty] private string _witnessKeyError = string.Empty;
    /// D052 / Defect-2: did the LAST SaveWitnessKey() attempt fail? The honest sentinel, kept
    /// separate from <see cref="WitnessKeyError"/> because a presented detail may legitimately be
    /// empty. A failure with no readable cause must still render as a failure, never as a save that
    /// quietly succeeded.
    [ObservableProperty] private bool _witnessKeySaveFailed;
    [ObservableProperty] private string _witnessSignature = string.Empty;
    [ObservableProperty] private bool _showTestimony;
    [ObservableProperty] private bool _showValidation;
    [ObservableProperty] private bool _witnessValid;
    [ObservableProperty] private WitnessSnapshot? _witnessSnapshot;
    /// E021: the detailed verification outcome behind WitnessValid; null when never verified.
    [ObservableProperty] private SnapshotVerification? _witnessVerification;
    /// Monotonically increasing generation counter incremented by WitnessFree() so that a
    /// concurrently running WitnessTestifyAsync() can detect that its snapshot was cleared
    /// mid-flight and abandon its stale state (Fix 1: WitnessFree ordering race).
    private int _witnessGeneration;
    /// B195: honest lifecycle state of the last Witness Testimony (sealing) action, distinct from
    /// WitnessVerification (the VERIFY outcome). The testimony panel renders from this so a probe
    /// that failed or aborted is never shown as the pending "Not yet signed" placeholder — a dead
    /// probe and a running probe must be visually distinguishable (Honesty Protocol §4). This is
    /// the parity twin of macOS AppModel.witnessTestimonyState; previously Windows reported NOTHING
    /// on failure (silent early-returns + an empty catch), which was strictly worse than macOS.
    [ObservableProperty] private WitnessTestimonyState _witnessTestimony = WitnessTestimonyState.Idle;
    /// The failure reason surfaced when WitnessTestimony == Failed; empty otherwise.
    [ObservableProperty] private string _witnessTestimonyMessage = string.Empty;
    /// #22 (Windows parity with Linux WitnessViewModel.VerifyExternal, WitnessViewModel.cs:257-284):
    /// raw JSON of an externally-supplied signed testimony — a seal made on another device — pasted
    /// by the user so its signature can be checked here. Kept separate from the self-verify path;
    /// both are judged by the SAME <see cref="WitnessSigner.VerifySnapshotDetailed"/> and rendered
    /// through the SAME result switch (WitnessView.UpdateValidation).
    [ObservableProperty] private string _externalSnapshotJson = string.Empty;

    /// Canonical snapshot reader options — case-insensitive, matching the CLI verify path and the
    /// Linux twin (WitnessViewModel.cs:161-162). The date/status wire converters are attribute-bound
    /// on WitnessSnapshot itself, so no extra converter registration is needed here.
    private static readonly System.Text.Json.JsonSerializerOptions ExternalVerifyReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Conclusion screen
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private Conclusion? _conclusion;

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Dimension (Workstream D) — startup / broadcast / update
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private StartupResponse? _startupResponse;
    [ObservableProperty] private Broadcast? _pendingBroadcast;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateLatest = string.Empty;
    [ObservableProperty] private string _updateUrl = string.Empty;

    /// E129: tri-state outcome for the last update check, so the UI can distinguish
    /// "server said current" from "server never answered" or "served from stale cache."
    public enum UpdateCheckOutcome { Live, Cached, Failed }
    [ObservableProperty] private UpdateCheckOutcome _updateCheckOutcome = UpdateCheckOutcome.Failed;

    /// Called once on launch — one phone-home per day. Shows broadcast if undismissed.
    /// Mirrors macOS checkStartup() which calls validateLicenseOnLaunch() FIRST, before
    /// the dimension service fetch (AppModel.swift:635-659).
    public async Task CheckStartupAsync()
    {
        // Validate license state first (parity: macOS calls validateLicenseOnLaunch at top of checkStartup).
        await CheckLicenseAsync();

        var resp = await DimensionService.Shared.FetchStartupAsync();
        StartupResponse = resp;
        // E129: record whether the check actually succeeded live, fell back to cache, or failed entirely.
        UpdateCheckOutcome = resp is null
            ? UpdateCheckOutcome.Failed
            : DimensionService.Shared.LastResponseWasCached
                ? UpdateCheckOutcome.Cached
                : UpdateCheckOutcome.Live;
        if (resp?.Update is not null)
        {
            UpdateAvailable = resp.Update.Available;
            UpdateLatest = resp.Update.Latest;
            UpdateUrl = resp.Update.Url ?? "https://syxx.app/download";
        }
        if (resp?.Broadcast is not null)
        {
            var b = resp.Broadcast;
            if (!DimensionService.Shared.HasDismissed(b.Id) &&
                DimensionService.Shared.IsVersionInRange(SyxxConfig.Version, b.MinVersion, b.MaxVersion))
            {
                PendingBroadcast = b;
            }
        }
    }

    /// <summary>
    /// E028: wire LicenseManager validation result to IsPro at launch.
    /// The macOS twin does this in AppModel.validateLicenseOnLaunch().
    /// Without this, Mirror (the flagship paid module) never runs on Windows.
    ///
    /// On network failure (catch), does NOT hard-set IsPro = false — a censored or
    /// filtered network must not silently demote a paying subscriber to Lite (E-7
    /// offline grace). Preserves the current value and attempts offline grace fallback.
    /// </summary>
    public async Task CheckLicenseAsync()
    {
        try
        {
            var (unlocked, _) = await LicenseManager.IsFeatureUnlockedAsync();
            var oldPro = IsPro;
            IsPro = unlocked;
            SentrySdk.AddBreadcrumb(
                message: $"License state changed: pro={oldPro} → pro={unlocked}",
                category: "gui.state",
                level: BreadcrumbLevel.Info);
        }
        catch
        {
            // Network failure: do NOT set IsPro = false. Preserve current value.
            // If the user was previously Pro and the network is down, stay Pro.
            // Attempt offline grace fallback (30-day window from last valid validation).
            try
            {
                var lastValid = LicenseManager.GetLastValidValidationEpoch();
                if (LicenseManager.OfflineGraceApplies(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        lastValid))
                {
                    IsPro = true;
                    SentrySdk.AddBreadcrumb(
                        message: "License check failed — offline grace applied, IsPro preserved as true",
                        category: "gui.state",
                        level: BreadcrumbLevel.Info);
                }
                // else: no recent validation record — IsPro stays as-is (false if never validated).
            }
            catch
            {
                // Silent: last-resort guard against corruption in the grace check itself.
            }
        }
    }

    public void DismissBroadcast(Broadcast broadcast)
    {
        DimensionService.Shared.MarkDismissed(broadcast.Id);
        PendingBroadcast = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Commands
    // ──────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RunProphetAsync()
    {
        if (IsRunning) return;
        ScanCorrelationId = Guid.NewGuid().ToString();
        IsRunning = true;
        ProphetRunState = ProbeRunState.Running;
        ProphetRunMessage = string.Empty;
        // Parity with macOS AppModel.runLookup (AppModel.swift:967): clear the 12-oracle consensus
        // result and the DNSSEC posture at scan start so stale cards never render while a fresh
        // scan is in flight. Both surface as null (empty state) until the new probes land.
        ProphetConsensusResult = null;
        DnssecStatus = null;
        SentrySdk.AddBreadcrumb(message: "Prophet scan started", category: "gui.state", level: BreadcrumbLevel.Info);
        try
        {
            // D278 parity (macOS AppModel.swift:939-944): never fabricate a resolver. When no
            // user-selected server is set AND the system resolver cannot be measured, refuse to
            // run — an unaudited resolver must never be presented as audited.
            var systemResolver = NetworkKeyState.GetSystemDNSServers().FirstOrDefault();
            string resolver;
            if (string.IsNullOrWhiteSpace(Server))
            {
                if (systemResolver is null)
                {
                    ProphetRunState = ProbeRunState.Failed;
                    ProphetRunMessage = "System resolver could not be determined. Check your network connection, then retry.";
                    return;
                }
                resolver = systemResolver;
            }
            else
            {
                resolver = Server;
            }

            // B096: pass the user-selected record type through to the probe plan.
            var qtype = MapRecordTypeToQtype(RecordType);
            var plan = await ProbePlan.RunProphetAsync(Domain, resolver, qtype: qtype);
            _sectionInputs.Resolution = plan.Resolution;
            _sectionInputs.Dnssec = plan.Dnssec;
            _sectionInputs.NxCanary = plan.NxCanary;
            _sectionInputs.DnssecCanary = plan.DnssecCanary;
            _sectionInputs.Latency = plan.Latency;
            _sectionInputs.Stability = plan.Stability;
            _sectionInputs.WildcardSynthesis = plan.WildcardSynthesis;
            _sectionInputs.Denial = plan.Denial;
            _sectionInputs.DualStack = plan.DualStack;
            _sectionInputs.Edns0 = plan.Edns0;
            _sectionInputs.VersionBind = plan.VersionBind;

            // 12-oracle consensus fan-out (parity with macOS AppModel.runLookup lines 1011-1044).
            // ProphetConsensus.RunAsync queries the full 12-oracle roster in parallel and builds
            // the resolverResponses list shown in the consensus card. The median latency is carried
            // through from ProbePlan.Latency; null when the latency probe was skipped or timed out
            // (B231: null renders as "—", never "0ms").
            int? medianLatencyMs = plan.Latency is { Status: not ResolverLatencyStatus.Untested } lat
                ? (int)Math.Round(lat.MedianMs)
                : null;
            ProphetConsensusResult = await ProphetConsensus.RunAsync(Domain, qtype, resolver, medianLatencyMs);

            // DNSSEC posture for the duplicate result card: reuse the DNSSECStatus already fetched
            // by ProbePlan.RunProphetAsync (ProbePlan.cs:51) instead of issuing a second
            // DNSSECVerifier.CheckStatusAsync round-trip. The macOS gold standard issues a separate
            // query (AppModel.swift:1047); on Windows we reuse plan.Dnssec to avoid a redundant
            // network call. Both paths query the same resolver with the DO bit set.
            DnssecStatus = plan.Dnssec;

            // Single notification after all probes complete
            OnPropertyChanged(nameof(SectionInputs));
            ProphetRunState = ProbeRunState.Completed;
        }
        catch (Exception ex)
        {
            // E132: a probe that failed must not render as one that never ran.
            ProphetRunState = ProbeRunState.Failed;
            ProphetRunMessage = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "prophet.run") ?? ex.GetType().Name;
        }
        finally
        {
            SentrySdk.AddBreadcrumb(message: "Prophet scan complete", category: "gui.state", level: BreadcrumbLevel.Info);
            IsRunning = false;
            ScanCorrelationId = null;
        }
    }

    [RelayCommand]
    private async Task RunMirrorAsync()
    {
        // E130: removed !IsPro gate. ProbePlan.RunMirrorAsync already receives isPro below,
        // so Warp is withheld when not Pro but the rest of Mirror still runs — parity with macOS
        // AppModel.runMirror() which calls runCompare() unconditionally.
        if (IsRunning) return;
        ScanCorrelationId = Guid.NewGuid().ToString();
        IsRunning = true;
        MirrorRunState = ProbeRunState.Running;
        MirrorRunMessage = string.Empty;
        SentrySdk.AddBreadcrumb(message: "Mirror scan started", category: "gui.state", level: BreadcrumbLevel.Info);
        try
        {
            var local = NetworkKeyState.GetSystemDNSServers().FirstOrDefault();
            if (local == null)
            {
                // D278 parity (macOS AppModel.swift:939-944): never fabricate a resolver. Refuse to
                // run so an unaudited resolver is never presented as audited.
                MirrorRunState = ProbeRunState.Failed;
                MirrorRunMessage = "System resolver could not be determined. Check your network connection, then retry.";
                return;
            }
            var plan = await ProbePlan.RunMirrorAsync(Domain, local, isPro: IsPro, targetURL: $"https://{Domain}");
            _sectionInputs.Comparison = plan.Comparison;
            _sectionInputs.TargetDomain = Domain;   // Stage-5 answer-matrix query context (type defaults to A)
            _sectionInputs.Authoritative = plan.Authoritative;
            _sectionInputs.ServerGroundTruth = plan.ServerGroundTruth;
            _sectionInputs.Svcb = plan.Svcb;
            _sectionInputs.TtlAnomaly = plan.TtlAnomaly;
            _sectionInputs.CnameChain = plan.CnameChain;
            _sectionInputs.Warp = plan.Warp;
            // UI-PRELUDE: ProbePlan populates the WARP_MANIFEST_V1 result, not the retired
            // proxy-path Warp field (always null). Without this assignment a completed Mirror run
            // rendered the never-ran branch. The legacy assignment above stays while the Mirror
            // View surface (owned by the UI stream) migrates to WarpManifest.
            _sectionInputs.WarpManifest = plan.WarpManifest;

            OnPropertyChanged(nameof(SectionInputs));
            MirrorRunState = ProbeRunState.Completed;
        }
        catch (Exception ex)
        {
            // E132: a probe that failed must not render as one that never ran.
            MirrorRunState = ProbeRunState.Failed;
            MirrorRunMessage = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "mirror.run") ?? ex.GetType().Name;
        }
        finally
        {
            SentrySdk.AddBreadcrumb(message: "Mirror scan complete", category: "gui.state", level: BreadcrumbLevel.Info);
            IsRunning = false;
            ScanCorrelationId = null;
        }
    }

    [RelayCommand]
    private async Task RunPortalAsync()
    {
        if (IsRunning) return;
        ScanCorrelationId = Guid.NewGuid().ToString();
        IsRunning = true;
        PortalRunState = ProbeRunState.Running;
        PortalRunMessage = string.Empty;
        SentrySdk.AddBreadcrumb(message: "Portal scan started", category: "gui.state", level: BreadcrumbLevel.Info);
        try
        {
            var plan = await ProbePlan.RunPortalAsync();
            PortalReport = plan.Network;
            _sectionInputs.Network = plan.Network;
            _sectionInputs.EncryptedTransport = plan.EncryptedTransport;
            _sectionInputs.OpenResolver = plan.OpenResolver;

            OnPropertyChanged(nameof(SectionInputs));
            PortalRunState = ProbeRunState.Completed;
        }
        catch (Exception ex)
        {
            // E132: a probe that failed must not render as one that never ran.
            PortalRunState = ProbeRunState.Failed;
            PortalRunMessage = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "portal.run") ?? ex.GetType().Name;
        }
        finally
        {
            SentrySdk.AddBreadcrumb(message: "Portal scan complete", category: "gui.state", level: BreadcrumbLevel.Info);
            IsRunning = false;
            ScanCorrelationId = null;
        }
    }

    [RelayCommand]
    private async Task RunGluttonAsync()
    {
        if (IsRunning) return;
        ScanCorrelationId = Guid.NewGuid().ToString();
        IsRunning = true;
        GluttonRunState = ProbeRunState.Running;
        GluttonRunMessage = string.Empty;
        SentrySdk.AddBreadcrumb(message: "Glutton scan started", category: "gui.state", level: BreadcrumbLevel.Info);
        try
        {
            var servers = NetworkKeyState.GetSystemDNSServers();
            var budget = ProbeBudget.DefaultGlutton;

            LeakTestSubdomain = LeakDetector.GenerateTestSubdomain();

            var probes = await ProbeRunner.RunAsync<Dictionary<string, bool>>("triggerQueries", budget, async () =>
                (Dictionary<string, bool>?)await LeakDetector.TriggerQueriesAsync(LeakTestSubdomain, servers)) ?? new Dictionary<string, bool>();
            LeakProbeResults.Clear();
            foreach (var (server, ok) in probes)
                LeakProbeResults.Add(new(server, ok));

            // Full collector test requires a collector URL and returns a result with signature.
            // E026: use SyxxConfig.DimensionBaseURL (the canonical base host) instead of a
            // hardcoded path. LeakDetector appends /api/leak/token and /api/leak/verify, so
            // passing a URL that already includes /api/leak doubled the path and made the
            // collector unreachable.
            var result = await ProbeRunner.RunAsync<LeakTestResult>("collector", budget, async () =>
                (LeakTestResult?)await LeakDetector.RunCollectorTestAsync(
                    LeakTestSubdomain, servers, SyxxConfig.DimensionBaseURL,
                    licenseKey: string.IsNullOrEmpty(LicenseKey) ? null : LicenseKey));
            _sectionInputs.Leak = result;
            OnPropertyChanged(nameof(SectionInputs));
            GluttonRunState = ProbeRunState.Completed;
        }
        catch (Exception ex)
        {
            // E132: a probe that failed must not render as one that never ran.
            GluttonRunState = ProbeRunState.Failed;
            GluttonRunMessage = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "glutton.run") ?? ex.GetType().Name;
        }
        finally
        {
            SentrySdk.AddBreadcrumb(message: "Glutton scan complete", category: "gui.state", level: BreadcrumbLevel.Info);
            IsRunning = false;
            ScanCorrelationId = null;
        }
    }

    [RelayCommand]
    private void GenerateSynthesis()
    {
        Conclusion = Synthesizer.Conclude(SectionInputs);
        var determination = Conclusion?.Determination;
        SentrySdk.AddBreadcrumb(message: "Synthesis concluded", category: "engine.result", level: BreadcrumbLevel.Info,
            data: new Dictionary<string, string>
            {
                ["result"] = determination?.State.ToString() ?? "none",
                ["section_count"] = (SectionInputs?.Count() ?? 0).ToString(),
                ["engine_version"] = "matrix.1"
            });
    }

    [RelayCommand]
    private void ResetConclusion() => Conclusion = null;

    /// Resets every input and output to its just-opened state — the "Start your story anew"
    /// header action. Mirrors macOS AppModel.resetAll(). Behaviour only: clears state, no probes.
    [RelayCommand]
    private void ResetAll()
    {
        Domain = string.Empty;
        Server = string.Empty;
        RecordType = "A";
        LicenseKey = string.Empty;

        SectionInputs = new();
        PortalReport = null;
        Conclusion = null;

        // E132: reset per-module probe run states so a "Start your story anew" returns to idle.
        ProphetRunState = ProbeRunState.Idle;
        ProphetRunMessage = string.Empty;
        MirrorRunState = ProbeRunState.Idle;
        MirrorRunMessage = string.Empty;
        PortalRunState = ProbeRunState.Idle;
        PortalRunMessage = string.Empty;
        GluttonRunState = ProbeRunState.Idle;
        GluttonRunMessage = string.Empty;

        LeakTestSubdomain = string.Empty;
        LeakProbeResults.Clear();

        CageCapturing = false;
        CageDuration = 30;
        CageEventsCount = 0;
        CageRows.Clear();

        // Note: the witness key is DPAPI-backed (WitnessKeyStore) and deliberately NOT cleared here,
        // mirroring macOS resetAll() which leaves the Keychain key intact.
        WitnessSignature = string.Empty;
        ShowTestimony = false;
        ShowValidation = false;
        WitnessValid = false;
        WitnessSnapshot = null;
        WitnessTestimony = WitnessTestimonyState.Idle;
        WitnessTestimonyMessage = string.Empty;

        IsRunning = false;
    }

    /// <summary>
    /// Re-reads the DPAPI key-store's presence flag into <see cref="HasWitnessKey"/>. Called at
    /// construction (D052-adjacent Windows defect: a key persisted in a PREVIOUS run must not read
    /// as "no signing key" just because this view-model instance never re-saved it) and again on
    /// every Witness view navigation, and from <see cref="WitnessVerify"/> immediately before it is
    /// consulted, so the property is never allowed to answer from a stale cached value.
    /// </summary>
    public void RefreshWitnessKeyStatus() => HasWitnessKey = WitnessKeyStore.HasKey;

    /// <summary>
    /// Persists a user-supplied signing key via DPAPI (WitnessKeyStore). The plaintext key never
    /// lives on the view-model; only <see cref="HasWitnessKey"/> is exposed. On failure the key is
    /// NOT saved and downstream flows correctly no-op as if no key had been entered, but the reason
    /// is now captured in <see cref="WitnessKeyError"/> instead of being silently discarded. (D015/D052)
    /// </summary>
    public void SaveWitnessKey(string base64Key)
    {
        // Reset the failure sentinel BEFORE the empty-input early-return, not after — otherwise a
        // stale WitnessKeySaveFailed from a prior failed attempt would linger when the user clears
        // the field and re-submits empty, and the save-gate (WitnessView.SaveKeyBoxToViewModel)
        // would keep refusing to seal even though there is now nothing to save.
        WitnessKeyError = string.Empty;
        WitnessKeySaveFailed = false;
        var trimmed = base64Key?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return;
        try { WitnessKeyStore.SaveKey(trimmed); }
        catch (System.Exception ex)
        {
            // Defect-2 parity (macOS AppModel.saveWitnessKey / ErrorPresenter.swift): the full
            // technical detail goes to the diagnostic sink; only text a person can read reaches the
            // screen. A DPAPI CryptographicException's message is crash-reporter material, so the
            // detail may legitimately be empty — the FAILURE is carried by WitnessKeySaveFailed.
            WitnessKeySaveFailed = true;
            WitnessKeyError = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "witness.saveKey")
                              ?? string.Empty;
        }
        RefreshWitnessKeyStatus();
    }

    // E045: generate a new Ed25519 keypair and store the private key via DPAPI.
    // Parity with macOS AppModel.generateWitnessKey() / WitnessKeyManager.generateAndStoreKey().
    // Surfaces the public key so the user can share it for trust-set configuration.
    public void GenerateWitnessKey()
    {
        WitnessKeyError = string.Empty;
        WitnessKeySaveFailed = false;
        try
        {
            var (priv, pub) = WitnessSigner.GenerateKeyPair();
            WitnessKeyStore.SaveKey(priv);
            HasWitnessKey = true;
            // Surface the public key as the conclusion text — the private key is already
            // persisted and must not linger in view state.
            WitnessTestimony = WitnessTestimonyState.Idle;
            WitnessTestimonyMessage = $"New Ed25519 keypair generated. Public key: {pub}";
        }
        catch (Exception ex)
        {
            WitnessKeySaveFailed = true;
            WitnessKeyError = SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "witness.generateKey")
                              ?? "Could not generate signing key.";
        }
        RefreshWitnessKeyStatus();
    }

    [RelayCommand]
    private async Task WitnessTestifyAsync()
    {
        var generation = _witnessGeneration;
        ShowValidation = false;
        ShowTestimony = true;
        WitnessSignature = string.Empty;
        WitnessSnapshot = null;
        WitnessTestimony = WitnessTestimonyState.Running;
        WitnessTestimonyMessage = string.Empty;

        // Load the signing key transiently from DPAPI storage; null ⇒ no usable key. Checked before
        // the live probe so a missing key fails fast rather than after a network round trip.
        var key = WitnessKeyStore.LoadKey();
        if (string.IsNullOrEmpty(key))
        {
            // B195: parity with macOS witnessTestify()'s missing-key guard (approved copy, ported).
            WitnessTestimony = WitnessTestimonyState.Failed;
            WitnessTestimonyMessage = "A signing key is required — a snapshot signed with a default/empty key would be forgeable.";
            return;
        }

        // D051: sign a LIVE comparison taken right now for the CURRENT domain — never reuse
        // _sectionInputs.Comparison, which may hold a stale result from a Mirror run against a
        // different domain (or none at all). Same call path RunMirrorAsync() uses to invoke the
        // vantage comparison (ProbeRunner.RunAsync over VantageComparer.CompareVantagePointsAsync),
        // so a transport failure here degrades the same way a Mirror probe failure does — it does
        // not crash the command.
        var name = Domain.Trim();
        var local = NetworkKeyState.GetSystemDNSServers().FirstOrDefault();
        if (local == null)
        {
            // D278 parity (macOS AppModel.swift:939-944): never fabricate a resolver. Refuse to
            // seal a witness over a vantage comparison made against an unaudited resolver.
            WitnessTestimony = WitnessTestimonyState.Failed;
            WitnessTestimonyMessage = "System resolver could not be determined. Check your network connection, then retry.";
            return;
        }
        var budget = ProbeBudget.DefaultMirror;
        var comparison = await ProbeRunner.RunAsync<ComparisonResult>("vantage", budget, async () =>
        {
            try { return await _compareVantagePoints(name, local); }
            catch { return null; }
        });

        if (comparison is null)
        {
            // B195: no live comparison to seal — reuses the existing approved wording (previously
            // shown when _sectionInputs.Comparison was null; now covers an unresolved/empty domain
            // at sign time, which is the same underlying condition).
            WitnessTestimony = WitnessTestimonyState.Failed;
            WitnessTestimonyMessage = "Nothing to witness — the domain did not resolve, so there is no result to seal.";
            return;
        }

        try
        {
            // Steps 5-7 (sign → interpret redirection → build drift) are the pure determinative core,
            // single-sourced in Syxx.Kit.WitnessTestifyPlanner (parity twin of macOS). The drift
            // population (D093) is now a field of the returned Outcome rather than a separate inlined
            // block that a future hand-edit could drop again; a Kit-level unit test pins it on CI.
            var outcome = WitnessTestifyPlanner.Seal(name, local, comparison, key);

            // Fix 1 (WitnessFree ordering race): if WitnessFree() was called while we were
            // awaiting the network probe, discard the stale outcome. The generation counter is
            // incremented by WitnessFree() and captured at the top of this method.
            if (generation != _witnessGeneration)
            {
                WitnessTestimony = WitnessTestimonyState.Idle;
                WitnessTestimonyMessage = string.Empty;
                return;
            }

            WitnessSnapshot = outcome.Snapshot;
            WitnessSignature = outcome.Snapshot.Signature;
            WitnessTestimony = WitnessTestimonyState.Signed;
            _sectionInputs.Drifts = outcome.Drift is null
                ? new List<AnswerDrift>()
                : new List<AnswerDrift> { outcome.Drift };
            OnPropertyChanged(nameof(SectionInputs));
        }
        catch (System.Exception ex)
        {
            // B195: parity with macOS witnessTestify()'s catch (approved copy, ported). Previously an
            // empty catch that dropped the error entirely.
            //
            // Defect-2 parity: the cause is now presented, not raw. A WitnessSignerException's
            // message ("Exception of type 'Syxx.Kit.WitnessSignerException' was thrown.") is
            // crash-reporter material; the full detail goes to the diagnostic sink and the user gets
            // the bare, honest failure when there is no readable cause — never an invented one.
            WitnessTestimony = WitnessTestimonyState.Failed;
            WitnessTestimonyMessage = SyxxUI.Presentation.ErrorPresenter.Compose(
                "Could not create witness snapshot.",
                SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "witness.testify"));
        }
    }

    [RelayCommand]
    private void WitnessVerify()
    {
        ShowValidation = true;
        // Refresh from the live DPAPI store immediately before use — HasWitnessKey is the single
        // property both this command and the view's result rendering read, so it must never be
        // answered from a value cached at construction or from a previous SaveWitnessKey call.
        RefreshWitnessKeyStatus();
        if (WitnessSnapshot is null || !HasWitnessKey)
        { WitnessValid = false; WitnessVerification = null; return; }
        // E021: keep the DISTINCT outcome (expired vs stale vs untrusted vs forged) so the
        // surface can render forensically different failures differently.
        var detailed = WitnessSigner.VerifySnapshotDetailed(WitnessSnapshot);
        WitnessVerification = detailed;
        WitnessValid = detailed == SnapshotVerification.Valid;
    }

    /// <summary>
    /// #22 (Windows parity with Linux WitnessViewModel.VerifyExternal): verify a testimony sealed on
    /// another device by pasting its snapshot JSON. Deserializes with the canonical case-insensitive
    /// reader contract and routes the outcome through the SAME
    /// <see cref="WitnessSigner.VerifySnapshotDetailed"/> and result switch as self-verify, so a third
    /// party's seal is judged by exactly the same rules and rendered by the same code
    /// (WitnessView.UpdateValidation reads WitnessSnapshot/WitnessVerification/WitnessValid).
    /// Synchronous: JSON parse + Ed25519 verify are CPU-bound and fast, matching WitnessVerify.
    ///
    /// Unreadable input (empty, malformed JSON, or a JSON <c>null</c>) folds into
    /// <see cref="SnapshotVerification.MalformedInput"/> — an honest inconclusive rendered by the
    /// same switch, never a green pass and never a crash. WitnessSnapshot is left null in that case
    /// so the switch's MalformedInput branch (not the "no snapshot / press Testimony" prompt) fires.
    /// </summary>
    [RelayCommand]
    private void WitnessVerifyExternal()
    {
        ShowValidation = true;

        WitnessSnapshot? parsed = null;
        if (!string.IsNullOrWhiteSpace(ExternalSnapshotJson))
        {
            try
            {
                parsed = System.Text.Json.JsonSerializer.Deserialize<WitnessSnapshot>(
                    ExternalSnapshotJson, ExternalVerifyReaderOptions);
            }
            catch (System.Text.Json.JsonException)
            {
                // Unreadable bytes are a MalformedInput outcome, not a guessed result — fall through
                // with parsed still null.
            }
        }

        if (parsed is null)
        {
            // Empty / malformed / JSON-null: honest inconclusive via the shared switch. Snapshot is
            // deliberately left null; the UpdateValidation "no verification outcome" and "no snapshot"
            // guards both key off WitnessVerification, so a non-null MalformedInput here renders the
            // malformed message rather than the pre-seal prompt.
            WitnessSnapshot = null;
            WitnessVerification = SnapshotVerification.MalformedInput;
            WitnessValid = false;
            return;
        }

        WitnessSnapshot = parsed;
        var detailed = WitnessSigner.VerifySnapshotDetailed(parsed);
        WitnessVerification = detailed;
        WitnessValid = detailed == SnapshotVerification.Valid;
    }

    [RelayCommand]
    private void WitnessFree()
    {
        // DNS cache flush is CLI-level (ipconfig /flushdns). Instruct user.
        // Nothing safe to call from the GUI on Windows without admin elevation.
        // Increment FIRST so any concurrently running WitnessTestifyAsync() sees the
        // generation change and discards its stale outcome (Fix 1: ordering race).
        _witnessGeneration++;
        ShowTestimony = false;
        ShowValidation = false;
        WitnessTestimony = WitnessTestimonyState.Idle;
        WitnessTestimonyMessage = string.Empty;
        // Previously left WitnessSnapshot/WitnessSignature/WitnessValid/WitnessVerification
        // untouched, so a stale result from before Free reappeared on the next Verify press
        // before the button was clicked again. Free must actually free the prior testimony.
        // Clear verification BEFORE snapshot to avoid a race where the view reads a null snapshot
        // but stale verification state (Fix 1: WitnessFree ordering race).
        WitnessVerification = null;
        WitnessValid = false;
        WitnessSignature = string.Empty;
        WitnessSnapshot = null;
    }

    /// <summary>
    /// B096: maps the record-type picker string to its DNS qtype number so the selected
    /// type reaches ProbePlan instead of always hardcoding A (type 1).
    /// </summary>
    private static ushort MapRecordTypeToQtype(string recordType) => recordType.ToUpperInvariant() switch
    {
        "A"     => 1,
        "AAAA"  => 28,
        "MX"    => 15,
        "NS"    => 2,
        "TXT"   => 16,
        "CNAME" => 5,
        _       => 1,
    };
}

/// <summary>
/// B195: honest lifecycle state of the Witness Testimony (sealing) action. Parity twin of macOS
/// AppModel.WitnessTestimonyState. The Failed reason travels in
/// <see cref="AppViewModel.WitnessTestimonyMessage"/> rather than as an enum payload (C# enums
/// carry no associated value).
/// </summary>
public enum WitnessTestimonyState
{
    Idle,
    Running,
    Signed,
    Failed
}

// ──────────────────────────────────────────────────────────────────────────────
// MARK: Cage row UI model
// ──────────────────────────────────────────────────────────────────────────────

public sealed record CageRow(
    Guid Id,
    string Time,
    string Domain,
    string Note,
    string Process,
    string Resolver,
    Severity Severity);

// ──────────────────────────────────────────────────────────────────────────────
// MARK: Probe result row UI model
// ──────────────────────────────────────────────────────────────────────────────

public sealed record LeakProbeRow(string Server, bool Sent);

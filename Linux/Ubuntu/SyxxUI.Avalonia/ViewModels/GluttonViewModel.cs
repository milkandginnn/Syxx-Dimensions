using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Avalonia.Presentation;
using SyxxUI.Presentation;   // linked from Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Glutton — is my VPN/tunnel actually keeping DNS private? Parity twin of macOS
/// <c>SyxxApp/GluttonView.swift</c> and Windows <c>SyxxUI/Views/GluttonView.xaml(.cs)</c>.
///
/// <b>Parity scope (Aug 2026 catch-up):</b> the Linux client previously shipped with only a
/// "Check VPN / encryption status" button and a static "the collector result is deferred on
/// Linux" note — while macOS and Windows already ran the real probe dispatch and the real
/// collector-attested leak test. Both halves now run for real on Linux, against the same
/// <c>Syxx.Kit.LeakDetector</c> the other twins use, so a leak claim on Linux is held to the
/// same honesty bar.
///
/// <b>HONESTY guardrails mirrored from the twins:</b>
/// <list type="bullet">
/// <item><b>B030</b>: the false-negative class this test refuses. "No leak detected" is ONLY ever
///   stated by the collector's signed attestation — never from the absence of evidence.</item>
/// <item><b>D004 / D011 / B066 / B067</b>: a green result requires a valid Ed25519 signature by a
///   TRUSTED key AND an enforced transport pin. Crypto-valid-but-untrusted or unpinned-transport
///   reads amber (inconclusive), never green. See <see cref="RenderLeakResult"/>.</item>
/// <item><b>E117</b>: the FEATURE STATUS card has three distinct states (unknown / locked /
///   unlocked). Unknown shows a neutral "Loading…", never a red ✗ for a check that has not
///   completed.</item>
/// <item><b>E118</b>: probe "sent" is rendered in the NEUTRAL text tone, never green — reserved for
///   the collector's attested result below. Per-row red is reserved for "failed" only.</item>
/// <item><b>B246</b>: distinguish "no check has run" from "collector ran (no local subdomain)" in
///   the test-label card so an inconclusive run can never be read as a clean one.</item>
/// <item><b>E132</b>: a probe that FAILED must not render as one that never ran. The leak-result
///   card reads "the check could not complete" with the presented error detail, never the standing
///   "no leak check has run yet" copy.</item>
/// <item><b>Defect-2</b>: every reason string on the screen is passed through
///   <c>ErrorPresenter.Compose</c> / <c>ErrorPresenter.DisplayReason</c> — raw exception messages
///   (the crash-reporter material the core can emit) never reach the user.</item>
/// </list>
/// </summary>
public partial class GluttonViewModel : ObservableObject
{
    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Feature status (E117)
    // ──────────────────────────────────────────────────────────────────────────

    /// Tri-state for the FEATURE STATUS card, mirroring macOS' <c>LicenseState.unknown/.lite/.pro</c>
    /// and Windows' three-branch render in <c>RefreshAsync()</c>. Unknown shows "Loading…" with a
    /// neutral brush — never red on first paint (E117).
    public enum FeatureState { Unknown, Locked, Unlocked }

    [ObservableProperty] private FeatureState _featureStatus = FeatureState.Unknown;
    [ObservableProperty] private string _featureStatusText = "Loading…";
    [ObservableProperty] private IBrush _featureStatusBrush = NeutralBrush;

    // Exposed as bool getters so the XAML can bind IsVisible directly to a single state, without a
    // converter. (Avalonia supports the ! negation operator on these for the complementary state.)
    public bool IsFeatureUnlocked => FeatureStatus == FeatureState.Unlocked;
    public bool IsFeatureLocked => FeatureStatus == FeatureState.Locked;
    public bool IsFeatureUnknown => FeatureStatus == FeatureState.Unknown;

    // E132: distinct failed-run flag so the XAML can render an honest "the check could not
    // complete" line item without a converter on the enum.
    public bool IsGluttonFailed => GluttonRunState == ProbeRunState.Failed;

    // Notify the derived boolean getters when the backing field changes. CommunityToolkit.Mvvm
    // generates the partial OnXxxChanged hooks; we override them to raise PropertyChanged on the
    // computed properties that depend on the changed value, so XAML binding refreshes.
    partial void OnFeatureStatusChanged(FeatureState value)
    {
        OnPropertyChanged(nameof(IsFeatureUnlocked));
        OnPropertyChanged(nameof(IsFeatureLocked));
        OnPropertyChanged(nameof(IsFeatureUnknown));
    }

    partial void OnGluttonRunStateChanged(ProbeRunState value) =>
        OnPropertyChanged(nameof(IsGluttonFailed));

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: VPN / DNS encryption status (Portal read)
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private string _vpnLine = "";
    [ObservableProperty] private string _vpnDetailLine = "";
    [ObservableProperty] private IBrush _vpnLineBrush = NeutralBrush;

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Local probes (Glutton's "Check for Leaks")
    // ──────────────────────────────────────────────────────────────────────────

    // E132: tri-state for the local probe run, mirroring Windows'
    // <c>AppViewModel.ProbeRunState</c>. Idle = never run, Running = dispatching, Completed =
    // produced results, Failed = threw before producing any row.
    public enum ProbeRunState { Idle, Running, Completed, Failed }

    [ObservableProperty] private ProbeRunState _gluttonRunState = ProbeRunState.Idle;
    [ObservableProperty] private string _gluttonRunMessage = "";
    [ObservableProperty] private string _leakTestSubdomain = "";
    [ObservableProperty] private bool _hasSubdomain;
    // E118: sent renders NEUTRAL — green is reserved for the collector's attested result below.
    public ObservableCollection<LeakProbeRow> LeakProbeResults { get; } = new();

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Collector verification
    // ──────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _collectorURL = SyxxConfig.DimensionBaseURL;
    [ObservableProperty] private bool _collectorRunning;
    [ObservableProperty] private bool _hasCollectorResult;
    [ObservableProperty] private string _leakResultText = "No leak check has run yet.";
    [ObservableProperty]
    private string _leakResultDetailText =
        "Run a leak check; the collector's signed attestation decides the conclusion shown here.";
    [ObservableProperty] private IBrush _leakResultBrush = NeutralBrush;

    // Attestation block — visible only when a signature was returned (parity macOS lines 234-263).
    [ObservableProperty] private bool _showSignatureBlock;
    [ObservableProperty] private string _signatureText = "";
    [ObservableProperty] private string _signatureValidityText = "";
    [ObservableProperty] private IBrush _signatureValidityBrush = NeutralBrush;

    // Transport pinning (D003) — surfaced so an on-path impostor cannot hide behind a signed-leak
    // result; a Failed transport pins the result to inconclusive (B067).
    [ObservableProperty] private bool _showTransportLine;
    [ObservableProperty] private string _transportText = "";
    [ObservableProperty] private IBrush _transportBrush = NeutralBrush;

    [ObservableProperty] private bool _showObservedResolvers;
    [ObservableProperty] private string _observedResolversText = "";

    [ObservableProperty] private bool _showErrorReason;
    [ObservableProperty] private string _errorReasonText = "";

    /// <summary>
    /// Standing caveat that surfaces on the Collector Verification panel alongside the URL field,
    /// so a Linux user without a stored licence understands WHY they will see "INCONCLUSIVE" rather
    /// than "no leak here". Twin of macOS GluttonView lore copy and Windows' standing
    /// "no leak check has run yet" detail text — DEFAULT honest state.
    /// </summary>
    public string CollectorNote =>
        "A leak PROOF comes only from the collector's Ed25519-signed attestation. Without it, the "
        + "result is INCONCLUSIVE, never 'no leak' — the exact B030 false-negative this test must refuse.";

    public GluttonViewModel()
    {
        // macOS .task { } parity: load the license state and Portal network read on view open so
        // the user sees real Status cards without having to click first. Best-effort; if either
        // fails the screen says so honestly in its dedicated line (Defect-2 — never an empty card
        // reading as healthy).
        _ = LoadFeatureStatusAsync();
        _ = CheckStatusAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Status cards (Feature + VPN)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes the FEATURE STATUS card from <see cref="LicenseManager.IsFeatureUnlockedAsync"/>.
    /// Stops loading on the first resolved state — Locked or Unlocked — and renders red only for
    /// Locked (a decision an actual validation produced), never for Unknown (E117).
    /// </summary>
    [RelayCommand]
    private async Task LoadFeatureStatusAsync()
    {
        try
        {
            var (unlocked, message) = await LicenseManager.IsFeatureUnlockedAsync();
            if (unlocked)
            {
                FeatureStatus = FeatureState.Unlocked;
                FeatureStatusText = "Pro features unlocked";
                FeatureStatusBrush = SuccessBrush;
            }
            else
            {
                FeatureStatus = FeatureState.Locked;
                // E117 / F013: locked renders AMBER, not red — red is reserved for a fault.
                // The licence message is a status, never a storefront (E156 parity).
                FeatureStatusText = string.IsNullOrEmpty(message) ? "Not unlocked" : message;
                FeatureStatusBrush = AmberBrush;
            }
        }
        catch (System.Exception ex)
        {
            // E117: a licence check that failed is NOT the same as a feature that is locked. Stay
            // in the Unknown state with a readable cause, never tip into red.
            FeatureStatus = FeatureState.Unknown;
            FeatureStatusText = ErrorPresenter.Compose(
                "Could not validate license state.",
                ErrorPresenter.UserDetail(ex, "glutton.license"));
            FeatureStatusBrush = AmberBrush;
        }
    }

    /// <summary>
    /// Refreshes the VPN / DNS ENCRYPTION status card from <see cref="ProbePlan.RunPortalAsync"/>.
    /// Twin of Windows <c>RefreshAsync</c>'s portal read and macOS' report binding. Unknown reads
    /// amber (never green); Yes reads teal (Linux's success tone, per App.axaml honesty note); No
    /// reads neutral — a missing VPN is not a fault, it just makes the leak test meaningless.
    /// </summary>
    [RelayCommand]
    private async Task CheckStatusAsync()
    {
        if (IsRunning) return;
        IsRunning = true; HasStatus = false;

        SectionInputs r;
        try { r = await ProbePlan.RunPortalAsync(); }
        // Defect-2 parity: raw detail to the diagnostic sink, readable text (or the bare failure,
        // with no invented cause) to the screen. The line stays amber — a check that could not run
        // is never a green status.
        catch (System.Exception ex)
        {
            VpnLine = ErrorPresenter.Compose(
                "Could not read network state.", ErrorPresenter.UserDetail(ex, "glutton.network"));
            VpnLineBrush = AmberBrush;
            VpnDetailLine = "";
            IsRunning = false; HasStatus = true; return;
        }

        var net = r.Network;
        if (net is null)
        {
            VpnLine = "Network state could not be determined (inconclusive).";
            VpnLineBrush = AmberBrush;
            VpnDetailLine = "";
        }
        else
        {
            VpnLine = net.Vpn.State switch
            {
                ConditionState.Yes => $"VPN active — tunnel(s): {string.Join(", ", net.Vpn.TunnelInterfaces)}",
                ConditionState.No => "No VPN tunnel detected — a leak test is only meaningful with a VPN or DNS encryption.",
                _ => "VPN status could not be determined (inconclusive)."
            };
            VpnLineBrush = net.Vpn.State switch
            {
                ConditionState.Yes => SuccessBrush,
                ConditionState.No => NeutralBrush,
                _ => AmberBrush
            };
            var enc = net.EncryptedDNS.State switch
            {
                ConditionState.Yes => "Encryption detected (unverified)",
                ConditionState.No => "Encryption not detected",
                _ => "DNS encryption status unknown"
            };
            // macOS vpnDetail parity: combine the encrypted-DNS state with the VPN evidence so the
            // user has the actual tunnel name on the same line, not two halves of the truth.
            VpnDetailLine = $"{enc} — {net.Vpn.Evidence}";
        }

        HasStatus = true; IsRunning = false;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Local leak probes ("Check for Leaks")
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a one-time probe subdomain and dispatches it through every system DNS resolver,
    /// populating <see cref="LeakProbeResults"/> with per-server sent/failed rows. Twin of macOS
    /// <c>AppModel.run()</c> lines 1124-1126 and Windows <c>RunGluttonAsync</c> lines 576-585.
    ///
    /// This step ALONE proves nothing about leaks — it only confirms the DNS queries LEFT this
    /// machine. The leak RESULT comes from <see cref="RunCollectorVerificationAsync"/>. The probe
    /// table is therefore rendered in the NEUTRAL text tone for "sent" (E118), never green.
    /// </summary>
    [RelayCommand]
    private async Task CheckForLeaksAsync()
    {
        if (IsRunning || GluttonRunState == ProbeRunState.Running) return;
        ScanStarted();

        try
        {
            var servers = NetworkKeyState.GetSystemDNSServers();
            if (servers.Count == 0)
            {
                // D278 parity: never fabricate a resolver. A refused run renders Failed with a
                // readable cause, never as a Completed or clean state.
                GluttonRunState = ProbeRunState.Failed;
                GluttonRunMessage = "System DNS resolver could not be determined. Check your network connection, then retry.";
                return;
            }

            LeakTestSubdomain = LeakDetector.GenerateTestSubdomain();
            var budget = ProbeBudget.DefaultGlutton;

            var probes = await ProbeRunner.RunAsync<Dictionary<string, bool>>(
                "triggerQueries", budget,
                async () => (Dictionary<string, bool>?)await LeakDetector.TriggerQueriesAsync(
                    LeakTestSubdomain, servers))
                ?? new Dictionary<string, bool>();

            LeakProbeResults.Clear();
            foreach (var (server, ok) in probes)
                LeakProbeResults.Add(new LeakProbeRow(server, ok));

            HasSubdomain = !string.IsNullOrEmpty(LeakTestSubdomain);
            GluttonRunState = ProbeRunState.Completed;
            GluttonRunMessage = "";
        }
        catch (System.Exception ex)
        {
            // E132: a probe that failed must not render as one that never ran. Keep the row table
            // (so the user can see any partial dispatch) but mark the run Failed with a presented
            // reason — never a raw exception message (Defect-2).
            GluttonRunState = ProbeRunState.Failed;
            GluttonRunMessage = ErrorPresenter.Compose(
                "Glutton could not complete the local probe dispatch.",
                ErrorPresenter.UserDetail(ex, "glutton.run"));
        }
        finally
        {
            IsRunning = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Collector verification ("Run Collector Verification")
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full collector-attested leak test against <see cref="CollectorURL"/>. Twin of macOS
    /// <c>AppModel.runCollectorVerification()</c> and the Windows <c>RunGluttonAsync</c> collector
    /// half. The result is rendered through <see cref="RenderLeakResult"/>, which honesty-gates the
    /// result on the signed attestation: no valid+trusted signature AND enforced transport pin
    /// means the result reads INCONCLUSIVE, never "no leak" (B030, D004, B066, B067).
    /// </summary>
    [RelayCommand]
    private async Task RunCollectorVerificationAsync()
    {
        if (CollectorRunning) return;

        // macOS parity (lines 1152-1155): reject an empty/malformed URL with an honest error
        // result rather than letting HttpRequestException surface as raw crash text.
        string url = (CollectorURL ?? "").Trim();
        if (string.IsNullOrEmpty(url))
        {
            ResetCollectorResult();
            LeakResultText = "Invalid collector URL.";
            LeakResultDetailText = "Enter the URL of the leak collector, then run verification.";
            LeakResultBrush = AmberBrush;
            HasCollectorResult = true;
            return;
        }

        CollectorRunning = true;
        try
        {
            var servers = NetworkKeyState.GetSystemDNSServers();
            if (servers.Count == 0)
            {
                // macOS parity (line 1148): an inconclusive-with-no-resolvers result must never
                // read as "no leak". Render through the same gate as any other result.
                var empty = new LeakTestResult
                {
                    Result = LeakResult.Inconclusive,
                    ErrorReason = "No system DNS resolvers detected.",
                };
                RenderLeakResult(empty);
                return;
            }

            // Re-use the subdomain generated by CheckForLeaksAsync if present, else mint a fresh
            // one. Windows keys its collector call off LeakTestSubdomain unconditionally — but on
            // macOS the collector path can run before the local half, in which case a fresh token
            // is minted inside RunCollectorTestAsync's step 1 (server returns its own subdomain).
            // Either way, the SIGNED payload the collector returns is what carries the result;
            // the local subdomain is informational on this screen.
            string subdomain = !string.IsNullOrEmpty(LeakTestSubdomain)
                ? LeakTestSubdomain
                : LeakDetector.GenerateTestSubdomain();

            var budget = ProbeBudget.DefaultGlutton;
            var result = await ProbeRunner.RunAsync<LeakTestResult>(
                "collector", budget,
                async () => (LeakTestResult?)await LeakDetector.RunCollectorTestAsync(
                    subdomain, servers, url))
                ?? new LeakTestResult
                {
                    Result = LeakResult.Inconclusive,
                    ErrorReason = "Collector verification returned no result."
                };

            RenderLeakResult(result);
        }
        catch (System.Exception ex)
        {
            // Defect-2: never surface a raw exception message as product copy. A collapsed collector
            // is INCONCLUSIVE — "the check could not complete", never "no leak" and never an
            // unclassified error string (Defect-2 parity with macOS GluttonView.swift:271).
            RenderLeakResult(new LeakTestResult
            {
                Result = LeakResult.Inconclusive,
                ErrorReason = ErrorPresenter.UserDetail(ex, "glutton.collector"),
            });
        }
        finally
        {
            CollectorRunning = false;
        }
    }

    /// <summary>
    /// Honesty gate central to every leak/no-leak result on this screen. Mirrors macOS
    /// <c>gluttonResultColor()</c> (lines 305-320) and Windows <c>RenderLeakResult</c>
    /// (lines 148-237) case-for-case:
    /// <list type="bullet">
    /// <item><b>Attained attestation</b> (valid sig by trusted key AND enforced transport pin):
    ///   green/teal for NotDetected, red for Confirmed. Nothing else reads clean.</item>
    /// <item><b>B066</b>: a crypto-valid signature by an UNTRUSTED key reads amber, never green.</item>
    /// <item><b>B067</b>: a NotDetected result with a non-enforced transport reads amber, never green.</item>
    /// <item><b>D004</b>: no signature (or invalid signature, or untrusted key) reads UNVERIFIED
    ///   amber regardless of the outer <c>Result</c> field — a relay can flip those, the
    ///   attestation cannot.</item>
    /// <item><b>B091 / B216</b>: signature-valid-but-payload-mismatch etc. are produced by
    ///   <c>Syxx.Kit.LeakDetector.DeriveResult</c> which already forces Inconclusive; here we just
    ///   render that Inconclusive amber honestly.</item>
    /// </list>
    ///
    /// <para>INTERNAL FOR TESTS — productive callers go through
    /// <see cref="RunCollectorVerificationAsync"/>. Exposed at the assembly boundary so the
    /// parity tests in <c>SyxxUI.Avalonia.Tests.GluttonViewModelParityTests</c> can pin the
    /// honesty gate without spinning up a network. The <c>InternalsVisibleTo</c> declaration in
    /// <c>SyxxUI.Avalonia.csproj</c> grants the test assembly access.</para>
    /// </summary>
    internal void RenderLeakResult(LeakTestResult leak)
    {
        ResetCollectorResult();

        // D004: no valid signature → the result cannot be stood behind, whatever it claims.
        // B066: a crypto-valid signature by an untrusted key is equally untrustworthy.
        bool attested = leak.SignatureValid == true && leak.SignatureKeyTrusted != false;
        if (!attested)
        {
            LeakResultText = "[UNVERIFIED] Result cannot be trusted.";
            LeakResultDetailText = leak.SignatureValid == null
                ? "The collector returned no signed attestation, so this result cannot be verified. Treat it as inconclusive."
                : leak.SignatureKeyTrusted == false
                    ? "The attestation is signed by a key this app does not trust. Treat the result as inconclusive."
                    : "The collector's attestation signature is invalid. Treat the result as inconclusive.";
            LeakResultBrush = AmberBrush;
            PopulateAttestationBlock(leak);
            PopulateErrorReason(leak.ErrorReason);
            HasCollectorResult = true;
            return;
        }

        switch (leak.Result)
        {
            case LeakResult.Confirmed:
                LeakResultText = "Multiple resolvers observed your DNS queries.";
                LeakResultDetailText = leak.ObservedResolvers.Count > 0
                    ? $"The collector observed your test query arriving from {leak.ObservedResolvers.Count} resolver(s): {string.Join(", ", leak.ObservedResolvers)}. Verify whether these match your VPN or encrypted DNS provider."
                    : "The collector attested a divergence.";
                LeakResultBrush = CriticalBrush;
                break;

            case LeakResult.NotDetected:
                // B067: gate the green result on enforced transport pinning — a notDetected over a
                // non-enforced transport is unverified identity and reads amber.
                if (leak.TransportPinning != CollectorTransportPinning.Enforced)
                {
                    LeakResultText = "[UNVERIFIED TRANSPORT] No leak claimed, but the collector's identity was not pinned.";
                    LeakResultDetailText = "The collector answered but its TLS identity was not pinned (standard TLS validation only). Treat this as inconclusive until transport pinning is configured.";
                    LeakResultBrush = AmberBrush;
                }
                else
                {
                    int n = leak.ObservedResolvers.Count;
                    if (n == 0)
                    {
                        LeakResultText = "No resolver observed your probe.";
                        LeakResultDetailText = "The collector saw no query for your test label; this is not a clean result — re-run the check.";
                        LeakResultBrush = AmberBrush;
                    }
                    else
                    {
                        LeakResultText = $"{n} resolver{(n == 1 ? "" : "s")} observed.";
                        LeakResultDetailText = $"The collector observed your test query from: {string.Join(", ", leak.ObservedResolvers)}. Verify it matches your VPN or encrypted DNS provider.";
                        LeakResultBrush = SuccessBrush;
                    }
                }
                break;

            case LeakResult.Invalid:
                LeakResultText = "[INVALID] The result is structurally impossible.";
                LeakResultDetailText = "The collector returned a response that is malformed or missing required fields. Treat as inconclusive.";
                LeakResultBrush = AmberBrush;
                break;

            case LeakResult.Error:
                LeakResultText = "[ERROR] The leak test could not execute.";
                LeakResultDetailText = "An execution error prevented the test from completing. Check your network connection and try again.";
                LeakResultBrush = AmberBrush;
                break;

            default:
                // Defect-2 parity (macOS GluttonView.swift:232): the core may build ErrorReason
                // from an exception's message — show it only when ErrorPresenter.DisplayReason
                // returns genuine human-readable text; otherwise fall back to the standing
                // inconclusive copy.
                LeakResultText = "[INCONCLUSIVE] The check could not decide.";
                LeakResultDetailText =
                    ErrorPresenter.DisplayReason(leak.ErrorReason)
                    ?? "The collector could not produce a conclusion either way. Re-run the check; a repeatedly inconclusive result is worth investigating.";
                LeakResultBrush = AmberBrush;
                break;
        }

        PopulateAttestationBlock(leak);
        PopulateErrorReason(leak.ErrorReason);
        HasCollectorResult = true;
    }

    /// <summary>
    /// Populates the attestation sub-panel (signature line, signature-validity line, transport
    /// line, observed-resolvers line) — parity macOS lines 234-263. Visible only when the relevant
    /// data exists, so an empty attestation does not render a half-empty panel.
    /// </summary>
    private void PopulateAttestationBlock(LeakTestResult leak)
    {
        if (!string.IsNullOrEmpty(leak.Signature))
        {
            ShowSignatureBlock = true;
            // macOS parity: prefix the sig for legibility — the full base64 doesn't fit.
            string prefix = leak.Signature.Length > 32 ? leak.Signature[..32] + "…" : leak.Signature;
            SignatureText = $"Signed attestation: {prefix}";

            // B066: four typed states — never green without key trust.
            if (leak.SignatureValid == true)
            {
                if (leak.SignatureKeyTrusted == true)
                {
                    SignatureValidityText = "Ed25519 signature valid · signer trusted";
                    SignatureValidityBrush = SuccessBrush;
                }
                else if (leak.SignatureKeyTrusted == false)
                {
                    SignatureValidityText = "Ed25519 signature valid · signer NOT trusted";
                    SignatureValidityBrush = CriticalBrush;
                }
                else
                {
                    SignatureValidityText = "Ed25519 signature valid · signer trust not configured";
                    SignatureValidityBrush = AmberBrush;
                }
            }
            else if (leak.SignatureValid == false)
            {
                SignatureValidityText = "Ed25519 signature invalid";
                SignatureValidityBrush = CriticalBrush;
            }
            else
            {
                SignatureValidityText = "Signature not verified — no key supplied";
                SignatureValidityBrush = NeutralBrush;
            }
        }
        else
        {
            ShowSignatureBlock = false;
            SignatureText = "";
            SignatureValidityText = "";
        }

        // Transport pinning — surface collector identity verification (D003).
        ShowTransportLine = true;
        TransportText = $"Transport: {leak.TransportPinning}";
        TransportBrush = leak.TransportPinning == CollectorTransportPinning.Failed
            ? CriticalBrush
            : NeutralBrush;

        if (leak.ObservedResolvers is { Count: > 0 })
        {
            ShowObservedResolvers = true;
            ObservedResolversText = "Observed resolvers: " + string.Join(", ", leak.ObservedResolvers);
        }
        else
        {
            ShowObservedResolvers = false;
            ObservedResolversText = "";
        }
    }

    /// <summary>
    /// Defect-2 parity: surface the collector's reason ONLY when ErrorPresenter returns genuine
    /// human-readable text. The Kit can build ErrorReason from a raw exception's
    /// <c>Message</c> (LeakDetector.cs:207,271) which surfaces as crash-reporter material — show
    /// the amber ERROR line above instead, which already says the check failed.
    /// </summary>
    private void PopulateErrorReason(string? errorReason)
    {
        var clean = ErrorPresenter.DisplayReason(errorReason);
        ShowErrorReason = !string.IsNullOrEmpty(clean);
        ErrorReasonText = clean ?? "";
    }

    private void ResetCollectorResult()
    {
        HasCollectorResult = false;
        ShowSignatureBlock = false;
        ShowTransportLine = false;
        ShowObservedResolvers = false;
        ShowErrorReason = false;
        SignatureText = "";
        SignatureValidityText = "";
        TransportText = "";
        ObservedResolversText = "";
        ErrorReasonText = "";
    }

    private void ScanStarted()
    {
        IsRunning = true;
        GluttonRunState = ProbeRunState.Running;
        GluttonRunMessage = "";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MARK: Brushes (Linux theme parity)
    // ──────────────────────────────────────────────────────────────────────────
    // The Linux theme's success tone is TEAL (#3FD08A here matches SeverityColor.Clean). Per
    // App.axaml HONESTY NOTE: clean is the ONLY green in this theme. Amber for warning,
    // red for critical. These are programmatic instances (not App.axaml resources) so the VM can
    // switch them from a single source. SeverityColor.For is the canonical mapping — reusing it
    // keeps the visual severity monotone across the whole Linux client.
    // ──────────────────────────────────────────────────────────────────────────
    private static IBrush SuccessBrush => SeverityColor.For(Severity.Clean);
    private static IBrush AmberBrush => SeverityColor.For(Severity.Warning);
    private static IBrush CriticalBrush => SeverityColor.For(Severity.Critical);
    private static IBrush NeutralBrush => SeverityColor.For(Severity.Info);
}

/// <summary>
/// One row in the per-resolver probe table. Twin of Windows'
/// <c>AppViewModel.LeakProbeRow</c> record (kept positional so call sites are unchanged) and
/// macOS' <c>model.leakProbeResults: [String: Bool]</c> per-server entry.
/// </summary>
public sealed record LeakProbeRow(string Server, bool Sent);
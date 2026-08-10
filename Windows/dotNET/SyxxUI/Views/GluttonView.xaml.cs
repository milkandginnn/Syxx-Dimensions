using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyxxUI.ViewModels;
using Syxx.Kit;

namespace SyxxUI.Views;

public sealed partial class GluttonView : Page
{
    private static readonly HashSet<string> _gluttonProps = new()
    {
        nameof(AppViewModel.PortalReport), nameof(AppViewModel.LeakTestSubdomain),
        nameof(AppViewModel.SectionInputs), nameof(AppViewModel.GluttonRunState),
        nameof(AppViewModel.GluttonRunMessage),
    };

    public AppViewModel? ViewModel { get; private set; }
    public ObservableCollection<GluttonProbeRow> ProbeRows { get; } = [];

    public GluttonView()
    {
        InitializeComponent();

        Header.AddContent(new TextBlock
        {
            Text = "Alas, the Glutton hungered for encrypted packets and demanded that any leak in the sealed DNS traffic be found and named, for a single crack was enough to render him famished.",
            FontFamily = (FontFamily)Application.Current.Resources["IoFontLore"],
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["IoTextMutedBrush"],
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    // E155: subscribe to PropertyChanged so the pane refreshes when data changes while visible.
    private System.ComponentModel.PropertyChangedEventHandler? _pcHandler;

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = e.Parameter as AppViewModel;
        if (ViewModel is not null)
        {
            _pcHandler = async (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) || _gluttonProps.Contains(e.PropertyName))
                {
                    try { await RefreshAsync(); }
                    catch { /* best-effort refresh */ }
                }
            };
            ViewModel.PropertyChanged += _pcHandler;
        }
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            // GluttonView.OnNavigatedTo is async void — a licensing failure here must not crash.
            System.Diagnostics.Debug.WriteLine($"[Syxx] Glutton navigation refresh failed: {ex}");
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (ViewModel is not null && _pcHandler is not null)
            ViewModel.PropertyChanged -= _pcHandler;
        base.OnNavigatedFrom(e);
    }

    private async void CheckBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        CheckBtn.IsEnabled = false;
        try
        {
            await ViewModel.RunGluttonCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] Glutton check failed: {ex}");
        }
        finally
        {
            CheckBtn.IsEnabled = true;
        }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (ViewModel is null) return;

        // Feature status — ✓/✗ + message, both coloured (parity with macOS). Locked never green.
        var unlocked = await LicenseManager.IsFeatureUnlockedAsync();
        FeatureStatusIcon.Text = unlocked.Unlocked ? "✓" : "✗";
        FeatureStatusText.Text = unlocked.Unlocked ? "Unlocked" : unlocked.Message;
        var featureBrush = Brush(unlocked.Unlocked ? "IoSuccessGreenBrush" : "IoCriticalRedBrush");
        FeatureStatusIcon.Foreground = featureBrush;
        FeatureStatusText.Foreground = featureBrush;

        // VPN / DNS encryption — dot + label + detail. Unknown reads amber, never green.
        var report = ViewModel.PortalReport;
        if (report is not null)
        {
            VpnEmptyText.Visibility = Visibility.Collapsed;
            VpnContent.Visibility = Visibility.Visible;

            var vpnBrush = report.Vpn.State switch
            {
                ConditionState.Yes => Brush("IoSuccessGreenBrush"),
                ConditionState.Unknown => Brush("IoWarningAmberBrush"),
                _ => Brush("IoTextSecondaryBrush"),
            };
            VpnStatusText.Text = report.Vpn.State switch
            {
                ConditionState.Yes => $"VPN active — tunnel(s): {string.Join(", ", report.Vpn.TunnelInterfaces)}",
                ConditionState.No => "No VPN tunnel detected",
                _ => "VPN status unknown",
            };
            VpnStatusText.Foreground = vpnBrush;
            VpnDot.Fill = vpnBrush;

            string enc = report.EncryptedDNS.State switch
            {
                ConditionState.Yes => "Encryption detected (unverified)",
                ConditionState.No => "Encryption not detected",
                _ => "DNS encryption status unknown",
            };
            VpnDetailText.Text = $"{enc} — {report.Vpn.Evidence}";
        }
        else
        {
            VpnEmptyText.Visibility = Visibility.Visible;
            VpnContent.Visibility = Visibility.Collapsed;
        }

        // Probe table — subdomain + per-resolver dispatch status (sent/failed, coloured).
        bool hasSubdomain = !string.IsNullOrEmpty(ViewModel.LeakTestSubdomain);
        SubdomainText.Text = hasSubdomain
            ? ViewModel.LeakTestSubdomain
            : "Run a leak check to generate a probe subdomain.";

        ProbeRows.Clear();
        foreach (var p in ViewModel.LeakProbeResults)
        {
            ProbeRows.Add(new GluttonProbeRow(
                p.Server,
                p.Sent ? "sent" : "failed",
                Brush(p.Sent ? "IoSuccessGreenBrush" : "IoCriticalRedBrush")));
        }
        NoProbesText.Visibility = (hasSubdomain && ProbeRows.Count == 0)
            ? Visibility.Visible : Visibility.Collapsed;

        RenderLeakResult(ViewModel.SectionInputs.Leak);
    }

    /// D044: render the leak result from the REAL collector result, gated on the attestation
    /// (D004/D011). An unsigned, unverifiable, or key-untrusted result never renders as a
    /// confident leak/no-leak — it reads unverified/inconclusive.
    private void RenderLeakResult(LeakTestResult? leak)
    {
        void Set(string result, string detail, string brushKey)
        {
            LeakResultText.Text = result;
            LeakResultText.Foreground = Brush(brushKey);
            LeakResultDetailText.Text = detail;
        }

        if (leak is null)
        {
            // E132: a probe that failed must not render as one that never ran.
            if (ViewModel is not null && ViewModel.GluttonRunState == AppViewModel.ProbeRunState.Failed)
            {
                var reason = SyxxUI.Presentation.ErrorPresenter.DisplayReason(ViewModel.GluttonRunMessage) ?? "the check could not complete";
                Set("The check could not complete.",
                    $"{reason}. This is not a clean result.",
                    "IoWarningAmberBrush");
            }
            else
            {
                Set("No leak check has run yet.",
                    "Run a leak check; the collector's signed attestation decides the conclusion shown here.",
                    "IoTextSecondaryBrush");
            }
            return;
        }

        // D004: no valid signature → the result cannot be stood behind, whatever it claims.
        bool attested = leak.SignatureValid == true && leak.SignatureKeyTrusted != false;
        if (!attested)
        {
            Set("[UNVERIFIED] Result cannot be trusted.",
                leak.SignatureValid == null
                    ? "The collector returned no signed attestation, so this result cannot be verified. Treat it as inconclusive."
                    : leak.SignatureKeyTrusted == false
                        ? "The attestation is signed by a key this app does not trust. Treat the result as inconclusive."
                        : "The collector's attestation signature is invalid. Treat the result as inconclusive.",
                "IoWarningAmberBrush");
            return;
        }

        switch (leak.Result)
        {
            case LeakResult.Confirmed:
                Set("Multiple resolvers observed your DNS queries.",
                    leak.ObservedResolvers.Count > 0
                        ? $"The collector observed your test query arriving from {leak.ObservedResolvers.Count} resolver(s): {string.Join(", ", leak.ObservedResolvers)}. Verify whether these match your VPN or encrypted DNS provider."
                        : "The collector attested a divergence.",
                    "IoWarningAmberBrush");
                break;
            case LeakResult.NotDetected:
                int n = leak.ObservedResolvers.Count;
                if (n == 0)
                {
                    Set("No resolver observed your probe.",
                        "The collector saw no query for your test label; this is not a clean result — re-run the check.",
                        "IoWarningAmberBrush");
                }
                else
                {
                    Set($"{n} resolver{(n == 1 ? "" : "s")} observed.",
                        $"The collector observed your test query from: {string.Join(", ", leak.ObservedResolvers)}. "
                        + "Verify it matches your VPN or encrypted DNS provider.",
                        "IoSuccessGreenBrush");
                }
                break;
            case LeakResult.Invalid:
                Set("[INVALID] The result is structurally impossible.",
                    "The collector returned a response that is malformed or missing required fields. Treat as inconclusive.",
                    "IoWarningAmberBrush");
                break;
            case LeakResult.Error:
                Set("[ERROR] The leak test could not execute.",
                    "An execution error prevented the test from completing. Check your network connection and try again.",
                    "IoWarningAmberBrush");
                break;
            default:
                // Defect-2 parity (macOS GluttonView.swift:232): the core may build ErrorReason from
                // an exception's message — see Syxx.Kit/LeakDetector.cs:207,271 ("Token request
                // failed: " + ex.Message) — which surfaces as crash-reporter material. Show it only
                // when it is genuine human-readable text; otherwise fall back to the standing
                // inconclusive copy, which already states honestly that nothing was decided.
                Set("[INCONCLUSIVE] The check could not decide.",
                    SyxxUI.Presentation.ErrorPresenter.DisplayReason(leak.ErrorReason)
                        ?? "The collector could not produce a conclusion either way. Re-run the check; a repeatedly inconclusive result is worth investigating.",
                    "IoWarningAmberBrush");
                break;
        }
    }
}

// A plain class with SETTABLE properties, not a positional record: WinUI compiled x:Bind generates
// code that ASSIGNS these via setter (XamlTypeInfo.g.cs), and a positional record's init-only
// properties fail that with CS8852. Constructor kept positional so call sites are unchanged.
public sealed class GluttonProbeRow
{
    public string Server { get; set; }
    public string SentLabel { get; set; }
    public Brush SentBrush { get; set; }
    public GluttonProbeRow(string server, string sentLabel, Brush sentBrush)
    {
        Server = server; SentLabel = sentLabel; SentBrush = sentBrush;
    }
}

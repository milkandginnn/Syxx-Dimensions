using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyxxUI.ViewModels;
using Syxx.Kit;

namespace SyxxUI.Views;

public sealed partial class PortalView : Page
{
    private static readonly HashSet<string> _portalProps = new()
    {
        nameof(AppViewModel.PortalReport), nameof(AppViewModel.PortalRunState), nameof(AppViewModel.PortalRunMessage),
    };

    private AppViewModel? _vm;

    public PortalView()
    {
        InitializeComponent();

        Header.AddContent(new TextBlock
        {
            Text = "A Portal carried the traveler to a distant land, tracing the path of a hidden interceptor that had reached into the stream.",
            FontFamily = (FontFamily)Application.Current.Resources["IoFontLore"],
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["IoTextMutedBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

    }

    // E155: subscribe to PropertyChanged so the pane refreshes when data changes while visible.
    private System.ComponentModel.PropertyChangedEventHandler? _pcHandler;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null)
        {
            _pcHandler = (_, e) =>
            {
                if (string.IsNullOrEmpty(e.PropertyName) || _portalProps.Contains(e.PropertyName))
                    Refresh();
            };
            _vm.PropertyChanged += _pcHandler;
        }

        // Populate the resolvers list after navigation (not in the constructor) so the
        // synchronous NetworkKeyState call does not block the UI thread during page creation.
        // macOS parity: macOS reads from cached model data; this is the closest WinUI equivalent.
        var servers = NetworkKeyState.GetSystemDNSServers();
        ResolversList.ItemsSource = servers;
        ResolversEmptyText.Visibility = servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        Refresh();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_vm is not null && _pcHandler is not null)
            _vm.PropertyChanged -= _pcHandler;
        base.OnNavigatedFrom(e);
    }

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];

    private async void CheckBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Domain = DomainBox.Text;
        CheckBtn.IsEnabled = false;
        try
        {
            await _vm.RunPortalCommand.ExecuteAsync(null);
            Refresh();
            await RunTracerouteAsync(DomainBox.Text);
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] Portal check failed: {ex}");
        }
        finally
        {
            CheckBtn.IsEnabled = true;
        }
    }

    private void Refresh()
    {
        if (_vm?.PortalReport is { } r)
        {
            InterfacesEmptyText.Visibility = Visibility.Collapsed;
            InterfacesContent.Visibility = Visibility.Visible;
            InterfacesList.ItemsSource = r.ActiveInterfaces;

            // VPN tri-state — parity with macOS PortalView. Unknown reads amber "Unknown", never a
            // confident state; only a real active tunnel reads green.
            var (vpnLabel, vpnBrush) = r.Vpn.State switch
            {
                ConditionState.Yes => ("Yes • active", Brush("IoSuccessGreenBrush")),
                ConditionState.Unknown => ("Unknown", Brush("IoWarningAmberBrush")),
                _ => ("No tunnel", Brush("IoTextSecondaryBrush")),
            };
            VpnText.Text = vpnLabel;
            VpnText.Foreground = vpnBrush;
            VpnDot.Fill = vpnBrush;

            // E150: render the three signals Portal already collects but previously discarded.

            // Captive portal — tri-state. Unknown is distinct from No.
            var (cpLabel, cpBrush) = r.CaptivePortal.State switch
            {
                ConditionState.Yes => ("Yes — portal intercepting", Brush("IoCriticalRedBrush")),
                ConditionState.No => ("No captive portal", Brush("IoSuccessGreenBrush")),
                _ => ("Unknown", Brush("IoWarningAmberBrush")),
            };
            CaptivePortalText.Text = cpLabel;
            CaptivePortalText.Foreground = cpBrush;
            CaptivePortalDot.Fill = cpBrush;

            // Encrypted DNS — tri-state.
            var (encLabel, encBrush) = r.EncryptedDNS.State switch
            {
                ConditionState.Yes => ("Encrypted (unverified)", Brush("IoSuccessGreenBrush")),
                ConditionState.No => ("Not encrypted", Brush("IoWarningAmberBrush")),
                _ => ("Unknown", Brush("IoWarningAmberBrush")),
            };
            EncryptedDnsText.Text = encLabel;
            EncryptedDnsText.Foreground = encBrush;
            EncryptedDnsDot.Fill = encBrush;

            // Gateway — null when detection could not run.
            if (r.Gateway is { } gw)
            {
                GatewayText.Text = string.IsNullOrEmpty(gw.Ip)
                    ? "Gateway not detected"
                    : $"{gw.Ip}{(gw.IsDNS ? " (also DNS server)" : "")}";
                GatewayText.Foreground = Brush(gw.IsDNS ? "IoWarningAmberBrush" : "IoTextBrush");
            }
            else
            {
                GatewayText.Text = "Run Portal to see gateway info.";
                GatewayText.Foreground = Brush("IoTextSecondaryBrush");
            }
        }
        else
        {
            // E132: a probe that failed must not render as one that never ran.
            InterfacesEmptyText.Text = _vm is not null && _vm.PortalRunState == AppViewModel.ProbeRunState.Failed
                ? $"The check could not complete: {SyxxUI.Presentation.ErrorPresenter.DisplayReason(_vm.PortalRunMessage) ?? "the check could not complete"}. This is not a clean result."
                : "Run Portal to see active interfaces and VPN status.";
            InterfacesEmptyText.Visibility = Visibility.Visible;
            InterfacesContent.Visibility = Visibility.Collapsed;
        }
    }

    // MARK: - Portal traceroute (GUI wrapper around the system `tracert`)
    // Mirrors the guided-CLI Portal trace (Program.cs: `tracert -h 15 -w 1000 -d <domain>`) and the
    // verified macOS GUI traceroute. Honest states only: an unsafe/empty target reports as such, a
    // trace that cannot run reports failure, and hops shown are only ever tracert's real output —
    // never fabricated. Runs off the UI thread so the window stays responsive.
    private async Task RunTracerouteAsync(string domain)
    {
        string target = (domain ?? "").Trim();
        TraceHopsList.ItemsSource = null;
        TraceNoteText.Text = "";

        if (string.IsNullOrEmpty(target) || !IsSafeTraceTarget(target))
        {
            TraceProgress.IsActive = false;
            TraceProgress.Visibility = Visibility.Collapsed;
            TraceStatusText.Visibility = Visibility.Visible;
            TraceStatusText.Text = "Enter a valid domain or IP address above, then run Portal, to trace its route.";
            return;
        }

        TraceStatusText.Visibility = Visibility.Visible;
        TraceStatusText.Text = $"Tracing the route to {target}…";
        TraceProgress.IsActive = true;
        TraceProgress.Visibility = Visibility.Visible;

        (List<string> hops, bool ok, string reason) = await Task.Run(() => RunTracert(target));

        TraceProgress.IsActive = false;
        TraceProgress.Visibility = Visibility.Collapsed;

        if (!ok)
        {
            TraceStatusText.Visibility = Visibility.Visible;
            // An empty reason means the failure carried no cause we can honestly state. Say the
            // traceroute could not run and stop there — never fill the parenthesis with a guess.
            TraceStatusText.Text = string.IsNullOrWhiteSpace(reason)
                ? "The traceroute could not run. This is not evidence of a clean path."
                : $"The traceroute could not run ({reason}). This is not evidence of a clean path.";
            return;
        }

        TraceStatusText.Visibility = Visibility.Collapsed;
        TraceHopsList.ItemsSource = hops;
        TraceNoteText.Text = "If the trace stops early, the last responding hop is where interception or blocking begins.";
    }

    /// Run the system `tracert` for `domain` and return its real output lines plus a tri-state
    /// outcome. `-d` skips reverse-name lookups; `-h 15` caps hops; `-w 1000` bounds per-hop wait.
    // TODO parity (UNIFIED-BINARY-PLAN Phase 2, Step 1): fold this into the shared
    // Syxx.Kit.TracerouteRunner.TraceAsync used by `syxx route` and the guided Portal flow.
    // Left in place deliberately: this WinUI project cannot be built on the macOS host where the
    // consolidation was done, and RunTracert is synchronous with a (hops, ok, reason) tuple that
    // its caller depends on — reshaping it must be verified on a Windows host, not risked blind.
    private static (List<string> hops, bool ok, string reason) RunTracert(string domain)
    {
        var hops = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tracert",
                Arguments = $"-h 15 -w 1000 -d {domain}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return (hops, false, "the traceroute tool could not be launched");
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(50000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
            }
            foreach (var line in output.Split('\n'))
            {
                string t = line.Trim();
                if (!string.IsNullOrEmpty(t)) hops.Add(t);
            }
        }
        catch (Exception ex)
        {
            // Defect-2 parity (macOS ErrorPresenter.swift): `reason` is interpolated straight into
            // the user-visible "The traceroute could not run (…)" line, so a raw Win32Exception
            // message ("The system cannot find the file specified") or a namespace-qualified type
            // dump would land on screen as product copy. Full detail to the diagnostic sink; the
            // screen gets readable text, or an EMPTY reason, which the caller renders as a failure
            // with no stated cause rather than an invented one.
            return (hops, false, SyxxUI.Presentation.ErrorPresenter.UserDetail(ex, "portal.trace") ?? "");
        }
        if (hops.Count == 0) return (hops, false, "the traceroute produced no output");
        return (hops, true, "");
    }

    /// Reject empty, flag-like, whitespace-bearing, or non-hostname/IP input before it reaches the
    /// traceroute subprocess. Mirrors the guided CLI's IsSafeTraceTarget (Program.cs).
    private static bool IsSafeTraceTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.StartsWith("-") || target.Any(char.IsWhiteSpace)) return false;
        if (target.EndsWith(".")) target = target.Substring(0, target.Length - 1);
        return IPAddress.TryParse(target, out _) || Uri.CheckHostName(target) == UriHostNameType.Dns;
    }
}

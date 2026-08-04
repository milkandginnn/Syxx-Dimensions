using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Presentation;   // linked from Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Portal — what the local network and the path to a host actually look like, including the C1
/// gateway/router audit. Runs the real <see cref="ProbePlan.RunPortalAsync"/>. Every field is a
/// direct read; an unavailable one says so rather than showing a reassuring blank.
/// </summary>
public partial class PortalViewModel : ObservableObject
{
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resolversLine = "";
    [ObservableProperty] private string _vpnLine = "";
    [ObservableProperty] private string _gatewayLine = "";
    public ObservableCollection<string> Interfaces { get; } = new();

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        IsRunning = true; HasResult = false; Interfaces.Clear();

        SectionInputs r;
        try { r = await ProbePlan.RunPortalAsync(); }
        // Defect-2 parity: raw detail to the diagnostic sink, readable text (or the bare failure,
        // with no invented cause) to the screen.
        catch (System.Exception ex)
        {
            ResolversLine = ErrorPresenter.Compose(
                "Portal could not complete.", ErrorPresenter.UserDetail(ex, "portal.run"));
            IsRunning = false; HasResult = true; return;
        }

        var net = r.Network;
        var servers = NetworkKeyState.GetSystemDNSServers();
        ResolversLine = servers.Count == 0 ? "No DNS servers discovered" : string.Join(", ", servers);

        if (net is not null)
        {
            foreach (var iface in net.ActiveInterfaces) Interfaces.Add(iface);
            VpnLine = net.Vpn.State switch
            {
                ConditionState.Yes => $"VPN active — {net.Vpn.Evidence}",
                ConditionState.No => "No VPN tunnel detected",
                _ => "VPN status could not be determined (inconclusive)"
            };
            // C1 — the gateway audit's own composed evidence string, or its honest failure reason.
            GatewayLine = net.Gateway is { } gw && !string.IsNullOrEmpty(gw.Detail)
                ? gw.Detail
                : "Gateway could not be determined on this run.";
        }
        else
        {
            VpnLine = "Network state unavailable (inconclusive).";
            GatewayLine = "";
        }

        HasResult = true; IsRunning = false;
    }
}

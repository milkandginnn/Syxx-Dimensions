using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Syxx.Kit;

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Dashboard system-status stats. HONESTY: a stat that has not been measured this session shows a
/// DASH, never a fabricated number and never a zero (a zero would read as a measured zero). The only
/// value we can know without running a probe is the resolver count, which is a local read.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty] private string _resolver = "not detected";
    [ObservableProperty] private string _trustScore = "—";
    [ObservableProperty] private IBrush _trustBrush = Brushes.Gray;
    [ObservableProperty] private string _alertsToday = "—";
    [ObservableProperty] private string _resolvers = "—";
    [ObservableProperty] private string _leakFound = "—";
    [ObservableProperty] private string _queriesSeen = "—";

    public DashboardViewModel()
    {
        // Resolver identity + count are the only stats knowable from a local read with no probe.
        var servers = NetworkKeyState.GetSystemDNSServers();
        if (servers.Count > 0)
        {
            Resolver = servers[0];
            Resolvers = servers.Count.ToString();
        }
        // Trust score / alerts / leak / queries stay dashes until a real scan or capture runs.
        // They are wired to live results as those surfaces populate; a dash is the honest resting state.
    }
}

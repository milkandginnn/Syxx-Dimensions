using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using SyxxUI.Avalonia.Views;

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Shell navigation for the Linux client. Deliberately thin: the reasoning lives in Syxx.Kit and the
/// honesty-critical rendering lives in the linked Presentation layer, so this only picks a view.
///
/// NOTE (WS-E1): this is NOT a fork of the Windows <c>AppViewModel</c>. That type is linked only
/// once its Windows-only dependencies (DPAPI <c>WitnessKeyStore</c>, <c>DimensionService</c>'s
/// %LOCALAPPDATA% path) have portable backends — see the Linux-portability note in the changelog.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    // Order matches macOS and Windows: What's New is FIRST on every client and is the launch view;
    // the six disciplines, then Conclusions (synthesis), then License.
    public ObservableCollection<string> NavItems { get; } =
        new(ShellLaunchPlan.NavigationItems);

    [ObservableProperty]
    private string _selectedNav = ShellLaunchPlan.InitialDestination;

    [ObservableProperty]
    private Control? _currentView;

    // Views are held, not rebuilt, so the MOTD is fetched once per launch and a run's results
    // survive navigating away and back.
    private readonly System.Collections.Generic.Dictionary<string, Control> _views = new()
    {
        ["What's New"] = new WhatsNewView(),
        ["Dashboard"]  = new DashboardView(),
        ["Prophet"]    = new ProphetView(),
        ["Mirror"]     = new MirrorView(),
        ["Portal"]     = new PortalView(),
        ["Glutton"]    = new GluttonView(),
        ["Cage"]       = new CageView(),
        ["Witness"]    = new WitnessView(),
        ["Conclusions"] = new ConclusionsView(),
        ["Settings"]   = new SettingsView(),
        ["License"]    = new LicenseView(),
    };

    public ShellViewModel()
    {
        CurrentView = _views[ShellLaunchPlan.InitialDestination];
    }

    partial void OnSelectedNavChanged(string value)
    {
        if (_views.TryGetValue(value, out var v))
            CurrentView = v;
    }
}

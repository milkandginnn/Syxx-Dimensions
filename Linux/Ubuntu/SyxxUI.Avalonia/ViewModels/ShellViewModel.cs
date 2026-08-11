using System;
using System.Collections.Generic;
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
public partial class ShellViewModel : ObservableObject, IDisposable
{
    // Order matches macOS and Windows: What's New is FIRST on every client and is the launch view;
    // the six disciplines, then Conclusions (synthesis), then License.
    public ObservableCollection<string> NavItems { get; } =
        new(ShellLaunchPlan.NavigationItems);

    [ObservableProperty]
    private string _selectedNav = ShellLaunchPlan.InitialDestination;

    [ObservableProperty]
    private Control? _currentView;

    // L2-026: views are initialized lazily on first navigation rather than all at startup.
    // This avoids the startup cost of constructing 11 view trees when the user may only visit 2-3.
    // Views are held (not rebuilt) so the MOTD is fetched once per launch and a run's results
    // survive navigating away and back.
    private readonly Dictionary<string, Func<Control>> _viewFactories = new()
    {
        ["What's New"] = () => new WhatsNewView(),
        ["Dashboard"] = () => new DashboardView(),
        ["Prophet"] = () => new ProphetView(),
        ["Mirror"] = () => new MirrorView(),
        ["Portal"] = () => new PortalView(),
        ["Glutton"] = () => new GluttonView(),
        ["Cage"] = () => new CageView(),
        ["Witness"] = () => new WitnessView(),
        ["Conclusions"] = () => new ConclusionsView(),
        ["Settings"] = () => new SettingsView(),
        ["License"] = () => new LicenseView(),
    };

    private readonly Dictionary<string, Control> _views = new();

    public ShellViewModel()
    {
        CurrentView = GetOrCreateView(ShellLaunchPlan.InitialDestination);
    }

    private Control GetOrCreateView(string name)
    {
        if (!_views.TryGetValue(name, out var view))
        {
            if (_viewFactories.TryGetValue(name, out var factory))
            {
                view = factory();
                _views[name] = view;
            }
            else
            {
                // Fallback: should never happen if NavItems matches _viewFactories keys.
                view = new TextBlock { Text = $"Unknown view: {name}" };
            }
        }
        return view;
    }

    partial void OnSelectedNavChanged(string value)
    {
        CurrentView = GetOrCreateView(value);
    }

    // L2-036: dispose all cached views and clear subscriptions on shutdown.
    public void Dispose()
    {
        foreach (var view in _views.Values)
        {
            if (view is IDisposable disposable)
                disposable.Dispose();
            if (view.DataContext is IDisposable dcDisposable)
                dcDisposable.Dispose();
        }
        _views.Clear();
    }
}

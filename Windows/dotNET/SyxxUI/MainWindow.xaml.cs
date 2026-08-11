using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Syxx.Kit;
using SyxxUI.Services;
using SyxxUI.ViewModels;
using SyxxUI.Views;
using Windows.System;

namespace SyxxUI;

public sealed partial class MainWindow : Window
{
    private readonly FirstRunViewModel _firstRun = FirstRunViewModel.CreateLive();
    private readonly ReadyServicesGate _readyServices = new();
    private AppViewModel? _viewModel;
    private TrayIconService? _tray;

    private AppViewModel ViewModel =>
        _viewModel
        ?? throw new InvalidOperationException("The application shell is not ready.");

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        // D311: increased from 1050×700 to 1280×800 to accommodate all module views at default DPI.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        // D380: show version in title bar for bug reports.
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Title = $"Syxx {version?.Major}.{version?.Minor}.{version?.Build}";
        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
            // E160: OnSuspending never fires for unpackaged Win32 desktop apps
            // (WindowsPackageType=None), so flush telemetry from Closed instead.
            (Application.Current as App)?.FlushTelemetry();
        };
        // E160: belt-and-braces — ProcessExit fires for all termination paths including
        // ctrl-close and logoff, where the Closed event may not.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            (Application.Current as App)?.FlushTelemetry();

        FirstRunGate.Ready += FirstRunGate_Ready;
        if (Content is FrameworkElement root)
            root.Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (Content is FrameworkElement root)
            root.Loaded -= MainWindow_Loaded;
        FirstRunGate.Initialize(_firstRun);
    }

    private async void FirstRunGate_Ready(object? sender, EventArgs e)
    {
        await InitializeReadyShellAsync();
    }

    private async Task InitializeReadyShellAsync()
    {
        var settings = _firstRun.Settings;
        if (settings is null)
        {
            return;
        }

        var started = _readyServices.TryStart(_firstRun.State, () =>
        {
            _viewModel = new AppViewModel(settings);
            InitializeTray();
            (Application.Current as App)?.StartReadyServices(settings);

            FirstRunGate.Visibility = Visibility.Collapsed;
            NavView.Visibility = Visibility.Visible;
            NavView.SelectionChanged -= NavView_SelectionChanged;
            NavView.SelectedItem = NavView.MenuItems[0];
            NavView.SelectionChanged += NavView_SelectionChanged;
            ContentFrame.Navigate(typeof(RemoteContentPanel), ViewModel);
        });
        if (!started)
        {
            return;
        }

        // License state is checked only after onboarding is fully persisted. Remote update and
        // broadcast fetches additionally respect the setting selected during guided setup.
        if (settings.UpdateCheckEnabled)
        {
            await CheckStartupAsync();
        }
        else
        {
            await ViewModel.CheckLicenseAsync();
        }
    }

    private void InitializeTray()
    {
        _tray = new TrayIconService
        {
            IsMonitoringOn = () => ViewModel.MonitoringOn,
            LatchedChange = () => ViewModel.LastMonitorChange,
            OnWatchToggled = on => DispatcherQueue.TryEnqueue(() => ViewModel.SetMonitoring(on)),
            OnAcknowledge = () => DispatcherQueue.TryEnqueue(ViewModel.AcknowledgeMonitorChange),
            OnOpen = () => DispatcherQueue.TryEnqueue(() => Activate()),
            OnQuit = () => DispatcherQueue.TryEnqueue(() => { _tray?.Dispose(); Application.Current.Exit(); }),
        };
        _tray.Update(ViewModel.Conclusion);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppViewModel.Conclusion))
            {
                _tray?.Update(ViewModel.Conclusion);
            }
        };
        ViewModel.MonitorTicked += _ => DispatcherQueue.TryEnqueue(() => _tray?.Update(ViewModel.Conclusion));
    }

    private async Task CheckStartupAsync()
    {
        await ViewModel.CheckStartupAsync();
        // F025: serialize dialogs — WinUI allows only one ContentDialog at a time. Awaiting the
        // broadcast dialog before showing the update dialog prevents the second ShowAsync from
        // failing silently while DismissBroadcast has already recorded the MOTD as seen.
        if (ViewModel.PendingBroadcast is not null)
            await ShowBroadcastDialog(ViewModel.PendingBroadcast);

        // Show update dialog and sidebar banner when a new version is available.
        if (ViewModel.UpdateAvailable)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            await ShowUpdateDialogAsync();
        }
    }

    private async Task ShowUpdateDialogAsync()
    {
        var panel = new Views.UpdatePanel(ViewModel);
        panel.ApplyStartupState();

        var dialog = new ContentDialog
        {
            Title = "Check for Updates",
            Content = panel,
            PrimaryButtonText = null,    // No built-in buttons — panel has its own
            CloseButtonText = null,
            XamlRoot = ContentFrame.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        await ShowUpdateDialogAsync();
    }

    private async Task ShowBroadcastDialog(Broadcast broadcast)
    {
        var severityColor = broadcast.Severity?.ToLower() switch
        {
            "critical" => Microsoft.UI.Colors.Red,
            "warning"  => Microsoft.UI.Colors.Orange,
            "info"     => Microsoft.UI.Colors.Purple,
            _          => Microsoft.UI.Colors.Gray,
        };
        var dialog = new ContentDialog
        {
            Title = broadcast.Title,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(severityColor),
                        Height = 3,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 0, 0, 4),
                    },
                    new TextBlock
                    {
                        Text = broadcast.Body,
                        TextWrapping = TextWrapping.Wrap,
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                    }
                }
            },
            PrimaryButtonText = "Dismiss",
            CloseButtonText = broadcast.Url is not null ? "Learn More" : null,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ContentFrame.XamlRoot,
        };
        if (broadcast.Url is not null)
        {
            dialog.CloseButtonClick += async (s, e) =>
            {
                // Phase 2.2: single gate — refusal never falls through to LaunchUriAsync.
                var uri = RemoteUrlPolicy.AuthorizeForNavigation(broadcast.Url, out var eval);
                if (uri is not null)
                {
                    _ = await Launcher.LaunchUriAsync(uri);
                }
                else
                {
                    var msgDialog = new ContentDialog
                    {
                        Title = "Link Blocked",
                        Content = eval.Reason ?? "This link is not authorized to be opened.",
                        CloseButtonText = "OK",
                        XamlRoot = ContentFrame.XamlRoot
                    };
                    _ = await msgDialog.ShowAsync();
                }
            };
        }
        // E145: await the dialog and only persist the dismissal when the user explicitly pressed
        // "Dismiss" (Primary). Fire-and-forget suppressed critical broadcasts the user never saw.
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            ViewModel.DismissBroadcast(broadcast);
    }

    private void ClearContentBtn_Click(object sender, RoutedEventArgs e)
    {
        // "Start your story anew" — reset every pane to its just-opened state.
        ViewModel.ResetAllCommand.Execute(null);
    }

    // E152: cache the last navigated item so the Dimensions external link can restore it.
    // Without this, NavigationView commits the selection but no page is navigated, leaving
    // "Dimensions" highlighted indefinitely while ContentFrame shows the previous pane.
    private object? _lastNavigatedItem;

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        // Dimensions is an external link (macOS parity): open the site, keep the current pane.
        // E152: restore the prior selection so the sidebar does not stay on "Dimensions".
        if (item.Tag as string == "Dimensions")
        {
            // Phase 2.2: even the hardcoded product origin goes through RemoteUrlPolicy so a
            // future edit cannot open an unvetted destination without tripping the gate tests.
            var dimensionsUri = RemoteUrlPolicy.AuthorizeForNavigation("https://syxx.app");
            if (dimensionsUri is not null)
                _ = Launcher.LaunchUriAsync(dimensionsUri);
            sender.SelectedItem = _lastNavigatedItem ?? sender.MenuItems[0];
            return;
        }
        Type? pageType = (item.Tag as string) switch
        {
            "Welcome"    => typeof(RemoteContentPanel),
            "Dashboard"  => typeof(DashboardView),
            "Prophet"    => typeof(ProphetView),
            "Mirror"     => typeof(MirrorView),
            "Portal"     => typeof(PortalView),
            "Glutton"    => typeof(GluttonView),
            "Cage"       => typeof(CageView),
            "Witness"    => typeof(WitnessView),
            "Conclusion" => typeof(ConclusionsView),
            "Settings"   => typeof(SettingsView),
            "License"    => typeof(LicenseView),
            _            => null
        };
        if (pageType is not null)
        {
            ContentFrame.Navigate(pageType, ViewModel);
            _lastNavigatedItem = item;
        }
    }
}

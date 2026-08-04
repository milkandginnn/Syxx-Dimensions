using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using SyxxUI.Presentation;
using SyxxUI.Services;
using Windows.System;

namespace SyxxUI.Views;

/// <summary>
/// A locked-down WebView2 that renders the remote MOTD, with a shared five-second deadline.
/// On timeout or definitive failure, loads packaged <c>Assets/fallback-motd.html</c> into the
/// same WebView. Mirrors macOS <c>RemoteContentView</c> MOTD mode.
/// </summary>
public sealed partial class RemoteContentPanel : Page
{
    // E038: nullable — null when the user has opted out of Dimension telemetry, in which case
    // the panel shows no remote content.
    public string? SourceUrl { get; }

    private readonly MotdLoadController _motd = new();
    private CancellationTokenSource? _deadlineCts;
    private ulong _activeGeneration;

    public RemoteContentPanel()
    {
        InitializeComponent();
        SourceUrl = DimensionService.Shared.FetchWhatsNewUrl();
        // Modern WinUI 3 / WindowsAppSDK 2.x init: EnsureCoreWebView2Async, not the
        // (WPF-only) CoreWebView2InitializationCompleted event. Navigation is deferred
        // to InitializeWebViewAsync so the lock-down settings are applied BEFORE the
        // first load; setting Source in XAML would auto-init and could load with
        // JavaScript still enabled.
        Loaded += (s, e) => _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        _activeGeneration = _motd.BeginRemoteLoad();
        var gen = _activeGeneration;
        _deadlineCts?.Cancel();
        _deadlineCts = new CancellationTokenSource();
        var deadlineToken = _deadlineCts.Token;

        // Five-second surface deadline starts before renderer init / navigation.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(MotdLoadController.DeadlineSeconds), deadlineToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            _ = DispatcherQueue.TryEnqueue(() => ActivateMotdFallback(gen));
        }, deadlineToken);

        CoreWebView2 core;
        try
        {
            await ContentWebView.EnsureCoreWebView2Async();
            core = ContentWebView.CoreWebView2;
        }
        catch
        {
            // WebView2 runtime missing or init failed.
            if (_motd.MarkRendererUnavailable(gen))
                ShowOffline();
            return;
        }

        if (core is null)
        {
            if (_motd.MarkRendererUnavailable(gen))
                ShowOffline();
            return;
        }

        // JavaScript OFF for remote and fallback.
        core.Settings.IsScriptEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsWebMessageEnabled = false;
        core.NewWindowRequested += (s, e) => e.Handled = true;

        core.DOMContentLoaded += (s, e) =>
        {
            if (!_motd.Accepts(gen)) return;
            if (_motd.State == MotdLoadState.ShowingFallback) return;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        };

        core.NavigationCompleted += (s, e) =>
        {
            if (!_motd.Accepts(gen)) return;
            if (_motd.State is MotdLoadState.ShowingFallback or MotdLoadState.RendererUnavailable)
                return;
            if (e.IsSuccess)
            {
                if (_motd.RemoteSucceeded(gen))
                {
                    _deadlineCts?.Cancel();
                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                }
                return;
            }
            ActivateMotdFallback(gen);
        };

        // E038: when the user has opted out, SourceUrl is null — show no remote content.
        if (SourceUrl is null)
        {
            ActivateMotdFallback(gen);
            return;
        }

        // D103 / Phase 2.2: INITIAL URL must pass RemoteUrlPolicy via AuthorizeForNavigation.
        var initialUri = Syxx.Kit.RemoteUrlPolicy.AuthorizeForNavigation(SourceUrl);
        if (initialUri is null)
        {
            ActivateMotdFallback(gen);
            return;
        }

        if (!_motd.Accepts(gen) || _motd.State == MotdLoadState.ShowingFallback)
            return;

        ContentWebView.Source = initialUri;
    }

    /// <summary>Load packaged fallback-motd.html into the same WebView. Late remote callbacks are ignored.</summary>
    private void ActivateMotdFallback(ulong gen)
    {
        if (!_motd.ActivateFallback(gen)) return;
        _deadlineCts?.Cancel();
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;

        try
        {
            ContentWebView.CoreWebView2?.Stop();
        }
        catch
        {
            // Stop is best-effort; fallback still proceeds.
        }

        var html = MotdLoadController.ReadPackagedFallbackHtml();
        if (html is null)
        {
            _ = _motd.MarkRendererUnavailable(gen);
            ShowOffline();
            return;
        }

        ContentWebView.Visibility = Visibility.Visible;
        OfflineOverlay.Visibility = Visibility.Collapsed;
        try
        {
            ContentWebView.CoreWebView2?.NavigateToString(html);
        }
        catch
        {
            _ = _motd.MarkRendererUnavailable(gen);
            ShowOffline();
        }
    }

    private void ShowOffline()
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        // COLLAPSE the WebView2, or it can occlude the overlay: WebView2 hosts a composition surface
        // that renders over overlapping XAML siblings regardless of z-order.
        ContentWebView.Visibility = Visibility.Collapsed;
        OfflineOverlay.Visibility = Visibility.Visible;
    }

    private async void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // D034: parse URI and compare normalized host, not substring.
        // D103: use TryCreate — SourceUrl can be unparseable if RemoteUrlPolicy was bypassed.
        if (SourceUrl is null)
        {
            // About:blank / NavigateToString for fallback has no remote host; allow document loads.
            if (args.Uri is null || args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                || args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return;
            args.Cancel = true;
            return;
        }

        if (!Uri.TryCreate(SourceUrl, UriKind.Absolute, out var sourceUri))
        {
            args.Cancel = true;
            return;
        }
        var sourceHost = sourceUri.Host.ToLowerInvariant();

        if (args.Uri is null) return;
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var navUri))
        {
            args.Cancel = true;
            return;
        }

        // Allow about:/data: used by NavigateToString fallback document.
        if (navUri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase)
            || navUri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
            return;

        if (!navUri.Host.Equals(sourceHost, StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            // Phase 2.2: external open only after AuthorizeForNavigation; refusal stays closed.
            var openUri = Syxx.Kit.RemoteUrlPolicy.AuthorizeForNavigation(args.Uri, out var eval);
            if (openUri is not null)
            {
                _ = await Launcher.LaunchUriAsync(openUri);
            }
            else
            {
                var msgDialog = new ContentDialog
                {
                    Title = "Link Blocked",
                    Content = eval.Reason ?? "This link is not authorized to be opened.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                _ = await msgDialog.ShowAsync();
            }
        }
    }
}

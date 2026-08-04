using System.Diagnostics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Presentation;
using SyxxUI.Services;

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// The MOTD ("What's New") — the FIRST view of the Linux client.
///
/// Phase 3.6: remote MOTD is attempted with a shared five-second deadline. On timeout or
/// definitive failure the packaged <c>Assets/fallback-motd.html</c> is selected (byte-identical
/// to macOS/Windows). Avalonia 12.1.0 does not yet ship a pinned Linux WebView for both RIDs, so
/// the HTML is held for an embedded renderer when available and the offline panel surfaces the
/// honest fallback state immediately. Never substitutes motd-local or fallback-mac.
/// </summary>
public partial class WhatsNewViewModel : ObservableObject
{
    public const string PlatformMotdUrl = "https://debmotd.syxx.app/";
    public const string ApexMotdUrl = "https://syxx.app/";

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _statusText = "Checking for news…";
    [ObservableProperty] private bool _hasBroadcast;
    [ObservableProperty] private string _broadcastTitle = "";
    [ObservableProperty] private string _broadcastBody = "";
    [ObservableProperty] private IBrush _broadcastAccent = Brushes.Gray;
    [ObservableProperty] private bool _hasBroadcastLink;
    [ObservableProperty] private bool _isFromCache;
    [ObservableProperty] private string _provenanceText = "";
    [ObservableProperty] private string _linkRefusalText = "";
    [ObservableProperty] private bool _isShowingFallback;
    [ObservableProperty] private string? _fallbackHtml;
    [ObservableProperty] private bool _fallbackHasHero;

    private string? _broadcastUrl;
    private string _motdUrl = ApexMotdUrl;
    private readonly MotdLoadController _motd = new();

    public static string ChooseMotdUrl(string? serverPointer, bool platformHostServing)
    {
        if (!string.IsNullOrWhiteSpace(serverPointer) &&
            serverPointer.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return serverPointer;
        return platformHostServing ? PlatformMotdUrl : ApexMotdUrl;
    }

    private static async Task<bool> IsPlatformHostServingAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Head, PlatformMotdUrl);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(1));
            var resp = await http.SendAsync(req, linked.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LinkRefusalText = "";
        IsShowingFallback = false;
        FallbackHtml = null;
        FallbackHasHero = false;

        var gen = _motd.BeginRemoteLoad();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(MotdLoadController.DeadlineSeconds));
        var ct = deadline.Token;

        StartupResponse? resp = null;
        try
        {
            var fetch = DimensionService.Shared.FetchStartupAsync(platform: "linux");
            var winner = await Task.WhenAny(fetch, Task.Delay(Timeout.InfiniteTimeSpan, ct));
            if (winner == fetch && fetch.IsCompletedSuccessfully)
                resp = await fetch;
            else
                resp = null;
        }
        catch
        {
            resp = null;
        }

        if (!_motd.Accepts(gen))
            return;

        if (resp is null || ct.IsCancellationRequested)
        {
            ActivateFallback(gen,
                "Could not reach syxx.app for news within five seconds. Showing the packaged offline notice.");
            return;
        }

        bool platformOk = false;
        if (!ct.IsCancellationRequested)
            platformOk = await IsPlatformHostServingAsync(ct);

        if (!_motd.RemoteSucceeded(gen))
        {
            ActivateFallback(gen,
                "Remote What's New did not finish in time. Showing the packaged offline notice.");
            return;
        }

        _motdUrl = ChooseMotdUrl(resp.WhatsNewUrl, platformOk);

        if (resp.Broadcast is { } b && IsApplicable(b))
        {
            HasBroadcast = true;
            BroadcastTitle = b.Title;
            BroadcastBody = b.Body;
            BroadcastAccent = AccentFor(b.Severity);
            _broadcastUrl = b.Url;
            HasBroadcastLink = !string.IsNullOrWhiteSpace(b.Url);
            StatusText = "";
        }
        else
        {
            HasBroadcast = false;
            StatusText = "No announcements right now.";
        }

        IsLoading = false;
    }

    private void ActivateFallback(ulong gen, string status)
    {
        if (!_motd.ActivateFallback(gen)) return;
        var html = MotdLoadController.ReadPackagedFallbackHtml();
        if (html is null)
        {
            _ = _motd.MarkRendererUnavailable(gen);
            HasBroadcast = false;
            IsFromCache = false;
            ProvenanceText = "";
            StatusText = status + " Packaged fallback-motd.html was not available.";
            IsShowingFallback = false;
            IsLoading = false;
            return;
        }

        FallbackHtml = html;
        FallbackHasHero = html.Contains("masthead", StringComparison.Ordinal)
                          || html.Contains("<svg", StringComparison.OrdinalIgnoreCase)
                          || html.Contains("<img", StringComparison.OrdinalIgnoreCase);
        IsShowingFallback = true;
        HasBroadcast = false;
        IsFromCache = false;
        ProvenanceText = "";
        StatusText = status;
        IsLoading = false;
    }

    private static bool IsApplicable(Broadcast b) =>
        !DimensionService.Shared.HasDismissed(b.Id) &&
        DimensionService.Shared.IsVersionInRange(SyxxConfig.Version, b.MinVersion, b.MaxVersion);

    private static IBrush AccentFor(string? severity) => (severity ?? "").ToLowerInvariant() switch
    {
        "critical" or "error" => new SolidColorBrush(Color.Parse("#FF5C7A")),
        "warning"             => new SolidColorBrush(Color.Parse("#F2B33D")),
        _                     => new SolidColorBrush(Color.Parse("#7C5CFF")),
    };

    [RelayCommand]
    private void OpenFullNews() => Open(_motdUrl);

    [RelayCommand]
    private void OpenBroadcastLink() => Open(_broadcastUrl);

    [RelayCommand]
    private void DismissBroadcast()
    {
        HasBroadcast = false;
        StatusText = "No announcements right now.";
    }

    private void Open(string? url)
    {
        var decision = RemoteUrlPolicy.Evaluate(url);
        if (!decision.IsAllowed)
        {
            LinkRefusalText = $"Link not opened. {decision.Reason}";
            return;
        }
        LinkRefusalText = "";
        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LinkRefusalText = ErrorPresenter.Compose(
                "Link could not be opened.", ErrorPresenter.UserDetail(ex, "whatsnew.openLink"));
        }
    }
}

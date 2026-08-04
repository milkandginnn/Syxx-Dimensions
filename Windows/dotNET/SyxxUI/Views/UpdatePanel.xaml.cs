using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Syxx.Kit;
using SyxxUI.ViewModels;
using Windows.UI;

namespace SyxxUI.Views;

/// Mirrors macOS UpdatePanel.swift — shows current version, last-checked time,
/// and a status banner. Check and download both go through the authenticated
/// UpdateService (Ed25519 appcast + SHA-256 package). The previous browser-only
/// LaunchUriAsync path is removed: an unauthenticated URL is not an update.
public sealed partial class UpdatePanel : Page
{
    private readonly AppViewModel _vm;
    private readonly UpdateService _updates = UpdateService.Shared;
    private DateTime? _lastChecked;

    public UpdatePanel(AppViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        VersionText.Text = SyxxConfig.Version;
        LastCheckedText.Text = "Never";
        _updates.StateChanged += OnUpdateStateChanged;
        ApplyFromService();
    }

    // ─── Public entry — called from MainWindow after startup check ─────────

    /// Apply the VM's pre-fetched update state (from startup response).
    /// E129: distinguish live-success from cached/failed — a failed or cached check must not
    /// render green "Syxx is up to date." on absent evidence.
    ///
    /// Startup metadata is a HINT only. Install still requires a signed appcast via
    /// <see cref="UpdateService"/>; the Download button runs DownloadAndInstallAsync after
    /// CheckForUpdatesAsync has verified a manifest.
    public void ApplyStartupState()
    {
        if (_vm.UpdateAvailable)
        {
            // Surface the hint; user must still run Check (or we already have a verified
            // service state). Do not invent a verified manifest from startup JSON.
            _lastChecked = DateTime.UtcNow;
            StatusDot.Fill = new SolidColorBrush(IoWarningAmber);
            StatusText.Text = $"Update available: version {_vm.UpdateLatest}";
            StatusText.Foreground = new SolidColorBrush(IoWarningAmber);
            StatusBanner.Background = new SolidColorBrush(Color.FromArgb(0x14, IoWarningAmber.R, IoWarningAmber.G, IoWarningAmber.B));
            StatusBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, IoWarningAmber.R, IoWarningAmber.G, IoWarningAmber.B));
            DownloadBtn.Visibility = Visibility.Collapsed; // require authenticated check first
            LastCheckedText.Text = _lastChecked.Value.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
            return;
        }
        if (_vm.UpdateCheckOutcome == AppViewModel.UpdateCheckOutcome.Live)
        {
            _lastChecked = DateTime.UtcNow;
        }
        ApplyFromService();
        if (_vm.UpdateCheckOutcome == AppViewModel.UpdateCheckOutcome.Live &&
            _updates.CurrentState is UpdateState.Idle)
        {
            // Startup said up-to-date live, service idle — show the existing up-to-date copy.
            StatusDot.Fill = new SolidColorBrush(IoSuccessGreen);
            StatusText.Text = "Syxx is up to date.";
            StatusText.Foreground = new SolidColorBrush(IoSuccessGreen);
            StatusBanner.Background = new SolidColorBrush(Color.FromArgb(0x14, IoSuccessGreen.R, IoSuccessGreen.G, IoSuccessGreen.B));
            StatusBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, IoSuccessGreen.R, IoSuccessGreen.G, IoSuccessGreen.B));
            DownloadBtn.Visibility = Visibility.Collapsed;
            if (_lastChecked is not null)
            {
                LastCheckedText.Text = _lastChecked.Value.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
            }
        }
        else if (_vm.UpdateCheckOutcome == AppViewModel.UpdateCheckOutcome.Cached)
        {
            StatusDot.Fill = new SolidColorBrush(IoTextSecondary);
            StatusText.Text = "Last answer was from a cached check; could not reach the server to re-check.";
            StatusText.Foreground = new SolidColorBrush(IoTextSecondary);
            StatusBanner.Background = new SolidColorBrush(Color.FromArgb(0x14, IoTextSecondary.R, IoTextSecondary.G, IoTextSecondary.B));
            StatusBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, IoTextSecondary.R, IoTextSecondary.G, IoTextSecondary.B));
            DownloadBtn.Visibility = Visibility.Collapsed;
        }
        else if (_vm.UpdateCheckOutcome == AppViewModel.UpdateCheckOutcome.Failed &&
                 _updates.CurrentState is UpdateState.Idle)
        {
            StatusDot.Fill = new SolidColorBrush(IoCriticalRed);
            StatusText.Text = "Update check failed. Check your internet connection.";
            StatusText.Foreground = new SolidColorBrush(IoCriticalRed);
            StatusBanner.Background = new SolidColorBrush(Color.FromArgb(0x14, IoCriticalRed.R, IoCriticalRed.G, IoCriticalRed.B));
            StatusBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, IoCriticalRed.R, IoCriticalRed.G, IoCriticalRed.B));
            DownloadBtn.Visibility = Visibility.Collapsed;
        }
    }

    // ─── Button handlers ──────────────────────────────────────────────────

    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        DownloadBtn.IsEnabled = false;
        CheckBtnText.Text = "Checking…";

        await _updates.CheckForUpdatesAsync();

        // Stamp last-checked only when the check produced a non-failure state (B089/E111 parity).
        if (_updates.CurrentState is not UpdateState.Failed)
        {
            _lastChecked = DateTime.UtcNow;
            _vm.UpdateCheckOutcome = AppViewModel.UpdateCheckOutcome.Live;
        }
        else
        {
            _vm.UpdateCheckOutcome = AppViewModel.UpdateCheckOutcome.Failed;
        }

        if (_updates.CurrentState is UpdateState.AvailableState avail)
        {
            _vm.UpdateAvailable = true;
            _vm.UpdateLatest = avail.Version;
            // Do not stash an unauthenticated browser URL — install uses the signed package URL.
            _vm.UpdateUrl = "";
        }
        else if (_updates.CurrentState is UpdateState.UpToDate)
        {
            _vm.UpdateAvailable = false;
        }

        CheckBtnText.Text = "Check for Updates";
        CheckBtn.IsEnabled = true;
        DownloadBtn.IsEnabled = true;
        ApplyFromService();
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        // Authenticated path only. No Launcher.LaunchUriAsync browser fallback.
        if (_updates.CurrentState is not UpdateState.AvailableState)
        {
            // Require a verified check first when only the startup hint is present.
            CheckBtn.IsEnabled = false;
            DownloadBtn.IsEnabled = false;
            CheckBtnText.Text = "Checking…";
            await _updates.CheckForUpdatesAsync();
            CheckBtnText.Text = "Check for Updates";
            CheckBtn.IsEnabled = true;
            DownloadBtn.IsEnabled = true;
            if (_updates.CurrentState is not UpdateState.AvailableState)
            {
                ApplyFromService();
                return;
            }
            _lastChecked = DateTime.UtcNow;
        }

        CheckBtn.IsEnabled = false;
        DownloadBtn.IsEnabled = false;
        await _updates.DownloadAndInstallAsync();
        CheckBtn.IsEnabled = true;
        DownloadBtn.IsEnabled = true;
        ApplyFromService();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // Find parent ContentDialog and close it
        var parent = this.Parent;
        while (parent is not null and not ContentDialog)
            parent = (parent as FrameworkElement)?.Parent;
        if (parent is ContentDialog dialog)
            dialog.Hide();
    }

    // ─── Status rendering ─────────────────────────────────────────────────

    private void OnUpdateStateChanged(UpdateState _)
    {
        // UpdateService may raise from a background thread during download progress.
        DispatcherQueue.TryEnqueue(ApplyFromService);
    }

    private void ApplyFromService()
    {
        LastCheckedText.Text = _lastChecked is not null
            ? _lastChecked.Value.ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
            : "Never";

        var state = _updates.CurrentState;
        Color color;
        string text;
        bool showDownload;

        switch (state)
        {
            case UpdateState.Idle:
                color = IoTextSecondary;
                text = "Not yet checked.";
                showDownload = false;
                break;
            case UpdateState.Checking:
                color = IoTextSecondary;
                text = "Not yet checked.";
                showDownload = false;
                break;
            case UpdateState.UpToDate:
                color = IoSuccessGreen;
                text = "Syxx is up to date.";
                showDownload = false;
                break;
            case UpdateState.AvailableState avail:
                color = IoWarningAmber;
                text = $"Update available: version {avail.Version}";
                showDownload = true;
                break;
            case UpdateState.Downloading:
                // Reuse the available banner; button row is disabled during the op.
                color = IoWarningAmber;
                text = _vm.UpdateAvailable && !string.IsNullOrEmpty(_vm.UpdateLatest)
                    ? $"Update available: version {_vm.UpdateLatest}"
                    : "Not yet checked.";
                showDownload = true;
                break;
            case UpdateState.Installing:
                color = IoWarningAmber;
                text = _vm.UpdateAvailable && !string.IsNullOrEmpty(_vm.UpdateLatest)
                    ? $"Update available: version {_vm.UpdateLatest}"
                    : "Not yet checked.";
                showDownload = true;
                break;
            case UpdateState.Failed failed:
                color = IoCriticalRed;
                // Prefer the service's existing refusal strings when present; otherwise the
                // panel's existing connectivity failure copy.
                text = string.IsNullOrWhiteSpace(failed.Reason)
                    ? "Update check failed. Check your internet connection."
                    : failed.Reason;
                showDownload = false;
                break;
            default:
                color = IoTextSecondary;
                text = "Not yet checked.";
                showDownload = false;
                break;
        }

        StatusDot.Fill = new SolidColorBrush(color);
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(color);
        StatusBanner.Background = new SolidColorBrush(Color.FromArgb(0x14, color.R, color.G, color.B));
        StatusBanner.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
        DownloadBtn.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
    }

    // E148: resolve severity colours from the single design-system source (IoDesignSystem.xaml),
    // not a fourth, divergent hard-coded palette. Two mappings eventually disagree.
    private static Color IoTextSecondary => (Color)Application.Current.Resources["IoTextSecondaryColor"];
    private static Color IoSuccessGreen => (Color)Application.Current.Resources["IoSuccessGreenColor"];
    private static Color IoWarningAmber => (Color)Application.Current.Resources["IoWarningAmberColor"];
    private static Color IoCriticalRed => (Color)Application.Current.Resources["IoCriticalRedColor"];
}

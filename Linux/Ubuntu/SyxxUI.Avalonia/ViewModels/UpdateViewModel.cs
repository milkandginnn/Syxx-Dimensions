using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// Linux Avalonia update surface (Phase 3.2). Same authenticated UpdateService as
/// Windows/macOS: signed appcast first, SHA-256 before install, package type from the
/// manifest (.deb on Linux). User-visible strings reuse existing UpdatePanel copy only.
/// </summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly UpdateService _updates = UpdateService.Shared;

    [ObservableProperty] private string _versionText = SyxxConfig.Version;
    [ObservableProperty] private string _lastCheckedText = "Never";
    [ObservableProperty] private string _statusText = "Not yet checked.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _downloadVisible;

    private DateTime? _lastChecked;
    private string? _lastAvailableVersion;

    public UpdateViewModel()
    {
        _updates.StateChanged += _ => Dispatcher.UIThread.Post(ApplyFromService);
        ApplyFromService();
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _updates.CheckForUpdatesAsync();
            if (_updates.CurrentState is not UpdateState.Failed)
            {
                _lastChecked = DateTime.UtcNow;
            }
            ApplyFromService();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (_updates.CurrentState is not UpdateState.AvailableState)
            {
                await _updates.CheckForUpdatesAsync();
                if (_updates.CurrentState is not UpdateState.AvailableState)
                {
                    ApplyFromService();
                    return;
                }
                _lastChecked = DateTime.UtcNow;
            }
            await _updates.DownloadAndInstallAsync();
            ApplyFromService();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFromService()
    {
        LastCheckedText = _lastChecked is not null
            ? _lastChecked.Value.ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
            : "Never";

        switch (_updates.CurrentState)
        {
            case UpdateState.Idle:
            case UpdateState.Checking:
                StatusText = "Not yet checked.";
                DownloadVisible = false;
                break;
            case UpdateState.UpToDate:
                StatusText = "Syxx is up to date.";
                DownloadVisible = false;
                break;
            case UpdateState.AvailableState avail:
                _lastAvailableVersion = avail.Version;
                StatusText = $"Update available: version {avail.Version}";
                DownloadVisible = true;
                break;
            case UpdateState.Downloading:
            case UpdateState.Installing:
                // Reuse the existing "Update available: version …" line; busy disables buttons.
                if (!string.IsNullOrEmpty(_lastAvailableVersion))
                {
                    StatusText = $"Update available: version {_lastAvailableVersion}";
                }
                DownloadVisible = true;
                break;
            case UpdateState.Failed failed:
                StatusText = string.IsNullOrWhiteSpace(failed.Reason)
                    ? "Update check failed. Check your internet connection."
                    : failed.Reason;
                DownloadVisible = false;
                break;
            default:
                StatusText = "Not yet checked.";
                DownloadVisible = false;
                break;
        }
    }
}

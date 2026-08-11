using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Syxx.Kit;

namespace SyxxUI.ViewModels;

public sealed record BundledAgreement(AgreementDocument Document, string Text)
{
    public const string Version = "syxx-eula-draft-2026-07-30";

    public static BundledAgreement? Load(string? baseDirectory = null)
    {
        var path = Path.Combine(
            baseDirectory ?? AppContext.BaseDirectory,
            "Assets",
            "Legal",
            "Syxx-EULA.txt");

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new BundledAgreement(
            new AgreementDocument(Version, digest),
            text);
    }
}

/// <summary>
/// Presentation-only state for the EULA and guided-setup gates. This type intentionally has no
/// WinUI dependency so persistence and lifecycle behavior can be tested on every development host.
/// </summary>
public sealed class FirstRunViewModel : INotifyPropertyChanged
{
    public const string AcceptanceRequiredMessage =
        "You must accept the EULA in order to use Syxx. You have not yet accepted the EULA.";
    public const string AgreementSaveFailureMessage =
        "Syxx could not save your agreement acceptance. Nothing has been accepted.";
    public const string SetupSaveFailureMessage =
        "Syxx could not save your setup. Your choices have not been applied.";
    public const string StateUnknownMessage =
        "Syxx could not verify the saved setup. Syxx will remain closed until the settings can be read.";

    private FirstRunCoordinator? _coordinator;
    private readonly Func<(FirstRunCoordinator Coordinator, string AgreementText)?>? _liveLoader;
    private string _agreementText;
    private bool _hasAcceptedTerms;
    private string? _pendingAlertMessage;
    private string? _failureMessage;
    private string _experienceMode = "story";
    private string _resolverId = EncryptedDNSConfig.DefaultResolverId;
    private EncryptedDNSTransport _transport = EncryptedDNSTransport.Doh;
    private bool _encryptedDnsEnabled = true;
    private bool _notificationsEnabled = true;
    private bool _telemetryEnabled;
    private bool _updateCheckEnabled;
    private bool _crashReportingEnabled;
    private string _themeMode = "system";

    public FirstRunViewModel(
        FirstRunCoordinator coordinator,
        string agreementText)
    {
        _coordinator = coordinator;
        _agreementText = agreementText;
    }

    private FirstRunViewModel(
        Func<(FirstRunCoordinator Coordinator, string AgreementText)?> liveLoader)
    {
        _liveLoader = liveLoader;
        _agreementText = string.Empty;
        ReloadLive();
    }

    public static FirstRunViewModel CreateLive() =>
        new(() =>
        {
            var agreement = BundledAgreement.Load();
            return agreement is null
                ? null
                : (
                    new FirstRunCoordinator(
                        agreement.Document,
                        SyxxSettingsStore.DefaultPath),
                    agreement.Text);
        });

    public event PropertyChangedEventHandler? PropertyChanged;

    public OnboardingState State => _coordinator?.State ?? OnboardingState.StateUnknown;
    public SyxxSettings? Settings => _coordinator?.Settings;
    public string AgreementText => _agreementText;

    public bool HasAcceptedTerms
    {
        get => _hasAcceptedTerms;
        set => SetField(ref _hasAcceptedTerms, value);
    }

    public bool ShowAcceptanceAlert => PendingAlertMessage is not null;

    public string? PendingAlertMessage
    {
        get => _pendingAlertMessage;
        private set
        {
            if (SetField(ref _pendingAlertMessage, value))
            {
                OnPropertyChanged(nameof(ShowAcceptanceAlert));
            }
        }
    }

    public string? FailureMessage
    {
        get => _failureMessage;
        private set => SetField(ref _failureMessage, value);
    }

    public string ExperienceMode
    {
        get => _experienceMode;
        set => SetField(ref _experienceMode, value);
    }

    public string ResolverId
    {
        get => _resolverId;
        set => SetField(ref _resolverId, value);
    }

    public EncryptedDNSTransport Transport
    {
        get => _transport;
        set => SetField(ref _transport, value);
    }

    public bool EncryptedDnsEnabled
    {
        get => _encryptedDnsEnabled;
        set => SetField(ref _encryptedDnsEnabled, value);
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set => SetField(ref _notificationsEnabled, value);
    }

    public bool TelemetryEnabled
    {
        get => _telemetryEnabled;
        set => SetField(ref _telemetryEnabled, value);
    }

    public bool UpdateCheckEnabled
    {
        get => _updateCheckEnabled;
        set => SetField(ref _updateCheckEnabled, value);
    }

    public bool CrashReportingEnabled
    {
        get => _crashReportingEnabled;
        set => SetField(ref _crashReportingEnabled, value);
    }

    public string ThemeMode
    {
        get => _themeMode;
        set => SetField(ref _themeMode, value);
    }

    public bool AttemptAcceptAgreement()
    {
        PendingAlertMessage = null;
        FailureMessage = null;
        if (!HasAcceptedTerms)
        {
            PendingAlertMessage = AcceptanceRequiredMessage;
            return false;
        }
        if (_coordinator is null)
        {
            FailureMessage = StateUnknownMessage;
            RaiseLifecycleProperties();
            return false;
        }

        var result = _coordinator.AcceptAgreement();
        FailureMessage = result switch
        {
            PersistenceResult.FailureResult => AgreementSaveFailureMessage,
            PersistenceResult.UnknownResult => StateUnknownMessage,
            _ => null,
        };
        RaiseLifecycleProperties();
        return result is PersistenceResult.SuccessResult
            && State == OnboardingState.SetupRequired;
    }

    public void DismissAcceptanceAlert() => PendingAlertMessage = null;

    public bool CompleteSetup(bool useDefaults)
    {
        FailureMessage = null;
        if (_coordinator is null)
        {
            FailureMessage = StateUnknownMessage;
            RaiseLifecycleProperties();
            return false;
        }

        var selection = useDefaults
            ? GuidedSetupSelection.SafeDefaults
            : new GuidedSetupSelection(
                ExperienceMode,
                ResolverId,
                Transport,
                EncryptedDnsEnabled,
                NotificationsEnabled,
                TelemetryEnabled,
                UpdateCheckEnabled,
                CrashReportingEnabled,
                ThemeMode);
        var result = _coordinator.CompleteSetup(selection);
        FailureMessage = result switch
        {
            PersistenceResult.FailureResult => SetupSaveFailureMessage,
            PersistenceResult.UnknownResult => StateUnknownMessage,
            _ => null,
        };
        RaiseLifecycleProperties();
        return result is PersistenceResult.SuccessResult
            && State == OnboardingState.Ready;
    }

    public void Retry()
    {
        FailureMessage = null;
        PendingAlertMessage = null;
        if (_coordinator is not null)
        {
            _coordinator.Reload();
        }
        else
        {
            ReloadLive();
        }
        RaiseLifecycleProperties();
    }

    private void ReloadLive()
    {
        var loaded = _liveLoader?.Invoke();
        _coordinator = loaded?.Coordinator;
        _agreementText = loaded?.AgreementText ?? string.Empty;
    }

    private void RaiseLifecycleProperties()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(AgreementText));
        OnPropertyChanged(nameof(FailureMessage));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// One-way startup latch. No application service can start until verified onboarding state is Ready.
/// </summary>
public sealed class ReadyServicesGate
{
    private bool _started;

    public bool TryStart(OnboardingState state, Action initializer)
    {
        if (_started || state != OnboardingState.Ready)
        {
            return false;
        }
        initializer();
        _started = true;
        return true;
    }
}

public static class ReadyRuntimePolicy
{
    public static bool ShouldDeliverMonitorNotification(
        MonitorState state,
        bool notificationsEnabled) =>
        state == MonitorState.Changed && notificationsEnabled;
}

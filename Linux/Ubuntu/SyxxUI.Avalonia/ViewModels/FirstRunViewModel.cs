using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Syxx.Kit;

namespace SyxxUI.Avalonia.ViewModels;

public sealed partial class FirstRunViewModel : ObservableObject
{
    public const string AcceptanceRequiredMessage =
        "You must accept the EULA in order to use Syxx. You have not yet accepted the EULA.";
    public const string AgreementSaveFailureMessage =
        "Syxx could not save your agreement acceptance. Nothing has been accepted.";
    public const string SetupSaveFailureMessage =
        "Syxx could not save your setup. Your choices have not been applied.";
    public const string StateUnknownMessage =
        "Syxx could not verify the saved setup. Syxx will remain closed until the settings can be read.";

    private readonly FirstRunCoordinator? _coordinator;

    public FirstRunViewModel(FirstRunCoordinator coordinator, string agreementText)
    {
        _coordinator = coordinator;
        AgreementText = agreementText;
        State = coordinator.State;

        foreach (var resolver in EncryptedDNSConfig.Catalog)
        {
            Resolvers.Add(resolver.DisplayName);
        }

        ResolverIndex = Math.Max(
            0,
            Array.FindIndex(
                EncryptedDNSConfig.Catalog.ToArray(),
                resolver => resolver.Id == coordinator.Settings.EncryptedDnsResolverId));
        ExperienceMode = coordinator.Settings.ExperienceMode;
        UseDot = coordinator.Settings.EncryptedDnsTransport == EncryptedDNSTransport.Dot;
        EncryptedDnsEnabled =
            coordinator.State is OnboardingState.AgreementRequired or OnboardingState.SetupRequired
            || coordinator.Settings.EncryptedDnsEnabled;
        NotificationsEnabled = coordinator.Settings.NotificationsEnabled;
        TelemetryEnabled = coordinator.Settings.TelemetryEnabled;
        UpdateCheckEnabled = coordinator.Settings.UpdateCheckEnabled;
        CrashReportingEnabled = coordinator.Settings.CrashReportingEnabled;
        RefreshPresentation();
    }

    private FirstRunViewModel(string failureMessage)
    {
        AgreementText = "";
        State = OnboardingState.StateUnknown;
        FailureMessage = failureMessage;
        RefreshPresentation();
    }

    public static FirstRunViewModel AgreementUnavailable() =>
        new(StateUnknownMessage);

    public event Action<SyxxSettings>? Ready;

    public string AgreementText { get; }
    public string AcceptanceAlertMessage => AcceptanceRequiredMessage;
    public ObservableCollection<string> Resolvers { get; } = new();

    [ObservableProperty] private OnboardingState _state;
    [ObservableProperty] private bool _hasAcceptedTerms;
    [ObservableProperty] private bool _showAcceptanceAlert;
    [ObservableProperty] private string _failureMessage = "";
    [ObservableProperty] private bool _isAgreementVisible;
    [ObservableProperty] private bool _isSetupVisible;
    [ObservableProperty] private bool _isFailureVisible;
    [ObservableProperty] private bool _isLoadingVisible;

    [ObservableProperty] private string _experienceMode = "story";
    [ObservableProperty] private int _resolverIndex;
    [ObservableProperty] private bool _useDot;
    [ObservableProperty] private bool _encryptedDnsEnabled = true;
    [ObservableProperty] private bool _notificationsEnabled = true;
    [ObservableProperty] private bool _telemetryEnabled;
    [ObservableProperty] private bool _updateCheckEnabled;
    [ObservableProperty] private bool _crashReportingEnabled;

    public bool AttemptAcceptAgreement()
    {
        ShowAcceptanceAlert = false;
        if (!HasAcceptedTerms)
        {
            ShowAcceptanceAlert = true;
            return false;
        }
        if (_coordinator is null)
        {
            State = OnboardingState.StateUnknown;
            FailureMessage = StateUnknownMessage;
            RefreshPresentation();
            return false;
        }

        var result = _coordinator.AcceptAgreement();
        State = _coordinator.State;
        FailureMessage = result switch
        {
            PersistenceResult.FailureResult => AgreementSaveFailureMessage,
            PersistenceResult.UnknownResult => StateUnknownMessage,
            _ => "",
        };
        RefreshPresentation();
        return result is PersistenceResult.SuccessResult;
    }

    public bool CompleteSetup(bool useDefaults)
    {
        if (_coordinator is null)
        {
            State = OnboardingState.StateUnknown;
            FailureMessage = StateUnknownMessage;
            RefreshPresentation();
            return false;
        }

        var selection = useDefaults
            ? GuidedSetupSelection.SafeDefaults
            : new GuidedSetupSelection(
                ExperienceMode,
                CurrentResolverId,
                UseDot ? EncryptedDNSTransport.Dot : EncryptedDNSTransport.Doh,
                EncryptedDnsEnabled,
                NotificationsEnabled,
                TelemetryEnabled,
                UpdateCheckEnabled,
                CrashReportingEnabled);
        var result = _coordinator.CompleteSetup(selection);
        State = _coordinator.State;
        FailureMessage = result switch
        {
            PersistenceResult.FailureResult => SetupSaveFailureMessage,
            PersistenceResult.UnknownResult => StateUnknownMessage,
            _ => "",
        };
        RefreshPresentation();
        if (result is PersistenceResult.SuccessResult)
        {
            Ready?.Invoke(_coordinator.Settings);
            return true;
        }
        return false;
    }

    public void Retry()
    {
        if (_coordinator is null)
        {
            return;
        }

        _coordinator.Reload();
        State = _coordinator.State;
        FailureMessage = State switch
        {
            OnboardingState.PersistenceFailure
                when _coordinator.FailureContext == LifecycleFailureContext.Agreement =>
                AgreementSaveFailureMessage,
            OnboardingState.PersistenceFailure => SetupSaveFailureMessage,
            OnboardingState.StateUnknown => StateUnknownMessage,
            _ => "",
        };
        RefreshPresentation();
    }

    public SyxxSettings? ReadySettings =>
        State == OnboardingState.Ready ? _coordinator?.Settings : null;

    private string CurrentResolverId =>
        EncryptedDNSConfig.Catalog[
            Math.Clamp(ResolverIndex, 0, EncryptedDNSConfig.Catalog.Count - 1)].Id;

    private void RefreshPresentation()
    {
        IsAgreementVisible = State == OnboardingState.AgreementRequired;
        IsSetupVisible = State == OnboardingState.SetupRequired;
        IsFailureVisible =
            State is OnboardingState.PersistenceFailure or OnboardingState.StateUnknown;
        IsLoadingVisible = State == OnboardingState.Loading;
    }
}

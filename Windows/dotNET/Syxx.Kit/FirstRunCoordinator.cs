using System;
using System.Globalization;

namespace Syxx.Kit;

public enum LifecycleFailureContext
{
    Agreement,
    Setup,
}

public sealed record GuidedSetupSelection(
    string ExperienceMode,
    string ResolverId,
    EncryptedDNSTransport Transport,
    bool EncryptedDnsEnabled,
    bool NotificationsEnabled,
    bool TelemetryEnabled,
    bool UpdateCheckEnabled,
    bool CrashReportingEnabled,
    string ThemeMode)
{
    public static GuidedSetupSelection SafeDefaults { get; } = new(
        ExperienceMode: "story",
        ResolverId: EncryptedDNSConfig.DefaultResolverId,
        Transport: EncryptedDNSTransport.Doh,
        EncryptedDnsEnabled: false,
        NotificationsEnabled: true,
        TelemetryEnabled: false,
        UpdateCheckEnabled: false,
        CrashReportingEnabled: false,
        ThemeMode: "system");
}

public sealed class FirstRunCoordinator
{
    public const int CurrentSetupVersion = 1;

    public OnboardingState State { get; private set; } = OnboardingState.Loading;
    public SyxxSettings Settings { get; private set; }
    public PersistenceResult? LastPersistenceResult { get; private set; }
    public LifecycleFailureContext FailureContext { get; private set; } = LifecycleFailureContext.Setup;
    public AgreementDocument Document { get; }

    private readonly string _settingsPath;
    private readonly SettingsPersistence _persistence;

    public FirstRunCoordinator(
        AgreementDocument document,
        string settingsPath,
        SettingsPersistence? persistence = null)
    {
        Document = document;
        _settingsPath = settingsPath;
        _persistence = persistence ?? SettingsPersistence.Live;
        Settings = SyxxSettingsStore.LoadWithStatus(settingsPath) switch
        {
            SettingsLoadStatus.LoadedStatus loaded => loaded.Settings,
            _ => FirstRunSettings(),
        };
        Reload();
    }

    public void Reload()
    {
        var load = SyxxSettingsStore.LoadWithStatus(_settingsPath);
        if (load is SettingsLoadStatus.LoadedStatus loaded)
        {
            Settings = loaded.Settings;
        }
        State = OnboardingLifecycle.Resolve(load, Document, CurrentSetupVersion);
    }

    public PersistenceResult AcceptAgreement(DateTimeOffset? acceptedAt = null)
    {
        var candidate = Clone(Settings);
        candidate.SettingsSchemaVersion = SyxxSettings.CurrentSchemaVersion;
        candidate.AgreementVersion = Document.Version;
        candidate.AgreementSha256 = Document.Sha256;
        candidate.AgreementAcceptedAtUtc = UtcString(acceptedAt ?? DateTimeOffset.UtcNow);

        var result = SyxxSettingsStore.SaveAndVerify(candidate, _settingsPath, _persistence);
        LastPersistenceResult = result;
        switch (result)
        {
            case PersistenceResult.SuccessResult:
                Settings = candidate;
                State = OnboardingState.SetupRequired;
                break;
            case PersistenceResult.FailureResult:
                FailureContext = LifecycleFailureContext.Agreement;
                State = OnboardingState.PersistenceFailure;
                break;
            default:
                FailureContext = LifecycleFailureContext.Agreement;
                State = OnboardingState.StateUnknown;
                break;
        }
        return result;
    }

    public PersistenceResult CompleteSetup(
        GuidedSetupSelection selection,
        DateTimeOffset? completedAt = null)
    {
        if (OnboardingLifecycle.AgreementState(Settings, Document) != AgreementState.Accepted)
        {
            State = OnboardingState.AgreementRequired;
            return PersistenceResult.Failure("agreement-required");
        }

        var candidate = Clone(Settings);
        candidate.SettingsSchemaVersion = SyxxSettings.CurrentSchemaVersion;
        candidate.ExperienceMode = selection.ExperienceMode;
        candidate.EncryptedDnsResolverId = selection.ResolverId;
        candidate.EncryptedDnsTransport = selection.Transport;
        candidate.EncryptedDnsEnabled = selection.EncryptedDnsEnabled;
        candidate.NotificationsEnabled = selection.NotificationsEnabled;
        candidate.TelemetryEnabled = selection.TelemetryEnabled;
        candidate.UpdateCheckEnabled = selection.UpdateCheckEnabled;
        candidate.CrashReportingEnabled = selection.CrashReportingEnabled;
        candidate.ThemeMode = selection.ThemeMode;
        candidate.CompletedSetupVersion = CurrentSetupVersion;
        candidate.SetupCompletedAtUtc = UtcString(completedAt ?? DateTimeOffset.UtcNow);

        var result = SyxxSettingsStore.SaveAndVerify(candidate, _settingsPath, _persistence);
        LastPersistenceResult = result;
        switch (result)
        {
            case PersistenceResult.SuccessResult:
                Settings = candidate;
                State = OnboardingState.Ready;
                break;
            case PersistenceResult.FailureResult:
                FailureContext = LifecycleFailureContext.Setup;
                State = OnboardingState.PersistenceFailure;
                break;
            default:
                FailureContext = LifecycleFailureContext.Setup;
                State = OnboardingState.StateUnknown;
                break;
        }
        return result;
    }

    private static SyxxSettings FirstRunSettings() => new()
    {
        NotificationsEnabled = GuidedSetupSelection.SafeDefaults.NotificationsEnabled,
        TelemetryEnabled = GuidedSetupSelection.SafeDefaults.TelemetryEnabled,
        UpdateCheckEnabled = GuidedSetupSelection.SafeDefaults.UpdateCheckEnabled,
        CrashReportingEnabled = GuidedSetupSelection.SafeDefaults.CrashReportingEnabled,
        ThemeMode = GuidedSetupSelection.SafeDefaults.ThemeMode,
    };

    private static SyxxSettings Clone(SyxxSettings settings) => new()
    {
        EncryptedDnsEnabled = settings.EncryptedDnsEnabled,
        EncryptedDnsResolverId = settings.EncryptedDnsResolverId,
        EncryptedDnsTransport = settings.EncryptedDnsTransport,
        ScanResolverId = settings.ScanResolverId,
        NotificationsEnabled = settings.NotificationsEnabled,
        TelemetryEnabled = settings.TelemetryEnabled,
        UpdateCheckEnabled = settings.UpdateCheckEnabled,
        CrashReportingEnabled = settings.CrashReportingEnabled,
        EulaAccepted = settings.EulaAccepted,
        ExperienceMode = settings.ExperienceMode,
        ThemeMode = settings.ThemeMode,
        SettingsSchemaVersion = settings.SettingsSchemaVersion,
        AgreementVersion = settings.AgreementVersion,
        AgreementSha256 = settings.AgreementSha256,
        AgreementAcceptedAtUtc = settings.AgreementAcceptedAtUtc,
        CompletedSetupVersion = settings.CompletedSetupVersion,
        SetupCompletedAtUtc = settings.SetupCompletedAtUtc,
    };

    private static string UtcString(DateTimeOffset value) =>
        value.UtcDateTime.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture);
}

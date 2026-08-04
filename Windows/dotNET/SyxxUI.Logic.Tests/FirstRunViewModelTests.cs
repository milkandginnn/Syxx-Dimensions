using Syxx.Kit;
using SyxxUI.ViewModels;
using Xunit;
using System.Security.Cryptography;
using System.Text;

namespace SyxxUI.Logic.Tests;

public sealed class FirstRunViewModelTests : IDisposable
{
    private static readonly AgreementDocument CurrentDocument =
        new("syxx-eula-draft-2026-07-30", new string('a', 64));

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"syxx-windows-first-run-{Guid.NewGuid():N}");

    [Fact]
    public void ContinueWithoutCheckboxKeepsAgreementGateClosedAndRequestsAlert()
    {
        var model = CreateModel();

        var advanced = model.AttemptAcceptAgreement();

        Assert.False(advanced);
        Assert.True(model.ShowAcceptanceAlert);
        Assert.Equal(
            "You must accept the EULA in order to use Syxx. You have not yet accepted the EULA.",
            model.PendingAlertMessage);
        Assert.Equal(OnboardingState.AgreementRequired, model.State);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void CheckedAgreementAdvancesOnlyAfterVerifiedPersistence()
    {
        var model = CreateModel();
        model.HasAcceptedTerms = true;

        var advanced = model.AttemptAcceptAgreement();

        Assert.True(advanced);
        Assert.Equal(OnboardingState.SetupRequired, model.State);
        Assert.True(File.Exists(SettingsPath));
        Assert.IsType<SettingsLoadStatus.LoadedStatus>(
            SyxxSettingsStore.LoadWithStatus(SettingsPath));
    }

    [Fact]
    public void AgreementWriteFailureDoesNotAdvanceToSetup()
    {
        var persistence = new SettingsPersistence(
            write: (_, _) => throw new IOException("disk unavailable"),
            read: _ => throw new InvalidOperationException("read must not run"));
        var model = CreateModel(persistence);
        model.HasAcceptedTerms = true;

        var advanced = model.AttemptAcceptAgreement();

        Assert.False(advanced);
        Assert.Equal(OnboardingState.PersistenceFailure, model.State);
        Assert.Equal(
            "Syxx could not save your agreement acceptance. Nothing has been accepted.",
            model.FailureMessage);
    }

    [Fact]
    public void SetupReadbackFailureDoesNotOpenApplicationShell()
    {
        var writes = new Dictionary<string, byte[]>();
        var readCount = 0;
        var persistence = new SettingsPersistence(
            write: (data, path) => writes[path] = data,
            read: path =>
            {
                var bytes = writes[path].ToArray();
                readCount++;
                if (readCount == 2)
                {
                    bytes[^2] ^= 0x01;
                }
                return bytes;
            });
        var model = CreateModel(persistence);
        model.HasAcceptedTerms = true;

        Assert.True(model.AttemptAcceptAgreement());
        Assert.False(model.CompleteSetup(useDefaults: false));
        Assert.NotEqual(OnboardingState.Ready, model.State);
        Assert.Equal(
            "Syxx could not verify the saved setup. Syxx will remain closed until the settings can be read.",
            model.FailureMessage);
    }

    [Fact]
    public void CompletedSetupUsesSelectedValuesAndBecomesReady()
    {
        var model = CreateModel();
        model.HasAcceptedTerms = true;
        Assert.True(model.AttemptAcceptAgreement());

        model.ExperienceMode = "general";
        model.ResolverId = "quad9";
        model.Transport = EncryptedDNSTransport.Dot;
        model.EncryptedDnsEnabled = true;
        model.NotificationsEnabled = false;
        model.UpdateCheckEnabled = true;
        model.CrashReportingEnabled = true;
        model.TelemetryEnabled = true;

        var completed = model.CompleteSetup(useDefaults: false);

        Assert.True(completed);
        Assert.Equal(OnboardingState.Ready, model.State);
        var loaded = Assert.IsType<SettingsLoadStatus.LoadedStatus>(
            SyxxSettingsStore.LoadWithStatus(SettingsPath));
        Assert.Equal("general", loaded.Settings.ExperienceMode);
        Assert.Equal("quad9", loaded.Settings.EncryptedDnsResolverId);
        Assert.Equal(EncryptedDNSTransport.Dot, loaded.Settings.EncryptedDnsTransport);
        Assert.True(loaded.Settings.EncryptedDnsEnabled);
        Assert.False(loaded.Settings.NotificationsEnabled);
        Assert.True(loaded.Settings.UpdateCheckEnabled);
        Assert.True(loaded.Settings.CrashReportingEnabled);
        Assert.True(loaded.Settings.TelemetryEnabled);
    }

    [Fact]
    public void SkipUsesFailClosedDefaultsAndBecomesReady()
    {
        var model = CreateModel();
        model.HasAcceptedTerms = true;
        Assert.True(model.AttemptAcceptAgreement());

        Assert.True(model.CompleteSetup(useDefaults: true));

        var loaded = Assert.IsType<SettingsLoadStatus.LoadedStatus>(
            SyxxSettingsStore.LoadWithStatus(SettingsPath));
        Assert.Equal("story", loaded.Settings.ExperienceMode);
        Assert.False(loaded.Settings.EncryptedDnsEnabled);
        Assert.True(loaded.Settings.NotificationsEnabled);
        Assert.False(loaded.Settings.UpdateCheckEnabled);
        Assert.False(loaded.Settings.CrashReportingEnabled);
        Assert.False(loaded.Settings.TelemetryEnabled);
    }

    [Fact]
    public void ReadyServicesGateRunsInitializerOnceAndOnlyForReadyState()
    {
        var gate = new ReadyServicesGate();
        var calls = 0;

        Assert.False(gate.TryStart(OnboardingState.AgreementRequired, () => calls++));
        Assert.False(gate.TryStart(OnboardingState.SetupRequired, () => calls++));
        Assert.False(gate.TryStart(OnboardingState.StateUnknown, () => calls++));
        Assert.Equal(0, calls);

        Assert.True(gate.TryStart(OnboardingState.Ready, () => calls++));
        Assert.False(gate.TryStart(OnboardingState.Ready, () => calls++));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void MonitorNotificationPolicyRequiresBothChangeAndPersistedOptIn()
    {
        Assert.True(ReadyRuntimePolicy.ShouldDeliverMonitorNotification(
            MonitorState.Changed,
            notificationsEnabled: true));
        Assert.False(ReadyRuntimePolicy.ShouldDeliverMonitorNotification(
            MonitorState.Changed,
            notificationsEnabled: false));
        Assert.False(ReadyRuntimePolicy.ShouldDeliverMonitorNotification(
            MonitorState.NoChange,
            notificationsEnabled: true));
        Assert.False(ReadyRuntimePolicy.ShouldDeliverMonitorNotification(
            MonitorState.BaselineEstablished,
            notificationsEnabled: true));
    }

    [Fact]
    public void BundledAgreementUsesExactBytesForTextAndDigest()
    {
        var legalDirectory = Path.Combine(_directory, "Assets", "Legal");
        Directory.CreateDirectory(legalDirectory);
        var bytes = Encoding.UTF8.GetBytes("Agreement body\n");
        File.WriteAllBytes(Path.Combine(legalDirectory, "Syxx-EULA.txt"), bytes);

        var agreement = Assert.IsType<BundledAgreement>(
            BundledAgreement.Load(_directory));

        Assert.Equal("Agreement body\n", agreement.Text);
        Assert.Equal(BundledAgreement.Version, agreement.Document.Version);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            agreement.Document.Sha256);
    }

    [Fact]
    public void MissingOrInvalidAgreementResourceCannotProduceAcceptableDocument()
    {
        Directory.CreateDirectory(_directory);
        Assert.Null(BundledAgreement.Load(_directory));

        var legalDirectory = Path.Combine(_directory, "Assets", "Legal");
        Directory.CreateDirectory(legalDirectory);
        File.WriteAllBytes(
            Path.Combine(legalDirectory, "Syxx-EULA.txt"),
            [0xC3, 0x28]);

        Assert.Null(BundledAgreement.Load(_directory));
    }

    private FirstRunViewModel CreateModel(SettingsPersistence? persistence = null)
    {
        Directory.CreateDirectory(_directory);
        return new FirstRunViewModel(
            new FirstRunCoordinator(CurrentDocument, SettingsPath, persistence),
            agreementText: "Agreement body");
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

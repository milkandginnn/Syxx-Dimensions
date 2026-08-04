using System.Text;
using Syxx.Kit;
using SyxxUI.Avalonia.Services;
using SyxxUI.Avalonia.ViewModels;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

public sealed class FirstRunViewModelTests : IDisposable
{
    private static readonly AgreementDocument CurrentDocument =
        new("syxx-eula-draft-2026-07-30", new string('a', 64));

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"syxx-linux-first-run-{Guid.NewGuid():N}");

    [Fact]
    public void ContinueWithoutCheckboxKeepsAgreementGateClosedAndRequestsExactAlert()
    {
        var model = CreateModel();

        var advanced = model.AttemptAcceptAgreement();

        Assert.False(advanced);
        Assert.True(model.ShowAcceptanceAlert);
        Assert.Equal(
            "You must accept the EULA in order to use Syxx. You have not yet accepted the EULA.",
            model.AcceptanceAlertMessage);
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
    public void FreshPresentationUsesMockupDefaultsWithoutCompletingSetup()
    {
        var model = CreateModel();

        Assert.Equal("story", model.ExperienceMode);
        Assert.Equal(0, model.ResolverIndex);
        Assert.False(model.UseDot);
        Assert.True(model.EncryptedDnsEnabled);
        Assert.True(model.NotificationsEnabled);
        Assert.False(model.TelemetryEnabled);
        Assert.False(model.UpdateCheckEnabled);
        Assert.False(model.CrashReportingEnabled);
        Assert.Equal(OnboardingState.AgreementRequired, model.State);
        Assert.False(File.Exists(SettingsPath));
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
    public void SetupReadbackMismatchDoesNotOpenApplicationShell()
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
    public void CorruptSettingsRemainFailClosedAndDoNotRequestAgreementAgain()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ invalid json");

        var model = CreateModel();

        Assert.Equal(OnboardingState.StateUnknown, model.State);
        Assert.False(model.IsAgreementVisible);
        Assert.False(model.IsSetupVisible);
        Assert.True(model.IsFailureVisible);
    }

    [Fact]
    public void CompletedSetupPersistsSelectedValuesAndBecomesReady()
    {
        var model = CreateModel();
        model.HasAcceptedTerms = true;
        Assert.True(model.AttemptAcceptAgreement());

        model.ExperienceMode = "general";
        model.ResolverIndex = 1;
        model.UseDot = true;
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
    public void ReadyApplicationGateConstructsShellOnceAndOnlyAfterReady()
    {
        var gate = new ReadyApplicationGate();
        var constructions = 0;
        var settings = new SyxxSettings();

        Assert.Null(gate.TryOpen(
            OnboardingState.AgreementRequired, settings, _ =>
            {
                constructions++;
                return new object();
            }));
        Assert.Null(gate.TryOpen(
            OnboardingState.SetupRequired, settings, _ =>
            {
                constructions++;
                return new object();
            }));
        Assert.Null(gate.TryOpen(
            OnboardingState.StateUnknown, settings, _ =>
            {
                constructions++;
                return new object();
            }));
        Assert.Equal(0, constructions);

        Assert.NotNull(gate.TryOpen(
            OnboardingState.Ready, settings, _ =>
            {
                constructions++;
                return new object();
            }));
        Assert.Null(gate.TryOpen(
            OnboardingState.Ready, settings, _ =>
            {
                constructions++;
                return new object();
            }));
        Assert.Equal(1, constructions);
    }

    [Fact]
    public void ReadyShellAlwaysOpensAtWhatsNew()
    {
        Assert.Equal("What's New", ShellLaunchPlan.InitialDestination);
        Assert.Equal(
            ShellLaunchPlan.InitialDestination,
            Assert.IsAssignableFrom<IReadOnlyList<string>>(
                ShellLaunchPlan.NavigationItems)[0]);
    }

    [Fact]
    public void AgreementLoaderComputesDigestFromTheExactDisplayedBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("agreement body");

        var result = BundledAgreement.LoadFrom(() => new MemoryStream(bytes));

        var loaded = Assert.IsType<BundledAgreementLoadResult.Loaded>(result);
        Assert.Equal("agreement body", loaded.Agreement.Text);
        Assert.Equal(
            "e1312950a806cda855e53f603301c78c3c23ea486e1beca337731fdba766720a",
            loaded.Agreement.Document.Sha256);
    }

    [Fact]
    public void AgreementLoaderReturnsUnknownWhenResourceCannotBeRead()
    {
        var result = BundledAgreement.LoadFrom(
            () => throw new IOException("resource unavailable"));

        Assert.IsType<BundledAgreementLoadResult.Unknown>(result);
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

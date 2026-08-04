using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

public sealed class OnboardingLifecycleTests
{
    private static readonly AgreementDocument CurrentDocument =
        new("1.0", new string('a', 64));

    [Fact]
    public void AbsentSettingsRequireAgreement()
    {
        Assert.Equal(
            OnboardingState.AgreementRequired,
            OnboardingLifecycle.Resolve(
                SettingsLoadStatus.Absent(),
                CurrentDocument,
                currentSetupVersion: 1));
    }

    [Fact]
    public void CorruptOrUnavailableSettingsNeverOpenTheShell()
    {
        Assert.Equal(
            OnboardingState.StateUnknown,
            OnboardingLifecycle.Resolve(
                SettingsLoadStatus.Corrupt(),
                CurrentDocument,
                currentSetupVersion: 1));
        Assert.Equal(
            OnboardingState.StateUnknown,
            OnboardingLifecycle.Resolve(
                SettingsLoadStatus.Unavailable(),
                CurrentDocument,
                currentSetupVersion: 1));
    }

    [Fact]
    public void MatchingAgreementAndSetupResolveReady()
    {
        var settings = new SyxxSettings
        {
            AgreementVersion = CurrentDocument.Version,
            AgreementSha256 = CurrentDocument.Sha256,
            AgreementAcceptedAtUtc = "2026-07-30T15:00:00Z",
            CompletedSetupVersion = 1,
            SetupCompletedAtUtc = "2026-07-30T15:01:00Z",
        };

        Assert.Equal(
            OnboardingState.Ready,
            OnboardingLifecycle.Resolve(
                SettingsLoadStatus.Loaded(settings),
                CurrentDocument,
                currentSetupVersion: 1));
    }

    [Fact]
    public void VersionDigestAndTimestampMismatchRequireAgreement()
    {
        var settings = new SyxxSettings
        {
            AgreementVersion = "0.9",
            AgreementSha256 = CurrentDocument.Sha256,
            AgreementAcceptedAtUtc = "2026-07-30T15:00:00Z",
        };
        Assert.Equal(
            AgreementState.Required,
            OnboardingLifecycle.AgreementState(settings, CurrentDocument));

        settings.AgreementVersion = CurrentDocument.Version;
        settings.AgreementSha256 = new string('b', 64);
        Assert.Equal(
            AgreementState.Required,
            OnboardingLifecycle.AgreementState(settings, CurrentDocument));

        settings.AgreementSha256 = CurrentDocument.Sha256;
        settings.AgreementAcceptedAtUtc = "not-a-timestamp";
        Assert.Equal(
            AgreementState.Required,
            OnboardingLifecycle.AgreementState(settings, CurrentDocument));
    }

    [Fact]
    public void LegacyBooleanDoesNotProveCanonicalAgreementAcceptance()
    {
        var path = TemporaryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{"eulaAccepted":true,"experienceMode":"story"}""");

        var load = SyxxSettingsStore.LoadWithStatus(path);
        var settings = Assert.IsType<SettingsLoadStatus.LoadedStatus>(load).Settings;
        Assert.Equal(
            AgreementState.Required,
            OnboardingLifecycle.AgreementState(settings, CurrentDocument));
    }

    [Fact]
    public void SaveAndVerifyReportsSuccessOnlyAfterExactReadBack()
    {
        var settings = new SyxxSettings { ExperienceMode = "general" };
        var path = TemporaryPath();

        Assert.IsType<PersistenceResult.SuccessResult>(
            SyxxSettingsStore.SaveAndVerify(settings, path));
        var loaded = Assert.IsType<SettingsLoadStatus.LoadedStatus>(
            SyxxSettingsStore.LoadWithStatus(path));
        Assert.Equal(
            JsonSerializer.Serialize(settings),
            JsonSerializer.Serialize(loaded.Settings));
    }

    [Fact]
    public void SaveAndVerifyReportsFailureWhenWriteCannotStart()
    {
        var parentFile = TemporaryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(parentFile)!);
        File.WriteAllText(parentFile, "not a directory");
        var impossibleChild = Path.Combine(parentFile, "settings.json");

        Assert.IsType<PersistenceResult.FailureResult>(
            SyxxSettingsStore.SaveAndVerify(new SyxxSettings(), impossibleChild));
    }

    [Fact]
    public void SaveAndVerifyReportsUnknownWhenReadBackDiffers()
    {
        var expected = new SyxxSettings { ExperienceMode = "general" };
        var mismatched = new SyxxSettings { ExperienceMode = "story" };
        var persistence = new SettingsPersistence(
            write: (_, _) => { },
            read: _ => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mismatched)));

        Assert.IsType<PersistenceResult.UnknownResult>(
            SyxxSettingsStore.SaveAndVerify(expected, TemporaryPath(), persistence));
    }

    [Fact]
    public void CoordinatorPersistsAgreementAndSetupBeforeReady()
    {
        var path = TemporaryPath();
        var coordinator = new FirstRunCoordinator(CurrentDocument, path);

        Assert.Equal(OnboardingState.AgreementRequired, coordinator.State);
        Assert.IsType<PersistenceResult.SuccessResult>(
            coordinator.AcceptAgreement(DateTimeOffset.Parse("2026-07-30T15:00:00Z")));
        Assert.Equal(OnboardingState.SetupRequired, coordinator.State);
        Assert.IsType<PersistenceResult.SuccessResult>(
            coordinator.CompleteSetup(
                GuidedSetupSelection.SafeDefaults,
                DateTimeOffset.Parse("2026-07-30T15:01:00Z")));
        Assert.Equal(OnboardingState.Ready, coordinator.State);

        var returning = new FirstRunCoordinator(CurrentDocument, path);
        Assert.Equal(OnboardingState.Ready, returning.State);
    }

    [Fact]
    public void CoordinatorRemainsGatedWhenAgreementWriteFails()
    {
        var persistence = new SettingsPersistence(
            write: (_, _) => throw new IOException("fixture"),
            read: _ => []);
        var coordinator = new FirstRunCoordinator(
            CurrentDocument,
            TemporaryPath(),
            persistence);

        Assert.IsType<PersistenceResult.FailureResult>(coordinator.AcceptAgreement());
        Assert.Equal(OnboardingState.PersistenceFailure, coordinator.State);
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), "syxx-onboarding", Guid.NewGuid().ToString("N"), "settings.json");
}

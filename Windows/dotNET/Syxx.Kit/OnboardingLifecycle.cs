using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Syxx.Kit;

public sealed record AgreementDocument(string Version, string Sha256);

public enum AgreementState
{
    Accepted,
    Required,
    Unknown,
}

public enum SetupState
{
    Complete,
    Required,
    Unknown,
}

public enum OnboardingState
{
    Loading,
    AgreementRequired,
    SetupRequired,
    Ready,
    PersistenceFailure,
    StateUnknown,
}

public abstract record SettingsLoadStatus
{
    public sealed record LoadedStatus(SyxxSettings Settings) : SettingsLoadStatus;
    public sealed record AbsentStatus : SettingsLoadStatus;
    public sealed record CorruptStatus : SettingsLoadStatus;
    public sealed record UnavailableStatus : SettingsLoadStatus;

    public static SettingsLoadStatus Loaded(SyxxSettings settings) => new LoadedStatus(settings);
    public static SettingsLoadStatus Absent() => new AbsentStatus();
    public static SettingsLoadStatus Corrupt() => new CorruptStatus();
    public static SettingsLoadStatus Unavailable() => new UnavailableStatus();
}

public abstract record PersistenceResult
{
    public sealed record SuccessResult : PersistenceResult;
    public sealed record FailureResult(string Category) : PersistenceResult;
    public sealed record UnknownResult(string Category) : PersistenceResult;

    public static PersistenceResult Success() => new SuccessResult();
    public static PersistenceResult Failure(string category) => new FailureResult(category);
    public static PersistenceResult Unknown(string category) => new UnknownResult(category);
}

public sealed class SettingsPersistence
{
    public Action<byte[], string> Write { get; }
    public Func<string, byte[]> Read { get; }

    public SettingsPersistence(Action<byte[], string> write, Func<string, byte[]> read)
    {
        Write = write;
        Read = read;
    }

    public static SettingsPersistence Live { get; } = new(
        write: (data, path) => File.WriteAllBytes(path, data),
        read: path => File.ReadAllBytes(path));
}

public static class OnboardingLifecycle
{
    public static OnboardingState Resolve(
        SettingsLoadStatus loadStatus,
        AgreementDocument document,
        int currentSetupVersion)
    {
        if (loadStatus is SettingsLoadStatus.AbsentStatus)
        {
            return OnboardingState.AgreementRequired;
        }
        if (loadStatus is not SettingsLoadStatus.LoadedStatus loaded)
        {
            return OnboardingState.StateUnknown;
        }

        return AgreementState(loaded.Settings, document) switch
        {
            Syxx.Kit.AgreementState.Required => OnboardingState.AgreementRequired,
            Syxx.Kit.AgreementState.Unknown => OnboardingState.StateUnknown,
            _ => SetupState(loaded.Settings, currentSetupVersion) switch
            {
                Syxx.Kit.SetupState.Complete => OnboardingState.Ready,
                Syxx.Kit.SetupState.Required => OnboardingState.SetupRequired,
                _ => OnboardingState.StateUnknown,
            },
        };
    }

    public static AgreementState AgreementState(
        SyxxSettings settings,
        AgreementDocument document)
    {
        var digest = settings.AgreementSha256?.ToLowerInvariant();
        if (settings.AgreementVersion != document.Version
            || digest != document.Sha256.ToLowerInvariant()
            || digest?.Length != 64
            || !digest.All(Uri.IsHexDigit)
            || !IsUtcTimestamp(settings.AgreementAcceptedAtUtc))
        {
            return Syxx.Kit.AgreementState.Required;
        }
        return Syxx.Kit.AgreementState.Accepted;
    }

    public static SetupState SetupState(SyxxSettings settings, int currentSetupVersion)
    {
        if (settings.CompletedSetupVersion != currentSetupVersion
            || !IsUtcTimestamp(settings.SetupCompletedAtUtc)
            || (settings.ExperienceMode != "story" && settings.ExperienceMode != "general"))
        {
            return Syxx.Kit.SetupState.Required;
        }
        return Syxx.Kit.SetupState.Complete;
    }

    private static bool IsUtcTimestamp(string? value) =>
        value is not null
        && value.EndsWith('Z')
        && DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out _);
}

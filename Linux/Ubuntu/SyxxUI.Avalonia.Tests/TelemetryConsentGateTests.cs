using System;
using System.IO;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Twin of macOS TelemetryConsentGateTests. The Settings toggle wrote a key that NO telemetry code
/// read — Sentry and OTel both initialized unconditionally — while the privacy policy claimed
/// telemetry "can be turned off in Settings". A user-visible claim with no backing behavior is the
/// same defect class as a result with no evidence, applied to consent.
///
/// This gate governs Windows AND Ubuntu, since the Avalonia client references this assembly.
/// </summary>
public class TelemetryConsentGateTests
{
    private static SyxxSettings Settings(bool telemetry) => new() { TelemetryEnabled = telemetry };

    /// Failure case: delete the consent guard from a bootstrap → an opted-out user is still
    /// reported on → this contract no longer holds.
    [Fact]
    public void ExplicitOptOut_DeniesTelemetry()
    {
        var consent = TelemetryGate.CurrentConsent(Settings(false), envOverride: "");
        Assert.Equal(TelemetryConsent.Denied, consent);
        Assert.False(consent.AllowsTelemetry());
    }

    [Fact]
    public void ExplicitOptIn_GrantsTelemetry()
    {
        var consent = TelemetryGate.CurrentConsent(Settings(true), envOverride: "");
        Assert.Equal(TelemetryConsent.Granted, consent);
        Assert.True(consent.AllowsTelemetry());
    }

    /// SYXX_TELEMETRY=0 must beat stored settings: CI and scripted runs have to be able to
    /// guarantee silence without editing the user's file.
    [Fact]
    public void EnvironmentOverride_BeatsStoredOptIn()
    {
        var consent = TelemetryGate.CurrentConsent(Settings(true), envOverride: "0");
        Assert.Equal(TelemetryConsent.Denied, consent);
    }

    /// Unknown is NOT consent — the entire reason this is tri-state rather than a bool.
    [Fact]
    public void Unknown_IsNotConsent()
    {
        Assert.False(TelemetryConsent.Unknown.AllowsTelemetry());
        Assert.False(TelemetryConsent.Denied.AllowsTelemetry());
        Assert.True(TelemetryConsent.Granted.AllowsTelemetry());
    }

    /// A corrupt settings file must not be read as permission to report. Ties the gate to the
    /// fail-closed load path. Failure case: revert SyxxSettingsStore.Load to `catch { return
    /// new SyxxSettings(); }` → TelemetryEnabled defaults true → red.
    [Fact]
    public void CorruptSettingsFile_YieldsDeniedThroughTheGate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"syxx-gate-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "{ truncated");
        try
        {
            var loaded = SyxxSettingsStore.Load(path);
            Assert.False(loaded.TelemetryEnabled,
                "a corrupt settings file must not re-enable telemetry the user turned off");
            Assert.Equal(TelemetryConsent.Denied,
                TelemetryGate.CurrentConsent(loaded, envOverride: ""));
        }
        finally { File.Delete(path); }
    }

    /// The guard must be narrow: an ABSENT file is a first run, not a withdrawal.
    /// Failure case: fail closed on absence too → silently changes first-run behavior → red.
    [Fact]
    public void AbsentSettingsFile_StillGetsFirstRunDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"syxx-absent-{Guid.NewGuid()}.json");
        var loaded = SyxxSettingsStore.Load(path);
        Assert.Equal(new SyxxSettings().TelemetryEnabled, loaded.TelemetryEnabled);
    }
}

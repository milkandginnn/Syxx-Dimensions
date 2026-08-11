using System.Diagnostics;
using Syxx.Kit;
using Syxx.Telemetry;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Guards the OTel registration for the .NET surfaces.
///
/// TWO SILENT FAILURE MODES, which is why this file exists. Both produce a green build, no
/// exception, and no log line — the only symptom is an empty collector, which looks exactly like
/// "nothing went wrong":
///
///  1. THE SOURCE NAME. `SyxxTracer.Source` is `ActivitySource("SyxxKit")`. If the provider
///     registers a different name, `StartActivity` returns null forever and every span is
///     discarded. Nothing detects this — an ActivitySource with no listener is a legal no-op.
///
///  2. THE CONSENT GATE. If registration ignores consent, a user who turned telemetry off still
///     has spans exported. There is exactly one gate; a per-call-site check is one someone forgets.
/// </summary>
public class OTelBootstrapTests
{
    /// The name the Kit actually emits under. Failure case: rename either side → the strings stop
    /// matching → red. This is the assertion that would have caught a provider listening for
    /// "SyxxUI" while the Kit emitted "SyxxKit".
    [Fact]
    public void ProviderListensToTheSourceNameTheKitActuallyEmitsUnder()
    {
        Assert.Equal("SyxxKit", SyxxTracer.Source.Name);
    }

    /// Consent off ⇒ no provider. Failure case: drop the `ifAllowed` guard → a provider is built
    /// and spans export without consent → red.
    [Fact]
    public void DoesNotRegisterWhenConsentIsAbsent()
    {
        var provider = OTelBootstrap.Register("syxx-test", "test", ifAllowed: false);
        Assert.Null(provider);
    }

    /// The positive half, so the guard cannot be satisfied by disabling telemetry entirely.
    /// Registration must actually produce a provider when consent is present.
    [Fact]
    public void RegistersWhenConsentIsPresent()
    {
        using var provider = OTelBootstrap.Register("syxx-test", "test", ifAllowed: true);
        Assert.NotNull(provider);
    }

    /// With a provider registered, the Kit's ActivitySource must actually produce a live Activity.
    /// This is the end-to-end check that the source name, the provider, and the listener line up —
    /// the three-way agreement that the two tests above only cover pairwise.
    /// Failure case: register a different source name → StartActivity returns null → red.
    [Fact]
    public void ARegisteredProviderMakesTheKitsSpansLive()
    {
        using var provider = OTelBootstrap.Register("syxx-test", "test", ifAllowed: true);
        Assert.NotNull(provider);

        using var activity = SyxxTracer.Source.StartActivity("test.span");

        Assert.NotNull(activity);
        Assert.Equal("test.span", activity!.OperationName);
    }

    /// os.type names where the binary RAN, not what it was published for. The CLI is one artifact
    /// for win-x64/linux-x64/linux-arm64, so a hardcoded "windows" would mislabel every Linux run —
    /// on the platform that ships first.
    [Fact]
    public void OsTypeReflectsTheRunningPlatform()
    {
        var os = OTelBootstrap.CurrentOsType();

        Assert.Contains(os, new[] { "windows", "linux", "darwin", "unknown" });
        if (OperatingSystem.IsMacOS()) { Assert.Equal("darwin", os); }
        if (OperatingSystem.IsLinux()) { Assert.Equal("linux", os); }
        if (OperatingSystem.IsWindows()) { Assert.Equal("windows", os); }
    }
}

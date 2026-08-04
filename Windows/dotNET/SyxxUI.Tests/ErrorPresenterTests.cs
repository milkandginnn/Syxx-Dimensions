using System;
using Xunit;
using SyxxUI.Presentation;

namespace SyxxUI.Tests;

/// <summary>
/// Defect-2 (Windows half): raw exception text must never reach a user-visible string.
///
/// ============================================================================================
/// HONESTY NOTICE — READ BEFORE TRUSTING THIS FILE
/// ============================================================================================
/// This file was NOT compiled or run on the machine that authored it. SyxxUI.csproj is
/// net10.0-windows/UseWinUI and its XAML compiler crashes on a non-Windows host
/// (Microsoft.UI.Xaml.Markup.Compiler.interop.targets(596,9): "output.json was not created"), so
/// SyxxUI.Tests — which project-references it — cannot restore, build, or run here either. Treat
/// this file as UNVERIFIED until a Windows host runs it. See SyxxUI.Tests.csproj's header and
/// DELTAS.md D101 for the still-unwired CI step this is waiting on.
///
/// WHAT *IS* VERIFIED. The class under test,
/// <c>Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs</c>, is framework-neutral C# that is
/// LINKED into the Avalonia client and compiled from the SAME FILE there. An equivalent and larger
/// suite (ErrorPresenterTests + two end-to-end view-model tests) was written against that link and
/// RUN GREEN on this host, including four mutation runs that each turned it red. This file exists so
/// the Windows leg is not left with zero coverage of its own, not because the behaviour is unproven.
/// ============================================================================================
/// </summary>
public class ErrorPresenterTests
{
    [Theory]
    [InlineData("Exception of type 'Syxx.Kit.WitnessSignerException' was thrown.")]
    [InlineData("Object reference not set to an instance of an object.")]
    [InlineData("Value cannot be null. (Parameter 'privateKeyBase64')")]
    [InlineData("Unable to cast object of type 'System.String' to type 'System.Int32'.")]
    [InlineData("System.Net.Sockets.SocketException (10061): Connection refused")]
    [InlineData("Access is denied. (0x80070005)")]
    [InlineData("boom\n   at SyxxUI.ViewModels.AppViewModel.Save(String key)")]
    // The exact shape Syxx.Kit/LeakDetector.cs:207 composes: a product prefix in FRONT of BCL
    // boilerplate. A prefix-only rule missed this; it is the regression this case pins.
    [InlineData("Token request failed: Exception of type 'System.Exception' was thrown.")]
    [InlineData("")]
    public void IsBoilerplate_rejects_crash_reporter_material(string raw)
        => Assert.True(ErrorPresenter.IsBoilerplate(raw));

    [Theory]
    [InlineData("No such host is known.")]
    [InlineData("The network is unreachable")]
    [InlineData("No system DNS resolvers detected.")]
    public void IsBoilerplate_keeps_genuine_human_readable_causes(string raw)
        => Assert.False(ErrorPresenter.IsBoilerplate(raw));

    [Fact]
    public void UserDetail_returns_null_rather_than_inventing_a_cause()
    {
        Assert.Null(ErrorPresenter.UserDetail(new Exception(), "test.bare"));
        Assert.Null(ErrorPresenter.UserDetail(new NullReferenceException(), "test.nre"));
    }

    [Fact]
    public void UserDetail_passes_through_a_product_authored_cause()
        => Assert.Equal("No such host is known.",
            ErrorPresenter.UserDetail(new Exception("No such host is known."), "test.dns"));

    /// <summary>
    /// The honesty half: a failure with no readable cause must still READ as a failure. A presenter
    /// that returned an empty string here would turn every unexplained failure into silence, and
    /// silence on a results surface reads as success.
    /// </summary>
    [Fact]
    public void Compose_states_the_bare_failure_when_there_is_no_presentable_cause()
    {
        Assert.Equal("Could not create witness snapshot.",
            ErrorPresenter.Compose("Could not create witness snapshot.", null));
        Assert.Equal("Could not create witness snapshot: No such host is known.",
            ErrorPresenter.Compose("Could not create witness snapshot.", "No such host is known."));
    }

    /// <summary>
    /// The Windows Witness testify path composes its message through the presenter
    /// (AppViewModel.cs, WitnessTestifyAsync's catch). A boilerplate exception must produce a
    /// non-empty, non-boilerplate line.
    /// </summary>
    [Fact]
    public void Testify_failure_message_is_stated_and_carries_no_raw_exception_text()
    {
        var composed = ErrorPresenter.Compose(
            "Could not create witness snapshot.",
            ErrorPresenter.UserDetail(
                new Exception("Exception of type 'Syxx.Kit.WitnessSignerException' was thrown."),
                "witness.testify"));

        Assert.False(string.IsNullOrWhiteSpace(composed));
        Assert.DoesNotContain("Exception of type", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("Syxx.Kit.", composed, StringComparison.Ordinal);
        Assert.False(ErrorPresenter.IsBoilerplate(composed));
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyxxUI.Avalonia.ViewModels;
using SyxxUI.Presentation;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Defect-2 regression coverage: raw exception text must never reach a user-visible string.
///
/// <para>The class under test is <c>Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs</c>, LINKED
/// into this Avalonia client (see SyxxUI.Avalonia.csproj) and compiled — byte for byte — into the
/// WinUI client too. Testing it here therefore tests both .NET clients' boundary from a project that
/// actually builds and runs off Windows; the WinUI GUI cannot be compiled on a non-Windows host, so
/// a test that lived only in SyxxUI.Tests would be a test nobody could run here.</para>
///
/// <para>THE RULE BEING PROTECTED. Two failures are possible and only one of them is obvious:
/// (1) crash-reporter material rendered as product copy, and (2) a failure with no readable cause
/// being SUPPRESSED into silence — which is the honesty defect, because a failure that renders as
/// nothing reads as a success. Both are asserted below.</para>
/// </summary>
public class ErrorPresenterTests
{
    // ── IsBoilerplate: the crash-reporter shapes .NET actually emits ─────────────────────────────

    [Theory]
    // Default Exception.Message — leaks the internal type name and says nothing else.
    [InlineData("Exception of type 'Syxx.Kit.WitnessSignerException' was thrown.")]
    // Programming-error messages: real English, zero user value, no action implied.
    [InlineData("Object reference not set to an instance of an object.")]
    [InlineData("Specified cast is not valid.")]
    [InlineData("Index was outside the bounds of the array.")]
    [InlineData("Value cannot be null. (Parameter 'privateKeyBase64')")]
    [InlineData("One or more errors occurred.")]
    [InlineData("Unable to cast object of type 'System.String' to type 'System.Int32'.")]
    [InlineData("The given key 'collector' was not present in the dictionary.")]
    // Namespace-qualified type dump (the .NET analogue of macOS's "Module.Type error N").
    [InlineData("System.Net.Sockets.SocketException (61): Connection refused")]
    [InlineData("Syxx.Kit.WitnessSignerException: seal failed")]
    // HRESULT / memory address.
    [InlineData("Access is denied. (0x80070005)")]
    // Stack-trace fragment.
    [InlineData("boom\n   at SyxxUI.ViewModels.AppViewModel.Save(String key)")]
    [InlineData("failed --- End of stack trace from previous location ---")]
    // The Cocoa template, which .NET hosted on macOS also surfaces.
    [InlineData("The operation couldn't be completed. (SyxxKit.WitnessSignerError error 1.)")]
    [InlineData("The operation couldn’t be completed. (unknown context at $1a2b3c4d)")]
    // Nothing at all is not a cause either.
    [InlineData("")]
    [InlineData("   ")]
    public void IsBoilerplate_rejects_crash_reporter_material(string raw)
        => Assert.True(ErrorPresenter.IsBoilerplate(raw),
            $"'{raw}' is crash-reporter material and must never be shown to a user");

    [Theory]
    // Runtime messages that name a CONCRETE condition. These are the most useful thing we can tell
    // the user, and a presenter that swallowed them would be worse than the defect it fixes.
    [InlineData("No such host is known.")]
    [InlineData("Connection refused")]
    [InlineData("The network is unreachable")]
    [InlineData("Access to the path is denied.")]
    // Product-authored copy from the core.
    [InlineData("No system DNS resolvers detected.")]
    [InlineData("All DNS dispatches failed; no queries reached any resolver.")]
    [InlineData("Local check only; use collector for confirmed result.")]
    public void IsBoilerplate_keeps_genuine_human_readable_causes(string raw)
        => Assert.False(ErrorPresenter.IsBoilerplate(raw),
            $"'{raw}' names a real condition and must still reach the user");

    // ── DisplayReason: the raw-reason-STRING path (Syxx.Kit LeakDetector builds these from ex.Message) ──

    [Fact]
    public void DisplayReason_drops_boilerplate_and_keeps_real_text()
    {
        // Syxx.Kit/LeakDetector.cs:207 composes exactly this shape: "Token request failed: " + ex.Message.
        Assert.Null(ErrorPresenter.DisplayReason(
            "Token request failed: Exception of type 'System.Exception' was thrown."));
        Assert.Null(ErrorPresenter.DisplayReason(null));
        Assert.Null(ErrorPresenter.DisplayReason("   "));
        Assert.Equal("No system DNS resolvers detected.",
            ErrorPresenter.DisplayReason("  No system DNS resolvers detected.  "));
    }

    // ── UserDetail: the caught-Exception path ───────────────────────────────────────────────────

    [Fact]
    public void UserDetail_returns_null_rather_than_inventing_a_cause()
    {
        // A bare Exception carries only the default "Exception of type '…' was thrown." message.
        Assert.Null(ErrorPresenter.UserDetail(new Exception(), "test.bare"));
        Assert.Null(ErrorPresenter.UserDetail(new NullReferenceException(), "test.nre"));
        Assert.Null(ErrorPresenter.UserDetail(
            new InvalidOperationException("Exception of type 'X.Y.ZException' was thrown."), "test.dump"));
    }

    [Fact]
    public void UserDetail_passes_through_a_product_authored_cause()
        => Assert.Equal("No such host is known.",
            ErrorPresenter.UserDetail(new Exception("No such host is known."), "test.dns"));

    [Fact]
    public void UserDetail_unwraps_to_an_inner_cause_when_the_outer_frame_says_nothing()
    {
        // The outer message is boilerplate; the inner one names the real condition. Reaching for it
        // is not invention — it is the cause the runtime already established.
        var wrapped = new InvalidOperationException(
            "Exception of type 'System.InvalidOperationException' was thrown.",
            new Exception("Connection refused"));
        Assert.Equal("Connection refused", ErrorPresenter.UserDetail(wrapped, "test.wrapped"));

        var aggregate = new AggregateException(new Exception("No such host is known."));
        Assert.Equal("No such host is known.", ErrorPresenter.UserDetail(aggregate, "test.aggregate"));
    }

    [Fact]
    public void UserDetail_survives_a_null_exception_without_inventing_anything()
        => Assert.Null(ErrorPresenter.UserDetail(null!, "test.null"));

    // ── Compose: the failure must still READ as a failure when there is no cause ─────────────────

    [Fact]
    public void Compose_states_the_bare_failure_when_there_is_no_presentable_cause()
    {
        Assert.Equal("Signing failed.", ErrorPresenter.Compose("Signing failed.", null));
        Assert.Equal("Signing failed.", ErrorPresenter.Compose("Signing failed.", "  "));
    }

    [Fact]
    public void Compose_appends_a_real_cause_after_a_colon()
        => Assert.Equal("Signing failed: No such host is known.",
            ErrorPresenter.Compose("Signing failed.", "No such host is known."));

    // ── End-to-end through the real view-models (the surfaces a user reads) ──────────────────────

    /// <summary>
    /// The Witness sign path is the one view-model here with an injectable failure seam, so it can
    /// be driven to a real caught exception without a network. The rendered line must (a) state the
    /// failure, (b) carry the honest failed state, and (c) contain no crash-reporter fragment.
    /// </summary>
    [Fact]
    public async Task WitnessViewModel_renders_a_failure_without_leaking_raw_exception_text()
    {
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => throw new Exception(
                "Exception of type 'Syxx.Kit.WitnessSignerException' was thrown."),
            createSnapshot: (_, _, _) => throw new InvalidOperationException("unused"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        // The failure is STATED — an empty line here would read as "nothing went wrong".
        Assert.False(string.IsNullOrWhiteSpace(vm.SignatureLine));
        // …and it is stated WITHOUT the raw exception text.
        Assert.DoesNotContain("Exception of type", vm.SignatureLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Syxx.Kit.", vm.SignatureLine, StringComparison.Ordinal);
        Assert.False(ErrorPresenter.IsBoilerplate(vm.SignatureLine),
            "the rendered line must not itself be crash-reporter material");
    }

    /// <summary>
    /// The SECOND failure branch of the same command — the seal itself throwing (the
    /// <c>createSnapshot</c> seam), not the resolve. It needs its own test: a mutation that restored
    /// <c>$"Signing failed: {ex.Message}"</c> on this branch alone left the resolve-branch test above
    /// green, which is precisely the coverage hole this closes. WitnessSignerException's own default
    /// message is a namespace-qualified type dump.
    /// </summary>
    [Fact]
    public async Task WitnessViewModel_sign_branch_renders_a_failure_without_leaking_raw_exception_text()
    {
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => Task.FromResult(
                new Syxx.Kit.SectionInputs { Comparison = new Syxx.Kit.ComparisonResult() }),
            createSnapshot: (_, _, _) => throw new Syxx.Kit.WitnessSignerException(
                "Exception of type 'Syxx.Kit.WitnessSignerException' was thrown."));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        // The failure is STATED — silence here would read as a seal that worked.
        Assert.False(string.IsNullOrWhiteSpace(vm.SignatureLine));
        Assert.DoesNotContain("Exception of type", vm.SignatureLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Syxx.Kit.", vm.SignatureLine, StringComparison.Ordinal);
        Assert.False(ErrorPresenter.IsBoilerplate(vm.SignatureLine),
            "the rendered line must not itself be crash-reporter material");
        // Nothing was sealed, so no signing identity may be shown.
        Assert.True(string.IsNullOrEmpty(vm.IdentityLine));
    }

    /// <summary>
    /// Same surface, but the exception DOES carry a readable cause: it must survive to the screen.
    /// Without this, a presenter that simply blanked every cause would pass the test above.
    /// </summary>
    [Fact]
    public async Task WitnessViewModel_keeps_a_readable_cause_on_screen()
    {
        var vm = new WitnessViewModel(
            runMirror: (_, _, _) => throw new Exception("No such host is known."),
            createSnapshot: (_, _, _) => throw new InvalidOperationException("unused"));
        vm.Domain = "example.com";

        await vm.SignCommand.ExecuteAsync(null);

        Assert.Equal(WitnessTestimonyState.Failed, vm.TestimonyState);
        Assert.Contains("No such host is known.", vm.SignatureLine, StringComparison.Ordinal);
    }
}

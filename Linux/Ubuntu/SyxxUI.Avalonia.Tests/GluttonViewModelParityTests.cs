using System.Collections.Generic;
using Avalonia.Media;
using Syxx.Kit;
using SyxxUI.Avalonia.ViewModels;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Honesty-gate parity tests for the Linux Glutton view-model — the Aug 2026 catch-up that brought
/// the Linux client to feature parity with macOS' <c>SyxxApp/GluttonView.swift</c> and Windows'
/// <c>SyxxUI/Views/GluttonView.xaml(.cs)</c>. Every test exercises the
/// <see cref="GluttonViewModel.RenderLeakResult"/> gate (marked internal, visible to this assembly
/// via <c>InternalsVisibleTo</c> in <c>SyxxUI.Avalonia.csproj</c>) — the gate is the honesty spine
/// this screen runs on, and rendering a leak/no-leak verdict wrongly is the single defect class
/// (B030) this whole module was written to refuse.
///
/// <para>Each test documents the mutation that turns it red (AGENTS.md Protocol §3): if a one-line
/// change to <c>RenderLeakResult</c> can let green through on an unattested result, the assertion
/// below catches it. The tests are offline (no network, no Avalonia render host), so they run
/// headless on every platform — exactly like <c>ProphetViewModelPresentationTests</c>.</para>
///
/// <para><b>HONESTY SCOPE</b>: these tests pin the verdict MAPPING (right text, right brush). They
/// do NOT run the real collector HTTP/TLS round-trip; <see cref="LeakDetector.RunCollectorTestAsync"/>
/// is exercised by <c>Syxx.Tests.LeakDetectorTests</c> on a Linux DNS-fixture host. Here, every
/// <see cref="LeakTestResult"/> is constructed explicitly so the assertions are reproducible.</para>
/// </summary>
public class GluttonViewModelParityTests
{
    // --- Severity-Color brush constants (the Linux theme's canonical mapping, parroting
    // Presentation/SeverityColor.cs so a drift in EITHER file surfaces here) ---
    private static readonly Color Teal  = Color.Parse("#3FD08A"); // SuccessBrush
    private static readonly Color Amber = Color.Parse("#F2B33D"); // AmberBrush
    private static readonly Color Red   = Color.Parse("#FF5C7A"); // CriticalBrush
    private static readonly Color Info  = Color.Parse("#A79CC4"); // NeutralBrush

    private static Color BrushColor(IBrush b) => ((SolidColorBrush)b).Color;

    // GluttonViewModel's constructor kicks off async license/portal reads. The presentation gate we
    // exercise here is independent of those (the tests feed a complete LeakTestResult), so the
    // background reads simply race — they cannot make any of these assertions flake out.
    private static GluttonViewModel NewVm() => new();

    private static LeakTestResult Result(
        LeakResult r,
        bool? sigValid = null,
        bool? sigTrusted = null,
        CollectorTransportPinning transport = CollectorTransportPinning.NotConfigured,
        string? signature = null,
        List<string>? observed = null,
        string? errorReason = null) => new()
    {
        Result = r,
        SignatureValid = sigValid,
        SignatureKeyTrusted = sigTrusted,
        TransportPinning = transport,
        Signature = signature,
        ObservedResolvers = observed ?? new(),
        ErrorReason = errorReason,
    };

    // =========================================================================
    // D004 — no attestation ⇒ always UNVERIFIED amber, regardless of outer Result.
    // The outer Result field is what a relay can flip; the attestation cannot.
    // =========================================================================

    [Fact]
    public void NoSignature_NotDetected_ReadsUnverifiedNeverGreen()
    {
        var vm = NewVm();
        // A relay returned NotDetected with no signature — the LEAST honest shape possible, exactly
        // what the B030 false-negative class describes. MUTATION: remove the !attested branch in
        // RenderLeakResult so this falls through to the green NotDetected case -> both assertions
        // below fail.
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: null,
            observed: new() { "1.1.1.1" },
            transport: CollectorTransportPinning.Enforced));

        Assert.StartsWith("[UNVERIFIED]", vm.LeakResultText, System.StringComparison.Ordinal);
        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        // Clean (teal) is reserved for ATTESTED NotDetected only — never for absence of evidence.
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
    }

    [Fact]
    public void NoSignature_Confirmed_ReadsUnverifiedNeverRed()
    {
        var vm = NewVm();
        // Symmetric: even a Confirmed-with-no-sig cannot read as a confident red "leak found". It
        // reads UNVERIFIED amber — the same gate, both directions, B030 parity.
        vm.RenderLeakResult(Result(LeakResult.Confirmed, sigValid: null));

        Assert.StartsWith("[UNVERIFIED]", vm.LeakResultText, System.StringComparison.Ordinal);
        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Red,  BrushColor(vm.LeakResultBrush));
    }

    // =========================================================================
    // B066 — crypto-valid signature by an UNTRUSTED key ⇒ amber, never green.
    // =========================================================================

    [Fact]
    public void ValidSig_UntrustedKey_NotDetected_ReadsUnverifiedNeverGreen()
    {
        var vm = NewVm();
        // The collector returned a cryptographically valid signature, but the signer is not in our
        // trust set. MUTATION: change the !attested gate to treat SignatureValid==true alone as
        // attested -> both assertions below fail (the vm would render teal, breaking B066).
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: true,
            sigTrusted: false,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            observed: new() { "1.1.1.1" }));

        Assert.StartsWith("[UNVERIFIED]", vm.LeakResultText, System.StringComparison.Ordinal);
        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
    }

    [Fact]
    public void InvalidSig_NotDetected_ReadsUnverifiedNeverGreen()
    {
        var vm = NewVm();
        // A signature that FAILED verification reads the same as no signature: the attestation is
        // untrustworthy either way. MUTATION: relax the gate to admit sigValid==false as attested
        // -> the Teal-leak assertion would render green for a tampered attestation.
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: false,
            signature: "badbase64",
            observed: new() { "1.1.1.1" }));

        Assert.StartsWith("[UNVERIFIED]", vm.LeakResultText, System.StringComparison.Ordinal);
        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
    }

    // =========================================================================
    // B067 — a NotDetected over a non-enforced transport reads amber, never green.
    // The collector's TLS identity must be verified before its verdict can be stood behind.
    // =========================================================================

    [Fact]
    public void ValidSigTrustedKey_NotDetected_ButUnpinnedTransport_ReadsAmberNeverGreen()
    {
        var vm = NewVm();
        // Everything is in order EXCEPT the transport pin: standard TLS validation is a weaker
        // identity guarantee than an SPKI pin, so the verdict is not clean. MUTATION: drop the
        // transport-enforcement check in the NotDetected arm -> vm renders teal, breaking B067.
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.NotConfigured,
            signature: "base64sig",
            observed: new() { "1.1.1.1" }));

        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
        Assert.Contains("UNVERIFIED TRANSPORT", vm.LeakResultText, System.StringComparison.Ordinal);
    }

    [Fact]
    public void FailedTransport_NotDetected_TakesAmberNeverGreen()
    {
        var vm = NewVm();
        // The most dangerous transport state — a configured-but-non-matching pin — forces the
        // verdict off the green path too. The transport line reads red (the pin DID fail), but the
        // headline verdict stays amber because no leak evidence exists either.
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Failed,
            signature: "base64sig",
            observed: new() { "1.1.1.1" }));

        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
        // The transport sub-line should surface the failed pin in RED — distinct from the verdict.
        Assert.Equal(Red, BrushColor(vm.TransportBrush));
    }

    // =========================================================================
    // ATTESTED CLEAN — the ONE shape that legitimately reads green.
    // =========================================================================

    [Fact]
    public void AttestedNotDetected_EnforcedTransportTrustedKey_ReadsGreen()
    {
        var vm = NewVm();
        // All four gates pass: valid sig, trusted key, enforced transport, and the collector actually
        // observed one or more resolvers (a clean NotDetected states an observation, not the
        // absence of one). MUTATION: change the success brush to Amber/Red -> both assertions fail.
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            observed: new() { "1.1.1.1" }));

        Assert.Equal(Teal, BrushColor(vm.LeakResultBrush));
        Assert.DoesNotContain("[UNVERIFIED]", vm.LeakResultText, System.StringComparison.Ordinal);
        // The signature sub-line marks the signer trusted — green.
        Assert.Equal(Teal, BrushColor(vm.SignatureValidityBrush));
    }

    [Fact]
    public void AttestedConfirmed_EnforcedTransportTrustedKey_ReadsRed()
    {
        var vm = NewVm();
        // A leaking resolver with a valid attestation reads red — the only confident verdict this
        // screen renders in the negative direction.
        vm.RenderLeakResult(Result(LeakResult.Confirmed,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            observed: new() { "1.1.1.1", "8.8.8.8" }));

        Assert.Equal(Red, BrushColor(vm.LeakResultBrush));
        Assert.Contains("observed your DNS queries", vm.LeakResultText, System.StringComparison.Ordinal);
        Assert.Contains("8.8.8.8", vm.LeakResultDetailText, System.StringComparison.Ordinal);
    }

    // =========================================================================
    // B030 / parity macOS line 247 — empty observed list cannot read "no leak".
    // =========================================================================

    [Fact]
    public void Attested_NotDetected_EmptyObserved_ReadsAmberNeverGreen()
    {
        var vm = NewVm();
        // A collector that claimed "no leak" but saw no resolvers is internally inconsistent — the
        // Kit's ReconcileResult usually downgrades this to Inconclusive, but an attacker or a
        // malformed collector could still hand us NotDetected with empty observed. We must refuse
        // to render green: "this is not a clean result — re-run the check."
        vm.RenderLeakResult(Result(LeakResult.NotDetected,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            observed: new()));

        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
        Assert.Contains("not a clean result", vm.LeakResultDetailText, System.StringComparison.Ordinal);
    }

    // =========================================================================
    // Invalid / Error / Inconclusive always read amber, never red or green.
    // =========================================================================

    [Theory]
    [InlineData(LeakResult.Invalid)]
    [InlineData(LeakResult.Error)]
    [InlineData(LeakResult.Inconclusive)]
    public void Attested_ResultClasses_Invalid_Error_Inconclusive_AllReadAmber(LeakResult r)
    {
        var vm = NewVm();
        // Every non-binary verdict is amber — never confident in either direction.
        vm.RenderLeakResult(Result(r,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            errorReason: "Collector reported a transient error."));

        Assert.Equal(Amber, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Teal, BrushColor(vm.LeakResultBrush));
        Assert.NotEqual(Red,  BrushColor(vm.LeakResultBrush));
    }

    // =========================================================================
    // Defect-2 — raw crash-reporter text never reaches the screen, even when the
    // LeakTestResult.ErrorReason field contains it (the Kit can build it from ex.Message).
    // =========================================================================

    [Fact]
    public void ErrorReason_Boilerplate_IsSuppressedByErrorPresenter()
    {
        var vm = NewVm();
        // The EXACT boilerplate the BCL emits as the default message of NullReferenceException —
        // which LeakDetector.cs:207/271 concatenates into ErrorReason. MUTATION: bypass
        // ErrorPresenter.DisplayReason so the raw text reaches ErrorReasonText -> the test fails.
        vm.RenderLeakResult(Result(LeakResult.Inconclusive,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            errorReason: "Object reference not set to an instance of an object."));

        // Boilerplate ⇒ ShowErrorReason false (the standing inconclusive copy is what shows).
        Assert.False(vm.ShowErrorReason);
        Assert.Equal("", vm.ErrorReasonText);
    }

    [Fact]
    public void ErrorReason_GenuineHumanReadableText_IsSurfaced()
    {
        var vm = NewVm();
        // A real sentence — the kind the Kit writes itself ("All DNS dispatches failed; ...") —
        // should reach the screen as the amber reason line, NOT be suppressed as boilerplate.
        vm.RenderLeakResult(Result(LeakResult.Inconclusive,
            sigValid: true,
            sigTrusted: true,
            transport: CollectorTransportPinning.Enforced,
            signature: "base64sig",
            errorReason: "No system DNS resolvers detected."));

        Assert.True(vm.ShowErrorReason);
        Assert.Equal("No system DNS resolvers detected.", vm.ErrorReasonText);
    }
}
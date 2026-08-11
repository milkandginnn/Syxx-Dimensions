using System;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// Phase 2.2 — every Windows navigation entry point (checkout, trial, update, broadcast,
    /// remote-content, Dimensions, Welcome) must authorize through
    /// <see cref="RemoteUrlPolicy.AuthorizeForNavigation"/>. Refusal never yields a Uri.
    ///
    /// These Kit-level tests run under <c>dotnet test</c> on macOS. WinUI XAML host tests
    /// cannot compile here; the pure gate is the load-bearing contract the UI callers share.
    ///
    /// Mutation requirement: if AuthorizeForNavigation is bypassed (always returns a Uri from
    /// bare TryCreate), <see cref="DisallowedOrigin_AuthorizeReturnsNull_NeverUri"/> and
    /// <see cref="BypassSimulation_BareUriCreate_WouldAdmitAttackerOrigin"/> turn red relative
    /// to the gate contract — the second documents the anti-pattern the UI must not use.
    /// </summary>
    public class RemoteNavigationGateTests
    {
        // ── Allowed origin ──────────────────────────────────────────────────

        [Theory]
        [InlineData("https://syxx.app/")]
        [InlineData("https://syxx.app/api/checkout?sku=syxx_win_monthly_sub")]
        [InlineData("https://winmotd.syxx.app/")]
        [InlineData("https://milkandgin.com/")]
        [InlineData("https://download.syxx.app/appcast.xml")]
        public void AllowedOrigin_AuthorizeReturnsUri(string url)
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(url, out var eval);

            Assert.NotNull(uri);
            Assert.True(eval.IsAllowed);
            Assert.Equal(RemoteUrlPolicy.Decision.Allowed, eval.Decision);
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        }

        // ── Null / malformed ──────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a url")]
        [InlineData("https://")]
        public void NullOrMalformed_AuthorizeReturnsNull(string? url)
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(url, out var eval);

            Assert.Null(uri);
            Assert.False(eval.IsAllowed);
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedMalformed, eval.Decision);
            Assert.NotNull(eval.Reason);
        }

        [Fact]
        public void RelativePath_AuthorizeReturnsNull_NeverNavigable()
        {
            // On some runtimes Uri.TryCreate("/relative", Absolute) yields file:// — still refused
            // (scheme), never Allowed. The contract is fail-closed, not a specific Decision enum.
            var uri = RemoteUrlPolicy.AuthorizeForNavigation("/relative", out var eval);

            Assert.Null(uri);
            Assert.False(eval.IsAllowed);
            Assert.NotEqual(RemoteUrlPolicy.Decision.Allowed, eval.Decision);
        }

        // ── Disallowed origin / scheme ──────────────────────────────────────

        [Theory]
        [InlineData("https://evil.example.com/")]
        [InlineData("https://evilsyxx.app/")]
        [InlineData("https://syxx.app.evil.com/")]
        [InlineData("http://syxx.app/")]
        [InlineData("file:///etc/passwd")]
        [InlineData("smb://attacker/share")]
        [InlineData("javascript:alert(1)")]
        [InlineData("https://user:pass@syxx.app/")]
        [InlineData("https://syxx.app:8443/")]
        public void DisallowedOrigin_AuthorizeReturnsNull_NeverUri(string url)
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(url, out var eval);

            Assert.Null(uri);
            Assert.False(eval.IsAllowed);
            Assert.NotEqual(RemoteUrlPolicy.Decision.Allowed, eval.Decision);
            Assert.False(string.IsNullOrWhiteSpace(eval.Reason));
        }

        // ── Policy-unavailable / empty allowlist ────────────────────────────

        [Fact]
        public void EmptyAllowlist_RefusesEvenProductOrigin()
        {
            // "Policy unavailable" twin: an empty allowlist must fail closed, not open the world.
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(
                "https://syxx.app/",
                out var eval,
                allowedDomains: Array.Empty<string>());

            Assert.Null(uri);
            Assert.False(eval.IsAllowed);
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedHost, eval.Decision);
        }

        [Fact]
        public void NullAllowlistArgument_UsesDefaultProductOrigins()
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(
                "https://syxx.app/",
                out var eval,
                allowedDomains: null);

            Assert.NotNull(uri);
            Assert.True(eval.IsAllowed);
        }

        // ── Checkout / trial shape (DimensionBaseURL override attack) ───────

        [Fact]
        public void HostileDimensionOverride_CheckoutUrl_Refused()
        {
            // LicenseView builds CheckoutUrl from SyxxConfig.DimensionBaseURL, which is
            // overridable via SYXX_DIMENSION_URL. Without AuthorizeForNavigation a hostile
            // override would open an attacker origin. This is the contract that path depends on.
            const string hostileCheckout = "https://evil.example.com/api/checkout?sku=syxx_win_monthly_sub";

            var uri = RemoteUrlPolicy.AuthorizeForNavigation(hostileCheckout, out var eval);

            Assert.Null(uri);
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedHost, eval.Decision);
        }

        [Fact]
        public void ProductCheckoutUrl_Allowed()
        {
            const string checkout = "https://syxx.app/api/checkout?sku=syxx_win_monthly_sub";
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(checkout, out var eval);

            Assert.NotNull(uri);
            Assert.True(eval.IsAllowed);
            Assert.Contains("/api/checkout", uri!.AbsoluteUri, StringComparison.Ordinal);
        }

        // ── Update / broadcast / remote-content shapes ──────────────────────

        [Fact]
        public void UpdateDownloadOnForeignHost_Refused()
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(
                "https://malware.example/Syxx-setup.exe", out var eval);

            Assert.Null(uri);
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedHost, eval.Decision);
        }

        [Fact]
        public void BroadcastLearnMoreOnProductHost_Allowed()
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(
                "https://syxx.app/blog/incident-2026", out _);

            Assert.NotNull(uri);
        }

        [Fact]
        public void RemoteContentInitial_HttpMotd_Refused()
        {
            // MOTD must be https. Plaintext winmotd must never become WebView.Source.
            var uri = RemoteUrlPolicy.AuthorizeForNavigation("http://winmotd.syxx.app/", out var eval);

            Assert.Null(uri);
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedScheme, eval.Decision);
        }

        // ── Mutation documentation: bare Uri.TryCreate is the anti-pattern ──

        [Fact]
        public void BypassSimulation_BareUriCreate_WouldAdmitAttackerOrigin()
        {
            // This is the mutation the plan requires: if a caller bypasses AuthorizeForNavigation
            // and uses bare Uri.TryCreate + LaunchUriAsync, attacker origins become navigable.
            // The assertion BELOW proves that the anti-pattern admits evil://... wait, https evil.
            // The GATE test above proves the correct path refuses. Together they pin the contract:
            // UI code must use the gate; bare create is insufficient.
            const string attacker = "https://evil.example.com/phish";

            bool bareWouldAdmit = Uri.TryCreate(attacker, UriKind.Absolute, out var bareUri)
                                  && bareUri is not null
                                  && bareUri.Scheme == Uri.UriSchemeHttps;
            Assert.True(bareWouldAdmit,
                "Sanity: bare TryCreate admits the attacker URL — that is why the gate exists.");

            var gated = RemoteUrlPolicy.AuthorizeForNavigation(attacker);
            Assert.Null(gated);
        }

        /// <summary>
        /// Simulates the UI decision function every open path must use:
        /// authorize → navigate only when non-null. Mutation: remove the null check
        /// (always navigate after bare create) and this turns red.
        /// </summary>
        [Theory]
        [InlineData("https://syxx.app/whats-new", true)]
        [InlineData("https://evil.example.com/", false)]
        [InlineData("http://syxx.app/", false)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("file:///tmp/x", false)]
        public void UiNavigationDecision_NavigateOnlyWhenAuthorized(string? url, bool expectNavigate)
        {
            bool wouldNavigate = RemoteUrlPolicy.AuthorizeForNavigation(url) is not null;
            Assert.Equal(expectNavigate, wouldNavigate);
        }
    }
}

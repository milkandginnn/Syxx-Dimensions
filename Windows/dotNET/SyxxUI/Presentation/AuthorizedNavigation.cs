using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Syxx.Kit;
using Windows.System;

namespace SyxxUI.Presentation;

/// <summary>
/// The single outbound-navigation gate for XAML-declared links.
///
/// WHY THIS EXISTS: <c>HyperlinkButton.NavigateUri</c> is honoured by the framework itself.
/// The shell opens the URL before any application code runs, so a link declared that way can
/// never be refused by <see cref="RemoteUrlPolicy"/> — the gate is not merely bypassed, it is
/// never consulted. Three call sites did exactly that (DashboardView.xaml:224 and :253,
/// SettingsView.xaml:67) while every other navigation path on Windows already failed closed
/// through <see cref="RemoteUrlPolicy.AuthorizeForNavigation(string, out RemoteUrlPolicy.Evaluation)"/>:
/// MainWindow.xaml.cs:198 and :243, WelcomeView.xaml.cs:18, LicenseView.xaml.cs:132 and :222,
/// RemoteContentPanel.xaml.cs:123 and :221, DimensionService.cs:200.
///
/// A fail-closed contract with holes in it is not a contract, so the declarative form is
/// replaced by an explicit Click handler that authorizes first and launches only on an
/// allowed Uri.
///
/// HONESTY: a refusal is shown to the user as a refusal. It never silently no-ops, because a
/// dead link that reports nothing reads as "the app is broken" rather than "that destination
/// was not authorized", and it never falls back to launching the unauthorized Uri.
///
/// Copy note: the refusal dialog reuses the already-shipped strings from
/// LicenseView.xaml.cs:229-230 verbatim. No new user-visible wording is introduced here.
/// </summary>
internal static class AuthorizedNavigation
{
    /// <summary>Dialog title for a policy refusal. Verbatim from LicenseView.xaml.cs:229.</summary>
    internal const string RefusedTitle = "Link Blocked";

    /// <summary>Fallback body when the evaluation carried no reason. Verbatim from LicenseView.xaml.cs:230.</summary>
    internal const string RefusedFallbackBody = "This link is not authorized to be opened.";

    /// <summary>
    /// Authorize <paramref name="url"/> through <see cref="RemoteUrlPolicy"/> and launch it only
    /// if the policy allows it. Returns true when the shell was actually asked to open the Uri.
    /// </summary>
    /// <param name="url">The absolute URL the link declares.</param>
    /// <param name="xamlRoot">Root used to host the refusal dialog. When null, the refusal is
    /// logged and no navigation happens — refusal still wins over navigation.</param>
    internal static async Task<bool> OpenAsync(string url, XamlRoot? xamlRoot)
    {
        var uri = RemoteUrlPolicy.AuthorizeForNavigation(url, out var eval);
        if (uri is null)
        {
            await ShowRefusalAsync(eval, xamlRoot);
            return false;
        }

        try
        {
            return await Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            // An unhandled exception inside an async void Click handler terminates the process
            // (the E147 pattern already guarded elsewhere in this project).
            System.Diagnostics.Debug.WriteLine($"[Syxx] Authorized link launch failed: {ex}");
            return false;
        }
    }

    private static async Task ShowRefusalAsync(RemoteUrlPolicy.Evaluation eval, XamlRoot? xamlRoot)
    {
        if (xamlRoot is null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Syxx] Link refused by RemoteUrlPolicy and no XamlRoot to report it: {eval.Reason}");
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                Title = RefusedTitle,
                Content = eval.Reason ?? RefusedFallbackBody,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot,
            };
            _ = await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Syxx] Link refusal dialog failed: {ex}");
        }
    }
}

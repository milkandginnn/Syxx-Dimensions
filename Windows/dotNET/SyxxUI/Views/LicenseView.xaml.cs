using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SyxxUI.ViewModels;
using Syxx.Kit;
using Windows.System;

namespace SyxxUI.Views;

/// <summary>
/// License and registration surface — Windows twin of macOS LicenseView.swift.
///
/// The status line is the REAL entitlement state from LicenseManager.IsFeatureUnlockedAsync()
/// (validates the stored key with the licensing service, then falls back to trial/locked). It is
/// never a hardcoded "Pro" or fabricated activation count. Activation itself completes on
/// syxx.app, matching macOS which opens the site rather than doing local activation.
/// </summary>
public sealed partial class LicenseView : Page
{
    private bool _isMonthly = true;

    private static string BaseUrl => SyxxConfig.DimensionBaseURL.TrimEnd('/');
    private string CheckoutUrl => $"{BaseUrl}/api/checkout?sku={(_isMonthly ? "syxx_win_monthly_sub" : "syxx_win_yearly_sub")}";
    private static string SyxxUrl => BaseUrl;
    private AppViewModel? _vm;

    public LicenseView()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null && !string.IsNullOrEmpty(_vm.LicenseKey)) KeyBox.Text = _vm.LicenseKey;
        try
        {
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] License status refresh failed: {ex}");
        }
    }

    private async Task<bool> RefreshStatusAsync()
    {
        var (unlocked, message) = await LicenseManager.IsFeatureUnlockedAsync();
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.Resources[
            unlocked ? "IoSuccessGreenBrush" : "IoWarningAmberBrush"];
        if (_vm is not null) _vm.IsPro = unlocked;
        return unlocked;
    }

    /// In-app activation (macOS parity — the old handler just opened the website). The message
    /// is set ONLY from the real LicenseManager result: a network failure is not an invalid key
    /// (D043), and a verified key that could not be saved never reads as an unqualified success (D042).
    private async void ActivateBtn_Click(object sender, RoutedEventArgs e)
    {
        var key = KeyBox.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            ActivateStatusText.Text = "Enter a license key first.";
            ActivateStatusText.Foreground = (Brush)Application.Current.Resources["IoWarningAmberBrush"];
            return;
        }
        if (_vm is not null) _vm.LicenseKey = key;

        ActivateBtn.IsEnabled = false;
        ActivateProgress.IsActive = true;
        ActivateProgress.Visibility = Visibility.Visible;
        ActivateStatusText.Text = "Verifying with the licensing service…";
        ActivateStatusText.Foreground = (Brush)Application.Current.Resources["IoTextSecondaryBrush"];

        try
        {
            var result = await LicenseManager.ValidateLicenseDetailedAsync(key, SyxxConfig.AccountID);
            string message;
            string brushKey;
            switch (result)
            {
                case LicenseValidationResult.Valid:
                    bool saved = true;
                    try { LicenseManager.SaveLicenseKey(key); }
                    catch { saved = false; }
                    message = saved
                        ? "License activated. Pro features are unlocked on this machine."
                        : "License verified and Pro is unlocked for this session, but the key could not be saved on this device. You will need to enter it again next launch.";
                    brushKey = saved ? "IoSuccessGreenBrush" : "IoWarningAmberBrush";
                    break;
                case LicenseValidationResult.Invalid:
                    message = "This license key is invalid, expired, or locked.";
                    brushKey = "IoCriticalRedBrush";
                    break;
                default: // Timeout / NetworkError
                    message = "Could not reach the licensing service, so your key was not checked. This says nothing about whether the key is valid. Check your connection and try again.";
                    brushKey = "IoWarningAmberBrush";
                    break;
            }
            ActivateStatusText.Text = message;
            ActivateStatusText.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] License activation failed: {ex}");
            ActivateStatusText.Text = $"Activation could not complete: {SyxxUI.Presentation.ErrorPresenter.DisplayReason(ex.Message) ?? ex.GetType().Name}";
            ActivateStatusText.Foreground = (Brush)Application.Current.Resources["IoCriticalRedBrush"];
        }
        finally
        {
            ActivateProgress.IsActive = false;
            ActivateProgress.Visibility = Visibility.Collapsed;
            ActivateBtn.IsEnabled = true;
        }
        var unlocked = await RefreshStatusAsync();
        if (_vm is not null) _vm.IsPro = unlocked;
    }

    private async void TrialBtn_Click(object sender, RoutedEventArgs e)
    {
        _isMonthly = PeriodCombo.SelectedIndex == 0;
        PriceText.Text = _isMonthly ? "$3.95 / month" : "$19.95 / year";
        SaveText.Visibility = _isMonthly ? Visibility.Collapsed : Visibility.Visible;
        // Phase 2.2: checkout/trial must pass RemoteUrlPolicy. DimensionBaseURL is env-overridable
        // (SYXX_DIMENSION_URL); without the gate a hostile override would open an attacker origin.
        var checkoutUri = RemoteUrlPolicy.AuthorizeForNavigation(CheckoutUrl, out var eval);
        if (checkoutUri is null)
        {
            // Fail closed. Reuse the existing Link Blocked surface (no new user-visible copy).
            try
            {
                var msgDialog = new ContentDialog
                {
                    Title = "Link Blocked",
                    Content = eval.Reason ?? "This link is not authorized to be opened.",
                    CloseButtonText = "OK",
                    XamlRoot = XamlRoot
                };
                _ = await msgDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Syxx] Checkout policy refusal dialog failed: {ex}");
            }
            return;
        }
        _ = await Launcher.LaunchUriAsync(checkoutUri);
    }

    // MARK: - System panels (About / EULA), matching the "Syxx System Panels" mockup.

    private async void AboutBtn_Click(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 6, MinWidth = 360 };
        body.Children.Add(new Image
        {
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/syxx-logo.png")),
            Width = 64, Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        });
        void AddLine(string text, double size, bool bold = false, bool secondary = false)
        {
            body.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = (FontFamily)Application.Current.Resources["IoFontGrotesk"],
                FontSize = size,
                FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = (Brush)Application.Current.Resources[secondary ? "IoTextSecondaryBrush" : "IoTextBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        AddLine("Syxx", 22, bold: true);
        AddLine("“Lyin’ Eyes”", 13, secondary: true);
        AddLine($"Version {SyxxConfig.Version}", 12, secondary: true);
        AddLine("DNS Investigation Suite", 12, secondary: true);
        AddLine("© Joel “J.J.” Warne 2026. All Rights Reserved.", 11, secondary: true);
        AddLine("MilkandGin", 12);
        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // Phase 2.2: do not set NavigateUri (bypasses policy). Click handlers authorize first.
        links.Children.Add(MakeAuthorizedLink("syxx.app", SyxxUrl));
        links.Children.Add(MakeAuthorizedLink("milkandgin.com", "https://milkandgin.com"));
        body.Children.Add(links);

        try
        {
            var dialog = new ContentDialog
            {
                Title = "About Syxx",
                Content = body,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] About dialog failed: {ex}");
        }
    }

    /// Builds a hyperlink that opens only after <see cref="RemoteUrlPolicy.AuthorizeForNavigation"/>.
    /// HyperlinkButton.NavigateUri would bypass the gate, so clicks are handled explicitly.
    private HyperlinkButton MakeAuthorizedLink(string label, string url)
    {
        var btn = new HyperlinkButton { Content = label };
        btn.Click += async (_, _) =>
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation(url, out var eval);
            if (uri is null)
            {
                try
                {
                    var msgDialog = new ContentDialog
                    {
                        Title = "Link Blocked",
                        Content = eval.Reason ?? "This link is not authorized to be opened.",
                        CloseButtonText = "OK",
                        XamlRoot = XamlRoot
                    };
                    _ = await msgDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] About-link policy refusal dialog failed: {ex}");
                }
                return;
            }
            _ = await Launcher.LaunchUriAsync(uri);
        };
        return btn;
    }

    private async void EulaBtn_Click(object sender, RoutedEventArgs e)
    {
        var agreement = BundledAgreement.Load();
        var text = new TextBlock
        {
            Text = agreement?.Text ?? FirstRunViewModel.StateUnknownMessage,
            FontFamily = (FontFamily)Application.Current.Resources["IoFontUI"],
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["IoTextBrush"],
            TextWrapping = TextWrapping.Wrap,
        };
        try
        {
            var dialog = new ContentDialog
            {
                Title = "End-User License Agreement",
                Content = new ScrollViewer { Content = text, MaxHeight = 420, MaxWidth = 560 },
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] EULA dialog failed: {ex}");
        }
    }
}

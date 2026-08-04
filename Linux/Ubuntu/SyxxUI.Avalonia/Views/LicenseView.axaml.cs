using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Syxx.Kit;

namespace SyxxUI.Avalonia.Views;

/// <summary>
/// License and registration surface — Ubuntu/Avalonia twin of the Windows and macOS LicenseView.
///
/// The status line is the REAL entitlement state from LicenseManager.IsFeatureUnlockedAsync()
/// (validates the stored key with the licensing service, then falls back to trial/locked). It is
/// never a hardcoded "Pro". Activation runs in-app: the key is validated with the licensing service
/// and then persisted through the cross-platform ISecretStore (LinuxSecretStore on Linux — OS keyring
/// with an AES-256-GCM encrypted-file fallback), so it survives a relaunch. No DPAPI on Linux.
/// </summary>
public partial class LicenseView : UserControl
{
    private bool _isMonthly = true;

    private string CheckoutUrl =>
        $"{SyxxConfig.DimensionBaseURL.TrimEnd('/')}/api/checkout?sku={(_isMonthly ? "syxx_deb_monthly_sub" : "syxx_deb_yearly_sub")}";

    public LicenseView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Refresh the live entitlement line when the view is shown, matching the Windows twin's
    // OnNavigatedTo → RefreshStatusAsync. async void is confined to the framework event handler;
    // the body is wrapped so an exception cannot tear down the process (E147 parity).
    private async void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Syxx] License status refresh failed: {ex}");
        }
    }

    private async Task<bool> RefreshStatusAsync()
    {
        var (unlocked, message) = await LicenseManager.IsFeatureUnlockedAsync();
        if (StatusText != null)
        {
            StatusText.Text = message;
            StatusText.Foreground = Brush(unlocked ? "SyxSuccessGreenBrush" : "SyxOrangeBrush");
        }
        return unlocked;
    }

    /// In-app activation (Windows/macOS parity). The message is set ONLY from the real
    /// LicenseManager result: a network failure is not an invalid key (D043), and a verified key
    /// that could not be saved never reads as an unqualified success (D042). Because the Linux
    /// file store is best-effort (silent on failure), success is confirmed by reading the key back.
    private async void ActivateBtn_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var key = KeyBox?.Text?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            ActivateStatusText.Text = "Enter a license key first.";
            ActivateStatusText.Foreground = Brush("SyxOrangeBrush");
            return;
        }

        ActivateBtn.IsEnabled = false;
        ActivateStatusText.Text = "Verifying with the licensing service…";
        ActivateStatusText.Foreground = Brush("SyxTextSecondaryBrush");

        try
        {
            var result = await LicenseManager.ValidateLicenseDetailedAsync(key, SyxxConfig.AccountID);
            string message;
            string brushKey;
            switch (result)
            {
                case LicenseValidationResult.Valid:
                    bool saved;
                    try
                    {
                        LicenseManager.SaveLicenseKey(key);
                        // The Linux secret store is best-effort and swallows persistence failures,
                        // so a try/catch alone cannot detect a silent write failure. Read the key
                        // back through the store to confirm it actually persisted (D042).
                        saved = LicenseManager.GetStoredLicenseKey() == key;
                    }
                    catch
                    {
                        saved = false;
                    }
                    message = saved
                        ? "License activated. Pro features are unlocked on this machine."
                        : "License verified and Pro is unlocked for this session, but the key could not be saved on this device. You will need to enter it again next launch.";
                    brushKey = saved ? "SyxSuccessGreenBrush" : "SyxOrangeBrush";
                    break;
                case LicenseValidationResult.Invalid:
                    message = "This license key is invalid, expired, or locked.";
                    brushKey = "SyxRedBrush";
                    break;
                default: // Timeout / NetworkError
                    message = "Could not reach the licensing service, so your key was not checked. This says nothing about whether the key is valid. Check your connection and try again.";
                    brushKey = "SyxOrangeBrush";
                    break;
            }
            ActivateStatusText.Text = message;
            ActivateStatusText.Foreground = Brush(brushKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Syxx] License activation failed: {ex}");
            ActivateStatusText.Text = $"Activation could not complete: {ex.GetType().Name}";
            ActivateStatusText.Foreground = Brush("SyxRedBrush");
        }
        finally
        {
            ActivateBtn.IsEnabled = true;
        }

        try
        {
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Syxx] License status refresh failed: {ex}");
        }
    }

    // Resolves a StaticResource brush by key. global:: guards against the SyxxUI.Avalonia namespace
    // shadowing the real Avalonia.Media namespace inside this file.
    private global::Avalonia.Media.IBrush? Brush(string resourceKey)
        => this.TryFindResource(resourceKey, out var res) ? res as global::Avalonia.Media.IBrush : null;

    // global:: is required. Inside namespace SyxxUI.Avalonia.Views, a bare "Avalonia."
    // binds to SyxxUI.Avalonia first, so Avalonia.Interactivity resolves to the
    // nonexistent SyxxUI.Avalonia.Interactivity and the project fails to compile.
    private void TrialBtn_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _isMonthly = PeriodCombo?.SelectedIndex == 0;
        PriceText.Text = _isMonthly ? "$3.95 / month" : "$19.95 / year";
        if (SaveText != null)
            SaveText.IsVisible = !_isMonthly;
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = CheckoutUrl,
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);
    }
}

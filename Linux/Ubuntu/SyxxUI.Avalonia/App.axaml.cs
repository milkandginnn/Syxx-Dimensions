using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Syxx.Kit;
using SyxxUI.Avalonia.Services;
using SyxxUI.Avalonia.ViewModels;
using SyxxUI.Avalonia.Views;

namespace SyxxUI.Avalonia;

public partial class App : Application
{
    private readonly ReadyApplicationGate _readyGate = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Back WitnessKeyStore (and LicenseManager) with the platform secret store so the witness
        // signing key persists across launches. On Linux this is LinuxSecretStore (libsecret keyring
        // with an AES-256-GCM encrypted-file fallback) — never DPAPI/crypt32, which is Windows-only.
        // Must run before any key operation; the Witness view loads-or-creates its key on first sign.
        PlatformSecretStore.Initialise();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = CreateFirstRunViewModel();
            if (viewModel.ReadySettings is { } readySettings)
            {
                desktop.MainWindow = OpenReadyApplication(
                    viewModel.State,
                    readySettings);
            }
            else
            {
                var onboarding = new OnboardingWindow(viewModel);
                viewModel.Ready += settings =>
                    TransitionToReadyApplication(desktop, onboarding, viewModel, settings);
                desktop.MainWindow = onboarding;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static FirstRunViewModel CreateFirstRunViewModel() =>
        BundledAgreement.Load() switch
        {
            BundledAgreementLoadResult.Loaded loaded =>
                new FirstRunViewModel(
                    new FirstRunCoordinator(
                        loaded.Agreement.Document,
                        SyxxSettingsStore.DefaultPath),
                    loaded.Agreement.Text),
            _ => FirstRunViewModel.AgreementUnavailable(),
        };

    private MainWindow? OpenReadyApplication(
        OnboardingState state,
        SyxxSettings settings) =>
        _readyGate.TryOpen(
            state,
            settings,
            savedSettings =>
            {
                ThemeManager.ApplyFromSettings(savedSettings);
                LinuxReadyServices.Start(savedSettings);
                return new MainWindow();
            });

    private void TransitionToReadyApplication(
        IClassicDesktopStyleApplicationLifetime desktop,
        OnboardingWindow onboarding,
        FirstRunViewModel viewModel,
        SyxxSettings settings)
    {
        var main = OpenReadyApplication(viewModel.State, settings);
        if (main is null)
        {
            return;
        }

        desktop.MainWindow = main;
        main.Show();
        onboarding.Close();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Syxx.Kit;
using SyxxUI.Services;
using SyxxUI.ViewModels;

namespace SyxxUI.Views;

public sealed partial class FirstRunView : UserControl
{
    private FirstRunViewModel? _model;
    private bool _readyRaised;

    public FirstRunView()
    {
        InitializeComponent();
    }

    public event EventHandler? Ready;

    public void Initialize(FirstRunViewModel model)
    {
        _model = model;
        AgreementText.Text = model.AgreementText;
        AcceptanceCheck.IsChecked = model.HasAcceptedTerms;
        ResolverCombo.ItemsSource = EncryptedDNSConfig.Catalog;
        ResolverCombo.SelectedItem =
            EncryptedDNSConfig.Resolver(model.ResolverId)
            ?? EncryptedDNSConfig.Catalog.First();
        TransportCombo.SelectedIndex =
            model.Transport == EncryptedDNSTransport.Dot ? 1 : 0;
        EncryptDnsToggle.IsOn = model.EncryptedDnsEnabled;
        NotificationsToggle.IsOn = model.NotificationsEnabled;
        UpdateCheckToggle.IsOn = model.UpdateCheckEnabled;
        CrashReportingToggle.IsOn = model.CrashReportingEnabled;
        TelemetryToggle.IsOn = model.TelemetryEnabled;
        RenderState();
        RenderThemeSelection();
    }

    private async void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }
        _model.HasAcceptedTerms = AcceptanceCheck.IsChecked == true;
        var advanced = _model.AttemptAcceptAgreement();
        if (_model.ShowAcceptanceAlert)
        {
            var dialog = new ContentDialog
            {
                Title = "End-User License Agreement",
                Content = _model.PendingAlertMessage,
                CloseButtonText = "OK",
                XamlRoot = Root.XamlRoot,
            };
            await dialog.ShowAsync();
            _model.DismissAcceptanceAlert();
        }
        if (advanced)
        {
            RenderState();
        }
        else if (_model.State is OnboardingState.PersistenceFailure or OnboardingState.StateUnknown)
        {
            RenderState();
        }
    }

    private void DeclineButton_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Exit();

    private void StoryModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }
        _model.ExperienceMode = "story";
        RenderExperienceSelection();
    }

    private void GeneralModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }
        _model.ExperienceMode = "general";
        RenderExperienceSelection();
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        _ = _model?.CompleteSetup(useDefaults: true);
        RenderState();
    }

    private void BeginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (ResolverCombo.SelectedItem is EncryptedResolver resolver)
        {
            _model.ResolverId = resolver.Id;
        }
        _model.Transport = TransportCombo.SelectedIndex == 1
            ? EncryptedDNSTransport.Dot
            : EncryptedDNSTransport.Doh;
        _model.EncryptedDnsEnabled = EncryptDnsToggle.IsOn;
        _model.NotificationsEnabled = NotificationsToggle.IsOn;
        _model.UpdateCheckEnabled = UpdateCheckToggle.IsOn;
        _model.CrashReportingEnabled = CrashReportingToggle.IsOn;
        _model.TelemetryEnabled = TelemetryToggle.IsOn;

        _ = _model.CompleteSetup(useDefaults: false);
        RenderState();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _model?.Retry();
        RenderState();
    }

    private void QuitButton_Click(object sender, RoutedEventArgs e) =>
        Application.Current.Exit();

    private void RenderState()
    {
        var state = _model?.State ?? OnboardingState.StateUnknown;
        LoadingIndicator.Visibility =
            state == OnboardingState.Loading ? Visibility.Visible : Visibility.Collapsed;
        AgreementPanel.Visibility =
            state == OnboardingState.AgreementRequired ? Visibility.Visible : Visibility.Collapsed;
        SetupPanel.Visibility =
            state == OnboardingState.SetupRequired ? Visibility.Visible : Visibility.Collapsed;
        FailurePanel.Visibility =
            state is OnboardingState.PersistenceFailure or OnboardingState.StateUnknown
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (state == OnboardingState.SetupRequired)
        {
            RenderExperienceSelection();
            RenderThemeSelection();
        }
        if (FailurePanel.Visibility == Visibility.Visible)
        {
            FailureText.Text = _model?.FailureMessage
                ?? (state == OnboardingState.PersistenceFailure
                    ? FirstRunViewModel.SetupSaveFailureMessage
                    : FirstRunViewModel.StateUnknownMessage);
        }
        if (state == OnboardingState.Ready && !_readyRaised)
        {
            _readyRaised = true;
            Ready?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RenderExperienceSelection()
    {
        if (_model is null)
        {
            return;
        }

        var storySelected = _model.ExperienceMode == "story";
        StorySelectedIcon.Visibility = storySelected ? Visibility.Visible : Visibility.Collapsed;
        GeneralSelectedIcon.Visibility = storySelected ? Visibility.Collapsed : Visibility.Visible;
        StoryModeButton.BorderBrush = ResourceBrush(
            storySelected ? "IoBlueBrush" : "IoBorderSubtleBrush");
        GeneralModeButton.BorderBrush = ResourceBrush(
            storySelected ? "IoBorderSubtleBrush" : "IoSuccessGreenBrush");
        StoryModeButton.BorderThickness = new Thickness(storySelected ? 2 : 1);
        GeneralModeButton.BorderThickness = new Thickness(storySelected ? 1 : 2);
    }

    private static Brush ResourceBrush(string key) =>
        (Brush)Application.Current.Resources[key];

    private void ThemeSystemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        _model.ThemeMode = "system";
        RenderThemeSelection();
        ApplyThemeLive("system");
    }

    private void ThemeDarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        _model.ThemeMode = "dark";
        RenderThemeSelection();
        ApplyThemeLive("dark");
    }

    private void ThemeLightButton_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        _model.ThemeMode = "light";
        RenderThemeSelection();
        ApplyThemeLive("light");
    }

    private void RenderThemeSelection()
    {
        if (_model is null) return;

        var mode = _model.ThemeMode.ToLowerInvariant();
        ThemeSystemSelectedIcon.Visibility = mode == "system" ? Visibility.Visible : Visibility.Collapsed;
        ThemeDarkSelectedIcon.Visibility = mode == "dark" ? Visibility.Visible : Visibility.Collapsed;
        ThemeLightSelectedIcon.Visibility = mode == "light" ? Visibility.Visible : Visibility.Collapsed;

        ThemeSystemButton.BorderBrush = ResourceBrush(mode == "system" ? "IoBlueBrush" : "IoBorderSubtleBrush");
        ThemeDarkButton.BorderBrush = ResourceBrush(mode == "dark" ? "IoBlueBrush" : "IoBorderSubtleBrush");
        ThemeLightButton.BorderBrush = ResourceBrush(mode == "light" ? "IoBlueBrush" : "IoBorderSubtleBrush");
        ThemeSystemButton.BorderThickness = new Thickness(mode == "system" ? 2 : 1);
        ThemeDarkButton.BorderThickness = new Thickness(mode == "dark" ? 2 : 1);
        ThemeLightButton.BorderThickness = new Thickness(mode == "light" ? 2 : 1);
    }

    private static void ApplyThemeLive(string mode)
    {
        if (Application.Current is App app)
        {
            app.ThemeManager?.ApplyTheme(mode);
        }
    }
}

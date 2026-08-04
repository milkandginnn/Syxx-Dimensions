using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class OnboardingWindow : Window
{
    private FirstRunViewModel ViewModel =>
        (FirstRunViewModel)(DataContext
            ?? throw new InvalidOperationException("First-run view model is unavailable."));

    public OnboardingWindow()
        : this(FirstRunViewModel.AgreementUnavailable())
    {
    }

    public OnboardingWindow(FirstRunViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void AgreeContinue_Click(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.AttemptAcceptAgreement() && ViewModel.ShowAcceptanceAlert)
        {
            _ = new AcceptanceAlertWindow().ShowDialog(this);
        }
    }

    private void AgreementCheckbox_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox)
        {
            ViewModel.HasAcceptedTerms = checkbox.IsChecked == true;
        }
    }

    private void Decline_Click(object? sender, RoutedEventArgs e) => Close();

    private void StoryMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            ViewModel.ExperienceMode = "story";
        }
    }

    private void GeneralMode_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            ViewModel.ExperienceMode = "general";
        }
    }

    private void Doh_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            ViewModel.UseDot = false;
        }
    }

    private void Dot_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            ViewModel.UseDot = true;
        }
    }

    private void SkipSetup_Click(object? sender, RoutedEventArgs e) =>
        ViewModel.CompleteSetup(useDefaults: true);

    private void BeginSetup_Click(object? sender, RoutedEventArgs e) =>
        ViewModel.CompleteSetup(useDefaults: false);

    private void Retry_Click(object? sender, RoutedEventArgs e) =>
        ViewModel.Retry();

    private void Quit_Click(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public sealed class AcceptanceAlertWindow : Window
{
    public AcceptanceAlertWindow()
    {
        Title = "End-User License Agreement";
        Width = 500;
        Height = 210;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#170c2e"));

        var message = new TextBlock
        {
            Text = FirstRunViewModel.AcceptanceRequiredMessage,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(
                "avares://Syxx/Assets/Fonts/Jost-Regular.ttf#Jost"),
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.Parse("#efeafe")),
        };
        var ok = new Button
        {
            Content = "OK",
            MinWidth = 88,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(28),
            Spacing = 24,
            Children = { message, ok },
        };
    }
}

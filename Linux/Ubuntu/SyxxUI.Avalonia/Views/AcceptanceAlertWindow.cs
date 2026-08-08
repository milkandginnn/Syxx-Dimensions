using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public sealed class AcceptanceAlertWindow : Window
{
    public AcceptanceAlertWindow()
    {
        Title = "End-User License Agreement";
        // D343: use MinWidth/MinHeight with SizeToContent instead of hardcoded dimensions so the
        // window scales with the message text length and display DPI. The content drives the size;
        // the min prevents a too-narrow dialog on short messages.
        MinWidth = 420;
        MinHeight = 180;
        MaxWidth = 680;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Theme-aware: use DynamicResource-style brushes from the application resource dictionary
        // instead of hardcoded dark hex values, so this window respects on theme switch.
        var surfaceBrush = Application.Current?.Resources.TryGetResource("SyxSurfaceDarkBrush", null, out var sb) == true
            ? sb as IBrush : new ImmutableSolidColorBrush(Color.Parse("#170c2e"));
        var textBrush = Application.Current?.Resources.TryGetResource("SyxTextBrush", null, out var tb) == true
            ? tb as IBrush : new ImmutableSolidColorBrush(Color.Parse("#efeafe"));
        Background = surfaceBrush;

        var message = new TextBlock
        {
            Text = FirstRunViewModel.AcceptanceRequiredMessage,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(
                "avares://Syxx/Assets/Fonts/Jost-Regular.ttf#Jost"),
            FontSize = 15,
            Foreground = textBrush,
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

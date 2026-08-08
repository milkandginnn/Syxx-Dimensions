using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class WitnessView : UserControl
{
    public WitnessView()
    {
        InitializeComponent();
        DataContext = new WitnessViewModel();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "And the Witness testified: when local and remote records matched, when they held steady over time, and when no leak betrayed the encrypted path, the DNS was healthy. When they did not, the network, and its eyes, were lying indeed.",
        });
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

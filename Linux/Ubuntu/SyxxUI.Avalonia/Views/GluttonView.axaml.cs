using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class GluttonView : UserControl
{
    public GluttonView()
    {
        InitializeComponent();
        DataContext = new GluttonViewModel();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "Alas, the Glutton hungered for encrypted packets and demanded that any leak in the sealed DNS traffic be found and named, for a single crack was enough to render him famished.",
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

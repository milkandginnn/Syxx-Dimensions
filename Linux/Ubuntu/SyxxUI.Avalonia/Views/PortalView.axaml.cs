using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class PortalView : UserControl
{
    public PortalView()
    {
        InitializeComponent();
        DataContext = new PortalViewModel();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "A Portal carried the traveler to a distant land, tracing the path of a hidden interceptor that had reached into the stream.",
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

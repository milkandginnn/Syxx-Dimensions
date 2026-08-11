using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;

namespace SyxxUI.Avalonia.Views;

public partial class CageView : UserControl
{
    public CageView()
    {
        InitializeComponent();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "The Cage trapped the DNS queries so they could be inspected without escape, their headers and answers laid bare for judgment.",
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

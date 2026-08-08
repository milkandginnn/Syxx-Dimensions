using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class ConclusionsView : UserControl
{
    public ConclusionsView()
    {
        InitializeComponent();
        DataContext = new ConclusionsViewModel();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "Unified synthesis of network evidence across all six wardens.",
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

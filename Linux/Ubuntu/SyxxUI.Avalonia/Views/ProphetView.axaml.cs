using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class ProphetView : UserControl
{
    public ProphetView()
    {
        InitializeComponent();
        DataContext = new ProphetViewModel();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "Thus the voyage began: The Prophet names each domain aright, so no false guise may be borne into view.",
        });
        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxTypeBody"] ?? FontFamily.Default,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextSecondaryBrush"] ?? Brushes.Gray,
            Text = "Prophet compares twelve resolver answers to queries sent for the domain you identify. A match only indicates consistency and does not mean the answers are valid. Resolver answers can be spoofed. Prophet then checks for DNSSEC on the domain, which does communicate trustworthiness.",
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

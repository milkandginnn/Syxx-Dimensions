using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.Controls;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class MirrorView : UserControl
{
    public MirrorView()
    {
        InitializeComponent();
        DataContext = new MirrorViewModel();

        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxLoreFont"] ?? FontFamily.Default,
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextMutedBrush"] ?? Brushes.Gray,
            Text = "Grasped fast in the Prophet's hand, the Mirror did answer, and shewed the records born locally in accord with those discerned afar, proving that the true names shifted not their visages at each summoning.",
        });
        Header.AddContent(new TextBlock
        {
            FontFamily = (FontFamily?)App.Current?.Resources["SyxTypeBody"] ?? FontFamily.Default,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (IBrush?)App.Current?.Resources["SyxTextSecondaryBrush"] ?? Brushes.Gray,
            Text = "Mirror pulls the static web contents as seen from your device, as well as the static web contents as seen from the remote server, and then compares their hashes. A match of the static web content hashes pulled from your device and the remote server generally means that the content you're being served is what the world is being served.",
        });
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

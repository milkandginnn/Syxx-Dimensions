using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class MirrorView : UserControl
{
    public MirrorView()
    {
        InitializeComponent();
        DataContext = new MirrorViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

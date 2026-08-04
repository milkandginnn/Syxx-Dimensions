using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class GluttonView : UserControl
{
    public GluttonView()
    {
        InitializeComponent();
        DataContext = new GluttonViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

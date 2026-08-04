using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class PortalView : UserControl
{
    public PortalView()
    {
        InitializeComponent();
        DataContext = new PortalViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

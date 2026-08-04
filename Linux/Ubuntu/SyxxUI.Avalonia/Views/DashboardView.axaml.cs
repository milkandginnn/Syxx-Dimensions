using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        DataContext = new DashboardViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

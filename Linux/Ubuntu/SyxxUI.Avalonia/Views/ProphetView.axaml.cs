using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class ProphetView : UserControl
{
    public ProphetView()
    {
        InitializeComponent();
        DataContext = new ProphetViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

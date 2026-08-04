using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class WitnessView : UserControl
{
    public WitnessView()
    {
        InitializeComponent();
        DataContext = new WitnessViewModel();
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

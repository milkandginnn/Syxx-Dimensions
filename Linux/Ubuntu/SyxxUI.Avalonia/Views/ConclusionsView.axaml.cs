using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class ConclusionsView : UserControl
{
    public ConclusionsView()
    {
        InitializeComponent();
        DataContext = new ConclusionsViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

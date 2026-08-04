using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SyxxUI.Avalonia.Views;

public partial class CageView : UserControl
{
    public CageView() => InitializeComponent();
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

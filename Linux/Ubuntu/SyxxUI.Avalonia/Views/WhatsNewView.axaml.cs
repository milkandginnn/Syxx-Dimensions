using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SyxxUI.Avalonia.ViewModels;

namespace SyxxUI.Avalonia.Views;

public partial class WhatsNewView : UserControl
{
    private readonly WhatsNewViewModel _vm = new();

    public WhatsNewView()
    {
        InitializeComponent();
        DataContext = _vm;
        // Fetch on attach rather than in the constructor: a network call in a ctor is how the
        // Windows client once crashed before its window ever appeared.
        AttachedToVisualTree += async (_, _) => await _vm.LoadAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

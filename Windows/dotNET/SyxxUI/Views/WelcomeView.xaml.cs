using Microsoft.UI.Xaml.Controls;
using Syxx.Kit;
using SyxxUI.ViewModels;
using Windows.System;

namespace SyxxUI.Views;

public sealed partial class WelcomeView : Page
{
    public WelcomeView()
    {
        InitializeComponent();
    }

    private async void OpenWebsite_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // Phase 2.2: fail closed through RemoteUrlPolicy; never LaunchUri without authorization.
        var uri = RemoteUrlPolicy.AuthorizeForNavigation("https://syxx.app");
        if (uri is not null)
            await Launcher.LaunchUriAsync(uri);
    }
}

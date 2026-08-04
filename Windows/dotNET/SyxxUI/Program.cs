using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace SyxxUI;

/// Explicit process entry point.
///
/// The XAML-generated Main calls <see cref="Application.Start"/> directly without
/// initializing the Windows App SDK runtime. For a packaged app that is fine (the
/// package provides identity and WinRT activation). For an UNPACKAGED loose .exe it
/// is fatal: nothing loads the runtime, so the first WinUI type activation fails with
/// 0x80073D54 APPMODEL_ERROR_NO_PACKAGE ("the process has no package identity") and the
/// app crashes in Microsoft.UI.Xaml.dll before any window appears.
///
/// We suppress the generated Main (DISABLE_XAML_GENERATED_MAIN) and initialize the
/// bootstrapper here first. It loads the installed Windows App Runtime and enables
/// registration-free WinRT activation, then we start the app.
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 0x00020002 == Windows App SDK major.minor 2.2 (matches the PackageReference).
        Bootstrap.Initialize(0x00020002);
        try
        {
            // The callback parameter must not be named "_": inside the body,
            // "_ = new App()" would then assign to it (type mismatch, CS0029)
            // instead of acting as a discard.
            Application.Start(callbackParams =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        finally
        {
            Bootstrap.Shutdown();
        }
    }
}

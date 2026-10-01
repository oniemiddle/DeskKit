using Avalonia;
using Avalonia.Controls;
using DeskKit.App.Diagnostics;

namespace DeskKit.App;

internal static class Program
{
    // Avalonia configuration; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static int Main(string[] args)
    {
        if (DesktopLayerSelfTest.IsRequested(args))
        {
            DesktopLayerSelfTest.Requested = DesktopLayerSelfTest.FromArgs(args);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args, ShutdownMode.OnExplicitShutdown);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }
}

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
        // Headless, and before Avalonia is set up, because its whole job is to be
        // killed without warning while it is writing. It refuses rather than falling
        // through, so a launch this mode cannot honour never becomes a normal run
        // against the real database.
        if (DesktopLayerSelfTest.IsWriteLoopRequested(args))
            return DesktopLayerSelfTest.RunWriteLoop(args);

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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DeskKit.App.Diagnostics;

namespace DeskKit.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The self test drives the same lifetime with its own sessions, so it takes
            // the place of one rather than starting beside it.
            if (DesktopLayerSelfTest.Requested is { } selfTest)
                selfTest.Begin(desktop);
            else
                AppSession.Start(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

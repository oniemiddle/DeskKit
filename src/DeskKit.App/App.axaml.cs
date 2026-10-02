using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DeskKit.App.Diagnostics;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;
using DeskKit.Platform;
using DeskKit.Platform.Windows;
using DeskKit.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeskKit.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private WidgetShell? _shell;

    /// <summary>Held for the lifetime of the process, so one instance owns the database.</summary>
    private SingleInstanceGuard? _instanceGuard;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (DesktopLayerSelfTest.Requested is { } selfTest)
            {
                selfTest.Begin(desktop);
            }
            else
            {
                StartShell(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Closing the settings window must not end the process; the widgets and
        // the tray icon stay alive until the user picks "退出".
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _services = BuildServices();

        _instanceGuard = SingleInstanceGuard.Acquire();
        if (_instanceGuard.AlreadyRunning)
        {
            // Before a single widget is created: a second instance would build its
            // own set and then overwrite the first one's configuration.
            _services.GetRequiredService<ILogger<App>>()
                .LogInformation("DeskKit is already running; this instance will exit");

            _instanceGuard.Dispose();
            _instanceGuard = null;
            _services.Dispose();
            _services = null;

            // Not shut down directly: this runs inside framework initialisation,
            // and the lifetime is still starting. The first turn of the dispatcher
            // is the earliest safe point.
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        if (!_instanceGuard.IsProtecting)
        {
            // The guard is best-effort: losing it must not stop DeskKit from
            // starting, but it must not pass unmentioned either, because it is the
            // difference between one instance owning the config and two racing.
            _services.GetRequiredService<ILogger<App>>().LogWarning(
                "Could not create the single-instance guard, so a second DeskKit could "
                + "overwrite this one's configuration");
        }

        _shell = _services.GetRequiredService<WidgetShell>();

        desktop.Exit += (_, _) =>
        {
            _shell?.Dispose();
            _services?.Dispose();
            _instanceGuard?.Dispose();
        };

        try
        {
            _shell.Start();
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<App>>()
                .LogCritical(ex, "DeskKit failed to start");
            throw;
        }

        if (_shell.StoredDataIsNewer)
        {
            // Nothing was read and nothing may be written, so carrying on would show an
            // empty desktop and keep none of what the user did with it. The notice says
            // so, and the process waits for it to be read rather than vanishing behind it.
            var wait = new DispatcherTimer { Interval = NoticeWindow.Duration + TimeSpan.FromSeconds(1) };

            wait.Tick += (_, _) =>
            {
                wait.Stop();
                desktop.Shutdown();
            };

            wait.Start();
        }
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(AppPaths.LogDirectory));
        });

        services.AddSingleton<StateStore>();
        services.AddSingleton<TickService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<INoticePresenter, NoticePresenter>();

        // One instance for the whole process: it drives both the shell's and the
        // widgets' resource managers, and two of them would fight over the culture.
        services.AddSingleton<LanguageService>();

        services.AddSingleton<IDesktopLayerService>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsDesktopLayerService()
                : new NullDesktopLayerService());

        services.AddSingleton<IWindowMaterialService>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsWindowMaterialService()
                : new NullWindowMaterialService());

        services.AddSingleton<IAutoStartService>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsAutoStartService()
                : new NullAutoStartService());

        services.AddSingleton<IShellIconLoader>(_ =>
            OperatingSystem.IsWindows()
                ? new WindowsShellIconLoader()
                : new NullShellIconLoader());

        services.AddSingleton(provider =>
        {
            var registry = new WidgetRegistry();
            var iconLoader = provider.GetRequiredService<IShellIconLoader>();

            foreach (var widgetProvider in BuiltInWidgets.CreateProviders(iconLoader))
                registry.Register(widgetProvider);

            return registry;
        });

        services.AddSingleton<WidgetShell>();

        return services.BuildServiceProvider();
    }
}

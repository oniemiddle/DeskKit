using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DeskKit.App.Diagnostics;
using DeskKit.App.Services;
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
        _shell = _services.GetRequiredService<WidgetShell>();

        desktop.Exit += (_, _) =>
        {
            _shell?.Dispose();
            _services?.Dispose();
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
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(AppPaths.LogDirectory));
        });

        services.AddSingleton<ConfigStore>();
        services.AddSingleton<TickService>();
        services.AddSingleton<ThemeService>();

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

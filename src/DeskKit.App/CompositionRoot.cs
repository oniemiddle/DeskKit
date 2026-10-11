using DeskKit.App.Services;
using DeskKit.App.Shell;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;
using DeskKit.Persistence;
using DeskKit.Platform;
using DeskKit.Runtime;
using DeskKit.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DeskKit.App;

/// <summary>
/// The application's object graph: everything a session runs on, and the seams the
/// product hangs its own UI off.
/// </summary>
/// <remarks>
/// It is a wiring table rather than a lifecycle, which is why it is not part of
/// <see cref="App"/>: the container it returns owns everything it built, so disposing it
/// is the whole teardown and no caller has to keep a list.
/// </remarks>
internal static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);

            // Named so the file for a day is still the one a person looking for it
            // would guess — the date is appended where the dot would go — and shared so a
            // second process can read it while this one has it open, which is the whole
            // point of a log on a desktop tool that runs for days.
            builder.AddSerilog(
                new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        Path.Combine(AppPaths.LogDirectory, "deskkit-.log"),
                        rollingInterval: RollingInterval.Day,
                        shared: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} "
                                        + "[{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                    .CreateLogger(),
                dispose: true);
        });

        services.AddSingleton<IStateStore, StateStore>();
        services.AddSingleton<IWidgetMessageBus, WidgetMessageBus>();
        services.AddSingleton<TickService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<INoticePresenter, NoticePresenter>();
        services.AddSingleton<ShellAssets>();
        services.AddSingleton<WidgetCatalog>();

        // One instance for the whole process: it drives both the shell's and the
        // widgets' resource managers, and two of them would fight over the culture.
        services.AddSingleton<LanguageService>();

        services.AddDeskKitPlatform();

        // The shell only sees the contract. Built-ins and future plugins both add
        // providers through DI, and the registry checks duplicate IDs at startup.
        services.AddBuiltInWidgets();
        services.AddSingleton(provider =>
            new WidgetRegistry(provider.GetServices<IWidgetProvider>()));

        // The runtime is handed the product's data rather than reaching for it: its icon,
        // the one way it may change the UI language, and which widgets a first run gets.
        services.AddSingleton(provider => new ShellEnvironment(
            provider.GetRequiredService<ShellAssets>().Icon,
            provider.GetRequiredService<LanguageService>().Apply));

        services.AddSingleton(provider => new WidgetShell(
            provider.GetRequiredService<IStateStore>(),
            provider.GetRequiredService<WidgetRegistry>(),
            provider.GetRequiredService<IDesktopLayerService>(),
            provider.GetRequiredService<TickService>(),
            provider.GetRequiredService<ThemeService>(),
            provider.GetRequiredService<IWindowMaterialService>(),
            provider.GetRequiredService<ShellEnvironment>(),
            provider.GetRequiredService<IWidgetMessageBus>(),
            DefaultLayout.WidgetIds,
            provider.GetRequiredService<ILogger<WidgetShell>>()));

        // The shell is the one facade the product's own UI sees, so it is asked for by
        // role rather than by type.
        services.AddSingleton<IShellFacade>(provider => provider.GetRequiredService<WidgetShell>());

        // The product's own UI. Every one of these is a container singleton, so the
        // container owns their dependencies and disposes them with itself.
        services.AddSingleton<SettingsWindowController>();
        services.AddSingleton<TrayIconController>();
        services.AddSingleton<WidgetContextMenuFactory>();
        services.AddSingleton<DesktopGestureController>();
        services.AddSingleton<AutoStartController>();
        services.AddSingleton<StorageNoticePresenter>();

        return services.BuildServiceProvider();
    }
}

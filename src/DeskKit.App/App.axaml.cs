using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DeskKit.App.Diagnostics;
using DeskKit.App.Shell;
using DeskKit.App.Services;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Services;
using DeskKit.Persistence;
using DeskKit.Platform;
using DeskKit.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using DeskKit.Runtime;

namespace DeskKit.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private WidgetShell? _shell;
    private TrayIconController? _tray;
    private SettingsWindowController? _settings;
    private WidgetContextMenuFactory? _menu;

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

        // The product's own UI is built here, not inside the shell: the runtime knows
        // nothing about a tray icon, a settings window or a notice, and it is the
        // composition root that decides what the application hangs off it.
        // The settings window first: the tray's menu opens it, so the tray is built with
        // something to open.
        _settings = new SettingsWindowController(
            _shell,
            _services.GetRequiredService<ShellAssets>(),
            _services.GetRequiredService<LanguageService>(),
            _services.GetRequiredService<WidgetCatalog>());

        _tray = new TrayIconController(
            _shell,
            _services.GetRequiredService<WidgetCatalog>(),
            _services.GetRequiredService<ShellAssets>(),
            _services.GetRequiredService<LanguageService>(),
            () => _settings.Open(null));

        _menu = new WidgetContextMenuFactory();

        // The menu belongs to a widget's window, which the runtime owns, so the runtime
        // says a widget appeared and the product hangs its menu on it.
        _shell.WidgetAdded += (_, runtime) => runtime.Window.SetContextMenu(
            _menu.Create(
                openSettings: () => _settings.Open(runtime.ViewModel),
                remove: () => _shell.RemoveWidget(runtime.ViewModel)));

        // A widget asking for its own settings never reaches the runtime's contract: the
        // runtime forwards the request and the product answers it.
        _shell.SettingsRequested += (_, widget) => _settings.Open(widget);

        desktop.Exit += (_, _) =>
        {
            _tray?.Dispose();
            _shell?.Dispose();
            _services?.Dispose();
            _instanceGuard?.Dispose();
        };

        try
        {
            _shell.Start();

            // Nothing is shown for a first run or a migration: the state is what it is
            // by the time Start returns.
            ReconcileAutoStart();

            _tray.Show();

            // After the tray, so the same warning also lands on the icon, which unlike a
            // notice neither expires nor can be covered.
            new StorageNoticePresenter(
                _services.GetRequiredService<INoticePresenter>(), _tray)
                .Show(_shell.LoadReport);
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

    /// <summary>
    /// Brings the stored start-with-Windows preference back in line with this machine.
    /// </summary>
    /// <remarks>
    /// Autostart is registered per user <em>per machine</em>, but the preference travels
    /// with the profile, so a stored "on" is routinely wrong on a second computer: the
    /// settings window would show it as on while no registry entry exists. The machine is
    /// treated as the truth, because writing a run key at startup because a file said so
    /// is a side effect nobody asked for on that machine.
    /// <para>
    /// It is product policy, so it happens here rather than inside the runtime. All it
    /// does is correct a stored preference, which is why it can run once the shell has
    /// started instead of in the middle of the startup sequence.
    /// </para>
    /// </remarks>
    private void ReconcileAutoStart()
    {
        if (_shell is not { } shell || _services is null)
            return;

        var services = _services!;
        var controller = new AutoStartController(
            services.GetRequiredService<IAutoStartService>(),
            services.GetRequiredService<ILogger<App>>());

        if (controller.Reconcile(shell.State.Settings) is { } corrected)
            shell.ApplySettings(corrected);
    }

    private static ServiceProvider BuildServices()
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

        return services.BuildServiceProvider();
    }
}

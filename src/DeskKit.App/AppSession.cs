using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DeskKit.App.Shell;
using DeskKit.App.Views;
using DeskKit.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeskKit.App;

/// <summary>
/// One run of the product: it takes the single instance, hangs the product's own windows,
/// menu, tray icon and notice off the shell, and brings them up in order.
/// </summary>
/// <remarks>
/// It exists so that <see cref="App"/> only has to decide which flow to run. The container
/// built by <see cref="CompositionRoot"/> owns the shell and every controller it built, so
/// disposing the session is the whole teardown; the guard is the one thing the session
/// holds itself. The lifetime is what keeps the session alive, through the exit event that
/// also releases it.
/// </remarks>
internal sealed class AppSession : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly SingleInstanceGuard _guard;
    private readonly WidgetShell _shell;
    private readonly SettingsWindowController _settings;
    private readonly DesktopGestureController _gestures;
    private readonly TrayIconController _tray;
    private readonly WidgetContextMenuFactory _menu;
    private readonly StorageNoticePresenter _notices;
    private readonly AutoStartController _autoStart;

    private AppSession(ServiceProvider services, SingleInstanceGuard guard)
    {
        _services = services;
        _guard = guard;
        _shell = services.GetRequiredService<WidgetShell>();
        _settings = services.GetRequiredService<SettingsWindowController>();
        _gestures = services.GetRequiredService<DesktopGestureController>();
        _tray = services.GetRequiredService<TrayIconController>();
        _menu = services.GetRequiredService<WidgetContextMenuFactory>();
        _notices = services.GetRequiredService<StorageNoticePresenter>();
        _autoStart = services.GetRequiredService<AutoStartController>();
    }

    /// <summary>
    /// Runs a session on the given lifetime, or hands the process back to the instance that
    /// is already running.
    /// </summary>
    public static void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ArgumentNullException.ThrowIfNull(desktop);

        // Closing the settings window must not end the process; the widgets and the tray
        // icon stay alive until the user picks "退出".
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = CompositionRoot.Build();
        var guard = SingleInstanceGuard.Acquire();

        if (guard.AlreadyRunning)
        {
            // Before a single widget is created: a second instance would build its own set
            // and then overwrite the first one's configuration.
            services.GetRequiredService<ILogger<AppSession>>()
                .LogInformation("DeskKit is already running; this instance will exit");

            guard.Dispose();
            services.Dispose();

            // Not shut down directly: this runs inside framework initialisation, and the
            // lifetime is still starting. The first turn of the dispatcher is the earliest
            // safe point.
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
            return;
        }

        if (!guard.IsProtecting)
        {
            // The guard is best-effort: losing it must not stop DeskKit from starting, but
            // it must not pass unmentioned either, because it is the difference between one
            // instance owning the config and two racing.
            services.GetRequiredService<ILogger<AppSession>>().LogWarning(
                "Could not create the single-instance guard, so a second DeskKit could "
                + "overwrite this one's configuration");
        }

        var session = new AppSession(services, guard);

        // Subscribed before the first window exists, so a start that fails halfway still
        // releases the container and the guard.
        desktop.Exit += (_, _) => session.Dispose();

        session.Wire();
        session.Run(desktop);
    }

    public void Dispose()
    {
        _services.Dispose();
        _guard.Dispose();
    }

    /// <summary>
    /// Puts the product's own UI on the shell: the menu belongs to a widget's window, which
    /// the runtime owns, so the runtime says a widget appeared and the product hangs its
    /// menu on it; and a widget asking for its own settings is forwarded by the runtime to
    /// the product, which is the only one that has a settings window.
    /// </summary>
    private void Wire()
    {
        _shell.WidgetAdded += (_, runtime) => runtime.Window.SetContextMenu(
            _menu.Create(
                openSettings: () => _settings.Open(runtime.ViewModel),
                remove: () => _shell.RemoveWidget(runtime.ViewModel)));

        _shell.SettingsRequested += (_, widget) => _settings.Open(widget);
    }

    /// <summary>
    /// Brings the session up in order, and waits out the one case that has to end it.
    /// </summary>
    /// <remarks>
    /// The order is the content of this method. Nothing is shown for a first run or a
    /// migration, because the state is what it is by the time <c>Start</c> returns, and the
    /// desktop gesture follows that state, so it can only be answered after it. The notice
    /// comes after the tray so that the same warning also lands on the icon, which unlike a
    /// notice neither expires nor can be covered.
    /// </remarks>
    private void Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _shell.Start();
            _gestures.Start();
            _autoStart.Reconcile(_shell);
            _tray.Show();
            _notices.Show(_shell.LoadReport);
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<AppSession>>()
                .LogCritical(ex, "DeskKit failed to start");
            throw;
        }

        if (_shell.StoredDataIsNewer)
            ShutDownWhenTheNoticeHasBeenRead(desktop);
    }

    /// <summary>
    /// Ends the process once the newer-schema notice has had its time on screen.
    /// </summary>
    /// <remarks>
    /// Nothing was read and nothing may be written, so carrying on would show an empty
    /// desktop and keep none of what the user did with it. The notice says so, and the
    /// process waits for it to be read rather than vanishing behind it.
    /// </remarks>
    private static void ShutDownWhenTheNoticeHasBeenRead(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var wait = new DispatcherTimer
        {
            Interval = NoticeWindow.Duration + TimeSpan.FromSeconds(1),
        };

        wait.Tick += (_, _) =>
        {
            wait.Stop();
            desktop.Shutdown();
        };

        wait.Start();
    }
}

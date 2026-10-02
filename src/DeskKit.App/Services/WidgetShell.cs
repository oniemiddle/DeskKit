using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DeskKit.App.Localization;
using DeskKit.App.Shell;
using DeskKit.App.ViewModels;
using DeskKit.App.Views;
using DeskKit.Core;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Platform;
using Microsoft.Extensions.Logging;

namespace DeskKit.App.Services;

/// <summary>
/// Owns every widget: creating and destroying windows, keeping placements in
/// sync with the database, driving the shared tick, and providing the tray
/// menu and settings window.
/// </summary>
public sealed class WidgetShell : IWidgetHost, IShellFacade, IDisposable
{
    private readonly WorkspaceState _workspace;
    private readonly PlacementController _placement;
    private readonly WidgetRuntimeHost _runtimes;
    private readonly WidgetRegistry _registry;
    private readonly WidgetCatalog _catalog;
    private readonly ShellAssets _assets;
    private readonly IDesktopLayerService _desktopLayer;
    private readonly IAutoStartService _autoStart;
    private readonly AppearanceController _appearance;
    private readonly ILogger<WidgetShell> _logger;
    private readonly IWidgetMessageBus _messages;

    /// <summary>The material every widget window carries, already resolved.</summary>
    private readonly WidgetMaterial _material;

    /// <summary>Transparent inset each window keeps around its card for that material.</summary>
    private readonly double _surfaceMargin;

    private readonly TrayIconController _tray;
    private readonly SettingsWindowController _settings;
    private readonly WidgetContextMenuFactory _contextMenus = new();
    private readonly StorageNoticePresenter _storageNotices;
    private readonly ShellStartup _startup;

    public WidgetShell(
        IStateStore stateStore,
        WidgetRegistry registry,
        IDesktopLayerService desktopLayer,
        IAutoStartService autoStart,
        TickService tickService,
        ThemeService themeService,
        IWindowMaterialService materials,
        LanguageService languageService,
        INoticePresenter notices,
        IWidgetMessageBus messages,
        ILogger<WidgetShell> logger)
        : this(
            new WorkspaceState(stateStore, logger),
            registry,
            new ShellAssets(),
            desktopLayer,
            autoStart,
            tickService,
            themeService,
            materials,
            languageService,
            notices,
            messages,
            logger)
    {
    }

    internal WidgetShell(
        WorkspaceState workspace,
        WidgetRegistry registry,
        ShellAssets assets,
        IDesktopLayerService desktopLayer,
        IAutoStartService autoStart,
        TickService tickService,
        ThemeService themeService,
        IWindowMaterialService materials,
        LanguageService languageService,
        INoticePresenter notices,
        IWidgetMessageBus messages,
        ILogger<WidgetShell> logger)
    {
        _workspace = workspace;
        _placement = new PlacementController(workspace);
        _registry = registry;
        _catalog = new WidgetCatalog(registry);
        _assets = assets;
        _desktopLayer = desktopLayer;
        _autoStart = autoStart;
        _appearance = new AppearanceController(themeService, _material);
        Language = languageService;
        _messages = messages;
        _logger = logger;

        _workspace.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        // Resolved once, at construction, because the answer changes the layout of
        // every window and every snap measurement. It also has to be known before
        // the first window is created, so it cannot wait for the config to load.
        _material = materials.Resolve(materials.Default);
        _surfaceMargin = WidgetWindow.MarginFor(_material);

        _runtimes = new WidgetRuntimeHost(
            registry,
            _placement,
            tickService,
            workspace,
            desktopLayer,
            _surfaceMargin,
            new WidgetSurfaceFactory(desktopLayer, materials, _material, _surfaceMargin, assets),
            logger);

        _startup = new ShellStartup(registry, logger);
        _settings = new SettingsWindowController(this, assets);
        _tray = new TrayIconController(this, _catalog, assets, () => _settings.Open(null));
        _storageNotices = new StorageNoticePresenter(notices, _tray);
    }

    /// <summary>The language preference and the managers it drives.</summary>
    public LanguageService Language { get; }

    internal IReadOnlyList<WidgetRuntime> Runtimes => _runtimes.Runtimes;

    public AppState State => _workspace.State;

    /// <summary>Raised after persisted state changes so open UI can refresh.</summary>
    public event EventHandler? StateChanged;

    public IReadOnlyList<WidgetInfo> Widgets =>
    [
        .. _runtimes.Runtimes
            .Select(w => new WidgetInfo(
                w.Placement.InstanceId,
                w.Placement.WidgetId,

                // Resolved now rather than carried from the descriptor, which holds
                // a resource key. The settings window re-reads this list whenever the
                // culture changes, so the names follow the language.
                _catalog.Name(w.Placement.WidgetId),
                w.ViewModel))
    ];

    public IReadOnlyList<IWidgetProvider> AvailableWidgets => _registry.Providers;

    // ---- IWidgetHost -----------------------------------------------------

    public IReadOnlyList<ScreenBounds> Screens => ScreenProbe.GetScreens();

    public IWidgetMessageBus Messages => _messages;

    public void ShowSettings(WidgetViewModel widget)
    {
        _settings.Open(widget);
    }

    public void RemoveWidget(WidgetViewModel widget)
    {
        if (FindRuntime(widget) is { } runtime)
            Remove(runtime);
    }

    public void RequestSave() => _workspace.ScheduleSave();

    /// <summary>
    /// True when the stored data belongs to a newer build. Nothing was read and
    /// nothing may be written, so a session that carried on would show an empty
    /// desktop and quietly keep none of it.
    /// </summary>
    public bool StoredDataIsNewer => _workspace.LoadReport.Outcome == StoreOutcome.NewerSchema;

    // ---- Lifecycle -------------------------------------------------------

    public void Start()
    {
        _workspace.Load();

        _startup.ReportLoad(_workspace.LoadReport, _workspace.DatabasePath);

        _appearance.ApplyTheme(State.Settings.Theme);

        // Before any widget is created, so the first window it builds is already
        // titled in the right language.
        Language.Apply(State.Settings.Language);

        ReconcileAutoStart();

        // A migration reached the disk now rather than whenever something else happens
        // to change: one that only landed on exit would run again on every start until
        // then.
        if (_startup.MigrateWidgetSettings(State.Widgets) is { } migrated)
            _workspace.ReplaceWidgets(migrated);

        if (!_workspace.HasStoredState)
            SeedDefaultWidgets();

        // The stored placements are deliberately not normalised into State: a display
        // layout that cannot show a widget is this session's problem to solve, not a
        // reason to overwrite the position the user chose. Doing it here is what used
        // to make a layout degrade a little every time a laptop was undocked, and on
        // a roaming profile it made two machines overwrite each other's. CreateWidget
        // places the window where it can be seen instead.
        foreach (var placement in State.Widgets.Where(p => p.Enabled))
            CreateWidget(placement);

        _runtimes.StartTicking();
        _tray.Show();

        _storageNotices.Show(_workspace.LoadReport);
    }

    /// <summary>
    /// Brings the stored autostart preference back in line with the machine it is
    /// being read on.
    /// <para>
    /// Autostart is registered per user <em>per machine</em>, but the preference
    /// travels with the profile. A config that says "starts with Windows" is
    /// therefore routinely wrong on a second computer, where the settings window
    /// would show it as on while no registry entry exists. The registry is treated
    /// as the truth here rather than the file, because the opposite reconciliation
    /// —writing a run key at startup because a file said so —is a side effect
    /// nobody asked for on that machine.
    /// </para>
    /// </summary>
    private void ReconcileAutoStart()
    {
        // The rule itself is pure and tested; this only applies its answer and decides
        // whether anything has to be written.
        if (!AutoStartReconciliation.NeedsCorrection(
                State.Settings.StartWithWindows,
                _autoStart.IsSupported,
                _autoStart.IsEnabled))
        {
            return;
        }

        _workspace.ReplaceSettings(State.Settings with
        {
            StartWithWindows = AutoStartReconciliation.Reconcile(
                State.Settings.StartWithWindows,
                _autoStart.IsSupported,
                _autoStart.IsEnabled),
        });
    }

    /// <summary>
    /// Puts one clock on the desktop the very first time the application runs,
    /// so a new user sees something rather than an empty desktop and a tray
    /// icon. It is not restored after the user removes it.
    /// </summary>
    private void SeedDefaultWidgets()
    {
        var placements = WidgetSeedPolicy.CreateFirstRunPlacements(
            DefaultLayout.WidgetIds,
            _registry,
            Screens,
            () => Guid.NewGuid().ToString("N"));

        if (placements.Count == 0)
            return;

        _workspace.AddWidgets(placements);
        _workspace.SaveNow();
    }

    public void Dispose()
    {
        _workspace.Dispose();

        _runtimes.Dispose();

        _tray.Dispose();

        _settings.Close();
    }

    // ---- Widget management ----------------------------------------------

    public WidgetInfo? AddWidget(IWidgetProvider provider)
    {
        var placement = CreatePlacement(provider);
        var runtime = CreateWidget(placement);

        if (runtime is null)
            return null;

        _workspace.AddWidget(placement);
        _workspace.RaiseChanged();

        return new WidgetInfo(
            placement.InstanceId,
            placement.WidgetId,
            provider.Descriptor.DisplayName,
            runtime.ViewModel);
    }

    private WidgetRuntime? CreateWidget(WidgetPlacement placement) =>
        _runtimes.Add(placement, this, Screens, BuildContextMenu);

    private void DestroyWidget(WidgetRuntime runtime) => _runtimes.Destroy(runtime);

    private void Remove(WidgetRuntime runtime)
    {
        DestroyWidget(runtime);
        _workspace.RemoveWidget(runtime.Placement.InstanceId);
        _workspace.RaiseChanged();
    }

    /// <summary>
    /// Copies the window's live position and size back into the stored placement.
    /// </summary>
    private WidgetPlacement CreatePlacement(IWidgetProvider provider) =>
        WidgetSeedPolicy.CreatePlacement(
            provider,
            _runtimes.Runtimes.Count,
            Screens,
            Guid.NewGuid().ToString("N"));

    private ContextMenu BuildContextMenu(WidgetRuntime runtime) =>
        _contextMenus.Create(
            openSettings: () => _settings.Open(runtime.ViewModel),
            remove: () => Remove(runtime));

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _workspace.ReplaceSettings(settings);

        _appearance.ApplyTheme(settings.Theme);

        if (!string.Equals(Language.Setting, settings.Language, StringComparison.Ordinal))
            Language.Apply(settings.Language);

        if (_autoStart.IsSupported && _autoStart.IsEnabled != settings.StartWithWindows)
        {
            try
            {
                _autoStart.SetEnabled(settings.StartWithWindows);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not change the start-with-Windows setting");
            }
        }

        _tray.SetVisible(settings.ShowTrayIcon);

        _appearance.ApplyCardSurfaces(_runtimes.Runtimes);

        _workspace.RaiseChanged();
    }

    public void SetWidgetsVisible(bool visible)
    {
        _workspace.ReplaceSettings(State.Settings with { WidgetsVisible = visible });

        foreach (var widget in _runtimes.Runtimes)
        {
            _desktopLayer.SetVisible(widget.Window, visible);
            widget.IsVisible = visible;
        }

        _workspace.RaiseChanged();
    }

    private WindowIcon AppIcon => _assets.Icon;

    private WidgetRuntime? FindRuntime(WidgetViewModel viewModel) => _runtimes.Find(viewModel);
}

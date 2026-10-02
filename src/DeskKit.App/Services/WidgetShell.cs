using Avalonia.Controls;
using DeskKit.App.Localization;
using DeskKit.App.Shell;
using DeskKit.App.ViewModels;
using DeskKit.App.Views;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Platform;
using Microsoft.Extensions.Logging;
using DeskKit.Runtime;
using DeskKit.Runtime.Views;

namespace DeskKit.App.Services;

/// <summary>
/// The front door to the widgets: what the application starts, stops and asks for, and
/// what the product's own UI talks to.
/// </summary>
/// <remarks>
/// Every job this class used to do itself now lives in a collaborator under
/// <c>DeskKit.App.Shell</c>: <see cref="WorkspaceState"/> owns the state,
/// <see cref="WidgetRuntimeHost"/> owns the widget instances and their lifetimes, and the
/// placement, appearance, tray, settings window, context menu and storage notice each own
/// their own part. What is left here is the order a session starts in, the two interfaces
/// the application and the widgets are written against, and the decisions that need more
/// than one collaborator at once.
/// </remarks>
public sealed class WidgetShell : IWidgetHost, IShellFacade, IDisposable
{
    private readonly WorkspaceState _workspace;
    private readonly PlacementController _placement;
    private readonly WidgetRuntimeHost _runtimes;
    private readonly WidgetCatalog _catalog;
    private readonly ShellAssets _assets;
    private readonly AutoStartController _autoStartPolicy;
    private readonly AppearanceController _appearance;
    private readonly LanguageService _language;
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
    {
        var workspace = new WorkspaceState(stateStore, logger);
        var assets = new ShellAssets();

        // Resolved before anything that consumes it is built: the material decides the
        // layout of every window and the brush every card is painted with, and it cannot
        // wait for the stored config to load because the first window is created before
        // that is read.
        _material = materials.Resolve(materials.Default);
        _surfaceMargin = WidgetWindow.MarginFor(_material);

        _workspace = workspace;
        _placement = new PlacementController(workspace);
        _catalog = new WidgetCatalog(registry);
        _assets = assets;
        _autoStartPolicy = new AutoStartController(autoStart, logger);
        _appearance = new AppearanceController(themeService);
        _language = languageService;
        _messages = messages;

        _workspace.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        _runtimes = new WidgetRuntimeHost(
            registry,
            _placement,
            tickService,
            workspace,
            desktopLayer,
            _surfaceMargin,
            new WidgetSurfaceFactory(desktopLayer, materials, _material, _surfaceMargin, assets.Icon),
            logger);

        _startup = new ShellStartup(registry, DefaultLayout.WidgetIds, logger);
        _settings = new SettingsWindowController(this, assets, languageService);
        _tray = new TrayIconController(this, _catalog, assets, languageService, () => _settings.Open(null));
        _storageNotices = new StorageNoticePresenter(notices, _tray);
    }

    /// <summary>What the last load found, so the product can explain the session.</summary>
    public StoreLoadReport LoadReport => _workspace.LoadReport;

    internal IReadOnlyList<WidgetRuntime> Runtimes => _runtimes.Runtimes;

    public AppState State => _workspace.State;

    /// <summary>Raised after persisted state changes so open UI can refresh.</summary>
    public event EventHandler? StateChanged;

    public IReadOnlyList<WidgetInfo> Widgets =>
        [.. _runtimes.Runtimes.Select(_catalog.Describe)];

    public IReadOnlyList<IWidgetProvider> AvailableWidgets => _catalog.Providers;

    // ---- IWidgetHost -----------------------------------------------------

    public IReadOnlyList<ScreenBounds> Screens => ScreenProbe.GetScreens();

    public IWidgetMessageBus Messages => _messages;

    public void ShowSettings(WidgetViewModel widget)
    {
        _settings.Open(widget);
    }

    public void RemoveWidget(WidgetViewModel widget)
    {
        if (_runtimes.Find(widget) is { } runtime)
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
        _language.Apply(State.Settings.Language);

        if (_autoStartPolicy.Reconcile(State.Settings) is { } corrected)
            _workspace.ReplaceSettings(corrected);

        // A migration reached the disk now rather than whenever something else happens
        // to change: one that only landed on exit would run again on every start until
        // then.
        if (_startup.MigrateWidgetSettings(State.Widgets) is { } migrated)
            _workspace.ReplaceWidgets(migrated);

        if (!_workspace.HasStoredState)
        {
            // A first run gets something to show rather than an empty desktop, and the
            // result is written straight away instead of waiting for the debounce.
            _workspace.AddWidgets(_startup.FirstRunPlacements(Screens));
            _workspace.SaveNow();
        }

        // The stored placements are deliberately not normalised into State: a display
        // layout that cannot show a widget is this session's problem to solve, not a
        // reason to overwrite the position the user chose. Doing it here is what used
        // to make a layout degrade a little every time a laptop was undocked, and on
        // a roaming profile it made two machines overwrite each other's. CreateWidget
        // places the window where it can be seen instead.
        foreach (var placement in State.Widgets.Where(p => p.Enabled))
            _runtimes.Add(placement, this, Screens, BuildContextMenu);

        _runtimes.StartTicking();
        _tray.Show();

        _storageNotices.Show(_workspace.LoadReport);
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
        ArgumentNullException.ThrowIfNull(provider);

        if (_runtimes.Add(provider, this, Screens, BuildContextMenu) is not { } runtime)
            return null;

        _workspace.AddWidget(runtime.Placement);
        _workspace.RaiseChanged();

        return _catalog.Describe(runtime);
    }

    private void Remove(WidgetRuntime runtime)
    {
        _runtimes.Destroy(runtime);
        _workspace.RemoveWidget(runtime.Placement.InstanceId);
        _workspace.RaiseChanged();
    }

    private ContextMenu BuildContextMenu(WidgetRuntime runtime) =>
        _contextMenus.Create(
            openSettings: () => _settings.Open(runtime.ViewModel),
            remove: () => Remove(runtime));

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _workspace.ReplaceSettings(settings);
        _appearance.ApplyTheme(settings.Theme);

        if (!string.Equals(_language.Setting, settings.Language, StringComparison.Ordinal))
            _language.Apply(settings.Language);

        _autoStartPolicy.Apply(settings);
        _tray.SetVisible(settings.ShowTrayIcon);
        _appearance.ApplyCardSurfaces(_runtimes.Runtimes);

        _workspace.RaiseChanged();
    }

    public void SetWidgetsVisible(bool visible)
    {
        _workspace.ReplaceSettings(State.Settings with { WidgetsVisible = visible });
        _runtimes.SetVisible(visible);
        _workspace.RaiseChanged();
    }
}

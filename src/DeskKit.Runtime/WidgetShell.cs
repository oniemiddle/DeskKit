using Avalonia.Controls;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using DeskKit.Runtime.Views;
using Microsoft.Extensions.Logging;

namespace DeskKit.Runtime;

/// <summary>
/// The front door to the widgets: what the application starts, stops and asks for, and
/// what the product's own UI talks to.
/// </summary>
/// <remarks>
/// Every job this class used to do itself lives in a collaborator: <see cref="WorkspaceState"/>
/// owns the state, <see cref="WidgetRuntimeHost"/> the widget instances and their
/// lifetimes, <see cref="PlacementController"/> the snapping rules,
/// <see cref="AppearanceController"/> what the theme does to a live surface, and
/// <see cref="ShellStartup"/> what a load report is worth saying. What is left is the
/// order a session starts in, the interfaces the application and the widgets are written
/// against, and the decisions that need more than one collaborator.
/// <para>
/// Nothing product-shaped is here. The tray icon, the settings window, the context menu,
/// the storage notice and start-with-Windows are the application's, wired in the
/// composition root; the runtime only says that a widget exists (<see cref="WidgetAdded"/>)
/// and that a widget asked for its settings (<see cref="SettingsRequested"/>).
/// </para>
/// </remarks>
public sealed class WidgetShell : IWidgetHost, IShellFacade, IDisposable
{
    private readonly WorkspaceState _workspace;
    private readonly WidgetRuntimeHost _runtimes;
    private readonly WidgetRegistry _registry;
    private readonly ShellStartup _startup;
    private readonly ShellEnvironment _environment;
    private readonly AppearanceController _appearance;
    private readonly IWidgetMessageBus _messages;

    // Resolved together before anything that consumes them: the material decides the
    // inset, and both decide the layout of every window.
    private readonly WidgetMaterial _material;
    private readonly double _surfaceMargin;

    /// <summary>The language last handed to the product, so it is not applied twice.</summary>
    private string? _appliedLanguage;

    public WidgetShell(
        IStateStore stateStore,
        WidgetRegistry registry,
        IDesktopLayerService desktopLayer,
        TickService tickService,
        ThemeService themeService,
        IWindowMaterialService materials,
        ShellEnvironment environment,
        IWidgetMessageBus messages,
        IReadOnlyList<string> firstRunWidgetIds,
        ILogger<WidgetShell> logger)
    {
        ArgumentNullException.ThrowIfNull(firstRunWidgetIds);

        var workspace = new WorkspaceState(stateStore, logger);
        var placement = new PlacementController(workspace);

        // Resolved before anything that consumes it is built: the material decides the
        // layout of every window and the brush every card is painted with, and it cannot
        // wait for the stored config to load because the first window is created before
        // that is read.
        _material = materials.Resolve(materials.Default);
        _surfaceMargin = WidgetWindow.MarginFor(_material);

        _workspace = workspace;
        _registry = registry;
        _environment = environment;
        _appearance = new AppearanceController(themeService);
        _messages = messages;

        _workspace.Changed += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);

        _runtimes = new WidgetRuntimeHost(
            registry,
            placement,
            tickService,
            workspace,
            desktopLayer,
            _surfaceMargin,
            new WidgetSurfaceFactory(desktopLayer, materials, _material, _surfaceMargin, environment.Icon),
            logger);

        _startup = new ShellStartup(registry, firstRunWidgetIds, logger);
    }

    /// <summary>
    /// What the last load found. The product is what explains a session that could not
    /// read its stored state, and the runtime holds the answer.
    /// </summary>
    public StoreLoadReport LoadReport => _workspace.LoadReport;

    public AppState State => _workspace.State;

    /// <summary>Raised after persisted state changes so open UI can refresh.</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised for every widget as it appears, before it is shown, so the product can hang
    /// its own chrome on the window. Not raised for one that failed to be created.
    /// </summary>
    public event EventHandler<WidgetRuntime>? WidgetAdded;

    /// <summary>
    /// Raised when a widget asks for its own settings. The runtime has no settings
    /// window, so the product answers this.
    /// </summary>
    public event EventHandler<WidgetViewModel>? SettingsRequested;

    public IReadOnlyList<WidgetRuntime> Runtimes => _runtimes.Runtimes;

    public IReadOnlyList<IWidgetProvider> AvailableWidgets => _registry.Providers;

    public IReadOnlyList<ScreenBounds> Screens => ScreenProbe.GetScreens();

    public IWidgetMessageBus Messages => _messages;

    public void ShowSettings(WidgetViewModel widget)
    {
        ArgumentNullException.ThrowIfNull(widget);

        SettingsRequested?.Invoke(this, widget);
    }

    public void RemoveWidget(WidgetViewModel widget)
    {
        if (_runtimes.Find(widget) is { } runtime)
            Remove(runtime);
    }

    public void RequestSave() => _workspace.ScheduleSave();

    /// <summary>
    /// True when the stored data belongs to a newer build: nothing was read and nothing
    /// may be written, so a session that carried on would show an empty desktop.
    /// </summary>
    public bool StoredDataIsNewer => _workspace.LoadReport.Outcome == StoreOutcome.NewerSchema;

    public void Start()
    {
        _workspace.Load();
        _startup.ReportLoad(_workspace.LoadReport, _workspace.DatabasePath);

        _appearance.ApplyTheme(State.Settings.Theme);

        // Before any widget is created, so the first window it builds is already
        // titled in the right language.
        ApplyLanguage(State.Settings.Language);

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
        // reason to overwrite the position the user chose. CreateWidget places the window
        // where it can be seen instead.
        foreach (var placement in State.Widgets.Where(p => p.Enabled))
            Add(placement);

        _runtimes.StartTicking();
    }

    public void Dispose()
    {
        // The product's own windows and the tray are not the shell's to close.
        _workspace.Dispose();
        _runtimes.Dispose();
    }

    public WidgetRuntime? AddWidget(IWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (_runtimes.Add(provider, this, Screens) is not { } runtime)
            return null;

        WidgetAdded?.Invoke(this, runtime);

        _workspace.AddWidget(runtime.Placement);
        _workspace.RaiseChanged();

        return runtime;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _workspace.ReplaceSettings(settings);
        _appearance.ApplyTheme(settings.Theme);
        ApplyLanguage(settings.Language);
        _appearance.ApplyCardSurfaces(_runtimes.Runtimes);

        _workspace.RaiseChanged();
    }

    public void SetWidgetsVisible(bool visible)
    {
        _workspace.ReplaceSettings(State.Settings with { WidgetsVisible = visible });
        _runtimes.SetVisible(visible);
        _workspace.RaiseChanged();
    }

    /// <summary>
    /// Creates one widget and tells the product it exists, before the window is shown:
    /// whatever the product hangs on it is then in place from the first frame.
    /// </summary>
    private void Add(WidgetPlacement placement)
    {
        if (_runtimes.Add(placement, this, Screens) is { } runtime)
            WidgetAdded?.Invoke(this, runtime);
    }

    private void Remove(WidgetRuntime runtime)
    {
        _runtimes.Destroy(runtime);
        _workspace.RemoveWidget(runtime.Placement.InstanceId);
        _workspace.RaiseChanged();
    }

    /// <summary>
    /// Hands the language to the product, once per change: the runtime is what drives the
    /// change, so it remembers what it last applied rather than asking the product.
    /// </summary>
    private void ApplyLanguage(string setting)
    {
        if (string.Equals(_appliedLanguage, setting, StringComparison.Ordinal))
            return;

        _appliedLanguage = setting;
        _environment.ApplyLanguage?.Invoke(setting);
    }
}
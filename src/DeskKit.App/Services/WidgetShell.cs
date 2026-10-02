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
using DeskKit.Widgets.Clock;
using DeskKit.Widgets.Localization;
using Microsoft.Extensions.Logging;

namespace DeskKit.App.Services;

/// <summary>
/// Owns every widget: creating and destroying windows, keeping placements in
/// sync with the database, driving the shared tick, and providing the tray
/// menu and settings window.
/// </summary>
public sealed class WidgetShell : IWidgetHost, IDisposable
{
    /// <summary>Space left between two widgets that snap next to each other.</summary>
    private const int SnapGap = WidgetSnapEngine.DefaultGap;

    /// <summary>How close an edge must be, in physical pixels, before it snaps.</summary>
    private const int SnapThreshold = WidgetSnapEngine.DefaultThreshold;

    private readonly WorkspaceState _workspace;
    private readonly PlacementController _placement;
    private readonly WidgetRuntimeHost _runtimes;
    private readonly WidgetRegistry _registry;
    private readonly WidgetCatalog _catalog;
    private readonly ShellAssets _assets;
    private readonly IDesktopLayerService _desktopLayer;
    private readonly IAutoStartService _autoStart;
    private readonly AppearanceController _appearance;
    private readonly IWindowMaterialService _materials;
    private readonly ILogger<WidgetShell> _logger;
    private readonly INoticePresenter _notices;
    private readonly IWidgetMessageBus _messages;

    /// <summary>The material every widget window carries, already resolved.</summary>
    private readonly WidgetMaterial _material;

    /// <summary>Transparent inset each window keeps around its card for that material.</summary>
    private readonly double _surfaceMargin;

    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;

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
        _materials = materials;
        Language = languageService;
        _notices = notices;
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
    }

    /// <summary>The language preference and the managers it drives.</summary>
    internal LanguageService Language { get; }

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
        OpenSettings(FindRuntime(widget));
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

        var report = _workspace.LoadReport;

        foreach (var problem in report.Problems)
        {
            _logger.LogWarning(
                "Storage problem with {Subject}: {Detail}", problem.Subject, problem.Detail);
        }

        switch (report.Outcome)
        {
            // The two messages that explain a session. They are logged here rather than
            // when a save is refused, because this runs before anything can be changed
            // and a session may end without ever attempting to save 鈥?which would leave
            // the user with an empty desktop and no explanation anywhere.
            case StoreOutcome.NewerSchema:
                _logger.LogWarning(
                    "The database {File} was written by a newer version of DeskKit, so it was not "
                    + "read and will not be written over. Upgrade DeskKit to use the layout it "
                    + "holds; nothing done in this session will be saved.",
                    _workspace.DatabasePath);
                break;

            case StoreOutcome.Unavailable:
                _logger.LogWarning(
                    "The database {File} could not be opened, so DeskKit started with nothing "
                    + "loaded and will not write over it. Nothing done in this session will be "
                    + "saved. Close whatever is holding it, then restart DeskKit.",
                    _workspace.DatabasePath);
                break;

            case StoreOutcome.Imported:
                _logger.LogInformation(
                    "Imported the configuration from {Source} into {Database}",
                    report.ImportedFrom,
                    _workspace.DatabasePath);
                break;
        }

        _appearance.ApplyTheme(State.Settings.Theme);

        // Before any widget is created, so the first window it builds is already
        // titled in the right language.
        Language.Apply(State.Settings.Language);

        ReconcileAutoStart();

        MigrateWidgetSettings();

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
        CreateTrayIcon();

        ShowStorageNotice();
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
    /// 鈥?writing a run key at startup because a file said so 鈥?is a side effect
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
    /// Brings each widget's own settings up to the version its provider declares.
    /// </summary>
    /// <remarks>
    /// This does write back into the stored placements, which the placement rules
    /// otherwise never do. The difference is what is being written: a display that
    /// cannot show a widget is this session's problem, while a settings migration is
    /// a real change to what was stored 鈥?the same kind of correction as reconciling
    /// the start-with-Windows flag against the registry.
    /// </remarks>
    private void MigrateWidgetSettings()
    {
        var result = WidgetSettingsMigrator.Apply(State.Widgets, _registry);

        // Logged in the order the placements were read, so the log of a session with
        // several widgets reads the same way it did before the walk moved out of here.
        foreach (var entry in result.Entries)
        {
            if (entry.Problem is not null)
            {
                _logger.LogWarning(
                    "Widget {InstanceId} ({WidgetId}) settings were left alone: {Problem}",
                    entry.InstanceId,
                    entry.WidgetId,
                    entry.Problem);
                continue;
            }

            if (entry.Migrated)
            {
                _logger.LogInformation(
                    "Widget {InstanceId} ({WidgetId}) settings brought forward to version {Version}",
                    entry.InstanceId,
                    entry.WidgetId,
                    entry.Version);
            }
        }

        if (!result.Changed)
            return;

        // Persisted now rather than whenever something else happens to change: a
        // migration that only reached the disk on exit would run again on every
        // start until then.
        _workspace.ReplaceWidgets(result.Placements);
    }

    /// <summary>
    /// Puts whatever went wrong with the stored files in front of the user, once,
    /// rather than only in the log.
    /// </summary>
    /// <remarks>
    /// The log holds the details, but it is only read once something has already
    /// gone wrong. A session that has quietly stopped saving loses the user's work at
    /// the point they close the application, which is far too late to say so.
    /// </remarks>
    private void ShowStorageNotice()
    {
        var report = _workspace.LoadReport;
        if (!report.HasProblems)
            return;

        var lines = new List<string>();

        switch (report.Outcome)
        {
            case StoreOutcome.Unavailable:
                lines.Add(AppLanguage.Instance.Notice_Unavailable.CurrentText());
                break;

            case StoreOutcome.NewerSchema:
                lines.Add(AppLanguage.Instance.Notice_NewerSchema.CurrentText());
                break;

            default:
                // Import or read problems, which the outcome alone does not describe:
                // the session runs as usual, minus whatever could not be read. The two
                // outcomes above already say that nothing was read at all, so line by
                // line detail would only bury them.
                if (report.Problems.Count > 0)
                    lines.Add(AppLanguage.Instance.Notice_DataProblem.CurrentText());
                break;
        }

        var title = AppLanguage.Instance.Notice_Title.CurrentText();

        _notices.Show(new Notice(
            title,
            string.Join(Environment.NewLine + Environment.NewLine, lines)));

        // Repeated on the tray icon, which neither expires when the notice does nor can
        // be covered by anything.
        _trayIcon?.ToolTipText = $"DeskKit 鈥?{title}";
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

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;

            if (Application.Current is { } application
                && TrayIcon.GetIcons(application) is { } icons)
            {
                icons.Remove(_trayIcon);
            }

            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _settingsWindow?.Close();
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
    private void CapturePlacement(WidgetRuntime runtime) =>
        _placement.Capture(runtime, _surfaceMargin);

    private Size WindowSizeForPlacement(WidgetPlacement placement, WidgetDescriptor descriptor)
    {
        var cardWidth = placement.Width > 0 ? placement.Width : descriptor.DefaultWidth;
        var cardHeight = placement.Height > 0 ? placement.Height : descriptor.DefaultHeight;
        return WidgetWindow.WindowSizeForCard(cardWidth, cardHeight, _surfaceMargin);
    }

    // ---- Magnetic snapping ----------------------------------------------

    private PixelPoint SnapPosition(WidgetRuntime moving, PixelPoint proposed) =>
        _placement.Snap(_runtimes.Runtimes, moving, proposed, _surfaceMargin);

    private void ClearSnapHighlights() => PlacementController.ClearHighlights(_runtimes.Runtimes);

    private void OnDragCompleted(WidgetRuntime runtime)
    {
        ClearSnapHighlights();
        _placement.Capture(runtime, _surfaceMargin);
    }

    private WidgetPlacement CreatePlacement(IWidgetProvider provider) =>
        WidgetSeedPolicy.CreatePlacement(
            provider,
            _runtimes.Runtimes.Count,
            Screens,
            Guid.NewGuid().ToString("N"));

    private ContextMenu BuildContextMenu(WidgetRuntime runtime)
    {
        var menu = new ContextMenu();

        // Subscribing sets the current text and keeps it right afterwards, because
        // the observable emits the value it already has on subscribe.
        var settings = new MenuItem();
        AppLanguage.Instance.Menu_Settings.SubscribeAction(text => settings.Header = text);
        settings.Click += (_, _) => OpenSettings(runtime);
        menu.Items.Add(settings);

        var remove = new MenuItem();
        AppLanguage.Instance.Menu_Remove.SubscribeAction(text => remove.Header = text);
        remove.Click += (_, _) => Remove(runtime);
        menu.Items.Add(remove);

        return menu;
    }

    // ---- Settings and tray ----------------------------------------------

    private void OpenSettings(WidgetRuntime? runtime)
    {
        _settingsWindow ??= CreateSettingsWindow();

        if (!_settingsWindow.IsVisible)
            _settingsWindow.Show();

        _settingsWindow.Activate();

        if (runtime is not null)
            _settingsWindow.SelectWidget(runtime.ViewModel);
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var window = new SettingsWindow
        {
            Icon = AppIcon,
            DataContext = new SettingsViewModel(this),
        };

        window.Closed += (_, _) => _settingsWindow = null;
        return window;
    }

    /// <summary>
    /// Stores a whole set of preferences and makes the running application match.
    /// </summary>
    /// <remarks>
    /// A record rather than four parameters, because the four were exactly the fields
    /// of <see cref="AppSettings"/>: passing them one by one meant two adjacent
    /// strings that a caller could swap without the compiler noticing.
    /// </remarks>
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

        if (_trayIcon is not null)
            _trayIcon.IsVisible = settings.ShowTrayIcon;

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

    private void CreateTrayIcon()
    {
        var addMenu = new NativeMenu();
        foreach (var provider in _registry.Providers)
        {
            var item = new NativeMenuItem();
            var captured = provider;
            item.Click += (_, _) => AddWidget(captured);
            addMenu.Add(item);

            // Live, so a language change renames the entries in place instead of
            // leaving the tray in the language it happened to start in.
            if (_catalog.ObservableName(provider.Descriptor.Id) is { } name)
                name.SubscribeAction(text => item.Header = text);
            else
                item.Header = provider.Descriptor.DisplayName;
        }

        var menu = new NativeMenu();

        var add = new NativeMenuItem { Menu = addMenu };
        AppLanguage.Instance.Tray_AddWidget.SubscribeAction(text => add.Header = text);
        menu.Add(add);

        menu.Add(new NativeMenuItemSeparator());

        // The header depends on the widget state as well as the language, so both
        // the click and the culture change go through the same refresh.
        var toggle = new NativeMenuItem();
        void RefreshToggle() => toggle.Header = State.Settings.WidgetsVisible
            ? AppLanguage.Instance.Tray_HideAll.CurrentText()
            : AppLanguage.Instance.Tray_ShowAll.CurrentText();

        toggle.Click += (_, _) =>
        {
            SetWidgetsVisible(!State.Settings.WidgetsVisible);
            RefreshToggle();
        };

        menu.Add(toggle);

        var settings = new NativeMenuItem();
        AppLanguage.Instance.Tray_Settings.SubscribeAction(text => settings.Header = text);
        settings.Click += (_, _) => OpenSettings(null);
        menu.Add(settings);

        menu.Add(new NativeMenuItemSeparator());

        var exit = new NativeMenuItem();
        AppLanguage.Instance.Tray_Exit.SubscribeAction(text => exit.Header = text);
        exit.Click += (_, _) => (Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        menu.Add(exit);

        // One combined refresh: the toggle needs both the language and the state,
        // and every other header is already subscribed.
        void OnCultureChanged(object? sender, EventArgs e) => RefreshToggle();

        RefreshToggle();

        // The shell owns both the service and this handler, so they end together.
        Language.CultureChanged += OnCultureChanged;

        _trayIcon = new TrayIcon
        {
            Icon = AppIcon,
            ToolTipText = "DeskKit",
            Menu = menu,
            IsVisible = State.Settings.ShowTrayIcon,
        };

        _trayIcon.Clicked += (_, _) => SetWidgetsVisible(!State.Settings.WidgetsVisible);

        // Avalonia 12 hosts tray icons in a collection attached to the
        // Application rather than a single Application.TrayIcon property.
        if (Application.Current is { } application)
        {
            var icons = TrayIcon.GetIcons(application) ?? new TrayIcons();
            icons.Add(_trayIcon);
            TrayIcon.SetIcons(application, icons);
        }
    }

    private WindowIcon AppIcon => _assets.Icon;

    private WidgetRuntime? FindRuntime(WidgetViewModel viewModel) => _runtimes.Find(viewModel);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using DeskKit.App.Localization;
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
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>Space left between two widgets that snap next to each other.</summary>
    private const int SnapGap = WidgetSnapEngine.DefaultGap;

    /// <summary>How close an edge must be, in physical pixels, before it snaps.</summary>
    private const int SnapThreshold = WidgetSnapEngine.DefaultThreshold;

    private readonly StateStore _stateStore;
    private readonly WidgetRegistry _registry;
    private readonly IDesktopLayerService _desktopLayer;
    private readonly IAutoStartService _autoStart;
    private readonly TickService _tickService;
    private readonly ThemeService _themeService;
    private readonly IWindowMaterialService _materials;
    private readonly ILogger<WidgetShell> _logger;
    private readonly INoticePresenter _notices;
    private readonly IWidgetMessageBus _messages;
    private readonly List<WidgetRuntime> _widgets = [];

    /// <summary>The material every widget window carries, already resolved.</summary>
    private readonly WidgetMaterial _material;

    /// <summary>Transparent inset each window keeps around its card for that material.</summary>
    private readonly double _surfaceMargin;

    private WindowIcon? _appIcon;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _saveTimer;

    public WidgetShell(
        StateStore stateStore,
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
        _stateStore = stateStore;
        _registry = registry;
        _desktopLayer = desktopLayer;
        _autoStart = autoStart;
        _tickService = tickService;
        _themeService = themeService;
        _materials = materials;
        Language = languageService;
        _notices = notices;
        _messages = messages;
        _logger = logger;

        // Resolved once, at construction, because the answer changes the layout of
        // every window and every snap measurement. It also has to be known before
        // the first window is created, so it cannot wait for the config to load.
        _material = materials.Resolve(materials.Default);
        _surfaceMargin = WidgetWindow.MarginFor(_material);
    }

    /// <summary>The language preference and the managers it drives.</summary>
    internal LanguageService Language { get; }

    internal IReadOnlyList<WidgetRuntime> Runtimes => _widgets;

    public AppState State { get; private set; } = new();

    /// <summary>Raised after persisted state changes so open UI can refresh.</summary>
    public event EventHandler? StateChanged;

    public IReadOnlyList<WidgetInfo> Widgets =>
    [
        .. _widgets
            .Select(w => new WidgetInfo(
                w.Placement.InstanceId,
                w.Placement.WidgetId,

                // Resolved now rather than carried from the descriptor, which holds
                // a resource key. The settings window re-reads this list whenever the
                // culture changes, so the names follow the language.
                WidgetText.Value(
                    _registry.Find(w.Placement.WidgetId)?.Descriptor.DisplayName
                    ?? w.Placement.WidgetId),
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

    public void RequestSave() => ScheduleSave();

    /// <summary>
    /// True when the stored data belongs to a newer build. Nothing was read and
    /// nothing may be written, so a session that carried on would show an empty
    /// desktop and quietly keep none of it.
    /// </summary>
    public bool StoredDataIsNewer => _stateStore.LoadReport.Outcome == StoreOutcome.NewerSchema;

    // ---- Lifecycle -------------------------------------------------------

    public void Start()
    {
        State = _stateStore.Load();

        var report = _stateStore.LoadReport;

        foreach (var problem in report.Problems)
        {
            _logger.LogWarning(
                "Storage problem with {Subject}: {Detail}", problem.Subject, problem.Detail);
        }

        switch (report.Outcome)
        {
            // The two messages that explain a session. They are logged here rather than
            // when a save is refused, because this runs before anything can be changed
            // and a session may end without ever attempting to save — which would leave
            // the user with an empty desktop and no explanation anywhere.
            case StoreOutcome.NewerSchema:
                _logger.LogWarning(
                    "The database {File} was written by a newer version of DeskKit, so it was not "
                    + "read and will not be written over. Upgrade DeskKit to use the layout it "
                    + "holds; nothing done in this session will be saved.",
                    _stateStore.DatabasePath);
                break;

            case StoreOutcome.Unavailable:
                _logger.LogWarning(
                    "The database {File} could not be opened, so DeskKit started with nothing "
                    + "loaded and will not write over it. Nothing done in this session will be "
                    + "saved. Close whatever is holding it, then restart DeskKit.",
                    _stateStore.DatabasePath);
                break;

            case StoreOutcome.Imported:
                _logger.LogInformation(
                    "Imported the configuration from {Source} into {Database}",
                    report.ImportedFrom,
                    _stateStore.DatabasePath);
                break;
        }

        _themeService.Apply(State.Settings.Theme);

        // Before any widget is created, so the first window it builds is already
        // titled in the right language.
        Language.Apply(State.Settings.Language);

        ReconcileAutoStart();

        MigrateWidgetSettings();

        if (!_stateStore.HasStoredState)
            SeedDefaultWidgets();

        // The stored placements are deliberately not normalised into State: a display
        // layout that cannot show a widget is this session's problem to solve, not a
        // reason to overwrite the position the user chose. Doing it here is what used
        // to make a layout degrade a little every time a laptop was undocked, and on
        // a roaming profile it made two machines overwrite each other's. CreateWidget
        // places the window where it can be seen instead.
        foreach (var placement in State.Widgets.Where(p => p.Enabled))
            CreateWidget(placement);

        _tickService.Start();
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
    /// — writing a run key at startup because a file said so — is a side effect
    /// nobody asked for on that machine.
    /// </para>
    /// </summary>
    private void ReconcileAutoStart()
    {
        if (!_autoStart.IsSupported || State.Settings.StartWithWindows == _autoStart.IsEnabled)
            return;

        State = State with
        {
            Settings = State.Settings with { StartWithWindows = _autoStart.IsEnabled },
        };

        ScheduleSave();
    }

    /// <summary>
    /// Brings each widget's own settings up to the version its provider declares.
    /// </summary>
    /// <remarks>
    /// This does write back into the stored placements, which the placement rules
    /// otherwise never do. The difference is what is being written: a display that
    /// cannot show a widget is this session's problem, while a settings migration is
    /// a real change to what was stored — the same kind of correction as reconciling
    /// the start-with-Windows flag against the registry.
    /// </remarks>
    private void MigrateWidgetSettings()
    {
        var migrated = false;

        for (var index = 0; index < State.Widgets.Count; index++)
        {
            var placement = State.Widgets[index];

            if (_registry.Find(placement.WidgetId) is IWidgetSettingsMigrations declared)
            {
                var version = SettingsMigrations.Apply(
                    new WidgetSettings(placement.Settings),
                    placement.SettingsVersion,
                    declared.SettingsVersion,
                    declared.Migrations,
                    out var problem);

                if (problem is not null)
                {
                    _logger.LogWarning(
                        "Widget {InstanceId} ({WidgetId}) settings were left alone: {Problem}",
                        placement.InstanceId,
                        placement.WidgetId,
                        problem);
                    continue;
                }

                if (version == placement.SettingsVersion)
                    continue;

                State.Widgets[index] = placement with { SettingsVersion = version };
                migrated = true;

                _logger.LogInformation(
                    "Widget {InstanceId} ({WidgetId}) settings brought forward to version {Version}",
                    placement.InstanceId,
                    placement.WidgetId,
                    version);
            }
        }

        // Persisted now rather than whenever something else happens to change: a
        // migration that only reached the disk on exit would run again on every
        // start until then.
        if (migrated)
            ScheduleSave();
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
        var report = _stateStore.LoadReport;
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
        _trayIcon?.ToolTipText = $"DeskKit — {title}";
    }

    /// <summary>
    /// Puts one clock on the desktop the very first time the application runs,
    /// so a new user sees something rather than an empty desktop and a tray
    /// icon. It is not restored after the user removes it.
    /// </summary>
    private void SeedDefaultWidgets()
    {
        if (_registry.Find(ClockWidgetProvider.WidgetId) is { } provider)
        {
            State.Widgets.Add(CreatePlacement(provider));
            SaveNow();
        }
    }

    public void Dispose()
    {
        _saveTimer?.Stop();
        SaveNow();

        foreach (var widget in _widgets.ToArray())
            DestroyWidget(widget);

        _tickService.Dispose();

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

        State.Widgets.Add(placement);
        ScheduleSave();
        StateChanged?.Invoke(this, EventArgs.Empty);

        return new WidgetInfo(
            placement.InstanceId,
            placement.WidgetId,
            provider.Descriptor.DisplayName,
            runtime.ViewModel);
    }

    private WidgetRuntime? CreateWidget(WidgetPlacement placement)
    {
        var provider = _registry.Find(placement.WidgetId);
        if (provider is null)
        {
            _logger.LogWarning("Ignoring widget {WidgetId}: no provider is registered", placement.WidgetId);
            return null;
        }

        var descriptor = provider.Descriptor;
        var settings = new WidgetSettings(placement.Settings);
        var context = new WidgetContext(placement, settings, this);

        // The stored position, adjusted only if the current displays cannot show it.
        var onScreen = PlacementNormalizer.EnsureOnScreen(placement, Screens);

        WidgetViewModel viewModel;
        try
        {
            viewModel = provider.Create(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Widget {WidgetId} failed to be created", placement.WidgetId);
            return null;
        }

        var window = new WidgetWindow(_desktopLayer, _materials, _material)
        {
            Title = WidgetText.Value(descriptor.DisplayName),
            AcceptsKeyboardFocus = !descriptor.PreventActivation,
            Icon = GetAppIcon(),
            CardBackground = ThemeService.CardBrushFor(_material),
            WidgetContent = viewModel.CreateView(),
            Width = WindowSizeForPlacement(placement, descriptor).Width,
            Height = WindowSizeForPlacement(placement, descriptor).Height,
            MinWidth = WidgetWindow.WindowSizeForCard(descriptor.MinWidth, descriptor.MinHeight, _surfaceMargin).Width,
            MinHeight = WidgetWindow.WindowSizeForCard(descriptor.MinWidth, descriptor.MinHeight, _surfaceMargin).Height,

            // Where the window is actually put, which is not necessarily where it
            // was stored: a monitor may have gone away since. The placement itself
            // is left alone, so the widget returns to where the user left it once
            // that monitor is back.
            Position = new PixelPoint(onScreen.X, onScreen.Y),
        };

        var runtime = new WidgetRuntime(placement, viewModel, window);
        window.SetContextMenu(BuildContextMenu(runtime));
        window.SnapStrategy = proposed => SnapPosition(runtime, proposed);
        window.DragCompleted += (_, _) => OnDragCompleted(runtime);
        window.ResizeCompleted += (_, _) => CapturePlacement(runtime);

        if (viewModel is ITickAware tickAware)
            _tickService.Subscribe(tickAware);

        _widgets.Add(runtime);

        try
        {
            window.Show();
            viewModel.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Widget {WidgetId} failed to be shown", placement.WidgetId);
            DestroyWidget(runtime);
            return null;
        }

        // The window manager does not always honour the requested origin, and a
        // widget that is stored somewhere other than where it actually sits
        // drifts a little further on every restart. Storing what the window
        // really is removes the whole class of problem.
        //
        // Compared against what was asked for, not against the stored placement:
        // this records where the window manager put the window, and must not turn
        // a display-driven adjustment into a stored one.
        var requested = new PixelPoint(onScreen.X, onScreen.Y);
        if (window.Position != requested)
        {
            _logger.LogInformation(
                "Widget {WidgetId} was asked for {Requested} but placed at {Actual}",
                placement.WidgetId, requested, window.Position);
            CapturePlacement(runtime);
        }

        if (!State.Settings.WidgetsVisible)
            _desktopLayer.SetVisible(window, false);

        return runtime;
    }

    private void DestroyWidget(WidgetRuntime runtime)
    {
        _widgets.Remove(runtime);

        if (runtime.ViewModel is ITickAware tickAware)
            _tickService.Unsubscribe(tickAware);

        try
        {
            runtime.ViewModel.Stop();
            runtime.ViewModel.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Widget {WidgetId} failed to shut down cleanly", runtime.Placement.WidgetId);
        }

        try
        {
            runtime.Window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Widget window failed to close");
        }
    }

    private void Remove(WidgetRuntime runtime)
    {
        DestroyWidget(runtime);
        State.Widgets.RemoveAll(p => p.InstanceId == runtime.Placement.InstanceId);

        ScheduleSave();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Copies the window's live position and size back into the stored
    /// placement. Position is physical, size is logical; see
    /// <see cref="WidgetPlacement"/> for why.
    /// </summary>
    private void CapturePlacement(WidgetRuntime runtime)
    {
        var window = runtime.Window;

        // Stored sizes describe the visible card, not the window, so the
        // transparent margin never leaks into the persisted layout.
        var card = WidgetWindow.CardSizeForWindow(
            window.Width > 0 ? window.Width : window.Bounds.Width,
            window.Height > 0 ? window.Height : window.Bounds.Height,
            _surfaceMargin);

        runtime.Placement = runtime.Placement with
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = card.Width,
            Height = card.Height,
        };

        var index = State.Widgets.FindIndex(p => p.InstanceId == runtime.Placement.InstanceId);
        if (index >= 0)
            State.Widgets[index] = runtime.Placement;

        ScheduleSave();
    }

    private Size WindowSizeForPlacement(WidgetPlacement placement, WidgetDescriptor descriptor)
    {
        var cardWidth = placement.Width > 0 ? placement.Width : descriptor.DefaultWidth;
        var cardHeight = placement.Height > 0 ? placement.Height : descriptor.DefaultHeight;
        return WidgetWindow.WindowSizeForCard(cardWidth, cardHeight, _surfaceMargin);
    }

    // ---- Magnetic snapping ----------------------------------------------

    /// <summary>
    /// Applies magnetism to a proposed drag position and highlights whatever the
    /// widget snapped to, including the widget being dragged so the magnetised
    /// group reads as one.
    /// </summary>
    /// <remarks>
    /// Rectangles are compared in <b>card</b> space, not window space: the window
    /// carries a transparent margin for the glow, and measuring that instead
    /// would make two snapped widgets sit 32px further apart than the configured
    /// gap. Everything is in physical pixels, because that is the unit the
    /// pointer and the window position are both in.
    /// </remarks>
    private PixelPoint SnapPosition(WidgetRuntime moving, PixelPoint proposed)
    {
        if (!TryGetCardSize(moving, out var cardSize))
        {
            ClearSnapHighlights();
            return proposed;
        }

        // The card sits inside the window by the surface margin, so the same
        // offset has to come off every rectangle before comparing and go back on
        // afterwards.
        var offset = new PixelVector(
            (int)Math.Round(_surfaceMargin * WindowScaling(moving)),
            (int)Math.Round(_surfaceMargin * WindowScaling(moving)));

        var candidates = new List<PixelRect>(_widgets.Count);
        var owners = new List<WidgetRuntime>(_widgets.Count);

        foreach (var other in _widgets)
        {
            if (ReferenceEquals(other, moving) || !other.IsVisible)
                continue;

            if (!TryGetCardRect(other, offset, out var rect))
                continue;

            candidates.Add(rect);
            owners.Add(other);
        }

        if (candidates.Count == 0)
        {
            ClearSnapHighlights();
            return proposed;
        }

        var proposedCard = new PixelRect(proposed + offset, cardSize);
        var result = WidgetSnapEngine.Snap(proposedCard, candidates);

        ClearSnapHighlights();

        if (result.Snapped)
        {
            // Each side lights the stretch it actually shares with the other, so
            // the glow points at the specific region the two widgets have in
            // common rather than vaguely at a whole edge.
            moving.Window.SetSnapHighlight(result.Glow);

            var settled = new PixelRect(result.Position, cardSize);

            foreach (var index in result.Neighbours)
            {
                if (index < 0 || index >= owners.Count)
                    continue;

                owners[index].Window.SetSnapHighlight(
                    WidgetSnapEngine.GlowSegments(candidates[index], [settled]));
            }
        }

        return result.Position - offset;
    }

    private void ClearSnapHighlights()
    {
        foreach (var widget in _widgets)
            widget.Window.SetSnapHighlight([]);
    }

    private void OnDragCompleted(WidgetRuntime runtime)
    {
        ClearSnapHighlights();
        CapturePlacement(runtime);
    }

    private static double WindowScaling(WidgetRuntime runtime)
    {
        var scaling = runtime.Window.RenderScaling;
        return scaling > 0 ? scaling : 1;
    }

    /// <summary>The widget's visible card size, in physical pixels.</summary>
    private static bool TryGetCardSize(WidgetRuntime runtime, out PixelSize size)
    {
        size = default;

        var card = runtime.Window.CardBounds;
        if (card.Width <= 0 || card.Height <= 0)
            return false;

        var scaling = WindowScaling(runtime);
        size = new PixelSize(
            (int)Math.Round(card.Width * scaling),
            (int)Math.Round(card.Height * scaling));

        return true;
    }

    /// <summary>The widget's visible card rectangle, in physical screen pixels.</summary>
    private static bool TryGetCardRect(WidgetRuntime runtime, PixelVector offset, out PixelRect rect)
    {
        rect = default;

        if (!TryGetCardSize(runtime, out var size))
            return false;

        rect = new PixelRect(runtime.Window.Position + offset, size);
        return true;
    }

    private WidgetPlacement CreatePlacement(IWidgetProvider provider)
    {
        var descriptor = provider.Descriptor;
        var screen = Screens.FirstOrDefault();
        var offset = (_widgets.Count % 6) * 32;

        return new WidgetPlacement
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            WidgetId = descriptor.Id,
            X = screen.X + 80 + offset,
            Y = screen.Y + 80 + offset,
            Width = descriptor.DefaultWidth,
            Height = descriptor.DefaultHeight,

            // A widget created now is written in today's shape, so it never has to
            // be migrated and must not be stamped with a version it did not come from.
            SettingsVersion = (provider as IWidgetSettingsMigrations)?.SettingsVersion ?? 1,
        };
    }

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
            Icon = GetAppIcon(),
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

        State = State with { Settings = settings };

        _themeService.Apply(settings.Theme);

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

        foreach (var widget in _widgets)
            widget.Window.CardBackground = ThemeService.CardBrushFor(_material);

        ScheduleSave();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetWidgetsVisible(bool visible)
    {
        State = State with { Settings = State.Settings with { WidgetsVisible = visible } };

        foreach (var widget in _widgets)
        {
            _desktopLayer.SetVisible(widget.Window, visible);
            widget.IsVisible = visible;
        }

        ScheduleSave();
        StateChanged?.Invoke(this, EventArgs.Empty);
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
            if (WidgetText.Observable(provider.Descriptor.DisplayName) is { } name)
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
            Icon = GetAppIcon(),
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

    private WindowIcon GetAppIcon() =>
        _appIcon ??= new WindowIcon(
            AssetLoader.Open(new Uri("avares://DeskKit.App/Assets/deskkit.ico")));

    // ---- Persistence -----------------------------------------------------

    /// <summary>
    /// Coalesces bursts of changes into one write. Dragging a widget raises a
    /// change per pixel, so writing immediately would hammer the disk.
    /// </summary>
    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = SaveDebounce };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                SaveNow();
            };
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        try
        {
            // A refusal is deliberately not reported here: Start already said what may
            // not be written, in full, and this runs on every debounced change.
            // Repeating it would only fill the log.
            _stateStore.Save(State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the configuration");
        }
    }

    private WidgetRuntime? FindRuntime(WidgetViewModel viewModel) =>
        _widgets.FirstOrDefault(w => ReferenceEquals(w.ViewModel, viewModel));
}

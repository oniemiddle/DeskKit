using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
/// sync with the config file, driving the shared tick, and providing the tray
/// menu and settings window.
/// </summary>
public sealed class WidgetShell : IWidgetHost, IDisposable
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(500);

    /// <summary>Space left between two widgets that snap next to each other.</summary>
    private const int SnapGap = WidgetSnapEngine.DefaultGap;

    /// <summary>How close an edge must be, in physical pixels, before it snaps.</summary>
    private const int SnapThreshold = WidgetSnapEngine.DefaultThreshold;

    private readonly ConfigStore _configStore;
    private readonly WidgetRegistry _registry;
    private readonly IDesktopLayerService _desktopLayer;
    private readonly IAutoStartService _autoStart;
    private readonly TickService _tickService;
    private readonly ThemeService _themeService;
    private readonly LanguageService _languageService;
    private readonly IWindowMaterialService _materials;
    private readonly ILogger<WidgetShell> _logger;
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
        ConfigStore configStore,
        WidgetRegistry registry,
        IDesktopLayerService desktopLayer,
        IAutoStartService autoStart,
        TickService tickService,
        ThemeService themeService,
        ILogger<WidgetShell> logger)
        : this(
            configStore,
            registry,
            desktopLayer,
            autoStart,
            tickService,
            themeService,
            new NullWindowMaterialService(),
            new LanguageService(),
            logger)
    {
    }

    public WidgetShell(
        ConfigStore configStore,
        WidgetRegistry registry,
        IDesktopLayerService desktopLayer,
        IAutoStartService autoStart,
        TickService tickService,
        ThemeService themeService,
        IWindowMaterialService materials,
        ILogger<WidgetShell> logger)
        : this(
            configStore,
            registry,
            desktopLayer,
            autoStart,
            tickService,
            themeService,
            materials,
            new LanguageService(),
            logger)
    {
    }

    public WidgetShell(
        ConfigStore configStore,
        WidgetRegistry registry,
        IDesktopLayerService desktopLayer,
        IAutoStartService autoStart,
        TickService tickService,
        ThemeService themeService,
        IWindowMaterialService materials,
        LanguageService languageService,
        ILogger<WidgetShell> logger)
    {
        _configStore = configStore;
        _registry = registry;
        _desktopLayer = desktopLayer;
        _autoStart = autoStart;
        _tickService = tickService;
        _themeService = themeService;
        _materials = materials;
        _languageService = languageService;
        _logger = logger;

        // Resolved once, at construction, because the answer changes the layout of
        // every window and every snap measurement. It also has to be known before
        // the first window is created, so it cannot wait for the config to load.
        _material = materials.Resolve(materials.Default);
        _surfaceMargin = WidgetWindow.MarginFor(_material);
    }

    /// <summary>The language preference and the managers it drives.</summary>
    internal LanguageService Language => _languageService;

    internal IReadOnlyList<WidgetRuntime> Runtimes => _widgets;

    public AppState State { get; private set; } = new();

    /// <summary>Raised after persisted state changes so open UI can refresh.</summary>
    public event EventHandler? StateChanged;

    public IReadOnlyList<WidgetInfo> Widgets =>
        _widgets
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
            .ToList();

    public IReadOnlyList<IWidgetProvider> AvailableWidgets => _registry.Providers;

    // ---- IWidgetHost -----------------------------------------------------

    public IReadOnlyList<ScreenBounds> Screens => ScreenProbe.GetScreens();

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

    // ---- Lifecycle -------------------------------------------------------

    public void Start()
    {
        State = _configStore.Load();

        if (_configStore.LastCorruptFileBackup is { Length: > 0 } backup)
        {
            _logger.LogWarning(
                "The configuration file could not be parsed and was moved to {Backup}", backup);
        }

        _themeService.Apply(State.Settings.Theme);

        // Before any widget is created, so the first window it builds is already
        // titled in the right language.
        _languageService.Apply(State.Settings.Language);

        var normalized = PlacementNormalizer.EnsureAllOnScreen(State.Widgets, Screens);
        State = State with { Widgets = [.. normalized] };

        if (!_configStore.FileExistedOnLoad)
            SeedDefaultWidgets();

        foreach (var placement in State.Widgets.Where(p => p.Enabled))
            CreateWidget(placement);

        _tickService.Start();
        CreateTrayIcon();
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
            Position = new PixelPoint(placement.X, placement.Y),
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
        var requested = new PixelPoint(placement.X, placement.Y);
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

    public void ApplySettings(string theme, bool startWithWindows, bool showTrayIcon, string language)
    {
        State = State with
        {
            Settings = State.Settings with
            {
                Theme = theme,
                StartWithWindows = startWithWindows,
                ShowTrayIcon = showTrayIcon,
                Language = language,
            },
        };

        _themeService.Apply(theme);

        if (!string.Equals(_languageService.Setting, language, StringComparison.Ordinal))
            _languageService.Apply(language);

        if (_autoStart.IsSupported && _autoStart.IsEnabled != startWithWindows)
        {
            try
            {
                _autoStart.SetEnabled(startWithWindows);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not change the start-with-Windows setting");
            }
        }

        if (_trayIcon is not null)
            _trayIcon.IsVisible = showTrayIcon;

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
            ? LinguaText.Of(AppLanguage.Instance.Tray_HideAll)
            : LinguaText.Of(AppLanguage.Instance.Tray_ShowAll);

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
        _languageService.CultureChanged += OnCultureChanged;

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
            _configStore.Save(State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the configuration file");
        }
    }

    private WidgetRuntime? FindRuntime(WidgetViewModel viewModel) =>
        _widgets.FirstOrDefault(w => ReferenceEquals(w.ViewModel, viewModel));
}

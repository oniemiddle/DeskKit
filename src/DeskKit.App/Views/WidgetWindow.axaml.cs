using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using DeskKit.Core.Models;
using DeskKit.Platform;

namespace DeskKit.App.Views;

/// <summary>
/// A single widget window: a borderless, fully transparent top-level window that
/// hosts one widget's content and is glued to the desktop by
/// <see cref="IDesktopLayerService"/>.
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly IDesktopLayerService _desktopLayer;

    private bool _dragging;
    private WidgetDragSession _dragSession;
    private bool _resizing;
    private Point _resizeStartLocal;
    private Size _resizeStartSize;

    public WidgetWindow()
        : this(new NullDesktopLayerService())
    {
    }

    public WidgetWindow(IDesktopLayerService desktopLayer)
    {
        _desktopLayer = desktopLayer;

        InitializeComponent();

        CardBorder.PointerPressed += OnDragSurfacePointerPressed;
        CardBorder.PointerMoved += OnDragSurfacePointerMoved;
        CardBorder.PointerReleased += OnDragSurfacePointerReleased;

        ResizeGrip.PointerPressed += OnResizeGripPointerPressed;
        ResizeGrip.PointerMoved += OnResizeGripPointerMoved;
        ResizeGrip.PointerReleased += OnResizeGripPointerReleased;
    }

    /// <summary>True when this window can take keyboard focus (sticky notes need it).</summary>
    public bool AcceptsKeyboardFocus { get; init; } = true;

    /// <summary>Raised after the user finishes dragging the widget.</summary>
    public event EventHandler? DragCompleted;

    /// <summary>Raised after the user finishes resizing the widget.</summary>
    public event EventHandler? ResizeCompleted;

    /// <summary>Attaches the widget's right-click menu.</summary>
    public void SetContextMenu(ContextMenu menu) => CardBorder.ContextMenu = menu;

    /// <summary>The native window handle, or <see cref="IntPtr.Zero"/> before the window is shown.</summary>
    public IntPtr Handle => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    /// <summary>The control hosted inside the widget card.</summary>
    public object? WidgetContent
    {
        get => ContentHost.Content;
        set => ContentHost.Content = value;
    }

    public IBrush CardBackground
    {
        get => CardBorder.Background ?? Brushes.Transparent;
        set => CardBorder.Background = value;
    }

    public CornerRadius CardCornerRadius
    {
        get => CardBorder.CornerRadius;
        set => CardBorder.CornerRadius = value;
    }

    /// <summary>
    /// Transparent space kept around the card so its drop shadow has room to
    /// render. The shadow is clipped by the window, so a non-zero margin is
    /// what makes it visible.
    /// </summary>
    public Thickness CardMargin
    {
        get => CardBorder.Margin;
        set => CardBorder.Margin = value;
    }

    /// <summary>Exposed so diagnostics can drive show/hide the same way the shell does.</summary>
    public IDesktopLayerService DesktopLayer => _desktopLayer;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // The pinning hook can only be installed once the platform handle exists.
        _desktopLayer.Attach(
            this,
            new DesktopLayerOptions(PreventActivation: !AcceptsKeyboardFocus));

        _desktopLayer.SyncNormalSize(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _desktopLayer.Detach(this);
        base.OnClosed(e);
    }

    private void OnDragSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // Let buttons, text boxes and menus inside the widget handle their own input.
        if (IsInteractive(e.Source as Visual))
            return;

        _dragging = true;

        // Record where the cursor is relative to the window's origin, in screen
        // pixels. The cursor is still inside the window at this instant, so the
        // window has definitely not moved yet.
        _dragSession = WidgetDragSession.Start(
            this.PointToScreen(e.GetPosition(this)), Position);

        e.Pointer.Capture(CardBorder);
        e.Handled = true;
    }

    private void OnDragSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        // Deliberately a function of the pointer alone. Deriving the new origin
        // from the window's current position would feed the window's own
        // movement back into the calculation, which makes the widget lurch back
        // towards where the drag started instead of tracking the cursor.
        var target = _dragSession.PositionFor(this.PointToScreen(e.GetPosition(this)));

        if (Position != target)
            Position = target;
    }

    private void OnDragSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _resizing = true;
        _resizeStartLocal = e.GetPosition(this);
        _resizeStartSize = new Size(Bounds.Width, Bounds.Height);
        e.Pointer.Capture(ResizeGrip);
        e.Handled = true;
    }

    private void OnResizeGripPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_resizing)
            return;

        var current = e.GetPosition(this);
        Width = Math.Max(MinWidth, _resizeStartSize.Width + (current.X - _resizeStartLocal.X));
        Height = Math.Max(MinHeight, _resizeStartSize.Height + (current.Y - _resizeStartLocal.Y));
    }

    private void OnResizeGripPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_resizing)
            return;

        _resizing = false;
        e.Pointer.Capture(null);

        // Remember the settled size so the pinning hook can tell a genuine
        // resize apart from a collapse to the caption icon rect.
        _desktopLayer.SyncNormalSize(this);
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// True when the pointer event originated on a control that handles input
    /// itself, in which case the window must not start a drag.
    /// </summary>
    private static bool IsInteractive(Visual? source)
    {
        for (var current = source; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or TextBox or ToggleButton or Menu or ScrollBar)
                return true;
        }

        return false;
    }
}

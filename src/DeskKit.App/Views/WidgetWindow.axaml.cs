using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using DeskKit.Core.Abstractions;
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
    /// <summary>
    /// Transparent inset between the window edge and the card, in logical
    /// pixels. It exists so the card's own drop shadow has somewhere to render: a
    /// shadow drawn outside the card would otherwise be clipped by the window.
    /// </summary>
    public const double GlowMargin = 16;

    /// <summary>The bar's colour while the widget is being moved.</summary>
    internal static readonly IBrush DragBarActiveBrush =
        new SolidColorBrush(Color.Parse("#FF2563EB"));

    /// <summary>The bar's colour on hover, before the widget starts moving.</summary>
    internal static readonly IBrush DragBarIdleBrush =
        new SolidColorBrush(Color.Parse("#59FFFFFF"));

    private readonly IDesktopLayerService _desktopLayer;
    private readonly IWindowMaterialService _materials;

    private bool _dragging;
    private WidgetDragSession _dragSession;
    private bool _hoveringDragHandle;
    private WidgetResizeSession _resizeSession;
    private WidgetEdges _resizingEdges = WidgetEdges.None;

    public WidgetWindow()
        : this(new NullDesktopLayerService(), new NullWindowMaterialService(), WidgetMaterial.None)
    {
    }

    public WidgetWindow(IDesktopLayerService desktopLayer)
        : this(desktopLayer, new NullWindowMaterialService(), WidgetMaterial.None)
    {
    }

    public WidgetWindow(
        IDesktopLayerService desktopLayer,
        IWindowMaterialService materials,
        WidgetMaterial material)
    {
        _desktopLayer = desktopLayer;
        _materials = materials;
        Material = material;

        InitializeComponent();
        ApplyMaterial(material);

        CardBorder.PointerPressed += OnDragSurfacePointerPressed;
        CardBorder.PointerMoved += OnDragSurfacePointerMoved;
        CardBorder.PointerReleased += OnDragSurfacePointerReleased;

        // Hover reveals the drag affordance. PointerMoved is wired in as well,
        // because Entered/Exited can be missed when the window slides out from
        // under the cursor mid-drag.
        CardBorder.PointerEntered += (_, _) => SetDragAffordance(hovered: true, _dragging);
        CardBorder.PointerExited += (_, _) => SetDragAffordance(hovered: false, _dragging);
    }

    /// <summary>
    /// The surface material this window carries, already resolved to something the
    /// platform can render.
    /// </summary>
    public WidgetMaterial Material { get; }

    /// <summary>
    /// The transparent inset a widget window keeps around its card when given the
    /// material. A material is painted by the window across its whole rectangle,
    /// so an inset card would sit on a visible plate of it — and the platform,
    /// not the card, draws the rounded corners and the shadow in that mode.
    /// </summary>
    public static double MarginFor(WidgetMaterial material) =>
        material.FillsWindow() ? 0 : GlowMargin;

    /// <summary>
    /// Puts the window into the mode its material implies. Called from the
    /// constructor because the surface has to be chosen before the window is
    /// created on the platform, and the layout has to agree with it.
    /// </summary>
    private void ApplyMaterial(WidgetMaterial material)
    {
        CardMargin = new Thickness(MarginFor(material));
        _materials.Prepare(this, material);

        if (!material.FillsWindow())
            return;

        // The window is the surface now. A card shadow painted inside an opaque
        // window would darken the material rather than fall on anything, and a
        // rounded card inside a rectangular one would show the material as corner
        // wedges, so both belong to the platform.
        CardShadow = default;
        CardCornerRadius = default;
    }

    /// <summary>True when this window can take keyboard focus (sticky notes need it).</summary>
    public bool AcceptsKeyboardFocus { get; init; } = true;

    /// <summary>
    /// Consulted during a drag to magnetically align the widget. The shell
    /// supplies it because only the shell knows about the other widgets; it also
    /// takes care of highlighting whichever neighbours were snapped to.
    /// </summary>
    public Func<PixelPoint, PixelPoint>? SnapStrategy { get; set; }

    /// <summary>Raised after the user finishes dragging the widget.</summary>
    public event EventHandler? DragCompleted;

    /// <summary>Raised after the user finishes resizing the widget.</summary>
    public event EventHandler? ResizeCompleted;

    /// <summary>Attaches the widget's right-click menu.</summary>
    public void SetContextMenu(ContextMenu menu) => CardBorder.ContextMenu = menu;

    /// <summary>The native window handle, or <see cref="IntPtr.Zero"/> before the window is shown.</summary>
    internal IntPtr Handle => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

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
        set
        {
            CardBorder.CornerRadius = value;

            // The glow is clipped to the card's outline, so it has to follow the
            // card's radius.
            SnapGlow.CardCornerRadius = value.TopLeft;
        }
    }

    /// <summary>
    /// Transparent space kept around the card so its drop shadow has room to
    /// render. Nothing is painted out there, so the glow layer stays inside the
    /// card and does not track this.
    /// </summary>
    public Thickness CardMargin
    {
        get => CardBorder.Margin;
        set => CardBorder.Margin = value;
    }

    /// <summary>
    /// The reserved drag region, in window coordinates. Every widget has one,
    /// including widgets whose content covers the rest of the surface.
    /// </summary>
    internal Rect DragHandleBounds => ToWindowBounds(DragHandle);

    /// <summary>The widget content area, in window coordinates.</summary>
    internal Rect ContentBounds => ToWindowBounds(ContentHost);

    /// <summary>
    /// The visible card, in window coordinates. This is the window inset by the
    /// surface margin, and it is what snapping measures, so that two widgets
    /// snapped together show the configured gap between their visible edges rather
    /// than between their windows. With a material the inset is zero, and the card
    /// and the window are the same rectangle.
    /// </summary>
    internal Rect CardBounds => ToWindowBounds(CardBorder);

    /// <summary>Shadows cast by the card. Cleared when the platform draws them.</summary>
    public BoxShadows CardShadow
    {
        get => CardBorder.BoxShadow;
        set => CardBorder.BoxShadow = value;
    }

    /// <summary>Screen size of a window sized to hold a card of the given size.</summary>
    public static Size WindowSizeForCard(
        double cardWidth, double cardHeight, double margin = GlowMargin)
    {
        var (width, height) = WindowCardGeometry.WindowSizeForCard(cardWidth, cardHeight, margin);
        return new Size(width, height);
    }

    /// <summary>Card size held by a window of the given size.</summary>
    public static Size CardSizeForWindow(
        double windowWidth, double windowHeight, double margin = GlowMargin)
    {
        var (width, height) = WindowCardGeometry.CardSizeForWindow(windowWidth, windowHeight, margin);
        return new Size(width, height);
    }

    /// <summary>
    /// Background of the drag strip. It is an overlay lying on top of the widget
    /// content, so this must stay transparent — anything else would paint over
    /// whatever the widget is showing.
    /// </summary>
    internal IBrush? DragHandleBackground => DragHandle.Background;

    /// <summary>
    /// The cursor the drag strip asks for. Null means hovering it does not
    /// change the cursor.
    /// </summary>
    internal Cursor? DragHandleCursor => DragHandle.Cursor;

    /// <summary>Opacity of the drag affordance bar: 0 while it is hidden.</summary>
    internal double DragBarOpacity => DragBar.Opacity;

    /// <summary>Colour of the drag affordance bar.</summary>
    internal IBrush? DragBarBackground => DragBar.Background;

    /// <summary>How many shadows the drag bar casts. Zero would mean it risks
    /// vanishing on a light background.</summary>
    internal int DragBarShadowCount => DragBar.BoxShadow.Count;

    /// <summary>Whether the magnetism glow is currently drawn at all.</summary>
    internal bool IsSnapGlowVisible => SnapGlow.IsVisible;

    /// <summary>The stretches of the card currently lit by the magnetism glow.</summary>
    internal IReadOnlyList<WidgetGlowSegment> GlowSegments => SnapGlow.Segments;

    /// <summary>Reach of the glow inwards from a vertical edge, in DIPs.</summary>
    internal double GlowHorizontalFadeLength => SnapGlow.HorizontalFadeLength;

    /// <summary>Reach of the glow inwards from a horizontal edge, in DIPs.</summary>
    internal double GlowVerticalFadeLength => SnapGlow.VerticalFadeLength;

    /// <summary>
    /// How far the light reaches along the edge beyond the region the widgets
    /// share. The shared region decides where the light is, not how far it goes.
    /// </summary>
    internal double GlowSpreadAlongEdge => SnapGlow.SpreadAlongEdge;

    /// <summary>
    /// The rectangle the glow can paint on, in window coordinates. The glow is a
    /// surface effect on the card, so this must sit inside the card — the
    /// renderer additionally clips it to the card's rounded outline.
    /// </summary>
    internal Rect GlowBounds => ToWindowBounds(SnapGlow);

    /// <summary>Alpha used at the shared edge; well below opaque on purpose.</summary>
    internal byte GlowEdgeAlpha => SnapGlow.EdgeAlpha;

    /// <summary>Shape of the falloff away from the shared edge.</summary>
    internal double GlowFalloffExponent => SnapGlow.FalloffExponent;

    /// <summary>Width of the specular band on the outermost edge, in DIPs.</summary>
    internal double GlowHighlightWidth => SnapGlow.EdgeHighlightWidth;

    /// <summary>Alpha of the specular band where it is brightest.</summary>
    internal byte GlowHighlightAlpha => SnapGlow.HighlightAlpha;

    /// <summary>Colour of the specular band.</summary>
    internal Color GlowHighlightColor => SnapGlow.HighlightColor;

    /// <summary>
    /// False means the glow cannot swallow pointer input, which is what lets it
    /// be an overlay.
    /// </summary>
    internal bool SnapGlowHitTestable => SnapGlow.IsHitTestVisible;

    /// <summary>
    /// Shows the magnetism glow on the given stretches of the card's edges, and
    /// clears it otherwise. The stretches come from the region the widget shares
    /// with the widgets it snapped to, so a partly overlapping neighbour lights
    /// only the part of the edge it actually covers.
    /// </summary>
    public void SetSnapHighlight(IReadOnlyList<WidgetGlowSegment> segments)
    {
        SnapGlow.Segments = segments;

        // The property setter normally handles this, but an empty list leaves it
        // to us to make sure a stale glow cannot linger.
        SnapGlow.IsVisible = segments.Count > 0;
    }

    /// <summary>
    /// Sets the drag affordance appearance. Exposed so the self-test can drive
    /// it without synthesising pointer input.
    /// </summary>
    internal void SetDragAffordance(bool hovered, bool dragging)
    {
        _hoveringDragHandle = hovered;

        // The bar is decoration: it is absent at rest, fades in while the
        // pointer is over the widget, and turns accent-coloured for as long as
        // the widget is actually being moved.
        if (dragging)
        {
            DragBar.Background = DragBarActiveBrush;
            DragBar.Opacity = 1;
        }
        else if (hovered)
        {
            DragBar.Background = DragBarIdleBrush;
            DragBar.Opacity = 0.85;
        }
        else
        {
            DragBar.Opacity = 0;
        }
    }

    /// <summary>Exposed so diagnostics can drive show/hide the same way the shell does.</summary>
    internal IDesktopLayerService DesktopLayer => _desktopLayer;

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

        var pointerInWindow = e.GetPosition(this);

        // A resize band wins over everything else: it sits on the card's outer
        // few pixels, which are padding rather than content.
        var edges = HitTestResizeEdges(pointerInWindow);
        if (edges != WidgetEdges.None)
        {
            BeginResize(edges, this.PointToScreen(pointerInWindow));
            e.Pointer.Capture(CardBorder);
            e.Handled = true;
            return;
        }

        // Let buttons, text boxes and menus inside the widget handle their own input.
        if (IsInteractive(e.Source as Visual))
            return;

        _dragging = true;
        SetDragAffordance(hovered: true, dragging: true);

        // Record where the cursor is relative to the window's origin, in screen
        // pixels. The cursor is still inside the window at this instant, so the
        // window has definitely not moved yet.
        _dragSession = WidgetDragSession.Start(
            this.PointToScreen(pointerInWindow), Position);

        e.Pointer.Capture(CardBorder);
        e.Handled = true;
    }

    private void OnDragSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        // Safety net for a missed PointerEntered, and the only thing that keeps
        // the affordance correct when the window slides under the cursor.
        if (!_hoveringDragHandle && !_dragging)
            SetDragAffordance(hovered: true, dragging: false);

        if (_resizingEdges != WidgetEdges.None)
        {
            ApplyResize(this.PointToScreen(e.GetPosition(this)));
            e.Handled = true;
            return;
        }

        if (!_dragging)
        {
            UpdateResizeCursor(e.GetPosition(this));
            return;
        }

        // Deliberately a function of the pointer alone. Deriving the new origin
        // from the window's current position would feed the window's own
        // movement back into the calculation, which makes the widget lurch back
        // towards where the drag started instead of tracking the cursor.
        var target = _dragSession.PositionFor(this.PointToScreen(e.GetPosition(this)));

        // Magnetism is applied afterwards, so it nudges the pointer-derived
        // position rather than feeding into it.
        if (SnapStrategy is { } snap)
            target = snap(target);

        if (Position != target)
            Position = target;
    }

    private void OnDragSurfacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingEdges != WidgetEdges.None)
        {
            EndResize();
            e.Pointer.Capture(null);
            return;
        }

        if (!_dragging)
            return;

        _dragging = false;
        SetDragAffordance(hovered: true, dragging: false);
        e.Pointer.Capture(null);
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Which edges, if any, a pointer position grabs. The card's top band is
    /// excluded because the drag strip owns it; the top corners still resize.
    /// </summary>
    internal WidgetEdges HitTestResizeEdges(Point pointerInWindow) =>
        WidgetResizeSession.HitTest(CardBounds, pointerInWindow, allowTopEdge: false);

    /// <summary>
    /// Starts dragging the given edges from the given screen pointer position.
    /// The press handlers call this; it is public so the resize can also be
    /// driven programmatically.
    /// </summary>
    internal void BeginResize(WidgetEdges edges, PixelPoint pointerScreen)
    {
        if (edges == WidgetEdges.None)
            return;

        _resizingEdges = edges;
        _resizeSession = WidgetResizeSession.Begin(
            edges,
            pointerScreen,
            new PixelRect(Position, PixelSize.FromSize(Bounds.Size, RenderScaling)));
    }

    /// <summary>Applies a resize for the given pointer position.</summary>
    internal void ApplyResize(PixelPoint pointerScreen)
    {
        if (_resizingEdges == WidgetEdges.None)
            return;

        var target = _resizeSession.Resolve(pointerScreen, MinimumSizeInPixels());

        // Window.Width is the window size in device-independent pixels, and the
        // resolved rectangle is in physical pixels, so it is a plain conversion —
        // the glow margin is already part of the window rectangle and must not be
        // taken out here, or the window ends up a margin smaller every time.
        Width = Math.Max(MinWidth, target.Width / RenderScaling);
        Height = Math.Max(MinHeight, target.Height / RenderScaling);
        Position = new PixelPoint(target.X, target.Y);
    }

    /// <summary>Finishes a resize and reports it.</summary>
    internal void EndResize()
    {
        if (_resizingEdges == WidgetEdges.None)
            return;

        _resizingEdges = WidgetEdges.None;

        // Remember the settled size, so the pinning hook can tell a genuine
        // resize from a collapse to the caption icon rect.
        _desktopLayer.SyncNormalSize(this);
        ResizeCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True while an edge or corner is being dragged.</summary>
    internal bool IsResizing => _resizingEdges != WidgetEdges.None;

    /// <summary>Which edges the in-progress resize is dragging.</summary>
    internal WidgetEdges ResizingEdges => _resizingEdges;

    /// <summary>Shows a resize cursor over the resize bands, and nothing elsewhere.</summary>
    private void UpdateResizeCursor(Point pointerInWindow)
    {
        var edges = HitTestResizeEdges(pointerInWindow);
        Cursor = edges switch
        {
            WidgetEdges.Left or WidgetEdges.Right => new Cursor(StandardCursorType.SizeWestEast),
            WidgetEdges.Top or WidgetEdges.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
            WidgetEdges.Left | WidgetEdges.Top => new Cursor(StandardCursorType.TopLeftCorner),
            WidgetEdges.Right | WidgetEdges.Top => new Cursor(StandardCursorType.TopRightCorner),
            WidgetEdges.Left | WidgetEdges.Bottom => new Cursor(StandardCursorType.BottomLeftCorner),
            WidgetEdges.Right | WidgetEdges.Bottom => new Cursor(StandardCursorType.BottomRightCorner),
            _ => null,
        };
    }

    /// <summary>The minimum window size in physical pixels.</summary>
    private PixelSize MinimumSizeInPixels() =>
        PixelSize.FromSize(new Size(MinWidth, MinHeight), RenderScaling);

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

    /// <summary>Converts a child's bounds into window coordinates.</summary>
    private Rect ToWindowBounds(Visual visual)
    {
        var origin = visual.TranslatePoint(default, this) ?? default;
        return new Rect(origin, visual.Bounds.Size);
    }
}

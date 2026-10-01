using Avalonia;

namespace DeskKit.Core.Models;

/// <summary>
/// Resizing a widget window by dragging one of its edges or corners.
/// <para>
/// The system resize loop is not used: it reorders the window, which fights the
/// "always at the bottom of the z-order" rule the desktop layer depends on. So
/// the geometry is done here, and like dragging it is expressed as a pure
/// function of the pointer's screen position and the state captured when the
/// button went down — never of where the window currently is. Feeding the
/// window's own position back in is exactly what made the drag oscillate.
/// </para>
/// </summary>
public readonly record struct WidgetResizeSession
{
    private WidgetResizeSession(WidgetEdges edges, PixelPoint pointerStart, PixelRect start)
    {
        Edges = edges;
        PointerStart = pointerStart;
        Start = start;
    }

    /// <summary>Which edges are being dragged.</summary>
    public WidgetEdges Edges { get; }

    /// <summary>Pointer position, in screen pixels, when the drag began.</summary>
    public PixelPoint PointerStart { get; }

    /// <summary>The window rectangle when the drag began.</summary>
    public PixelRect Start { get; }

    /// <summary>Width of the strip along an edge that starts a resize.</summary>
    public const double DefaultBandWidth = 5;

    /// <summary>
    /// Size of the square at each corner that starts a two-axis resize. Kept
    /// modest so it does not eat much of the top drag strip.
    /// </summary>
    public const double DefaultCornerSize = 14;

    public static WidgetResizeSession Begin(
        WidgetEdges edges, PixelPoint pointerScreen, PixelRect windowRect) =>
        new(edges, pointerScreen, windowRect);

    /// <summary>
    /// Works out where the pointer is relative to the card and which edges, if
    /// any, that position grabs.
    /// </summary>
    /// <param name="card">The visible card, in window coordinates.</param>
    /// <param name="point">Pointer position in window coordinates.</param>
    /// <param name="allowTopEdge">
    /// False when the top edge belongs to something else — the drag strip lives
    /// there — leaving only the top corners able to resize.
    /// </param>
    public static WidgetEdges HitTest(
        Rect card,
        Point point,
        bool allowTopEdge,
        double bandWidth = DefaultBandWidth,
        double cornerSize = DefaultCornerSize)
    {
        if (card.Width <= 0 || card.Height <= 0)
            return WidgetEdges.None;

        if (point.X < card.X || point.Y < card.Y || point.X > card.Right || point.Y > card.Bottom)
            return WidgetEdges.None;

        var fromLeft = point.X - card.X;
        var fromRight = card.Right - point.X;
        var fromTop = point.Y - card.Y;
        var fromBottom = card.Bottom - point.Y;

        var horizontalCorner = fromLeft <= cornerSize || fromRight <= cornerSize;
        var verticalCorner = fromTop <= cornerSize || fromBottom <= cornerSize;

        // Corners win, so a two-axis resize stays reachable even where an edge
        // band is reserved for something else.
        if (horizontalCorner && verticalCorner)
            return Side(fromLeft, fromRight, WidgetEdges.Left, WidgetEdges.Right)
                   | Side(fromTop, fromBottom, WidgetEdges.Top, WidgetEdges.Bottom);

        var edges = WidgetEdges.None;

        if (fromLeft <= bandWidth)
            edges |= WidgetEdges.Left;
        else if (fromRight <= bandWidth)
            edges |= WidgetEdges.Right;

        if (fromBottom <= bandWidth)
            edges |= WidgetEdges.Bottom;
        else if (fromTop <= bandWidth && allowTopEdge)
            edges |= WidgetEdges.Top;

        return edges;

        static WidgetEdges Side(double near, double far, WidgetEdges nearEdge, WidgetEdges farEdge) =>
            near <= far ? nearEdge : farEdge;
    }

    /// <summary>
    /// The window rectangle for the given pointer position. Dragging an edge
    /// moves it; the opposite edge stays put, so a resize from the left or top
    /// also changes the origin.
    /// </summary>
    public PixelRect Resolve(PixelPoint pointerScreen, PixelSize minSize)
    {
        var dx = pointerScreen.X - PointerStart.X;
        var dy = pointerScreen.Y - PointerStart.Y;

        var left = Start.X;
        var top = Start.Y;
        var right = Start.X + Start.Width;
        var bottom = Start.Y + Start.Height;

        if (Edges.HasFlag(WidgetEdges.Left))
            left += dx;

        if (Edges.HasFlag(WidgetEdges.Right))
            right += dx;

        if (Edges.HasFlag(WidgetEdges.Top))
            top += dy;

        if (Edges.HasFlag(WidgetEdges.Bottom))
            bottom += dy;

        // Clamp to the minimum by pushing back the edge the user is dragging, so
        // the anchored edge never moves.
        if (right - left < minSize.Width)
        {
            if (Edges.HasFlag(WidgetEdges.Left))
                left = right - minSize.Width;
            else
                right = left + minSize.Width;
        }

        if (bottom - top < minSize.Height)
        {
            if (Edges.HasFlag(WidgetEdges.Top))
                top = bottom - minSize.Height;
            else
                bottom = top + minSize.Height;
        }

        return new PixelRect(
            new PixelPoint(left, top), new PixelSize(right - left, bottom - top));
    }
}

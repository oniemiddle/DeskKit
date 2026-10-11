using Avalonia;

namespace DeskKit.Core.Models;

/// <summary>
/// A set of sides of a widget rectangle. Used both to describe which sides a
/// widget is magnetically touching, and which sides a resize is dragging.
/// </summary>
[Flags]
public enum WidgetEdges
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8,
}

/// <summary>Outcome of applying magnetic snapping to a widget being dragged.</summary>
/// <param name="Position">Where the widget should be placed, in physical pixels.</param>
/// <param name="SnappedX">True when the horizontal position came from a snap.</param>
/// <param name="SnappedY">True when the vertical position came from a snap.</param>
/// <param name="Neighbours">
/// Indexes into the list that was passed to <see cref="WidgetSnapEngine.Snap"/> of
/// the widgets the moving one snapped to. These are the ones worth highlighting.
/// </param>
/// <param name="Glow">
/// The stretches of the dragged widget's edges that should light up, derived from
/// where it actually overlaps the widgets it snapped to.
/// </param>
public readonly record struct WidgetSnapResult(
    PixelPoint Position,
    bool SnappedX,
    bool SnappedY,
    IReadOnlyList<int> Neighbours,
    IReadOnlyList<WidgetGlowSegment> Glow)
{
    public bool Snapped => SnappedX || SnappedY;
}

/// <summary>Outcome of applying magnetic snapping to a widget being resized.</summary>
/// <param name="Rect">The rectangle the widget should take, in physical pixels.</param>
/// <param name="SnappedX">True when a vertical edge was pulled onto a snap.</param>
/// <param name="SnappedY">True when a horizontal edge was pulled onto a snap.</param>
/// <param name="Neighbours">
/// Indexes into the list passed to <see cref="WidgetSnapEngine.SnapResize"/> of the
/// widgets the resized one snapped to.
/// </param>
/// <param name="Glow">
/// The stretches of the resized widget's edges that should light up, derived from
/// where it actually overlaps the widgets it snapped to.
/// </param>
public readonly record struct WidgetResizeSnapResult(
    PixelRect Rect,
    bool SnappedX,
    bool SnappedY,
    IReadOnlyList<int> Neighbours,
    IReadOnlyList<WidgetGlowSegment> Glow)
{
    public bool Snapped => SnappedX || SnappedY;
}

/// <summary>
/// Magnetic alignment for dragged widgets.
/// <para>
/// Two kinds of snap are offered against each other widget:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Adjacent</b> — sit beside it with a gap. This only applies when the two
/// overlap on the other axis, so widgets that are nowhere near each other do not
/// get glued side by side. The gap is deliberate: flush edges read as a single
/// merged surface, and widgets are meant to stay separate objects.
/// </item>
/// <item>
/// <b>Aligned</b> — share a left, right, top, bottom or centre line.
/// </item>
/// </list>
/// <para>
/// Snapping is applied per axis and independently, so a widget can end up
/// aligned horizontally with one neighbour and adjacent vertically to another.
/// Only the closest candidate within the threshold wins, and the result is a
/// pure function of the inputs — nothing here depends on where the widget was a
/// frame ago.
/// </para>
/// </summary>
public static class WidgetSnapEngine
{
    /// <summary>Space left between two widgets that snap next to each other.</summary>
    public const int DefaultGap = 8;

    /// <summary>
    /// How far an edge may be from a snap position, in physical pixels, for the
    /// snap to engage. Deliberately equal to the gap: the capture range is a
    /// small nudge, not a gravity well.
    /// </summary>
    public const int DefaultThreshold = DefaultGap;

    /// <summary>
    /// Only widgets this close to each other take part in snapping at all.
    /// <para>
    /// Without this, a widget could be magnetised into alignment with another one
    /// on the far side of the desktop purely because they happened to share an
    /// edge — reading as global grid alignment rather than as two widgets being
    /// placed together. Snapping is meant to be a local gesture.
    /// </para>
    /// </summary>
    public const int DefaultProximity = DefaultGap + DefaultThreshold;

    public static WidgetSnapResult Snap(
        PixelRect moving,
        IReadOnlyList<PixelRect> others,
        int gap = DefaultGap,
        int threshold = DefaultThreshold,
        int proximity = DefaultProximity)
    {
        ArgumentNullException.ThrowIfNull(others);

        var unchanged = new WidgetSnapResult(
            new PixelPoint(moving.X, moving.Y), false, false, [], []);

        if (others.Count == 0 || threshold <= 0)
            return unchanged;

        var xIndexes = new List<int>();
        int? xValue = null;
        var xDistance = int.MaxValue;

        var yIndexes = new List<int>();
        int? yValue = null;
        var yDistance = int.MaxValue;

        for (var index = 0; index < others.Count; index++)
        {
            var other = others[index];

            var gapX = EdgeDistance(moving.X, moving.Right, other.X, other.Right);
            var gapY = EdgeDistance(moving.Y, moving.Bottom, other.Y, other.Bottom);

            // Not a neighbour: too far away for either kind of snap to mean
            // anything.
            if (gapX > proximity || gapY > proximity)
                continue;

            if (gapY == 0)
            {
                // Beside the other widget, with a gap.
                ConsiderX(other.Right + gap, index);
                ConsiderX(other.X - moving.Width - gap, index);
            }

            if (gapX == 0)
            {
                ConsiderY(other.Bottom + gap, index);
                ConsiderY(other.Y - moving.Height - gap, index);
            }

            // Edge and centre alignment.
            ConsiderX(other.X, index);
            ConsiderX(other.Right - moving.Width, index);
            ConsiderX(other.X + ((other.Width - moving.Width) / 2), index);

            ConsiderY(other.Y, index);
            ConsiderY(other.Bottom - moving.Height, index);
            ConsiderY(other.Y + ((other.Height - moving.Height) / 2), index);
        }

        // Every widget the move snapped to is worth highlighting, so ties are
        // collected rather than letting the first one win.
        var neighbours = new List<int>(2);
        foreach (var index in xIndexes)
            neighbours.Add(index);

        foreach (var index in yIndexes)
        {
            if (!neighbours.Contains(index))
                neighbours.Add(index);
        }

        neighbours.Sort();

        var position = new PixelPoint(xValue ?? moving.X, yValue ?? moving.Y);
        var settled = new PixelRect(position, moving.Size);

        var settledNeighbours = new List<PixelRect>(neighbours.Count);
        foreach (var index in neighbours)
            settledNeighbours.Add(others[index]);

        return new WidgetSnapResult(
            position,
            xValue.HasValue,
            yValue.HasValue,
            neighbours,
            GlowSegments(settled, settledNeighbours, proximity));

        void ConsiderX(int candidate, int source)
        {
            var distance = Math.Abs(candidate - moving.X);
            if (distance > threshold)
                return;

            if (distance < xDistance)
            {
                xDistance = distance;
                xValue = candidate;
                xIndexes.Clear();
                xIndexes.Add(source);
            }
            else if (distance == xDistance && xValue == candidate && !xIndexes.Contains(source))
            {
                xIndexes.Add(source);
            }
        }

        void ConsiderY(int candidate, int source)
        {
            var distance = Math.Abs(candidate - moving.Y);
            if (distance > threshold)
                return;

            if (distance < yDistance)
            {
                yDistance = distance;
                yValue = candidate;
                yIndexes.Clear();
                yIndexes.Add(source);
            }
            else if (distance == yDistance && yValue == candidate && !yIndexes.Contains(source))
            {
                yIndexes.Add(source);
            }
        }
    }

    /// <summary>
    /// Magnetic alignment for a widget being resized by one of its edges.
    /// <para>
    /// Unlike a drag, a resize keeps the opposite edge still, so only the dragged
    /// edges are offered a snap and the widget grows or shrinks towards the
    /// neighbour instead of sliding onto it. Each dragged edge gets the same two
    /// candidates a drag would produce against that neighbour: <b>aligned</b>
    /// with the matching edge, and <b>adjacent</b> to it at the configured gap —
    /// the position a drag would have settled the two widgets into. Adjacency
    /// needs the two widgets to overlap on the other axis, so a resize is not
    /// caught by an unrelated widget elsewhere on the desktop.
    /// </para>
    /// <para>
    /// A candidate that would shrink the widget below <paramref name="minimum"/>
    /// is ignored, so snapping can never fight the minimum-size clamp.
    /// </para>
    /// </summary>
    public static WidgetResizeSnapResult SnapResize(
        PixelRect moving,
        WidgetEdges edges,
        IReadOnlyList<PixelRect> others,
        PixelSize minimum,
        int gap = DefaultGap,
        int threshold = DefaultThreshold,
        int proximity = DefaultProximity)
    {
        ArgumentNullException.ThrowIfNull(others);

        var unchanged = new WidgetResizeSnapResult(moving, false, false, [], []);

        if (others.Count == 0 || threshold <= 0)
            return unchanged;

        var dragLeft = edges.HasFlag(WidgetEdges.Left);
        var dragRight = edges.HasFlag(WidgetEdges.Right);
        var dragTop = edges.HasFlag(WidgetEdges.Top);
        var dragBottom = edges.HasFlag(WidgetEdges.Bottom);

        // Only one edge per axis moves. When both arrive, the negated side wins,
        // because that is the one that carries the origin and so the one whose
        // position is comparable across frames.
        if (dragLeft)
            dragRight = false;
        else if (dragRight)
            dragLeft = false;

        if (dragTop)
            dragBottom = false;
        else if (dragBottom)
            dragTop = false;

        if (!dragLeft && !dragRight && !dragTop && !dragBottom)
            return unchanged;

        var xIndexes = new List<int>();
        int? xValue = null;
        var xDistance = int.MaxValue;
        var xCurrent = dragLeft ? moving.X : moving.Right;

        var yIndexes = new List<int>();
        int? yValue = null;
        var yDistance = int.MaxValue;
        var yCurrent = dragTop ? moving.Y : moving.Bottom;

        for (var index = 0; index < others.Count; index++)
        {
            var other = others[index];

            var gapX = EdgeDistance(moving.X, moving.Right, other.X, other.Right);
            var gapY = EdgeDistance(moving.Y, moving.Bottom, other.Y, other.Bottom);

            if (gapX > proximity || gapY > proximity)
                continue;

            if (dragLeft)
            {
                // Sharing the neighbour's left edge, or stopping the gap short of
                // its right one — which is where a drag would have left them.
                if (moving.Right - other.X >= minimum.Width)
                    ConsiderX(other.X, index);

                if (gapY == 0 && moving.Right - (other.Right + gap) >= minimum.Width)
                    ConsiderX(other.Right + gap, index);
            }
            else if (dragRight)
            {
                if (other.Right - moving.X >= minimum.Width)
                    ConsiderX(other.Right, index);

                if (gapY == 0 && (other.X - gap) - moving.X >= minimum.Width)
                    ConsiderX(other.X - gap, index);
            }

            if (dragTop)
            {
                if (moving.Bottom - other.Y >= minimum.Height)
                    ConsiderY(other.Y, index);

                if (gapX == 0 && moving.Bottom - (other.Bottom + gap) >= minimum.Height)
                    ConsiderY(other.Bottom + gap, index);
            }
            else if (dragBottom)
            {
                if (other.Bottom - moving.Y >= minimum.Height)
                    ConsiderY(other.Bottom, index);

                if (gapX == 0 && (other.Y - gap) - moving.Y >= minimum.Height)
                    ConsiderY(other.Y - gap, index);
            }
        }

        // Every widget the resize snapped to is worth highlighting, so ties are
        // collected rather than letting the first one win.
        var neighbours = new List<int>(xIndexes.Count + yIndexes.Count);
        foreach (var index in xIndexes)
            neighbours.Add(index);

        foreach (var index in yIndexes)
        {
            if (!neighbours.Contains(index))
                neighbours.Add(index);
        }

        neighbours.Sort();

        var left = dragLeft ? xValue ?? moving.X : moving.X;
        var right = dragRight ? xValue ?? moving.Right : moving.Right;
        var top = dragTop ? yValue ?? moving.Y : moving.Y;
        var bottom = dragBottom ? yValue ?? moving.Bottom : moving.Bottom;

        var settled = new PixelRect(
            new PixelPoint(left, top), new PixelSize(right - left, bottom - top));

        var settledNeighbours = new List<PixelRect>(neighbours.Count);
        foreach (var index in neighbours)
            settledNeighbours.Add(others[index]);

        return new WidgetResizeSnapResult(
            settled,
            xValue.HasValue,
            yValue.HasValue,
            neighbours,
            GlowSegments(settled, settledNeighbours, proximity));

        void ConsiderX(int candidate, int source)
        {
            var distance = Math.Abs(candidate - xCurrent);
            if (distance > threshold)
                return;

            if (distance < xDistance)
            {
                xDistance = distance;
                xValue = candidate;
                xIndexes.Clear();
                xIndexes.Add(source);
            }
            else if (distance == xDistance && xValue == candidate && !xIndexes.Contains(source))
            {
                xIndexes.Add(source);
            }
        }

        void ConsiderY(int candidate, int source)
        {
            var distance = Math.Abs(candidate - yCurrent);
            if (distance > threshold)
                return;

            if (distance < yDistance)
            {
                yDistance = distance;
                yValue = candidate;
                yIndexes.Clear();
                yIndexes.Add(source);
            }
            else if (distance == yDistance && yValue == candidate && !yIndexes.Contains(source))
            {
                yIndexes.Add(source);
            }
        }
    }

    /// <summary>Distance between two intervals; zero when they overlap.</summary>
    private static int EdgeDistance(int aStart, int aEnd, int bStart, int bEnd) =>
        Math.Max(0, Math.Max(aStart - bEnd, bStart - aEnd));

    /// <summary>
    /// Which stretches of <paramref name="self"/>'s edges should light up,
    /// derived from the region it actually shares with each neighbour.
    /// <para>
    /// The axis two rectangles are <em>separated</em> on decides which edge
    /// faces the neighbour; an axis they already overlap on means they are not
    /// facing each other along it. A pair separated on both axes — sitting
    /// diagonally — therefore lights two edges at once, and two neighbours on
    /// the same side light two stretches of the same edge.
    /// </para>
    /// <para>
    /// Along the facing edge, only the stretch the neighbour really covers is
    /// lit. When the rectangles do not overlap on that axis at all, a short
    /// stretch at the nearest end is lit instead, so a diagonal placement still
    /// shows that the widgets are attached to each other.
    /// </para>
    /// </summary>
    public static IReadOnlyList<WidgetGlowSegment> GlowSegments(
        PixelRect self,
        IReadOnlyList<PixelRect> others,
        int proximity = DefaultProximity,
        double minimumFraction = MinimumGlowFraction)
    {
        ArgumentNullException.ThrowIfNull(others);

        var segments = new List<WidgetGlowSegment>(others.Count);
        if (self.Width <= 0 || self.Height <= 0)
            return segments;

        foreach (var other in others)
        {
            var gapX = EdgeDistance(self.X, self.Right, other.X, other.Right);
            var gapY = EdgeDistance(self.Y, self.Bottom, other.Y, other.Bottom);

            if (gapX > proximity || gapY > proximity)
                continue;

            // Separated horizontally, so a vertical edge faces the neighbour.
            if (gapX > 0)
            {
                var edge = CentreX(other) >= CentreX(self) ? WidgetEdge.Right : WidgetEdge.Left;

                Add(segments, edge,
                    spanStart: self.Y, spanLength: self.Height,
                    otherStart: other.Y, otherLength: other.Height,
                    minimumFraction);
            }

            // Separated vertically, so a horizontal edge faces the neighbour.
            if (gapY > 0)
            {
                var edge = CentreY(other) >= CentreY(self) ? WidgetEdge.Bottom : WidgetEdge.Top;

                Add(segments, edge,
                    spanStart: self.X, spanLength: self.Width,
                    otherStart: other.X, otherLength: other.Width,
                    minimumFraction);
            }
        }

        return segments;
    }

    /// <summary>
    /// Fraction of an edge lit when the two rectangles do not overlap along it,
    /// so that a diagonal placement still reads as attached.
    /// </summary>
    public const double MinimumGlowFraction = 0.3;

    private static void Add(
        List<WidgetGlowSegment> segments,
        WidgetEdge edge,
        int spanStart,
        int spanLength,
        int otherStart,
        int otherLength,
        double minimumFraction)
    {
        if (spanLength <= 0)
            return;

        var overlapStart = Math.Max(spanStart, otherStart);
        var overlapEnd = Math.Min(spanStart + spanLength, otherStart + otherLength);

        double start;
        double length;

        if (overlapEnd > overlapStart)
        {
            start = (overlapStart - spanStart) / (double)spanLength;
            length = (overlapEnd - overlapStart) / (double)spanLength;
        }
        else
        {
            // No shared stretch. Anchor a short piece at the end the neighbour
            // is closest to.
            var fraction = Math.Clamp(minimumFraction, 0, 1);
            var neighbourIsBeyondStart = otherStart + otherLength <= spanStart;
            start = neighbourIsBeyondStart ? 0 : 1 - fraction;
            length = fraction;
        }

        segments.Add(new WidgetGlowSegment(edge, Math.Clamp(start, 0, 1), Math.Clamp(length, 0, 1)));
    }

    private static int CentreX(PixelRect rect) => rect.X + (rect.Width / 2);

    private static int CentreY(PixelRect rect) => rect.Y + (rect.Height / 2);
}

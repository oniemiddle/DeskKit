namespace DeskKit.Core.Models;

/// <summary>One side of a widget rectangle.</summary>
public enum WidgetEdge
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// A lit stretch of one edge, in fractions of the card (0 to 1) so it stays
/// correct when the widget is resized.
/// <para>
/// The glow follows the region two widgets actually share, so a neighbour that
/// only overlaps part of an edge lights only that part. Several segments can be
/// lit at once, on different edges and at different places along the same edge.
/// </para>
/// </summary>
/// <param name="Edge">Which side of the card this stretch runs along.</param>
/// <param name="Start">Where the stretch begins, as a fraction of that edge.</param>
/// <param name="Length">How long the stretch is, as a fraction of that edge.</param>
public readonly record struct WidgetGlowSegment(WidgetEdge Edge, double Start, double Length)
{
    public double End => Start + Length;
}

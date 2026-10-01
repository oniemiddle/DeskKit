using Avalonia;
using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

/// <summary>
/// The glow is meant to show the region two widgets actually share, so these
/// tests are mostly about partial overlaps: which part of an edge lights up, and
/// how many stretches can be lit at once.
/// </summary>
public sealed class WidgetGlowSegmentTests
{
    /// <summary>A 200x100 widget at (0, 0).</summary>
    private static readonly PixelRect Self = new(0, 0, 200, 100);

    private static IReadOnlyList<WidgetGlowSegment> Glow(PixelRect self, params PixelRect[] others) =>
        WidgetSnapEngine.GlowSegments(self, others);

    private static PixelRect At(int x, int y, int w = 200, int h = 100) => new(x, y, w, h);

    [Fact]
    public void Glow_WithNoNeighbours_LightsNothing()
    {
        Assert.Empty(Glow(Self));
    }

    [Fact]
    public void Glow_WithADistantNeighbour_LightsNothing()
    {
        // Far enough away to be unrelated.
        Assert.Empty(Glow(Self, At(0, 900)));
        Assert.Empty(Glow(Self, At(900, 0)));
    }

    [Fact]
    public void Glow_WithAFullyOverlappingNeighbour_LightsTheWholeEdge()
    {
        // Same height, sitting just to the right.
        var segments = Glow(Self, At(208, 0));

        var segment = Assert.Single(segments);
        Assert.Equal(WidgetEdge.Right, segment.Edge);
        Assert.Equal(0, segment.Start, 0.001);
        Assert.Equal(1, segment.Length, 0.001);
    }

    [Fact]
    public void Glow_WithAPartiallyOverlappingNeighbour_LightsOnlyTheSharedPart()
    {
        // The neighbour only covers the lower third of the edge.
        var segments = Glow(Self, At(208, 70, 200, 50));

        var segment = Assert.Single(segments);

        // 70..100 of a 100 tall edge is the last 30%.
        Assert.Equal(WidgetEdge.Right, segment.Edge);
        Assert.Equal(0.7, segment.Start, 0.001);
        Assert.Equal(0.3, segment.Length, 0.001);
    }

    [Fact]
    public void Glow_LightsTheSharedPartInTheMiddleToo()
    {
        var segments = Glow(Self, At(208, 25, 200, 50));

        var segment = Assert.Single(segments);
        Assert.Equal(0.25, segment.Start, 0.001);
        Assert.Equal(0.5, segment.Length, 0.001);
    }

    [Theory]
    [InlineData(WidgetEdge.Left, -208, 0)]
    [InlineData(WidgetEdge.Right, 208, 0)]
    public void Glow_PicksTheEdgeFacingTheNeighbour(WidgetEdge expected, int x, int y)
    {
        var segment = Assert.Single(Glow(Self, At(x, y)));
        Assert.Equal(expected, segment.Edge);
    }

    [Theory]
    [InlineData(WidgetEdge.Top, 0, -108)]
    [InlineData(WidgetEdge.Bottom, 0, 108)]
    public void Glow_PicksTheHorizontalEdgeWhenStacked(WidgetEdge expected, int x, int y)
    {
        var segment = Assert.Single(Glow(Self, At(x, y)));
        Assert.Equal(expected, segment.Edge);
    }

    [Fact]
    public void Glow_OnAHorizontalEdge_MeasuresAlongTheWidth()
    {
        // Below, covering the right half.
        var segments = Glow(Self, At(100, 108, 100, 100));

        var segment = Assert.Single(segments);
        Assert.Equal(WidgetEdge.Bottom, segment.Edge);
        Assert.Equal(0.5, segment.Start, 0.001);
        Assert.Equal(0.5, segment.Length, 0.001);
    }

    [Fact]
    public void Glow_WithTwoNeighboursDownTheSameSide_LightsTwoSeparateStretches()
    {
        var segments = Glow(Self, At(208, 0, 200, 40), At(208, 60, 200, 40));

        Assert.Equal(2, segments.Count);
        Assert.All(segments, s => Assert.Equal(WidgetEdge.Right, s.Edge));

        // The first covers 0..0.4 and the second 0.6..1.0, so they do not touch.
        Assert.Equal(0, segments[0].Start, 0.001);
        Assert.Equal(0.4, segments[0].Length, 0.001);
        Assert.Equal(0.6, segments[1].Start, 0.001);
        Assert.True(segments[0].End < segments[1].Start);
    }

    [Fact]
    public void Glow_WithANeighbourOnEachSide_LightsOppositeEdges()
    {
        var segments = Glow(Self, At(208, 0), At(-208, 0));

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.Edge == WidgetEdge.Left);
        Assert.Contains(segments, s => s.Edge == WidgetEdge.Right);
    }

    [Fact]
    public void Glow_WithADiagonalNeighbour_LightsTwoEdgesAtOnce()
    {
        // Separated on both axes, so both a vertical and a horizontal edge face it.
        var segments = Glow(Self, At(208, 108));

        Assert.Equal(2, segments.Count);
        Assert.Contains(segments, s => s.Edge == WidgetEdge.Right);
        Assert.Contains(segments, s => s.Edge == WidgetEdge.Bottom);

        // There is no shared stretch on either edge, so each lights a short
        // piece at the end nearest the neighbour rather than nothing at all.
        foreach (var segment in segments)
        {
            Assert.Equal(WidgetSnapEngine.MinimumGlowFraction, segment.Length, 0.001);
        }
    }

    [Fact]
    public void Glow_WithNoSharedStretch_AnchorsTheShortPieceAtTheNearestEnd()
    {
        // Below and to the right, close enough to matter but past the bottom of
        // the widget, so the vertical edges share nothing.
        var segments = Glow(Self, At(208, 110));

        var vertical = segments.Single(s => s.Edge == WidgetEdge.Right);

        // The neighbour is beyond the bottom, so the piece sits at the bottom.
        Assert.Equal(1 - WidgetSnapEngine.MinimumGlowFraction, vertical.Start, 0.001);
    }

    [Fact]
    public void Glow_WithAFullyOverlappingWidget_LightsNothing()
    {
        // Exactly on top of each other: no edge faces the other, so there is
        // nothing meaningful to point at.
        Assert.Empty(Glow(Self, At(0, 0)));
    }

    [Fact]
    public void Glow_KeepsSegmentsInsideTheEdge()
    {
        // A neighbour far larger than the widget, so the overlap is clamped.
        var segments = Glow(Self, At(208, -500, 200, 2000));

        var segment = Assert.Single(segments);
        Assert.InRange(segment.Start, 0, 1);
        Assert.InRange(segment.End, 0, 1);
    }

    [Fact]
    public void Glow_IsReportedBySnap_ForTheWidgetBeingDragged()
    {
        // Dragged to just short of sitting beside the anchor.
        var anchor = new PixelRect(0, 300, 200, 100);
        var moving = new PixelRect(206, 300, 200, 100);

        var result = WidgetSnapEngine.Snap(moving, [anchor], 8, 8, 16);

        Assert.True(result.Snapped);

        var segment = Assert.Single(result.Glow);
        Assert.Equal(WidgetEdge.Left, segment.Edge);

        // Level with the anchor, so the whole edge is shared.
        Assert.Equal(1, segment.Length, 0.001);
    }

    [Fact]
    public void Glow_IsIndependentOfPreviousFrames()
    {
        var moving = At(0, 0);
        var neighbour = At(208, 30, 200, 40);

        var first = Glow(moving, neighbour);
        var second = Glow(moving, neighbour);

        Assert.Equal(first, second);
    }
}

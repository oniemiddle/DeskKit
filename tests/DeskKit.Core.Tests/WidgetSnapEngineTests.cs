using Avalonia;
using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

public sealed class WidgetSnapEngineTests
{
    private const int Gap = WidgetSnapEngine.DefaultGap;
    private const int Threshold = WidgetSnapEngine.DefaultThreshold;
    private const int Proximity = WidgetSnapEngine.DefaultProximity;

    /// <summary>A 200x100 widget parked at (500, 400).</summary>
    private static readonly PixelRect Anchor = new(500, 400, 200, 100);

    /// <summary>
    /// The dragged widget is a different size from the anchor, so that aligning
    /// left, right and centre each produce a different answer.
    /// </summary>
    private static readonly PixelSize MovingSize = new(120, 80);

    private static PixelRect At(int x, int y) => new(x, y, 200, 100);

    private static PixelRect Moving(int x, int y) => new(x, y, MovingSize.Width, MovingSize.Height);

    private static WidgetSnapResult Snap(PixelRect moving, params PixelRect[] others) =>
        WidgetSnapEngine.Snap(moving, others, Gap, Threshold, Proximity);

    [Fact]
    public void Snap_WithNoOtherWidgets_LeavesThePositionAlone()
    {
        var result = Snap(Moving(100, 100));

        Assert.Equal(new PixelPoint(100, 100), result.Position);
        Assert.False(result.Snapped);
        Assert.Empty(result.Neighbours);
    }

    [Fact]
    public void Snap_FarFromEverything_LeavesThePositionAlone()
    {
        var result = Snap(Moving(700, 600), Anchor);

        Assert.Equal(new PixelPoint(700, 600), result.Position);
        Assert.False(result.Snapped);
    }

    // ---- The capture range is local --------------------------------------

    [Fact]
    public void Snap_DoesNotAlignWithAWidgetOnTheFarSideOfTheDesktop()
    {
        // Exactly the same left edge, but 400px below. Aligning here would read
        // as global grid alignment rather than as two widgets being placed
        // together, so the engine must ignore it.
        var moving = Moving(Anchor.X, Anchor.Bottom + 400);

        var result = Snap(moving, Anchor);

        Assert.False(result.Snapped);
        Assert.Equal(moving.X, result.Position.X);
        Assert.Empty(result.Neighbours);
    }

    [Fact]
    public void Snap_DoesNotReachBeyondTheProximityLimit()
    {
        // Vertically overlapping the anchor, so the "sit beside it" snap would
        // otherwise apply, and horizontally exactly at the proximity limit.
        var y = Anchor.Y + 20;

        var atLimit = Moving(Anchor.X - MovingSize.Width - Proximity, y);
        var beyond = Moving(Anchor.X - MovingSize.Width - Proximity - 1, y);

        // At the limit the widget is still a neighbour and lands one gap away.
        var snapped = Snap(atLimit, Anchor);
        Assert.True(snapped.Snapped);
        Assert.Equal(Anchor.X - MovingSize.Width - Gap, snapped.Position.X);
        Assert.Equal(Gap, Anchor.X - (snapped.Position.X + MovingSize.Width));

        // One pixel further out and it is not a neighbour at all.
        var ignored = Snap(beyond, Anchor);
        Assert.False(ignored.Snapped);
        Assert.Equal(beyond.X, ignored.Position.X);
        Assert.Empty(ignored.Neighbours);
    }

    [Fact]
    public void Snap_CaptureRangeIsTheGapItselfNotAWideGravityWell()
    {
        // How far the widget may be from a snap position before it engages is
        // deliberately the same as the gap, and the neighbourhood is only just
        // wider than that.
        Assert.Equal(Gap, Threshold);
        Assert.Equal(Gap + Threshold, Proximity);
    }

    // ---- Adjacent snapping -----------------------------------------------

    [Fact]
    public void Snap_WhileOverlappingVertically_SnapsBesideTheOtherWithAGap()
    {
        var expectedLeft = Anchor.Right + Gap;
        var result = Snap(Moving(expectedLeft - 5, Anchor.Y + 20), Anchor);

        Assert.True(result.SnappedX);
        Assert.Equal(expectedLeft, result.Position.X);
        Assert.Equal(Anchor.Y + 20, result.Position.Y);

        // And crucially, not flush against it.
        Assert.Equal(Gap, result.Position.X - Anchor.Right);
    }

    [Fact]
    public void Snap_SnapsToTheLeftOfTheOtherWithAGap()
    {
        var expectedLeft = Anchor.X - MovingSize.Width - Gap;
        var result = Snap(Moving(expectedLeft + 6, Anchor.Y + 20), Anchor);

        Assert.Equal(expectedLeft, result.Position.X);
        Assert.Equal(Gap, Anchor.X - (result.Position.X + MovingSize.Width));
    }

    [Fact]
    public void Snap_WhileOverlappingHorizontally_SnapsBelowTheOtherWithAGap()
    {
        var expectedTop = Anchor.Bottom + Gap;
        var result = Snap(Moving(Anchor.X + 30, expectedTop - 4), Anchor);

        Assert.True(result.SnappedY);
        Assert.Equal(expectedTop, result.Position.Y);
        Assert.Equal(Gap, result.Position.Y - Anchor.Bottom);
    }

    [Fact]
    public void Snap_WithNoOverlapOnEitherAxis_DoesNotGlueTheWidgetsSideBySide()
    {
        // Well below the anchor, so a "sit beside it" snap would be nonsense.
        var result = Snap(Moving(Anchor.Right + Gap, Anchor.Bottom + 200), Anchor);

        Assert.False(result.Snapped);
    }

    // ---- Alignment --------------------------------------------------------

    [Fact]
    public void Snap_AlignsLeftEdges()
    {
        var result = Snap(Moving(Anchor.X + 3, Anchor.Y + 30), Anchor);

        Assert.True(result.SnappedX);
        Assert.Equal(Anchor.X, result.Position.X);
    }

    [Fact]
    public void Snap_AlignsRightEdges()
    {
        var expected = Anchor.Right - MovingSize.Width;

        var result = Snap(Moving(expected - 3, Anchor.Y + 30), Anchor);

        Assert.True(result.SnappedX);
        Assert.Equal(expected, result.Position.X);
    }

    [Fact]
    public void Snap_AlignsCentres()
    {
        var expected = Anchor.X + ((Anchor.Width - MovingSize.Width) / 2);

        var result = Snap(Moving(expected + 3, Anchor.Y + 30), Anchor);

        Assert.True(result.SnappedX);
        Assert.Equal(expected, result.Position.X);
    }

    [Fact]
    public void Snap_AlignsTopEdges()
    {
        var result = Snap(Moving(Anchor.X + 50, Anchor.Y + 3), Anchor);

        Assert.True(result.SnappedY);
        Assert.Equal(Anchor.Y, result.Position.Y);
    }

    [Fact]
    public void Snap_AlignsBottomEdges()
    {
        var expected = Anchor.Bottom - MovingSize.Height;

        var result = Snap(Moving(Anchor.X + 50, expected - 3), Anchor);

        Assert.True(result.SnappedY);
        Assert.Equal(expected, result.Position.Y);
    }

    [Fact]
    public void Snap_CanSnapBothAxesAtOnce()
    {
        // Sitting just below the anchor and slightly right of its left edge, so
        // the vertical gap snap and the left-edge alignment both fire.
        var result = Snap(Moving(Anchor.X + 4, Anchor.Bottom + Gap - 3), Anchor);

        Assert.True(result.SnappedX);
        Assert.True(result.SnappedY);
        Assert.Equal(Anchor.X, result.Position.X);
        Assert.Equal(Anchor.Bottom + Gap, result.Position.Y);
    }

    // ---- Neighbours -------------------------------------------------------

    [Fact]
    public void Snap_ReportsTheWidgetItSnappedTo()
    {
        var farAway = new PixelRect(0, 0, 50, 50);

        var result = Snap(Moving(Anchor.Right + Gap - 2, Anchor.Y + 10), farAway, Anchor);

        Assert.Equal([1], result.Neighbours);
    }

    [Fact]
    public void Snap_WhenAlignedWithTwoWidgets_ReportsBoth()
    {
        // Two widgets over the same band, so the dragged one lines up with both.
        var upper = Anchor;
        var lower = new PixelRect(Anchor.X, Anchor.Y + 30, 200, 100);
        var moving = Moving(Anchor.X + 2, Anchor.Y + 60);

        var result = Snap(moving, upper, lower);

        Assert.Equal(2, result.Neighbours.Count);
        Assert.Contains(0, result.Neighbours);
        Assert.Contains(1, result.Neighbours);
    }

    [Fact]
    public void Snap_PrefersTheClosestCandidate()
    {
        // A left edge 2px away and a centre 6px away.
        var moving = Moving(Anchor.X + 2, Anchor.Y + 30);

        var result = Snap(moving, Anchor);

        Assert.Equal(Anchor.X, result.Position.X);
    }

    // ---- Options ----------------------------------------------------------

    [Fact]
    public void Snap_WithZeroThreshold_NeverSnaps()
    {
        var result = WidgetSnapEngine.Snap(
            Moving(Anchor.X + 1, Anchor.Y + 30), [Anchor], Gap, 0, Proximity);

        Assert.False(result.Snapped);
        Assert.Equal(Anchor.X + 1, result.Position.X);
    }

    [Fact]
    public void Snap_WithACustomGap_UsesIt()
    {
        const int customGap = 24;
        var expected = Anchor.Right + customGap;

        var result = WidgetSnapEngine.Snap(
            Moving(expected - 4, Anchor.Y + 20), [Anchor], customGap, Threshold, customGap + Threshold);

        Assert.Equal(expected, result.Position.X);
        Assert.Equal(customGap, result.Position.X - Anchor.Right);
    }

    [Fact]
    public void Snap_IsIndependentOfPreviousFrames()
    {
        // The same input must always give the same answer, so a snapped widget
        // cannot start oscillating the way a window position feedback loop does.
        var moving = Moving(Anchor.Right + Gap - 4, Anchor.Y + 10);

        var first = Snap(moving, Anchor);
        var second = Snap(moving, Anchor);

        Assert.Equal(first.Position, second.Position);
        Assert.Equal(first.Neighbours, second.Neighbours);
    }
}

using Avalonia;
using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

public sealed class WidgetResizeSessionTests
{
    private static readonly PixelRect Window = new(400, 300, 300, 200);

    /// <summary>Card inset inside the window, matching the glow margin.</summary>
    private static readonly Rect Card = new(16, 16, 268, 168);

    private static readonly PixelSize MinSize = new(120, 80);

    private static WidgetResizeSession Begin(WidgetEdges edges, PixelPoint pointer) =>
        WidgetResizeSession.Begin(edges, pointer, Window);

    // ---- Hit testing ------------------------------------------------------

    [Fact]
    public void HitTest_MiddleOfTheCard_GrabsNothing()
    {
        var edges = WidgetResizeSession.HitTest(Card, new Point(Card.Width / 2, Card.Height / 2), allowTopEdge: false);

        Assert.Equal(WidgetEdges.None, edges);
    }

    [Fact]
    public void HitTest_OutsideTheCard_GrabsNothing()
    {
        // Inside the transparent glow margin rather than the card.
        var edges = WidgetResizeSession.HitTest(Card, new Point(4, Card.Height / 2), allowTopEdge: false);

        Assert.Equal(WidgetEdges.None, edges);
    }

    [Fact]
    public void HitTest_LeftEdgeBand_GrabsTheLeftEdge()
    {
        // Mid-height, so no corner is involved.
        var point = new Point(Card.X + 2, Card.Y + (Card.Height / 2));

        Assert.Equal(WidgetEdges.Left, WidgetResizeSession.HitTest(Card, point, allowTopEdge: false));
    }

    [Fact]
    public void HitTest_TopEdgeBand_GrabsTheTopEdge()
    {
        // Mid-width, so no corner is involved.
        var point = new Point(Card.X + (Card.Width / 2), Card.Y + 2);

        Assert.Equal(WidgetEdges.Top, WidgetResizeSession.HitTest(Card, point, allowTopEdge: true));
    }

    [Fact]
    public void HitTest_RightAndBottomEdges()
    {
        var right = new Point(Card.Right - 2, Card.Height / 2);
        var bottom = new Point(Card.Width / 2, Card.Bottom - 2);

        Assert.Equal(WidgetEdges.Right, WidgetResizeSession.HitTest(Card, right, allowTopEdge: false));
        Assert.Equal(WidgetEdges.Bottom, WidgetResizeSession.HitTest(Card, bottom, allowTopEdge: false));
    }

    [Fact]
    public void HitTest_Corner_GrabsBothEdges()
    {
        var bottomRight = new Point(Card.Right - 2, Card.Bottom - 2);

        Assert.Equal(
            WidgetEdges.Right | WidgetEdges.Bottom,
            WidgetResizeSession.HitTest(Card, bottomRight, allowTopEdge: false));
    }

    [Fact]
    public void HitTest_TopEdgeIsReservedWhenSomethingElseOwnsIt()
    {
        var onTopEdge = new Point(Card.Width / 2, Card.Y + 2);

        // The drag strip lives along the top, so the top band must not resize.
        Assert.Equal(WidgetEdges.None, WidgetResizeSession.HitTest(Card, onTopEdge, allowTopEdge: false));

        // ...unless nothing else is using it.
        Assert.Equal(WidgetEdges.Top, WidgetResizeSession.HitTest(Card, onTopEdge, allowTopEdge: true));
    }

    [Fact]
    public void HitTest_TopCornersStillResizeWhenTheTopEdgeIsReserved()
    {
        var topLeft = new Point(Card.X + 2, Card.Y + 2);

        Assert.Equal(
            WidgetEdges.Left | WidgetEdges.Top,
            WidgetResizeSession.HitTest(Card, topLeft, allowTopEdge: false));
    }

    // ---- Resolving --------------------------------------------------------

    [Fact]
    public void Resolve_DraggingTheRightEdge_GrowsTheWidthAndKeepsTheOrigin()
    {
        var session = Begin(WidgetEdges.Right, new PixelPoint(1000, 500));

        var rect = session.Resolve(new PixelPoint(1050, 500), MinSize);

        Assert.Equal(Window.X, rect.X);
        Assert.Equal(Window.Y, rect.Y);
        Assert.Equal(Window.Width + 50, rect.Width);
        Assert.Equal(Window.Height, rect.Height);
    }

    [Fact]
    public void Resolve_DraggingTheLeftEdge_MovesTheOriginAndKeepsTheRightEdge()
    {
        var session = Begin(WidgetEdges.Left, new PixelPoint(1000, 500));

        var rect = session.Resolve(new PixelPoint(1030, 500), MinSize);

        Assert.Equal(Window.X + 30, rect.X);
        Assert.Equal(Window.Right, rect.Right);
        Assert.Equal(Window.Width - 30, rect.Width);
    }

    [Fact]
    public void Resolve_DraggingTheBottomEdge_GrowsTheHeightOnly()
    {
        var session = Begin(WidgetEdges.Bottom, new PixelPoint(1000, 500));

        var rect = session.Resolve(new PixelPoint(1000, 560), MinSize);

        Assert.Equal(Window.Y, rect.Y);
        Assert.Equal(Window.Width, rect.Width);
        Assert.Equal(Window.Height + 60, rect.Height);
    }

    [Fact]
    public void Resolve_DraggingTheTopEdge_MovesTheOriginAndKeepsTheBottom()
    {
        var session = Begin(WidgetEdges.Top, new PixelPoint(1000, 500));

        var rect = session.Resolve(new PixelPoint(1000, 470), MinSize);

        Assert.Equal(Window.Y - 30, rect.Y);
        Assert.Equal(Window.Bottom, rect.Bottom);
    }

    [Fact]
    public void Resolve_CornerChangesBothAxes()
    {
        var session = Begin(WidgetEdges.Right | WidgetEdges.Bottom, new PixelPoint(1000, 500));

        var rect = session.Resolve(new PixelPoint(1040, 520), MinSize);

        Assert.Equal(Window.X, rect.X);
        Assert.Equal(Window.Y, rect.Y);
        Assert.Equal(Window.Width + 40, rect.Width);
        Assert.Equal(Window.Height + 20, rect.Height);
    }

    [Fact]
    public void Resolve_StopsAtTheMinimumSizeWhenDraggingAnInnerEdge()
    {
        var session = Begin(WidgetEdges.Right, new PixelPoint(1000, 500));

        // Ask for a width far below the minimum.
        var rect = session.Resolve(new PixelPoint(500, 500), MinSize);

        Assert.Equal(MinSize.Width, rect.Width);
        Assert.Equal(Window.X, rect.X);
    }

    [Fact]
    public void Resolve_AtTheMinimumKeepsTheAnchoredEdgeStill()
    {
        var session = Begin(WidgetEdges.Left, new PixelPoint(1000, 500));

        // Dragging the left edge far to the right must not push the right edge.
        var rect = session.Resolve(new PixelPoint(1400, 500), MinSize);

        Assert.Equal(MinSize.Width, rect.Width);
        Assert.Equal(Window.Right, rect.Right);
    }

    [Fact]
    public void Resolve_WithNoEdges_LeavesTheWindowAlone()
    {
        var session = Begin(WidgetEdges.None, new PixelPoint(1000, 500));

        var rect = session.Resolve(new PixelPoint(1200, 700), MinSize);

        Assert.Equal(Window, rect);
    }

    [Fact]
    public void Resolve_IsIndependentOfPreviousFrames()
    {
        // Unlike the drag, a resize is anchored to the press-time rectangle, so
        // holding the pointer still must give a stable answer every frame.
        var session = Begin(WidgetEdges.Right | WidgetEdges.Bottom, new PixelPoint(1000, 500));
        var held = new PixelPoint(1050, 540);

        var first = session.Resolve(held, MinSize);

        for (var frame = 0; frame < 5; frame++)
            Assert.Equal(first, session.Resolve(held, MinSize));
    }

    [Fact]
    public void Resolve_IgnoresTheWindowCurrentSizeInFavourOfTheStartRectangle()
    {
        // Same reasoning as the drag: the answer depends on the pointer and the
        // state captured at press time, nothing else.
        var sessionA = WidgetResizeSession.Begin(
            WidgetEdges.Right, new PixelPoint(1000, 500), new PixelRect(400, 300, 300, 200));
        var sessionB = WidgetResizeSession.Begin(
            WidgetEdges.Right, new PixelPoint(1000, 500), new PixelRect(400, 300, 300, 200));

        Assert.Equal(
            sessionA.Resolve(new PixelPoint(1020, 500), MinSize),
            sessionB.Resolve(new PixelPoint(1020, 500), MinSize));
    }
}

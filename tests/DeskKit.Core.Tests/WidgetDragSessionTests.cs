using Avalonia;
using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

/// <summary>
/// The drag arithmetic is the one place where a plausible-looking formula
/// produces a window that lurches back to where the drag started instead of
/// following the cursor. These tests pin the property that matters: the window
/// position depends on the pointer alone.
/// </summary>
public sealed class WidgetDragSessionTests
{
    private static readonly PixelPoint WindowAtPress = new(400, 300);

    /// <summary>The cursor sits 50 right and 40 down from the window's corner.</summary>
    private static readonly PixelPoint PointerAtPress = new(450, 340);

    private static readonly PixelPoint ExpectedGrabOffset = new(50, 40);

    [Fact]
    public void Start_RecordsWhereTheCursorIsRelativeToTheWindow()
    {
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);

        Assert.Equal(ExpectedGrabOffset, session.GrabOffset);
    }

    [Fact]
    public void PositionFor_WithTheCursorUnmoved_KeepsTheWindowWhereItWas()
    {
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);

        // Pressing the button must not move the widget.
        Assert.Equal(WindowAtPress, session.PositionFor(PointerAtPress));
    }

    [Fact]
    public void PositionFor_TracksThePointerOneForOne()
    {
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);

        var moved = session.PositionFor(new PixelPoint(PointerAtPress.X + 1, PointerAtPress.Y + 1));

        // One pixel of cursor movement must be one pixel of window movement.
        // Deriving the origin from the window's live position instead of the
        // pointer makes this come out at roughly half a pixel.
        Assert.Equal(new PixelPoint(WindowAtPress.X + 1, WindowAtPress.Y + 1), moved);
    }

    [Fact]
    public void PositionFor_IsIndependentOfWhereTheWindowCurrentlyIs()
    {
        // The bug being guarded against: the next position was computed from the
        // window's current position, so the result depended on the previous
        // frame. Feeding the same pointer position repeatedly against a moving
        // window then alternated between two answers, which is the visible
        // snap-back. The result must instead be a pure function of the pointer.
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);
        var settledPointer = new PixelPoint(900, 700);
        var expected = new PixelPoint(850, 660);

        var window = WindowAtPress;
        for (var frame = 0; frame < 8; frame++)
        {
            window = session.PositionFor(settledPointer);
            Assert.Equal(expected, window);
        }
    }

    [Fact]
    public void PositionFor_OverAMultiStepDrag_FollowsEveryPointerPositionExactly()
    {
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);

        // Walk the cursor across the screen a pixel at a time, as a slow drag
        // does, and check the window never drifts from it.
        for (var step = 1; step <= 40; step++)
        {
            var pointer = new PixelPoint(PointerAtPress.X + step, PointerAtPress.Y - step);
            var expected = new PixelPoint(WindowAtPress.X + step, WindowAtPress.Y - step);

            Assert.Equal(expected, session.PositionFor(pointer));
        }
    }

    [Fact]
    public void PositionFor_WithALargeJump_LandsExactlyUnderTheCursor()
    {
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);

        var pointer = new PixelPoint(1500, 900);
        var window = session.PositionFor(pointer);

        // The grabbed point must still be under the cursor, which is the
        // invariant the whole calculation exists to preserve.
        Assert.Equal(pointer, new PixelPoint(window.X + 50, window.Y + 40));
    }

    [Fact]
    public void TheNaiveFormula_IsProvablyUnstable_WhichIsTheBugThisReplaced()
    {
        // An executable record of the original defect, so the reasoning is not
        // just a comment someone can delete. The old implementation computed
        //     startWindow + (pointerNow - (windowNow + grab))
        // where windowNow was the window's own live position. That is right
        // exactly once, and thereafter alternates.
        var session = WidgetDragSession.Start(PointerAtPress, WindowAtPress);
        var pointer = new PixelPoint(900, 700);
        var correct = session.PositionFor(pointer);

        PixelPoint NaiveNext(PixelPoint windowNow) => new(
            WindowAtPress.X + (pointer.X - (windowNow.X + ExpectedGrabOffset.X)),
            WindowAtPress.Y + (pointer.Y - (windowNow.Y + ExpectedGrabOffset.Y)));

        // Frame 1: the window has not moved yet, so the naive answer looks fine.
        var frame1 = NaiveNext(WindowAtPress);
        Assert.Equal(correct, frame1);

        // Frame 2: now the window is where it should be, and the naive formula
        // sends it all the way back to where the drag started. That is the
        // visible snap-back the pointer-following implementation avoids.
        var frame2 = NaiveNext(frame1);
        Assert.Equal(WindowAtPress, frame2);

        // Frame 3: and forward again. An oscillation, not a drag.
        Assert.Equal(correct, NaiveNext(frame2));

        // The real implementation is stable across all three frames.
        Assert.Equal(correct, session.PositionFor(pointer));
        Assert.Equal(correct, session.PositionFor(pointer));
        Assert.Equal(correct, session.PositionFor(pointer));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(-200, -150)]
    public void PositionFor_HandlesGrabbingAnywhereInTheWindow(int grabX, int grabY)
    {
        var pointer = new PixelPoint(WindowAtPress.X + grabX, WindowAtPress.Y + grabY);
        var session = WidgetDragSession.Start(pointer, WindowAtPress);

        Assert.Equal(WindowAtPress, session.PositionFor(pointer));

        var movedPointer = new PixelPoint(pointer.X + 120, pointer.Y + 90);
        Assert.Equal(
            new PixelPoint(WindowAtPress.X + 120, WindowAtPress.Y + 90),
            session.PositionFor(movedPointer));
    }
}

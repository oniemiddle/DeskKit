using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

/// <summary>
/// The shape of a slide: how far it goes, and how it is spread over the time it
/// takes. Both are pure arithmetic, so they are pinned here rather than judged by
/// watching a widget move.
/// </summary>
public sealed class WidgetSlideAnimationTests
{
    // A 1920x1080 screen with a 300x200 widget at (100, 80), unless stated otherwise.
    private static readonly ScreenBounds Screen = new(0, 0, 1920, 1080);

    private const int WindowX = 100;
    private const int WindowY = 80;
    private const int WindowWidth = 300;
    private const int WindowHeight = 200;

    // ---- How far a slide travels -----------------------------------------

    [Fact]
    public void SlidingRightLeavesTheScreenByItsRightEdge()
    {
        var (x, y) = Offset(WidgetAnimationDirectionSetting.Right);

        // Moving right by this much puts the widget's left edge on the screen's
        // right edge: nothing of it is still on the screen.
        Assert.Equal(Screen.Right - WindowX, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void SlidingLeftLeavesTheScreenByItsLeftEdge()
    {
        var (x, y) = Offset(WidgetAnimationDirectionSetting.Left);

        Assert.Equal(-(WindowX + WindowWidth), x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void SlidingUpLeavesTheScreenByItsTopEdge()
    {
        var (x, y) = Offset(WidgetAnimationDirectionSetting.Up);

        Assert.Equal(0, x);
        Assert.Equal(-(WindowY + WindowHeight), y);
    }

    [Fact]
    public void SlidingDownLeavesTheScreenByItsBottomEdge()
    {
        var (x, y) = Offset(WidgetAnimationDirectionSetting.Down);

        Assert.Equal(0, x);
        Assert.Equal(Screen.Bottom - WindowY, y);
    }

    [Fact]
    public void TheOffsetIsWorkedOutForTheWholeSetRatherThanPerWidget()
    {
        // Two widgets on one screen: one near the left, one near the right. A set
        // slides as one thing, so the travel comes from the edge of the rectangle
        // they share — the same vector for both — and not from each widget's own
        // distance to the edge, which would move them at different speeds.
        var left = new ScreenBounds(80, 80, 300, 180);
        var right = new ScreenBounds(1400, 80, 300, 180);
        var together = new ScreenBounds(80, 80, 1620, 180);

        var shared = WidgetSlideAnimation.Offset(
            WidgetAnimationDirectionSetting.Right,
            together.X, together.Y, together.Width, together.Height, Screen);

        var ifItSlidAlone = WidgetSlideAnimation.Offset(
            WidgetAnimationDirectionSetting.Right,
            right.X, right.Y, right.Width, right.Height, Screen);

        Assert.Equal(Screen.Right - together.X, shared.X);

        // The far widget moves further than it would have on its own, which is the
        // point: it stays in step with the near one instead of being overtaken.
        Assert.True(shared.X > ifItSlidAlone.X);

        // And one vector is enough to take both of them off the screen.
        Assert.True(left.X + shared.X >= Screen.Right);
        Assert.True(right.X + shared.X >= Screen.Right);
    }

    [Fact]
    public void EveryDirectionKeepsGoingWhenTheWidgetIsAlreadyAtThatEdge()
    {
        // A widget flush against the edge it is leaving would otherwise have
        // nowhere to go and the "animation" would be a blink.
        foreach (var direction in WidgetAnimationDirectionSetting.Offered)
        {
            var widget = direction switch
            {
                WidgetAnimationDirectionSetting.Left => (X: Screen.X, Y: 0),
                WidgetAnimationDirectionSetting.Up => (X: 0, Y: Screen.Y),
                WidgetAnimationDirectionSetting.Down => (X: 0, Y: Screen.Bottom - WindowHeight),
                _ => (X: Screen.Right - WindowWidth, Y: 0),
            };

            var (x, y) = WidgetSlideAnimation.Offset(
                direction, widget.X, widget.Y, WindowWidth, WindowHeight, Screen);

            // Horizontally it must travel at least its own width, vertically at
            // least its own height: that is what "nowhere to go" is.
            var minimum = direction is WidgetAnimationDirectionSetting.Left or WidgetAnimationDirectionSetting.Right
                ? WindowWidth
                : WindowHeight;

            Assert.True(
                Math.Abs(x) + Math.Abs(y) >= minimum,
                $"{direction} travelled only {Math.Abs(x) + Math.Abs(y)}px");
        }
    }

    [Fact]
    public void ADirectionLeavesByTheSideItIsNamedAfter()
    {
        var right = Offset(WidgetAnimationDirectionSetting.Right);
        var left = Offset(WidgetAnimationDirectionSetting.Left);
        var up = Offset(WidgetAnimationDirectionSetting.Up);
        var down = Offset(WidgetAnimationDirectionSetting.Down);

        Assert.True(right.X > 0);
        Assert.True(left.X < 0);
        Assert.True(up.Y < 0);
        Assert.True(down.Y > 0);
    }

    [Fact]
    public void TheOffsetIsMeasuredOnTheScreenTheWidgetIsOn()
    {
        // The same widget on the second monitor, which starts where the first ends.
        var second = new ScreenBounds(1920, 0, 1920, 1080);
        var onSecond = WidgetSlideAnimation.Offset(
            WidgetAnimationDirectionSetting.Right, 2000, 80, WindowWidth, WindowHeight, second);

        Assert.Equal(second.Right - 2000, onSecond.X);

        // And a screen that could not be read still gives a real slide.
        var noScreen = WidgetSlideAnimation.Offset(
            WidgetAnimationDirectionSetting.Right, WindowX, WindowY, WindowWidth, WindowHeight, default);

        Assert.Equal(WindowWidth, noScreen.X);
    }

    [Fact]
    public void AnUnknownDirectionStillSlides()
    {
        Assert.Equal(
            Offset(WidgetAnimationDirectionSetting.Right),
            WidgetSlideAnimation.Offset("Diagonal", WindowX, WindowY, WindowWidth, WindowHeight, Screen));
    }

    // ---- How the travel is spread over the time --------------------------

    [Theory]
    [InlineData(WidgetAnimationEasingSetting.None)]
    [InlineData(WidgetAnimationEasingSetting.Light)]
    [InlineData(WidgetAnimationEasingSetting.Standard)]
    [InlineData(WidgetAnimationEasingSetting.Strong)]
    public void EveryCurveStartsStillAndEndsFinished(string easing)
    {
        Assert.Equal(0, WidgetSlideAnimation.Progress(easing, 0, showing: true), 6);
        Assert.Equal(1, WidgetSlideAnimation.Progress(easing, 1, showing: true), 6);
        Assert.Equal(0, WidgetSlideAnimation.Progress(easing, 0, showing: false), 6);
        Assert.Equal(1, WidgetSlideAnimation.Progress(easing, 1, showing: false), 6);
    }

    [Theory]
    [InlineData(WidgetAnimationEasingSetting.None)]
    [InlineData(WidgetAnimationEasingSetting.Light)]
    [InlineData(WidgetAnimationEasingSetting.Standard)]
    [InlineData(WidgetAnimationEasingSetting.Strong)]
    public void NoCurveEverDoublesBackOnItself(string easing)
    {
        foreach (var showing in new[] { true, false })
        {
            var biggestStepBack = 0.0;
            var previous = 0.0;

            for (var step = 0; step <= 200; step++)
            {
                var progress = WidgetSlideAnimation.Progress(easing, step / 200.0, showing);

                // "Strong" is allowed a hair of overshoot on the way in and of
                // anticipation on the way out — a pixel or two on a slide across a
                // screen — but never enough to read as a stutter.
                Assert.InRange(progress, -0.01, 1.01);

                if (previous - progress > biggestStepBack)
                    biggestStepBack = previous - progress;

                previous = progress;
            }

            Assert.True(
                biggestStepBack <= 0.01,
                $"{easing} (showing={showing}) went backwards by {biggestStepBack}");
        }
    }

    /// <summary>
    /// The property that makes a slide read as motion rather than as a jump: a
    /// widget arriving decelerates into its place, and one leaving accelerates
    /// away. Using either curve for both directions is what makes one of the two
    /// look like it shot off and then crawled.
    /// </summary>
    [Theory]
    [InlineData(WidgetAnimationEasingSetting.Light)]
    [InlineData(WidgetAnimationEasingSetting.Standard)]
    [InlineData(WidgetAnimationEasingSetting.Strong)]
    public void ShowingDeceleratesAndHidingAccelerates(string easing)
    {
        var halfwayShowing = WidgetSlideAnimation.Progress(easing, 0.5, showing: true);
        var halfwayHiding = WidgetSlideAnimation.Progress(easing, 0.5, showing: false);

        // Far more than half the distance is behind an arriving widget by the time
        // half the time is: it is slowing down into place.
        Assert.True(halfwayShowing > 0.9, $"{easing} show covered only {halfwayShowing} in half the time");

        // And far less than half is behind a leaving one: it has barely started.
        Assert.True(halfwayHiding < 0.2, $"{easing} hide covered {halfwayHiding} in half the time");

        // Which is also what the ends look like: a show is mostly done early, a
        // hide is mostly done late.
        Assert.True(WidgetSlideAnimation.Progress(easing, 0.25, true)
            > WidgetSlideAnimation.Progress(easing, 0.75, false));
    }

    [Fact]
    public void NoEasingIsAConstantSpeedInEitherDirection()
    {
        Assert.Equal(0.5, WidgetSlideAnimation.Progress(WidgetAnimationEasingSetting.None, 0.5, true), 6);
        Assert.Equal(0.5, WidgetSlideAnimation.Progress(WidgetAnimationEasingSetting.None, 0.5, false), 6);

        // An unrecognised key is the shipped default, not a straight line.
        Assert.Equal(
            WidgetSlideAnimation.Progress(WidgetAnimationEasingSetting.Standard, 0.3, true),
            WidgetSlideAnimation.Progress("Spring", 0.3, true),
            6);
    }

    [Fact]
    public void ProgressIsClampedOutsideTheRun()
    {
        Assert.Equal(0, WidgetSlideAnimation.Progress(WidgetAnimationEasingSetting.Standard, -1, true), 6);
        Assert.Equal(1, WidgetSlideAnimation.Progress(WidgetAnimationEasingSetting.Standard, 2, true), 6);
    }

    [Fact]
    public void ADistanceIsTheTravelSoFar()
    {
        Assert.Equal(0, WidgetSlideAnimation.Distance(WidgetAnimationEasingSetting.Standard, 400, 0, true));
        Assert.Equal(400, WidgetSlideAnimation.Distance(WidgetAnimationEasingSetting.Standard, 400, 1, true));
        Assert.Equal(200, WidgetSlideAnimation.Distance(WidgetAnimationEasingSetting.None, 400, 0.5, true));

        // An arriving widget has covered nearly all of it halfway through, and a
        // leaving one barely any: 389px against 11px of the same 400.
        Assert.InRange(
            WidgetSlideAnimation.Distance(WidgetAnimationEasingSetting.Standard, 400, 0.5, true), 385, 392);
        Assert.InRange(
            WidgetSlideAnimation.Distance(WidgetAnimationEasingSetting.Standard, 400, 0.5, false), 5, 20);
    }

    private static (int X, int Y) Offset(string direction) =>
        WidgetSlideAnimation.Offset(direction, WindowX, WindowY, WindowWidth, WindowHeight, Screen);
}

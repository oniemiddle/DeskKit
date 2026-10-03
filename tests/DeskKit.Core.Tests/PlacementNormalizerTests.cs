using DeskKit.Core.Models;
using DeskKit.Core.Services;

namespace DeskKit.Core.Tests;

public sealed class PlacementNormalizerTests
{
    private static readonly ScreenBounds Primary = new(0, 0, 1920, 1080);

    private static readonly ScreenBounds Secondary = new(1920, 0, 1280, 1024);

    [Fact]
    public void EnsureOnScreen_LeavesAVisibleWidgetAlone()
    {
        var placement = new WidgetPlacement { X = 400, Y = 300 };

        var result = PlacementNormalizer.EnsureOnScreen(placement, [Primary, Secondary]);

        Assert.Equal(400, result.X);
        Assert.Equal(300, result.Y);
    }

    [Fact]
    public void EnsureOnScreen_BringsBackAWidgetFromARemovedMonitor()
    {
        // The widget lived on a monitor that is no longer connected.
        var placement = new WidgetPlacement { X = 5000, Y = 300 };

        var result = PlacementNormalizer.EnsureOnScreen(placement, [Primary]);

        Assert.InRange(result.X, Primary.X, Primary.Right - PlacementNormalizer.RequiredVisibleWidth);
        Assert.True(Primary.HasVisibleCorner(
            result.X, result.Y, PlacementNormalizer.RequiredVisibleWidth, PlacementNormalizer.RequiredVisibleHeight));
    }

    [Fact]
    public void EnsureOnScreen_ChoosesTheNearestMonitor()
    {
        var placement = new WidgetPlacement { X = 3000, Y = 500 };

        var result = PlacementNormalizer.EnsureOnScreen(placement, [Primary, Secondary]);

        Assert.InRange(result.X, Secondary.X, Secondary.Right);
    }

    [Fact]
    public void EnsureOnScreen_ClampsANegativePositionToTheMonitorOrigin()
    {
        var placement = new WidgetPlacement { X = -900, Y = -900 };

        var result = PlacementNormalizer.EnsureOnScreen(placement, [Primary]);

        Assert.Equal(Primary.X, result.X);
        Assert.Equal(Primary.Y, result.Y);
    }

    [Fact]
    public void EnsureOnScreen_WithNoScreens_ReturnsThePlacementUnchanged()
    {
        var placement = new WidgetPlacement { X = 1234, Y = 5678 };

        var result = PlacementNormalizer.EnsureOnScreen(placement, []);

        Assert.Equal(1234, result.X);
        Assert.Equal(5678, result.Y);
    }

    [Fact]
    public void EnsureOnScreen_RecoversAWidgetWhoseMonitorCameBack()
    {
        // The same placement that was nudged onto the primary screen is left alone
        // once the monitor it belonged to is back, which is what makes keeping the
        // saved position worth doing.
        var placement = new WidgetPlacement { X = 2000, Y = 100 };

        var withoutSecondary = PlacementNormalizer.EnsureOnScreen(placement, [Primary]);
        var withSecondary = PlacementNormalizer.EnsureOnScreen(placement, [Primary, Secondary]);

        Assert.NotEqual(placement.X, withoutSecondary.X);
        Assert.Equal(2000, withSecondary.X);
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(1839, 1039, true)]
    [InlineData(1919, 1079, false)] // only 1 x 1 of it would still be on screen
    [InlineData(1850, 1060, false)] // only 70 x 20 of it would still be on screen
    public void HasVisibleCorner_RequiresTheWholeReachAreaOnScreen(int x, int y, bool expected)
    {
        Assert.Equal(
            expected,
            Primary.HasVisibleCorner(
                x, y, PlacementNormalizer.RequiredVisibleWidth, PlacementNormalizer.RequiredVisibleHeight));
    }

    [Theory]
    [InlineData(400, 300, 0)]    // primary
    [InlineData(2000, 300, 1920)] // secondary, which starts where primary ends
    public void FindNearestScreen_AnswersWithTheScreenAPointIsOn(int x, int y, int expectedLeft)
    {
        var screen = PlacementNormalizer.FindNearestScreen(x, y, [Primary, Secondary]);

        Assert.Equal(expectedLeft, screen.X);
    }

    [Fact]
    public void FindNearestScreen_AnswersWithTheClosestScreenForAPointOnNone()
    {
        // A widget stranded far to the right picks the monitor it is nearest to for
        // the same reason placement does, so a slide leaves by that monitor's edge.
        Assert.Equal(Secondary.X, PlacementNormalizer.FindNearestScreen(9000, 300, [Primary, Secondary]).X);
        Assert.Equal(Primary.X, PlacementNormalizer.FindNearestScreen(-3000, 300, [Primary, Secondary]).X);
    }

    [Fact]
    public void FindNearestScreen_RefusesAnEmptyLayout() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlacementNormalizer.FindNearestScreen(10, 10, []));
}

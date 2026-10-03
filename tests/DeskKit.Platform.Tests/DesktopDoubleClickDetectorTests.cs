namespace DeskKit.Platform.Tests;

/// <summary>
/// A low-level mouse hook has no click count, so the gesture depends entirely on
/// this reconstruction of one. The thresholds are supplied by the test, which is
/// what lets every boundary be checked without depending on how the machine
/// running the test is configured.
/// </summary>
public sealed class DesktopDoubleClickDetectorTests
{
    private const uint DoubleClickTime = 500;
    private const int MaxDistance = 4;

    [Fact]
    public void TwoPressesOnTheBackdropWithinTheWindowAreADoubleClick()
    {
        var detector = NewDetector();

        Assert.False(detector.Accept(Backdrop(100, 100, 1_000)));
        Assert.True(detector.Accept(Backdrop(100, 100, 1_200)));
    }

    [Theory]
    [InlineData(1_500)] // exactly the double-click time
    [InlineData(1_400)]
    public void TheTimeWindowIsInclusive(uint secondPressTime)
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));

        Assert.True(detector.Accept(Backdrop(100, 100, secondPressTime)));
    }

    [Theory]
    [InlineData(104, 100)]
    [InlineData(96, 100)]
    [InlineData(100, 104)]
    [InlineData(100, 96)]
    [InlineData(104, 104)]
    public void TheDistanceWindowIsInclusive(int x, int y)
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));

        Assert.True(detector.Accept(Backdrop(x, y, 1_100)));
    }

    [Fact]
    public void APressTooLateIsAFreshFirstPress()
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));

        // A second later: a click of its own rather than the second half of the
        // first, and therefore the start of the next pair.
        Assert.False(detector.Accept(Backdrop(100, 100, 2_000)));

        Assert.True(detector.Accept(Backdrop(100, 100, 2_100)));
    }

    [Fact]
    public void APressTooFarAwayIsAFreshFirstPress()
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));

        Assert.False(detector.Accept(Backdrop(200, 100, 1_100)));

        Assert.True(detector.Accept(Backdrop(201, 100, 1_200)));
    }

    [Fact]
    public void APressOffTheBackdropEndsTheSequence()
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));

        // The middle of a double-click landed on a widget, a window or an icon.
        Assert.False(detector.Accept(OffBackdrop(100, 100, 1_100)));

        Assert.False(detector.Accept(Backdrop(100, 100, 1_200)));
    }

    [Fact]
    public void APairOnSomethingElseIsNotTheBackdropGesture()
    {
        var detector = NewDetector();

        Assert.False(detector.Accept(OffBackdrop(100, 100, 1_000)));
        Assert.False(detector.Accept(OffBackdrop(100, 100, 1_100)));
    }

    [Fact]
    public void AThirdPressStartsANewPair()
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));
        Assert.True(detector.Accept(Backdrop(100, 100, 1_100)));

        // Consumed: the third press is the beginning of the next pair, so it
        // takes a fourth to complete it.
        Assert.False(detector.Accept(Backdrop(100, 100, 1_200)));
        Assert.True(detector.Accept(Backdrop(100, 100, 1_300)));
    }

    [Fact]
    public void ATimeThatWentBackwardsIsNotADoubleClick()
    {
        var detector = NewDetector();

        detector.Accept(Backdrop(100, 100, 1_000));

        Assert.False(detector.Accept(Backdrop(100, 100, 900)));
    }

    private static DesktopDoubleClickDetector NewDetector() =>
        new(DoubleClickTime, MaxDistance, MaxDistance);

    private static PointerSample Backdrop(int x, int y, uint timeMs) =>
        new(x, y, timeMs, IsDesktopBackdrop: true);

    private static PointerSample OffBackdrop(int x, int y, uint timeMs) =>
        new(x, y, timeMs, IsDesktopBackdrop: false);
}

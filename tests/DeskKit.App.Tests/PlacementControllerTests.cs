using Avalonia;
using DeskKit.App.Shell;
using DeskKit.Core.Abstractions;
using DeskKit.Core.Models;
using DeskKit.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskKit.App.Tests;

/// <summary>
/// The snapping rules are pure arithmetic over rectangles, but they run against live
/// windows in the application. These tests stand in for the windows so the rules can be
/// exercised without a desktop: what is checked is which edge lights up and where the
/// widget lands, not what a window happens to report.
/// </summary>
public sealed class PlacementControllerTests
{
    private const double NoMargin = 0;

    [Fact]
    public void AWidgetWithNothingToSnapToStaysWhereItWasProposed()
    {
        var controller = Create();
        var moving = new FakeWidget(new PixelPoint(100, 100), new PixelSize(200, 100));
        var proposed = new PixelPoint(412, 337);

        var result = controller.Snap([moving], moving, proposed, NoMargin);

        Assert.Equal(proposed, result);
        Assert.Null(moving.Highlight);
    }

    [Fact]
    public void AWidgetBesideAnotherSnapsToTheConfiguredGap()
    {
        var controller = Create();
        var neighbour = new FakeWidget(new PixelPoint(100, 100), new PixelSize(200, 100));
        var moving = new FakeWidget(new PixelPoint(0, 0), new PixelSize(200, 100));

        // Four pixels past the gap: inside the capture range, so it is pulled in.
        var proposed = new PixelPoint(100 + 200 + WidgetSnapEngine.DefaultGap + 4, 100);

        var result = controller.Snap([neighbour, moving], moving, proposed, NoMargin);

        Assert.Equal(100 + 200 + WidgetSnapEngine.DefaultGap, result.X);
        Assert.Equal(100, result.Y);
    }

    [Fact]
    public void AWidgetOutOfCaptureRangeIsNotPulled()
    {
        var controller = Create();
        var neighbour = new FakeWidget(new PixelPoint(100, 100), new PixelSize(200, 100));
        var moving = new FakeWidget(new PixelPoint(0, 0), new PixelSize(200, 100));

        var proposed = new PixelPoint(100 + 200 + WidgetSnapEngine.DefaultGap + 60, 100);

        var result = controller.Snap([neighbour, moving], moving, proposed, NoMargin);

        Assert.Equal(proposed, result);
    }

    [Fact]
    public void TheWidgetThatSnappedLightsTheEdgeItShares()
    {
        var controller = Create();
        var neighbour = new FakeWidget(new PixelPoint(100, 100), new PixelSize(200, 100));
        var moving = new FakeWidget(new PixelPoint(0, 0), new PixelSize(200, 100));

        controller.Snap([neighbour, moving], moving, new PixelPoint(312, 100), NoMargin);

        Assert.NotNull(moving.Highlight);
        var segment = Assert.Single(moving.Highlight!);
        Assert.Equal(WidgetEdge.Left, segment.Edge);

        Assert.NotNull(neighbour.Highlight);
        Assert.Equal(WidgetEdge.Right, neighbour.Highlight![0].Edge);
    }

    [Fact]
    public void AnInvisibleWidgetIsNotASnappingCandidate()
    {
        var controller = Create();
        var hidden = new FakeWidget(new PixelPoint(100, 100), new PixelSize(200, 100)) { IsVisible = false };
        var moving = new FakeWidget(new PixelPoint(0, 0), new PixelSize(200, 100));

        var proposed = new PixelPoint(308, 100);
        var result = controller.Snap([hidden, moving], moving, proposed, NoMargin);

        Assert.Equal(proposed, result);
        Assert.Null(moving.Highlight);
    }

    [Fact]
    public void AWidgetThatHasNotBeenLaidOutYetIsLeftAlone()
    {
        var controller = Create();
        var unmeasured = new FakeWidget(new PixelPoint(0, 0), default) { Measurable = false };
        var moving = new FakeWidget(new PixelPoint(0, 0), new PixelSize(200, 100));

        var proposed = new PixelPoint(300, 100);

        Assert.Equal(proposed, controller.Snap([unmeasured, moving], moving, proposed, NoMargin));
    }

    [Fact]
    public void TheSurfaceMarginIsRemovedFromBothSidesOfEveryMeasurement()
    {
        // Two windows that carry a glow margin must snap their visible cards together,
        // which means the margin has to come off before the comparison and go back on
        // afterwards. With a margin of 16 the windows end up 32px further apart than the
        // gap, and the cards end up exactly the gap apart.
        const int margin = 16;
        var controller = Create();

        var neighbour = new FakeWidget(new PixelPoint(100, 100), new PixelSize(200, 100));
        var moving = new FakeWidget(new PixelPoint(0, 0), new PixelSize(200, 100));

        var neighbourCardLeft = 100 + margin;
        var proposedCard = neighbourCardLeft + 200 + WidgetSnapEngine.DefaultGap;

        var result = controller.Snap(
            [neighbour, moving], moving, new PixelPoint(proposedCard - margin, 100), margin);

        Assert.Equal(
            neighbourCardLeft + 200 + WidgetSnapEngine.DefaultGap - margin,
            result.X);
    }

    [Fact]
    public void ClearingHighlightsClearsEveryWidget()
    {
        var first = new FakeWidget(new PixelPoint(0, 0), new PixelSize(10, 10));
        var second = new FakeWidget(new PixelPoint(0, 0), new PixelSize(10, 10));
        first.SetSnapHighlight([new WidgetGlowSegment(WidgetEdge.Left, 0, 1)]);

        PlacementController.ClearHighlights([first, second]);

        Assert.Null(first.Highlight);
        Assert.Null(second.Highlight);
    }

    private static PlacementController Create() => new(CreateWorkspace());

    private static WorkspaceState CreateWorkspace() =>
        new(new StubStore(), NullLogger.Instance);

    private sealed class StubStore : IStateStore
    {
        public string DatabasePath => "unused";

        public string PreMigrationBackupPath => "unused";

        public StoreLoadReport LoadReport { get; } = new(StoreOutcome.NotLoaded, [], null);

        public bool HasStoredState => false;

        public AppState Load() => new();

        public StoreSaveReport Save(AppState state) => new(StoreSaveOutcome.Saved, 0);
    }

    /// <summary>A widget reduced to what the placement rules actually use.</summary>
    private sealed class FakeWidget(PixelPoint position, PixelSize cardSize) : IPlaceableWidget
    {
        public bool IsVisible { get; set; } = true;

        public bool Measurable { get; set; } = true;

        public IReadOnlyList<WidgetGlowSegment>? Highlight { get; private set; }

        public PixelPoint Position { get; set; } = position;

        public bool TryGetCardSize(out PixelSize size, out double scaling)
        {
            size = cardSize;
            scaling = 1;
            return Measurable;
        }

        public void SetSnapHighlight(IReadOnlyList<WidgetGlowSegment> segments) =>
            Highlight = segments.Count == 0 ? null : segments;
    }
}

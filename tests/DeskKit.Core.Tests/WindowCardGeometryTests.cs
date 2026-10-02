using DeskKit.Core.Models;

namespace DeskKit.Core.Tests;

public sealed class WindowCardGeometryTests
{
    [Theory]
    [InlineData(260, 130, 16)]
    [InlineData(260, 130, 0)]
    [InlineData(140, 80, 16)]
    public void AWindowRoundTripsBackToTheCardItWasSizedFor(double cardWidth, double cardHeight, double margin)
    {
        var (windowWidth, windowHeight) = WindowCardGeometry.WindowSizeForCard(cardWidth, cardHeight, margin);
        var (backWidth, backHeight) = WindowCardGeometry.CardSizeForWindow(windowWidth, windowHeight, margin);

        Assert.Equal(cardWidth, backWidth, precision: 6);
        Assert.Equal(cardHeight, backHeight, precision: 6);
    }

    [Fact]
    public void WithNoMarginTheWindowIsTheCard()
    {
        var (windowWidth, windowHeight) = WindowCardGeometry.WindowSizeForCard(300, 150, margin: 0);
        var (cardWidth, cardHeight) = WindowCardGeometry.CardSizeForWindow(300, 150, margin: 0);

        Assert.Equal(300, windowWidth);
        Assert.Equal(150, windowHeight);
        Assert.Equal(300, cardWidth);
        Assert.Equal(150, cardHeight);
    }

    [Fact]
    public void TheMarginIsAddedOnBothSides()
    {
        var (windowWidth, windowHeight) = WindowCardGeometry.WindowSizeForCard(100, 50, margin: 16);

        Assert.Equal(132, windowWidth);
        Assert.Equal(82, windowHeight);
    }

    [Fact]
    public void ACardNeverComesBackSmallerThanOnePixel()
    {
        // A window that has not been laid out yet reports a size of zero, and a card of
        // zero or negative size would be handed to the layout system as a minimum.
        var (width, height) = WindowCardGeometry.CardSizeForWindow(0, 0, margin: 16);

        Assert.Equal(1, width);
        Assert.Equal(1, height);

        var (negativeWidth, negativeHeight) = WindowCardGeometry.CardSizeForWindow(-100, -100, margin: 16);

        Assert.Equal(1, negativeWidth);
        Assert.Equal(1, negativeHeight);
    }

    [Fact]
    public void ACardExactlyTheSizeOfTheMarginStillComesBackAsOnePixel()
    {
        var (width, height) = WindowCardGeometry.CardSizeForWindow(32, 32, margin: 16);

        Assert.Equal(1, width);
        Assert.Equal(1, height);
    }
}

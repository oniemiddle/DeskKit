using Avalonia.Media;
using DeskKit.Core.Abstractions;
using DeskKit.Runtime;

namespace DeskKit.Runtime.Tests;

/// <summary>
/// What a live card is painted with, which is the one thing about the surface a widget
/// window sits on that can be checked without a desktop.
/// </summary>
/// <remarks>
/// The difference between the two cases is the whole point: a window made of a material
/// is its own surface, so the card must paint nothing over it, and a window with no
/// material has to paint the card itself. A card painted for the wrong material is a
/// theme change away from diluting the material with a flat rectangle, which is why the
/// repaint asks each window what it is made of rather than being handed an answer.
/// </remarks>
public sealed class MaterialSurfaceTests
{
    [Fact]
    public void ACardOnAMaterialPaintsNothing()
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(ThemeService.CardBrushFor(WidgetMaterial.Mica));

        Assert.Equal(0, brush.Color.A);
    }

    [Fact]
    public void ACardWithoutAMaterialIsPaintedByTheCard()
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(ThemeService.CardBrushFor(WidgetMaterial.None));

        Assert.Equal(0xF5, brush.Color.A);
    }
}
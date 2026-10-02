using DeskKit.Core.Abstractions;

namespace DeskKit.Core.Tests;

/// <summary>
/// What a material does to a window's layout, which is the rule the whole widget layout
/// hangs off: a material is drawn by the window itself, so an inset card would sit on a
/// visible plate of it.
/// </summary>
/// <remarks>
/// Moved here with the method when it left <c>MaterialPolicy</c> for Core, because the
/// widget windows that depend on the answer are Runtime's and Runtime may only reference
/// Core (D-3, D-6).
/// </remarks>
public sealed class WidgetMaterialTests
{
    [Fact]
    public void FillsWindow_IsFalseWithoutAMaterial()
    {
        Assert.False(WidgetMaterial.None.FillsWindow());
    }

    [Theory]
    [InlineData(WidgetMaterial.Mica)]
    [InlineData(WidgetMaterial.MicaAlt)]
    [InlineData(WidgetMaterial.Acrylic)]
    [InlineData(WidgetMaterial.LiquidGlass)]
    public void FillsWindow_IsTrueForEveryDeclaredMaterial(WidgetMaterial material)
    {
        Assert.True(material.FillsWindow());
    }
}
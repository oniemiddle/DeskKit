using DeskKit.Platform;

namespace DeskKit.Platform.Tests;

/// <summary>
/// Which material a widget window can be given decides its whole layout — with a
/// material the card fills the window and without one it is inset — so the rules
/// are pinned here rather than discovered by looking at a window on one particular
/// machine.
/// </summary>
public sealed class MaterialPolicyTests
{
    private const int Windows10 = 19045;
    private const int Windows11_21H2 = 22000;
    private const int Windows11_22H2 = 22621;

    // ---- Ordering of the Windows build gates ----------------------------

    [Fact]
    public void Resolve_KeepsMicaOnTheBuildThatOnlyHasTheMicaEffect()
    {
        Assert.Equal(
            WidgetMaterial.Mica,
            MaterialPolicy.Resolve(WidgetMaterial.Mica, isWindows: true, Windows11_21H2));
    }

    [Theory]
    [InlineData(WidgetMaterial.Mica)]
    [InlineData(WidgetMaterial.MicaAlt)]
    [InlineData(WidgetMaterial.Acrylic)]
    public void Resolve_KeepsEveryMaterialOnTheBuildThatHasTheBackdropAttribute(WidgetMaterial material)
    {
        Assert.Equal(
            material,
            MaterialPolicy.Resolve(material, isWindows: true, Windows11_22H2));
    }

    // ---- Falling back rather than rendering as a flat card ---------------

    [Fact]
    public void Resolve_RefusesAcrylicBeforeTheBackdropAttributeExisted()
    {
        // There is no pre-22H2 Acrylic for this window, and answering "Acrylic"
        // anyway would leave the caller laying the card out for a material that
        // never appears.
        Assert.Equal(
            WidgetMaterial.None,
            MaterialPolicy.Resolve(WidgetMaterial.Acrylic, isWindows: true, Windows11_21H2));
    }

    [Theory]
    [InlineData(WidgetMaterial.Mica)]
    [InlineData(WidgetMaterial.MicaAlt)]
    [InlineData(WidgetMaterial.Acrylic)]
    public void Resolve_RefusesEveryMaterialOnWindows10(WidgetMaterial material)
    {
        Assert.Equal(
            WidgetMaterial.None,
            MaterialPolicy.Resolve(material, isWindows: true, Windows10));
    }

    // ---- Platforms with no implementation --------------------------------

    [Fact]
    public void Resolve_RefusesLiquidGlassUntilItIsImplemented()
    {
        // Declared so the choice travels through the same seam, but macOS has no
        // implementation yet and must not claim to have one.
        Assert.Equal(
            WidgetMaterial.None,
            MaterialPolicy.Resolve(WidgetMaterial.LiquidGlass, isWindows: false, 0));
        Assert.Equal(
            WidgetMaterial.None,
            MaterialPolicy.Resolve(WidgetMaterial.LiquidGlass, isWindows: true, Windows11_22H2));
    }

    [Theory]
    [InlineData(WidgetMaterial.Mica)]
    [InlineData(WidgetMaterial.MicaAlt)]
    [InlineData(WidgetMaterial.Acrylic)]
    public void Resolve_RefusesEveryMaterialOffWindows(WidgetMaterial material)
    {
        Assert.Equal(
            WidgetMaterial.None,
            MaterialPolicy.Resolve(material, isWindows: false, Windows11_22H2));
    }

    [Fact]
    public void Resolve_KeepsNoneAsNone()
    {
        Assert.Equal(
            WidgetMaterial.None,
            MaterialPolicy.Resolve(WidgetMaterial.None, isWindows: true, Windows11_22H2));
    }

    // ---- The layout consequence -----------------------------------------

    [Fact]
    public void FillsWindow_IsTrueExactlyWhenThereIsAMaterial()
    {
        // This is the rule the whole layout hangs off: a material is drawn by the
        // window, so an inset card would sit on a visible plate of it.
        Assert.False(MaterialPolicy.FillsWindow(WidgetMaterial.None));

        Assert.True(MaterialPolicy.FillsWindow(WidgetMaterial.Mica));
        Assert.True(MaterialPolicy.FillsWindow(WidgetMaterial.MicaAlt));
        Assert.True(MaterialPolicy.FillsWindow(WidgetMaterial.Acrylic));
        Assert.True(MaterialPolicy.FillsWindow(WidgetMaterial.LiquidGlass));
    }
}

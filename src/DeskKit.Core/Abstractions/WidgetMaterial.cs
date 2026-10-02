namespace DeskKit.Core.Abstractions;

/// <summary>
/// The material a widget window's surface is made of.
/// <para>
/// Widgets are chrome-less, so the surface behind their content is the whole
/// window rather than a themed backdrop inside it. Where the platform can supply
/// one, that surface is a system material — the desktop shows through it, blurred
/// and tinted — instead of a flat colour that has to be repainted whenever the
/// wallpaper changes.
/// </para>
/// </summary>
public enum WidgetMaterial
{
    /// <summary>No material: the window is fully transparent and the card is its own surface.</summary>
    None,

    /// <summary>Windows 11 Mica, which samples the desktop wallpaper.</summary>
    Mica,

    /// <summary>Windows 11 Mica Alt, a more contrasted variant of <see cref="Mica"/>.</summary>
    MicaAlt,

    /// <summary>Windows Acrylic, which blurs whatever is actually behind the window.</summary>
    Acrylic,

    /// <summary>
    /// The macOS equivalent (Liquid Glass). Declared so the choice travels through
    /// the same seam, but not implemented yet; it resolves to
    /// <see cref="None"/> rather than to a material that would not render.
    /// </summary>
    LiquidGlass,
}
/// <summary>
/// What a material does to a window's layout.
/// </summary>
public static class WidgetMaterialExtensions
{
    /// <summary>
    /// True when the material is rendered by the window itself, so the window is the
    /// widget's surface.
    /// <para>
    /// This is what decides the layout: a material fills the window's whole rectangle,
    /// so a card inset from it would sit on a visible plate of material instead of being
    /// the thing the material shows through. With a material the card therefore fills the
    /// window and the platform draws the rounded corners and the drop shadow that the
    /// inset and the card's own shadow used to provide.
    /// </para>
    /// </summary>
    public static bool FillsWindow(this WidgetMaterial material) => material != WidgetMaterial.None;
}